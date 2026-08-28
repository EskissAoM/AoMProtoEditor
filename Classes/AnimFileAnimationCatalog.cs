using System.Xml;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal sealed record AnimFileAnimationNames(IReadOnlyList<string> UnitAnimations)
{
    public static AnimFileAnimationNames Empty { get; } = new([]);
}

/// <summary>
/// Extracts the action-facing names from animation XML. Unit animations exclude
/// animation nodes owned by attachments. AnimOverride deliberately uses the
/// editor's shared global animation catalog instead.
/// </summary>
internal static class AnimFileAnimationCatalog
{
    public static AnimFileAnimationNames ParseAnimFileXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return AnimFileAnimationNames.Empty;

        var document = XDocument.Parse(xml, LoadOptions.None);
        var unitAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var animation in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("anim", StringComparison.OrdinalIgnoreCase)))
        {
            // The animation name is the direct text node immediately inside <anim>;
            // XElement.Value would incorrectly append nested asset paths and tags.
            var name = animation.Nodes()
                .OfType<XText>()
                .Select(text => text.Value.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var belongsToAttachment = animation.Ancestors().Any(ancestor =>
                ancestor.Name.LocalName.Equals("attachment", StringComparison.OrdinalIgnoreCase));
            if (!belongsToAttachment)
                unitAnimations.Add(name);
        }

        return Create(unitAnimations);
    }

    public static IReadOnlyDictionary<string, AnimFileAnimationNames> ParseSimDataXml(
        string xml,
        IEnumerable<string> requestedAnimFiles)
    {
        var requested = requestedAnimFiles
            .Select(NormalizeAnimFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0 || string.IsNullOrWhiteSpace(xml))
            return new Dictionary<string, AnimFileAnimationNames>(StringComparer.OrdinalIgnoreCase);

        var namesByPath = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using var textReader = new StringReader(xml);
        using var reader = XmlReader.Create(textReader, new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit
        });

        string currentPath = "";
        var insideAnimations = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals("animxml", StringComparison.OrdinalIgnoreCase))
            {
                var path = NormalizeAnimFilePath(reader.GetAttribute("file"));
                currentPath = requested.Contains(path) ? path : "";
                insideAnimations = false;
                if (!string.IsNullOrWhiteSpace(currentPath))
                    namesByPath.TryAdd(currentPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentPath))
                continue;

            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals("animations", StringComparison.OrdinalIgnoreCase))
            {
                insideAnimations = true;
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement &&
                reader.LocalName.Equals("animations", StringComparison.OrdinalIgnoreCase))
            {
                insideAnimations = false;
                continue;
            }

            if (insideAnimations && reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.ReadElementContentAsString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    namesByPath[currentPath].Add(value);
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement &&
                reader.LocalName.Equals("animxml", StringComparison.OrdinalIgnoreCase))
            {
                currentPath = "";
                insideAnimations = false;
            }
        }

        return namesByPath.ToDictionary(
            pair => pair.Key,
            pair => Create(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public static AnimFileAnimationNames Merge(IEnumerable<AnimFileAnimationNames> catalogs)
    {
        var catalogList = catalogs.ToList();
        var unit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogList)
            unit.UnionWith(catalog.UnitAnimations);
        return Create(unit);
    }

    public static string NormalizeAnimFilePath(string? path)
    {
        var normalized = path?.Trim().Replace('/', '\\') ?? "";
        if (normalized.EndsWith(".xml.XMB", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^".XMB".Length];
        return normalized;
    }

    private static AnimFileAnimationNames Create(IEnumerable<string> unit)
        => new(
            unit.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList());
}
