using System.Text;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Prepares all changed XML documents before a multi-file rename transaction.
/// This keeps discovery separate from writing and lets the caller roll every
/// affected file back together through <see cref="FileIntegrityTransaction"/>.
/// </summary>
public sealed class GameIdentifierReferenceMigrationPlan
{
    private const long MaximumReferenceFileBytes = 256L * 1024 * 1024;
    private const int ScanBufferCharacters = 64 * 1024;
    private readonly IReadOnlyDictionary<string, XDocument> _documents;
    private readonly FileDiscoverySnapshot? _discovery;
    private readonly Dictionary<string, string> _metadataUpdates = new(StringComparer.OrdinalIgnoreCase);

    private GameIdentifierReferenceMigrationPlan(
        IReadOnlyDictionary<string, XDocument> documents,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> expectedStates,
        IReadOnlyList<GameIdentifierReferenceUsage> affectedReferences,
        FileDiscoverySnapshot? discovery)
    {
        _documents = documents;
        ExpectedStates = expectedStates;
        AffectedReferences = affectedReferences;
        _discovery = discovery;
    }

    public IReadOnlyList<string> Paths => _documents.Keys.Concat(_metadataUpdates.Keys).ToList();
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }
    public IReadOnlyList<GameIdentifierReferenceUsage> AffectedReferences { get; }

    // Validate before the owner transaction writes, rather than in Save: the
    // transaction may intentionally create or move files before saving this plan.
    public void ValidateDiscovery() => _discovery?.Validate();

    public static void ValidateUnitReferenceIdentity(XContainer document, GameIdentifierKind kind,
        string oldName, string newName, IEnumerable<string>? oppositeUnitKindNames, string source)
    {
        if (kind is not (GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType)) return;
        var names = oppositeUnitKindNames?.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (GameIdentifierReferencePolicy.HasMixedUnitReference(document, oldName) &&
            (names == null || names.Contains(oldName.Trim()) || names.Contains(newName.Trim())))
            throw new InvalidOperationException(
                $"'{oldName}' was not renamed because '{source}' contains a mixed Unit/Unit Type reference " +
                "whose identity cannot be resolved safely. Verify both identifier catalogs and choose names without collisions. No files were written.");
        if (GameIdentifierReferencePolicy.HasResourceModeTransition(document, oldName, newName))
            throw new InvalidOperationException(
                $"'{oldName}' was not renamed because '{newName}' selects Resource mode in '{source}'. No files were written.");
    }

    public static GameIdentifierReferenceMigrationPlan Prepare(
        IEnumerable<TechnologyReferenceFile> files,
        GameIdentifierKind kind,
        IEnumerable<KeyValuePair<string, string>> renames,
        IEnumerable<ProtoActionRenameOperation>? protoActionRenames = null,
        IEnumerable<string>? oppositeUnitKindNames = null,
        string? bindingModPath = null,
        string? bindingMetadataPath = null,
        FileDiscoverySnapshot? discovery = null)
        => PrepareWithBudget(new ReferenceReadBudget(), files, kind, renames, protoActionRenames,
            oppositeUnitKindNames, bindingModPath, bindingMetadataPath, discovery);

    internal static GameIdentifierReferenceMigrationPlan PrepareWithBudget(ReferenceReadBudget budget,
        IEnumerable<TechnologyReferenceFile> files, GameIdentifierKind kind,
        IEnumerable<KeyValuePair<string, string>> renames,
        IEnumerable<ProtoActionRenameOperation>? protoActionRenames = null,
        IEnumerable<string>? oppositeUnitKindNames = null,
        string? bindingModPath = null, string? bindingMetadataPath = null,
        FileDiscoverySnapshot? discovery = null)
    {
        var operations = renames
            .Where(rename => !string.IsNullOrWhiteSpace(rename.Key) &&
                             !string.IsNullOrWhiteSpace(rename.Value) &&
                             !rename.Key.Equals(rename.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var actionOperations = protoActionRenames?.ToList() ?? [];
        var oppositeNames = oppositeUnitKindNames?.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        var expectedStates = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        var affectedReferences = new List<GameIdentifierReferenceUsage>();
        var searchValues = operations.Select(operation => operation.Key)
            .Concat(actionOperations.Select(operation => operation.OldName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Select(value => kind == GameIdentifierKind.Tactics && value.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase)
                ? value[..^".tactics".Length] : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (searchValues.Length == 0)
        {
            var emptyPlan = new GameIdentifierReferenceMigrationPlan(documents, expectedStates, affectedReferences, discovery);
            emptyPlan.ValidateDiscovery();
            return emptyPlan;
        }

        foreach (var file in files
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            budget.AdmitFile(path);
            if (!File.Exists(path))
            {
                var missingState = FileIntegrityTransaction.CaptureState(path);
                if (!missingState.Exists)
                {
                    expectedStates[path] = missingState;
                    continue;
                }
            }
            var mayContainReference = InspectStableText(path, searchValues, out var inspectedState);
            budget.AdmitSource(path, inspectedState.Length);
            expectedStates[path] = inspectedState;
            if (!mayContainReference)
                continue;

            var stateBeforeRead = FileIntegrityTransaction.CaptureState(path);
            if (stateBeforeRead != inspectedState)
                throw new IOException($"'{path}' changed while its references were being inspected. No files were written.");
            var document = ModReferenceDocumentReader.Load(path, budget);
            var stateAfterRead = FileIntegrityTransaction.CaptureState(path);
            if (stateBeforeRead != stateAfterRead)
                throw new IOException($"'{path}' changed while its references were being inspected. No files were written.");
            var changed = 0;
            foreach (var rename in operations)
            {
                if (ModReferenceDocumentReader.IsCompiledTactics(path) &&
                    (GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, rename.Key).Count > 0 ||
                     GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, kind, rename.Key).Count > 0))
                    throw ModReferenceDocumentReader.CannotRewrite(path);
                ValidateUnitReferenceIdentity(document, kind, rename.Key, rename.Value, oppositeNames, path);
                var unknown = GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, kind, rename.Key);
                if (unknown.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"'{rename.Key}' was not renamed because '{path}' contains unsupported reference field(s): " +
                        string.Join(", ", unknown) + ". No files were written.");
                }
                affectedReferences.AddRange(
                    GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, rename.Key)
                        .Select(field => new GameIdentifierReferenceUsage(rename.Key, file.Label, path, field)));
                changed += GameIdentifierReferencePolicy.RenameReferences(document, kind, rename.Key, rename.Value);
                if (GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, rename.Key).Count > 0)
                {
                    throw new InvalidOperationException(
                        $"The rename verification still found a recognized reference to '{rename.Key}' in '{path}'. No files were written.");
                }
            }
            if (file.Kind is TechnologyReferenceDocumentKind.Techtree or TechnologyReferenceDocumentKind.Powers)
            {
                foreach (var operation in actionOperations)
                {
                    var actionChanges = GameIdentifierReferencePolicy.RenameEffectProtoActionReferences(document, operation, oppositeNames);
                    changed += actionChanges;
                    for (var index = 0; index < actionChanges; index++)
                    {
                        affectedReferences.Add(new GameIdentifierReferenceUsage(
                            operation.OldName,
                            file.Label,
                            path,
                            $"ProtoAction reference for '{operation.UnitName}'"));
                    }
                }
            }

            if (changed > 0)
            {
                budget.InspectExisting(document, path);
                documents[path] = document;
            }
            expectedStates[path] = stateAfterRead;
        }

        var plan = new GameIdentifierReferenceMigrationPlan(documents, expectedStates, affectedReferences, discovery);
        if (kind == GameIdentifierKind.Ability && operations.Count > 0 && !string.IsNullOrWhiteSpace(bindingModPath))
        {
            var metadataPath = Path.GetFullPath(bindingMetadataPath ?? AbilityBindingMetadataStore.StoragePath);
            var before = FileIntegrityTransaction.CaptureState(metadataPath);
            var contents = AbilityBindingMetadataStore.PrepareAbilityRenames(bindingModPath, operations, metadataPath);
            if (before != FileIntegrityTransaction.CaptureState(metadataPath))
                throw new IOException("Ability binding metadata changed during rename preparation. No files were written.");
            expectedStates[metadataPath] = before;
            if (contents != null) plan._metadataUpdates[metadataPath] = contents;
        }
        plan.ValidateDiscovery();
        return plan;
    }

    public void Save(Action<XDocument, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var (path, document) in _documents)
            writer(document, path);
        foreach (var (path, contents) in _metadataUpdates)
            AbilityBindingMetadataStore.SavePrepared(path, contents);
    }

    public XDocument? GetDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return _documents.TryGetValue(Path.GetFullPath(path), out var document)
            ? new XDocument(document)
            : null;
    }

    private static bool InspectStableText(
        string path,
        IReadOnlyList<string> searchValues,
        out FileIntegrityTransaction.FileState state)
    {
        var before = FileIntegrityTransaction.CaptureState(path);
        if (ModReferenceDocumentReader.IsCompiledTactics(path))
        {
            state = before;
            return true; // Binary XML must be decoded; a text prefilter cannot establish absence.
        }
        if (before.Length > MaximumReferenceFileBytes)
        {
            throw new InvalidOperationException(
                $"Reference inspection was cancelled because '{path}' is larger than " +
                $"{MaximumReferenceFileBytes / (1024 * 1024):N0} MiB. No files were written.");
        }

        var maximumSearchLength = Math.Max(3, searchValues.Max(value => value.Length));
        var overlap = "";
        var found = false;
        using (var stream = new FileStream(
                   path, FileMode.Open, FileAccess.Read, FileShare.Read,
                   bufferSize: 128 * 1024, FileOptions.SequentialScan))
        using (var reader = new StreamReader(
                   stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                   bufferSize: 128 * 1024, leaveOpen: false))
        {
            var buffer = new char[ScanBufferCharacters];
            while (!found)
            {
                var read = reader.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                var text = overlap + new string(buffer, 0, read);
                // A literal miss is conclusive only for simple ASCII XML. XML declarations
                // can select another encoding; entities/CDATA/comments/PIs can hide or split
                // values, and attribute whitespace is normalized by the XML reader. Let the
                // existing bounded XML parser decide whenever those cases are possible.
                found = text.Any(character => character > 127 || character is '\0' or '&' or '\r' or '\n' or '\t') ||
                    text.Contains("<!", StringComparison.Ordinal) ||
                    text.Contains("<?", StringComparison.Ordinal) ||
                    searchValues.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
                var retained = Math.Min(Math.Max(0, maximumSearchLength - 1), text.Length);
                overlap = retained == 0 ? "" : text[^retained..];
            }
        }

        var after = FileIntegrityTransaction.CaptureState(path);
        if (before != after)
            throw new IOException($"'{path}' changed while its references were being inspected. No files were written.");
        state = after;
        return found;
    }

    internal static XDocument LoadReferenceDocument(string path)
        => ModReferenceDocumentReader.Load(path);
}
