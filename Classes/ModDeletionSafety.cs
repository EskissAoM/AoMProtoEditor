using System.Text;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record ModDeletionReference(
    string Identifier,
    string SourceLabel,
    string Path,
    string Owner,
    string Field,
    bool IsRecognized)
{
    public string DisplayLocation => string.IsNullOrWhiteSpace(Owner)
        ? $"{SourceLabel}: {Field}"
        : $"{SourceLabel}: {Owner} — {Field}";
}

public sealed class ModDeletionInspection
{
    public ModDeletionInspection(IEnumerable<ModDeletionReference> references)
    {
        References = references
            .DistinctBy(reference =>
                $"{reference.Path}\0{reference.Owner}\0{reference.Field}\0{reference.Identifier}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Field, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ModDeletionReference> References { get; }
    public bool IsBlocked => References.Count > 0;

    public string Format(int maximum = 12)
    {
        var shown = References.Take(maximum)
            .Select(reference => string.IsNullOrWhiteSpace(reference.Owner)
                ? $"• {reference.SourceLabel}: {reference.Field}"
                : $"• {reference.SourceLabel}: {reference.Owner} — {reference.Field}")
            .ToList();
        if (References.Count > shown.Count)
            shown.Add($"• …and {References.Count - shown.Count} more reference(s)");
        return string.Join(Environment.NewLine, shown);
    }
}

/// <summary>
/// Conservative, mod-wide checks used before deleting identifiers or generated
/// strings. XML and tactics files use the schema-aware reference policies; other
/// small textual/custom files are searched as exact tokens and therefore block a
/// delete instead of being guessed at.
/// </summary>
public static class ModDeletionSafety
{
    private const long MaximumUnknownFileSize = 8L * 1024 * 1024;
    private const long MaximumKnownTextFileSize = 64L * 1024 * 1024;

    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cfg", ".ini", ".json", ".lua", ".xs", ".trg", ".xml", ".tactics",
        ".xaml", ".csv", ".tsv", ".md", ".script", ".rule", ".data", ".xmb"
    };

    private static readonly HashSet<string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".dds", ".tga",
        ".wav", ".mp3", ".ogg", ".wem", ".bnk", ".zip", ".7z", ".rar", ".bar",
        ".grn", ".gr2", ".brg", ".mesh", ".model", ".anim", ".exe", ".dll", ".pdb"
    };

    public static ModDeletionInspection InspectIdentifier(
        IEnumerable<TechnologyReferenceFile> files,
        GameIdentifierKind kind,
        string identifier,
        string? modRootDirectory,
        IReadOnlyDictionary<string, XDocument>? documentOverrides = null,
        IEnumerable<string>? excludedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var excluded = NormalizePaths(excludedPaths);
        var overrides = (documentOverrides ?? new Dictionary<string, XDocument>())
            .ToDictionary(entry => Path.GetFullPath(entry.Key), entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        var references = new List<ModDeletionReference>();

        foreach (var file in files
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            if (excluded.Contains(path))
                continue;

            XDocument document;
            if (overrides.TryGetValue(path, out var replacement))
            {
                document = replacement;
            }
            else
            {
                if (!File.Exists(path))
                    continue;
                var before = FileIntegrityTransaction.CaptureState(path);
                try
                {
                    document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"'{identifier}' was not deleted because '{path}' could not be inspected safely.",
                        exception);
                }
                var after = FileIntegrityTransaction.CaptureState(path);
                if (before != after)
                    throw new IOException($"'{path}' changed while deletion references were being inspected. No files were written.");
            }

            references.AddRange(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, kind, identifier)
                .Select(reference => new ModDeletionReference(
                    identifier,
                    file.Label,
                    path,
                    reference.Owner,
                    reference.Field,
                    reference.IsRecognized)));
        }

