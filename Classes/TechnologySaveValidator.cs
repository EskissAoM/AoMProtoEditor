using System.Globalization;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record TechnologySaveCatalogs(
    IReadOnlyCollection<string> KnownDataSubtypes,
    IReadOnlyCollection<string> KnownStructuredEffectTypes,
    IReadOnlyCollection<string> TechnologyNames,
    IReadOnlyCollection<string> ProtoUnitOrTypeNames,
    IReadOnlyCollection<string> ProtoUnitNames,
    IReadOnlyCollection<string> TechTypeNames,
    IReadOnlyCollection<string> ProtoActionNames,
    IReadOnlyCollection<string> TacticNames,
    IReadOnlyCollection<string> GodPowerNames,
    IReadOnlyCollection<string> CultureNames,
    IReadOnlyCollection<string> MajorGodNames,
    IReadOnlyCollection<string> BoneNames,
    IReadOnlyCollection<string> CommandNames);

public sealed record TechnologySaveValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates editor-owned technology fields immediately before persistence.
/// Unknown effect types and unknown Data subtypes remain lossless raw XML and
/// are deliberately not interpreted here.
/// </summary>
public static class TechnologySaveValidator
{
    private static readonly HashSet<string> NumericEffectAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "amount2", "chance", "cooldown", "duration", "lifespan", "newHP",
        "progFreezeDuration", "quantity", "rank", "resourceCap", "select", "threshold",
        "timeout", "timeRatio", "value", "x", "y", "z"
    };

    private static readonly HashSet<string> IntegerEffectAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "newHP", "quantity", "rank", "resourceCap", "select"
    };

    private static readonly HashSet<string> PositiveEffectAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cooldown", "progFreezeDuration", "quantity", "resourceCap", "select"
    };

    private static readonly HashSet<string> NonNegativeEffectAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeRatio", "timeout"
    };

    private static readonly HashSet<string> TechnologyAges = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"
    };

    private static readonly HashSet<string> PrerequisiteStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Obtainable", "Unobtainable", "Active"
    };

    private static readonly HashSet<string> PrerequisiteOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "ne", "gt", "gte", "lt", "lte"
    };

    private static readonly HashSet<string> PrerequisiteStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "aliveState", "buildingState", "anyState", "nonExistState"
    };

    private static readonly HashSet<string> KbStatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "enemyMythUnitsKilled", "enemyBuildingsKilled", "unitsLost", "buildingsLost", "tradeProfit",
        "totalMinedResources", "unitsKilledCost", "buildingsKilledCost", "villagersLost", "herdablesLost",
        "totalTechCost", "costUnitsTrained", "researchCount"
    };

    private static readonly HashSet<string> KbStatsUsingResourceParameter = new(StringComparer.OrdinalIgnoreCase)
    {
        "tradeProfit", "totalTechCost", "costUnitsTrained"
    };

    private static readonly HashSet<string> PowerAttributeSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "GodPower"
    };

    private static readonly HashSet<string> ProtoPowerAttributeSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PowerCost", "PowerIconOverride", "PowerMaxUses", "PowerROF"
    };

    private static readonly HashSet<string> GoalNameSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddGoal", "AddGoalContributor", "AddGoalReward", "AddGoalRewardExclusion",
        "SetGoalActive", "SetGoalFlag", "SetGoalSpawnLocationLand", "SetGoalSpawnLocationWater"
    };

    private static readonly HashSet<string> ActionRequiredSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accuracy", "ActionEnable", "AnimationRate", "ArmorVulnerability", "BoostRadius", "Damage",
        "DamageArea", "DamageCap", "DisplayedNumberProjectiles", "EmpowerArea", "EmpowerEnable",
        "EmpowerModify", "FullCapacityMultiplier", "GatherResourceOverride", "HomingBallistics",
        "InstantBallistics", "MaximumRange", "MinimumRange", "ModifyDuration", "ModifyRate",
        "ModifyRateCap", "ModifyStackLimit", "NumberBounces", "NumberProjectiles", "OnHitEffect",
        "OnHitEffectActive", "OnHitEffectAttachBone", "OnHitEffectDuration", "OnHitEffectProbability",
        "OnHitEffectRate", "OnHitEffectStatModify", "PerfectAccuracy", "RateOfFire", "Snare",
        "StackControl", "TargetedSpeedMultiplier", "Trackrating", "VolleyMode", "WorkRateAll",
        "Yield", "YieldSpecific"
    };

    private static readonly HashSet<string> ResourceAttributeSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BuildingChainResourceFactor", "CarryCapacity", "CostBuildingAll", "CostBuildingTechs",
        "Cost", "cost", "CostBuildingUnits", "InventoryAmount", "KillReward", "MaximumResourceTrickleRate",
        "MaxResource", "MinimumResourceTrickleRate", "Resource", "ResourceByKBQuery",
        "ResourceByKBStat", "ResourceByUnitCount", "ResourceIfTechActive", "ResourceReturn",
        "ResourceReturnRate", "ResourceTrickleRate", "WorkRateSpecific", "YieldSpecific"
    };

    private static readonly IReadOnlyDictionary<string, string[]> RequiredDataAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AddGoalContributor"] = ["contributortype", "contributorid"],
            ["AddGoalReward"] = ["rewardtype"],
            ["AddGoalRewardExclusion"] = ["rewardtype"],
            ["AutoAttackType"] = ["tactic", "unitType"],
            ["BountyResourceEarningMultiplier"] = ["condition", "unittype", "resourcetype"],
            ["BountyResourceEarningReward"] = ["condition", "unittype", "resourcetype"],
            ["BuildingChainEffect"] = ["unittype", "effecttype", "modifytype"],
            ["ContainedHitpointBonusUnitType"] = ["unittype"],
            ["EmpowerArea"] = ["unittype", "empowertype"],
            ["EmpowerEnable"] = ["unittype", "empowertype"],
            ["EmpowerModify"] = ["unittype", "modifytype", "empowertype"],
            ["GarrisonBonusDamage"] = ["unittype"],
            ["GatherResourceOverride"] = ["unittype", "resource"],
            ["MovementType"] = ["movementtype"],
            ["OnHitEffect"] = ["effecttype"],
            ["OnHitEffectActive"] = ["effecttype"],
            ["OnHitEffectAttachBone"] = ["effecttype", "bone"],
            ["OnHitEffectDuration"] = ["effecttype"],
            ["OnHitEffectProbability"] = ["effecttype"],
            ["OnHitEffectRate"] = ["effecttype"],
            ["OnHitEffectStatModify"] = ["effecttype", "modifytype"],
            ["ProtoActionAdd"] = ["protoaction"],
            ["RechargeType"] = ["rechargetype"],
            ["AuxRechargeType"] = ["rechargetype"],
            ["ResourceByKBQuery"] = ["queryUnitType", "queryState"],
            ["ResourceByKBStat"] = ["kbStat"],
            ["ResourceByUnitCount"] = ["unittype"],
            ["ResourceIfTechActive"] = ["tech"],
            ["SetCivilization"] = ["civ"],
            ["SetGoalFlag"] = ["flagname"],
            ["SetGoalSpawnLocationLand"] = ["locationProtoID"],
            ["SetGoalSpawnLocationWater"] = ["locationProtoID"],
            ["SelfDestructProtoAction"] = ["protoaction"],
            ["SetUnitType"] = ["unittype"],
            ["SetVeterancyRankActive"] = ["rank"],
            ["SpeedModifier"] = ["tactic"],
            ["TacticEnable"] = ["tactic"],
            ["TimeShiftingAdd"] = ["timeRatio"],
            ["VeterancyBonus"] = ["rank", "modifytype"],
            ["VeterancyRankAdd"] = ["ranktype"],
            ["WorkRateSpecific"] = ["unittype"],
            ["YieldSpecific"] = ["unittype"]
        };

    private static readonly IReadOnlyDictionary<string, string> ProtoOnlyRequiredAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DeadTransform"] = "unittype",
            ["PartisanUnit"] = "unittype",
            ["PlacementRulesOverride"] = "unittype",
            ["Projectile"] = "unittype",
            ["ProtoActionAdd"] = "unittype",
            ["SharedBuildLimitUnit"] = "unitType",
            ["TimeShiftingAdd"] = "unittype"
        };

    public static TechnologySaveValidation Validate(XDocument document, TechnologySaveCatalogs catalogs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalogs);

        var errors = new List<string>();
        var structural = TechnologyDocumentSafety.ValidateModDocument(document);
        errors.AddRange(structural.Errors);
        if (!structural.IsValid)
            return new TechnologySaveValidation(errors);

        var knownDataSubtypes = ToSet(catalogs.KnownDataSubtypes);
        var knownStructuredTypes = ToSet(catalogs.KnownStructuredEffectTypes);
        var technologyNames = ToSet(catalogs.TechnologyNames);
        var unitOrTypeNames = ToSet(catalogs.ProtoUnitOrTypeNames);
        var protoUnitNames = ToSet(catalogs.ProtoUnitNames);
        var techTypeNames = ToSet(catalogs.TechTypeNames);
        var actionNames = ToSet(catalogs.ProtoActionNames);
        var tacticNames = ToSet(catalogs.TacticNames);
        var powerNames = ToSet(catalogs.GodPowerNames);
        var cultureNames = ToSet(catalogs.CultureNames);
        var majorGodNames = ToSet(catalogs.MajorGodNames);
        var boneNames = ToSet(catalogs.BoneNames);
        var commandNames = ToSet(catalogs.CommandNames);
        var resources = ToSet(ProtoConstants.KnownResourceTypes);

        foreach (var technology in document.Root!.Elements().Where(IsNamed("tech")))
        {
            var technologyName = AttributeValue(technology, "name");
            if (!InternalNamePolicy.IsValid(technologyName))
                errors.Add($"Technology '{technologyName}' has an invalid internal name.");

            var namespaceOwner = TechnologyStringIdPolicy.FindNamespaceCollision(
                technologyName,
                catalogs.TechnologyNames);
            if (namespaceOwner != null)
            {
                errors.Add(
                    $"Technology '{technologyName}' and '{namespaceOwner}' generate the same string ID namespace. Rename one technology before saving.");
            }

            ValidateTechnologyProperties(technology, technologyName, techTypeNames, errors);
            ValidatePrerequisites(technology, technologyName, technologyNames, unitOrTypeNames, cultureNames, majorGodNames, resources, errors);

            var effects = TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(technology);
            for (var index = 0; index < effects.Count; index++)
            {
                ValidateEffect(
                    effects[index], technologyName, index + 1, knownDataSubtypes, knownStructuredTypes,
                    technologyNames, unitOrTypeNames, protoUnitNames, techTypeNames, actionNames,
                    tacticNames, powerNames, majorGodNames, boneNames, commandNames, resources, errors);
            }
        }

        return new TechnologySaveValidation(errors.Distinct(StringComparer.Ordinal).ToList());
    }

    private static void ValidateTechnologyProperties(
        XElement technology,
        string technologyName,
        HashSet<string> techTypeNames,
        List<string> errors)
    {
        var orderHint = AttributeValue(technology, "orderhint");
        if (orderHint.Length > 0)
            ValidateNumber(orderHint, $"Technology '{technologyName}' attribute 'orderhint'", integer: false, errors);

        foreach (var element in technology.Elements())
        {
            var tag = element.Name.LocalName;
            if (tag.Equals("researchpoints", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("researchlimit", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("orderhint", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("delay", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("initialdelay", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("combatxptier", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("devotioncost", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("cost", StringComparison.OrdinalIgnoreCase))
            {
                ValidateNumber(element.Value, $"Technology '{technologyName}' property <{tag}>", integer: false, errors);
                if ((tag.Equals("researchpoints", StringComparison.OrdinalIgnoreCase) ||
                     tag.Equals("researchlimit", StringComparison.OrdinalIgnoreCase) ||
                     tag.Equals("devotioncost", StringComparison.OrdinalIgnoreCase) ||
                     tag.Equals("cost", StringComparison.OrdinalIgnoreCase)) &&
                    TryParseNumber(element.Value, out var propertyNumber) && propertyNumber < 0)
                    errors.Add($"Technology '{technologyName}' property <{tag}> cannot be negative.");
                if (tag.Equals("cost", StringComparison.OrdinalIgnoreCase))
                    ValidateRequiredCatalogAttribute(element, "resourcetype", ToSet(ProtoConstants.KnownResourceTypes), $"Technology '{technologyName}' cost", errors);
            }
            else if (tag.Equals("techtype", StringComparison.OrdinalIgnoreCase))
            {
                ValidateCatalogValue(element.Value, techTypeNames, $"Technology '{technologyName}' tech type", errors);
            }
        }
    }

    private static void ValidatePrerequisites(
        XElement technology,
        string technologyName,
        HashSet<string> technologyNames,
        HashSet<string> unitOrTypeNames,
        HashSet<string> cultureNames,
        HashSet<string> majorGodNames,
        HashSet<string> resources,
        List<string> errors)
    {
        foreach (var prerequisite in technology.Elements().Where(IsNamed("prereqs")).SelectMany(element => element.Elements()))
        {
            var context = $"Technology '{technologyName}' prerequisite <{prerequisite.Name.LocalName}>";
            if (prerequisite.Name.LocalName.Equals("techstatus", StringComparison.OrdinalIgnoreCase))
            {
                ValidateCatalogValue(prerequisite.Value, technologyNames, context, errors);
                ValidateRequiredCatalogAttribute(prerequisite, "status", PrerequisiteStatuses, context, errors);
            }
            else if (prerequisite.Name.LocalName.Equals("specificage", StringComparison.OrdinalIgnoreCase))
                ValidateCatalogValue(prerequisite.Value, TechnologyAges, context, errors);
            else if (prerequisite.Name.LocalName.Equals("typecount", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRequiredAttribute(prerequisite, "unit", context, errors);
                ValidateCatalogAttribute(prerequisite, "unit", unitOrTypeNames, context, errors);
                ValidateNumber(AttributeValue(prerequisite, "count"), context + " count", integer: false, errors);
                if (TryParseNumber(AttributeValue(prerequisite, "count"), out var count) &&
                    (count < 0 || count != decimal.Truncate(count)))
                    errors.Add($"{context} count must be a non-negative integer.");
                ValidateRequiredCatalogAttribute(prerequisite, "operator", PrerequisiteOperators, context, errors);
                ValidateRequiredCatalogAttribute(prerequisite, "state", PrerequisiteStates, context, errors);
            }
            else if (prerequisite.Name.LocalName.Equals("culture", StringComparison.OrdinalIgnoreCase))
                ValidateMultiValuePrerequisite(prerequisite, "cultureName", cultureNames, context, errors);
            else if (prerequisite.Name.LocalName.Equals("civilization", StringComparison.OrdinalIgnoreCase))
                ValidateMultiValuePrerequisite(prerequisite, "civName", majorGodNames, context, errors);
            else if (prerequisite.Name.LocalName.Equals("KBStat", StringComparison.OrdinalIgnoreCase))
            {
                var kbStat = AttributeValue(prerequisite, "kbStat");
                ValidateCatalogValue(kbStat, KbStatNames, context + " stat", errors);
                ValidateRequiredCatalogAttribute(prerequisite, "operator", PrerequisiteOperators, context, errors);
                ValidateNumber(AttributeValue(prerequisite, "value"), context + " value", integer: false, errors);
                ValidateOptionalCatalogAttribute(prerequisite, "kbParam", resources, context, errors);
                if (KbStatsUsingResourceParameter.Contains(kbStat) && AttributeValue(prerequisite, "kbParam").Length == 0)
                    errors.Add($"{context} stat '{kbStat}' requires a Resource selection.");
            }
        }
    }

    private static void ValidateMultiValuePrerequisite(
        XElement prerequisite,
        string childName,
        HashSet<string> catalog,
        string context,
        List<string> errors)
    {
        var children = prerequisite.Elements().Where(IsNamed(childName)).ToList();
        if (children.Count == 0)
        {
            ValidateCatalogValue(prerequisite.Value, catalog, context, errors);
            return;
        }

        foreach (var child in children)
            ValidateCatalogValue(child.Value, catalog, context, errors);
    }

    private static void ValidateEffect(
        XElement effect,
        string technologyName,
        int effectNumber,
        HashSet<string> knownDataSubtypes,
        HashSet<string> knownStructuredTypes,
        HashSet<string> technologyNames,
        HashSet<string> unitOrTypeNames,
        HashSet<string> protoUnitNames,
        HashSet<string> techTypeNames,
        HashSet<string> actionNames,
        HashSet<string> tacticNames,
        HashSet<string> powerNames,
        HashSet<string> majorGodNames,
        HashSet<string> boneNames,
        HashSet<string> commandNames,
        HashSet<string> resources,
        List<string> errors)
    {
        var context = $"Technology '{technologyName}', effect {effectNumber}";
        var type = AttributeValue(effect, "type");
        if (type.Length == 0)
        {
            errors.Add($"{context} has no effect type.");
            return;
        }

        var isData = type.Equals("Data", StringComparison.OrdinalIgnoreCase);
        var subtype = AttributeValue(effect, "subtype");
        var isKnownData = isData && knownDataSubtypes.Contains(subtype);
        var isKnownStructured = knownStructuredTypes.Contains(type);
        if (isData && subtype.Length == 0)
        {
            errors.Add($"{context} is Data but has no subtype.");
            return;
        }
        if (!isKnownData && !isKnownStructured)
            return;

        foreach (var attribute in effect.Attributes().Where(attribute => NumericEffectAttributes.Contains(attribute.Name.LocalName)))
        {
            var name = attribute.Name.LocalName;
            ValidateNumber(attribute.Value, $"{context} attribute '{name}'", IntegerEffectAttributes.Contains(name), errors);
            if (TryParseNumber(attribute.Value, out var number))
            {
                if (PositiveEffectAttributes.Contains(name) && number <= 0)
                    errors.Add($"{context} attribute '{name}' must be greater than 0.");
                else if (NonNegativeEffectAttributes.Contains(name) && number < 0)
                    errors.Add($"{context} attribute '{name}' cannot be negative.");
            }
        }

        if (isKnownData)
        {
            ValidateRequiredAttribute(effect, "amount", context, errors);
            ValidateRequiredAttribute(effect, "relativity", context, errors);
            var targets = effect.Elements().Where(IsNamed("target")).ToList();
            if (targets.Count != 1)
                errors.Add($"{context} ({subtype}) must contain exactly one <target>.");
            else
                ValidateTarget(targets[0], context, technologyNames, unitOrTypeNames, techTypeNames, errors);

            if (ActionRequiredSubtypes.Contains(subtype) &&
                AttributeValue(effect, "action").Length == 0 &&
                !AttributeValue(effect, "allactions").Equals("1", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{context} ({subtype}) requires an Action selection.");
            if (subtype.Equals("ActionEnable", StringComparison.OrdinalIgnoreCase) && AttributeValue(effect, "action").Length == 0)
                errors.Add($"{context} (ActionEnable) requires a concrete Action selection.");

            if (PowerAttributeSubtypes.Contains(subtype))
                ValidateRequiredCatalogAttribute(effect, "power", powerNames, context, errors);
            if (ProtoPowerAttributeSubtypes.Contains(subtype))
                ValidateRequiredCatalogAttribute(effect, "protopower", powerNames, context, errors);
            if (GoalNameSubtypes.Contains(subtype))
            {
                var goalName = AttributeValue(effect, "goalname");
                if (!InternalNamePolicy.IsValid(goalName))
                    errors.Add($"{context} ({subtype}) requires a valid Goal internal name.");
            }
            if (ResourceAttributeSubtypes.Contains(subtype))
                ValidateRequiredCatalogAttribute(effect, "resource", resources, context, errors);
            if (RequiredDataAttributes.TryGetValue(subtype, out var requiredAttributes))
            {
                foreach (var attributeName in requiredAttributes)
                    ValidateRequiredAttribute(effect, attributeName, $"{context} ({subtype})", errors);
            }
            if (ProtoOnlyRequiredAttributes.TryGetValue(subtype, out var protoOnlyAttribute))
                ValidateRequiredCatalogAttribute(effect, protoOnlyAttribute, protoUnitNames, $"{context} ({subtype})", errors);
            if ((subtype.Equals("CommandAdd", StringComparison.OrdinalIgnoreCase) ||
                 subtype.Equals("CommandRemove", StringComparison.OrdinalIgnoreCase)) &&
                AttributeValue(effect, "proto").Length == 0 &&
                AttributeValue(effect, "tech").Length == 0 &&
                AttributeValue(effect, "command").Length == 0)
                errors.Add($"{context} ({subtype}) requires a Unit, Tech, or Command selection.");

            ValidateOptionalCatalogAttribute(effect, "action", actionNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "protoaction", actionNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "tactic", tacticNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "power", powerNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "protopower", powerNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "tech", technologyNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "reqTech", technologyNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "techType", techTypeNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "civ", majorGodNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "bone", boneNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "command", commandNames, context, errors);
            ValidateOptionalCatalogAttribute(effect, "resource", resources, context, errors);
            ValidateOptionalCatalogAttribute(effect, "resourcetype", resources, context, errors);
            ValidateOptionalCatalogAttribute(effect, "resourceType", resources, context, errors);
            ValidateOptionalCatalogAttribute(effect, "kbparamresource", resources, context, errors);
            ValidateOptionalCatalogAttribute(effect, "modifytype", ToSet(ProtoConstants.KnownModifyTypes), context, errors);
            ValidateOptionalCatalogAttribute(effect, "damagetype", ToSet(ProtoConstants.KnownDamageTypes.Prepend("All")), context, errors);
            ValidateOptionalUnitAttribute(effect, "proto", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "unittype", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "unitType", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "targetType", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "queryUnitType", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "rewardType", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "attackerType", unitOrTypeNames, context, errors);
            ValidateOptionalUnitAttribute(effect, "locationProtoID", unitOrTypeNames, context, errors);

            if (subtype.StartsWith("OnHitEffect", StringComparison.OrdinalIgnoreCase))
                ValidateOptionalCatalogAttribute(effect, "effecttype", ToSet(ProtoConstants.KnownOnHitEffectTypes), context, errors);
            else if (subtype.Equals("BuildingChainEffect", StringComparison.OrdinalIgnoreCase))
                ValidateOptionalCatalogAttribute(effect, "effecttype", ToSet(["Connected", "Isolate", "InRange"]), context, errors);

            if (subtype.Equals("MovementType", StringComparison.OrdinalIgnoreCase) &&
                ProtoConstants.FieldSuggestions.TryGetValue("movementtype", out var movementTypes))
                ValidateOptionalCatalogAttribute(effect, "movementtype", ToSet(movementTypes), context, errors);
            if (subtype.Equals("VeterancyRankAdd", StringComparison.OrdinalIgnoreCase))
                ValidateOptionalCatalogAttribute(effect, "ranktype", ToSet(["Attacks", "Kills", "Damage"]), context, errors);

            if (subtype.Equals("ResourceByKBStat", StringComparison.OrdinalIgnoreCase))
            {
                var kbStat = AttributeValue(effect, "kbStat");
                ValidateCatalogValue(kbStat, KbStatNames, context + " stat", errors);
                if (KbStatsUsingResourceParameter.Contains(kbStat))
                    ValidateRequiredCatalogAttribute(effect, "kbparamresource", resources, context, errors);
            }
        }
        else
        {
            ValidateStructuredEffect(effect, type, context, technologyNames, protoUnitNames, techTypeNames, powerNames, errors);
        }
    }

    private static void ValidateStructuredEffect(
        XElement effect,
        string type,
        string context,
        HashSet<string> technologyNames,
        HashSet<string> protoUnitNames,
        HashSet<string> techTypeNames,
        HashSet<string> powerNames,
        List<string> errors)
    {
        if (type.Equals("CreatePower", StringComparison.OrdinalIgnoreCase))
            ValidateRequiredCatalogAttribute(effect, "protoPower", powerNames, context, errors);
        else if (type.Equals("ModifyProtoUnit", StringComparison.OrdinalIgnoreCase))
            ValidateRequiredCatalogAttribute(effect, "proto", protoUnitNames, context, errors);
        else if (type.Equals("TransformUnit", StringComparison.OrdinalIgnoreCase))
        {
            ValidateRequiredCatalogAttribute(effect, "fromProtoID", protoUnitNames, context, errors);
            ValidateRequiredCatalogAttribute(effect, "toProtoID", protoUnitNames, context, errors);
        }
        else if (type.Equals("ForbidTech", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("SetOnBuildingDeathTech", StringComparison.OrdinalIgnoreCase))
            ValidateCatalogValue(effect.Value, technologyNames, context, errors);
        else if (type.Equals("SetOnTechResearchedTech", StringComparison.OrdinalIgnoreCase))
        {
            ValidateRequiredCatalogAttribute(effect, "techType", techTypeNames, context, errors);
            ValidateCatalogValue(effect.Value, technologyNames, context, errors);
        }
        else if (type.Equals("RandomTech", StringComparison.OrdinalIgnoreCase))
        {
            ValidateNumber(AttributeValue(effect, "select"), context + " selection count", integer: true, errors);
            foreach (var technology in effect.Elements().Where(IsNamed("Tech")))
                ValidateCatalogValue(technology.Value, technologyNames, context + " technology", errors);
        }
    }

    private static void ValidateTarget(
        XElement target,
        string context,
        HashSet<string> technologyNames,
        HashSet<string> unitOrTypeNames,
        HashSet<string> techTypeNames,
        List<string> errors)
    {
        var type = AttributeValue(target, "type");
        if (type.Length == 0)
        {
            errors.Add($"{context} target has no type.");
            return;
        }

        if (type.Equals("Player", StringComparison.OrdinalIgnoreCase))
            return;
        if (type.Equals("techAll", StringComparison.OrdinalIgnoreCase))
            return;
        if (type.Equals("Tech", StringComparison.OrdinalIgnoreCase))
            ValidateCatalogValue(target.Value, technologyNames, context + " target", errors);
        else if (type.Equals("TechType", StringComparison.OrdinalIgnoreCase))
            ValidateCatalogValue(target.Value, techTypeNames, context + " target", errors);
        else if (type.Equals("techWithFlag", StringComparison.OrdinalIgnoreCase))
        {
            if (!InternalNamePolicy.IsValid(target.Value.Trim()))
                errors.Add($"{context} target requires a valid technology flag name.");
        }
        else if (type.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Unit", StringComparison.OrdinalIgnoreCase))
            ValidateCatalogValue(target.Value, unitOrTypeNames, context + " target", errors);
        else
            errors.Add($"{context} uses unsupported target type '{type}'.");
    }

    private static void ValidateRequiredAttribute(XElement element, string name, string context, List<string> errors)
    {
        if (AttributeValue(element, name).Length == 0)
            errors.Add($"{context} requires attribute '{name}'.");
    }

    private static void ValidateRequiredCatalogAttribute(
        XElement element,
        string name,
        HashSet<string> catalog,
        string context,
        List<string> errors)
    {
        ValidateRequiredAttribute(element, name, context, errors);
        ValidateOptionalCatalogAttribute(element, name, catalog, context, errors);
    }

    private static void ValidateCatalogAttribute(
        XElement element,
        string name,
        HashSet<string> catalog,
        string context,
        List<string> errors)
        => ValidateOptionalCatalogAttribute(element, name, catalog, context, errors);

    private static void ValidateOptionalCatalogAttribute(
        XElement element,
        string name,
        HashSet<string> catalog,
        string context,
        List<string> errors)
    {
        var value = AttributeValue(element, name);
        if (value.Length > 0)
            ValidateCatalogValue(value, catalog, $"{context} attribute '{name}'", errors);
    }

    private static void ValidateOptionalUnitAttribute(
        XElement element,
        string name,
        HashSet<string> catalog,
        string context,
        List<string> errors)
        => ValidateOptionalCatalogAttribute(element, name, catalog, context, errors);

    private static void ValidateCatalogValue(string value, HashSet<string> catalog, string context, List<string> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            errors.Add($"{context} is required.");
            return;
        }
        if (catalog.Count > 0 && !catalog.Contains(normalized))
            errors.Add($"{context} references unknown value '{normalized}'.");
    }

    private static void ValidateNumber(string value, string context, bool integer, List<string> errors)
    {
        var valid = integer
            ? long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : TryParseNumber(value, out _);
        if (!valid)
            errors.Add($"{context} must contain a valid {(integer ? "integer" : "number")}.");
    }

    private static bool TryParseNumber(string value, out decimal number)
        => decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static HashSet<string> ToSet(IEnumerable<string> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Func<XElement, bool> IsNamed(string localName)
        => element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static string AttributeValue(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
}
