using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Rename-only readiness checks; suggestion catalogs may still display partial results.</summary>
public static class ReferenceCatalogSafety
{
    public static IReadOnlyList<string>? ProtoUnitNames(IEnumerable<string?> baseXmlParts, XElement? modRoot)
    {
        if (modRoot?.Name != "protomods") return null;
        var roots = ReadProtoRoots(baseXmlParts);
        if (roots == null) return null;
        roots.Add(modRoot);
        if (!HaveNamedUnits(roots)) return null;
        return roots.SelectMany(root => root.Descendants("unit"))
            .Select(unit => ((string)unit.Attribute("name")!).Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string>? UsedUnitTypeNames(IEnumerable<string?> baseXmlParts)
    {
        var roots = ReadProtoRoots(baseXmlParts);
        return roots?.SelectMany(root => UnitTypeCatalog.ExtractUsedDefinitionsFromProto(root))
            .Select(definition => definition.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HaveNamedUnits(IEnumerable<XElement> roots)
        => roots.All(root => root.DescendantsAndSelf().All(element => element.Name.Namespace == XNamespace.None) &&
            root.Descendants("unit").All(unit => !string.IsNullOrWhiteSpace((string?)unit.Attribute("name"))));

    private static List<XElement>? ReadProtoRoots(IEnumerable<string?> baseXmlParts)
    {
        var roots = new List<XElement>();
        try
        {
            foreach (var xml in baseXmlParts)
            {
                if (string.IsNullOrWhiteSpace(xml)) return null;
                var root = XDocument.Parse(xml).Root;
                if (root == null) return null;
                roots.Add(root);
            }
        }
        catch (System.Xml.XmlException) { return null; }
        if (roots.Count == 0 || !roots.Any(root => root.Descendants("unit").Any())) return null;
        return HaveNamedUnits(roots) ? roots : null;
    }
}
