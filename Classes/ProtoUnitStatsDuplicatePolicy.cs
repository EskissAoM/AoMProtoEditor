using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record ProtoUnitDuplicateIssue(string Key, string Label, string Value, int Count);

public static class ProtoUnitStatsDuplicatePolicy
{
    public static IReadOnlyList<ProtoUnitDuplicateIssue> FindDuplicates(
        string key,
        string label,
        IEnumerable<string> values)
        => values
            .Select(value => value?.Trim() ?? "")
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ProtoUnitDuplicateIssue(key, label, group.First(), group.Count()))
            .OrderBy(issue => issue.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<ProtoUnitDuplicateIssue> FindXmlDuplicates(XElement unit)
    {
        var issues = new List<ProtoUnitDuplicateIssue>();

        Add("unittypes", "Unit Types", unit.Elements("unittype"));
        Add("flags", "Flags", unit.Elements("flag"));
        Add("contain", "Contain", unit.Elements("contain"));
        Add("notcontain", "Not Contain", unit.Elements("notcontain"));
        Add("dynamicbuildlimit", "Dynamic Build Limit targets", unit.Element("dynamicbuildlimitunittypes")?.Elements("unittype"));
        Add("sharedbuildlimit", "Shared Build Limit targets", unit.Element("sharedbuildlimitunittypes")?.Elements("unittype"));
        Add("respawntypes", "Respawn Types", unit.Element("respawntraindata")?.Element("respawntypes")?.Elements("unittype"));
        Add("respawnexcludetypes", "Respawn Exclude Types", unit.Element("respawntraindata")?.Element("excludetypes")?.Elements("unittype"));
        Add("veterancyincludetypes", "Veterancy Include Types", unit.Element("veterancybonus")?.Element("includetypes")?.Elements("unittype"));
        Add("veterancyexcludetypes", "Veterancy Exclude Types", unit.Element("veterancybonus")?.Element("excludetypes")?.Elements("unittype"));
        Add("sharedselectionunittypes", "Shared Selection Unit Types", unit.Element("sharedselectionunittypes")?.Elements("unittype"));
        Add("rechargeincludetypes", "Recharge Include Types", unit.Element("rechargeincludetypes")?.Elements("unittype"));
        Add("rechargeexcludetypes", "Recharge Exclude Types", unit.Element("rechargeexcludetypes")?.Elements("unittype"));
        Add("auxrechargeincludetypes", "Aux Recharge Include Types", unit.Element("auxrechargeincludetypes")?.Elements("unittype"));
        Add("auxrechargeexcludetypes", "Aux Recharge Exclude Types", unit.Element("auxrechargeexcludetypes")?.Elements("unittype"));

        return issues;

        void Add(string key, string label, IEnumerable<XElement>? elements)
        {
            if (elements != null)
                issues.AddRange(FindDuplicates(key, label, elements.Select(element => element.Value)));
        }
    }
}
