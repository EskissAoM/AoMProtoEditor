using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Replaces editor-owned named children while leaving wrapper metadata and trivia in place.</summary>
public static class ManagedXmlChildren
{
    public static void Replace(
        XElement owner,
        string childElementName,
        IEnumerable<XElement> replacements,
        Func<XElement, string?> identitySelector)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(childElementName);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(identitySelector);

        var remaining = replacements.Select(element => new XElement(element)).ToList();
        var existing = owner.Elements()
            .Where(element => element.Name.LocalName.Equals(childElementName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var current in existing)
        {
            var identity = identitySelector(current)?.Trim() ?? "";
            var index = remaining.FindIndex(candidate =>
                string.Equals(identitySelector(candidate)?.Trim(), identity, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                current.Remove();
                continue;
            }

            var replacement = remaining[index];
            remaining.RemoveAt(index);
            XmlTriviaPreserver.Preserve(current, replacement);
            current.ReplaceWith(replacement);
        }

        if (remaining.Count == 0)
            return;
        var trailingWhitespace = owner.Nodes().LastOrDefault() is XText text && string.IsNullOrWhiteSpace(text.Value)
            ? text
            : null;
        if (trailingWhitespace != null)
            trailingWhitespace.AddBeforeSelf(remaining);
        else
            owner.Add(remaining);
    }
}
