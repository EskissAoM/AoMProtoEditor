using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record TechnologyReferenceFile(string Path, TechnologyReferenceDocumentKind Kind, string Label);

public sealed class TechnologyReferenceMigrationPlan
{
    private readonly IReadOnlyList<TechnologyReferenceSource> _sources;
    private readonly FileDiscoverySnapshot? _discovery;

    private TechnologyReferenceMigrationPlan(
        IReadOnlyList<TechnologyReferenceSource> sources,
        IReadOnlyList<TechnologyReferenceUsage> affected,
        IReadOnlyList<TechnologyReferenceUsage> unknown,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> expectedStates,
        FileDiscoverySnapshot? discovery)
    {
        _sources = sources;
        AffectedReferences = affected;
        UnknownReferences = unknown;
        ExpectedStates = expectedStates;
        _discovery = discovery;
    }

    public IReadOnlyList<string> Paths => _sources.Select(source => source.Path!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<TechnologyReferenceUsage> AffectedReferences { get; }
    public IReadOnlyList<TechnologyReferenceUsage> UnknownReferences { get; }
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }

    // Call inside the owner transaction before its first write. Save may run after
    // that transaction has intentionally created or moved reference files.
    public void ValidateDiscovery() => _discovery?.Validate();

    public static TechnologyReferenceMigrationPlan Prepare(
        IEnumerable<TechnologyReferenceFile> files,
        IReadOnlyList<TechnologyRenameOperation> operations,
        FileDiscoverySnapshot? discovery = null)
        => PrepareWithBudget(new ReferenceReadBudget(), files, operations, discovery);

    internal static TechnologyReferenceMigrationPlan PrepareWithBudget(ReferenceReadBudget budget,
        IEnumerable<TechnologyReferenceFile> files, IReadOnlyList<TechnologyRenameOperation> operations,
        FileDiscoverySnapshot? discovery = null)
    {
        var sources = new List<TechnologyReferenceSource>();
        var states = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files
                     .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                     .GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            budget.AdmitFile(path);
            var stateBeforeRead = FileIntegrityTransaction.CaptureState(path);
            states[path] = stateBeforeRead;
            if (!stateBeforeRead.Exists)
                continue;
            var document = ModReferenceDocumentReader.Load(path, budget);
            var stateAfterRead = FileIntegrityTransaction.CaptureState(path);
            if (stateBeforeRead != stateAfterRead)
                throw new IOException($"'{path}' changed while its technology references were being inspected. No files were written.");
            states[path] = stateAfterRead;
            sources.Add(new TechnologyReferenceSource(file.Label, document, file.Kind, path,
                CanMigrate: !ModReferenceDocumentReader.IsCompiledTactics(path)));
        }

        var affected = new List<TechnologyReferenceUsage>();
        var unknown = new List<TechnologyReferenceUsage>();
        var changedSources = new HashSet<TechnologyReferenceSource>(ReferenceEqualityComparer.Instance);
        foreach (var operation in operations)
        {
            var usages = TechnologyReferenceIndex.FindReferencesTo(sources, operation.OldName);
            var compiledUsage = usages.FirstOrDefault(usage => usage.Path != null &&
                ModReferenceDocumentReader.IsCompiledTactics(usage.Path));
            if (compiledUsage != null)
                throw ModReferenceDocumentReader.CannotRewrite(compiledUsage.Path!);
            affected.AddRange(usages.Where(usage => usage.IsRecognized));
            unknown.AddRange(usages.Where(usage => !usage.IsRecognized));
            if (unknown.Count > 0)
                continue;
            foreach (var source in sources)
            {
                if (TechnologyReferenceIndex.RenameRecognizedReferences(source, operation.OldName, operation.NewName) > 0)
                    changedSources.Add(source);
            }

            var remainingRecognized = TechnologyReferenceIndex.FindReferencesTo(sources, operation.OldName)
                .Where(usage => usage.IsRecognized)
                .ToList();
            if (remainingRecognized.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The technology rename verification still found {remainingRecognized.Count} recognized reference(s) to '{operation.OldName}'. No files were written.");
            }
        }

        var changed = sources.Where(changedSources.Contains).ToList();
        foreach (var source in changed)
            budget.InspectExisting(source.Document, source.Path!);
        // Unchanged sources are read dependencies too: a concurrent edit may add
        // an old-name reference after inspection. Only Paths are write targets.
        var plan = new TechnologyReferenceMigrationPlan(changed, affected, unknown, states, discovery);
        plan.ValidateDiscovery();
        return plan;
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
