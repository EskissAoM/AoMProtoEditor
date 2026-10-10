using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Applies the editor-owned outer ProtoUnit section order and repairs indentation
/// after structured setters have replaced or appended elements.
/// </summary>
public static class ProtoUnitXmlOrderPolicy
{
    public static void NormalizeForEditor(XElement unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!unit.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a ProtoUnit <unit> element.", nameof(unit));

        var orderedElements = unit.Elements()
            .Select((element, index) => (Element: element, Index: index))
            .OrderBy(item => GetSection(item.Element.Name.LocalName))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();

        ReplaceElementSlots(unit, orderedElements);
        NormalizeStructuredIndentation(unit);
    }

    private static int GetSection(string tag)
    {
        if (tag.Equals("command", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("optionalcommand", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("transformcommand", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (tag.Equals("tactics", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (tag.Equals("protoaction", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 0;
    }

    private static void ReplaceElementSlots(XElement parent, IReadOnlyList<XElement> orderedElements)
    {
        var nextElement = 0;
        var content = parent.Nodes()
            .Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
            .Select(node => node is XElement ? orderedElements[nextElement++] : node)
            .ToList();

        parent.RemoveNodes();
        foreach (var node in content)
            parent.Add(node);
    }

    private static void NormalizeStructuredIndentation(XElement parent)
    {
        foreach (var child in parent.Elements().ToList())
            NormalizeStructuredIndentation(child);

        if (!parent.HasElements || parent.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            return;

        var content = parent.Nodes()
            .Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
            .ToList();
        if (content.Count == 0)
            return;

        var ownIndent = FindElementIndentation(parent);
        var childIndent = ownIndent + "  ";
        parent.RemoveNodes();
        foreach (var node in content)
        {
            parent.Add(new XText(Environment.NewLine + childIndent));
            parent.Add(node);
        }
        parent.Add(new XText(Environment.NewLine + ownIndent));
    }

    private static string FindElementIndentation(XElement element)
    {
        if (element.PreviousNode is XText precedingWhitespace && string.IsNullOrWhiteSpace(precedingWhitespace.Value))
        {
            var lastNewLine = Math.Max(
                precedingWhitespace.Value.LastIndexOf('\n'),
                precedingWhitespace.Value.LastIndexOf('\r'));
            return precedingWhitespace.Value[(lastNewLine + 1)..];
        }

        return element.Parent == null ? "" : FindElementIndentation(element.Parent) + "  ";
    }
}
