using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record TechnologyReferenceFile(string Path, TechnologyReferenceDocumentKind Kind, string Label);

public sealed class TechnologyReferenceMigrationPlan
{
    private readonly IReadOnlyList<TechnologyReferenceSource> _sources;

    private TechnologyReferenceMigrationPlan(
        IReadOnlyList<TechnologyReferenceSource> sources,
        IReadOnlyList<TechnologyReferenceUsage> affected,
        IReadOnlyList<TechnologyReferenceUsage> unknown,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> expectedStates)
    {
        _sources = sources;
        AffectedReferences = affected;
        UnknownReferences = unknown;
        ExpectedStates = expectedStates;
    }

    public IReadOnlyList<string> Paths => _sources.Select(source => source.Path!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<TechnologyReferenceUsage> AffectedReferences { get; }
    public IReadOnlyList<TechnologyReferenceUsage> UnknownReferences { get; }
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }

    public static TechnologyReferenceMigrationPlan Prepare(
        IEnumerable<TechnologyReferenceFile> files,
        IReadOnlyList<TechnologyRenameOperation> operations)
    {
        var sources = new List<TechnologyReferenceSource>();
        var states = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
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
                throw new IOException($"'{path}' changed while its technology references were being inspected. No files were written.");
            states[path] = stateAfterRead;
            sources.Add(new TechnologyReferenceSource(file.Label, document, file.Kind, path, CanMigrate: true));
        }

        var affected = new List<TechnologyReferenceUsage>();
        var unknown = new List<TechnologyReferenceUsage>();
        var changedSources = new HashSet<TechnologyReferenceSource>(ReferenceEqualityComparer.Instance);
        foreach (var operation in operations)
        {
            var usages = TechnologyReferenceIndex.Build(sources, [operation.OldName])
                .Where(usage => usage.TechnologyName.Equals(operation.OldName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            affected.AddRange(usages.Where(usage => usage.IsRecognized));
            unknown.AddRange(usages.Where(usage => !usage.IsRecognized));
            if (unknown.Count > 0)
                continue;
            foreach (var source in sources)
            {
                if (TechnologyReferenceIndex.RenameRecognizedReferences(source, operation.OldName, operation.NewName) > 0)
                    changedSources.Add(source);
            }

            var remainingRecognized = TechnologyReferenceIndex.Build(sources, [operation.OldName])
                .Where(usage => usage.TechnologyName.Equals(operation.OldName, StringComparison.OrdinalIgnoreCase) &&
                                usage.IsRecognized)
                .ToList();
            if (remainingRecognized.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The technology rename verification still found {remainingRecognized.Count} recognized reference(s) to '{operation.OldName}'. No files were written.");
            }
        }

        var changed = sources.Where(changedSources.Contains).ToList();
        var changedStates = states
            .Where(entry => changed.Any(source => source.Path!.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        return new TechnologyReferenceMigrationPlan(changed, affected, unknown, changedStates);
    }

    public void Save(Action<XDocument, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var source in _sources)
            writer(source.Document, source.Path!);
    }

    public XDocument? GetDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var fullPath = Path.GetFullPath(path);
        var source = _sources.FirstOrDefault(source =>
            source.Path != null && Path.GetFullPath(source.Path).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        return source == null ? null : new XDocument(source.Document);
    }
}
