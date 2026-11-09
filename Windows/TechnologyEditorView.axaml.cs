using System.Globalization;
using Avalonia.LogicalTree;
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
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView : UserControl
{
    internal Func<Task>? ImmediateDeletionRequestedAsync { get; set; }
    private IReadOnlyList<ModDeletionInspection>? _immediateDeletionChecks;
    private Window? _immediateDeletionOwner;
    internal bool IsCurrentEntrySaved => _current != null && FindSavedTechnologySnapshot(_current) != null;
    internal Func<IReadOnlyList<string>, IReadOnlyDictionary<string, XDocument>, IReadOnlyList<ModDeletionInspection>>? PrepareDeletionChecks { get; set; }
    internal Func<IEnumerable<string>, IReadOnlyDictionary<string, XDocument>, OwnedStringSaveInspection>? PrepareStringCleanup { get; set; }
    internal IEnumerable<string> PendingStringIds => _pendingStringUpdates.Keys;
    internal XDocument? CreateModifiedDocumentSnapshot()
        => CreateTechnologyXmlSnapshot();
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
        .. TechnologyXmlOrderPolicy.OptionalFields
    ];

    private static readonly string[] TechnologyAges =
    [
        "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"
    ];

    private static readonly string[] PrerequisiteTypes =
    [
        "TechStatus", "SpecificAge", "TypeCount", "Culture", "Civilization", "KBStat"
    ];

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
        "ActionEnable", "HomingBallistics", "InstantBallistics", "PerfectAccuracy", "VolleyMode", "Snare"
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
    private readonly string? _modStringsPath;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync;
    private readonly Action? _reloadStringsFromDisk;
    private readonly Func<IReadOnlyList<TechnologyReferenceUsage>>? _externalTechnologyReferenceProvider;
    private readonly Func<string, IReadOnlyList<TechnologyReferenceUsage>>? _technologyDeletionReferenceProvider;
    private readonly Func<IEnumerable<string>, IReadOnlySet<string>>? _externalStringReferenceProvider;
    private readonly Func<XDocument, Task>? _technologySavedAsync;
    private readonly Func<string, bool>? _hasLinkedMinorGod;
    private readonly Func<Window, string, Task<bool>>? _confirmLinkedMinorGodRemovalAsync;
    private readonly Func<XDocument, IReadOnlyList<TechnologyRenameOperation>, IReadOnlyCollection<string>, PreparedModXmlDocument?>?
        _prepareMinorGodLifecycle;
    private readonly Func<IReadOnlyList<TechnologyRenameOperation>, TechnologyReferenceMigrationPlan>? _prepareTechnologyReferenceMigration;
    private readonly Action<TechnologyReferenceMigrationPlan>? _adoptTechnologyReferenceMigration;
    private readonly IconPreviewService? _iconPreviewService;
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingStringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _automaticStringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _loadedStringValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly SelectorCatalog _iconPaths = new([]);
    private readonly IReadOnlyList<string> _prereqUnitNames = [];
    private readonly IReadOnlyList<string> _protoUnitNames = [];
    private readonly SelectorCatalog _unitTypeNames = new(ProtoConstants.KnownUnitTypes);
    private readonly IReadOnlyList<string> _cultureNames = [];
    private IReadOnlyList<string> _majorGodNames = [];
    private readonly IReadOnlyList<string> _techTypeNames = [];
    private readonly IReadOnlyList<string> _protoActionNames = [];
    private readonly IReadOnlyList<string> _tacticNames = [];
    private readonly IReadOnlyList<string> _protoUnitCommandNames = [];
    private readonly IReadOnlyList<string> _boneNames = [];
    private readonly IReadOnlyList<string> _godPowerNames = [];
    private readonly Dictionary<string, XElement> _original = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XElement> _modified = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _savedModifiedTechnologyXml = new(StringComparer.OrdinalIgnoreCase);
    private XDocument _modDocument = new(new XElement("techtreemods"));
    private XElement? _current;
    private string? _currentOriginalName;
    private bool _loadingUi;
    private int _editorBuildGeneration;
    private readonly SemaphoreSlim _editorBuildGate = new(1, 1);
    private readonly SemaphoreSlim _technologyNameCommitGate = new(1, 1);
    private CancellationTokenSource? _activeEditorBuildCancellation;
    private readonly AsyncLocal<CancellationToken> _editorBuildCancellationContext = new();
    private Task _activeEditorBuildTask = Task.CompletedTask;
    private readonly HashSet<Task> _activeUiOperations = [];
    private bool _dirty;
    private readonly HashSet<string> _dirtyTechnologyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TechnologyRenameOperation> _pendingTechnologyRenames = [];
    private readonly HashSet<string> _pendingMinorGodRemovals = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TechnologyReferenceUsage> _technologyReferenceUsages = [];
    private string? _technologyReferenceIndexError;
    private readonly HashSet<(XElement Effect, string Attribute)> _openOnHitOptionalSelectors = [];
    private readonly HashSet<XElement> _effectsPendingDefaultInitialization = new(ReferenceEqualityComparer.Instance);
    private bool _initializingEffectDefaults;
    private string? _modDocumentSafetyError;
    private FileIntegrityTransaction.FileState? _loadedTechtreeFileState;
    private FileIntegrityTransaction.FileState? _loadedStringsFileState;
    private bool _controlsReady;
    private bool _isXmlPreviewCollapsed;
    private IconPreviewControl? _iconPreviewControl;
    private EditorSaveFeedback _saveFeedback = null!;
    internal Action<EditorSaveOutcome>? SaveFeedbackReported { get; set; }
    internal void UseHostStatusFooter(Action<string> reportContext) => _saveFeedback.UseHostFooter(reportContext);
    private bool _documentSafetyPromptShown;

    public TechnologyEditorView()
    {
        InitializeComponent();
        EditorXmlPreviewPane.Configure(_mainGrid, 2, 3, 4, _xmlPreviewContent, _previewSplitter, _xmlPreviewToggleButton);
        _saveFeedback = new EditorSaveFeedback(_statusMessage);
        _controlsReady = true;
        XmlSyntaxEditorService.Configure(_xmlPreview);
        InitializeTechnologyXmlEditing();
        AttachedToVisualTree += TechnologyEditorView_AttachedToVisualTree;
    }

    private void TechnologyEditorView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_documentSafetyPromptShown || string.IsNullOrWhiteSpace(_modDocumentSafetyError) ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        _documentSafetyPromptShown = true;
        ObserveUiOperation(ShowDocumentSafetyPromptAsync(owner), "show the technology safety warning");
    }

    private async Task ShowDocumentSafetyPromptAsync(Window owner)
    {
        await DocumentSafetyDialog.ShowAsync(
            owner,
            "Technology",
            _modDocumentSafetyError ?? "The technology file cannot be edited safely.");
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
        IEnumerable<string>? godPowerNames = null,
        string? modStringsPath = null,
        Action? reloadStringsFromDisk = null,
        Func<IReadOnlyList<TechnologyReferenceUsage>>? externalTechnologyReferenceProvider = null,
        Func<IReadOnlyList<TechnologyRenameOperation>, TechnologyReferenceMigrationPlan>? prepareTechnologyReferenceMigration = null,
        Action<TechnologyReferenceMigrationPlan>? adoptTechnologyReferenceMigration = null,
        Func<string, IReadOnlyList<TechnologyReferenceUsage>>? technologyDeletionReferenceProvider = null,
        Func<XDocument, Task>? technologySavedAsync = null,
        Func<string, bool>? hasLinkedMinorGod = null,
        Func<Window, string, Task<bool>>? confirmLinkedMinorGodRemovalAsync = null,
        Func<XDocument, IReadOnlyList<TechnologyRenameOperation>, IReadOnlyCollection<string>, PreparedModXmlDocument?>?
            prepareMinorGodLifecycle = null,
        Func<IEnumerable<string>, IReadOnlySet<string>>? externalStringReferenceProvider = null,
        IEnumerable<string>? unitTypeNames = null)
        : this()
    {
        _originalBarDocuments = originalBarDocuments?.ToList() ?? [];
        _hasOriginalTechnologyCatalog = originalBarDocuments != null;
        _baseGameplayDirectory = baseGameplayDirectory;
        _modTechtreePath = modTechtreePath;
        _modStringsPath = modStringsPath;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _reloadStringsFromDisk = reloadStringsFromDisk;
        _externalTechnologyReferenceProvider = externalTechnologyReferenceProvider;
        _prepareTechnologyReferenceMigration = prepareTechnologyReferenceMigration;
        _adoptTechnologyReferenceMigration = adoptTechnologyReferenceMigration;
        _technologyDeletionReferenceProvider = technologyDeletionReferenceProvider;
        _technologySavedAsync = technologySavedAsync;
        _hasLinkedMinorGod = hasLinkedMinorGod;
        _confirmLinkedMinorGodRemovalAsync = confirmLinkedMinorGodRemovalAsync;
        _prepareMinorGodLifecycle = prepareMinorGodLifecycle;
        _externalStringReferenceProvider = externalStringReferenceProvider;
        _iconPreviewService = iconPreviewService;
        _iconPaths.ReplaceWith(iconPaths ?? []);
        _prereqUnitNames = new SelectorCatalog(prereqUnitNames);
        _protoUnitNames = new SelectorCatalog(protoUnitNames);
        if (unitTypeNames != null) _unitTypeNames.ReplaceWith(unitTypeNames);
        _cultureNames = new SelectorCatalog(cultureNames);
        _majorGodNames = new SelectorCatalog(majorGodNames);
        _techTypeNames = new SelectorCatalog(techTypeNames);
        _protoActionNames = new SelectorCatalog(protoActionNames);
        _tacticNames = new SelectorCatalog(tacticNames);
        _protoUnitCommandNames = new SelectorCatalog(protoUnitCommandNames);
        _boneNames = boneNames?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _godPowerNames = new SelectorCatalog(godPowerNames);
        _techTabs.SelectedIndex = 0;
        LoadAll();
        RefreshTechnologyReferenceUsage();
        RefreshList();
    }

    private bool IsModifiedTab => _techTabs.SelectedIndex == 1;

    private bool CanEditModifiedDocument => IsModifiedTab && _modDocumentSafetyError == null &&
        (_currentOriginalName == null || GetTechnologyEntryKind(_currentOriginalName) == AdditiveEntryKind.Custom);

    public bool IsModifiedMode => IsModifiedTab;

    public bool CanModifyTechnologyDocument => _modDocumentSafetyError == null;

    public string? ModificationBlockReason => _modDocumentSafetyError;

    public void AcknowledgeStringFileSavedByEditor()
    {
        if (string.IsNullOrWhiteSpace(_modStringsPath))
            return;
        try
        {
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
            ReplaceLoadedStringValues(ReadModStringValuesFromDisk());
        }
        catch (Exception exception)
        {
            _modDocumentSafetyError =
                $"The updated string file could not be fingerprinted safely. Further technology modifications are disabled. {exception.Message}";
            _statusMessage.Text = _modDocumentSafetyError;
        }
    }

    public string? CurrentTechnologyName => _currentOriginalName;

    public bool IsTechnologyDirty(string technologyName)
    {
        var isDirty = TechnologyHasUnsavedChanges(technologyName);
        if (isDirty)
            _dirtyTechnologyNames.Add(technologyName);
        else
            _dirtyTechnologyNames.Remove(technologyName);
        RecalculateDirtyState();
        return isDirty;
    }

    public XElement? LoadSavedTechnologyElement(string technologyName)
    {
        if (string.IsNullOrWhiteSpace(_modTechtreePath) || !File.Exists(_modTechtreePath))
            return null;

        try
        {
            var document = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace);
            var technology = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetCaseInsensitiveAttribute(element, "name")?.Value, technologyName, StringComparison.OrdinalIgnoreCase));
            return technology == null ? null : new XElement(technology);
        }
        catch
        {
            return null;
        }
    }

    public void DiscardTechnologyChanges(string technologyName, XElement? savedElement)
    {
        if (GetTechnologyEntryKind(technologyName) != AdditiveEntryKind.Custom) return;
        ForgetTechnologyXmlDraft(technologyName);
        var current = _modified.GetValueOrDefault(technologyName);
        var restored = savedElement == null ? null : new XElement(savedElement);
        var savedName = ((string?)restored?.Attribute("name"))?.Trim();
        if (string.IsNullOrWhiteSpace(savedName))
            savedName = technologyName;
        _pendingTechnologyRenames.RemoveAll(rename =>
            rename.OldName.Equals(technologyName, StringComparison.OrdinalIgnoreCase) ||
            rename.NewName.Equals(technologyName, StringComparison.OrdinalIgnoreCase) ||
            rename.OldName.Equals(savedName, StringComparison.OrdinalIgnoreCase) ||
            rename.NewName.Equals(savedName, StringComparison.OrdinalIgnoreCase));
        _pendingMinorGodRemovals.Remove(technologyName);
        _pendingMinorGodRemovals.Remove(savedName);

        var affectedStringIds = CollectReferencedTechnologyStringIds(current);
        affectedStringIds.UnionWith(CollectReferencedTechnologyStringIds(restored));
        var prefixes = new[] { technologyName, savedName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(name => BuildTechnologyOwnedStringPrefixes(name!))
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
        {
            _modified[savedName] = restored;
            _savedModifiedTechnologyXml[savedName] = SerializeTechnologyForComparison(restored);
        }
        else
        {
            _savedModifiedTechnologyXml.Remove(technologyName);
        }

        foreach (var id in affectedStringIds)
        {
            if (IsTechnologyStringIdOwnedByAnotherDirtyDocument(id, technologyName, savedName))
                continue;
            _pendingStringUpdates.Remove(id);
            _pendingStringRemovals.Remove(id);
            _automaticStringRemovals.Remove(id);
        }
        _dirtyTechnologyNames.Remove(technologyName);
        _dirtyTechnologyNames.Remove(savedName);
        if (string.Equals(_currentOriginalName, technologyName, StringComparison.OrdinalIgnoreCase))
            ClearEditor();
        RecalculateDirtyState();
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
                BuildTechnologyOwnedStringPrefixes(dirtyName).Any(prefix =>
                    stringId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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

    private static IEnumerable<string> BuildTechnologyOwnedStringPrefixes(string technologyName)
    {
        var token = NormalizeTechnologyStringToken(technologyName);
        yield return BuildTechnologyStringPrefix(technologyName);
        yield return $"STR_BUFFICON_{token}_";
        yield return $"STR_POWERICON_{token}_";
    }

    public IReadOnlyList<string> GetTechnologyNames(bool modified)
    {
        var source = modified ? _modified : _original;
        return source.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void UpdateMajorGodNames(IEnumerable<string> names)
    {
        var loading = _loadingUi;
        _loadingUi = true;
        try
        {
            if (((SelectorCatalog)_majorGodNames).ReplaceWith(names)) SelectorCatalogRefresh.Apply(this);
        }
        finally { _loadingUi = loading; }
    }

    internal void UpdateReferenceCatalogs(IEnumerable<string> units, IEnumerable<string> mixedUnits,
        IEnumerable<string> cultures, IEnumerable<string> gods, IEnumerable<string> techTypes,
        IEnumerable<string> actions, IEnumerable<string> tactics, IEnumerable<string> commands,
        IEnumerable<string> powers, IEnumerable<string>? unitTypes = null)
    {
        var loading = _loadingUi;
        _loadingUi = true;
        try
        {
            var changed = false;
            foreach (var pair in new[] { (_protoUnitNames, units), (_prereqUnitNames, mixedUnits),
                         (_cultureNames, cultures), (_majorGodNames, gods), (_techTypeNames, techTypes),
                         (_protoActionNames, actions), (_tacticNames, tactics), (_protoUnitCommandNames, commands),
                         (_godPowerNames, powers) })
                changed |= ((SelectorCatalog)pair.Item1).ReplaceWith(pair.Item2);
            if (unitTypes != null) changed |= _unitTypeNames.ReplaceWith(unitTypes);
            if (changed) SelectorCatalogRefresh.Apply(this);
        }
        finally { _loadingUi = loading; }
    }

    public void RefreshTechnologyReferenceUsage()
    {
        try
        {
            var knownNames = _original.Keys.Concat(_modified.Keys).ToList();
            var originalDocument = new XDocument(new XElement("techtree",
                _original.Values.Select(technology => new XElement(technology))));
            var sources = new List<TechnologyReferenceSource>
            {
                new("Base techtree", originalDocument, TechnologyReferenceDocumentKind.Techtree, IsBuiltIn: true),
                new("Mod techtree", _modDocument, TechnologyReferenceDocumentKind.Techtree,
                    _modTechtreePath, CanMigrate: true)
            };
            var usages = TechnologyReferenceIndex.Build(sources, knownNames).ToList();
            if (_externalTechnologyReferenceProvider != null)
                usages.AddRange(_externalTechnologyReferenceProvider());

            _technologyReferenceUsages = usages
                .Select(MapUsageThroughPendingRenames)
                .ToList();
            _technologyReferenceIndexError = null;
        }
        catch (Exception exception)
        {
            _technologyReferenceUsages = [];
            _technologyReferenceIndexError = exception.Message;
        }
    }

    private TechnologyReferenceUsage MapUsageThroughPendingRenames(TechnologyReferenceUsage usage)
    {
        var mappedName = usage.TechnologyName;
        foreach (var rename in _pendingTechnologyRenames)
        {
            if (usage.IsBuiltIn && _original.ContainsKey(rename.OldName))
                continue;
            if (mappedName.Equals(rename.OldName, StringComparison.OrdinalIgnoreCase))
                mappedName = rename.NewName;
        }
        return mappedName.Equals(usage.TechnologyName, StringComparison.Ordinal)
            ? usage
            : usage with { TechnologyName = mappedName };
    }

    private IReadOnlyList<TechnologyReferenceUsage> GetTechnologyReferences(string technologyName)
    {
        RefreshTechnologyReferenceUsage();
        return _technologyReferenceUsages
            .Where(usage => usage.TechnologyName.Equals(technologyName, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
            try
            {
                var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                MergeTechs(document, _original, overwrite: false);
                if (document.Root?.Name.LocalName.Equals("techtree", StringComparison.OrdinalIgnoreCase) == true)
                    _hasOriginalTechnologyCatalog = true;
            }
            catch { }
        }
    }

    private void LoadOriginalFromBarDocuments()
    {
        foreach (var document in _originalBarDocuments)
            MergeTechs(document, _original, overwrite: false);
    }

    private void LoadModified()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_modTechtreePath))
                _loadedTechtreeFileState = FileIntegrityTransaction.CaptureState(_modTechtreePath);
            if (!string.IsNullOrWhiteSpace(_modStringsPath))
            {
                _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
                ReplaceLoadedStringValues(ReadModStringValuesFromDisk());
            }
        }
        catch (Exception exception)
        {
            _modDocumentSafetyError =
                $"The technology files could not be fingerprinted safely. Technology modifications are disabled. {exception.Message}";
        }

        if (!string.IsNullOrWhiteSpace(_modTechtreePath) && File.Exists(_modTechtreePath))
        {
            try
            {
                var document = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace);
                var validation = ValidateTechnologySourceStructure(document);
                _modDocument = document;
                if (!validation.IsValid)
                    _modDocumentSafetyError = validation.ToDisplayMessage(_modTechtreePath);
            }
            catch (Exception exception)
            {
                _modDocument = new XDocument(new XElement("techtreemods"));
                _modDocumentSafetyError =
                    $"'{_modTechtreePath}' could not be read safely. Technology modifications are disabled and the file will not be overwritten. {exception.Message}";
            }
        }
        else
        {
            _modDocument = new XDocument(new XElement("techtreemods"));
        }

        MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
        foreach (var (name, technology) in _modified)
            _savedModifiedTechnologyXml[name] = SerializeTechnologyForComparison(technology);
        if (_modDocumentSafetyError != null)
            _statusMessage.Text = _modDocumentSafetyError;
    }

    private static void MergeTechs(XDocument doc, Dictionary<string, XElement> destination, bool overwrite, bool clone = true)
    {
        foreach (var tech in doc.Descendants().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)))
        {
            var name = GetCaseInsensitiveAttribute(tech, "name")?.Value;
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
        _current = ResolveTechnologyForReading(name);
        _currentOriginalName = name;
        UpdatePreview();
        _ = BuildEditorAsync();
    }

    private void ClearEditor()
    {
        _activeEditorBuildCancellation?.Cancel();
        _editorBuildGeneration++;
        _loadingUi = true;
        ClearTechnologyAdditivePresentation();
        _current = null;
        _currentOriginalName = null;
        _iconPreviewControl = null;
        _techNameBox.Text = "";
        _techNameBox.IsReadOnly = true;
        _techNameBox.IsEnabled = false;
        _propertiesPanel.Children.Clear();
        _prereqsPanel.Children.Clear();
        _effectsPanel.Children.Clear();
        _technologyXmlController.Bind(null, formEnabled: false);
        _loadingUi = false;
    }

    private Task BuildEditorAsync()
    {
        EditorValidationNavigation.Clear(this);
        if (_current != null) EditorValidationBindings.Bind(_techNameBox, _current, "@name");
        var task = BuildEditorCoreAsync();
        _activeEditorBuildTask = task;
        return task;
    }

    private async Task BuildEditorCoreAsync()
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _activeEditorBuildCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        var cancellationToken = cancellation.Token;
        var generation = ++_editorBuildGeneration;
        var gateEntered = false;
        var previousContext = _editorBuildCancellationContext.Value;
        try
        {
            await _editorBuildGate.WaitAsync(cancellationToken);
            gateEntered = true;
            _editorBuildCancellationContext.Value = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (_current == null || generation != _editorBuildGeneration) return;
            var tech = _current;
            _loadingUi = true;
            _iconPreviewControl = null;
            ClearTechnologySectionNotices();
            _propertiesPanel.Children.Clear();
            _prereqsPanel.Children.Clear();
            _effectsPanel.Children.Clear();
            _techNameBox.Text = (string?)tech.Attribute("name") ?? "";
            _techNameBox.IsReadOnly = !CanEditModifiedDocument;
            _techNameBox.IsEnabled = CanEditModifiedDocument;

            AddSectionHeader("Properties");
            await AddKnownPropertyEditorsAsync(tech);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEditorBuildCurrent(tech, generation)) return;

            foreach (var attr in tech.Attributes().Where(a =>
                     !a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                     !a.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                     !a.Name.LocalName.Equals("orderhint", StringComparison.OrdinalIgnoreCase)).ToList())
            EditorValidationBindings.BindXml(AddTextPropertyRow(HumanizeLabel(attr.Name.LocalName), attr.Value, v => attr.Value = v), attr);

            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
            "displaynameid", "rollovertextid", "advancedrollovertextoverrideid",
            "cost", "researchpoints", "status", "devotioncost", "techtype", "icon", "flag", "effects", "prereqs"
            };

            var optionalPreview = new XElement(tech);
            var optionalSources = optionalPreview.Elements().Zip(tech.Elements())
                .ToDictionary(pair => pair.First, pair => pair.Second);
            TechnologyXmlOrderPolicy.NormalizeForEditor(optionalPreview);
            // Registered optional fields follow their XML/form order. Unknown fields
            // retain their source positions and remain preservation boundaries.
            foreach (var child in optionalPreview.Elements().Where(e => !e.HasElements && !handled.Contains(e.Name.LocalName))
                         .Select(e => optionalSources[e]).ToList())
            {
            if (!CanEditModifiedDocument && string.IsNullOrWhiteSpace(child.Value) && !child.HasAttributes)
                continue;

            if (child.Name.LocalName.Equals("techage", StringComparison.OrdinalIgnoreCase))
            {
                AddTechAgeEditor(child);
                continue;
            }

            string suffix = string.Join(", ", child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
            string label = HumanizeLabel(child.Name.LocalName) + (suffix.Length > 0 ? $" [{suffix}]" : "");
            var role = child.Name.LocalName.ToLowerInvariant() is "researchlimit" or "delay" or "initialdelay" or "combatxptier"
                ? EditorFieldWidthRole.Numeric : EditorFieldWidthRole.SingleLineText;
            EditorValidationBindings.Bind(AddTextPropertyRow(label, child.Value, v => child.Value = v, CanEditModifiedDocument ? () => RemoveOptionalElement(child) : null, role), child);
            }

            if (CanEditModifiedDocument)
                AddOtherAttributesSelector(tech, handled);

            AddCostsEditor(tech);
            AddChipListEditor(tech, "techtype", "Technology Types");
            AddChipListEditor(tech, "flag", "Flags");
            AddPreservedXmlSection(
                _propertiesPanel,
                TechnologyXmlShapePolicy.FindUnhandledNestedTechnologyChildren(tech, handled),
                "Preserved technology XML");

            AddPrerequisiteHeader(tech);
            AddPrerequisiteButton(tech);
            var prereqsContainer = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("prereqs", StringComparison.OrdinalIgnoreCase));
            var prereqs = prereqsContainer?.Elements().ToList() ?? [];
            if (prereqs.Count == 0 && !IsModifiedTab)
                _prereqsPanel.Children.Add(new TextBlock { Text = "No prerequisites.", Foreground = Brushes.Gray });
            foreach (var prereq in prereqs)
            {
                if (PrerequisiteTypes.Contains(GetPrereqTypeName(prereq), StringComparer.OrdinalIgnoreCase))
                    AddPrereqEditor(tech, prereq);
                else
                    AddPreservedXmlSection(_prereqsPanel, [prereq], "Imported prerequisite XML");
            }

            AddEffectsHeader();
            var effects = TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(tech);
            if (!IsModifiedTab && effects.Count == 0)
                _effectsPanel.Children.Add(new TextBlock { Text = "No effects.", Foreground = Brushes.Gray });
            foreach (var effect in effects)
            {
                await AddEffectEditorAsync(effect);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsEditorBuildCurrent(tech, generation)) return;
            }
            AddPreservedXmlSection(
                _effectsPanel,
                TechnologyXmlShapePolicy.FindNonEffectContainerChildren(tech),
                "Preserved effects-container XML");
            if (CanEditModifiedDocument) AddEffectButton(tech);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEditorBuildCurrent(tech, generation)) return;
            ApplyReadOnlyVisualState();
            FinalizeTechnologyPresentation();
            PresentTechnologyAdditiveReading();
            UpdatePreview();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer selection/build owns the editor now.
        }
        catch (Exception exception)
        {
            if (generation == _editorBuildGeneration)
            {
                _statusMessage.Text = "Technology editor could not be built: " + exception.Message;
                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    var prompt = new Prompt(
                        PromptType.Error,
                        "Unable to display technology",
                        $"The technology editor could not be built. No data was changed.\n\n{exception.Message}")
                    {
                        Width = 620,
                        Height = 260,
                        MinWidth = 520,
                        MinHeight = 220
                    };
                    await prompt.ShowDialog(owner);
                }
            }
        }
        finally
        {
            _editorBuildCancellationContext.Value = previousContext;
            if (generation == _editorBuildGeneration)
                _loadingUi = false;
            if (gateEntered)
                _editorBuildGate.Release();
            if (ReferenceEquals(Interlocked.CompareExchange(
                    ref _activeEditorBuildCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
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
            if (!CanEditModifiedDocument && string.IsNullOrWhiteSpace(element.Value) && !element.HasAttributes) return null;
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
            Margin = new Thickness(
                IconPreviewControl.PropertyGridLeftOffset + (CanEditModifiedDocument ? 40 : 0),
                4,
                12,
                4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        identityLayout.Children.Add(_iconPreviewControl);
        EditorRolloverField.ConfigureIdentity(identityLayout, identityFields, _iconPreviewControl);
        RefreshIconPreview();
        _propertiesPanel.Children.Add(identityLayout);

        var displayName = FindVisible("displaynameid") ?? (CanEditModifiedDocument ? new XElement("displaynameid") : null);
        await AddPrimaryTechnologyRowAsync(tech, displayName, identityFields);

        var rollover = FindVisible("rollovertextid") ?? (CanEditModifiedDocument ? new XElement("rollovertextid") : null);
        if (rollover != null)
            await AddStringBackedPropertyRowAsync(EditorDisplayText.Rollover, rollover, multiline: true, target: identityFields, owner: tech);

        var advancedRollover = FindVisible("advancedrollovertextoverrideid");
        if (advancedRollover != null)
            await AddStringBackedPropertyRowAsync(EditorDisplayText.AdvancedRollover, advancedRollover, multiline: true, removable: CanEditModifiedDocument, target: identityFields);

        var icon = FindVisible("icon");
        if (icon != null || CanEditModifiedDocument)
            AddIconEditor(icon ?? new XElement("icon"), identityFields);

        var status = FindVisible("status");
        if (status != null)
            AddStatusEditor(status, identityFields);

        void RefreshIconPreview()
        {
            if (_iconPreviewControl == null)
                return;
            ObserveUiOperation(_iconPreviewControl.ShowOptionsAsync(
                tech.Elements()
                    .Where(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                    .Select(element => (
                        element.Value,
                        (string?)element.Attribute("culture")))),
                "refresh the technology icon preview");
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
        var cancellationToken = _editorBuildCancellationContext.Value;
        cancellationToken.ThrowIfCancellationRequested();
        if (_pendingStringUpdates.TryGetValue(stringId, out var pendingText))
            return pendingText;
        if (_resolveStringAsync != null && !string.IsNullOrWhiteSpace(stringId))
        {
            var value = await _resolveStringAsync(stringId) ?? stringId;
            cancellationToken.ThrowIfCancellationRequested();
            return value;
        }
        return stringId;
    }

    private void SetPendingTechnologyStringValue(string stringId, string value)
    {
        if (_loadedStringValues.TryGetValue(stringId, out var savedValue) &&
            savedValue.Equals(value, StringComparison.Ordinal))
        {
            _pendingStringUpdates.Remove(stringId);
            return;
        }
        _pendingStringUpdates[stringId] = value;
    }

    // Missing core text fields are form placeholders, not document changes. Allocate
    // the normal owned ID only on an actual edit; the existing save collision checks
    // and owned-string lifecycle then apply exactly as for other Technology fields.
    private string PrepareTechnologyStringField(XElement owner, XElement element, string value)
    {
        var stringId = element.Value.Trim();
        if (stringId.Length == 0)
        {
            if (value.Length == 0) return "";
            stringId = BuildTechnologyStringId((string?)owner.Attribute("name") ?? "", element.Name.LocalName);
            XmlTriviaPreserver.SetText(element, stringId);
        }
        if (element.Parent == null)
            InsertBeforeEffectsOrAppend(owner, element);
        return stringId;
    }

    private async Task AddPrimaryTechnologyRowAsync(XElement tech, XElement? displayNameElement, Panel target)
    {
        var hasIcon = tech.Elements()
            .Any(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
        var typeAttribute = tech.Attribute("type");
        if (!CanEditModifiedDocument && string.IsNullOrWhiteSpace(typeAttribute?.Value)) typeAttribute = null;
        var orderHintAttribute = tech.Attribute("orderhint");
        if (!CanEditModifiedDocument && string.IsNullOrWhiteSpace(orderHintAttribute?.Value)) orderHintAttribute = null;
        if (!CanEditModifiedDocument && displayNameElement == null && typeAttribute == null &&
            orderHintAttribute == null && !hasIcon)
            return;

        var displayGrid = CreatePropertyGrid(displayNameElement != null ? EditorDisplayText.DisplayName : "");
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
                IsEnabled = CanEditModifiedDocument,
                Margin = new Thickness(0, 4, 8, 4)
            }, EditorFieldWidthRole.SingleLineText);
            EditorValidationBindings.Bind(displayBox, displayNameElement);
            var lastDisplayText = text;
            ObserveTechnologyTextEdits(displayBox, (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
                var value = displayBox.Text ?? "";
                if (value.Equals(lastDisplayText, StringComparison.Ordinal)) return;
                stringId = PrepareTechnologyStringField(tech, displayNameElement, value);
                if (stringId.Length == 0) return;
                lastDisplayText = value;
                _pendingStringRemovals.Remove(stringId);
                SetPendingTechnologyStringValue(stringId, value);
                MarkDirty();
            });
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
                IsEnabled = CanEditModifiedDocument,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 4)
            });
            ConfigureTechnologyField(typeSelector, EditorFieldWidthRole.ShortEnum);
            EditorValidationBindings.Bind(typeSelector, tech, "@type");
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
                    if (_loadingUi || !CanEditModifiedDocument) return;
                    if (string.IsNullOrWhiteSpace(value)) tech.Attribute("type")?.Remove();
                    else tech.SetAttributeValue("type", value);
                    MarkDirty();
                    UpdatePreview();
                });
            segment.Children.Add(typeSelector);
            if (CanEditModifiedDocument)
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
            var orderBox = CreateNumericTextBox(tech.Attribute("orderhint")?.Value ?? "", EditorFieldWidthRole.DenseNumeric);
            EditorValidationBindings.Bind(orderBox, tech, "@orderhint");
            ObserveTechnologyTextEdits(orderBox, (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
                var value = (orderBox.Text ?? "").Trim();
                if (value.Length == 0) tech.Attribute("orderhint")?.Remove();
                else tech.SetAttributeValue("orderhint", value);
                MarkDirty();
                UpdatePreview();
            });
            segment.Children.Add(orderBox);
            if (CanEditModifiedDocument)
                segment.Children.Add(CreateRemoveButton(() => tech.Attribute("orderhint")?.Remove()));
            metadataRow.Children.Add(segment);
        }

        if (typeAttribute != null)
            AddTypeEditor();
        if (orderHintAttribute != null)
            AddOrderHintEditor();

        if (CanEditModifiedDocument)
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

    private Button CreateRemoveButton(Action removeAction, bool entireCard = false)
    {
        var button = entireCard ? EditorRemoveButtons.Card() : EditorRemoveButtons.Field();
        button.Click += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            removeAction();
            MarkDirty();
            _ = BuildEditorAsync();
        };
        return button;
    }

    private void RemoveOptionalElement(XElement element)
    {
        if (!CanEditModifiedDocument) return;
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
        EditorValidationBindings.Bind(box, researchPoints);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        EditorNumericInputBehavior.AttachValueChanged(box, () => _loadingUi || !CanEditModifiedDocument, value =>
        {
            researchPoints.Value = value;
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(box);

        if (CanEditModifiedDocument && devotionCost == null)
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
            IsEnabled = CanEditModifiedDocument,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        });
        ConfigureTechnologyField(typeSelector, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(typeSelector, devotionCost, "@devotiontype");
        EditorAutoCompleteService.ConfigureStrict(
            typeSelector,
            DevotionTypes,
            currentType,
            () => _loadingUi,
            preserveUnknownInitialValue: true,
            allowEmpty: false,
            valueCommitted: value =>
            {
                if (_loadingUi || !CanEditModifiedDocument || string.IsNullOrWhiteSpace(value)) return;
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
        EditorValidationBindings.Bind(numberBox, devotionCost);
        EditorNumericInputBehavior.AttachRule(numberBox, ProtoUnitNumericKind.UnsignedInteger);
        ObserveTechnologyTextEdits(numberBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            devotionCost.Value = numberBox.Text ?? "";
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(numberBox);
        if (CanEditModifiedDocument)
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
        Panel? target = null,
        XElement? owner = null)
    {
        var stringId = element.Value.Trim();
        var text = await ResolveTechnologyStringValueAsync(stringId);

        var grid = CreatePropertyGrid(label);
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (multiline)
        {
            EditorRolloverField.Configure(box);
        }
        EditorValidationBindings.Bind(box, element);
        var lastText = text;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        if (removable && CanEditModifiedDocument)
            row.Children.Add(CreateRemoveButton(() => RemoveOptionalElement(element)));
        Grid.SetColumn(row, 1);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (value.Equals(lastText, StringComparison.Ordinal)) return;
            if (owner != null) stringId = PrepareTechnologyStringField(owner, element, value);
            if (stringId.Length == 0) return;
            lastText = value;
            _pendingStringRemovals.Remove(stringId);
            SetPendingTechnologyStringValue(stringId, value);
            MarkDirty();
        });
        grid.Children.Add(row);
        if (multiline)
        {
            row.Children.Clear();
            grid = EditorRolloverField.CreateRow(label, box,
                removable && CanEditModifiedDocument ? CreateRemoveButton(() => RemoveOptionalElement(element)) : null);
        }
        (target ?? _propertiesPanel).Children.Add(grid);
    }

    private TextBox CreateNumericTextBox(string value, EditorFieldWidthRole role = EditorFieldWidthRole.Numeric)
    {
        var box = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox
        {
            Text = value,
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 0, 4)
        }, role);
        ConfigureTechnologyField(box, role);
        return box;
    }

    private void AddCompactNumericPropertyRow(string label, string value, Action<string> setter, EditorFieldWidthRole role = EditorFieldWidthRole.Numeric)
    {
        var grid = CreatePropertyGrid(label);
        var box = CreateNumericTextBox(value, role);
        Grid.SetColumn(box, 1);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            setter(box.Text ?? "");
            MarkDirty();
            UpdatePreview();
        });
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
            ConfigureTechnologyField(picker, EditorFieldWidthRole.LongEnum);
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
        var allCosts = tech.Elements().Where(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)).ToList();
        var costs = allCosts
            .Where(e => GetCaseInsensitiveAttribute(e, "resourcetype") != null)
            .GroupBy(e => GetCaseInsensitiveAttribute(e, "resourcetype")?.Value ?? "", StringComparer.OrdinalIgnoreCase)
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
            if (existing != null) EditorValidationBindings.Bind(box, existing);
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
            box.Margin = new Thickness(0, 4, index < ProtoConstants.KnownResourceTypes.Length - 1 ? 8 : 0, 4);
            Grid.SetColumn(box, index * 2 + 1);
            EditorNumericInputBehavior.AttachValueChanged(box, () => _loadingUi || !CanEditModifiedDocument, value =>
            {
                if (existing == null)
                {
                    existing = new XElement("cost", new XAttribute("resourcetype", resource), value);
                    InsertBeforeEffectsOrAppend(tech, existing);
                    EditorValidationBindings.Bind(box, existing);
                }
                else
                {
                    existing.Value = value;
                }
                MarkDirty();
                UpdatePreview();
            });
            grid.Children.Add(box);
        }
        EditorFormLayout.WrapGridColumns(grid, [0, 1], [2, 3], [4, 5], [6, 7]);
        _propertiesPanel.Children.Add(grid);

        var primaryCosts = costs.Values.ToHashSet(ReferenceEqualityComparer.Instance);
        var preservedCosts = allCosts.Where(cost =>
                !primaryCosts.Contains(cost) ||
                !ProtoConstants.KnownResourceTypes.Contains(
                    GetCaseInsensitiveAttribute(cost, "resourcetype")?.Value ?? "",
                    StringComparer.OrdinalIgnoreCase) ||
                cost.HasElements ||
                cost.Attributes().Any(attribute =>
                    !attribute.Name.LocalName.Equals("resourcetype", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        AddPreservedXmlSection(_propertiesPanel, preservedCosts, "Additional cost XML");
    }

    private void AddStatusEditor(XElement status, Panel? target = null)
    {
        var grid = CreatePropertyGrid("Status");
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Unobtainable", "Active" },
            SelectedItem = ToStatusDisplay(status.Value),
            IsEnabled = CanEditModifiedDocument,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        ConfigureTechnologyField(combo, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(combo, status);
        Grid.SetColumn(combo, 1);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string value) return;
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
        var owningTechnology = icon.Parent ?? _current;
        var grid = CreatePropertyGrid("Icon");
        var initial = ProtoEditorWindow.NormalizeIconCatalogValue(icon.Value, _iconPaths);
        var editor = new AssetPathEditor
        {
            IsEnabled = CanEditModifiedDocument,
            Opacity = CanEditModifiedDocument ? 1.0 : 0.55,
            Margin = new Thickness(0, 4, 0, 4)
        };
        EditorValidationBindings.Bind(editor, icon);
        editor.CompactPresenter.Background = Brush.Parse(CanEditModifiedDocument ? "#0E1110" : "#202220");
        editor.Configure(initial, _iconPaths, value =>
        {
            if (!CanEditModifiedDocument || value.Equals(initial, StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;
            var previewControl = _iconPreviewControl;
            initial = value;
            if (value.Equals(icon.Value, StringComparison.Ordinal))
                return Task.CompletedTask;
            if (owningTechnology == null) return Task.CompletedTask;
            SetOptionalIconValue(owningTechnology, icon, value);
            MarkDirty();
            UpdatePreview();
            if (previewControl != null && ReferenceEquals(_current, owningTechnology))
                ObserveUiOperation(
                    previewControl.ShowOptionsAsync(
                        owningTechnology?.Elements()
                        .Where(element => element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))
                        .Select(element => (element.Value, (string?)element.Attribute("culture")))
                        ?? []),
                    "refresh the technology icon preview");
            return Task.CompletedTask;
        });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        (target ?? _propertiesPanel).Children.Add(grid);
    }

    internal static void SetOptionalIconValue(XElement technology, XElement icon, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (icon.Parent != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(icon);
            icon.Value = "";
            return;
        }
        XmlTriviaPreserver.SetText(icon, value);
        if (icon.Parent == null)
        {
            var following = technology.Elements().FirstOrDefault(e =>
                e.Name.LocalName is "status" or "researchpoints" or "prereqs" or "effects");
            if (following != null) following.AddBeforeSelf(icon);
            else technology.Add(icon);
        }
    }

    private void AddChipListEditor(XElement tech, string tag, string sectionTitle)
    {
        var currentElements = tech.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!CanEditModifiedDocument && currentElements.Count == 0)
            return;

        AddSectionHeader(sectionTitle);
        var content = new StackPanel { Spacing = 4 };
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

        IEnumerable<string> KnownValues()
        {
            var values = (tag.Equals("techtype", StringComparison.OrdinalIgnoreCase) && _techTypeNames.Count > 0
                ? _techTypeNames
                : _original.Values.Concat(_modified.Values)
                    .SelectMany(t => t.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
            foreach (var value in values) yield return value;
        }

        AutoCompleteBox? picker = null;
        void RefreshPickerItems()
        {
            if (picker == null) return;
            var present = tech.Elements()
                .Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            EditorAutoCompleteService.SetCatalogSuggestions(picker, KnownValues().Where(value => !present.Contains(value)));
        }

        void Render()
        {
            chips.Children.Clear();
            foreach (var element in tech.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var captured = element;
                chips.Children.Add(AdditiveChangePresentation.Bind(EditorChipService.CreateBlueChip(
                    captured.Value.Trim(),
                    CanEditModifiedDocument ? () =>
                    {
                        if (tag.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                            captured.Value.Trim().Equals("AgeUpgrade", StringComparison.OrdinalIgnoreCase))
                        {
                            ObserveUiOperation(
                                RemoveAgeUpgradeFlagAsync(tech, captured, Render, RefreshPickerItems),
                                "remove the AgeUpgrade flag");
                            return;
                        }
                        RemoveChip();

                        void RemoveChip()
                        {
                            captured.Remove();
                            MarkDirty();
                            Render();
                            RefreshPickerItems();
                            UpdatePreview();
                        }
                    } : null,
                    readOnly: !CanEditModifiedDocument), captured));
            }
        }

        if (CanEditModifiedDocument)
        {
            picker = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            ConfigureTechnologyField(picker, EditorFieldWidthRole.ChipSearch);
            if (tag.Equals("techtype", StringComparison.OrdinalIgnoreCase))
                EditorValidationBindings.Bind(picker, tech, tag, repeated: true);
            RefreshPickerItems();
            EditorAutoCompleteService.EnableDropdown(picker, () => _loadingUi, selectAllOnFirstClick: false);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string value || string.IsNullOrWhiteSpace(value)) return;
                if (!tech.Elements().Any(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) && e.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    var element = new XElement(tag, value);
                    InsertBeforeEffectsOrAppend(tech, element);
                    if (tag.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                        value.Equals("AgeUpgrade", StringComparison.OrdinalIgnoreCase))
                        _pendingMinorGodRemovals.Remove(GetLinkedMinorGodTechnologyName(tech));
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

    private async Task RemoveAgeUpgradeFlagAsync(
        XElement technology,
        XElement flag,
        Action render,
        Action refreshPickerItems)
    {
        if (!CanEditModifiedDocument || flag.Parent == null)
            return;

        var linkedTechName = GetLinkedMinorGodTechnologyName(technology);
        if (_hasLinkedMinorGod?.Invoke(linkedTechName) == true)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner ||
                _confirmLinkedMinorGodRemovalAsync == null ||
                !await _confirmLinkedMinorGodRemovalAsync(owner, linkedTechName))
                return;
            _pendingMinorGodRemovals.Add(linkedTechName);
        }

        flag.Remove();
        MarkDirty();
        render();
        refreshPickerItems();
        UpdatePreview();
    }

    private string GetLinkedMinorGodTechnologyName(XElement technology)
    {
        var currentName = GetCaseInsensitiveAttribute(technology, "name")?.Value.Trim() ?? "";
        return _pendingTechnologyRenames
                   .LastOrDefault(rename => rename.NewName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                   ?.OldName
               ?? currentName;
    }

    private TextBox AddTextPropertyRow(string label, string value, Action<string> setter, Action? removeAction = null,
        EditorFieldWidthRole role = EditorFieldWidthRole.SingleLineText)
    {
        var grid = CreatePropertyGrid(label);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        TextBox box;
        if (role == EditorFieldWidthRole.Numeric)
        {
            box = CreateNumericTextBox(value);
        }
        else
        {
            box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
            {
                Text = value,
                IsEnabled = CanEditModifiedDocument,
                Margin = new Thickness(0, 4, 0, 4)
            }, role);
        }
        // Initial TextChanged notifications may arrive after the build guard clears.
        // Do not rewrite untouched XML (including self-closing unknown elements).
        var lastValue = box.Text ?? "";
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            var nextValue = box.Text ?? "";
            if (_loadingUi || !CanEditModifiedDocument || nextValue == lastValue) return;
            setter(nextValue);
            lastValue = nextValue;
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(box);
        if (removeAction != null && CanEditModifiedDocument)
            row.Children.Add(CreateRemoveButton(removeAction));
        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);
        return box;
    }

    private void AddTechAgeEditor(XElement techAge)
    {
        var grid = CreatePropertyGrid("Tech Age");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox
        {
            ItemsSource = TechnologyAges,
            SelectedItem = TechnologyAges.FirstOrDefault(age => age.Equals(techAge.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? techAge.Value.Trim(),
            IsEnabled = CanEditModifiedDocument,
            Width = 150,
            MaxWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        ConfigureTechnologyField(combo, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(combo, techAge);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string age) return;
            techAge.Value = age;
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(combo);
        if (CanEditModifiedDocument)
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

    private static void AddPreservedXmlSection(
        Panel target,
        IEnumerable<XElement> elements,
        string title)
    {
        var preserved = elements.ToList();
        if (preserved.Count == 0) return;

        target.Children.Add(new TextBlock
        {
            Text = $"──── {title} ────",
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#C59A52"),
            Margin = new Thickness(0, 15, 0, 5)
        });
        target.Children.Add(new TextBlock
        {
            Text = "This XML is not owned by a structured editor and will be preserved unchanged.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        foreach (var element in preserved)
        {
            var box = new TextEditor
            {
                Text = element.ToString(),
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 42,
                MaxHeight = 180,
                IsReadOnly = true,
                Focusable = true,
                IsTabStop = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6)
            };
            XmlSyntaxEditorService.Configure(box);
            XmlSyntaxEditorService.SetReadOnly(box, isReadOnly: true);
            target.Children.Add(box);
        }
    }

    private void ApplyReadOnlyVisualState()
    {
        var canEdit = CanEditModifiedDocument;
        // Keep the preview's culture-cycle button interactive for original technologies.
        // Every actual property editor still applies its own modified-tab guard.
        _propertiesPanel.IsEnabled = _current != null;
        _prereqsPanel.IsEnabled = canEdit;
        _effectsPanel.IsEnabled = canEdit;
        _propertiesPanel.Opacity = canEdit ? 1.0 : 0.55;
        _prereqsPanel.Opacity = canEdit ? 1.0 : 0.55;
        _effectsPanel.Opacity = canEdit ? 1.0 : 0.55;
        _xmlPreview.IsEnabled = _current != null;
        BindTechnologyXmlPreview();
        _xmlPreview.Focusable = _current != null;
        _xmlPreview.IsTabStop = false;
        _xmlPreview.Opacity = _current != null ? 1.0 : 0.55;
        _xmlPreview.Background = Brush.Parse(canEdit ? "#090C0B" : "#090C0B");
        _xmlPreview.Foreground = Brush.Parse(_current != null ? "#E8DECC" : "#8a8a8a");
        if (_modDocumentSafetyError != null)
            _statusMessage.Text = _modDocumentSafetyError;
    }

    private static void InsertBeforeEffectsOrAppend(XElement tech, XElement element)
        => TechnologyXmlOrderPolicy.InsertNewField(tech, element);

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
        if (!CanEditModifiedDocument) return;

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
            IsEnabled = CanEditModifiedDocument,
            Width = 150,
            MaxWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(typeCombo, EditorFieldWidthRole.LongEnum);
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || typeCombo.SelectedItem is not string selectedType ||
                selectedType.Equals(GetPrereqTypeName(prereq), StringComparison.OrdinalIgnoreCase)) return;
            ReplacePrerequisiteType(prereq, selectedType);
            MarkDirty();
            _ = BuildEditorAsync();
        };
        firstRow.Children.Add(typeCombo);

        AddPrerequisiteFields(prereq, currentType, firstRow);

        if (CanEditModifiedDocument)
        {
            var removePrereq = CreateRemoveButton(() =>
            {
                var parent = prereq.Parent;
                prereq.Remove();
                if (parent != null && !parent.Elements().Any()) parent.Remove();
            }, entireCard: true);
            EditorButtonDescriptions.Set(removePrereq, "Remove prerequisite");
            removePrereq.HorizontalAlignment = HorizontalAlignment.Right;
            removePrereq.VerticalAlignment = VerticalAlignment.Top;
            removePrereq.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(removePrereq, 1);
            shell.Children.Add(removePrereq);
        }

        content.Children.Insert(0, firstRow);
        _prereqsPanel.Children.Add(border);
    }

    private void AddPrerequisiteFields(XElement prereq, string type, WrapPanel firstRow)
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
                AddMultiValuePrerequisiteFields(prereq, firstRow, "cultureName", _cultureNames, "Culture");
                break;
            case "civilization":
                AddMultiValuePrerequisiteFields(prereq, firstRow, "civName", _majorGodNames, "Major god");
                break;
            case "kbstat":
                AddKbStatPrerequisiteFields(prereq, firstRow);
                break;
        }
    }

    private void AddTechStatusPrerequisiteFields(XElement prereq, WrapPanel row)
    {
        row.Children.Add(CreateInlineLabel("Technology"));
        row.Children.Add(EditorValidationBindings.Bind(CreateStrictPrereqSelector(
            _original.Keys.Concat(_modified.Keys),
            prereq.Value.Trim(),
            EditorFieldWidthRole.Reference,
            value =>
            {
                prereq.Value = value;
                MarkDirty();
                UpdatePreview();
            }), prereq));

        row.Children.Add(CreateInlineLabel("Status"));
        var status = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Unobtainable", "Active" },
            SelectedItem = ToStatusDisplay((string?)prereq.Attribute("status") ?? "UNOBTAINABLE"),
            IsEnabled = CanEditModifiedDocument,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(status, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(status, prereq, "@status");
        status.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || status.SelectedItem is not string value) return;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(age, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(age, prereq);
        age.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || age.SelectedItem is not string value) return;
            prereq.Value = value;
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(age);
    }

    private void AddTypeCountPrerequisiteFields(XElement prereq, WrapPanel row)
    {
        row.Children.Add(CreateInlineLabel("Unit"));
        row.Children.Add(EditorValidationBindings.Bind(CreateStrictPrereqSelector(
            _prereqUnitNames,
            (string?)prereq.Attribute("unit") ?? "",
            EditorFieldWidthRole.Reference,
            value => SetPrereqAttribute(prereq, "unit", value)), prereq, "@unit"));

        row.Children.Add(CreateInlineLabel("Count"));
        row.Children.Add(CreateOperatorCombo(prereq));

        var count = CreateNumericTextBox(FormatNumericForDisplay((string?)prereq.Attribute("count") ?? "0"), EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(count, prereq, "@count");
        EditorNumericInputBehavior.AttachRule(count, ProtoUnitNumericKind.UnsignedInteger);
        ObserveTechnologyTextEdits(count, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var raw = (count.Text ?? "0").Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) && parsedCount >= 0)
                SetPrereqAttribute(prereq, "count", parsedCount.ToString("0.00", CultureInfo.InvariantCulture), rebuild: false);
        });
        row.Children.Add(count);

        row.Children.Add(CreateInlineLabel("is"));
        var states = new[] { "alive", "building", "any", "nonexistent" };
        var state = new ComboBox
        {
            ItemsSource = states,
            SelectedItem = ToPrereqStateDisplay((string?)prereq.Attribute("state") ?? "aliveState"),
            IsEnabled = CanEditModifiedDocument,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        EditorValidationBindings.Bind(state, prereq, "@state");
        state.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || state.SelectedItem is not string selected) return;
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
            EditorFieldWidthRole.LongEnum,
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
        EditorValidationBindings.Bind(statSelector, prereq, "@kbStat");
        row.Children.Add(statSelector);

        if (KbStatsUsingResourceParameter.Contains(currentStat))
        {
            row.Children.Add(CreateInlineLabel("Resource"));
            var param = prereq.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("kbParam", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            var resource = new ComboBox
            {
                ItemsSource = ProtoConstants.KnownResourceTypes,
                SelectedItem = ProtoConstants.KnownResourceTypes.FirstOrDefault(x => x.Equals(param, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = CanEditModifiedDocument,
                Width = 100,
                MaxWidth = 100,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 8, 4)
            };
            EditorValidationBindings.Bind(resource, prereq, "@kbParam");
            resource.SelectionChanged += (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument || resource.SelectedItem is not string value) return;
                SetCaseInsensitiveAttribute(prereq, "kbParam", value);
                MarkDirty();
                UpdatePreview();
            };
            row.Children.Add(resource);
        }

        row.Children.Add(CreateOperatorCombo(prereq));
        var valueBox = CreateNumericTextBox(FormatNumericForDisplay((string?)prereq.Attribute("value") ?? "0"), EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(valueBox, prereq, "@value");
        ObserveTechnologyTextEdits(valueBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetPrereqAttribute(prereq, "value", valueBox.Text ?? "0", rebuild: false);
        });
        row.Children.Add(valueBox);
    }

    private void AddMultiValuePrerequisiteFields(
        XElement prereq,
        WrapPanel firstRow,
        string childName,
        IEnumerable<string> options,
        string label)
    {
        var entries = prereq.Elements().Where(e => e.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0 && !string.IsNullOrWhiteSpace(prereq.Value))
            entries.Add(new XElement(childName, prereq.Value.Trim()));
        if (entries.Count == 0 && CanEditModifiedDocument)
            entries.Add(new XElement(childName)); // A detached form placeholder must not dirty an untouched document.

        var catalogOptions = options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var activeEntry = entry;
            var isAttached = activeEntry.Parent == prereq;
            var currentValue = activeEntry.Value.Trim();
            IEnumerable<string> AvailableOptions()
            {
                foreach (var option in GetAvailableMultiValuePrerequisiteOptions(catalogOptions,
                    prereq.Elements().Where(other => other.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(other, activeEntry)).Select(other => other.Value.Trim()), activeEntry.Value.Trim()))
                    yield return option;
            }
            var entryGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 8, 0)
            };
            if (index == 0)
                entryGroup.Children.Add(CreateInlineLabel(label));
            var entrySelector = CreateStrictPrereqSelector(AvailableOptions(), currentValue, EditorFieldWidthRole.Reference, value =>
            {
                if (!isAttached)
                {
                    activeEntry = new XElement(childName, value);
                    foreach (var text in prereq.Nodes().OfType<XText>().ToArray()) text.Remove();
                    prereq.Add(activeEntry);
                    isAttached = true;
                }
                else
                {
                    activeEntry.Value = value;
                }
                MarkDirty();
                UpdatePreview();
                // Clearing/choosing a value must not replace the focused input.
                // Refresh only membership/exclusions and dependent Add availability.
                var loading = _loadingUi;
                _loadingUi = true;
                try
                {
                    // The active AutoCompleteBox is still processing its text event.
                    // Resetting its ItemsSource here can restore the just-deleted text.
                    foreach (var control in firstRow.Children.Where(control => !ReferenceEquals(control, entryGroup)))
                        SelectorCatalogRefresh.Apply(control);
                }
                finally { _loadingUi = loading; }
            }, deferSelectionCommit: true, selectAllOnFirstClick: true);
            EditorValidationBindings.Bind(entrySelector, isAttached ? entry : prereq);
            if (CanEditModifiedDocument && isAttached)
                entrySelector.Margin = new Thickness(entrySelector.Margin.Left, entrySelector.Margin.Top, 0, entrySelector.Margin.Bottom);
            entryGroup.Children.Add(entrySelector);
            if (CanEditModifiedDocument && isAttached)
                entryGroup.Children.Add(CreateRemoveButton(() => entry.Remove()));
            EditorFormLayout.UseInlineRowLayout(entryGroup);
            entryGroup.Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap);
            firstRow.Children.Add(entryGroup);
        }

        if (CanEditModifiedDocument)
        {
            var add = CreateOptionalPropertyButton($"Add {label.ToLowerInvariant()}");
            add.Margin = new Thickness(8, 4, 8, 4);
            bool HasUnusedOptions()
            {
                var used = prereq.Elements().Where(element => element.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    .Select(element => element.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!prereq.HasElements) used.Add(prereq.Value.Trim());
                return catalogOptions.Any(option => !used.Contains(option));
            }
            SelectorCatalogRefresh.RegisterDependent(add, () => add.IsEnabled = HasUnusedOptions());
            add.Click += (_, _) =>
            {
                if (!HasUnusedOptions()) return;
                // Preserve a legacy direct-text prerequisite when adding a second entry.
                if (!prereq.HasElements && !string.IsNullOrWhiteSpace(prereq.Value))
                {
                    var legacy = prereq.Value.Trim();
                    foreach (var text in prereq.Nodes().OfType<XText>().ToArray()) text.Remove();
                    prereq.Add(new XElement(childName, legacy));
                }
                prereq.Add(new XElement(childName));
                MarkDirty();
                UpdatePreview();
                _ = BuildEditorAsync();
            };
            firstRow.Children.Add(add);
        }
    }

    internal static IEnumerable<string> GetAvailableMultiValuePrerequisiteOptions(
        IEnumerable<string> options,
        IEnumerable<string> valuesUsedByOtherEntries,
        string currentValue)
    {
        var used = valuesUsedByOtherEntries
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return options
            .Where(option => option.Equals(currentValue, StringComparison.OrdinalIgnoreCase) || !used.Contains(option))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private AutoCompleteBox CreateStrictPrereqSelector(
        IEnumerable<string> options,
        string current,
        EditorFieldWidthRole role,
        Action<string> committed,
        bool deferSelectionCommit = false,
        bool selectAllOnFirstClick = false)
    {
        var values = options
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var selector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
        {
            Text = current,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = CanEditModifiedDocument,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        });
        ConfigureTechnologyField(selector, role);
        var lastCommittedValue = current.Trim();
        EditorAutoCompleteService.ConfigureStrict(
            selector,
            values,
            current,
            () => _loadingUi,
            preserveUnknownInitialValue: true,
            allowEmpty: true,
            commitEmptyAsValid: true,
            deferSelectionCommit: deferSelectionCommit,
            selectAllOnFirstClick: selectAllOnFirstClick,
            keepStartVisibleAfterCommit: true,
            valueCommitted: value =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
                var normalized = value.Trim();
                if (normalized.Equals(lastCommittedValue, StringComparison.OrdinalIgnoreCase)) return;
                lastCommittedValue = normalized;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 62,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 6, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string symbol) return;
            SetPrereqAttribute(prereq, "operator", SymbolToOperator(symbol));
        };
        ConfigureTechnologyField(combo, EditorFieldWidthRole.ComparisonOperator);
        EditorValidationBindings.Bind(combo, prereq, "@operator");
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
        if (string.Equals(GetCaseInsensitiveAttribute(prereq, name)?.Value, value, StringComparison.Ordinal)) return;
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
            "Culture" => new XElement("culture", new XElement("cultureName", "")),
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

    private static string? ToPrereqStateDisplay(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "alivestate" => "alive",
            "buildingstate" => "building",
            "anystate" => "any",
            "nonexiststate" => "nonexistent",
            _ => null
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
            if (!CanEditModifiedDocument) return;
            var container = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
            var effect = new XElement("effect");
            if (container != null)
            {
                container.Add(effect);
            }
            else if (tech.Elements().Any(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
            {
                tech.Add(effect);
            }
            else
            {
                container = new XElement("effects", effect);
                tech.Add(container);
            }
            MarkDirty();
            _ = BuildEditorAsync();
        };
        _effectsPanel.Children.Add(add);
    }

    private void ResetEffectForType(XElement effect, string type)
    {
        _effectsPendingDefaultInitialization.Remove(effect);
        QueueEffectOwnedStringsForRemoval(effect);
        effect.RemoveAttributes();
        effect.RemoveNodes();

        if (string.IsNullOrWhiteSpace(type))
            return;

        effect.SetAttributeValue("type", type);
        _effectsPendingDefaultInitialization.Add(effect);
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
        AdditiveChangePresentation.Bind(border, effect);

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var selectorRow = new WrapPanel { Orientation = Orientation.Horizontal };
        headerGrid.Children.Add(selectorRow);
        if (CanEditModifiedDocument)
        {
            var removeEffect = CreateRemoveButton(() =>
            {
                _effectsPendingDefaultInitialization.Remove(effect);
                QueueEffectOwnedStringsForRemoval(effect);
                var parent = effect.Parent;
                effect.Remove();
                if (parent != null && parent.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase) && !parent.Elements().Any())
                    parent.Remove();
            }, entireCard: true);
            EditorButtonDescriptions.Set(removeEffect, "Remove effect");
            removeEffect.HorizontalAlignment = HorizontalAlignment.Right;
            removeEffect.VerticalAlignment = VerticalAlignment.Top;
            removeEffect.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(removeEffect, 1);
            headerGrid.Children.Add(removeEffect);
        }

        selectorRow.Children.Add(CreateInlineLabel("Type"));

        var currentType = ((string?)effect.Attribute("type") ?? "").Trim();
        var typeSelector = CreateStrictEffectSelector(TechnologyEffectCatalogInventory.CreatableEffectTypes, currentType, value =>
        {
            if (value.Equals(currentType, StringComparison.OrdinalIgnoreCase)) return;
            ResetEffectForType(effect, value);
            MarkDirty();
            _ = BuildEditorAsync();
        }, EditorFieldWidthRole.LongEnum);
        selectorRow.Children.Add(typeSelector);
        EditorValidationBindings.Bind(typeSelector, effect, "@type");

        if (currentType.Equals("Data", StringComparison.OrdinalIgnoreCase))
        {
            var currentSubtype = ((string?)effect.Attribute("subtype") ?? "").Trim();
            var displayedSubtype = currentSubtype.Equals("Damagebonus", StringComparison.OrdinalIgnoreCase) ? "DamageBonus" : currentSubtype;
            var subtypeSelector = CreateStrictEffectSelector(TechnologyEffectCatalogInventory.CreatableDataSubtypes, displayedSubtype, value =>
            {
                if (value.Equals(currentSubtype, StringComparison.OrdinalIgnoreCase)) return;
                ResetDataEffectForSubtype(effect, value);
                MarkDirty();
                UpdatePreview();
                _ = BuildEditorAsync();
            }, EditorFieldWidthRole.LongEnum);
            subtypeSelector.Margin = new Thickness(8, 4, 0, 4);
            selectorRow.Children.Add(subtypeSelector);
            EditorValidationBindings.Bind(subtypeSelector, effect, "@subtype");
        }

        var hideTooltipAttribute = effect.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("hideTooltip", StringComparison.OrdinalIgnoreCase));
        if (CanEditModifiedDocument || hideTooltipAttribute != null)
        {
            var hideTooltip = new CheckBox
            {
                Content = "Hide tooltip",
                IsChecked = hideTooltipAttribute != null,
                IsEnabled = CanEditModifiedDocument,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 8, 4)
            };
            hideTooltip.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
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
            if (CanEditModifiedDocument)
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
            var delayBox = CreateNumericTextBox(FormatNumericForDisplay(delayAttribute.Value), EditorFieldWidthRole.Numeric);
            EditorNumericInputBehavior.AttachRule(delayBox, ProtoUnitNumericKind.UnsignedFloat);
            ObserveTechnologyTextEdits(delayBox, (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
                delayAttribute.Value = delayBox.Text ?? "0";
                MarkDirty();
                UpdatePreview();
            });
            segment.Children.Add(delayBox);
            if (CanEditModifiedDocument) segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "delay")));
            selectorRow.Children.Add(segment);
        }

        var tooltipIdAttribute = effect.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("tooltipID", StringComparison.OrdinalIgnoreCase));
        if (tooltipIdAttribute == null && CanEditModifiedDocument)
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

        var initializeDefaults = CanEditModifiedDocument && _effectsPendingDefaultInitialization.Remove(effect);
        var previousInitializationState = _initializingEffectDefaults;
        _initializingEffectDefaults = initializeDefaults;
        try
        {
            var structured = await AddStructuredEffectBodyAsync(effect, content, currentType);
            if (ShouldShowRawEffectXmlEditor(effect, currentType, structured))
                AddRawEffectXmlEditor(effect, content);
        }
        catch
        {
            if (initializeDefaults)
                _effectsPendingDefaultInitialization.Add(effect);
            throw;
        }
        finally
        {
            _initializingEffectDefaults = previousInitializationState;
        }

        _effectsPanel.Children.Add(border);
    }

    internal static bool ShouldShowRawEffectXmlEditor(XElement effect, string currentType, bool structured)
    {
        if (structured || string.IsNullOrWhiteSpace(currentType))
            return false;
        return !currentType.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(GetCaseInsensitiveAttribute(effect, "subtype")?.Value);
    }

    private async Task<bool> AddStructuredEffectBodyAsync(XElement effect, StackPanel content, string currentType)
    {
        if (currentType.Equals("Data", StringComparison.OrdinalIgnoreCase))
        {
            var subtype = GetCaseInsensitiveAttribute(effect, "subtype")?.Value ?? "";
            if (!TechnologyEffectCatalogInventory.IsStructuredDataSubtype(subtype))
                return false;
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
            if (subtype.Equals("GodPowerCost", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("GodPowerCostFactor", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerRelativityAmountDataEffectEditor(effect, content, allowOverride: true);
                return true;
            }
            if (subtype.Equals("GodPowerROF", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("GodPowerROFFactor", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("TributePenalty", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerRelativityAmountDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("TimeShiftingConcurrentShifts", StringComparison.OrdinalIgnoreCase))
            {
                AddRestrictedPlayerRelativityAmountDataEffectEditor(
                    effect, content, ["Absolute", "Assign", "Override"]);
                return true;
            }
            if (subtype.Equals("Enable", StringComparison.OrdinalIgnoreCase))
            {
                AddEnableDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("DoubleEffect", StringComparison.OrdinalIgnoreCase))
            {
                AddDoubleEffectDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("Cost", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("cost", StringComparison.OrdinalIgnoreCase))
            {
                AddCostDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("ResearchPoints", StringComparison.OrdinalIgnoreCase))
            {
                AddResearchPointsDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BuildingChainActive", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("CombatXP", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("RevealAllyUI", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("RevealEnemyUI", StringComparison.OrdinalIgnoreCase))
            {
                AddPlayerEnableDisableDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("GodPower", StringComparison.OrdinalIgnoreCase))
            {
                AddGodPowerDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BuildingChainEffect", StringComparison.OrdinalIgnoreCase))
            {
                AddBuildingChainEffectDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BuildingChainResourceFactor", StringComparison.OrdinalIgnoreCase))
            {
                AddBuildingChainResourceFactorDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("BuffIconOverride", StringComparison.OrdinalIgnoreCase))
            {
                await AddIconOverrideDataEffectEditorAsync(effect, content, subtype, includePower: false);
                return true;
            }
            if (subtype.Equals("PowerIconOverride", StringComparison.OrdinalIgnoreCase))
            {
                await AddIconOverrideDataEffectEditorAsync(effect, content, subtype, includePower: true);
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
            if (subtype.Equals("SetGoalFlag", StringComparison.OrdinalIgnoreCase))
            {
                AddSetGoalFlagDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("SetGoalSpawnLocationLand", StringComparison.OrdinalIgnoreCase) ||
                subtype.Equals("SetGoalSpawnLocationWater", StringComparison.OrdinalIgnoreCase))
            {
                AddSetGoalSpawnLocationDataEffectEditor(effect, content);
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
            if (subtype.Equals("ResourceByKBQuery", StringComparison.OrdinalIgnoreCase))
            {
                AddResourceByKbQueryDataEffectEditor(effect, content);
                return true;
            }
            if (subtype.Equals("ResourceByKBStat", StringComparison.OrdinalIgnoreCase))
            {
                AddResourceByKbStatDataEffectEditor(effect, content);
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
                AddEnableDisableUnitDataEffectEditor(
                    effect,
                    content,
                    includeAction: true,
                    allowAllActions: !subtype.Equals("ActionEnable", StringComparison.OrdinalIgnoreCase));
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

        if (!TechnologyEffectCatalogInventory.IsStructuredEffectType(currentType)) return false;

        switch (currentType.ToLowerInvariant())
        {
            case "setname":
                await AddSetNameEffectEditorAsync(effect, content);
                break;
            case "sound":
                AddSimpleEffectValueEditor(effect, content, "Play sound", EditorFieldWidthRole.SingleLineText);
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
                AddSimpleEffectValueEditor(effect, content, "Console command", EditorFieldWidthRole.SingleLineText);
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
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
            row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        }
        AddDataRelativityAndAmountEditors(effect, row);

        var options = includeDivine
            ? new[] { "All", "Hack", "Pierce", "Crush", "Divine" }
            : new[] { "All", "Hack", "Pierce", "Crush" };
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "All";
        var typeCombo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 8, 4)
        };
        EditorValidationBindings.Bind(typeCombo, effect, "@" + attributeName);
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || typeCombo.SelectedItem is not string selected) return;
            if (selected.Equals("All", StringComparison.OrdinalIgnoreCase)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment(typeLabel, typeCombo));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddDamageBonusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddResourceAmountDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddPlayerResourceDataEffectEditor(XElement effect, StackPanel content)
    {
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizeResourceEffect(effect))
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
            IsEnabled = CanEditModifiedDocument,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ConfigureTechnologyField(relativityCombo, EditorFieldWidthRole.ShortEnum);
        relativityCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || relativityCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(relativityCombo);
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
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
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
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
                EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        }
        AddDataRelativityAndAmountEditors(effect, row);
        if (includeResource)
            row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
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
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var ageCombo = new ComboBox
        {
            ItemsSource = ageAmounts.Keys.ToList(),
            SelectedItem = ageAmounts.First(pair => pair.Value == currentAmount).Key,
            IsEnabled = CanEditModifiedDocument,
            Width = 110,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(ageCombo, EditorFieldWidthRole.LongEnum);
        ageCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || ageCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", ageAmounts[selected]);
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Age", ageCombo));
        content.Children.Add(row);
    }

    private void AddPlayerPowerAmountDataEffectEditor(XElement effect, StackPanel content, bool allowOverride = false)
    {
        MigrateDataAttribute(effect, "power", "protopower");
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Power", CreateStrictEffectSelector(
            _godPowerNames,
            GetCaseInsensitiveAttribute(effect, "protopower")?.Value.Trim() ??
            GetCaseInsensitiveAttribute(effect, "power")?.Value.Trim() ?? "",
            value =>
            {
                RemoveCaseInsensitiveAttribute(effect, "power");
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "protopower");
                else SetCaseInsensitiveAttribute(effect, "protopower", value);
                MarkDirty();
                UpdatePreview();
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@protopower")));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        content.Children.Add(row);
    }

    private void AddGodPowerDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureFixedDataAttribute(effect, "relativity", "Absolute");
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "cooldown", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Power", CreateStrictEffectSelector(
            _godPowerNames,
            GetCaseInsensitiveAttribute(effect, "power")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "power", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@power")));
        row.Children.Add(CreateRestrictedDataRelativityCombo(effect, ["Absolute", "Assign"]));
        row.Children.Add(CreateLabeledEffectSegment(
            "Amount",
            CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment(
            "Cooldown",
            CreatePositiveFloatEffectBox(effect, "cooldown", EditorFieldWidthRole.Numeric, "1")));
        content.Children.Add(row);
    }

    private void AddBuildingChainEffectDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "effecttype", "Connected");
        EnsurePlayerTarget(effect);

        var currentModifyType = ProtoConstants.GetModifyTypeValue(GetCaseInsensitiveAttribute(effect, "modifytype")?.Value ?? "");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateLabeledEffectSegment("If", CreateDataAttributeCombo(
            effect, "effecttype", ["Connected", "Isolate", "InRange"], "Connected", EditorFieldWidthRole.LongEnum)));
        row.Children.Add(CreateLabeledEffectSegment("Modify", CreateStrictEffectSelector(
            ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName),
            ProtoConstants.GetModifyTypeDisplayName(currentModifyType),
            value =>
            {
                var normalized = ProtoConstants.GetModifyTypeValue(value);
                if (string.IsNullOrWhiteSpace(normalized)) RemoveCaseInsensitiveAttribute(effect, "modifytype");
                else SetCaseInsensitiveAttribute(effect, "modifytype", normalized);
                if (normalized is not "DamageSpecific" and not "ArmorSpecific")
                {
                    RemoveCaseInsensitiveAttribute(effect, "damagetype");
                    _openOnHitOptionalSelectors.Remove((effect, "damagetype"));
                }
                _ = BuildEditorAsync();
            },
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype")));
        if (currentModifyType is "DamageSpecific" or "ArmorSpecific")
        {
            EnsureFixedDataAttribute(effect, "damagetype", "Hack");
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateRequiredDataTypeCombo(effect, currentModifyType == "DamageSpecific")));
        }
        AddDataRelativityAndAmountEditors(effect, row);
        AddOptionalEffectAttribute(
            row, effect, "Damage time out", "timeout", "0", EditorFieldWidthRole.Numeric,
            ProtoUnitNumericKind.UnsignedFloat, buttonLeftSpacing: 8);
        content.Children.Add(row);
    }

    private void AddBuildingChainResourceFactorDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        content.Children.Add(row);
    }

    private async Task AddIconOverrideDataEffectEditorAsync(
        XElement effect,
        StackPanel content,
        string subtype,
        bool includePower)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsurePlayerTarget(effect);

        var pathStringId = GetCaseInsensitiveAttribute(effect, "pathstrid")?.Value.Trim() ?? "";
        var iconPath = pathStringId.Length == 0 ? "" : await ResolveTechnologyStringValueAsync(pathStringId);
        var iconEditor = new AssetPathEditor
        {
            IsEnabled = CanEditModifiedDocument,
            Opacity = CanEditModifiedDocument ? 1.0 : 0.55,
            Width = 260,
            Margin = new Thickness(0, 4, 0, 4)
        };
        iconEditor.CompactPresenter.Background = Brush.Parse(CanEditModifiedDocument ? "#0E1110" : "#202220");
        var configuredIconPath = ProtoEditorWindow.NormalizeIconCatalogValue(iconPath, _iconPaths);
        iconEditor.Configure(
            configuredIconPath,
            _iconPaths,
            async value =>
            {
                if (!CanEditModifiedDocument || value.Equals(configuredIconPath, StringComparison.OrdinalIgnoreCase)) return;
                configuredIconPath = value;
                var existingId = GetCaseInsensitiveAttribute(effect, "pathstrid")?.Value.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (IsOwnedIconOverrideStringId(existingId)) QueueStringForRemoval(existingId);
                    RemoveCaseInsensitiveAttribute(effect, "pathstrid");
                    MarkDirty();
                    UpdatePreview();
                    return;
                }

                var technology = effect.Ancestors().FirstOrDefault(element =>
                    element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? _current;
                if (technology == null) return;
                var technologyName = GetCaseInsensitiveAttribute(technology, "name")?.Value.Trim() ?? "";
                var stringId = IsIconOverrideStringIdForEffect(existingId, technologyName, subtype)
                    ? existingId
                    : BuildNextIconOverrideStringId(technology, subtype, effect);
                if (!existingId.Equals(stringId, StringComparison.OrdinalIgnoreCase) && IsOwnedIconOverrideStringId(existingId))
                    QueueStringForRemoval(existingId);
                SetCaseInsensitiveAttribute(effect, "pathstrid", stringId);
                _pendingStringRemovals.Remove(stringId);
                _pendingStringUpdates[stringId] = value;
                MarkDirty();
                UpdatePreview();
                await Task.CompletedTask;
            });

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        if (includePower)
        {
            row.Children.Add(CreateLabeledEffectSegment("Power", CreateStrictEffectSelector(
                _godPowerNames,
                GetCaseInsensitiveAttribute(effect, "protopower")?.Value.Trim() ?? "",
                value => SetOptionalDataAttribute(effect, "protopower", value),
                EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@protopower")));
        }
        row.Children.Add(CreateLabeledEffectSegment("New icon", iconEditor));
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
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Type", CreateDataAttributeCombo(
            effect, "goaltype", ["Damage", "Resource", "DeathCount"], "Damage", EditorFieldWidthRole.LongEnum)));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Tracking type", CreateDataAttributeCombo(
            effect, "rewardtrackingtype", ["Single", "PerPossibleReward"], "Single", EditorFieldWidthRole.LongEnum)));
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
            EditorFieldWidthRole.LongEnum,
            selected =>
            {
                RemoveCaseInsensitiveAttribute(effect, "contributorid");
                _ = BuildEditorAsync();
            });

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Type", typeCombo));
        if (currentType.Equals("Resource", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateResourceCombo(effect, "contributorid"));
        else
        {
            var selector = CreateStrictEffectSelector(
                _prereqUnitNames,
                GetCaseInsensitiveAttribute(effect, "contributorid")?.Value.Trim() ?? "",
                value => SetOptionalDataAttribute(effect, "contributorid", value),
                EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@contributorid");
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
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Reward", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "rewardtype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "rewardtype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@rewardtype")));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        content.Children.Add(row);
    }

    private void AddGoalRewardExclusionDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Exclude", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "rewardtype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "rewardtype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@rewardtype")));
        content.Children.Add(row);
    }

    private void AddSetGoalActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddSetGoalFlagDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var flags = new[]
        {
            "AgeRestricted", "ArchaicSpawn", "CultureRestricted", "CurrentAgeOnly",
            "DisplayOnHUD", "ForceSpawn", "NextAgeOnly", "IgnoreAgeRoll"
        };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Flag", CreateStrictEffectSelector(
            flags,
            GetCaseInsensitiveAttribute(effect, "flagname")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "flagname", value),
            EditorFieldWidthRole.LongEnum,
            preserveSuggestionOrder: true, validationSource: effect, validationField: "@flagname")));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddSetGoalSpawnLocationDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Goal", CreateInternalNameEffectAttributeBox(effect, "goalname", EditorFieldWidthRole.SingleLineText)));
        row.Children.Add(CreateLabeledEffectSegment("Location", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "locationprotoid")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "locationprotoid", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@locationprotoid")));
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
            EditorFieldWidthRole.LongEnum,
            selected => { _ = BuildEditorAsync(); });

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Condition", conditionCombo));
        var unitSelector = CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype");
        if (currentCondition.Equals("Damage", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateLabeledEffectSegment("From", unitSelector));
        else
        {
            unitSelector.Margin = new Thickness(8, 4, 0, 4);
            row.Children.Add(unitSelector);
        }
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resourcetype")));
        AddDataRelativityAndAmountEditors(effect, row);
        if (allowAttackerBonus && currentCondition.Equals("Destroy", StringComparison.OrdinalIgnoreCase))
        {
            AddOnHitOptionalReferenceSelector(
                row, effect, "Bonus for", "Bonus for", "attackertype", _prereqUnitNames, EditorFieldWidthRole.Reference,
                showWhenMissing: false, buttonLeftSpacing: 8);
        }
        content.Children.Add(row);
    }

    private ComboBox CreateDataAttributeCombo(
        XElement effect,
        string attributeName,
        IReadOnlyList<string> options,
        string defaultValue,
        EditorFieldWidthRole role,
        Action<string>? selectionChanged = null)
    {
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? defaultValue;
        var values = options.Append(current).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = values.First(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = CanEditModifiedDocument,
            Width = EditorFormLayout.WidthFor(role),
            Margin = new Thickness(0, 4, 0, 4)
        };
        ConfigureTechnologyField(combo, role);
        EditorValidationBindings.Bind(combo, effect, "@" + attributeName);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
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
            IsEnabled = CanEditModifiedDocument,
            Width = 150,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(componentCombo, EditorFieldWidthRole.LongEnum);
        componentCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || componentCombo.SelectedItem is not string selected) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Component", componentCombo));
        if (currentComponent.Equals("BuyFactorSpecific", StringComparison.OrdinalIgnoreCase) ||
            currentComponent.Equals("SellFactorSpecific", StringComparison.OrdinalIgnoreCase))
            row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        content.Children.Add(row);
    }

    private void AddMarketResetDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "amount", "1");
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect))
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
        row.Children.Add(CreateLabeledEffectSegment("Reset rate", resetRate));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateLabeledEffectSegment(
            "Time Ratio",
            CreateUnsignedFloatEffectBox(effect, "timeratio", EditorFieldWidthRole.Numeric, "0")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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

    private void AddRestrictedPlayerRelativityAmountDataEffectEditor(
        XElement effect,
        StackPanel content,
        IReadOnlyList<string> allowedRelativities)
    {
        EnsureFixedDataAttribute(effect, "relativity", allowedRelativities[0]);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateRestrictedDataRelativityCombo(effect, allowedRelativities));
        row.Children.Add(CreateLabeledEffectSegment(
            "Amount",
            CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        content.Children.Add(row);
    }

    private void AddPlayerEnableDisableDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddEnableDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var playerMode = string.Equals(
            GetCaseInsensitiveAttribute(target ?? new XElement("target"), "type")?.Value,
            "Player",
            StringComparison.OrdinalIgnoreCase);
        if (playerMode)
        {
            EnsureFixedDataAttribute(effect, "system", "BonusUnits");
        }
        else if (_initializingEffectDefaults && GetCaseInsensitiveAttribute(effect, "system") != null)
        {
            RemoveCaseInsensitiveAttribute(effect, "system");
            MarkDirty();
            UpdatePreview();
        }
        var targetMode = playerMode ? "Player" : "Unit";
        var targetModeCombo = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Player" },
            SelectedItem = targetMode,
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 8, 4)
        };
        targetModeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || targetModeCombo.SelectedItem is not string selected) return;
            var currentTarget = EnsureDataTarget(effect);
            currentTarget.RemoveAttributes();
            currentTarget.RemoveNodes();
            currentTarget.SetAttributeValue("type", selected == "Player" ? "Player" : "ProtoUnit");
            RemoveCaseInsensitiveAttribute(effect, "system");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            _ = BuildEditorAsync();
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(targetModeCombo);
        if (playerMode)
        {
            row.Children.Add(CreateLabeledEffectSegment(
                "System",
                CreateDataAttributeCombo(
                    effect,
                    "system",
                    ["BonusUnits", "BountyResourceEarning"],
                    "BonusUnits",
                    EditorFieldWidthRole.LongEnum)));
        }
        else
        {
            row.Children.Add(CreateStrictEffectSelector(
                _protoUnitNames,
                target?.Value.Trim() ?? "",
                value =>
                {
                    var currentTarget = EnsureDataTarget(effect);
                    SetCaseInsensitiveAttribute(currentTarget, "type", "ProtoUnit");
                    currentTarget.Value = value;
                    RemoveCaseInsensitiveAttribute(effect, "system");
                    MarkDirty();
                    UpdatePreview();
                },
                EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "ProtoUnit", "Unit"), validationField: "target"));
        }
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddDoubleEffectDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureExactDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddTechnologyTargetEditor(effect, row);
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        content.Children.Add(row);
    }

    private void AddCostDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");

        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var targetType = GetCaseInsensitiveAttribute(target ?? new XElement("target"), "type")?.Value ?? "ProtoUnit";
        var unitMode = targetType.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase);
        var targetKindCombo = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Tech" },
            SelectedItem = unitMode ? "Unit" : "Tech",
            IsEnabled = CanEditModifiedDocument,
            Width = 90,
            Margin = new Thickness(0, 4, 8, 4)
        };
        targetKindCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || targetKindCombo.SelectedItem is not string selected) return;
            SetTechnologyDataTargetMode(effect, selected == "Unit" ? "ProtoUnit" : "Tech");
            _ = BuildEditorAsync();
        };

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(targetKindCombo);
        if (unitMode)
        {
            row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
                _prereqUnitNames,
                target?.Value.Trim() ?? "",
                value =>
                {
                    var currentTarget = EnsureDataTarget(effect);
                    SetCaseInsensitiveAttribute(currentTarget, "type", "ProtoUnit");
                    currentTarget.Value = value;
                    MarkDirty();
                    UpdatePreview();
                },
                EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "ProtoUnit", "Unit"), validationField: "target")));
        }
        else
        {
            AddTechnologyTargetEditor(effect, row);
        }

        AddDataRelativityAndAmountEditors(effect, row, allowOverride: true);
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddOnHitOptionalReferenceSelector(
            row,
            effect,
            "Required tech",
            "Required tech",
            "reqTech",
            GetTechnologyNames(),
            EditorFieldWidthRole.Reference,
            showWhenMissing: false,
            buttonLeftSpacing: 8);
        content.Children.Add(row);
    }

    private void AddResearchPointsDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddTechnologyTargetEditor(effect, row);
        AddDataRelativityAndAmountEditors(effect, row);
        content.Children.Add(row);
    }

    private void AddTechnologyTargetEditor(XElement effect, WrapPanel row)
    {
        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var targetType = GetCaseInsensitiveAttribute(target ?? new XElement("target"), "type")?.Value ?? "Tech";
        var currentMode = TechnologyTargetTypeToDisplay(targetType);
        var modeCombo = new ComboBox
        {
            ItemsSource = new[] { "Tech", "All Techs", "Tech with Flag", "Tech Type" },
            SelectedItem = target != null && new[] { "Tech", "techAll", "techWithFlag", "TechType" }.Contains(targetType, StringComparer.OrdinalIgnoreCase) ? currentMode : null,
            IsEnabled = CanEditModifiedDocument,
            Width = 125,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ConfigureTechnologyField(modeCombo, EditorFieldWidthRole.LongEnum);
        if (effect.Elements().Count(element => element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)) == 1)
            EditorValidationBindings.Bind(modeCombo, effect, "target/@type");
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || modeCombo.SelectedItem is not string selected) return;
            SetTechnologyDataTargetMode(effect, TechnologyTargetDisplayToType(selected));
            _ = BuildEditorAsync();
        };
        row.Children.Add(modeCombo);

        if (currentMode == "Tech")
        {
            row.Children.Add(CreateStrictEffectSelector(
                GetTechnologyNames(),
                target?.Value.Trim() ?? "",
                value => SetTechnologyDataTargetValue(effect, "Tech", value),
                EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "Tech"), validationField: "target"));
            return;
        }
        if (currentMode == "Tech with Flag")
        {
            row.Children.Add(CreateStrictEffectSelector(
                GetTechnologyFlagNames(),
                target?.Value.Trim() ?? "",
                value => SetTechnologyDataTargetValue(effect, "techWithFlag", value),
                EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "techWithFlag"), validationField: "target"));
            return;
        }
        if (currentMode == "Tech Type")
        {
            row.Children.Add(CreateStrictEffectSelector(
                _techTypeNames,
                target?.Value.Trim() ?? "",
                value => SetTechnologyDataTargetValue(effect, "TechType", value),
                EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "TechType"), validationField: "target"));
            return;
        }

        AddAllTechnologiesTargetOptions(effect, target, row);
    }

    private static XElement? RepresentedDataTarget(XElement effect, params string[] types)
    {
        var targets = effect.Elements().Where(element => element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).ToArray();
        return targets.Length == 0 || targets.Length == 1 && types.Contains(GetCaseInsensitiveAttribute(targets[0], "type")?.Value ?? "", StringComparer.OrdinalIgnoreCase)
            ? effect : null;
    }

    private void AddAllTechnologiesTargetOptions(XElement effect, XElement? target, WrapPanel row)
    {
        var noAgeUp = new CheckBox
        {
            Content = "No age tech",
            IsChecked = target != null && GetCaseInsensitiveAttribute(target, "ignoreageups") != null,
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        noAgeUp.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var currentTarget = EnsureTechnologyDataTarget(effect, "techAll");
            if (noAgeUp.IsChecked == true) SetCaseInsensitiveAttribute(currentTarget, "ignoreageups", "");
            else RemoveCaseInsensitiveAttribute(currentTarget, "ignoreageups");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(noAgeUp);

        var exclusionAttribute = target == null ? null : GetCaseInsensitiveAttribute(target, "excludetypes");
        var exclusionKey = (effect, "target.excludetypes");
        if (exclusionAttribute == null && !_openOnHitOptionalSelectors.Contains(exclusionKey))
        {
            if (!CanEditModifiedDocument) return;
            var addExclusion = CreateOptionalPropertyButton("Exclude tech");
            addExclusion.Click += (_, _) =>
            {
                _openOnHitOptionalSelectors.Add(exclusionKey);
                _ = BuildEditorAsync();
            };
            row.Children.Add(addExclusion);
            return;
        }

        var exclusions = (exclusionAttribute?.Value ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exclusionGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        exclusionGroup.Children.Add(CreateInlineLabel("Exclude"));
        if (CanEditModifiedDocument)
        {
            exclusionGroup.Children.Add(CreateStrictEffectSelector(
                _techTypeNames.Where(value => !exclusions.Contains(value, StringComparer.OrdinalIgnoreCase)),
                "",
                value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    var currentTarget = EnsureTechnologyDataTarget(effect, "techAll");
                    var currentValues = (GetCaseInsensitiveAttribute(currentTarget, "excludetypes")?.Value ?? "")
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    if (!currentValues.Contains(value, StringComparer.OrdinalIgnoreCase)) currentValues.Add(value);
                    SetCaseInsensitiveAttribute(currentTarget, "excludetypes", string.Join('|', currentValues));
                    _openOnHitOptionalSelectors.Remove(exclusionKey);
                    MarkDirty();
                    _ = BuildEditorAsync();
                },
                EditorFieldWidthRole.ChipSearch));
        }

        foreach (var exclusion in exclusions)
        {
            var captured = exclusion;
            exclusionGroup.Children.Add(EditorChipService.CreateBlueChip(
                captured,
                CanEditModifiedDocument ? () =>
                {
                    var currentTarget = EnsureTechnologyDataTarget(effect, "techAll");
                    var remaining = (GetCaseInsensitiveAttribute(currentTarget, "excludetypes")?.Value ?? "")
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(value => !value.Equals(captured, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    SetCaseInsensitiveAttribute(currentTarget, "excludetypes", string.Join('|', remaining));
                    MarkDirty();
                    _ = BuildEditorAsync();
                } : null,
                readOnly: !CanEditModifiedDocument));
        }
        if (CanEditModifiedDocument)
        {
            exclusionGroup.Children.Add(CreateRemoveButton(() =>
            {
                var currentTarget = EnsureTechnologyDataTarget(effect, "techAll");
                RemoveCaseInsensitiveAttribute(currentTarget, "excludetypes");
                _openOnHitOptionalSelectors.Remove(exclusionKey);
            }));
        }
        row.Children.Add(exclusionGroup);
    }

    private IEnumerable<string> GetTechnologyNames()
        => _original.Keys.Concat(_modified.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> GetTechnologyFlagNames()
        => _original.Values.Concat(_modified.Values)
            .SelectMany(technology => technology.Elements()
                .Where(element => element.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase)))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

    private void SetTechnologyDataTargetMode(XElement effect, string type)
    {
        var target = EnsureDataTarget(effect);
        target.RemoveAttributes();
        target.RemoveNodes();
        target.SetAttributeValue("type", type);
        MarkDirty();
        UpdatePreview();
    }

    private void SetTechnologyDataTargetValue(XElement effect, string type, string value)
    {
        var target = EnsureTechnologyDataTarget(effect, type);
        target.Value = value;
        MarkDirty();
        UpdatePreview();
    }

    private XElement EnsureTechnologyDataTarget(XElement effect, string type)
    {
        var target = EnsureDataTarget(effect);
        SetCaseInsensitiveAttribute(target, "type", type);
        return target;
    }

    private static string TechnologyTargetTypeToDisplay(string type)
        => type.ToLowerInvariant() switch
        {
            "techall" => "All Techs",
            "techwithflag" => "Tech with Flag",
            "techtype" => "Tech Type",
            _ => "Tech"
        };

    private static string TechnologyTargetDisplayToType(string display)
        => display switch
        {
            "All Techs" => "techAll",
            "Tech with Flag" => "techWithFlag",
            "Tech Type" => "TechType",
            _ => "Tech"
        };

    private void AddResourceIfTechActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        MigrateDataAttribute(effect, "active", "tech");
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("If", CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            GetCaseInsensitiveAttribute(effect, "tech")?.Value.Trim() ??
            GetCaseInsensitiveAttribute(effect, "active")?.Value.Trim() ?? "",
            value =>
            {
                RemoveCaseInsensitiveAttribute(effect, "active");
                SetOptionalDataAttribute(effect, "tech", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@tech")));
        content.Children.Add(row);
    }

    private void AddResourceByUnitCountDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "unittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));

        var includeDead = new CheckBox
        {
            Content = "Include dead",
            IsChecked = GetCaseInsensitiveAttribute(effect, "includedead")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        includeDead.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            if (includeDead.IsChecked == true) SetCaseInsensitiveAttribute(effect, "includedead", "true");
            else RemoveCaseInsensitiveAttribute(effect, "includedead");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(includeDead);
        content.Children.Add(row);
    }

    private void AddResourceByKbQueryDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsureFixedDataAttribute(effect, "querystate", "Any");
        EnsurePlayerTarget(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("For each", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "queryunittype")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "queryunittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@queryunittype")));
        row.Children.Add(CreateDataAttributeCombo(
            effect, "querystate", ["Building", "Alive", "Dead", "Queued", "Any"], "Any", EditorFieldWidthRole.LongEnum));
        AddOptionalEffectAttribute(
            row, effect, "Resource cap", "resourcecap", "1", EditorFieldWidthRole.Numeric,
            ProtoUnitNumericKind.PositiveInteger, requirePositive: true, buttonLeftSpacing: 8);
        content.Children.Add(row);
    }

    private void AddResourceByKbStatDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "amount", "0");
        EnsurePlayerTarget(effect);

        var currentStat = GetCaseInsensitiveAttribute(effect, "kbstat")?.Value.Trim() ?? "";
        var currentUsesResource = KbStatsUsingResourceParameter.Contains(currentStat);
        var statSelector = CreateStrictEffectSelector(
            KbStatNames,
            currentStat,
            value =>
            {
                var newUsesResource = KbStatsUsingResourceParameter.Contains(value);
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "kbstat");
                else SetCaseInsensitiveAttribute(effect, "kbstat", value);
                if (!newUsesResource) RemoveCaseInsensitiveAttribute(effect, "kbparamresource");
                if (currentUsesResource != newUsesResource) _ = BuildEditorAsync();
            },
            EditorFieldWidthRole.LongEnum,
            preserveSuggestionOrder: true, validationSource: effect, validationField: "@kbstat");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizePlayerTargetEffect));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Stat", statSelector));
        if (currentUsesResource)
        {
            row.Children.Add(CreateLabeledEffectSegment(
                "Resource",
                CreateResourceCombo(effect, "kbparamresource")));
        }
        AddOptionalEffectAttribute(
            row, effect, "Resource cap", "resourcecap", "1", EditorFieldWidthRole.Numeric,
            ProtoUnitNumericKind.PositiveInteger, requirePositive: true, buttonLeftSpacing: 8);
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        row.Children.Add(CreateRestrictedDataRelativityCombo(effect, ["Absolute", "Assign"]));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedIntegerEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        content.Children.Add(row);
    }

    private void EnsurePlayerTarget(XElement effect)
    {
        if (!_initializingEffectDefaults || !TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect)) return;
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
        if (!_initializingEffectDefaults) return;
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
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizeMaxResourceEffect(effect))
        {
            MarkDirty();
            UpdatePreview();
        }

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateForcedPlayerTargetCombo(effect, TechnologyDataEffectRules.NormalizeMaxResourceEffect));
        row.Children.Add(CreateLabeledEffectSegment(
            "Capped at initial resource +",
            CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        content.Children.Add(row);
    }

    private void AddPopulationCapDataEffectEditor(XElement effect, StackPanel content)
    {
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizePopulationCapEffect(effect))
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
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizeSetCivilizationEffect(effect))
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@civ")));
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
            if (_loadingUi || !CanEditModifiedDocument || !normalize(effect)) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));

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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype")));

        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect)));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "empowertype", selected.ToLowerInvariant());
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void AddProtoActionAddDataEffectEditor(XElement effect, StackPanel content)
    {
        if (_initializingEffectDefaults && TechnologyDataEffectRules.NormalizeProtoActionAddEffect(effect))
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@protoaction")));
        row.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));

        var addToTactics = new CheckBox
        {
            Content = "Add to tactics",
            IsChecked = !string.Equals(GetCaseInsensitiveAttribute(effect, "addToTactics")?.Value, "0", StringComparison.OrdinalIgnoreCase),
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(8, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        addToTactics.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect)));
        row.Children.Add(CreateLabeledEffectSegment("Can auto attack", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unitType")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unitType");
                else SetCaseInsensitiveAttribute(effect, "unitType", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unitType")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private Control CreateTacticNameEditor(XElement effect, EditorFieldWidthRole role = EditorFieldWidthRole.Reference)
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
                role, validationSource: effect, validationField: "@tactic")
            : CreateFreeTextEffectAttributeBox(effect, "tactic", role);
    }

    private void AddBoostRadiusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row);
        AddOnHitOptionalReferenceSelector(
            row,
            effect,
            "Target",
            "Target",
            "targetType",
            _prereqUnitNames,
            EditorFieldWidthRole.Reference,
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@" + attributeName)));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect)));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        row.Children.Add(CreateLabeledEffectSegment("Target", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Player affected", CreateEmpowerPlayerTypeCombo(effect)));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddGatherResourceOverrideDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect, "Gather");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("For", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSetVeterancyRankActiveDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);
        EnsureFixedDataAttribute(effect, "rank", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Rank", CreateUnsignedIntegerEffectBox(effect, "rank", EditorFieldWidthRole.Numeric, "0")));
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
            CreateUnsignedIntegerEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddVeterancyBonusDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureFixedDataAttribute(effect, "rank", "0");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Rank", CreateUnsignedIntegerEffectBox(effect, "rank", EditorFieldWidthRole.Numeric, "0")));

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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype")));

        if (currentModifyType is "DamageSpecific" or "ArmorSpecific")
        {
            EnsureFixedDataAttribute(effect, "damagetype", "Hack");
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateRequiredDataTypeCombo(effect, includeDivine: currentModifyType == "DamageSpecific")));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        rankType.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || rankType.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "rankType", selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Type", rankType));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddSpeedModifierDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect)));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddTacticEnableDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultEnableDisableDataState(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateTacticNameEditor(effect)));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "targetunittype")?.Value.Trim() ?? "Unit",
            value => SetCaseInsensitiveAttribute(effect, "targetunittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@targetunittype")));
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "1")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddAddAttackTypeDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureFixedDataAttribute(effect, "relativity", "Assign");
        EnsureFixedDataAttribute(effect, "amount", "1");

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Tactic", CreateFreeTextEffectAttributeBox(effect, "tactic", EditorFieldWidthRole.Reference)));

        var current = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "1";
        var toggle = new ComboBox
        {
            ItemsSource = new[] { "Enable attack", "Disable attack" },
            SelectedItem = current == "0" ? "Disable attack" : "Enable attack",
            IsEnabled = CanEditModifiedDocument,
            Width = 135,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ConfigureTechnologyField(toggle, EditorFieldWidthRole.LongEnum);
        toggle.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || toggle.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Disable attack" ? "0" : "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(toggle);
        row.Children.Add(CreateLabeledEffectSegment("Unit type", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unittype", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@proto")));

        var currentRelativity = GetCaseInsensitiveAttribute(effect, "relativity")?.Value ?? "Absolute";
        var relativity = new ComboBox
        {
            ItemsSource = new[] { "Add", "Set to" },
            SelectedItem = currentRelativity.Equals("Assign", StringComparison.OrdinalIgnoreCase) ? "Set to" : "Add",
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(8, 4, 8, 4)
        };
        relativity.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || relativity.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", selected == "Set to" ? "Assign" : "Absolute");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(relativity);
        row.Children.Add(CreateLabeledEffectSegment("Amount", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        foreach (var axis in new[] { "x", "y", "z" })
            row.Children.Add(CreateLabeledEffectSegment(axis.ToUpperInvariant(), CreateSignedFloatEffectBox(effect, axis, EditorFieldWidthRole.Numeric, "0")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddChargedModifyAdjustDataEffectEditor(XElement effect, StackPanel content)
    {
        EnsureDefaultDataRelativity(effect);
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype");
        row.Children.Add(CreateLabeledEffectSegment("Modify type", modifyTypeSelector));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 110,
            Margin = new Thickness(8, 4, 0, 4)
        };
        kindCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || kindCombo.SelectedItem is not string selected ||
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@" + attributeName));

        if (includePosition)
        {
            row.Children.Add(CreateLabeledEffectSegment("Row", CreateUnsignedIntegerEffectBox(effect, "row", EditorFieldWidthRole.Numeric, "0")));
            row.Children.Add(CreateLabeledEffectSegment("Column", CreateUnsignedIntegerEffectBox(effect, "column", EditorFieldWidthRole.Numeric, "0")));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Damage type", CreateDataTypeCombo(effect, "damagetype", includeDivine: true)));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));

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
                IsEnabled = CanEditModifiedDocument,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
                if (checkBox.IsChecked == true) selected.Add(flag);
                else selected.Remove(flag);
                var value = string.Join("|", ProtoConstants.KnownDamageAreaTargetFlags.Where(selected.Contains));
                if (value.Length == 0) RemoveCaseInsensitiveAttribute(effect, "flags");
                else SetCaseInsensitiveAttribute(effect, "flags", value);
                MarkDirty(); UpdatePreview();
            };
            targets.Children.Add(checkBox);
        }
        row.Children.Add(CreateLabeledEffectSegment("Targets", targets));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 120,
            Margin = new Thickness(0, 4, 0, 4)
        };
        shadingCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || shadingCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "shadingtype", ProtoConstants.GetShadingTypeXmlValue(selected));
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Shading", shadingCombo));

        var threshold = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, "threshold")?.Value ?? "0"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(threshold, ProtoUnitNumericKind.ClampZeroToOne);
        ObserveTechnologyTextEdits(threshold, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
        });
        row.Children.Add(CreateLabeledEffectSegment("Threshold", threshold));

        row.Children.Add(CreateLabeledEffectSegment("Rate", CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Interval (ms)", CreateUnsignedIntegerEffectBox(effect, "time", EditorFieldWidthRole.Numeric, "0")));
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@flag")));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        row.Children.Add(CreateEnableDisableAmountCombo(effect));
        row.Children.Add(CreateLabeledEffectSegment("Flag", CreateStrictEffectSelector(
            ProtoActionMetadataCatalog.GetSuggestedFlagTags(),
            GetCaseInsensitiveAttribute(effect, "flag")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "flag");
                else SetCaseInsensitiveAttribute(effect, "flag", value);
                MarkDirty(); UpdatePreview();
            },
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@flag")));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(leftMargin, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        updatePercent.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
        if (_initializingEffectDefaults && playerMode && target != null)
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
            IsEnabled = CanEditModifiedDocument,
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
                EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            }, EditorFieldWidthRole.Reference, validationSource: RepresentedDataTarget(effect, "ProtoUnit", "Unit"), validationField: "target"));
        }

        targetKind.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || targetKind.SelectedItem is not string selected) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);

        var currentFor = GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "";
        var resourceMode = ProtoConstants.KnownResourceTypes.Contains(currentFor, StringComparer.OrdinalIgnoreCase);
        var forKind = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Resource" },
            SelectedItem = resourceMode ? "Resource" : "Unit",
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        row.Children.Add(CreateLabeledEffectSegment("For", forKind));

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
            }, EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype"));
        }

        forKind.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || forKind.SelectedItem is not string selected) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        AddDataRelativityAndAmountEditors(effect, row, allowOverride);
        row.Children.Add(CreateLabeledEffectSegment("For", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
        row.Children.Add(CreateLabeledEffectSegment("Resource", CreateResourceCombo(effect, "resource")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            SelectedItem = GetCaseInsensitiveAttribute(effect, attributeName) == null ? null : ProtoConstants.KnownResourceTypes.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        EditorValidationBindings.Bind(combo, effect, "@" + attributeName);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@proto")));
        row.Children.Add(CreateLabeledEffectSegment("Type", CreateStrictEffectSelector(
            ProtoConstants.KnownReplacementTypes,
            GetCaseInsensitiveAttribute(effect, "replacementtype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "replacementtype");
                else SetCaseInsensitiveAttribute(effect, "replacementtype", value);
                MarkDirty(); UpdatePreview();
            },
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@replacementtype")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@proto")));
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@spawntype")));
        AddOptionalEffectAttribute(row, effect, "Chance", "chance", "0", EditorFieldWidthRole.Numeric, ProtoUnitNumericKind.SignedFloat);
        AddOptionalEffectAttribute(row, effect, "Lifespan", "lifespan", "1", EditorFieldWidthRole.Numeric, ProtoUnitNumericKind.PositiveFloat, requirePositive: true);
        AddOptionalPlacementCheck(row, effect);
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOptionalPlacementCheck(WrapPanel row, XElement effect)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, "placementcheck");
        if (attribute == null)
        {
            if (!CanEditModifiedDocument) return;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 120,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            if (selected == "Default") RemoveCaseInsensitiveAttribute(effect, "placementcheck");
            else SetCaseInsensitiveAttribute(effect, "placementcheck", "TerrainOnly");
            MarkDirty(); UpdatePreview();
            if (selected == "Default") _ = BuildEditorAsync();
        };
        row.Children.Add(CreateLabeledEffectSegment("Placement check", combo));
        if (CanEditModifiedDocument)
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype");
        row.Children.Add(CreateLabeledEffectSegment("Modify type", modifyTypeSelector));

        if (currentModifyType is "DamageSpecific" or "ArmorSpecific")
        {
            if (_initializingEffectDefaults && GetCaseInsensitiveAttribute(effect, "damagetype") == null)
                EnsureFixedDataAttribute(effect, "damagetype", "Hack");
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateRequiredDataTypeCombo(effect, currentModifyType == "DamageSpecific")));
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
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));
        row.Children.Add(CreateLabeledEffectSegment("Projectile", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@rechargetype")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@protoaction")));
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
            _unitTypeNames,
            GetCaseInsensitiveAttribute(effect, "unittype")?.Value.Trim() ?? "",
            value =>
            {
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "unittype");
                else SetCaseInsensitiveAttribute(effect, "unittype", value);
                MarkDirty(); UpdatePreview();
            },
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unittype")));
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
            SelectedItem = GetCaseInsensitiveAttribute(effect, "damagetype") == null ? null : options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        EditorValidationBindings.Bind(combo, effect, "@damagetype");
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        EditorValidationBindings.Bind(combo, effect, "@" + attributeName);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            if (selected.Equals("All", StringComparison.OrdinalIgnoreCase)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, selected);
            MarkDirty(); UpdatePreview();
        };
        return combo;
    }

    private TextBox CreateUnsignedIntegerEffectBox(XElement effect, string attributeName, EditorFieldWidthRole role, string defaultValue)
    {
        var box = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? defaultValue), role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        });
        return box;
    }

    private TextBox CreateSignedIntegerEffectBox(XElement effect, string attributeName, EditorFieldWidthRole role, string defaultValue)
    {
        var box = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? defaultValue), role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedInteger);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty();
            UpdatePreview();
        });
        return box;
    }

    private void EnsureExactDataAttribute(XElement effect, string attributeName, string value)
    {
        if (!_initializingEffectDefaults || string.Equals(GetCaseInsensitiveAttribute(effect, attributeName)?.Value, value, StringComparison.OrdinalIgnoreCase)) return;
        SetCaseInsensitiveAttribute(effect, attributeName, value);
        MarkDirty(); UpdatePreview();
    }

    private void EnsureFixedDataAttribute(XElement effect, string attributeName, string defaultValue)
    {
        if (!_initializingEffectDefaults || GetCaseInsensitiveAttribute(effect, attributeName) != null) return;
        SetCaseInsensitiveAttribute(effect, attributeName, defaultValue);
        MarkDirty(); UpdatePreview();
    }

    private TextBox CreateSignedFloatEffectBox(XElement effect, string attributeName, EditorFieldWidthRole role, string defaultValue)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute?.Value ?? defaultValue), role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        });
        return box;
    }

    private TextBox CreateFreeTextEffectAttributeBox(XElement effect, string attributeName, EditorFieldWidthRole role)
    {
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? "",
            IsEnabled = CanEditModifiedDocument,
            Width = EditorFormLayout.WidthFor(role),
            MaxWidth = EditorFormLayout.WidthFor(role),
            Margin = new Thickness(0, 4, 0, 4)
        });
        ConfigureTechnologyField(box, role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            if (string.IsNullOrWhiteSpace(box.Text)) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? "");
            MarkDirty(); UpdatePreview();
        });
        return box;
    }

    private TextBox CreatePositiveFloatEffectBox(XElement effect, string attributeName, EditorFieldWidthRole role, string defaultValue)
    {
        var box = CreateNumericTextBox(
            FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? defaultValue),
            role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.PositiveFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0) return;
            SetCaseInsensitiveAttribute(effect, attributeName, value);
            MarkDirty();
            UpdatePreview();
        });
        return box;
    }

    private TextBox CreateInternalNameEffectAttributeBox(XElement effect, string attributeName, EditorFieldWidthRole role)
    {
        var stored = GetCaseInsensitiveAttribute(effect, attributeName)?.Value.Trim() ?? "";
        var current = new string(stored.Where(InternalNamePolicy.IsAllowedCharacter).ToArray());
        if (_initializingEffectDefaults && !current.Equals(stored, StringComparison.Ordinal))
        {
            if (current.Length == 0) RemoveCaseInsensitiveAttribute(effect, attributeName);
            else SetCaseInsensitiveAttribute(effect, attributeName, current);
            MarkDirty();
            UpdatePreview();
        }
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = current,
            IsEnabled = CanEditModifiedDocument,
            Width = EditorFormLayout.WidthFor(role),
            MaxWidth = EditorFormLayout.WidthFor(role),
            Margin = new Thickness(0, 4, 0, 4)
        });
        ConfigureTechnologyField(box, role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        var updating = false;
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || updating) return;
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
        });
        return box;
    }

    private void AddEnableDisableUnitDataEffectEditor(
        XElement effect,
        StackPanel content,
        bool includeAction,
        bool allowAllActions = true)
    {
        EnsureDefaultEnableDisableDataState(effect);
        if (includeAction && allowAllActions) EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        if (includeAction)
        {
            var actionLabel = CreateInlineLabel("Action");
            actionLabel.Margin = new Thickness(8, 4, 8, 4);
            row.Children.Add(actionLabel);
            row.Children.Add(CreateDataActionSelector(effect, allowAllActions));
        }

        var currentAmount = GetCaseInsensitiveAttribute(effect, "amount")?.Value.Trim() ?? "1";
        var stateCombo = new ComboBox
        {
            ItemsSource = new[] { "Enable", "Disable" },
            SelectedItem = currentAmount == "0" ? "Disable" : "Enable",
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        stateCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || stateCombo.SelectedItem is not string selected) return;
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
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unitType")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOnHitEffectDataEffectEditor(XElement effect, StackPanel content)
    {
        if (_initializingEffectDefaults && TechnologyOnHitEffectRules.Normalize(effect))
        {
            MarkDirty();
            UpdatePreview();
        }
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));

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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@effecttype")));

        AddOptionalEffectAttribute(
            row,
            effect,
            "Duration",
            "duration",
            "1",
            EditorFieldWidthRole.Numeric,
            ProtoUnitNumericKind.PositiveFloat,
            requirePositive: true,
            buttonLeftSpacing: 8,
            offerWhenMissing: TechnologyOnHitEffectRules.OffersDuration(effectType));
        AddOnHitOptionalReferenceSelector(row, effect, "Target", "Target", "targettype", _prereqUnitNames, EditorFieldWidthRole.Reference, showWhenMissing: false);

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
                EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype")));
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
            EditorFieldWidthRole.Reference,
            showWhenMissing: TechnologyOnHitEffectRules.AutomaticProtoEffectTypes.Contains(effectType),
            offerWhenMissing: TechnologyOnHitEffectRules.OffersProto(effectType));

        AddOnHitFreezeTypeEditor(row, effect, effectType);
        if (TechnologyOnHitEffectRules.UsesProgressiveFreezeDuration(effectType))
            AddOnHitProgressiveFreezeDurationEditor(row, effect);

        row.Children.Add(CreateLabeledEffectSegment(
            "Amount",
            CreateSignedFloatEffectBox(effect, "amount", EditorFieldWidthRole.Numeric, "0")));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void AddOnHitEffectAttributeDataEffectEditor(XElement effect, StackPanel content, string subtype)
    {
        if (_initializingEffectDefaults && TechnologyOnHitEffectRules.NormalizeAttributeSubtype(effect, subtype))
        {
            MarkDirty();
            UpdatePreview();
        }
        EnsureDefaultDataAction(effect);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        AddDataTargetEditor(effect, row);
        row.Children.Add(CreateLabeledEffectSegment("Action", CreateDataActionSelector(effect)));

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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@effecttype")));

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
                EditorFieldWidthRole.AttachmentBone, validationSource: effect, validationField: "@attachbone")));
        }

        if (subtype.Equals("OnHitEffectRate", StringComparison.OrdinalIgnoreCase) &&
            (effectType.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase) ||
             GetCaseInsensitiveAttribute(effect, "dmgtype") != null))
        {
            row.Children.Add(CreateLabeledEffectSegment(
                "Damage type",
                CreateExplicitOnHitDamageTypeCombo(effect, includeAll: true)));
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
            EditorFieldWidthRole.Reference,
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(8, 4, 0, 4)
        };
        EditorValidationBindings.Bind(combo, effect, "@amount");
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        EditorValidationBindings.Bind(combo, effect, "@dmgtype");
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
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
            EditorFieldWidthRole.LongEnum, validationSource: effect, validationField: "@modifytype")));

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
            IsEnabled = CanEditModifiedDocument,
            Width = 110,
            Margin = new Thickness(0, 4, 0, 4)
        };
        damageTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || damageTypeCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "dmgtype", selected);
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Damage type", damageTypeCombo));
    }

    private void AddOnHitOptionalReferenceSelector(
        WrapPanel row,
        XElement effect,
        string buttonLabel,
        string fieldLabel,
        string attributeName,
        IEnumerable<string> options,
        EditorFieldWidthRole role,
        bool showWhenMissing,
        bool offerWhenMissing = true,
        double buttonLeftSpacing = 0)
    {
        var key = (effect, attributeName.ToLowerInvariant());
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null && !showWhenMissing && !_openOnHitOptionalSelectors.Contains(key))
        {
            if (!CanEditModifiedDocument || !offerWhenMissing) return;
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
        }, role, validationSource: effect, validationField: "@" + attributeName);
        var segment = CreateLabeledEffectSegment(fieldLabel, selector);
        if (CanEditModifiedDocument && !showWhenMissing && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
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
            if (!CanEditModifiedDocument || !offerWhenMissing) return;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 100,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, selected);
            _openOnHitOptionalSelectors.Remove(key);
            MarkDirty();
            UpdatePreview();
        };
        var segment = CreateLabeledEffectSegment("Damage type", combo);
        if (CanEditModifiedDocument && !showWhenMissing && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
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
            if (!CanEditModifiedDocument) return;
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
            IsEnabled = CanEditModifiedDocument,
            Width = 130,
            Margin = new Thickness(0, 4, 0, 4)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, attributeName, ProtoConstants.GetOnHitEffectFreezeTypeXmlValue(selected));
            _openOnHitOptionalSelectors.Remove(key);
            MarkDirty();
            UpdatePreview();
        };
        var segment = CreateLabeledEffectSegment("Type", combo);
        if (!required && CanEditModifiedDocument && (attribute != null || _openOnHitOptionalSelectors.Contains(key)))
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
            EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.PositiveFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0) return;
            SetCaseInsensitiveAttribute(effect, "progFreezeDuration", value);
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(CreateLabeledEffectSegment("Duration", box));
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
            IsEnabled = CanEditModifiedDocument,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        ConfigureTechnologyField(movementTypeCombo, EditorFieldWidthRole.LongEnum);
        movementTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || movementTypeCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "movementtype", selected);
            SetCaseInsensitiveAttribute(effect, "amount", "1");
            SetCaseInsensitiveAttribute(effect, "relativity", "Assign");
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(CreateLabeledEffectSegment("Set movement type to", movementTypeCombo));
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
            CreateEnableDisableAmountCombo(effect, relativity: "Absolute", leftMargin: 0)));
        AddDataIgnoreNatureEditor(effect, row);
        content.Children.Add(row);
    }

    private void EnsureDefaultEnableDisableDataState(XElement effect)
    {
        if (!_initializingEffectDefaults) return;
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
        AutoCompleteBox? selector = null;
        void BindUnitTarget(XElement? source)
        {
            var type = source == null ? null : GetCaseInsensitiveAttribute(source, "type")?.Value;
            if (source != null && effect.Elements().Count(element => element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)) == 1 &&
                (string.Equals(type, "ProtoUnit", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Unit", StringComparison.OrdinalIgnoreCase)))
                EditorValidationBindings.Bind(selector!, source);
        }
        selector = CreateStrictEffectSelector(_prereqUnitNames, target?.Value.Trim() ?? "", value =>
        {
            var currentTarget = EnsureDataTarget(effect);
            currentTarget.Value = value;
            BindUnitTarget(currentTarget);
            MarkDirty();
            UpdatePreview();
        }, EditorFieldWidthRole.Reference);
        BindUnitTarget(target);
        row.Children.Add(selector);
    }

    private void AddDataIgnoreNatureEditor(XElement effect, WrapPanel row)
    {
        var target = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var ignoreNature = new CheckBox
        {
            Content = "Not Nature",
            IsChecked = target?.Attributes().Any(a => a.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase)) == true,
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ignoreNature.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
            SelectedItem = GetCaseInsensitiveAttribute(effect, "relativity") == null ? null : RelativityToDisplay(currentRelativity),
            IsEnabled = CanEditModifiedDocument,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ConfigureTechnologyField(relativityCombo, EditorFieldWidthRole.ShortEnum);
        EditorValidationBindings.Bind(relativityCombo, effect, "@relativity");
        relativityCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || relativityCombo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        row.Children.Add(relativityCombo);

        var amount = CreateNumericTextBox(FormatNumericForDisplay(GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? ""), EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(amount, effect, "@amount");
        EditorNumericInputBehavior.AttachRule(amount, ProtoUnitNumericKind.SignedFloat);
        ObserveTechnologyTextEdits(amount, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "amount", amount.Text ?? "0");
            MarkDirty();
            UpdatePreview();
        });
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
            SelectedItem = GetCaseInsensitiveAttribute(effect, "relativity") == null ? null : RelativityToDisplay(currentRelativity),
            IsEnabled = CanEditModifiedDocument,
            Width = 132,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 8, 4)
        };
        ConfigureTechnologyField(combo, EditorFieldWidthRole.ShortEnum);
        EditorValidationBindings.Bind(combo, effect, "@relativity");
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "relativity", DisplayToRelativity(selected));
            MarkDirty();
            UpdatePreview();
        };
        return combo;
    }

    private void ResetDataEffectForSubtype(XElement effect, string subtype)
    {
        _effectsPendingDefaultInitialization.Remove(effect);
        QueueDataIconOverrideStringForRemoval(effect);
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
        {
            effect.SetAttributeValue("subtype", subtype);
            _effectsPendingDefaultInitialization.Add(effect);
        }
        foreach (var attribute in metadata)
            effect.Add(attribute);
    }

    private void EnsureDefaultDataRelativity(XElement effect)
    {
        if (!_initializingEffectDefaults || GetCaseInsensitiveAttribute(effect, "relativity") != null) return;
        SetCaseInsensitiveAttribute(effect, "relativity", "BasePercent");
        MarkDirty();
        UpdatePreview();
    }

    private void EnsureDefaultDataAction(XElement effect, string? defaultAction = null)
    {
        if (!_initializingEffectDefaults) return;
        if (GetCaseInsensitiveAttribute(effect, "action") != null || GetCaseInsensitiveAttribute(effect, "allactions") != null) return;
        if (string.IsNullOrWhiteSpace(defaultAction)) SetCaseInsensitiveAttribute(effect, "allactions", "1");
        else SetCaseInsensitiveAttribute(effect, "action", defaultAction);
        MarkDirty();
        UpdatePreview();
    }

    private AutoCompleteBox CreateDataActionSelector(XElement effect, bool allowAllActions = true)
    {
        var actionAttribute = GetCaseInsensitiveAttribute(effect, "action")?.Value.Trim() ?? "";
        var allActions = GetCaseInsensitiveAttribute(effect, "allactions")?.Value.Trim();
        var current = allowAllActions && string.IsNullOrWhiteSpace(actionAttribute) && string.Equals(allActions, "1", StringComparison.OrdinalIgnoreCase)
            ? "All"
            : actionAttribute;
        var suggestions = (allowAllActions ? new[] { "All" } : [])
            .Concat(_protoActionNames.Where(value => !value.Equals("All", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var selector = CreateStrictEffectSelector(suggestions, current, value =>
        {
            if (allowAllActions && value.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                RemoveCaseInsensitiveAttribute(effect, "action");
                SetCaseInsensitiveAttribute(effect, "allactions", "1");
            }
            else
            {
                RemoveCaseInsensitiveAttribute(effect, "allactions");
                if (string.IsNullOrWhiteSpace(value)) RemoveCaseInsensitiveAttribute(effect, "action");
                else SetCaseInsensitiveAttribute(effect, "action", value);
            }
            MarkDirty();
            UpdatePreview();
        }, EditorFieldWidthRole.CompactReference, preserveSuggestionOrder: true);
        selector.ItemTemplate = new FuncDataTemplate<string>((item, _) => new TextBlock
        {
            Text = item ?? "",
            FontWeight = string.Equals(item, "All", StringComparison.OrdinalIgnoreCase) ? FontWeight.Bold : FontWeight.Normal
        });
        EditorValidationBindings.Bind(selector, effect, "@action");
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
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 8, 4)
        };
        row.Children.Add(targetKindCombo);

        var targetOptions = targetKind == "Tech" ? _original.Keys.Concat(_modified.Keys) : _protoUnitNames;
        var targetValue = targetKind == "Tech" ? techAttribute?.Value ?? "" : protoAttribute?.Value ?? "";
        var targetSelector = CreateStrictEffectSelector(targetOptions, targetValue, value =>
        {
            var kind = targetKindCombo.SelectedItem?.ToString() ?? targetKind;
            ObserveUiOperation(
                HandleSetNameTargetChangedAsync(effect, kind, value),
                "change the SetName target");
        }, EditorFieldWidthRole.Reference);
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
                IsEnabled = CanEditModifiedDocument,
                Width = 200,
                MaxWidth = 200,
                Margin = new Thickness(0, 4, 8, 4)
            });
            newNameBox.Width = newNameBox.MaxWidth = 200;
            ObserveTechnologyTextEdits(newNameBox, (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument || string.IsNullOrEmpty(newNameBox.Text)) return;
                createdId ??= BuildUniqueSetNameStringId(effect, "NEW_NAME");
                if (GetCaseInsensitiveAttribute(effect, "newName") == null)
                    SetCaseInsensitiveAttribute(effect, "newName", createdId);
                _pendingStringRemovals.Remove(createdId);
                _pendingStringUpdates[createdId] = newNameBox.Text ?? "";
                MarkDirty();
                UpdatePreview();
            });
            row.Children.Add(newNameBox);
        }

        AddSetNameOptionalStringButton(effect, row, "New rollover", "newRollover", "NEW_ROLLOVER");
        AddSetNameOptionalStringButton(effect, row, "New short rollover", "newShortRollover", "NEW_SHORT_ROLLOVER");

        var reqTech = GetCaseInsensitiveAttribute(effect, "reqTech");
        if (reqTech == null && CanEditModifiedDocument)
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
            row.Children.Add(CreateStrictEffectSelector(_original.Keys.Concat(_modified.Keys), reqTech.Value, value => SetCaseInsensitiveAttribute(effect, "reqTech", value), EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@reqTech"));
            if (CanEditModifiedDocument) row.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "reqTech")));
        }

        targetKindCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || targetKindCombo.SelectedItem is not string kind) return;
            ObserveUiOperation(
                ChangeSetNameTargetKindAsync(effect, kind),
                "change the SetName target type");
        };

        content.Children.Add(row);

        foreach (var (attributeName, label) in new[] { ("newRollover", "New rollover"), ("newShortRollover", "New short rollover") })
        {
            var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            if (attribute != null)
                await AddEffectStringAttributeRowAsync(content, effect, attribute, label, 380, removable: true,
                    multiline: attributeName == "newRollover");
        }
    }

    private async Task ChangeSetNameTargetKindAsync(XElement effect, string kind)
    {
        var effectBackup = new XElement(effect);
        var updatesBackup = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
        var removalsBackup = new HashSet<string>(_pendingStringRemovals, StringComparer.OrdinalIgnoreCase);
        try
        {
            RemoveCaseInsensitiveAttribute(effect, "proto");
            RemoveCaseInsensitiveAttribute(effect, "tech");
            SetCaseInsensitiveAttribute(effect, kind == "Tech" ? "tech" : "proto", "");
            await RegenerateSetNameEffectStringIdsAsync(effect, removeOldIds: true);
            MarkDirty();
            if (IsCurrentEffect(effect))
                await BuildEditorAsync();
        }
        catch
        {
            effect.ReplaceAttributes(effectBackup.Attributes());
            effect.ReplaceNodes(effectBackup.Nodes());
            _pendingStringUpdates.Clear();
            foreach (var (id, value) in updatesBackup)
                _pendingStringUpdates[id] = value;
            _pendingStringRemovals.Clear();
            foreach (var id in removalsBackup)
                _pendingStringRemovals.Add(id);
            throw;
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
        if (GetCaseInsensitiveAttribute(effect, attributeName) != null || !CanEditModifiedDocument) return;
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
        if (string.IsNullOrWhiteSpace(stringId) && _initializingEffectDefaults && _current != null)
        {
            stringId = BuildNextTextOutputStringId(_current, allIsIntrinsic ? "OUTPUTALL" : "OUTPUT");
            effect.Value = stringId;
            _pendingStringUpdates[stringId] = "";
        }
        var text = await ResolveTechnologyStringValueAsync(stringId);
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = CanEditModifiedDocument,
            Width = 380,
            MaxWidth = 380,
            MinHeight = 32,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 8, 4)
        });
        box.Width = box.MaxWidth = 380;
        var lastText = text;
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (value.Equals(lastText, StringComparison.Ordinal)) return;
            lastText = value;
            if (string.IsNullOrWhiteSpace(stringId))
            {
                if (_current == null || string.IsNullOrEmpty(value)) return;
                stringId = BuildNextTextOutputStringId(_current, allIsIntrinsic ? "OUTPUTALL" : "OUTPUT");
                effect.Value = stringId;
            }
            _pendingStringRemovals.Remove(stringId);
            SetPendingTechnologyStringValue(stringId, value);
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(box);
        if (!allIsIntrinsic)
        {
            var all = new CheckBox { Content = "To all", IsChecked = GetCaseInsensitiveAttribute(effect, "all")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            all.IsCheckedChanged += (_, _) =>
            {
                if (_loadingUi || !CanEditModifiedDocument) return;
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
        var combo = new ComboBox { ItemsSource = TechnologyAges, SelectedItem = TechnologyAges.FirstOrDefault(a => a.Equals(effect.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? effect.Value.Trim(), Width = 150, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 0, 4) };
        ConfigureTechnologyField(combo, EditorFieldWidthRole.LongEnum);
        combo.SelectionChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string value) return; effect.Value = value; MarkDirty(); UpdatePreview(); };
        row.Children.Add(combo);
        content.Children.Add(row);
    }

    private void AddTechStatusEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Tech"));
        row.Children.Add(CreateStrictEffectSelector(_original.Keys.Concat(_modified.Keys), effect.Value.Trim(), value => { effect.Value = value; MarkDirty(); UpdatePreview(); }, EditorFieldWidthRole.Reference, validationSource: effect));

        var statusLabel = CreateInlineLabel("Set status to");
        statusLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(statusLabel);
        var currentStatus = GetCaseInsensitiveAttribute(effect, "status")?.Value ?? "obtainable";
        var statusCombo = new ComboBox { ItemsSource = new[] { "Obtainable", "Active", "Unobtainable" }, SelectedItem = ToDisplayStatus(currentStatus), Width = 130, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 8, 4) };
        ConfigureTechnologyField(statusCombo, EditorFieldWidthRole.LongEnum);
        statusCombo.SelectionChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument || statusCombo.SelectedItem is not string value) return; SetCaseInsensitiveAttribute(effect, "status", value.ToLowerInvariant()); MarkDirty(); UpdatePreview(); };
        row.Children.Add(statusCombo);

        var majorGod = GetCaseInsensitiveAttribute(effect, "uiShowIfMajorGod");
        if (majorGod == null && CanEditModifiedDocument)
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
            row.Children.Add(CreateStrictEffectSelector(_majorGodNames, majorGod.Value, value => SetCaseInsensitiveAttribute(effect, "uiShowIfMajorGod", value), EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@uiShowIfMajorGod"));
            if (CanEditModifiedDocument) row.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "uiShowIfMajorGod")));
        }
        content.Children.Add(row);
    }

    private void AddSharedLosEffectEditor(XElement effect, StackPanel content)
    {
        var reveal = new CheckBox { Content = "Reveal all", IsChecked = GetCaseInsensitiveAttribute(effect, "all")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 0, 4) };
        reveal.IsCheckedChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument) return; if (reveal.IsChecked == true) SetCaseInsensitiveAttribute(effect, "all", "true"); else RemoveCaseInsensitiveAttribute(effect, "all"); MarkDirty(); UpdatePreview(); };
        content.Children.Add(reveal);
    }

    private void AddModifyProtoUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Unit"));
        var proto = GetCaseInsensitiveAttribute(effect, "proto")?.Value ?? "";
        row.Children.Add(EditorValidationBindings.Bind(CreateStrictEffectSelector(_protoUnitNames, proto, value => SetCaseInsensitiveAttribute(effect, "proto", value), EditorFieldWidthRole.Reference), effect, "@proto"));

        var newNameSpacer = new Border { Width = 12 };
        row.Children.Add(newNameSpacer);
        AddOptionalEffectAttribute(row, effect, "New name", "newName", "", EditorFieldWidthRole.Numeric, numericKind: null);
        var newHpSpacer = new Border { Width = 12 };
        row.Children.Add(newHpSpacer);
        AddOptionalEffectAttribute(row, effect, "New HP", "newHP", "1", EditorFieldWidthRole.Numeric, ProtoUnitNumericKind.UnsignedInteger, requirePositive: true);

        var reset = new CheckBox { Content = "Reset quick action", IsChecked = GetCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(8, 4, 0, 4) };
        reset.IsCheckedChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument) return; if (reset.IsChecked == true) SetCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex", "true"); else RemoveCaseInsensitiveAttribute(effect, "ResetQuickActionCommandIndex"); MarkDirty(); UpdatePreview(); };
        row.Children.Add(reset);
        content.Children.Add(row);
    }

    private void AddTransformUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel("Transform from"));
        row.Children.Add(EditorValidationBindings.Bind(CreateStrictEffectSelector(_protoUnitNames, GetCaseInsensitiveAttribute(effect, "fromProtoID")?.Value ?? "", value => SetCaseInsensitiveAttribute(effect, "fromProtoID", value), EditorFieldWidthRole.Reference), effect, "@fromProtoID"));
        var toLabel = CreateInlineLabel("to");
        toLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(toLabel);
        row.Children.Add(EditorValidationBindings.Bind(CreateStrictEffectSelector(_protoUnitNames, GetCaseInsensitiveAttribute(effect, "toProtoID")?.Value ?? "", value => SetCaseInsensitiveAttribute(effect, "toProtoID", value), EditorFieldWidthRole.Reference), effect, "@toProtoID"));
        var queued = new CheckBox { Content = "Include queued", IsChecked = GetCaseInsensitiveAttribute(effect, "includeQueued")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(8, 4, 0, 4) };
        queued.IsCheckedChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument) return; if (queued.IsChecked == true) SetCaseInsensitiveAttribute(effect, "includeQueued", "true"); else RemoveCaseInsensitiveAttribute(effect, "includeQueued"); MarkDirty(); UpdatePreview(); };
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
            EditorFieldWidthRole.Reference, validationSource: effect));

        var amountLabel = CreateInlineLabel("Amount");
        amountLabel.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(amountLabel);
        var amount = GetCaseInsensitiveAttribute(effect, "amount");
        var amountBox = CreateNumericTextBox(FormatNumericForDisplay(amount?.Value ?? "0"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(amountBox, ProtoUnitNumericKind.UnsignedFloat);
        ObserveTechnologyTextEdits(amountBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "amount", amountBox.Text ?? "0");
            MarkDirty(); UpdatePreview();
        });
        row.Children.Add(amountBox);

        var amount2Label = CreateInlineLabel("Amount 2");
        amount2Label.Margin = new Thickness(8, 4, 8, 4);
        row.Children.Add(amount2Label);
        var amount2 = GetCaseInsensitiveAttribute(effect, "amount2");
        var amount2Box = CreateNumericTextBox(FormatNumericForDisplay(amount2?.Value ?? "0"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(amount2Box, ProtoUnitNumericKind.UnsignedFloat);
        ObserveTechnologyTextEdits(amount2Box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "amount2", amount2Box.Text ?? "0");
            MarkDirty(); UpdatePreview();
        });
        row.Children.Add(amount2Box);
        content.Children.Add(row);
    }

    private void AddCreatePowerEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Power", EditorValidationBindings.Bind(CreateStrictEffectSelector(
            _godPowerNames,
            GetCaseInsensitiveAttribute(effect, "protoPower")?.Value.Trim() ?? "",
            value => SetOptionalDataAttribute(effect, "protoPower", value),
            EditorFieldWidthRole.Reference), effect, "@protoPower")));
        content.Children.Add(row);
    }

    private void AddRandomTechEffectEditor(XElement effect, StackPanel content)
    {
        var firstRow = new WrapPanel { Orientation = Orientation.Horizontal };
        firstRow.Children.Add(CreateInlineLabel("Number of techs to select"));
        var selectAttribute = GetCaseInsensitiveAttribute(effect, "select");
        var selectBox = CreateNumericTextBox(FormatNumericForDisplay(selectAttribute?.Value ?? "1"), EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(selectBox, effect, "@select");
        EditorNumericInputBehavior.AttachRule(selectBox, ProtoUnitNumericKind.UnsignedInteger);
        ObserveTechnologyTextEdits(selectBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            if (!int.TryParse(selectBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0) return;
            SetCaseInsensitiveAttribute(effect, "select", value.ToString(CultureInfo.InvariantCulture));
            MarkDirty(); UpdatePreview();
        });
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
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 0, 4)
        };
        ConfigureTechnologyField(statusCombo, EditorFieldWidthRole.LongEnum);
        statusCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || statusCombo.SelectedItem is not string value) return;
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
            if (_loadingUi || !CanEditModifiedDocument || string.IsNullOrWhiteSpace(value)) return;
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
        }, EditorFieldWidthRole.ChipSearch);

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
                    CanEditModifiedDocument ? () =>
                    {
                        captured.Remove();
                        MarkDirty(); UpdatePreview();
                        RenderChips(); RefreshPicker();
                    } : null,
                    readOnly: !CanEditModifiedDocument));
            }
        }

        RefreshPicker();
        techRow.Children.Add(picker!);
        EditorValidationBindings.Bind(picker!, effect, "Tech", repeated: true);
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
        if (attribute == null && _initializingEffectDefaults && _current != null)
        {
            var id = BuildUniqueTechnologyEffectStringId(_current, suffix, effect);
            SetCaseInsensitiveAttribute(effect, attributeName, id);
            attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            _pendingStringRemovals.Remove(id);
            _pendingStringUpdates[id] = "";
        }
        var idValue = attribute?.Value.Trim() ?? "";
        var text = await ResolveTechnologyStringValueAsync(idValue);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel(label));
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = CanEditModifiedDocument,
            Width = 380,
            MaxWidth = 380,
            MinHeight = 32,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = 380;
        var lastText = text;
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (value.Equals(lastText, StringComparison.Ordinal)) return;
            lastText = value;
            if (attribute == null)
            {
                if (_current == null || string.IsNullOrEmpty(value)) return;
                var generatedId = BuildUniqueTechnologyEffectStringId(_current, suffix, effect);
                SetCaseInsensitiveAttribute(effect, attributeName, generatedId);
                attribute = GetCaseInsensitiveAttribute(effect, attributeName);
            }
            if (attribute == null) return;
            var id = attribute.Value.Trim();
            _pendingStringRemovals.Remove(id);
            SetPendingTechnologyStringValue(id, value);
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private void AddForbidTechEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Tech", CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            effect.Value.Trim(),
            value => { effect.Value = value; MarkDirty(); UpdatePreview(); },
            EditorFieldWidthRole.Reference, validationSource: effect)));

        var amount = GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? "1";
        var mode = new ComboBox
        {
            ItemsSource = new[] { "Forbid", "Unforbid" },
            SelectedItem = amount.Trim().StartsWith("0", StringComparison.Ordinal) ? "Unforbid" : "Forbid",
            Width = 110,
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(4, 4, 0, 4)
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || mode.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Forbid" ? "1" : "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(mode);
        content.Children.Add(row);
    }

    private void AddSetOnTechResearchedTechEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Tech type", EditorValidationBindings.Bind(CreateStrictEffectSelector(
            _techTypeNames,
            GetCaseInsensitiveAttribute(effect, "techType")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "techType", value),
            EditorFieldWidthRole.Reference), effect, "@techType")));

        var amount = GetCaseInsensitiveAttribute(effect, "amount")?.Value ?? "1";
        var action = new ComboBox
        {
            ItemsSource = new[] { "Activates", "Disable" },
            SelectedItem = amount.Trim().StartsWith("0", StringComparison.Ordinal) ? "Disable" : "Activates",
            Width = 110,
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(4, 4, 8, 4)
        };
        action.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || action.SelectedItem is not string selected) return;
            SetCaseInsensitiveAttribute(effect, "amount", selected == "Activates" ? "1" : "0");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(action);
        row.Children.Add(CreateLabeledEffectSegment("Tech", EditorValidationBindings.Bind(CreateStrictEffectSelector(
            _original.Keys.Concat(_modified.Keys),
            effect.Value.Trim(),
            value => { effect.Value = value; MarkDirty(); UpdatePreview(); },
            EditorFieldWidthRole.Reference), effect)));
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
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 8, 4)
        };
        row.Children.Add(CreateLabeledEffectSegment("Target", target));
        target.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || target.SelectedItem is not string value) return;
            SetCaseInsensitiveAttribute(effect, "target", value);
            MarkDirty(); UpdatePreview();
        };

        var playerName = new CheckBox
        {
            Content = "Include player name",
            IsChecked = GetCaseInsensitiveAttribute(effect, "playerName")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        playerName.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "playerName", playerName.IsChecked == true ? "True" : "False");
            MarkDirty(); UpdatePreview();
        };
        row.Children.Add(playerName);

        var duration = GetCaseInsensitiveAttribute(effect, "duration");
        var durationBox = CreateNumericTextBox(FormatNumericForDisplay(duration?.Value ?? "2500"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(durationBox, ProtoUnitNumericKind.UnsignedInteger);
        ObserveTechnologyTextEdits(durationBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "duration", durationBox.Text ?? "2500");
            MarkDirty(); UpdatePreview();
        });
        row.Children.Add(CreateLabeledEffectSegment("Duration (ms)", durationBox));
        content.Children.Add(row);
    }


    private void AddCreateUnitEffectEditor(XElement effect, StackPanel content)
    {
        var pattern = effect.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pattern", StringComparison.OrdinalIgnoreCase));

        var firstRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var quantityBox = CreateNumericTextBox(FormatNumericForDisplay((pattern == null ? null : GetCaseInsensitiveAttribute(pattern, "quantity"))?.Value ?? "1"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(quantityBox, ProtoUnitNumericKind.PositiveInteger);
        ObserveTechnologyTextEdits(quantityBox, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            if (!int.TryParse(quantityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0) return;
            var editablePattern = GetOrCreateCreateUnitPattern(effect);
            SetCaseInsensitiveAttribute(editablePattern, "quantity", quantity.ToString(CultureInfo.InvariantCulture));
            MarkDirty(); UpdatePreview();
        });
        firstRow.Children.Add(CreateLabeledEffectSegment("Creates", quantityBox));
        firstRow.Children.Add(CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "unit")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unit", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unit"));
        firstRow.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "generator")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "generator", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@generator")));

        firstRow.Children.Add(CreateCreateUnitPresenceCheckBox(effect, "allgenerators", "All generators", "true"));
        firstRow.Children.Add(CreateCreateUnitPresenceCheckBox(effect, "mute", "Mute", ""));

        var queue = new CheckBox
        {
            Content = "Queue",
            IsChecked = !string.Equals(GetCaseInsensitiveAttribute(effect, "queue")?.Value, "false", StringComparison.OrdinalIgnoreCase),
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        queue.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
            IsEnabled = CanEditModifiedDocument,
            Margin = new Thickness(0, 4, 8, 4)
        };
        patternType.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || patternType.SelectedItem is not string value) return;
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
            if (CanEditModifiedDocument)
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
                var axisBox = CreateNumericTextBox(FormatNumericForDisplay(axisAttribute?.Value ?? "0"), EditorFieldWidthRole.Numeric);
                EditorNumericInputBehavior.AttachRule(axisBox, ProtoUnitNumericKind.SignedFloat);
                ObserveTechnologyTextEdits(axisBox, (_, _) =>
                {
                    if (_loadingUi || !CanEditModifiedDocument) return;
                    SetCaseInsensitiveAttribute(offset, axis, axisBox.Text ?? "0");
                    MarkDirty(); UpdatePreview();
                });
                offsetGroup.Children.Add(axisBox);
            }
            if (CanEditModifiedDocument)
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
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
            if (!CanEditModifiedDocument) return;
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
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute.Value), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(box, signed ? ProtoUnitNumericKind.SignedFloat : ProtoUnitNumericKind.UnsignedFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            attribute.Value = box.Text ?? defaultValue;
            MarkDirty(); UpdatePreview();
        });
        group.Children.Add(box);
        if (CanEditModifiedDocument) group.Children.Add(CreateRemoveButton(() => attribute.Remove()));
        row.Children.Add(group);
    }

    private void AddResourceInventoryExchangeEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("Unit", CreateStrictEffectSelector(
            _prereqUnitNames,
            GetCaseInsensitiveAttribute(effect, "unitType")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "unitType", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@unitType")));
        row.Children.Add(CreateLabeledEffectSegment("From", CreateResourceCombo(effect, "fromResource")));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateResourceCombo(effect, "toResource")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier", EditorFieldWidthRole.Numeric, "0")));

        var keepAlive = new CheckBox
        {
            Content = "Keep alive",
            IsChecked = GetCaseInsensitiveAttribute(effect, "keepUnit")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = CanEditModifiedDocument,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4, 0, 4)
        };
        keepAlive.IsCheckedChanged += (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
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
        row.Children.Add(CreateLabeledEffectSegment("Min Value", CreateUnsignedFloatEffectBox(effect, "minValue", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Max", CreateUnsignedFloatEffectBox(effect, "maxValue", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Source Resource", CreateResourceCombo(effect, "srcResource1")));

        var source2 = GetCaseInsensitiveAttribute(effect, "srcResource2");
        if (source2 == null && CanEditModifiedDocument)
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
            if (CanEditModifiedDocument) source2Segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, "srcResource2")));
            row.Children.Add(source2Segment);
        }

        row.Children.Add(CreateLabeledEffectSegment("Min Source value", CreateUnsignedFloatEffectBox(effect, "minSrcValue", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("Max", CreateUnsignedFloatEffectBox(effect, "maxSrcValue", EditorFieldWidthRole.Numeric, "0")));
        content.Children.Add(row);
    }

    private void AddResourceExchange2EffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("From", CreateResourceCombo(effect, "fromResource")));
        row.Children.Add(CreateLabeledEffectSegment("To", CreateResourceCombo(effect, "toResource")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier", EditorFieldWidthRole.Numeric, "0")));
        row.Children.Add(CreateLabeledEffectSegment("And to", CreateResourceCombo(effect, "toResource2")));
        row.Children.Add(CreateLabeledEffectSegment("Multiplier", CreateUnsignedFloatEffectBox(effect, "multiplier2", EditorFieldWidthRole.Numeric, "0")));
        content.Children.Add(row);
    }

    private void AddReplaceUnitEffectEditor(XElement effect, StackPanel content)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateLabeledEffectSegment("From", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "fromProtoID")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "fromProtoID", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@fromProtoID")));
        row.Children.Add(CreateLabeledEffectSegment("to", CreateStrictEffectSelector(
            _protoUnitNames,
            GetCaseInsensitiveAttribute(effect, "toProtoID")?.Value ?? "",
            value => SetCaseInsensitiveAttribute(effect, "toProtoID", value),
            EditorFieldWidthRole.Reference, validationSource: effect, validationField: "@toProtoID")));
        content.Children.Add(row);
    }

    private StackPanel CreateLabeledEffectSegment(string label, Control control)
    {
        control.Margin = new Thickness(0);
        var segment = EditorFormLayout.LabeledGroup(label, control);
        EditorFormLayout.UseInlineRowLayout(segment);
        return segment;
    }

    private TextBox CreateUnsignedFloatEffectBox(XElement effect, string attributeName, EditorFieldWidthRole role, string defaultValue)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        var box = CreateNumericTextBox(FormatNumericForDisplay(attribute?.Value ?? defaultValue), role);
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, attributeName, box.Text ?? defaultValue);
            MarkDirty(); UpdatePreview();
        });
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
        var box = CreateNumericTextBox(FormatNumericForDisplay(multiplier?.Value ?? "0"), EditorFieldWidthRole.Numeric);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            SetCaseInsensitiveAttribute(effect, "multiplier", box.Text ?? "0");
            MarkDirty();
            UpdatePreview();
        });
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private ComboBox CreateResourceCombo(XElement effect, string attributeName)
    {
        var current = GetCaseInsensitiveAttribute(effect, attributeName)?.Value ?? "";
        var values = ProtoConstants.KnownResourceTypes.Append(current).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase)), Width = 100, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 8, 4) };
        EditorValidationBindings.Bind(combo, effect, "@" + attributeName);
        combo.SelectionChanged += (_, _) => { if (_loadingUi || !CanEditModifiedDocument || combo.SelectedItem is not string value) return; SetCaseInsensitiveAttribute(effect, attributeName, value); MarkDirty(); UpdatePreview(); };
        return combo;
    }

    private void AddSimpleEffectValueEditor(XElement effect, StackPanel content, string label, EditorFieldWidthRole role)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateInlineLabel(label));
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox { Text = effect.Value, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 0, 4) }, role);
        EditorValidationBindings.Bind(box, effect);
        ObserveTechnologyTextEdits(box, (_, _) => { if (_loadingUi || !CanEditModifiedDocument) return; effect.Value = box.Text ?? ""; MarkDirty(); UpdatePreview(); });
        row.Children.Add(box);
        content.Children.Add(row);
    }

    private void AddOptionalEffectAttribute(
        WrapPanel row,
        XElement effect,
        string label,
        string attributeName,
        string defaultValue,
        EditorFieldWidthRole role,
        ProtoUnitNumericKind? numericKind,
        bool requirePositive = false,
        double buttonLeftSpacing = 0,
        bool offerWhenMissing = true)
    {
        var attribute = GetCaseInsensitiveAttribute(effect, attributeName);
        if (attribute == null)
        {
            if (!CanEditModifiedDocument || !offerWhenMissing) return;
            var button = CreateOptionalPropertyButton(label);
            button.Margin = new Thickness(buttonLeftSpacing, button.Margin.Top, button.Margin.Right, button.Margin.Bottom);
            button.Click += (_, _) => { SetCaseInsensitiveAttribute(effect, attributeName, defaultValue); MarkDirty(); _ = BuildEditorAsync(); };
            row.Children.Add(button);
            return;
        }
        TextBox box;
        if (numericKind.HasValue)
        {
            box = CreateNumericTextBox(FormatNumericForDisplay(attribute.Value), role);
            EditorNumericInputBehavior.AttachRule(box, numericKind.Value);
        }
        else
        {
            box = EditorTextFieldStyle.ConfigureTextBox(new TextBox { Text = attribute.Value, IsEnabled = CanEditModifiedDocument, Margin = new Thickness(0, 4, 0, 4) }, role);
        }
        EditorValidationBindings.Bind(box, effect, "@" + attributeName);
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var value = box.Text ?? "";
            if (requirePositive && (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number <= 0)) return;
            attribute.Value = value;
            MarkDirty(); UpdatePreview();
        });
        var segment = CreateLabeledEffectSegment(label, box);
        if (CanEditModifiedDocument) segment.Children.Add(CreateRemoveButton(() => RemoveCaseInsensitiveAttribute(effect, attributeName)));
        row.Children.Add(segment);
    }

    private async Task AddEffectStringAttributeRowAsync(StackPanel content, XElement effect, XAttribute attribute, string label, double width, bool removable, bool multiline = false)
    {
        var id = attribute.Value.Trim();
        var text = await ResolveTechnologyStringValueAsync(id);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var box = CreateEffectStringTextBox(attribute, text, width, multiline);
        if (multiline)
        {
            content.Children.Add(EditorRolloverField.CreateRow(label, box,
                removable && CanEditModifiedDocument ? CreateRemoveButton(() => { RemoveCaseInsensitiveAttribute(effect, attribute.Name.LocalName); QueueStringForRemoval(id); }) : null));
            return;
        }
        var segment = CreateLabeledEffectSegment(label, box);
        if (removable && CanEditModifiedDocument)
            segment.Children.Add(CreateRemoveButton(() => { RemoveCaseInsensitiveAttribute(effect, attribute.Name.LocalName); QueueStringForRemoval(id); }));
        row.Children.Add(segment);
        content.Children.Add(row);
    }

    private TextBox CreateEffectStringTextBox(XAttribute attribute, string text, double width, bool multiline = false)
    {
        var id = attribute.Value.Trim();
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = CanEditModifiedDocument,
            Width = width,
            MaxWidth = width,
            MinHeight = 32,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = new Thickness(0, 4, 0, 4)
        });
        box.Width = box.MaxWidth = width;
        if (multiline) EditorRolloverField.Configure(box);
        if (!multiline) ConfigureTechnologyField(box, attribute.Name.LocalName.Contains("rollover", StringComparison.OrdinalIgnoreCase)
            ? EditorFieldWidthRole.ShortRollover : EditorFieldWidthRole.SingleLineText);
        var lastText = text;
        ObserveTechnologyTextEdits(box, (_, _) =>
        {
            if (_loadingUi || !CanEditModifiedDocument || string.IsNullOrWhiteSpace(id)) return;
            var value = box.Text ?? "";
            if (value.Equals(lastText, StringComparison.Ordinal)) return;
            lastText = value;
            _pendingStringRemovals.Remove(id);
            SetPendingTechnologyStringValue(id, value);
            MarkDirty();
        });
        return box;
    }

    private void AddRawEffectXmlEditor(XElement effect, StackPanel content)
    {
        var box = new TextEditor
        {
            Text = effect.ToString(SaveOptions.DisableFormatting),
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 42,
            IsReadOnly = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        XmlSyntaxEditorService.Configure(box);
        // Context only: the parent XML session owns validation, Save and Discard.
        XmlSyntaxEditorService.SetReadOnly(box, isReadOnly: true);
        content.Children.Add(box);
    }

    private AutoCompleteBox CreateStrictEffectSelector(
        IEnumerable<string> suggestions,
        string value,
        Action<string> commit,
        EditorFieldWidthRole role = EditorFieldWidthRole.Reference,
        bool preserveSuggestionOrder = false,
        XElement? validationSource = null,
        string? validationField = null)
    {
        var selector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox { Text = value, FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, IsEnabled = CanEditModifiedDocument, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) });
        ConfigureTechnologyField(selector, role);
        var lastCommittedValue = value.Trim();
        EditorAutoCompleteService.ConfigureStrict(selector, suggestions.Append(value).Where(v => !string.IsNullOrWhiteSpace(v)), value, () => _loadingUi, preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true, deferSelectionCommit: true, selectAllOnFirstClick: true, keepStartVisibleAfterCommit: true, preserveSuggestionOrder: preserveSuggestionOrder, valueCommitted: v =>
        {
            if (_loadingUi || !CanEditModifiedDocument) return;
            var normalized = v.Trim();
            if (normalized.Equals(lastCommittedValue, StringComparison.OrdinalIgnoreCase)) return;
            lastCommittedValue = normalized;
            commit(v);
            MarkDirty();
            UpdatePreview();
        });
        if (validationSource != null) EditorValidationBindings.Bind(selector, validationSource, validationField);
        return selector;
    }

    private bool IsCurrentEffect(XElement effect)
        => _current != null && effect.Ancestors().Any(ancestor => ReferenceEquals(ancestor, _current));

    private void ObserveUiOperation(Task operation, string description)
    {
        _activeUiOperations.Add(operation);
        _ = CompleteObservedUiOperationAsync(operation, description);
    }

    private async Task CompleteObservedUiOperationAsync(Task operation, string description)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
            // Superseded editor work is expected.
        }
        catch (Exception exception)
        {
            _statusMessage.Text = $"Unable to {description}: {exception.Message}";
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Technology editor operation failed", _statusMessage.Text).ShowDialog(owner);
        }
        finally
        {
            _activeUiOperations.Remove(operation);
        }
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
        QueueDataIconOverrideStringForRemoval(effect);
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
        => TechnologyStringIdPolicy.NormalizeToken(value);

    private static string BuildTechnologyEffectStringId(string technologyName, string suffix)
        => TechnologyStringIdPolicy.EffectId(technologyName, suffix);

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
        if (!_controlsReady || _loadingUi || !CanEditModifiedDocument || _current == null || _currentOriginalName == null) return;
        var newName = (_techNameBox.Text ?? "").Trim();
        if (newName.Equals(_currentOriginalName, StringComparison.Ordinal)) return;
        if (InternalNamePolicy.IsValidOrUnchangedLegacy(newName, _currentOriginalName))
            _statusMessage.Text = "";
        MarkDirty();
    }

    private void TechNameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        // Capture the departure before a quickly dismissed modal restores focus.
        if (!IsKeyboardFocusWithin || TopLevel.GetTopLevel(this) is Window { OwnedWindows.Count: > 0 }) return;
        var technology = _current;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Leaving the form belongs to navigation/discard, not name validation.
            if (ReferenceEquals(technology, _current) && IsKeyboardFocusWithin)
                ObserveUiOperation(CommitCurrentTechnologyAsync(), "commit the technology name");
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    public async Task<bool> CommitCurrentTechnologyAsync()
    {
        if (IsCurrentAdditiveTechnology) return true;
        await _technologyNameCommitGate.WaitAsync();
        try
        {
            // Navigation retains raw drafts, including malformed XML. Validation
            // belongs to Save so it cannot trap the user before Discard.
            if (CurrentTechnologyXmlDraft?.Session.IsEditing == true) return true;
            return await CommitPendingTechnologyNameAsync();
        }
        finally
        {
            _technologyNameCommitGate.Release();
        }
    }

    private void QueueDataIconOverrideStringForRemoval(XElement effect)
    {
        var type = GetCaseInsensitiveAttribute(effect, "type")?.Value ?? "";
        var subtype = GetCaseInsensitiveAttribute(effect, "subtype")?.Value ?? "";
        if (!type.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
            (!subtype.Equals("BuffIconOverride", StringComparison.OrdinalIgnoreCase) &&
             !subtype.Equals("PowerIconOverride", StringComparison.OrdinalIgnoreCase)))
            return;

        var id = GetCaseInsensitiveAttribute(effect, "pathstrid")?.Value.Trim() ?? "";
        if (IsOwnedIconOverrideStringId(id)) QueueStringForRemoval(id);
    }

    internal static bool IsOwnedIconOverrideStringId(string id)
        => Regex.IsMatch(id, @"^STR_(?:BUFFICON|POWERICON)_.+_OVERRIDE\d*$", RegexOptions.IgnoreCase);

    private static bool IsIconOverrideStringIdForEffect(string id, string technologyName, string subtype)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var baseId = BuildIconOverrideStringBase(technologyName, subtype);
        if (!id.StartsWith(baseId, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = id[baseId.Length..];
        return suffix.Length == 0 || suffix.All(char.IsDigit);
    }

    private string BuildNextIconOverrideStringId(XElement technology, string subtype, XElement excludingEffect)
    {
        var technologyName = GetCaseInsensitiveAttribute(technology, "name")?.Value.Trim() ?? "";
        var used = CollectTechnologyStringIds(technology, excludingEffect);
        return BuildNextIconOverrideStringId(technologyName, subtype, used);
    }

    internal static string BuildNextIconOverrideStringId(string technologyName, string subtype, ISet<string> used)
    {
        var baseId = BuildIconOverrideStringBase(technologyName, subtype);
        if (!used.Contains(baseId)) return baseId;
        for (var index = 1; ; index++)
        {
            var candidate = baseId + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    internal static string BuildIconOverrideStringBase(string technologyName, string subtype)
    {
        var kind = subtype.Equals("PowerIconOverride", StringComparison.OrdinalIgnoreCase)
            ? "POWERICON"
            : "BUFFICON";
        return $"STR_{kind}_{NormalizeTechnologyStringToken(technologyName)}_OVERRIDE";
    }

    private async Task<bool> CommitPendingTechnologyNameAsync(XElement? rawTechnology = null, string? rawName = null)
    {
        if (rawTechnology == null && (_loadingUi || !CanEditModifiedDocument || _current == null || _currentOriginalName == null)) return true;
        var technology = rawTechnology ?? _current!;
        var oldName = rawTechnology == null ? _currentOriginalName! : (string)technology.Attribute("name")!;
        var newName = rawName ?? (_techNameBox.Text ?? "").Trim();
        if (newName.Equals(oldName, StringComparison.Ordinal)) return true;

        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, oldName))
        {
            _statusMessage.Text = $"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}.";
            RestoreTechnologyNameInput(technology, oldName);
            await ShowTechnologyNameErrorAsync(
                "Invalid technology name",
                $"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}. The previous name was kept.");
            return false;
        }
        if (_modified.Keys.Any(x => !x.Equals(oldName, StringComparison.OrdinalIgnoreCase) && x.Equals(newName, StringComparison.OrdinalIgnoreCase)) ||
            _original.ContainsKey(newName))
        {
            _statusMessage.Text = $"Technology '{newName}' already exists.";
            RestoreTechnologyNameInput(technology, oldName);
            await ShowTechnologyNameErrorAsync(
                "Duplicate technology name",
                $"Technology '{newName}' already exists in the base game or this mod. The previous name was kept.");
            return false;
        }

        var stringNamespaceOwner = TechnologyStringIdPolicy.FindNamespaceCollision(
            newName,
            _original.Keys.Concat(_modified.Keys),
            oldName);
        if (stringNamespaceOwner != null)
        {
            _statusMessage.Text = $"Technology '{newName}' would reuse strings owned by '{stringNamespaceOwner}'.";
            RestoreTechnologyNameInput(technology, oldName);
            await ShowTechnologyNameErrorAsync(
                "Technology string ID collision",
                $"Technology '{newName}' and '{stringNamespaceOwner}' generate the same string ID namespace. The previous name was kept to prevent shared strings from being overwritten.");
            return false;
        }

        var references = GetTechnologyReferences(oldName);
        if (_original.ContainsKey(oldName))
        {
            // Renaming a mod override reveals the still-valid base technology
            // under the old name. Base references continue to resolve to that
            // definition and must not be rewritten to the new custom name.
            references = references.Where(reference => !reference.IsBuiltIn).ToList();
        }
        if (_technologyReferenceIndexError != null)
        {
            await ShowTechnologyNameErrorAsync(
                "Technology reference check failed",
                $"'{oldName}' was not renamed because its references could not be checked safely. {_technologyReferenceIndexError}");
            RestoreTechnologyNameInput(technology, oldName);
            return false;
        }

        var unsafeReferences = references.Where(reference =>
            !reference.IsRecognized || !reference.CanMigrate).ToList();
        if (unsafeReferences.Count > 0)
        {
            await ShowTechnologyNameErrorAsync(
                "Technology rename blocked",
                $"'{oldName}' has {unsafeReferences.Count} reference(s) that the editor cannot migrate safely. The name was kept.\n\n" +
                FormatTechnologyReferenceList(unsafeReferences));
            RestoreTechnologyNameInput(technology, oldName);
            return false;
        }

        var technologyBackup = new XElement(technology);
        var stringUpdatesBackup = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
        var stringRemovalsBackup = new HashSet<string>(_pendingStringRemovals, StringComparer.OrdinalIgnoreCase);
        var automaticRemovalsBackup = new HashSet<string>(_automaticStringRemovals, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<OwnedStringRename> xmlStringRenames = [];
        try
        {
            var ownedBeforeRename = TechnologyStringIdPolicy.CollectOwnedStringIds(technologyBackup);
            var sourceValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ownedBeforeRename)
            {
                var text = await ResolveTechnologyStringValueAsync(id);
                if (_loadedStringValues.ContainsKey(id) || !text.Equals(id, StringComparison.Ordinal)) sourceValues[id] = text;
            }
            var changes = TechnologyStringIdPolicy.RenameOwnedStringIds(technologyBackup, technology, newName,
                sourceValues.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            xmlStringRenames = changes;
            var saved = FindSavedTechnologySnapshot(technologyBackup);
            var reusable = saved != null && string.Equals((string?)saved.Attribute("name"), newName, StringComparison.OrdinalIgnoreCase)
                ? TechnologyStringIdPolicy.CollectOwnedStringIds(saved) : new HashSet<string>();
            var existing = _loadedStringValues.Where(entry => !reusable.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            var stringPlan = OwnedStringLifecyclePolicy.Prepare(changes, [], ownedBeforeRename, existing,
                sourceValues, ownedBeforeRename, new HashSet<string>());
            foreach (var change in stringPlan.ReferenceRenames)
            {
                _pendingStringRemovals.Add(change.OldId);
                _pendingStringRemovals.Remove(change.NewId);
                if (!TechnologyStringIdPolicy.IsReferencedByTechnologies(change.OldId,
                        _modified.Values.Where(value => !ReferenceEquals(value, technology))))
                    _pendingStringUpdates.Remove(change.OldId);
            }
            foreach (var update in stringPlan.StringUpdates) _pendingStringUpdates[update.Key] = update.Value;
            _automaticStringRemovals.UnionWith(
                _pendingStringRemovals.Where(id => !stringRemovalsBackup.Contains(id)));
            if (!_modified.TryGetValue(oldName, out var tracked) || !ReferenceEquals(tracked, technology) ||
                (rawTechnology == null && (!ReferenceEquals(_current, technology) ||
                !string.Equals(_currentOriginalName, oldName, StringComparison.Ordinal))))
            {
                RestorePendingTechnologyRenameState(
                    technology,
                    technologyBackup,
                    stringUpdatesBackup,
                    stringRemovalsBackup,
                    automaticRemovalsBackup);
                return false;
            }
        }
        catch (Exception exception)
        {
            RestorePendingTechnologyRenameState(
                technology,
                technologyBackup,
                stringUpdatesBackup,
                stringRemovalsBackup,
                automaticRemovalsBackup);
            RestoreTechnologyNameInput(technology, oldName);
            _statusMessage.Text = exception.Message;
            await ShowTechnologyNameErrorAsync("Technology rename blocked", exception.Message);
            return false;
        }

        technology.SetAttributeValue("name", newName);
        RecordTechnologyXmlStringMigration(oldName, newName, xmlStringRenames);
        _modified.Remove(oldName);
        _modified[newName] = technology;
        MoveTechnologyXmlDraft(oldName, newName);
        if (ReferenceEquals(_current, technology)) _currentOriginalName = newName;
        var collapsedRenames = TechnologyReferenceIndex.AddOrCollapseRename(
            _pendingTechnologyRenames, oldName, newName);
        _pendingTechnologyRenames.Clear();
        _pendingTechnologyRenames.AddRange(collapsedRenames);
        _dirtyTechnologyNames.Remove(oldName);
        _dirtyTechnologyNames.Add(newName);
        MarkDirty();
        RefreshList(_currentOriginalName);
        UpdatePreview();
        _statusMessage.Text = "";
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static string FormatTechnologyReferenceList(IEnumerable<TechnologyReferenceUsage> references)
    {
        var items = references.Take(10).Select(reference =>
            $"• {reference.DisplayLocation}" +
            (!reference.IsRecognized ? " (unknown reference form)" :
                !reference.CanMigrate ? " (read-only)" : "")).ToList();
        var total = references.Count();
        if (total > items.Count)
            items.Add($"• …and {total - items.Count} more");
        return string.Join("\n", items);
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
        IReadOnlyCollection<string> stringRemovalsBackup,
        IReadOnlyCollection<string> automaticRemovalsBackup)
    {
        technology.ReplaceAttributes(technologyBackup.Attributes());
        technology.ReplaceNodes(technologyBackup.Nodes());
        _pendingStringUpdates.Clear();
        foreach (var entry in stringUpdatesBackup)
            _pendingStringUpdates[entry.Key] = entry.Value;
        _pendingStringRemovals.Clear();
        foreach (var id in stringRemovalsBackup)
            _pendingStringRemovals.Add(id);
        _automaticStringRemovals.Clear();
        foreach (var id in automaticRemovalsBackup)
            _automaticStringRemovals.Add(id);
    }

    internal static string BuildTechnologyStringId(string technologyName, string tag)
        => TechnologyStringIdPolicy.BuildId(technologyName, tag);

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

    private void EnsureDuplicatedTechnologyCoreProperties(XElement tech, string newName)
    {
        var displayName = tech.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase));
        if (displayName == null)
        {
            var displayId = BuildTechnologyStringId(newName, "displaynameid");
            InsertBeforeEffectsOrAppend(tech, new XElement("displaynameid", displayId));
            _pendingStringRemovals.Remove(displayId);
            _pendingStringUpdates[displayId] = newName;
        }

        var rollover = tech.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("rollovertextid", StringComparison.OrdinalIgnoreCase));
        if (rollover == null)
        {
            var rolloverId = BuildTechnologyStringId(newName, "rollovertextid");
            InsertBeforeEffectsOrAppend(tech, new XElement("rollovertextid", rolloverId));
            _pendingStringRemovals.Remove(rolloverId);
            _pendingStringUpdates[rolloverId] = "";
        }

        EnsureCoreProperty("icon", "");
        EnsureCoreProperty("status", "UNOBTAINABLE");
        EnsureCoreProperty("researchpoints", "0");
        return;

        void EnsureCoreProperty(string elementName, string defaultValue)
        {
            if (tech.Elements().Any(element =>
                    element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)))
                return;
            InsertBeforeEffectsOrAppend(tech, new XElement(elementName, defaultValue));
        }
    }

    private async Task RegenerateAllEffectStringsAsync(XElement tech, string technologyName, bool removeOldIds)
    {
        await RegenerateEffectTooltipStringsAsync(tech, technologyName, removeOldIds);
        var used = CollectTechnologyStringIds(tech);
        foreach (var effect in TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(tech))
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
            else if (type.Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                var subtype = GetCaseInsensitiveAttribute(effect, "subtype")?.Value ?? "";
                if (!subtype.Equals("BuffIconOverride", StringComparison.OrdinalIgnoreCase) &&
                    !subtype.Equals("PowerIconOverride", StringComparison.OrdinalIgnoreCase))
                    continue;

                var attribute = GetCaseInsensitiveAttribute(effect, "pathstrid");
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.Value)) continue;
                var oldId = attribute.Value.Trim();
                var text = await ResolveTechnologyStringValueAsync(oldId);
                used.Remove(oldId);
                var newId = BuildNextIconOverrideStringId(technologyName, subtype, used);
                used.Add(newId);
                if (removeOldIds && !oldId.Equals(newId, StringComparison.OrdinalIgnoreCase) &&
                    IsOwnedIconOverrideStringId(oldId))
                    QueueStringForRemoval(oldId);
                attribute.Value = newId;
                _pendingStringRemovals.Remove(newId);
                _pendingStringUpdates[newId] = text;
            }
        }
    }

    private async Task RegenerateEffectTooltipStringsAsync(XElement tech, string technologyName, bool removeOldIds = true)
    {
        var tooltipAttributes = TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(tech)
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

    private void AddTech_Click(object? sender, RoutedEventArgs e)
    {
        ObserveUiOperation(AddTechnologyAsync(), "add the technology");
    }

    public async Task AddTechnologyAsync(bool duplicateSelected = false)
    {
        if (!CanModifyTechnologyDocument)
        {
            _statusMessage.Text = _modDocumentSafetyError ?? "Technology modifications are disabled.";
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        XElement? source = null;
        string? selected = null;
        if (duplicateSelected)
        {
            selected = _techList.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                if (IsModifiedTab && !CanEditTechnology(selected)) return;
                (IsModifiedTab ? _modified : _original).TryGetValue(selected, out source);
            }
        }
        var duplicate = source != null && !string.IsNullOrWhiteSpace(selected);

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

        var stringNamespaceOwner = TechnologyStringIdPolicy.FindNamespaceCollision(
            name,
            _original.Keys.Concat(_modified.Keys));
        if (stringNamespaceOwner != null)
        {
            await new Prompt(
                PromptType.Error,
                "Technology string ID collision",
                $"Technology '{name}' and '{stringNamespaceOwner}' generate the same string ID namespace. Choose a different name so existing strings cannot be overwritten.").ShowDialog(owner);
            return;
        }

        XElement tech;
        if (duplicate && source != null)
        {
            tech = new XElement(source);
            tech.SetAttributeValue("name", name);
            var stringUpdatesBackup = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
            var stringRemovalsBackup = new HashSet<string>(_pendingStringRemovals, StringComparer.OrdinalIgnoreCase);
            try
            {
                await RegenerateDuplicatedTechnologyStringsAsync(tech, name);
                EnsureDuplicatedTechnologyCoreProperties(tech, name);
            }
            catch (Exception exception)
            {
                RestorePendingStringState(stringUpdatesBackup, stringRemovalsBackup);
                await new Prompt(
                    PromptType.Error,
                    "Unable to duplicate technology",
                    $"The technology and its strings were not duplicated. {exception.Message}").ShowDialog(owner);
                return;
            }
        }
        else
        {
            var displayId = BuildTechnologyStringId(name, "displaynameid");
            var rolloverId = BuildTechnologyStringId(name, "rollovertextid");
            tech = new XElement("tech",
                new XAttribute("name", name),
                new XElement("displaynameid", displayId),
                new XElement("rollovertextid", rolloverId),
                new XElement("status", "UNOBTAINABLE"),
                new XElement("researchpoints", "0"));
            _pendingStringRemovals.Remove(displayId);
            _pendingStringRemovals.Remove(rolloverId);
            _pendingStringUpdates[displayId] = name;
            _pendingStringUpdates[rolloverId] = "";
        }

        _modDocument.Root!.Add(tech);
        TechnologyXmlOrderPolicy.NormalizeForEditor(tech);
        _modified[name] = tech;
        _techTabs.SelectedIndex = 1;
        _current = tech;
        _currentOriginalName = name;
        _dirtyTechnologyNames.Add(name);
        MarkDirty();
        RefreshList(name);
        await BuildEditorAsync();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> AddAgeUpgradeTechnologyAsync(string name)
    {
        var normalized = name.Trim();
        if (!CanModifyTechnologyDocument || !InternalNamePolicy.IsValid(normalized) ||
            _modified.ContainsKey(normalized) || _original.ContainsKey(normalized))
            return false;

        var displayId = BuildTechnologyStringId(normalized, "displaynameid");
        var rolloverId = BuildTechnologyStringId(normalized, "rollovertextid");
        var technology = new XElement("tech",
            new XAttribute("name", normalized),
            new XElement("displaynameid", displayId),
            new XElement("rollovertextid", rolloverId),
            new XElement("status", "UNOBTAINABLE"),
            new XElement("researchpoints", "0"),
            new XElement("flag", "AgeUpgrade"));
        _pendingStringRemovals.Remove(displayId);
        _pendingStringRemovals.Remove(rolloverId);
        _pendingStringUpdates[displayId] = MinorGodCatalog.GetDisplayName(normalized);
        _pendingStringUpdates[rolloverId] = "";
        _modDocument.Root!.Add(technology);
        TechnologyXmlOrderPolicy.NormalizeForEditor(technology);
        _modified[normalized] = technology;
        _techTabs.SelectedIndex = 1;
        _current = technology;
        _currentOriginalName = normalized;
        _loadingUi = true;
        _techNameBox.Text = normalized;
        _loadingUi = false;
        _dirtyTechnologyNames.Add(normalized);
        MarkDirty();
        RefreshList(normalized);
        if (await SaveAsync())
            return true;
        DiscardTechnologyChanges(normalized, savedElement: null);
        return false;
    }

    public async Task<bool> RenameTechnologyAsync(string oldName, string newName)
    {
        var normalizedOldName = oldName.Trim();
        var normalizedNewName = newName.Trim();
        if (!CanEditTechnology(normalizedOldName) || !_modified.ContainsKey(normalizedOldName))
            return false;
        var savedElement = LoadSavedTechnologyElement(normalizedOldName);

        SetModifiedMode(true);
        SelectTechnology(normalizedOldName);
        await AwaitActiveEditorBuildCompletionAsync();
        if (_current == null || !_currentOriginalName!.Equals(normalizedOldName, StringComparison.OrdinalIgnoreCase))
            return false;

        _techNameBox.Text = normalizedNewName;
        if (!await CommitCurrentTechnologyAsync())
            return false;

        if (await SaveAsync())
            return true;
        DiscardTechnologyChanges(normalizedNewName, savedElement);
        return false;
    }

    private void RestorePendingStringState(
        IReadOnlyDictionary<string, string> stringUpdates,
        IReadOnlyCollection<string> stringRemovals)
    {
        _pendingStringUpdates.Clear();
        foreach (var update in stringUpdates)
            _pendingStringUpdates[update.Key] = update.Value;
        _pendingStringRemovals.Clear();
        foreach (var removal in stringRemovals)
            _pendingStringRemovals.Add(removal);
    }

    private void DeleteTech_Click(object? sender, RoutedEventArgs e)
    {
        ObserveUiOperation(DeleteTechnologyAsync(), "delete the technology");
    }

    public Task DeleteTechnologyAsync()
        => !CanEditModifiedDocument ? Task.CompletedTask : ImmediateDeletionRequestedAsync?.Invoke() ?? DeleteTechnologyCoreAsync();

    internal async Task DeleteAndSaveCurrentTechnologyAsync(Window owner)
    {
        await AwaitActiveEditorBuildCompletionAsync();
        if (!CanEditModifiedDocument) return;
        if (_current == null || _currentOriginalName == null || _modDocument == null || _modTechtreePath == null)
            return;
        var name = _currentOriginalName;
        var saved = new XElement(_current);
        var documentBackup = new XDocument(_modDocument);
        var candidate = new XDocument(_modDocument);
        candidate.Root?.Elements().FirstOrDefault(element =>
            string.Equals((string?)element.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))?.Remove();
        var linkedName = GetLinkedMinorGodTechnologyName(_current);
        var minorGod = _prepareMinorGodLifecycle?.Invoke(candidate, [],
            _hasLinkedMinorGod?.Invoke(linkedName) == true ? [linkedName] : []);
        var candidates = new Dictionary<string, XDocument> { [Path.GetFullPath(_modTechtreePath)] = candidate };
        if (minorGod != null) candidates[Path.GetFullPath(minorGod.Path)] = minorGod.Document;
        _immediateDeletionChecks = PrepareDeletionChecks?.Invoke([name], candidates) ?? [];
        _immediateDeletionOwner = owner;
        var removed = false;
        var committed = false;
        try
        {
            await DeleteTechnologyCoreAsync();
            removed = !_modified.ContainsKey(name);
            if (!removed) return;
            committed = await SaveAsync();
        }
        finally
        {
            _immediateDeletionChecks = null;
            _immediateDeletionOwner = null;
            if (removed && !committed)
            {
                _modDocument = documentBackup;
                _modified.Clear();
                MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
                DiscardTechnologyChanges(name, saved);
                SelectTechnology(name);
            }
        }
        if (committed)
        {
            ClearEditor();
            RefreshList();
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task DeleteTechnologyCoreAsync()
    {
        // The host uses its shared browser. Saving/rebinding that browser can
        // clear the hidden internal list while the current entry remains valid.
        var name = _immediateDeletionChecks != null ? _currentOriginalName : _techList.SelectedItem as string;
        if (!CanEditModifiedDocument || string.IsNullOrWhiteSpace(name) || !_modified.TryGetValue(name, out var tech))
        {
            if (_modDocumentSafetyError != null)
                _statusMessage.Text = _modDocumentSafetyError;
            return;
        }

        if ((_immediateDeletionOwner ?? TopLevel.GetTopLevel(this)) is not Window owner) return;

        var linkedMinorGodTechName = GetLinkedMinorGodTechnologyName(tech);
        var hasLinkedMinorGod = false;

        IReadOnlyList<TechnologyReferenceUsage> references;
        try
        {
            hasLinkedMinorGod = _hasLinkedMinorGod?.Invoke(linkedMinorGodTechName) == true;
            references = _original.ContainsKey(name)
                ? []
                : GetTechnologyReferences(name)
                    .Concat(_technologyDeletionReferenceProvider?.Invoke(name) ?? [])
                    .Where(reference =>
                        reference.TechnologyName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        !reference.Field.Equals("Minor God technology", StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(reference =>
                        $"{reference.Path}\0{reference.Owner}\0{reference.Field}",
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch (Exception exception)
        {
            await new Prompt(
                PromptType.Error,
                "Technology reference check failed",
                $"'{name}' was not deleted because its references could not be checked safely. {exception.Message}")
                .ShowDialog(owner);
            return;
        }
        if (_technologyReferenceIndexError != null)
        {
            await new Prompt(
                PromptType.Error,
                "Technology reference check failed",
                $"'{name}' was not deleted because its references could not be checked safely. {_technologyReferenceIndexError}").ShowDialog(owner);
            return;
        }
        if (references.Count > 0)
        {
            await DeletionBlockedDialog.ShowAsync(
                owner,
                "Technology",
                name,
                references.Select(reference => reference.DisplayLocation));
            return;
        }

        if (hasLinkedMinorGod && _immediateDeletionChecks == null)
        {
            if (_confirmLinkedMinorGodRemovalAsync == null ||
                !await _confirmLinkedMinorGodRemovalAsync(owner, linkedMinorGodTechName))
                return;
            _pendingMinorGodRemovals.Add(linkedMinorGodTechName);
        }
        else
        {
            var confirm = new Prompt(PromptType.Confirm, "Delete Technology",
                _immediateDeletionChecks == null ? $"Are you sure you want to delete '{name}'?" :
                $"Permanently delete '{name}' and save the deletion now?" +
                (hasLinkedMinorGod ? $"\n\nAssociated Minor God '{MinorGodCatalog.GetDisplayName(linkedMinorGodTechName)}' will also be deleted." : ""),
                confirmButtonText: "Delete technology", actionRole: PromptActionRole.Destructive)
                .FitToContent(580, 240);
            await confirm.ShowDialog(owner);
            if (!confirm.Confirmed) return;
            if (hasLinkedMinorGod) _pendingMinorGodRemovals.Add(linkedMinorGodTechName);
        }

        QueueTechnologyLifecycleStringRemovals(tech);
        foreach (var effect in tech.Descendants().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList())
            QueueEffectOwnedStringsForRemoval(effect);

        tech.Remove();
        _modified.Remove(name);
        ForgetTechnologyXmlDraft(name);
        _pendingTechnologyRenames.RemoveAll(rename =>
            rename.OldName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            rename.NewName.Equals(name, StringComparison.OrdinalIgnoreCase));
        _dirtyTechnologyNames.Add(name);
        MarkDirty();
        if (_immediateDeletionChecks != null)
        {
            _current = null;
            _currentOriginalName = null;
            return;
        }
        ClearEditor();
        RefreshList();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueTechnologyLifecycleStringRemovals(XElement technology)
    {
        foreach (var stringId in CollectReferencedTechnologyStringIds(technology))
        {
            _pendingStringUpdates.Remove(stringId);
            _pendingStringRemovals.Add(stringId);
            _automaticStringRemovals.Add(stringId);
        }
    }

    private void XmlPreviewToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isXmlPreviewCollapsed = !_isXmlPreviewCollapsed;
        EditorXmlPreviewPane.SetCollapsed(_mainGrid, _isXmlPreviewCollapsed);
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

    private void NormalizeChangedEffectAttributeOrder(XElement technology)
    {
        var savedTechnology = FindSavedTechnologySnapshot(technology);
        foreach (var effect in TechnologyXmlShapePolicy.FindChangedEffects(technology, savedTechnology))
            NormalizeEffectAttributeOrder(effect);
    }

    private XElement? FindSavedTechnologySnapshot(XElement technology)
    {
        var currentName = GetCaseInsensitiveAttribute(technology, "name")?.Value.Trim() ?? "";
        if (_savedModifiedTechnologyXml.TryGetValue(currentName, out var savedXml))
            return XElement.Parse(savedXml, LoadOptions.PreserveWhitespace);

        var originalName = _pendingTechnologyRenames
            .LastOrDefault(rename => rename.NewName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            ?.OldName;
        return !string.IsNullOrWhiteSpace(originalName) &&
               _savedModifiedTechnologyXml.TryGetValue(originalName, out savedXml)
            ? XElement.Parse(savedXml, LoadOptions.PreserveWhitespace)
            : null;
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
            "displaynameid" => EditorDisplayText.DisplayName,
            "rollovertextid" => EditorDisplayText.Rollover,
            "advancedrollovertextoverrideid" => EditorDisplayText.AdvancedRollover,
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

    public bool IsDirty
    {
        get
        {
            RecalculateDirtyState();
            return _dirty;
        }
    }

    public async Task<bool> SaveAsync()
    {
        using var feedback = _saveFeedback.BeginSave();
        try
        {
            var saved = await SaveCoreWithFeedbackAsync();
            if (saved) feedback.Complete(_dirty);
            else SaveFeedbackReported?.Invoke(EditorSaveOutcome.Blocked);
            return saved;
        }
        finally
        {
            feedback.Dispose();
            if (SaveFeedbackReported != null) _statusMessage.Text = "";
        }
    }

    private async Task<bool> SaveCoreWithFeedbackAsync()
    {
        await AwaitActiveEditorBuildCompletionAsync();
        if (_modDocumentSafetyError != null)
        {
            ReportSaveFeedback(EditorSaveOutcome.Blocked);
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Technology save blocked", _modDocumentSafetyError).ShowDialog(owner);
            return false;
        }
        if (!await CommitCurrentTechnologyAsync())
            return false;

        if (!await PrepareTechnologyXmlForSaveAsync())
            return false;

        if (string.IsNullOrWhiteSpace(_modTechtreePath))
        {
            ReportSaveFeedback(EditorSaveOutcome.Blocked);
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Technology save blocked", "No active mod is loaded.").ShowDialog(owner);
            return false;
        }

        if (_immediateDeletionChecks == null && !await ReloadExternallyChangedTechtreeAsync())
            return false;
        if (_immediateDeletionChecks == null && !await ReloadExternallyChangedStringsAsync())
            return false;

        try
        {
            var xmlSaves = CapturePreparedTechnologyXmlSaves();
            var stringUpdatesToSave = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
            var requestedStringRemovals = new HashSet<string>(_pendingStringRemovals, StringComparer.OrdinalIgnoreCase);
            var candidateDocument = new XDocument(_modDocument);
            var renamesToSave = _pendingTechnologyRenames.ToList();
            var candidateSource = new TechnologyReferenceSource(
                "Mod techtree",
                candidateDocument,
                TechnologyReferenceDocumentKind.Techtree,
                _modTechtreePath,
                CanMigrate: true);
            foreach (var rename in renamesToSave)
            {
                var localReferences = TechnologyReferenceIndex.FindReferencesTo([candidateSource], rename.OldName);
                var unknown = localReferences.Where(reference => !reference.IsRecognized).ToList();
                if (unknown.Count > 0)
                {
                    await ShowTechnologyNameErrorAsync(
                        "Technology rename blocked",
                        $"The rename from '{rename.OldName}' to '{rename.NewName}' was not saved because an unknown reference form was found.\n\n" +
                        FormatTechnologyReferenceList(unknown));
                    return false;
                }
                TechnologyReferenceIndex.RenameRecognizedReferences(candidateSource, rename.OldName, rename.NewName);
            }

            TechnologyReferenceMigrationPlan? externalMigration = null;
            if (renamesToSave.Count > 0 && _prepareTechnologyReferenceMigration != null)
            {
                externalMigration = _prepareTechnologyReferenceMigration(renamesToSave);
                if (externalMigration.Paths.Any(path => Path.GetFullPath(path).Equals(
                        Path.GetFullPath(_modTechtreePath), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("External reference migration must not overwrite the Technology document being saved. No files were written.");
                if (externalMigration.UnknownReferences.Count > 0)
                {
                    await ShowTechnologyNameErrorAsync(
                        "Technology rename blocked",
                        "No files were written because an unknown external technology-reference form was found.\n\n" +
                        FormatTechnologyReferenceList(externalMigration.UnknownReferences));
                    return false;
                }
            }

            var minorGodLifecycle = _prepareMinorGodLifecycle?.Invoke(
                candidateDocument,
                renamesToSave,
                _pendingMinorGodRemovals.ToList());

            var rawXmlNames = xmlSaves.Select(saved => saved.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var technology in candidateDocument.Root?.Elements().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? [])
            {
                var name = GetCaseInsensitiveAttribute(technology, "name")?.Value.Trim() ?? "";
                if (GetTechnologyEntryKind(name) != AdditiveEntryKind.Custom) continue;
                NormalizeChangedEffectAttributeOrder(technology);
                if (_dirtyTechnologyNames.Contains(name) && !rawXmlNames.Contains(name))
                    TechnologyXmlOrderPolicy.NormalizeForEditor(technology);
            }

            var savedTechnologyXml = (candidateDocument.Root?.Elements().Where(element =>
                    element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? [])
                .Where(element => !string.IsNullOrWhiteSpace(GetCaseInsensitiveAttribute(element, "name")?.Value))
                .ToDictionary(
                    element => GetCaseInsensitiveAttribute(element, "name")!.Value.Trim(),
                    SerializeTechnologyForComparison,
                    StringComparer.OrdinalIgnoreCase);

            var validation = ValidateTechnologySaveCandidate(candidateDocument, CreateSaveValidationCatalogs());
            if (!validation.IsValid)
            {
                await ShowSaveValidationErrorsAsync(validation.Errors, validation.Issues);
                return false;
            }

            if (!IsDirty && savedTechnologyXml.Count == _savedModifiedTechnologyXml.Count &&
                savedTechnologyXml.All(entry => _savedModifiedTechnologyXml.GetValueOrDefault(entry.Key) == entry.Value))
            {
                AcceptTechnologyXmlSaves(xmlSaves);
                UpdatePreview();
                return true;
            }

            var originalOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, xml) in _savedModifiedTechnologyXml.Where(entry =>
                         _dirtyTechnologyNames.Contains(entry.Key) || !savedTechnologyXml.ContainsKey(entry.Key) || renamesToSave.Any(rename =>
                             rename.OldName.Equals(entry.Key, StringComparison.OrdinalIgnoreCase))))
                originalOwned.UnionWith(TechnologyStringIdPolicy.CollectOwnedStringIds(XElement.Parse(xml)));
            var currentOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var technology in _modified.Values.Where(technology =>
                         _dirtyTechnologyNames.Contains((string?)technology.Attribute("name") ?? "")))
                currentOwned.UnionWith(TechnologyStringIdPolicy.CollectOwnedStringIds(technology));
            var verifiedOwned = originalOwned.Union(currentOwned).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cleanupCandidates = requestedStringRemovals.Union(originalOwned).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stringOverrides = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase)
            { [Path.GetFullPath(_modTechtreePath)] = candidateDocument };
            if (minorGodLifecycle != null) stringOverrides[Path.GetFullPath(minorGodLifecycle.Path)] = minorGodLifecycle.Document;
            foreach (var path in externalMigration?.Paths ?? [])
                if (externalMigration!.GetDocument(path) is { } migrated) stringOverrides[Path.GetFullPath(path)] = migrated;
            var inspectedIds = cleanupCandidates.Concat(stringUpdatesToSave.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var cleanup = PrepareStringCleanup?.Invoke(inspectedIds, stringOverrides) ?? new OwnedStringSaveInspection(
                ModDeletionSafety.InspectStringReferences(ResolveStringInspectionRoot(), inspectedIds, stringOverrides,
                    excludedPaths: string.IsNullOrWhiteSpace(_modStringsPath) ? [] : [_modStringsPath]),
                new HashSet<string>());
            var remainingReferences = cleanup.References.ReferencedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (PrepareStringCleanup == null && _externalStringReferenceProvider != null)
                remainingReferences.UnionWith(_externalStringReferenceProvider(inspectedIds));
            var existingEntries = ReadModStringValuesFromDisk();
            foreach (var id in stringUpdatesToSave.Keys.Where(currentOwned.Contains))
                if ((!originalOwned.Contains(id) && existingEntries.ContainsKey(id)) || cleanup.ReservedIds.Contains(id))
                    throw new InvalidOperationException($"String ID '{id}' already exists or is claimed by another edit. Choose another name. No files were written.");
            var cleanupPlan = OwnedStringLifecyclePolicy.Prepare([], cleanupCandidates, verifiedOwned,
                existingEntries, new Dictionary<string, string>(), remainingReferences, cleanup.ReservedIds);
            var stringRemovalsToSave = cleanupPlan.StringRemovals;
            foreach (var id in stringUpdatesToSave.Keys.Where(id => cleanupCandidates.Contains(id) &&
                         !remainingReferences.Contains(id)).ToList()) stringUpdatesToSave.Remove(id);
            var writesStrings = stringUpdatesToSave.Count > 0 || stringRemovalsToSave.Count > 0;
            if (writesStrings && (_saveStringsAsync == null || string.IsNullOrWhiteSpace(_modStringsPath)))
                throw new InvalidOperationException("Technology strings cannot be saved because the active mod string file is unavailable.");

            var paths = new List<string?> { _modTechtreePath };
            var expectedStates = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
            if (_loadedTechtreeFileState != null)
                expectedStates[Path.GetFullPath(_modTechtreePath)] = _loadedTechtreeFileState;
            if (writesStrings)
            {
                paths.Add(_modStringsPath);
                if (_loadedStringsFileState != null)
                    expectedStates[Path.GetFullPath(_modStringsPath!)] = _loadedStringsFileState;
            }
            if (externalMigration != null)
            {
                paths.AddRange(externalMigration.Paths);
                foreach (var state in externalMigration.ExpectedStates)
                    expectedStates[state.Key] = state.Value;
            }
            if (minorGodLifecycle != null)
            {
                paths.Add(minorGodLifecycle.Path);
                expectedStates[minorGodLifecycle.Path] = minorGodLifecycle.LoadedState;
            }
            cleanup.References.MergeExpectedStatesInto(expectedStates);

            var deletedNames = ModDeletionSafety.FindDeletedNames(
                _savedModifiedTechnologyXml.Keys.Concat(_dirtyTechnologyNames), savedTechnologyXml.Keys,
                renamesToSave.Select(rename => new KeyValuePair<string, string>(rename.OldName, rename.NewName)));
            var deletionOverrides = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(_modTechtreePath)] = candidateDocument
            };
            if (minorGodLifecycle != null)
                deletionOverrides[Path.GetFullPath(minorGodLifecycle.Path)] = minorGodLifecycle.Document;
            var deletionChecks = PrepareDeletionChecks?.Invoke(deletedNames, deletionOverrides) ?? [];
            await FileIntegrityTransaction.ExecuteAsync(paths, expectedStates, async () =>
            {
                cleanup.References.ValidateReadDependencies();
                externalMigration?.ValidateDiscovery();
                if (_immediateDeletionChecks != null)
                    foreach (var inspection in _immediateDeletionChecks) inspection.ValidateReadDependencies();
                foreach (var inspection in deletionChecks) inspection.ValidateReadDependencies();
                ProtoEditorWindow.SaveAbilityXmlDocument(new XDocument(candidateDocument), _modTechtreePath);
                if (writesStrings)
                    await _saveStringsAsync!(stringUpdatesToSave, stringRemovalsToSave);
                externalMigration?.Save((document, path) =>
                    ProtoEditorWindow.SaveAbilityXmlDocument(document, path));
                if (minorGodLifecycle != null)
                    ProtoEditorWindow.SaveMinorGodXmlDocument(
                        minorGodLifecycle.Document,
                        minorGodLifecycle.Path);
            });

            if (_technologySavedAsync != null)
                await _technologySavedAsync(new XDocument(candidateDocument));

            if (externalMigration != null && _adoptTechnologyReferenceMigration != null)
                _adoptTechnologyReferenceMigration(externalMigration);
            var selectedTechnologyName = _currentOriginalName;
            _modDocument = candidateDocument;
            _modified.Clear();
            MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
            _current = !string.IsNullOrWhiteSpace(selectedTechnologyName)
                ? ResolveTechnologyForReading(selectedTechnologyName)
                : null;
            _effectsPendingDefaultInitialization.Clear();
            _openOnHitOptionalSelectors.Clear();

            try
            {
                _loadedTechtreeFileState = FileIntegrityTransaction.CaptureState(_modTechtreePath);
                if (writesStrings)
                {
                    _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath!);
                    ReplaceLoadedStringValues(ReadModStringValuesFromDisk());
                }
            }
            catch (Exception exception)
            {
                _modDocumentSafetyError =
                    $"The saved technology files could not be fingerprinted safely. Further technology modifications are disabled. {exception.Message}";
            }
            foreach (var update in stringUpdatesToSave)
            {
                if (_pendingStringUpdates.TryGetValue(update.Key, out var currentValue) &&
                    currentValue.Equals(update.Value, StringComparison.Ordinal))
                    _pendingStringUpdates.Remove(update.Key);
            }
            foreach (var removal in requestedStringRemovals)
            {
                _pendingStringRemovals.Remove(removal);
                _automaticStringRemovals.Remove(removal);
            }
            foreach (var rename in renamesToSave)
                _pendingTechnologyRenames.Remove(rename);
            _pendingMinorGodRemovals.Clear();
            _savedModifiedTechnologyXml.Clear();
            foreach (var (name, xml) in savedTechnologyXml)
                _savedModifiedTechnologyXml[name] = xml;
            AcceptTechnologyXmlSaves(xmlSaves);
            _dirtyTechnologyNames.Clear();
            RefreshTechnologyReferenceUsage();
            RecalculateDirtyState();
            RefreshList(selectedTechnologyName);
            if (_current != null)
                await BuildEditorAsync();
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            if (_modDocumentSafetyError != null)
                _statusMessage.Text = _modDocumentSafetyError;
            else
                ReportSaveFeedback(_dirty ? EditorSaveOutcome.NewerEdits : EditorSaveOutcome.Saved);
            return true;
        }
        catch (Exception ex)
        {
            ReportSaveFeedback(EditorSaveOutcome.Failed);
            if ((_immediateDeletionOwner ?? TopLevel.GetTopLevel(this)) is Window owner)
                await new Prompt(PromptType.Error, "Technology save failed", ex.Message).ShowDialog(owner);
            return false;
        }
    }

    private async Task AwaitActiveEditorBuildCompletionAsync()
    {
        while (true)
        {
            var build = _activeEditorBuildTask;
            await build;
            if (ReferenceEquals(build, _activeEditorBuildTask))
                return;
        }
    }

    private TechnologySaveCatalogs CreateSaveValidationCatalogs()
        => new(
            TechnologyEffectCatalogInventory.CreatableDataSubtypes,
            TechnologyEffectCatalogInventory.StructuredEffectTypes,
            _original.Keys.Concat(_modified.Keys).ToList(),
            _prereqUnitNames,
            _protoUnitNames,
            _techTypeNames,
            _protoActionNames,
            _tacticNames,
            _godPowerNames,
            _cultureNames,
            _majorGodNames,
            _boneNames,
            _protoUnitCommandNames,
            _unitTypeNames);

    internal static TechnologyEffectCatalogRuntimeSnapshot GetEffectCatalogRuntimeSnapshot()
        => TechnologyEffectCatalogInventory.CreateSnapshot();

    internal Task<bool> ProbeStructuredEffectEditorForAuditAsync(string effectType, string? dataSubtype = null)
    {
        var effect = new XElement("effect", new XAttribute("type", effectType));
        if (!string.IsNullOrWhiteSpace(dataSubtype))
            effect.SetAttributeValue("subtype", dataSubtype);
        return AddStructuredEffectBodyAsync(effect, new StackPanel(), effectType);
    }

    private async Task<bool> ReloadExternallyChangedTechtreeAsync()
    {
        if (string.IsNullOrWhiteSpace(_modTechtreePath) || _loadedTechtreeFileState == null)
            return true;

        FileIntegrityTransaction.FileState currentState;
        try
        {
            currentState = FileIntegrityTransaction.CaptureState(_modTechtreePath);
        }
        catch (Exception exception)
        {
            _statusMessage.Text = "The external technology file could not be checked safely: " + exception.Message;
            return false;
        }

        if (currentState == _loadedTechtreeFileState)
            return true;

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            _statusMessage.Text = "techtree_mods.xml changed outside the editor. Reload it before saving.";
            return false;
        }

        var prompt = new Prompt(
            PromptType.Confirm,
            "Technologies changed outside the editor",
            "techtree_mods.xml changed after the Technology editor loaded. Reload those external changes and merge them with unsaved editor changes?\n\n" +
            "If the same technology changed both externally and in the editor, Reload uses the external technology. Unsaved edits to other technologies and strings are kept. Saving is paused after reload so you can review the result.",
            confirmButtonText: "Reload")
        {
            Width = 620,
            Height = 260,
            MinWidth = 560,
            MinHeight = 230
        };
        await prompt.ShowDialog(owner);
        if (!prompt.Confirmed)
        {
            ReportSaveFeedback(EditorSaveOutcome.Cancelled);
            return false;
        }

        try
        {
            var externalDocument = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace);
            var validation = ValidateTechnologySourceStructure(externalDocument);
            if (!validation.IsValid)
            {
                var message = validation.ToDisplayMessage(_modTechtreePath);
                _statusMessage.Text = message;
                var invalidPrompt = new Prompt(
                    PromptType.Error,
                    "Unable to reload technologies",
                    message)
                {
                    Width = 700,
                    Height = 320,
                    MinWidth = 580,
                    MinHeight = 250
                };
                await invalidPrompt.ShowDialog(owner);
                return false;
            }

            var externalBaseline = TechnologyExternalDocumentMergePolicy.CaptureTechnologyXml(externalDocument);
            var merge = TechnologyExternalDocumentMergePolicy.Merge(
                _savedModifiedTechnologyXml,
                _modDocument,
                externalDocument);
            var renamedConflict = _pendingTechnologyRenames.FirstOrDefault(rename =>
                merge.ExternallyChangedTechnologies.Contains(rename.OldName, StringComparer.OrdinalIgnoreCase) ||
                merge.ExternallyChangedTechnologies.Contains(rename.NewName, StringComparer.OrdinalIgnoreCase));
            if (renamedConflict != null)
            {
                await new Prompt(
                    PromptType.Error,
                    "Unable to merge technology rename",
                    $"'{renamedConflict.OldName}' was renamed in the editor while that technology also changed externally. The files were not overwritten because this rename cannot be merged without risking its references. Reopen the editor to use the external version.")
                    .ShowDialog(owner);
                return false;
            }

            var selectedName = _currentOriginalName;
            _modDocument = merge.Document;
            ReloadTechnologyXmlDrafts(merge.ReplacedEditorChanges.Concat(merge.ExternallyChangedTechnologies));
            _modified.Clear();
            MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
            _savedModifiedTechnologyXml.Clear();
            foreach (var (name, xml) in externalBaseline)
                _savedModifiedTechnologyXml[name] = xml;
            foreach (var conflict in merge.ReplacedEditorChanges)
                _pendingMinorGodRemovals.Remove(conflict);
            _loadedTechtreeFileState = FileIntegrityTransaction.CaptureState(_modTechtreePath);
            _effectsPendingDefaultInitialization.Clear();
            _openOnHitOptionalSelectors.Clear();
            _dirtyTechnologyNames.Clear();
            _current = !string.IsNullOrWhiteSpace(selectedName)
                ? ResolveTechnologyForReading(selectedName)
                : null;
            _currentOriginalName = _current == null ? null : selectedName;
            RefreshTechnologyReferenceUsage();
            RecalculateDirtyState();
            RefreshList(selectedName);
            if (_current != null)
                await BuildEditorAsync();
            else
                ClearEditor();
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            ReportSaveFeedback(EditorSaveOutcome.ReviewRequired);
            return false;
        }
        catch (Exception exception)
        {
            _statusMessage.Text = "The external technology file could not be reloaded safely: " + exception.Message;
            await new Prompt(
                PromptType.Error,
                "Unable to reload technologies",
                $"No files were written. {exception.Message}").ShowDialog(owner);
            return false;
        }
    }

    private async Task<bool> ReloadExternallyChangedStringsAsync()
    {
        if (string.IsNullOrWhiteSpace(_modStringsPath) || _loadedStringsFileState == null)
            return true;

        FileIntegrityTransaction.FileState currentState;
        try
        {
            currentState = FileIntegrityTransaction.CaptureState(_modStringsPath);
        }
        catch (Exception exception)
        {
            _statusMessage.Text = "The external string file could not be checked safely: " + exception.Message;
            return false;
        }

        if (currentState == _loadedStringsFileState)
            return true;

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            _statusMessage.Text = "stringmods.txt changed outside the editor. Reload it before saving.";
            return false;
        }

        var prompt = new Prompt(
            PromptType.Confirm,
            "Strings changed outside the editor",
            "stringmods.txt changed after the Technology editor loaded. Reload those external changes and merge them with the unsaved technology edits?\n\n" +
            "For strings changed both externally and in the editor, Reload uses the external value. Unsaved edits to other strings and the Technology XML are kept. Saving is paused after reload so you can review the result.",
            confirmButtonText: "Reload");
        prompt.Width = 560;
        prompt.Height = 220;
        await prompt.ShowDialog(owner);
        if (!prompt.Confirmed)
        {
            ReportSaveFeedback(EditorSaveOutcome.Cancelled);
            return false;
        }

        try
        {
            var externalValues = ReadModStringValuesFromDisk();
            var externallyChangedIds = TechnologyStringMergePolicy.GetExternallyChangedIds(
                _loadedStringValues,
                externalValues);
            var replacedLocalChanges = TechnologyStringMergePolicy.UseExternalVersionsForChangedIds(
                externallyChangedIds,
                _pendingStringUpdates,
                _pendingStringRemovals);

            _reloadStringsFromDisk?.Invoke();
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
            ReplaceLoadedStringValues(externalValues);
            RecalculateDirtyState();
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            await BuildEditorAsync();
            ReportSaveFeedback(EditorSaveOutcome.ReviewRequired);
            return false;
        }
        catch (Exception exception)
        {
            _statusMessage.Text = "The external string file could not be reloaded safely: " + exception.Message;
            await new Prompt(
                PromptType.Error,
                "Unable to reload strings",
                $"No technology files were written. {exception.Message}").ShowDialog(owner);
            return false;
        }
    }

    private Dictionary<string, string> ReadModStringValuesFromDisk()
    {
        if (string.IsNullOrWhiteSpace(_modStringsPath) || !File.Exists(_modStringsPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var source = PreservedTextEncoding.Decode(File.ReadAllBytes(_modStringsPath));
        return StringTableDocument.Parse(source.Text).ToDictionary();
    }

    private string ResolveStringInspectionRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(_modTechtreePath!))!);
        for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Name.Equals("game", StringComparison.OrdinalIgnoreCase) && ancestor.Parent != null)
                return ancestor.Parent.FullName;
        return directory.FullName;
    }

    private void ReplaceLoadedStringValues(IReadOnlyDictionary<string, string> values)
    {
        _loadedStringValues.Clear();
        foreach (var value in values)
            _loadedStringValues[value.Key] = value.Value;
    }

    private bool IsStringIdReferencedByAnyTechnology(string stringId)
        => TechnologyStringIdPolicy.IsReferencedByTechnologies(
            stringId,
            _original.Values.Concat(_modified.Values));

    private async Task ShowSaveValidationErrorsAsync(IReadOnlyList<string> errors, IReadOnlyList<LocatedValidationIssue>? issues = null)
    {
        EditorValidationNavigation.Clear(this);
        var entry = _current;
        var generation = _editorBuildGeneration;
        var target = CurrentTechnologyXmlDraft?.Session.IsEditing == true ? null :
            issues?.Select(issue => EditorValidationBindings.Find(this, issue)).FirstOrDefault(control => control != null);
        var failure = target == null ? null : EditorValidationNavigation.Mark(this, target);
        var visibleErrors = errors.Take(12).ToList();
        var message = "Technology changes were not saved:\n\n" + string.Join("\n", visibleErrors.Select(error => "• " + error));
        if (errors.Count > visibleErrors.Count)
            message += $"\n• …and {errors.Count - visibleErrors.Count} more error(s).";
        ReportSaveFeedback(EditorSaveOutcome.Blocked);
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new Prompt(PromptType.Error, "Technology validation failed", message).ShowDialog(owner);
        if (failure != null && target != null)
            await failure.RevealAsync(() => ReferenceEquals(entry, _current) && generation == _editorBuildGeneration &&
                    CurrentTechnologyXmlDraft?.Session.IsEditing != true,
                () =>
                {
                    var ancestors = target.GetLogicalAncestors().ToArray();
                    var index = ancestors.Contains(_effectsPanel) ? 2 : ancestors.Contains(_prereqsPanel) ? 1 : 0;
                    this.GetLogicalDescendants().OfType<TabControl>().First().SelectedIndex = index;
                });
    }

    private void MarkDirty()
    {
        if (_modDocumentSafetyError != null)
            return;
        if (IsModifiedTab && !string.IsNullOrWhiteSpace(_currentOriginalName))
        {
            if (TechnologyHasUnsavedChanges(_currentOriginalName))
                _dirtyTechnologyNames.Add(_currentOriginalName);
            else
                _dirtyTechnologyNames.Remove(_currentOriginalName);
        }
        RecalculateDirtyState();
        if (_dirty)
        {
            _saveFeedback.ClearSuccessOnEdit();
        }
        if (_statusMessage.Text == "Modified")
        {
            _statusMessage.Text = "";
        }
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportSaveFeedback(EditorSaveOutcome outcome)
    {
        _saveFeedback.Report(outcome);
        SaveFeedbackReported?.Invoke(outcome);
    }

    private void RecalculateDirtyState()
    {
        var technologyNames = _savedModifiedTechnologyXml.Keys
            .Concat(_modified.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _dirtyTechnologyNames.RemoveWhere(name => !TechnologyHasUnsavedChanges(name));
        foreach (var name in technologyNames.Where(TechnologyHasUnsavedChanges))
            _dirtyTechnologyNames.Add(name);
        _dirty = _dirtyTechnologyNames.Count > 0 ||
                 _pendingStringUpdates.Count > 0 ||
                 _pendingStringRemovals.Count > 0;
    }

    private bool TechnologyHasUnsavedChanges(string technologyName)
    {
        if (_technologyXmlDrafts.TryGetValue(technologyName, out var raw) && raw.Session.IsDirty) return true;
        if (!_loadingUi && IsModifiedTab && _current != null &&
            string.Equals(_currentOriginalName, technologyName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals((_techNameBox.Text ?? "").Trim(), (string?)_current.Attribute("name") ?? "", StringComparison.Ordinal))
            return true;
        var hasSavedTechnology = _savedModifiedTechnologyXml.TryGetValue(technologyName, out var savedXml);
        var hasCurrentTechnology = _modified.TryGetValue(technologyName, out var currentTechnology);
        if (hasSavedTechnology != hasCurrentTechnology)
            return true;
        if (!hasCurrentTechnology || currentTechnology == null)
            return false;
        if (!string.Equals(savedXml, SerializeTechnologyForComparison(currentTechnology), StringComparison.Ordinal))
            return true;

        var referencedStringIds = CollectReferencedTechnologyStringIds(currentTechnology);
        return _pendingStringUpdates.Keys.Any(referencedStringIds.Contains) ||
               _pendingStringRemovals.Any(referencedStringIds.Contains);
    }

    internal static string SerializeTechnologyForComparison(XElement technology)
    {
        var comparable = new XElement(technology);
        foreach (var whitespace in comparable.DescendantNodes().OfType<XText>()
                     .Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList())
            whitespace.Remove();
        return comparable.ToString(SaveOptions.DisableFormatting);
    }

    private void UpdatePreview()
    {
        if (_current == null)
        {
            _technologyXmlController.Bind(null, formEnabled: false);
            return;
        }

        if (IsCurrentAdditiveTechnology)
        {
            var authored = _modified.GetValueOrDefault(_currentOriginalName!);
            BindTechnologyXmlPreview(authored == null ? "" : FormatTechnologyXmlPreview(authored));
            return;
        }
        var previewTech = new XElement(_current);
        var protectedContent = EditorXmlOrderPolicy.HasProtectedContent(_current);
        var currentName = GetCaseInsensitiveAttribute(_current, "name")?.Value.Trim() ?? "";
        if (!protectedContent && IsModifiedTab && _dirtyTechnologyNames.Contains(currentName) &&
            CurrentTechnologyXmlDraft?.Session.IsEditing != true)
            TechnologyXmlOrderPolicy.NormalizeForEditor(previewTech);
        var savedTechnology = FindSavedTechnologySnapshot(_current);
        foreach (var effect in TechnologyXmlShapePolicy.FindChangedEffects(previewTech, savedTechnology))
            NormalizeEffectAttributeOrder(effect);
        BindTechnologyXmlPreview(XmlSyntaxEditorService.PreparePreviewText(protectedContent
            ? previewTech.ToString(SaveOptions.DisableFormatting)
            : XmlSyntaxEditorService.FormatElementForPreview(previewTech)));
    }
}
