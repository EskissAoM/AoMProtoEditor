using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum TechnologyReferenceDocumentKind
{
    Techtree,
    ProtoUnits,
    ProtoUnitCommands,
    Abilities,
    UnitTransforms,
    Powers,
    MajorGods,
    Tactics,
    Other
}

public sealed record TechnologyReferenceSource(
    string Label,
    XDocument Document,
    TechnologyReferenceDocumentKind Kind,
    string? Path = null,
    bool IsBuiltIn = false,
    bool CanMigrate = false);

public sealed record TechnologyReferenceUsage(
    string TechnologyName,
    string SourceLabel,
    string Owner,
    string Field,
    string? Path,
    bool IsBuiltIn,
    bool IsRecognized,
    bool CanMigrate)
{
    public string DisplayLocation
        => string.IsNullOrWhiteSpace(Owner)
            ? $"{SourceLabel}: {Field}"
            : $"{SourceLabel}: {Owner} — {Field}";
}

public sealed record TechnologyRenameOperation(string OldName, string NewName);

/// <summary>
/// One conservative catalog for technology-name references. Only shapes which
/// the editors or shipped data use are migratable. Tech-looking fields outside
/// those shapes are reported as unknown instead of being rewritten by guesswork.
/// </summary>
public static class TechnologyReferenceIndex
{
    private static readonly HashSet<string> LeafReferenceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "tech", "technology", "associatedtech", "forbidtech", "requiredtech"
    };

    private static readonly HashSet<string> TechtreeValueEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TechStatus", "ForbidTech", "SetOnBuildingDeathTech", "SetOnTechResearchedTech"
    };

    private static readonly HashSet<string> IgnoredTechLikeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "techtype", "techtypes", "techflag", "techflags", "techage"
    };

    public static IReadOnlyList<TechnologyReferenceUsage> Build(
        IEnumerable<TechnologyReferenceSource> sources,
        IEnumerable<string> knownTechnologyNames)
    {
        var known = knownTechnologyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<TechnologyReferenceUsage>();
        foreach (var source in sources)
        {
            foreach (var candidate in EnumerateCandidates(source))
            {
                var value = candidate.Value.Trim();
                if (value.Length == 0)
                    continue;
                if (!candidate.IsRecognized && !known.Contains(value))
                    continue;

                results.Add(new TechnologyReferenceUsage(
                    value,
                    source.Label,
                    candidate.Owner,
                    candidate.Field,
                    source.Path,
                    source.IsBuiltIn,
                    candidate.IsRecognized,
                    source.CanMigrate && candidate.IsRecognized));
            }
        }

        return results;
    }

    public static int RenameRecognizedReferences(
        TechnologyReferenceSource source,
        string oldName,
        string newName)
    {
        var changed = 0;
        foreach (var candidate in EnumerateCandidates(source))
        {
            if (!candidate.IsRecognized ||
                !candidate.Value.Trim().Equals(oldName, StringComparison.OrdinalIgnoreCase))
                continue;

            candidate.SetValue(newName);
            changed++;
        }
        return changed;
    }

    public static IReadOnlyList<TechnologyRenameOperation> AddOrCollapseRename(
        IEnumerable<TechnologyRenameOperation> current,
        string oldName,
        string newName)
    {
        var operations = current.ToList();
        var chained = operations.FindIndex(operation =>
            operation.NewName.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (chained >= 0)
        {
            var original = operations[chained].OldName;
            if (original.Equals(newName, StringComparison.OrdinalIgnoreCase))
                operations.RemoveAt(chained);
            else
                operations[chained] = new TechnologyRenameOperation(original, newName);
            return operations;
        }

        operations.Add(new TechnologyRenameOperation(oldName, newName));
        return operations;
    }

    private static IEnumerable<ReferenceCandidate> EnumerateCandidates(TechnologyReferenceSource source)
    {
        if (source.Document.Root == null)
            yield break;

        var recognizedObjects = new HashSet<XObject>(ReferenceEqualityComparer.Instance);
        foreach (var element in source.Document.Descendants())
        {
            if (TryRecognizeElement(source.Kind, element, out var field))
            {
                recognizedObjects.Add(element);
                yield return new ReferenceCandidate(
                    element.Value,
                    GetOwner(element, source.Kind),
                    field,
                    true,
                    value => element.Value = value);
            }

            foreach (var attribute in element.Attributes())
            {
                if (!TryRecognizeAttribute(source.Kind, element, attribute, out field))
                    continue;
                recognizedObjects.Add(attribute);
                yield return new ReferenceCandidate(
                    attribute.Value,
                    GetOwner(element, source.Kind),
                    field,
                    true,
                    value => attribute.Value = value);
            }
        }

        foreach (var element in source.Document.Descendants().Where(element => !element.HasElements))
        {
            if (recognizedObjects.Contains(element) ||
                !LooksLikeTechnologyField(element.Name.LocalName) ||
                IgnoredTechLikeNames.Contains(element.Name.LocalName))
                continue;
            yield return new ReferenceCandidate(
                element.Value,
                GetOwner(element, source.Kind),
                $"unknown <{element.Name.LocalName}>",
                false,
                _ => { });
        }

        foreach (var attribute in source.Document.Descendants().Attributes())
        {
            if (recognizedObjects.Contains(attribute) ||
                !LooksLikeTechnologyField(attribute.Name.LocalName) ||
                IgnoredTechLikeNames.Contains(attribute.Name.LocalName))
                continue;
            yield return new ReferenceCandidate(
                attribute.Value,
                GetOwner(attribute.Parent, source.Kind),
                $"unknown @{attribute.Name.LocalName}",
                false,
                _ => { });
        }
    }

    private static bool TryRecognizeElement(
        TechnologyReferenceDocumentKind kind,
        XElement element,
        out string field)
    {
        field = "";
        if (element.HasElements)
            return false;

        var name = element.Name.LocalName;
        if (kind == TechnologyReferenceDocumentKind.Techtree)
        {
            if (name.Equals("techstatus", StringComparison.OrdinalIgnoreCase))
            {
                field = "prerequisite technology";
                return true;
            }

            if (name.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                GetAttribute(element, "type")?.Value.Equals("Tech", StringComparison.OrdinalIgnoreCase) == true)
            {
                field = "effect target technology";
                return true;
            }

            if (name.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
                element.Parent?.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase) == true &&
                GetAttribute(element.Parent, "type")?.Value.Equals("RandomTech", StringComparison.OrdinalIgnoreCase) == true)
            {
                field = "RandomTech choice";
                return true;
            }

            if (name.Equals("effect", StringComparison.OrdinalIgnoreCase) &&
                TechtreeValueEffectTypes.Contains(GetAttribute(element, "type")?.Value ?? ""))
            {
                field = $"{GetAttribute(element, "type")?.Value} effect";
                return true;
            }

            return false;
        }

        if (!LeafReferenceNames.Contains(name))
            return false;

        field = $"<{name}>";
        return true;
    }

    private static bool TryRecognizeAttribute(
        TechnologyReferenceDocumentKind kind,
        XElement owner,
        XAttribute attribute,
        out string field)
    {
        field = "";
        var name = attribute.Name.LocalName;
        if (kind == TechnologyReferenceDocumentKind.Techtree)
        {
            if (!owner.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase))
                return false;

            if (name.Equals("reqTech", StringComparison.OrdinalIgnoreCase))
            {
                field = "required technology";
                return true;
            }

            if (!name.Equals("tech", StringComparison.OrdinalIgnoreCase))
                return false;

            var type = GetAttribute(owner, "type")?.Value ?? "";
            var subtype = GetAttribute(owner, "subtype")?.Value ?? "";
            if (type.Equals("SetName", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Data", StringComparison.OrdinalIgnoreCase) &&
                subtype.Equals("ResourceIfTechActive", StringComparison.OrdinalIgnoreCase))
            {
                field = type.Equals("SetName", StringComparison.OrdinalIgnoreCase)
                    ? "SetName technology"
                    : "conditional technology";
                return true;
            }
            return false;
        }

        if (!LeafReferenceNames.Contains(name))
            return false;
        field = $"@{name}";
        return true;
    }

    private static bool LooksLikeTechnologyField(string name)
        => name.Contains("tech", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("technology", StringComparison.OrdinalIgnoreCase);

    private static XAttribute? GetAttribute(XElement? element, string name)
        => element?.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string GetOwner(XElement? element, TechnologyReferenceDocumentKind kind)
    {
        if (element == null)
            return "";

        if (kind == TechnologyReferenceDocumentKind.Techtree)
        {
            var tech = element.AncestorsAndSelf().FirstOrDefault(candidate =>
                candidate.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
                GetAttribute(candidate, "name") != null);
            return GetAttribute(tech, "name")?.Value.Trim() ?? "";
        }

        if (kind == TechnologyReferenceDocumentKind.ProtoUnitCommands)
        {
            var command = element.AncestorsAndSelf().FirstOrDefault(candidate =>
                candidate.Name.LocalName.Equals("protounitcommand", StringComparison.OrdinalIgnoreCase));
            return command?.Elements().FirstOrDefault(child =>
                child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
        }

        var named = element.AncestorsAndSelf().FirstOrDefault(candidate =>
            GetAttribute(candidate, "name") != null);
        return GetAttribute(named, "name")?.Value.Trim() ?? "";
    }

    private sealed record ReferenceCandidate(
        string Value,
        string Owner,
        string Field,
        bool IsRecognized,
        Action<string> SetValue);
}
