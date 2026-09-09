using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum GameIdentifierKind
{
    ProtoUnit,
    UnitType
}

public sealed record ProtoActionRenameOperation(
    string UnitName,
    string OldName,
    string NewName);

/// <summary>
/// Central, conservative rename policy for identifiers shared by the ProtoUnit
/// and Technology editors. Definition names are deliberately excluded: callers
/// rename the definition themselves and this class migrates only references.
/// </summary>
public static class GameIdentifierReferencePolicy
{
    private static readonly HashSet<string> ProtoUnitElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "train", "projectile", "conversionprotoid", "attachprotounit", "activevfxproto",
        "modifyprotoid", "partisantype", "disguiseprotoid", "deadreplacement", "deadtransform",
        "eidolonprotoid", "nonsocketplaceprotoid", "locationprotoid", "fromprotoid", "toprotoid",
        "dependentunit", "generator", "spawn", "unit"
    };

    private static readonly HashSet<string> UnitTypeElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unittype", "modifyabstracttype", "forbidabstracttype", "forbidunittype", "targettype",
        "targetunittype", "queryunittype", "attackertype", "contributorid", "rewardtype",
        "socketunittype", "modifyunittype", "castpowertargettype", "modifydamagetargettype",
        "abstractplacementtargettype", "explicitlyrestrictedplacementtargettype"
    };

    private static readonly HashSet<string> ProtoUnitAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "proto", "unit", "generator", "fromprotoid", "toprotoid", "locationprotoid",
        "waterprotounit", "modifyprotoid"
    };

    private static readonly HashSet<string> UnitTypeAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unittype", "targettype", "targetunittype", "queryunittype", "attackertype",
        "contributorid", "rewardtype", "forbidunittype", "modifyabstracttype", "forbidabstracttype",
        "attacktype", "ignoretype"
    };

    private static readonly HashSet<string> TypeAttributeReferenceOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        "damagebonus", "typedanim", "modifyabstracttype", "forbidabstracttype", "forbidunittype",
        "unittype", "rate"
    };

    private static readonly HashSet<string> ProtoActionReferenceElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "selfdestructprotoaction", "birthprotoaction", "stackprotoaction", "areaprotoaction",
        "stackaddaction", "stacksubaction"
    };

    public static int RenameReferences(
        XContainer document,
        GameIdentifierKind kind,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var oldValue = oldName.Trim();
        var newValue = newName.Trim();
        if (oldValue.Length == 0 || newValue.Length == 0 ||
            oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            return 0;

        var changed = 0;
        if (kind == GameIdentifierKind.UnitType)
            changed += RenameProtoActionDynamicUnitTypeElements(document, oldValue, newValue);
        foreach (var element in document.Descendants())
        {
            if (IsReferenceElement(element, kind) && EqualsIdentifier(element.Value, oldValue))
            {
                element.Value = newValue;
                changed++;
            }

            foreach (var attribute in element.Attributes())
            {
                if (!IsReferenceAttribute(element, attribute, kind) ||
                    !EqualsIdentifier(attribute.Value, oldValue))
                    continue;
                attribute.Value = newValue;
                changed++;
            }
        }

        return changed;
    }

    public static int RenameProtoActionReferences(XElement unit, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var oldValue = oldName.Trim();
        var newValue = newName.Trim();
        if (oldValue.Length == 0 || newValue.Length == 0 ||
            oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            return 0;

        var changed = 0;
        foreach (var element in unit.Descendants().Where(element =>
                     ProtoActionReferenceElements.Contains(element.Name.LocalName) &&
                     EqualsIdentifier(element.Value, oldValue)))
        {
            element.Value = newValue;
            changed++;
        }

        foreach (var attribute in unit.Descendants()
                     .Where(element => element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))
                     .Attributes()
                     .Where(attribute => attribute.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase) &&
                                         EqualsIdentifier(attribute.Value, oldValue)))
        {
            attribute.Value = newValue;
            changed++;
        }
        return changed;
    }

    public static IReadOnlyList<string> FindUnrecognizedProtoActionReferences(
        XElement unit,
        string actionName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var recognized = new HashSet<XObject>(ReferenceEqualityComparer.Instance);
        foreach (var element in unit.Descendants().Where(element =>
                     ProtoActionReferenceElements.Contains(element.Name.LocalName)))
            recognized.Add(element);
        foreach (var attribute in unit.Descendants()
                     .Where(element => element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))
                     .Attributes()
                     .Where(attribute => attribute.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
            recognized.Add(attribute);

        var unknown = new List<string>();
        foreach (var element in unit.Descendants().Where(element =>
                     !element.HasElements &&
                     !recognized.Contains(element) &&
                     element.Name.LocalName.Contains("action", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, actionName)))
            unknown.Add($"<{element.Name.LocalName}>");
        foreach (var attribute in unit.Descendants().Attributes().Where(attribute =>
                     !recognized.Contains(attribute) &&
                     attribute.Name.LocalName.Contains("action", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, actionName)))
            unknown.Add($"@{attribute.Name.LocalName}");
        return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> FindUnrecognizedTechTypeReferences(
        XContainer techtree,
        string techTypeName)
    {
        ArgumentNullException.ThrowIfNull(techtree);
        var recognized = new HashSet<XObject>(ReferenceEqualityComparer.Instance);
        foreach (var element in techtree.Descendants().Where(element =>
                     element.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)))
            recognized.Add(element);
        foreach (var effect in techtree.Descendants().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var attribute in effect.Attributes().Where(attribute =>
                         attribute.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)))
                recognized.Add(attribute);
            foreach (var target in effect.Elements().Where(element =>
                         element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)))
            {
                if (GetAttribute(target, "type")?.Value.Equals("TechType", StringComparison.OrdinalIgnoreCase) == true)
                    recognized.Add(target);
                var exclusions = GetAttribute(target, "excludetypes");
                if (exclusions != null)
                    recognized.Add(exclusions);
            }
        }

        var unknown = new List<string>();
        foreach (var element in techtree.Descendants().Where(element =>
                     !element.HasElements &&
                     !recognized.Contains(element) &&
                     element.Name.LocalName.Contains("techtype", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, techTypeName)))
            unknown.Add($"<{element.Name.LocalName}>");
        foreach (var attribute in techtree.Descendants().Attributes().Where(attribute =>
                     !recognized.Contains(attribute) &&
                     attribute.Name.LocalName.Contains("techtype", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, techTypeName)))
            unknown.Add($"@{attribute.Name.LocalName}");
        return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int RenameTechnologyProtoActionReferences(
        XContainer techtree,
        ProtoActionRenameOperation operation)
    {
        ArgumentNullException.ThrowIfNull(techtree);
        var changed = 0;
        foreach (var effect in techtree.Descendants().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
        {
            if (!EffectTargetsProtoUnit(effect, operation.UnitName))
                continue;

            foreach (var attribute in effect.Attributes().Where(attribute =>
                         (attribute.Name.LocalName.Equals("action", StringComparison.OrdinalIgnoreCase) ||
                          attribute.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)) &&
                         EqualsIdentifier(attribute.Value, operation.OldName)))
            {
                attribute.Value = operation.NewName.Trim();
                changed++;
            }
        }
        return changed;
    }

    public static int RenameAttributeReferences(
        XContainer document,
        string attributeName,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = 0;
        foreach (var attribute in document.Descendants().Attributes().Where(attribute =>
                     attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, oldName)))
        {
            attribute.Value = newName.Trim();
            changed++;
        }
        return changed;
    }

    public static int RenameElementReferences(
        XContainer document,
        string elementName,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = 0;
        foreach (var element in document.Descendants().Where(element =>
                     !element.HasElements &&
                     element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, oldName)))
        {
            element.Value = newName.Trim();
            changed++;
        }
        return changed;
    }

    public static IReadOnlyList<string> FindUnrecognizedReferences(
        XContainer document,
        GameIdentifierKind kind,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<string>();
        foreach (var element in document.Descendants())
        {
            if (!element.HasElements && EqualsIdentifier(element.Value, identifier) &&
                LooksLikeReferenceField(element.Name.LocalName, kind) &&
                !IsReferenceElement(element, kind) &&
                !IsDefinitionElement(element, kind))
            {
                result.Add($"<{element.Name.LocalName}>");
            }

            foreach (var attribute in element.Attributes().Where(attribute =>
                         EqualsIdentifier(attribute.Value, identifier) &&
                         LooksLikeReferenceField(attribute.Name.LocalName, kind) &&
                         !IsReferenceAttribute(element, attribute, kind) &&
                         !IsDefinitionAttribute(element, attribute, kind)))
            {
                result.Add($"@{attribute.Name.LocalName}");
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsReferenceElement(XElement element, GameIdentifierKind kind)
    {
        if (element.HasElements)
            return false;
        var name = element.Name.LocalName;
        if (kind == GameIdentifierKind.ProtoUnit && ProtoUnitElementNames.Contains(name))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && UnitTypeElementNames.Contains(name) && IsTechnologyField(element))
            return true;
        if (kind == GameIdentifierKind.UnitType && UnitTypeElementNames.Contains(name))
            return true;

        if (!name.Equals("target", StringComparison.OrdinalIgnoreCase))
            return false;
        if (kind == GameIdentifierKind.UnitType && element.Ancestors().Any(ancestor =>
                ancestor.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
            return true;
        var targetType = GetAttribute(element, "type")?.Value.Trim();
        return targetType?.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsReferenceAttribute(
        XElement owner,
        XAttribute attribute,
        GameIdentifierKind kind)
    {
        var name = attribute.Name.LocalName;
        if (kind == GameIdentifierKind.ProtoUnit && ProtoUnitAttributeNames.Contains(name))
            return !(owner.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
                     name.Equals("unit", StringComparison.OrdinalIgnoreCase));
        if (kind == GameIdentifierKind.ProtoUnit && UnitTypeAttributeNames.Contains(name) && IsTechnologyField(owner))
            return true;
        if (kind == GameIdentifierKind.UnitType && UnitTypeAttributeNames.Contains(name))
            return true;
        if (kind == GameIdentifierKind.UnitType &&
            name.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
            IsTechnologyField(owner))
            return true;
        return kind == GameIdentifierKind.UnitType &&
               name.Equals("type", StringComparison.OrdinalIgnoreCase) &&
               TypeAttributeReferenceOwners.Contains(owner.Name.LocalName);
    }

    private static int RenameProtoActionDynamicUnitTypeElements(
        XContainer document,
        string oldName,
        string newName)
    {
        var changed = 0;
        var empowerSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empowerdata", "enemyempowerdata", "natureempowerdata"
        };
        foreach (var target in document.Descendants().Where(element =>
                     element.Parent != null &&
                     empowerSectionNames.Contains(element.Parent.Name.LocalName) &&
                     element.Name.LocalName.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
        {
            target.Name = target.Name.Namespace + newName;
            changed++;
        }
        return changed;
    }

    private static bool EffectTargetsProtoUnit(XElement effect, string unitName)
    {
        if (effect.Elements().Any(target =>
                target.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                GetAttribute(target, "type")?.Value.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase) == true &&
                EqualsIdentifier(target.Value, unitName)))
            return true;

        return effect.Attributes().Any(attribute =>
            (attribute.Name.LocalName.Equals("proto", StringComparison.OrdinalIgnoreCase) ||
             attribute.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase)) &&
            EqualsIdentifier(attribute.Value, unitName));
    }

    private static XAttribute? GetAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsTechnologyField(XElement element)
        => element.AncestorsAndSelf().Any(candidate =>
            candidate.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            GetAttribute(candidate, "name") != null);

    private static bool IsDefinitionElement(XElement element, GameIdentifierKind kind)
        => kind == GameIdentifierKind.UnitType &&
           element.Name.LocalName.Equals("abstractunittype", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefinitionAttribute(
        XElement owner,
        XAttribute attribute,
        GameIdentifierKind kind)
        => kind == GameIdentifierKind.ProtoUnit &&
           owner.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
           attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeReferenceField(string name, GameIdentifierKind kind)
        => kind == GameIdentifierKind.ProtoUnit
            ? name.Contains("proto", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("unit", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("projectile", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("spawn", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("generator", StringComparison.OrdinalIgnoreCase)
            : name.Contains("unittype", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("abstracttype", StringComparison.OrdinalIgnoreCase) ||
              name.Equals("type", StringComparison.OrdinalIgnoreCase) ||
              name.Equals("unit", StringComparison.OrdinalIgnoreCase);

    private static bool EqualsIdentifier(string? value, string expected)
        => value?.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase) == true;
}
