using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum TechnologyEffectCatalogScope
{
    EffectType,
    DataSubtype,
    LegacyUnknown
}

public enum TechnologyEffectCatalogVisibility
{
    Creatable,
    ImportOnly,
    Deprecated
}

public enum TechnologyEffectCatalogEditorMode
{
    Structured,
    RawXml
}

public enum TechnologyEffectCatalogValidationMode
{
    Structured,
    PreserveOnly
}

public sealed record TechnologyEffectCatalogEntry(
    string Name,
    TechnologyEffectCatalogScope Scope,
    TechnologyEffectCatalogVisibility Visibility,
    TechnologyEffectCatalogEditorMode EditorMode,
    TechnologyEffectCatalogValidationMode ValidationMode,
    string LegacyRoute,
    string? AliasOf = null,
    string? Note = null);

public sealed record TechnologyEffectCatalogRuntimeSnapshot(
    IReadOnlyList<string> EffectTypes,
    IReadOnlyList<string> DataSubtypes,
    IReadOnlyCollection<string> StructuredEffectTypes);

/// <summary>
/// Authoritative technology effect catalog policy. Pass 8A audited this data
/// independently; Pass 8B made it the shared source for creation, routing, and
/// save-validation catalog membership.
/// </summary>
public static class TechnologyEffectCatalogInventory
{
    public static readonly IReadOnlyList<string> CreatableEffectTypes =
    [
        "Data", "CreatePower", "CreateUnit", "ForbidTech", "ModifyProtoUnit", "SetAge", "SetName",
        "SetOnTechResearchedTech", "SharedLOS", "TechStatus", "TextOutput", "TransformUnit",
        "AddTrickleByResource", "ConsoleCommand", "RandomTech", "ReplaceUnit", "ResourceExchange",
        "ResourceExchange2", "ResourceInventoryExchange", "SetOnBuildingDeathTech",
        "Sound", "TextEffectOutput", "TextOutputAll", "TextOutputTechName", "UIAlert"
    ];

