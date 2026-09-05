using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public static class TechnologyDataEffectRules
{
    public static readonly string[] ResourceTargetOptions = ["Player"];
    public static readonly string[] ResourceRelativityDisplayOptions = ["Add", "Multiply", "Multiply base", "Set to"];

    public static bool NormalizeResourceEffect(XElement effect)
    {
        var changed = false;

        var relativity = GetAttribute(effect, "relativity");
        if (relativity == null)
        {
            effect.SetAttributeValue("relativity", "Absolute");
            changed = true;
        }
        else if (!relativity.Value.Equals("Absolute", StringComparison.OrdinalIgnoreCase) &&
                 !relativity.Value.Equals("Percent", StringComparison.OrdinalIgnoreCase) &&
                 !relativity.Value.Equals("BasePercent", StringComparison.OrdinalIgnoreCase) &&
                 !relativity.Value.Equals("Assign", StringComparison.OrdinalIgnoreCase) &&
                 !relativity.Value.Equals("Override", StringComparison.OrdinalIgnoreCase))
        {
            relativity.Value = "Absolute";
            changed = true;
        }

        if (GetAttribute(effect, "amount") == null)
        {
            effect.SetAttributeValue("amount", "0");
            changed = true;
        }

        return NormalizePlayerTarget(effect) || changed;
    }

    public static bool NormalizeMaxResourceEffect(XElement effect)
    {
        var changed = SetAttribute(effect, "relativity", "Absolute");
        if (GetAttribute(effect, "amount") == null)
        {
            effect.SetAttributeValue("amount", "0");
            changed = true;
        }

        return NormalizePlayerTarget(effect) || changed;
    }

    public static bool NormalizePopulationCapEffect(XElement effect)
        => NormalizeResourceEffect(effect);

    public static bool NormalizePlayerTargetEffect(XElement effect)
        => NormalizePlayerTarget(effect);

    public static bool NormalizeSetCivilizationEffect(XElement effect)
    {
        var changed = SetAttribute(effect, "amount", "1");
        changed = SetAttribute(effect, "relativity", "Assign") || changed;
        return NormalizePlayerTarget(effect) || changed;
    }

    public static bool NormalizeProtoActionAddEffect(XElement effect)
    {
        var changed = SetAttribute(effect, "amount", "1");
        changed = SetAttribute(effect, "relativity", "Assign") || changed;

        var addToTactics = GetAttribute(effect, "addToTactics");
        if (addToTactics != null && !addToTactics.Value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            addToTactics.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizePlayerTarget(XElement effect)
    {
        var changed = false;
        var targets = effect.Elements()
            .Where(element => element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var target = targets.FirstOrDefault();
        if (target == null)
        {
            target = new XElement("target");
            effect.Add(target);
            changed = true;
        }

        foreach (var duplicate in targets.Skip(1))
        {
            duplicate.Remove();
            changed = true;
        }

        var targetType = GetAttribute(target, "type");
        if (targetType == null || !targetType.Value.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
            target.HasAttributes && target.Attributes().Any(attribute => !attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase)) ||
            target.HasElements || !string.IsNullOrWhiteSpace(target.Value))
        {
            target.RemoveAttributes();
            target.RemoveNodes();
            target.SetAttributeValue("type", "Player");
            changed = true;
        }

        return changed;
    }

    private static bool SetAttribute(XElement effect, string name, string value)
    {
        var attribute = GetAttribute(effect, name);
        if (attribute != null && attribute.Value.Equals(value, StringComparison.OrdinalIgnoreCase)) return false;
        if (attribute == null) effect.SetAttributeValue(name, value);
        else attribute.Value = value;
        return true;
    }

    private static XAttribute? GetAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
}
