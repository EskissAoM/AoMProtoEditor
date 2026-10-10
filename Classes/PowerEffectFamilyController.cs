using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Owns the XML rules shared by Rain and the effect-based Power families.
/// UI construction and dirty-state orchestration deliberately remain in the view.
/// </summary>
public static class PowerEffectFamilyController
{
    private static readonly string[] RelativityDisplays =
        ["Add", "Multiply", "Multiply base", "Set to", "Override"];

    private static readonly HashSet<string> SharedPowerTypes = new(StringComparer.OrdinalIgnoreCase)
        { "GeneralEffect", "Eclipse", "CeaseFire", "Prosperity" };

    private static readonly HashSet<string> PlayerTargetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Market", "MinimumResourceTrickleRate", "MaximumResourceTrickleRate",
        "ResourceTrickleRate", "GodPowerROFFactor", "Resource", "AutoGatherBonus",
        "UpdateVisuals", "EqualizeResources", "RevealEnemyLOS", "SharedLOS",
        "EnemyAttachProtoUnit"
    };

    private static readonly HashSet<string> AmountlessTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TechActivate", "UpdateVisuals", "AssignOnHitEffect", "TacticEnable", "ActionEnable",
        "EnemyAttachProtoUnit", "ProtoActionAdd", "CannotSnare", "CantBeSlowed",
        "Invulnerable", "CanTargetButTakesNoDamage", "EqualizeResources", "RevealEnemyLOS",
        "SharedLOS"
    };

    private static readonly HashSet<string> IgnoreNatureTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkRate", "WorkRateSpecific", "TacticEnable", "RateOfFire", "ActionEnable",
        "MaximumVelocity", "DamageBonus", "Damage", "HitPoints", "TrainPoints",
        "OnHitEffectProbability", "WorkRateAll", "ProtoActionAdd", "ShieldPoints",
        "OnHitEffectRate", "ArmorVulnerability", "RechargeTime", "BuildPoints",
        "ModifyRate", "BuildingWorkRate"
    };

    public static bool IsSharedPowerType(string? type)
        => !string.IsNullOrWhiteSpace(type) && SharedPowerTypes.Contains(type.Trim());

    public static XElement AddDefaultEffect(XElement owner)
        => PowerCatalog.AddRepeatedField(owner, "effect", "");

    public static void ChangeType(XElement effect, string? previousType, string newType)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (string.IsNullOrWhiteSpace(newType)) return;

        // An untyped modded Effect may already contain its valid target. Selecting the missing
        // Type completes that record and must not silently erase the user's target. A genuine
        // type-to-type transition still starts clean so incompatible subtype data cannot leak.
        var preservedTarget = string.IsNullOrWhiteSpace(previousType)
            ? effect.Elements().FirstOrDefault(element =>
                element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase))
            : null;
        var preservedTargetCopy = preservedTarget == null ? null : new XElement(preservedTarget);

        foreach (var attribute in effect.Attributes().Where(attribute =>
                     !attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                     !attribute.Name.LocalName.Equals("hidetooltip", StringComparison.OrdinalIgnoreCase) &&
                     !attribute.Name.LocalName.Equals("norevert", StringComparison.OrdinalIgnoreCase)).ToList())
            attribute.Remove();
        effect.RemoveNodes();
        if (preservedTargetCopy != null)
            effect.Add(preservedTargetCopy);

        var typeAttribute = Attribute(effect, "type");
        if (typeAttribute == null) effect.SetAttributeValue("type", newType);
        else typeAttribute.Value = newType;
        InitializeForType(effect, newType);
        NormalizeTargetForType(effect, newType);
    }

    public static void InitializeForType(XElement effect, string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        var normalized = type.Trim().ToLowerInvariant();
        if (normalized is "techactivate" or "updatevisuals" or "assignonhiteffect" or
            "equalizeresources" or "revealenemylos" or "sharedlos" or "cannotsnare" or
            "cantbeslowed" or "invulnerable" or "cantargetbuttakesnodamage" or
            "tacticenable" or "actionenable" or "enemyattachprotounit") return;

        if (normalized == "protoactionadd")
        {
            effect.SetAttributeValue("relativity", "Assign");
            effect.SetAttributeValue("amount", "1");
            return;
        }

        if (normalized == "armorvulnerability")
            effect.SetAttributeValue("armortype", "Hack");

        effect.SetAttributeValue("relativity",
            normalized is "minimumresourcetricklerate" or "maximumresourcetricklerate" or
                "resource" or "resourcetricklerate" or "godpowerroffactor" or "autogatherbonus"
                ? "Absolute"
                : "BasePercent");
        effect.SetAttributeValue("amount", "0");
    }

    public static IReadOnlyList<string> GetRelativityDisplays(string? type, string? current)
    {
        type ??= "";
        current ??= "";
        if (type.Equals("MinimumResourceTrickleRate", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("MaximumResourceTrickleRate", StringComparison.OrdinalIgnoreCase))
            return ["Add", "Set to"];

        var values = RelativityDisplays.Where(value =>
            !value.Equals("Override", StringComparison.Ordinal) &&
            (!type.Equals("ArmorVulnerability", StringComparison.OrdinalIgnoreCase) ||
             !value.Equals("Set to", StringComparison.Ordinal))).ToList();
        if (AllowsOverride(type) || current.Equals("Override", StringComparison.OrdinalIgnoreCase))
            values.Add("Override");
        return values;
    }

    public static bool AllowsOverride(string? type)
        => type != null && (type.Equals("BuildingWorkRate", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("WorkRate", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("WorkRateSpecific", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("WorkRateAll", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("Cost", StringComparison.OrdinalIgnoreCase));

    public static bool UsesIntegerAmount(string? type)
        => type != null && (type.Equals("MinimumResourceTrickleRate", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("MaximumResourceTrickleRate", StringComparison.OrdinalIgnoreCase));

    public static bool SupportsIgnoreNature(string? type)
        => !string.IsNullOrWhiteSpace(type) && IgnoreNatureTypes.Contains(type.Trim());

    public static bool UsesPlayerTarget(string? type)
        => !string.IsNullOrWhiteSpace(type) && PlayerTargetTypes.Contains(type.Trim());

    public static bool UsesStandardAmount(string? type)
        => string.IsNullOrWhiteSpace(type) || !AmountlessTypes.Contains(type.Trim());

    public static string RelativityToDisplay(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "percent" => "Multiply",
            "basepercent" => "Multiply base",
            "assign" => "Set to",
            "override" => "Override",
            _ => "Add"
        };

    public static string DisplayToRelativity(string? value)
        => value switch
        {
            "Multiply" => "Percent",
            "Multiply base" => "BasePercent",
            "Set to" => "Assign",
            "Override" => "Override",
            _ => "Absolute"
        };

    public static XAttribute? Attribute(XElement effect, string name)
        => effect.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static void RemoveAttribute(XElement effect, string name)
        => Attribute(effect, name)?.Remove();

    public static XElement EnsureUnitTarget(XElement effect)
        => EnsureTypedTarget(effect, "ProtoUnit", clearValueOnTypeChange: true);

    public static XElement EnsureTypedTarget(XElement effect, string targetType,
        bool clearValueOnTypeChange)
    {
        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            var type = Attribute(target, "type");
            if (type == null) target.SetAttributeValue("type", targetType);
            else if (!type.Value.Equals(targetType, StringComparison.OrdinalIgnoreCase))
            {
                type.Value = targetType;
                if (clearValueOnTypeChange) target.RemoveNodes();
            }
            return target;
        }

        target = new XElement("target", new XAttribute("type", targetType));
        AddNestedElement(effect, target);
        return target;
    }

    public static void NormalizeTargetForType(XElement effect, string? type)
    {
        type ??= "";
        var targets = effect.Elements().Where(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).ToList();
        var target = targets.FirstOrDefault();
        if (target == null)
        {
            target = new XElement("target");
            effect.Add(target);
        }
        foreach (var duplicate in targets.Skip(1)) duplicate.Remove();

        var player = UsesPlayerTarget(type);
        var tech = type.Equals("TechActivate", StringComparison.OrdinalIgnoreCase);
        var existingType = Attribute(target, "type");
        if (player || tech)
        {
            target.RemoveAttributes();
            target.RemoveNodes();
            target.SetAttributeValue("type", player ? "Player" : "Tech");
        }
        else if (existingType?.Value.Equals("Player", StringComparison.OrdinalIgnoreCase) == true)
        {
            target.RemoveAttributes();
            target.RemoveNodes();
            target.SetAttributeValue("type", "ProtoUnit");
        }
        else if (existingType == null)
            target.SetAttributeValue("type", "ProtoUnit");
        else
            existingType.Value = "ProtoUnit";
    }

    private static void AddNestedElement(XElement container, XElement element)
    {
        var preceding = container.PreviousNode as XText;
        var indentation = preceding?.Value[(preceding.Value.LastIndexOf('\n') + 1)..] ?? "    ";
        var childIndentation = indentation + (indentation.Contains('\t') ? "\t" : "  ");
        if (!container.Elements().Any())
        {
            container.RemoveNodes();
            container.Add(new XText("\n" + childIndentation), element,
                new XText("\n" + indentation));
            return;
        }
        var trailing = container.Nodes().LastOrDefault() as XText;
        if (trailing != null)
            trailing.AddBeforeSelf(new XText("\n" + childIndentation), element);
        else
            container.Add(new XText("\n" + childIndentation), element,
                new XText("\n" + indentation));
    }
}
