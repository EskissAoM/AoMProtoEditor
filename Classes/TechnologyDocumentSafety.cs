using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record TechnologyDocumentValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string ToDisplayMessage(string? path = null)
    {
        var location = string.IsNullOrWhiteSpace(path) ? "The technology mod file" : $"'{path}'";
        return $"{location} cannot be edited safely and will not be overwritten: {string.Join(" ", Errors)}";
    }
}

/// <summary>
/// Performs the non-repairing structural checks required before a technology
/// document is exposed for editing. Unknown elements and attributes are valid;
/// only shapes that make editor ownership ambiguous are rejected.
/// </summary>
public static class TechnologyDocumentSafety
{
    public static TechnologyDocumentValidation ValidateModDocument(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        var root = document.Root;
        if (root == null || !root.Name.LocalName.Equals("techtreemods", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Expected a <techtreemods> root element.");
            return new TechnologyDocumentValidation(errors);
        }

        var directTechnologies = root.Elements()
            .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var nestedTechnologies = root.Descendants()
            .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            .Except(directTechnologies)
            .ToList();
        if (nestedTechnologies.Count > 0)
            errors.Add($"Found {nestedTechnologies.Count} <tech> element(s) outside the root technology list.");

        var namedTechnologies = new List<(XElement Element, string Name)>();
        foreach (var technology in directTechnologies)
        {
            var name = technology.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
            if (name.Length == 0)
            {
                errors.Add("Found a <tech> element without a non-empty name attribute.");
                continue;
            }

            namedTechnologies.Add((technology, name));
            var effectsContainers = technology.Elements().Where(element =>
                element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
            var prerequisiteContainers = technology.Elements().Where(element =>
                element.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase)).ToList();
            var duplicateCosts = technology.Elements().Where(element =>
                    element.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("resourcetype", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "")
                .Where(resource => resource.Length > 0)
                .GroupBy(resource => resource, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);
            if (effectsContainers.Count > 1)
                errors.Add($"Technology '{name}' contains {effectsContainers.Count} <effects> containers; the game accepts at most one.");
            if (prerequisiteContainers.Count > 1)
                errors.Add($"Technology '{name}' contains {prerequisiteContainers.Count} <prereqs> containers; the game accepts at most one.");
            foreach (var duplicateCost in duplicateCosts)
                errors.Add($"Technology '{name}' contains {duplicateCost.Count()} <cost> entries for resource '{duplicateCost.Key}'; the game accepts at most one cost per resource.");
        }

        foreach (var duplicate in namedTechnologies
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Technology name '{duplicate.Key}' occurs {duplicate.Count()} times.");
        }

        return new TechnologyDocumentValidation(errors.Distinct(StringComparer.Ordinal).ToList());
    }
}
