using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Formats XML for display without changing the source document stored by the editor.</summary>
public static class XmlPreviewFormatter
{
    public static string Beautify(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return xml;

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            if (document.Root == null)
                return xml;

            NormalizeElement(document.Root, 0);
            var formatted = document.ToString(SaveOptions.DisableFormatting);
            return document.Declaration == null ? formatted : document.Declaration +
                (formatted.StartsWith('\r') || formatted.StartsWith('\n') ? "" : Environment.NewLine) + formatted;
        }
        catch
        {
            // Preview formatting must never prevent a readable source payload from being shown.
            return xml;
        }
    }

    private static void NormalizeElement(XElement element, int depth)
    {
        if ((string?)element.Attribute(XNamespace.Xml + "space") == "preserve") return;
        var sawChild = false;
        foreach (var node in element.Nodes())
        {
            if (node is XElement) sawChild = true;
            else if (sawChild && node is XText text && !string.IsNullOrWhiteSpace(text.Value)) return;
        }
        var children = element.Elements().ToList();
        foreach (var child in children)
            NormalizeElement(child, depth + 1);

        if (children.Count == 0)
            return;

        foreach (var whitespace in element.Nodes().OfType<XText>().Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();

        foreach (var child in element.Elements().ToList())
        {
            // Mixed-content AnimFile names precede their child elements. Their
            // existing indentation is part of that text node after parsing.
            if (child.PreviousNode is XText prefix && !string.IsNullOrWhiteSpace(prefix.Value) &&
                prefix.Value.EndsWith("\n" + new string('\t', depth + 1), StringComparison.Ordinal)) continue;
            child.AddBeforeSelf(new XText(Environment.NewLine + new string('\t', depth + 1)));
        }

        element.Add(new XText(Environment.NewLine + new string('\t', depth)));
    }
}
