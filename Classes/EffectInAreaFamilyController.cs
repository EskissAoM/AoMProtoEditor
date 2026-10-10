using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public static class EffectInAreaFamilyController
{
    public static bool IsPower(XElement? entry)
        => entry?.Attribute("type")?.Value.Trim().Equals("EffectInArea",
            StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsSupportedOnHitType(string? type)
        => string.IsNullOrWhiteSpace(type) || ProtoConstants.KnownOnHitEffectTypes.Contains(
            type.Trim(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetOnHitAttributeOrder(string? type)
    {
        type ??= "";
        var result = new List<string>();
        if (IsAny(type, "Stun", "Flee", "Exile", "DamageOverTime", "StatModify",
                "SelfModify", "SelfStealth", "Chaos", "Sleep")) result.Add("duration");
        if (IsAny(type, "Snare", "Lifesteal") ||
            type.StartsWith("ProgFreeze", StringComparison.OrdinalIgnoreCase)) result.Add("rate");
        if (type.Equals("Snare", StringComparison.OrdinalIgnoreCase)) result.Add("duration");
        if (type.Equals("Freeze", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["freezetype", "shadingtype", "duration"]);
        if (IsAny(type, "Attach", "Reincarnation") || ProtoConstants.IsMutateOnHitEffectType(type))
            result.Add("proto");
        if (type.Equals("Attach", StringComparison.OrdinalIgnoreCase)) result.Add("attachbone");
        if (IsAny(type, "Attach", "Reincarnation")) result.Add("duration");
        if (type.Equals("Spawn", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["proto", "amount", "terraintype", "duration"]);
        if (type.Equals("Infect", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["protoaction", "duration"]);
        if (type.Equals("Root", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["amount", "proto", "duration"]);
        if (type.Equals("Throw", StringComparison.OrdinalIgnoreCase)) result.Add("delay");
        if (type.Equals("Boost", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["radius", "duration"]);
        if (IsAny(type, "Pull", "Push")) result.Add("radius");
        if (type.Equals("AnimOverride", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["anim", "duration"]);
        if (type.Equals("KillReward", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["amount", "rewardtype", "duration", "additive"]);
        if (type.Equals("InstantKillablePercentChance", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["amount", "relativity", "byunittype", "duration"]);
        if (type.Equals("ShadingFade", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["shadingtype", "fadetime", "factor", "duration"]);
        if (type.Equals("Shading", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["shadingtype", "factor"]);
        if (type.Equals("ProgShading", StringComparison.OrdinalIgnoreCase)) result.Add("shadingtype");
        if (type.StartsWith("ProgFreeze", StringComparison.OrdinalIgnoreCase))
            result.AddRange(["duration", "freezeduration"]);
        return result;
    }

    public static void SeedOnHitDefaults(XElement effect, string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        if (type.Equals("Freeze", StringComparison.OrdinalIgnoreCase))
            effect.SetAttributeValue("freezetype", ProtoConstants.KnownOnHitEffectFreezeTypes.FirstOrDefault());
        if (type.Equals("InstantKillablePercentChance", StringComparison.OrdinalIgnoreCase))
            effect.SetAttributeValue("relativity", "Set");
    }

    private static bool IsAny(string type, params string[] values)
        => values.Contains(type, StringComparer.OrdinalIgnoreCase);
}