        references.AddRange(FindCustomFileReferences(modRootDirectory, identifier, excluded));
        return new ModDeletionInspection(references);
    }

    public static IReadOnlyList<ModDeletionReference> FindCustomFileReferences(
        string? modRootDirectory,
        string identifier,
        IEnumerable<string>? excludedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return FindReferencesInOtherFiles(
                modRootDirectory,
                identifier,
                NormalizePaths(excludedPaths))
            .ToList();
    }

    public static IReadOnlySet<string> FindReferencedStringIds(
        string? modRootDirectory,
        IEnumerable<string> candidateIds,
        IEnumerable<XDocument>? currentDocuments = null,
        IEnumerable<string>? excludedPaths = null)
    {
        var candidates = candidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0)
            return referenced;

        foreach (var document in currentDocuments ?? [])
        {
            foreach (var value in document.Descendants()
                         .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                             .Concat(!element.HasElements ? [element.Value] : []))
                         .Select(value => value.Trim()))
            {
                if (candidates.Contains(value))
                    referenced.Add(value);
            }
        }

        var excluded = NormalizePaths(excludedPaths);
        foreach (var path in EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: true))
        {
            var fullPath = Path.GetFullPath(path);
            if (excluded.Contains(fullPath) ||
                Path.GetFileName(fullPath).Equals("stringmods.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var unresolved = candidates.Where(id => !referenced.Contains(id)).ToList();
            if (unresolved.Count == 0)
                break;
            var contents = ReadStableBytes(fullPath, "string cleanup");
            foreach (var id in unresolved.Where(id => ContainsWholeToken(contents, id)))
                referenced.Add(id);
        }

        return referenced;
    }

    private static IEnumerable<ModDeletionReference> FindReferencesInOtherFiles(
        string? modRootDirectory,
        string identifier,
        IReadOnlySet<string> excluded)
    {
        foreach (var path in EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: false))
        {
            var fullPath = Path.GetFullPath(path);
            if (excluded.Contains(fullPath) ||
                Path.GetFileName(fullPath).Equals("stringmods.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contents = ReadStableBytes(fullPath, "deletion reference checking");
            if (ContainsWholeToken(contents, identifier) ||
                identifier.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase) &&
                ContainsWholeToken(contents, identifier[..^".tactics".Length]))
            {
                yield return new ModDeletionReference(
                    identifier,
                    Path.GetRelativePath(modRootDirectory!, fullPath),
                    fullPath,
                    "",
                    "possible reference in a custom/non-XML file",
                    false);
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string? modRootDirectory, bool includeXmlAndTactics)
    {
        if (string.IsNullOrWhiteSpace(modRootDirectory) || !Directory.Exists(modRootDirectory))
            yield break;

        foreach (var path in Directory.EnumerateFiles(modRootDirectory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!includeXmlAndTactics &&
                (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (KnownBinaryExtensions.Contains(extension))
                continue;

            var size = new FileInfo(path).Length;
            var limit = KnownTextExtensions.Contains(extension) ? MaximumKnownTextFileSize : MaximumUnknownFileSize;
            if (size > limit)
            {
                throw new InvalidOperationException(
                    $"Deletion safety checking was cancelled because '{path}' is too large to inspect conservatively. No files were written.");
            }
            yield return path;
        }
    }

    private static byte[] ReadStableBytes(string path, string operation)
    {
        try
        {
            var before = FileIntegrityTransaction.CaptureState(path);
            var bytes = File.ReadAllBytes(path);
            var after = FileIntegrityTransaction.CaptureState(path);
            if (before != after)
                throw new IOException($"'{path}' changed while it was being inspected.");
            return bytes;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{operation} was cancelled because '{path}' could not be inspected safely. No files were written.",
                exception);
        }
    }

    internal static bool ContainsWholeToken(byte[] contents, string token)
    {
        if (contents.Length == 0 || string.IsNullOrWhiteSpace(token))
            return false;

        return ContainsWholeToken(Encoding.UTF8.GetString(contents), token) ||
               ContainsWholeToken(Encoding.Unicode.GetString(contents), token) ||
               ContainsWholeToken(Encoding.BigEndianUnicode.GetString(contents), token);
    }

    internal static bool ContainsWholeToken(string contents, string token)
    {
        for (var start = 0; ;)
        {
            start = contents.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;
            var end = start + token.Length;
            if ((start == 0 || !IsIdentifierCharacter(contents[start - 1])) &&
                (end == contents.Length || !IsIdentifierCharacter(contents[end])))
            {
                return true;
            }
            start = end;
        }
    }

    private static bool IsIdentifierCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '-';

    private static HashSet<string> NormalizePaths(IEnumerable<string>? paths)
        => (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
