using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum PowerEditorCoverageMode
{
    Structured,
    Shared,
    PreserveOnly,
    Unsupported
}

public sealed record PowerEditorCoveragePolicy(
    string Scope,
    string Name,
    PowerEditorCoverageMode Mode,
    string Route,
    string? Note = null);

public sealed record PowerEditorNestedCoverageRoute(
    string EditorToken,
    IReadOnlyList<string> SupportedAttributes);

public sealed record PowerSchemaFieldSnapshot(
    string Path,
    int Count,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<string> Shapes);

public sealed record PowerSchemaPowerSnapshot(
    string Name,
    string Type,
    IReadOnlyList<string> RootAttributes,
    IReadOnlyList<PowerSchemaFieldSnapshot> Fields);

public sealed record PowerSchemaCorpusSnapshot(
    int FormatVersion,
    IReadOnlyList<PowerSchemaPowerSnapshot> Powers,
    IReadOnlyList<string> ElementNames,
    IReadOnlyList<string> AttributeNames)
{
    public const int CurrentFormatVersion = 1;

    public static PowerSchemaCorpusSnapshot Capture(IEnumerable<XDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var powers = documents
            .SelectMany(document => document.Root?.DescendantsAndSelf() ?? [])
            .Where(element => element.Name.LocalName.Equals(PowerCatalog.EntryName,
                StringComparison.OrdinalIgnoreCase))
            .Select(CapturePower)
            .Where(power => power.Name.Length > 0)
            .OrderBy(power => power.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(power => power.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PowerSchemaCorpusSnapshot(
            CurrentFormatVersion,
            powers,
            powers.SelectMany(power => power.Fields)
                .Select(field => field.Path.Split('/')[^1])
                .Prepend(PowerCatalog.EntryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            powers.SelectMany(power => power.RootAttributes
                    .Concat(power.Fields.SelectMany(field => field.Attributes)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public static PowerSchemaCorpusSnapshot Parse(string json)
        => JsonSerializer.Deserialize<PowerSchemaCorpusSnapshot>(json, JsonOptions)
           ?? throw new InvalidDataException("The power schema baseline is empty.");

    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);

    private static PowerSchemaPowerSnapshot CapturePower(XElement power)
    {
        var name = PowerCatalog.GetName(power);
        var type = power.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
        var fields = power.Descendants()
            .GroupBy(GetRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PowerSchemaFieldSnapshot(
                group.Key,
                group.Count(),
                group.SelectMany(element => element.Attributes())
                    .Select(attribute => attribute.Name.LocalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                group.Select(GetShape)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(field => field.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PowerSchemaPowerSnapshot(
            name,
            type,
            power.Attributes().Select(attribute => attribute.Name.LocalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            fields);
    }

    private static string GetRelativePath(XElement element)
        => string.Join('/', element.AncestorsAndSelf()
            .TakeWhile(ancestor => !ancestor.Name.LocalName.Equals(PowerCatalog.EntryName,
                StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Select(ancestor => ancestor.Name.LocalName));

    private static string GetShape(XElement element)
    {
        var attributes = element.Attributes()
            .Select(attribute => attribute.Name.LocalName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var children = element.Elements()
            .Select(child => child.Name.LocalName)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}[{group.Count()}]");
        return $"attrs({string.Join(',', attributes)});children({string.Join(',', children)})";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// The auditable contract between the shipped power corpus and the current editor. Any base field
/// that is not explicitly listed as a gap is expected to have a structured or shared editor route.
/// Unknown mod fields remain preserved by the raw XML system, but are not counted as base support.
/// </summary>
public static class PowerEditorAuditInventory
{
    public static IReadOnlyList<string> KnownPowerTypes { get; } = PowerSchemaRegistry.OriginalPowerTypes;

    /// <summary>
    /// Power types accepted by the game and supported by the editor even though they do not occur
    /// in the shipped god-power corpus used by the audit baseline.
    /// </summary>
    public static IReadOnlyList<string> SupportedExtensionPowerTypes { get; } =
        PowerSchemaRegistry.ExtensionPowerTypes;

    public static IReadOnlyList<string> EditorPowerTypes { get; } = PowerSchemaRegistry.EditorPowerTypes;

    public static IReadOnlyList<PowerEditorCoveragePolicy> KnownGaps { get; } =
    [
        ElementGap("rootedheal", "FavorForest", "Deprecated base field; intentionally not exposed."),
        ElementGap("scorchfactor", "Bolt", "Deprecated base field; intentionally not exposed."),
        ElementGap("snowpercent", "Fimbulwinter", "Deprecated base field; intentionally not exposed."),
        ElementGap("uivfx", "PowerClone", "Deprecated base field; intentionally not exposed.")
    ];

    public static IReadOnlyDictionary<string, string> KnownSpellingMismatches { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> KnownSourceNormalizations { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ShiftingSands/rangeindicator"] =
                "The malformed original contains two values; the editor intentionally retains only the last one."
        };

    public static IReadOnlyDictionary<string, string> SharedNestedAttributeRoutes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["allowdisconnectedgrowth"] = "MajorGodCatalog.TerrainCreepFlags",
            ["avoidimpassable"] = "MajorGodCatalog.TerrainCreepFlags"
        };

    /// <summary>
    /// Path-aware routes for every field nested below a root power field in the checked-in base
    /// corpus. The key is power type plus relative XML path. Keeping the supported attributes on
    /// the route prevents a root-level or unrelated field with the same leaf name from satisfying
    /// the audit accidentally.
    /// </summary>
    public static IReadOnlyDictionary<string, PowerEditorNestedCoverageRoute> KnownNestedFieldRoutes { get; } =
        new Dictionary<string, PowerEditorNestedCoverageRoute>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bronze/armor/abstracttype"] = NestedRoute("RefreshBronzeArmors"),
            ["Bronze/armor/armortype"] = NestedRoute("RefreshBronzeArmors", "vulnerabilitypercentdelta"),
            ["Bronze/armor/vfxproto"] = NestedRoute("RefreshBronzeArmors"),
            ["CageUnit/similarunittypes/unittype"] = NestedRoute("_cageSimilarTypeChips"),
            ["CeaseFire/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["EarthWall/damagetiers/damagetier"] = NestedRoute("RefreshDamageTiers", "mindestroyed"),
            ["Eclipse/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["EffectInArea/onhiteffect/damage"] = NestedRoute("BuildEffectInAreaOnHitCard", "type"),
            ["EffectInArea/onhiteffect/target"] = NestedRoute("BuildEffectInAreaOnHitCard", "attacktype", "ignoretype"),
            ["GeneralEffect/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["PowerClone/duplicatetargetreplacement/target"] = NestedRoute("RefreshPowerCloneFields"),
            ["Prosperity/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["Rain/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["SwitchingEffects/effects/combatxptierreq"] = NestedRoute("BuildSwitchingRequirementFields"),
            ["SwitchingEffects/effects/displaynameid"] = NestedRoute("BuildSwitchingEffectContainer"),
            ["SwitchingEffects/effects/effect"] = NestedRoute("BuildRainEffectCard", "action", "amount",
                "hidetooltip", "norevert", "relativity", "resource", "type"),
            ["SwitchingEffects/effects/effect/target"] = NestedRoute("BuildRainEffectCard", "type"),
            ["SwitchingEffects/effects/extratooltips"] = NestedRoute("AddSwitchingExtraTooltipField"),
            ["SwitchingEffects/effects/extratooltips/string"] = NestedRoute("AddSwitchingExtraTooltipField"),
            ["SwitchingEffects/effects/icon"] = NestedRoute("BuildSwitchingEffectContainer"),
            ["SwitchingEffects/effects/notificationmessageid"] = NestedRoute("BuildSwitchingEffectContainer"),
            ["SwitchingEffects/effects/notificationsound"] = NestedRoute("BuildSwitchingEffectContainer"),
            ["SwitchingEffects/effects/resourcetierreq"] = NestedRoute("BuildSwitchingRequirementFields", "type"),
            ["SwitchingEffects/effects/rolloverid"] = NestedRoute("BuildSwitchingEffectContainer"),
            ["SwitchingEffects/effects/timerrolloverid"] = NestedRoute("RefreshSwitchingFields"),
            ["Tempest/largestrikeintervaltiming/time"] = NestedRoute("RefreshTempestLargeTimings")
        };

    public static IReadOnlyList<PowerEditorCoveragePolicy> Classify(PowerSchemaCorpusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var gaps = KnownGaps.ToDictionary(policy => Key(policy.Scope, policy.Name),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<PowerEditorCoveragePolicy>();

        foreach (var name in snapshot.ElementNames)
        {
            if (gaps.TryGetValue(Key("element", name), out var gap))
                result.Add(gap);
            else
                result.Add(new PowerEditorCoveragePolicy("element", name,
                    IsSharedElement(name) ? PowerEditorCoverageMode.Shared : PowerEditorCoverageMode.Structured,
                    IsSharedElement(name) ? "shared/general power editor" : "type-specific power editor"));
        }

        foreach (var name in snapshot.AttributeNames)
        {
            if (gaps.TryGetValue(Key("attribute", name), out var gap))
                result.Add(gap);
            else
            {
                var shared = SharedNestedAttributeRoutes.TryGetValue(name, out var route);
                result.Add(new PowerEditorCoveragePolicy("attribute", name,
                    shared ? PowerEditorCoverageMode.Shared : PowerEditorCoverageMode.Structured,
                    shared ? route! : "power attribute or structured card editor"));
            }
        }

        return result.OrderBy(policy => policy.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(policy => policy.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSharedElement(string name)
        => SharedElements.Contains(name);

    private static PowerEditorCoveragePolicy ElementGap(string name, string route, string note)
        => new("element", name, PowerEditorCoverageMode.Unsupported, route, note);

    private static PowerEditorCoveragePolicy AttributeGap(string name, string route, string note)
        => new("attribute", name, PowerEditorCoverageMode.Unsupported, route, note);

    private static PowerEditorNestedCoverageRoute NestedRoute(string editorToken, params string[] attributes)
        => new(editorToken, attributes);

    private static string Key(string scope, string name) => $"{scope}:{name}";

    private static readonly HashSet<string> SharedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "power", "displayname", "rollovertext", "active", "icon", "activetime", "activetimeontarget",
        "buildup", "shutdown", "shutdownfadetime", "cost", "repeatcost", "placement", "rangeindicator", "radius",
        "powerplayerrelation", "attacktarget", "soundset", "notification", "minimapnotification",
        "effect", "target", "onhiteffect"
    };
}
