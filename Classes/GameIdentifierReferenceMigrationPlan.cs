using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Prepares all changed XML documents before a multi-file rename transaction.
/// This keeps discovery separate from writing and lets the caller roll every
/// affected file back together through <see cref="FileIntegrityTransaction"/>.
/// </summary>
public sealed class GameIdentifierReferenceMigrationPlan
{
    private readonly IReadOnlyDictionary<string, XDocument> _documents;

    private GameIdentifierReferenceMigrationPlan(
        IReadOnlyDictionary<string, XDocument> documents,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> expectedStates,
        IReadOnlyList<GameIdentifierReferenceUsage> affectedReferences)
    {
        _documents = documents;
        ExpectedStates = expectedStates;
        AffectedReferences = affectedReferences;
    }

    public IReadOnlyList<string> Paths => _documents.Keys.ToList();
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }
    public IReadOnlyList<GameIdentifierReferenceUsage> AffectedReferences { get; }

    public static GameIdentifierReferenceMigrationPlan Prepare(
        IEnumerable<TechnologyReferenceFile> files,
        GameIdentifierKind kind,
        IEnumerable<KeyValuePair<string, string>> renames,
        IEnumerable<ProtoActionRenameOperation>? protoActionRenames = null)
    {
        var operations = renames
            .Where(rename => !string.IsNullOrWhiteSpace(rename.Key) &&
                             !string.IsNullOrWhiteSpace(rename.Value) &&
                             !rename.Key.Equals(rename.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var actionOperations = protoActionRenames?.ToList() ?? [];
        var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        var expectedStates = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        var affectedReferences = new List<GameIdentifierReferenceUsage>();

        foreach (var file in files
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            var stateBeforeRead = FileIntegrityTransaction.CaptureState(path);
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var stateAfterRead = FileIntegrityTransaction.CaptureState(path);
            if (stateBeforeRead != stateAfterRead)
                throw new IOException($"'{path}' changed while its references were being inspected. No files were written.");
            var changed = 0;
            foreach (var rename in operations)
            {
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
            if (file.Kind == TechnologyReferenceDocumentKind.Techtree)
            {
                foreach (var operation in actionOperations)
                {
                    var actionChanges = GameIdentifierReferencePolicy.RenameTechnologyProtoActionReferences(document, operation);
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
                documents[path] = document;
                expectedStates[path] = stateAfterRead;
            }
        }

        return new GameIdentifierReferenceMigrationPlan(documents, expectedStates, affectedReferences);
    }

    public void Save(Action<XDocument, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var (path, document) in _documents)
            writer(document, path);
    }

    public XDocument? GetDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return _documents.TryGetValue(Path.GetFullPath(path), out var document)
            ? new XDocument(document)
            : null;
    }
}
