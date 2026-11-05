using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Centralizes deterministic technology-owned string IDs and namespace collision checks.</summary>
public static class TechnologyStringIdPolicy
{
    public static IReadOnlySet<string> CollectOwnedStringIds(XElement technology)
    {
        var name = technology.Attribute("name")?.Value ?? "";
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (technology.Name.Namespace != XNamespace.None || name.Length == 0) return owned;
        foreach (var tag in new[] { "displaynameid", "rollovertextid", "advancedrollovertextoverrideid" })
        {
            var field = technology.Elements().FirstOrDefault(element => element.Name.Namespace == XNamespace.None && element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (field?.Value.Trim().Equals(BuildId(name, tag), StringComparison.OrdinalIgnoreCase) == true)
                owned.Add(field.Value.Trim());
        }
        foreach (var effect in TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(technology)
                     .Where(effect => effect.Name.Namespace == XNamespace.None))
        {
            var type = effect.Attribute("type")?.Value ?? "";
            AddNumbered(effect.Attribute("tooltipID")?.Value, BuildId(name, "advancedrollovertextoverrideid"), 2, true);
            if (type.Equals("SetName", StringComparison.OrdinalIgnoreCase))
            {
                var target = NormalizeToken(effect.Attribute("tech")?.Value ?? effect.Attribute("proto")?.Value ?? "");
                if (target.Length == 0) target = "TARGET";
                foreach (var (field, suffix) in new[] { ("newName", "NEW_NAME"), ("newRollover", "NEW_ROLLOVER"), ("newShortRollover", "NEW_SHORT_ROLLOVER") })
                    AddNumbered(effect.Attribute(field)?.Value, EffectId(name, target + "_" + suffix), 2, true);
            }
            else if (new[] { "TextOutput", "TextOutputAll", "TextOutputTechName" }.Contains(type, StringComparer.OrdinalIgnoreCase))
                AddNumbered(effect.Value, EffectId(name, type.Equals("TextOutputAll", StringComparison.OrdinalIgnoreCase) ? "OUTPUTALL" : "OUTPUT"), 1, false);
            else if (type.Equals("TextEffectOutput", StringComparison.OrdinalIgnoreCase) || type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase) ? "UIALERT_" : "";
                AddNumbered(effect.Attribute("selfMsg")?.Value, EffectId(name, prefix + "SELFMSG"), 2, true);
                AddNumbered(effect.Attribute("playerMsg")?.Value, EffectId(name, prefix + "PLAYERMSG"), 2, true);
            }
            else if (type.Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                var subtype = effect.Attribute("subtype")?.Value ?? "";
                if (subtype.Equals("BuffIconOverride", StringComparison.OrdinalIgnoreCase) || subtype.Equals("PowerIconOverride", StringComparison.OrdinalIgnoreCase))
                    AddNumbered(effect.Attribute("pathstrid")?.Value,
                        $"STR_{(subtype.Equals("BuffIconOverride", StringComparison.OrdinalIgnoreCase) ? "BUFFICON" : "POWERICON")}_{NormalizeToken(name)}_OVERRIDE", 2, true);
            }
        }
        return owned;

        void AddNumbered(string? value, string basis, int minimum, bool allowBase)
        {
            var id = value?.Trim() ?? "";
            if (!id.StartsWith(basis, StringComparison.OrdinalIgnoreCase)) return;
            var suffix = id[basis.Length..];
            if ((allowBase && suffix.Length == 0) ||
                (int.TryParse(suffix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) &&
                 index >= minimum && index.ToString(System.Globalization.CultureInfo.InvariantCulture) == suffix))
                owned.Add(id);
        }
    }

    public static string EffectId(string technologyName, string suffix)
    {
        var nameId = BuildId(technologyName, "displaynameid");
        return nameId[..^5] + "_" + suffix;
    }

    public static IReadOnlyList<OwnedStringRename> RenameOwnedStringIds(XElement before, XElement target,
        string newName, IReadOnlySet<string> availableSourceIds)
    {
        var owned = CollectOwnedStringIds(before);
        var oldName = before.Attribute("name")?.Value ?? "";
        var oldPrefixes = new[] { EffectId(oldName, ""), $"STR_BUFFICON_{NormalizeToken(oldName)}_", $"STR_POWERICON_{NormalizeToken(oldName)}_" };
        var newPrefixes = new[] { EffectId(newName, ""), $"STR_BUFFICON_{NormalizeToken(newName)}_", $"STR_POWERICON_{NormalizeToken(newName)}_" };
        var changes = new List<OwnedStringRename>();
        var effects = TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(before).ToHashSet();
        foreach (var pair in before.DescendantsAndSelf().Zip(target.DescendantsAndSelf()))
        {
            var source = pair.First;
            if (source.Name.Namespace != XNamespace.None) continue;
            foreach (var attribute in source.Attributes().Where(attribute => attribute.Name.Namespace == XNamespace.None &&
                         new[] { "tooltipID", "newName", "newRollover", "newShortRollover", "selfMsg", "playerMsg", "pathstrid" }
                             .Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
                if (effects.Contains(source) && IsSupportedEffectStringAttribute(source, attribute.Name.LocalName) &&
                    pair.Second.Attribute(attribute.Name) is { } destination)
                    Rename(attribute.Value, destination.Name.LocalName, value => destination.Value = value);
            if (!source.HasElements && ((ReferenceEquals(source.Parent, before) &&
                    new[] { "displaynameid", "rollovertextid", "advancedrollovertextoverrideid" }.Contains(source.Name.LocalName, StringComparer.OrdinalIgnoreCase)) ||
                (effects.Contains(source) &&
                 new[] { "TextOutput", "TextOutputAll", "TextOutputTechName" }.Contains(source.Attribute("type")?.Value ?? "", StringComparer.OrdinalIgnoreCase))))
                Rename(source.Value, source.Name.LocalName, value => pair.Second.Value = value);
        }
        return changes;

        void Rename(string value, string field, Action<string> set)
        {
            var id = value.Trim();
            if (!owned.Contains(id) || !availableSourceIds.Contains(id)) return;
            for (var index = 0; index < oldPrefixes.Length; index++)
            {
                if (!id.StartsWith(oldPrefixes[index], StringComparison.OrdinalIgnoreCase)) continue;
                var next = newPrefixes[index] + id[oldPrefixes[index].Length..];
                if (!next.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    set(next);
                    changes.Add(new OwnedStringRename(field, id, next));
                }
                return;
            }
        }
    }

    private static bool IsSupportedEffectStringAttribute(XElement effect, string attribute)
    {
        if (attribute.Equals("tooltipID", StringComparison.OrdinalIgnoreCase)) return true;
        var type = effect.Attribute("type")?.Value ?? "";
        if (type.Equals("SetName", StringComparison.OrdinalIgnoreCase))
            return new[] { "newName", "newRollover", "newShortRollover" }.Contains(attribute, StringComparer.OrdinalIgnoreCase);
        if (type.Equals("TextEffectOutput", StringComparison.OrdinalIgnoreCase) || type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase))
            return new[] { "selfMsg", "playerMsg" }.Contains(attribute, StringComparer.OrdinalIgnoreCase);
        return type.Equals("Data", StringComparison.OrdinalIgnoreCase) && attribute.Equals("pathstrid", StringComparison.OrdinalIgnoreCase) &&
               new[] { "BuffIconOverride", "PowerIconOverride" }.Contains(effect.Attribute("subtype")?.Value ?? "", StringComparer.OrdinalIgnoreCase);
    }

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
