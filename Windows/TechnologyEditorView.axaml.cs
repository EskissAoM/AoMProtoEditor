using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView : UserControl
{
    public event EventHandler? BrowserStateChanged;
    public event EventHandler? DirtyStateChanged;

    private static readonly string[] DevotionTypes =
    [
        "Livestock",
        "LogicalTypeValidCosmicGuardSacrifice",
        "Villager",
        "WarriorPriest"
    ];

    private static readonly string[] TechnologyStringBackedTags =
    [
        "displaynameid",
        "rollovertextid",
        "advancedrollovertextoverrideid"
    ];

    private static readonly string[] CommonOptionalTechnologyTags =
    [
        "advancedrollovertextoverrideid",
        "researchlimit",
        "valuetext",
        "delay",
        "initialdelay",
        "techage",
        "combatxptier"
    ];

    private static readonly string[] TechnologyAges =
    [
        "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"
    ];

    private static readonly string[] PrerequisiteTypes =
    [
        "TechStatus", "SpecificAge", "TypeCount", "Culture", "Civilization", "KBStat"
    ];

    private static readonly string[] TechnologyEffectTypes =
    [
        "Data", "CreatePower", "CreateUnit", "ForbidTech", "ModifyProtoUnit", "SetAge", "SetName",
        "SetOnTechResearchedTech", "SharedLOS", "TechStatus", "TextOutput", "TransformUnit",
        "AddTrickleByResource", "ConsoleCommand", "RandomTech", "ReplaceUnit", "ResourceExchange",
        "ResourceExchange2", "ResourceInventoryExchange", "SetOnBuildingDeathTech",
        "Sound", "TextEffectOutput", "TextOutputAll", "TextOutputTechName", "UIAlert"
    ];

    private static readonly HashSet<string> StructuredTechnologyEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetName", "Sound", "TextOutput", "TextOutputAll", "TextOutputTechName", "SetAge", "TechStatus",
        "SharedLOS", "ModifyProtoUnit", "TransformUnit", "ResourceExchange", "SetOnBuildingDeathTech",
        "ConsoleCommand", "CreatePower", "RandomTech", "TextEffectOutput", "ResourceInventoryExchange",
        "AddTrickleByResource", "ResourceExchange2", "ReplaceUnit", "ForbidTech", "SetOnTechResearchedTech", "UIAlert", "CreateUnit"
    };

    private static readonly HashSet<string> SimpleUnitAmountDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AdditionalScale", "AutoBuildRate", "AuxRechargeTime", "BuildingWorkRate", "BuildLimit",
        "BuildPoints", "DisplayedRange", "DropoffHeal", "GathererLimit", "GodPowerBlockRadius",
        "Hitpoints", "InitialResource", "LOS", "MaximumContained", "MaximumVelocity",
        "ObstructionRadiusX", "ObstructionRadiusZ", "OnDeathCombatXP", "PopulationCapAddition",
        "PopulationCount", "RechargeTime", "ResearchRate", "ShieldPoints", "StealthDetectionRadius", "DodgeChance",
        "TrainingRate", "TrainPoints", "UnitRegenCombatMultiplier", "UnitRegenRate", "WanderDistance",
        "ContainedHitpointBonus", "GatherRateMultiplier", "TurnRate",
        "UnitShieldRegenDamageTimeout", "UnitShieldRegenIdleTimeout", "UnitShieldRegenRateLimit", "UnitShieldRegenRate"
    };

    private static readonly HashSet<string> ActionUnitAmountDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accuracy", "DamageArea", "DisplayedNumberProjectiles", "FullCapacityMultiplier",
        "MaximumRange", "MinimumRange", "ModifyDuration", "ModifyRate", "ModifyRateCap", "ModifyStackLimit",
        "NumberBounces", "NumberProjectiles", "RateOfFire", "TargetedSpeedMultiplier", "Trackrating", "DamageCap", "AnimationRate"
    };

    private static readonly HashSet<string> DamageTypeActionDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Damage"
    };

    private static readonly HashSet<string> ArmorTypeActionDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArmorVulnerability"
    };

    private static readonly HashSet<string> DamageTypeNoActionDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DamageForAllHandLogicActions", "DamageForAllRangedLogicActions"
    };

    private static readonly HashSet<string> DamageBonusDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DamageBonus", "Damagebonus"
    };

    private static readonly HashSet<string> ResourceAmountDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CarryCapacity", "InventoryAmount", "CostBuildingTechs", "ResourceReturn", "ResourceReturnRate",
        "CostBuildingAll", "CostBuildingUnits", "KillReward"
    };

    private static readonly HashSet<string> PlayerResourceDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Resource"
    };

    private static readonly HashSet<string> ActionAddAttachingUnitDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActionAddAttachingUnit"
    };

    private static readonly HashSet<string> AddAttackTypeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddAttackType"
    };

    private static readonly HashSet<string> AddDependentUnitDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddDependentUnit"
    };

    private static readonly HashSet<string> EnableDisableUnitDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuxRechargeInit", "RechargeInit", "RespawnTrainActive", "VeterancyEnable",
        "EnableDodge", "EnableSharedBuildLimit", "DeadTransformBuildLimit", "FreeRepair",
        "SetNextResearchFree", "FakeConversion", "ResourceReturnRateTotalCost"
    };

    private static readonly HashSet<string> EnableDisableActionUnitDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HomingBallistics", "InstantBallistics", "PerfectAccuracy", "VolleyMode", "Snare"
    };

    private static readonly HashSet<string> MovementTypeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MovementType"
    };

    private static readonly HashSet<string> RevealLosDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RevealLOS"
    };

    private static readonly HashSet<string> ChargedModifyAdjustDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChargedModifyAdjust"
    };

    private static readonly HashSet<string> CommandDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CommandAdd", "CommandRemove"
    };

    private static readonly HashSet<string> DamageByCostDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DamageByCost"
    };

    private static readonly HashSet<string> DamageFlagsDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DamageFlags"
    };

    private static readonly HashSet<string> DamageShadingDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DamageShading"
    };

    private static readonly HashSet<string> ProtoUnitFlagDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProtoUnitFlag", "Flag"
    };

    private static readonly HashSet<string> ProtoActionFlagDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProtoActionFlag"
    };

    private static readonly HashSet<string> LifespanDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lifespan"
    };

    private static readonly HashSet<string> MinWorkRateDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MinWorkRate", "WorkRate"
    };

    private static readonly HashSet<string> ContainingUnitAmountDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContainedHitpointBonusUnitType", "GarrisonBonusDamage"
    };

    private static readonly HashSet<string> ModifyReplacementDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModifyReplacement"
    };

    private static readonly HashSet<string> ModifySpawnDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModifySpawn"
    };

    private static readonly HashSet<string> OnDamageModifyDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OnDamageModify"
    };

    private static readonly HashSet<string> OnHitEffectDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OnHitEffect"
    };

    private static readonly HashSet<string> OnHitEffectAttributeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OnHitEffectActive", "OnHitEffectAttachBone", "OnHitEffectDuration",
        "OnHitEffectProbability", "OnHitEffectRate", "OnHitEffectStatModify"
    };

    private static readonly HashSet<string> ProjectileDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Projectile"
    };

    private static readonly HashSet<string> RechargeTypeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RechargeType", "AuxRechargeType"
    };

    private static readonly HashSet<string> SelfDestructProtoActionDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SelfDestructProtoAction"
    };

    private static readonly HashSet<string> SetUnitTypeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetUnitType"
    };

    private static readonly HashSet<string> ContainedTypeDataSubtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddContainedType", "AddNotContainedType", "AddSharedBuildLimitUnitType",
        "AddVeterancyExcludeType", "AddVeterancyIncludeType"
    };

    private static readonly string[] TechnologyDataEffectSubtypes =
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
        "UnitRegenRate", "VeterancyEnable", "WanderDistance", "WorkRate", "WorkRateSpecific", "ActionAdd",
        "AddContainedType", "AddGoalRewardExclusion", "AddNotContainedType", "AddSharedBuildLimitUnitType",
        "AddTrain", "AddVeterancyExcludeType", "AddVeterancyIncludeType", "AnimationRate",
        "AutoAttackType", "AutoGatherBonus", "AuxRechargeInit", "AuxRechargeType",
        "BlockTrainCount", "BoostRadius", "ContainedHitpointBonus", "ContainedHitpointBonusUnitType",
        "CostBuildingAll", "CostBuildingUnits", "DamageCap", "DamageForAllHandLogicActions",
        "DamageForAllRangedLogicActions", "DeadTransform", "DeadTransformBuildLimit", "DodgeChance", "EmpowerArea",
        "EmpowerEnable", "EnableDodge", "EnableSharedBuildLimit", "FakeConversion", "FreeBuildPoints",
        "FreeBuildRate", "FreeRepair", "GarrisonBonusDamage", "GatherRateMultiplier", "GatherResourceOverride",
        "InitialVeterancyRank", "InstantBallistics",
        "KillReward", "MarketReset", "MaximumResourceTrickleRate", "MaxResource", "MinimumRange",
        "MinimumResourceTrickleRate", "MovementType", "PartisanUnit", "PerfectAccuracy", "PlacementRulesOverride",
        "PopulationLimit", "ProtoActionFlag", "ResourceByKBQuery", "ResourceByUnitCount", "ResourceIfTechActive",
        "ResourceReturnRateTotalCost", "RevealLOS", "Scale", "SendRandomCard", "SetCivilization", "SetGoalActive",
        "SetNextResearchFree", "SetVeterancyRankActive", "SharedBuildLimitUnit", "Snare",
        "SpeedModifier", "SquareAura", "StackControl", "TacticEnable", "TechCostAbsolute", "TimeShiftingAdd",
        "TurnRate", "UnitRegenRateLimit",
        "UnitShieldRegenDamageTimeout", "UnitShieldRegenIdleTimeout", "UnitShieldRegenRate",
        "UnitShieldRegenRateLimit", "UpdateVisual", "UpgradeLevel", "VeterancyBonus", "VeterancyRankAdd",
        "VolleyMode", "WorkRateAll", "Yield", "YieldSpecific"
    ];

    private static readonly string[] KbStatNames =
    [
        "enemyMythUnitsKilled",
        "enemyBuildingsKilled",
        "unitsLost",
        "buildingsLost",
        "tradeProfit",
        "totalMinedResources",
        "unitsKilledCost",
        "buildingsKilledCost",
        "villagersLost",
        "herdablesLost",
        "totalTechCost",
        "costUnitsTrained",
        "researchCount"
    ];

    private static readonly HashSet<string> KbStatsUsingResourceParameter = new(StringComparer.OrdinalIgnoreCase)
    {
        "tradeProfit", "totalTechCost", "costUnitsTrained"
    };

    private readonly IReadOnlyList<XDocument> _originalBarDocuments = [];
    private readonly string? _baseGameplayDirectory;
    private readonly string? _modTechtreePath;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync;
    private readonly IconPreviewService? _iconPreviewService;
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingStringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<string> _iconPaths = [];
    private readonly IReadOnlyList<string> _prereqUnitNames = [];
    private readonly IReadOnlyList<string> _protoUnitNames = [];
    private readonly IReadOnlyList<string> _cultureNames = [];
    private readonly IReadOnlyList<string> _majorGodNames = [];
    private readonly IReadOnlyList<string> _techTypeNames = [];
    private readonly IReadOnlyList<string> _protoActionNames = [];
    private readonly IReadOnlyList<string> _tacticNames = [];
    private readonly IReadOnlyList<string> _protoUnitCommandNames = [];
    private readonly IReadOnlyList<string> _boneNames = [];
    private readonly IReadOnlyList<string> _godPowerNames = [];
    private readonly Dictionary<string, XElement> _original = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XElement> _modified = new(StringComparer.OrdinalIgnoreCase);
    private XDocument _modDocument = new(new XElement("techtreemods"));
    private XElement? _current;
    private string? _currentOriginalName;
    private bool _loadingUi;
    private int _editorBuildGeneration;
    private readonly SemaphoreSlim _editorBuildGate = new(1, 1);
    private readonly SemaphoreSlim _technologyNameCommitGate = new(1, 1);
    private bool _dirty;
    private readonly HashSet<string> _dirtyTechnologyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(XElement Effect, string Attribute)> _openOnHitOptionalSelectors = [];
    private bool _controlsReady;
    private bool _isXmlPreviewCollapsed;
    private IconPreviewControl? _iconPreviewControl;

    public TechnologyEditorView()
    {
        InitializeComponent();
        _controlsReady = true;
        XmlSyntaxEditorService.Configure(_xmlPreview);
    }

    public TechnologyEditorView(
        IEnumerable<XDocument>? originalBarDocuments,
        string? baseGameplayDirectory,
        string? modTechtreePath,
        Func<string, Task<string?>>? resolveStringAsync = null,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
        IEnumerable<string>? iconPaths = null,
        IEnumerable<string>? prereqUnitNames = null,
        IEnumerable<string>? protoUnitNames = null,
        IEnumerable<string>? cultureNames = null,
        IEnumerable<string>? majorGodNames = null,
        IEnumerable<string>? techTypeNames = null,
        IEnumerable<string>? protoActionNames = null,
        IEnumerable<string>? tacticNames = null,
        IEnumerable<string>? protoUnitCommandNames = null,
        IEnumerable<string>? boneNames = null,
        IconPreviewService? iconPreviewService = null,
        IEnumerable<string>? godPowerNames = null)
        : this()
    {
        _originalBarDocuments = originalBarDocuments?.ToList() ?? [];
        _baseGameplayDirectory = baseGameplayDirectory;
        _modTechtreePath = modTechtreePath;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _iconPreviewService = iconPreviewService;
        _iconPaths = iconPaths?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _prereqUnitNames = prereqUnitNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _protoUnitNames = protoUnitNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _cultureNames = cultureNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _majorGodNames = majorGodNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _techTypeNames = techTypeNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _protoActionNames = protoActionNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _tacticNames = tacticNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _protoUnitCommandNames = protoUnitCommandNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _boneNames = boneNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _godPowerNames = godPowerNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _techTabs.SelectedIndex = 0;
        LoadAll();
        RefreshList();
    }

    private bool IsModifiedTab => _techTabs.SelectedIndex == 1;

    public bool IsModifiedMode => IsModifiedTab;

    public string? CurrentTechnologyName => _currentOriginalName;

    public bool IsTechnologyDirty(string technologyName)
        => _dirtyTechnologyNames.Contains(technologyName);

    public XElement? LoadSavedTechnologyElement(string technologyName)
    {
        if (string.IsNullOrWhiteSpace(_modTechtreePath) || !File.Exists(_modTechtreePath))
            return null;

        try
        {
            var document = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace);
            var technology = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("name"), technologyName, StringComparison.OrdinalIgnoreCase));
            return technology == null ? null : new XElement(technology);
        }
        catch
        {
            return null;
        }
    }

    public void DiscardTechnologyChanges(string technologyName, XElement? savedElement)
    {
        var current = _modified.GetValueOrDefault(technologyName);
        var restored = savedElement == null ? null : new XElement(savedElement);
        var savedName = ((string?)restored?.Attribute("name"))?.Trim();
        if (string.IsNullOrWhiteSpace(savedName))
            savedName = technologyName;

        var affectedStringIds = CollectReferencedTechnologyStringIds(current);
        affectedStringIds.UnionWith(CollectReferencedTechnologyStringIds(restored));
        var prefixes = new[] { technologyName, savedName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => BuildTechnologyStringPrefix(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        affectedStringIds.UnionWith(_pendingStringUpdates.Keys.Where(id =>
            prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))));
        affectedStringIds.UnionWith(_pendingStringRemovals.Where(id =>
            prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))));

        if (current != null)
        {
            if (restored != null)
                current.ReplaceWith(restored);
            else
                current.Remove();
        }
        else if (restored != null)
        {
            _modDocument.Root?.Add(restored);
        }

        _modified.Remove(technologyName);
        if (restored != null)
            _modified[savedName] = restored;

        foreach (var id in affectedStringIds)
        {
            if (IsTechnologyStringIdOwnedByAnotherDirtyDocument(id, technologyName, savedName))
                continue;
            _pendingStringUpdates.Remove(id);
            _pendingStringRemovals.Remove(id);
        }
        _dirtyTechnologyNames.Remove(technologyName);
        _dirtyTechnologyNames.Remove(savedName);
        _dirty = _dirtyTechnologyNames.Count > 0 || _pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0;

        if (string.Equals(_currentOriginalName, technologyName, StringComparison.OrdinalIgnoreCase))
            ClearEditor();
        RefreshList();
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsTechnologyStringIdOwnedByAnotherDirtyDocument(
        string stringId,
        string discardedName,
        string restoredName)
    {
        foreach (var dirtyName in _dirtyTechnologyNames)
        {
            if (dirtyName.Equals(discardedName, StringComparison.OrdinalIgnoreCase) ||
                dirtyName.Equals(restoredName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_modified.TryGetValue(dirtyName, out var technology))
                continue;

            if (CollectReferencedTechnologyStringIds(technology).Contains(stringId) ||
                stringId.StartsWith(BuildTechnologyStringPrefix(dirtyName), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static HashSet<string> CollectReferencedTechnologyStringIds(XElement? technology)
    {
        if (technology == null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return technology.DescendantsAndSelf()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value).Append(element.Value))
            .Where(value => value.StartsWith("STR_", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildTechnologyStringPrefix(string technologyName)
    {
        var displayNameId = BuildTechnologyStringId(technologyName, "displaynameid");
        return displayNameId.EndsWith("_NAME", StringComparison.Ordinal)
            ? displayNameId[..^5] + "_"
            : displayNameId + "_";
    }

    public IReadOnlyList<string> GetTechnologyNames(bool modified)
    {
        var source = modified ? _modified : _original;
        return source.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetModifiedMode(bool modified)
    {
        var selectedIndex = modified ? 1 : 0;
        if (_techTabs.SelectedIndex != selectedIndex)
            _techTabs.SelectedIndex = selectedIndex;
    }

    public void SelectTechnology(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _techList.SelectedItem = null;
            ClearEditor();
            return;
        }

        var source = IsModifiedTab ? _modified : _original;
        _techList.SelectedItem = source.Keys.FirstOrDefault(candidate =>
            candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadAll()
    {
        LoadOriginalFromLooseFiles();
        LoadOriginalFromBarDocuments();
        LoadModified();
    }

    private void LoadOriginalFromLooseFiles()
    {
        if (string.IsNullOrWhiteSpace(_baseGameplayDirectory)) return;
        foreach (var name in new[] { "techtree.xml", "aotg_techtree.techtree" })
        {
            var path = Path.Combine(_baseGameplayDirectory, name);
            if (!File.Exists(path)) continue;
            try { MergeTechs(XDocument.Load(path, LoadOptions.PreserveWhitespace), _original, overwrite: false); } catch { }
        }
    }

    private void LoadOriginalFromBarDocuments()
    {
        foreach (var document in _originalBarDocuments)
            MergeTechs(document, _original, overwrite: false);
    }

    private void LoadModified()
    {
        if (!string.IsNullOrWhiteSpace(_modTechtreePath) && File.Exists(_modTechtreePath))
        {
            try { _modDocument = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace); }
            catch { _modDocument = new XDocument(new XElement("techtreemods")); }
        }
        else
        {
            _modDocument = new XDocument(new XElement("techtreemods"));
        }
        if (_modDocument.Root == null) _modDocument.Add(new XElement("techtreemods"));
        MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
    }

    private static void MergeTechs(XDocument doc, Dictionary<string, XElement> destination, bool overwrite, bool clone = true)
    {
        foreach (var tech in doc.Descendants().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)))
        {
            var name = (string?)tech.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (overwrite || !destination.ContainsKey(name)) destination[name] = clone ? new XElement(tech) : tech;
        }
    }

    private void RefreshList(string? select = null)
    {
        var source = IsModifiedTab ? _modified : _original;
        var query = (_searchBox.Text ?? "").Trim();
        var names = source.Keys.Where(n => query.Length == 0 || n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _techList.ItemsSource = names;
        if (!string.IsNullOrWhiteSpace(select)) _techList.SelectedItem = names.FirstOrDefault(n => n.Equals(select, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_controlsReady) return;
        RefreshList();
    }

    private void TechTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_controlsReady) return;
        ClearEditor();
        RefreshList();
    }

    private void TechList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_controlsReady) return;
        if (_techList.SelectedItem is not string name) { ClearEditor(); return; }
        var source = IsModifiedTab ? _modified : _original;
        if (!source.TryGetValue(name, out var tech)) { ClearEditor(); return; }
        _current = tech;
        _currentOriginalName = name;
        _ = BuildEditorAsync();
    }

    private void ClearEditor()
    {
        _editorBuildGeneration++;
        _loadingUi = true;
        _current = null;
        _currentOriginalName = null;
        _iconPreviewControl = null;
        _techNameBox.Text = "";
        _techNameBox.IsReadOnly = true;
        _techNameBox.IsEnabled = false;
        _propertiesPanel.Children.Clear();
        _prereqsPanel.Children.Clear();
        _effectsPanel.Children.Clear();
        _xmlPreview.Text = "";
        _loadingUi = false;
    }

    private async Task BuildEditorAsync()
    {
        var generation = ++_editorBuildGeneration;
        await _editorBuildGate.WaitAsync();
        try
        {
            if (_current == null || generation != _editorBuildGeneration) return;
            var tech = _current;
            _loadingUi = true;
            _iconPreviewControl = null;
            _propertiesPanel.Children.Clear();
            _prereqsPanel.Children.Clear();
            _effectsPanel.Children.Clear();
            _techNameBox.Text = (string?)tech.Attribute("name") ?? "";
            _techNameBox.IsReadOnly = !IsModifiedTab;
            _techNameBox.IsEnabled = IsModifiedTab;

            AddSectionHeader("Properties");
            await AddKnownPropertyEditorsAsync(tech);
            if (!IsEditorBuildCurrent(tech, generation)) return;

            foreach (var attr in tech.Attributes().Where(a =>
                     !a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                     !a.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                     !a.Name.LocalName.Equals("orderhint", StringComparison.OrdinalIgnoreCase)).ToList())
            AddTextPropertyRow(HumanizeLabel(attr.Name.LocalName), attr.Value, v => attr.Value = v);

            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
            "displaynameid", "rollovertextid", "advancedrollovertextoverrideid",
            "cost", "researchpoints", "status", "devotioncost", "techtype", "icon", "flag", "effects", "prereqs"
            };

            foreach (var child in tech.Elements().Where(e => !e.HasElements && !handled.Contains(e.Name.LocalName)).ToList())
            {
            if (!IsModifiedTab && string.IsNullOrWhiteSpace(child.Value) && !child.HasAttributes)
                continue;

            if (child.Name.LocalName.Equals("techage", StringComparison.OrdinalIgnoreCase))
            {
                AddTechAgeEditor(child);
                continue;
            }

            string suffix = string.Join(", ", child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
            string label = HumanizeLabel(child.Name.LocalName) + (suffix.Length > 0 ? $" [{suffix}]" : "");
            AddTextPropertyRow(label, child.Value, v => child.Value = v, IsModifiedTab ? () => RemoveOptionalElement(child) : null);
            }

            if (IsModifiedTab)
                AddOtherAttributesSelector(tech, handled);

            AddCostsEditor(tech);
            AddChipListEditor(tech, "techtype", "Technology Types");
            AddChipListEditor(tech, "flag", "Flags");

            AddPrerequisiteHeader(tech);
            AddPrerequisiteButton(tech);
            var prereqsContainer = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase));
            var prereqs = prereqsContainer?.Elements().ToList() ?? [];
            if (prereqs.Count == 0 && !IsModifiedTab)
                _prereqsPanel.Children.Add(new TextBlock { Text = "No prerequisites.", Foreground = Brushes.Gray });
            foreach (var prereq in prereqs) AddPrereqEditor(tech, prereq);

            AddEffectsHeader();
            var effectsContainer = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
            var effects = effectsContainer?.Elements().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList()
                          ?? tech.Elements().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!IsModifiedTab && effects.Count == 0)
                _effectsPanel.Children.Add(new TextBlock { Text = "No effects.", Foreground = Brushes.Gray });
            foreach (var effect in effects)
            {
                await AddEffectEditorAsync(effect);
                if (!IsEditorBuildCurrent(tech, generation)) return;
            }
            if (IsModifiedTab) AddEffectButton(tech);

            ApplyReadOnlyVisualState();
            UpdatePreview();
        }
        finally
        {
            if (generation == _editorBuildGeneration)
                _loadingUi = false;
            _editorBuildGate.Release();
        }
    }

    private bool IsEditorBuildCurrent(XElement technology, int generation)
        => generation == _editorBuildGeneration && ReferenceEquals(_current, technology);

    private async Task AddKnownPropertyEditorsAsync(XElement tech)
    {
        XElement? FindVisible(string tag)
        {
            var element = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (element == null) return null;
            if (!IsModifiedTab && string.IsNullOrWhiteSpace(element.Value) && !element.HasAttributes) return null;
            return element;
        }

        var identityLayout = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var identityFields = new StackPanel { Spacing = 0 };
        identityLayout.Children.Add(identityFields);
        _iconPreviewControl = new IconPreviewControl(_iconPreviewService)
        {
            Margin = new Thickness(IconPreviewControl.PropertyGridLeftOffset, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        identityLayout.Children.Add(_iconPreviewControl);
        RefreshIconPreview();
        _propertiesPanel.Children.Add(identityLayout);

        var displayName = FindVisible("displaynameid");
        await AddPrimaryTechnologyRowAsync(tech, displayName, identityFields);

        var rollover = FindVisible("rollovertextid");
        if (rollover != null)
            await AddStringBackedPropertyRowAsync("Rollover text", rollover, multiline: true, target: identityFields);

        var advancedRollover = FindVisible("advancedrollovertextoverrideid");
        if (advancedRollover != null)
            await AddStringBackedPropertyRowAsync("Advanced rollover", advancedRollover, multiline: true, removable: IsModifiedTab, target: identityFields);

        var icon = FindVisible("icon");
        if (icon != null)
            AddIconEditor(icon, identityFields);

        var status = FindVisible("status");
        if (status != null)
            AddStatusEditor(status, identityFields);

        void RefreshIconPreview()
        {
            if (_iconPreviewControl == null)
                return;
            _ = _iconPreviewControl.ShowOptionsAsync(
                tech.Elements()
                    .Where(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                    .Select(element => (
                        element.Value,
                        (string?)element.Attribute("culture"))));
        }

        var researchPoints = FindVisible("researchpoints");
        var devotionCost = FindVisible("devotioncost");
        if (researchPoints != null)
            AddResearchPointsEditor(tech, researchPoints, devotionCost);
        else if (devotionCost != null)
            AddDevotionCostEditor(devotionCost);
    }

    private async Task<string> ResolveTechnologyStringValueAsync(string stringId)
    {
        if (_pendingStringUpdates.TryGetValue(stringId, out var pendingText))
            return pendingText;
        if (_resolveStringAsync != null && !string.IsNullOrWhiteSpace(stringId))
            return await _resolveStringAsync(stringId) ?? stringId;
        return stringId;
    }

    private async Task AddPrimaryTechnologyRowAsync(XElement tech, XElement? displayNameElement, Panel target)
    {
        var hasIcon = tech.Elements()
            .Any(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
        var typeAttribute = tech.Attribute("type");
        if (!IsModifiedTab && string.IsNullOrWhiteSpace(typeAttribute?.Value)) typeAttribute = null;
        var orderHintAttribute = tech.Attribute("orderhint");
        if (!IsModifiedTab && string.IsNullOrWhiteSpace(orderHintAttribute?.Value)) orderHintAttribute = null;
        if (!IsModifiedTab && displayNameElement == null && typeAttribute == null &&
            orderHintAttribute == null && !hasIcon)
            return;

        var displayGrid = CreatePropertyGrid(displayNameElement != null ? "Display name" : "");
        if (displayGrid.Children.OfType<TextBlock>().FirstOrDefault() is { } primaryLabel)
        {
            primaryLabel.VerticalAlignment = VerticalAlignment.Center;
            primaryLabel.Margin = new Thickness(0, 4, 6, 4);
        }
        var displayRow = new WrapPanel { Orientation = Orientation.Horizontal };

        if (displayNameElement != null)
        {
            var stringId = displayNameElement.Value.Trim();
            var text = await ResolveTechnologyStringValueAsync(stringId);
            var displayBox = EditorTextFieldStyle.ConfigureTextBox(new TextBox
            {
                Text = text,
                IsEnabled = IsModifiedTab,
                Margin = new Thickness(0, 4, 8, 4)
            });
            displayBox.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(stringId)) return;
                _pendingStringRemovals.Remove(stringId);
                _pendingStringUpdates[stringId] = displayBox.Text ?? "";
                MarkDirty();
            };
            displayRow.Children.Add(displayBox);
            Grid.SetColumn(displayRow, 1);
            displayGrid.Children.Add(displayRow);
            target.Children.Add(displayGrid);
        }

        var metadataGrid = CreatePropertyGrid("");
        var metadataRow = new WrapPanel { Orientation = Orientation.Horizontal };

        void AddTypeEditor()
        {
            var segment = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            segment.Children.Add(new TextBlock
            {
                Text = "Type",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            });

            var currentType = ((string?)tech.Attribute("type") ?? "").Trim();
            var typeOptions = _original.Values.Concat(_modified.Values)
                .Select(t => ((string?)t.Attribute("type") ?? "").Trim())
                .Where(v => v.Length > 0)
                .Append("Normal")
                .Append(currentType)
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var typeSelector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
            {
                Text = currentType,
                FilterMode = AutoCompleteFilterMode.Contains,
                IsEnabled = IsModifiedTab,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 4)
            });
            typeSelector.Width = 130;
            typeSelector.MaxWidth = 130;
            EditorAutoCompleteService.ConfigureStrict(
                typeSelector,
                typeOptions,
                currentType,
                () => _loadingUi,
                preserveUnknownInitialValue: true,
                allowEmpty: true,
                commitEmptyAsValid: true,
                valueCommitted: value =>
                {
                    if (_loadingUi || !IsModifiedTab) return;
                    if (string.IsNullOrWhiteSpace(value)) tech.Attribute("type")?.Remove();
                    else tech.SetAttributeValue("type", value);
                    MarkDirty();
                    UpdatePreview();
                });
            segment.Children.Add(typeSelector);
            if (IsModifiedTab)
                segment.Children.Add(CreateRemoveButton(() => tech.Attribute("type")?.Remove()));
            metadataRow.Children.Add(segment);
        }

        void AddOrderHintEditor()
        {
            var segment = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            segment.Children.Add(new TextBlock
            {
                Text = "Order hint",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            });
            var orderBox = CreateNumericTextBox(tech.Attribute("orderhint")?.Value ?? "", 50);
            orderBox.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                var value = (orderBox.Text ?? "").Trim();
                if (value.Length == 0) tech.Attribute("orderhint")?.Remove();
                else tech.SetAttributeValue("orderhint", value);
                MarkDirty();
                UpdatePreview();
            };
            segment.Children.Add(orderBox);
            if (IsModifiedTab)
                segment.Children.Add(CreateRemoveButton(() => tech.Attribute("orderhint")?.Remove()));
            metadataRow.Children.Add(segment);
        }

        if (typeAttribute != null)
            AddTypeEditor();
        if (orderHintAttribute != null)
            AddOrderHintEditor();

        if (IsModifiedTab)
        {
            if (typeAttribute == null)
            {
                var addTypeButton = CreateOptionalPropertyButton("Type");
                addTypeButton.Click += (_, _) =>
                {
                    if (_loadingUi || tech.Attribute("type") != null) return;
                    tech.SetAttributeValue("type", "Normal");
                    MarkDirty();
                    _ = BuildEditorAsync();
                };
                metadataRow.Children.Add(addTypeButton);
            }
            if (orderHintAttribute == null)
            {
                var addOrderButton = CreateOptionalPropertyButton("Order hint");
                addOrderButton.Click += (_, _) =>
                {
                    if (_loadingUi || tech.Attribute("orderhint") != null) return;
                    tech.SetAttributeValue("orderhint", "0");
                    MarkDirty();
                    _ = BuildEditorAsync();
                };
                metadataRow.Children.Add(addOrderButton);
            }
        }

        if (metadataRow.Children.Count > 0)
        {
            Grid.SetColumn(metadataRow, 1);
            metadataGrid.Children.Add(metadataRow);
            target.Children.Add(metadataGrid);
        }
    }

    private static Button CreateOptionalPropertyButton(string label)
        => new()
        {
            Content = label,
            Classes = { "add-component" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4),
            Padding = new Thickness(8, 3)
        };

    private Button CreateRemoveButton(Action removeAction)
    {
        var button = new Button
        {
            Classes = { "remove-button" },
            Margin = new Thickness(2, 0, 0, 0)
        };
        button.Click += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            removeAction();
            MarkDirty();
            _ = BuildEditorAsync();
        };
        return button;
    }

    private void RemoveOptionalElement(XElement element)
    {
        if (!IsModifiedTab) return;
        var technology = element.Parent;
        var removeUsesValueText = element.Name.LocalName.Equals("valuetext", StringComparison.OrdinalIgnoreCase);
        if (TechnologyStringBackedTags.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            var id = element.Value.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                _pendingStringUpdates.Remove(id);
                _pendingStringRemovals.Add(id);
            }
        }
        element.Remove();
        if (removeUsesValueText && technology != null &&
            !technology.Elements().Any(e => e.Name.LocalName.Equals("valuetext", StringComparison.OrdinalIgnoreCase)))
            RemoveTechnologyFlag(technology, "UsesValueText");
    }

    private static void EnsureTechnologyFlag(XElement technology, string flagName)
    {
        if (technology.Elements().Any(e =>
                e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                e.Value.Trim().Equals(flagName, StringComparison.OrdinalIgnoreCase)))
            return;
        InsertBeforeEffectsOrAppend(technology, new XElement("flag", flagName));
    }

    private static void RemoveTechnologyFlag(XElement technology, string flagName)
    {
        foreach (var flag in technology.Elements().Where(e =>
                     e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                     e.Value.Trim().Equals(flagName, StringComparison.OrdinalIgnoreCase)).ToList())
            flag.Remove();
    }


    private void AddResearchPointsEditor(XElement tech, XElement researchPoints, XElement? devotionCost)
    {
        var grid = CreatePropertyGrid("Research points");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var box = CreateNumericTextBox(researchPoints.Value);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            researchPoints.Value = box.Text ?? "";
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(box);

        if (IsModifiedTab && devotionCost == null)
        {
            var button = CreateOptionalPropertyButton("Devotion cost");
            button.Margin = new Thickness(8, 4, 0, 4);
            button.Click += (_, _) =>
            {
                if (_loadingUi || tech.Elements().Any(e => e.Name.LocalName.Equals("devotioncost", StringComparison.OrdinalIgnoreCase))) return;
                var added = new XElement("devotioncost", new XAttribute("devotiontype", DevotionTypes[0]), "0");
                InsertBeforeEffectsOrAppend(tech, added);
                MarkDirty();
                _ = BuildEditorAsync();
            };
            row.Children.Add(button);
        }

        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);

        if (devotionCost != null)
            AddDevotionCostEditor(devotionCost);
    }

    private void AddDevotionCostEditor(XElement devotionCost)
    {
        var grid = CreatePropertyGrid("Devotion cost");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };

        var currentType = ((string?)devotionCost.Attribute("devotiontype") ?? "").Trim();
        var typeSelector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
        {
            Text = currentType,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedTab,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        });
        typeSelector.Width = 200;
        typeSelector.MaxWidth = 200;
        EditorAutoCompleteService.ConfigureStrict(
            typeSelector,
            DevotionTypes,
            currentType,
            () => _loadingUi,
            preserveUnknownInitialValue: true,
            allowEmpty: false,
            valueCommitted: value =>
            {
                if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(value)) return;
                devotionCost.SetAttributeValue("devotiontype", value);
                MarkDirty();
                UpdatePreview();
            });
        row.Children.Add(typeSelector);

        row.Children.Add(new TextBlock
        {
            Text = "Number",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        });
        var numberBox = CreateNumericTextBox(devotionCost.Value);
        EditorNumericInputBehavior.AttachRule(numberBox, ProtoUnitNumericKind.UnsignedInteger);
        numberBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            devotionCost.Value = numberBox.Text ?? "";
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(numberBox);
        if (IsModifiedTab)
            row.Children.Add(CreateRemoveButton(() => RemoveOptionalElement(devotionCost)));

        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);
    }

    private async Task AddStringBackedPropertyRowAsync(
        string label,
        XElement element,
        bool multiline = false,
        bool removable = false,
        Panel? target = null)
    {
        var stringId = element.Value.Trim();
        var text = await ResolveTechnologyStringValueAsync(stringId);

        var grid = CreatePropertyGrid(label);
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (multiline)
        {
            box.MinHeight = 32;
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
        }
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        if (removable && IsModifiedTab)
            row.Children.Add(CreateRemoveButton(() => RemoveOptionalElement(element)));
        Grid.SetColumn(row, 1);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(stringId)) return;
            _pendingStringRemovals.Remove(stringId);
            _pendingStringUpdates[stringId] = box.Text ?? "";
            MarkDirty();
        };
        grid.Children.Add(row);
        (target ?? _propertiesPanel).Children.Add(grid);
    }

    private TextBox CreateNumericTextBox(string value, double? width = null)
    {
        var box = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox
        {
            Text = value,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (width.HasValue)
        {
            box.Width = width.Value;
            box.MaxWidth = width.Value;
        }
        return box;
    }

    private void AddCompactNumericPropertyRow(string label, string value, Action<string> setter, double? width = null)
    {
        var grid = CreatePropertyGrid(label);
        var box = CreateNumericTextBox(value, width);
        Grid.SetColumn(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            setter(box.Text ?? "");
            MarkDirty();
            UpdatePreview();
        };
        grid.Children.Add(box);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddOtherAttributesSelector(XElement tech, IReadOnlySet<string> handled)
    {
        var observedSimpleTags = _original.Values.Concat(_modified.Values)
            .SelectMany(t => t.Elements())
            .Where(e => !e.HasElements && !e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name.LocalName);
        var candidates = CommonOptionalTechnologyTags.Concat(observedSimpleTags)
            .Where(tag => !handled.Contains(tag) || tag.Equals("advancedrollovertextoverrideid", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("displaynameid", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("rollovertextid", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("icon", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("status", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("researchpoints", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("devotioncost", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("techtype", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tag.Equals("flag", StringComparison.OrdinalIgnoreCase))
            .Where(tag => !tech.Elements().Any(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(HumanizeLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
            return;

        var host = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 4) };
        var button = CreateOptionalPropertyButton("Other attributes");
        host.Children.Add(button);
        button.Click += (_, _) =>
        {
            if (host.Children.Count > 1) return;
            var byLabel = candidates.ToDictionary(HumanizeLabel, tag => tag, StringComparer.OrdinalIgnoreCase);
            var labels = byLabel.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var picker = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
            {
                ItemsSource = labels,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 260,
                MaxWidth = 260
            });
            EditorAutoCompleteService.EnableDropdown(picker, () => _loadingUi, selectAllOnFirstClick: false);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string label || !byLabel.TryGetValue(label, out var tag)) return;
                XElement element;
                if (tag.Equals("advancedrollovertextoverrideid", StringComparison.OrdinalIgnoreCase))
                {
                    var techName = ((string?)tech.Attribute("name") ?? "Technology").Trim();
                    var id = BuildTechnologyStringId(techName, tag);
                    element = new XElement(tag, id);
                    _pendingStringRemovals.Remove(id);
                    _pendingStringUpdates[id] = "";
                }
                else
                {
                    var numericOptional = tag.Equals("researchlimit", StringComparison.OrdinalIgnoreCase) ||
                                          tag.Equals("delay", StringComparison.OrdinalIgnoreCase) ||
                                          tag.Equals("initialdelay", StringComparison.OrdinalIgnoreCase) ||
                                          tag.Equals("combatxptier", StringComparison.OrdinalIgnoreCase);
                    element = new XElement(tag, tag.Equals("techage", StringComparison.OrdinalIgnoreCase)
                        ? TechnologyAges[0]
                        : numericOptional ? "0" : "");
                }
                InsertBeforeEffectsOrAppend(tech, element);
                if (tag.Equals("valuetext", StringComparison.OrdinalIgnoreCase))
                    EnsureTechnologyFlag(tech, "UsesValueText");
                MarkDirty();
                _ = BuildEditorAsync();
            };
            host.Children.Add(picker);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                picker.Focus();
                picker.IsDropDownOpen = true;
            });
        };
        _propertiesPanel.Children.Add(host);
    }

    private void AddCostsEditor(XElement tech)
    {
        AddSectionHeader("Costs");
        var costs = tech.Elements().Where(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Attribute("resourcetype") != null)
            .GroupBy(e => (string?)e.Attribute("resourcetype") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var index = Array.IndexOf(ProtoConstants.KnownResourceTypes, resource);
            var label = new TextBlock
            {
                Text = resource,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            Grid.SetColumn(label, index * 2);
            grid.Children.Add(label);

            costs.TryGetValue(resource, out var existing);
            var box = CreateNumericTextBox(existing?.Value ?? "0");
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
            box.Margin = new Thickness(0, 4, index < ProtoConstants.KnownResourceTypes.Length - 1 ? 8 : 0, 4);
            Grid.SetColumn(box, index * 2 + 1);
            box.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                if (existing == null)
                {
                    existing = new XElement("cost", new XAttribute("resourcetype", resource), box.Text ?? "0");
                    InsertBeforeEffectsOrAppend(tech, existing);
                }
                else
                {
                    existing.Value = box.Text ?? "0";
                }
                MarkDirty();
                UpdatePreview();
            };
            grid.Children.Add(box);
        }
        _propertiesPanel.Children.Add(grid);
    }

    private void AddStatusEditor(XElement status, Panel? target = null)
    {
        var grid = CreatePropertyGrid("Status");
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Unobtainable", "Active" },
            SelectedItem = ToStatusDisplay(status.Value),
            IsEnabled = IsModifiedTab,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetColumn(combo, 1);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string value) return;
            status.Value = value.ToUpperInvariant();
            MarkDirty();
            UpdatePreview();
        };
        grid.Children.Add(combo);
        (target ?? _propertiesPanel).Children.Add(grid);
    }

    private static string ToStatusDisplay(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "OBTAINABLE" => "Obtainable",
            "ACTIVE" => "Active",
            _ => "Unobtainable"
        };

    private void AddIconEditor(XElement icon, Panel? target = null)
    {
        var grid = CreatePropertyGrid("Icon");
        var initial = ProtoEditorWindow.NormalizeIconCatalogValue(icon.Value, _iconPaths);
        var editor = new AssetPathEditor
        {
            IsEnabled = IsModifiedTab,
            Opacity = IsModifiedTab ? 1.0 : 0.55,
            Margin = new Thickness(0, 4, 0, 4)
        };
        editor.CompactPresenter.Background = Brush.Parse(IsModifiedTab ? "#0E1110" : "#202220");
        editor.Configure(initial, _iconPaths, async value =>
        {
            if (!IsModifiedTab) return;
            icon.Value = value;
            MarkDirty();
            UpdatePreview();
            await Task.CompletedTask;
        });
        editor.FullValueChanged += (_, _) =>
        {
            if (_iconPreviewControl != null)
                _ = _iconPreviewControl.ShowOptionsAsync(
                    icon.Parent?.Elements()
                        .Where(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                        .Select(element => (element.Value, (string?)element.Attribute("culture")))
                    ?? []);
        };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        (target ?? _propertiesPanel).Children.Add(grid);
    }

    private void AddChipListEditor(XElement tech, string tag, string sectionTitle)
    {
        var currentElements = tech.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!IsModifiedTab && currentElements.Count == 0)
            return;

        AddSectionHeader(sectionTitle);
        var content = new StackPanel { Spacing = 4 };
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

        var knownValues = (tag.Equals("techtype", StringComparison.OrdinalIgnoreCase) && _techTypeNames.Count > 0
                ? _techTypeNames
                : _original.Values.Concat(_modified.Values)
                    .SelectMany(t => t.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AutoCompleteBox? picker = null;
        void RefreshPickerItems()
        {
            if (picker == null) return;
            var present = tech.Elements()
                .Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            picker.ItemsSource = knownValues.Where(value => !present.Contains(value)).ToList();
        }

        void Render()
        {
            chips.Children.Clear();
            foreach (var element in tech.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var captured = element;
                chips.Children.Add(EditorChipService.CreateBlueChip(
                    captured.Value.Trim(),
                    IsModifiedTab ? () =>
                    {
                        captured.Remove();
                        MarkDirty();
                        Render();
                        RefreshPickerItems();
                        UpdatePreview();
                    } : null,
                    readOnly: !IsModifiedTab));
            }
        }

        if (IsModifiedTab)
        {
            picker = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            picker.Width = 200;
            picker.MaxWidth = 200;
            RefreshPickerItems();
            EditorAutoCompleteService.EnableDropdown(picker, () => _loadingUi, selectAllOnFirstClick: false);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string value || string.IsNullOrWhiteSpace(value)) return;
                if (!tech.Elements().Any(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) && e.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    var element = new XElement(tag, value);
                    InsertBeforeEffectsOrAppend(tech, element);
                    MarkDirty();
                    Render();
                    UpdatePreview();
                }
                picker.SelectedItem = null;
                picker.Text = "";
                RefreshPickerItems();
            };
            content.Children.Add(picker);
        }

        Render();
        RefreshPickerItems();
        content.Children.Add(chips);
        _propertiesPanel.Children.Add(content);
    }

    private void AddTextPropertyRow(string label, string value, Action<string> setter, Action? removeAction = null)
    {
        var grid = CreatePropertyGrid(label);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        TextBox box;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            box = CreateNumericTextBox(value);
        }
        else
        {
            box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
            {
                Text = value,
                IsEnabled = IsModifiedTab,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            setter(box.Text ?? "");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(box);
        if (removeAction != null && IsModifiedTab)
            row.Children.Add(CreateRemoveButton(removeAction));
        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddTechAgeEditor(XElement techAge)
    {
        var grid = CreatePropertyGrid("Tech Age");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox
        {
            ItemsSource = TechnologyAges,
            SelectedItem = TechnologyAges.FirstOrDefault(age => age.Equals(techAge.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? techAge.Value.Trim(),
            IsEnabled = IsModifiedTab,
            Width = 150,
            MaxWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string age) return;
            techAge.Value = age;
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(combo);
        if (IsModifiedTab)
            row.Children.Add(CreateRemoveButton(() => RemoveOptionalElement(techAge)));
        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);
    }

    private static Grid CreatePropertyGrid(string label)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 6, 4) });
        return grid;
    }

    private void AddSectionHeader(string text)
    {
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = $"──── {text} ────",
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#C59A52"),
            Margin = new Thickness(0, 15, 0, 5)
        });
    }

    private void ApplyReadOnlyVisualState()
    {
        var canEdit = IsModifiedTab;
        // Keep the preview's culture-cycle button interactive for original technologies.
        // Every actual property editor still applies its own modified-tab guard.
        _propertiesPanel.IsEnabled = _current != null;
        _prereqsPanel.IsEnabled = canEdit;
        _effectsPanel.IsEnabled = canEdit;
        _propertiesPanel.Opacity = canEdit ? 1.0 : 0.55;
        _prereqsPanel.Opacity = canEdit ? 1.0 : 0.55;
        _effectsPanel.Opacity = canEdit ? 1.0 : 0.55;
        _xmlPreview.IsEnabled = _current != null;
        XmlSyntaxEditorService.SetReadOnly(_xmlPreview, isReadOnly: true);
        _xmlPreview.Focusable = _current != null;
        _xmlPreview.IsTabStop = false;
        _xmlPreview.Opacity = _current != null ? 1.0 : 0.55;
        _xmlPreview.Background = Brush.Parse(canEdit ? "#090C0B" : "#090C0B");
        _xmlPreview.Foreground = Brush.Parse(_current != null ? "#E8DECC" : "#8a8a8a");
    }

    private static void InsertBeforeEffectsOrAppend(XElement tech, XElement element)
    {
        var effects = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
        if (effects != null) effects.AddBeforeSelf(element);
        else tech.Add(element);
    }

    private void AddPrerequisiteHeader(XElement tech)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = "──── Prerequisites ────",
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#C59A52"),
            Margin = new Thickness(0, 15, 0, 8)
        });

        _prereqsPanel.Children.Add(header);
    }

    private void AddPrerequisiteButton(XElement tech)
    {
        if (!IsModifiedTab) return;

        var add = CreateOptionalPropertyButton("Add prerequisite");
        add.Margin = new Thickness(0, 0, 0, 6);
        add.Click += (_, _) =>
        {
            var container = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase));
            if (container == null)
            {
                container = new XElement("prereqs");
                var effects = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
                if (effects != null) effects.AddBeforeSelf(container);
                else tech.Add(container);
            }

            container.Add(CreateDefaultPrerequisite("TechStatus"));
            MarkDirty();
            _ = BuildEditorAsync();
        };
        _prereqsPanel.Children.Add(add);
    }

    private void AddPrereqEditor(XElement tech, XElement prereq)
    {
        var border = new Border
        {
            BorderBrush = Brush.Parse("#4C4031"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var content = new StackPanel { Spacing = 6 };
        shell.Children.Add(content);
        border.Child = shell;

        var firstRow = new WrapPanel { Orientation = Orientation.Horizontal };

        var currentType = GetPrereqTypeName(prereq);
        var typeCombo = new ComboBox
        {
            ItemsSource = PrerequisiteTypes,
            SelectedItem = PrerequisiteTypes.FirstOrDefault(x => x.Equals(currentType, StringComparison.OrdinalIgnoreCase)) ?? currentType,
            IsEnabled = IsModifiedTab,
            Width = 150,
            MaxWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || typeCombo.SelectedItem is not string selectedType ||
                selectedType.Equals(GetPrereqTypeName(prereq), StringComparison.OrdinalIgnoreCase)) return;
            ReplacePrerequisiteType(prereq, selectedType);
            MarkDirty();
            _ = BuildEditorAsync();
        };
        firstRow.Children.Add(typeCombo);

        AddPrerequisiteFields(prereq, currentType, firstRow, content);

        if (IsModifiedTab)
        {
            var removePrereq = CreateRemoveButton(() =>
            {
                var parent = prereq.Parent;
                prereq.Remove();
                if (parent != null && !parent.Elements().Any()) parent.Remove();
            });
            removePrereq.HorizontalAlignment = HorizontalAlignment.Right;
            removePrereq.VerticalAlignment = VerticalAlignment.Top;
            removePrereq.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(removePrereq, 1);
            shell.Children.Add(removePrereq);
        }

        content.Children.Insert(0, firstRow);
        _prereqsPanel.Children.Add(border);
    }

    private void AddPrerequisiteFields(XElement prereq, string type, WrapPanel firstRow, StackPanel content)
    {
        switch (type.ToLowerInvariant())
        {
            case "techstatus":
                AddTechStatusPrerequisiteFields(prereq, firstRow);
                break;
            case "specificage":
                AddSpecificAgePrerequisiteFields(prereq, firstRow);
                break;
            case "typecount":
                AddTypeCountPrerequisiteFields(prereq, firstRow);
                break;
            case "culture":
                AddMultiValuePrerequisiteFields(prereq, content, "cultureName", _cultureNames, "Culture");
                break;
            case "civilization":
                AddMultiValuePrerequisiteFields(prereq, content, "civName", _majorGodNames, "Major god");
                break;
            case "kbstat":
                AddKbStatPrerequisiteFields(prereq, firstRow);
                break;
        }
    }

    private void AddTechStatusPrerequisiteFields(XElement prereq, WrapPanel row)
    {
        row.Children.Add(CreateInlineLabel("Technology"));
        row.Children.Add(CreateStrictPrereqSelector(
            _original.Keys.Concat(_modified.Keys),
            prereq.Value.Trim(),
            200,
            value =>
            {
                prereq.Value = value;
                MarkDirty();
                UpdatePreview();
            }));

        row.Children.Add(CreateInlineLabel("Status"));
        var status = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Unobtainable", "Active" },
            SelectedItem = ToStatusDisplay((string?)prereq.Attribute("status") ?? "UNOBTAINABLE"),
            IsEnabled = IsModifiedTab,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        status.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || status.SelectedItem is not string value) return;
            prereq.SetAttributeValue("status", value);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(status);
    }

    private void AddSpecificAgePrerequisiteFields(XElement prereq, WrapPanel row)
    {
        var age = new ComboBox
        {
            ItemsSource = TechnologyAges,
            SelectedItem = TechnologyAges.FirstOrDefault(x => x.Equals(prereq.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? prereq.Value.Trim(),
            IsEnabled = IsModifiedTab,
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        age.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || age.SelectedItem is not string value) return;
            prereq.Value = value;
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(age);
    }

    private void AddTypeCountPrerequisiteFields(XElement prereq, WrapPanel row)
    {
        row.Children.Add(CreateInlineLabel("Unit"));
        row.Children.Add(CreateStrictPrereqSelector(
            _prereqUnitNames,
            (string?)prereq.Attribute("unit") ?? "",
            150,
            value => SetPrereqAttribute(prereq, "unit", value)));

        row.Children.Add(CreateInlineLabel("Count"));
        row.Children.Add(CreateOperatorCombo(prereq));

        var count = CreateNumericTextBox(FormatNumericForDisplay((string?)prereq.Attribute("count") ?? "0"), 70);
        EditorNumericInputBehavior.AttachRule(count, ProtoUnitNumericKind.UnsignedInteger);
        count.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            var raw = (count.Text ?? "0").Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) && parsedCount >= 0)
                SetPrereqAttribute(prereq, "count", parsedCount.ToString("0.00", CultureInfo.InvariantCulture), rebuild: false);
        };
        row.Children.Add(count);

        row.Children.Add(CreateInlineLabel("is"));
        var states = new[] { "alive", "building", "any", "nonexistent" };
        var state = new ComboBox
        {
            ItemsSource = states,
            SelectedItem = ToPrereqStateDisplay((string?)prereq.Attribute("state") ?? "aliveState"),
            IsEnabled = IsModifiedTab,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        state.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || state.SelectedItem is not string selected) return;
            SetPrereqAttribute(prereq, "state", FromPrereqStateDisplay(selected));
        };
        row.Children.Add(state);
    }

    private void AddKbStatPrerequisiteFields(XElement prereq, WrapPanel row)
    {
        var statAttribute = prereq.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("kbStat", StringComparison.OrdinalIgnoreCase));
        var currentStat = statAttribute?.Value ?? "";
        var currentUsesResource = KbStatsUsingResourceParameter.Contains(currentStat);
        var statSelector = CreateStrictPrereqSelector(
            KbStatNames,
            currentStat,
            150,
            value =>
            {
                var newUsesResource = KbStatsUsingResourceParameter.Contains(value);
                SetCaseInsensitiveAttribute(prereq, "kbStat", value);
                if (!newUsesResource)
                    RemoveCaseInsensitiveAttribute(prereq, "kbParam");
                MarkDirty();
                UpdatePreview();
                if (currentUsesResource != newUsesResource)
                    _ = BuildEditorAsync();
            },
            deferSelectionCommit: true);
        row.Children.Add(statSelector);

        if (KbStatsUsingResourceParameter.Contains(currentStat))
        {
            row.Children.Add(CreateInlineLabel("Resource"));
            var param = prereq.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("kbParam", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            var resource = new ComboBox
            {
                ItemsSource = ProtoConstants.KnownResourceTypes,
                SelectedItem = ProtoConstants.KnownResourceTypes.FirstOrDefault(x => x.Equals(param, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = IsModifiedTab,
                Width = 100,
                MaxWidth = 100,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 8, 4)
            };
            resource.SelectionChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab || resource.SelectedItem is not string value) return;
                SetCaseInsensitiveAttribute(prereq, "kbParam", value);
                MarkDirty();
                UpdatePreview();
            };
            row.Children.Add(resource);
        }

        row.Children.Add(CreateOperatorCombo(prereq));
        var valueBox = CreateNumericTextBox(FormatNumericForDisplay((string?)prereq.Attribute("value") ?? "0"), 80);
        valueBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetPrereqAttribute(prereq, "value", valueBox.Text ?? "0", rebuild: false);
        };
        row.Children.Add(valueBox);
    }

    private void AddMultiValuePrerequisiteFields(
        XElement prereq,
        StackPanel content,
        string childName,
        IEnumerable<string> options,
        string label)
    {
        var entries = prereq.Elements().Where(e => e.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0 && !string.IsNullOrWhiteSpace(prereq.Value))
        {
            if (IsModifiedTab)
            {
                var converted = new XElement(childName, prereq.Value.Trim());
                prereq.RemoveNodes();
                prereq.Add(converted);
                entries.Add(converted);
            }
            else
            {
                entries.Add(new XElement(childName, prereq.Value.Trim()));
            }
        }

        foreach (var entry in entries)
        {
            var isAttached = entry.Parent == prereq;
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(158, 0, 0, 0) };
            row.Children.Add(CreateInlineLabel(label));
            var entrySelector = CreateStrictPrereqSelector(options, entry.Value.Trim(), 150, value =>
            {
                if (!isAttached) return;
                entry.Value = value;
                MarkDirty();
                UpdatePreview();
            });
            if (IsModifiedTab && isAttached)
                entrySelector.Margin = new Thickness(entrySelector.Margin.Left, entrySelector.Margin.Top, 0, entrySelector.Margin.Bottom);
            row.Children.Add(entrySelector);
            if (IsModifiedTab && isAttached)
                row.Children.Add(CreateRemoveButton(() => entry.Remove()));
            content.Children.Add(row);
        }

        if (IsModifiedTab)
        {
            var add = CreateOptionalPropertyButton($"Add {label.ToLowerInvariant()}");
            add.Margin = new Thickness(158, 0, 0, 0);
            add.Click += (_, _) =>
            {
                prereq.Add(new XElement(childName, options.FirstOrDefault() ?? ""));
                MarkDirty();
                _ = BuildEditorAsync();
            };
            content.Children.Add(add);
        }
    }

    private AutoCompleteBox CreateStrictPrereqSelector(
        IEnumerable<string> options,
        string current,
        double width,
        Action<string> committed,
        bool deferSelectionCommit = false)
    {
        var values = options
            .Append(current)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
        {
            Text = current,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = IsModifiedTab,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        });
        // ConfigureSelector applies the shared standard text width. Prerequisite selectors
        // intentionally use compact widths, so enforce the requested width afterwards.
        selector.Width = width;
        selector.MaxWidth = width;
        EditorAutoCompleteService.ConfigureStrict(
            selector,
            values,
            current,
            () => _loadingUi,
            preserveUnknownInitialValue: true,
            allowEmpty: false,
            deferSelectionCommit: deferSelectionCommit,
            selectAllOnFirstClick: false,
            keepStartVisibleAfterCommit: true,
            valueCommitted: value =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                committed(value);
            });
        return selector;
    }

    private ComboBox CreateOperatorCombo(XElement prereq)
    {
        var displayValues = new[] { "<", "≤", "=", "≠", ">", "≥" };
        var combo = new ComboBox
        {
            ItemsSource = displayValues,
            SelectedItem = OperatorToSymbol((string?)prereq.Attribute("operator") ?? "gte"),
            IsEnabled = IsModifiedTab,
            Width = 62,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 6, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string symbol) return;
            SetPrereqAttribute(prereq, "operator", SymbolToOperator(symbol));
        };
        return combo;
    }

    private static TextBlock CreateInlineLabel(string text)
        => new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };

    private void SetPrereqAttribute(XElement prereq, string name, string value, bool rebuild = false)
    {
        prereq.SetAttributeValue(name, value);
        MarkDirty();
        UpdatePreview();
        if (rebuild) _ = BuildEditorAsync();
    }

    private static void SetCaseInsensitiveAttribute(XElement element, string canonicalName, string value)
    {
        var existing = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(canonicalName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.Value = value;
        else element.SetAttributeValue(canonicalName, value);
    }

    private static void RemoveCaseInsensitiveAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Remove();

    private static string GetPrereqTypeName(XElement prereq)
        => prereq.Name.LocalName.ToLowerInvariant() switch
        {
            "techstatus" => "TechStatus",
            "specificage" => "SpecificAge",
            "typecount" => "TypeCount",
            "culture" => "Culture",
            "civilization" => "Civilization",
            "kbstat" => "KBStat",
            _ => prereq.Name.LocalName
        };

    private static XElement CreateDefaultPrerequisite(string type)
        => type switch
        {
            "SpecificAge" => new XElement("specificage", TechnologyAges[0]),
            "TypeCount" => new XElement("typecount",
                new XAttribute("unit", ""),
                new XAttribute("count", "1.00"),
                new XAttribute("state", "aliveState"),
                new XAttribute("operator", "gte")),
            "Culture" => new XElement("culture", new XElement("cultureName", "Greek")),
            "Civilization" => new XElement("civilization", new XElement("civName", "")),
            "KBStat" => new XElement("KBStat",
                new XAttribute("kbStat", ""),
                new XAttribute("value", "0"),
                new XAttribute("operator", "gte")),
            _ => new XElement("techstatus", new XAttribute("status", "Unobtainable"), "")
        };

    private static void ReplacePrerequisiteType(XElement prereq, string type)
    {
        var replacement = CreateDefaultPrerequisite(type);
        prereq.ReplaceWith(replacement);
    }

    private static string OperatorToSymbol(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "lt" => "<",
            "lte" => "≤",
            "eq" => "=",
            "ne" => "≠",
            "gt" => ">",
            _ => "≥"
        };

    private static string SymbolToOperator(string value)
        => value switch
        {
            "<" => "lt",
            "≤" => "lte",
            "=" => "eq",
            "≠" => "ne",
            ">" => "gt",
            _ => "gte"
        };

    private static string ToPrereqStateDisplay(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "buildingstate" => "building",
            "anystate" => "any",
            "nonexiststate" => "nonexistent",
            _ => "alive"
        };

    private static string FromPrereqStateDisplay(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "building" => "buildingState",
            "any" => "anyState",
            "nonexistent" => "nonExistState",
            _ => "aliveState"
        };

    private static string FormatNumericForDisplay(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return number.ToString("0.############################", CultureInfo.InvariantCulture);
        return value;
    }

    private void AddEffectsHeader()
    {
        _effectsPanel.Children.Add(new TextBlock
        {
            Text = "──── Effects ────",
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#C59A52"),
            Margin = new Thickness(0, 15, 0, 8)
        });
    }

    private void AddEffectButton(XElement tech)
    {
        var add = CreateOptionalPropertyButton("Add effect");
        add.Margin = new Thickness(0, 0, 0, 6);
        add.Click += (_, _) =>
        {
            var container = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
            if (container == null)
            {
                container = new XElement("effects");
                tech.Add(container);
            }
            container.Add(new XElement("effect"));
            MarkDirty();
            _ = BuildEditorAsync();
        };
        _effectsPanel.Children.Add(add);
    }

    private void ResetEffectForType(XElement effect, string type)
    {
        QueueEffectOwnedStringsForRemoval(effect);
        effect.RemoveAttributes();
        effect.RemoveNodes();

        if (string.IsNullOrWhiteSpace(type))
            return;

        effect.SetAttributeValue("type", type);
        if (type.Equals("TechStatus", StringComparison.OrdinalIgnoreCase))
            effect.SetAttributeValue("status", "obtainable");
        else if (type.Equals("RandomTech", StringComparison.OrdinalIgnoreCase))
        {
            effect.SetAttributeValue("select", "1");
            effect.SetAttributeValue("status", "active");
        }
        else if (type.Equals("ForbidTech", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("SetOnTechResearchedTech", StringComparison.OrdinalIgnoreCase))
            effect.SetAttributeValue("amount", "1");
        else if (type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase))
        {
            effect.SetAttributeValue("target", "Self");
            effect.SetAttributeValue("playerName", "False");
            effect.SetAttributeValue("duration", "2500");
        }
        else if (type.Equals("CreateUnit", StringComparison.OrdinalIgnoreCase))
        {
            effect.Add(new XElement("pattern",
                new XAttribute("type", "Leaving"),
                new XAttribute("quantity", "1")));
        }
    }

    private async Task AddEffectEditorAsync(XElement effect)
    {
        var border = new Border
        {
            BorderBrush = Brush.Parse("#4C4031"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var content = new StackPanel { Spacing = 6 };
        border.Child = content;

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var selectorRow = new WrapPanel { Orientation = Orientation.Horizontal };
        headerGrid.Children.Add(selectorRow);
        if (IsModifiedTab)
        {
            var removeEffect = CreateRemoveButton(() =>
            {
                QueueEffectOwnedStringsForRemoval(effect);
                var parent = effect.Parent;
                effect.Remove();
                if (parent != null && parent.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase) && !parent.Elements().Any())
                    parent.Remove();
            });
            removeEffect.HorizontalAlignment = HorizontalAlignment.Right;
            removeEffect.VerticalAlignment = VerticalAlignment.Top;
            removeEffect.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(removeEffect, 1);
            headerGrid.Children.Add(removeEffect);
        }

        selectorRow.Children.Add(CreateInlineLabel("Type"));

        var currentType = ((string?)effect.Attribute("type") ?? "").Trim();
        var typeSelector = CreateStrictEffectSelector(TechnologyEffectTypes, currentType, value =>
        {
            if (value.Equals(currentType, StringComparison.OrdinalIgnoreCase)) return;
            ResetEffectForType(effect, value);
            MarkDirty();
            _ = BuildEditorAsync();
        }, 180);
        selectorRow.Children.Add(typeSelector);

        if (currentType.Equals("Data", StringComparison.OrdinalIgnoreCase))
        {
            var currentSubtype = ((string?)effect.Attribute("subtype") ?? "").Trim();
            var displayedSubtype = currentSubtype.Equals("Damagebonus", StringComparison.OrdinalIgnoreCase) ? "DamageBonus" : currentSubtype;
            var subtypeSelector = CreateStrictEffectSelector(TechnologyDataEffectSubtypes, displayedSubtype, value =>
            {
                if (value.Equals(currentSubtype, StringComparison.OrdinalIgnoreCase)) return;
                ResetDataEffectForSubtype(effect, value);
                MarkDirty();
                UpdatePreview();
                _ = BuildEditorAsync();
            }, 180);
            subtypeSelector.Margin = new Thickness(8, 4, 0, 4);
            selectorRow.Children.Add(subtypeSelector);
        }

        var hideTooltipAttribute = effect.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("hideTooltip", StringComparison.OrdinalIgnoreCase));
        if (IsModifiedTab || hideTooltipAttribute != null)
        {
            var hideTooltip = new CheckBox
            {
                Content = "Hide tooltip",
                IsChecked = hideTooltipAttribute != null,
                IsEnabled = IsModifiedTab,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 8, 4)
            };
            hideTooltip.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                if (hideTooltip.IsChecked == true) SetCaseInsensitiveAttribute(effect, "hideTooltip", "");
                else RemoveCaseInsensitiveAttribute(effect, "hideTooltip");
                MarkDirty();
                UpdatePreview();
            };
            selectorRow.Children.Add(hideTooltip);
        }

        var delayAttribute = effect.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("delay", StringComparison.OrdinalIgnoreCase));
        if (delayAttribute == null)
        {
            if (IsModifiedTab)
            {
                var addDelay = CreateOptionalPropertyButton("Delay");
                addDelay.Margin = new Thickness(0, 4, 8, 4);
                addDelay.Click += (_, _) =>
                {
                    SetCaseInsensitiveAttribute(effect, "delay", "0");
                    MarkDirty();
                    _ = BuildEditorAsync();
                };
                selectorRow.Children.Add(addDelay);
            }
        }
        else
        {
            var segment = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            segment.Children.Add(CreateInlineLabel("Delay"));
            var delayBox = CreateNumericTextBox(FormatNumericForDisplay(delayAttribute.Value), 70);
            EditorNumericInputBehavior.AttachRule(delayBox, ProtoUnitNumericKind.UnsignedFloat);
            delayBox.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                delayAttribute.Value = delayBox.Text ?? "0";
                MarkDirty();
                UpdatePreview();
            };
            segment.Children.Add(delayBox);
            if (IsModifiedTab) segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "delay")));
            selectorRow.Children.Add(segment);
        }

        var tooltipIdAttribute = effect.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase));
        if (tooltipIdAttribute == null && IsModifiedTab)
        {
            var addTooltip = CreateOptionalPropertyButton("Tooltip override");
            addTooltip.Margin = new Thickness(0, 4, 8, 4);
            addTooltip.Click += (_, _) =>
            {
                if (_current == null) return;
                var tooltipId = BuildNextEffectTooltipStringId(_current);
                SetCaseInsensitiveAttribute(effect, "tooltipID", tooltipId);
                _pendingStringRemovals.Remove(tooltipId);
                _pendingStringUpdates[tooltipId] = "";
                MarkDirty();
                _ = BuildEditorAsync();
            };
            selectorRow.Children.Add(addTooltip);
        }

        content.Children.Add(headerGrid);

        if (tooltipIdAttribute != null)
            await AddEffectStringAttributeRowAsync(content, effect, tooltipIdAttribute, "Tooltip override", 380, removable: true, multiline: true);

        var structured = await AddStructuredEffectBodyAsync(effect, content, currentType);
        if (!structured)
            AddRawEffectXmlEditor(effect, content);

        _effectsPanel.Children.Add(border);
    }

    private async Task<bool> AddStructuredEffectBodyAsync(XElement effect, StackPanel content, string currentType)
    {
        if (currentType.Equals("Data", StringComparison.OrdinalIgnoreCase))
        {
            var subtype = GetCaseInsensitiveAttribute(effect, "subtype")?.Value ?? "";
            if (SimpleUnitAmountDataSubtypes.Contains(subtype))
            {
                if (subtype.Equals("BuildingWorkRate", StringComparison.OrdinalIgnoreCase))
                    AddSimpleUnitAmountDataEffectEditor(effect, content, allowOverride: true);
                else
                    AddSimpleUnitAmountDataEffectEditor(effect, content);
                return true;
            }
            if (ActionUnitAmountDataSubtypes.Contains(subtype))
            {
                AddActionUnitAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("WorkRateAll", StringComparison.OrdinalIgnoreCase))
            {
                AddActionUnitAmountDataEffectEditor(effect, content, allowOverride: true);
                return true;
            }
            if (DamageTypeActionDataSubtypes.Contains(subtype))
            {
                AddTypedDamageDataEffectEditor(effect, content, "Damage type", "damagetype", includeAction: true, includeDivine: true);
                return true;
            }
            if (ArmorTypeActionDataSubtypes.Contains(subtype))
            {
                AddTypedDamageDataEffectEditor(effect, content, "Armor type", "armortype", includeAction: false, includeDivine: false);
                return true;
            }
            if (DamageTypeNoActionDataSubtypes.Contains(subtype))
            {
                AddTypedDamageDataEffectEditor(effect, content, "Damage type", "damagetype", includeAction: false, includeDivine: true);
                return true;
            }
            if (DamageBonusDataSubtypes.Contains(subtype))
            {
                AddDamageBonusDataEffectEditor(effect, content);
                return true;
            }
            if (ResourceAmountDataSubtypes.Contains(subtype))
            {
                AddResourceAmountDataEffectEditor(effect, content);
                return true;
            }
            if (PlayerResourceDataSubtypes.Contains(subtype))
            {
                AddPlayerResourceDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("RepairCostFactor", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("AutoGatherBonus", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("ResourceTrickleRate", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerAmountDataEffectEditor(effect, content, includeResource: true);
                return true;
            }
            if (subtype.Equals("PowerROF", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("PowerMaxUses", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerPowerAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("PowerCost", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerPowerAmountDataEffectEditor(effect, content, allowOverride: true);
                return true;
            }
            if (subtype.Equals("AddGoal", StringComparison.OrdinalIgnoreCase))
            {
                AddGoalDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("AddGoalContributor", StringComparison.OrdinalIgnoreCase))
            {
                AddGoalContributorDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("AddGoalReward", StringComparison.OrdinalIgnoreCase))
            {
                AddGoalRewardDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("AddGoalRewardExclusion", StringComparison.OrdinalIgnoreCase))
            {
                AddGoalRewardExclusionDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("SetGoalActive", StringComparison.OrdinalIgnoreCase))
            {
                AddSetGoalActiveDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BountyResourceEarningReward", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("BountyResourceEarningMultiplier", StringComparison.OrdinalIgnoreCase))
            {
                AddBountyResourceEarningDataEffectEditor(
                    effect,
                    content,
                    allowAttackerBonus: subtype.Equals("BountyResourceEarningMultiplier", StringComparison.OrdinalIgnoreCase));
                return true;
            }
            if (subtype.Equals("TimeShiftingCost", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("TimeShiftingTimeRatio", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerAmountDataEffectEditor(effect, content, includeUnitType: true);
                return true;
            }
            if (subtype.Equals("SetAge", StringComparison.OrdinalIgnoreCase))
            {
                AddSetAgeDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("Market", StringComparison.OrdinalIgnoreCase))
            {
                AddMarketDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("MarketReset", StringComparison.OrdinalIgnoreCase))
            {
                AddMarketResetDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("TimeShiftingAdd", StringComparison.OrdinalIgnoreCase))
            {
                AddTimeShiftingAddDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("UpdateVisual", StringComparison.OrdinalIgnoreCase))
            {
                AddFixedPlayerUnitTypeDataEffectEditor(effect, content, _prereqUnitNames);
                return true;
            }
            if (subtype.Equals("TechCostAbsolute", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerRelativityAmountDataEffectEditor(effect, content, allowOverride: true);
                return true;
            }
            if (subtype.Equals("ResourceIfTechActive", StringComparison.OrdinalIgnoreCase))
            {
                AddResourceIfTechActiveDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("ResourceByUnitCount", StringComparison.OrdinalIgnoreCase))
            {
                AddResourceByUnitCountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("PopulationLimit", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerRelativityAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("PartisanUnit", StringComparison.OrdinalIgnoreCase))
            {
                AddPartisanUnitDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("MinimumResourceTrickleRate", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("MaximumResourceTrickleRate", StringComparison.OrdinalIgnoreCase))
            {
                AddResourceTrickleLimitDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("MaxResource", StringComparison.OrdinalIgnoreCase))
            {
                AddMaxResourceDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("PopulationCap", StringComparison.OrdinalIgnoreCase))
            {
                AddPopulationCapDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("SetCivilization", StringComparison.OrdinalIgnoreCase))
            {
                AddSetCivilizationDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("EmpowerModify", StringComparison.OrdinalIgnoreCase))
            {
                AddEmpowerModifyDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("ProtoActionAdd", StringComparison.OrdinalIgnoreCase))
            {
                AddProtoActionAddDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("AutoAttackType", StringComparison.OrdinalIgnoreCase))
            {
                AddAutoAttackTypeDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BoostRadius", StringComparison.OrdinalIgnoreCase))
            {
                AddBoostRadiusDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("DeadTransform", StringComparison.OrdinalIgnoreCase))
            {
                AddFixedUnitReferenceDataEffectEditor(effect, content, "Transform to", "unittype", _protoUnitNames);
                return true;
            }
            if (subtype.Equals("PlacementRulesOverride", StringComparison.OrdinalIgnoreCase))
            {
                AddFixedUnitReferenceDataEffectEditor(effect, content, "Assign rules of", "unittype", _protoUnitNames);
                return true;
            }
            if (subtype.Equals("EmpowerArea", StringComparison.OrdinalIgnoreCase))
            {
                AddEmpowerAreaDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("EmpowerEnable", StringComparison.OrdinalIgnoreCase))
            {
                AddEmpowerEnableDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("GatherResourceOverride", StringComparison.OrdinalIgnoreCase))
            {
                AddGatherResourceOverrideDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("SetVeterancyRankActive", StringComparison.OrdinalIgnoreCase))
            {
                AddSetVeterancyRankActiveDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("InitialVeterancyRank", StringComparison.OrdinalIgnoreCase))
            {
                AddInitialVeterancyRankDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("UnitRegenRateLimit", StringComparison.OrdinalIgnoreCase))
            {
                AddUnitRegenRateLimitDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("VeterancyBonus", StringComparison.OrdinalIgnoreCase))
            {
                AddVeterancyBonusDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("VeterancyRankAdd", StringComparison.OrdinalIgnoreCase))
            {
                AddVeterancyRankAddDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("SpeedModifier", StringComparison.OrdinalIgnoreCase))
            {
                AddSpeedModifierDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("StackControl", StringComparison.OrdinalIgnoreCase))
            {
                AddStackControlDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("TacticEnable", StringComparison.OrdinalIgnoreCase))
            {
                AddTacticEnableDataEffectEditor(effect, content);
                return true;
            }
            if (ActionAddAttachingUnitDataSubtypes.Contains(subtype))
            {
                AddActionAddAttachingUnitDataEffectEditor(effect, content);
                return true;
            }
            if (AddAttackTypeDataSubtypes.Contains(subtype))
            {
                AddAddAttackTypeDataEffectEditor(effect, content);
                return true;
            }
            if (AddDependentUnitDataSubtypes.Contains(subtype))
            {
                AddAddDependentUnitDataEffectEditor(effect, content);
                return true;
            }
            if (EnableDisableUnitDataSubtypes.Contains(subtype))
            {
                AddEnableDisableUnitDataEffectEditor(effect, content, includeAction: false);
                return true;
            }
            if (EnableDisableActionUnitDataSubtypes.Contains(subtype))
            {
                AddEnableDisableUnitDataEffectEditor(effect, content, includeAction: true);
                return true;
            }
            if (MovementTypeDataSubtypes.Contains(subtype))
            {
                AddMovementTypeDataEffectEditor(effect, content);
                return true;
            }
            if (RevealLosDataSubtypes.Contains(subtype))
            {
                AddRevealLosDataEffectEditor(effect, content);
                return true;
            }
            if (ChargedModifyAdjustDataSubtypes.Contains(subtype))
            {
                AddChargedModifyAdjustDataEffectEditor(effect, content);
                return true;
            }
            if (CommandDataSubtypes.Contains(subtype))
            {
                AddCommandDataEffectEditor(effect, content, includePosition: subtype.Equals("CommandAdd", StringComparison.OrdinalIgnoreCase));
                return true;
            }
            if (DamageByCostDataSubtypes.Contains(subtype))
            {
                AddDamageByCostDataEffectEditor(effect, content);
                return true;
            }
            if (DamageFlagsDataSubtypes.Contains(subtype))
            {
                AddDamageFlagsDataEffectEditor(effect, content);
                return true;
            }
            if (DamageShadingDataSubtypes.Contains(subtype))
            {
                AddDamageShadingDataEffectEditor(effect, content);
                return true;
            }
            if (ProtoUnitFlagDataSubtypes.Contains(subtype))
            {
                AddProtoUnitFlagDataEffectEditor(effect, content);
                return true;
            }
            if (ProtoActionFlagDataSubtypes.Contains(subtype))
            {
                AddProtoActionFlagDataEffectEditor(effect, content);
                return true;
            }
            if (LifespanDataSubtypes.Contains(subtype))
            {
                AddLifespanDataEffectEditor(effect, content);
                return true;
            }
            if (MinWorkRateDataSubtypes.Contains(subtype))
            {
                AddMinWorkRateDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("Yield", StringComparison.OrdinalIgnoreCase))
            {
                AddMinWorkRateDataEffectEditor(effect, content, defaultAction: "Gather", allowOverride: false);
                return true;
            }
            if (ContainingUnitAmountDataSubtypes.Contains(subtype))
            {
                AddContainingUnitAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("WorkRateSpecific", StringComparison.OrdinalIgnoreCase))
            {
                AddWorkRateSpecificDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("YieldSpecific", StringComparison.OrdinalIgnoreCase))
            {
                AddWorkRateSpecificDataEffectEditor(effect, content, defaultAction: "Gather", allowOverride: false);
                return true;
            }
            if (ModifyReplacementDataSubtypes.Contains(subtype))
            {
                AddModifyReplacementDataEffectEditor(effect, content);
                return true;
            }
            if (ModifySpawnDataSubtypes.Contains(subtype))
            {
                AddModifySpawnDataEffectEditor(effect, content);
                return true;
            }
            if (OnDamageModifyDataSubtypes.Contains(subtype))
            {
                AddOnDamageModifyDataEffectEditor(effect, content);
                return true;
            }
            if (OnHitEffectDataSubtypes.Contains(subtype))
            {
                AddOnHitEffectDataEffectEditor(effect, content);
                return true;
            }
            if (OnHitEffectAttributeDataSubtypes.Contains(subtype))
            {
                AddOnHitEffectAttributeDataEffectEditor(effect, content, subtype);
                return true;
            }
            if (ProjectileDataSubtypes.Contains(subtype))
            {
                AddProjectileDataEffectEditor(effect, content);
                return true;
            }
            if (RechargeTypeDataSubtypes.Contains(subtype))
            {
                AddRechargeTypeDataEffectEditor(effect, content);
                return true;
            }
            if (SelfDestructProtoActionDataSubtypes.Contains(subtype))
            {
                AddSelfDestructProtoActionDataEffectEditor(effect, content);
                return true;
            }
            if (SetUnitTypeDataSubtypes.Contains(subtype))
            {
                AddSetUnitTypeDataEffectEditor(effect, content);
                return true;
            }
            if (ContainedTypeDataSubtypes.Contains(subtype))
            {
                AddContainedTypeDataEffectEditor(effect, content, subtype);
                return true;
            }
            if (subtype.Equals("SharedBuildLimitUnit", StringComparison.OrdinalIgnoreCase))
            {
                AddSharedBuildLimitUnitDataEffectEditor(effect, content);
                return true;
            }
            return false;
        }

        if (!StructuredTechnologyEffectTypes.Contains(currentType)) return false;

        switch (currentType.ToLowerInvariant())
        {
            case "setname":
                await AddSetNameEffectEditorAsync(effect, content);
                break;
            case "sound":
                AddSimpleEffectValueEditor(effect, content, "Play sound", 380);
                break;
            case "textoutput":
                await AddTextOutputEffectEditorAsync(effect, content, allIsIntrinsic: false);
                break;
            case "textoutputall":
                await AddTextOutputEffectEditorAsync(effect, content, allIsIntrinsic: true);
                break;
            case "textoutputtechname":
                await AddTextOutputEffectEditorAsync(effect, content, allIsIntrinsic: false);
                break;
            case "texteffectoutput":
                await AddTextEffectOutputEditorAsync(effect, content);
                break;
            case "setonbuildingdeathtech":
                AddSetOnBuildingDeathTechEditor(effect, content);
                break;
            case "consolecommand":
                AddSimpleEffectValueEditor(effect, content, "Console command", 200);
                break;
            case "createpower":
                AddCreatePowerEffectEditor(effect, content);
                break;
            case "randomtech":
                AddRandomTechEffectEditor(effect, content);
                break;
            case "setage":
                AddSetAgeEffectEditor(effect, content);
                break;
            case "techstatus":
                AddTechStatusEffectEditor(effect, content);
                break;
            case "sharedlos":
                AddSharedLosEffectEditor(effect, content);
                break;
            case "modifyprotounit":
                AddModifyProtoUnitEffectEditor(effect, content);
                break;
            case "transformunit":
                AddTransformUnitEffectEditor(effect, content);
                break;
            case "resourceexchange":
                AddResourceExchangeEffectEditor(effect, content);
                break;
            case "resourceinventoryexchange":
                AddResourceInventoryExchangeEffectEditor(effect, content);
                break;
            case "addtricklebyresource":
                AddTrickleByResourceEffectEditor(effect, content);
                break;
            case "resourceexchange2":
                AddResourceExchange2EffectEditor(effect, content);
                break;
            case "replaceunit":
                AddReplaceUnitEffectEditor(effect, content);
                break;
            case "forbidtech":
                AddForbidTechEffectEditor(effect, content);
                break;
            case "setontechresearchedtech":
                AddSetOnTechResearchedTechEffectEditor(effect, content);
                break;
            case "uialert":
                await AddUiAlertEffectEditorAsync(effect, content);
                break;
            case "createunit":
                AddCreateUnitEffectEditor(effect, content);
                break;
        }
        return true;
    }

    private void AddSimpleUnitAmountDataEffectEditor(XElement effect, StackPanel content, bool allowOverride = false)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddActionUnitAmountDataEffectEditor(XElement effect, StackPanel content, bool allowOverride = false)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddTypedDamageDataEffectEditor(
        XElement effect,
        StackPanel content,
        string typeLabel,
        string attributeName,
        bool includeAction,
        bool includeDivine)
    {
        EnsureDefaultDataRelativity(effect);
        if (includeAction) EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        if (includeAction)
        {
            row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        }
        AddDataRelativityAndAmountEditors(effect, row);

        var options = includeDivine
            ? new[] { "All", "Hack", "Pierce", "Crush", "Divine" }
            : new[] { "All", "Hack", "Pierce", "Crush" };
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "All";
        var typeCombo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All",
            IsEnabled = IsModifiedTab,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || typeCombo.SelectedItem is not string selected) return;
            if (selected.Equals("All", StringComparison.OrdinalIgnoreCase)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment(typeLabel, typeCombo, leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddDamageBonusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);

        row.Children.Add(CreateLabeledEffectSegment("Bonus against", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty();
                UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddResourceAmountDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddPlayerResourceDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizeResourceEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizeResourceEffect));

        var currentRelativity = GetCaseInsensitiveAttribute(effect, "relativity")?.Value ?? "Absolute";
        var relativityOptions = TechnologyDataEffectRules.ResourceRelativityDisplayOptions.AsEnumerable();
        if (currentRelativity.Equals("Override", StringComparison.OrdinalIgnoreCase))
            relativityOptions = relativityOptions.Append("Override");
        var relativityCombo = new ComboBox
        {
            ItemsSource = relativityOptions.ToList(),
            SelectedItem = RelativityToDisplay(currentRelativity),
            IsEnabled = IsModifiedTab,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        relativityCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || relativityCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(relativityCombo);
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 80, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddPlayerAmountDataEffectEditor(
        XElement effect,
        StackPanel content,
        bool includeResource = false,
        bool includeUnitType = false)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        if (includeUnitType)
        {
            row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
                _prereqUnitNames,
                GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
                value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                    else SetCaseInsensitiveAttribute(effect, "unittype", value);
                    MarkDirty();
                    UpdatePreview();
                },
                200), leftSpacing: 8));
        }
        AddDataRelativityAndAmountEditors(effect, row);
        if (includeResource)
            row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddSetAgeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        var ageAmounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Archaic"] = "1",
            ["Classical"] = "2",
            ["Heroic"] = "3",
            ["Mythic"] = "4",
            ["Wonder"] = "5"
        };
        var currentAmount = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "";
        if (!ageAmounts.Values.Contains(currentAmount, StringComparer.OrdinalIgnoreCase))
        {
            EnsureExactDataAttribute(effect, "amount", "1");
            currentAmount = "1";
        }
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var ageCombo = new ComboBox
        {
            ItemsSource = ageAmounts.Keys.ToList(),
            SelectedItem = ageAmounts.First(pair => pair.Value == currentAmount).Key,
            IsEnabled = IsModifiedTab,
            Width = 110,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ageCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || ageCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", ageAmounts[selected]);
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Age", ageCombo, leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddPlayerPowerAmountDataEffectEditor(XElement effect, StackPanel content, bool allowOverride = false)
    {
        MigrateDataAttribute(effect, "power", "protopower");
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Power", CreateStrictEffectSelector(
            _godPowerNames,
            GetCaseInsensitiveAttribute(effect, "protopower")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "protopower");
                else SetCaseInsensitiveAttribute(effect, "protopower", value);
                MarkDirty();
                UpdatePreview();
            },
            200), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        content.Children.Add(row);
    }

    private void AddGoalDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "goaltype", "Damage");
        EnsureFixedDataAttribute(effect, "rewardtrackingtype", "Single");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", 150), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Type", CreateDataAttributeCombo(
            effect, "goaltype", ["Damage", "Resource", "DeathCount"], "Damage", 120), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 80, "0"), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Tracking type", CreateDataAttributeCombo(
            effect, "rewardtrackingtype", ["Single", "PerPossibleReward"], "Single", 160), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddGoalContributorDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureFixedDataAttribute(effect, "contributortype", "Unit");
        EnsurePlayerTarget(effect);

        var currentType = GetCaseInsensitiveAttribute(effect, "contributortype")?.Value.Trim() ?? "Unit";
        var typeCombo = CreateDataAttributeCombo(
            effect,
            "contributortype",
            ["Unit", "Resource"],
            "Unit",
            110,
            selected =>
            {
                RemoveCaseInsensitiveAttribute(effect, "contributorid");
                _ = BuildEditorAsync();
            });

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", 150), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Type", typeCombo, leftSpacing: 8));
        if (currentType.Equals("Resource", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateResourceCombo(effect, "contributorid"));
        else
        {
            var selector = CreateStrictEffectSelector(
                _prereqUnitNames,
                GetCaseInsensitiveAttribute(effect, "contributorid")?.Value.Trim() ?? "",
                value => SetOptionalDataAttribute(effect, "contributorid", value),
                190);
            selector.Margin = new Thickness(8, 4, 0, 4);
            row.Children.Add(selector);
        }
        content.Children.Add(row);
    }

    private void AddGoalRewardDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", 150), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Reward", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "rewardtype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "rewardtype", value),
            190), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 80, "0"), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddGoalRewardExclusionDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", 150), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Exclude", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "rewardtype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "rewardtype", value),
            190), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddSetGoalActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", 150), leftSpacing: 8));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddBountyResourceEarningDataEffectEditor(XElement effect, StackPanel content, bool allowAttackerBonus)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "condition", "Damage");
        EnsurePlayerTarget(effect);

        var currentCondition = GetCaseInsensitiveAttribute(effect, "condition")?.Value.Trim() ?? "Damage";
        var conditionCombo = CreateDataAttributeCombo(
            effect,
            "condition",
            ["Damage", "Destroy"],
            "Damage",
            110,
            selected => { _ = BuildEditorAsync(); });

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Condition", conditionCombo, leftSpacing: 8));
        var unitSelector = CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            190);
        if (currentCondition.Equals("Damage", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateLabeledEffectSegment("From", unitSelector, leftSpacing: 8));
        else
        {
            unitSelector.Margin = new Thickness(8, 4, 0, 4);
            row.Children.Add(unitSelector);
        }
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resourcetype"), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        if (allowAttackerBonus && currentCondition.Equals("Destroy", StringComparison.OrdinalIgnoreCase))
        {
            AddOnHitOptionalReferenceSelector(
                row, effect, "Bonus for", "Bonus for", "attackertype", _prereqUnitNames, 190,
                showWhenMissing: false, buttonLeftSpacing: 8);
        }
        content.Children.Add(row);
    }

    private ComboBox CreateDataAttributeCombo(
        XElement effect,
        string attributeName,
        IReadOnlyList<string> options,
        string defaultValue,
        double width,
        Action<string>? selectionChanged = null)
    {
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? defaultValue;
        var values = options.Append(current).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = values.First(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = width,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, selected);
            selectionChanged?.Invoke(selected);
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void AddMarketDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "component", "BuyFactor");
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var componentOptions = new[]
        {
            "BuyFactor", "SellFactor", "BuyDelta", "SellDelta", "BuyFactorSpecific", "SellFactorSpecific"
        };
        var currentComponent = GetCaseInsensitiveAttribute(effect, "component")?.Value.Trim() ?? "BuyFactor";
        var componentValues = componentOptions.Append(currentComponent)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var componentCombo = new ComboBox
        {
            ItemsSource = componentValues,
            SelectedItem = componentValues.First(value => value.Equals(currentComponent, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 150,
            Margin = new Thickness(0, 4, 8, 4)
        };
        componentCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || componentCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "component", selected);
            if (!selected.Equals("BuyFactorSpecific", StringComparison.OrdinalIgnoreCase) &&
                !selected.Equals("SellFactorSpecific", StringComparison.OrdinalIgnoreCase))
                RemoveCaseInsensitiveAttribute(effect, "resource");
            MarkDirty();
            UpdatePreview();
            _ = BuildEditorAsync();
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Component", componentCombo, leftSpacing: 8));
        if (currentComponent.Equals("BuyFactorSpecific", StringComparison.OrdinalIgnoreCase) ||
            currentComponent.Equals("SellFactorSpecific", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        content.Children.Add(row);
    }

    private void AddMarketResetDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var resetRate = new ComboBox
        {
            ItemsSource = new[] { "Enable" },
            SelectedItem = "Enable",
            IsEnabled = false,
            Width = 100,
            Margin = new Thickness(0, 4, 8, 4)
        };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Reset rate", resetRate, leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddTimeShiftingAddDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "timeratio", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            200), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment(
            "Time Ratio",
            CreateUnsignedFloatEffectBox(effect, "timeratio", 80, "0"),
            leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddFixedPlayerUnitTypeDataEffectEditor(
        XElement effect,
        StackPanel content,
        IEnumerable<string> suggestions)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            suggestions,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            200), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddPlayerRelativityAmountDataEffectEditor(XElement effect, StackPanel content, bool allowOverride = false)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        content.Children.Add(row);
    }

    private void AddResourceIfTechActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        MigrateDataAttribute(effect, "active", "tech");
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("If", CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            GetCaseInsensitiveAttribute(effect, "tech")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "tech", value),
            200), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddResourceByUnitCountDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            200), leftSpacing: 8));

        var includeDead = new CheckBox
        {
            Content = "Include dead",
            IsChecked = GetCaseInsensitiveAttribute(effect, "includedead")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        includeDead.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (includeDead.IsChecked == true) SetCaseInsensitiveAttribute(effect, "includedead", "true");
            else RemoveCaseInsensitiveAttribute(effect, "includedead");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(includeDead);
        content.Children.Add(row);
    }

    private void AddPartisanUnitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Partisan", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            200), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddResourceTrickleLimitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureFixedDataAttribute(effect, "relativity", "Absolute");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        row.Children.Add(CreateRestrictedDataRelativityCombo(effect, ["Absolute", "Assign"]));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedIntegerEffectBox(effect, "amount", 80, "0")));
        content.Children.Add(row);
    }

    private void EnsurePlayerTarget(XElement effect)
    {
        if (!IsModifiedTab || !TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect)) return;
        MarkDirty();
        UpdatePreview();
    }

    private void SetOptionalDataAttribute(XElement effect, string attributeName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, attributeName);
        else SetCaseInsensitiveAttribute(effect, attributeName, value);
        MarkDirty();
        UpdatePreview();
    }

    private void MigrateDataAttribute(XElement effect, string oldName, string newName)
    {
        if (!IsModifiedTab) return;
        var oldAttribute = GetCaseInsensitiveAttribute(effect, oldName);
        if (oldAttribute == null) return;
        if (GetCaseInsensitiveAttribute(effect, newName) == null)
            SetCaseInsensitiveAttribute(effect, newName, oldAttribute.Value);
        RemoveCaseInsensitiveAttribute(effect, oldName);
        MarkDirty();
        UpdatePreview();
    }

    private void AddMaxResourceDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizeMaxResourceEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizeMaxResourceEffect));
        row.Children.Add(CreateLabeledEffectSegment(
            "Capped at initial resource +",
            CreateSignedFloatEffectBox(effect, "amount", 80, "0"),
            leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        content.Children.Add(row);
    }

    private void AddPopulationCapDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizePopulationCapEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePopulationCapEffect));
        AddDataRelativityAndAmountEditors(effect, row);
        content.Children.Add(row);
    }

    private void AddSetCivilizationDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizeSetCivilizationEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizeSetCivilizationEffect));
        row.Children.Add(CreateLabeledEffectSegment("Set to", CreateStrictEffectSelector(
            _majorGodNames,
            GetCaseInsensitiveAttribute(effect, "civ")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "civ");
                else SetCaseInsensitiveAttribute(effect, "civ", value);
                MarkDirty();
                UpdatePreview();
            },
            180), leftSpacing: 8));
        content.Children.Add(row);
    }

    private ComboBox CreateForcedPlayerTargetCombo(XElement effect, Func<XElement, bool> normalize)
    {
        var targetCombo = new ComboBox
        {
            ItemsSource = TechnologyDataEffectRules.ResourceTargetOptions,
            SelectedItem = "Player",
            IsEnabled = false,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        targetCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || !normalize(effect)) return;
            MarkDirty();
            UpdatePreview();
        };
        return targetCombo;
    }

    private void AddEmpowerModifyDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, "Empower");
        EnsureFixedDataAttribute(effect, "empowertype", "self");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        row.Children.Add(CreateLabeledEffectSegment("Modify Type", CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
            },
            180), leftSpacing: 8));

        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private ComboBox CreateEmpowerPlayerTypeCombo(XElement effect)
    {
        var options = new[] { "Self", "Enemy", "Gaia" };
        var current = GetCaseInsensitiveAttribute(effect, "empowertype")?.Value.Trim() ?? "self";
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "Self",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "empowertype", selected.ToLowerInvariant());
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void AddProtoActionAddDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyDataEffectRules.NormalizeProtoActionAddEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Grants", CreateStrictEffectSelector(
            _protoActionNames,
            GetCaseInsensitiveAttribute(effect, "protoaction")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "protoaction");
                else SetCaseInsensitiveAttribute(effect, "protoaction", value);
            },
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));

        var addToTactics = new CheckBox
        {
            Content = "Add to tactics",
            IsChecked = !string.Equals(GetCaseInsensitiveAttribute(effect, "addToTactics")?.Value, "0", StringComparison.OrdinalIgnoreCase),
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(8, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        addToTactics.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (addToTactics.IsChecked == false) SetCaseInsensitiveAttribute(effect, "addToTactics", "0");
            else RemoveCaseInsensitiveAttribute(effect, "addToTactics");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(addToTactics);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddAutoAttackTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Can auto attack", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unitType")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unitType");
                else SetCaseInsensitiveAttribute(effect, "unitType", value);
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private Control CreateTacticNameEditor(XElement effect, double width = 150)
    {
        return _tacticNames.Count > 0
            ? CreateStrictEffectSelector(
                _tacticNames,
                GetCaseInsensitiveAttribute(effect, "tactic")?.Value.Trim() ?? "",
                value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "tactic");
                    else SetCaseInsensitiveAttribute(effect, "tactic", value);
                },
                width)
            : CreateFreeTextEffectAttributeBox(effect, "tactic", width);
    }

    private void AddBoostRadiusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        AddOnHitOptionalReferenceSelector(
            row,
            effect,
            "Target",
            "Target",
            "targetType",
            _prereqUnitNames,
            180,
            showWhenMissing: false,
            buttonLeftSpacing: 8);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddFixedUnitReferenceDataEffectEditor(
        XElement effect,
        StackPanel content,
        string label,
        string attributeName,
        IEnumerable<string> suggestions)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment(label, CreateStrictEffectSelector(
            suggestions,
            GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, attributeName);
                else SetCaseInsensitiveAttribute(effect, attributeName, value);
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddEmpowerAreaDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, "Empower");
        EnsureFixedDataAttribute(effect, "empowertype", "self");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddEmpowerEnableDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);
        EnsureDefaultDataAction(effect, "Empower");
        EnsureFixedDataAttribute(effect, "empowertype", "self");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddGatherResourceOverrideDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, "Gather");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("For", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSetVeterancyRankActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);
        EnsureFixedDataAttribute(effect, "rank", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Rank", CreateUnsignedIntegerEffectBox(effect, "rank", 70, "0"), leftSpacing: 8));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddInitialVeterancyRankDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment(
            "Start at rank",
            CreateUnsignedIntegerEffectBox(effect, "amount", 80, "0"),
            leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddVeterancyBonusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "rank", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Rank", CreateUnsignedIntegerEffectBox(effect, "rank", 70, "0"), leftSpacing: 8));

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        row.Children.Add(CreateLabeledEffectSegment("Bonus", CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                if (normalized is not "DamageSpecific" and not "ArmorSpecific")
                    RemoveCaseInsensitiveAttribute(effect, "damagetype");
                _ = BuildEditorAsync();
            },
            180), leftSpacing: 8));

        if (currentModifyType is "DamageSpecific" or "ArmorSpecific")
        {
            EnsureFixedDataAttribute(effect, "damagetype", "Hack");
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateRequiredDataTypeCombo(effect, includeDivine: currentModifyType == "DamageSpecific"),
                leftSpacing: 8));
        }

        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddVeterancyRankAddDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "rankType", "Attacks");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        var rankTypeOptions = new[] { "Attacks", "Kills", "Damage" };
        var currentRankType = GetCaseInsensitiveAttribute(effect, "rankType")?.Value.Trim() ?? "Attacks";
        var rankType = new ComboBox
        {
            ItemsSource = rankTypeOptions,
            SelectedItem = rankTypeOptions.FirstOrDefault(value => value.Equals(currentRankType, StringComparison.OrdinalIgnoreCase)) ?? "Attacks",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        rankType.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || rankType.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "rankType", selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Type", rankType, leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 80, "0"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSpeedModifierDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddStackControlDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, "StackControl");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddTacticEnableDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect), leftSpacing: 8));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddActionAddAttachingUnitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataAction(effect);
        EnsureFixedDataAttribute(effect, "relativity", "Absolute");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsureFixedDataAttribute(effect, "targetunittype", "Unit");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        var actionLabel = CreateInlineLabel("Action");
        actionLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(actionLabel);
        row.Children.Add(CreateDataActionSelector(effect));
        row.Children.Add(CreateLabeledEffectSegment("Attach", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unittype", value),
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "targetunittype")?.Value.Trim() ?? "Unit",
            value => SetCaseInsensitiveAttribute(effect, "targetunittype", value),
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 70, "1"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddAddAttackTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureFixedDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateFreeTextEffectAttributeBox(effect, "tactic", 100), leftSpacing: 8));

        var current = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "1";
        var toggle = new ComboBox
        {
            ItemsSource = new[] { "Enable attack", "Disable attack" },
            SelectedItem = current == "0" ? "Disable attack" : "Enable attack",
            IsEnabled = IsModifiedTab,
            Width = 135,
            Margin = new Thickness(8, 4, 8, 4)
        };
        toggle.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || toggle.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Disable attack" ? "0" : "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(toggle);
        row.Children.Add(CreateLabeledEffectSegment("Unit type", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unittype", value),
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddAddDependentUnitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureFixedDataAttribute(effect, "relativity", "Absolute");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Dependent", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "proto")?.Value.Trim() ?? "",
            value => SetCaseInsensitiveAttribute(effect, "proto", value),
            180), leftSpacing: 8));

        var currentRelativity = GetCaseInsensitiveAttribute(effect, "relativity")?.Value ?? "Absolute";
        var relativity = new ComboBox
        {
            ItemsSource = new[] { "Add", "Set to" },
            SelectedItem = currentRelativity.Equals("Assign", StringComparison.OrdinalIgnoreCase) ? "Set to" : "Add",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(8, 4, 8, 4)
        };
        relativity.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || relativity.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", selected == "Set to" ? "Assign" : "Absolute");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(relativity);
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", 70, "0"), leftSpacing: 8));
        foreach (var axis in new[] { "x", "y", "z" })
            row.Children.Add(CreateLabeledEffectSegment(axis.ToUpperInvariant(), CreateSignedFloatEffectBox(effect, axis, 60, "0"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddChargedModifyAdjustDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        var modifyTypeSelector = CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                MarkDirty(); UpdatePreview();
            },
            180);
        row.Children.Add(CreateLabeledEffectSegment("Modify type", modifyTypeSelector, leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddCommandDataEffectEditor(XElement effect, StackPanel content, bool includePosition)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        if (includePosition)
        {
            EnsureFixedDataAttribute(effect, "row", "0");
            EnsureFixedDataAttribute(effect, "column", "0");
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);

        var referenceKind = GetCommandReferenceKind(effect);
        var kindCombo = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Tech", "Command" },
            SelectedItem = referenceKind,
            IsEnabled = IsModifiedTab,
            Width = 110,
            Margin = new Thickness(8, 4, 0, 4)
        };
        kindCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || kindCombo.SelectedItem is not string selected ||
                selected.Equals(GetCommandReferenceKind(effect), StringComparison.OrdinalIgnoreCase)) return;
            RemoveCaseInsensitiveAttribute(effect, "proto");
            RemoveCaseInsensitiveAttribute(effect, "tech");
            RemoveCaseInsensitiveAttribute(effect, "command");
            SetCaseInsensitiveAttribute(effect, selected switch
            {
                "Tech" => "tech",
                "Command" => "command",
                _ => "proto"
            }, "");
            MarkDirty(); UpdatePreview();
            _ = BuildEditorAsync();
        };
        row.Children.Add(kindCombo);

        var (attributeName, suggestions, width) = referenceKind switch
        {
            "Tech" => ("tech", _original.Keys.Concat(_modified.Keys), 200d),
            "Command" => ("command", _protoUnitCommandNames.AsEnumerable(), 180d),
            _ => ("proto", _protoUnitNames.AsEnumerable(), 180d)
        };
        row.Children.Add(CreateStrictEffectSelector(
            suggestions,
            GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, attributeName);
                else SetCaseInsensitiveAttribute(effect, attributeName, value);
                MarkDirty(); UpdatePreview();
            },
            width));

        if (includePosition)
        {
            row.Children.Add(CreateLabeledEffectSegment("Row", CreateUnsignedIntegerEffectBox(effect, "row", 60, "0"), leftSpacing: 8));
            row.Children.Add(CreateLabeledEffectSegment("Column", CreateUnsignedIntegerEffectBox(effect, "column", 60, "0"), leftSpacing: 8));
        }

        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private static string GetCommandReferenceKind(XElement effect)
    {
        if (GetCaseInsensitiveAttribute(effect, "tech") != null) return "Tech";
        if (GetCaseInsensitiveAttribute(effect, "command") != null) return "Command";
        return "Unit";
    }

    private void AddDamageByCostDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Damage type", CreateDataTypeCombo(effect, "damagetype", includeDivine: true), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddDamageFlagsDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));

        var selected = new HashSet<string>(
            (GetCaseInsensitiveAttribute(effect, "flags")?.Value ?? "")
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        var targets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var flag in ProtoConstants.KnownDamageAreaTargetFlags)
        {
            var checkBox = new CheckBox
            {
                Content = flag,
                IsChecked = selected.Contains(flag),
                IsEnabled = IsModifiedTab,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                if (checkBox.IsChecked == true) selected.Add(flag);
                else selected.Remove(flag);
                var value = string.Join("|", ProtoConstants.KnownDamageAreaTargetFlags.Where(selected.Contains));
                if (value.Length == 0) RemoveCaseInsensitiveAttribute(effect, "flags");
                else SetCaseInsensitiveAttribute(effect, "flags", value);
                MarkDirty(); UpdatePreview();
            };
            targets.Children.Add(checkBox);
        }
        row.Children.Add(CreateLabeledEffectSegment("Targets", targets, leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddDamageShadingDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Percent");
        EnsureFixedDataAttribute(effect, "threshold", "0");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "time", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);

        var currentShading = GetCaseInsensitiveAttribute(effect, "shadingtype")?.Value.Trim() ?? "";
        var shadingCombo = new ComboBox
        {
            ItemsSource = ProtoConstants.KnownShadingTypeDisplayNames,
            SelectedItem = ProtoConstants.KnownShadingTypeDisplayNames.FirstOrDefault(value => value.Equals(currentShading, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 120,
            Margin = new Thickness(0, 4, 0, 4)
        };
        shadingCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || shadingCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "shadingtype", ProtoConstants.GetShadingTypeXmlValue(selected));
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Shading", shadingCombo, leftSpacing: 8));

        var threshold = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, "threshold")?.Value ?? "0"), 70);
        EditorNumericInputBehavior.AttachRule(threshold, ProtoUnitNumericKind.ClampZeroToOne);
        threshold.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (!double.TryParse(threshold.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return;
            var clamped = Math.Clamp(parsed, 0d, 1d);
            var normalized = clamped.ToString("0.################", CultureInfo.InvariantCulture);
            if (!string.Equals(threshold.Text, normalized, StringComparison.Ordinal))
            {
                threshold.Text = normalized;
                threshold.CaretIndex = normalized.Length;
            }
            SetCaseInsensitiveAttribute(effect, "threshold", normalized);
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Threshold", threshold, leftSpacing: 8));

        row.Children.Add(CreateLabeledEffectSegment("Rate", CreateSignedFloatEffectBox(effect, "amount", 70, "0"), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Interval (ms)", CreateUnsignedIntegerEffectBox(effect, "time", 70, "0"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddProtoUnitFlagDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Flag", CreateStrictEffectSelector(
            ProtoConstants.KnownFlags,
            GetCaseInsensitiveAttribute(effect, "flag")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "flag");
                else SetCaseInsensitiveAttribute(effect, "flag", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddProtoActionFlagDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Flag", CreateStrictEffectSelector(
            ProtoActionMetadataCatalog.GetKnownFlagTags(),
            GetCaseInsensitiveAttribute(effect, "flag")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "flag");
                else SetCaseInsensitiveAttribute(effect, "flag", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private ComboBox CreateEnableDisableAmountCombo(XElement effect, string relativity = "Assign", double leftMargin = 8)
    {
        var currentAmount = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "1";
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Enable", "Disable" },
            SelectedItem = currentAmount == "0" ? "Disable" : "Enable",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(leftMargin, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Disable" ? "0" : "1");
            SetCaseInsensitiveAttribute(effect, "relativity", relativity);
            MarkDirty(); UpdatePreview();
        };
        return combo;
    }

    private void AddLifespanDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row);

        var updatePercent = new CheckBox
        {
            Content = "Update lifespan as percent",
            IsChecked = GetCaseInsensitiveAttribute(effect, "updateLifespanAsPercent") != null,
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        updatePercent.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (updatePercent.IsChecked == true) SetCaseInsensitiveAttribute(effect, "updateLifespanAsPercent", "");
            else RemoveCaseInsensitiveAttribute(effect, "updateLifespanAsPercent");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(updatePercent);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddUnitRegenRateLimitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);

        var target = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var playerMode = target != null &&
            GetCaseInsensitiveAttribute(target, "type")?.Value.Equals("Player", StringComparison.OrdinalIgnoreCase) == true;
        if (IsModifiedTab && playerMode && target != null)
        {
            var misplacedUnitType = GetCaseInsensitiveAttribute(target, "unittype")?.Value.Trim() ?? "";
            if (GetCaseInsensitiveAttribute(effect, "unittype") == null && !string.IsNullOrWhiteSpace(misplacedUnitType))
                SetCaseInsensitiveAttribute(effect, "unittype", misplacedUnitType);
            if (GetCaseInsensitiveAttribute(target, "unittype") != null)
            {
                RemoveCaseInsensitiveAttribute(target, "unittype");
                MarkDirty();
                UpdatePreview();
            }
        }
        var targetKind = new ComboBox
        {
            ItemsSource = new[] { "Player", "Unit" },
            SelectedItem = playerMode ? "Player" : "Unit",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(targetKind);
        if (playerMode)
        {
            row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
                _prereqUnitNames,
                GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
                value =>
                {
                    var currentTarget = EnsureDataTarget(effect);
                    SetCaseInsensitiveAttribute(currentTarget, "type", "Player");
                    RemoveCaseInsensitiveAttribute(currentTarget, "unittype");
                    if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                    else SetCaseInsensitiveAttribute(effect, "unittype", value);
                    MarkDirty();
                    UpdatePreview();
                },
                200), leftSpacing: 8));
        }
        else
        {
            row.Children.Add(CreateStrictEffectSelector(_prereqUnitNames, target?.Value.Trim() ?? "", value =>
            {
                var currentTarget = EnsureDataTarget(effect);
                SetCaseInsensitiveAttribute(currentTarget, "type", "ProtoUnit");
                RemoveCaseInsensitiveAttribute(effect, "unittype");
                currentTarget.Value = value;
                MarkDirty();
                UpdatePreview();
            }, 200));
        }

        targetKind.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || targetKind.SelectedItem is not string selected) return;
            var currentTarget = EnsureDataTarget(effect);
            if (selected == "Player")
            {
                var unitValue = currentTarget.Value.Trim();
                var misplacedUnitType = GetCaseInsensitiveAttribute(currentTarget, "unittype")?.Value.Trim() ?? "";
                currentTarget.RemoveNodes();
                SetCaseInsensitiveAttribute(currentTarget, "type", "Player");
                RemoveCaseInsensitiveAttribute(currentTarget, "unittype");
                if (GetCaseInsensitiveAttribute(effect, "unittype") == null)
                {
                    var selectedUnitType = !string.IsNullOrWhiteSpace(unitValue) ? unitValue : misplacedUnitType;
                    if (!string.IsNullOrWhiteSpace(selectedUnitType))
                        SetCaseInsensitiveAttribute(effect, "unittype", selectedUnitType);
                }
            }
            else
            {
                var unitType = GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(unitType))
                    unitType = GetCaseInsensitiveAttribute(currentTarget, "unittype")?.Value.Trim() ?? "";
                RemoveCaseInsensitiveAttribute(effect, "unittype");
                RemoveCaseInsensitiveAttribute(currentTarget, "unittype");
                SetCaseInsensitiveAttribute(currentTarget, "type", "ProtoUnit");
                currentTarget.Value = unitType;
            }
            MarkDirty();
            UpdatePreview();
            _ = BuildEditorAsync();
        };

        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddMinWorkRateDataEffectEditor(
        XElement effect,
        StackPanel content,
        string? defaultAction = null,
        bool allowOverride = true)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, defaultAction);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);

        var currentFor = GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "";
        var resourceMode = ProtoConstants.KnownResourceTypes.Contains(currentFor, StringComparer.OrdinalIgnoreCase);
        var forKind = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Resource" },
            SelectedItem = resourceMode ? "Resource" : "Unit",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        row.Children.Add(CreateLabeledEffectSegment("For", forKind, leftSpacing: 8));

        if (resourceMode)
        {
            row.Children.Add(CreateResourceValueCombo(effect, "unittype"));
        }
        else
        {
            row.Children.Add(CreateStrictEffectSelector(_prereqUnitNames, currentFor, value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            }, 180));
        }

        forKind.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || forKind.SelectedItem is not string selected) return;
            RemoveCaseInsensitiveAttribute(effect, "unittype");
            if (selected == "Resource") SetCaseInsensitiveAttribute(effect, "unittype", ProtoConstants.KnownResourceTypes[0]);
            MarkDirty(); UpdatePreview();
            _ = BuildEditorAsync();
        };
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddWorkRateSpecificDataEffectEditor(
        XElement effect,
        StackPanel content,
        string? defaultAction = null,
        bool allowOverride = true)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, defaultAction);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        row.Children.Add(CreateLabeledEffectSegment("For", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource"), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddContainingUnitAmountDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Containing", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            180), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private ComboBox CreateResourceValueCombo(XElement effect, string attributeName)
    {
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? ProtoConstants.KnownResourceTypes[0];
        var combo = new ComboBox
        {
            ItemsSource = ProtoConstants.KnownResourceTypes,
            SelectedItem = ProtoConstants.KnownResourceTypes.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? ProtoConstants.KnownResourceTypes[0],
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, selected);
            MarkDirty(); UpdatePreview();
        };
        return combo;
    }

    private void AddModifyReplacementDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Replaced by", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "proto")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "proto");
                else SetCaseInsensitiveAttribute(effect, "proto", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Type", CreateStrictEffectSelector(
            ProtoConstants.KnownReplacementTypes,
            GetCaseInsensitiveAttribute(effect, "replacementtype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "replacementtype");
                else SetCaseInsensitiveAttribute(effect, "replacementtype", value);
                MarkDirty(); UpdatePreview();
            },
            150), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddModifySpawnDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Spawn", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "proto")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "proto");
                else SetCaseInsensitiveAttribute(effect, "proto", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Type", CreateStrictEffectSelector(
            ProtoConstants.KnownSpawnTypes,
            GetCaseInsensitiveAttribute(effect, "spawntype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "spawntype");
                else SetCaseInsensitiveAttribute(effect, "spawntype", value);
                MarkDirty(); UpdatePreview();
            },
            150), leftSpacing: 8));
        AddOptionalEffectAttribute(row, effect, "Chance", "chance", "0", 70, ProtoUnitNumericKind.SignedFloat);
        AddOptionalEffectAttribute(row, effect, "Lifespan", "lifespan", "1", 70, ProtoUnitNumericKind.PositiveFloat, requirePositive: true);
        AddOptionalPlacementCheck(row, effect);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOptionalPlacementCheck(WrapPanel row, XElement effect)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, "placementcheck");
        if (attribute == null)
        {
            if (!IsModifiedTab) return;
            var button = CreateOptionalPropertyButton("Placement check");
            button.Click += (_, _) =>
            {
                SetCaseInsensitiveAttribute(effect, "placementcheck", "TerrainOnly");
                MarkDirty();
                _ = BuildEditorAsync();
            };
            row.Children.Add(button);
            return;
        }

        var combo = new ComboBox
        {
            ItemsSource = new[] { "Default", "TerrainOnly" },
            SelectedItem = attribute.Value.Equals("TerrainOnly", StringComparison.OrdinalIgnoreCase) ? "TerrainOnly" : "Default",
            IsEnabled = IsModifiedTab,
            Width = 120,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            if (selected == "Default") RemoveCaseInsensitiveAttribute(effect, "placementcheck");
            else SetCaseInsensitiveAttribute(effect, "placementcheck", "TerrainOnly");
            MarkDirty(); UpdatePreview();
            if (selected == "Default") _ = BuildEditorAsync();
        };
        row.Children.Add(CreateLabeledEffectSegment("Placement check", combo, leftSpacing: 8));
        if (IsModifiedTab)
            row.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "placementcheck")));
    }

    private void AddOnDamageModifyDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row);

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        var modifyTypeSelector = CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                if (normalized is not "DamageSpecific" and not "ArmorSpecific")
                    RemoveCaseInsensitiveAttribute(effect, "damagetype");
                MarkDirty(); UpdatePreview();
                _ = BuildEditorAsync();
            },
            180);
        row.Children.Add(CreateLabeledEffectSegment("Modify type", modifyTypeSelector, leftSpacing: 8));

        if (currentModifyType is "DamageSpecific" or "ArmorSpecific")
        {
            if (IsModifiedTab && GetCaseInsensitiveAttribute(effect, "damagetype") == null)
                EnsureFixedDataAttribute(effect, "damagetype", "Hack");
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateRequiredDataTypeCombo(effect, currentModifyType == "DamageSpecific"),
                leftSpacing: 8));
        }
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddProjectileDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureDefaultDataAction(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Projectile", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddRechargeTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Recharge type", CreateStrictEffectSelector(
            (new[] { "Time" }).Concat(ProtoConstants.KnownRechargeTypes),
            GetCaseInsensitiveAttribute(effect, "rechargetype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "rechargetype");
                else SetCaseInsensitiveAttribute(effect, "rechargetype", value);
                MarkDirty(); UpdatePreview();
            },
            150), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSelfDestructProtoActionDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateStrictEffectSelector(
            _protoActionNames,
            GetCaseInsensitiveAttribute(effect, "protoaction")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "protoaction");
                else SetCaseInsensitiveAttribute(effect, "protoaction", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSetUnitTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Unit type", CreateStrictEffectSelector(
            ProtoConstants.KnownUnitTypes,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddContainedTypeDataEffectEditor(XElement effect, StackPanel content, string subtype)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var label = subtype.ToLowerInvariant() switch
        {
            "addcontainedtype" => "Contain",
            "addnotcontainedtype" => "Not contain",
            "addsharedbuildlimitunittype" => "Shared with",
            "addveterancyexcludetype" => "Exclude type",
            "addveterancyincludetype" => "Include type",
            _ => "Unit type"
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment(label, CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private ComboBox CreateRequiredDataTypeCombo(XElement effect, bool includeDivine)
    {
        var options = includeDivine ? ProtoConstants.KnownDamageTypes : ProtoConstants.KnownArmorTypes;
        var current = GetCaseInsensitiveAttribute(effect, "damagetype")?.Value.Trim() ?? options[0];
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? options[0],
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "damagetype", selected);
            MarkDirty(); UpdatePreview();
        };
        return combo;
    }

    private ComboBox CreateDataTypeCombo(XElement effect, string attributeName, bool includeDivine)
    {
        var options = includeDivine
            ? new[] { "All", "Hack", "Pierce", "Crush", "Divine" }
            : new[] { "All", "Hack", "Pierce", "Crush" };
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "All";
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            if (selected.Equals("All", StringComparison.OrdinalIgnoreCase)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, selected);
            MarkDirty(); UpdatePreview();
        };
        return combo;
    }

    private TextBox CreateUnsignedIntegerEffectBox(XElement effect, string attributeName, double width, string defaultValue)
    {
        var box = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? defaultValue), width);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        };
        return box;
    }

    private TextBox CreateSignedIntegerEffectBox(XElement effect, string attributeName, double width, string defaultValue)
    {
        var box = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? defaultValue), width);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedInteger);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty();
            UpdatePreview();
        };
        return box;
    }

    private void EnsureExactDataAttribute(XElement effect, string attributeName, string value)
    {
        if (!IsModifiedTab || string.Equals(GetCaseInsensitiveAttribute(effect, attributeName)?.Value, value, StringComparison.OrdinalIgnoreCase)) return;
        SetCaseInsensitiveAttribute(effect, attributeName, value);
        MarkDirty(); UpdatePreview();
    }

    private void EnsureFixedDataAttribute(XElement effect, string attributeName, string defaultValue)
    {
        if (!IsModifiedTab || GetCaseInsensitiveAttribute(effect, attributeName) != null) return;
        SetCaseInsensitiveAttribute(effect, attributeName, defaultValue);
        MarkDirty(); UpdatePreview();
    }

    private TextBox CreateSignedFloatEffectBox(XElement effect, string attributeName, double width, string defaultValue)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute?.Value ?? defaultValue), width);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        };
        return box;
    }

    private TextBox CreateFreeTextEffectAttributeBox(XElement effect, string attributeName, double width)
    {
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? "",
            IsEnabled = IsModifiedTab,
            Width = width,
            MaxWidth = width,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = width;
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (string.IsNullOrWhiteSpace(box.Text)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? "");
            MarkDirty(); UpdatePreview();
        };
        return box;
    }

    private TextBox CreateInternalNameEffectAttributeBox(XElement effect, string attributeName, double width)
    {
        var stored = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "";
        var current = new string(stored.Where(InternalNamePolicy.IsAllowedCharacter).ToArray());
        if (IsModifiedTab && !current.Equals(stored, StringComparison.Ordinal))
        {
            if (current.Length == 0) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, current);
            MarkDirty();
            UpdatePreview();
        }
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = current,
            IsEnabled = IsModifiedTab,
            Width = width,
            MaxWidth = width,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = width;
        var updating = false;
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || updating) return;
            var filtered = new string((box.Text ?? "").Where(InternalNamePolicy.IsAllowedCharacter).ToArray());
            if (!filtered.Equals(box.Text, StringComparison.Ordinal))
            {
                updating = true;
                box.Text = filtered;
                box.CaretIndex = filtered.Length;
                updating = false;
            }
            if (filtered.Length == 0) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, filtered);
            MarkDirty();
            UpdatePreview();
        };
        return box;
    }

    private void AddEnableDisableUnitDataEffectEditor(XElement effect, StackPanel content, bool includeAction)
    {
        EnsureDefaultEnableDisableDataState(effect);
        if (includeAction) EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        if (includeAction)
        {
            var actionLabel = CreateInlineLabel("Action");
            actionLabel.Margin = new Thickness(8, 4, 8, 4);
            row.Children.Add(actionLabel);
            row.Children.Add(CreateDataActionSelector(effect));
        }

        var currentAmount = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "1";
        var stateCombo = new ComboBox
        {
            ItemsSource = new[] { "Enable", "Disable" },
            SelectedItem = currentAmount == "0" ? "Disable" : "Enable",
            IsEnabled = IsModifiedTab,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        stateCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || stateCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Disable" ? "0" : "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(stateCombo);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSharedBuildLimitUnitDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Shared with", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unitType")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unitType");
                else SetCaseInsensitiveAttribute(effect, "unitType", value);
                MarkDirty();
                UpdatePreview();
            },
            180), leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOnHitEffectDataEffectEditor(XElement effect, StackPanel content)
    {
        if (IsModifiedTab && TechnologyOnHitEffectRules.Normalize(effect))
        {
            MarkDirty();
            UpdatePreview();
        }
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));

        var effectType = GetCaseInsensitiveAttribute(effect, "effecttype")?.Value.Trim() ?? "";
        row.Children.Add(CreateLabeledEffectSegment("Effect", CreateStrictEffectSelector(
            ProtoConstants.KnownOnHitEffectTypes,
            effectType,
            value =>
            {
                if (value.Equals(effectType, StringComparison.OrdinalIgnoreCase)) return;
                _openOnHitOptionalSelectors.RemoveWhere(entry => ReferenceEquals(entry.Effect, effect));
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "effecttype");
                else SetCaseInsensitiveAttribute(effect, "effecttype", value);
                TechnologyOnHitEffectRules.Normalize(effect);
                MarkDirty();
                UpdatePreview();
                _ = BuildEditorAsync();
            },
            180), leftSpacing: 8));

        AddOptionalEffectAttribute(
            row,
            effect,
            "Duration",
            "duration",
            "1",
            70,
            ProtoUnitNumericKind.PositiveFloat,
            requirePositive: true,
            buttonLeftSpacing: 8,
            offerWhenMissing: TechnologyOnHitEffectRules.OffersDuration(effectType));
        AddOnHitOptionalReferenceSelector(row, effect, "Target", "Target", "targettype", _prereqUnitNames, 180, showWhenMissing: false);

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        if (TechnologyOnHitEffectRules.UsesModify(effectType))
        {
            row.Children.Add(CreateLabeledEffectSegment("Modify", CreateStrictEffectSelector(
                ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
                ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
                value =>
                {
                    var normalized = ProtoConstants.GetModifyTypeValue(value);
                    if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                    else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                    TechnologyOnHitEffectRules.Normalize(effect);
                    MarkDirty();
                    UpdatePreview();
                    _ = BuildEditorAsync();
                },
                180), leftSpacing: 8));
        }

        AddOnHitDamageTypeEditor(
            row,
            effect,
            showWhenMissing: TechnologyOnHitEffectRules.RequiresDamageType(effectType, currentModifyType),
            offerWhenMissing: TechnologyOnHitEffectRules.OffersDamageType(effectType),
            armorOnly: currentModifyType.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase));

        AddOnHitOptionalReferenceSelector(
            row,
            effect,
            "Protounit",
            TechnologyOnHitEffectRules.GetProtoFieldLabel(effectType),
            "proto",
            _protoUnitNames,
            180,
            showWhenMissing: TechnologyOnHitEffectRules.AutomaticProtoEffectTypes.Contains(effectType),
            offerWhenMissing: TechnologyOnHitEffectRules.OffersProto(effectType));

        AddOnHitFreezeTypeEditor(row, effect, effectType);
        if (TechnologyOnHitEffectRules.UsesProgressiveFreezeDuration(effectType))
            AddOnHitProgressiveFreezeDurationEditor(row, effect);

        row.Children.Add(CreateLabeledEffectSegment(
            "Amount",
            CreateSignedFloatEffectBox(effect, "amount", 80, "0"),
            leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOnHitEffectAttributeDataEffectEditor(XElement effect, StackPanel content, string subtype)
    {
        if (IsModifiedTab && TechnologyOnHitEffectRules.NormalizeAttributeSubtype(effect, subtype))
        {
            MarkDirty();
            UpdatePreview();
        }
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect), leftSpacing: 8));

        if (subtype.Equals("OnHitEffectActive", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateOnHitEffectActiveStateCombo(effect));

        var effectType = GetCaseInsensitiveAttribute(effect, "effecttype")?.Value.Trim() ?? "";
        var effectTypeOptions = subtype.Equals("OnHitEffectStatModify", StringComparison.OrdinalIgnoreCase)
            ? TechnologyOnHitEffectRules.StatModifyEffectTypes
            : ProtoConstants.KnownOnHitEffectTypes;
        row.Children.Add(CreateLabeledEffectSegment("Effect", CreateStrictEffectSelector(
            effectTypeOptions,
            effectType,
            value =>
            {
                if (value.Equals(effectType, StringComparison.OrdinalIgnoreCase)) return;
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "effecttype");
                else SetCaseInsensitiveAttribute(effect, "effecttype", value);

                if (subtype.Equals("OnHitEffectRate", StringComparison.OrdinalIgnoreCase))
                {
                    if (effectType.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase))
                        RemoveCaseInsensitiveAttribute(effect, "dmgtype");
                    TechnologyOnHitEffectRules.NormalizeAttributeSubtype(effect, subtype);
                    _ = BuildEditorAsync();
                }
            },
            180), leftSpacing: 8));

        if (subtype.Equals("OnHitEffectAttachBone", StringComparison.OrdinalIgnoreCase))
        {
            row.Children.Add(CreateLabeledEffectSegment("Bone", CreateStrictEffectSelector(
                _boneNames,
                GetCaseInsensitiveAttribute(effect, "attachbone")?.Value.Trim() ?? "",
                value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "attachbone");
                    else SetCaseInsensitiveAttribute(effect, "attachbone", value);
                },
                180), leftSpacing: 8));
        }

        if (subtype.Equals("OnHitEffectRate", StringComparison.OrdinalIgnoreCase) &&
            (effectType.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase) ||
             GetCaseInsensitiveAttribute(effect, "dmgtype") != null))
        {
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateExplicitOnHitDamageTypeCombo(effect, includeAll: true),
                leftSpacing: 8));
        }

        if (subtype.Equals("OnHitEffectStatModify", StringComparison.OrdinalIgnoreCase))
            AddOnHitEffectStatModifyFields(effect, row);

        if (TechnologyOnHitEffectRules.UsesEditableAmount(subtype))
            AddDataRelativityAndAmountEditors(effect, row);

        AddOnHitOptionalReferenceSelector(
            row,
            effect,
            "Target",
            "Target",
            "targettype",
            _prereqUnitNames,
            180,
            showWhenMissing: false,
            buttonLeftSpacing: 8);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private ComboBox CreateOnHitEffectActiveStateCombo(XElement effect)
    {
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Enable", "Disable" },
            SelectedItem = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() == "0" ? "Disable" : "Enable",
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Disable" ? "0" : "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private ComboBox CreateExplicitOnHitDamageTypeCombo(XElement effect, bool includeAll)
    {
        var options = includeAll
            ? new[] { "All" }.Concat(ProtoConstants.KnownDamageTypes).ToArray()
            : ProtoConstants.KnownDamageTypes;
        var current = GetCaseInsensitiveAttribute(effect, "dmgtype")?.Value.Trim() ?? options[0];
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? options[0],
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "dmgtype", selected);
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void AddOnHitEffectStatModifyFields(XElement effect, WrapPanel row)
    {
        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        row.Children.Add(CreateLabeledEffectSegment("Modify", CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                if (TechnologyOnHitEffectRules.RequiresSpecificDamageType(currentModifyType) &&
                    !TechnologyOnHitEffectRules.RequiresSpecificDamageType(normalized))
                    RemoveCaseInsensitiveAttribute(effect, "dmgtype");
                _ = BuildEditorAsync();
            },
            180), leftSpacing: 8));

        var damageType = GetCaseInsensitiveAttribute(effect, "dmgtype");
        if (!TechnologyOnHitEffectRules.RequiresSpecificDamageType(currentModifyType) && damageType == null) return;

        var armorOnly = currentModifyType.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase);
        var options = armorOnly ? ProtoConstants.KnownArmorTypes : ProtoConstants.KnownDamageTypes;
        var currentDamageType = damageType?.Value.Trim() ?? "";
        var damageTypeOptions = options
            .Append(currentDamageType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var damageTypeCombo = new ComboBox
        {
            ItemsSource = damageTypeOptions,
            SelectedItem = damageTypeOptions.FirstOrDefault(value => value.Equals(currentDamageType, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 110,
            Margin = new Thickness(0, 4, 0, 4)
        };
        damageTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || damageTypeCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "dmgtype", selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Damage type", damageTypeCombo, leftSpacing: 8));
    }

    private void AddOnHitOptionalReferenceSelector(
        WrapPanel row,
        XElement effect,
        string buttonLabel,
        string fieldLabel,
        string attributeName,
        IEnumerable<string> options,
        double width,
        bool showWhenMissing,
        bool offerWhenMissing = true,
        double buttonLeftSpacing = 0)
    {
        var key = (effect, attributeName.ToLowerInvariant());
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null && !showWhenMissing && !_openOnHitOptionalSelectors.Contains(key))
        {
            if (!IsModifiedTab || !offerWhenMissing) return;
            var button = CreateOptionalPropertyButton(buttonLabel);
            button.Margin = new Thickness(buttonLeftSpacing, button.Margin.Top, button.Margin.Right, button.Margin.Bottom);
            button.Click += (_, _) =>
            {
                _openOnHitOptionalSelectors.Add(key);
                _ = BuildEditorAsync();
            };
            row.Children.Add(button);
            return;
        }

        var selector = CreateStrictEffectSelector(options, attribute?.Value.Trim() ?? "", value =>
        {
            if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, value);
            _openOnHitOptionalSelectors.Remove(key);
            MarkDirty();
            UpdatePreview();
        }, width);
        var segment = CreateLabeledEffectSegment(fieldLabel, selector, leftSpacing: 8);
        if (IsModifiedTab && !showWhenMissing && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
        {
            segment.Children.Add(CreateRemoveButton(() =>
            {
                RemoveCaseInsensitiveAttribute(effect, attributeName);
                _openOnHitOptionalSelectors.Remove(key);
            }));
        }
        row.Children.Add(segment);
    }

    private void AddOnHitDamageTypeEditor(WrapPanel row, XElement effect, bool showWhenMissing, bool offerWhenMissing, bool armorOnly)
    {
        const string attributeName = "dmgtype";
        var key = (effect, attributeName);
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null && !showWhenMissing && !_openOnHitOptionalSelectors.Contains(key))
        {
            if (!IsModifiedTab || !offerWhenMissing) return;
            var button = CreateOptionalPropertyButton("Damage type");
            button.Click += (_, _) =>
            {
                _openOnHitOptionalSelectors.Add(key);
                _ = BuildEditorAsync();
            };
            row.Children.Add(button);
            return;
        }

        var options = armorOnly ? ProtoConstants.KnownArmorTypes : ProtoConstants.KnownDamageTypes;
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(attribute?.Value.Trim() ?? "", StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, selected);
            _openOnHitOptionalSelectors.Remove(key);
            MarkDirty();
            UpdatePreview();
        };
        var segment = CreateLabeledEffectSegment("Damage type", combo, leftSpacing: 8);
        if (IsModifiedTab && !showWhenMissing && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
        {
            segment.Children.Add(CreateRemoveButton(() =>
            {
                RemoveCaseInsensitiveAttribute(effect, attributeName);
                _openOnHitOptionalSelectors.Remove(key);
            }));
        }
        row.Children.Add(segment);
    }

    private void AddOnHitFreezeTypeEditor(WrapPanel row, XElement effect, string effectType)
    {
        if (!TechnologyOnHitEffectRules.UsesFreezeType(effectType)) return;

        const string attributeName = "freezetype";
        var key = (effect, attributeName);
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        var required = effectType.Equals("Freeze", StringComparison.OrdinalIgnoreCase);
        if (!required && attribute == null && !_openOnHitOptionalSelectors.Contains(key))
        {
            if (!IsModifiedTab) return;
            var button = CreateOptionalPropertyButton("Type");
            button.Click += (_, _) =>
            {
                _openOnHitOptionalSelectors.Add(key);
                _ = BuildEditorAsync();
            };
            row.Children.Add(button);
            return;
        }

        var current = ProtoConstants.GetOnHitEffectFreezeTypeDisplayName(attribute?.Value);
        var combo = new ComboBox
        {
            ItemsSource = ProtoConstants.KnownOnHitEffectFreezeTypeDisplayNames,
            SelectedItem = ProtoConstants.KnownOnHitEffectFreezeTypeDisplayNames.FirstOrDefault(value =>
                value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 130,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, ProtoConstants.GetOnHitEffectFreezeTypeXmlValue(selected));
            _openOnHitOptionalSelectors.Remove(key);
            MarkDirty();
            UpdatePreview();
        };
        var segment = CreateLabeledEffectSegment("Type", combo, leftSpacing: 8);
        if (!required && IsModifiedTab && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
        {
            segment.Children.Add(CreateRemoveButton(() =>
            {
                RemoveCaseInsensitiveAttribute(effect, attributeName);
                _openOnHitOptionalSelectors.Remove(key);
            }));
        }
        row.Children.Add(segment);
    }

    private void AddOnHitProgressiveFreezeDurationEditor(WrapPanel row, XElement effect)
    {
        var box = CreateNumericTextBox(
            FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, "progFreezeDuration")?.Value ?? "1"),
            80);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.PositiveFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            var value = box.Text ?? "";
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0) return;
            SetCaseInsensitiveAttribute(effect, "progFreezeDuration", value);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Duration", box, leftSpacing: 8));
    }

    private void AddMovementTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);

        var movementTypes = ProtoConstants.FieldSuggestions.TryGetValue("movementtype", out var knownMovementTypes)
            ? knownMovementTypes
            : [];
        var currentMovementType = GetCaseInsensitiveAttribute(effect, "movementtype")?.Value.Trim() ?? "";
        var movementTypeCombo = new ComboBox
        {
            ItemsSource = movementTypes,
            SelectedItem = movementTypes.FirstOrDefault(value => value.Equals(currentMovementType, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedTab,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        movementTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || movementTypeCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "movementtype", selected);
            SetCaseInsensitiveAttribute(effect, "amount", "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Set movement type to", movementTypeCombo, leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddRevealLosDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Absolute");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment(
            "LOS revealed",
            CreateEnableDisableAmountCombo(effect, relativity: "Absolute", leftMargin: 0),
            leftSpacing: 8));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void EnsureDefaultEnableDisableDataState(XElement effect)
    {
        if (!IsModifiedTab) return;
        var changed = false;
        if (GetCaseInsensitiveAttribute(effect, "amount") == null)
        {
            SetCaseInsensitiveAttribute(effect, "amount", "1");
            changed = true;
        }
        if (!string.Equals(GetCaseInsensitiveAttribute(effect, "relativity")?.Value, "Assign", StringComparison.OrdinalIgnoreCase))
        {
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            changed = true;
        }
        if (changed)
        {
            MarkDirty();
            UpdatePreview();
        }
    }

    private void AddDataTargetEditor(XElement effect, WrapPanel row)
    {
        row.Children.Add(CreateInlineLabel("Unit"));
        var target = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        row.Children.Add(CreateStrictEffectSelector(_prereqUnitNames, target?.Value.Trim() ?? "", value =>
        {
            var currentTarget = EnsureDataTarget(effect);
            currentTarget.Value = value;
            MarkDirty();
            UpdatePreview();
        }, 200));
    }

    private void AddDataIgnoreNatureEditor(XElement effect, WrapPanel row)
    {
        var target = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var ignoreNature = new CheckBox
        {
            Content = "Not Nature",
            IsChecked = target?.Attributes().Any(a => a.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase)) == true,
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ignoreNature.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            var currentTarget = EnsureDataTarget(effect);
            if (ignoreNature.IsChecked == true) SetCaseInsensitiveAttribute(currentTarget, "ignoreNature", "");
            else RemoveCaseInsensitiveAttribute(currentTarget, "ignoreNature");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(ignoreNature);
    }

    private XElement EnsureDataTarget(XElement effect)
    {
        var target = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            if (target.Attribute("type") == null) target.SetAttributeValue("type", "ProtoUnit");
            return target;
        }

        target = new XElement("target", new XAttribute("type", "ProtoUnit"));
        effect.Add(target);
        return target;
    }

    private void AddDataRelativityAndAmountEditors(XElement effect, WrapPanel row, bool allowOverride = false)
    {
        var currentRelativity = GetCaseInsensitiveAttribute(effect, "relativity")?.Value ?? "BasePercent";
        var relativityOptions = new List<string> { "Add", "Multiply", "Multiply base", "Set to" };
        if (allowOverride || currentRelativity.Equals("Override", StringComparison.OrdinalIgnoreCase))
            relativityOptions.Add("Override");
        var relativityCombo = new ComboBox
        {
            ItemsSource = relativityOptions,
            SelectedItem = RelativityToDisplay(currentRelativity),
            IsEnabled = IsModifiedTab,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        relativityCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || relativityCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(relativityCombo);

        var amount = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? "0"), 80);
        EditorNumericInputBehavior.AttachRule(amount, ProtoUnitNumericKind.SignedFloat);
        amount.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "amount", amount.Text ?? "0");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Amount", amount));
    }

    private ComboBox CreateRestrictedDataRelativityCombo(XElement effect, IReadOnlyList<string> allowedRelativities)
    {
        var currentRelativity = GetCaseInsensitiveAttribute(effect, "relativity")?.Value ?? allowedRelativities[0];
        var relativityValues = allowedRelativities
            .Append(currentRelativity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var displayValues = relativityValues.Select(RelativityToDisplay).ToList();
        var combo = new ComboBox
        {
            ItemsSource = displayValues,
            SelectedItem = RelativityToDisplay(currentRelativity),
            IsEnabled = IsModifiedTab,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void ResetDataEffectForSubtype(XElement effect, string subtype)
    {
        var metadata = effect.Attributes()
            .Where(a => a.Name.LocalName.Equals("hideTooltip", StringComparison.OrdinalIgnoreCase)
                     || a.Name.LocalName.Equals("delay", StringComparison.OrdinalIgnoreCase)
                     || a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase))
            .Select(a => new XAttribute(a))
            .ToList();

        effect.RemoveAttributes();
        effect.RemoveNodes();
        effect.SetAttributeValue("type", "Data");
        if (!string.IsNullOrWhiteSpace(subtype))
            effect.SetAttributeValue("subtype", subtype);
        foreach (var attribute in metadata)
            effect.Add(attribute);
    }

    private void EnsureDefaultDataRelativity(XElement effect)
    {
        if (!IsModifiedTab || GetCaseInsensitiveAttribute(effect, "relativity") != null) return;
        SetCaseInsensitiveAttribute(effect, "relativity", "BasePercent");
        MarkDirty();
        UpdatePreview();
    }

    private void EnsureDefaultDataAction(XElement effect, string? defaultAction = null)
    {
        if (!IsModifiedTab) return;
        if (GetCaseInsensitiveAttribute(effect, "action") != null || GetCaseInsensitiveAttribute(effect, "allactions") != null) return;
        if (string.IsNullOrWhiteSpace(defaultAction)) SetCaseInsensitiveAttribute(effect, "allactions", "1");
        else SetCaseInsensitiveAttribute(effect, "action", defaultAction);
        MarkDirty();
        UpdatePreview();
    }

    private AutoCompleteBox CreateDataActionSelector(XElement effect)
    {
        var actionAttribute = GetCaseInsensitiveAttribute(effect, "action")?.Value.Trim() ?? "";
        var allActions = GetCaseInsensitiveAttribute(effect, "allactions")?.Value.Trim();
        var current = string.IsNullOrWhiteSpace(actionAttribute) && !string.Equals(allActions, "0", StringComparison.OrdinalIgnoreCase)
            ? "All"
            : actionAttribute;
        var suggestions = new[] { "All" }
            .Concat(_protoActionNames.Where(value => !value.Equals("All", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selector = CreateStrictEffectSelector(suggestions, current, value =>
        {
            if (value.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                RemoveCaseInsensitiveAttribute(effect, "action");
                SetCaseInsensitiveAttribute(effect, "allactions", "1");
            }
            else
            {
                RemoveCaseInsensitiveAttribute(effect, "allactions");
                SetCaseInsensitiveAttribute(effect, "action", value);
            }
            MarkDirty();
            UpdatePreview();
        }, 165, preserveSuggestionOrder: true);
        selector.ItemTemplate = new FuncDataTemplate<string>((item, _) => new TextBlock
        {
            Text = item ?? "",
            FontWeight = string.Equals(item, "All", StringComparison.OrdinalIgnoreCase) ? FontWeight.Bold : FontWeight.Normal
        });
        return selector;
    }

    private static string RelativityToDisplay(string relativity)
        => relativity.ToLowerInvariant() switch
        {
            "percent" => "Multiply",
            "basepercent" => "Multiply base",
            "assign" => "Set to",
            "override" => "Override",
            _ => "Add"
        };

    private static string DisplayToRelativity(string display)
        => display switch
        {
            "Multiply" => "Percent",
            "Multiply base" => "BasePercent",
            "Set to" => "Assign",
            "Override" => "Override",
            _ => "Absolute"
        };

    private async Task AddSetNameEffectEditorAsync(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var protoAttribute = GetCaseInsensitiveAttribute(effect, "proto");
        var techAttribute = GetCaseInsensitiveAttribute(effect, "tech");
        var targetKind = techAttribute != null ? "Tech" : "Unit";
        var targetKindCombo = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Tech" },
            SelectedItem = targetKind,
            Width = 90,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 8, 4)
        };
        row.Children.Add(targetKindCombo);

        var targetOptions = targetKind == "Tech" ? _original.Keys.Concat(_modified.Keys) : _protoUnitNames;
        var targetValue = targetKind == "Tech" ? techAttribute?.Value ?? "" : protoAttribute?.Value ?? "";
        var targetSelector = CreateStrictEffectSelector(targetOptions, targetValue, value =>
        {
            var kind = targetKindCombo.SelectedItem?.ToString() ?? targetKind;
            _ = HandleSetNameTargetChangedAsync(effect, kind, value);
        }, targetKind == "Tech" ? 200 : 150);
        row.Children.Add(targetSelector);

        var newNameLabel = CreateInlineLabel("New name");
        newNameLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(newNameLabel);
        var newNameAttribute = GetCaseInsensitiveAttribute(effect, "newName");
        if (newNameAttribute != null)
        {
            var newNameText = await ResolveTechnologyStringValueAsync(newNameAttribute.Value.Trim());
            row.Children.Add(CreateEffectStringTextBox(newNameAttribute, newNameText, 200));
        }
        else
        {
            string? createdId = null;
            var newNameBox = EditorTextFieldStyle.ConfigureTextBox(new TextBox
            {
                Text = "",
                IsEnabled = IsModifiedTab,
                Width = 200,
                MaxWidth = 200,
                Margin = new Thickness(0, 4, 8, 4)
            });
            newNameBox.Width = newNameBox.MaxWidth = 200;
            newNameBox.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab || string.IsNullOrEmpty(newNameBox.Text)) return;
                createdId ??= BuildUniqueSetNameStringId(effect, "NEW_NAME");
                if (GetCaseInsensitiveAttribute(effect, "newName") == null)
                    SetCaseInsensitiveAttribute(effect, "newName", createdId);
                _pendingStringRemovals.Remove(createdId);
                _pendingStringUpdates[createdId] = newNameBox.Text ?? "";
                MarkDirty();
                UpdatePreview();
            };
            row.Children.Add(newNameBox);
        }

        AddSetNameOptionalStringButton(effect, row, "New rollover", "newRollover", "NEW_ROLLOVER");
        AddSetNameOptionalStringButton(effect, row, "New short rollover", "newShortRollover", "NEW_SHORT_ROLLOVER");

        var reqTech = GetCaseInsensitiveAttribute(effect, "reqTech");
        if (reqTech == null && IsModifiedTab)
        {
            var addReq = CreateOptionalPropertyButton("Tech Req");
            addReq.Margin = new Thickness(8, 4, 8, 4);
            addReq.Click += (_, _) => { SetCaseInsensitiveAttribute(effect, "reqTech", ""); MarkDirty(); _ = BuildEditorAsync(); };
            row.Children.Add(addReq);
        }
        else if (reqTech != null)
        {
            var reqLabel = CreateInlineLabel("Tech Req");
            reqLabel.Margin = new Thickness(8, 4, 8, 4);
            row.Children.Add(reqLabel);
            row.Children.Add(CreateStrictEffectSelector(_original.Keys.Concat(_modified.Keys), reqTech.Value, value => SetCaseInsensitiveAttribute(effect, "reqTech", value), 200));
            if (IsModifiedTab) row.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "reqTech")));
        }

        targetKindCombo.SelectionChanged += async (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || targetKindCombo.SelectedItem is not string kind) return;
            RemoveCaseInsensitiveAttribute(effect, "proto");
            RemoveCaseInsensitiveAttribute(effect, "tech");
            SetCaseInsensitiveAttribute(effect, kind == "Tech" ? "tech" : "proto", "");
            await RegenerateSetNameEffectStringIdsAsync(effect, removeOldIds: true);
            MarkDirty();
            _ = BuildEditorAsync();
        };

        content.Children.Add(row);

        foreach (var (attributeName, label) in new[] { ("newRollover", "New rollover"), ("newShortRollover", "New short rollover") })
        {
            var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            if (attribute != null)
                await AddEffectStringAttributeRowAsync(content, effect, attribute, label, 380, removable: true);
        }
    }

    private async Task HandleSetNameTargetChangedAsync(XElement effect, string targetKind, string value)
    {
        SetEffectExclusiveTarget(effect, targetKind == "Tech" ? "tech" : "proto", targetKind == "Tech" ? "proto" : "tech", value);
        await RegenerateSetNameEffectStringIdsAsync(effect, removeOldIds: true);
        MarkDirty();
        await BuildEditorAsync();
    }

    private void AddSetNameOptionalStringButton(XElement effect, WrapPanel row, string label, string attributeName, string suffix)
    {
        if (GetCaseInsensitiveAttribute(effect, attributeName) != null || !IsModifiedTab) return;
        var button = CreateOptionalPropertyButton(label);
        button.Click += (_, _) =>
        {
            var id = BuildUniqueSetNameStringId(effect, suffix);
            SetCaseInsensitiveAttribute(effect, attributeName, id);
            _pendingStringRemovals.Remove(id);
            _pendingStringUpdates[id] = "";
            MarkDirty();
            _ = BuildEditorAsync();
        };
        row.Children.Add(button);
    }

    private async Task AddTextOutputEffectEditorAsync(XElement effect, StackPanel content, bool allIsIntrinsic)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Message"));
        var stringId = effect.Value.Trim();
        if (string.IsNullOrWhiteSpace(stringId) && IsModifiedTab && _current != null)
        {
            stringId = BuildNextTextOutputStringId(_current, allIsIntrinsic ? "OUTPUTALL" : "OUTPUT");
            effect.Value = stringId;
            _pendingStringUpdates[stringId] = "";
        }
        var text = await ResolveTechnologyStringValueAsync(stringId);
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Width = 380,
            MaxWidth = 380,
            MinHeight = 32,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 8, 4)
        });
        box.Width = box.MaxWidth = 380;
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(stringId)) return;
            _pendingStringRemovals.Remove(stringId);
            _pendingStringUpdates[stringId] = box.Text ?? "";
            MarkDirty();
        };
        row.Children.Add(box);
        if (!allIsIntrinsic)
        {
            var all = new CheckBox { Content = "To all", IsChecked = GetCaseInsensitiveAttribute(effect, "all")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = IsModifiedTab, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            all.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                if (all.IsChecked == true) SetCaseInsensitiveAttribute(effect, "all", "true"); else RemoveCaseInsensitiveAttribute(effect, "all");
                MarkDirty(); UpdatePreview();
            };
            row.Children.Add(all);
        }
        content.Children.Add(row);
    }

    private void AddSetAgeEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Set"));
        var combo = new ComboBox { ItemsSource = TechnologyAges, SelectedItem = TechnologyAges.FirstOrDefault(a => a.Equals(effect.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? effect.Value.Trim(), Width = 150, IsEnabled = IsModifiedTab, Margin = new Thickness(0, 4, 0, 4) };
        combo.SelectionChanged += (_, _) => { if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string value) return; effect.Value = value; MarkDirty(); UpdatePreview(); };
        row.Children.Add(combo);
        content.Children.Add(row);
    }

    private void AddTechStatusEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Tech"));
        row.Children.Add(CreateStrictEffectSelector(_original.Keys.Concat(_modified.Keys), effect.Value.Trim(), value => { effect.Value = value; MarkDirty(); UpdatePreview(); }, 200));

        var statusLabel = CreateInlineLabel("Set status to");
        statusLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(statusLabel);
        var currentStatus = GetCaseInsensitiveAttribute(effect, "status")?.Value ?? "obtainable";
        var statusCombo = new ComboBox { ItemsSource = new[] { "Obtainable", "Active", "Unobtainable" }, SelectedItem = ToDisplayStatus(currentStatus), Width = 130, IsEnabled = IsModifiedTab, Margin = new Thickness(0, 4, 8, 4) };
        statusCombo.SelectionChanged += (_, _) => { if (_loadingUi || !IsModifiedTab || statusCombo.SelectedItem is not string value) return; SetCaseInsensitiveAttribute(effect, "status", value.ToLowerInvariant()); MarkDirty(); UpdatePreview(); };
        row.Children.Add(statusCombo);

        var majorGod = GetCaseInsensitiveAttribute(effect, "uiShowIfMajorGod");
        if (majorGod == null && IsModifiedTab)
        {
            var button = CreateOptionalPropertyButton("Show in UI if major god is");
            button.Margin = new Thickness(8, 4, 8, 4);
            button.Click += (_, _) => { SetCaseInsensitiveAttribute(effect, "uiShowIfMajorGod", ""); MarkDirty(); _ = BuildEditorAsync(); };
            row.Children.Add(button);
        }
        else if (majorGod != null)
        {
            var majorGodLabel = CreateInlineLabel("Show in UI if major god is");
            majorGodLabel.Margin = new Thickness(8, 4, 8, 4);
            row.Children.Add(majorGodLabel);
            row.Children.Add(CreateStrictEffectSelector(_majorGodNames, majorGod.Value, value => SetCaseInsensitiveAttribute(effect, "uiShowIfMajorGod", value), 150));
            if (IsModifiedTab) row.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "uiShowIfMajorGod")));
        }
        content.Children.Add(row);
    }

    private void AddSharedLosEffectEditor(XElement effect, StackPanel content)
    {
        var reveal = new CheckBox { Content = "Reveal all", IsChecked = GetCaseInsensitiveAttribute(effect, "all")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = IsModifiedTab, Margin = new Thickness(0, 4, 0, 4) };
        reveal.IsCheckedChanged += (_, _) => { if (_loadingUi || !IsModifiedTab) return; if (reveal.IsChecked == true) SetCaseInsensitiveAttribute(effect, "all", "true"); else RemoveCaseInsensitiveAttribute(effect, "all"); MarkDirty(); UpdatePreview(); };
        content.Children.Add(reveal);
    }

    private void AddModifyProtoUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Unit"));
        var proto = GetCaseInsensitiveAttribute(effect, "proto")?.Value ?? "";
        row.Children.Add(CreateStrictEffectSelector(_protoUnitNames, proto, value => SetCaseInsensitiveAttribute(effect, "proto", value), 150));

        var newNameSpacer = new Border { Width = 12 };
        row.Children.Add(newNameSpacer);
        AddOptionalEffectAttribute(row, effect, "New name", "newName", "", 150, numericKind: null);
        var newHpSpacer = new Border { Width = 12 };
        row.Children.Add(newHpSpacer);
        AddOptionalEffectAttribute(row, effect, "New HP", "newHP", "1", 70, ProtoUnitNumericKind.UnsignedInteger, requirePositive: true);

        var reset = new CheckBox { Content = "Reset quick action", IsChecked = GetCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = IsModifiedTab, Margin = new Thickness(8, 4, 0, 4) };
        reset.IsCheckedChanged += (_, _) => { if (_loadingUi || !IsModifiedTab) return; if (reset.IsChecked == true) SetCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex", "true"); else RemoveCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex"); MarkDirty(); UpdatePreview(); };
        row.Children.Add(reset);
        content.Children.Add(row);
    }

    private void AddTransformUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Transform from"));
        row.Children.Add(CreateStrictEffectSelector(_protoUnitNames, GetCaseInsensitiveAttribute(effect, "fromProtoID")?.Value ?? "", value => SetCaseInsensitiveAttribute(effect, "fromProtoID", value), 150));
        var toLabel = CreateInlineLabel("to");
        toLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(toLabel);
        row.Children.Add(CreateStrictEffectSelector(_protoUnitNames, GetCaseInsensitiveAttribute(effect, "toProtoID")?.Value ?? "", value => SetCaseInsensitiveAttribute(effect, "toProtoID", value), 150));
        var queued = new CheckBox { Content = "Include queued", IsChecked = GetCaseInsensitiveAttribute(effect, "includeQueued")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = IsModifiedTab, Margin = new Thickness(8, 4, 0, 4) };
        queued.IsCheckedChanged += (_, _) => { if (_loadingUi || !IsModifiedTab) return; if (queued.IsChecked == true) SetCaseInsensitiveAttribute(effect, "includeQueued", "true"); else RemoveCaseInsensitiveAttribute(effect, "includeQueued"); MarkDirty(); UpdatePreview(); };
        row.Children.Add(queued);
        content.Children.Add(row);
    }

    private void AddSetOnBuildingDeathTechEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Tech"));
        row.Children.Add(CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            effect.Value.Trim(),
            value => { effect.Value = value; MarkDirty(); UpdatePreview(); },
            200));

        var amountLabel = CreateInlineLabel("Amount");
        amountLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(amountLabel);
        var amount = GetCaseInsensitiveAttribute(effect, "amount");
        var amountBox = CreateNumericTextBox(FormatNumericForDisplay(amount?.Value ?? "0"), 70);
        EditorNumericInputBehavior.AttachRule(amountBox, ProtoUnitNumericKind.UnsignedFloat);
        amountBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "amount", amountBox.Text ?? "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(amountBox);

        var amount2Label = CreateInlineLabel("Amount 2");
        amount2Label.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(amount2Label);
        var amount2 = GetCaseInsensitiveAttribute(effect, "amount2");
        var amount2Box = CreateNumericTextBox(FormatNumericForDisplay(amount2?.Value ?? "0"), 70);
        EditorNumericInputBehavior.AttachRule(amount2Box, ProtoUnitNumericKind.UnsignedFloat);
        amount2Box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "amount2", amount2Box.Text ?? "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(amount2Box);
        content.Children.Add(row);
    }

    private void AddCreatePowerEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Power"));
        var attribute = GetCaseInsensitiveAttribute(effect, "protoPower");
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = attribute?.Value ?? "",
            IsEnabled = IsModifiedTab,
            Width = 200,
            MaxWidth = 200,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = 200;
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "protoPower", box.Text ?? "");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private void AddRandomTechEffectEditor(XElement effect, StackPanel content)
    {
        var firstRow = new WrapPanel { Orientation = Orientation.Horizontal };
        firstRow.Children.Add(CreateInlineLabel("Number of techs to select"));
        var selectAttribute = GetCaseInsensitiveAttribute(effect, "select");
        var selectBox = CreateNumericTextBox(FormatNumericForDisplay(selectAttribute?.Value ?? "1"), 70);
        EditorNumericInputBehavior.AttachRule(selectBox, ProtoUnitNumericKind.UnsignedInteger);
        selectBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (!int.TryParse(selectBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0) return;
            SetCaseInsensitiveAttribute(effect, "select", value.ToString(CultureInfo.InvariantCulture));
            MarkDirty(); UpdatePreview();
        };
        firstRow.Children.Add(selectBox);
        var setToLabel = CreateInlineLabel("Set to");
        setToLabel.Margin = new Thickness(8, 4, 8, 4);
        firstRow.Children.Add(setToLabel);
        var currentStatus = GetCaseInsensitiveAttribute(effect, "status")?.Value ?? "active";
        var statusCombo = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Active", "Unobtainable" },
            SelectedItem = ToDisplayStatus(currentStatus),
            Width = 130,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 0, 4)
        };
        statusCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || statusCombo.SelectedItem is not string value) return;
            SetCaseInsensitiveAttribute(effect, "status", value.ToLowerInvariant());
            MarkDirty(); UpdatePreview();
        };
        firstRow.Children.Add(statusCombo);
        content.Children.Add(firstRow);

        var techRow = new WrapPanel { Orientation = Orientation.Horizontal };
        techRow.Children.Add(CreateInlineLabel("Techs"));
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 0, 2) };
        AutoCompleteBox? picker = null;
        picker = CreateStrictEffectSelector(_original.Keys.Concat(_modified.Keys), "", value =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(value)) return;
            if (!effect.Elements().Any(e => e.Name.LocalName.Equals("Tech", StringComparison.OrdinalIgnoreCase) && e.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                effect.Add(new XElement("Tech", value));
                MarkDirty(); UpdatePreview();
            }
            if (picker != null)
            {
                picker.Text = "";
                picker.SelectedItem = null;
            }
            RenderChips(); RefreshPicker();
        }, 100);

        void RefreshPicker()
        {
            if (picker == null) return;
            var present = effect.Elements()
                .Where(e => e.Name.LocalName.Equals("Tech", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            picker.ItemsSource = _original.Keys.Concat(_modified.Keys)
                .Where(name => !present.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        void RenderChips()
        {
            chips.Children.Clear();
            foreach (var techElement in effect.Elements().Where(e => e.Name.LocalName.Equals("Tech", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var captured = techElement;
                chips.Children.Add(EditorChipService.CreateBlueChip(
                    captured.Value.Trim(),
                    IsModifiedTab ? () =>
                    {
                        captured.Remove();
                        MarkDirty(); UpdatePreview();
                        RenderChips(); RefreshPicker();
                    } : null,
                    readOnly: !IsModifiedTab));
            }
        }

        RefreshPicker();
        techRow.Children.Add(picker!);
        techRow.Children.Add(chips);
        RenderChips();
        content.Children.Add(techRow);
    }

    private async Task AddTextEffectOutputEditorAsync(XElement effect, StackPanel content)
    {
        if (_current == null) return;
        await AddTextEffectOutputStringFieldAsync(effect, content, "Message Self", "selfMsg", "SELFMSG");
        await AddTextEffectOutputStringFieldAsync(effect, content, "Message other", "playerMsg", "PLAYERMSG");
    }

    private async Task AddTextEffectOutputStringFieldAsync(XElement effect, StackPanel content, string label, string attributeName, string suffix)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null && IsModifiedTab && _current != null)
        {
            var id = BuildUniqueTechnologyEffectStringId(_current, suffix, effect);
            SetCaseInsensitiveAttribute(effect, attributeName, id);
            attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            _pendingStringRemovals.Remove(id);
            _pendingStringUpdates[id] = "";
        }
        if (attribute == null) return;

        var idValue = attribute.Value.Trim();
        var text = await ResolveTechnologyStringValueAsync(idValue);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel(label));
        var box = CreateMultilineEffectStringTextBox(attribute, text, 380);
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private TextBox CreateMultilineEffectStringTextBox(XAttribute attribute, string text, double width)
    {
        var id = attribute.Value.Trim();
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Width = width,
            MaxWidth = width,
            MinHeight = 32,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = width;
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(id)) return;
            _pendingStringRemovals.Remove(id);
            _pendingStringUpdates[id] = box.Text ?? "";
            MarkDirty();
        };
        return box;
    }

    private void AddForbidTechEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Tech", CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            effect.Value.Trim(),
            value => { effect.Value = value; MarkDirty(); UpdatePreview(); },
            200)));

        var amount = GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? "1";
        var mode = new ComboBox
        {
            ItemsSource = new[] { "Forbid", "Unforbid" },
            SelectedItem = amount.Trim().StartsWith("0", StringComparison.Ordinal) ? "Unforbid" : "Forbid",
            Width = 110,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(4, 4, 0, 4)
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || mode.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Forbid" ? "1" : "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(mode);
        content.Children.Add(row);
    }

    private void AddSetOnTechResearchedTechEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Tech type", CreateStrictEffectSelector(
            _techTypeNames,
            GetCaseInsensitiveAttribute(effect, "techType")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "techType", value),
            150)));

        var amount = GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? "1";
        var action = new ComboBox
        {
            ItemsSource = new[] { "Activates", "Disable" },
            SelectedItem = amount.Trim().StartsWith("0", StringComparison.Ordinal) ? "Disable" : "Activates",
            Width = 110,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(4, 4, 8, 4)
        };
        action.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || action.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Activates" ? "1" : "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(action);
        row.Children.Add(CreateLabeledEffectSegment("Tech", CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            effect.Value.Trim(),
            value => { effect.Value = value; MarkDirty(); UpdatePreview(); },
            200)));
        content.Children.Add(row);
    }

    private async Task AddUiAlertEffectEditorAsync(XElement effect, StackPanel content)
    {
        if (_current == null) return;
        await AddTextEffectOutputStringFieldAsync(effect, content, "Message Self", "selfMsg", "UIALERT_SELFMSG");
        await AddTextEffectOutputStringFieldAsync(effect, content, "Message other", "playerMsg", "UIALERT_PLAYERMSG");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var target = new ComboBox
        {
            ItemsSource = new[] { "Self", "Ally", "Enemy", "All" },
            SelectedItem = GetCaseInsensitiveAttribute(effect, "target")?.Value ?? "Self",
            Width = 100,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 8, 4)
        };
        row.Children.Add(CreateLabeledEffectSegment("Target", target));
        target.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || target.SelectedItem is not string value) return;
            SetCaseInsensitiveAttribute(effect, "target", value);
            MarkDirty(); UpdatePreview();
        };

        var playerName = new CheckBox
        {
            Content = "Include player name",
            IsChecked = GetCaseInsensitiveAttribute(effect, "playerName")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        playerName.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "playerName", playerName.IsChecked == true ? "True" : "False");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(playerName);

        var duration = GetCaseInsensitiveAttribute(effect, "duration");
        var durationBox = CreateNumericTextBox(FormatNumericForDisplay(duration?.Value ?? "2500"), 70);
        EditorNumericInputBehavior.AttachRule(durationBox, ProtoUnitNumericKind.UnsignedInteger);
        durationBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "duration", durationBox.Text ?? "2500");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Duration (ms)", durationBox));
        content.Children.Add(row);
    }


    private void AddCreateUnitEffectEditor(XElement effect, StackPanel content)
    {
        var pattern = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pattern", StringComparison.OrdinalIgnoreCase));

        var firstRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var quantityBox = CreateNumericTextBox(FormatNumericForDisplay((pattern == null ? null : GetCaseInsensitiveAttribute(pattern, "quantity"))?.Value ?? "1"), 70);
        EditorNumericInputBehavior.AttachRule(quantityBox, ProtoUnitNumericKind.PositiveInteger);
        quantityBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (!int.TryParse(quantityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0) return;
            var editablePattern = GetOrCreateCreateUnitPattern(effect);
            SetCaseInsensitiveAttribute(editablePattern, "quantity", quantity.ToString(CultureInfo.InvariantCulture));
            MarkDirty(); UpdatePreview();
        };
        firstRow.Children.Add(CreateLabeledEffectSegment("Creates", quantityBox));
        firstRow.Children.Add(CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unit")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unit", value),
            150));
        firstRow.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "generator")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "generator", value),
            150), leftSpacing: 8));

        firstRow.Children.Add(CreateCreateUnitPresenceCheckBox(effect, "allgenerators", "All generators", "true"));
        firstRow.Children.Add(CreateCreateUnitPresenceCheckBox(effect, "mute", "Mute", ""));

        var queue = new CheckBox
        {
            Content = "Queue",
            IsChecked = !string.Equals(GetCaseInsensitiveAttribute(effect, "queue")?.Value, "false", StringComparison.OrdinalIgnoreCase),
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        queue.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (queue.IsChecked == true) RemoveCaseInsensitiveAttribute(effect, "queue");
            else SetCaseInsensitiveAttribute(effect, "queue", "false");
            MarkDirty(); UpdatePreview();
        };
        firstRow.Children.Add(queue);
        firstRow.Children.Add(CreateCreateUnitPresenceCheckBox(effect, "ignorerally", "Ignore Rally", ""));

        // Keep the main CreateUnit controls in a wrapping row, but Pattern always starts a new row.
        content.Children.Add(firstRow);
        var patternRow = new WrapPanel { Orientation = Orientation.Horizontal };
        patternRow.Children.Add(CreateInlineLabel("Pattern"));
        var currentPatternType = (pattern == null ? null : GetCaseInsensitiveAttribute(pattern, "type"))?.Value ?? "Leaving";
        var patternTypes = new[] { "Simple", "Leaving", "Scatter" }
            .Append(currentPatternType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var patternType = new ComboBox
        {
            ItemsSource = patternTypes,
            SelectedItem = patternTypes.FirstOrDefault(value => value.Equals(currentPatternType, StringComparison.OrdinalIgnoreCase)),
            Width = 100,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 8, 4)
        };
        patternType.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || patternType.SelectedItem is not string value) return;
            SetCaseInsensitiveAttribute(GetOrCreateCreateUnitPattern(effect), "type", value);
            MarkDirty(); UpdatePreview();
        };
        patternRow.Children.Add(patternType);

        AddOptionalPatternFloat(patternRow, effect, pattern, "Speed", "speed", "0", signed: false);
        AddOptionalPatternFloat(patternRow, effect, pattern, "Min radius", "minradius", "0", signed: false);
        AddOptionalPatternFloat(patternRow, effect, pattern, "Radius", "radius", "0", signed: false);

        var offset = pattern?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("offset", StringComparison.OrdinalIgnoreCase));
        if (offset == null)
        {
            if (IsModifiedTab)
            {
                var addOffset = CreateOptionalPropertyButton("Offset");
                addOffset.Click += (_, _) =>
                {
                    var editablePattern = GetOrCreateCreateUnitPattern(effect);
                    editablePattern.Add(new XElement("offset",
                        new XAttribute("x", "0"),
                        new XAttribute("y", "0"),
                        new XAttribute("z", "0")));
                    MarkDirty(); _ = BuildEditorAsync();
                };
                patternRow.Children.Add(addOffset);
            }
        }
        else
        {
            var offsetGroup = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            offsetGroup.Children.Add(CreateInlineLabel("Offset"));
            foreach (var axis in new[] { "x", "y", "z" })
            {
                offsetGroup.Children.Add(CreateInlineLabel(axis.ToUpperInvariant()));
                var axisAttribute = GetCaseInsensitiveAttribute(offset, axis);
                var axisBox = CreateNumericTextBox(FormatNumericForDisplay(axisAttribute?.Value ?? "0"), 60);
                EditorNumericInputBehavior.AttachRule(axisBox, ProtoUnitNumericKind.SignedFloat);
                axisBox.TextChanged += (_, _) =>
                {
                    if (_loadingUi || !IsModifiedTab) return;
                    SetCaseInsensitiveAttribute(offset, axis, axisBox.Text ?? "0");
                    MarkDirty(); UpdatePreview();
                };
                offsetGroup.Children.Add(axisBox);
            }
            if (IsModifiedTab)
                offsetGroup.Children.Add(CreateRemoveButton(() => offset.Remove()));
            patternRow.Children.Add(offsetGroup);
        }
        content.Children.Add(patternRow);
    }

    private CheckBox CreateCreateUnitPresenceCheckBox(XElement effect, string attributeName, string label, string enabledValue)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = GetCaseInsensitiveAttribute(effect, attributeName) != null,
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (checkBox.IsChecked == true) SetCaseInsensitiveAttribute(effect, attributeName, enabledValue);
            else RemoveCaseInsensitiveAttribute(effect, attributeName);
            MarkDirty(); UpdatePreview();
        };
        return checkBox;
    }

    private XElement GetOrCreateCreateUnitPattern(XElement effect)
    {
        var pattern = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pattern", StringComparison.OrdinalIgnoreCase));
        if (pattern != null) return pattern;
        pattern = new XElement("pattern", new XAttribute("type", "Leaving"), new XAttribute("quantity", "1"));
        effect.Add(pattern);
        return pattern;
    }

    private void AddOptionalPatternFloat(WrapPanel row, XElement effect, XElement? pattern, string label, string attributeName, string defaultValue, bool signed)
    {
        var attribute = pattern == null ? null : GetCaseInsensitiveAttribute(pattern, attributeName);
        if (attribute == null)
        {
            if (!IsModifiedTab) return;
            var add = CreateOptionalPropertyButton(label);
            add.Click += (_, _) =>
            {
                SetCaseInsensitiveAttribute(GetOrCreateCreateUnitPattern(effect), attributeName, defaultValue);
                MarkDirty(); _ = BuildEditorAsync();
            };
            row.Children.Add(add);
            return;
        }

        var group = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        group.Children.Add(CreateInlineLabel(label));
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute.Value), 70);
        EditorNumericInputBehavior.AttachRule(box, signed ? ProtoUnitNumericKind.SignedFloat : ProtoUnitNumericKind.UnsignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            attribute.Value = box.Text ?? defaultValue;
            MarkDirty(); UpdatePreview();
        };
        group.Children.Add(box);
        if (IsModifiedTab) group.Children.Add(CreateRemoveButton(() => attribute.Remove()));
        row.Children.Add(group);
    }

    private void AddResourceInventoryExchangeEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unitType")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unitType", value),
            150)));
        row.Children.Add(CreateLabeledEffectSegment("From", CreateResourceCombo(effect, "fromResource")));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateResourceCombo(effect, "toResource")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier", 70, "0")));

        var keepAlive = new CheckBox
        {
            Content = "Keep alive",
            IsChecked = GetCaseInsensitiveAttribute(effect, "keepUnit")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = IsModifiedTab,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        keepAlive.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            if (keepAlive.IsChecked == true) SetCaseInsensitiveAttribute(effect, "keepUnit", "true");
            else RemoveCaseInsensitiveAttribute(effect, "keepUnit");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(keepAlive);
        content.Children.Add(row);
    }

    private void AddTrickleByResourceEffectEditor(XElement effect, StackPanel content)
    {
        // One wrapping flow: paired label/field segments move down only when the available width requires it.
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Grants", CreateResourceCombo(effect, "resource")));
        row.Children.Add(CreateLabeledEffectSegment("Min Value", CreateUnsignedFloatEffectBox(effect, "minValue", 70, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Max", CreateUnsignedFloatEffectBox(effect, "maxValue", 70, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Source Resource", CreateResourceCombo(effect, "srcResource1")));

        var source2 = GetCaseInsensitiveAttribute(effect, "srcResource2");
        if (source2 == null && IsModifiedTab)
        {
            var addSource2 = CreateOptionalPropertyButton("Source Resource 2");
            addSource2.Margin = new Thickness(0, 4, 8, 4);
            addSource2.Click += (_, _) =>
            {
                SetCaseInsensitiveAttribute(effect, "srcResource2", "");
                MarkDirty(); _ = BuildEditorAsync();
            };
            row.Children.Add(addSource2);
        }
        else if (source2 != null)
        {
            var source2Segment = CreateLabeledEffectSegment("Source Resource 2", CreateResourceCombo(effect, "srcResource2"));
            if (IsModifiedTab) source2Segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "srcResource2")));
            row.Children.Add(source2Segment);
        }

        row.Children.Add(CreateLabeledEffectSegment("Min Source value", CreateUnsignedFloatEffectBox(effect, "minSrcValue", 70, "0"), leftSpacing: 8));
        row.Children.Add(CreateLabeledEffectSegment("Max", CreateUnsignedFloatEffectBox(effect, "maxSrcValue", 70, "0")));
        content.Children.Add(row);
    }

    private void AddResourceExchange2EffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("From", CreateResourceCombo(effect, "fromResource")));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateResourceCombo(effect, "toResource")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier", 70, "0")));
        row.Children.Add(CreateLabeledEffectSegment("And to", CreateResourceCombo(effect, "toResource2")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier2", 70, "0")));
        content.Children.Add(row);
    }

    private void AddReplaceUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "fromProtoID")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "fromProtoID", value),
            150)));
        row.Children.Add(CreateLabeledEffectSegment("to", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "toProtoID")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "toProtoID", value),
            150),
            leftSpacing: 8));
        content.Children.Add(row);
    }

    private WrapPanel CreateLabeledEffectSegment(string label, Control control, double leftSpacing = 0)
    {
        var segment = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(leftSpacing > 0 ? 8 : 0, 0, 0, 0)
        };
        segment.Children.Add(CreateInlineLabel(label));
        segment.Children.Add(control);
        return segment;
    }

    private TextBox CreateUnsignedFloatEffectBox(XElement effect, string attributeName, double width, string defaultValue)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute?.Value ?? defaultValue), width);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        };
        return box;
    }

    private void AddResourceExchangeEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Exchange"));
        row.Children.Add(CreateResourceCombo(effect, "fromResource"));
        row.Children.Add(CreateInlineLabel("To"));
        row.Children.Add(CreateResourceCombo(effect, "toResource"));
        row.Children.Add(CreateInlineLabel("Multiplier rate"));
        var multiplier = GetCaseInsensitiveAttribute(effect, "multiplier");
        var box = CreateNumericTextBox(FormatNumericForDisplay(multiplier?.Value ?? "0"), 70);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            SetCaseInsensitiveAttribute(effect, "multiplier", box.Text ?? "0");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private ComboBox CreateResourceCombo(XElement effect, string attributeName)
    {
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? "";
        var values = ProtoConstants.KnownResourceTypes.Append(current).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)), Width = 100, IsEnabled = IsModifiedTab, Margin = new Thickness(0, 4, 8, 4) };
        combo.SelectionChanged += (_, _) => { if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string value) return; SetCaseInsensitiveAttribute(effect, attributeName, value); MarkDirty(); UpdatePreview(); };
        return combo;
    }

    private void AddSimpleEffectValueEditor(XElement effect, StackPanel content, string label, double width)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel(label));
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox { Text = effect.Value, IsEnabled = IsModifiedTab, Width = width, MaxWidth = width, Margin = new Thickness(0, 4, 0, 4) });
        box.Width = box.MaxWidth = width;
        box.TextChanged += (_, _) => { if (_loadingUi || !IsModifiedTab) return; effect.Value = box.Text ?? ""; MarkDirty(); UpdatePreview(); };
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private void AddOptionalEffectAttribute(
        WrapPanel row,
        XElement effect,
        string label,
        string attributeName,
        string defaultValue,
        double width,
        ProtoUnitNumericKind? numericKind,
        bool requirePositive = false,
        double buttonLeftSpacing = 0,
        bool offerWhenMissing = true)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null)
        {
            if (!IsModifiedTab || !offerWhenMissing) return;
            var button = CreateOptionalPropertyButton(label);
            button.Margin = new Thickness(buttonLeftSpacing, button.Margin.Top, button.Margin.Right, button.Margin.Bottom);
            button.Click += (_, _) => { SetCaseInsensitiveAttribute(effect, attributeName, defaultValue); MarkDirty(); _ = BuildEditorAsync(); };
            row.Children.Add(button);
            return;
        }
        TextBox box;
        if (numericKind.HasValue)
        {
            box = CreateNumericTextBox(FormatNumericForDisplay(attribute.Value), width);
            EditorNumericInputBehavior.AttachRule(box, numericKind.Value);
        }
        else
        {
            box = EditorTextFieldStyle.ConfigureTextBox(new TextBox { Text = attribute.Value, IsEnabled = IsModifiedTab, Width = width, MaxWidth = width, Margin = new Thickness(0, 4, 0, 4) });
            box.Width = box.MaxWidth = width;
        }
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            var value = box.Text ?? "";
            if (requirePositive && (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0)) return;
            attribute.Value = value;
            MarkDirty(); UpdatePreview();
        };
        var segment = CreateLabeledEffectSegment(label, box, leftSpacing: 8);
        if (IsModifiedTab) segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, attributeName)));
        row.Children.Add(segment);
    }

    private async Task AddEffectStringAttributeRowAsync(StackPanel content, XElement effect, XAttribute attribute, string label, double width, bool removable, bool multiline = false)
    {
        var id = attribute.Value.Trim();
        var text = await ResolveTechnologyStringValueAsync(id);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var box = CreateEffectStringTextBox(attribute, text, width, multiline);
        row.Children.Add(CreateLabeledEffectSegment(label, box));
        if (removable && IsModifiedTab)
            row.Children.Add(CreateRemoveButton(() => { RemoveCaseInsensitiveAttribute(effect, attribute.Name.LocalName); QueueStringForRemoval(id); }));
        content.Children.Add(row);
    }

    private TextBox CreateEffectStringTextBox(XAttribute attribute, string text, double width, bool multiline = false)
    {
        var id = attribute.Value.Trim();
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Width = width,
            MaxWidth = width,
            MinHeight = 32,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = width;
        box.TextChanged += (_, _) => { if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(id)) return; _pendingStringRemovals.Remove(id); _pendingStringUpdates[id] = box.Text ?? ""; MarkDirty(); };
        return box;
    }

    private void AddRawEffectXmlEditor(XElement effect, StackPanel content)
    {
        var box = new TextEditor
        {
            Text = effect.ToString(SaveOptions.DisableFormatting),
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 42,
            IsReadOnly = !IsModifiedTab,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        XmlSyntaxEditorService.Configure(box);
        box.LostFocus += (_, _) =>
        {
            if (!IsModifiedTab) return;
            try
            {
                var parsed = XElement.Parse(box.Text ?? "");
                if (!parsed.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Root must be <effect>.");
                var currentXml = effect.ToString(SaveOptions.DisableFormatting);
                var parsedXml = parsed.ToString(SaveOptions.DisableFormatting);
                if (currentXml.Equals(parsedXml, StringComparison.Ordinal)) return;
                effect.ReplaceWith(parsed);
                effect = parsed;
                MarkDirty();
                UpdatePreview();
                _statusMessage.Text = "";
                _ = BuildEditorAsync();
            }
            catch (Exception ex) { _statusMessage.Text = "Invalid effect XML: " + ex.Message; }
        };
        content.Children.Add(box);
    }

    private AutoCompleteBox CreateStrictEffectSelector(
        IEnumerable<string> suggestions,
        string value,
        Action<string> commit,
        double width = 150,
        bool preserveSuggestionOrder = false)
    {
        var selector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox { Text = value, FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, IsEnabled = IsModifiedTab, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) });
        selector.Width = selector.MaxWidth = width;
        var lastCommittedValue = value.Trim();
        EditorAutoCompleteService.ConfigureStrict(selector, suggestions.Append(value).Where(v => !string.IsNullOrWhiteSpace(v)), value, () => _loadingUi, preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true, deferSelectionCommit: true, selectAllOnFirstClick: true, keepStartVisibleAfterCommit: true, preserveSuggestionOrder: preserveSuggestionOrder, valueCommitted: v =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            var normalized = v.Trim();
            if (normalized.Equals(lastCommittedValue, StringComparison.OrdinalIgnoreCase)) return;
            lastCommittedValue = normalized;
            commit(v);
            MarkDirty();
            UpdatePreview();
        });
        return selector;
    }

    private static XAttribute? GetCaseInsensitiveAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void SetEffectExclusiveTarget(XElement effect, string targetAttribute, string otherAttribute, string value)
    {
        RemoveCaseInsensitiveAttribute(effect, otherAttribute);
        SetCaseInsensitiveAttribute(effect, targetAttribute, value);
        MarkDirty(); UpdatePreview();
    }

    private void QueueStringForRemoval(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _pendingStringUpdates.Remove(id);
        _pendingStringRemovals.Add(id);
    }

    private void QueueEffectOwnedStringsForRemoval(XElement effect)
    {
        foreach (var attrName in new[] { "tooltipID", "newName", "newRollover", "newShortRollover", "selfMsg", "playerMsg" })
        {
            var id = GetCaseInsensitiveAttribute(effect, attrName)?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("STR_TECH_", StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(id);
        }
        var type = GetCaseInsensitiveAttribute(effect, "type")?.Value ?? "";
        if (type.Equals("TextOutput", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("TextOutputAll", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("TextOutputTechName", StringComparison.OrdinalIgnoreCase))
        {
            var id = effect.Value.Trim();
            if (id.StartsWith("STR_TECH_", StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(id);
        }
    }

    private string BuildUniqueSetNameStringId(XElement effect, string suffix)
    {
        if (_current == null) return "";
        var target = GetCaseInsensitiveAttribute(effect, "tech")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(target))
            target = GetCaseInsensitiveAttribute(effect, "proto")?.Value.Trim();
        var targetToken = NormalizeTechnologyStringToken(target ?? "");
        if (targetToken.Length == 0) targetToken = "TARGET";
        return BuildUniqueTechnologyEffectStringId(_current, targetToken + "_" + suffix, effect);
    }

    private string BuildNextTextOutputStringId(XElement tech, string suffix)
    {
        var prefix = BuildTechnologyEffectStringId(((string?)tech.Attribute("name") ?? "").Trim(), suffix);
        var used = CollectTechnologyStringIds(tech);
        for (var index = 1; ; index++)
        {
            var candidate = prefix + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private string BuildUniqueTechnologyEffectStringId(XElement tech, string suffix, XElement? excludingEffect = null)
    {
        var baseId = BuildTechnologyEffectStringId(((string?)tech.Attribute("name") ?? "").Trim(), suffix);
        var used = CollectTechnologyStringIds(tech, excludingEffect);
        if (!used.Contains(baseId)) return baseId;
        for (var index = 2; ; index++)
        {
            var candidate = baseId + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private HashSet<string> CollectTechnologyStringIds(XElement tech, XElement? excludingEffect = null)
    {
        var used = tech.DescendantsAndSelf()
            .Where(e => excludingEffect == null || !ReferenceEquals(e, excludingEffect))
            .SelectMany(e => e.Attributes().Select(a => a.Value).Append(e.Value))
            .Where(v => v.StartsWith("STR_", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        used.UnionWith(_pendingStringUpdates.Keys);
        return used;
    }

    private static string NormalizeTechnologyStringToken(string value)
    {
        var token = value.Trim();
        token = Regex.Replace(token, "([A-Z]+)([A-Z][a-z])", "$1_$2");
        token = Regex.Replace(token, "([a-z0-9])([A-Z])", "$1_$2");
        var normalized = new string(token.ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }

    private static string BuildTechnologyEffectStringId(string technologyName, string suffix)
    {
        var prefix = BuildTechnologyStringId(technologyName, "displaynameid");
        prefix = prefix.EndsWith("_NAME", StringComparison.Ordinal) ? prefix[..^5] : prefix;
        return prefix + "_" + suffix;
    }

    private async Task RegenerateSetNameEffectStringIdsAsync(XElement effect, bool removeOldIds)
    {
        foreach (var (attributeName, suffix) in new[] { ("newName", "NEW_NAME"), ("newRollover", "NEW_ROLLOVER"), ("newShortRollover", "NEW_SHORT_ROLLOVER") })
        {
            var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            if (attribute == null) continue;
            var oldId = attribute.Value.Trim();
            var text = await ResolveTechnologyStringValueAsync(oldId);
            var newId = BuildUniqueSetNameStringId(effect, suffix);
            if (removeOldIds && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(oldId);
            attribute.Value = newId;
            _pendingStringRemovals.Remove(newId);
            _pendingStringUpdates[newId] = text;
        }
    }

    private string BuildNextEffectTooltipStringId(XElement tech)
    {
        var techName = ((string?)tech.Attribute("name") ?? "").Trim();
        var used = tech.Elements()
            .Where(e => TechnologyStringBackedTags.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        used.UnionWith(tech.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Value.Trim())
            .Where(v => v.Length > 0));
        used.UnionWith(_pendingStringUpdates.Keys);
        return BuildNextEffectTooltipStringId(techName, used);
    }

    private static string BuildNextEffectTooltipStringId(string technologyName, ISet<string> used)
    {
        var baseId = BuildTechnologyStringId(technologyName, "advancedrollovertextoverrideid");
        if (!used.Contains(baseId))
            return baseId;

        for (var index = 2; ; index++)
        {
            var candidate = baseId + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private void TechNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_controlsReady || _loadingUi || !IsModifiedTab || _current == null || _currentOriginalName == null) return;
        var newName = (_techNameBox.Text ?? "").Trim();
        if (InternalNamePolicy.IsValidOrUnchangedLegacy(newName, _currentOriginalName))
            _statusMessage.Text = "";
        MarkDirty();
    }

    private async void TechNameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        await CommitCurrentTechnologyAsync();
    }

    public async Task<bool> CommitCurrentTechnologyAsync()
    {
        await _technologyNameCommitGate.WaitAsync();
        try
        {
            return await CommitPendingTechnologyNameAsync();
        }
        finally
        {
            _technologyNameCommitGate.Release();
        }
    }

    private async Task<bool> CommitPendingTechnologyNameAsync()
    {
        if (_loadingUi || !IsModifiedTab || _current == null || _currentOriginalName == null) return true;
        var technology = _current;
        var oldName = _currentOriginalName;
        var newName = (_techNameBox.Text ?? "").Trim();
        if (newName.Equals(oldName, StringComparison.Ordinal)) return true;

        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, oldName))
        {
            _statusMessage.Text = $"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}.";
            _loadingUi = true;
            _techNameBox.Text = oldName;
            _loadingUi = false;
            await ShowTechnologyNameErrorAsync(
                "Invalid technology name",
                $"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}. The previous name was kept.");
            return false;
        }
        if (_modified.Keys.Any(x => !x.Equals(oldName, StringComparison.OrdinalIgnoreCase) && x.Equals(newName, StringComparison.OrdinalIgnoreCase)) ||
            _original.ContainsKey(newName))
        {
            _statusMessage.Text = $"Technology '{newName}' already exists.";
            _loadingUi = true;
            _techNameBox.Text = oldName;
            _loadingUi = false;
            await ShowTechnologyNameErrorAsync(
                "Duplicate technology name",
                $"Technology '{newName}' already exists in the base game or this mod. The previous name was kept.");
            return false;
        }

        var technologyBackup = new XElement(technology);
        var stringUpdatesBackup = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
        var stringRemovalsBackup = new HashSet<string>(_pendingStringRemovals, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var tag in TechnologyStringBackedTags)
            {
                var element = technology.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (element == null) continue;
                var oldId = element.Value.Trim();
                if (oldId.Length == 0) continue;
                var text = await ResolveTechnologyStringValueAsync(oldId);
                var newId = BuildTechnologyStringId(newName, tag);
                if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) continue;
                _pendingStringUpdates.Remove(oldId);
                _pendingStringRemovals.Add(oldId);
                _pendingStringRemovals.Remove(newId);
                _pendingStringUpdates[newId] = text;
                element.Value = newId;
            }

            await RegenerateAllEffectStringsAsync(technology, newName, removeOldIds: true);
            if (!ReferenceEquals(_current, technology) ||
                !string.Equals(_currentOriginalName, oldName, StringComparison.Ordinal))
            {
                RestorePendingTechnologyRenameState(
                    technology,
                    technologyBackup,
                    stringUpdatesBackup,
                    stringRemovalsBackup);
                return false;
            }
        }
        catch
        {
            RestorePendingTechnologyRenameState(
                technology,
                technologyBackup,
                stringUpdatesBackup,
                stringRemovalsBackup);
            throw;
        }

        technology.SetAttributeValue("name", newName);
        _modified.Remove(oldName);
        _modified[newName] = technology;
        _currentOriginalName = newName;
        _dirtyTechnologyNames.Remove(oldName);
        _dirtyTechnologyNames.Add(newName);
        MarkDirty();
        RefreshList(newName);
        UpdatePreview();
        _statusMessage.Text = "";
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task ShowTechnologyNameErrorAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new Prompt(PromptType.Error, title, message).ShowDialog(owner);
    }

    private void RestorePendingTechnologyRenameState(
        XElement technology,
        XElement technologyBackup,
        IReadOnlyDictionary<string, string> stringUpdatesBackup,
        IReadOnlyCollection<string> stringRemovalsBackup)
    {
        technology.ReplaceAttributes(technologyBackup.Attributes());
        technology.ReplaceNodes(technologyBackup.Nodes());
        _pendingStringUpdates.Clear();
        foreach (var entry in stringUpdatesBackup)
            _pendingStringUpdates[entry.Key] = entry.Value;
        _pendingStringRemovals.Clear();
        foreach (var id in stringRemovalsBackup)
            _pendingStringRemovals.Add(id);
    }

    internal static string BuildTechnologyStringId(string technologyName, string tag)
    {
        var token = technologyName.Trim();
        token = Regex.Replace(token, "([A-Z]+)([A-Z][a-z])", "$1_$2");
        token = Regex.Replace(token, "([a-z0-9])([A-Z])", "$1_$2");
        var normalized = new string(token
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        normalized = normalized.Trim('_');

        var suffix = tag.ToLowerInvariant() switch
        {
            "displaynameid" => "NAME",
            "rollovertextid" => "LR",
            "advancedrollovertextoverrideid" => "OVERRIDE",
            _ => "TEXT"
        };
        return $"STR_TECH_{normalized}_{suffix}";
    }

    private async Task RegenerateDuplicatedTechnologyStringsAsync(XElement tech, string newName)
    {
        foreach (var tag in TechnologyStringBackedTags)
        {
            var element = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (element == null) continue;

            var oldId = element.Value.Trim();
            var text = await ResolveTechnologyStringValueAsync(oldId);

            var newId = BuildTechnologyStringId(newName, tag);
            element.Value = newId;
            _pendingStringUpdates[newId] = text;
        }

        await RegenerateAllEffectStringsAsync(tech, newName, removeOldIds: false);
    }

    private async Task RegenerateAllEffectStringsAsync(XElement tech, string technologyName, bool removeOldIds)
    {
        await RegenerateEffectTooltipStringsAsync(tech, technologyName, removeOldIds);
        var used = CollectTechnologyStringIds(tech);
        foreach (var effect in tech.Descendants().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var type = GetCaseInsensitiveAttribute(effect, "type")?.Value ?? "";
            if (type.Equals("SetName", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetCaseInsensitiveAttribute(effect, "tech")?.Value.Trim();
                if (string.IsNullOrWhiteSpace(target)) target = GetCaseInsensitiveAttribute(effect, "proto")?.Value.Trim();
                var targetToken = NormalizeTechnologyStringToken(target ?? "");
                if (targetToken.Length == 0) targetToken = "TARGET";
                foreach (var (attributeName, suffix) in new[] { ("newName", "NEW_NAME"), ("newRollover", "NEW_ROLLOVER"), ("newShortRollover", "NEW_SHORT_ROLLOVER") })
                {
                    var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
                    if (attribute == null) continue;
                    var oldId = attribute.Value.Trim();
                    var text = await ResolveTechnologyStringValueAsync(oldId);
                    var baseId = BuildTechnologyEffectStringId(technologyName, targetToken + "_" + suffix);
                    var newId = baseId;
                    for (var index = 2; used.Contains(newId); index++) newId = baseId + index.ToString(CultureInfo.InvariantCulture);
                    used.Add(newId);
                    if (removeOldIds && oldId.Length > 0 && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(oldId);
                    attribute.Value = newId;
                    _pendingStringRemovals.Remove(newId);
                    _pendingStringUpdates[newId] = text;
                }
            }
            else if (type.Equals("TextOutput", StringComparison.OrdinalIgnoreCase) ||
                     type.Equals("TextOutputAll", StringComparison.OrdinalIgnoreCase) ||
                     type.Equals("TextOutputTechName", StringComparison.OrdinalIgnoreCase))
            {
                var oldId = effect.Value.Trim();
                if (oldId.Length == 0) continue;
                var text = await ResolveTechnologyStringValueAsync(oldId);
                var suffix = type.Equals("TextOutputAll", StringComparison.OrdinalIgnoreCase) ? "OUTPUTALL" : "OUTPUT";
                var baseId = BuildTechnologyEffectStringId(technologyName, suffix);
                string newId = "";
                for (var index = 1; ; index++)
                {
                    var candidate = baseId + index.ToString(CultureInfo.InvariantCulture);
                    if (!used.Contains(candidate)) { newId = candidate; break; }
                }
                used.Add(newId);
                if (removeOldIds && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(oldId);
                effect.Value = newId;
                _pendingStringRemovals.Remove(newId);
                _pendingStringUpdates[newId] = text;
            }
            else if (type.Equals("TextEffectOutput", StringComparison.OrdinalIgnoreCase) ||
                     type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase))
            {
                var messageSuffixes = type.Equals("UIAlert", StringComparison.OrdinalIgnoreCase)
                    ? new[] { ("selfMsg", "UIALERT_SELFMSG"), ("playerMsg", "UIALERT_PLAYERMSG") }
                    : new[] { ("selfMsg", "SELFMSG"), ("playerMsg", "PLAYERMSG") };
                foreach (var (attributeName, suffix) in messageSuffixes)
                {
                    var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
                    if (attribute == null) continue;
                    var oldId = attribute.Value.Trim();
                    if (oldId.Length == 0) continue;
                    var text = await ResolveTechnologyStringValueAsync(oldId);
                    var baseId = BuildTechnologyEffectStringId(technologyName, suffix);
                    var newId = baseId;
                    for (var index = 2; used.Contains(newId); index++) newId = baseId + index.ToString(CultureInfo.InvariantCulture);
                    used.Add(newId);
                    if (removeOldIds && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) QueueStringForRemoval(oldId);
                    attribute.Value = newId;
                    _pendingStringRemovals.Remove(newId);
                    _pendingStringUpdates[newId] = text;
                }
            }
        }
    }

    private async Task RegenerateEffectTooltipStringsAsync(XElement tech, string technologyName, bool removeOldIds = true)
    {
        var tooltipAttributes = tech.Descendants()
            .Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase)))
            .Where(a => a != null)
            .Cast<XAttribute>()
            .ToList();

        if (tooltipAttributes.Count == 0) return;

        var used = tech.Elements()
            .Where(e => TechnologyStringBackedTags.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in tooltipAttributes)
        {
            var oldId = attribute.Value.Trim();
            var text = await ResolveTechnologyStringValueAsync(oldId);
            var newId = BuildNextEffectTooltipStringId(technologyName, used);
            used.Add(newId);

            if (removeOldIds && oldId.Length > 0 && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingStringUpdates.Remove(oldId);
                _pendingStringRemovals.Add(oldId);
            }
            _pendingStringRemovals.Remove(newId);
            _pendingStringUpdates[newId] = text;
            attribute.Value = newId;
        }
    }

    private async void AddTech_Click(object? sender, RoutedEventArgs e)
    {
        await AddTechnologyAsync();
    }

    public async Task AddTechnologyAsync(bool duplicateSelected = false)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        XElement? source = null;
        string? selected = _techList.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selected))
            (IsModifiedTab ? _modified : _original).TryGetValue(selected, out source);

        var duplicate = duplicateSelected && source != null && !string.IsNullOrWhiteSpace(selected);
        if (!duplicateSelected && source != null && !string.IsNullOrWhiteSpace(selected))
        {
            var choice = new Prompt(
                PromptType.Confirm,
                "Add Technology",
                $"Do you want to DUPLICATE the selected technology '{selected}'?\n(Click Confirm to duplicate, or Cancel to create a blank technology instead.)");
            await choice.ShowDialog(owner);
            duplicate = choice.Confirmed;
        }

        var input = new InputPromptWindow(duplicate ? "Enter duplicate technology name:" : "Enter new technology name:", allowWhitespace: false);
        await input.ShowDialog(owner);
        var name = input.InputText?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!InternalNamePolicy.IsValid(name))
        {
            await new Prompt(
                PromptType.Error,
                "Invalid Technology name",
                $"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}.").ShowDialog(owner);
            return;
        }

        if (_modified.ContainsKey(name) || _original.ContainsKey(name))
        {
            await new Prompt(PromptType.Error, "Duplicate", $"Technology '{name}' already exists in the base game or this mod.").ShowDialog(owner);
            return;
        }

        XElement tech;
        if (duplicate && source != null)
        {
            tech = new XElement(source);
            tech.SetAttributeValue("name", name);
            await RegenerateDuplicatedTechnologyStringsAsync(tech, name);
        }
        else
        {
            var displayId = BuildTechnologyStringId(name, "displaynameid");
            var rolloverId = BuildTechnologyStringId(name, "rollovertextid");
            tech = new XElement("tech",
                new XAttribute("name", name),
                new XElement("displaynameid", displayId),
                new XElement("rollovertextid", rolloverId),
                new XElement("icon", ""),
                new XElement("status", "UNOBTAINABLE"),
                new XElement("researchpoints", "0"));
            _pendingStringRemovals.Remove(displayId);
            _pendingStringRemovals.Remove(rolloverId);
            _pendingStringUpdates[displayId] = name;
            _pendingStringUpdates[rolloverId] = "";
        }

        _modDocument.Root!.Add(tech);
        _modified[name] = tech;
        _techTabs.SelectedIndex = 1;
        _dirtyTechnologyNames.Add(name);
        MarkDirty();
        RefreshList(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void DeleteTech_Click(object? sender, RoutedEventArgs e)
    {
        await DeleteTechnologyAsync();
    }

    public async Task DeleteTechnologyAsync()
    {
        if (!IsModifiedTab || _techList.SelectedItem is not string name || !_modified.TryGetValue(name, out var tech)) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirm = new Prompt(PromptType.Confirm, "Delete Technology", $"Are you sure you want to delete '{name}'?");
        await confirm.ShowDialog(owner);
        if (!confirm.Confirmed) return;

        var relatedStringIds = tech.Elements()
            .Where(e => TechnologyStringBackedTags.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Concat(tech.Descendants()
                .Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? ""))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var stringId in relatedStringIds)
        {
            _pendingStringUpdates.Remove(stringId);
            _pendingStringRemovals.Add(stringId);
        }
        foreach (var effect in tech.Descendants().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList())
            QueueEffectOwnedStringsForRemoval(effect);

        tech.Remove();
        _modified.Remove(name);
        _dirtyTechnologyNames.Add(name);
        MarkDirty();
        ClearEditor();
        RefreshList();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void XmlPreviewToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isXmlPreviewCollapsed = !_isXmlPreviewCollapsed;
        _xmlPreviewContent.IsVisible = !_isXmlPreviewCollapsed;
        _previewSplitter.IsVisible = !_isXmlPreviewCollapsed;
        if (_isXmlPreviewCollapsed)
        {
            _mainGrid.ColumnDefinitions[3].Width = new GridLength(0);
            _mainGrid.ColumnDefinitions[4].MinWidth = 28;
            _mainGrid.ColumnDefinitions[4].Width = new GridLength(28, GridUnitType.Pixel);
        }
        else
        {
            // Restore the Technology editor's default 80/20 editor/preview split.
            // The expanded preview keeps the same practical 250 px floor used by the ProtoUnit editor.
            _mainGrid.ColumnDefinitions[2].Width = new GridLength(4, GridUnitType.Star);
            _mainGrid.ColumnDefinitions[3].Width = new GridLength(5, GridUnitType.Pixel);
            _mainGrid.ColumnDefinitions[4].MinWidth = 250;
            _mainGrid.ColumnDefinitions[4].Width = new GridLength(1, GridUnitType.Star);
        }

        _xmlPreviewToggleButton.Content = _isXmlPreviewCollapsed ? "◀" : "▶";
        ToolTip.SetTip(_xmlPreviewToggleButton, _isXmlPreviewCollapsed ? "Restore XML Preview" : "Collapse XML Preview");
    }

    private static void NormalizeEffectAttributeOrder(XElement effect)
    {
        var attributes = effect.Attributes().ToList();
        if (attributes.Count < 2) return;

        static bool IsNamed(XAttribute attribute, string name)
            => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

        var type = attributes.Where(a => IsNamed(a, "type"));
        var subtype = attributes.Where(a => IsNamed(a, "subtype"));
        var amount = attributes.Where(a => IsNamed(a, "amount"));
        var relativity = attributes.Where(a => IsNamed(a, "relativity"));
        var metadata = attributes.Where(a => IsNamed(a, "hideTooltip") || IsNamed(a, "delay") || IsNamed(a, "tooltipID"));
        var subtypeSpecific = attributes.Where(a => !IsNamed(a, "type") && !IsNamed(a, "subtype") &&
                                                     !IsNamed(a, "amount") && !IsNamed(a, "relativity") &&
                                                     !IsNamed(a, "hideTooltip") && !IsNamed(a, "delay") && !IsNamed(a, "tooltipID"));
        var ordered = type.Concat(subtype).Concat(subtypeSpecific).Concat(amount).Concat(relativity).Concat(metadata).ToList();
        if (attributes.SequenceEqual(ordered)) return;
        foreach (var attribute in ordered) attribute.Remove();
        foreach (var attribute in ordered) effect.Add(attribute);
    }

    private static void NormalizeTechnologyChildOrder(XElement tech)
    {
        foreach (var effect in tech.Descendants().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
            NormalizeEffectAttributeOrder(effect);

        var children = tech.Elements().ToList();
        if (children.Count < 2) return;
        var properties = children.Where(e => !e.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)
                                             && !e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase)
                                             && !e.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase)
                                             && !e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
        var techTypes = children.Where(e => e.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)).ToList();
        var flags = children.Where(e => e.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase)).ToList();
        var prereqs = children.Where(e => e.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase)).ToList();
        var effects = children.Where(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
        var ordered = properties.Concat(techTypes).Concat(flags).Concat(prereqs).Concat(effects).ToList();
        if (children.SequenceEqual(ordered)) return;
        foreach (var child in ordered) child.Remove();
        tech.Add(ordered);
    }

    private static string ToDisplayStatus(string status)
        => status.Trim().ToLowerInvariant() switch
        {
            "active" => "Active",
            "obtainable" => "Obtainable",
            _ => "Unobtainable"
        };

    internal static string HumanizeLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var normalized = raw.TrimStart('@');
        var known = normalized.ToLowerInvariant() switch
        {
            "displaynameid" => "Display name",
            "rollovertextid" => "Rollover text",
            "advancedrollovertextoverrideid" => "Advanced rollover",
            "valuetext" => "Value text",
            "researchpoints" => "Research points",
            "researchlimit" => "Research limit",
            "techtype" => "Tech type",
            "orderhint" => "Order hint",
            "initialdelay" => "Initial delay",
            "techage" => "Tech Age",
            "combatxptier" => "Combat XP tier",
            "devotioncost" => "Devotion cost",
            _ => ""
        };
        if (known.Length > 0) return known;

        var value = normalized.Replace('_', ' ');
        value = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
        value = Regex.Replace(value, "\\s+", " ").Trim().ToLowerInvariant();
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    public bool IsDirty => _dirty;

    public async Task<bool> SaveAsync()
    {
        if (!await CommitCurrentTechnologyAsync())
            return false;

        if (string.IsNullOrWhiteSpace(_modTechtreePath))
        {
            _statusMessage.Text = "No active mod is loaded.";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_modTechtreePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            foreach (var technology in _modDocument.Root?.Elements().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? [])
                NormalizeTechnologyChildOrder(technology);
            ProtoEditorWindow.SaveAbilityXmlDocument(_modDocument, _modTechtreePath);
            if (_saveStringsAsync != null && (_pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0))
                await _saveStringsAsync(_pendingStringUpdates, _pendingStringRemovals);
            _pendingStringUpdates.Clear();
            _pendingStringRemovals.Clear();
            _dirty = false;
            _dirtyTechnologyNames.Clear();
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            _statusMessage.Text = "Saved successfully.";
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage.Text = "Save failed: " + ex.Message;
            return false;
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        if (IsModifiedTab && !string.IsNullOrWhiteSpace(_currentOriginalName))
            _dirtyTechnologyNames.Add(_currentOriginalName);
        _statusMessage.Text = "Modified";
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UpdatePreview()
    {
        if (_current == null)
        {
            _xmlPreview.Text = "";
            return;
        }

        // Reparse a compact copy for preview only so source whitespace cannot
        // leave the closing </tech> over-indented. The backing XML is untouched.
        var previewTech = XElement.Parse(_current.ToString(SaveOptions.DisableFormatting), LoadOptions.None);
        NormalizeTechnologyChildOrder(previewTech);
        _xmlPreview.Text = previewTech.ToString();
    }
}
