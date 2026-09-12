using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record NamedXmlExternalDocumentMergeResult(
    XDocument Document,
    IReadOnlyList<string> LocallyChangedNames,
    IReadOnlyList<string> ExternallyChangedNames,
    IReadOnlyList<string> ReplacedEditorChanges);

/// <summary>
/// Three-way merge for mod XML files whose editable records are named direct
/// children of the root. The external document becomes the new base; local
/// records are retained only when that same record was not changed externally.
/// </summary>
public static class NamedXmlExternalDocumentMergePolicy
{
    public static NamedXmlExternalDocumentMergeResult Merge(
        XDocument loadedDocument,
        XDocument editorDocument,
        XDocument externalDocument,
        string elementName,
        Func<XElement, string?> nameSelector)
    {
        ArgumentNullException.ThrowIfNull(loadedDocument);
        ArgumentNullException.ThrowIfNull(editorDocument);
        ArgumentNullException.ThrowIfNull(externalDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);
        ArgumentNullException.ThrowIfNull(nameSelector);

        var loaded = GetRecords(loadedDocument, elementName, nameSelector);
        var editor = GetRecords(editorDocument, elementName, nameSelector);
        var external = GetRecords(externalDocument, elementName, nameSelector);
        var allNames = loaded.Keys.Concat(editor.Keys).Concat(external.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var localChanges = allNames.Where(name => HasChanged(name, loaded, editor))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalChanges = allNames.Where(name => HasChanged(name, loaded, external))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = localChanges.Where(externalChanges.Contains)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new XDocument(externalDocument);
        var mergedRecords = GetRecords(merged, elementName, nameSelector);
        foreach (var name in localChanges.Where(name => !externalChanges.Contains(name)))
        {
            if (editor.TryGetValue(name, out var editorRecord))
            {
                if (mergedRecords.TryGetValue(name, out var externalRecord))
                    externalRecord.ReplaceWith(new XElement(editorRecord));
                else
                    merged.Root!.Add(new XElement(editorRecord));
            }
            else if (mergedRecords.TryGetValue(name, out var deletedRecord))
            {
                deletedRecord.Remove();
            }
        }

        return new NamedXmlExternalDocumentMergeResult(
            merged,
            localChanges.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
            externalChanges.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
            conflicts);
    }

    private static bool HasChanged(
        string name,
        IReadOnlyDictionary<string, XElement> baseline,
        IReadOnlyDictionary<string, XElement> current)
    {
        var hadBaseline = baseline.TryGetValue(name, out var baselineElement);
        var hasCurrent = current.TryGetValue(name, out var currentElement);
        return hadBaseline != hasCurrent ||
               hasCurrent && !string.Equals(Serialize(baselineElement!), Serialize(currentElement!), StringComparison.Ordinal);
    }

    private static Dictionary<string, XElement> GetRecords(
        XDocument document,
        string elementName,
        Func<XElement, string?> nameSelector)
        => (document.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)) ?? [])
            .Select(element => (Name: nameSelector(element)?.Trim() ?? "", Element: element))
            .Where(record => record.Name.Length > 0)
            .ToDictionary(record => record.Name, record => record.Element, StringComparer.OrdinalIgnoreCase);

    private static string Serialize(XElement element)
    {
        var copy = new XElement(element);
        foreach (var whitespace in copy.DescendantNodes().OfType<XText>()
                     .Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }
}