    public static readonly IReadOnlyList<string> CreatableDataSubtypes =
    [
        "Accuracy", "ActionAddAttachingUnit", "ActionEnable", "AddAttackType", "AddDependentUnit", "AddGoal",
        "AddGoalContributor", "AddGoalReward", "AdditionalScale", "ArmorVulnerability", "AutoBuildRate",
        "AuxRechargeTime", "BountyResourceEarningMultiplier", "BountyResourceEarningReward", "BuffIconOverride",
        "BuildingChainActive", "BuildingChainEffect", "BuildingChainResourceFactor", "BuildingWorkRate", "BuildLimit",
        "BuildPoints", "CarryCapacity", "ChargedModifyAdjust", "CombatXP", "CommandAdd", "CommandRemove", "Cost",
        "cost", "CostBuildingTechs", "Damage", "DamageArea", "DamageBonus", "DamageByCost",
        "DamageFlags", "DamageShading", "DisplayedNumberProjectiles", "DisplayedRange", "DoubleEffect", "DropoffHeal",
        "EmpowerModify", "Enable", "FullCapacityMultiplier", "GathererLimit", "GodPower",
        "GodPowerBlockRadius", "GodPowerCost", "GodPowerCostFactor", "GodPowerROF", "GodPowerROFFactor",
        "Hitpoints", "HomingBallistics", "InitialResource", "InventoryAmount", "Lifespan", "LOS", "Market",
        "MaximumContained", "MaximumRange", "MaximumVelocity", "MinWorkRate", "ModifyDuration", "ModifyRate",
        "ModifyRateCap", "ModifyReplacement", "ModifySpawn", "ModifyStackLimit", "NumberBounces",
        "NumberProjectiles", "ObstructionRadiusX", "ObstructionRadiusZ", "OnDamageModify", "OnDeathCombatXP",
        "OnHitEffect", "OnHitEffectActive", "OnHitEffectAttachBone", "OnHitEffectDuration",
        "OnHitEffectProbability", "OnHitEffectRate", "OnHitEffectStatModify", "PopulationCap",
        "PopulationCapAddition", "PopulationCount", "PowerCost", "PowerIconOverride", "PowerMaxUses", "PowerROF",
        "Projectile", "ProtoActionAdd", "ProtoUnitFlag", "RateOfFire", "RechargeInit", "RechargeTime",
        "RechargeType", "RepairCostFactor", "ResearchPoints", "ResearchRate", "Resource", "ResourceByKBStat",
        "ResourceReturn", "ResourceReturnRate", "ResourceTrickleRate", "RespawnTrainActive", "RevealAllyUI",
        "RevealEnemyUI", "SelfDestructProtoAction", "SetAge", "SetGoalFlag", "SetGoalSpawnLocationLand",
        "SetGoalSpawnLocationWater", "SetUnitType", "ShieldPoints", "StealthDetectionRadius",
        "TargetedSpeedMultiplier", "TimeShiftingConcurrentShifts", "TimeShiftingCost", "TimeShiftingTimeRatio",
        "Trackrating", "TrainingRate", "TrainPoints", "TributePenalty", "UnitRegenCombatMultiplier",
        "UnitRegenRate", "VeterancyEnable", "WanderDistance", "WorkRate", "WorkRateSpecific",
        "AddContainedType", "AddGoalRewardExclusion", "AddNotContainedType", "AddSharedBuildLimitUnitType",
        "AddVeterancyExcludeType", "AddVeterancyIncludeType", "AnimationRate",
        "AutoAttackType", "AutoGatherBonus", "AuxRechargeInit", "AuxRechargeType",
        "BoostRadius", "ContainedHitpointBonus", "ContainedHitpointBonusUnitType",
        "CostBuildingAll", "CostBuildingUnits", "DamageCap", "DamageForAllHandLogicActions",
        "DamageForAllRangedLogicActions", "DeadTransform", "DeadTransformBuildLimit", "DodgeChance", "EmpowerArea",
        "EmpowerEnable", "EnableDodge", "EnableSharedBuildLimit", "FakeConversion",
        "FreeRepair", "GarrisonBonusDamage", "GatherRateMultiplier", "GatherResourceOverride",
        "InitialVeterancyRank", "InstantBallistics",
        "KillReward", "MarketReset", "MaximumResourceTrickleRate", "MaxResource", "MinimumRange",
        "MinimumResourceTrickleRate", "MovementType", "PartisanUnit", "PerfectAccuracy", "PlacementRulesOverride",
        "PopulationLimit", "ProtoActionFlag", "ResourceByKBQuery", "ResourceByUnitCount", "ResourceIfTechActive",
        "ResourceReturnRateTotalCost", "RevealLOS", "SetCivilization", "SetGoalActive",
        "SetNextResearchFree", "SetVeterancyRankActive", "SharedBuildLimitUnit", "Snare",
        "SpeedModifier", "StackControl", "TacticEnable", "TechCostAbsolute", "TimeShiftingAdd",
        "TurnRate", "UnitRegenRateLimit",
        "UnitShieldRegenDamageTimeout", "UnitShieldRegenIdleTimeout", "UnitShieldRegenRate",
        "UnitShieldRegenRateLimit", "UpdateVisual", "VeterancyBonus", "VeterancyRankAdd",
        "VolleyMode", "WorkRateAll", "Yield", "YieldSpecific"
    ];

    public static readonly IReadOnlyList<string> DeprecatedNames =
    [
        "ShowWorldView", "AddTrain", "Armor", "ArmorSpecific", "BlockTrainCount",
        "FreeBuildPoints", "FreeBuildRate", "InvestmentAmount", "InvestmentCap",
        "InvestmentEnable", "Scale", "SendRandomCard", "SetProtoMaxArmor",
        "TradeRouteBonus", "TradeRouteBonusTeam", "UpgradeLevel"
    ];

    public static readonly IReadOnlyList<TechnologyEffectCatalogEntry> Entries = BuildEntries();

