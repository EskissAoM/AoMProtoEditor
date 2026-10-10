namespace AoMDivineDataEditor.Classes;

public sealed record PowerTypeSchema(
    string Name,
    bool OccursInOriginalCorpus,
    IReadOnlyList<string> SpecificGroups);

/// <summary>
/// Single source of truth for Power types and the ordered groups owned by each
/// structured editor. XML tag shape remains in the field/card implementations.
/// </summary>
public static class PowerSchemaRegistry
{
    private static readonly string[] OriginalTypeNames =
    [
        "ArcadianMeadow", "Bolt", "Bronze", "CageUnit", "CeaseFire", "Chaos", "Curse",
        "Deconstruction", "DivineSlash", "DroughtLand", "DwarvenMine", "EarthMonster", "EarthWall",
        "Earthquake", "Eclipse", "EffectInArea", "FavorForest", "Fimbulwinter", "ForestFire",
        "FreyrsGift", "Frost", "GaiaForest", "GeneralEffect", "GreatFlood", "GreatHunt", "Implode",
        "LightningStorm", "Locust", "Lure", "Meteor", "NewMoon", "PSeeds", "Pestilence",
        "PowerClone", "Prosperity", "Rain", "Restoration", "SacredGate", "ShiftingSands", "Shockwave",
        "SingleModify", "SmitingGust", "Spiders", "Spy", "Stampede", "Stealth", "SwapUnit",
        "SwitchingEffects", "TartarianGate", "Teleport", "TempUnit", "Tempest", "ThunderBurst",
        "Tornado", "Traitor", "Typhoon", "Undermine", "UnderworldInvasion", "UnitModify", "Valor",
        "VibrantLand", "Vortex", "Wither"
    ];

    private static readonly string[] ExtensionTypeNames = ["FoxCharm"];

