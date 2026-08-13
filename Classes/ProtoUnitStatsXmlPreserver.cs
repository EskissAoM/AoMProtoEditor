using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Restores XML content that the Stats UI does not own after its managed fields
/// have been rebuilt. Optional elements removed by the user are not recreated.
/// </summary>
public static class ProtoUnitStatsXmlPreserver
{
    private sealed record ElementSchema(string[] Attributes, string[] Children);

    private static readonly Dictionary<string, ElementSchema> Schemas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cost"] = Schema(["resourcetype"]),
        ["armor"] = Schema(["type", "value"]),
        ["directionalarmor"] = Schema(["angle", "value"]),
        ["creationfadetime"] = Schema(["initalpha"]),
        ["heightbob"] = Schema(["period", "magnitude"]),
        ["initialshading"] = Schema(["type", "factor"]),
        ["damageshading"] = Schema(["type", "threshold", "rate", "time"]),
        ["killreward"] = Schema(["resourcetype"]),
        ["ResourceReturn"] = Schema(["resourceType"]),
        ["ResourceReturnRate"] = Schema(["resourceType"]),
        ["dependentunit"] = Schema(["x", "z", "attachbone"]),
        ["veterancyranks"] = Schema([], ["rank"]),
        ["veterancybonus"] = Schema([], ["rank", "includetypes", "excludetypes"]),
        ["rank"] = Schema(["id"], ["numkills", "numattacks", "totaldamage", "damageandresourceseaten", "veterancymodify"]),
        ["veterancymodify"] = Schema(["modifytype", "damagetype"]),
        ["OnDamageModifiers"] = Schema([], ["OnDamageModify"]),
        ["OnDamageModify"] = Schema(["modifyType", "damageType"]),
        ["spawn"] = Schema(["type", "count", "lifespan", "chance", "delay", "skipPlacementCheck", "controlGroup", "waterProtoUnit", "setowner", "shadingtype"]),
        ["respawntraindata"] = Schema([], ["targettype", "trainproto", "respawntime", "respawnvfx", "respawntypes", "excludetypes", "respawnrates", "respawnlimit"]),
        ["respawnrates"] = Schema([], ["food", "wood", "gold", "favor"]),
        ["respawntypes"] = Schema([], ["unittype"]),
        ["includetypes"] = Schema([], ["unittype"]),
        ["excludetypes"] = Schema([], ["unittype"]),
        ["unittype"] = Schema([]),
        ["flag"] = Schema([]),
        ["unitregen"] = Schema(["idletimeout", "damagetimeout", "combatmultiplier", "ratelimit"]),
        ["unitshieldregen"] = Schema(["idletimeout", "damagetimeout", "combatmultiplier", "ratelimit"]),
        ["carrycapacity"] = Schema(["resourcetype", "dropoffmultiplier"]),
        ["initialresource"] = Schema(["resourcetype"]),
        ["resourceconversion"] = Schema(["fromresourcetype", "toresourcetype"]),
        ["contain"] = Schema([]),
        ["notcontain"] = Schema([]),
        ["dynamicbuildlimitunittypes"] = Schema([], ["unittype"]),
        ["sharedbuildlimitunittypes"] = Schema([], ["unittype"]),
        ["sharedselectionunittypes"] = Schema([], ["unittype"]),
        ["decay"] = Schema(["delay", "duration"]),
        ["rechargetime"] = Schema(["init"]),
        ["recharge"] = Schema(["type", "init"]),
        ["auxrechargetime"] = Schema(["init"]),
        ["auxrecharge"] = Schema(["type", "init"]),
        ["rechargeincludetypes"] = Schema([], ["unittype"]),
        ["rechargeexcludetypes"] = Schema([], ["unittype"]),
        ["auxrechargeincludetypes"] = Schema([], ["unittype"]),
        ["auxrechargeexcludetypes"] = Schema([], ["unittype"]),
        ["minimapcolor"] = Schema(["red", "green", "blue"]),
        ["replacement"] = Schema(["type", "lifespan"]),
        ["icon"] = Schema(["culture"]),
        ["animfile"] = Schema(["culture"]),
    };

    public static void PreserveUnmanagedContent(XElement originalUnit, XElement updatedUnit)
    {
        ArgumentNullException.ThrowIfNull(originalUnit);
        ArgumentNullException.ThrowIfNull(updatedUnit);

        foreach (var schemaEntry in Schemas)
        {
            var originalElements = ElementsNamed(originalUnit, schemaEntry.Key).ToList();
            var updatedElements = ElementsNamed(updatedUnit, schemaEntry.Key).ToList();
            if (originalElements.Count == 0 || updatedElements.Count == 0)
                continue;

            var usedOriginals = new HashSet<XElement>();
            for (var index = 0; index < updatedElements.Count; index++)
            {
                var updated = updatedElements[index];
                var identity = GetIdentity(updated);
                var original = !string.IsNullOrWhiteSpace(identity)
                    ? originalElements.FirstOrDefault(candidate =>
                        !usedOriginals.Contains(candidate) &&
                        GetIdentity(candidate).Equals(identity, StringComparison.OrdinalIgnoreCase))
                    : originalElements.Skip(index).FirstOrDefault();

                if (original == null)
                    continue;

                usedOriginals.Add(original);
                MergeElement(original, updated);
            }
        }

        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "cost", "resourcetype", ProtoConstants.KnownResourceTypes);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "armor", "type", ProtoConstants.KnownArmorTypes);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "killreward", "resourcetype", ProtoConstants.KnownResourceTypes, requireUpdatedEntry: true);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "ResourceReturn", "resourceType", ProtoConstants.KnownResourceTypes, requireUpdatedEntry: true);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "ResourceReturnRate", "resourceType", ProtoConstants.KnownResourceTypes, requireUpdatedEntry: true);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "carrycapacity", "resourcetype", ProtoConstants.KnownResourceTypes, requireUpdatedEntry: true);
        PreserveAdditionalMapEntries(originalUnit, updatedUnit, "initialresource", "resourcetype", ProtoConstants.KnownResourceTypes, requireUpdatedEntry: true);
    }

    private static void MergeElement(XElement original, XElement updated)
    {
        if (!Schemas.TryGetValue(updated.Name.LocalName, out var schema))
            return;

        var managedAttributes = new HashSet<string>(schema.Attributes, StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in original.Attributes())
        {
            if (!managedAttributes.Contains(attribute.Name.LocalName) &&
                updated.Attributes().All(existing => !existing.Name.LocalName.Equals(attribute.Name.LocalName, StringComparison.OrdinalIgnoreCase)))
            {
                updated.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }

        var managedChildren = new HashSet<string>(schema.Children, StringComparer.OrdinalIgnoreCase);
        foreach (var child in original.Elements())
        {
            if (!managedChildren.Contains(child.Name.LocalName))
                updated.Add(new XElement(child));
        }

        foreach (var childName in managedChildren)
        {
            var originalChildren = ElementsNamed(original, childName).ToList();
            var updatedChildren = ElementsNamed(updated, childName).ToList();
            for (var index = 0; index < Math.Min(originalChildren.Count, updatedChildren.Count); index++)
                MergeElement(originalChildren[index], updatedChildren[index]);
        }
    }

    private static void PreserveAdditionalMapEntries(
        XElement originalUnit,
        XElement updatedUnit,
        string elementName,
        string keyAttribute,
        IEnumerable<string> managedKeys,
        bool requireUpdatedEntry = false)
    {
        var managed = new HashSet<string>(managedKeys, StringComparer.OrdinalIgnoreCase);
        var originals = ElementsNamed(originalUnit, elementName).ToList();
        var updated = ElementsNamed(updatedUnit, elementName).ToList();
        if (requireUpdatedEntry && updated.Count == 0)
            return;
        var updatedKeys = updated
            .Select(element => AttributeValue(element, keyAttribute))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in originals.GroupBy(element => AttributeValue(element, keyAttribute), StringComparer.OrdinalIgnoreCase))
        {
            var values = group.ToList();
            if (!managed.Contains(group.Key))
            {
                foreach (var value in values)
                    updatedUnit.Add(new XElement(value));
                continue;
            }

            // A missing managed key means the user deliberately removed it (for
            // example, setting a cost to zero). If it remains, preserve only
            // additional ambiguous occurrences beyond the UI-owned first entry.
            if (!updatedKeys.Contains(group.Key))
                continue;

            foreach (var duplicate in values.Skip(1))
                updatedUnit.Add(new XElement(duplicate));
        }
    }

    private static string GetIdentity(XElement element)
    {
        var name = element.Name.LocalName;
        if (name.Equals("cost", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("carrycapacity", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("initialresource", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("killreward", StringComparison.OrdinalIgnoreCase))
            return AttributeValue(element, "resourcetype");
        if (name.Equals("armor", StringComparison.OrdinalIgnoreCase))
            return AttributeValue(element, "type");
        if (name.Equals("ResourceReturn", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ResourceReturnRate", StringComparison.OrdinalIgnoreCase))
            return AttributeValue(element, "resourceType");
        if (name.Equals("resourceconversion", StringComparison.OrdinalIgnoreCase))
            return AttributeValue(element, "fromresourcetype") + "\0" + AttributeValue(element, "toresourcetype");
        if (name.Equals("icon", StringComparison.OrdinalIgnoreCase) || name.Equals("animfile", StringComparison.OrdinalIgnoreCase))
            return AttributeValue(element, "culture");
        if (name.Equals("unittype", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("flag", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("contain", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("notcontain", StringComparison.OrdinalIgnoreCase))
            return element.Value?.Trim() ?? "";
        return "";
    }

    private static IEnumerable<XElement> ElementsNamed(XElement parent, string name)
        => parent.Elements().Where(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string AttributeValue(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

    private static ElementSchema Schema(string[] attributes, string[]? children = null)
        => new(attributes, children ?? []);
}
