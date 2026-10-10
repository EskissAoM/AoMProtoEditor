using System.Text;
using System.Xml;
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
    private const long MaximumKnownTextFileSize = 512L * 1024 * 1024;
    private const long MaximumReferenceXmlFileSize = 256L * 1024 * 1024;
    private const long MaximumCandidateBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumCandidateFileCount = 20_000;
    private const int ScanBufferCharacters = 64 * 1024;

    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".xml", ".tactics"
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
                    document = LoadReferenceDocument(path);
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
            var found = FindWholeTokensInStableFile(fullPath, unresolved, "string cleanup");
            foreach (var id in found)
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

            var searchValues = identifier.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase)
                ? new[] { identifier, identifier[..^".tactics".Length] }
                : new[] { identifier };
            if (FindWholeTokensInStableFile(fullPath, searchValues, "deletion reference checking").Count > 0)
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

        var candidateCount = 0;
        long candidateBytes = 0;
        foreach (var path in ContainedFileEnumeration.EnumerateFiles(modRootDirectory))
        {
            var extension = Path.GetExtension(path);
            if (!includeXmlAndTactics &&
                (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            // Files with extensions outside the supported text catalog are not game
            // reference sources. Do not guess their encoding or inspect their payload.
            if (!KnownTextExtensions.Contains(extension))
                continue;

            var size = new FileInfo(path).Length;
            if (size > MaximumKnownTextFileSize)
            {
                throw new InvalidOperationException(
                    $"Deletion safety checking was cancelled because '{path}' is too large to inspect conservatively. No files were written.");
            }
            candidateCount++;
            candidateBytes = checked(candidateBytes + size);
            if (candidateCount > MaximumCandidateFileCount || candidateBytes > MaximumCandidateBytes)
            {
                throw new InvalidOperationException(
                    "Deletion safety checking was cancelled because the mod contains too many candidate files " +
                    "or more than 2 GiB of candidate text. No files were written.");
            }
            yield return path;
        }
    }

    private static IReadOnlySet<string> FindWholeTokensInStableFile(
        string path,
        IReadOnlyCollection<string> tokens,
        string operation)
    {
        var remaining = tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0)
            return found;

        try
        {
            var before = FileIntegrityTransaction.CaptureState(path);
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.Read,
                       bufferSize: 128 * 1024, FileOptions.SequentialScan))
            {
                var encoding = DetectTextEncoding(stream);
                using var reader = new StreamReader(
                    stream, encoding, detectEncodingFromByteOrderMarks: true,
                    bufferSize: 128 * 1024, leaveOpen: false);
                var buffer = new char[ScanBufferCharacters];
                var maximumTokenLength = remaining.Max(token => token.Length);
                var overlap = "";
                while (remaining.Count > 0)
                {
                    var read = reader.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    var text = overlap + new string(buffer, 0, read);
                    // A token ending exactly at a non-final buffer edge cannot be
                    // classified until the following character is available.
                    var searchableText = reader.EndOfStream ? text : text + "_";
                    foreach (var token in remaining.Where(token => ContainsWholeToken(searchableText, token)).ToList())
                    {
                        found.Add(token);
                        remaining.Remove(token);
                    }
                    var retained = Math.Min(maximumTokenLength + 1, text.Length);
                    overlap = retained == 0 ? "" : text[^retained..];
                }
            }
            var after = FileIntegrityTransaction.CaptureState(path);
            if (before != after)
                throw new IOException($"'{path}' changed while it was being inspected.");
            return found;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{operation} was cancelled because '{path}' could not be inspected safely. No files were written.",
                exception);
        }
    }

    private static Encoding DetectTextEncoding(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[4];
        var read = stream.Read(prefix);
        stream.Position = 0;
        if (read >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
            return Encoding.UTF8;
        if (read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
            return Encoding.Unicode;
        if (read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        if (read >= 2 && prefix[1] == 0 && prefix[0] != 0)
            return Encoding.Unicode;
        if (read >= 2 && prefix[0] == 0 && prefix[1] != 0)
            return Encoding.BigEndianUnicode;
        return Encoding.UTF8;
    }

    private static XDocument LoadReferenceDocument(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaximumReferenceXmlFileSize)
        {
            throw new InvalidOperationException(
                $"'{path}' is larger than {MaximumReferenceXmlFileSize / (1024 * 1024):N0} MiB.");
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumReferenceXmlFileSize * 2,
            IgnoreComments = false,
            IgnoreWhitespace = false
        };
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.SequentialScan);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
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