    public static readonly IReadOnlySet<string> StructuredEffectTypes = Entries
        .Where(entry => entry.Scope == TechnologyEffectCatalogScope.EffectType &&
                        entry.EditorMode == TechnologyEffectCatalogEditorMode.Structured &&
                        !entry.Name.Equals("Data", StringComparison.OrdinalIgnoreCase))
        .Select(entry => entry.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> StructuredDataSubtypes = Entries
        .Where(entry => entry.Scope == TechnologyEffectCatalogScope.DataSubtype &&
                        entry.EditorMode == TechnologyEffectCatalogEditorMode.Structured)
        .Select(entry => entry.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsStructuredEffectType(string? effectType)
        => !string.IsNullOrWhiteSpace(effectType) && StructuredEffectTypes.Contains(effectType);

    public static bool IsStructuredDataSubtype(string? subtype)
        => !string.IsNullOrWhiteSpace(subtype) && StructuredDataSubtypes.Contains(subtype);

    public static TechnologyEffectCatalogRuntimeSnapshot CreateSnapshot()
        => new(
            CreatableEffectTypes.ToArray(),
            CreatableDataSubtypes.ToArray(),
            StructuredEffectTypes.ToArray());

    private static IReadOnlyList<TechnologyEffectCatalogEntry> BuildEntries()
    {
        var entries = new List<TechnologyEffectCatalogEntry>();
        entries.AddRange(CreatableEffectTypes.Select(name => new TechnologyEffectCatalogEntry(
            name,
            TechnologyEffectCatalogScope.EffectType,
            TechnologyEffectCatalogVisibility.Creatable,
            TechnologyEffectCatalogEditorMode.Structured,
            TechnologyEffectCatalogValidationMode.Structured,
            name.Equals("Data", StringComparison.Ordinal) ? "Data subtype dispatcher" : $"Effect/{name}")));

        entries.AddRange(CreatableDataSubtypes.Select(name => new TechnologyEffectCatalogEntry(
            name,
            TechnologyEffectCatalogScope.DataSubtype,
            TechnologyEffectCatalogVisibility.Creatable,
            TechnologyEffectCatalogEditorMode.Structured,
            TechnologyEffectCatalogValidationMode.Structured,
            $"Data/{name}",
            name.Equals("cost", StringComparison.Ordinal) ? "Cost" : null,
            name.Equals("cost", StringComparison.Ordinal) ? "Legacy case alias retained by the current autocomplete." : null)));

        entries.Add(new TechnologyEffectCatalogEntry(
            "Flag", TechnologyEffectCatalogScope.DataSubtype, TechnologyEffectCatalogVisibility.ImportOnly,
            TechnologyEffectCatalogEditorMode.Structured, TechnologyEffectCatalogValidationMode.PreserveOnly,
            "Data/ProtoUnitFlag", Note: "Intentional XML-only exception; never offer it for creation."));
        entries.Add(new TechnologyEffectCatalogEntry(
            "ActionAdd", TechnologyEffectCatalogScope.DataSubtype, TechnologyEffectCatalogVisibility.ImportOnly,
            TechnologyEffectCatalogEditorMode.RawXml, TechnologyEffectCatalogValidationMode.PreserveOnly,
            "Raw XML", Note: "Not implemented; preserve imported XML only."));
        entries.Add(new TechnologyEffectCatalogEntry(
            "SquareAura", TechnologyEffectCatalogScope.DataSubtype, TechnologyEffectCatalogVisibility.ImportOnly,
            TechnologyEffectCatalogEditorMode.RawXml, TechnologyEffectCatalogValidationMode.PreserveOnly,
            "Raw XML", Note: "Not implemented; preserve imported XML only."));

        entries.AddRange(DeprecatedNames.Select(name => new TechnologyEffectCatalogEntry(
            name,
            TechnologyEffectCatalogScope.LegacyUnknown,
            TechnologyEffectCatalogVisibility.Deprecated,
            TechnologyEffectCatalogEditorMode.RawXml,
            TechnologyEffectCatalogValidationMode.PreserveOnly,
            "Raw XML",
            Note: "Hidden from creation catalogs; imported XML remains losslessly preserved.")));
        return entries;
    }
}

public sealed record TechnologyEffectCatalogOccurrence(
    TechnologyEffectCatalogScope Scope,
    string Name,
    int Count);

public sealed record TechnologyEffectCatalogAuditResult(
    IReadOnlyList<string> MissingEffectTypes,
    IReadOnlyList<string> ExtraEffectTypes,
    IReadOnlyList<string> MissingDataSubtypes,
    IReadOnlyList<string> ExtraDataSubtypes,
    IReadOnlyList<string> MissingStructuredEffectTypes,
    IReadOnlyList<string> ExtraStructuredEffectTypes,
    IReadOnlyList<string> CaseAliases,
    IReadOnlyList<TechnologyEffectCatalogOccurrence> UnclassifiedCorpusEntries,
    IReadOnlyList<TechnologyEffectCatalogOccurrence> ImportOnlyCorpusEntries,
    IReadOnlyList<TechnologyEffectCatalogOccurrence> DeprecatedCorpusEntries)
{
    public bool RuntimeCatalogsMatch => MissingEffectTypes.Count == 0 && ExtraEffectTypes.Count == 0 &&
                                        MissingDataSubtypes.Count == 0 && ExtraDataSubtypes.Count == 0 &&
                                        MissingStructuredEffectTypes.Count == 0 && ExtraStructuredEffectTypes.Count == 0;
}

public static class TechnologyEffectCatalogAudit
{
    public static IReadOnlyList<TechnologyEffectCatalogOccurrence> CountCorpus(IEnumerable<XDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var occurrences = new Dictionary<(TechnologyEffectCatalogScope Scope, string Name), int>(
            TechnologyEffectOccurrenceKeyComparer.Instance);
        foreach (var effect in documents.SelectMany(document => document.Descendants()
                     .Where(element => element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase))))
        {
            var type = AttributeValue(effect, "type");
            if (string.IsNullOrWhiteSpace(type))
                continue;
            Add(TechnologyEffectCatalogScope.EffectType, type);
            if (type.Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                var subtype = AttributeValue(effect, "subtype");
                if (!string.IsNullOrWhiteSpace(subtype))
                    Add(TechnologyEffectCatalogScope.DataSubtype, subtype);
            }
        }

        return occurrences
            .Select(item => new TechnologyEffectCatalogOccurrence(item.Key.Scope, item.Key.Name, item.Value))
            .OrderBy(item => item.Scope)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Add(TechnologyEffectCatalogScope scope, string name)
        {
            var key = (scope, name.Trim());
            occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
        }
    }

    public static TechnologyEffectCatalogAuditResult Compare(
        TechnologyEffectCatalogRuntimeSnapshot runtime,
        IEnumerable<TechnologyEffectCatalogOccurrence>? corpus = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var entries = TechnologyEffectCatalogInventory.Entries;
        var expectedEffects = TechnologyEffectCatalogInventory.CreatableEffectTypes;
        var expectedData = TechnologyEffectCatalogInventory.CreatableDataSubtypes;
        var expectedStructuredEffects = expectedEffects.Where(name =>
            !name.Equals("Data", StringComparison.OrdinalIgnoreCase)).ToList();

        var occurrences = corpus?.Where(item => item.Count > 0).ToList() ?? [];
        var importOnly = entries.Where(entry => entry.Visibility == TechnologyEffectCatalogVisibility.ImportOnly).ToList();
        var deprecated = entries.Where(entry => entry.Visibility == TechnologyEffectCatalogVisibility.Deprecated).ToList();

        return new TechnologyEffectCatalogAuditResult(
            Except(expectedEffects, runtime.EffectTypes),
            Except(runtime.EffectTypes, expectedEffects),
            ExceptExact(expectedData, runtime.DataSubtypes),
            ExceptExact(runtime.DataSubtypes, expectedData),
            Except(expectedStructuredEffects, runtime.StructuredEffectTypes),
            Except(runtime.StructuredEffectTypes, expectedStructuredEffects),
            runtime.DataSubtypes.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(name => name).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(group => string.Join(" / ", group))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            occurrences.Where(item => !IsClassified(item, entries)).ToList(),
            occurrences.Where(item => importOnly.Any(entry => Matches(item, entry))).ToList(),
            occurrences.Where(item => deprecated.Any(entry => Matches(item, entry))).ToList());
    }

    private static IReadOnlyList<string> Except(IEnumerable<string> left, IEnumerable<string> right)
        => left.Except(right, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToList();

    private static IReadOnlyList<string> ExceptExact(IEnumerable<string> left, IEnumerable<string> right)
        => left.Except(right, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();

    private static bool IsClassified(TechnologyEffectCatalogOccurrence occurrence,
        IEnumerable<TechnologyEffectCatalogEntry> entries)
        => entries.Any(entry => Matches(occurrence, entry));

    private static bool Matches(TechnologyEffectCatalogOccurrence occurrence, TechnologyEffectCatalogEntry entry)
        => occurrence.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
           (entry.Scope == TechnologyEffectCatalogScope.LegacyUnknown || entry.Scope == occurrence.Scope);

    private static string? AttributeValue(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private sealed class TechnologyEffectOccurrenceKeyComparer : IEqualityComparer<(TechnologyEffectCatalogScope Scope, string Name)>
    {
        public static readonly TechnologyEffectOccurrenceKeyComparer Instance = new();

        public bool Equals((TechnologyEffectCatalogScope Scope, string Name) x,
            (TechnologyEffectCatalogScope Scope, string Name) y)
            => x.Scope == y.Scope && x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((TechnologyEffectCatalogScope Scope, string Name) obj)
            => HashCode.Combine(obj.Scope, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
