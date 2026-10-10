using Avalonia.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Immutable lookup catalogs consumed while composing Power editor controls.</summary>
public sealed class PowerEditorCatalogs
{
    public PowerEditorCatalogs(
        IEnumerable<string>? iconPaths = null,
        IEnumerable<string>? protoUnitNames = null,
        IEnumerable<string>? unitTypeNames = null,
        IEnumerable<string>? terrainCreepNames = null,
        IEnumerable<string>? protoActionNames = null,
        IEnumerable<string>? technologyNames = null,
        IEnumerable<string>? animationNames = null,
        IEnumerable<string>? boneNames = null,
        IEnumerable<string>? tacticNames = null)
    {
        IconPaths = Normalize(iconPaths, IconCatalog.ToResourceRelativePath);
        ProtoUnitNames = Normalize(protoUnitNames);
        UnitTypeNames = Normalize(unitTypeNames);
        UnitOrTypeNames = ProtoUnitNames.Concat(UnitTypeNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        TerrainCreepNames = Normalize(terrainCreepNames);
        ProtoActionNames = Normalize(protoActionNames);
        TechnologyNames = Normalize(technologyNames);
        AnimationNames = Normalize(animationNames);
        BoneNames = Normalize(boneNames);
        TacticNames = Normalize(tacticNames);
    }

    public IReadOnlyList<string> IconPaths { get; }
    public IReadOnlyList<string> ProtoUnitNames { get; }
    public IReadOnlyList<string> UnitTypeNames { get; }
    public IReadOnlyList<string> UnitOrTypeNames { get; }
    public IReadOnlyList<string> TerrainCreepNames { get; }
    public IReadOnlyList<string> ProtoActionNames { get; }
    public IReadOnlyList<string> TechnologyNames { get; }
    public IReadOnlyList<string> AnimationNames { get; }
    public IReadOnlyList<string> BoneNames { get; }
    public IReadOnlyList<string> TacticNames { get; }

    private static IReadOnlyList<string> Normalize(
        IEnumerable<string>? values,
        Func<string, string>? convert = null)
        => (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => convert?.Invoke(value) ?? value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Host callbacks used at Power transaction and dialog boundaries.</summary>
public sealed record PowerEditorHostServices(
    Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
        PrepareReferenceMigration = null,
    Action<GameIdentifierReferenceMigrationPlan>? AdoptReferenceMigration = null,
    Func<Window, string, Task<bool>>? EnsureCanDeleteAsync = null,
    Func<Window, string, Task<bool>>? ConfirmDeleteAsync = null,
    Window? DialogOwner = null,
    Func<string, Task<string?>>? ResolveStringAsync = null,
    Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? SaveStringsAsync = null,
    IconPreviewService? IconPreviewService = null);

/// <summary>
/// The complete non-visual input needed to construct a Power editor. This is the production
/// composition boundary; the view does not discover host services or game catalogs itself.
/// </summary>
public sealed class PowerEditorComposition
{
    public PowerEditorComposition(
        IEnumerable<PowerDefinition> baseDefinitions,
        string? modPath,
        string? modStringsPath = null,
        PowerEditorCatalogs? catalogs = null,
        PowerEditorHostServices? hostServices = null)
    {
        ArgumentNullException.ThrowIfNull(baseDefinitions);
        BaseDefinitions = baseDefinitions.ToList();
        ModPath = modPath;
        ModStringsPath = modStringsPath;
        Catalogs = catalogs ?? new PowerEditorCatalogs();
        HostServices = hostServices ?? new PowerEditorHostServices();
    }

    public IReadOnlyList<PowerDefinition> BaseDefinitions { get; }
    public string? ModPath { get; }
    public string? ModStringsPath { get; }
    public PowerEditorCatalogs Catalogs { get; }
    public PowerEditorHostServices HostServices { get; }
}
