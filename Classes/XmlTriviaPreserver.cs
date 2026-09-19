using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Restores the original child-node layout after editor-owned elements have been
/// rebuilt. Final element values and membership come from the updated tree, while
/// comments, processing instructions, structural whitespace, and the relative
/// placement of surviving elements come from the original tree.
/// </summary>
public static class XmlTriviaPreserver
{
    /// <summary>Removes a direct XML element without leaving its indentation as an empty line.</summary>
    public static void RemoveElementAndAdjacentWhitespace(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.PreviousNode is XText previous && string.IsNullOrWhiteSpace(previous.Value))
            previous.Remove();
        else if (element.PreviousNode == null && element.NextNode is XText next &&
                 string.IsNullOrWhiteSpace(next.Value))
            next.Remove();
        element.Remove();
    }

    /// <summary>
    /// Replaces an element's scalar text while retaining comments, processing
    /// instructions, and its existing whitespace layout.
    /// </summary>
    public static void SetText(XElement element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        var original = new XElement(element);
        element.Value = value ?? "";
        Preserve(original, element);
        if (string.IsNullOrEmpty(value) && !element.Nodes().Any())
            element.Add(new XText(""));
    }

    public static void Preserve(XElement original, XElement updated)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);

        var childMatches = MatchChildElements(original, updated);
        foreach (var match in childMatches)
            Preserve(match.Key, match.Value);

        RestoreDirectNodeLayout(original, updated, childMatches);
    }

    private static Dictionary<XElement, XElement> MatchChildElements(XElement original, XElement updated)
    {
        var matches = new Dictionary<XElement, XElement>();
        var usedUpdated = new HashSet<XElement>();
        var originalChildren = original.Elements().ToList();
        var updatedChildren = updated.Elements().ToList();

        foreach (var originalChild in originalChildren)
        {
            var candidates = updatedChildren
                .Where(candidate =>
                    !usedUpdated.Contains(candidate) &&
                    candidate.Name == originalChild.Name)
                .ToList();
            if (candidates.Count == 0)
                continue;

            var sameNameOriginalCount = originalChildren.Count(candidate => candidate.Name == originalChild.Name);
            var sameNameUpdatedCount = updatedChildren.Count(candidate => candidate.Name == originalChild.Name);
            XElement? match = null;
            if (sameNameOriginalCount == 1 && sameNameUpdatedCount == 1)
            {
                match = candidates[0];
            }
            else
            {
                var identity = GetElementIdentity(originalChild);
                if (!string.IsNullOrWhiteSpace(identity))
                {
                    match = candidates.FirstOrDefault(candidate =>
                        GetElementIdentity(candidate).Equals(identity, StringComparison.OrdinalIgnoreCase));
                }
                match ??= candidates[0];
            }

            usedUpdated.Add(match);
            matches[originalChild] = match;
        }

        return matches;
    }

    private static void RestoreDirectNodeLayout(
        XElement original,
        XElement updated,
        IReadOnlyDictionary<XElement, XElement> childMatches)
    {
        var updatedNodes = updated.Nodes().ToList();
        var matchedUpdatedElements = childMatches.Values.ToHashSet();
        var preserveWhitespace = original.HasElements || updated.HasElements;
        var availableContentNodes = updatedNodes
            .Where(node => node is not XElement && !IsStructuralTrivia(node, preserveWhitespace))
            .ToList();
        var contentNodeIndex = 0;
        var rebuilt = new List<XNode>();

        foreach (var originalNode in original.Nodes())
        {
            if (originalNode is XElement originalElement)
            {
                if (childMatches.TryGetValue(originalElement, out var updatedElement))
                    rebuilt.Add(updatedElement);
                continue;
            }

            if (IsStructuralTrivia(originalNode, preserveWhitespace))
            {
                rebuilt.Add(CloneNode(originalNode));
                continue;
            }

            if (contentNodeIndex < availableContentNodes.Count)
                rebuilt.Add(availableContentNodes[contentNodeIndex++]);
        }

        var additions = updatedNodes
            .OfType<XElement>()
            .Where(element => !matchedUpdatedElements.Contains(element))
            .Cast<XNode>()
            .Concat(availableContentNodes.Skip(contentNodeIndex))
            .ToList();

        var insertionIndex = rebuilt.Count;
        while (insertionIndex > 0 &&
               rebuilt[insertionIndex - 1] is XText trailingWhitespace &&
               string.IsNullOrWhiteSpace(trailingWhitespace.Value))
        {
            insertionIndex--;
        }
        rebuilt.InsertRange(insertionIndex, additions);

        updated.ReplaceNodes(rebuilt);
    }

    private static bool IsStructuralTrivia(XNode node, bool preserveWhitespace)
        => node is XComment or XProcessingInstruction ||
           node is XText text && preserveWhitespace && string.IsNullOrWhiteSpace(text.Value);

    private static XNode CloneNode(XNode node)
        => node switch
        {
            XComment comment => new XComment(comment.Value),
            XProcessingInstruction instruction =>
                new XProcessingInstruction(instruction.Target, instruction.Data),
            XText text => new XText(text.Value),
            _ => throw new InvalidOperationException($"Unsupported XML preservation node '{node.NodeType}'.")
        };

    private static string GetElementIdentity(XElement element)
    {
        foreach (var attributeName in new[]
                 {
                     "name", "resourcetype", "resourceType", "type", "id", "culture", "subtype"
                 })
        {
            var value = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return attributeName.ToUpperInvariant() + "\0" + value.Trim();
        }

        var childName = element.Elements().FirstOrDefault(child =>
            child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value;
        return string.IsNullOrWhiteSpace(childName)
            ? ""
            : "CHILDNAME\0" + childName.Trim();
    }
}
