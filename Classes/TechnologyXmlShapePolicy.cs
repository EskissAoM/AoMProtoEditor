using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Non-mutating helpers for unusual but readable technology XML shapes.
/// Duplicate owned containers are rejected by <see cref="TechnologyDocumentSafety"/>;
/// this policy preserves the ordering and identity of content that remains valid.
/// </summary>
public static class TechnologyXmlShapePolicy
{
    public static IReadOnlyList<XElement> EnumerateEffectsInDocumentOrder(XElement technology)
    {
        ArgumentNullException.ThrowIfNull(technology);
        var effects = new List<XElement>();
        foreach (var child in technology.Elements())
        {
            if (IsNamed(child, "effect"))
            {
                effects.Add(child);
                continue;
            }

            if (IsNamed(child, "effects"))
                effects.AddRange(child.Elements().Where(element => IsNamed(element, "effect")));
        }
        return effects;
    }

    public static IReadOnlyList<XElement> FindUnhandledNestedTechnologyChildren(
        XElement technology,
        ISet<string> editorOwnedNames)
    {
        ArgumentNullException.ThrowIfNull(technology);
        ArgumentNullException.ThrowIfNull(editorOwnedNames);
        return technology.Elements()
            .Where(element => element.HasElements && !editorOwnedNames.Contains(element.Name.LocalName))
            .ToList();
    }

    public static IReadOnlyList<XElement> FindNonEffectContainerChildren(XElement technology)
    {
        ArgumentNullException.ThrowIfNull(technology);
        return technology.Elements()
            .Where(element => IsNamed(element, "effects"))
            .SelectMany(container => container.Elements().Where(element => !IsNamed(element, "effect")))
            .ToList();
    }

    /// <summary>
    /// Returns only effects whose XML differs from the last committed snapshot.
    /// Exact duplicate effects are counted, so adding one more identical effect is
    /// still recognized as a new node. Unchanged effects are never normalized as a
    /// side effect of editing another field on the technology.
    /// </summary>
    public static IReadOnlyList<XElement> FindChangedEffects(
        XElement currentTechnology,
        XElement? savedTechnology)
    {
        ArgumentNullException.ThrowIfNull(currentTechnology);
        var remainingSaved = new Dictionary<string, int>(StringComparer.Ordinal);
        if (savedTechnology != null)
        {
            foreach (var effect in EnumerateEffectsInDocumentOrder(savedTechnology))
            {
                var key = SerializeForComparison(effect);
                remainingSaved[key] = remainingSaved.GetValueOrDefault(key) + 1;
            }
        }

        var changed = new List<XElement>();
        foreach (var effect in EnumerateEffectsInDocumentOrder(currentTechnology))
        {
            var key = SerializeForComparison(effect);
            if (remainingSaved.TryGetValue(key, out var count) && count > 0)
            {
                if (count == 1) remainingSaved.Remove(key);
                else remainingSaved[key] = count - 1;
            }
            else
            {
                changed.Add(effect);
            }
        }
        return changed;
    }

    private static string SerializeForComparison(XElement element)
    {
        var copy = new XElement(element);
        foreach (var whitespace in copy.DescendantNodes().OfType<XText>()
                     .Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsNamed(XElement element, string name)
        => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
}
