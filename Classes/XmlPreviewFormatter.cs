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
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            // Preview formatting must never prevent a readable source payload from being shown.
            return xml;
        }
    }

    private static void NormalizeElement(XElement element, int depth)
    {
        var children = element.Elements().ToList();
        foreach (var child in children)
            NormalizeElement(child, depth + 1);

        if (children.Count == 0)
            return;

        foreach (var whitespace in element.Nodes().OfType<XText>().Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();

        foreach (var child in element.Elements().ToList())
            child.AddBeforeSelf(new XText(Environment.NewLine + new string('\t', depth + 1)));

        element.Add(new XText(Environment.NewLine + new string('\t', depth)));
    }
}
