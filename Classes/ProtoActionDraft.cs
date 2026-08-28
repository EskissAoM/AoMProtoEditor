using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Stable, control-independent snapshot used while a ProtoAction card is rebuilt.
/// All collections are copied so renderers cannot observe controls being removed by
/// another renderer in the same refresh transaction.
/// </summary>
internal sealed class ProtoActionDraft
{
    internal sealed record ElementSnapshot(XElement Element, bool IsSupported = true);

    public long Revision { get; private set; }
    public string Name { get; private set; } = "";
    public string Type { get; private set; } = "";
    public string AttackMode { get; private set; } = "";
    public string RateOfFire { get; private set; } = "";
    public string MinRange { get; private set; } = "";
    public string MaxRange { get; private set; } = "";
    public IReadOnlyList<(string DamageType, string Amount)> Damages { get; private set; } = [];
    public IReadOnlyList<(string UnitType, string Multiplier)> DamageBonuses { get; private set; } = [];
    public IReadOnlyDictionary<string, string> SimpleValues { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, List<ProtoActionStructuredFieldEntry>> StructuredValues { get; private set; }
        = new Dictionary<string, List<ProtoActionStructuredFieldEntry>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> SelectedFlagTags { get; private set; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ForcedVisibleFieldTags { get; private set; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> CustomValues { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, XElement?>? EmpowerSections { get; private set; }
    public IReadOnlyList<ElementSnapshot>? OnHitEffects { get; private set; }
    public IReadOnlyList<XElement>? FullChargedElements { get; private set; }
    public IReadOnlyList<XElement>? ChargedElements { get; private set; }

    public void Replace(
        long revision,
        string? name,
        string? type,
        string? attackMode,
        string? rateOfFire,
        string? minRange,
        string? maxRange,
        IEnumerable<(string DamageType, string Amount)> damages,
        IEnumerable<(string UnitType, string Multiplier)> damageBonuses,
        IReadOnlyDictionary<string, string> simpleValues,
        IReadOnlyDictionary<string, List<ProtoActionStructuredFieldEntry>> structuredValues,
        IEnumerable<string> selectedFlagTags,
        IEnumerable<string> forcedVisibleFieldTags,
        IReadOnlyDictionary<string, string> customValues,
        IReadOnlyDictionary<string, XElement?>? empowerSections,
        IEnumerable<ElementSnapshot>? onHitEffects,
        IEnumerable<XElement>? fullChargedElements,
        IEnumerable<XElement>? chargedElements)
    {
        Revision = revision;
        Name = name?.Trim() ?? "";
        Type = type?.Trim() ?? "";
        AttackMode = attackMode?.Trim() ?? "";
        RateOfFire = rateOfFire?.Trim() ?? "";
        MinRange = minRange?.Trim() ?? "";
        MaxRange = maxRange?.Trim() ?? "";
        Damages = damages.Select(entry => (entry.DamageType, entry.Amount)).ToList();
        DamageBonuses = damageBonuses.Select(entry => (entry.UnitType, entry.Multiplier)).ToList();
        SimpleValues = simpleValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        StructuredValues = structuredValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(CloneEntry).ToList(),
            StringComparer.OrdinalIgnoreCase);
        SelectedFlagTags = new HashSet<string>(selectedFlagTags, StringComparer.OrdinalIgnoreCase);
        ForcedVisibleFieldTags = new HashSet<string>(forcedVisibleFieldTags, StringComparer.OrdinalIgnoreCase);
        CustomValues = customValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        EmpowerSections = empowerSections?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value == null ? null : new XElement(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        OnHitEffects = onHitEffects?.Select(snapshot =>
            new ElementSnapshot(new XElement(snapshot.Element), snapshot.IsSupported)).ToList();
        FullChargedElements = CloneElements(fullChargedElements);
        ChargedElements = CloneElements(chargedElements);
    }

    public Dictionary<string, string> CopySimpleValues()
        => SimpleValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<ProtoActionStructuredFieldEntry>> CopyStructuredValues()
        => StructuredValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(CloneEntry).ToList(),
            StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CopySelectedFlagTags()
        => new(SelectedFlagTags, StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CopyForcedVisibleFieldTags()
        => new(ForcedVisibleFieldTags, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> CopyCustomValues()
        => CustomValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, XElement?>? CopyEmpowerSections()
        => EmpowerSections?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value == null ? null : new XElement(pair.Value),
            StringComparer.OrdinalIgnoreCase);

    public List<ElementSnapshot>? CopyOnHitEffects()
        => OnHitEffects?.Select(snapshot =>
            new ElementSnapshot(new XElement(snapshot.Element), snapshot.IsSupported)).ToList();
    public List<XElement>? CopyFullChargedElements() => CloneElements(FullChargedElements);
    public List<XElement>? CopyChargedElements() => CloneElements(ChargedElements);

    private static ProtoActionStructuredFieldEntry CloneEntry(ProtoActionStructuredFieldEntry source)
    {
        var clone = new ProtoActionStructuredFieldEntry { Value = source.Value };
        foreach (var attribute in source.Attributes)
            clone.Attributes[attribute.Key] = attribute.Value;
        return clone;
    }

    private static List<XElement>? CloneElements(IEnumerable<XElement>? elements)
        => elements?.Select(element => new XElement(element)).ToList();
}
