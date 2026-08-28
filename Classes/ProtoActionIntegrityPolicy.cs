using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal static class ProtoActionIntegrityPolicy
{
    internal sealed record ReferenceIssue(string ActionName, string FieldLabel, string TargetName);

    /// <summary>
    /// Existing legacy and tactics-backed actions may intentionally omit a local
    /// type. A newly created action must resolve to a type before it can be saved.
    /// </summary>
    public static bool IsMissingRequiredType(bool isNewCustomAction, string? resolvedType)
        => isNewCustomAction && string.IsNullOrWhiteSpace(resolvedType);

    /// <summary>
    /// Validates references managed by the ProtoAction editor against the unit's
    /// effective action catalog. Comparisons are deliberately case-insensitive.
    /// An unchanged broken reference from legacy XML is preserved so opening and
    /// saving an old mod never becomes destructive; newly introduced or edited
    /// broken references are reported.
    /// </summary>
    public static IReadOnlyList<ReferenceIssue> FindBrokenReferences(
        XElement candidateUnit,
        XElement? originalUnit,
        IEnumerable<string>? inheritedActionNames = null)
    {
        ArgumentNullException.ThrowIfNull(candidateUnit);

        var availableActionNames = GetActionElements(candidateUnit)
            .Select(GetActionName)
            .Concat(inheritedActionNames ?? [])
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var originalReferences = originalUnit == null
            ? new HashSet<ReferenceIdentity>()
            : EnumerateReferences(originalUnit).Select(ToIdentity).ToHashSet();

        return EnumerateReferences(candidateUnit)
            .Where(reference => reference.TargetName.Length > 0)
            .Where(reference => !availableActionNames.Contains(reference.TargetName))
            .Where(reference => !originalReferences.Contains(ToIdentity(reference)))
            .Select(reference => new ReferenceIssue(
                reference.ActionName,
                reference.FieldLabel,
                reference.TargetName))
            .Distinct()
            .OrderBy(issue => issue.ActionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.FieldLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.TargetName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildReferenceValidationReport(
        string? unitName,
        IEnumerable<ReferenceIssue> issues)
    {
        var normalizedUnitName = string.IsNullOrWhiteSpace(unitName)
            ? "Unknown ProtoUnit"
            : unitName.Trim();
        var issueList = issues.ToList();
        var lines = new List<string>
        {
            $"ProtoUnit: {normalizedUnitName}",
            "",
            "Correct the following ProtoAction reference issues before saving:"
        };

        foreach (var actionGroup in issueList.GroupBy(
                     issue => issue.ActionName,
                     StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("");
            lines.Add(actionGroup.Key.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase)
                ? "ProtoUnit attributes:"
                : $"Action '{actionGroup.Key}':");
            lines.AddRange(actionGroup.Select(issue =>
                $"- {issue.FieldLabel} references missing action '{issue.TargetName}'."));
        }

        return string.Join("\n", lines);
    }

    private sealed record ReferenceCandidate(string ActionName, string FieldLabel, string TargetName);

    private sealed record ReferenceIdentity(string ActionName, string FieldLabel, string TargetName);

    private static ReferenceIdentity ToIdentity(ReferenceCandidate reference)
        => new(
            reference.ActionName.ToUpperInvariant(),
            reference.FieldLabel.ToUpperInvariant(),
            reference.TargetName.ToUpperInvariant());

    private static IEnumerable<ReferenceCandidate> EnumerateReferences(XElement unit)
    {
        foreach (var (tag, label) in new[]
                 {
                     ("selfdestructprotoaction", "Self Destruct Action"),
                     ("birthprotoaction", "Birth Action"),
                     ("stackprotoaction", "Stack Proto Action")
                 })
        {
            var value = GetChildValue(unit, tag);
            if (value.Length > 0)
                yield return new ReferenceCandidate("ProtoUnit", label, value);
        }

        foreach (var action in GetActionElements(unit))
        {
            var actionName = GetActionName(action);
            if (actionName.Length == 0)
                actionName = "Unnamed ProtoAction";

            var areaAction = GetChildValue(action, "areaprotoaction");
            if (areaAction.Length > 0)
                yield return new ReferenceCandidate(actionName, "Area Proto Action", areaAction);

            foreach (var stackControl in ChildElements(action, "stackcontrol"))
            {
                var addAction = GetChildValue(stackControl, "stackaddaction");
                if (addAction.Length > 0)
                    yield return new ReferenceCandidate(actionName, "Stack Add Action", addAction);

                var subAction = GetChildValue(stackControl, "stacksubaction");
                if (subAction.Length > 0)
                    yield return new ReferenceCandidate(actionName, "Stack Sub Action", subAction);
            }

            foreach (var onHitEffect in ChildElements(action, "onhiteffect"))
            {
                var infectAction = Normalize(GetAttributeValue(onHitEffect, "protoaction"));
                if (infectAction.Length > 0)
                    yield return new ReferenceCandidate(actionName, "Infect Action", infectAction);
            }
        }
    }

    private static IEnumerable<XElement> GetActionElements(XElement unit)
        => unit.Elements().Where(element =>
            element.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> ChildElements(XElement parent, string localName)
        => parent.Elements().Where(element =>
            element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static string GetActionName(XElement action)
        => GetChildValue(action, "name");

    private static string GetChildValue(XElement parent, string localName)
        => Normalize(ChildElements(parent, localName).FirstOrDefault()?.Value);

    private static string GetAttributeValue(XElement element, string localName)
        => element.Attributes()
               .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
               ?.Value ?? "";

    private static string Normalize(string? value) => value?.Trim() ?? "";
}
