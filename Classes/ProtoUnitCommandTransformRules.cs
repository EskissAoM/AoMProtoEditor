using System;
using System.Collections.Generic;
using System.Linq;

namespace AoMDivineDataEditor.Classes;

public enum ProtoUnitCommandTransformKind
{
    Unique,
    Multiple
}

public readonly record struct ProtoUnitCommandTransformValidation(
    bool FromValid,
    bool ToValid,
    bool PrereqTechValid,
    bool AssociatedTechValid)
{
    public bool IsValid => FromValid && ToValid && PrereqTechValid && AssociatedTechValid;
}

public static class ProtoUnitCommandTransformRules
{
    private static readonly string[] TransformFamilyFlags = ["transform", "transformselected", "transformvillager"];
    private static readonly string[] UniqueDefaults = ["transformselected", "displayontarget", "unitcommand"];
    private static readonly string[] MultipleDefaults = ["transform", "displayontarget", "researchonselected", "unitcommand"];

    public static string RequiredFlag(ProtoUnitCommandTransformKind kind)
        => kind == ProtoUnitCommandTransformKind.Multiple ? "transform" : "transformselected";

    public static bool IsRequiredFlag(string flag, ProtoUnitCommandTransformKind kind)
        => flag.Equals(RequiredFlag(kind), StringComparison.OrdinalIgnoreCase);

    public static bool IsTransformFamilyFlag(string flag)
        => TransformFamilyFlags.Contains(flag, StringComparer.OrdinalIgnoreCase);

    public static void ApplyModeDefaults(ISet<string> flags, ProtoUnitCommandTransformKind kind)
    {
        foreach (var flag in TransformFamilyFlags)
            flags.Remove(flag);
        if (kind == ProtoUnitCommandTransformKind.Unique)
            flags.Remove("researchonselected");
        foreach (var flag in kind == ProtoUnitCommandTransformKind.Multiple ? MultipleDefaults : UniqueDefaults)
            flags.Add(flag);
    }

    public static void EnsureStructuralFlag(ISet<string> flags, ProtoUnitCommandTransformKind kind)
    {
        foreach (var flag in TransformFamilyFlags)
            flags.Remove(flag);
        flags.Add(RequiredFlag(kind));
    }

    public static ProtoUnitCommandTransformValidation ValidateRequired(
        ProtoUnitTransformDefinition transform,
        IDictionary<string, string> values,
        IReadOnlyCollection<string> protoUnitNames,
        IReadOnlyCollection<string> techNames,
        string? expectedFrom = null)
    {
        var from = transform.From?.Trim() ?? string.Empty;
        var to = transform.To?.Trim() ?? string.Empty;
        var prereq = transform.Tech?.Trim() ?? string.Empty;
        var associated = values.TryGetValue("associatedtech", out var value) ? value?.Trim() ?? string.Empty : string.Empty;

        var fromValid = !string.IsNullOrWhiteSpace(from) &&
                        protoUnitNames.Contains(from, StringComparer.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(expectedFrom) || from.Equals(expectedFrom, StringComparison.OrdinalIgnoreCase));
        var toValid = !string.IsNullOrWhiteSpace(to) && protoUnitNames.Contains(to, StringComparer.OrdinalIgnoreCase);
        var prereqValid = !string.IsNullOrWhiteSpace(prereq) && techNames.Contains(prereq, StringComparer.OrdinalIgnoreCase);
        var associatedValid = !string.IsNullOrWhiteSpace(associated) && techNames.Contains(associated, StringComparer.OrdinalIgnoreCase);

        return new ProtoUnitCommandTransformValidation(fromValid, toValid, prereqValid, associatedValid);
    }
}
