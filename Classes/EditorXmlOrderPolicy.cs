using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Shared, formatting-free mechanics for structured editor ordering. Entity
/// registries supply ranks; callers decide whether a draft is structured or raw.
/// Unknown content separates runs and repeated entries retain their sequence.
/// </summary>
internal static class EditorXmlOrderPolicy
{
    internal static void Normalize(XElement entry, IReadOnlyDictionary<string, int> ranks)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(ranks);
        if (HasProtectedContent(entry)) return;

        var output = new List<XNode>();
        var run = new List<(int Rank, List<XNode> Nodes)>();
        var leading = new List<XNode>();
        void Flush()
        {
            foreach (var block in run.OrderBy(block => block.Rank)) output.AddRange(block.Nodes);
            run.Clear();
        }
        void Barrier(XNode? node = null)
        {
            Flush(); output.AddRange(leading); leading.Clear();
            if (node != null) output.Add(node);
        }
        foreach (var node in entry.Nodes())
        {
            if (node is XText || node is XComment) { leading.Add(node); continue; }
            if (node is XElement field && field.Name.NamespaceName.Length == 0 &&
                ranks.TryGetValue(field.Name.LocalName, out var rank))
            {
                // A separated comment may describe a section rather than a field.
                if (leading.Any(item => item is XComment) && leading.OfType<XText>().Any(HasBlankLine)) Barrier();
                var block = new List<XNode>(leading) { field };
                leading.Clear(); run.Add((rank, block));
            }
            else Barrier(node);
        }
        Barrier();
        if (entry.Nodes().SequenceEqual(output)) return;
        entry.RemoveNodes();
        entry.Add(output);
    }

    internal static void InsertNewField(XElement entry, XElement field, IReadOnlyDictionary<string, int> ranks)
    {
        var nodes = entry.Nodes().ToList();
        var index = FindNewFieldInsertionIndex(nodes, field, ranks);
        if (index >= 0 && index < nodes.Count) nodes[index].AddBeforeSelf(field);
        else entry.Add(field);
    }

    internal static int FindNewFieldInsertionIndex(
        IReadOnlyList<XNode> nodes, XElement field, IReadOnlyDictionary<string, int> ranks)
    {
        if (field.Name.NamespaceName.Length != 0 || !ranks.TryGetValue(field.Name.LocalName, out var rank)) return -1;
        var lastKnown = -1;
        var sameGroup = -1;
        var nextGroup = -1;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not XElement sibling || sibling.Name.NamespaceName.Length != 0 ||
                !ranks.TryGetValue(sibling.Name.LocalName, out var other)) continue;
            lastKnown = i;
            if (other == rank) sameGroup = i;
            else if (other > rank && nextGroup < 0) nextGroup = i;
        }
        if (sameGroup >= 0) return sameGroup + 1;
        if (nextGroup < 0) return lastKnown < 0 ? -1 : lastKnown + 1;
        while (nextGroup > 0 && (nodes[nextGroup - 1] is XComment ||
            nodes[nextGroup - 1] is XText text && string.IsNullOrWhiteSpace(text.Value))) nextGroup--;
        return nextGroup;
    }

    internal static bool HasProtectedContent(XElement element)
        => PreservesSpace(element) || element.Nodes().Any(node => node is XCData ||
            node is XText text && !string.IsNullOrWhiteSpace(text.Value));

    internal static bool PreservesSpace(XElement element)
    {
        foreach (var ancestor in element.AncestorsAndSelf())
        {
            var value = (string?)ancestor.Attribute(XNamespace.Xml + "space");
            if (value == "preserve") return true;
            if (value == "default") return false;
        }
        return false;
    }

    private static bool HasBlankLine(XText text)
        => text.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Count(c => c == '\n') > 1;
}
