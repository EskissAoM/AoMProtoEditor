using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public static class TechnologyOnHitEffectRules
{
    public static readonly string[] StatModifyEffectTypes = ["StatModify", "Boost", "SelfModify"];

    public static readonly HashSet<string> ModifyEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SelfModify", "StatModify", "Boost"
    };

    public static readonly HashSet<string> AutomaticProtoEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reincarnation", "Mutate", "MutateNature", "Attach", "Spawn"
    };

    public static readonly HashSet<string> ProtoEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reincarnation", "Mutate", "MutateNature", "Attach", "Spawn", "Root"
    };

    public static readonly HashSet<string> DamageTypeButtonEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "StatModify", "DamageOverTime", "Boost", "SelfModify"
    };

    public static readonly HashSet<string> DurationExcludedEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lifesteal", "Reincarnation", "Throw", "Mutate", "MutateNature"
    };

    public static readonly HashSet<string> ProgressiveFreezeEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProgFreeze", "ProgFreezeSpeed", "ProgFreezeROF"
    };

    public static bool UsesModify(string? effectType)
        => ModifyEffectTypes.Contains(effectType?.Trim() ?? "");

    public static bool UsesFreezeType(string? effectType)
        => string.Equals(effectType?.Trim(), "Freeze", StringComparison.OrdinalIgnoreCase) ||
           ProgressiveFreezeEffectTypes.Contains(effectType?.Trim() ?? "");

    public static bool UsesProgressiveFreezeDuration(string? effectType)
        => ProgressiveFreezeEffectTypes.Contains(effectType?.Trim() ?? "");

    public static bool OffersDuration(string? effectType)
        => !DurationExcludedEffectTypes.Contains(effectType?.Trim() ?? "");

    public static bool OffersDamageType(string? effectType)
        => DamageTypeButtonEffectTypes.Contains(effectType?.Trim() ?? "");

    public static bool OffersProto(string? effectType)
        => ProtoEffectTypes.Contains(effectType?.Trim() ?? "");

    public static string GetProtoFieldLabel(string? effectType)
    {
        var normalized = effectType?.Trim() ?? "";
        if (normalized.Equals("Attach", StringComparison.OrdinalIgnoreCase)) return "Attach";
        if (normalized.Equals("Spawn", StringComparison.OrdinalIgnoreCase)) return "Spawn";
        if (normalized.Equals("Reincarnation", StringComparison.OrdinalIgnoreCase) ||
            ProtoConstants.IsMutateOnHitEffectType(normalized)) return "To";
        return "Protounit";
    }

    public static bool RequiresDamageType(string? effectType, string? modifyType)
        => string.Equals(effectType?.Trim(), "DamageOverTime", StringComparison.OrdinalIgnoreCase) ||
           ProtoConstants.GetModifyTypeValue(modifyType ?? "") is "DamageSpecific" or "ArmorSpecific";

    public static bool RequiresSpecificDamageType(string? modifyType)
        => ProtoConstants.GetModifyTypeValue(modifyType ?? "") is "DamageSpecific" or "ArmorSpecific";

    public static bool UsesEditableAmount(string? subtype)
        => subtype?.Trim() is { } value &&
           (value.Equals("OnHitEffectDuration", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("OnHitEffectProbability", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("OnHitEffectRate", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("OnHitEffectStatModify", StringComparison.OrdinalIgnoreCase));

    public static bool NormalizeAttributeSubtype(XElement effect, string? subtype)
    {
        var normalizedSubtype = subtype?.Trim() ?? "";
        var changed = false;

        if (normalizedSubtype.Equals("OnHitEffectActive", StringComparison.OrdinalIgnoreCase))
        {
            var amount = GetAttribute(effect, "amount")?.Value.Trim();
            changed |= SetAttribute(effect, "amount", amount == "0" ? "0" : "1");
            changed |= SetAttribute(effect, "relativity", "Assign");
        }
        else if (normalizedSubtype.Equals("OnHitEffectAttachBone", StringComparison.OrdinalIgnoreCase))
        {
            changed |= SetAttribute(effect, "amount", "1");
            changed |= SetAttribute(effect, "relativity", "Assign");
        }
        else if (UsesEditableAmount(normalizedSubtype))
        {
            if (GetAttribute(effect, "amount") == null)
                changed |= SetAttribute(effect, "amount", "0");
            if (GetAttribute(effect, "relativity") == null)
                changed |= SetAttribute(effect, "relativity", "BasePercent");
        }

        if (normalizedSubtype.Equals("OnHitEffectRate", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetAttribute(effect, "effecttype")?.Value.Trim(), "DamageOverTime", StringComparison.OrdinalIgnoreCase) &&
            GetAttribute(effect, "dmgtype") == null)
        {
            changed |= SetAttribute(effect, "dmgtype", "All");
        }

        return changed;
    }

    public static bool Normalize(XElement effect)
    {
        var changed = false;
        if (GetAttribute(effect, "amount") == null)
            changed |= SetAttribute(effect, "amount", "0");
        changed |= SetAttribute(effect, "relativity", "Assign");

        var effectType = GetAttribute(effect, "effecttype")?.Value.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(effectType))
        {
            if (!UsesModify(effectType))
                changed |= RemoveAttribute(effect, "modifytype");

            if (!UsesFreezeType(effectType))
                changed |= RemoveAttribute(effect, "freezetype");

            if (!UsesProgressiveFreezeDuration(effectType))
                changed |= RemoveAttribute(effect, "progFreezeDuration");

            if (effectType.Equals("Freeze", StringComparison.OrdinalIgnoreCase) && GetAttribute(effect, "freezetype") == null)
                changed |= SetAttribute(effect, "freezetype", "default");

            if (UsesProgressiveFreezeDuration(effectType) && GetAttribute(effect, "progFreezeDuration") == null)
                changed |= SetAttribute(effect, "progFreezeDuration", "1");
        }

        return changed;
    }

    private static XAttribute? GetAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool SetAttribute(XElement element, string name, string value)
    {
        var attribute = GetAttribute(element, name);
        if (attribute != null)
        {
            if (attribute.Value.Equals(value, StringComparison.Ordinal)) return false;
            attribute.Value = value;
            return true;
        }

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool RemoveAttribute(XElement element, string name)
    {
        var attribute = GetAttribute(element, name);
        if (attribute == null) return false;
        attribute.Remove();
        return true;
    }
}
