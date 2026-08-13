using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record AssetReferenceMigrationResult(int ReferenceCount, IReadOnlyList<string> UnitNames);

public static class ProtoUnitAssetReferenceMigration
{
    public static AssetReferenceMigrationResult Replace(
        XElement root,
        string elementName,
        string oldValue,
        string newValue)
    {
        ArgumentNullException.ThrowIfNull(root);
        var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var unit in root.Elements().Where(element =>
                     element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)))
        {
            var matched = false;
            foreach (var element in unit.Descendants().Where(element =>
                         element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase) &&
                         PathsEqual(element.Value, oldValue)))
            {
                element.Value = newValue;
                count++;
                matched = true;
            }

            if (matched)
                units.Add(unit.Attribute("name")?.Value?.Trim() ?? unit.Element("name")?.Value?.Trim() ?? "(unnamed)");
        }

        return new AssetReferenceMigrationResult(
            count,
            units.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static AssetReferenceMigrationResult Inspect(XElement root, string elementName, string value)
    {
        var copy = new XElement(root);
        return Replace(copy, elementName, value, value);
    }

    internal static bool PathsEqual(string left, string right)
        => Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
        => (value ?? "").Trim().Replace('/', '\\');
}
