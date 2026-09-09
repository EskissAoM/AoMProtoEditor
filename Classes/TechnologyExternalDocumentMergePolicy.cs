using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record TechnologyExternalDocumentMergeResult(
    XDocument Document,
    IReadOnlyList<string> ExternallyChangedTechnologies,
    IReadOnlyList<string> ReplacedEditorChanges);

/// <summary>
/// Three-way merge used when techtree_mods.xml changes after it was loaded.
/// The disk version is the new base. Unsaved editor technologies are overlaid
/// only when that same technology did not also change on disk.
/// </summary>
public static class TechnologyExternalDocumentMergePolicy
{
    public static TechnologyExternalDocumentMergeResult Merge(
        IReadOnlyDictionary<string, string> loadedTechnologyXml,
        XDocument editorDocument,
        XDocument externalDocument)
    {
        ArgumentNullException.ThrowIfNull(loadedTechnologyXml);
        ArgumentNullException.ThrowIfNull(editorDocument);
        ArgumentNullException.ThrowIfNull(externalDocument);

        var editor = GetTechnologies(editorDocument);
        var external = GetTechnologies(externalDocument);
        var allNames = loadedTechnologyXml.Keys.Concat(editor.Keys).Concat(external.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var localChanges = allNames.Where(name => HasChanged(name, loadedTechnologyXml, editor))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalChanges = allNames.Where(name => HasChanged(name, loadedTechnologyXml, external))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = localChanges.Where(externalChanges.Contains)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new XDocument(externalDocument);
        var mergedTechnologies = GetTechnologies(merged);
        foreach (var name in localChanges.Where(name => !externalChanges.Contains(name)))
        {
            if (editor.TryGetValue(name, out var editorTechnology))
            {
                if (mergedTechnologies.TryGetValue(name, out var externalTechnology))
                    externalTechnology.ReplaceWith(new XElement(editorTechnology));
                else
                    merged.Root!.Add(new XElement(editorTechnology));
            }
            else if (mergedTechnologies.TryGetValue(name, out var deletedTechnology))
            {
                deletedTechnology.Remove();
            }
        }

        return new TechnologyExternalDocumentMergeResult(
            merged,
            externalChanges.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
            conflicts);
    }

    public static Dictionary<string, string> CaptureTechnologyXml(XDocument document)
        => GetTechnologies(document).ToDictionary(
            pair => pair.Key,
            pair => Serialize(pair.Value),
            StringComparer.OrdinalIgnoreCase);

    private static bool HasChanged(
        string name,
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, XElement> current)
    {
        var hadBaseline = baseline.TryGetValue(name, out var baselineXml);
        var hasCurrent = current.TryGetValue(name, out var currentTechnology);
        return hadBaseline != hasCurrent ||
               hasCurrent && !string.Equals(baselineXml, Serialize(currentTechnology!), StringComparison.Ordinal);
    }

    private static Dictionary<string, XElement> GetTechnologies(XDocument document)
        => (document.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? [])
            .Where(element => !string.IsNullOrWhiteSpace(GetName(element)))
            .ToDictionary(element => GetName(element), element => element, StringComparer.OrdinalIgnoreCase);

    private static string GetName(XElement technology)
        => technology.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    private static string Serialize(XElement technology)
    {
        var copy = new XElement(technology);
        foreach (var whitespace in copy.DescendantNodes().OfType<XText>()
                     .Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }
}