    private static readonly IReadOnlyDictionary<string, string[]> Layouts =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Deconstruction"] = ["Cracks Sound", "Time Factor"],
            ["Shockwave"] = ["Stun lenght", "Shockwave VFX", "Throw properties"],
            ["GaiaForest"] = ["Tree Unit", "Tree amount", "Tree Spawn Interval", "Tree Effect VFX", "Growth radius"],
            ["Valor"] = ["Effect VFX"],
            ["Traitor"] = ["Damage Percent on Conversion", "VFX Proto"],
            ["Chaos"] = ["Effect time", "VFX Proto"],
            ["Spiders"] = ["Spider properties", "Number to create", "Separation Distance", "Hatch properties", "Warn time", "Initial Delay", "Human Death Sound"],
            ["Vortex"] = ["Initial Delay", "Unit Effect Time", "Vortex properties", "Drop Interval", "Max Retry", "Max Unit Height", "Lift Sound", "Land Sound"],
            ["TartarianGate"] = ["Spawn Proto", "Max Spawn Amount", "Revealer Unit", "Tartarian Gate", "Rumble Time", "First Spawn delay", "Spawn interval", "Throw properties"],
            ["Implode"] = ["Implode Protos", "Implode Sounds", "Pull properties", "Explosion", "Sphere properties", "Implode Damage interval", "Shear", "Implode delay", "Implode Timing", "Orbit Radius Squared", "Angular Acceleration", "Implode Camera Shake", "Throw properties"],
            ["PSeeds"] = ["Farm Proto", "Number to Transform", "Farm VFX", "Stealth Level"],
            ["EarthWall"] = ["Earth Wall", "Damage Tiers"],
            ["Stealth"] = ["Vanish VFX", "Stealth Level"],
            ["FavorForest"] = ["Rooted enemy VFX", "Tree Area VFX", "Tree VFX", "Stay Time", "Root duration", "Root damage", "Slow Heal Multiplier"],
            ["TempUnit"] = ["Create Unit", "Attach Unit", "Age Adjustment", "Create placement"],
            ["FreyrsGift"] = ["Age Specific Create Unit", "Attach Unit", "Age Adjustment", "Create placement"],
            ["VibrantLand"] = ["Vibrant Generation", "Rotate Speed", "Growth", "Scale"],
            ["Restoration"] = ["Heal speed", "Slow Heal Multiplier", "Scan interval", "Heal VFX"],
            ["DroughtLand"] = ["Terrain Creep", "Land VFX", "Wave Unit", "Initial Damage", "Burn Time"],
            ["GreatFlood"] = ["Flood Wave", "Velocity", "Attack Action", "Attack Interval", "Damage Cap"],
            ["ShiftingSands"] = ["Portal", "Portal timing", "Placement Radius"],
            ["Locust"] = ["Swarm Unit", "Swarm Way Points", "Damage Interval"],
            ["Bolt"] = ["Strike Unit", "Bolt VFX", "Strike Position"],
            ["Pestilence"] = ["Pestilence Penalty", "Pestilence VFX"],
            ["Bronze"] = ["Armor"],
            ["Teleport"] = ["Teleport Portal"],
            ["GreatHunt"] = ["Max food duplicated", "Great Hunt extras"],
            ["Spy"] = ["Spy Indicator"],
            ["Stampede"] = ["Stampede", "Stampede Full length"],
            ["SwapUnit"] = ["Swap", "Swap Min/Max", "Swap Options", "Swap Flags"],
            ["Curse"] = ["Curse Resources", "Curse Swap"],
            ["SmitingGust"] = ["Gust", "Gust Timing", "Gust Damage", "Throw Max Size"],
            ["SacredGate"] = ["Create Unit", "Attach Unit", "Age Adjustment", "Create placement"],
            ["DwarvenMine"] = ["Create Unit", "Attach Unit", "Age Adjustment", "Create placement"],
            ["DivineSlash"] = ["Divine Attack", "Divine Damage", "Camera Shake Delay"],
            ["Lure"] = ["Lure Unit", "Attract Type", "Attraction", "Lure Spawn", "Lure Age info"],
            ["Undermine"] = ["Undermine Attack", "Undermine Targets", "Deform Depth"],
            ["ForestFire"] = ["Forest Fire Units", "Strike Interval", "Forest Fire Burn", "Forest Fire Search"],
            ["Tempest"] = ["Tempest Strike", "Strike Interval", "Tempest Strike Position", "Tempest Soundset", "Large Strike", "Tempest Attack", "Tempest VFX", "Tempest Clouds", "Tempest Drift"],
            ["LightningStorm"] = ["Tempest Strike", "Strike Interval", "Tempest Strike Position", "Tempest Soundset", "Lightning VFX", "Tempest Clouds", "Lightning Rain"],
            ["Earthquake"] = ["Earthquake Attack", "Earthquake Zone Radius", "Earthquake Zone Damage Ratio", "Earthquake Dust VFX"],
            ["ThunderBurst"] = ["Thunder Burst", "Throw properties", "Thunder Burst VFX"],
            ["Typhoon"] = ["Typhoon Dragon", "Typhoon", "Tree Bend"],
            ["Frost"] = ["Frost Freeze", "Frost VFX", "Frost Drift"],
            ["Wither"] = ["Wither Wave", "Wither Kill Unit Type", "Wither Damage", "Wither Creep Generators", "Terrain Creep"],
            ["ArcadianMeadow"] = ["Arcadian Placement", "Arcadian Spawn", "Arcadian VFX Grass", "Terrain Creep"],
            ["UnderworldInvasion"] = ["Underworld Eidolon", "Underworld Spawn", "Underworld VFX", "Underworld Lighting", "Underworld Tech Effect", "Underworld Sudden Death"],
            ["PowerClone"] = ["Power Clone Slot", "Power Clone VFX", "Duplicate Replacement"],
            ["Fimbulwinter"] = ["Fimbul Wolf", "Fimbul Main Pack", "Fimbul Secondary Pack", "Fimbul Wander", "Fimbul Target system", "Fimbul Snow Drift", "Fimbul Strike Time"],
            ["CageUnit"] = ["Caged Unit", "Cage Kill Similar", "Cage VFX", "Cage Soundset"],
            ["Rain"] = ["Rain", "Rain Effects"],
            ["EffectInArea"] = ["Effect In Area", "Effect In Area On Hit"],
            ["SwitchingEffects"] = ["Switching Type", "Switching Progress", "Switching VFX Attach", "Switching Effects"],
            ["UnitModify"] = ["Unit Modify VFX", "Unit Modify"],
            ["SingleModify"] = ["Unit Modify VFX", "Unit Modify"],
            ["NewMoon"] = ["New Moon Research", "New Moon VFX", "Unit Modify"],
            ["FoxCharm"] = ["Fox Charm Affected Number", "Fox Charm VFX"],
            ["EarthMonster"] = ["Earth Monster Unit", "Earth Monster Strike", "Earth Monster Regurgitate", "Earth Monster Chomp", "Earth Monster Swallow", "Earth Monster Throw Radius", "Throw properties", "Earth Monster Land Impact", "Earth Monster Soundset"],
            ["Meteor"] = ["Meteor Unit", "Strike Interval", "Meteor Strikes", "Meteor Splash VFX", "Meteor Soundset", "Throw properties"],
            ["Tornado"] = ["Tornado Unit", "Tornado Funnel", "Throw properties", "Debris Velocity"],
            ["GeneralEffect"] = ["Power Effects"],
            ["Eclipse"] = ["Power Effects"],
            ["CeaseFire"] = ["CeaseFire VFX", "Power Effects"],
            ["Prosperity"] = ["Prosperity VFX", "Prosperity Types", "Power Effects"]
        };

    public static IReadOnlyList<string> OriginalPowerTypes { get; } = [.. OriginalTypeNames];
    public static IReadOnlyList<string> ExtensionPowerTypes { get; } = [.. ExtensionTypeNames];
    public static IReadOnlyList<string> EditorPowerTypes { get; } = OriginalTypeNames
        .Concat(ExtensionTypeNames)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static IReadOnlyDictionary<string, PowerTypeSchema> Schemas { get; } = EditorPowerTypes
        .ToDictionary(
            name => name,
            name => new PowerTypeSchema(
                name,
                OriginalTypeNames.Contains(name, StringComparer.OrdinalIgnoreCase),
                Layouts.TryGetValue(name, out var groups) ? groups : []),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> TypeOnlySpecificGroups { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Implode Protos", "Implode Sounds", "Pull properties", "Explosion", "Sphere properties",
            "Implode Damage interval", "Shear", "Implode delay", "Implode Timing", "Orbit Radius Squared",
            "Angular Acceleration", "Implode Camera Shake", "Farm Proto", "Farm VFX", "Earth Wall",
            "Damage Tiers", "Vanish VFX", "Rooted enemy VFX", "Tree Area VFX", "Root duration", "Root damage",
            "Vibrant Generation", "Rotate Speed", "Growth", "Scale", "Heal speed", "Scan interval", "Heal VFX",
            "Land VFX", "Burn Time", "Flood Wave", "Velocity", "Attack Action", "Bolt VFX",
            "Pestilence Penalty", "Pestilence VFX", "Armor", "Teleport Portal", "Max food duplicated",
            "Great Hunt extras", "Spy Indicator", "Age Specific Create Unit", "Stampede", "Stampede Full length",
            "Swap", "Swap Min/Max", "Swap Options", "Swap Flags", "Curse Resources", "Curse Swap", "Gust",
            "Gust Timing", "Gust Damage", "Divine Attack", "Divine Damage", "Lure Unit", "Attract Type",
            "Attraction", "Lure Spawn", "Lure Age info", "Undermine Attack", "Undermine Targets", "Deform Depth",
            "Tempest Strike", "Strike Interval", "Tempest Strike Position", "Tempest Soundset", "Tempest Attack",
            "Large Strike", "Tempest VFX", "Tempest Clouds", "Tempest Drift", "Lightning VFX", "Lightning Rain",
            "Earthquake Attack", "Earthquake Zone Radius", "Earthquake Zone Damage Ratio", "Earthquake Dust VFX",
            "Thunder Burst", "Thunder Burst VFX", "Typhoon Dragon", "Typhoon", "Frost Freeze", "Frost VFX",
            "Frost Drift", "Wither Wave", "Wither Kill Unit Type", "Wither Damage", "Wither Creep Generators",
            "Arcadian Placement", "Arcadian Spawn", "Arcadian VFX Grass", "Underworld Eidolon", "Underworld Spawn",
            "Underworld VFX", "Underworld Lighting", "Underworld Tech Effect", "Underworld Sudden Death",
            "Power Clone Slot", "Power Clone VFX", "Duplicate Replacement", "Fimbul Wolf", "Fimbul Main Pack",
            "Fimbul Secondary Pack", "Fimbul Wander", "Fimbul Target system", "Fimbul Snow Drift",
            "Fimbul Strike Time", "Caged Unit", "Cage Kill Similar", "Cage VFX", "Cage Soundset", "Rain",
            "Rain Effects", "Switching Type", "Switching Progress", "Switching VFX Attach", "Switching Effects",
            "New Moon Research", "New Moon VFX", "Unit Modify VFX", "Unit Modify", "Earth Monster Unit",
            "Earth Monster Strike", "Earth Monster Regurgitate", "Earth Monster Chomp", "Earth Monster Swallow",
            "Earth Monster Throw Radius", "Earth Monster Land Impact", "Earth Monster Soundset", "Meteor Unit",
            "Meteor Strikes", "Meteor Splash VFX", "Meteor Soundset", "Tornado Unit", "Tornado Funnel",
            "Debris Velocity", "Forest Fire Units", "Forest Fire Burn", "Forest Fire Search", "Power Effects",
            "CeaseFire VFX", "Effect In Area", "Effect In Area On Hit", "Prosperity VFX", "Prosperity Types",
            "Fox Charm Affected Number", "Fox Charm VFX"
        };

    public static IReadOnlyList<string> GetSpecificGroups(string? powerType)
        => powerType != null && Schemas.TryGetValue(powerType, out var schema)
            ? schema.SpecificGroups
            : [];

    public static bool IsDefaultSpecificGroup(string? powerType, string groupName)
        => GetSpecificGroups(powerType).Contains(groupName, StringComparer.OrdinalIgnoreCase);

    public static int GetSpecificGroupOrder(string? powerType, string groupName, int fallback)
    {
        var groups = GetSpecificGroups(powerType);
        for (var index = 0; index < groups.Count; index++)
        {
            if (groups[index].Equals(groupName, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return 1000 + fallback;
    }
}
