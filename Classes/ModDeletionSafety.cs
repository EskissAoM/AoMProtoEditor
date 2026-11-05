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
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; internal set; }
        = new Dictionary<string, FileIntegrityTransaction.FileState>();
    internal Action? ValidateManifest { get; set; }

    public void ValidateReadDependencies()
    {
        ValidateManifest?.Invoke();
        FileIntegrityTransaction.Execute([], ExpectedStates, () => { });
    }

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
        IEnumerable<string>? excludedPaths = null,
        bool captureReadDependencies = false)
        => InspectReferences(files, identifier, modRootDirectory,
            (file, document) => FindIdentifierReferences(file, document, kind, identifier),
            documentOverrides, excludedPaths, captureReadDependencies);

    private static IEnumerable<ModDeletionReference> FindIdentifierReferences(
        TechnologyReferenceFile file, XDocument document, GameIdentifierKind kind, string identifier)
        => GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, kind, identifier)
            .Select(reference => new ModDeletionReference(identifier, file.Label, file.Path,
                reference.Owner, reference.Field, reference.IsRecognized));

    // Display snapshot only: never use these counts to authorize a mutation.
    // Parse one document at a time, rather than retaining every document in memory.
    internal static IReadOnlyDictionary<string, int> CountIdentifierReferencesForDisplay(
        IEnumerable<TechnologyReferenceFile> files, GameIdentifierKind kind,
        IEnumerable<string> identifiers, string? modRootDirectory)
    {
        var names = identifiers.Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var counts = names.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);
        if (names.Length == 0) return counts;
        foreach (var file in files.Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            if (!File.Exists(path)) continue;
            var before = FileIntegrityTransaction.CaptureState(path);
            var document = LoadReferenceDocument(path);
            var after = FileIntegrityTransaction.CaptureState(path);
            if (before != after)
                throw new IOException($"'{path}' changed while usage counts were being inspected.");
            foreach (var name in names)
                counts[name] += new ModDeletionInspection(FindIdentifierReferences(file, document, kind, name)).References.Count;
        }
        // Deduplication includes the path, so counts from distinct files can be added.
        foreach (var reference in FindReferencesInOtherFiles(modRootDirectory, names, NormalizePaths(null)))
            counts[reference.Identifier]++;
        return counts;
    }

    public static ModDeletionInspection InspectTechnology(
        IEnumerable<TechnologyReferenceFile> files, string identifier, string? modRootDirectory,
        IReadOnlyDictionary<string, XDocument>? documentOverrides = null)
        => InspectReferences(files, identifier, modRootDirectory,
            (file, document) => TechnologyReferenceIndex.FindReferencesTo(
                [new TechnologyReferenceSource(file.Label, document, file.Kind, file.Path, CanMigrate: true)], identifier)
                .Select(reference => new ModDeletionReference(identifier, file.Label, file.Path,
                    reference.Owner, reference.Field, reference.IsRecognized)),
            documentOverrides, null, true);

    public static IReadOnlyList<string> FindDeletedNames(IEnumerable<string> savedAndDirtyNames,
        IEnumerable<string> currentNames, IEnumerable<KeyValuePair<string, string>> renames)
    {
        var current = currentNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var survivingRenames = renames.Where(rename => current.Contains(rename.Value))
            .Select(rename => rename.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return savedAndDirtyNames.Where(name => !string.IsNullOrWhiteSpace(name) &&
            !current.Contains(name) && !survivingRenames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ModDeletionInspection InspectReferences(
        IEnumerable<TechnologyReferenceFile> files, string identifier, string? modRootDirectory,
        Func<TechnologyReferenceFile, XDocument, IEnumerable<ModDeletionReference>> findReferences,
        IReadOnlyDictionary<string, XDocument>? documentOverrides,
        IEnumerable<string>? excludedPaths, bool captureReadDependencies)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var excluded = NormalizePaths(excludedPaths);
        var overrides = (documentOverrides ?? new Dictionary<string, XDocument>())
            .ToDictionary(entry => Path.GetFullPath(entry.Key), entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        var references = new List<ModDeletionReference>();
        var states = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        var manifest = captureReadDependencies
            ? EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: true).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        if (manifest != null)
            foreach (var path in manifest.Where(path => !excluded.Contains(Path.GetFullPath(path))))
                states[Path.GetFullPath(path)] = FileIntegrityTransaction.CaptureState(path);

        foreach (var file in files
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            if (excluded.Contains(path))
                continue;
            if (captureReadDependencies && !states.ContainsKey(path))
                states[path] = FileIntegrityTransaction.CaptureState(path);

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

            references.AddRange(findReferences(file, document));
        }

        references.AddRange(FindCustomFileReferences(modRootDirectory, identifier, excluded));
        var inspection = new ModDeletionInspection(references) { ExpectedStates = states };
        if (manifest != null)
        {
            inspection.ValidateManifest = () =>
            {
                if (!manifest.SetEquals(EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: true)))
                    throw new IOException("The mod reference file list changed during deletion checking. Retry Save. No files were written.");
            };
            inspection.ValidateReadDependencies();
        }
        return inspection;
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
        => InspectStringReferencesCore(modRootDirectory, candidateIds, currentDocuments, null,
            excludedPaths, captureReadDependencies: false).ReferencedIds;

    /// <summary>
    /// Inspects a proposed result and live pending drafts in one batch. Overrides replace
    /// disk documents automatically. Keep the supplied documents/enumerables live until
    /// commit so final validation can detect draft or override-membership changes.
    /// String definitions are not references. Callers must also guard the string table's
    /// loaded baseline when supplying its values to OwnedStringLifecyclePolicy.
    /// </summary>
    public static ModStringReferenceInspection InspectStringReferences(
        string modRootDirectory,
        IEnumerable<string> candidateIds,
        IReadOnlyDictionary<string, XDocument>? documentOverrides = null,
        IEnumerable<XDocument>? pendingDrafts = null,
        IEnumerable<string>? excludedPaths = null)
        => InspectStringReferencesCore(modRootDirectory, candidateIds, pendingDrafts,
            documentOverrides, excludedPaths, captureReadDependencies: true);

    private static ModStringReferenceInspection InspectStringReferencesCore(
        string? modRootDirectory, IEnumerable<string> candidateIds,
        IEnumerable<XDocument>? currentDocuments,
        IReadOnlyDictionary<string, XDocument>? documentOverrides,
        IEnumerable<string>? excludedPaths, bool captureReadDependencies)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);
        var candidates = candidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0)
            return new ModStringReferenceInspection(referenced, states);

        FileDiscoverySnapshot? discovery = null;
        IEnumerable<string> paths;
        if (captureReadDependencies)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);
            var root = Path.GetFullPath(modRootDirectory);
            var discovered = DiscoverStringReferenceFiles(root);
            discovery = FileDiscoverySnapshot.Capture(discovered, () => DiscoverStringReferenceFiles(root));
            paths = discovery.Paths;
        }
        else paths = EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: true);

        var overrides = (documentOverrides ?? new Dictionary<string, XDocument>())
            .ToDictionary(entry => Path.GetFullPath(entry.Key), entry => new XDocument(entry.Value), StringComparer.OrdinalIgnoreCase);
        var documents = (currentDocuments ?? []).Select(document =>
            captureReadDependencies ? new XDocument(document) : document).ToList();
        var excluded = NormalizePaths(excludedPaths);
        if (captureReadDependencies)
            foreach (var path in overrides.Keys.Concat(excluded).Distinct(StringComparer.OrdinalIgnoreCase))
                states[path] = FileIntegrityTransaction.CaptureState(path);

        foreach (var document in documents.Concat(overrides.Values))
            CollectStringReferences(document, candidates, referenced, includeWholeTokens: captureReadDependencies);

        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (excluded.Contains(fullPath) || overrides.ContainsKey(fullPath) ||
                Path.GetFileName(fullPath).Equals("stringmods.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var unresolved = candidates.Where(id => !referenced.Contains(id)).ToList();
            if (unresolved.Count == 0)
                break; // Every candidate is retained; unread files cannot change that decision.
            if (ModReferenceDocumentReader.IsCompiledTactics(fullPath))
            {
                var before = FileIntegrityTransaction.CaptureState(fullPath);
                CollectStringReferences(LoadReferenceDocument(fullPath), candidates, referenced, includeWholeTokens: true);
                if (before != FileIntegrityTransaction.CaptureState(fullPath))
                    throw new IOException($"'{fullPath}' changed during compiled string reference inspection. No files were written.");
                if (captureReadDependencies) states[fullPath] = before;
                continue;
            }
            var found = FindWholeTokensInStableFile(fullPath, unresolved, "string cleanup", out var state);
            referenced.UnionWith(found);
            if (captureReadDependencies)
            {
                // Reuse the bounded XML reader too: entities/CDATA can hide a reference
                // from a literal text scan. Keep conservative raw-token matches as well.
                var extension = Path.GetExtension(fullPath);
                if (unresolved.Any(id => !referenced.Contains(id)) &&
                    (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        CollectStringReferences(LoadReferenceDocument(fullPath), candidates, referenced, includeWholeTokens: true);
                        if (FileIntegrityTransaction.CaptureState(fullPath) != state)
                            throw new IOException($"'{fullPath}' changed during string reference inspection.");
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"String cleanup was cancelled because '{fullPath}' could not be inspected safely. No files were written.", exception);
                    }
                }
                states[fullPath] = state;
            }
        }

        Action? validateDocuments = null;
        if (captureReadDependencies)
        {
            validateDocuments = () =>
            {
                var currentOverrides = (documentOverrides ?? new Dictionary<string, XDocument>())
                    .ToDictionary(entry => Path.GetFullPath(entry.Key), entry => entry.Value, StringComparer.OrdinalIgnoreCase);
                var current = (currentDocuments ?? []).ToList();
                if (!NormalizePaths(excludedPaths).SetEquals(excluded) ||
                    currentOverrides.Count != overrides.Count ||
                    overrides.Any(entry => !currentOverrides.TryGetValue(entry.Key, out var document) ||
                        !XNode.DeepEquals(entry.Value, document)) ||
                    current.Count != documents.Count ||
                    !current.Zip(documents).All(pair => XNode.DeepEquals(pair.First, pair.Second)))
                    throw new IOException("Proposed documents or pending drafts changed during string cleanup checking. Retry the operation. No files were written.");
            };
        }
        var inspection = new ModStringReferenceInspection(referenced, states, discovery, validateDocuments);
        if (captureReadDependencies) inspection.ValidateReadDependencies();
        return inspection;
    }

    private static List<string> DiscoverStringReferenceFiles(string root)
    {
        RequireStringReferenceRoot(root);
        var paths = EnumerateCandidateFiles(root, includeXmlAndTactics: true).ToList();
        RequireStringReferenceRoot(root);
        return paths;
    }

    private static void RequireStringReferenceRoot(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.Directory) == 0)
            throw new IOException($"The mod reference root '{root}' is not a directory. No files were written.");
    }

    private static void CollectStringReferences(XDocument document, IReadOnlySet<string> candidates,
        ISet<string> referenced, bool includeWholeTokens)
    {
        foreach (var value in document.Descendants()
                     .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                         .Concat(!element.HasElements ? [element.Value] : [])))
        {
            var trimmed = value.Trim();
            if (candidates.Contains(trimmed)) referenced.Add(trimmed);
            if (includeWholeTokens)
                foreach (var id in candidates.Where(id => !referenced.Contains(id)))
                    if (ContainsWholeToken(trimmed, id)) referenced.Add(id);
        }
    }

    private static IEnumerable<ModDeletionReference> FindReferencesInOtherFiles(
        string? modRootDirectory,
        string identifier,
        IReadOnlySet<string> excluded)
        => FindReferencesInOtherFiles(modRootDirectory, new[] { identifier }, excluded);

    private static IEnumerable<ModDeletionReference> FindReferencesInOtherFiles(
        string? modRootDirectory,
        IReadOnlyCollection<string> identifiers,
        IReadOnlySet<string> excluded)
    {
        var searchValues = identifiers.ToDictionary(identifier => identifier,
            identifier => identifier.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase)
                ? new[] { identifier, identifier[..^".tactics".Length] }
                : new[] { identifier }, StringComparer.OrdinalIgnoreCase);
        var tokens = searchValues.Values.SelectMany(values => values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in EnumerateCandidateFiles(modRootDirectory, includeXmlAndTactics: false))
        {
            var fullPath = Path.GetFullPath(path);
            if (excluded.Contains(fullPath) ||
                Path.GetFileName(fullPath).Equals("stringmods.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var found = FindWholeTokensInStableFile(fullPath, tokens, "deletion reference checking");
            foreach (var entry in searchValues.Where(entry => entry.Value.Any(found.Contains)))
            {
                yield return new ModDeletionReference(
                    entry.Key,
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
                 extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase) ||
                 ModReferenceDocumentReader.IsCompiledTactics(path)))
            {
                continue;
            }
            // Files with extensions outside the supported text catalog are not game
            // reference sources. Do not guess their encoding or inspect their payload.
            if (!KnownTextExtensions.Contains(extension) && !ModReferenceDocumentReader.IsCompiledTactics(path))
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
        => FindWholeTokensInStableFile(path, tokens, operation, out _);

    private static IReadOnlySet<string> FindWholeTokensInStableFile(
        string path, IReadOnlyCollection<string> tokens, string operation,
        out FileIntegrityTransaction.FileState inspectedState)
    {
        inspectedState = new FileIntegrityTransaction.FileState(false, 0, "");
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
            inspectedState = after;
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
        => ModReferenceDocumentReader.Load(path);

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
