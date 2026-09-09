using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Centralizes deterministic technology-owned string IDs and namespace collision checks.</summary>
public static class TechnologyStringIdPolicy
{
    public static string NormalizeToken(string value)
    {
        var token = (value ?? "").Trim();
        token = Regex.Replace(token, "([A-Z]+)([A-Z][a-z])", "$1_$2");
        token = Regex.Replace(token, "([a-z0-9])([A-Z])", "$1_$2");
        var normalized = new string(token.ToUpperInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }

    public static string BuildId(string technologyName, string tag)
    {
        var suffix = tag.ToLowerInvariant() switch
        {
            "displaynameid" => "NAME",
            "rollovertextid" => "LR",
            "advancedrollovertextoverrideid" => "OVERRIDE",
            _ => "TEXT"
        };
        return $"STR_TECH_{NormalizeToken(technologyName)}_{suffix}";
    }

    public static string? FindNamespaceCollision(
        string technologyName,
        IEnumerable<string> existingTechnologyNames,
        string? excludedTechnologyName = null)
    {
        ArgumentNullException.ThrowIfNull(existingTechnologyNames);
        var token = NormalizeToken(technologyName);
        return existingTechnologyNames.FirstOrDefault(existing =>
            !string.IsNullOrWhiteSpace(existing) &&
            !existing.Equals(technologyName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(excludedTechnologyName) ||
             !existing.Equals(excludedTechnologyName, StringComparison.OrdinalIgnoreCase)) &&
            NormalizeToken(existing).Equals(token, StringComparison.Ordinal));
    }

    public static bool IsReferencedByTechnologies(string stringId, IEnumerable<XElement> technologies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stringId);
        ArgumentNullException.ThrowIfNull(technologies);
        return technologies.Any(technology =>
            technology.DescendantsAndSelf().Any(element =>
                element.Attributes().Any(attribute =>
                    attribute.Value.Trim().Equals(stringId, StringComparison.OrdinalIgnoreCase)) ||
                (!element.HasElements && element.Value.Trim().Equals(stringId, StringComparison.OrdinalIgnoreCase))));
    }
}
