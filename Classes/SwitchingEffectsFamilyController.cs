using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record SwitchingEffectCreation(XElement Container, IReadOnlyList<string> StringIds);

public static class SwitchingEffectsFamilyController
{
    public static readonly IReadOnlyList<string> TierTypes = ["TotalResources", "CombatXP", "Time"];
    public static readonly IReadOnlyList<string> ProgressTypes = ["Hide", "Default", "ShieldBlessing", "SpawnReward"];

    public static bool IsPower(XElement? entry)
        => entry?.Attribute("type")?.Value.Trim().Equals("SwitchingEffects",
            StringComparison.OrdinalIgnoreCase) == true;

    public static string GetTierType(XElement entry)
    {
        var value = PowerCatalog.FindField(entry, "tiertype")?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? "Time" : value;
    }

    public static string GetProgressType(XElement entry)
    {
        if (PowerCatalog.FindField(entry, "hidewheelview") != null) return "Hide";
        var value = PowerCatalog.FindField(entry, "progresstype")?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? "Default" : value;
    }

    public static string CreateStringId(string powerName, string kind, int index = 0)
    {
        var token = TechnologyStringIdPolicy.NormalizeToken(powerName);
        return $"STR_SWITCHING_EFFECT_{token}_{kind}" + (index == 0 ? "" : $"_{index}");
    }

    public static bool IsOwnedStringId(string powerName, string? id)
        => !string.IsNullOrWhiteSpace(id) && id.StartsWith(
            $"STR_SWITCHING_EFFECT_{TechnologyStringIdPolicy.NormalizeToken(powerName)}_",
            StringComparison.OrdinalIgnoreCase);

    public static void ApplyTierType(XElement entry, string tierType)
    {
        PowerCatalog.SetField(entry, "tiertype", tierType);
        var interval = PowerCatalog.FindField(entry, "interval");
        if (tierType.Equals("Time", StringComparison.OrdinalIgnoreCase))
        {
            if (interval == null) PowerCatalog.SetField(entry, "interval", "0");
        }
        else if (interval != null)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(interval);

        foreach (var effects in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)))
        {
            var resource = PowerCatalog.FindField(effects, "resourcetierreq");
            var combat = PowerCatalog.FindField(effects, "combatxptierreq");
            if (tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase))
            {
                if (combat != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(combat);
                resource ??= PowerCatalog.SetField(effects, "resourcetierreq", "0");
                resource.SetAttributeValue("type", resource.Attribute("type")?.Value ?? "Food");
            }
            else if (tierType.Equals("CombatXP", StringComparison.OrdinalIgnoreCase))
            {
                if (resource != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(resource);
                if (combat == null) PowerCatalog.SetField(effects, "combatxptierreq", "0");
            }
            else
            {
                if (resource != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(resource);
                if (combat != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(combat);
            }
            PowerCatalog.NormalizeNestedElementIndentation(effects);
        }
    }

    public static void ApplyProgressType(XElement entry, string progressType)
    {
        var progress = PowerCatalog.FindField(entry, "progresstype");
        var hidden = PowerCatalog.FindField(entry, "hidewheelview");
        if (progressType.Equals("Hide", StringComparison.OrdinalIgnoreCase))
        {
            if (progress != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(progress);
            PowerCatalog.SetField(entry, "hidewheelview", "");
            if (PowerCatalog.FindField(entry, "timerrolloverid") is { } timer)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(timer);
        }
        else
        {
            if (hidden != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(hidden);
            PowerCatalog.SetField(entry, "progresstype", progressType);
        }
    }

    public static SwitchingEffectCreation AddEffectContainer(XElement entry, string powerName,
        string tierType)
    {
        var index = entry.Elements().Count(element =>
            element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
        var container = PowerCatalog.AddRepeatedField(entry, "effects", "");
        var ids = new[]
        {
            CreateStringId(powerName, "NAME", index),
            CreateStringId(powerName, "LR", index),
            CreateStringId(powerName, "NOTIF", index)
        };
        container.Add(new XElement("displaynameid", ids[0]));
        container.Add(new XElement("icon", ""));
        container.Add(new XElement("rolloverid", ids[1]));
        container.Add(new XElement("notificationmessageid", ids[2]));
        container.Add(new XElement("notificationsound", ""));
        if (tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase))
            container.Add(new XElement("resourcetierreq", new XAttribute("type", "Food"), "0"));
        else if (tierType.Equals("CombatXP", StringComparison.OrdinalIgnoreCase))
            container.Add(new XElement("combatxptierreq", "0"));
        PowerEffectFamilyController.AddDefaultEffect(container);
        PowerCatalog.NormalizeNestedElementIndentation(container);
        return new SwitchingEffectCreation(container, ids);
    }

    public static XElement AddExtraTooltip(XElement entry, XElement effectContainer, string powerName)
    {
        var extras = PowerCatalog.FindField(effectContainer, "extratooltips") ??
                     PowerCatalog.SetField(effectContainer, "extratooltips", "");
        var index = entry.Descendants().Count(element =>
            element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase) &&
            element.Parent?.Name.LocalName.Equals("extratooltips", StringComparison.OrdinalIgnoreCase) == true);
        var field = new XElement("string", CreateStringId(powerName, "TOOLTIP", index));
        extras.Add(field);
        PowerCatalog.NormalizeNestedElementIndentation(extras);
        return field;
    }

    public static IEnumerable<string> EnumerateOwnedStringIds(XElement container, string ownerName)
        => container.DescendantsAndSelf().Where(element => !element.HasElements)
            .Select(element => element.Value.Trim())
            .Where(id => IsOwnedStringId(ownerName, id))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
