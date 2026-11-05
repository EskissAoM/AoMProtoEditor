using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Technology form order; effect/prerequisite payload order is not changed.</summary>
internal static class TechnologyXmlOrderPolicy
{
    internal static readonly string[] OptionalFields =
    ["researchlimit", "valuetext", "delay", "initialdelay", "techage", "combatxptier"];

    private static readonly IReadOnlyDictionary<string, int> Ranks = BuildRanks();

    private static IReadOnlyDictionary<string, int> BuildRanks()
    {
        string[] fields = ["displaynameid", "rollovertextid", "advancedrollovertextoverrideid", "icon",
            "status", "researchpoints", "devotioncost", .. OptionalFields, "cost", "techtype", "flag", "prereqs"];
        var ranks = fields.Select((tag, rank) => (tag, rank)).ToDictionary(p => p.tag, p => p.rank, StringComparer.OrdinalIgnoreCase);
        // Direct and wrapped effects are one ordered stream; never regroup them.
        ranks.Add("effect", fields.Length);
        ranks.Add("effects", fields.Length);
        return ranks;
    }

    internal static void NormalizeForEditor(XElement technology) => EditorXmlOrderPolicy.Normalize(technology, Ranks);

    internal static void InsertNewField(XElement technology, XElement field)
    {
        if (field.Name.NamespaceName.Length == 0 && Ranks.ContainsKey(field.Name.LocalName))
            EditorXmlOrderPolicy.InsertNewField(technology, field, Ranks);
        else
        {
            // Unknown optional properties retain the legacy, conservative placement.
            var effects = technology.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
            if (effects != null) effects.AddBeforeSelf(field);
            else technology.Add(field);
        }
    }
}
