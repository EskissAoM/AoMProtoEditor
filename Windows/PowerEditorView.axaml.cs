using System.Xml.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Services;

namespace AoMDivineDataEditor.Windows;

public partial class PowerEditorView : UserControl
{
    private static readonly string[] StringBackedTags =
        ["displaynameid", "rolloverid", "activepowerrolloverid", "exclusiveerrormsgid",
            "notificationformatid", "selfnotificationformatid"];
    private static readonly string[] OptionalStringTags =
        ["activepowerrolloverid", "exclusiveerrormsgid", "notificationformatid", "selfnotificationformatid"];
    private static readonly string[] PlacementModes = ["Full", "Overlap", "InRange", "Unit", "Skip"];
    private static readonly string[] RangeIndicatorModes = ["Circle", "Arrow", "Cone"];
    private static readonly string[] PowerPlayerRelations =
        ["All", "Player", "Ally", "Enemy", "EnemyNotMotherNature", "Neutral"];
    private static readonly string[] AlertPlayerRelations =
        ["All", "Player", "Ally", "Enemy", "Neutral", "None"];
    private static readonly string[] MinimapRecipients = ["All", "Ally", "Player", "Enemy", "None"];
    private static readonly string[] ActiveListVisibilities = ["Self", "AlliesOnly", "None"];
    private static readonly string[] SoundSetTypes = ["StartSound", "EndSound", "ExistSound", "BuildUp", "Manual"];
    private static readonly string[] SoundSetListeners =
        ["All", "Caster", "Enemy", "Ally", "AllExceptCaster", "IfOnScreenAll",
            "IfOnScreenCaster", "IfOnScreenEnemy", "IfOnScreenAlly", "IfOnScreenAllExceptCaster"];
    private static readonly string[] TempUnitCreateModes = ["Unit", "Selection"];
    private static readonly string[] TempUnitRadiusModes = ["Simple", "Min/Max"];
    private static readonly string[] TempUnitPatterns = ["Symmetric", "Scatter", "Simple"];
    private static readonly string[] TempUnitLifespanTypes = ["Relative", "Absolute"];
    private static readonly string[] StandardAges =
        ["ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"];
    private static readonly (string Label, string XmlName)[] SwapFlags =
    [
        ("UnitTarget", "unittarget"), ("Global", "global"), ("SwapEnemyOnly", "swapenemyonly"),
        ("SwapSelfOnly", "swapselfonly"), ("PerformWalkingWoodsSwap", "performwalkingwoodsswap"),
        ("ConvertUnitsToGodPowerPlayer", "convertunitstogodpowerplayer"), ("Mutate", "mutate"),
        ("ForceFullHP", "forcefullhp"), ("PlayBirthAfterMutate", "playbirthaftermutate"),
        ("FindBestPosition", "findbestposition")
    ];
    private static readonly (string Label, string XmlName)[] TempUnitFlags =
    [
        ("Permanent", "permanent"), ("Norandomrotate", "norandomrotate"),
        ("Norotate", "norotate"), ("Destroyunderneath", "destroyunderneath"),
        ("Foundation", "foundation"),
        ("Donotdestroyfriendliesunderneath", "donotdestroyfriendliesunderneath"),
        ("playCreatesound", "playcreatesound"), ("placementRef", "placementref")
    ];
    private static readonly (string Label, string XmlName)[] TempestAvoids =
    [
        ("Water", "largestrikeavoidswater"), ("Buildings", "largestrikeavoidsbuildings"),
        ("Impassable", "largestrikeavoidslandimpassable")
    ];
    private static readonly string[] RainEffectTypes = [.. PowerSaveValidator.KnownEffectTypes];
    private static readonly HashSet<string> RainSupplementalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "allactions", "unittype", "resource", "tactic", "damagetype", "armortype",
        "component", "reqTech", "effecttype", "targettype", "dmgtype", "protoaction", "addToTactics",
        "srcproto", "srcaction"
    };
    private static readonly string[] PowerTypes = [.. PowerSchemaRegistry.EditorPowerTypes];
    private static readonly string[] PlacementFlags =
    [
        "disallowWaterCast", "disallowWaterCastRadius", "disallowLandObstructedCast",
        "disallowLandObstructedCastRadius", "disallowLandCast", "allowGroundTarget",
        "allowFogOfWarPlacement", "allowGaia", "noLosExtraLoc",
        "allowSnap", "placeFoundation", "ignoreProtoPlacement", "forceOnMap", "placeInArea",
        "notOverlapSelfOrAlly", "enemy", "ally", "self", "allyOrEnemyNotSelf", "allowNature",
        "natureOnly", "useTargetUnitPositionForPlacement", "fullyBuiltOnly", "allowDeadTarget",
        "ignoreattacktypesforplacement",
        "ExpandCheckPlacementHull", "VectorPlacement"
    ];
    private readonly Dictionary<string, XElement> _baseEntries;
    private readonly string? _modPath;
    private readonly PowerEditorCatalogs _catalogs;
    private readonly PowerEditorHostServices _hostServices;
    private readonly IconPreviewControl _iconPreview;
    private readonly string? _modStringsPath;
    private readonly PowerEditingSession _session = new();
    private readonly HashSet<XElement> _invalidEffectTypes = [];
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private bool _allowSelectionDuringCommand;
    private readonly List<(TextBox Box, XElement Sound)> _soundNumericBoxes = [];
    private readonly List<(AutoCompleteBox Type, TextBox Radius, XElement Restriction)> _placementRestrictionFields = [];
    private readonly List<(TextBox Segment, TextBox Reduction, XElement Tier)> _damageTierFields = [];
    private readonly List<(AutoCompleteBox Type, TextBox Damage, XElement RootDamage)> _rootDamageFields = [];
    private readonly List<TempUnitCreateFields> _tempUnitCreateFields = [];
    private readonly List<(TextBox Quantity, ComboBox Age, XElement Adjustment)> _tempUnitAgeFields = [];
    private readonly HashSet<XElement> _tempUnitSelectionEntries = [];
    private readonly List<(AutoCompleteBox Box, XElement Element)> _vibrantTreeUnitFields = [];
    private readonly List<(AutoCompleteBox Unit, TextBox Factor, XElement Element)> _droughtDamageFields = [];
    private readonly List<(AutoCompleteBox Unit, TextBox Cap, XElement Element)> _greatFloodDamageCapFields = [];
    private readonly List<(ComboBox Age, TextBox Food, XElement Element)> _greatHuntMaxFoodFields = [];
    private readonly List<(ComboBox Age, TextBox Number, XElement Element)> _foxCharmAgeFields = [];
    private readonly List<(ComboBox Age, TextBox MaxFood, TextBox SpawnAmount, XElement Element)> _lureAgeInfoFields = [];
    private readonly List<(AutoCompleteBox Target, CheckBox InstantKill, XElement Element)> _undermineTargetFields = [];
    private readonly List<(AutoCompleteBox Proto, TextBox Radius, TextBox Theta, XElement Element)> _arcadianGrassFields = [];
    private readonly List<(AutoCompleteBox Target, AutoCompleteBox Drift, XElement Element)> _fimbulSnowDriftFields = [];
    private readonly List<(ComboBox Resource, TextBox Multiplier, XElement Element)> _cageResourceMultiplierFields = [];
    private readonly List<(TextBox Box, SwitchingStringBinding Binding)> _switchingStringFields = [];
    private readonly Dictionary<TextBox, string> _programmaticStringBoxValues = [];
    private readonly WrapPanel _tempestOptionalFlow = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, Button> _removeOtherAttributeButtons = new(StringComparer.OrdinalIgnoreCase);

    private XDocument? _modDocument;
    private XDocument? _loadedModDocument;
    private FileIntegrityTransaction.FileState? _loadedFileState;
    private FileIntegrityTransaction.FileState? _loadedStringsFileState;
    private bool _isPopulating;
    private int _populateGeneration;
    private Task _populationTask = Task.CompletedTask;

    private sealed class TempUnitCreateFields
    {
        public required XElement Element { get; init; }
        public required ComboBox Mode { get; init; }
        public required StackPanel Host { get; init; }
    }

    private sealed class SwitchingStringBinding
    {
        public required XElement Owner { get; init; }
        public required string Tag { get; init; }
        public required string ExpectedId { get; init; }
        public XElement? Field { get; set; }
    }

    public bool IsModifiedMode { get; private set; }
    public string? CurrentPowerName => _session.SelectedName;
    public bool IsDirty => _session.IsDirty;
    public string StatusMessage => _statusMessage.Text ?? "";

    private XElement? _currentEntry => _session.CurrentEntry;
    private string? _loadedEntryName => _session.LoadedEntryName;
    private bool _rawEditorDirty { get => _session.RawEditorDirty; set => _session.RawEditorDirty = value; }
    private ISet<string> _openOtherAttributes => _session.Transient.OpenOtherAttributes;
    private ISet<string> _openOtherSpecificAttributes => _session.Transient.OpenOtherSpecificAttributes;
    private bool Transient(PowerTransientArea area) => _session.Transient[area];
    private void Transient(PowerTransientArea area, bool value) => _session.Transient[area] = value;
    private bool _isUseLimitOpen { get => Transient(PowerTransientArea.UseLimit); set => Transient(PowerTransientArea.UseLimit, value); }
    private bool _losProtoUnitOpen { get => Transient(PowerTransientArea.LosProtoUnit); set => Transient(PowerTransientArea.LosProtoUnit, value); }
    private bool _restrictPlacementOpen { get => Transient(PowerTransientArea.RestrictPlacement); set => Transient(PowerTransientArea.RestrictPlacement, value); }
    private bool _overlapTypeOpen { get => Transient(PowerTransientArea.OverlapType); set => Transient(PowerTransientArea.OverlapType, value); }
    private bool _isRangeDecalOpen { get => Transient(PowerTransientArea.RangeDecal); set => Transient(PowerTransientArea.RangeDecal, value); }
    private bool _notificationsOpen { get => Transient(PowerTransientArea.Notifications); set => Transient(PowerTransientArea.Notifications, value); }
    private bool _minimapNotificationsOpen { get => Transient(PowerTransientArea.MinimapNotifications); set => Transient(PowerTransientArea.MinimapNotifications, value); }
    private bool _activeVisibilityOpen { get => Transient(PowerTransientArea.ActiveVisibility); set => Transient(PowerTransientArea.ActiveVisibility, value); }
    private bool _activeListVisibilityOpen { get => Transient(PowerTransientArea.ActiveListVisibility); set => Transient(PowerTransientArea.ActiveListVisibility, value); }
    private bool _cameraRangeOpen { get => Transient(PowerTransientArea.CameraRange); set => Transient(PowerTransientArea.CameraRange, value); }
    private bool _otherAttributePickerOpen { get => Transient(PowerTransientArea.OtherAttributePicker); set => Transient(PowerTransientArea.OtherAttributePicker, value); }
    private bool _otherSpecificAttributePickerOpen { get => Transient(PowerTransientArea.OtherSpecificAttributePicker); set => Transient(PowerTransientArea.OtherSpecificAttributePicker, value); }
    private bool _tempestVfxOpen { get => Transient(PowerTransientArea.TempestVfx); set => Transient(PowerTransientArea.TempestVfx, value); }
    private bool _throwExcludePullOpen { get => Transient(PowerTransientArea.ThrowExcludePull); set => Transient(PowerTransientArea.ThrowExcludePull, value); }
    private bool _throwExcludeThrowOpen { get => Transient(PowerTransientArea.ThrowExcludeThrow); set => Transient(PowerTransientArea.ThrowExcludeThrow, value); }
    private bool _witherKillTypesOpen { get => Transient(PowerTransientArea.WitherKillTypes); set => Transient(PowerTransientArea.WitherKillTypes, value); }
    private bool _cageKillSimilarOpen { get => Transient(PowerTransientArea.CageKillSimilar); set => Transient(PowerTransientArea.CageKillSimilar, value); }
    private bool _cageVfxOpen { get => Transient(PowerTransientArea.CageVfx); set => Transient(PowerTransientArea.CageVfx, value); }
    private bool _cageSoundOpen { get => Transient(PowerTransientArea.CageSound); set => Transient(PowerTransientArea.CageSound, value); }
    private bool _modifyVfxOpen { get => Transient(PowerTransientArea.ModifyVfx); set => Transient(PowerTransientArea.ModifyVfx, value); }
    private bool _switchingVfxAttachOpen { get => Transient(PowerTransientArea.SwitchingVfxAttach); set => Transient(PowerTransientArea.SwitchingVfxAttach, value); }
    private bool _foxCharmVfxOpen { get => Transient(PowerTransientArea.FoxCharmVfx); set => Transient(PowerTransientArea.FoxCharmVfx, value); }
    private bool _castMessageOpen { get => Transient(PowerTransientArea.CastMessage); set => Transient(PowerTransientArea.CastMessage, value); }
    private bool _castSelfMessageOpen { get => Transient(PowerTransientArea.CastSelfMessage); set => Transient(PowerTransientArea.CastSelfMessage, value); }
    private bool _alertPlayerOpen { get => Transient(PowerTransientArea.AlertPlayer); set => Transient(PowerTransientArea.AlertPlayer, value); }
    private bool _isXmlPreviewCollapsed { get => Transient(PowerTransientArea.XmlPreviewCollapsed); set => Transient(PowerTransientArea.XmlPreviewCollapsed, value); }
    private Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
        _prepareReferenceMigration => _hostServices.PrepareReferenceMigration;
    private Action<GameIdentifierReferenceMigrationPlan>? _adoptReferenceMigration => _hostServices.AdoptReferenceMigration;
    private Func<Window, string, Task<bool>>? _ensureCanDeleteAsync => _hostServices.EnsureCanDeleteAsync;
    private Func<Window, string, Task<bool>>? _confirmDeleteAsync => _hostServices.ConfirmDeleteAsync;
    private Window? _dialogOwner => _hostServices.DialogOwner;
    private Func<string, Task<string?>>? _resolveStringAsync => _hostServices.ResolveStringAsync;
    private Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync
        => _hostServices.SaveStringsAsync;
    private IReadOnlyList<string> _iconPaths => _catalogs.IconPaths;
    private IReadOnlyList<string> _protoUnitNames => _catalogs.ProtoUnitNames;
    private IReadOnlyList<string> _unitTypeNames => _catalogs.UnitTypeNames;
    private IReadOnlyList<string> _unitOrTypeNames => _catalogs.UnitOrTypeNames;
    private IReadOnlyList<string> _terrainCreepNames => _catalogs.TerrainCreepNames;
    private IReadOnlyList<string> _protoActionNames => _catalogs.ProtoActionNames;
    private IReadOnlyList<string> _techNames => _catalogs.TechnologyNames;
    private IReadOnlyList<string> _animationNames => _catalogs.AnimationNames;
    private IReadOnlyList<string> _boneNames => _catalogs.BoneNames;
    private IReadOnlyList<string> _tacticNames => _catalogs.TacticNames;

    public event EventHandler? BrowserStateChanged;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? Saved;

    public PowerEditorView() : this(new PowerEditorComposition([], null))
    {
    }

    // Compatibility seam for focused tests. Production composition uses the single-context
    // constructor below so adding a catalog or callback cannot expand the public view API.
    internal PowerEditorView(
        IEnumerable<PowerDefinition> baseDefinitions,
        string? modPath,
        Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
            prepareReferenceMigration = null,
        Action<GameIdentifierReferenceMigrationPlan>? adoptReferenceMigration = null,
        Func<Window, string, Task<bool>>? ensureCanDeleteAsync = null,
        Window? dialogOwner = null,
        Func<string, Task<string?>>? resolveStringAsync = null,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
        IEnumerable<string>? iconPaths = null,
        IconPreviewService? iconPreviewService = null,
        string? modStringsPath = null,
        IEnumerable<string>? protoUnitNames = null,
        IEnumerable<string>? unitTypeNames = null,
        IEnumerable<string>? terrainCreepNames = null,
        IEnumerable<string>? protoActionNames = null,
        IEnumerable<string>? techNames = null,
        IEnumerable<string>? animationNames = null,
        IEnumerable<string>? boneNames = null,
        IEnumerable<string>? tacticNames = null,
        Func<Window, string, Task<bool>>? confirmDeleteAsync = null)
        : this(new PowerEditorComposition(
            baseDefinitions,
            modPath,
            modStringsPath,
            new PowerEditorCatalogs(
                iconPaths,
                protoUnitNames,
                unitTypeNames,
                terrainCreepNames,
                protoActionNames,
                techNames,
                animationNames,
                boneNames,
                tacticNames),
            new PowerEditorHostServices(
                prepareReferenceMigration,
                adoptReferenceMigration,
                ensureCanDeleteAsync,
                confirmDeleteAsync,
                dialogOwner,
                resolveStringAsync,
                saveStringsAsync,
                iconPreviewService)))
    {
    }

    public PowerEditorView(PowerEditorComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        InitializeComponent();
        XmlSyntaxEditorService.Configure(_rawXmlEditor);
        _baseEntries = composition.BaseDefinitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new XElement(group.First().SourceElement), StringComparer.OrdinalIgnoreCase);
        _modPath = composition.ModPath;
        _modStringsPath = composition.ModStringsPath;
        _catalogs = composition.Catalogs;
        _hostServices = composition.HostServices;
        _iconPreview = new IconPreviewControl(_hostServices.IconPreviewService);
        _iconPreviewHost.Child = _iconPreview;
        EditorTextFieldStyle.ConfigureTextBox(_nameBox);
        EditorTextFieldStyle.ConfigureTextBox(_displayNameBox);
        EditorTextFieldStyle.ConfigureTextBox(_rolloverBox);
        EditorTextFieldStyle.ConfigureTextBox(_activeRolloverBox);
        EditorTextFieldStyle.ConfigureTextBox(_exclusiveMessageBox);
        EditorTextFieldStyle.ConfigureTextBox(_castMessageBox);
        EditorTextFieldStyle.ConfigureTextBox(_castSelfMessageBox);
        foreach (var box in new[]
                 { _orderHintBox, _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox,
                     _costBox, _repeatCostBox,
                     _limitGlobalBox, _limitPlayerBox, _restrictDistanceBox, _radiusBox,
                     _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                     _rangeIndicatorProtoSpeedBox,
                     _crackIntervalMinBox, _crackIntervalMaxBox, _timeFactorBox,
                     _stunLengthBox, _minThrowDistanceBox, _maxThrowDistanceBox,
                     _minThrowHeightBox, _maxThrowHeightBox,
                     _minThrowVelocityBox, _maxThrowVelocityBox,
                     _treeAmountBox, _treeSpawnIntervalMinBox, _treeSpawnIntervalMaxBox,
                     _growthRadiusStartBox, _growthRadiusIncreaseRateBox,
                     _damagePercentConversionBox, _unitEffectTimeSecondsBox,
                     _sharedEffectTimeSecondsBox,
                     _spiderAttackRangeBox, _spiderSpawnOffsetBox, _numberToCreateBox,
                     _separationDistanceBox, _hatchTimeBox, _hatchIntervalMinBox,
                     _hatchIntervalMaxBox, _warnTimeBox, _initialDelayBox,
                     _unitEffectTimeBox, _dropIntervalMinBox, _dropIntervalMaxBox,
                     _maxRetryBox, _maxUnitHeightBox,
                     _maxSpawnNumberBox, _sinkHoleTimeBox, _sinkHoleShakeBox,
                     _rumbleTimeBox, _firstSpawnDelayBox, _spawnIntervalMinBox,
                     _spawnIntervalMaxBox, _stampedeScaleBox, _stampedeAnimRateBox,
                     _stampedeAttackIntervalBox,
                     _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox,
                     _minimapDurationBox, _cameraDurationBox, _cameraStrengthBox, _cameraRangeBox,
                     _terrainHeightBox, _terrainWidthBox, _terrainSpeedBox, _terrainFadeTimeBox,
                     _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                     _accuracyBox, _allyDamageRatioBox })
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        foreach (var box in ImplodeNumberBoxes()) EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox, _vibrantGenerateIntervalBox,
                     _vibrantRotateMinBox, _vibrantRotateMaxBox, _vibrantGrowthMinBox,
                     _vibrantGrowthMaxBox, _vibrantScaleMinBox, _vibrantScaleMaxBox,
                     _restorationHealSpeedBox, _restorationScanIntervalBox,
                     _droughtBurnTimeBox, _greatFloodWaveCountBox, _greatFloodWaveRadiusBox,
                     _greatFloodSpacingBox, _greatFloodInitialVelocityBox, _greatFloodFinalVelocityBox,
                     _greatFloodRampTimeBox, _greatFloodAttackIntervalBox,
                     _shiftingSandsFadeTimeBox, _shiftingSandsSpawnTimeBox,
                     _shiftingSandsScanIntervalBox, _shiftingSandsPlacementRadiusBox,
                     _locustSwarmCountBox, _locustSwarmRadiusBox, _locustWayPointCountBox,
                     _locustWayPointOffsetBox, _locustDamageIntervalBox,
                     _boltStrikeHeightBox, _boltStrikeMinOffsetBox, _boltStrikeMaxOffsetBox,
                     _pestilenceRofPenaltyBox, _pestilenceTrainRatePenaltyBox,
                     _pestilenceScanIntervalBox, _greatHuntFoodRatioBox })
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        foreach (var box in NewPowerNumberBoxes())
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(_curseMaxResourcesBox, ProtoUnitNumericKind.UnsignedInteger);
        EditorNumericInputBehavior.AttachRule(_newMoonMaxFreeBox, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _meteorStandardStrikesBox, _meteorPriorityStrikesBox,
                     _meteorMinGroupSizeBox, _throwSizeLimitBox })
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        EditorNumericInputBehavior.AttachRule(_thunderBurstAmountBox, ProtoUnitNumericKind.SignedInteger);
        EditorNumericInputBehavior.AttachRule(_tornadoWaypointCountBox, ProtoUnitNumericKind.UnsignedInteger);
        EditorNumericInputBehavior.AttachRule(_witherCreepGeneratorsBox, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _underworldEidolonMinBox, _underworldEidolonMaxBox,
                     _underworldEidolonStepBox })
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _fimbulWolfSoftCapBox, _fimbulWolfHardCapBox,
                     _fimbulMainPackSizeBox, _fimbulExtraSpawnsBox, _fimbulSecondaryPackSizeBox })
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _earthMonsterAdditionalStrikesBox,
                     _earthMonsterUnitLimitBox, _earthMonsterMinGroupBox })
            EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        EditorNumericInputBehavior.AttachRule(_cageResourceCapBox, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _gustLengthBox, _gustSpeedBox, _gustMinFactorBox,
                     _gustMaxFactorBox, _gustBlowTimeBox, _gustTimeBox, _gustThrowSizeBox,
                     _divineAttackDelayBox, _divineDamageWidthBox, _divineDamageDepthBox,
                     _divineCameraDelayBox, _lureMinRangeBox, _lureMaxRangeBox,
                     _lureAttractRateBox, _lureVarianceBox, _lureCapDistanceBox,
                     _undermineDamageRadiusBox, _undermineAttackIntervalBox, _undermineDeformBox,
                     _forestFireBurnLengthBox, _forestFireFadeLengthBox, _forestFireEmbersLifespanBox,
                     _forestFireTreeDestructionBox, _forestFireTreeSearchRadiusBox,
                     _forestFireDamageSearchRadiusBox, _tempestSlowestStrikeBox,
                     _tempestFastestStrikeBox, _tempestStrikeHeightBox,
                     _tempestStrikeMinOffsetBox, _tempestStrikeMaxOffsetBox,
                     _tempestCloudBuildStartBox, _tempestCloudBuildEndBox,
                     _tempestCloudFadeStartBox, _tempestCloudFadeEndBox,
                     _tempestSlowestDriftBox, _tempestFastestDriftBox,
                     _tempestStrengthMinBox, _tempestStrengthMaxBox, _tempestPeriodMinBox,
                     _tempestPeriodMaxBox, _tempestAmplitudeMinBox, _tempestAmplitudeMaxBox,
                     _tempestAngleOffsetBox, _tempestTreeDurationBox,
                     _meteorStandardStrikesBox, _meteorPriorityStrikesBox, _meteorMinGroupSizeBox,
                     _meteorGroupSearchRadiusBox, _meteorSecondaryAccuracyBox,
                     _tornadoDamageIntervalBox, _tornadoGreaterDamageRadiusBox,
                     _tornadoWaypointCountBox, _tornadoSpiralSizeBox, _tornadoFunnelRadiusBox,
                     _tornadoFunnelHeightBox, _tornadoDebrisXzBox, _tornadoDebrisYBox,
                     _tornadoDebrisAngularBox, _earthquakeIntervalBox, _earthquakeTreeAccuracyBox,
                     _earthquakeEpicenterRadiusBox, _earthquakeMiddleRadiusBox,
                     _earthquakeOuterRadiusBox, _earthquakeEpicenterDamageBox,
                     _earthquakeMiddleDamageBox, _earthquakeOuterDamageBox,
                     _thunderBurstAmountBox, _thunderBurstDelayBox,
                     _thunderBurstRadiusIncreaseBox, _thunderBurstStunDurationBox,
                     _throwSizeLimitBox, _minThrowDistanceBox, _maxThrowDistanceBox,
                     _minThrowHeightBox, _maxThrowHeightBox,
                     _minThrowVelocityBox, _maxThrowVelocityBox,
                     _typhoonDragonSpawnDelayBox, _typhoonDragonAttackIntervalBox,
                     _typhoonSpawnDelayBox,
                     _typhoonAttackDelayBox, _typhoonAttackIntervalBox,
                     _typhoonRadiusStartBox, _typhoonRadiusEndBox, _typhoonRadiusExpansionBox,
                     _frostExpansionBox, _frostTransitionBox, _frostDurationBox,
                     _frostStrikeMinBox, _frostStrikeMaxBox,
                     _frostSlowestDriftBox, _frostFastestDriftBox,
                     _witherDurationBox, _witherVfxIntervalBox,
                     _witherCreepGeneratorsBox, _arcadianBlockerDurationBox,
                     _newMoonMaxFreeBox, _newMoonRateBox })
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    SpecificNumber_TextChanged(box, new TextChangedEventArgs(TextBox.TextChangedEvent));
            };
        _greatHuntFoodRatioBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty)
                SpecificNumber_TextChanged(_greatHuntFoodRatioBox, new TextChangedEventArgs(TextBox.TextChangedEvent));
        };
        EditorAutoCompleteService.ConfigureStrict(_powerTypeBox, PowerTypes, "", () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: false, preserveSuggestionOrder: true,
            selectAllOnFirstClick: false, valueCommitted: CommitPowerType);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_powerTypeBox, _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_otherSpecificAttributePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.EnableDropdown(_cracksSoundBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.EnableDropdown(_humanDeathSoundBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        foreach (var box in new[] { _liftSoundBox, _landSoundBox })
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_otherSpecificAttributePicker,
            _specificScroll);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_cracksSoundBox, _specificScroll);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_humanDeathSoundBox, _specificScroll);
        foreach (var box in new[] { _liftSoundBox, _landSoundBox })
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        EditorAutoCompleteService.ConfigureStrict(_shockwaveVfxBox, _protoUnitNames, "", () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: CommitShockwaveVfx);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_shockwaveVfxBox, _specificScroll);
        foreach (var box in new[] { _treeUnitBox, _treeEffectVfxBox })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false,
                valueCommitted: value => CommitGaiaProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in new[] { _effectVfxBox, _vfxProtoBox })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false,
                valueCommitted: value => CommitSpecificProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in new[] { _spiderUnitBox, _spiderEggUnitBox, _spiderDoorBox, _spiderWarnBox })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false,
                valueCommitted: value => CommitSpiderProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in VortexProtoBoxes())
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false,
                valueCommitted: value => CommitVortexProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in TartarianProtoBoxes())
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false,
                valueCommitted: value => CommitTartarianProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in ImplodeProtoBoxes())
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => CommitImplodeProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var box in ImplodeSoundBoxes())
        {
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty) ImplodeSound_TextChanged(box);
            };
        }
        foreach (var box in NewPowerProtoBoxes())
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => CommitNewPowerProto(box, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        foreach (var (box, tag) in new[]
                 {
                     (_fimbulMainPackTargetBox, "mainpacktargettype"),
                     (_fimbulSecondaryPackTargetBox, "minipacktargettype")
                 })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _unitOrTypeNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => SetOrRemoveSpecificField(tag, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.EnableDropdown(_fimbulWolfPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_fimbulWolfPicker, _specificScroll);
        _fimbulWolfPicker.SelectionChanged += FimbulWolfPicker_SelectionChanged;
        EditorAutoCompleteService.EnableDropdown(_cageSimilarTypePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_cageSimilarTypePicker,
            _specificScroll);
        _cageSimilarTypePicker.SelectionChanged += CageSimilarTypePicker_SelectionChanged;
        foreach (var (box, tag) in new[]
                 {
                     (_cageVfxMainTargetBoneBox, "vfxmaintargetbone"),
                     (_cageVfxMarkAnimBoneBox, "vfxmarkanimbone")
                 })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _boneNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => SetOrRemoveSpecificField(tag, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.ConfigureStrict(_modifyVfxTargetBox, _protoUnitNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField(ModifyVfxTargetTag(), value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_modifyVfxTargetBox,
            _specificScroll);
        EditorAutoCompleteService.ConfigureStrict(_modifyVfxBoneBox, _boneNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField(ModifyVfxBoneTag(), value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_modifyVfxBoneBox,
            _specificScroll);
        EditorAutoCompleteService.ConfigureStrict(_modifyVfxBirthBox, _protoUnitNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("birthvfx", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_modifyVfxBirthBox,
            _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_cageAdditionalImpactSoundBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_cageAdditionalImpactSoundBox,
            _specificScroll);
        _cageAdditionalImpactSoundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty && !_isPopulating &&
                _cageAdditionalImpactSoundBox.IsVisible)
                SetOrRemoveSpecificField("additionalimpactsoundset",
                    _cageAdditionalImpactSoundBox.Text?.Trim() ?? "");
        };
        foreach (var (box, tag) in new[]
                 {
                     (_rainLargeTargetBox, "rainspotproto"),
                     (_rainSmallTargetBox, "smallrainspotproto")
                 })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _unitTypeNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => SetOrRemoveSpecificField(tag, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.ConfigureStrict(_ceaseFireAttachBox, _unitOrTypeNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("attachvfxto", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_ceaseFireAttachBox,
            _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_prosperityTypePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_prosperityTypePicker,
            _specificScroll);
        _prosperityTypePicker.SelectionChanged += ProsperityTypePicker_SelectionChanged;
        foreach (var (box, tag) in new[]
                 {
                     (_earthMonsterStrikeActionBox, "strikeaction"),
                     (_earthMonsterCrushActionBox, "crushaction")
                 })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoActionNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => SetOrRemoveSpecificField(tag, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.ConfigureStrict(_earthMonsterRegurgitateAnimBox, _animationNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("regurgitateanim", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_earthMonsterRegurgitateAnimBox,
            _specificScroll);
        _earthMonsterSwallowRelationBox.ItemsSource = PowerPlayerRelations;
        foreach (var (box, tag) in new[]
                 {
                     (_earthMonsterEmergeSoundBox, "emergesoundset"),
                     (_earthMonsterChompSoundBox, "chompsoundset"),
                     (_earthMonsterRegurgitateSoundBox, "regurgitatesoundset")
                 })
        {
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty && !_isPopulating && box.IsVisible)
                    SetOrRemoveSpecificField(tag, box.Text?.Trim() ?? "");
            };
        }
        EditorAutoCompleteService.ConfigureStrict(_underworldEidolonTypeBox, _unitOrTypeNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("eidolontype", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_underworldEidolonTypeBox,
            _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_underworldLightSetBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_underworldLightSetBox,
            _specificScroll);
        _underworldLightSetBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty && !_isPopulating &&
                _underworldLightSetBox.IsVisible)
                SetOrRemoveSpecificField("lightset", _underworldLightSetBox.Text?.Trim() ?? "");
        };
        foreach (var (box, tag) in new[]
                 {
                     (_underworldTechEffectBox, "durationtecheffect"),
                     (_underworldOnlyIfTechBox, "onlyapplydurationtecheffectiftechactive")
                 })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _techNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value => SetOrRemoveSpecificField(tag, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.ConfigureStrict(_arcadianPlacementBox, _unitOrTypeNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("terrainplacementcheck", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_arcadianPlacementBox, _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_witherKillTypePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_witherKillTypePicker, _specificScroll);
        _witherKillTypePicker.SelectionChanged += WitherKillTypePicker_SelectionChanged;
        EditorAutoCompleteService.EnableDropdown(_powerCloneReplacementPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_powerCloneReplacementPicker,
            _specificScroll);
        _powerCloneReplacementPicker.SelectionChanged += PowerCloneReplacementPicker_SelectionChanged;
        EditorAutoCompleteService.ConfigureStrict(_tempestAreaAttackActionBox, _protoActionNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => SetOrRemoveSpecificField("areaattackaction", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_tempestAreaAttackActionBox, _specificScroll);
        EditorAutoCompleteService.EnableDropdown(_tempestLargeSoundBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_tempestLargeSoundBox, _specificScroll);
        _tempestLargeSoundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty && !_isPopulating && _tempestLargeSoundBox.IsVisible)
                SetOrRemoveSpecificField("largestrikesoundset", _tempestLargeSoundBox.Text ?? "");
        };
        EditorAutoCompleteService.EnableDropdown(_tempestStrikeSoundBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_tempestStrikeSoundBox, _specificScroll);
        _tempestStrikeSoundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty && !_isPopulating && _tempestStrikeSoundBox.IsVisible)
                SetOrRemoveSpecificField("strikesoundset", _tempestStrikeSoundBox.Text ?? "");
        };
        EditorAutoCompleteService.EnableDropdown(_tempestAvoidPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_tempestAvoidPicker, _specificScroll);
        _tempestAvoidPicker.ItemsSource = TempestAvoids.Select(item => item.Label).ToArray();
        _tempestAvoidPicker.SelectionChanged += TempestAvoidPicker_SelectionChanged;
        foreach (var picker in new[] { _throwExcludePullPicker, _throwExcludeThrowPicker })
        {
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating,
                selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(picker, _specificScroll);
            picker.SelectionChanged += ThrowExclusionPicker_SelectionChanged;
        }
        foreach (var box in MeteorSoundBoxes())
        {
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty) MeteorSound_TextChanged(box);
            };
        }
        EditorAutoCompleteService.ConfigureStrict(_greatFloodAttackNameBox, _protoActionNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
                    !_greatFloodAttackNameBox.IsVisible) return;
                SetOrRemoveSpecificField("initialattackname", value);
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_greatFloodAttackNameBox, _specificScroll);
        EditorAutoCompleteService.ConfigureStrict(_stampedeUnitBox, _protoUnitNames, "", () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: CommitStampedeValue);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_stampedeUnitBox, _specificScroll);
        EditorAutoCompleteService.ConfigureStrict(_stampedeAttackActionBox, _protoActionNames, "",
            () => _isPopulating, preserveUnknownInitialValue: false, allowEmpty: true,
            commitEmptyAsValid: true, selectAllOnFirstClick: false,
            valueCommitted: value => CommitStampedeAttribute("attackaction", value));
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_stampedeAttackActionBox,
            _specificScroll);
        foreach (var box in new[] { _stampedeScaleBox, _stampedeAnimRateBox, _stampedeAttackIntervalBox })
            box.TextChanged += StampedeNumber_TextChanged;
        foreach (var box in new[] { _gustDamageActionBox, _divineAttackActionBox })
        {
            EditorAutoCompleteService.ConfigureStrict(box, _protoActionNames, "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
                selectAllOnFirstClick: false, valueCommitted: value =>
                {
                    if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
                    SetOrRemoveSpecificField(ReferenceEquals(box, _gustDamageActionBox)
                        ? "damagingprotoaction" : "attackaction", value);
                });
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        }
        EditorAutoCompleteService.EnableDropdown(_swapFlagPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_swapFlagPicker, _specificScroll);
        _swapFlagPicker.SelectionChanged += SwapFlagPicker_SelectionChanged;
        EditorAutoCompleteService.EnableDropdown(_lureAttractTypePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_lureAttractTypePicker, _specificScroll);
        _lureAttractTypePicker.SelectionChanged += LureAttractTypePicker_SelectionChanged;
        EditorNumericFieldStyle.ConfigureNumericTextBox(_swapMinAmountBox);
        EditorNumericFieldStyle.ConfigureNumericTextBox(_swapMaxAmountBox);
        EditorNumericInputBehavior.AttachRule(_swapMinAmountBox, ProtoUnitNumericKind.UnsignedInteger);
        EditorNumericInputBehavior.AttachRule(_swapMaxAmountBox, ProtoUnitNumericKind.UnsignedInteger);
        foreach (var box in new[] { _swapMinAmountBox, _swapMaxAmountBox })
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    SwapMinMax_TextChanged(box, new TextChangedEventArgs(TextBox.TextChangedEvent));
            };
        _cracksSoundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty) CracksSound_TextChanged();
        };
        _humanDeathSoundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty) HumanDeathSound_TextChanged();
        };
        foreach (var box in new[] { _liftSoundBox, _landSoundBox })
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty) VortexSound_TextChanged(box);
            };
        foreach (var row in new Control[]
                 { _cracksSoundRow, _timeFactorRow, _shockwaveTopRow, _throwPropertiesRow,
                   _gaiaTreeTopRow, _gaiaSpawnRow, _growthRadiusRow, _effectVfxRow,
                   _damagePercentConversionRow, _vfxProtoRow, _effectTimeRow,
                   _spiderPropertiesRow, _spiderCountRow, _hatchPropertiesRow,
                   _spiderTimingRow, _humanDeathSoundRow, _vortexPropertiesRow,
                   _vortexDropRow, _maxUnitHeightRow, _vortexSoundsRow,
                   _tartarianSpawnRow, _tartarianRevealerRow, _tartarianGateRow, _rumbleTimeRow,
                   _tartarianSpawnTimingRow, _implodeProtosRow, _implodeSoundsRow,
                   _implodePullRow, _implodeExplosionRow, _implodeSpherePropertiesRow,
                   _implodeDamageIntervalRow, _implodeShearRow, _implodeDelayRow,
                   _implodeTimingRow, _implodeOrbitRadiusRow, _implodeAngularRow,
                   _implodeCameraShakeRow, _pSeedsFarmRow, _pSeedsVfxRow, _stealthLevelRow,
                   _earthWallRow, _damageTiersRow, _stealthVfxRow, _forestVfxRow,
                   _forestTimingRow, _rootDamageRow, _slowHealMultiplierRow,
                   _tempUnitCreateRow, _tempUnitAttachRow, _tempUnitAgeRow,
                   _tempUnitPlacementRow, _freyrsGiftCreateRow, _stampedeRow,
                   _stampedeFullLengthRow, _vibrantGenerationRow, _vibrantRotateRow,
                   _vibrantGrowthRow, _vibrantScaleRow, _restorationVfxRow,
                   _droughtTerrainCreepRow, _droughtVfxRow, _droughtInitialDamageRow,
                   _droughtBurnTimeRow, _greatFloodWaveRow, _greatFloodVelocityRow,
                   _greatFloodAttackRow, _greatFloodDamageCapRow,
                   _shiftingSandsPortalRow, _shiftingSandsTimingRow,
                   _shiftingSandsPlacementRadiusRow, _locustSwarmRow,
                   _locustWayPointsRow, _locustDamageIntervalRow, _boltUnitsRow,
                   _boltStrikePositionRow, _pestilencePenaltyRow, _pestilenceVfxRow,
                   _bronzeArmorRow, _teleportPortalRow, _greatHuntMaxFoodRow,
                   _greatHuntExtrasRow, _spyIndicatorRow, _swapEntriesRow, _swapMinMaxRow,
                   _swapOptionalRow, _swapFlagsRow, _curseResourcesRow, _curseSwapRow,
                   _gustMainRow, _gustTimingRow, _gustDamageRow, _gustThrowSizeRow,
                   _divineAttackRow, _divineDamageRow, _divineCameraDelayRow,
                   _lureUnitRow, _lureAttractTypeRow, _lureAttractionRow, _lureSpawnRow,
                   _lureAgeInfoRow, _undermineAttackRow, _undermineTargetRow, _undermineDeformRow,
                   _forestFireUnitsRow, _forestFireBurnRow, _forestFireSearchRow,
                   _tempestStrikeRow, _tempestLargeStrikeRow, _tempestStrikeIntervalRow,
                   _tempestStrikePositionRow, _tempestSoundRow, _tempestAttackRow, _tempestVfxRow,
                   _tempestCloudsRow, _tempestDriftRow, _tempestTreeBendRow,
                   _lightningVfxRow, _lightningRainRow,
                   _earthquakeAttackRow, _earthquakeRadiusRow, _earthquakeDamageRatioRow,
                   _earthquakeDustVfxRow, _thunderBurstMainRow, _thunderBurstVfxRow,
                   _typhoonDragonRow, _typhoonMainRow,
                   _frostFreezeRow, _frostVfxRow, _frostDriftRow,
                   _witherMainRow, _witherKillTypesRow, _witherDamageRow,
                   _witherCreepGeneratorsRow, _arcadianPlacementRow, _arcadianSpawnRow,
                   _arcadianGrassRow,
                   _underworldEidolonRow, _underworldSpawnRow, _underworldVfxRow, _underworldLightingRow,
                   _underworldTechEffectRow, _underworldSuddenDeathRow,
                   _powerCloneSlotRow, _powerCloneVfxRow,
                   _powerCloneReplacementRow,
                   _fimbulWolfRow, _fimbulMainPackRow, _fimbulSecondaryPackRow,
                   _fimbulWanderRow, _fimbulTargetSystemRow, _fimbulSnowDriftRow,
                   _fimbulSoundStrikeRow,
                   _cageMainRow, _cageKillSimilarRow, _cageVfxRow, _cageSoundRow,
                   _rainMainRow, _rainEffectsRow,
                   _switchingTypeRow, _switchingProgressRow, _switchingVfxAttachRow,
                   _switchingEffectsRow,
                   _newMoonResearchRow, _modifyVfxRow, _unitModifyRow,
                   _foxCharmAffectedNumberRow, _foxCharmVfxRow,
                   _ceaseFireVfxRow,
                   _prosperityVfxRow, _prosperityTypesRow, _sharedEffectsRow,
                   _earthMonsterUnitRow, _earthMonsterStrikeRow, _earthMonsterRegurgitateRow,
                   _earthMonsterChompRow, _earthMonsterSwallowRow,
                   _earthMonsterThrowRadiusRow, _earthMonsterLandImpactRow,
                   _earthMonsterSoundsRow,
                   _meteorUnitRow, _meteorStrikesRow, _meteorSplashRow, _meteorSoundsRow,
                   _tornadoMainRow, _tornadoFunnelRow, _tornadoDebrisRow })

            if (row.Parent is Panel parent) parent.Children.Remove(row);
        _singleActivationCheck.IsCheckedChanged += (_, _) => PowerFlag_Changed(_singleActivationCheck);
        _exclusiveCheck.IsCheckedChanged += (_, _) => PowerFlag_Changed(_exclusiveCheck);
        _activeTimeOnTargetCheck.IsCheckedChanged += (_, _) => ActiveTimeOnTarget_Changed();
        _globalCheck.IsCheckedChanged += (_, _) => GlobalCheck_Changed();
        _displayAlertsCheck.IsCheckedChanged += (_, _) => NotificationFlag_Changed(
            _displayAlertsCheck, "displayalerts");
        _hideOnActiveListCheck.IsCheckedChanged += (_, _) => NotificationFlag_Changed(
            _hideOnActiveListCheck, "hideonactivegplist");
        _playsBattleMusicCheck.IsCheckedChanged += (_, _) => ExtraFlag_Changed();
        _tempUnitSmoothRotationCheck.IsCheckedChanged += (_, _) => TempUnitPlacementFlagChanged(
            _tempUnitSmoothRotationCheck, "smoothrotation");
        _tempUnitForcePlacementCheck.IsCheckedChanged += (_, _) => TempUnitPlacementFlagChanged(
            _tempUnitForcePlacementCheck, "forceplacement");
        _tempUnitForceOrientationCheck.IsCheckedChanged += (_, _) => TempUnitPlacementFlagChanged(
            _tempUnitForceOrientationCheck, "forceuseplacementorientation");
        _tempestRadialTargetingCheck.IsCheckedChanged += (_, _) => TempestRadialTargeting_Changed();
        _meteorLargestGroupCheck.IsCheckedChanged += (_, _) => MeteorLargestGroup_Changed();
        _stampedeFullLengthCheck.IsCheckedChanged += (_, _) => StampedeFullLength_Changed();
        _cageKillAfterEndCheck.IsCheckedChanged += (_, _) => CageKillAfterEnd_Changed();
        _accuracyBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) Accuracy_TextChanged();
        };
        _frostArmorBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty)
                FrostArmor_TextChanged(_frostArmorBox, new TextChangedEventArgs(TextBox.TextChangedEvent));
        };
        _witherDamageBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty)
                WitherDamage_TextChanged();
        };
        _powerCloneCopyCostCheck.IsCheckedChanged += (_, _) =>
            PowerCloneCopyCost_Changed(_powerCloneCopyCostCheck, "cost", "copycostoftargetpower");
        _powerCloneCopyRepeatCostCheck.IsCheckedChanged += (_, _) =>
            PowerCloneCopyCost_Changed(_powerCloneCopyRepeatCostCheck, "repeatcost",
                "copyrepeatcostoftargetpower");
        _powerCloneSeededCheck.IsCheckedChanged += (_, _) => PowerCloneSeeded_Changed();
        foreach (var box in new[] { _underworldEidolonMinBox, _underworldEidolonMaxBox,
                     _underworldEidolonStepBox, _underworldMaxHpMultiplierBox,
                     _underworldMaxSpeedMultiplierBox, _underworldSpawnRadiusBox,
                     _underworldSpawnDurationBox, _underworldLightBuildUpBox,
                     _underworldLightDurationBox, _underworldLightShutdownBox,
                     _underworldSuddenDeathBox })
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    SpecificNumber_TextChanged(box, new TextChangedEventArgs(TextBox.TextChangedEvent));
            };
        _placementBox.ItemsSource = PlacementModes;
        _powerCloneSlotBox.ItemsSource = Enumerable.Range(1, 5).Select(value => value.ToString()).ToList();
        _switchingTierTypeBox.ItemsSource = new[] { "TotalResources", "CombatXP", "Time" };
        _switchingProgressTypeBox.ItemsSource = new[] { "Hide", "Default", "ShieldBlessing", "SpawnReward" };
        EditorNumericInputBehavior.AttachRule(_switchingIntervalBox, ProtoUnitNumericKind.SignedFloat);
        EditorAutoCompleteService.EnableDropdown(_switchingVfxAttachPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_switchingVfxAttachPicker,
            _specificScroll);
        _switchingVfxAttachPicker.SelectionChanged += SwitchingVfxAttachPicker_SelectionChanged;
        _rangeIndicatorBox.ItemsSource = RangeIndicatorModes;
        _powerPlayerRelationBox.ItemsSource = PowerPlayerRelations;
        _alertPlayerBox.ItemsSource = AlertPlayerRelations;
        _minimapSendToBox.ItemsSource = MinimapRecipients;
        _activeListVisibilityBox.ItemsSource = ActiveListVisibilities;
        EditorAutoCompleteService.EnableDropdown(_otherAttributePicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        foreach (var (name, row, _) in OtherAttributeSpecs())
        {
            if (name is "Radius" or "Power Player Relation" or "DualPowerMinDistance" or
                "Power Blocker" or "Reveal LOS" or "Lighting" or "Accuracy" or
                "Ally Damage Ratio" or "Reset Ability Charge for" or "Plays battle Music")
                row.ColumnDefinitions[1].Width = GridLength.Auto;
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumn(remove, 2);
            remove.Click += (_, _) => RemoveOtherAttribute(name);
            row.Children.Add(remove);
            _removeOtherAttributeButtons[name] = remove;
        }
        _revealUnitBox.ItemsSource = _protoUnitNames.Concat(["Revealer"])
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        _resetAbilityChargeBox.ItemsSource = _unitOrTypeNames;
        foreach (var box in new[] { _revealUnitBox, _lightingBox, _resetAbilityChargeBox })
        {
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty) ExtraSelector_TextChanged(box);
            };
            box.LostFocus += (_, _) => ExtraSelector_LostFocus(box);
        }
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(_lightingBox, _propertiesScroll);
        EditorAutoCompleteService.EnableDropdown(_placementFlagPicker, () => _isPopulating,
            selectAllOnFirstClick: false);
        foreach (var picker in new[] { _losProtoUnitBox, _restrictPlacementBox, _overlapTypeBox })
        {
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating,
                selectAllOnFirstClick: false);
            picker.SelectionChanged += PlacementOption_SelectionChanged;
            picker.LostFocus += PlacementOption_LostFocus;
        }
        _losProtoUnitBox.ItemsSource = _protoUnitNames;
        _restrictPlacementBox.ItemsSource = _unitOrTypeNames;
        _overlapTypeBox.ItemsSource = _unitOrTypeNames;
        _rangeIndicatorProtoBox.ItemsSource = _protoUnitNames;
        EditorAutoCompleteService.EnableDropdown(_rangeIndicatorProtoBox, () => _isPopulating,
            selectAllOnFirstClick: false);
        _rangeIndicatorProtoBox.SelectionChanged += (_, _) => CommitRangeIndicatorProto();
        _rangeIndicatorProtoBox.LostFocus += (_, _) => CommitRangeIndicatorProto();
        foreach (var picker in new[] { _targetTypePicker, _excludeTypePicker,
                     _attackTargetTypePicker, _attackExcludeTypePicker })
        {
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating,
                selectAllOnFirstClick: false);
            picker.LostFocus += PlacementTypePicker_LostFocus;
        }
        _restrictAddObstructionCheck.IsCheckedChanged += (_, _) => RestrictAddObstruction_Changed();
        _iconEditor.RequireSuggestionMatch = true;

        if (!string.IsNullOrWhiteSpace(_modPath))
        {
            var prepared = ModDocumentPersistence.Prepare(_modPath, ModXmlDocumentKind.Powers);
            _modDocument = prepared.Document;
            _loadedFileState = prepared.LoadedState;
            _loadedModDocument = new XDocument(_modDocument);
        }
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
        ShowEmpty();
    }

    public IReadOnlyList<string> GetPowerNames(bool modified)
    {
        if (!modified)
            return _baseEntries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_modDocument?.Root == null)
            return [];
        return PowerCatalog.ExtractDefinitions(_modDocument, isBuiltIn: false)
            .Select(definition => definition.Name)
            .ToList();
    }

    public void SetModifiedMode(bool modified)
    {
        if (IsModifiedMode == modified)
            return;
        IsModifiedMode = modified;
        _session.ClearSelection();
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPower(string? name)
    {
        if (_commandGate.CurrentCount == 0 && !_allowSelectionDuringCommand)
            return;
        var entry = string.IsNullOrWhiteSpace(name)
            ? null
            : IsModifiedMode
                ? PowerCatalog.Find(_modDocument!, name)
                : _baseEntries.GetValueOrDefault(name);
        _session.Select(name, entry);
        _applyRawXmlButton.IsVisible = false;

        if (_currentEntry == null)
        {
            _populationTask = Task.CompletedTask;
            ShowEmpty();
            return;
        }

        SetRawXml(PowerCatalog.FormatPreview(_currentEntry));
        XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, !IsModifiedMode);
        var generation = ++_populateGeneration;
        _populationTask = PopulatePropertiesSafelyAsync(_currentEntry, generation);
        _statusMessage.Text = IsModifiedMode
            ? "Edit Properties or the raw <power> XML, then use Save."
            : "Original .godpowers entry from Data.bar (read-only).";
    }

    private void SelectPowerFromCommand(string? name)
    {
        _allowSelectionDuringCommand = true;
        try
        {
            SelectPower(name);
        }
        finally
        {
            _allowSelectionDuringCommand = false;
        }
    }

    /// <summary>
    /// Selects a power and observes completion of its asynchronous UI population. Browser callers
    /// can keep using <see cref="SelectPower"/>; audit and integration code should use this method
    /// so a population failure is reported against the power that caused it.
    /// </summary>
    public async Task SelectPowerAsync(string? name)
    {
        SelectPower(name);
        await _populationTask;
    }

    private async Task PopulatePropertiesSafelyAsync(XElement entry, int generation)
    {
        try
        {
            await PopulatePropertiesAsync(entry, generation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation != _populateGeneration || !ReferenceEquals(entry, _currentEntry))
                return;

            _statusMessage.Text =
                $"Unable to fully display Power '{PowerCatalog.GetName(entry)}': {exception.Message}";
        }
    }

    private async Task PopulatePropertiesAsync(XElement entry, int generation)
    {
        _isPopulating = true;
        _programmaticStringBoxValues.Clear();
        try
        {
            _nameBox.Text = PowerCatalog.GetName(entry);
            _openOtherAttributes.Clear();
            foreach (var (name, _, tags) in OtherAttributeSpecs())
                if (HasOtherAttribute(entry, tags)) _openOtherAttributes.Add(name);
            _otherAttributePickerOpen = false;
            foreach (var control in new Control[]
                     { _nameBox, _displayNameBox, _orderHintBox, _rolloverBox, _activeRolloverBox, _iconEditor,
                         _placementBox, _placementFlagPicker, _targetTypePicker, _excludeTypePicker,
                         _powerPlayerRelationBox, _radiusBox, _globalCheck,
                         _rangeIndicatorBox, _indicatorRangeBox, _indicatorWidthBox,
                         _indicatorSpeedBox, _rangeDecalBox, _rangeIndicatorProtoBox,
                         _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                         _rangeIndicatorProtoSpeedBox,
                         _powerTypeBox, _otherSpecificAttributePicker, _cracksSoundBox,
                         _crackIntervalMinBox, _crackIntervalMaxBox, _timeFactorBox,
                         _stunLengthBox, _shockwaveVfxBox,
                         _minThrowDistanceBox, _maxThrowDistanceBox,
                         _minThrowHeightBox, _maxThrowHeightBox,
                         _minThrowVelocityBox, _maxThrowVelocityBox,
                         _treeUnitBox, _treeAmountBox,
                         _treeSpawnIntervalMinBox, _treeSpawnIntervalMaxBox,
                         _treeEffectVfxBox, _growthRadiusStartBox, _growthRadiusIncreaseRateBox,
                         _effectVfxBox, _damagePercentConversionBox, _vfxProtoBox,
                         _unitEffectTimeSecondsBox, _sharedEffectTimeSecondsBox,
                         _spiderUnitBox, _spiderEggUnitBox, _spiderDoorBox, _spiderWarnBox,
                         _spiderAttackRangeBox, _spiderSpawnOffsetBox, _numberToCreateBox,
                         _separationDistanceBox, _hatchTimeBox, _hatchIntervalMinBox,
                         _hatchIntervalMaxBox, _warnTimeBox, _initialDelayBox,
                         _humanDeathSoundBox,
                         _unitEffectTimeBox, _vortexLandVfxBox, _vortexStartVfxBox,
                         _vortexStartLinkedBox, _vortexFinishLinkedBox,
                         _dropIntervalMinBox, _dropIntervalMaxBox, _maxRetryBox,
                         _maxUnitHeightBox, _liftSoundBox, _landSoundBox,
                         _spawnProtoBox, _maxSpawnNumberBox, _tartarianRevealerBox, _tartarianGateUnitBox,
                         _tartarianGateBirthBox, _tartarianGateFlameBox,
                         _sinkHoleTimeBox, _sinkHoleShakeBox, _rumbleTimeBox,
                         _firstSpawnDelayBox, _spawnIntervalMinBox, _spawnIntervalMaxBox,
                         _castMessageBox, _castSelfMessageBox, _alertPlayerBox,
                         _displayAlertsCheck, _minimapSendToBox, _minimapDurationBox,
                         _activeListVisibilityBox, _hideOnActiveListCheck,
                         _cameraDurationBox, _cameraStrengthBox, _cameraRangeBox,
                         _terrainHeightBox, _terrainWidthBox, _terrainSpeedBox, _terrainFadeTimeBox,
                         _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                         _revealUnitBox, _lightingBox, _accuracyBox, _allyDamageRatioBox,
                         _resetAbilityChargeBox, _playsBattleMusicCheck,
                         _attackTargetTypePicker, _attackExcludeTypePicker,
                         _losProtoUnitBox,
                         _restrictPlacementBox, _overlapTypeBox, _restrictDistanceBox,
                         _restrictAddObstructionCheck,
                         _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox, _costBox, _repeatCostBox,
                         _limitGlobalBox, _limitPlayerBox, _singleActivationCheck, _exclusiveCheck,
                         _activeTimeOnTargetCheck,
                         _exclusiveMessageBox })
                control.IsEnabled = IsModifiedMode;
            foreach (var control in ImplodeControls()) control.IsEnabled = IsModifiedMode;
            foreach (var control in NewPowerControls()) control.IsEnabled = IsModifiedMode;

            foreach (var box in new[]
                     { _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox, _costBox, _repeatCostBox,
                         _limitGlobalBox, _limitPlayerBox, _radiusBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
            _orderHintBox.Text = EditorNumericFieldStyle.FormatDisplay(FindAttribute(entry, "orderhint")?.Value);
            _singleActivationCheck.IsChecked = PowerCatalog.FindField(entry, "singleactivation") != null;
            _exclusiveCheck.IsChecked = PowerCatalog.FindField(entry, "exclusive") != null;
            _activeTimeOnTargetCheck.IsChecked = PowerCatalog.FindField(entry, "activetimeontarget") != null;
            _globalCheck.IsChecked = PowerCatalog.FindField(entry, "global") != null;
            var playerRelation = PowerCatalog.FindField(entry, "powerplayerrelation")?.Value.Trim();
            var playerRelationOptions = OptionsIncludingCurrent(PowerPlayerRelations, playerRelation);
            _powerPlayerRelationBox.ItemsSource = playerRelationOptions;
            _powerPlayerRelationBox.SelectedItem = playerRelationOptions.FirstOrDefault(relation =>
                relation.Equals(playerRelation, StringComparison.OrdinalIgnoreCase)) ?? "All";
            var rangeIndicator = PowerCatalog.FindField(entry, "rangeindicator");
            var rangeIndicatorProto = PowerCatalog.FindField(entry, "rangeindicatorprotoid");
            _rangeIndicatorProtoBox.Text = rangeIndicatorProto?.Value.Trim() ?? "";
            foreach (var box in new[] { _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                         _rangeIndicatorProtoSpeedBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    FindPlacementFlag(rangeIndicatorProto, (string)box.Tag!)?.Value);
            var indicatorMode = rangeIndicator?.Value.Trim();
            var indicatorOptions = OptionsIncludingCurrent(RangeIndicatorModes, indicatorMode);
            _rangeIndicatorBox.ItemsSource = indicatorOptions;
            _rangeIndicatorBox.SelectedItem = indicatorOptions.FirstOrDefault(mode =>
                mode.Equals(indicatorMode, StringComparison.OrdinalIgnoreCase));
            foreach (var box in new[] { _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    FindPlacementFlag(rangeIndicator, (string)box.Tag!)?.Value);
            _rangeDecalBox.Text = PowerCatalog.FindField(entry, "rangeindicatorcustomdecal")?.Value ?? "";
            _isRangeDecalOpen = PowerCatalog.FindField(entry, "rangeindicatorcustomdecal") != null;
            _castMessageOpen = PowerCatalog.FindField(entry, "notificationformatid") != null;
            _castSelfMessageOpen = PowerCatalog.FindField(entry, "selfnotificationformatid") != null;
            _alertPlayerOpen = PowerCatalog.FindField(entry, "messagealertplayerrelation") != null;
            _displayAlertsCheck.IsChecked = PowerCatalog.FindField(entry, "displayalerts") != null;
            var alertRelation = PowerCatalog.FindField(entry, "messagealertplayerrelation")?.Value.Trim();
            var alertOptions = OptionsIncludingCurrent(AlertPlayerRelations, alertRelation);
            _alertPlayerBox.ItemsSource = alertOptions;
            _alertPlayerBox.SelectedItem = alertOptions.FirstOrDefault(relation =>
                relation.Equals(alertRelation, StringComparison.OrdinalIgnoreCase)) ?? "All";
            _notificationsOpen = _castMessageOpen || _castSelfMessageOpen || _alertPlayerOpen ||
                                 _displayAlertsCheck.IsChecked == true;
            var minimap = PowerCatalog.FindField(entry, "minimapeventtime");
            _minimapNotificationsOpen = minimap != null;
            var minimapRecipient = FindPlacementFlag(minimap, "sendalertto")?.Value;
            var minimapOptions = OptionsIncludingCurrent(MinimapRecipients, minimapRecipient);
            _minimapSendToBox.ItemsSource = minimapOptions;
            _minimapSendToBox.SelectedItem = minimapOptions.FirstOrDefault(value =>
                value.Equals(minimapRecipient, StringComparison.OrdinalIgnoreCase)) ?? "All";
            _minimapDurationBox.Text = EditorNumericFieldStyle.FormatDisplay(minimap?.Value);
            var activeVisibility = PowerCatalog.FindField(entry, "activelistvisibility")?.Value.Trim();
            var visibilityOptions = OptionsIncludingCurrent(ActiveListVisibilities, activeVisibility);
            _activeListVisibilityBox.ItemsSource = visibilityOptions;
            _activeListVisibilityBox.SelectedItem = visibilityOptions.FirstOrDefault(value =>
                value.Equals(activeVisibility, StringComparison.OrdinalIgnoreCase));
            _activeListVisibilityOpen = PowerCatalog.FindField(entry, "activelistvisibility") != null;
            _hideOnActiveListCheck.IsChecked = PowerCatalog.FindField(entry, "hideonactivegplist") != null;
            _activeVisibilityOpen = PowerCatalog.FindField(entry, "activelistvisibility") != null ||
                                    _hideOnActiveListCheck.IsChecked == true;
            foreach (var box in EffectAttributeBoxes())
            {
                var parts = ((string)box.Tag!).Split(':');
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    FindPlacementFlag(PowerCatalog.FindField(entry, parts[0]), parts[1])?.Value);
            }
            _cameraRangeOpen = FindPlacementFlag(PowerCatalog.FindField(entry, "camerashake"), "range") != null;
            RefreshEffectVisibility();
            RefreshSoundSets();
            PopulateExtraFields(entry);
            var placement = PowerCatalog.FindField(entry, "placement");
            var placementMode = placement?.Value.Trim();
            var placementOptions = OptionsIncludingCurrent(PlacementModes, placementMode);
            _placementBox.ItemsSource = placementOptions;
            _placementBox.SelectedItem = placementOptions.FirstOrDefault(mode =>
                mode.Equals(placementMode, StringComparison.OrdinalIgnoreCase));
            _losProtoUnitBox.Text = FindPlacementFlag(placement, "losprotounit")?.Value ?? "";
            _restrictPlacementBox.Text = FindPlacementFlag(placement, "restrictplacementnear")?.Value ?? "";
            _restrictDistanceBox.Text = EditorNumericFieldStyle.FormatDisplay(
                FindPlacementFlag(placement, "restrictplacementdistance")?.Value);
            _restrictAddObstructionCheck.IsChecked =
                FindPlacementFlag(placement, "restrictplacementnearaddobstruction") != null;
            _overlapTypeBox.Text = FindPlacementFlag(placement, "overlaptype")?.Value ?? "";
            _losProtoUnitOpen = FindPlacementFlag(placement, "losprotounit") != null;
            _restrictPlacementOpen = FindPlacementFlag(placement, "restrictplacementnear") != null ||
                                     FindPlacementFlag(placement, "restrictplacementdistance") != null ||
                                     FindPlacementFlag(placement, "restrictplacementnearaddobstruction") != null;
            _overlapTypeOpen = FindPlacementFlag(placement, "overlaptype") != null;
            RefreshPlacementRestrictions();
            RefreshPlacementFlags();
            RefreshPlacementTypes();
            RefreshAttackTypes();
            RefreshRangeAndTargetVisibility();
            RefreshNotificationVisibility();
            RefreshPlacementVisibility();
            _isUseLimitOpen = PowerCatalog.FindField(entry, "limitglobal") != null ||
                              PowerCatalog.FindField(entry, "limitplayer") != null;
            RefreshOptionalPropertyVisibility();
            RefreshOtherAttributeRows();
            PopulateSpecificFields(entry);

            var icon = IconCatalog.ToResourceRelativePath(PowerCatalog.FindField(entry, "icon")?.Value ?? "");
            _iconEditor.Configure(ProtoEditorWindow.NormalizeIconCatalogValue(icon, _iconPaths), _iconPaths,
                value =>
                {
                    if (!CanEditProperties(entry))
                        return Task.CompletedTask;
                    PowerCatalog.SetField(entry, "icon", value);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    _ = _iconPreview.ShowAsync(value);
                    return Task.CompletedTask;
                });
            _ = _iconPreview.ShowAsync(icon);
            RefreshActiveRolloverVisibility();

            var resolutions = await Task.WhenAll(StringBackedTags.Select(async tag =>
            {
                var id = PowerCatalog.FindField(entry, tag)?.Value.Trim() ?? "";
                if (_session.TryGetPendingString(id, out var pending))
                    return (Tag: tag, Id: id, Text: pending);
                var resolved = id.Length == 0 || _resolveStringAsync == null
                    ? null
                    : await _resolveStringAsync(id);
                return (Tag: tag, Id: id, Text: resolved ?? id);
            }));
            if (generation != _populateGeneration || !ReferenceEquals(entry, _currentEntry))
                return;
            foreach (var resolution in resolutions)
            {
                SetStringBoxTextWithoutEditing(GetResolvedStringBox(resolution.Tag), resolution.Text);
            }
            await ResolveSwitchingStringsAsync(entry, generation);
        }
        finally
        {
            if (generation == _populateGeneration)
            {
                // SelectPower remains synchronously editable. The settling window is still awaited
                // by SelectPowerAsync so audits observe queued initialization events; handlers must
                // therefore be idempotent when the formatted UI value is equivalent to the XML.
                _isPopulating = false;
                await Task.Delay(25);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    static () => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
    }

    private bool CanEditProperties(XElement entry)
        => !_isPopulating && !_rawEditorDirty && IsModifiedMode && ReferenceEquals(entry, _currentEntry);

    private (string Name, Grid Row, string[] Tags)[] OtherAttributeSpecs() =>
    [
        ("Range Indicator", _rangeIndicatorRow, ["rangeindicator", "rangeindicatorcustomdecal"]),
        ("Range Indicator Proto", _rangeIndicatorProtoRow, ["rangeindicatorprotoid"]),
        ("Radius", _radiusRow, ["radius", "global"]),
        ("Power Player Relation", _powerPlayerRelationRow, ["powerplayerrelation"]),
        ("Attack target", _attackTargetRow, ["abstractattacktargettype", "explicitlyrestrictedattacktargettype"]),
        ("Notification", _notificationsSection,
            ["notificationformatid", "selfnotificationformatid", "messagealertplayerrelation", "displayalerts"]),
        ("Minimap Notification", _minimapNotificationsSection, ["minimapeventtime"]),
        ("Active Power Visibility", _activeVisibilitySection, ["activelistvisibility", "hideonactivegplist"]),
        ("Camera Shake", _cameraShakeRow, ["camerashake"]),
        ("Terrain Rumbling", _terrainRumblingRow, ["terrainrumbling"]),
        ("Sound set", _soundSetsSection, ["soundset"]),
        ("DualPowerMinDistance", _dualPowerMinDistanceRow, ["dualpowermindistance"]),
        ("Power Blocker", _powerBlockerRow, ["powerblocker"]),
        ("Reveal LOS", _revealLosRow, ["reveallos"]),
        ("Lighting", _lightingRow, ["lighting"]),
        ("Accuracy", _accuracyRow, ["accuracy"]),
        ("Ally Damage Ratio", _allyDamageRatioRow, ["allydamageratio"]),
        ("Reset Ability Charge for", _resetAbilityChargeRow, ["abstractpowerresetsabilitychargetype"]),
        ("Plays battle Music", _playsBattleMusicRow, ["playsbattlemusic"])
    ];

    private static bool HasOtherAttribute(XElement entry, string[] tags)
        => entry.Elements().Any(element => tags.Contains(element.Name.LocalName,
            StringComparer.OrdinalIgnoreCase));

    private bool OtherSelected(string name)
        => !IsModifiedMode || _openOtherAttributes.Contains(name);

    private (string Name, Grid Row, string[] Tags)[] SpecificAttributeSpecs() =>
    [
        ("Cracks Sound", _cracksSoundRow, ["cracksoundset", "crackintervalmin", "crackintervalmax"]),
        ("Time Factor", _timeFactorRow, ["timefactor"]),
        ("Stun lenght", _shockwaveTopRow, ["stunlength"]),
        ("Shockwave VFX", _shockwaveTopRow, ["shockwavevfx"]),
        ("Throw properties", _throwPropertiesRow,
            ["minthrowdistance", "maxthrowdistance", "minthrowheight", "maxthrowheight",
             "minthrowvelocity", "maxthrowvelocity", "throwsizelimit", "excludefromthrow"]),
        ("Tree Unit", _gaiaTreeTopRow, ["treeproto"]),
        ("Tree amount", _gaiaTreeTopRow, ["totalnumberoftrees"]),
        ("Tree Spawn Interval", _gaiaSpawnRow, ["treespawnintervalmin", "treespawnintervalmax"]),
        ("Tree Effect VFX", _gaiaSpawnRow, ["treeeffectproto"]),
        ("Growth radius", _growthRadiusRow, ["growthradiusstart", "growthradiusincreaserate"]),
        ("Effect VFX", _effectVfxRow, ["effectproto"]),
        ("Damage Percent on Conversion", _damagePercentConversionRow, ["percentdamageonconvert"]),
        ("VFX Proto", _vfxProtoRow, ["vfxproto"]),
        ("Effect time", _effectTimeRow, ["uniteffecttimeseconds", "sharedeffecttimeseconds"]),
        ("Spider properties", _spiderPropertiesRow,
            ["spiderproto", "spidereggproto", "spiderdoorproto", "spiderwarnproto",
             "spiderattackrange", "spiderspawnoffset"]),
        ("Number to create", _spiderCountRow, ["numbertocreate"]),
        ("Separation Distance", _spiderCountRow, ["separationdistance"]),
        ("Hatch properties", _hatchPropertiesRow,
            ["hatchtime", "hatchintervalmin", "hatchintervalmax"]),
        ("Warn time", _spiderTimingRow, ["warntime"]),
        ("Initial Delay", _spiderTimingRow, ["initialdelay"]),
        ("Human Death Sound", _humanDeathSoundRow, ["humandeathsoundset"]),
        ("Unit Effect Time", _spiderTimingRow, ["uniteffecttime"]),
        ("Vortex properties", _vortexPropertiesRow,
            ["vortexlandingproto", "vortexstartproto", "vortexstartlinkedproto",
             "vortexfinishlinkedproto"]),
        ("Drop Interval", _vortexDropRow, ["dropintervalmin", "dropintervalmax"]),
        ("Max Retry", _vortexDropRow, ["maxretryseconds"]),
        ("Max Unit Height", _maxUnitHeightRow, ["maxunitheight"]),
        ("Lift Sound", _vortexSoundsRow, ["liftsoundset"]),
        ("Land Sound", _vortexSoundsRow, ["landsoundset"]),
        ("Spawn Proto", _tartarianSpawnRow, ["spawnproto"]),
        ("Max Spawn Amount", _tartarianSpawnRow, ["maxnumspawns"]),
        ("Revealer Unit", _tartarianRevealerRow, ["revealerproto"]),
        ("Tartarian Gate", _tartarianGateRow,
            ["tartariangateproto", "gatebirthproto", "gateflameproto", "sinkholetime",
             "sinkholeshakestrength"]),
        ("Rumble Time", _rumbleTimeRow, ["rumbletime"]),
        ("First Spawn delay", _tartarianSpawnTimingRow, ["spawntimedelay"]),
        ("Spawn interval", _tartarianSpawnTimingRow, ["spawnintervalmin", "spawnintervalmax"]),
        ("Implode Protos", _implodeProtosRow, ["sphereproto", "shockwaveproto", "vfxpulleffectproto"]),
        ("Implode Sounds", _implodeSoundsRow,
            ["implodestartsoundset", "implodeexplodesoundset", "implodeexplosionwindupsoundset", "implodehitsoundset"]),
        ("Pull properties", _implodePullRow,
            ["pullradius", "unitescaperadius", "pullstrengthincrement", "maximumpullstrength",
             "pullstrengthoverkillincrement", "pullstrengthadjustfactor", "unitacceleration"]),
        ("Explosion", _implodeExplosionRow,
            ["explosionwinduplength", "exploderadius", "poweraccumulationincrement",
             "maximumaccumulatedpower", "explosiondelay", "shockwavevelocity"]),
        ("Sphere properties", _implodeSpherePropertiesRow,
            ["spherescalemultiplier", "spheretargetheight", "sphereheightadjustfactor",
             "spheremovementdelay", "unitfloatadjustfactor"]),
        ("Implode Damage interval", _implodeDamageIntervalRow, ["damageintervalseconds"]),
        ("Shear", _implodeShearRow, ["baseshearincrement", "shearmultiplier", "baseshearmax"]),
        ("Implode delay", _implodeDelayRow,
            ["unitimplodetimedelaymin", "unitimplodetimedelaymax", "unitimplodeendtimedelaymin",
             "unitimplodeendtimedelaymax"]),
        ("Implode Timing", _implodeTimingRow,
            ["mintimeinstagestarting", "mintimeinstagepullingunits", "mintimeinstageexploding"]),
        ("Orbit Radius Squared", _implodeOrbitRadiusRow,
            ["innerorbitradiussquared", "middleorbitradiussquared", "outerorbitradiussquared"]),
        ("Angular Acceleration", _implodeAngularRow,
            ["innerorbitangularacceleration", "middleorbitangularacceleration", "outerorbitangularacceleration"]),
        ("Implode Camera Shake", _implodeCameraShakeRow,
            ["initialrumblecamerashakeduration", "initialrumblecamerashakestrength",
             "unitdeathcamerashakeduration", "unitdeathcamerashakestrength",
             "explosioncamerashakeduration", "explosioncamerashakestrength"]),
        ("Farm Proto", _pSeedsFarmRow, ["farmproto"]),
        ("Number to Transform", _pSeedsFarmRow, ["numbertotransform"]),
        ("Farm VFX", _pSeedsVfxRow, ["pseedsfarmvfx", "pseedsfarmtargetvfx"]),
        ("Stealth Level", _stealthLevelRow, ["stealthlevel"]),
        ("Earth Wall", _earthWallRow, ["wallproto", "wallsegmentcount"]),
        ("Damage Tiers", _damageTiersRow, ["damagetiers"]),
        ("Vanish VFX", _stealthVfxRow, ["vanishvfx", "vanishunitvfx", "reappearunitvfx"]),
        ("Rooted enemy VFX", _forestVfxRow, ["rootedenemyvfx"]),
        ("Tree Area VFX", _forestVfxRow, ["treeareavfx"]),
        ("Tree VFX", _forestVfxRow, ["treevfx"]),
        ("Stay Time", _forestTimingRow, ["staytime"]),
        ("Root duration", _forestTimingRow, ["rootedtime"]),
        ("Root damage", _rootDamageRow, ["rooteddamage"]),
        ("Slow Heal Multiplier", _slowHealMultiplierRow, ["slowhealmultiplier"]),
        ("Create Unit", _tempUnitCreateRow, ["createunit"]),
        ("Age Specific Create Unit", _freyrsGiftCreateRow, ["createagespecificunit"]),
        ("Attach Unit", _tempUnitAttachRow, ["attachproto"]),
        ("Age Adjustment", _tempUnitAgeRow, ["ageadjustment"]),
        ("Create placement", _tempUnitPlacementRow,
            ["smoothrotation", "forceplacement", "forceuseplacementorientation"]),
        ("Vibrant Generation", _vibrantGenerationRow, ["generateinterval", "treeproto", "treevfx"]),
        ("Rotate Speed", _vibrantRotateRow, ["minrotatespeed", "maxrotatespeed"]),
        ("Growth", _vibrantGrowthRow, ["mingrowspeed", "maxgrowspeed"]),
        ("Scale", _vibrantScaleRow, ["minscale", "maxscale"]),
        ("Heal speed", _slowHealMultiplierRow, ["healspeed"]),
        ("Scan interval", _slowHealMultiplierRow, ["scaninterval"]),
        ("Heal VFX", _restorationVfxRow, ["healvfx", "areavfx"]),
        ("Terrain Creep", _droughtTerrainCreepRow, ["terraincreep"]),
        ("Land VFX", _droughtVfxRow, ["landprotounit"]),
        ("Wave Unit", _droughtVfxRow, ["waveprotounit"]),
        ("Initial Damage", _droughtInitialDamageRow, ["initialdamage"]),
        ("Burn Time", _droughtBurnTimeRow, ["burntime"]),
        ("Flood Wave", _greatFloodWaveRow, ["waveprotounit", "wavecount", "waveradius", "spacingdistance"]),
        ("Velocity", _greatFloodVelocityRow, ["initialvelocity", "finalvelocity", "ramptime"]),
        ("Attack Action", _greatFloodAttackRow, ["initialattackname"]),
        ("Attack Interval", _greatFloodAttackRow, ["attackinterval"]),
        ("Damage Cap", _greatFloodDamageCapRow, ["damagecap"]),
        ("Portal", _shiftingSandsPortalRow, ["inportalprotounit", "outportalprotounit"]),
        ("Portal timing", _shiftingSandsTimingRow, ["unitfadetime", "outportalspawntime", "scaninterval"]),
        ("Placement Radius", _shiftingSandsPlacementRadiusRow, ["placementradius"]),
        ("Swarm Unit", _locustSwarmRow, ["swarmprotounit", "swarmcount", "swarmradius"]),
        ("Swarm Way Points", _locustWayPointsRow, ["swarmwaypoints", "swarmwaypointoffset"]),
        ("Damage Interval", _locustDamageIntervalRow, ["damageinterval"]),
        ("Strike Unit", _boltUnitsRow, ["strikeproto"]),
        ("Bolt VFX", _boltUnitsRow, ["sparksvfxproto", "scorchvfxproto"]),
        ("Strike Position", _boltStrikePositionRow, ["strikeheight", "strikeminoffset", "strikemaxoffset"]),
        ("Pestilence Penalty", _pestilencePenaltyRow, ["rofpercentpenalty", "trainratepenalty", "rescaninterval"]),
        ("Pestilence VFX", _pestilenceVfxRow, ["smallvfxproto", "largevfxproto"]),
        ("Armor", _bronzeArmorRow, ["armor"]),
        ("Teleport Portal", _teleportPortalRow, ["inportal", "outportal"]),
        ("Max food duplicated", _greatHuntMaxFoodRow, ["maxfood"]),
        ("Great Hunt extras", _greatHuntExtrasRow, ["spawnproto", "spawnfoodratio", "vfxproto"])
        ,("Spy Indicator", _spyIndicatorRow, ["spyprotounit"])
        ,("Stampede", _stampedeRow, ["stampede"])
        ,("Stampede Full length", _stampedeFullLengthRow, ["fulllength"])
        ,("Swap", _swapEntriesRow, ["abstracttype"])
        ,("Swap Min/Max", _swapMinMaxRow, ["convertusingminmax"])
        ,("Swap Options", _swapOptionalRow, ["swapdelay", "overallchance", "vfxproto"])
        ,("Swap Flags", _swapFlagsRow, SwapFlags.Select(flag => flag.XmlName).ToArray())
        ,("Curse Resources", _curseResourcesRow, ["maxresources", "resourceconvertratio"])
        ,("Curse Swap", _curseSwapRow, ["swapdelay", "vfxproto"])
        ,("Gust", _gustMainRow, ["gustproto", "gustlength", "gustspeed", "gustminspeedfactor", "gustmaxspeedfactor"])
        ,("Gust Timing", _gustTimingRow, ["blowtime", "gusttime"])
        ,("Gust Damage", _gustDamageRow, ["damagingproto", "damagingprotoaction"])
        ,("Throw Max Size", _gustThrowSizeRow, ["throwsizelimit"])
        ,("Divine Attack", _divineAttackRow, ["attackproto", "attackaction", "attackdelay"])
        ,("Divine Damage", _divineDamageRow, ["damagewidth", "damagedepth", "decalproto"])
        ,("Camera Shake Delay", _divineCameraDelayRow, ["camerashakedelay"])
        ,("Lure Unit", _lureUnitRow, ["lureproto"])
        ,("Attract Type", _lureAttractTypeRow, ["attracttype"])
        ,("Attraction", _lureAttractionRow,
            ["minrange", "maxrange", "attractrate", "variance", "attractiondistancetocountagainstcap"])
        ,("Lure Spawn", _lureSpawnRow, ["spawnproto", "spawnvfx"])
        ,("Lure Age info", _lureAgeInfoRow, ["ageinfo"])
        ,("Undermine Attack", _undermineAttackRow,
            ["attackproto", "damageradius", "attackintervalseconds"])
        ,("Undermine Targets", _undermineTargetRow, ["attacktarget"])
        ,("Deform Depth", _undermineDeformRow, ["deformdepth"])
        ,("Forest Fire Units", _forestFireUnitsRow, ["attackproto", "embersproto", "vfxproto"])
        ,("Forest Fire Burn", _forestFireBurnRow, ["burnlength", "burnfadelength", "emberlifespan"])
        ,("Forest Fire Search", _forestFireSearchRow,
            ["treedestructionseconds", "treesearchradius", "damagesearchradius"])
        ,("Tempest Strike", _tempestStrikeRow, ["strikeproto"])
        ,("Strike Interval", _tempestStrikeIntervalRow, ["sloweststriketime", "fasteststriketime"])
        ,("Tempest Strike Position", _tempestStrikePositionRow,
            ["strikeheight", "strikeminoffset", "strikemaxoffset"])
        ,("Tempest Soundset", _tempestSoundRow, ["strikesoundset"])
        ,("Tempest Attack", _tempestAttackRow, ["areaattackaction", "radialtargeting"])
        ,("Large Strike", _tempestLargeStrikeRow,
            ["largestrikeproto", "largestrikeintervaltiming", "largestrikeavoidswater", "largestrikeavoidsbuildings",
             "largestrikeavoidslandimpassable", "largestrikesoundset"])
        ,("Tempest VFX", _tempestVfxRow,
            ["effectproto", "groundvfxproto", "groundimpactvfxproto", "icesheetvfxproto"])
        ,("Tempest Clouds", _tempestCloudsRow,
            ["cloudbuildupstarttime", "cloudbuildupendtime", "cloudfadeoutstarttime", "cloudfadeoutendtime"])
        ,("Tempest Drift", _tempestDriftRow, ["slowestdrifttime", "fastestdrifttime"])
        ,("Tree Bend", _tempestTreeBendRow,
            ["treebendminstrength", "treebendmaxstrength", "treebendminperiod", "treebendmaxperiod",
             "treebendminamplitude", "treebendmaxamplitude", "treebendangleoffset", "treebendingduration",
             "treebendstrengthmin", "treebendstrengthmax", "treebendperiodmin", "treebendperiodmax"])
        ,("Lightning VFX", _lightningVfxRow,
            ["sparksvfxproto", "groundsparksvfxproto", "scorchvfxproto", "rainvfxproto", "cloudvfxproto"])
        ,("Lightning Rain", _lightningRainRow,
            ["rainbuildupstarttime", "rainbuildupendtime", "rainfadeoutstarttime", "rainfadeoutendtime"])
        ,("Earthquake Attack", _earthquakeAttackRow,
            ["attackproto", "attackinterval", "attacktreeaccuracy"])
        ,("Earthquake Zone Radius", _earthquakeRadiusRow,
            ["epicenterzoneradius", "middlezoneradius", "outerzoneradius"])
        ,("Earthquake Zone Damage Ratio", _earthquakeDamageRatioRow,
            ["epicenterzonedamageratio", "middlezonedamageratio", "outerzonedamageratio"])
        ,("Earthquake Dust VFX", _earthquakeDustVfxRow,
            ["dustsmallvfxproto", "dustmediumvfxproto", "dustlargevfxproto"])
        ,("Thunder Burst", _thunderBurstMainRow,
            ["amount", "delay", "radiusincreasestep", "stunduration"])
        ,("Thunder Burst VFX", _thunderBurstVfxRow,
            ["startingvfx", "chargingvfx", "burstvfx"])
        ,("Typhoon Dragon", _typhoonDragonRow,
            ["dragonprotounit", "dragonspawndelayseconds", "dragonattackinterval"])
        ,("Typhoon", _typhoonMainRow,
            ["typhoonprotounit", "typhoonspawndelayseconds", "typhoonattackdelayseconds",
                "typhoonattackinterval", "typhoonradiusstart", "typhoonradiusend",
                "typhoonradiusexpansionseconds", "rainprotounit"])
        ,("Frost Freeze", _frostFreezeRow,
            ["freezeexpansiontime", "freezetransitiontime", "freezeholdtime",
                "fasteststriketime", "sloweststriketime", "armoramount"])
        ,("Frost VFX", _frostVfxRow, ["stormprotounit", "snowdriftprotounit"])
        ,("Frost Drift", _frostDriftRow, ["slowestdrifttime", "fastestdrifttime"])
        ,("Wither Wave", _witherMainRow, ["waveprotounit", "waveduration", "vfxinterval"])
        ,("Wither Kill Unit Type", _witherKillTypesRow, ["killunittype"])
        ,("Wither Damage", _witherDamageRow, ["damagepercentofmaxhp"])
        ,("Wither Creep Generators", _witherCreepGeneratorsRow, ["creepgenerators"])
        ,("Arcadian Placement", _arcadianPlacementRow, ["terrainplacementcheck"])
        ,("Arcadian Spawn", _arcadianSpawnRow,
            ["damagereceiverproto", "spawnproto", "blockerradius", "vfxproto"])
        ,("Arcadian VFX Grass", _arcadianGrassRow, ["vfxgrass"])
        ,("Underworld Eidolon", _underworldEidolonRow,
            ["eidolonmin", "maxeidoloncount", "eidolonstep", "eidolontype",
             "eidolonmaxhpmultiplier", "eidolonmaxspeedmultiplier"])
        ,("Underworld Spawn", _underworldSpawnRow, ["eidolonspawnradius", "eidolonspawnduration"])
        ,("Underworld VFX", _underworldVfxRow,
            ["vfxproto", "vfxeidolonspawnproto", "vfxforunitswithoutbirthanims", "vfxcurseproto"])
        ,("Underworld Lighting", _underworldLightingRow,
            ["lightset", "lightsetbuilduptime", "lightsetduration", "lightsetshutdowntime"])
        ,("Underworld Tech Effect", _underworldTechEffectRow,
            ["durationtecheffect", "onlyapplydurationtecheffectiftechactive"])
        ,("Underworld Sudden Death", _underworldSuddenDeathRow, ["suddendeathhpmultiplier"])
        ,("Power Clone Slot", _powerCloneSlotRow, ["powerslot"])
        ,("Power Clone VFX", _powerCloneVfxRow, ["placementvfx"])
        ,("Duplicate Replacement", _powerCloneReplacementRow, ["duplicatetargetreplacement"])
        ,("Fimbul Wolf", _fimbulWolfRow,
            ["wolfproto", "wolfcountsoftcap", "wolfcounthardcap", "wolfvfx"])
        ,("Fimbul Main Pack", _fimbulMainPackRow,
            ["packsize", "mainpacktargettype", "packspawnradius", "numextramainpackspawns"])
        ,("Fimbul Secondary Pack", _fimbulSecondaryPackRow,
            ["minipacksize", "minipacktargettype", "minpacktargetspawnradius", "maxpacktargetspawnradius"])
        ,("Fimbul Wander", _fimbulWanderRow, ["packwanderdistance", "maxpackwanderdistance"])
        ,("Fimbul Target system", _fimbulTargetSystemRow,
            ["packscaninterval", "spawntargetevalradius", "packattacksearchradius"])
        ,("Fimbul Snow Drift", _fimbulSnowDriftRow, ["snowdrift"])
        ,("Fimbul Strike Time", _fimbulSoundStrikeRow,
            ["sloweststriketime", "fasteststriketime"])
        ,("Caged Unit", _cageMainRow, ["targetfloatheight", "killcagedafterend"])
        ,("Cage Kill Similar", _cageKillSimilarRow,
            ["killsimilartargetsinradius", "similarunittypes", "resourcevaluecap",
             "resourcevaluemultiplier"])
        ,("Cage VFX", _cageVfxRow,
            ["vfxarea", "vfxmaintarget", "vfxmaintargetbone", "vfxmaintargetimpact",
             "vfxmarkanim", "vfxmarkanimbone", "vfxadditionalimpact"])
        ,("Cage Soundset", _cageSoundRow, ["additionalimpactsoundset"])
        ,("Rain", _rainMainRow, ["rainspotproto", "sfx", "smallrainspotproto", "smallsfx"])
        ,("Rain Effects", _rainEffectsRow, ["effect"])
        ,("Effect In Area", _effectInAreaMainRow, ["repeatdelay", "areavfx"])
        ,("Effect In Area On Hit", _effectInAreaOnHitRow, ["onhiteffect"])
        ,("Switching Type", _switchingTypeRow, ["tiertype", "interval"])
        ,("Switching Progress", _switchingProgressRow,
            ["progresstype", "hidewheelview", "timerrolloverid", "skipfirstnotification"])
        ,("Switching VFX Attach", _switchingVfxAttachRow, ["modelattachproto"])
        ,("Switching Effects", _switchingEffectsRow, ["effects"])
        ,("New Moon Research", _newMoonResearchRow, ["maxfreeresearches", "freeresearchrate"])
        ,("New Moon VFX", _modifyVfxRow, ["modelattachment", "modelattachmentbone"])
        ,("Unit Modify VFX", _modifyVfxRow, ["vfxproto", "vfxattachbone", "birthvfx"])
        ,("Unit Modify", _unitModifyRow, ["unitmodify", "killoneffectend"])
        ,("Fox Charm Affected Number", _foxCharmAffectedNumberRow, ["ageadjustment"])
        ,("Fox Charm VFX", _foxCharmVfxRow, ["unitvfx", "targetunitvfx", "areavfx"])
        ,("CeaseFire VFX", _ceaseFireVfxRow, ["vfxproto", "attachvfxto"])
        ,("Prosperity VFX", _prosperityVfxRow, ["largesfxprotoid", "smallsfxprotoid"])
        ,("Prosperity Types", _prosperityTypesRow, ["abstracttype"])
        ,("Power Effects", _sharedEffectsRow, ["effect"])
        ,("Earth Monster Unit", _earthMonsterUnitRow,
            ["earthmonsterprotounit", "strikeaction", "crushaction"])
        ,("Earth Monster Strike", _earthMonsterStrikeRow,
            ["numberofadditionalstrikes", "secondsbetweenstrikes", "strikeunitlimit",
             "minimumgroupsize", "groupsearchradius", "previousstrikeavoidanceradius"])
        ,("Earth Monster Regurgitate", _earthMonsterRegurgitateRow,
            ["regurgitatetime", "regurgitateunitstime", "regurgitateheight",
             "regurgitateanim", "regurgitateanimtime"])
        ,("Earth Monster Chomp", _earthMonsterChompRow,
            ["chompthrowswallowtimer", "chompthrowheight"])
        ,("Earth Monster Swallow", _earthMonsterSwallowRow, ["swallowplayerrelation"])
        ,("Earth Monster Throw Radius", _earthMonsterThrowRadiusRow,
            ["throwupwardsradius", "throwoutwardsradius"])
        ,("Earth Monster Land Impact", _earthMonsterLandImpactRow, ["landimpactprotounit"])
        ,("Earth Monster Soundset", _earthMonsterSoundsRow,
            ["emergesoundset", "chompsoundset", "regurgitatesoundset"])
        ,("Meteor Unit", _meteorUnitRow, ["meteorprotounit"])
        ,("Meteor Strikes", _meteorStrikesRow,
            ["numberofstrikes", "numberofprioritystrikes", "minimumgroupsize", "groupsearchradius",
             "uselargestgroupbelowminsize", "secondaryaccuracy"])
        ,("Meteor Splash VFX", _meteorSplashRow, ["landimpactprotounit", "watersplashprotounit"])
        ,("Meteor Soundset", _meteorSoundsRow,
            ["firstapproachsound", "firststrikesoundset", "strikesoundset", "approachsound"])
        ,("Tornado Unit", _tornadoMainRow,
            ["tornadoprotounit", "damageinterval", "greaterdamageradius", "numberofwaypoints", "spiralsize"])
        ,("Tornado Funnel", _tornadoFunnelRow,
            ["funnelradius", "funnelheight", "excludefrompullingintofunnel"])
        ,("Debris Velocity", _tornadoDebrisRow,
            ["debrisxzvelocity", "debrisyvelocity", "debrisangularvelocity"])
    ];

    private static bool IsDefaultSpecificAttribute(string? powerType, string name)
        => PowerSchemaRegistry.IsDefaultSpecificGroup(powerType, name);
    private void PopulateSpecificFields(XElement entry)
    {
        var powerType = entry.Attribute("type")?.Value.Trim() ?? "";
        if (powerType.Length > 0)
            _powerTypeBox.Classes.Remove("invalid-field");
        var knownPowerType = PowerTypes.FirstOrDefault(value =>
            value.Equals(powerType, StringComparison.OrdinalIgnoreCase));
        _powerTypeBox.SelectedItem = knownPowerType;
        _powerTypeBox.Text = knownPowerType ?? powerType;
        _powerTypeBox.IsEnabled = IsModifiedMode;
        _openOtherSpecificAttributes.Clear();
        foreach (var (name, _, tags) in SpecificAttributeSpecs())
            if (HasOtherAttribute(entry, tags)) _openOtherSpecificAttributes.Add(name);
        _otherSpecificAttributePickerOpen = false;
        _tempestVfxOpen = new[] { "effectproto", "groundvfxproto", "groundimpactvfxproto", "icesheetvfxproto" }
            .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _throwExcludePullOpen = PowerCatalog.FindField(entry, "excludefrompullingintofunnel") != null;
        _throwExcludeThrowOpen = PowerCatalog.FindField(entry, "excludefromthrow") != null;
        _witherKillTypesOpen = entry.Elements().Any(element =>
            element.Name.LocalName.Equals("killunittype", StringComparison.OrdinalIgnoreCase));
        _cageKillSimilarOpen = new[] { "killsimilartargetsinradius", "similarunittypes",
            "resourcevaluecap", "resourcevaluemultiplier" }.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _cageVfxOpen = new[] { "vfxarea", "vfxmaintarget", "vfxmaintargetbone",
            "vfxmaintargetimpact", "vfxmarkanim", "vfxmarkanimbone", "vfxadditionalimpact" }
            .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _cageSoundOpen = PowerCatalog.FindField(entry, "additionalimpactsoundset") != null;
        _foxCharmVfxOpen = new[] { "unitvfx", "targetunitvfx", "areavfx" }
            .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        var isNewMoon = entry.Attribute("type")?.Value.Trim().Equals("NewMoon",
            StringComparison.OrdinalIgnoreCase) == true;
        _modifyVfxOpen = isNewMoon
            ? PowerCatalog.FindField(entry, "modelattachment") != null ||
              PowerCatalog.FindField(entry, "modelattachmentbone") != null
            : new[] { "vfxproto", "vfxattachbone", "birthvfx" }
                .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _cracksSoundBox.ItemsSource = GetSoundSetSuggestions();
        _cracksSoundBox.Text = PowerCatalog.FindField(entry, "cracksoundset")?.Value ?? "";
        _crackIntervalMinBox.Text = EditorNumericFieldStyle.FormatDisplay(
            PowerCatalog.FindField(entry, "crackintervalmin")?.Value);
        _crackIntervalMaxBox.Text = EditorNumericFieldStyle.FormatDisplay(
            PowerCatalog.FindField(entry, "crackintervalmax")?.Value);
        _timeFactorBox.Text = EditorNumericFieldStyle.FormatDisplay(
            PowerCatalog.FindField(entry, "timefactor")?.Value);
        var shockwaveVfx = PowerCatalog.FindField(entry, "shockwavevfx")?.Value.Trim() ?? "";
        _shockwaveVfxBox.SelectedItem = _protoUnitNames.FirstOrDefault(value =>
            value.Equals(shockwaveVfx, StringComparison.OrdinalIgnoreCase));
        _shockwaveVfxBox.Text = shockwaveVfx;
        foreach (var box in ShockwaveNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        PopulateStrictProtoBox(_treeUnitBox, PowerCatalog.FindField(entry, "treeproto")?.Value);
        PopulateStrictProtoBox(_treeEffectVfxBox, PowerCatalog.FindField(entry, "treeeffectproto")?.Value);
        foreach (var box in GaiaNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        PopulateStrictProtoBox(_effectVfxBox, PowerCatalog.FindField(entry, "effectproto")?.Value);
        PopulateStrictProtoBox(_vfxProtoBox, PowerCatalog.FindField(entry, "vfxproto")?.Value);
        foreach (var box in ValorTraitorChaosNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        foreach (var box in SpiderProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, SpiderProtoTag(box))?.Value);
        foreach (var box in SpiderNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        _humanDeathSoundBox.ItemsSource = GetSoundSetSuggestions();
        _humanDeathSoundBox.Text = PowerCatalog.FindField(entry, "humandeathsoundset")?.Value ?? "";
        foreach (var box in VortexProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, VortexProtoTag(box))?.Value);
        foreach (var box in VortexNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        var soundSuggestions = GetSoundSetSuggestions();
        _liftSoundBox.ItemsSource = soundSuggestions;
        _liftSoundBox.Text = PowerCatalog.FindField(entry, "liftsoundset")?.Value ?? "";
        _landSoundBox.ItemsSource = soundSuggestions;
        _landSoundBox.Text = PowerCatalog.FindField(entry, "landsoundset")?.Value ?? "";
        foreach (var box in TartarianProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, TartarianProtoTag(box))?.Value);
        foreach (var box in TartarianNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        foreach (var box in ImplodeProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, ImplodeProtoTag(box))?.Value);
        var implodeSounds = GetSoundSetSuggestions();
        foreach (var box in ImplodeSoundBoxes())
        {
            box.ItemsSource = implodeSounds;
            box.Text = PowerCatalog.FindField(entry, ImplodeSoundTag(box))?.Value ?? "";
        }
        var meteorSounds = GetSoundSetSuggestions();
        foreach (var box in MeteorSoundBoxes())
        {
            box.ItemsSource = meteorSounds;
            box.Text = PowerCatalog.FindField(entry, MeteorSoundTag(box))?.Value ?? "";
        }
        foreach (var box in ImplodeNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        foreach (var box in NewPowerProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, NewPowerProtoTag(box))?.Value);
        foreach (var box in NewPowerNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        PopulateStrictUnitOrTypeBox(_underworldEidolonTypeBox,
            PowerCatalog.FindField(entry, "eidolontype")?.Value);
        PopulateStrictTechBox(_underworldTechEffectBox,
            PowerCatalog.FindField(entry, "durationtecheffect")?.Value);
        PopulateStrictTechBox(_underworldOnlyIfTechBox,
            PowerCatalog.FindField(entry, "onlyapplydurationtecheffectiftechactive")?.Value);
        PopulateStrictUnitOrTypeBox(_arcadianPlacementBox,
            PowerCatalog.FindField(entry, "terrainplacementcheck")?.Value);
        PopulateStrictActionBox(_gustDamageActionBox,
            PowerCatalog.FindField(entry, "damagingprotoaction")?.Value);
        PopulateStrictActionBox(_divineAttackActionBox,
            PowerCatalog.FindField(entry, "attackaction")?.Value);
        PopulateStrictActionBox(_tempestAreaAttackActionBox,
            PowerCatalog.FindField(entry, "areaattackaction")?.Value);
        var useLargestGroup = PowerCatalog.FindField(entry, "uselargestgroupbelowminsize") != null;
        _meteorLargestGroupCheck.IsChecked = useLargestGroup;
        _meteorLargestGroupCheck.IsVisible = IsModifiedMode || useLargestGroup;
        _meteorLargestGroupCheck.IsEnabled = IsModifiedMode;
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox, _vibrantGenerateIntervalBox,
                     _vibrantRotateMinBox, _vibrantRotateMaxBox, _vibrantGrowthMinBox,
                     _vibrantGrowthMaxBox, _vibrantScaleMinBox, _vibrantScaleMaxBox,
                     _restorationHealSpeedBox, _restorationScanIntervalBox,
                     _droughtBurnTimeBox, _greatFloodWaveCountBox, _greatFloodWaveRadiusBox,
                     _greatFloodSpacingBox, _greatFloodInitialVelocityBox, _greatFloodFinalVelocityBox,
                     _greatFloodRampTimeBox, _greatFloodAttackIntervalBox,
                     _shiftingSandsFadeTimeBox, _shiftingSandsSpawnTimeBox,
                     _shiftingSandsScanIntervalBox, _shiftingSandsPlacementRadiusBox,
                     _locustSwarmCountBox, _locustSwarmRadiusBox, _locustWayPointCountBox,
                     _locustWayPointOffsetBox, _locustDamageIntervalBox,
                     _boltStrikeHeightBox, _boltStrikeMinOffsetBox, _boltStrikeMaxOffsetBox,
                     _pestilenceRofPenaltyBox, _pestilenceTrainRatePenaltyBox,
                     _pestilenceScanIntervalBox, _greatHuntFoodRatioBox })
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        var stealthLevel = PowerCatalog.FindField(entry, "stealthlevel")?.Value.Trim();
        _stealthLevelBox.SelectedIndex = stealthLevel == "1" ? 0 : stealthLevel == "2" ? 1 : -1;
        RefreshDamageTiers(entry);
        RefreshRootDamages(entry);
        RefreshVibrantTreeUnits(entry);
        _greatFloodAttackNameBox.Text = PowerCatalog.FindField(entry, "initialattackname")?.Value ?? "";
        RefreshDroughtTerrainCreep(entry);
        RefreshDroughtInitialDamages(entry);
        RefreshGreatFloodDamageCaps(entry);
        RefreshBronzeArmors(entry);
        RefreshGreatHuntMaxFoods(entry);
        RefreshSwapFields(entry);
        RefreshLureFields(entry);
        RefreshUndermineTargets(entry);
        _tempUnitSelectionEntries.Clear();
        foreach (var create in entry.Elements().Where(element =>
                     (element.Name.LocalName.Equals("createunit", StringComparison.OrdinalIgnoreCase) ||
                      element.Name.LocalName.Equals("createagespecificunit", StringComparison.OrdinalIgnoreCase)) &&
                     element.Value.Contains('|')))
            _tempUnitSelectionEntries.Add(create);
        RefreshTempUnitFields(entry);
        RefreshStampedeFields(entry);
        RefreshFimbulFields(entry);
        RefreshCageFields(entry);
        RefreshRainFields(entry);
        RefreshEffectInAreaFields(entry);
        RefreshSwitchingFields(entry);
        RefreshLegacyEffectPowerFields(entry);
        RefreshUnitModifyFields(entry);
        RefreshModifyVfxFields(entry);
        RefreshFoxCharmFields(entry);
        RefreshEarthMonsterFields(entry);
        RefreshSpecificAttributeRows();
        RefreshTartarianRevealerFields(entry);
        RefreshTempestFields(entry);
        RefreshLightningFields(entry);
        RefreshEarthquakeFields(entry);
        RefreshThunderBurstFields(entry);
        RefreshTyphoonFields(entry);
        RefreshFrostFields(entry);
        RefreshWitherFields(entry);
        RefreshArcadianFields(entry);
        RefreshUnderworldFields(entry);
        ArrangeTempestOptionalRows(entry);
        RefreshPowerCloneFields(entry);
        RefreshThrowExclusions(entry);
    }

    private void PopulateStrictProtoBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _protoUnitNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private void PopulateStrictActionBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _protoActionNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private void PopulateStrictUnitOrTypeBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _unitOrTypeNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private void PopulateStrictTechBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _techNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private IEnumerable<TextBox> ShockwaveNumberBoxes()
    {
        yield return _stunLengthBox;
        yield return _minThrowDistanceBox;
        yield return _maxThrowDistanceBox;
        yield return _minThrowHeightBox;
        yield return _maxThrowHeightBox;
        yield return _minThrowVelocityBox;
        yield return _maxThrowVelocityBox;
    }

    private IEnumerable<TextBox> GaiaNumberBoxes()
    {
        yield return _treeAmountBox;
        yield return _treeSpawnIntervalMinBox;
        yield return _treeSpawnIntervalMaxBox;
        yield return _growthRadiusStartBox;
        yield return _growthRadiusIncreaseRateBox;
    }

    private IEnumerable<TextBox> ValorTraitorChaosNumberBoxes()
    {
        yield return _damagePercentConversionBox;
        yield return _unitEffectTimeSecondsBox;
        yield return _sharedEffectTimeSecondsBox;
    }

    private IEnumerable<AutoCompleteBox> SpiderProtoBoxes()
    {
        yield return _spiderUnitBox;
        yield return _spiderEggUnitBox;
        yield return _spiderDoorBox;
        yield return _spiderWarnBox;
    }

    private string SpiderProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _spiderUnitBox) ? "spiderproto" :
           ReferenceEquals(box, _spiderEggUnitBox) ? "spidereggproto" :
           ReferenceEquals(box, _spiderDoorBox) ? "spiderdoorproto" : "spiderwarnproto";

    private IEnumerable<TextBox> SpiderNumberBoxes()
    {
        yield return _spiderAttackRangeBox;
        yield return _spiderSpawnOffsetBox;
        yield return _numberToCreateBox;
        yield return _separationDistanceBox;
        yield return _hatchTimeBox;
        yield return _hatchIntervalMinBox;
        yield return _hatchIntervalMaxBox;
        yield return _warnTimeBox;
        yield return _initialDelayBox;
    }

    private IEnumerable<AutoCompleteBox> VortexProtoBoxes()
    {
        yield return _vortexLandVfxBox;
        yield return _vortexStartVfxBox;
        yield return _vortexStartLinkedBox;
        yield return _vortexFinishLinkedBox;
    }

    private string VortexProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _vortexLandVfxBox) ? "vortexlandingproto" :
           ReferenceEquals(box, _vortexStartVfxBox) ? "vortexstartproto" :
           ReferenceEquals(box, _vortexStartLinkedBox) ? "vortexstartlinkedproto" :
           "vortexfinishlinkedproto";

    private IEnumerable<TextBox> VortexNumberBoxes()
    {
        yield return _unitEffectTimeBox;
        yield return _dropIntervalMinBox;
        yield return _dropIntervalMaxBox;
        yield return _maxRetryBox;
        yield return _maxUnitHeightBox;
    }

    private IEnumerable<AutoCompleteBox> TartarianProtoBoxes()
    {
        yield return _spawnProtoBox;
        yield return _tartarianRevealerBox;
        yield return _tartarianGateUnitBox;
        yield return _tartarianGateBirthBox;
        yield return _tartarianGateFlameBox;
    }

    private string TartarianProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _spawnProtoBox) ? "spawnproto" :
           ReferenceEquals(box, _tartarianRevealerBox) ? "revealerproto" :
           ReferenceEquals(box, _tartarianGateUnitBox) ? "tartariangateproto" :
           ReferenceEquals(box, _tartarianGateBirthBox) ? "gatebirthproto" : "gateflameproto";

    private IEnumerable<TextBox> TartarianNumberBoxes()
    {
        yield return _maxSpawnNumberBox;
        yield return _sinkHoleTimeBox;
        yield return _sinkHoleShakeBox;
        yield return _rumbleTimeBox;
        yield return _firstSpawnDelayBox;
        yield return _spawnIntervalMinBox;
        yield return _spawnIntervalMaxBox;
    }

    private IEnumerable<AutoCompleteBox> ImplodeProtoBoxes()
    {
        yield return _implodeSphereBox;
        yield return _implodeShockwaveBox;
        yield return _implodePullEffectBox;
    }

    private string ImplodeProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _implodeSphereBox) ? "sphereproto" :
           ReferenceEquals(box, _implodeShockwaveBox) ? "shockwaveproto" : "vfxpulleffectproto";

    private IEnumerable<AutoCompleteBox> ImplodeSoundBoxes()
    {
        yield return _implodeStartSoundBox;
        yield return _implodeExplodeSoundBox;
        yield return _implodeWindupSoundBox;
        yield return _implodeHitSoundBox;
    }

    private string ImplodeSoundTag(AutoCompleteBox box)
        => ReferenceEquals(box, _implodeStartSoundBox) ? "implodestartsoundset" :
           ReferenceEquals(box, _implodeExplodeSoundBox) ? "implodeexplodesoundset" :
           ReferenceEquals(box, _implodeWindupSoundBox) ? "implodeexplosionwindupsoundset" :
           "implodehitsoundset";

    private IEnumerable<AutoCompleteBox> MeteorSoundBoxes()
    {
        yield return _meteorFirstApproachSoundBox; yield return _meteorFirstStrikeSoundBox;
        yield return _meteorStrikeSoundBox; yield return _meteorApproachSoundBox;
    }

    private string MeteorSoundTag(AutoCompleteBox box)
        => ReferenceEquals(box, _meteorFirstApproachSoundBox) ? "firstapproachsound" :
           ReferenceEquals(box, _meteorFirstStrikeSoundBox) ? "firststrikesoundset" :
           ReferenceEquals(box, _meteorStrikeSoundBox) ? "strikesoundset" : "approachsound";

    private IEnumerable<TextBox> ImplodeNumberBoxes()
    {
        yield return _implodePullRadiusBox; yield return _implodeEscapeRadiusBox;
        yield return _implodeStrengthIncrementBox; yield return _implodeMaxStrengthBox;
        yield return _implodeOverkillIncrementBox; yield return _implodeStrengthAdjustBox;
        yield return _implodeUnitAccelerationBox; yield return _implodeWindupLengthBox;
        yield return _implodeExplosionRadiusBox; yield return _implodeAccumulationBox;
        yield return _implodeMaxAccumulatedBox; yield return _implodeExplosionDelayBox;
        yield return _implodeShockwaveVelocityBox; yield return _implodeSphereScaleBox;
        yield return _implodeSphereHeightBox; yield return _implodeHeightAdjustBox;
        yield return _implodeMovementDelayBox; yield return _implodeFloatAdjustBox;
        yield return _implodeDamageIntervalBox; yield return _implodeBaseShearIncrementBox;
        yield return _implodeShearMultiplierBox; yield return _implodeBaseShearMaxBox;
        yield return _implodeDelayMinBox; yield return _implodeDelayMaxBox;
        yield return _implodeEndDelayMinBox; yield return _implodeEndDelayMaxBox;
        yield return _implodeMinStartingBox; yield return _implodeMinPullingBox;
        yield return _implodeMinExplodingBox; yield return _implodeInnerRadiusBox;
        yield return _implodeMiddleRadiusBox; yield return _implodeOuterRadiusBox;
        yield return _implodeInnerAngularBox; yield return _implodeMiddleAngularBox;
        yield return _implodeOuterAngularBox; yield return _implodeInitialShakeDurationBox;
        yield return _implodeInitialShakeStrengthBox; yield return _implodeDeathShakeDurationBox;
        yield return _implodeDeathShakeStrengthBox; yield return _implodeExplosionShakeDurationBox;
        yield return _implodeExplosionShakeStrengthBox;
    }

    private IEnumerable<Control> ImplodeControls()
        => ImplodeProtoBoxes().Cast<Control>().Concat(ImplodeSoundBoxes()).Concat(ImplodeNumberBoxes());

    private IEnumerable<AutoCompleteBox> NewPowerProtoBoxes()
    {
        yield return _farmProtoBox; yield return _pSeedsFarmVfxBox; yield return _pSeedsFarmTargetVfxBox;
        yield return _wallProtoBox; yield return _vanishCastVfxBox; yield return _vanishTargetVfxBox;
        yield return _reappearVfxBox; yield return _rootedEnemyVfxBox; yield return _treeAreaVfxBox;
        yield return _forestTreeVfxBox; yield return _vibrantTreeVfxBox;
        yield return _restorationHealVfxBox; yield return _restorationAreaVfxBox;
        yield return _droughtLandVfxBox; yield return _droughtWaveUnitBox;
        yield return _greatFloodWaveUnitBox;
        yield return _shiftingSandsStartPortalBox; yield return _shiftingSandsDestinationPortalBox;
        yield return _locustSwarmUnitBox;
        yield return _boltStrikeUnitBox; yield return _boltSparksVfxBox; yield return _boltScorchVfxBox;
        yield return _pestilenceSmallVfxBox; yield return _pestilenceLargeVfxBox;
        yield return _teleportStartPortalBox; yield return _teleportDestinationPortalBox;
        yield return _greatHuntSpawnIfNoTargetBox; yield return _greatHuntVfxBox;
        yield return _spyIndicatorBox; yield return _curseVfxBox; yield return _gustUnitBox;
        yield return _gustDamagingUnitBox; yield return _divineAttackUnitBox; yield return _divineHitDecalBox;
        yield return _lureUnitBox; yield return _lureSpawnProtoBox; yield return _lureSpawnVfxBox;
        yield return _undermineAttackUnitBox;
        yield return _forestFireAttackUnitBox; yield return _forestFireEmbersUnitBox;
        yield return _forestFireVfxBox;
        yield return _tempestStrikeUnitBox; yield return _tempestLargeStrikeUnitBox;
        yield return _tempestEffectBox; yield return _tempestGroundBox;
        yield return _tempestImpactBox; yield return _tempestIceBox;
        yield return _lightningSparksBox; yield return _lightningGroundSparksBox;
        yield return _lightningScorchBox; yield return _lightningRainVfxBox;
        yield return _lightningCloudVfxBox;
        yield return _earthquakeAttackUnitBox; yield return _earthquakeDustSmallBox;
        yield return _earthquakeDustMediumBox; yield return _earthquakeDustLargeBox;
        yield return _thunderBurstStartingVfxBox; yield return _thunderBurstChargingVfxBox;
        yield return _thunderBurstBurstVfxBox;
        yield return _typhoonDragonUnitBox; yield return _typhoonUnitBox; yield return _typhoonRainUnitBox;
        yield return _frostStormBox; yield return _frostSnowDriftBox;
        yield return _witherWaveUnitBox; yield return _arcadianDamageReceiverBox;
        yield return _arcadianBlockerBox; yield return _arcadianVfxBox;
        yield return _underworldStartVfxBox; yield return _underworldEidolonSpawnVfxBox;
        yield return _underworldNoBirthVfxBox;
        yield return _underworldEffectVfxBox;
        yield return _powerClonePlacementVfxBox;
        yield return _fimbulWolfVfxBox;
        yield return _cageVfxAreaBox; yield return _cageVfxMainTargetBox;
        yield return _cageVfxMainImpactBox; yield return _cageVfxMarkAnimBox;
        yield return _cageVfxAdditionalImpactBox;
        yield return _rainLargeVfxBox; yield return _rainSmallVfxBox;
        yield return _effectInAreaAreaVfxBox;
        yield return _foxCharmUnitVfxBox; yield return _foxCharmTargetVfxBox;
        yield return _foxCharmAreaVfxBox;
        yield return _ceaseFireVfxBox; yield return _prosperityLargeVfxBox;
        yield return _prosperitySmallVfxBox;
        yield return _earthMonsterUnitBox; yield return _earthMonsterLandImpactBox;
        yield return _meteorUnitBox; yield return _meteorLandVfxBox; yield return _meteorWaterVfxBox;
        yield return _tornadoUnitBox;
    }

    private string NewPowerProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _farmProtoBox) ? "farmproto" :
           ReferenceEquals(box, _pSeedsFarmVfxBox) ? "pseedsfarmvfx" :
           ReferenceEquals(box, _pSeedsFarmTargetVfxBox) ? "pseedsfarmtargetvfx" :
           ReferenceEquals(box, _wallProtoBox) ? "wallproto" :
           ReferenceEquals(box, _vanishCastVfxBox) ? "vanishvfx" :
           ReferenceEquals(box, _vanishTargetVfxBox) ? "vanishunitvfx" :
           ReferenceEquals(box, _reappearVfxBox) ? "reappearunitvfx" :
           ReferenceEquals(box, _rootedEnemyVfxBox) ? "rootedenemyvfx" :
           ReferenceEquals(box, _treeAreaVfxBox) ? "treeareavfx" :
           ReferenceEquals(box, _forestTreeVfxBox) || ReferenceEquals(box, _vibrantTreeVfxBox) ? "treevfx" :
           ReferenceEquals(box, _restorationHealVfxBox) ? "healvfx" :
           ReferenceEquals(box, _restorationAreaVfxBox) ? "areavfx" :
           ReferenceEquals(box, _droughtLandVfxBox) ? "landprotounit" :
           ReferenceEquals(box, _droughtWaveUnitBox) || ReferenceEquals(box, _greatFloodWaveUnitBox)
               ? "waveprotounit" :
           ReferenceEquals(box, _shiftingSandsStartPortalBox) ? "inportalprotounit" :
           ReferenceEquals(box, _shiftingSandsDestinationPortalBox) ? "outportalprotounit" :
           ReferenceEquals(box, _locustSwarmUnitBox) ? "swarmprotounit" :
           ReferenceEquals(box, _boltStrikeUnitBox) ? "strikeproto" :
           ReferenceEquals(box, _boltSparksVfxBox) ? "sparksvfxproto" :
           ReferenceEquals(box, _boltScorchVfxBox) ? "scorchvfxproto" :
           ReferenceEquals(box, _pestilenceSmallVfxBox) ? "smallvfxproto" :
           ReferenceEquals(box, _pestilenceLargeVfxBox) ? "largevfxproto" :
           ReferenceEquals(box, _teleportStartPortalBox) ? "inportal" :
           ReferenceEquals(box, _teleportDestinationPortalBox) ? "outportal" :
           ReferenceEquals(box, _greatHuntSpawnIfNoTargetBox) ? "spawnproto" :
           ReferenceEquals(box, _greatHuntVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _spyIndicatorBox) ? "spyprotounit" :
           ReferenceEquals(box, _curseVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _gustUnitBox) ? "gustproto" :
           ReferenceEquals(box, _gustDamagingUnitBox) ? "damagingproto" :
           ReferenceEquals(box, _divineAttackUnitBox) ? "attackproto" :
           ReferenceEquals(box, _divineHitDecalBox) ? "decalproto" :
           ReferenceEquals(box, _lureUnitBox) ? "lureproto" :
           ReferenceEquals(box, _lureSpawnProtoBox) ? "spawnproto" :
           ReferenceEquals(box, _lureSpawnVfxBox) ? "spawnvfx" :
           ReferenceEquals(box, _undermineAttackUnitBox) ? "attackproto" :
           ReferenceEquals(box, _forestFireAttackUnitBox) ? "attackproto" :
           ReferenceEquals(box, _forestFireEmbersUnitBox) ? "embersproto" :
           ReferenceEquals(box, _forestFireVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _tempestStrikeUnitBox) ? "strikeproto" :
           ReferenceEquals(box, _tempestLargeStrikeUnitBox) ? "largestrikeproto" :
           ReferenceEquals(box, _tempestEffectBox) ? "effectproto" :
           ReferenceEquals(box, _tempestGroundBox) ? "groundvfxproto" :
           ReferenceEquals(box, _tempestImpactBox) ? "groundimpactvfxproto" :
           ReferenceEquals(box, _tempestIceBox) ? "icesheetvfxproto" :
           ReferenceEquals(box, _lightningSparksBox) ? "sparksvfxproto" :
           ReferenceEquals(box, _lightningGroundSparksBox) ? "groundsparksvfxproto" :
           ReferenceEquals(box, _lightningScorchBox) ? "scorchvfxproto" :
           ReferenceEquals(box, _lightningRainVfxBox) ? "rainvfxproto" :
           ReferenceEquals(box, _lightningCloudVfxBox) ? "cloudvfxproto" :
           ReferenceEquals(box, _earthquakeAttackUnitBox) ? "attackproto" :
           ReferenceEquals(box, _earthquakeDustSmallBox) ? "dustsmallvfxproto" :
           ReferenceEquals(box, _earthquakeDustMediumBox) ? "dustmediumvfxproto" :
           ReferenceEquals(box, _earthquakeDustLargeBox) ? "dustlargevfxproto" :
           ReferenceEquals(box, _thunderBurstStartingVfxBox) ? "startingvfx" :
           ReferenceEquals(box, _thunderBurstChargingVfxBox) ? "chargingvfx" :
           ReferenceEquals(box, _thunderBurstBurstVfxBox) ? "burstvfx" :
           ReferenceEquals(box, _typhoonDragonUnitBox) ? "dragonprotounit" :
           ReferenceEquals(box, _typhoonUnitBox) ? "typhoonprotounit" :
           ReferenceEquals(box, _typhoonRainUnitBox) ? "rainprotounit" :
           ReferenceEquals(box, _frostStormBox) ? "stormprotounit" :
           ReferenceEquals(box, _frostSnowDriftBox) ? "snowdriftprotounit" :
           ReferenceEquals(box, _witherWaveUnitBox) ? "waveprotounit" :
           ReferenceEquals(box, _arcadianDamageReceiverBox) ? "damagereceiverproto" :
           ReferenceEquals(box, _arcadianBlockerBox) ? "spawnproto" :
           ReferenceEquals(box, _arcadianVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _underworldStartVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _underworldEidolonSpawnVfxBox) ? "vfxeidolonspawnproto" :
           ReferenceEquals(box, _underworldNoBirthVfxBox) ? "vfxforunitswithoutbirthanims" :
           ReferenceEquals(box, _underworldEffectVfxBox) ? "vfxcurseproto" :
           ReferenceEquals(box, _powerClonePlacementVfxBox) ? "placementvfx" :
           ReferenceEquals(box, _fimbulWolfVfxBox) ? "wolfvfx" :
           ReferenceEquals(box, _cageVfxAreaBox) ? "vfxarea" :
           ReferenceEquals(box, _cageVfxMainTargetBox) ? "vfxmaintarget" :
           ReferenceEquals(box, _cageVfxMainImpactBox) ? "vfxmaintargetimpact" :
           ReferenceEquals(box, _cageVfxMarkAnimBox) ? "vfxmarkanim" :
           ReferenceEquals(box, _cageVfxAdditionalImpactBox) ? "vfxadditionalimpact" :
           ReferenceEquals(box, _rainLargeVfxBox) ? "sfx" :
           ReferenceEquals(box, _rainSmallVfxBox) ? "smallsfx" :
           ReferenceEquals(box, _effectInAreaAreaVfxBox) ? "areavfx" :
           ReferenceEquals(box, _foxCharmUnitVfxBox) ? "unitvfx" :
           ReferenceEquals(box, _foxCharmTargetVfxBox) ? "targetunitvfx" :
           ReferenceEquals(box, _foxCharmAreaVfxBox) ? "areavfx" :
           ReferenceEquals(box, _ceaseFireVfxBox) ? "vfxproto" :
           ReferenceEquals(box, _prosperityLargeVfxBox) ? "largesfxprotoid" :
           ReferenceEquals(box, _prosperitySmallVfxBox) ? "smallsfxprotoid" :
           ReferenceEquals(box, _earthMonsterUnitBox) ? "earthmonsterprotounit" :
           ReferenceEquals(box, _earthMonsterLandImpactBox) ? "landimpactprotounit" :
           ReferenceEquals(box, _meteorUnitBox) ? "meteorprotounit" :
           ReferenceEquals(box, _meteorLandVfxBox) ? "landimpactprotounit" :
           ReferenceEquals(box, _meteorWaterVfxBox) ? "watersplashprotounit" :
           ReferenceEquals(box, _tornadoUnitBox) ? "tornadoprotounit" :
           throw new ArgumentOutOfRangeException(nameof(box), "Unknown specific Protounit field.");

    private IEnumerable<TextBox> NewPowerNumberBoxes()
    {
        yield return _effectInAreaRepeatDelayBox;
        yield return _newMoonMaxFreeBox; yield return _newMoonRateBox;
        yield return _curseMaxResourcesBox; yield return _curseConvertRatioBox;
        yield return _curseSwapDelayBox; yield return _gustLengthBox; yield return _gustSpeedBox;
        yield return _gustMinFactorBox; yield return _gustMaxFactorBox; yield return _gustBlowTimeBox;
        yield return _gustTimeBox; yield return _gustThrowSizeBox; yield return _divineAttackDelayBox;
        yield return _divineDamageWidthBox; yield return _divineDamageDepthBox;
        yield return _divineCameraDelayBox;
        yield return _lureMinRangeBox; yield return _lureMaxRangeBox;
        yield return _lureAttractRateBox; yield return _lureVarianceBox;
        yield return _lureCapDistanceBox; yield return _undermineDamageRadiusBox;
        yield return _undermineAttackIntervalBox; yield return _undermineDeformBox;
        yield return _forestFireBurnLengthBox; yield return _forestFireFadeLengthBox;
        yield return _forestFireEmbersLifespanBox; yield return _forestFireTreeDestructionBox;
        yield return _forestFireTreeSearchRadiusBox; yield return _forestFireDamageSearchRadiusBox;
        yield return _tempestSlowestStrikeBox; yield return _tempestFastestStrikeBox;
        yield return _tempestStrikeHeightBox; yield return _tempestStrikeMinOffsetBox;
        yield return _tempestStrikeMaxOffsetBox; yield return _tempestCloudBuildStartBox;
        yield return _tempestCloudBuildEndBox; yield return _tempestCloudFadeStartBox;
        yield return _tempestCloudFadeEndBox; yield return _tempestSlowestDriftBox;
        yield return _tempestFastestDriftBox; yield return _tempestStrengthMinBox;
        yield return _tempestStrengthMaxBox; yield return _tempestPeriodMinBox;
        yield return _tempestPeriodMaxBox; yield return _tempestAmplitudeMinBox;
        yield return _tempestAmplitudeMaxBox; yield return _tempestAngleOffsetBox;
        yield return _tempestTreeDurationBox;
        yield return _lightningRainBuildStartBox; yield return _lightningRainBuildEndBox;
        yield return _lightningRainFadeStartBox; yield return _lightningRainFadeEndBox;
        yield return _earthquakeIntervalBox; yield return _earthquakeTreeAccuracyBox;
        yield return _earthquakeEpicenterRadiusBox; yield return _earthquakeMiddleRadiusBox;
        yield return _earthquakeOuterRadiusBox; yield return _earthquakeEpicenterDamageBox;
        yield return _earthquakeMiddleDamageBox; yield return _earthquakeOuterDamageBox;
        yield return _thunderBurstAmountBox; yield return _thunderBurstDelayBox;
        yield return _thunderBurstRadiusIncreaseBox; yield return _thunderBurstStunDurationBox;
        yield return _throwSizeLimitBox;
        yield return _typhoonDragonSpawnDelayBox; yield return _typhoonDragonAttackIntervalBox;
        yield return _typhoonSpawnDelayBox;
        yield return _typhoonAttackDelayBox; yield return _typhoonAttackIntervalBox;
        yield return _typhoonRadiusStartBox; yield return _typhoonRadiusEndBox;
        yield return _typhoonRadiusExpansionBox;
        yield return _frostExpansionBox; yield return _frostTransitionBox;
        yield return _frostDurationBox; yield return _frostStrikeMinBox;
        yield return _frostStrikeMaxBox; yield return _frostArmorBox;
        yield return _frostSlowestDriftBox; yield return _frostFastestDriftBox;
        yield return _witherDurationBox; yield return _witherVfxIntervalBox;
        yield return _witherDamageBox; yield return _witherCreepGeneratorsBox;
        yield return _arcadianBlockerDurationBox;
        yield return _underworldEidolonMinBox; yield return _underworldEidolonMaxBox;
        yield return _underworldEidolonStepBox; yield return _underworldMaxHpMultiplierBox;
        yield return _underworldMaxSpeedMultiplierBox; yield return _underworldSpawnRadiusBox;
        yield return _underworldSpawnDurationBox; yield return _underworldLightBuildUpBox;
        yield return _underworldLightDurationBox; yield return _underworldLightShutdownBox;
        yield return _underworldSuddenDeathBox;
        yield return _fimbulWolfSoftCapBox; yield return _fimbulWolfHardCapBox;
        yield return _fimbulMainPackSizeBox; yield return _fimbulMainPackRadiusBox;
        yield return _fimbulExtraSpawnsBox; yield return _fimbulSecondaryPackSizeBox;
        yield return _fimbulSecondaryPackRadiusBox; yield return _fimbulSecondaryPackMaxRadiusBox;
        yield return _fimbulWanderDistanceBox;
        yield return _fimbulMaxWanderDistanceBox; yield return _fimbulScanIntervalBox;
        yield return _fimbulEvalRadiusBox; yield return _fimbulSearchRadiusBox;
        yield return _fimbulSlowestStrikeBox; yield return _fimbulFastestStrikeBox;
        yield return _cageFloatHeightBox; yield return _cageResourceCapBox;
        yield return _earthMonsterAdditionalStrikesBox; yield return _earthMonsterStrikeIntervalBox;
        yield return _earthMonsterUnitLimitBox; yield return _earthMonsterMinGroupBox;
        yield return _earthMonsterGroupRadiusBox; yield return _earthMonsterAvoidRadiusBox;
        yield return _earthMonsterRegurgitateTimingBox; yield return _earthMonsterRegurgitateDurationBox;
        yield return _earthMonsterRegurgitateHeightBox; yield return _earthMonsterRegurgitateAnimDurationBox;
        yield return _earthMonsterSwallowTimerBox; yield return _earthMonsterChompHeightBox;
        yield return _earthMonsterThrowUpwardBox; yield return _earthMonsterThrowOutwardBox;
        yield return _meteorStandardStrikesBox; yield return _meteorPriorityStrikesBox;
        yield return _meteorMinGroupSizeBox; yield return _meteorGroupSearchRadiusBox;
        yield return _meteorSecondaryAccuracyBox;
        yield return _tornadoDamageIntervalBox; yield return _tornadoGreaterDamageRadiusBox;
        yield return _tornadoWaypointCountBox; yield return _tornadoSpiralSizeBox;
        yield return _tornadoFunnelRadiusBox; yield return _tornadoFunnelHeightBox;
        yield return _tornadoDebrisXzBox; yield return _tornadoDebrisYBox;
        yield return _tornadoDebrisAngularBox;
    }

    private IEnumerable<Control> NewPowerControls()
        => NewPowerProtoBoxes().Cast<Control>().Concat(new Control[]
        {
            _numberToTransformBox, _stealthLevelBox, _wallSegmentCountBox, _addDamageTierButton,
            _stayTimeBox, _rootDurationBox, _addRootDamageButton, _slowHealMultiplierBox,
            _vibrantGenerateIntervalBox, _vibrantRotateMinBox, _vibrantRotateMaxBox,
            _vibrantGrowthMinBox, _vibrantGrowthMaxBox, _vibrantScaleMinBox, _vibrantScaleMaxBox,
            _restorationHealSpeedBox, _restorationScanIntervalBox
            , _droughtBurnTimeBox, _greatFloodWaveCountBox, _greatFloodWaveRadiusBox,
            _greatFloodSpacingBox, _greatFloodInitialVelocityBox, _greatFloodFinalVelocityBox,
            _greatFloodRampTimeBox, _greatFloodAttackIntervalBox, _greatFloodAttackNameBox,
            _shiftingSandsFadeTimeBox, _shiftingSandsSpawnTimeBox,
            _shiftingSandsScanIntervalBox, _shiftingSandsPlacementRadiusBox,
            _locustSwarmCountBox, _locustSwarmRadiusBox, _locustWayPointCountBox,
            _locustWayPointOffsetBox, _locustDamageIntervalBox,
            _addDroughtInitialDamageButton, _addGreatFloodDamageCapButton,
            _boltStrikeHeightBox, _boltStrikeMinOffsetBox, _boltStrikeMaxOffsetBox,
            _pestilenceRofPenaltyBox, _pestilenceTrainRatePenaltyBox,
            _pestilenceScanIntervalBox, _greatHuntFoodRatioBox,
            _addBronzeArmorButton, _addGreatHuntMaxFoodButton, _gustDamageActionBox,
            _divineAttackActionBox, _addSwapEntryButton, _addSwapMinMaxButton,
            _removeSwapMinMaxButton, _swapFlagPicker
            , _lureAttractTypePicker, _addLureSpawnButton, _removeLureSpawnButton,
            _addLureAgeInfoButton, _addUndermineTargetButton
            , _tempestStrikeSoundBox, _tempestAreaAttackActionBox, _tempestRadialTargetingCheck,
            _addTempestAttackButton, _removeTempestAttackButton,
            _addTempestLargeStrikeButton, _removeTempestLargeStrikeButton,
            _addTempestLargeTimingButton, _tempestAvoidPicker, _tempestLargeSoundBox,
            _addTempestVfxButton, _addTempestEffectButton, _addTempestGroundButton,
            _addTempestImpactButton, _addTempestIceButton, _removeTempestEffectButton,
            _removeTempestGroundButton, _removeTempestImpactButton, _removeTempestIceButton,
            _addTempestCloudsButton,
            _removeTempestCloudsButton, _addTempestDriftButton, _removeTempestDriftButton,
            _addTempestTreeBendButton, _removeTempestTreeBendButton
            , _addFrostArmorButton, _removeFrostArmorButton,
            _addFrostDriftButton, _removeFrostDriftButton
            , _arcadianPlacementBox, _witherKillTypePicker,
            _addWitherKillTypesButton, _removeWitherKillTypesButton,
            _addWitherDamageButton, _removeWitherDamageButton,
            _addWitherCreepGeneratorsButton, _removeWitherCreepGeneratorsButton,
            _addArcadianVfxButton, _removeArcadianVfxButton, _addArcadianGrassButton
            , _underworldEidolonTypeBox, _underworldTechEffectBox, _underworldOnlyIfTechBox,
            _addUnderworldMultipliersButton, _removeUnderworldMultipliersButton,
            _addUnderworldVfxButton, _removeUnderworldVfxButton,
            _underworldLightSetBox, _addUnderworldLightingButton, _removeUnderworldLightingButton,
            _addUnderworldTechEffectButton, _removeUnderworldTechEffectButton,
            _addUnderworldOnlyIfTechButton, _removeUnderworldOnlyIfTechButton,
            _addUnderworldSuddenDeathButton, _removeUnderworldSuddenDeathButton
            , _powerCloneSlotBox, _powerCloneCopyCostCheck, _powerCloneCopyRepeatCostCheck,
            _powerCloneSeededCheck, _powerCloneReplacementPicker
            , _fimbulWolfPicker, _fimbulMainPackTargetBox, _fimbulSecondaryPackTargetBox,
            _addFimbulWolfVfxButton, _removeFimbulWolfVfxButton,
            _addFimbulExtraSpawnsButton, _removeFimbulExtraSpawnsButton,
            _addFimbulSnowDriftOptionalButton, _addFimbulSnowDriftButton
            , _cageKillAfterEndCheck, _cageSimilarTypePicker, _addCageKillSimilarButton,
            _removeCageKillSimilarButton, _addCageResourceMultiplierButton,
            _cageVfxMainTargetBoneBox, _cageVfxMarkAnimBoneBox,
            _addCageVfxButton, _removeCageVfxButton, _addCageVfxAreaButton,
            _removeCageVfxAreaButton, _addCageVfxMainTargetButton,
            _removeCageVfxMainTargetButton, _addCageVfxMainImpactButton,
            _removeCageVfxMainImpactButton, _addCageVfxMarkAnimButton,
            _removeCageVfxMarkAnimButton, _addCageVfxAdditionalImpactButton,
            _removeCageVfxAdditionalImpactButton, _addCageSoundButton,
            _removeCageSoundButton, _cageAdditionalImpactSoundBox
            , _rainLargeTargetBox, _rainSmallTargetBox, _addRainEffectButton,
            _addEffectInAreaRepeatDelayButton, _removeEffectInAreaRepeatDelayButton,
            _addEffectInAreaOnHitButton
            , _switchingTierTypeBox, _switchingIntervalBox, _switchingProgressTypeBox,
            _switchingTimerRolloverBox, _addSwitchingTimerRolloverButton,
            _removeSwitchingTimerRolloverButton, _switchingSkipFirstNotificationCheck,
            _switchingVfxAttachPicker, _addSwitchingVfxAttachButton,
            _removeSwitchingVfxAttachButton, _addSwitchingEffectContainerButton
            , _ceaseFireAttachBox, _prosperityTypePicker, _addSharedEffectButton
            , _addModifyVfxButton, _removeModifyVfxButton,
            _addModifyVfxTargetButton, _removeModifyVfxTargetButton,
            _addModifyVfxBirthButton, _removeModifyVfxBirthButton,
            _modifyVfxTargetBox, _modifyVfxBoneBox, _modifyVfxBirthBox,
            _unitModifyKillOnEffectEndCheck
            , _addFoxCharmAgeButton, _addFoxCharmVfxButton, _removeFoxCharmVfxButton
            , _earthMonsterStrikeActionBox, _earthMonsterCrushActionBox,
            _earthMonsterRegurgitateAnimBox, _earthMonsterSwallowRelationBox,
            _earthMonsterEmergeSoundBox, _earthMonsterChompSoundBox,
            _earthMonsterRegurgitateSoundBox, _addEarthMonsterRegurgitateButton,
            _removeEarthMonsterRegurgitateButton
            , _addLightningRainVfxButton, _removeLightningRainVfxButton,
            _addLightningCloudVfxButton, _removeLightningCloudVfxButton,
            _addLightningRainButton, _removeLightningRainButton
            , _addEarthquakeDustVfxButton, _removeEarthquakeDustVfxButton
            , _addTyphoonRainUnitButton, _removeTyphoonRainUnitButton
            , _addTartarianRevealerButton, _removeTartarianRevealerButton
            , _addThrowSizeLimitButton, _removeThrowSizeLimitButton
            , _meteorLargestGroupCheck, _throwExcludePullPicker, _throwExcludeThrowPicker,
            _addThrowExcludePullButton, _addThrowExcludeThrowButton,
            _removeThrowExcludePullButton, _removeThrowExcludeThrowButton
        }).Concat(NewPowerNumberBoxes()).Concat(MeteorSoundBoxes());

    private void RefreshFoxCharmFields(XElement entry)
    {
        _foxCharmAgeRows.Children.Clear();
        _foxCharmAgeFields.Clear();
        if (entry.Attribute("type")?.Value.Trim().Equals("FoxCharm",
                StringComparison.OrdinalIgnoreCase) != true)
            return;

        var adjustments = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase) &&
            FindAttribute(element, "affectedNumber") != null).ToList();
        foreach (var adjustment in adjustments)
        {
            var currentAge = adjustment.Value.Trim();
            var usedByOthers = adjustments.Where(other => !ReferenceEquals(other, adjustment))
                .Select(other => other.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ageOptions = StandardAges.Where(age => !usedByOthers.Contains(age) ||
                age.Equals(currentAge, StringComparison.OrdinalIgnoreCase)).ToList();
            if (currentAge.Length > 0 && !ageOptions.Contains(currentAge, StringComparer.OrdinalIgnoreCase))
                ageOptions.Add(currentAge);

            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = "Age", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var ageBox = new ComboBox
            {
                Width = 150, ItemsSource = ageOptions, IsEnabled = IsModifiedMode,
                SelectedItem = ageOptions.FirstOrDefault(age => age.Equals(currentAge,
                    StringComparison.OrdinalIgnoreCase)), Margin = new Thickness(0, 0, 14, 0)
            };
            row.Children.Add(ageBox);
            row.Children.Add(new TextBlock
            {
                Text = "Number", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var affectedNumber = FindAttribute(adjustment, "affectedNumber");
            var numberBox = new TextBox
            {
                Width = 90, Text = EditorNumericFieldStyle.FormatDisplay(affectedNumber?.Value),
                IsEnabled = IsModifiedMode
            };
            EditorNumericInputBehavior.AttachRule(numberBox, ProtoUnitNumericKind.SignedInteger);
            row.Children.Add(numberBox);
            if (IsModifiedMode)
                row.Children.Add(CreateTempUnitRemoveButton(() =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(adjustment);
                    RefreshFoxCharmFields(entry);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                }));

            ageBox.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || ageBox.SelectedItem is not string age ||
                    adjustment.Value.Trim().Equals(age, StringComparison.OrdinalIgnoreCase)) return;
                adjustment.Value = age;
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(adjustment, () => RefreshFoxCharmFields(entry));
            };
            numberBox.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode ||
                    !int.TryParse(numberBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out _)) return;
                var value = numberBox.Text!.Trim();
                var attribute = FindAttribute(adjustment, "affectedNumber");
                if (attribute?.Value == value || NumericTextEquals(attribute?.Value, value)) return;
                if (attribute == null) adjustment.SetAttributeValue("affectedNumber", value);
                else attribute.Value = value;
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            };
            _foxCharmAgeRows.Children.Add(row);
            _foxCharmAgeFields.Add((ageBox, numberBox, adjustment));
        }

        _addFoxCharmAgeButton.IsVisible = IsModifiedMode && adjustments.Count < StandardAges.Length;
        var hasVfx = new[] { "unitvfx", "targetunitvfx", "areavfx" }
            .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _foxCharmVfxOpen |= hasVfx;
        _addFoxCharmVfxButton.IsVisible = IsModifiedMode && !_foxCharmVfxOpen;
        _foxCharmVfxLabel.IsVisible = _foxCharmVfxOpen;
        _foxCharmVfxFields.IsVisible = _foxCharmVfxOpen;
        _removeFoxCharmVfxButton.IsVisible = IsModifiedMode && _foxCharmVfxOpen;
        _foxCharmUnitVfxFields.IsVisible = IsModifiedMode || PowerCatalog.FindField(entry, "unitvfx") != null;
        _foxCharmTargetVfxFields.IsVisible = IsModifiedMode ||
                                             PowerCatalog.FindField(entry, "targetunitvfx") != null;
        _foxCharmAreaVfxFields.IsVisible = IsModifiedMode || PowerCatalog.FindField(entry, "areavfx") != null;
    }

    private static XAttribute? FindAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static List<string> OptionsIncludingCurrent(IEnumerable<string> knownValues, string? current)
        => knownValues.Append(current?.Trim() ?? "")
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void AddFoxCharmAge_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("FoxCharm",
                StringComparison.OrdinalIgnoreCase) != true) return;
        var adjustments = _currentEntry.Elements().Where(element =>
            element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase) &&
            FindAttribute(element, "affectedNumber") != null).ToList();
        if (adjustments.Count >= StandardAges.Length) return;
        var used = adjustments.Select(element => element.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var age = StandardAges.FirstOrDefault(candidate => !used.Contains(candidate));
        if (age == null) return;
        var adjustment = PowerCatalog.AddRepeatedField(_currentEntry, "ageadjustment", age);
        adjustment.SetAttributeValue("affectedNumber", (adjustments.Count + 1).ToString(CultureInfo.InvariantCulture));
        RefreshFoxCharmFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddFoxCharmVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("FoxCharm",
                StringComparison.OrdinalIgnoreCase) != true) return;
        _foxCharmVfxOpen = true;
        RefreshFoxCharmFields(_currentEntry);
    }

    private void RemoveFoxCharmVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var changed = false;
        foreach (var field in _currentEntry.Elements().Where(element =>
                     new[] { "unitvfx", "targetunitvfx", "areavfx" }.Contains(
                         element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            changed = true;
        }
        _foxCharmVfxOpen = false;
        RefreshFoxCharmFields(_currentEntry);
        if (!changed) return;
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RefreshFimbulFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("Fimbulwinter",
                StringComparison.OrdinalIgnoreCase) != true)
            return;

        var wolves = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("wolfproto", StringComparison.OrdinalIgnoreCase)).ToList();
        var selectedWolves = wolves.Select(element => element.Value.Trim())
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _fimbulWolfPicker.ItemsSource = _protoUnitNames.Where(value => !selectedWolves.Contains(value)).ToList();
        _fimbulWolfPicker.SelectedItem = null;
        _fimbulWolfPicker.Text = "";
        _fimbulWolfPicker.IsVisible = IsModifiedMode && selectedWolves.Count < _protoUnitNames.Count;
        _fimbulWolfUnitFields.IsVisible = IsModifiedMode || selectedWolves.Count > 0;
        _fimbulWolfChips.Children.Clear();
        foreach (var wolf in wolves.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = wolf;
            _fimbulWolfChips.Children.Add(PowerEditorDynamicControls.CreateChip(wolf.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RefreshFimbulFields(entry);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }

        PopulateStrictUnitOrTypeBox(_fimbulMainPackTargetBox,
            PowerCatalog.FindField(entry, "mainpacktargettype")?.Value);
        PopulateStrictUnitOrTypeBox(_fimbulSecondaryPackTargetBox,
            PowerCatalog.FindField(entry, "minipacktargettype")?.Value);

        SetFimbulFieldVisibility(entry, _fimbulWolfSoftCapFields, "wolfcountsoftcap");
        SetFimbulFieldVisibility(entry, _fimbulWolfHardCapFields, "wolfcounthardcap");
        _fimbulWolfAmountLabel.IsVisible = _fimbulWolfSoftCapFields.IsVisible ||
                                           _fimbulWolfHardCapFields.IsVisible;
        SetFimbulFieldVisibility(entry, _fimbulMainPackSizeFields, "packsize");
        SetFimbulFieldVisibility(entry, _fimbulMainPackTargetFields, "mainpacktargettype");
        SetFimbulFieldVisibility(entry, _fimbulMainPackRadiusFields, "packspawnradius");
        SetFimbulFieldVisibility(entry, _fimbulSecondaryPackSizeFields, "minipacksize");
        SetFimbulFieldVisibility(entry, _fimbulSecondaryPackTargetFields, "minipacktargettype");
        SetFimbulFieldVisibility(entry, _fimbulSecondaryPackRadiusFields,
            "minpacktargetspawnradius", "maxpacktargetspawnradius");
        SetFimbulFieldVisibility(entry, _fimbulWanderDistanceFields, "packwanderdistance");
        SetFimbulFieldVisibility(entry, _fimbulMaxWanderDistanceFields, "maxpackwanderdistance");
        SetFimbulFieldVisibility(entry, _fimbulScanIntervalFields, "packscaninterval");
        SetFimbulFieldVisibility(entry, _fimbulEvalRadiusFields, "spawntargetevalradius");
        SetFimbulFieldVisibility(entry, _fimbulSearchRadiusFields, "packattacksearchradius");
        SetFimbulFieldVisibility(entry, _fimbulSlowestStrikeFields, "sloweststriketime");
        SetFimbulFieldVisibility(entry, _fimbulFastestStrikeFields, "fasteststriketime");

        var wolfVfxOpen = PowerCatalog.FindField(entry, "wolfvfx") != null;
        _fimbulWolfVfxFields.IsVisible = wolfVfxOpen &&
            (IsModifiedMode || !string.IsNullOrWhiteSpace(PowerCatalog.FindField(entry, "wolfvfx")?.Value));
        _addFimbulWolfVfxButton.IsVisible = IsModifiedMode && !wolfVfxOpen;
        _removeFimbulWolfVfxButton.IsVisible = IsModifiedMode && wolfVfxOpen;

        var extraSpawnsOpen = PowerCatalog.FindField(entry, "numextramainpackspawns") != null;
        _fimbulExtraSpawnsFields.IsVisible = extraSpawnsOpen &&
            (IsModifiedMode || !string.IsNullOrWhiteSpace(
                PowerCatalog.FindField(entry, "numextramainpackspawns")?.Value));
        _addFimbulExtraSpawnsButton.IsVisible = IsModifiedMode && !extraSpawnsOpen;
        _removeFimbulExtraSpawnsButton.IsVisible = IsModifiedMode && extraSpawnsOpen;

        RefreshFimbulSnowDrifts(entry);
    }

    private void SetFimbulFieldVisibility(XElement entry, Control fields, params string[] tags)
        => fields.IsVisible = IsModifiedMode || tags.Any(tag =>
            !string.IsNullOrWhiteSpace(PowerCatalog.FindField(entry, tag)?.Value));

    private void FimbulWolfPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _fimbulWolfPicker.SelectedItem is not string value ||
            !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase) ||
            _currentEntry.Elements().Any(element =>
                element.Name.LocalName.Equals("wolfproto", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            return;
        PowerCatalog.AddRepeatedField(_currentEntry, "wolfproto", value);
        RefreshFimbulFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddFimbulWolfVfx_Click(object? sender, RoutedEventArgs e)
        => AddFimbulOptionalField("wolfvfx", "");

    private void RemoveFimbulWolfVfx_Click(object? sender, RoutedEventArgs e)
        => RemoveFimbulOptionalField("wolfvfx");

    private void AddFimbulExtraSpawns_Click(object? sender, RoutedEventArgs e)
        => AddFimbulOptionalField("numextramainpackspawns", "0");

    private void RemoveFimbulExtraSpawns_Click(object? sender, RoutedEventArgs e)
        => RemoveFimbulOptionalField("numextramainpackspawns");

    private void AddFimbulOptionalField(string tag, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, tag, value);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        if (tag == "wolfvfx") _fimbulWolfVfxBox.Focus();
        else _fimbulExtraSpawnsBox.Focus();
    }

    private void RemoveFimbulOptionalField(string tag)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RefreshFimbulSnowDrifts(XElement entry)
    {
        _fimbulSnowDriftRows.Children.Clear();
        _fimbulSnowDriftFields.Clear();
        var drifts = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("snowdrift", StringComparison.OrdinalIgnoreCase)).ToList();
        var open = drifts.Count > 0;
        _fimbulSnowDriftLabel.IsVisible = open;
        _fimbulSnowDriftRows.IsVisible = open;
        _addFimbulSnowDriftOptionalButton.IsVisible = IsModifiedMode && !open;
        _addFimbulSnowDriftButton.IsVisible = IsModifiedMode && open;
        foreach (var drift in drifts) BuildFimbulSnowDriftEntry(drift);
    }

    private void BuildFimbulSnowDriftEntry(XElement element)
    {
        var targetValue = element.Attribute("target")?.Value ?? "";
        var driftValue = element.Value;
        if (!IsModifiedMode && string.IsNullOrWhiteSpace(targetValue) &&
            string.IsNullOrWhiteSpace(driftValue))
            return;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 4)
        };
        var targetLabel = new TextBlock
        {
            Text = "Target", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
        };
        row.Children.Add(targetLabel);
        var target = new AutoCompleteBox
        {
            Width = 200, MaxWidth = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 8, 0)
        };
        EditorAutoCompleteService.ConfigureStrict(target, _unitOrTypeNames,
            targetValue, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                element.SetAttributeValue("target", value.Length == 0 ? null : value);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        target.Text = targetValue;
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(target, _specificScroll);
        row.Children.Add(target);
        var driftLabel = new TextBlock
        {
            Text = "Drift", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
        };
        row.Children.Add(driftLabel);
        var drift = new AutoCompleteBox
        {
            Width = 200, MaxWidth = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode
        };
        EditorAutoCompleteService.ConfigureStrict(drift, _protoUnitNames, element.Value, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                element.Value = value;
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        drift.Text = element.Value;
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(drift, _specificScroll);
        row.Children.Add(drift);
        targetLabel.IsVisible = target.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(targetValue);
        driftLabel.IsVisible = drift.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(driftValue);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.Click += (_, _) =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                if (_currentEntry != null) RefreshFimbulSnowDrifts(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(remove);
        }
        _fimbulSnowDriftFields.Add((target, drift, element));
        _fimbulSnowDriftRows.Children.Add(row);
    }

    private void AddFimbulSnowDriftOptional_Click(object? sender, RoutedEventArgs e)
        => AddFimbulSnowDrift();

    private void AddFimbulSnowDrift_Click(object? sender, RoutedEventArgs e)
        => AddFimbulSnowDrift();

    private void AddFimbulSnowDrift()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "snowdrift", "");
        element.SetAttributeValue("target", "");
        RefreshFimbulSnowDrifts(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _fimbulSnowDriftFields.LastOrDefault().Target?.Focus();
    }

    private void RefreshCageFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("CageUnit",
                StringComparison.OrdinalIgnoreCase) != true)
            return;

        var killAfter = PowerCatalog.FindField(entry, "killcagedafterend")?.Value.Trim();
        _cageKillAfterEndCheck.IsChecked = killAfter == null ||
            !killAfter.Equals("false", StringComparison.OrdinalIgnoreCase);

        _cageKillSimilarLabel.IsVisible = _cageKillSimilarOpen;
        _cageKillSimilarFields.IsVisible = _cageKillSimilarOpen;
        _addCageKillSimilarButton.IsVisible = IsModifiedMode && !_cageKillSimilarOpen;
        _removeCageKillSimilarButton.IsVisible = IsModifiedMode && _cageKillSimilarOpen;

        var similarTypes = PowerCatalog.FindField(entry, "similarunittypes")?.Elements()
            .Where(element => element.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        var selectedTypes = similarTypes.Select(element => element.Value.Trim())
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cageSimilarTypePicker.ItemsSource = _unitTypeNames.Where(value => !selectedTypes.Contains(value)).ToList();
        _cageSimilarTypePicker.SelectedItem = null;
        _cageSimilarTypePicker.Text = "";
        _cageSimilarTypePicker.IsVisible = IsModifiedMode && _cageKillSimilarOpen &&
                                           selectedTypes.Count < _unitTypeNames.Count;
        _cageSimilarTypeChips.Children.Clear();
        foreach (var type in similarTypes.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = type;
            _cageSimilarTypeChips.Children.Add(PowerEditorDynamicControls.CreateChip(type.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    var container = captured.Parent;
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    if (container == null || !container.Elements().Any())
                        PowerCatalog.SetField(entry, "killsimilartargetsinradius", "false");
                    RefreshCageFields(entry);
                    MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }
        RefreshCageResourceMultipliers(entry);

        _cageVfxLabel.IsVisible = _cageVfxOpen;
        _cageVfxFields.IsVisible = _cageVfxOpen;
        _addCageVfxButton.IsVisible = IsModifiedMode && !_cageVfxOpen;
        _removeCageVfxButton.IsVisible = IsModifiedMode && _cageVfxOpen;
        foreach (var box in new[] { _cageVfxAreaBox, _cageVfxMainTargetBox,
                     _cageVfxMainImpactBox, _cageVfxMarkAnimBox, _cageVfxAdditionalImpactBox })
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, NewPowerProtoTag(box))?.Value);
        RefreshCageVfxOption(entry, _cageVfxAreaFields, _addCageVfxAreaButton,
            _removeCageVfxAreaButton, "vfxarea");
        RefreshCageVfxOption(entry, _cageVfxMainTargetFields, _addCageVfxMainTargetButton,
            _removeCageVfxMainTargetButton, "vfxmaintarget", "vfxmaintargetbone");
        RefreshCageVfxOption(entry, _cageVfxMainImpactFields, _addCageVfxMainImpactButton,
            _removeCageVfxMainImpactButton, "vfxmaintargetimpact");
        RefreshCageVfxOption(entry, _cageVfxMarkAnimFields, _addCageVfxMarkAnimButton,
            _removeCageVfxMarkAnimButton, "vfxmarkanim", "vfxmarkanimbone");
        RefreshCageVfxOption(entry, _cageVfxAdditionalImpactFields, _addCageVfxAdditionalImpactButton,
            _removeCageVfxAdditionalImpactButton, "vfxadditionalimpact");
        ReorderCageVfxControls();
        PopulateCageBoneBox(_cageVfxMainTargetBoneBox,
            PowerCatalog.FindField(entry, "vfxmaintargetbone")?.Value);
        PopulateCageBoneBox(_cageVfxMarkAnimBoneBox,
            PowerCatalog.FindField(entry, "vfxmarkanimbone")?.Value);

        _cageSoundLabel.IsVisible = _cageSoundOpen;
        _cageSoundFields.IsVisible = _cageSoundOpen;
        _addCageSoundButton.IsVisible = IsModifiedMode && !_cageSoundOpen;
        _removeCageSoundButton.IsVisible = IsModifiedMode && _cageSoundOpen;
        _cageAdditionalImpactSoundBox.ItemsSource = GetSoundSetSuggestions();
        _cageAdditionalImpactSoundBox.Text =
            PowerCatalog.FindField(entry, "additionalimpactsoundset")?.Value ?? "";
    }

    private void PopulateCageBoneBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _boneNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private void RefreshCageVfxOption(XElement entry, Control fields, Button add, Button remove,
        params string[] tags)
    {
        var open = tags.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        fields.IsVisible = _cageVfxOpen && open;
        add.IsVisible = IsModifiedMode && _cageVfxOpen && !open;
        remove.IsVisible = IsModifiedMode && _cageVfxOpen && open;
    }

    private void ReorderCageVfxControls()
    {
        Control[] ordered =
        [
            _cageVfxAreaFields, _cageVfxMainTargetFields, _cageVfxMainImpactFields,
            _cageVfxMarkAnimFields, _cageVfxAdditionalImpactFields,
            _addCageVfxAreaButton, _addCageVfxMainTargetButton, _addCageVfxMainImpactButton,
            _addCageVfxMarkAnimButton, _addCageVfxAdditionalImpactButton, _removeCageVfxButton
        ];
        foreach (var control in ordered) _cageVfxFields.Children.Remove(control);
        foreach (var control in ordered) _cageVfxFields.Children.Add(control);
    }

    private void CageKillAfterEnd_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("CageUnit",
                StringComparison.OrdinalIgnoreCase) != true)
            return;
        SetOrRemoveSpecificField("killcagedafterend",
            _cageKillAfterEndCheck.IsChecked == true ? "true" : "false");
    }

    private void AddCageKillSimilar_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "killsimilartargetsinradius", "false");
        _cageKillSimilarOpen = true;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveCageKillSimilar_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[] { "killsimilartargetsinradius", "similarunittypes",
                     "resourcevaluecap", "resourcevaluemultiplier" })
            foreach (var field in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList())
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _cageKillSimilarOpen = false;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void CageSimilarTypePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _cageSimilarTypePicker.SelectedItem is not string value ||
            !_unitTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase))
            return;
        var container = PowerCatalog.FindField(_currentEntry, "similarunittypes") ??
                        PowerCatalog.SetField(_currentEntry, "similarunittypes", "");
        if (container.Elements().Any(element =>
                element.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            return;
        AddCageNestedElement(container, new XElement("unittype", value));
        PowerCatalog.SetField(_currentEntry, "killsimilartargetsinradius", "true");
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static void AddCageNestedElement(XElement container, XElement element)
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
        if (trailing != null && string.IsNullOrWhiteSpace(trailing.Value))
            trailing.AddBeforeSelf(new XText("\n" + childIndentation), element);
        else
            container.Add(new XText("\n" + childIndentation), element,
                new XText("\n" + indentation));
    }

    private void RefreshCageResourceMultipliers(XElement entry)
    {
        _cageResourceMultiplierRows.Children.Clear();
        _cageResourceMultiplierFields.Clear();
        var multipliers = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("resourcevaluemultiplier", StringComparison.OrdinalIgnoreCase)).ToList();
        var used = multipliers.Select(element => element.Attribute("resourcetype")?.Value.Trim() ?? "")
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var element in multipliers)
        {
            var current = element.Attribute("resourcetype")?.Value.Trim() ?? "";
            var resourceOptions = OptionsIncludingCurrent(
                ProtoConstants.KnownResourceTypes.Where(value =>
                    value.Equals(current, StringComparison.OrdinalIgnoreCase) || !used.Contains(value)),
                current);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };
            var resource = new ComboBox
            {
                Width = 120, IsEnabled = IsModifiedMode,
                ItemsSource = resourceOptions,
                SelectedItem = resourceOptions.FirstOrDefault(value =>
                    value.Equals(current, StringComparison.OrdinalIgnoreCase))
            };
            var multiplier = new TextBox
            {
                Text = EditorNumericFieldStyle.FormatDisplay(element.Value),
                IsEnabled = IsModifiedMode, Margin = new Thickness(6, 0, 0, 0)
            };
            EditorNumericFieldStyle.ConfigureNumericTextBox(multiplier);
            resource.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || resource.SelectedItem is not string selected) return;
                if (element.Attribute("resourcetype")?.Value.Equals(selected,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                element.SetAttributeValue("resourcetype", selected);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(element, () => RefreshCageResourceMultipliers(entry));
            };
            multiplier.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode ||
                    !double.TryParse(multiplier.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var value) || !double.IsFinite(value)) return;
                if (NumericTextEquals(element.Value, multiplier.Text!.Trim())) return;
                element.Value = multiplier.Text!.Trim();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(resource); row.Children.Add(multiplier);
            if (IsModifiedMode)
            {
                var remove = PowerEditorDynamicControls.CreateRemoveButton();
                remove.Click += (_, _) =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                    RefreshCageResourceMultipliers(entry);
                    MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                };
                row.Children.Add(remove);
            }
            _cageResourceMultiplierFields.Add((resource, multiplier, element));
            _cageResourceMultiplierRows.Children.Add(row);
        }
        _addCageResourceMultiplierButton.IsVisible = IsModifiedMode && _cageKillSimilarOpen &&
            ProtoConstants.KnownResourceTypes.Any(resource => !used.Contains(resource));
    }

    private void AddCageResourceMultiplier_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var used = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("resourcevaluemultiplier", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("resourcetype")?.Value.Trim() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resource = ProtoConstants.KnownResourceTypes.FirstOrDefault(value => !used.Contains(value));
        if (resource == null) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "resourcevaluemultiplier", "0");
        element.SetAttributeValue("resourcetype", resource);
        RefreshCageResourceMultipliers(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddCageVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _cageVfxOpen = true;
        RefreshCageFields(_currentEntry);
    }

    private void RemoveCageVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[] { "vfxarea", "vfxmaintarget", "vfxmaintargetbone",
                     "vfxmaintargetimpact", "vfxmarkanim", "vfxmarkanimbone", "vfxadditionalimpact" })
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _cageVfxOpen = false;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddCageVfxOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, tag, "");
        if (tag == "vfxmaintarget") PowerCatalog.SetField(_currentEntry, "vfxmaintargetbone", "");
        if (tag == "vfxmarkanim") PowerCatalog.SetField(_currentEntry, "vfxmarkanimbone", "");
        _cageVfxOpen = true;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveCageVfxOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        foreach (var removeTag in tag switch
                 {
                     "vfxmaintarget" => new[] { "vfxmaintarget", "vfxmaintargetbone" },
                     "vfxmarkanim" => new[] { "vfxmarkanim", "vfxmarkanimbone" },
                     _ => [tag]
                 })
            if (PowerCatalog.FindField(_currentEntry, removeTag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _cageVfxOpen = true;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddCageSound_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "additionalimpactsoundset", "");
        _cageSoundOpen = true;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveCageSound_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "additionalimpactsoundset") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _cageSoundOpen = false;
        RefreshCageFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshLegacyEffectPowerFields(XElement entry)
    {
        var type = entry.Attribute("type")?.Value.Trim() ?? "";
        if (type.Equals("CeaseFire", StringComparison.OrdinalIgnoreCase))
        {
            PopulateStrictProtoBox(_ceaseFireVfxBox, PowerCatalog.FindField(entry, "vfxproto")?.Value);
            PopulateStrictUnitOrTypeBox(_ceaseFireAttachBox,
                PowerCatalog.FindField(entry, "attachvfxto")?.Value);
        }
        else if (type.Equals("Prosperity", StringComparison.OrdinalIgnoreCase))
        {
            PopulateStrictProtoBox(_prosperityLargeVfxBox,
                PowerCatalog.FindField(entry, "largesfxprotoid")?.Value);
            PopulateStrictProtoBox(_prosperitySmallVfxBox,
                PowerCatalog.FindField(entry, "smallsfxprotoid")?.Value);
            RefreshProsperityTypes(entry);
        }

        if (!IsSharedEffectPowerType(type)) return;
        _sharedEffectRows.Children.Clear();
        foreach (var effect in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
            BuildRainEffectCard(effect, _sharedEffectRows);
        _addSharedEffectButton.IsVisible = IsModifiedMode;
    }

    private void RefreshProsperityTypes(XElement entry)
    {
        var elements = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = elements.Select(element => element.Value.Trim()).Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _prosperityTypePicker.ItemsSource = _unitOrTypeNames.Where(value => !selected.Contains(value)).ToList();
        _prosperityTypePicker.SelectedItem = null;
        _prosperityTypePicker.Text = "";
        _prosperityTypePicker.IsVisible = IsModifiedMode && selected.Count < _unitOrTypeNames.Count;
        _prosperityTypeChips.Children.Clear();
        foreach (var element in elements.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = element;
            _prosperityTypeChips.Children.Add(PowerEditorDynamicControls.CreateChip(element.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RefreshProsperityTypes(entry);
                    MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }
    }

    private void ProsperityTypePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("Prosperity",
                StringComparison.OrdinalIgnoreCase) != true ||
            _prosperityTypePicker.SelectedItem is not string value ||
            !_unitOrTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase) ||
            _currentEntry.Elements().Any(element =>
                element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        PowerCatalog.AddRepeatedField(_currentEntry, "abstracttype", value);
        RefreshProsperityTypes(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static bool IsSharedEffectPowerType(string type)
        => PowerEffectFamilyController.IsSharedPowerType(type);

    private void RefreshCurrentEffectRows()
    {
        if (_currentEntry == null) return;
        if (_currentEntry.Attribute("type")?.Value.Trim().Equals("Rain",
                StringComparison.OrdinalIgnoreCase) == true)
            RefreshRainFields(_currentEntry);
        else if (IsEffectInAreaPower(_currentEntry))
            RefreshEffectInAreaFields(_currentEntry);
        else if (_currentEntry.Attribute("type")?.Value.Trim().Equals("SwitchingEffects",
                     StringComparison.OrdinalIgnoreCase) == true)
            RefreshSwitchingFields(_currentEntry);
        else
            RefreshLegacyEffectPowerFields(_currentEntry);
    }

    private void RefreshAfterSelectorEvent(XElement owner, Action refresh)
    {
        var entry = _currentEntry;
        var generation = _populateGeneration;

        void RefreshIfCurrent(bool containFailure)
        {
            if (generation != _populateGeneration || !ReferenceEquals(entry, _currentEntry) ||
                entry == null || !owner.AncestorsAndSelf().Any(element => ReferenceEquals(element, entry)))
                return;

            if (!containFailure)
            {
                refresh();
                return;
            }

            try
            {
                refresh();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _statusMessage.Text =
                    $"Unable to refresh Power '{PowerCatalog.GetName(entry)}': {exception.Message}";
            }
        }

        // Removing the selector's card while Avalonia is still dispatching its SelectionChanged
        // event can leave the dropdown presenter in an invalid state. Detached/headless editors do
        // not have a presenter, so keep their deterministic synchronous behavior for tests/audits.
        if (TopLevel.GetTopLevel(this) == null)
            RefreshIfCurrent(containFailure: false);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => RefreshIfCurrent(containFailure: true),
                Avalonia.Threading.DispatcherPriority.Input);
    }

    private void AddSharedEffect_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsSharedEffectPowerType(_currentEntry.Attribute("type")?.Value.Trim() ?? "")) return;
        AddDefaultPowerEffect(_currentEntry);
        RefreshLegacyEffectPowerFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshRainFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("Rain",
                StringComparison.OrdinalIgnoreCase) != true)
            return;

        PopulateStrictUnitTypeBox(_rainLargeTargetBox, PowerCatalog.FindField(entry, "rainspotproto")?.Value);
        PopulateStrictUnitTypeBox(_rainSmallTargetBox, PowerCatalog.FindField(entry, "smallrainspotproto")?.Value);
        _rainEffectRows.Children.Clear();
        foreach (var effect in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
            BuildRainEffectCard(effect, _rainEffectRows);
        _addRainEffectButton.IsVisible = IsModifiedMode;
    }

    private static bool IsEffectInAreaPower(XElement entry)
        => EffectInAreaFamilyController.IsPower(entry);

    private void RefreshEffectInAreaFields(XElement entry)
    {
        if (!IsEffectInAreaPower(entry)) return;

        var repeatDelay = PowerCatalog.FindField(entry, "repeatdelay");
        _effectInAreaRepeatDelayFields.IsVisible = repeatDelay != null;
        _addEffectInAreaRepeatDelayButton.IsVisible = IsModifiedMode && repeatDelay == null;
        _removeEffectInAreaRepeatDelayButton.IsVisible = IsModifiedMode && repeatDelay != null;

        _effectInAreaOnHitRows.Children.Clear();
        foreach (var effect in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase)))
            BuildEffectInAreaOnHitCard(effect);
        _addEffectInAreaOnHitButton.IsVisible = IsModifiedMode;
    }

    private void AddEffectInAreaRepeatDelay_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsEffectInAreaPower(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "repeatdelay", "0");
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _effectInAreaRepeatDelayBox.Focus();
    }

    private void RemoveEffectInAreaRepeatDelay_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsEffectInAreaPower(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "repeatdelay") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddEffectInAreaOnHit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsEffectInAreaPower(_currentEntry)) return;
        PowerCatalog.AddRepeatedField(_currentEntry, "onhiteffect", "");
        RefreshEffectInAreaFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void BuildEffectInAreaOnHitCard(XElement effect)
    {
        var typeValue = effect.Attribute("type")?.Value.Trim() ?? "";
        var supported = EffectInAreaFamilyController.IsSupportedOnHitType(typeValue);
        var typeBox = new AutoCompleteBox
        {
            Text = typeValue,
            Width = 170,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        };
        var activeCheck = new CheckBox
        {
            IsChecked = effect.Attribute("active")?.Value.Trim() is not ("0" or "false"),
            IsEnabled = IsModifiedMode,
            VerticalAlignment = VerticalAlignment.Center
        };
        var card = new OnHitEffectEditor(typeBox, activeCheck, !IsModifiedMode, supported,
            showActiveField: false);
        _effectInAreaOnHitRows.Children.Add(card);
        if (_invalidEffectTypes.Contains(effect)) typeBox.Classes.Add("invalid-field");

        EditorAutoCompleteService.ConfigureStrict(typeBox, ProtoConstants.KnownOnHitEffectTypes,
            typeValue, () => _isPopulating, preserveUnknownInitialValue: true,
            allowEmpty: true, deferSelectionCommit: true, selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
                _invalidEffectTypes.Remove(effect);
                effect.RemoveAttributes();
                effect.RemoveNodes();
                if (!string.IsNullOrWhiteSpace(value))
                    effect.SetAttributeValue("type", value.Trim());
                SeedEffectInAreaOnHitDefaults(effect, value);
                RefreshEffectInAreaFields(_currentEntry);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(typeBox, _specificScroll);

        if (card.RemoveButton != null)
        {
            card.RemoveButton.Margin = new Thickness(2, 0, 0, 0);
            card.RemoveButton.Click += (_, _) =>
            {
                if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(effect);
                RefreshEffectInAreaFields(_currentEntry);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            };
        }

        if (!supported)
        {
            card.Body.Children.Add(new TextBlock
            {
                Text = "Unsupported in editor for now; XML will be preserved.",
                Foreground = Brushes.LightGray,
                FontStyle = FontStyle.Italic
            });
            return;
        }
        if (string.IsNullOrWhiteSpace(typeValue)) return;

        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        card.Body.Children.Add(fields);
        var orderedAttributes = EffectInAreaOnHitAttributeOrder(typeValue)
            .Concat(effect.Attributes().Select(attribute => attribute.Name.LocalName))
            .Where(name => !name.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                           !name.Equals("active", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var attributeName in orderedAttributes)
            AddEffectInAreaOnHitAttributeField(fields, effect, attributeName);

        BuildEffectInAreaDamageRows(card.Body, effect, typeValue);
        BuildEffectInAreaModifyRows(card.Body, effect, typeValue);
        BuildEffectInAreaTargetRows(card.Body, effect, typeValue);
    }

    private static IEnumerable<string> EffectInAreaOnHitAttributeOrder(string type)
        => EffectInAreaFamilyController.GetOnHitAttributeOrder(type);

    private static void SeedEffectInAreaOnHitDefaults(XElement effect, string type)
        => EffectInAreaFamilyController.SeedOnHitDefaults(effect, type);

    private void AddEffectInAreaOnHitAttributeField(WrapPanel fields, XElement effect, string name)
    {
        var current = effect.Attribute(name)?.Value ?? "";
        var label = name.ToLowerInvariant() switch
        {
            "proto" => "Proto", "attachbone" => "Bone", "protoaction" => "Infect Action",
            "targetunittype" => "Target Unit Type", "anim" => "Animation", "duration" => "Duration",
            "delay" => "Delay", "radius" => "Force", "stacklimit" => "Stack", "rate" => "Rate",
            "prob" => "Probability", "globalprob" => "Global Probability",
            "playerrelation" => "Player Relation", "rewardtype" => "Resource", "amount" => "Amount",
            "relativity" => "Relativity", "byunittype" => "By Unit Type", "terraintype" => "Terrain",
            "freezetype" => "Freeze Type", "shadingtype" => "Shading Type", "fadetime" => "Fade Time",
            "factor" => "Factor", "freezeduration" => "Freeze Duration", "damage" => "Damage",
            "additive" => "Additive", "deadonly" => "Dead Only", "hidevfx" => "Hide VFX",
            "ignoredeadhuntables" => "Ignore Dead Huntables", "stealthonly" => "Stealth Only",
            _ => name
        };
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0) });

        var flag = name is "additive" or "deadonly" or "hidevfx" or "ignoredeadhuntables" or "stealthonly";
        if (flag)
        {
            var check = new CheckBox { IsChecked = effect.Attribute(name) != null,
                IsEnabled = IsModifiedMode, VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
                effect.SetAttributeValue(name, check.IsChecked == true ? "" : null);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            group.Children.Add(check);
            fields.Children.Add(group);
            return;
        }

        IReadOnlyList<string>? catalog = name.ToLowerInvariant() switch
        {
            "proto" => _protoUnitNames, "attachbone" => _boneNames, "protoaction" => _protoActionNames,
            "targetunittype" or "byunittype" => _unitOrTypeNames, "anim" => _animationNames,
            "playerrelation" => ["Enemy", "Ally", "Self", "Nature", "Any"],
            "rewardtype" => ProtoConstants.KnownResourceTypes, "relativity" => ["Set", "Add", "Multiply"],
            "terraintype" => ["Land", "Water"],
            "freezetype" => ProtoConstants.KnownOnHitEffectFreezeTypeDisplayNames,
            _ => null
        };
        if (catalog != null)
        {
            var box = new AutoCompleteBox { Width = 180, Text = current, IsEnabled = IsModifiedMode,
                FilterMode = AutoCompleteFilterMode.Contains };
            EditorAutoCompleteService.ConfigureStrict(box, catalog, current, () => _isPopulating,
                preserveUnknownInitialValue: true, allowEmpty: true, selectAllOnFirstClick: false,
                valueCommitted: value => CommitEffectInAreaOnHitAttribute(effect, name, value));
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            group.Children.Add(box);
        }
        else
        {
            var box = new TextBox { Width = 100, Text = EditorNumericFieldStyle.FormatDisplay(current),
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
            box.TextChanged += (_, _) => CommitEffectInAreaOnHitAttribute(effect, name, box.Text ?? "");
            group.Children.Add(box);
        }
        fields.Children.Add(group);
    }

    private void CommitEffectInAreaOnHitAttribute(XElement effect, string name, string value)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var normalized = value.Trim();
        var existing = effect.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (normalized.Length == 0 && existing == null) return;
        if (existing != null &&
            (existing.Value.Equals(normalized, StringComparison.Ordinal) ||
             NumericTextEquals(existing.Value, normalized))) return;
        effect.SetAttributeValue(name, normalized.Length == 0 ? null : normalized);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void BuildEffectInAreaDamageRows(StackPanel body, XElement effect, string effectType)
    {
        var damages = effect.Elements().Where(element =>
            element.Name.LocalName.Equals("damage", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!effectType.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase) && damages.Count == 0) return;
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock { Text = "Damage", FontWeight = FontWeight.Bold });
        var rows = new WrapPanel();
        section.Children.Add(rows);
        foreach (var damage in damages)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
                Margin = new Thickness(0, 0, 8, 4) };
            var currentDamageType = damage.Attribute("type")?.Value;
            var damageTypeOptions = OptionsIncludingCurrent(ProtoConstants.KnownDamageTypes, currentDamageType);
            var type = new ComboBox { Width = 110, ItemsSource = damageTypeOptions,
                SelectedItem = damageTypeOptions.FirstOrDefault(value => value.Equals(
                    currentDamageType, StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            type.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || type.SelectedItem is not string value) return;
                if (damage.Attribute("type")?.Value.Equals(value,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                damage.SetAttributeValue("type", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            var amount = new TextBox { Width = 80, Text = EditorNumericFieldStyle.FormatDisplay(damage.Value),
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(amount);
            amount.TextChanged += (_, _) =>
            {
                if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
                var value = amount.Text?.Trim() ?? "";
                if (damage.Value.Trim().Equals(value, StringComparison.Ordinal) ||
                    NumericTextEquals(damage.Value, value)) return;
                damage.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(type); row.Children.Add(amount);
            if (IsModifiedMode)
            {
                var remove = PowerEditorDynamicControls.CreateRemoveButton();
                remove.Margin = new Thickness(2, 0, 0, 0);
                remove.Click += (_, _) =>
                {
                    if (_currentEntry == null) return; XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(damage);
                    RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                };
                row.Children.Add(remove);
            }
            rows.Children.Add(row);
        }
        if (IsModifiedMode)
        {
            var add = PowerEditorDynamicControls.CreateOptionalButton("Add Damage");
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                if (_currentEntry == null) return;
                effect.Add(new XElement("damage", new XAttribute("type", "Hack"), "0"));
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            rows.Children.Add(add);
        }
        body.Children.Add(section);
    }

    private void BuildEffectInAreaTargetRows(StackPanel body, XElement effect, string effectType)
    {
        if (effectType.Equals("SelfModify", StringComparison.OrdinalIgnoreCase) ||
            effectType.Equals("SelfStealth", StringComparison.OrdinalIgnoreCase)) return;
        var section = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        BuildEffectInAreaTargetChipGroup(section, effect, "attacktype", "Include Type");
        BuildEffectInAreaTargetChipGroup(section, effect, "ignoretype", "Exclude Type");
        body.Children.Add(section);
    }

    private void BuildEffectInAreaModifyRows(StackPanel body, XElement effect, string effectType)
    {
        var supportsModify = effectType.Equals("StatModify", StringComparison.OrdinalIgnoreCase) ||
                             effectType.Equals("Boost", StringComparison.OrdinalIgnoreCase) ||
                             effectType.Equals("SelfModify", StringComparison.OrdinalIgnoreCase);
        var modifies = effect.Elements().Where(element =>
            element.Name.LocalName.Equals("modify", StringComparison.OrdinalIgnoreCase)).ToList();
        var ramps = effect.Elements().Where(element =>
            element.Name.LocalName.Equals("modifyramp", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!supportsModify && modifies.Count == 0 && ramps.Count == 0) return;

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock { Text = "Modifiers", FontWeight = FontWeight.Bold });
        var modifyRows = new WrapPanel();
        section.Children.Add(modifyRows);
        foreach (var modify in modifies)
            AddEffectInAreaModifyRow(modifyRows, effect, modify);
        if (IsModifiedMode)
        {
            var add = PowerEditorDynamicControls.CreateOptionalButton("Add Modifier");
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                if (_currentEntry == null) return;
                effect.Add(new XElement("modify", new XAttribute("type", "Damage"), "0"));
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            modifyRows.Children.Add(add);
        }

        if (ramps.Count > 0 || IsModifiedMode)
        {
            section.Children.Add(new TextBlock { Text = "Modifier Ramps", FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 3, 0, 0) });
            var rampRows = new WrapPanel();
            section.Children.Add(rampRows);
            foreach (var ramp in ramps)
                AddEffectInAreaModifyRampRow(rampRows, effect, ramp);
            if (IsModifiedMode)
            {
                var addRamp = PowerEditorDynamicControls.CreateOptionalButton("Add Modifier Ramp");
                addRamp.Margin = new Thickness(0, 0, 8, 4);
                addRamp.Click += (_, _) =>
                {
                    if (_currentEntry == null) return;
                    effect.Add(new XElement("modifyramp", new XAttribute("type", "Damage"),
                        new XAttribute("init", "0"), new XAttribute("final", "0")));
                    RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                };
                rampRows.Children.Add(addRamp);
            }
        }
        body.Children.Add(section);
    }

    private void AddEffectInAreaModifyRow(WrapPanel rows, XElement owner, XElement modify)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 12, 4) };
        var typeValue = modify.Attribute("type")?.Value.Trim() ?? "";
        var type = new AutoCompleteBox { Width = 170, Text = ProtoConstants.GetModifyTypeDisplayName(typeValue),
            IsEnabled = IsModifiedMode, FilterMode = AutoCompleteFilterMode.Contains };
        var displayTypes = ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName).ToList();
        EditorAutoCompleteService.ConfigureStrict(type, displayTypes, type.Text ?? "", () => _isPopulating,
            preserveUnknownInitialValue: true, allowEmpty: false, deferSelectionCommit: true,
            selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (_currentEntry == null || string.IsNullOrWhiteSpace(value)) return;
                var xmlValue = ProtoConstants.GetModifyTypeValue(value);
                if (modify.Attribute("type")?.Value.Equals(xmlValue,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                modify.SetAttributeValue("type", xmlValue);
                if (xmlValue is not ("DamageSpecific" or "ArmorSpecific"))
                    modify.SetAttributeValue("dmgtype", null);
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        row.Children.Add(CreateEffectInAreaLabeledGroup("Modify", type));

        var currentApply = modify.Attribute("applytype")?.Value ?? "Multiply";
        var applyOptions = OptionsIncludingCurrent(["Multiply", "Add", "Set"], currentApply);
        var apply = new ComboBox { Width = 110, ItemsSource = applyOptions,
            SelectedItem = applyOptions.First(value => value.Equals(
                currentApply, StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode,
            Tag = "applytype" };
        apply.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || apply.SelectedItem is not string value) return;
            var current = modify.Attribute("applytype")?.Value ?? "Multiply";
            if (current.Equals(value, StringComparison.OrdinalIgnoreCase)) return;
            modify.SetAttributeValue("applytype", value.Equals("Multiply", StringComparison.OrdinalIgnoreCase)
                ? null : value);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(CreateEffectInAreaLabeledGroup("Apply", apply));

        if (typeValue is "DamageSpecific" or "ArmorSpecific")
        {
            var damageTypes = typeValue.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase)
                ? ProtoConstants.KnownArmorTypes : ProtoConstants.KnownDamageTypes;
            var currentDamageType = modify.Attribute("dmgtype")?.Value;
            var damageOptions = OptionsIncludingCurrent(damageTypes, currentDamageType);
            var damage = new ComboBox { Width = 110, ItemsSource = damageOptions,
                SelectedItem = damageOptions.FirstOrDefault(value => value.Equals(
                    currentDamageType, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = IsModifiedMode };
            damage.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || damage.SelectedItem is not string value) return;
                if (modify.Attribute("dmgtype")?.Value.Equals(value,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                modify.SetAttributeValue("dmgtype", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(CreateEffectInAreaLabeledGroup("Damage Type", damage));
        }
        var amount = new TextBox { Width = 80, Text = EditorNumericFieldStyle.FormatDisplay(modify.Value),
            IsEnabled = IsModifiedMode };
        EditorNumericFieldStyle.ConfigureNumericTextBox(amount);
        amount.TextChanged += (_, _) =>
        {
            if (_isPopulating) return;
            var value = amount.Text?.Trim() ?? "";
            if (modify.Value.Trim().Equals(value, StringComparison.Ordinal) ||
                NumericTextEquals(modify.Value, value)) return;
            modify.Value = value;
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(CreateEffectInAreaLabeledGroup("Value", amount));
        var stack = new CheckBox { Content = "Stack", IsChecked = modify.Attribute("stack") != null,
            IsEnabled = IsModifiedMode, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        stack.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || (modify.Attribute("stack") != null) == (stack.IsChecked == true)) return;
            modify.SetAttributeValue("stack", stack.IsChecked == true ? "" : null);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(stack);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.Margin = new Thickness(2, 0, 0, 0);
            remove.Click += (_, _) =>
            {
                if (_currentEntry == null) return; XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(modify);
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(remove);
        }
        rows.Children.Add(row);
    }

    private void AddEffectInAreaModifyRampRow(WrapPanel rows, XElement owner, XElement ramp)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 12, 4) };
        var typeValue = ramp.Attribute("type")?.Value.Trim() ?? "";
        var type = new AutoCompleteBox { Width = 170, Text = ProtoConstants.GetModifyTypeDisplayName(typeValue),
            IsEnabled = IsModifiedMode, FilterMode = AutoCompleteFilterMode.Contains };
        var displayTypes = ProtoConstants.KnownModifyTypes.Select(ProtoConstants.GetModifyTypeDisplayName).ToList();
        EditorAutoCompleteService.ConfigureStrict(type, displayTypes, type.Text ?? "", () => _isPopulating,
            preserveUnknownInitialValue: true, allowEmpty: false, deferSelectionCommit: true,
            selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (_currentEntry == null || string.IsNullOrWhiteSpace(value)) return;
                var xmlValue = ProtoConstants.GetModifyTypeValue(value);
                if (ramp.Attribute("type")?.Value.Equals(xmlValue,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                ramp.SetAttributeValue("type", xmlValue);
                if (xmlValue is not ("DamageSpecific" or "ArmorSpecific")) ramp.SetAttributeValue("dmgtype", null);
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        row.Children.Add(CreateEffectInAreaLabeledGroup("Modify", type));
        if (typeValue is "DamageSpecific" or "ArmorSpecific")
        {
            var damageTypes = typeValue.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase)
                ? ProtoConstants.KnownArmorTypes : ProtoConstants.KnownDamageTypes;
            var currentDamageType = ramp.Attribute("dmgtype")?.Value;
            var damageOptions = OptionsIncludingCurrent(damageTypes, currentDamageType);
            var damage = new ComboBox { Width = 110, ItemsSource = damageOptions,
                SelectedItem = damageOptions.FirstOrDefault(value => value.Equals(
                    currentDamageType, StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            damage.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || damage.SelectedItem is not string value) return;
                if (ramp.Attribute("dmgtype")?.Value.Equals(value,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                ramp.SetAttributeValue("dmgtype", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(CreateEffectInAreaLabeledGroup("Damage Type", damage));
        }
        foreach (var (label, attribute) in new[] { ("Init Value", "init"), ("Final Value", "final") })
        {
            var box = new TextBox { Width = 80,
                Text = EditorNumericFieldStyle.FormatDisplay(ramp.Attribute(attribute)?.Value), IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
            box.TextChanged += (_, _) =>
            {
                if (_isPopulating) return;
                var value = box.Text?.Trim() ?? "";
                var existing = ramp.Attribute(attribute);
                if (value.Length == 0 && existing == null) return;
                if (existing != null &&
                    (existing.Value.Equals(value, StringComparison.Ordinal) ||
                     NumericTextEquals(existing.Value, value))) return;
                ramp.SetAttributeValue(attribute, value.Length == 0 ? null : value);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(CreateEffectInAreaLabeledGroup(label, box));
        }
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.Margin = new Thickness(2, 0, 0, 0);
            remove.Click += (_, _) =>
            {
                if (_currentEntry == null) return; XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(ramp);
                RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(remove);
        }
        rows.Children.Add(row);
    }

    private static StackPanel CreateEffectInAreaLabeledGroup(string label, Control control)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0) });
        group.Children.Add(control);
        return group;
    }

    private void BuildEffectInAreaTargetChipGroup(WrapPanel section, XElement effect,
        string attributeName, string label)
    {
        var host = new StackPanel { Width = 330, Spacing = 2, Margin = new Thickness(0, 0, 14, 4) };
        var targets = effect.Elements().Where(element =>
                element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(element.Attribute(attributeName)?.Value))
            .ToList();
        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pickerRow.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (IsModifiedMode)
        {
            var picker = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains };
            var selected = targets.Select(target => target.Attribute(attributeName)!.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            EditorAutoCompleteService.ConfigureStrict(picker,
                _unitOrTypeNames.Where(value => !selected.Contains(value)), "", () => _isPopulating,
                preserveUnknownInitialValue: false, allowEmpty: true, deferSelectionCommit: true,
                selectAllOnFirstClick: false,
                valueCommitted: value =>
                {
                    if (_currentEntry == null || string.IsNullOrWhiteSpace(value) ||
                        effect.Elements("target").Any(target => value.Equals(
                            target.Attribute(attributeName)?.Value, StringComparison.OrdinalIgnoreCase))) return;
                    effect.Add(new XElement("target", new XAttribute(attributeName, value.Trim())));
                    RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                });
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(picker, _specificScroll);
            pickerRow.Children.Add(picker);
        }
        host.Children.Add(pickerRow);
        var chips = new WrapPanel();
        foreach (var target in targets)
        {
            var captured = target;
            chips.Children.Add(PowerEditorDynamicControls.CreateChip(target.Attribute(attributeName)!.Value,
                IsModifiedMode ? () =>
                {
                    if (_currentEntry == null) return;
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RefreshEffectInAreaFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }
        host.Children.Add(chips); section.Children.Add(host);
    }

    private static bool IsSwitchingEffectsPower(XElement entry)
        => SwitchingEffectsFamilyController.IsPower(entry);

    private static string SwitchingStringId(string powerName, string kind, int index = 0)
        => SwitchingEffectsFamilyController.CreateStringId(powerName, kind, index);

    private static bool IsOwnedSwitchingStringId(string powerName, string id)
        => SwitchingEffectsFamilyController.IsOwnedStringId(powerName, id);

    private void RefreshSwitchingFields(XElement entry)
    {
        if (!IsSwitchingEffectsPower(entry)) return;
        var tierType = SwitchingEffectsFamilyController.GetTierType(entry);
        var tierOptions = OptionsIncludingCurrent(SwitchingEffectsFamilyController.TierTypes, tierType);
        _switchingTierTypeBox.ItemsSource = tierOptions;
        _switchingTierTypeBox.SelectedItem = tierOptions.First(value =>
            value.Equals(tierType, StringComparison.OrdinalIgnoreCase));
        _switchingIntervalFields.IsVisible = tierType.Equals("Time", StringComparison.OrdinalIgnoreCase);
        _switchingIntervalBox.Text = EditorNumericFieldStyle.FormatDisplay(
            PowerCatalog.FindField(entry, "interval")?.Value);

        var hidden = PowerCatalog.FindField(entry, "hidewheelview") != null;
        var progress = SwitchingEffectsFamilyController.GetProgressType(entry);
        var progressOptions = OptionsIncludingCurrent(SwitchingEffectsFamilyController.ProgressTypes, progress);
        _switchingProgressTypeBox.ItemsSource = progressOptions;
        _switchingProgressTypeBox.SelectedItem = progressOptions.First(value =>
            value.Equals(progress, StringComparison.OrdinalIgnoreCase));
        _switchingSkipFirstNotificationCheck.IsChecked =
            PowerCatalog.FindField(entry, "skipfirstnotification") != null;

        var timer = PowerCatalog.FindField(entry, "timerrolloverid");
        _switchingTimerRolloverFields.IsVisible = !hidden && timer != null;
        _addSwitchingTimerRolloverButton.IsVisible = IsModifiedMode && !hidden && timer == null;
        _removeSwitchingTimerRolloverButton.IsVisible = IsModifiedMode && !hidden && timer != null;
        _switchingTimerRolloverBox.Tag = new SwitchingStringBinding
        {
            Owner = entry, Tag = "timerrolloverid",
            ExpectedId = SwitchingStringId(PowerCatalog.GetName(entry), "PROG"), Field = timer
        };
        SetStringBoxTextWithoutEditing(_switchingTimerRolloverBox,
            timer == null ? "" : PendingOrId(timer.Value.Trim()));

        var targets = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("modelattachproto", StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = targets.Select(element => element.Value.Trim())
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _switchingVfxAttachOpen = _switchingVfxAttachOpen || targets.Count > 0;
        _addSwitchingVfxAttachButton.IsVisible = IsModifiedMode && !_switchingVfxAttachOpen;
        _switchingVfxAttachLabel.IsVisible = _switchingVfxAttachOpen;
        _switchingVfxAttachFields.IsVisible = _switchingVfxAttachOpen;
        _removeSwitchingVfxAttachButton.IsVisible = IsModifiedMode;
        _switchingVfxAttachPicker.ItemsSource = _unitOrTypeNames.Where(value => !selected.Contains(value)).ToList();
        _switchingVfxAttachPicker.SelectedItem = null;
        _switchingVfxAttachPicker.Text = "";
        _switchingVfxAttachPicker.IsVisible = IsModifiedMode && selected.Count < _unitOrTypeNames.Count;
        _switchingVfxAttachChips.Children.Clear();
        foreach (var target in targets.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = target;
            _switchingVfxAttachChips.Children.Add(PowerEditorDynamicControls.CreateChip(target.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RefreshSwitchingFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }

        _switchingStringFields.Clear();
        _switchingEffectContainers.Children.Clear();
        var effects = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
        var tooltipIndex = 0;
        for (var index = 0; index < effects.Count; index++)
            BuildSwitchingEffectContainer(effects[index], index, ref tooltipIndex, tierType);
        _addSwitchingEffectContainerButton.IsVisible = IsModifiedMode;
        // The main population path resolves these fields after all controls have been built.
        // Only interaction-driven rebuilds need their own resolution pass.
        if (!_isPopulating)
            _ = ResolveSwitchingStringsAfterRefreshAsync(entry, _populateGeneration);
    }

    private async Task ResolveSwitchingStringsAfterRefreshAsync(XElement entry, int generation)
    {
        try
        {
            await ResolveSwitchingStringsAsync(entry, generation);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _populateGeneration && ReferenceEquals(entry, _currentEntry))
                _statusMessage.Text =
                    $"Unable to refresh strings for Power '{PowerCatalog.GetName(entry)}': {exception.Message}";
        }
    }

    private string PendingOrId(string id)
        => _session.TryGetPendingString(id, out var pending) ? pending : id;

    private void BuildSwitchingEffectContainer(XElement container, int index, ref int tooltipIndex,
        string tierType)
    {
        var content = new StackPanel { Spacing = 6 };
        var header = new WrapPanel();
        AddSwitchingResolvedField(header, container, "Display Name", "displaynameid",
            SwitchingStringId(CurrentPowerName!, "NAME", index), width: 250);
        AddSwitchingIconField(header, container);
        content.Children.Add(header);

        var rollover = new WrapPanel();
        AddSwitchingResolvedField(rollover, container, "Rollover", "rolloverid",
            SwitchingStringId(CurrentPowerName!, "LR", index), multiline: true);
        var extras = PowerCatalog.FindField(container, "extratooltips");
        if (extras != null)
            foreach (var field in extras.Elements().Where(element =>
                         element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase)).ToList())
                AddSwitchingExtraTooltipField(rollover, extras, field, tooltipIndex++);
        if (IsModifiedMode)
        {
            var addExtra = PowerEditorDynamicControls.CreateOptionalButton("Extra Tooltips", container);
            addExtra.Margin = new Thickness(0, 0, 8, 4);
            addExtra.Click += AddSwitchingExtraTooltip_Click;
            rollover.Children.Add(addExtra);
        }
        content.Children.Add(rollover);

        var notification = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        AddSwitchingResolvedField(notification, container, "Notification Message", "notificationmessageid",
            SwitchingStringId(CurrentPowerName!, "NOTIF", index), multiline: true);
        AddSwitchingSoundField(notification, container);
        content.Children.Add(notification);

        if (tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase) ||
            tierType.Equals("CombatXP", StringComparison.OrdinalIgnoreCase))
            content.Children.Add(BuildSwitchingRequirementFields(container, tierType));

        var effectSection = new StackPanel { Spacing = 4 };
        effectSection.Children.Add(new TextBlock { Text = "Effect", Margin = new Thickness(0, 4, 0, 0) });
        var rows = new StackPanel { Spacing = 8 };
        foreach (var effect in container.Elements().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
            BuildRainEffectCard(effect, rows);
        effectSection.Children.Add(rows);
        if (IsModifiedMode)
        {
            var add = PowerEditorDynamicControls.CreateOptionalButton("Add Effect", container);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                AddDefaultPowerEffect(container); RefreshSwitchingFields(_currentEntry!);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            effectSection.Children.Add(add);
        }
        content.Children.Add(effectSection);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(content);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton(container);
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Click += async (_, _) =>
                await RunNonReentrantCommandAsync("remove Switching Effect",
                    () => RemoveSwitchingEffectContainerAsync(container));
            Grid.SetColumn(remove, 1); grid.Children.Add(remove);
        }
        var border = PowerEditorDynamicControls.CreateCardSurface(grid);
        _switchingEffectContainers.Children.Add(border);
    }

    private void AddSwitchingResolvedField(WrapPanel row, XElement owner, string label, string tag,
        string expectedId, bool multiline = false, double width = 240)
    {
        var field = PowerCatalog.FindField(owner, tag);
        if (field == null && !IsModifiedMode) return;
        var binding = new SwitchingStringBinding
            { Owner = owner, Tag = tag, ExpectedId = expectedId, Field = field };
        var box = new TextBox { Width = width, Tag = binding,
            Text = field == null ? "" : PendingOrId(field.Value.Trim()),
            IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 8, 4) };
        if (multiline)
        {
            box.MinHeight = 32;
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        }
        EditorTextFieldStyle.ConfigureTextBox(box);
        box.Width = width;
        box.TextChanged += SwitchingStringBox_TextChanged;
        _switchingStringFields.Add((box, binding));
        var segment = RainLabeledSegment(label, box);
        segment.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(segment);
    }

    private void AddSwitchingIconField(WrapPanel row, XElement owner)
    {
        var field = PowerCatalog.FindField(owner, "icon");
        if (field == null && !IsModifiedMode) return;
        var current = IconCatalog.ToResourceRelativePath(field?.Value.Trim() ?? "");
        current = ProtoEditorWindow.NormalizeIconCatalogValue(current, _iconPaths);
        var editor = new AssetPathEditor
        {
            Width = 240,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 8, 4)
        };
        editor.Configure(current, _iconPaths.Append(current), value =>
        {
            if (field?.Value.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
                return Task.CompletedTask;
            if (field == null) field = PowerCatalog.SetField(owner, "icon", value);
            else XmlTriviaPreserver.SetText(field, value);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            return Task.CompletedTask;
        });
        var segment = RainLabeledSegment("Icon", editor);
        segment.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(segment);
    }

    private void AddSwitchingSoundField(WrapPanel row, XElement owner)
    {
        var field = PowerCatalog.FindField(owner, "notificationsound");
        if (field == null && !IsModifiedMode) return;
        var current = field?.Value.Trim() ?? "";
        var box = CreateRainStrictSelector(GetSoundSetSuggestions().Append(current), current, value =>
        {
            if (field?.Value.Equals(value, StringComparison.OrdinalIgnoreCase) == true) return;
            if (field == null) field = PowerCatalog.SetField(owner, "notificationsound", value);
            else XmlTriviaPreserver.SetText(field, value);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }, "notificationsound", width: 220);
        var segment = RainLabeledSegment("Sound", box);
        segment.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(segment);
    }

    private Control BuildSwitchingRequirementFields(XElement owner, string tierType)
    {
        var row = new WrapPanel();
        if (tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase))
        {
            var field = PowerCatalog.FindField(owner, "resourcetierreq");
            var current = field?.Attribute("type")?.Value.Trim() ?? ProtoConstants.KnownResourceTypes[0];
            var resourceOptions = OptionsIncludingCurrent(ProtoConstants.KnownResourceTypes, current);
            var resource = new ComboBox { Width = 130, ItemsSource = resourceOptions,
                SelectedItem = resourceOptions.First(value =>
                    value.Equals(current, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 8, 4) };
            resource.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || resource.SelectedItem is not string value) return;
                if (field?.Attribute("type")?.Value.Equals(value,
                        StringComparison.OrdinalIgnoreCase) == true) return;
                field ??= PowerCatalog.SetField(owner, "resourcetierreq", "0");
                field.SetAttributeValue("type", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(RainLabeledSegment("Tier Requirement",
                CreateSwitchingIntegerBox(owner, "resourcetierreq", field)));
            row.Children.Add(RainLabeledSegment("Resource", resource));
        }
        else
        {
            var field = PowerCatalog.FindField(owner, "combatxptierreq");
            row.Children.Add(RainLabeledSegment("Tier Requirement",
                CreateSwitchingIntegerBox(owner, "combatxptierreq", field)));
        }
        return row;
    }

    private TextBox CreateSwitchingIntegerBox(XElement owner, string tag, XElement? field)
    {
        var box = new TextBox { Text = EditorNumericFieldStyle.FormatDisplay(field?.Value),
            IsEnabled = IsModifiedMode, Tag = field, Margin = new Thickness(0, 0, 8, 4) };
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        box.TextChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || !box.IsVisible ||
                !int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
                return;
            if (NumericTextEquals(field?.Value, box.Text!.Trim())) return;
            field ??= PowerCatalog.SetField(owner, tag, box.Text!.Trim());
            XmlTriviaPreserver.SetText(field, box.Text!.Trim()); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void AddSwitchingExtraTooltipField(WrapPanel row, XElement owner, XElement field, int index)
    {
        var binding = new SwitchingStringBinding { Owner = owner, Tag = "string",
            ExpectedId = SwitchingStringId(CurrentPowerName!, "TOOLTIP", index), Field = field };
        var box = new TextBox { Width = 220, Tag = binding, Text = PendingOrId(field.Value.Trim()),
            IsEnabled = IsModifiedMode };
        EditorTextFieldStyle.ConfigureTextBox(box);
        box.TextChanged += SwitchingStringBox_TextChanged;
        _switchingStringFields.Add((box, binding));
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4)
        };
        group.Children.Add(new TextBlock { Text = "Extra Tooltip", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0) });
        group.Children.Add(box);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton(field);
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.Margin = new Thickness(2, 0, 0, 0);
            remove.Click += async (_, _) =>
                await RunNonReentrantCommandAsync("remove Switching Effect tooltip",
                    () => RemoveSwitchingExtraTooltipAsync(owner, field));
            group.Children.Add(remove);
        }
        row.Children.Add(group);
    }

    private void SwitchingStringBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: SwitchingStringBinding binding } box) return;
        if (ConsumeProgrammaticStringBoxChange(box)) return;
        if (_isPopulating || !IsModifiedMode || _currentEntry == null) return;
        if (!box.IsVisible) return;
        if (binding.Tag.Equals("timerrolloverid", StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.FindField(_currentEntry, "hidewheelview") != null)
            return;
        if (!ReferenceEquals(binding.Owner, _currentEntry) &&
            !binding.Owner.Ancestors().Any(ancestor => ReferenceEquals(ancestor, _currentEntry)))
            return;
        if (binding.Field == null)
        {
            binding.Field = PowerCatalog.SetField(binding.Owner, binding.Tag, binding.ExpectedId);
            RefreshRawXmlFromCurrent();
        }
        var id = binding.Field.Value.Trim();
        _session.SetPendingString(id, box.Text ?? "", CurrentPowerName!);
        MarkCurrentDirty();
    }

    private async Task ResolveSwitchingStringsAsync(XElement entry, int generation)
    {
        if (!IsSwitchingEffectsPower(entry)) return;
        var fields = _switchingStringFields.ToList();
        var timer = PowerCatalog.FindField(entry, "timerrolloverid");
        var resolutions = await Task.WhenAll(fields.Select(async item =>
        {
            var id = item.Binding.Field?.Value.Trim() ?? "";
            return (item.Box, Text: id.Length == 0 ? "" : await ResolveStringTextAsync(id));
        }).Concat(timer == null ? [] : new[]
        {
            ResolveSwitchingTimerAsync(timer)
        }));
        if (generation != _populateGeneration || !ReferenceEquals(entry, _currentEntry)) return;
        foreach (var resolution in resolutions)
        {
            if (ReferenceEquals(resolution.Box, _switchingTimerRolloverBox) &&
                (!ReferenceEquals(timer, PowerCatalog.FindField(entry, "timerrolloverid")) ||
                 PowerCatalog.FindField(entry, "hidewheelview") != null))
                continue;
            SetStringBoxTextWithoutEditing(resolution.Box, resolution.Text);
        }
    }

    private async Task<(TextBox Box, string Text)> ResolveSwitchingTimerAsync(XElement field)
        => (_switchingTimerRolloverBox, await ResolveStringTextAsync(field.Value.Trim()));

    private void SwitchingTierType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _switchingTierTypeBox.SelectedItem is not string value || !IsSwitchingEffectsPower(_currentEntry)) return;
        var currentTier = PowerCatalog.FindField(_currentEntry, "tiertype")?.Value.Trim();
        if ((string.IsNullOrWhiteSpace(currentTier) && value.Equals("Time", StringComparison.OrdinalIgnoreCase)) ||
            currentTier?.Equals(value, StringComparison.OrdinalIgnoreCase) == true) return;
        SwitchingEffectsFamilyController.ApplyTierType(_currentEntry, value);
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwitchingInterval_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_switchingIntervalBox.IsVisible) return;
        var text = _switchingIntervalBox.Text?.Trim() ?? "";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value)) return;
        if (NumericTextEquals(PowerCatalog.FindField(_currentEntry, "interval")?.Value, text)) return;
        PowerCatalog.SetField(_currentEntry, "interval", text); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwitchingProgressType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _switchingProgressTypeBox.SelectedItem is not string value || !IsSwitchingEffectsPower(_currentEntry)) return;
        var currentProgress = SwitchingEffectsFamilyController.GetProgressType(_currentEntry);
        if (currentProgress.Equals(value, StringComparison.OrdinalIgnoreCase)) return;
        if (value.Equals("Hide", StringComparison.OrdinalIgnoreCase))
        {
            var timer = PowerCatalog.FindField(_currentEntry, "timerrolloverid");
            if (timer != null) RemoveOwnedSwitchingStrings(timer);
        }
        SwitchingEffectsFamilyController.ApplyProgressType(_currentEntry, value);
        RefreshSwitchingFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddSwitchingTimerRollover_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var id = SwitchingStringId(CurrentPowerName!, "PROG");
        var field = PowerCatalog.SetField(_currentEntry, "timerrolloverid", id);
        _session.SetPendingString(id, "", CurrentPowerName!);
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _switchingTimerRolloverBox.Focus();
    }

    private void RemoveSwitchingTimerRollover_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "timerrolloverid");
        if (field == null) return;
        RemoveOwnedSwitchingStrings(field);
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwitchingSkipFirstNotification_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "skipfirstnotification");
        if (_switchingSkipFirstNotificationCheck.IsChecked == true)
            PowerCatalog.SetField(_currentEntry, "skipfirstnotification", "");
        else if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddSwitchingVfxAttach_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _switchingVfxAttachOpen = true; RefreshSwitchingFields(_currentEntry);
    }

    private void RemoveSwitchingVfxAttach_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var field in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("modelattachproto", StringComparison.OrdinalIgnoreCase)).ToList())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _switchingVfxAttachOpen = false;
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwitchingVfxAttachPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _switchingVfxAttachPicker.SelectedItem is not string value ||
            !_unitOrTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        PowerCatalog.AddRepeatedField(_currentEntry, "modelattachproto", value);
        var entry = _currentEntry;
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        RefreshAfterSelectorEvent(entry, () => RefreshSwitchingFields(entry));
    }

    private void AddSwitchingEffectContainer_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var name = CurrentPowerName!;
        var type = _switchingTierTypeBox.SelectedItem as string ?? "Time";
        var creation = SwitchingEffectsFamilyController.AddEffectContainer(_currentEntry, name, type);
        foreach (var id in creation.StringIds)
        {
            _session.SetPendingString(id, "", name);
        }
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddSwitchingExtraTooltip_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            sender is not Button { Tag: XElement container }) return;
        var field = SwitchingEffectsFamilyController.AddExtraTooltip(
            _currentEntry, container, CurrentPowerName!);
        var id = field.Value.Trim();
        _session.SetPendingString(id, "", CurrentPowerName!);
        RefreshSwitchingFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveOwnedSwitchingStrings(XElement container, string? ownerName = null)
    {
        ownerName ??= CurrentPowerName;
        if (string.IsNullOrWhiteSpace(ownerName)) return;
        foreach (var id in SwitchingEffectsFamilyController.EnumerateOwnedStringIds(container, ownerName))
            ScheduleStringRemoval(id, ownerName);
    }

    private async Task RemoveSwitchingEffectContainerAsync(XElement container)
    {
        var entry = _currentEntry;
        if (entry == null || !CanEditProperties(entry) ||
            !container.Ancestors().Any(ancestor => ReferenceEquals(ancestor, entry))) return;
        var powerName = PowerCatalog.GetName(entry);
        RemoveOwnedSwitchingStrings(container, powerName);
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(container);
        await ReindexSwitchingStringIdsAsync(entry);
        _session.MarkDirty(powerName);
        SetDirty(true);
        if (!ReferenceEquals(entry, _currentEntry)) return;
        RefreshSwitchingFields(entry);
        RefreshRawXmlFromCurrent();
    }

    private async Task RemoveSwitchingExtraTooltipAsync(XElement owner, XElement field)
    {
        var entry = _currentEntry;
        if (entry == null || !CanEditProperties(entry) ||
            !field.Ancestors().Any(ancestor => ReferenceEquals(ancestor, entry)) ||
            !ReferenceEquals(field.Parent, owner)) return;
        var powerName = PowerCatalog.GetName(entry);
        RemoveOwnedSwitchingStrings(field, powerName);
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        if (!owner.Elements().Any()) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(owner);
        await ReindexSwitchingStringIdsAsync(entry);
        _session.MarkDirty(powerName);
        SetDirty(true);
        if (!ReferenceEquals(entry, _currentEntry)) return;
        RefreshSwitchingFields(entry);
        RefreshRawXmlFromCurrent();
    }

    private async Task ReindexSwitchingStringIdsAsync(XElement entry)
    {
        var powerName = PowerCatalog.GetName(entry);
        var tooltipIndex = 0;
        var effects = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
        for (var index = 0; index < effects.Count; index++)
        {
            foreach (var (tag, kind) in new[]
                     { ("displaynameid", "NAME"), ("rolloverid", "LR"),
                       ("notificationmessageid", "NOTIF") })
            {
                var field = PowerCatalog.FindField(effects[index], tag);
                if (field != null) await RenameOwnedSwitchingStringAsync(field,
                    SwitchingStringId(powerName, kind, index), powerName);
            }
            var extras = PowerCatalog.FindField(effects[index], "extratooltips");
            if (extras == null) continue;
            foreach (var field in extras.Elements().Where(element =>
                         element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase)))
                await RenameOwnedSwitchingStringAsync(field,
                    SwitchingStringId(powerName, "TOOLTIP", tooltipIndex++), powerName);
        }
    }

    private async Task RenameOwnedSwitchingStringAsync(XElement field, string newId, string ownerName,
        string? newOwnerName = null)
    {
        var oldId = field.Value.Trim();
        if (!IsOwnedSwitchingStringId(ownerName, oldId) ||
            oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)) return;
        var textValue = await ResolveStringTextAsync(oldId);
        XmlTriviaPreserver.SetText(field, newId);
        _session.SetPendingString(newId, textValue, newOwnerName ?? ownerName);
        ScheduleStringRemoval(oldId, ownerName);
    }

    private async Task RenameSwitchingPowerStringsAsync(XElement entry, string oldName, string newName)
    {
        var timer = PowerCatalog.FindField(entry, "timerrolloverid");
        if (timer != null)
            await RenameOwnedSwitchingStringAsync(timer, SwitchingStringId(newName, "PROG"),
                oldName, newName);
        var tooltipIndex = 0;
        var effects = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)).ToList();
        for (var index = 0; index < effects.Count; index++)
        {
            foreach (var (tag, kind) in new[]
                     { ("displaynameid", "NAME"), ("rolloverid", "LR"),
                       ("notificationmessageid", "NOTIF") })
            {
                var field = PowerCatalog.FindField(effects[index], tag);
                if (field != null)
                    await RenameOwnedSwitchingStringAsync(field,
                        SwitchingStringId(newName, kind, index), oldName, newName);
            }
            var extras = PowerCatalog.FindField(effects[index], "extratooltips");
            if (extras == null) continue;
            foreach (var field in extras.Elements().Where(element =>
                         element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase)))
                await RenameOwnedSwitchingStringAsync(field,
                    SwitchingStringId(newName, "TOOLTIP", tooltipIndex++), oldName, newName);
        }
    }

    private static bool IsUnitModifyPowerType(string type)
        => UnitModifyFamilyController.IsPowerType(type);

    private bool IsNewMoonPowerType()
        => UnitModifyFamilyController.IsNewMoon(_currentEntry?.Attribute("type")?.Value.Trim());

    private string ModifyVfxTargetTag()
        => UnitModifyFamilyController.GetVfxTags(_currentEntry?.Attribute("type")?.Value.Trim()).Target;

    private string ModifyVfxBoneTag()
        => UnitModifyFamilyController.GetVfxTags(_currentEntry?.Attribute("type")?.Value.Trim()).Bone;

    private void RemoveModifyVfxField(string tag)
    {
        if (_currentEntry == null) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
    }

    private void RefreshModifyVfxFields(XElement entry)
    {
        if (!IsUnitModifyPowerType(entry.Attribute("type")?.Value.Trim() ?? "")) return;

        var powerType = entry.Attribute("type")?.Value.Trim();
        var newMoon = UnitModifyFamilyController.IsNewMoon(powerType);
        var vfxTags = UnitModifyFamilyController.GetVfxTags(powerType);
        var targetTag = vfxTags.Target;
        var boneTag = vfxTags.Bone;
        var target = PowerCatalog.FindField(entry, targetTag);
        var bone = PowerCatalog.FindField(entry, boneTag);
        var birth = newMoon ? null : PowerCatalog.FindField(entry, "birthvfx");
        var targetOpen = target != null || bone != null;
        var birthOpen = birth != null;

        PopulateStrictProtoBox(_modifyVfxTargetBox, target?.Value);
        var boneValue = bone?.Value.Trim() ?? "";
        _modifyVfxBoneBox.SelectedItem = _boneNames.FirstOrDefault(value =>
            value.Equals(boneValue, StringComparison.OrdinalIgnoreCase));
        _modifyVfxBoneBox.Text = boneValue;
        PopulateStrictProtoBox(_modifyVfxBirthBox, birth?.Value);

        _addModifyVfxButton.IsVisible = IsModifiedMode && !_modifyVfxOpen;
        _modifyVfxLabel.IsVisible = _modifyVfxOpen;
        _modifyVfxFields.IsVisible = _modifyVfxOpen;
        _modifyVfxTargetFields.IsVisible = targetOpen;
        _addModifyVfxTargetButton.IsVisible = IsModifiedMode && !targetOpen;
        _modifyVfxBirthFields.IsVisible = !newMoon && birthOpen;
        _addModifyVfxBirthButton.IsVisible = IsModifiedMode && !newMoon && !birthOpen;
        _removeModifyVfxTargetButton.IsVisible = IsModifiedMode && targetOpen;
        _removeModifyVfxBirthButton.IsVisible = IsModifiedMode && birthOpen;
        _removeModifyVfxButton.IsVisible = IsModifiedMode;
    }

    private void AddModifyVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsUnitModifyPowerType(_currentEntry.Attribute("type")?.Value.Trim() ?? "")) return;
        _modifyVfxOpen = true;
        RefreshModifyVfxFields(_currentEntry);
    }

    private void AddModifyVfxOption_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || sender is not Button button) return;
        if (button.Tag?.ToString() == "target")
        {
            PowerCatalog.SetField(_currentEntry, ModifyVfxTargetTag(), "");
            PowerCatalog.SetField(_currentEntry, ModifyVfxBoneTag(), "");
        }
        else if (button.Tag?.ToString() == "birth" && !IsNewMoonPowerType())
            PowerCatalog.SetField(_currentEntry, "birthvfx", "");
        RefreshModifyVfxFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveModifyVfxOption_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || sender is not Button button) return;
        if (button.Tag?.ToString() == "target")
        {
            RemoveModifyVfxField(ModifyVfxTargetTag());
            RemoveModifyVfxField(ModifyVfxBoneTag());
        }
        else if (button.Tag?.ToString() == "birth")
            RemoveModifyVfxField("birthvfx");
        RefreshModifyVfxFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveModifyVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in UnitModifyFamilyController.AllVfxTags)
            RemoveModifyVfxField(tag);
        _modifyVfxOpen = false;
        RefreshModifyVfxFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshUnitModifyFields(XElement entry)
    {
        _unitModifyRows.Children.Clear();
        var powerType = entry.Attribute("type")?.Value.Trim() ?? "";
        var isUnitModify = IsUnitModifyPowerType(powerType);
        var supportsKillOnEffectEnd = UnitModifyFamilyController.SupportsKillOnEffectEnd(powerType);
        var killOnEffectEnd = PowerCatalog.FindField(entry, "killoneffectend") != null;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _unitModifyKillOnEffectEndCheck.IsChecked = killOnEffectEnd; }
        finally { _isPopulating = wasPopulating; }
        _unitModifyKillOnEffectEndCheck.IsVisible = supportsKillOnEffectEnd &&
                                                     (IsModifiedMode || killOnEffectEnd);
        _unitModifyKillOnEffectEndCheck.IsEnabled = IsModifiedMode;
        if (isUnitModify)
        {
            foreach (var modify in entry.Elements().Where(element =>
                         element.Name.LocalName.Equals("unitmodify", StringComparison.OrdinalIgnoreCase)))
                BuildUnitModifyCard(modify);
        }
        _addUnitModifyButton.IsVisible = isUnitModify && IsModifiedMode;
    }

    private void UnitModifyKillOnEffectEnd_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            !UnitModifyFamilyController.SupportsKillOnEffectEnd(
                _currentEntry.Attribute("type")?.Value.Trim())) return;
        if (_unitModifyKillOnEffectEndCheck.IsChecked == true)
        {
            if (PowerCatalog.FindField(_currentEntry, "killoneffectend") != null) return;
            PowerCatalog.SetField(_currentEntry, "killoneffectend", "true");
        }
        else if (PowerCatalog.FindField(_currentEntry, "killoneffectend") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        else return;
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void BuildUnitModifyCard(XElement modify)
    {
        var (container, card) = PowerEditorDynamicControls.CreateCard();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };

        var applyValue = UnitModifyAttribute(modify, "apply")?.Value.Trim() ?? "";
        var applyOptions = new[] { "Add", "Multiply", "Set" }.Append(applyValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var apply = new ComboBox
        {
            Width = 130, ItemsSource = applyOptions, SelectedItem = applyOptions.FirstOrDefault(value =>
                value.Equals(applyValue, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 14, 4), Tag = "apply"
        };
        apply.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || apply.SelectedItem is not string value) return;
            SetUnitModifyAttribute(modify, "apply", value);
        };
        fields.Children.Add(RainLabeledSegment("Apply", apply));

        var modifyValue = modify.Value.Trim();
        var hasSpecificDamageType = modifyValue.Equals("DamageSpecific", StringComparison.OrdinalIgnoreCase) ||
                                    modifyValue.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase);
        var modifyDisplay = ProtoConstants.GetModifyTypeDisplayName(modifyValue);
        var modifyTypes = ProtoConstants.KnownModifyTypes
            .Select(ProtoConstants.GetModifyTypeDisplayName).Append(modifyDisplay);
        var modifyType = CreateRainStrictSelector(modifyTypes, modifyDisplay, selected =>
        {
            var normalized = string.IsNullOrWhiteSpace(selected)
                ? ""
                : ProtoConstants.GetModifyTypeValue(selected);
            if (modify.Value.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase)) return;
            modify.Value = normalized;
            if (normalized is "DamageSpecific" or "ArmorSpecific")
            {
                if (UnitModifyAttribute(modify, "damagetype") == null)
                    modify.SetAttributeValue("damagetype", "Hack");
            }
            else
                UnitModifyAttribute(modify, "damagetype")?.Remove();
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            RefreshAfterSelectorEvent(modify, () =>
            {
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
            });
        }, "modifytype");
        fields.Children.Add(RainLabeledSegment("Modify Type", modifyType));

        if (hasSpecificDamageType)
            fields.Children.Add(RainLabeledSegment("Damage Type",
                CreateUnitModifyDamageTypeEditor(modify, modifyValue)));

        var amount = new TextBox
        {
            Text = EditorNumericFieldStyle.FormatDisplay(UnitModifyAttribute(modify, "amount")?.Value),
            IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 14, 4), Tag = "amount"
        };
        EditorNumericFieldStyle.ConfigureNumericTextBox(amount);
        amount.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBox.TextProperty || !IsModifiedMode) return;
            SetUnitModifyAttribute(modify, "amount", amount.Text?.Trim() ?? "");
        };
        fields.Children.Add(RainLabeledSegment("Amount", amount));

        AddUnitModifyAgeEditor(modify, fields);

        var noStack = new CheckBox
        {
            Content = "No Stack", IsChecked = UnitModifyAttribute(modify, "nostack") != null,
            IsEnabled = IsModifiedMode, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 4), Tag = "nostack"
        };
        noStack.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            SetUnitModifyPresenceAttribute(modify, "nostack", noStack.IsChecked == true);
        };
        fields.Children.Add(noStack);

        var ignorePuid = new CheckBox
        {
            Content = "Ignore PU ID", IsChecked = UnitModifyAttribute(modify, "nostackignorepuid") != null,
            IsEnabled = IsModifiedMode, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 4), Tag = "nostackignorepuid"
        };
        ignorePuid.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            SetUnitModifyPresenceAttribute(modify, "nostackignorepuid", ignorePuid.IsChecked == true);
        };
        fields.Children.Add(ignorePuid);

        AddUnitModifyExcludeEditor(modify, fields);
        header.Children.Add(fields);

        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton("unitmodify");
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Margin = new Thickness(2, 0, 0, 0);
            remove.Click += (_, _) =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(modify);
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            Grid.SetColumn(remove, 1);
            header.Children.Add(remove);
        }

        card.Children.Add(header);
        _unitModifyRows.Children.Add(container);
    }

    private ComboBox CreateUnitModifyDamageTypeEditor(XElement modify, string modifyType)
    {
        var catalog = modifyType.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase)
            ? ProtoConstants.KnownArmorTypes
            : ProtoConstants.KnownDamageTypes;
        var current = UnitModifyAttribute(modify, "damagetype")?.Value.Trim() ?? "Hack";
        var options = catalog.Append(current).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox
        {
            Width = 120, ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 14, 4), Tag = "damagetype"
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || combo.SelectedItem is not string value) return;
            SetUnitModifyAttribute(modify, "damagetype", value);
        };
        return combo;
    }

    private void AddUnitModifyAgeEditor(XElement modify, WrapPanel fields)
    {
        var ageAttribute = UnitModifyAttribute(modify, "age");
        if (ageAttribute == null)
        {
            if (!IsModifiedMode) return;
            var add = PowerEditorDynamicControls.CreateOptionalButton("Age");
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                SetUnitModifyAttribute(modify, "age", StandardAges[0], refresh: false);
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            fields.Children.Add(add);
            return;
        }

        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        group.Children.Add(new TextBlock
        {
            Text = "Age", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
        });
        var ages = StandardAges.Append(ageAttribute.Value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var age = new ComboBox
        {
            Width = 150, ItemsSource = ages, SelectedItem = ages.FirstOrDefault(value =>
                value.Equals(ageAttribute.Value.Trim(), StringComparison.OrdinalIgnoreCase)),
            IsEnabled = IsModifiedMode, Tag = "age"
        };
        age.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || age.SelectedItem is not string value) return;
            SetUnitModifyAttribute(modify, "age", value);
        };
        group.Children.Add(age);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.Margin = new Thickness(2, 0, 0, 0);
            remove.Click += (_, _) =>
            {
                UnitModifyAttribute(modify, "age")?.Remove();
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            group.Children.Add(remove);
        }
        fields.Children.Add(group);
    }

    private void AddUnitModifyExcludeEditor(XElement modify, WrapPanel fields)
    {
        var excludeAttribute = UnitModifyAttribute(modify, "excludetypes");
        if (excludeAttribute == null)
        {
            if (!IsModifiedMode) return;
            var add = PowerEditorDynamicControls.CreateOptionalButton("Exclude Type");
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                SetUnitModifyAttribute(modify, "excludetypes", "", refresh: false, preserveEmpty: true);
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            fields.Children.Add(add);
            return;
        }

        var selected = excludeAttribute.Value.Split('|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var group = new WrapPanel { Margin = new Thickness(0, 0, 8, 4) };
        group.Children.Add(new TextBlock
        {
            Text = "Exclude Type", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4)
        });
        if (IsModifiedMode)
        {
            var picker = CreateRainStrictSelector(_unitOrTypeNames.Where(value =>
                !selected.Contains(value, StringComparer.OrdinalIgnoreCase)), "", value =>
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    selected.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
                selected.Add(value);
                SetUnitModifyAttribute(modify, "excludetypes", string.Join('|', selected), refresh: false);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(modify, () =>
                {
                    if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                });
            }, "excludetypes", width: 50);
            picker.Margin = new Thickness(0, 0, 4, 4);
            group.Children.Add(picker);
        }
        foreach (var value in selected)
        {
            var captured = value;
            group.Children.Add(PowerEditorDynamicControls.CreateChip(captured, IsModifiedMode ? () =>
            {
                var remaining = selected.Where(item =>
                    !item.Equals(captured, StringComparison.OrdinalIgnoreCase)).ToList();
                SetUnitModifyAttribute(modify, "excludetypes", string.Join('|', remaining),
                    refresh: false, preserveEmpty: true);
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } : null, readOnly: !IsModifiedMode));
        }
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton("excludetypes");
            remove.Margin = new Thickness(2, 0, 0, 4);
            remove.Click += (_, _) =>
            {
                UnitModifyAttribute(modify, "excludetypes")?.Remove();
                if (_currentEntry != null) RefreshUnitModifyFields(_currentEntry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            group.Children.Add(remove);
        }
        fields.Children.Add(group);
    }

    private static XAttribute? UnitModifyAttribute(XElement modify, string name)
        => modify.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void SetUnitModifyAttribute(XElement modify, string name, string value,
        bool refresh = true, bool preserveEmpty = false)
    {
        var attribute = UnitModifyAttribute(modify, name);
        if (string.IsNullOrWhiteSpace(value) && !preserveEmpty)
        {
            if (attribute == null) return;
            attribute.Remove();
        }
        else
        {
            if (attribute != null &&
                (attribute.Value.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                 NumericTextEquals(attribute.Value, value))) return;
            if (attribute == null) modify.SetAttributeValue(name, value);
            else attribute.Value = value;
        }
        if (!refresh) return;
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SetUnitModifyPresenceAttribute(XElement modify, string name, bool present)
    {
        var attribute = UnitModifyAttribute(modify, name);
        if (present)
        {
            if (attribute != null) return;
            modify.SetAttributeValue(name, "");
        }
        else
        {
            if (attribute == null) return;
            attribute.Remove();
        }
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddUnitModify_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !IsUnitModifyPowerType(_currentEntry.Attribute("type")?.Value.Trim() ?? "")) return;
        UnitModifyFamilyController.AddDefaultModify(_currentEntry);
        RefreshUnitModifyFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void PopulateStrictUnitTypeBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _unitTypeNames.FirstOrDefault(name =>
            name.Equals(text, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    private void BuildRainEffectCard(XElement effect, StackPanel effectRows)
    {
        var (container, card) = PowerEditorDynamicControls.CreateCard();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var headerFields = new WrapPanel { Orientation = Orientation.Horizontal };
        var typeValue = RainAttribute(effect, "type")?.Value.Trim() ?? "";
        var typeOptions = RainEffectTypes.Append(typeValue).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var type = CreateRainStrictSelector(typeOptions, typeValue, selected =>
        {
            if (string.IsNullOrWhiteSpace(selected)) return;
            _invalidEffectTypes.Remove(effect);
            if (selected.Equals(typeValue, StringComparison.OrdinalIgnoreCase)) return;
            PowerEffectFamilyController.ChangeType(effect, typeValue, selected);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            RefreshAfterSelectorEvent(effect, RefreshCurrentEffectRows);
        }, "type", width: 210);
        if (_invalidEffectTypes.Contains(effect)) type.Classes.Add("invalid-field");
        headerFields.Children.Add(RainLabeledSegment("Type", type));
        AddRainHeaderNotNatureCheckBox(effect, headerFields, typeValue);
        AddRainPresenceCheckBox(effect, headerFields, "No Revert", "norevert");
        AddRainPresenceCheckBox(effect, headerFields, "Hide Tooltip", "hidetooltip");
        header.Children.Add(headerFields);

        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Click += (_, _) =>
            {
                _invalidEffectTypes.Remove(effect);
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(effect);
                RefreshCurrentEffectRows();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            Grid.SetColumn(remove, 1);
            header.Children.Add(remove);
        }
        card.Children.Add(header);

        if (string.IsNullOrWhiteSpace(typeValue))
        {
            effectRows.Children.Add(container);
            return;
        }

        var details = new WrapPanel { Orientation = Orientation.Horizontal };
        var targetElement = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        if (typeValue.Equals("TechActivate", StringComparison.OrdinalIgnoreCase))
        {
            var target = CreateRainStrictSelector(_techNames, targetElement?.Value.Trim() ?? "", value =>
            {
                var current = EnsureRainTypedTarget(effect, "Tech", clearValueOnTypeChange: true);
                current.Value = value;
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }, "target");
            details.Children.Add(RainLabeledSegment("Tech", target));
        }
        else if (typeValue.Equals("Cost", StringComparison.OrdinalIgnoreCase))
        {
            AddRainCostTarget(effect, details, targetElement);
        }
        else if (RainEffectUsesPlayerTarget(typeValue))
        {
            details.Children.Add(RainLabeledSegment("Player", new TextBox
            {
                Width = 200, Text = "Player", IsEnabled = false,
                Margin = new Thickness(0, 0, 14, 4), Tag = "target"
            }));
        }
        else
        {
            var target = CreateRainStrictSelector(_unitOrTypeNames, targetElement?.Value.Trim() ?? "", value =>
            {
                var current = EnsureRainUnitTarget(effect);
                current.Value = value;
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }, "target");
            details.Children.Add(RainLabeledSegment("Unit", target));
        }

        BuildRainEffectLeadingFields(effect, typeValue, details);

        var hasStandardAmount = RainEffectUsesStandardAmount(typeValue);
        if (hasStandardAmount)
        {
            var currentRelativity = RainAttribute(effect, "relativity")?.Value.Trim() ?? "BasePercent";
            var relativityDisplays = RainRelativityDisplaysForType(typeValue, currentRelativity);
            var relativity = new ComboBox
            {
                ItemsSource = relativityDisplays,
                SelectedItem = relativityDisplays.Contains(RainRelativityToDisplay(currentRelativity),
                    StringComparer.OrdinalIgnoreCase)
                    ? RainRelativityToDisplay(currentRelativity)
                    : relativityDisplays[0],
                Width = 132,
                IsEnabled = IsModifiedMode,
                Margin = new Thickness(0, 0, 14, 4),
                Tag = "relativity"
            };
            relativity.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || relativity.SelectedItem is not string selected) return;
                SetRainAttribute(effect, "relativity", RainDisplayToRelativity(selected));
            };
            var amount = RainEffectUsesIntegerAmount(typeValue)
                ? CreateRainIntegerAttributeBox(effect, "amount")
                : CreateRainFloatAttributeBox(effect, "amount");
            var relativityAmount = new StackPanel { Orientation = Orientation.Horizontal };
            relativityAmount.Children.Add(relativity);
            relativityAmount.Children.Add(RainLabeledSegment("Amount", amount));
            details.Children.Add(relativityAmount);
        }
        BuildRainEffectSupplementalFields(effect, typeValue, details);
        if (hasStandardAmount) AddRainAltAmountField(effect, details);
        card.Children.Add(details);
        effectRows.Children.Add(container);
    }

    private void AddRainPresenceCheckBox(XElement effect, Panel row, string label, string attributeName)
    {
        var attribute = RainAttribute(effect, attributeName);
        if (!IsModifiedMode && attribute == null) return;
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = attribute != null,
            IsEnabled = IsModifiedMode,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 4),
            Tag = attributeName
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            if (checkBox.IsChecked == true)
                SetRainAttribute(effect, attributeName, "", refresh: false, preserveEmpty: true);
            else
                RemoveRainAttribute(effect, attributeName);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(checkBox);
    }

    private void AddRainHeaderNotNatureCheckBox(XElement effect, Panel row, string type)
    {
        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var attribute = target?.Attributes().FirstOrDefault(item =>
            item.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase));
        if (!RainEffectSupportsIgnoreNature(type.ToLowerInvariant()) && attribute == null) return;
        if (!IsModifiedMode && attribute == null) return;

        var checkBox = new CheckBox
        {
            Content = "Not Nature",
            IsChecked = attribute != null,
            IsEnabled = IsModifiedMode,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 4),
            Tag = "ignoreNature"
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            var current = EnsureRainUnitTarget(effect);
            var currentAttribute = current.Attributes().FirstOrDefault(item =>
                item.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase));
            if (checkBox.IsChecked == true)
            {
                if (currentAttribute == null) current.SetAttributeValue("ignoreNature", "");
            }
            else
                currentAttribute?.Remove();
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(checkBox);
    }

    private void AddRainCostTarget(XElement effect, Panel row, XElement? target)
    {
        var currentType = target?.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value ?? "ProtoUnit";
        var unitMode = currentType.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase);
        var allTechs = currentType.Equals("TechAll", StringComparison.OrdinalIgnoreCase);
        var kind = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Tech" },
            SelectedItem = unitMode ? "Unit" : "Tech",
            Width = 90,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 8, 4),
            Tag = "targetKind"
        };
        kind.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || kind.SelectedItem is not string selected) return;
            EnsureRainTypedTarget(effect, selected == "Unit" ? "ProtoUnit" : "Tech",
                clearValueOnTypeChange: true);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            RefreshAfterSelectorEvent(effect, RefreshCurrentEffectRows);
        };
        row.Children.Add(kind);

        var values = unitMode ? _unitOrTypeNames : new[] { "All" }.Concat(_techNames);
        var selector = CreateRainStrictSelector(values, allTechs ? "All" : target?.Value.Trim() ?? "", value =>
        {
            var type = !unitMode && value.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? "TechAll"
                : unitMode ? "ProtoUnit" : "Tech";
            var current = EnsureRainTypedTarget(effect, type, clearValueOnTypeChange: true);
            current.Value = type == "TechAll" ? "" : value;
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }, "target");
        row.Children.Add(RainLabeledSegment(unitMode ? "Unit" : "Tech", selector));
    }

    private static void InitializeRainEffectForType(XElement effect, string type)
        => PowerEffectFamilyController.InitializeForType(effect, type);

    private static IReadOnlyList<string> RainRelativityDisplaysForType(string type, string current)
        => PowerEffectFamilyController.GetRelativityDisplays(type, current);

    private static bool RainEffectAllowsOverride(string type)
        => PowerEffectFamilyController.AllowsOverride(type);

    private static bool RainEffectUsesIntegerAmount(string type)
        => PowerEffectFamilyController.UsesIntegerAmount(type);

    private TextBox CreateRainFloatAttributeBox(XElement effect, string attribute)
    {
        var box = new TextBox
        {
            Width = 90,
            Text = EditorNumericFieldStyle.FormatDisplay(RainAttribute(effect, attribute)?.Value),
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            Tag = attribute
        };
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedFloat);
        box.TextChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            SetRainAttribute(effect, attribute, box.Text ?? "");
        };
        return box;
    }

    private TextBox CreateRainIntegerAttributeBox(XElement effect, string attribute)
    {
        var box = CreateRainFloatAttributeBox(effect, attribute);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.SignedInteger);
        return box;
    }

    private void AddRainAltAmountField(XElement effect, Panel row)
    {
        var attribute = RainAttribute(effect, "altamount");
        if (attribute == null)
        {
            if (!IsModifiedMode) return;
            var add = PowerEditorDynamicControls.CreateOptionalButton("Alt Amount");
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                SetRainAttribute(effect, "altamount", "0", refresh: false);
                RefreshCurrentEffectRows();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(add);
            return;
        }

        var amount = CreateRainFloatAttributeBox(effect, "altamount");
        amount.Margin = new Thickness(0, 0, 2, 4);
        var segment = RainLabeledSegment("Alt Amount", amount);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.Margin = new Thickness(0, 0, 8, 4);
            remove.Click += (_, _) =>
            {
                RemoveRainAttribute(effect, "altamount");
                RefreshCurrentEffectRows();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            segment.Children.Add(remove);
        }
        row.Children.Add(segment);
    }

    private void BuildRainEffectLeadingFields(XElement effect, string type, WrapPanel row)
    {
        var normalized = type.ToLowerInvariant();
        if (normalized is "workrate" or "workratespecific" or "rateoffire" or "actionenable" or
            "damagebonus" or "damage" or "workrateall" or "onhiteffectprobability" or
            "onhiteffectrate" or "modifyrate")
            AddRainActionField(effect, row, allowAll: normalized != "actionenable",
                persistAllActions: normalized != "damage");

        if (normalized == "tacticenable")
            AddRainStrictAttribute(effect, row, "Tactic", "tactic", _tacticNames);
        else if (normalized == "market")
            AddRainMarketFields(effect, row);
        else if (normalized is "minimumresourcetricklerate" or "maximumresourcetricklerate")
            AddRainComboAttribute(effect, row, "Resource", "resource", ProtoConstants.KnownResourceTypes);
        else if (normalized is "onhiteffectprobability" or "onhiteffectrate")
            AddRainOnHitLeadingFields(effect, row, normalized == "onhiteffectrate");
    }

    private void BuildRainEffectSupplementalFields(XElement effect, string type, WrapPanel row)
    {
        var normalized = type.ToLowerInvariant();
        if (normalized is "cannotsnare" or "cantbeslowed" or "invulnerable" or
            "cantargetbuttakesnodamage" or "tacticenable" or "actionenable")
        {
            AddRainOperationLabel(row, "Enable");
            return;
        }
        if (normalized == "techactivate")
        {
            AddRainOperationLabel(row, "Activates");
            return;
        }
        if (normalized == "equalizeresources")
        {
            AddRainOperationLabel(row, "Equalize");
            return;
        }
        if (normalized is "revealenemylos" or "sharedlos")
        {
            AddRainOperationLabel(row, "Reveals LOS");
            return;
        }
        if (normalized == "assignonhiteffect")
        {
            AddRainAssignOnHitFields(effect, row);
            return;
        }
        if (normalized == "updatevisuals")
        {
            AddRainStrictAttribute(effect, row, "Updates Unit Visuals", "unittype", _unitOrTypeNames);
            return;
        }
        if (normalized == "enemyattachprotounit")
        {
            AddRainStrictAttribute(effect, row, "Attach", "attachProtoUnit", _protoUnitNames);
            AddRainStrictAttribute(effect, row, "To", "unittype", _unitOrTypeNames);
            return;
        }
        if (!TechnologyEffectCatalogInventory.IsStructuredDataSubtype(type)) return;

        switch (normalized)
        {
            case "workrate":
                AddRainWorkRateForFields(effect, row);
                break;
            case "workratespecific":
                AddRainStrictAttribute(effect, row, "For", "unittype", _unitOrTypeNames);
                AddRainComboAttribute(effect, row, "Resource", "resource", ProtoConstants.KnownResourceTypes);
                break;
            case "damagebonus":
                AddRainStrictAttribute(effect, row, "Bonus against", "unittype", _unitOrTypeNames);
                break;
            case "damage":
                AddRainDefaultAllComboAttribute(effect, row, "Damage type", "damagetype",
                    new[] { "All" }.Concat(ProtoConstants.KnownDamageTypes));
                break;
            case "minimumresourcetricklerate":
            case "maximumresourcetricklerate":
                break;
            case "resourcetricklerate":
            case "resource":
                AddRainComboAttribute(effect, row, "Resource", "resource", ProtoConstants.KnownResourceTypes);
                break;
            case "cost":
                AddRainComboAttribute(effect, row, "Resource", "resource", ProtoConstants.KnownResourceTypes);
                break;
            case "onhiteffectprobability":
            case "onhiteffectrate":
                AddRainOptionalStrictAttribute(effect, row, "Target", "On-hit target", "targettype",
                    _unitOrTypeNames);
                break;
            case "protoactionadd":
                AddRainStrictAttribute(effect, row, "Grants", "protoaction", _protoActionNames);
                AddRainStrictAttribute(effect, row, "From", "unittype", _protoUnitNames);
                AddRainAddToTacticsField(effect, row);
                break;
            case "armorvulnerability":
                AddRainComboAttribute(effect, row, "Armor type", "armortype",
                    ["Hack", "Pierce", "Crush"]);
                break;
        }
    }

    private void AddRainEnableDisableState(XElement effect, Panel row)
    {
        var state = new ComboBox
        {
            ItemsSource = new[] { "Enable", "Disable" },
            SelectedItem = RainAttribute(effect, "amount")?.Value.Trim() == "0" ? "Disable" : "Enable",
            Width = 100,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            Tag = "state"
        };
        state.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || state.SelectedItem is not string selected) return;
            SetRainAttribute(effect, "relativity", "Assign", refresh: false);
            SetRainAttribute(effect, "amount", selected == "Disable" ? "0" : "1", refresh: false);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(state);
    }

    private static void AddRainOperationLabel(Panel row, string text)
        => row.Children.Add(new TextBlock
        {
            Text = text,
            Tag = "operation",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 4)
        });

    private void AddRainWorkRateForFields(XElement effect, Panel row)
    {
        var current = RainAttribute(effect, "unittype")?.Value.Trim() ?? "";
        var resourceMode = ProtoConstants.KnownResourceTypes.Contains(current,
            StringComparer.OrdinalIgnoreCase);
        var kind = new ComboBox
        {
            ItemsSource = new[] { "Unit", "Resource" },
            SelectedItem = resourceMode ? "Resource" : "Unit",
            Width = 100,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 8, 4),
            Tag = "forKind"
        };
        kind.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || kind.SelectedItem is not string selected) return;
            if (selected == "Resource")
                SetRainAttribute(effect, "unittype", ProtoConstants.KnownResourceTypes[0], refresh: false);
            else
                RemoveRainAttribute(effect, "unittype");
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            RefreshAfterSelectorEvent(effect, RefreshCurrentEffectRows);
        };
        row.Children.Add(RainLabeledSegment("For", kind));
        if (resourceMode)
            AddRainComboAttribute(effect, row, "", "unittype", ProtoConstants.KnownResourceTypes);
        else
            AddRainStrictAttribute(effect, row, "", "unittype", _unitOrTypeNames);
    }

    private void AddRainAssignOnHitFields(XElement effect, Panel row)
    {
        AddRainActionField(effect, row, allowAll: true);
        AddRainStrictAttribute(effect, row, "Source Unit", "srcproto", _protoUnitNames);
        AddRainStrictAttribute(effect, row, "Source Action", "srcaction", _protoActionNames);
        AddRainStrictAttribute(effect, row, "Effect", "effecttype", ProtoConstants.KnownOnHitEffectTypes);
    }

    private void AddRainActionField(XElement effect, Panel row, bool allowAll,
        bool persistAllActions = true)
    {
        var action = RainAttribute(effect, "action")?.Value.Trim() ?? "";
        var all = RainAttribute(effect, "allactions")?.Value.Trim();
        var current = allowAll && action.Length == 0 && all != "0" ? "All" : action;
        var options = (allowAll ? new[] { "All" }.Concat(_protoActionNames) : _protoActionNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var selector = CreateRainStrictSelector(options, current, value =>
        {
            if (allowAll && value.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                RemoveRainAttribute(effect, "action");
                if (persistAllActions)
                    SetRainAttribute(effect, "allactions", "1", refresh: false);
                else
                    RemoveRainAttribute(effect, "allactions");
            }
            else
            {
                RemoveRainAttribute(effect, "allactions");
                SetRainAttribute(effect, "action", value, refresh: false);
            }
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }, "action", width: 165);
        row.Children.Add(RainLabeledSegment("Action", selector));
    }

    private void AddRainOptionalStrictAttribute(XElement effect, Panel row, string buttonLabel,
        string fieldLabel, string attribute, IEnumerable<string> values)
    {
        var currentAttribute = RainAttribute(effect, attribute);
        if (currentAttribute == null)
        {
            if (!IsModifiedMode) return;
            var add = PowerEditorDynamicControls.CreateOptionalButton(buttonLabel);
            add.Margin = new Thickness(0, 0, 8, 4);
            add.Click += (_, _) =>
            {
                SetRainAttribute(effect, attribute, "", refresh: false, preserveEmpty: true);
                RefreshCurrentEffectRows();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(add);
            return;
        }

        var selector = CreateRainStrictSelector(values, currentAttribute.Value.Trim(),
            value => SetRainAttribute(effect, attribute, value, preserveEmpty: true), attribute);
        selector.Margin = new Thickness(0, 0, 2, 4);
        var segment = RainLabeledSegment(fieldLabel, selector);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.Margin = new Thickness(0, 0, 8, 4);
            remove.Click += (_, _) =>
            {
                RemoveRainAttribute(effect, attribute);
                RefreshCurrentEffectRows();
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            segment.Children.Add(remove);
        }
        row.Children.Add(segment);
    }

    private void AddRainStrictAttribute(XElement effect, Panel row, string label, string attribute,
        IEnumerable<string> values)
    {
        var selector = CreateRainStrictSelector(values, RainAttribute(effect, attribute)?.Value.Trim() ?? "",
            value => SetRainAttribute(effect, attribute, value), attribute);
        row.Children.Add(RainLabeledSegment(label, selector));
    }

    private void AddRainComboAttribute(XElement effect, Panel row, string label, string attribute,
        IEnumerable<string> values, bool preserveCurrent = true)
    {
        var current = RainAttribute(effect, attribute)?.Value.Trim() ?? "";
        var candidates = preserveCurrent ? values.Append(current) : values;
        var options = candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            Width = 130,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            Tag = attribute
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || combo.SelectedItem is not string selected) return;
            SetRainAttribute(effect, attribute, selected);
        };
        row.Children.Add(RainLabeledSegment(label, combo));
    }

    private void AddRainDefaultAllComboAttribute(XElement effect, Panel row, string label,
        string attribute, IEnumerable<string> values)
    {
        var current = RainAttribute(effect, attribute)?.Value.Trim() ?? "All";
        var options = values.Append(current).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value =>
                value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All",
            Width = 130,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            Tag = attribute
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || combo.SelectedItem is not string selected) return;
            if (selected.Equals("All", StringComparison.OrdinalIgnoreCase))
                RemoveRainAttribute(effect, attribute);
            else
                SetRainAttribute(effect, attribute, selected, refresh: false);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(RainLabeledSegment(label, combo));
    }

    private void AddRainMarketFields(XElement effect, Panel row)
    {
        var components = new[]
            { "BuyFactor", "SellFactor", "BuyDelta", "SellDelta", "BuyFactorSpecific", "SellFactorSpecific" };
        var current = RainAttribute(effect, "component")?.Value.Trim() ?? "BuyFactor";
        var componentOptions = OptionsIncludingCurrent(components, current);
        var combo = new ComboBox
        {
            ItemsSource = componentOptions,
            SelectedItem = componentOptions.First(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
            Width = 150, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 14, 4), Tag = "component"
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode || combo.SelectedItem is not string selected) return;
            SetRainAttribute(effect, "component", selected, refresh: false);
            if (!selected.EndsWith("Specific", StringComparison.OrdinalIgnoreCase))
                RemoveRainAttribute(effect, "resource");
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            RefreshAfterSelectorEvent(effect, RefreshCurrentEffectRows);
        };
        row.Children.Add(RainLabeledSegment("Component", combo));
        if (current.EndsWith("Specific", StringComparison.OrdinalIgnoreCase))
            AddRainComboAttribute(effect, row, "Resource", "resource", ProtoConstants.KnownResourceTypes);
    }

    private void AddRainOnHitLeadingFields(XElement effect, Panel row, bool includeDamageType)
    {
        var effectType = RainAttribute(effect, "effecttype")?.Value.Trim() ?? "";
        var selector = CreateRainStrictSelector(ProtoConstants.KnownOnHitEffectTypes, effectType, value =>
        {
            if (value.Equals(effectType, StringComparison.OrdinalIgnoreCase)) return;
            SetRainAttribute(effect, "effecttype", value, refresh: false);
            if (!value.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase))
                RemoveRainAttribute(effect, "dmgtype");
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            if (includeDamageType)
                RefreshAfterSelectorEvent(effect, RefreshCurrentEffectRows);
        }, "effecttype");
        row.Children.Add(RainLabeledSegment("Effect", selector));
        if (includeDamageType && (effectType.Equals("DamageOverTime", StringComparison.OrdinalIgnoreCase) ||
                                  RainAttribute(effect, "dmgtype") != null))
            AddRainDefaultAllComboAttribute(effect, row, "Damage type", "dmgtype",
                new[] { "All" }.Concat(ProtoConstants.KnownDamageTypes));
    }

    private void AddRainAddToTacticsField(XElement effect, Panel row)
    {
        var check = new CheckBox
        {
            Content = "Add to tactics",
            IsChecked = !string.Equals(RainAttribute(effect, "addToTactics")?.Value, "0", StringComparison.OrdinalIgnoreCase),
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "addToTactics"
        };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            if (check.IsChecked == false) SetRainAttribute(effect, "addToTactics", "0");
            else { RemoveRainAttribute(effect, "addToTactics"); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
        };
        row.Children.Add(check);
    }

    private void AddRainIgnoreNatureField(XElement effect, Panel row)
    {
        var target = effect.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase));
        var check = new CheckBox
        {
            Content = "Not Nature",
            IsChecked = target?.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase)) == true,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 14, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "ignoreNature"
        };
        check.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            var current = EnsureRainUnitTarget(effect);
            var attribute = current.Attributes().FirstOrDefault(item =>
                item.Name.LocalName.Equals("ignoreNature", StringComparison.OrdinalIgnoreCase));
            if (check.IsChecked == true)
            {
                if (attribute == null) current.SetAttributeValue("ignoreNature", "");
            }
            else attribute?.Remove();
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(check);
    }

    private AutoCompleteBox CreateRainStrictSelector(IEnumerable<string> values, string current,
        Action<string> commit, string tag, double width = 200)
        => PowerEditorDynamicControls.CreateStrictSelector(
            values, current, IsModifiedMode, () => _isPopulating, () => IsModifiedMode,
            commit, tag, _specificScroll, width);

    private static StackPanel RainLabeledSegment(string label, Control control)
        => PowerEditorDynamicControls.CreateLabeledSegment(label, control);

    private static bool RainEffectSupportsIgnoreNature(string type)
        => PowerEffectFamilyController.SupportsIgnoreNature(type);

    private static bool RainEffectUsesPlayerTarget(string type)
        => PowerEffectFamilyController.UsesPlayerTarget(type);

    private static bool RainEffectUsesStandardAmount(string type)
        => PowerEffectFamilyController.UsesStandardAmount(type);

    private static string RainRelativityToDisplay(string value)
        => PowerEffectFamilyController.RelativityToDisplay(value);

    private static string RainDisplayToRelativity(string value)
        => PowerEffectFamilyController.DisplayToRelativity(value);

    private static XAttribute? RainAttribute(XElement effect, string name)
        => PowerEffectFamilyController.Attribute(effect, name);

    private static void RemoveRainAttribute(XElement effect, string name)
        => PowerEffectFamilyController.RemoveAttribute(effect, name);

    private void SetRainAttribute(XElement effect, string name, string value, bool refresh = true,
        bool preserveEmpty = false)
    {
        var attribute = RainAttribute(effect, name);
        if (string.IsNullOrWhiteSpace(value) && !preserveEmpty)
        {
            if (attribute == null) return;
            attribute.Remove();
        }
        else
        {
            if (attribute != null &&
                (attribute.Value.Equals(value, StringComparison.Ordinal) ||
                 NumericTextEquals(attribute.Value, value))) return;
            if (attribute == null) effect.SetAttributeValue(name, value);
            else attribute.Value = value;
        }
        if (!refresh) return;
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static XElement EnsureRainUnitTarget(XElement effect)
        => PowerEffectFamilyController.EnsureUnitTarget(effect);

    private static XElement EnsureRainTypedTarget(XElement effect, string targetType,
        bool clearValueOnTypeChange)
        => PowerEffectFamilyController.EnsureTypedTarget(effect, targetType, clearValueOnTypeChange);

    private static void NormalizeRainTargetForType(XElement effect, string type)
        => PowerEffectFamilyController.NormalizeTargetForType(effect, type);

    private void AddRainEffect_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        AddDefaultPowerEffect(_currentEntry);
        RefreshRainFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static void AddDefaultPowerEffect(XElement entry)
        => PowerEffectFamilyController.AddDefaultEffect(entry);

    private void RefreshEarthMonsterFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("EarthMonster",
                StringComparison.OrdinalIgnoreCase) != true)
            return;

        PopulateStrictActionBox(_earthMonsterStrikeActionBox,
            PowerCatalog.FindField(entry, "strikeaction")?.Value);
        PopulateStrictActionBox(_earthMonsterCrushActionBox,
            PowerCatalog.FindField(entry, "crushaction")?.Value);
        var animation = PowerCatalog.FindField(entry, "regurgitateanim")?.Value.Trim() ?? "";
        _earthMonsterRegurgitateAnimBox.SelectedItem = _animationNames.FirstOrDefault(value =>
            value.Equals(animation, StringComparison.OrdinalIgnoreCase));
        _earthMonsterRegurgitateAnimBox.Text = animation;

        var relation = PowerCatalog.FindField(entry, "swallowplayerrelation")?.Value.Trim() ?? "";
        var relationOptions = OptionsIncludingCurrent(PowerPlayerRelations, relation);
        _earthMonsterSwallowRelationBox.ItemsSource = relationOptions;
        _earthMonsterSwallowRelationBox.SelectedItem = relationOptions.FirstOrDefault(value =>
            value.Equals(relation, StringComparison.OrdinalIgnoreCase));

        var soundSuggestions = GetSoundSetSuggestions();
        foreach (var (box, tag) in new[]
                 {
                     (_earthMonsterEmergeSoundBox, "emergesoundset"),
                     (_earthMonsterChompSoundBox, "chompsoundset"),
                     (_earthMonsterRegurgitateSoundBox, "regurgitatesoundset")
                 })
        {
            box.ItemsSource = soundSuggestions;
            box.Text = PowerCatalog.FindField(entry, tag)?.Value ?? "";
        }

        var regurgitateOpen = new[]
        {
            "regurgitatetime", "regurgitateunitstime", "regurgitateheight",
            "regurgitateanim", "regurgitateanimtime"
        }.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _earthMonsterRegurgitateLabel.IsVisible = regurgitateOpen;
        _earthMonsterRegurgitateFields.IsVisible = regurgitateOpen;
        _addEarthMonsterRegurgitateButton.IsVisible = IsModifiedMode && !regurgitateOpen;
        _removeEarthMonsterRegurgitateButton.IsVisible = IsModifiedMode && regurgitateOpen;
    }

    private void EarthMonsterSwallowRelation_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _earthMonsterSwallowRelationBox.SelectedItem is not string value ||
            !PowerPlayerRelations.Contains(value, StringComparer.OrdinalIgnoreCase))
            return;
        SetOrRemoveSpecificField("swallowplayerrelation", value);
    }

    private void AddEarthMonsterRegurgitate_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var (tag, value) in new[]
                 {
                     ("regurgitatetime", "0"), ("regurgitateunitstime", "0"),
                     ("regurgitateheight", "0"), ("regurgitateanim", ""),
                     ("regurgitateanimtime", "0")
                 })
            PowerCatalog.SetField(_currentEntry, tag, value);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _earthMonsterRegurgitateTimingBox.Focus();
    }

    private void RemoveEarthMonsterRegurgitate_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[]
                 {
                     "regurgitatetime", "regurgitateunitstime", "regurgitateheight",
                     "regurgitateanim", "regurgitateanimtime"
                 })
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshDamageTiers(XElement entry)
    {
        _damageTierRows.Children.Clear();
        _damageTierFields.Clear();
        var container = PowerCatalog.FindField(entry, "damagetiers");
        if (container != null)
            foreach (var tier in container.Elements().Where(element =>
                         element.Name.LocalName.Equals("damagetier", StringComparison.OrdinalIgnoreCase)))
                BuildDamageTierRow(tier);
        _addDamageTierButton.IsVisible = IsModifiedMode;
    }

    private void BuildDamageTierRow(XElement tier)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Segment number", VerticalAlignment = VerticalAlignment.Center });
        var segment = new TextBox { Text = tier.Attribute("mindestroyed")?.Value ?? "" };
        EditorNumericFieldStyle.ConfigureNumericTextBox(segment);
        row.Children.Add(segment);
        row.Children.Add(new TextBlock { Text = "HP Loss Multiplier", VerticalAlignment = VerticalAlignment.Center });
        var reduction = new TextBox { Text = EditorNumericFieldStyle.FormatDisplay(tier.Value) };
        EditorNumericFieldStyle.ConfigureNumericTextBox(reduction);
        row.Children.Add(reduction);
        var remove = PowerEditorDynamicControls.CreateRemoveButton();
        remove.IsVisible = IsModifiedMode;
        remove.Click += (_, _) => RemoveDamageTier(tier);
        row.Children.Add(remove);
        segment.TextChanged += (_, _) => UpdateDamageTier(tier, segment, reduction);
        reduction.TextChanged += (_, _) => UpdateDamageTier(tier, segment, reduction);
        segment.IsEnabled = reduction.IsEnabled = IsModifiedMode;
        _damageTierFields.Add((segment, reduction, tier));
        _damageTierRows.Children.Add(row);
    }

    private void UpdateDamageTier(XElement tier, TextBox segment, TextBox reduction)
    {
        if (_isPopulating || !IsModifiedMode || _currentEntry == null) return;
        if (!int.TryParse(segment.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !double.TryParse(reduction.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value)) return;
        if (tier.Attribute("mindestroyed")?.Value == segment.Text!.Trim() &&
            NumericTextEquals(tier.Value, reduction.Text!.Trim())) return;
        tier.SetAttributeValue("mindestroyed", segment.Text!.Trim());
        tier.Value = reduction.Text!.Trim();
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddDamageTier_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var container = PowerCatalog.FindField(_currentEntry, "damagetiers") ??
                        PowerCatalog.SetField(_currentEntry, "damagetiers", "");
        var tier = new XElement("damagetier", new XAttribute("mindestroyed", "0"), "0");
        container.Add(tier);
        BuildDamageTierRow(tier);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveDamageTier(XElement tier)
    {
        var container = tier.Parent;
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(tier);
        if (container != null && !container.Elements().Any())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(container);
        if (_currentEntry != null) RefreshDamageTiers(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshRootDamages(XElement entry)
    {
        _rootDamageRows.Children.Clear();
        _rootDamageFields.Clear();
        foreach (var damage in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("rooteddamage", StringComparison.OrdinalIgnoreCase)))
            BuildRootDamageRow(damage);
        _addRootDamageButton.IsVisible = IsModifiedMode;
    }

    private void BuildRootDamageRow(XElement damage)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Unit Type", VerticalAlignment = VerticalAlignment.Center });
        var type = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0 };
        EditorAutoCompleteService.ConfigureStrict(type, _unitOrTypeNames, damage.Value, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: false, selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (!IsModifiedMode || value.Length == 0) return;
                if (damage.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return;
                damage.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(type, _specificScroll);
        var savedType = damage.Value.Trim();
        type.SelectedItem = _unitOrTypeNames.FirstOrDefault(value =>
            value.Equals(savedType, StringComparison.OrdinalIgnoreCase));
        type.Text = savedType;
        row.Children.Add(type);
        row.Children.Add(new TextBlock { Text = "Damage", VerticalAlignment = VerticalAlignment.Center });
        var amount = new TextBox { Text = EditorNumericFieldStyle.FormatDisplay(damage.Attribute("damage")?.Value) };
        EditorNumericFieldStyle.ConfigureNumericTextBox(amount);
        amount.TextChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode ||
                !double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0) return;
            if (double.TryParse(damage.Attribute("damage")?.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var original) && original.Equals(value)) return;
            damage.SetAttributeValue("damage", amount.Text!.Trim());
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(amount);
        var remove = PowerEditorDynamicControls.CreateRemoveButton(); remove.IsVisible = IsModifiedMode;
        remove.Click += (_, _) =>
        {
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(damage);
            if (_currentEntry != null) RefreshRootDamages(_currentEntry);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(remove);
        type.IsEnabled = amount.IsEnabled = IsModifiedMode;
        _rootDamageFields.Add((type, amount, damage));
        _rootDamageRows.Children.Add(row);
    }

    private void AddRootDamage_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var damage = new XElement("rooteddamage", new XAttribute("damage", "0"), "");
        var last = _currentEntry.Elements().LastOrDefault(element =>
            element.Name.LocalName.Equals("rooteddamage", StringComparison.OrdinalIgnoreCase));
        if (last != null) last.AddAfterSelf(damage);
        else _currentEntry.Add(damage);
        RefreshRootDamages(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshSwapFields(XElement entry)
    {
        _swapEntryRows.Children.Clear();
        foreach (var swap in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = "Transform", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4) });
            var target = CreateStrictSpecificBox(_unitOrTypeNames, swap.Value, value =>
            {
                swap.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
            target.Margin = new Thickness(0, 0, 14, 4);
            row.Children.Add(target);
            row.Children.Add(new TextBlock { Text = "To", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4) });
            var destination = CreateStrictSpecificBox(_protoUnitNames,
                swap.Attribute("swapto")?.Value ?? "", value =>
                {
                    swap.SetAttributeValue("swapto", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                });
            destination.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(destination);
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(swap);
                RefreshSwapFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _swapEntryRows.Children.Add(row);
        }
        _addSwapEntryButton.IsVisible = IsModifiedMode;

        var minMax = PowerCatalog.FindField(entry, "convertusingminmax");
        _swapMinMaxFields.IsVisible = minMax != null;
        _addSwapMinMaxButton.IsVisible = IsModifiedMode && minMax == null;
        _removeSwapMinMaxButton.IsVisible = IsModifiedMode && minMax != null;
        _swapMinAmountBox.Text = minMax?.Attribute("minswapamount")?.Value ?? "";
        _swapMaxAmountBox.Text = minMax?.Attribute("maxswapamount")?.Value ?? "";

        RefreshSwapOptions(entry);
        var selectedFlags = SwapFlags.Where(flag => PowerCatalog.FindField(entry, flag.XmlName) != null).ToList();
        _swapFlagPicker.IsVisible = IsModifiedMode && selectedFlags.Count < SwapFlags.Length;
        _swapFlagPicker.ItemsSource = SwapFlags.Where(flag => !selectedFlags.Contains(flag))
            .Select(flag => flag.Label).ToList();
        _swapFlagPicker.SelectedItem = null;
        _swapFlagPicker.Text = "";
        _swapFlagChips.Children.Clear();
        foreach (var flag in selectedFlags)
            _swapFlagChips.Children.Add(PowerEditorDynamicControls.CreateChip(flag.Label, IsModifiedMode ? () =>
            {
                var element = PowerCatalog.FindField(entry, flag.XmlName);
                if (element != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshSwapFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } : null, readOnly: !IsModifiedMode));
    }

    private AutoCompleteBox CreateStrictSpecificBox(IEnumerable<string> choices, string value,
        Action<string> commit)
    {
        var items = choices.ToList();
        var committedValue = value.Trim();
        var box = new AutoCompleteBox { Width = 200, MaxWidth = 200,
            FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        EditorAutoCompleteService.ConfigureStrict(box, items, value, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: committed =>
            {
                if (!IsModifiedMode || committed.Equals(committedValue, StringComparison.OrdinalIgnoreCase))
                    return;
                committedValue = committed;
                commit(committed);
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        box.SelectedItem = items.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        box.Text = value;
        return box;
    }

    private void RefreshSwapOptions(XElement entry)
    {
        _swapOptionalFields.Children.Clear();
        foreach (var option in new[]
                 {
                     (Label: "Delay", Tag: "swapdelay", Kind: "number"),
                     (Label: "Chance", Tag: "overallchance", Kind: "chance"),
                     (Label: "VFX", Tag: "vfxproto", Kind: "proto")
                 })
        {
            var element = PowerCatalog.FindField(entry, option.Tag);
            if (element == null) continue;
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4) };
            group.Children.Add(new TextBlock { Text = option.Label, VerticalAlignment = VerticalAlignment.Center });
            if (option.Kind == "proto")
                group.Children.Add(CreateStrictSpecificBox(_protoUnitNames, element.Value, value =>
                {
                    element.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                }));
            else
            {
                var box = new TextBox { Width = 90, Text = EditorNumericFieldStyle.FormatDisplay(element.Value),
                    IsEnabled = IsModifiedMode };
                EditorNumericFieldStyle.ConfigureNumericTextBox(box);
                box.PropertyChanged += (_, change) =>
                {
                    if (change.Property != TextBox.TextProperty) return;
                    if (_isPopulating || !IsModifiedMode ||
                        !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                        !double.IsFinite(number) || number < 0 || (option.Kind == "chance" && number > 1)) return;
                    element.Value = box.Text!.Trim(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                };
                group.Children.Add(box);
            }
            if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshSwapFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _swapOptionalFields.Children.Add(group);
        }
        if (!IsModifiedMode) return;
        foreach (var option in new[] { ("Delay", "swapdelay", "0"), ("Chance", "overallchance", "1"),
                     ("VFX", "vfxproto", "") })
            if (PowerCatalog.FindField(entry, option.Item2) == null)
                _swapOptionalFields.Children.Add(CreateTempUnitAddButton(option.Item1, () =>
                {
                    PowerCatalog.SetField(entry, option.Item2, option.Item3);
                    RefreshSwapFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                }));
    }

    private void AddSwapEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var swap = PowerCatalog.AddRepeatedField(_currentEntry, "abstracttype", "");
        swap.SetAttributeValue("swapto", "");
        RefreshSwapFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddSwapMinMax_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.SetField(_currentEntry, "convertusingminmax", "");
        field.SetAttributeValue("minswapamount", "0"); field.SetAttributeValue("maxswapamount", "0");
        RefreshSwapFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveSwapMinMax_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "convertusingminmax");
        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        RefreshSwapFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwapMinMax_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "convertusingminmax");
        if (field == null || !int.TryParse(_swapMinAmountBox.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out _) || !int.TryParse(_swapMaxAmountBox.Text,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return;
        field.SetAttributeValue("minswapamount", _swapMinAmountBox.Text!.Trim());
        field.SetAttributeValue("maxswapamount", _swapMaxAmountBox.Text!.Trim());
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void SwapFlagPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _swapFlagPicker.SelectedItem is not string label) return;
        var flag = SwapFlags.FirstOrDefault(value => value.Label.Equals(label, StringComparison.Ordinal));
        if (flag.XmlName == null) return;
        PowerCatalog.SetField(_currentEntry, flag.XmlName, "");
        RefreshSwapFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshLureFields(XElement entry)
    {
        var attractTypes = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("attracttype", StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = attractTypes.Select(element => element.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _lureAttractTypePicker.ItemsSource = _unitOrTypeNames.Where(value => !selected.Contains(value)).ToList();
        _lureAttractTypePicker.IsVisible = IsModifiedMode && selected.Count < _unitOrTypeNames.Count;
        _lureAttractTypePicker.SelectedItem = null;
        _lureAttractTypePicker.Text = "";
        _lureAttractTypeChips.Children.Clear();
        foreach (var element in attractTypes)
            _lureAttractTypeChips.Children.Add(PowerEditorDynamicControls.CreateChip(element.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                    RefreshLureFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));

        var hasSpawn = PowerCatalog.FindField(entry, "spawnproto") != null ||
                       PowerCatalog.FindField(entry, "spawnvfx") != null;
        _addLureSpawnButton.IsVisible = IsModifiedMode && !hasSpawn;
        _lureSpawnFields.IsVisible = hasSpawn;
        _removeLureSpawnButton.IsVisible = IsModifiedMode && hasSpawn;
        RefreshLureAgeInfo(entry);
    }

    private void LureAttractTypePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _lureAttractTypePicker.SelectedItem is not string value ||
            !_unitOrTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        PowerCatalog.AddRepeatedField(_currentEntry, "attracttype", value);
        RefreshLureFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddLureSpawn_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "spawnproto", "");
        PowerCatalog.SetField(_currentEntry, "spawnvfx", "");
        RefreshLureFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _lureSpawnProtoBox.Focus();
    }

    private void RemoveLureSpawn_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[] { "spawnproto", "spawnvfx" })
        {
            var field = PowerCatalog.FindField(_currentEntry, tag);
            if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        _lureSpawnProtoBox.Text = ""; _lureSpawnVfxBox.Text = "";
        RefreshLureFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshLureAgeInfo(XElement entry)
    {
        _lureAgeInfoRows.Children.Clear(); _lureAgeInfoFields.Clear();
        var elements = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("ageinfo", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var element in elements)
        {
            var rawAge = element.Value.Trim();
            var currentIndex = int.TryParse(rawAge, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 5 ? parsed - 1 :
                Array.FindIndex(StandardAges, value => value.Equals(rawAge,
                    StringComparison.OrdinalIgnoreCase));
            var usedByOthers = elements.Where(item => !ReferenceEquals(item, element)).Select(item =>
                int.TryParse(item.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var age)
                    ? age - 1 : Array.FindIndex(StandardAges, value => value.Equals(item.Value.Trim(),
                        StringComparison.OrdinalIgnoreCase))).ToHashSet();
            var options = StandardAges.Where((_, index) => index == currentIndex ||
                !usedByOthers.Contains(index)).ToList();
            var selectedAgeOption = currentIndex >= 0 ? StandardAges[currentIndex] : rawAge;
            if (selectedAgeOption.Length > 0 &&
                !options.Contains(selectedAgeOption, StringComparer.OrdinalIgnoreCase))
                options.Add(selectedAgeOption);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "Age", VerticalAlignment = VerticalAlignment.Center });
            var ageBox = new ComboBox { Width = 140, ItemsSource = options,
                SelectedItem = options.FirstOrDefault(value => value.Equals(selectedAgeOption,
                    StringComparison.OrdinalIgnoreCase)),
                IsEnabled = IsModifiedMode };
            row.Children.Add(ageBox);
            row.Children.Add(new TextBlock { Text = "Max Food", VerticalAlignment = VerticalAlignment.Center });
            var maxFood = CreateLureAgeIntegerBox(element.Attribute("maxfood")?.Value);
            row.Children.Add(maxFood);
            row.Children.Add(new TextBlock { Text = "Spawn Amount", VerticalAlignment = VerticalAlignment.Center });
            var spawnAmount = CreateLureAgeIntegerBox(element.Attribute("numspawns")?.Value);
            row.Children.Add(spawnAmount);
            ageBox.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || ageBox.SelectedItem is not string selectedAge) return;
                var selectedIndex = Array.IndexOf(StandardAges, selectedAge);
                if (selectedIndex < 0) return;
                element.Value = (selectedIndex + 1).ToString(CultureInfo.InvariantCulture);
                RefreshLureAgeInfo(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            maxFood.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    CommitLureAgeInteger(element, "maxfood", maxFood.Text);
            };
            spawnAmount.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    CommitLureAgeInteger(element, "numspawns", spawnAmount.Text);
            };
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshLureAgeInfo(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _lureAgeInfoRows.Children.Add(row);
            _lureAgeInfoFields.Add((ageBox, maxFood, spawnAmount, element));
        }
        var usedAges = elements.Select(element => int.TryParse(element.Value.Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : 0)
            .Where(age => age is >= 1 and <= 5).Distinct().Count();
        _addLureAgeInfoButton.IsVisible = IsModifiedMode && usedAges < StandardAges.Length;
    }

    private TextBox CreateLureAgeIntegerBox(string? value)
    {
        var box = new TextBox { Width = 90, Text = value ?? "", IsEnabled = IsModifiedMode };
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);
        return box;
    }

    private void CommitLureAgeInteger(XElement element, string attribute, string? text)
    {
        if (_isPopulating || !IsModifiedMode || !int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) || value < 0) return;
        element.SetAttributeValue(attribute, text!.Trim()); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddLureAgeInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var used = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("ageinfo", StringComparison.OrdinalIgnoreCase))
            .Select(element => int.TryParse(element.Value.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var age) ? age : 0).ToHashSet();
        var next = Enumerable.Range(1, StandardAges.Length).FirstOrDefault(age => !used.Contains(age));
        if (next == 0) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "ageinfo",
            next.ToString(CultureInfo.InvariantCulture));
        element.SetAttributeValue("maxfood", "0"); element.SetAttributeValue("numspawns", "0");
        RefreshLureAgeInfo(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshUndermineTargets(XElement entry)
    {
        _undermineTargetRows.Children.Clear(); _undermineTargetFields.Clear();
        _witherKillTypeChips.Children.Clear(); _witherKillTypePicker.SelectedItem = null;
        _witherKillTypePicker.Text = ""; _witherKillTypesOpen = false;
        _arcadianGrassRows.Children.Clear(); _arcadianGrassFields.Clear();
        _powerCloneReplacementChips.Children.Clear(); _powerCloneReplacementPicker.SelectedItem = null;
        _powerCloneReplacementPicker.Text = ""; _powerCloneSlotBox.SelectedItem = null;
        _powerCloneCopyCostCheck.IsChecked = false; _powerCloneCopyRepeatCostCheck.IsChecked = false;
        _powerCloneSeededCheck.IsChecked = false;
        foreach (var element in entry.Elements().Where(item =>
                     item.Name.LocalName.Equals("attacktarget", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4)
            };
            var target = CreateStrictSpecificBox(_unitOrTypeNames, element.Value.Trim(), value =>
            {
                element.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
            row.Children.Add(target);
            var instantKill = new CheckBox { Content = "Instant kill", IsEnabled = IsModifiedMode,
                IsChecked = element.Attribute("instantkill") != null, VerticalAlignment = VerticalAlignment.Center };
            instantKill.IsCheckedChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode) return;
                element.SetAttributeValue("instantkill", instantKill.IsChecked == true ? "1" : null);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(instantKill);
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshUndermineTargets(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _undermineTargetRows.Children.Add(row);
            _undermineTargetFields.Add((target, instantKill, element));
        }
        _addUndermineTargetButton.IsVisible = IsModifiedMode;
    }

    private void AddUndermineTarget_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.AddRepeatedField(_currentEntry, "attacktarget", "");
        RefreshUndermineTargets(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshTempestFields(XElement entry)
    {
        RefreshStrikeIntervalPresentation(entry);
        PopulateStrictActionBox(_tempestAreaAttackActionBox,
            PowerCatalog.FindField(entry, "areaattackaction")?.Value);
        _tempestRadialTargetingCheck.IsChecked = PowerCatalog.FindField(entry, "radialtargeting") != null;
        _tempestRadialTargetingCheck.IsEnabled = IsModifiedMode;
        _tempestStrikeSoundBox.ItemsSource = GetSoundSetSuggestions();
        _tempestStrikeSoundBox.Text = PowerCatalog.FindField(entry, "strikesoundset")?.Value ?? "";
        var lightning = entry.Attribute("type")?.Value.Trim()
            .Equals("LightningStorm", StringComparison.OrdinalIgnoreCase) == true;
        var strikeSoundOpen = PowerCatalog.FindField(entry, "strikesoundset") != null;
        _addTempestSoundButton.IsVisible = lightning && IsModifiedMode && !strikeSoundOpen;
        _tempestSoundLabel.IsVisible = !lightning || strikeSoundOpen;
        _tempestSoundFields.IsVisible = !lightning || strikeSoundOpen;
        _removeTempestSoundButton.IsVisible = lightning && IsModifiedMode && strikeSoundOpen;
        _tempestLargeSoundBox.ItemsSource = GetSoundSetSuggestions();
        _tempestLargeSoundBox.Text = PowerCatalog.FindField(entry, "largestrikesoundset")?.Value ?? "";

        var largeOpen = PowerCatalog.FindField(entry, "largestrikeproto") != null;
        _addTempestLargeStrikeButton.IsVisible = IsModifiedMode && !largeOpen;
        _tempestLargeStrikeRow.IsVisible = IsModifiedMode || largeOpen;
        _tempestLargeStrikeLabel.IsVisible = largeOpen;
        _tempestLargeStrikeFields.IsVisible = largeOpen;
        _removeTempestLargeStrikeButton.IsVisible = IsModifiedMode && largeOpen;
        _tempestLargeTimingRow.IsVisible = largeOpen;
        _tempestLargeOptionsRow.IsVisible = largeOpen;
        RefreshTempestLargeTimings(entry);
        var selectedAvoids = TempestAvoids.Where(item => PowerCatalog.FindField(entry, item.XmlName) != null).ToList();
        _tempestAvoidPicker.IsVisible = IsModifiedMode && largeOpen && selectedAvoids.Count < TempestAvoids.Length;
        _tempestAvoidPicker.ItemsSource = TempestAvoids.Where(item => !selectedAvoids.Contains(item))
            .Select(item => item.Label).ToList();
        _tempestAvoidPicker.SelectedItem = null; _tempestAvoidPicker.Text = "";
        _tempestAvoidChips.Children.Clear();
        foreach (var item in selectedAvoids)
            _tempestAvoidChips.Children.Add(PowerEditorDynamicControls.CreateChip(item.Label, IsModifiedMode ? () =>
            {
                var field = PowerCatalog.FindField(entry, item.XmlName);
                if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
                RefreshTempestFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } : null, readOnly: !IsModifiedMode));

        var attackOpen = PowerCatalog.FindField(entry, "areaattackaction") != null ||
                         PowerCatalog.FindField(entry, "radialtargeting") != null;
        _addTempestAttackButton.IsVisible = IsModifiedMode && !attackOpen;
        _tempestAttackLabel.IsVisible = attackOpen;
        _tempestAttackFields.IsVisible = attackOpen;
        _removeTempestAttackButton.IsVisible = IsModifiedMode && attackOpen;

        RefreshTempestVfx(entry);
        RefreshTempestOptionalGroup(entry, _addTempestCloudsButton, _tempestCloudFields,
            _removeTempestCloudsButton, _tempestCloudsLabel,
            ["cloudbuildupstarttime", "cloudbuildupendtime", "cloudfadeoutstarttime", "cloudfadeoutendtime"]);
        RefreshTempestOptionalGroup(entry, _addTempestDriftButton, _tempestDriftFields,
            _removeTempestDriftButton, _tempestDriftLabel, ["slowestdrifttime", "fastestdrifttime"]);
        RefreshTempestOptionalGroup(entry, _addTempestTreeBendButton, _tempestTreeBendFields,
            _removeTempestTreeBendButton, _tempestTreeBendLabel,
            ["treebendminstrength", "treebendmaxstrength", "treebendminperiod",
                "treebendmaxperiod", "treebendminamplitude", "treebendmaxamplitude", "treebendangleoffset",
                "treebendingduration", "treebendstrengthmin", "treebendstrengthmax",
                "treebendperiodmin", "treebendperiodmax"]);
        var treeBendPowerType = entry.Attribute("type")?.Value.Trim();
        var legacyTreeBend = treeBendPowerType?.Equals("Tempest", StringComparison.OrdinalIgnoreCase) == true &&
            new[] { "treebendstrengthmin", "treebendstrengthmax", "treebendperiodmin", "treebendperiodmax" }
                .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        if (legacyTreeBend)
        {
            _tempestStrengthMinBox.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, "treebendstrengthmin")?.Value);
            _tempestStrengthMaxBox.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, "treebendstrengthmax")?.Value);
            _tempestPeriodMinBox.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, "treebendperiodmin")?.Value);
            _tempestPeriodMaxBox.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, "treebendperiodmax")?.Value);
        }
        _tempestAmplitudeHost.IsVisible = !legacyTreeBend && (IsModifiedMode ||
            PowerCatalog.FindField(entry, "treebendminamplitude") != null ||
            PowerCatalog.FindField(entry, "treebendmaxamplitude") != null);
        _tempestAngleHost.IsVisible = !legacyTreeBend && (IsModifiedMode ||
            PowerCatalog.FindField(entry, "treebendangleoffset") != null);
        _tempestTreeDurationHost.IsVisible = !legacyTreeBend && (IsModifiedMode ||
            PowerCatalog.FindField(entry, "treebendingduration") != null);
        var treeBendIsDefault = legacyTreeBend ||
                                treeBendPowerType?.Equals("Typhoon", StringComparison.OrdinalIgnoreCase) == true;
        if (treeBendIsDefault)
            _tempestTreeBendRow.IsVisible = IsModifiedMode || legacyTreeBend ||
                new[] { "treebendminstrength", "treebendmaxstrength", "treebendminperiod", "treebendmaxperiod",
                    "treebendminamplitude", "treebendmaxamplitude", "treebendangleoffset", "treebendingduration" }
                .Any(tag => PowerCatalog.FindField(entry, tag) != null);
    }

    private void RefreshStrikeIntervalPresentation(XElement entry)
    {
        var lightning = entry.Attribute("type")?.Value.Equals("LightningStorm",
            StringComparison.OrdinalIgnoreCase) == true;
        if (_strikeIntervalFields.Parent is Panel currentParent)
            currentParent.Children.Remove(_strikeIntervalFields);
        if (lightning)
        {
            _tempestStrikeContent.Children.Add(_strikeIntervalFields);
            _tempestStrikeIntervalRow.IsVisible = false;
        }
        else
            _tempestStrikeIntervalRow.Children.Add(_strikeIntervalFields);
        _strikeIntervalFields.Children.Remove(_slowestStrikeFields);
        _strikeIntervalFields.Children.Remove(_fastestStrikeFields);
        if (lightning)
        {
            _fastestStrikeLabel.Text = "Min";
            _slowestStrikeLabel.Text = "Max";
            _fastestStrikeFields.Margin = new Thickness(0, 0, 18, 4);
            _strikeIntervalInlineLabel.IsVisible = true;
            _strikeIntervalFields.Children.Add(_fastestStrikeFields);
            _strikeIntervalFields.Children.Add(_slowestStrikeFields);
        }
        else
        {
            _slowestStrikeLabel.Text = "Slowest";
            _fastestStrikeLabel.Text = "Fastest";
            _fastestStrikeFields.Margin = new Thickness(0, 0, 0, 4);
            _strikeIntervalInlineLabel.IsVisible = false;
            _strikeIntervalFields.Children.Add(_slowestStrikeFields);
            _strikeIntervalFields.Children.Add(_fastestStrikeFields);
        }
    }

    private void RefreshLightningFields(XElement entry)
    {
        var required = new[]
        {
            (Tag: "sparksvfxproto", Host: (Control)_lightningSparksHost),
            (Tag: "groundsparksvfxproto", Host: (Control)_lightningGroundSparksHost),
            (Tag: "scorchvfxproto", Host: (Control)_lightningScorchHost)
        };
        foreach (var item in required)
            item.Host.IsVisible = IsModifiedMode || PowerCatalog.FindField(entry, item.Tag) != null;

        RefreshLightningOptionalVfx(entry, "rainvfxproto", _lightningRainVfxHost,
            _addLightningRainVfxButton, _removeLightningRainVfxButton);
        RefreshLightningOptionalVfx(entry, "cloudvfxproto", _lightningCloudVfxHost,
            _addLightningCloudVfxButton, _removeLightningCloudVfxButton);
        RefreshTempestOptionalGroup(entry, _addLightningRainButton, _lightningRainFields,
            _removeLightningRainButton, _lightningRainLabel,
            ["rainbuildupstarttime", "rainbuildupendtime", "rainfadeoutstarttime", "rainfadeoutendtime"]);
    }

    private void RefreshLightningOptionalVfx(XElement entry, string tag, Control host, Button add, Button remove)
    {
        var present = PowerCatalog.FindField(entry, tag) != null;
        host.IsVisible = present;
        add.IsVisible = IsModifiedMode && !present;
        remove.IsVisible = IsModifiedMode && present;
    }

    private void ResetTempestOptionalFlow()
    {
        foreach (var button in _tempestOptionalFlow.Children.OfType<Button>().ToList())
        {
            var owner = AllInlineOptionalRows().FirstOrDefault(item => ReferenceEquals(item.Add, button)).Row;
            _tempestOptionalFlow.Children.Remove(button);
            if (owner?.Children.FirstOrDefault() is Panel home && !home.Children.Contains(button))
                home.Children.Insert(0, button);
        }
        if (_tempestOptionalFlow.Parent is StackPanel parent)
            parent.Children.Remove(_tempestOptionalFlow);
    }

    private (Grid Row, Button Add)[] AllInlineOptionalRows()
        => [(_tempestSoundRow, _addTempestSoundButton),
            (_tempestLargeStrikeRow, _addTempestLargeStrikeButton),
            (_tempestAttackRow, _addTempestAttackButton), (_tempestVfxRow, _addTempestVfxButton),
            (_tempestCloudsRow, _addTempestCloudsButton), (_tempestDriftRow, _addTempestDriftButton),
            (_lightningRainRow, _addLightningRainButton),
            (_witherKillTypesRow, _addWitherKillTypesButton),
            (_droughtTerrainCreepRow, _addTerrainCreepButton),
            (_underworldVfxRow, _addUnderworldVfxButton),
            (_underworldLightingRow, _addUnderworldLightingButton),
            (_underworldTechEffectRow, _addUnderworldTechEffectButton),
            (_underworldSuddenDeathRow, _addUnderworldSuddenDeathButton)];

    private void ArrangeTempestOptionalRows(XElement entry)
    {
        ResetTempestOptionalFlow();
        var type = entry.Attribute("type")?.Value.Trim();
        (Grid Row, Button Add)[] candidates = type?.Equals("Tempest", StringComparison.OrdinalIgnoreCase) == true
            ? [(_tempestSoundRow, _addTempestSoundButton),
                (_tempestLargeStrikeRow, _addTempestLargeStrikeButton),
                (_tempestAttackRow, _addTempestAttackButton), (_tempestVfxRow, _addTempestVfxButton),
                (_tempestCloudsRow, _addTempestCloudsButton), (_tempestDriftRow, _addTempestDriftButton)]
            : type?.Equals("LightningStorm", StringComparison.OrdinalIgnoreCase) == true
                ? [(_tempestSoundRow, _addTempestSoundButton),
                    (_tempestCloudsRow, _addTempestCloudsButton),
                    (_lightningRainRow, _addLightningRainButton)]
                : type?.Equals("Wither", StringComparison.OrdinalIgnoreCase) == true
                    ? [(_witherKillTypesRow, _addWitherKillTypesButton),
                        (_droughtTerrainCreepRow, _addTerrainCreepButton)]
                    : type?.Equals("UnderworldInvasion", StringComparison.OrdinalIgnoreCase) == true
                        ? [(_underworldVfxRow, _addUnderworldVfxButton),
                            (_underworldLightingRow, _addUnderworldLightingButton),
                            (_underworldTechEffectRow, _addUnderworldTechEffectButton),
                            (_underworldSuddenDeathRow, _addUnderworldSuddenDeathButton)]
                        : [];
        var attached = candidates.Select(item => item.Row)
            .Where(row => row.Parent == _specificAttributeRows).ToList();
        if (attached.Count == 0) return;
        var insertionIndex = attached.Min(row => _specificAttributeRows.Children.IndexOf(row));
        foreach (var (row, add) in candidates.Where(item => attached.Contains(item.Row)))
        {
            row.ColumnDefinitions = new ColumnDefinitions("150,*");
            if (!add.IsVisible) continue;
            _specificAttributeRows.Children.Remove(row);
            if (add.Parent is Panel home) home.Children.Remove(add);
            add.Margin = new Thickness(0, 0, 8, 4);
            _tempestOptionalFlow.Children.Add(add);
        }
        if (_tempestOptionalFlow.Children.Count > 0)
            _specificAttributeRows.Children.Insert(Math.Min(insertionIndex,
                _specificAttributeRows.Children.Count), _tempestOptionalFlow);
    }

    private void RefreshEarthquakeFields(XElement entry)
    {
        SetSpecificFieldVisibility(_earthquakeAttackUnitBox, "attackproto", entry);
        SetSpecificFieldVisibility(_earthquakeIntervalHost, "attackinterval", entry);
        SetSpecificFieldVisibility(_earthquakeTreeAccuracyHost, "attacktreeaccuracy", entry);
        SetSpecificFieldVisibility(_earthquakeEpicenterRadiusHost, "epicenterzoneradius", entry);
        SetSpecificFieldVisibility(_earthquakeMiddleRadiusHost, "middlezoneradius", entry);
        SetSpecificFieldVisibility(_earthquakeOuterRadiusHost, "outerzoneradius", entry);
        SetSpecificFieldVisibility(_earthquakeEpicenterDamageHost, "epicenterzonedamageratio", entry);
        SetSpecificFieldVisibility(_earthquakeMiddleDamageHost, "middlezonedamageratio", entry);
        SetSpecificFieldVisibility(_earthquakeOuterDamageHost, "outerzonedamageratio", entry);

        var dustOpen = new[] { "dustsmallvfxproto", "dustmediumvfxproto", "dustlargevfxproto" }
            .Any(tag => PowerCatalog.FindField(entry, tag) != null);
        _addEarthquakeDustVfxButton.IsVisible = IsModifiedMode && !dustOpen;
        _earthquakeDustVfxLabel.IsVisible = dustOpen;
        _earthquakeDustVfxFields.IsVisible = dustOpen;
        _removeEarthquakeDustVfxButton.IsVisible = IsModifiedMode && dustOpen;
        SetSpecificFieldVisibility(_earthquakeDustSmallHost, "dustsmallvfxproto", entry);
        SetSpecificFieldVisibility(_earthquakeDustMediumHost, "dustmediumvfxproto", entry);
        SetSpecificFieldVisibility(_earthquakeDustLargeHost, "dustlargevfxproto", entry);
    }

    private void RefreshThunderBurstFields(XElement entry)
    {
        var amountVisible = IsModifiedMode || PowerCatalog.FindField(entry, "amount") != null;
        _thunderBurstAmountLabel.IsVisible = amountVisible;
        _thunderBurstAmountBox.IsVisible = amountVisible;
        SetSpecificFieldVisibility(_thunderBurstDelayHost, "delay", entry);
        SetSpecificFieldVisibility(_thunderBurstRadiusIncreaseHost, "radiusincreasestep", entry);
        SetSpecificFieldVisibility(_thunderBurstStunDurationHost, "stunduration", entry);
        SetSpecificFieldVisibility(_thunderBurstStartingVfxHost, "startingvfx", entry);
        SetSpecificFieldVisibility(_thunderBurstChargingVfxHost, "chargingvfx", entry);
        SetSpecificFieldVisibility(_thunderBurstBurstVfxHost, "burstvfx", entry);
    }

    private void RefreshTyphoonFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("Typhoon", StringComparison.OrdinalIgnoreCase) != true)
            return;
        var dragonVisible = IsModifiedMode || PowerCatalog.FindField(entry, "dragonprotounit") != null;
        _typhoonDragonLabel.IsVisible = dragonVisible;
        _typhoonDragonUnitBox.IsVisible = dragonVisible;
        SetSpecificFieldVisibility(_typhoonDragonSpawnDelayHost, "dragonspawndelayseconds", entry);
        SetSpecificFieldVisibility(_typhoonDragonAttackIntervalHost, "dragonattackinterval", entry);
        SetSpecificFieldVisibility(_typhoonUnitHost, "typhoonprotounit", entry);
        SetSpecificFieldVisibility(_typhoonSpawnDelayHost, "typhoonspawndelayseconds", entry);
        var rainUnitOpen = PowerCatalog.FindField(entry, "rainprotounit") != null;
        _typhoonRainUnitFields.IsVisible = rainUnitOpen;
        _addTyphoonRainUnitButton.IsVisible = IsModifiedMode && !rainUnitOpen;
        _removeTyphoonRainUnitButton.IsVisible = IsModifiedMode && rainUnitOpen;
        SetSpecificFieldVisibility(_typhoonAttackDelayHost, "typhoonattackdelayseconds", entry);
        SetSpecificFieldVisibility(_typhoonAttackIntervalHost, "typhoonattackinterval", entry);
        _typhoonRadiusHost.IsVisible = IsModifiedMode || new[]
        {
            "typhoonradiusstart", "typhoonradiusend", "typhoonradiusexpansionseconds"
        }.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        SetSpecificFieldVisibility(_typhoonRadiusStartHost, "typhoonradiusstart", entry);
        SetSpecificFieldVisibility(_typhoonRadiusEndHost, "typhoonradiusend", entry);
        SetSpecificFieldVisibility(_typhoonRadiusExpansionHost, "typhoonradiusexpansionseconds", entry);

    }

    private void AddTyphoonRainUnit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("Typhoon",
                StringComparison.OrdinalIgnoreCase) != true) return;
        PowerCatalog.SetField(_currentEntry, "rainprotounit", "");
        PopulateStrictProtoBox(_typhoonRainUnitBox, "");
        RefreshTyphoonFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _typhoonRainUnitBox.Focus();
    }

    private void RemoveTyphoonRainUnit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "rainprotounit");
        if (field == null) return;
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateStrictProtoBox(_typhoonRainUnitBox, "");
        RefreshTyphoonFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RefreshFrostFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("Frost", StringComparison.OrdinalIgnoreCase) != true)
            return;

        SetSpecificFieldVisibility(_frostExpansionHost, "freezeexpansiontime", entry);
        SetSpecificFieldVisibility(_frostTransitionHost, "freezetransitiontime", entry);
        SetSpecificFieldVisibility(_frostDurationHost, "freezeholdtime", entry);
        _frostStrikeIntervalHost.IsVisible = IsModifiedMode ||
            PowerCatalog.FindField(entry, "fasteststriketime") != null ||
            PowerCatalog.FindField(entry, "sloweststriketime") != null;
        _frostStrikeMinBox.IsVisible = IsModifiedMode ||
            PowerCatalog.FindField(entry, "fasteststriketime") != null;
        _frostStrikeMaxBox.IsVisible = IsModifiedMode ||
            PowerCatalog.FindField(entry, "sloweststriketime") != null;

        var armorOpen = PowerCatalog.FindField(entry, "armoramount") != null;
        _frostArmorHost.IsVisible = armorOpen;
        _addFrostArmorButton.IsVisible = IsModifiedMode && !armorOpen;
        _removeFrostArmorButton.IsVisible = IsModifiedMode && armorOpen;

        SetSpecificFieldVisibility(_frostStormHost, "stormprotounit", entry);
        SetSpecificFieldVisibility(_frostSnowDriftHost, "snowdriftprotounit", entry);
        var driftOpen = PowerCatalog.FindField(entry, "slowestdrifttime") != null ||
                        PowerCatalog.FindField(entry, "fastestdrifttime") != null;
        _frostDriftHost.IsVisible = driftOpen;
        _frostDriftLabel.IsVisible = driftOpen;
        _addFrostDriftButton.IsVisible = IsModifiedMode && !driftOpen;
        _removeFrostDriftButton.IsVisible = IsModifiedMode && driftOpen;
        _frostSlowestDriftHost.IsVisible = IsModifiedMode ||
            PowerCatalog.FindField(entry, "slowestdrifttime") != null;
        _frostFastestDriftHost.IsVisible = IsModifiedMode ||
            PowerCatalog.FindField(entry, "fastestdrifttime") != null;
    }

    private void AddFrostArmor_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "armoramount", "0");
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _frostArmorBox.Focus();
    }

    private void RemoveFrostArmor_Click(object? sender, RoutedEventArgs e)
        => RemoveFrostFields("armoramount");

    private void AddFrostDrift_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "slowestdrifttime", "0");
        PowerCatalog.SetField(_currentEntry, "fastestdrifttime", "0");
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _frostSlowestDriftBox.Focus();
    }

    private void RemoveFrostDrift_Click(object? sender, RoutedEventArgs e)
        => RemoveFrostFields("slowestdrifttime", "fastestdrifttime");

    private void RemoveFrostFields(params string[] tags)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in tags)
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void FrostArmor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_frostArmorBox.IsVisible) return;
        var text = _frostArmorBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            SetOrRemoveSpecificField("armoramount", "");
            return;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value)) return;
        var clamped = Math.Clamp(value, 0, 1);
        var normalized = clamped == value ? text : clamped.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(normalized, text, StringComparison.Ordinal))
            SetBoxTextWithoutEditing(_frostArmorBox, normalized);
        SetOrRemoveSpecificField("armoramount", normalized);
    }

    private void RefreshWitherFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("Wither", StringComparison.OrdinalIgnoreCase) != true)
            return;
        SetSpecificFieldVisibility(_witherWaveUnitBox, "waveprotounit", entry);
        SetSpecificFieldVisibility(_witherDurationHost, "waveduration", entry);
        SetSpecificFieldVisibility(_witherVfxIntervalHost, "vfxinterval", entry);

        var killTypes = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("killunittype", StringComparison.OrdinalIgnoreCase)).ToList();
        var killOpen = _witherKillTypesOpen || killTypes.Count > 0;
        _addWitherKillTypesButton.IsVisible = IsModifiedMode && !killOpen;
        _witherKillTypesLabel.IsVisible = killOpen;
        _witherKillTypesFields.IsVisible = killOpen;
        _removeWitherKillTypesButton.IsVisible = IsModifiedMode && killOpen;
        _witherKillTypeChips.Children.Clear();
        var selected = killTypes.Select(element => element.Value.Trim())
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _witherKillTypePicker.ItemsSource = _unitOrTypeNames
            .Where(value => !selected.Contains(value)).ToList();
        _witherKillTypePicker.SelectedItem = null;
        _witherKillTypePicker.Text = "";
        _witherKillTypePicker.IsVisible = IsModifiedMode && killOpen && selected.Count < _unitOrTypeNames.Count;
        foreach (var element in killTypes.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = element;
            _witherKillTypeChips.Children.Add(PowerEditorDynamicControls.CreateChip(element.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RefreshWitherFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }

        RefreshWitherOptionalField(entry, "damagepercentofmaxhp", _addWitherDamageButton,
            _witherDamageFields, _removeWitherDamageButton, _witherDamageLabel);
        RefreshWitherOptionalField(entry, "creepgenerators", _addWitherCreepGeneratorsButton,
            _witherCreepGeneratorsFields, _removeWitherCreepGeneratorsButton,
            _witherCreepGeneratorsLabel);
    }

    private void RefreshWitherOptionalField(XElement entry, string tag, Button add, Control fields,
        Button remove, TextBlock label)
    {
        var present = PowerCatalog.FindField(entry, tag) != null;
        add.IsVisible = IsModifiedMode && !present;
        label.IsVisible = present;
        fields.IsVisible = present;
        remove.IsVisible = IsModifiedMode && present;
    }

    private void AddWitherKillTypes_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _witherKillTypesOpen = true;
        RefreshWitherFields(_currentEntry);
        _witherKillTypePicker.Focus();
        _witherKillTypePicker.IsDropDownOpen = true;
    }

    private void WitherKillTypePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _witherKillTypePicker.SelectedItem is not string value ||
            !_unitOrTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        if (!_currentEntry.Elements().Any(element =>
                element.Name.LocalName.Equals("killunittype", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            PowerCatalog.AddRepeatedField(_currentEntry, "killunittype", value);
        _witherKillTypesOpen = true;
        RefreshWitherFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveWitherKillTypes_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var element in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("killunittype", StringComparison.OrdinalIgnoreCase)).ToList())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
        _witherKillTypesOpen = false;
        RefreshWitherFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddWitherDamage_Click(object? sender, RoutedEventArgs e)
        => AddWitherOptionalField("damagepercentofmaxhp", _witherDamageBox);

    private void RemoveWitherDamage_Click(object? sender, RoutedEventArgs e)
        => RemoveWitherOptionalField("damagepercentofmaxhp");

    private void AddWitherCreepGenerators_Click(object? sender, RoutedEventArgs e)
        => AddWitherOptionalField("creepgenerators", _witherCreepGeneratorsBox);

    private void RemoveWitherCreepGenerators_Click(object? sender, RoutedEventArgs e)
        => RemoveWitherOptionalField("creepgenerators");

    private void AddWitherOptionalField(string tag, Control focus)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, tag, "0");
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        focus.Focus();
    }

    private void RemoveWitherOptionalField(string tag)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void WitherDamage_TextChanged()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_witherDamageBox.IsVisible) return;
        var text = _witherDamageBox.Text?.Trim() ?? "";
        if (text.Length == 0) { SetOrRemoveSpecificField("damagepercentofmaxhp", ""); return; }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value)) return;
        var clamped = Math.Clamp(value, 0, 1);
        var normalized = clamped == value ? text : clamped.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(normalized, text, StringComparison.Ordinal))
            SetBoxTextWithoutEditing(_witherDamageBox, normalized);
        SetOrRemoveSpecificField("damagepercentofmaxhp", normalized);
    }

    private void RefreshArcadianFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("ArcadianMeadow", StringComparison.OrdinalIgnoreCase) != true)
            return;
        SetSpecificFieldVisibility(_arcadianPlacementBox, "terrainplacementcheck", entry);
        SetSpecificFieldVisibility(_arcadianDamageReceiverHost, "damagereceiverproto", entry);
        SetSpecificFieldVisibility(_arcadianBlockerHost, "spawnproto", entry);
        SetSpecificFieldVisibility(_arcadianBlockerDurationHost, "blockerradius", entry);
        var vfxOpen = PowerCatalog.FindField(entry, "vfxproto") != null;
        _arcadianVfxHost.IsVisible = vfxOpen;
        _addArcadianVfxButton.IsVisible = IsModifiedMode && !vfxOpen;
        _removeArcadianVfxButton.IsVisible = IsModifiedMode && vfxOpen;
        RefreshArcadianGrass(entry);
    }

    private void AddArcadianVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "vfxproto", "");
        RefreshArcadianFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _arcadianVfxBox.Focus();
    }

    private void RemoveArcadianVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "vfxproto") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        RefreshArcadianFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshArcadianGrass(XElement entry)
    {
        _arcadianGrassRows.Children.Clear();
        _arcadianGrassFields.Clear();
        foreach (var grass in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("vfxgrass", StringComparison.OrdinalIgnoreCase)))
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4) };
            var proto = CreateStrictSpecificBox(_protoUnitNames, grass.Value, value =>
            {
                grass.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
            group.Children.Add(proto);
            group.Children.Add(new TextBlock { Text = "r", VerticalAlignment = VerticalAlignment.Center });
            var radius = new TextBox { Text = EditorNumericFieldStyle.FormatDisplay(grass.Attribute("r")?.Value),
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(radius);
            group.Children.Add(radius);
            group.Children.Add(new TextBlock { Text = "theta", VerticalAlignment = VerticalAlignment.Center });
            var theta = new TextBox { Text = EditorNumericFieldStyle.FormatDisplay(grass.Attribute("theta")?.Value),
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(theta);
            group.Children.Add(theta);
            void CommitAttribute(TextBox box, string attribute)
            {
                if (_isPopulating || !IsModifiedMode ||
                    !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number)) return;
                if (double.TryParse(grass.Attribute(attribute)?.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var original) && original.Equals(number)) return;
                grass.SetAttributeValue(attribute, box.Text!.Trim()); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }
            radius.TextChanged += (_, _) => CommitAttribute(radius, "r");
            theta.TextChanged += (_, _) => CommitAttribute(theta, "theta");
            if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(grass);
                RefreshArcadianGrass(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _arcadianGrassFields.Add((proto, radius, theta, grass));
            _arcadianGrassRows.Children.Add(group);
        }
        _addArcadianGrassButton.IsVisible = IsModifiedMode;
    }

    private void AddArcadianGrass_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var grass = PowerCatalog.AddRepeatedField(_currentEntry, "vfxgrass", "");
        grass.SetAttributeValue("r", "0");
        grass.SetAttributeValue("theta", "0");
        RefreshArcadianGrass(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _arcadianGrassFields.LastOrDefault().Proto?.Focus();
    }

    private void RefreshUnderworldFields(XElement entry)
    {
        if (entry.Attribute("type")?.Value.Trim().Equals("UnderworldInvasion",
                StringComparison.OrdinalIgnoreCase) != true) return;

        foreach (var (control, tag) in new (Control Control, string Tag)[]
                 {
                     (_underworldEidolonMinBox, "eidolonmin"),
                     (_underworldEidolonMaxBox, "maxeidoloncount"),
                     (_underworldEidolonStepBox, "eidolonstep"),
                     (_underworldEidolonTypeBox, "eidolontype"),
                     (_underworldSpawnRadiusBox, "eidolonspawnradius"),
                     (_underworldSpawnDurationBox, "eidolonspawnduration")
                 })
            SetSpecificFieldVisibility(control, tag, entry);

        RefreshUnderworldOptionalGroup(entry, _addUnderworldMultipliersButton,
            _underworldMultiplierFields, _removeUnderworldMultipliersButton, null,
            ["eidolonmaxhpmultiplier", "eidolonmaxspeedmultiplier"]);
        RefreshUnderworldOptionalGroup(entry, _addUnderworldVfxButton,
            _underworldVfxFields, _removeUnderworldVfxButton, _underworldVfxLabel,
            ["vfxproto", "vfxeidolonspawnproto", "vfxforunitswithoutbirthanims", "vfxcurseproto"]);
        _underworldLightSetBox.ItemsSource = GetPowerFieldSuggestions("lightset")
            .Concat(GetPowerFieldSuggestions("lighting"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _underworldLightSetBox.Text = PowerCatalog.FindField(entry, "lightset")?.Value ?? "";
        RefreshUnderworldOptionalGroup(entry, _addUnderworldLightingButton,
            _underworldLightingFields, _removeUnderworldLightingButton, _underworldLightingLabel,
            ["lightset", "lightsetbuilduptime", "lightsetduration", "lightsetshutdowntime"]);
        RefreshUnderworldOptionalGroup(entry, _addUnderworldTechEffectButton,
            _underworldTechEffectFields, _removeUnderworldTechEffectButton,
            _underworldTechEffectLabel,
            ["durationtecheffect", "onlyapplydurationtecheffectiftechactive"]);
        RefreshUnderworldOptionalGroup(entry, _addUnderworldSuddenDeathButton,
            _underworldSuddenDeathFields, _removeUnderworldSuddenDeathButton,
            _underworldSuddenDeathLabel, ["suddendeathhpmultiplier"]);

        var onlyIfOpen = PowerCatalog.FindField(entry, "onlyapplydurationtecheffectiftechactive") != null;
        _underworldOnlyIfTechFields.IsVisible = onlyIfOpen;
        _addUnderworldOnlyIfTechButton.IsVisible = IsModifiedMode && !onlyIfOpen;
        _removeUnderworldOnlyIfTechButton.IsVisible = IsModifiedMode && onlyIfOpen;
    }

    private void RefreshUnderworldOptionalGroup(XElement entry, Button add, Control fields,
        Button remove, TextBlock? label, string[] tags)
    {
        var open = tags.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        add.IsVisible = IsModifiedMode && !open;
        fields.IsVisible = open;
        remove.IsVisible = IsModifiedMode && open;
        if (label != null) label.IsVisible = open;
    }

    private void AddUnderworldMultipliers_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("eidolonmaxhpmultiplier", _underworldMaxHpMultiplierBox, "0");

    private void RemoveUnderworldMultipliers_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("eidolonmaxhpmultiplier", "eidolonmaxspeedmultiplier");

    private void AddUnderworldVfx_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("vfxproto", _underworldStartVfxBox, "");

    private void RemoveUnderworldVfx_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("vfxproto", "vfxeidolonspawnproto", "vfxforunitswithoutbirthanims",
            "vfxcurseproto");

    private void AddUnderworldLighting_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("lightset", _underworldLightSetBox, "");

    private void RemoveUnderworldLighting_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("lightset", "lightsetbuilduptime", "lightsetduration",
            "lightsetshutdowntime");

    private void AddUnderworldTechEffect_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("durationtecheffect", _underworldTechEffectBox, "");

    private void RemoveUnderworldTechEffect_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("durationtecheffect",
            "onlyapplydurationtecheffectiftechactive");

    private void AddUnderworldOnlyIfTech_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("onlyapplydurationtecheffectiftechactive",
            _underworldOnlyIfTechBox, "");

    private void RemoveUnderworldOnlyIfTech_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("onlyapplydurationtecheffectiftechactive");

    private void AddUnderworldSuddenDeath_Click(object? sender, RoutedEventArgs e)
        => AddUnderworldOptionalField("suddendeathhpmultiplier", _underworldSuddenDeathBox, "0");

    private void RemoveUnderworldSuddenDeath_Click(object? sender, RoutedEventArgs e)
        => RemoveUnderworldOptionalFields("suddendeathhpmultiplier");

    private void AddUnderworldOptionalField(string tag, Control focus, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("UnderworldInvasion",
                StringComparison.OrdinalIgnoreCase) != true) return;
        PowerCatalog.SetField(_currentEntry, tag, value);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        focus.Focus();
    }

    private void RemoveUnderworldOptionalFields(params string[] tags)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in tags)
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshPowerCloneFields(XElement entry)
    {
        var isPowerClone = entry.Attribute("type")?.Value.Trim()
            .Equals("PowerClone", StringComparison.OrdinalIgnoreCase) == true;
        _powerCloneCopyCostCheck.IsVisible = false;
        _powerCloneCopyRepeatCostCheck.IsVisible = false;
        if (!isPowerClone)
            return;
        var slot = PowerCatalog.FindField(entry, "powerslot")?.Value.Trim() ?? "";
        _powerCloneSlotBox.SelectedItem = Enumerable.Range(1, 5).Select(value => value.ToString())
            .FirstOrDefault(value => value == slot);
        _powerCloneSlotBox.IsVisible = IsModifiedMode || slot.Length > 0;

        var cost = PowerCatalog.FindField(entry, "cost");
        var repeatCost = PowerCatalog.FindField(entry, "repeatcost");
        _powerCloneCopyCostCheck.IsVisible = IsModifiedMode || cost?.Attribute("copycostoftargetpower") != null;
        _powerCloneCopyRepeatCostCheck.IsVisible = IsModifiedMode ||
            repeatCost?.Attribute("copyrepeatcostoftargetpower") != null;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _powerCloneCopyCostCheck.IsChecked = cost?.Attribute("copycostoftargetpower") != null;
            _powerCloneCopyRepeatCostCheck.IsChecked = repeatCost?.Attribute("copyrepeatcostoftargetpower") != null;
        }
        finally { _isPopulating = wasPopulating; }
        SetSpecificFieldVisibility(_powerClonePlacementVfxBox, "placementvfx", entry);

        var replacement = PowerCatalog.FindField(entry, "duplicatetargetreplacement");
        var seeded = replacement?.Attribute("seeded") != null;
        wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _powerCloneSeededCheck.IsChecked = seeded; }
        finally { _isPopulating = wasPopulating; }
        _powerCloneSeededCheck.IsVisible = IsModifiedMode || seeded;
        var targets = replacement?.Elements().Where(element =>
            element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        var selected = targets.Select(element => element.Value.Trim())
            .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var powerNames = _baseEntries.Keys.Concat(GetAllModPowerNames())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        _powerCloneReplacementPicker.ItemsSource = powerNames.Where(value => !selected.Contains(value)).ToList();
        _powerCloneReplacementPicker.SelectedItem = null;
        _powerCloneReplacementPicker.Text = "";
        _powerCloneReplacementPicker.IsVisible = IsModifiedMode && selected.Count < powerNames.Count;
        _powerCloneReplacementChips.Children.Clear();
        foreach (var target in targets.Where(element => !string.IsNullOrWhiteSpace(element.Value)))
        {
            var captured = target;
            _powerCloneReplacementChips.Children.Add(PowerEditorDynamicControls.CreateChip(target.Value.Trim(),
                IsModifiedMode ? () =>
                {
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(captured);
                    RemoveEmptyPowerCloneReplacement(entry);
                    RefreshPowerCloneFields(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
        }
    }

    private void PowerCloneSlot_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                StringComparison.OrdinalIgnoreCase) != true ||
            _powerCloneSlotBox.SelectedItem is not string value ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) ||
            slot is < 1 or > 5) return;
        SetOrRemoveSpecificField("powerslot", value);
    }

    private void PowerCloneCopyCost_Changed(CheckBox check, string tag, string attribute)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                StringComparison.OrdinalIgnoreCase) != true) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (check.IsChecked == true)
        {
            field ??= PowerCatalog.SetField(_currentEntry, tag, "0");
            field.SetAttributeValue(attribute, "true");
        }
        else
            field?.SetAttributeValue(attribute, null);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void PowerCloneSeeded_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                StringComparison.OrdinalIgnoreCase) != true) return;
        var replacement = PowerCatalog.FindField(_currentEntry, "duplicatetargetreplacement");
        if (_powerCloneSeededCheck.IsChecked == true)
        {
            replacement ??= PowerCatalog.SetField(_currentEntry, "duplicatetargetreplacement", "");
            replacement.SetAttributeValue("seeded", "");
        }
        else
        {
            replacement?.SetAttributeValue("seeded", null);
            RemoveEmptyPowerCloneReplacement(_currentEntry);
        }
        RefreshPowerCloneFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void PowerCloneReplacementPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                StringComparison.OrdinalIgnoreCase) != true ||
            _powerCloneReplacementPicker.SelectedItem is not string value) return;
        var powerNames = _baseEntries.Keys.Concat(GetAllModPowerNames()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!powerNames.Contains(value)) return;
        var replacement = PowerCatalog.FindField(_currentEntry, "duplicatetargetreplacement") ??
                          PowerCatalog.SetField(_currentEntry, "duplicatetargetreplacement", "");
        if (!replacement.Elements().Any(element =>
                element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
            replacement.Add(new XElement("target", value));
        RefreshPowerCloneFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static void RemoveEmptyPowerCloneReplacement(XElement entry)
    {
        var replacement = PowerCatalog.FindField(entry, "duplicatetargetreplacement");
        if (replacement is { HasAttributes: false } && !replacement.Elements().Any() &&
            string.IsNullOrWhiteSpace(replacement.Value))
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(replacement);
    }

    private void SetSpecificFieldVisibility(Control control, string tag, XElement entry)
        => control.IsVisible = IsModifiedMode || PowerCatalog.FindField(entry, tag) != null;

    private void RefreshTempestOptionalGroup(XElement entry, Button add, Control fields, Button remove,
        TextBlock label, string[] tags)
    {
        var open = tags.Any(tag => PowerCatalog.FindField(entry, tag) != null);
        add.IsVisible = IsModifiedMode && !open; fields.IsVisible = open;
        label.IsVisible = open;
        remove.IsVisible = IsModifiedMode && open;
    }

    private void RefreshTempestVfx(XElement entry)
    {
        var fields = new[]
        {
            (Tag: "effectproto", Host: (Control)_tempestEffectHost, Add: _addTempestEffectButton,
                Remove: _removeTempestEffectButton),
            (Tag: "groundvfxproto", Host: (Control)_tempestGroundHost, Add: _addTempestGroundButton,
                Remove: _removeTempestGroundButton),
            (Tag: "groundimpactvfxproto", Host: (Control)_tempestImpactHost, Add: _addTempestImpactButton,
                Remove: _removeTempestImpactButton),
            (Tag: "icesheetvfxproto", Host: (Control)_tempestIceHost, Add: _addTempestIceButton,
                Remove: _removeTempestIceButton)
        };
        var any = fields.Any(item => PowerCatalog.FindField(entry, item.Tag) != null);
        var open = any || _tempestVfxOpen;
        _addTempestVfxButton.IsVisible = IsModifiedMode && !open;
        _tempestVfxLabel.IsVisible = open;
        _tempestVfxFields.IsVisible = open;
        foreach (var item in fields)
        {
            var present = PowerCatalog.FindField(entry, item.Tag) != null;
            item.Host.IsVisible = present; item.Add.IsVisible = IsModifiedMode && !present;
            item.Remove.IsVisible = IsModifiedMode && present;
        }
    }

    private void RefreshTempestLargeTimings(XElement entry)
    {
        _tempestLargeTimingRows.Children.Clear();
        var container = PowerCatalog.FindField(entry, "largestrikeintervaltiming");
        foreach (var time in container?.Elements().Where(item =>
                     item.Name.LocalName.Equals("time", StringComparison.OrdinalIgnoreCase)).ToList() ?? [])
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4) };
            var box = new TextBox { Width = 90, Text = EditorNumericFieldStyle.FormatDisplay(time.Value),
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property != TextBox.TextProperty) return;
                if (_isPopulating || !IsModifiedMode || !double.TryParse(box.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return;
                time.Value = box.Text!.Trim(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            group.Children.Add(box);
            if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(time);
                if (container != null && !container.Elements().Any())
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(container);
                RefreshTempestLargeTimings(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _tempestLargeTimingRows.Children.Add(group);
        }
        _addTempestLargeTimingButton.IsVisible = IsModifiedMode && PowerCatalog.FindField(entry, "largestrikeproto") != null;
    }

    private void AddTempestLargeStrike_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "largestrikeproto", "");
        RefreshTempestFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _tempestLargeStrikeUnitBox.Focus();
    }

    private void AddTempestSound_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "strikesoundset", "");
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _tempestStrikeSoundBox.Focus();
    }

    private void RemoveTempestSound_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "strikesoundset") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RemoveTempestLargeStrike_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[] { "largestrikeproto", "largestrikeintervaltiming", "largestrikeavoidswater",
                     "largestrikeavoidsbuildings", "largestrikeavoidslandimpassable", "largestrikesoundset" })
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddTempestAttack_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "areaattackaction", "");
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _tempestAreaAttackActionBox.Focus();
    }

    private void RemoveTempestAttack_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in new[] { "areaattackaction", "radialtargeting" })
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddTempestLargeTiming_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var container = PowerCatalog.FindField(_currentEntry, "largestrikeintervaltiming") ??
                        PowerCatalog.SetField(_currentEntry, "largestrikeintervaltiming", "");
        AddIndentedTempestTiming(container, "0");
        RefreshTempestLargeTimings(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private static void AddIndentedTempestTiming(XElement container, string value)
    {
        var parentIndent = (container.PreviousNode as XText)?.Value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "  ";
        var childIndent = parentIndent + (parentIndent.Contains('\t') ? "\t" : "  ");
        var time = new XElement("time", value);
        if (container.Nodes().LastOrDefault() is XText trailing &&
            string.IsNullOrWhiteSpace(trailing.Value))
            trailing.AddBeforeSelf(new XText("\n" + childIndent), time);
        else
            container.Add(new XText("\n" + childIndent), time, new XText("\n" + parentIndent));
    }

    private void TempestAvoidPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _tempestAvoidPicker.SelectedItem is not string label) return;
        var item = TempestAvoids.FirstOrDefault(value => value.Label == label);
        if (item.XmlName == null) return;
        PowerCatalog.SetField(_currentEntry, item.XmlName, "true");
        RefreshTempestFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void TempestRadialTargeting_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (_tempestRadialTargetingCheck.IsChecked == true) PowerCatalog.SetField(_currentEntry, "radialtargeting", "");
        else if (PowerCatalog.FindField(_currentEntry, "radialtargeting") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void MeteorLargestGroup_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_meteorLargestGroupCheck.IsVisible) return;
        var field = PowerCatalog.FindField(_currentEntry, "uselargestgroupbelowminsize");
        if (_meteorLargestGroupCheck.IsChecked == true)
        {
            if (field == null) PowerCatalog.SetField(_currentEntry, "uselargestgroupbelowminsize", "1");
        }
        else if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddTempestVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _tempestVfxOpen = true;
        RefreshTempestVfx(_currentEntry);
    }

    private void AddTempestVfxField_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }) AddTempestVfxTag(tag);
    }

    private void AddTempestVfxTag(string tag)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, tag, ""); PopulateSpecificFields(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveTempestVfxField_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || sender is not Button { Tag: string tag }) return;
        if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        RefreshTempestVfx(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddTempestClouds_Click(object? sender, RoutedEventArgs e)
        => AddTempestGroup([("cloudbuildupstarttime", "0"), ("cloudbuildupendtime", "0"),
            ("cloudfadeoutstarttime", "0"), ("cloudfadeoutendtime", "0")]);
    private void AddTempestDrift_Click(object? sender, RoutedEventArgs e)
        => AddTempestGroup([("slowestdrifttime", "0"), ("fastestdrifttime", "0")]);
    private void AddTempestTreeBend_Click(object? sender, RoutedEventArgs e)
        => AddTempestGroup([("treebendminstrength", "0"), ("treebendmaxstrength", "0"),
            ("treebendminperiod", "0"), ("treebendmaxperiod", "0"),
            ("treebendminamplitude", "0"), ("treebendmaxamplitude", "0"),
            ("treebendangleoffset", "0"), ("treebendingduration", "0")]);
    private void RemoveTempestClouds_Click(object? sender, RoutedEventArgs e) => RemoveTempestGroup(
        ["cloudbuildupstarttime", "cloudbuildupendtime", "cloudfadeoutstarttime", "cloudfadeoutendtime"]);
    private void RemoveTempestDrift_Click(object? sender, RoutedEventArgs e) => RemoveTempestGroup(
        ["slowestdrifttime", "fastestdrifttime"]);
    private void RemoveTempestTreeBend_Click(object? sender, RoutedEventArgs e) => RemoveTempestGroup(
        ["treebendminstrength", "treebendmaxstrength", "treebendminperiod", "treebendmaxperiod",
         "treebendminamplitude", "treebendmaxamplitude", "treebendangleoffset", "treebendingduration"]);

    private void AddLightningVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || sender is not Button { Tag: string tag })
            return;
        PowerCatalog.SetField(_currentEntry, tag, "");
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        (tag == "rainvfxproto" ? _lightningRainVfxBox : _lightningCloudVfxBox).Focus();
    }

    private void RemoveLightningVfx_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || sender is not Button { Tag: string tag })
            return;
        if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddLightningRain_Click(object? sender, RoutedEventArgs e)
        => AddTempestGroup([("rainbuildupstarttime", "0"), ("rainbuildupendtime", "0"),
            ("rainfadeoutstarttime", "0"), ("rainfadeoutendtime", "0")]);

    private void RemoveLightningRain_Click(object? sender, RoutedEventArgs e) => RemoveTempestGroup(
        ["rainbuildupstarttime", "rainbuildupendtime", "rainfadeoutstarttime", "rainfadeoutendtime"]);

    private void AddEarthquakeDustVfx_Click(object? sender, RoutedEventArgs e)
        => AddTempestGroup([("dustsmallvfxproto", ""), ("dustmediumvfxproto", ""),
            ("dustlargevfxproto", "")]);

    private void RemoveEarthquakeDustVfx_Click(object? sender, RoutedEventArgs e) => RemoveTempestGroup(
        ["dustsmallvfxproto", "dustmediumvfxproto", "dustlargevfxproto"]);

    private void AddTempestGroup((string Tag, string Value)[] fields)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var field in fields) PowerCatalog.SetField(_currentEntry, field.Tag, field.Value);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveTempestGroup(string[] tags)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        foreach (var tag in tags)
            if (PowerCatalog.FindField(_currentEntry, tag) is { } field)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshThrowExclusions(XElement entry)
    {
        RefreshThrowExclusion(entry, "excludefrompullingintofunnel", _throwExcludePullOpen,
            _throwExcludePullHost, _throwExcludePullPicker, _throwExcludePullChips,
            _addThrowExcludePullButton, _removeThrowExcludePullButton);
        RefreshThrowExclusion(entry, "excludefromthrow", _throwExcludeThrowOpen,
            _throwExcludeThrowHost, _throwExcludeThrowPicker, _throwExcludeThrowChips,
            _addThrowExcludeThrowButton, _removeThrowExcludeThrowButton);
        var thunderBurst = entry.Attribute("type")?.Value.Trim()
            .Equals("ThunderBurst", StringComparison.OrdinalIgnoreCase) == true;
        var sizeLimit = PowerCatalog.FindField(entry, "throwsizelimit");
        _throwSizeLimitHost.IsVisible = thunderBurst && sizeLimit != null;
        _addThrowSizeLimitButton.IsVisible = thunderBurst && IsModifiedMode && sizeLimit == null;
        _removeThrowSizeLimitButton.IsVisible = thunderBurst && IsModifiedMode && sizeLimit != null;
    }

    private void AddThrowSizeLimit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        PowerCatalog.SetField(_currentEntry, "throwsizelimit", "");
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        _throwSizeLimitBox.Focus();
    }

    private void RemoveThrowSizeLimit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "throwsizelimit") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        PopulateSpecificFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshThrowExclusion(XElement entry, string tag, bool requestedOpen, Control host,
        AutoCompleteBox picker, WrapPanel chips, Button add, Button remove)
    {
        var elements = entry.Elements().Where(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        var open = requestedOpen || elements.Count > 0;
        host.IsVisible = open && (IsModifiedMode || elements.Count > 0);
        add.IsVisible = IsModifiedMode && !open;
        remove.IsVisible = IsModifiedMode && open;
        var selected = elements.Select(element => element.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        picker.ItemsSource = _unitOrTypeNames.Where(value => !selected.Contains(value)).ToList();
        picker.IsVisible = IsModifiedMode && open && selected.Count < _unitOrTypeNames.Count;
        picker.SelectedItem = null; picker.Text = "";
        chips.Children.Clear();
        foreach (var element in elements)
            chips.Children.Add(PowerEditorDynamicControls.CreateChip(element.Value.Trim(), IsModifiedMode ? () =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshThrowExclusions(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } : null, readOnly: !IsModifiedMode));
    }

    private void AddThrowExcludePull_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _throwExcludePullOpen = true; RefreshThrowExclusions(_currentEntry);
        _throwExcludePullPicker.Focus();
    }

    private void AddThrowExcludeThrow_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _throwExcludeThrowOpen = true; RefreshThrowExclusions(_currentEntry);
        _throwExcludeThrowPicker.Focus();
    }

    private void ThrowExclusionPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            sender is not AutoCompleteBox picker || picker.SelectedItem is not string selected) return;
        var tag = ReferenceEquals(picker, _throwExcludePullPicker)
            ? "excludefrompullingintofunnel" : "excludefromthrow";
        if (!_unitOrTypeNames.Contains(selected, StringComparer.OrdinalIgnoreCase) ||
            _currentEntry.Elements().Any(element => element.Name.LocalName.Equals(tag,
                StringComparison.OrdinalIgnoreCase) && element.Value.Trim().Equals(selected,
                StringComparison.OrdinalIgnoreCase))) return;
        PowerCatalog.AddRepeatedField(_currentEntry, tag, selected);
        RefreshThrowExclusions(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RemoveThrowExclusion_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            sender is not Button { Tag: string tag }) return;
        foreach (var element in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
        if (tag.Equals("excludefrompullingintofunnel", StringComparison.OrdinalIgnoreCase))
            _throwExcludePullOpen = false;
        else _throwExcludeThrowOpen = false;
        RefreshThrowExclusions(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshTempUnitFields(XElement entry)
    {
        _tempUnitCreateRows.Children.Clear();
        _freyrsGiftCreateRows.Children.Clear();
        _tempUnitCreateFields.Clear();
        foreach (var create in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("createunit", StringComparison.OrdinalIgnoreCase)))
            BuildTempUnitCreateRow(create, _tempUnitCreateRows, ageSpecific: false);
        var ageSpecificCreates = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("createagespecificunit", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var create in ageSpecificCreates)
            BuildTempUnitCreateRow(create, _freyrsGiftCreateRows, ageSpecific: true);
        _addTempUnitCreateButton.IsVisible = IsModifiedMode;
        _addFreyrsGiftCreateButton.IsVisible = IsModifiedMode &&
            entry.Attribute("type")?.Value.Trim().Equals("FreyrsGift", StringComparison.OrdinalIgnoreCase) == true &&
            ageSpecificCreates.Count < StandardAges.Length &&
            ageSpecificCreates.Select(create => create.Attribute("age")?.Value.Trim())
                .Where(age => !string.IsNullOrWhiteSpace(age)).Distinct(StringComparer.OrdinalIgnoreCase).Count() <
            StandardAges.Length;
        RefreshTempUnitAttach(entry);
        RefreshTempUnitAges(entry);
        _tempUnitSmoothRotationCheck.IsChecked = PowerCatalog.FindField(entry, "smoothrotation") != null;
        _tempUnitForcePlacementCheck.IsChecked = PowerCatalog.FindField(entry, "forceplacement") != null;
        _tempUnitForceOrientationCheck.IsChecked =
            PowerCatalog.FindField(entry, "forceuseplacementorientation") != null;
        foreach (var check in new[] { _tempUnitSmoothRotationCheck, _tempUnitForcePlacementCheck,
                     _tempUnitForceOrientationCheck })
        {
            check.IsEnabled = IsModifiedMode;
            check.IsVisible = IsModifiedMode || check.IsChecked == true;
        }
        if (!IsModifiedMode)
            foreach (var button in Descendants(_tempUnitCreateRows).OfType<Button>().Where(button =>
                         button.Classes.Contains("remove-button")))
                button.IsVisible = false;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children)
                foreach (var descendant in Descendants(child)) yield return descendant;
        else if (root is Border { Child: Control child })
            foreach (var descendant in Descendants(child)) yield return descendant;
        else if (root is ContentControl { Content: Control content })
            foreach (var descendant in Descendants(content)) yield return descendant;
    }

    private void BuildTempUnitCreateRow(XElement create, Panel rows, bool ageSpecific)
    {
        var (container, card) = PowerEditorDynamicControls.CreateCard();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var subjectRow = new WrapPanel();
        var hasSelection = _tempUnitSelectionEntries.Contains(create) || create.Value.Contains('|');
        var mode = new ComboBox { Width = 110, ItemsSource = TempUnitCreateModes,
            SelectedItem = hasSelection ? "Selection" : "Unit", IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 8, 4) };
        subjectRow.Children.Add(mode);
        var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Margin = new Thickness(0, 0, 8, 4) };
        subjectRow.Children.Add(host);
        if (ageSpecific)
        {
            subjectRow.Children.Add(new TextBlock { Text = "Age", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4) });
            var usedByOthers = create.Parent?.Elements().Where(element =>
                    !ReferenceEquals(element, create) && element.Name.LocalName.Equals(
                        "createagespecificunit", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attribute("age")?.Value.Trim() ?? "")
                .Where(age => age.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var currentAge = create.Attribute("age")?.Value.Trim() ?? "";
            var ageOptions = StandardAges.Where(age => !usedByOthers.Contains(age) ||
                age.Equals(currentAge, StringComparison.OrdinalIgnoreCase)).ToList();
            if (currentAge.Length > 0 && !ageOptions.Contains(currentAge, StringComparer.OrdinalIgnoreCase))
                ageOptions.Add(currentAge);
            var age = new ComboBox { Width = 140, ItemsSource = ageOptions,
                SelectedItem = ageOptions.FirstOrDefault(value => value.Equals(currentAge,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode,
                Margin = new Thickness(0, 0, 8, 4) };
            age.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || age.SelectedItem is not string selected) return;
                create.SetAttributeValue("age", selected);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(create, () => RefreshTempUnitFields(_currentEntry!));
            };
            subjectRow.Children.Add(age);
        }
        top.Children.Add(subjectRow);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Click += (_, _) =>
            {
                if (!CanEditProperties(_currentEntry!)) return;
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(create);
                RefreshTempUnitFields(_currentEntry!); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            Grid.SetColumn(remove, 1);
            top.Children.Add(remove);
        }
        card.Children.Add(top);

        var details = new WrapPanel();
        AddTempUnitNumber(details, "Quantity", create, "quantity", integer: true, minimumZero: true);
        BuildTempUnitRadiusControls(details, create);
        AddTempUnitNumber(details, "Delay", create, "delay");
        BuildTempUnitLifespanControls(details, create);
        card.Children.Add(details);
        BuildTempUnitFlagControls(card, create);
        BuildTempUnitSoundControls(card, create);
        rows.Children.Add(container);
        _tempUnitCreateFields.Add(new TempUnitCreateFields { Element = create, Mode = mode, Host = host });

        void RefreshSubject()
        {
            host.Children.Clear();
            if ((mode.SelectedItem as string)?.Equals("Selection", StringComparison.OrdinalIgnoreCase) == true)
                BuildTempUnitSelectionEditor(host, create);
            else
                BuildTempUnitUnitEditor(host, create);
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            var selection = (mode.SelectedItem as string)?.Equals("Selection",
                StringComparison.OrdinalIgnoreCase) == true;
            if (selection) _tempUnitSelectionEntries.Add(create);
            else _tempUnitSelectionEntries.Remove(create);
            var values = SplitTempUnitSelection(create.Value);
            create.Value = selection ? string.Join('|', values) : values.FirstOrDefault() ?? "";
            RefreshSubject(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        RefreshSubject();
    }

    private void BuildTempUnitUnitEditor(StackPanel host, XElement create)
    {
        var unit = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        EditorAutoCompleteService.ConfigureStrict(unit, _protoUnitNames, create.Value.Trim(), () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (!IsModifiedMode || (value.Length > 0 &&
                    !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))) return;
                if (create.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return;
                create.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(unit, _specificScroll);
        unit.Text = create.Value.Trim();
        host.Children.Add(unit);
        var waterAttribute = create.Attribute("waterprotoid");
        var water = waterAttribute?.Value ?? "";
        if (waterAttribute != null || IsModifiedMode)
        {
            if (waterAttribute == null)
            {
                var add = CreateTempUnitAddButton("Water Unit", () =>
                {
                    create.SetAttributeValue("waterprotoid", ""); RefreshCurrentTempUnit(create);
                });
                host.Children.Add(add);
            }
            else
            {
                host.Children.Add(new TextBlock { Text = "Water Unit", VerticalAlignment = VerticalAlignment.Center });
                var waterBox = CreateTempUnitProtoBox(water, value =>
                    SetTempUnitAttribute(create, "waterprotoid", value));
                host.Children.Add(waterBox);
                host.Children.Add(CreateTempUnitRemoveButton(() =>
                {
                    create.SetAttributeValue("waterprotoid", null); RefreshCurrentTempUnit(create);
                }));
            }
        }
    }

    private void BuildTempUnitSelectionEditor(StackPanel host, XElement create)
    {
        var picker = new AutoCompleteBox { Width = 50, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void Refresh()
        {
            var values = SplitTempUnitSelection(create.Value);
            chips.Children.Clear();
            foreach (var value in values)
            {
                var captured = value;
                chips.Children.Add(PowerEditorDynamicControls.CreateChip(captured, IsModifiedMode ? () =>
                {
                    create.Value = string.Join('|', SplitTempUnitSelection(create.Value).Where(item =>
                        !item.Equals(captured, StringComparison.OrdinalIgnoreCase)));
                    Refresh(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                } : null, readOnly: !IsModifiedMode));
            }
            picker.ItemsSource = _protoUnitNames.Where(name =>
                !values.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        }
        EditorAutoCompleteService.ConfigureStrict(picker, _protoUnitNames, "", () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (!IsModifiedMode || value.Length == 0) return;
                var values = SplitTempUnitSelection(create.Value);
                if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
                create.Value = string.Join('|', values);
                picker.Text = ""; Refresh(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(picker, _specificScroll);
        host.Children.Add(picker); host.Children.Add(chips); Refresh();
    }

    private static List<string> SplitTempUnitSelection(string value)
        => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private AutoCompleteBox CreateTempUnitProtoBox(string value, Action<string> commit)
    {
        var committedValue = value.Trim();
        var box = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        EditorAutoCompleteService.ConfigureStrict(box, _protoUnitNames, value, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: committed =>
            {
                if (committed.Equals(committedValue, StringComparison.OrdinalIgnoreCase)) return;
                committedValue = committed;
                commit(committed);
            });
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
        box.Text = value;
        return box;
    }

    private void AddTempUnitNumber(Panel host, string label, XElement create, string attribute,
        bool integer = false, bool minimumZero = false)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Margin = new Thickness(0, 0, 14, 4) };
        group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Width = 90, Text = EditorNumericFieldStyle.FormatDisplay(
            create.Attribute(attribute)?.Value), IsEnabled = IsModifiedMode };
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        if (integer)
            EditorNumericInputBehavior.AttachRule(box, minimumZero
                ? ProtoUnitNumericKind.UnsignedInteger : ProtoUnitNumericKind.SignedInteger);
        box.TextChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            var text = box.Text?.Trim() ?? "";
            var valid = integer
                ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) &&
                  (!minimumZero || intValue >= 0)
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue) &&
                  double.IsFinite(floatValue) && (!minimumZero || floatValue >= 0);
            if (!valid) return;
            var original = create.Attribute(attribute)?.Value.Trim();
            var unchanged = string.Equals(original, text, StringComparison.Ordinal) ||
                            (integer && int.TryParse(original, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var originalInteger) &&
                             int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                 out var editedInteger) && originalInteger == editedInteger) ||
                            (!integer && double.TryParse(original, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var originalNumber) &&
                             double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out var editedNumber) && originalNumber.Equals(editedNumber));
            if (unchanged)
                return;
            create.SetAttributeValue(attribute, text); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        group.Children.Add(box); host.Children.Add(group);
    }

    private void BuildTempUnitRadiusControls(Panel host, XElement create)
    {
        var simple = create.Attribute("radius");
        var min = create.Attribute("minradius"); var max = create.Attribute("maxradius");
        if (simple == null && min == null && max == null)
        {
            if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton("Radius", () =>
            {
                create.SetAttributeValue("radius", "0"); RefreshCurrentTempUnit(create);
            }));
            return;
        }
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Margin = new Thickness(0, 0, 14, 4) };
        group.Children.Add(new TextBlock { Text = "Radius", VerticalAlignment = VerticalAlignment.Center });
        var mode = new ComboBox { Width = 110, ItemsSource = TempUnitRadiusModes,
            SelectedItem = min != null || max != null ? "Min/Max" : "Simple", IsEnabled = IsModifiedMode };
        group.Children.Add(mode);
        if ((mode.SelectedItem as string) == "Min/Max")
        {
            AddTempUnitNumber(group, "Min", create, "minradius");
            AddTempUnitNumber(group, "Max", create, "maxradius");
        }
        else AddTempUnitNumber(group, "", create, "radius");
        mode.SelectionChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            if ((mode.SelectedItem as string) == "Min/Max")
            {
                var value = create.Attribute("radius")?.Value ?? "0";
                create.SetAttributeValue("radius", null); create.SetAttributeValue("minradius", value);
                create.SetAttributeValue("maxradius", value);
            }
            else
            {
                var value = create.Attribute("minradius")?.Value ?? "0";
                create.SetAttributeValue("minradius", null); create.SetAttributeValue("maxradius", null);
                create.SetAttributeValue("radius", value);
            }
            RefreshCurrentTempUnit(create);
        };
        if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
        {
            create.SetAttributeValue("radius", null); create.SetAttributeValue("minradius", null);
            create.SetAttributeValue("maxradius", null); create.SetAttributeValue("pattern", null);
            RefreshCurrentTempUnit(create);
        }));
        host.Children.Add(group);

        var pattern = create.Attribute("pattern")?.Value;
        if (pattern != null)
        {
            var patternOptions = OptionsIncludingCurrent(TempUnitPatterns, pattern);
            var patternGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4) };
            patternGroup.Children.Add(new TextBlock { Text = "Pattern", VerticalAlignment = VerticalAlignment.Center });
            var combo = new ComboBox { Width = 110, ItemsSource = patternOptions,
                SelectedItem = patternOptions.FirstOrDefault(value => value.Equals(pattern,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            combo.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || combo.SelectedItem is not string value) return;
                SetTempUnitAttribute(create, "pattern", value.ToLowerInvariant());
            };
            patternGroup.Children.Add(combo);
            if (IsModifiedMode) patternGroup.Children.Add(CreateTempUnitRemoveButton(() =>
            { create.SetAttributeValue("pattern", null); RefreshCurrentTempUnit(create); }));
            host.Children.Add(patternGroup);
        }
        else if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton("Pattern", () =>
        { create.SetAttributeValue("pattern", "simple"); RefreshCurrentTempUnit(create); }));
    }

    private void BuildTempUnitLifespanControls(Panel host, XElement create)
    {
        var lifespan = create.Attribute("lifespan")?.Value;
        if (lifespan == null)
        {
            if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton("Lifespan", () =>
            { create.SetAttributeValue("lifespan", "0"); RefreshCurrentTempUnit(create); }));
            return;
        }
        AddTempUnitNumber(host, "Lifespan", create, "lifespan");
        if (IsModifiedMode) host.Children.Add(CreateTempUnitRemoveButton(() =>
        {
            create.SetAttributeValue("lifespan", null); create.SetAttributeValue("lifespantype", null);
            create.SetAttributeValue("removesilentlyatlifespanend", null); RefreshCurrentTempUnit(create);
        }));
        var lifespanType = create.Attribute("lifespantype")?.Value;
        if (lifespanType != null)
        {
            var lifespanOptions = OptionsIncludingCurrent(TempUnitLifespanTypes, lifespanType);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 14, 4) };
            group.Children.Add(new TextBlock { Text = "Lifespan Type", VerticalAlignment = VerticalAlignment.Center });
            var combo = new ComboBox { Width = 110, ItemsSource = lifespanOptions,
                SelectedItem = lifespanOptions.FirstOrDefault(value => value.Equals(lifespanType,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            combo.SelectionChanged += (_, _) =>
            { if (combo.SelectedItem is string value) SetTempUnitAttribute(create, "lifespantype", value); };
            group.Children.Add(combo);
            if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
            { create.SetAttributeValue("lifespantype", null); RefreshCurrentTempUnit(create); }));
            host.Children.Add(group);
        }
        else if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton("Lifespan Type", () =>
        { create.SetAttributeValue("lifespantype", "Relative"); RefreshCurrentTempUnit(create); }));
        var removeAtEnd = new CheckBox { Content = "Remove at lifespan end", IsEnabled = IsModifiedMode,
            IsChecked = create.Attribute("removesilentlyatlifespanend") != null,
            IsVisible = IsModifiedMode || create.Attribute("removesilentlyatlifespanend") != null,
            Margin = new Thickness(0, 0, 14, 4) };
        removeAtEnd.IsCheckedChanged += (_, _) =>
        {
            if (_isPopulating || !IsModifiedMode) return;
            create.SetAttributeValue("removesilentlyatlifespanend", removeAtEnd.IsChecked == true ? "1" : null);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        host.Children.Add(removeAtEnd);
    }

    private void BuildTempUnitFlagControls(StackPanel card, XElement create)
    {
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = "Flags", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4) });
        var selected = TempUnitFlags.Where(flag => create.Attributes().Any(attribute =>
            attribute.Name.LocalName.Equals(flag.XmlName, StringComparison.OrdinalIgnoreCase))).ToList();
        if (IsModifiedMode)
        {
            var picker = new AutoCompleteBox { Width = 50, FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0, ItemsSource = TempUnitFlags.Where(flag => !selected.Contains(flag))
                    .Select(flag => flag.Label).ToList() };
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(picker, _specificScroll);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string label) return;
                var flag = TempUnitFlags.First(value => value.Label == label);
                create.SetAttributeValue(flag.XmlName, "");
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(create, () => RefreshCurrentTempUnit(create));
            };
            row.Children.Add(picker);
        }
        foreach (var flag in selected)
            row.Children.Add(PowerEditorDynamicControls.CreateChip(flag.Label, IsModifiedMode ? () =>
            {
                create.SetAttributeValue(flag.XmlName, null); RefreshCurrentTempUnit(create);
            } : null, readOnly: !IsModifiedMode));
        if (IsModifiedMode || selected.Count > 0) card.Children.Add(row);
    }

    private void BuildTempUnitSoundControls(StackPanel card, XElement create)
    {
        var value = create.Attribute("createunitsoundset")?.Value;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (value == null)
        {
            if (IsModifiedMode) row.Children.Add(CreateTempUnitAddButton("Soundset", () =>
            { create.SetAttributeValue("createunitsoundset", ""); RefreshCurrentTempUnit(create); }));
        }
        else
        {
            row.Children.Add(new TextBlock { Text = "On creation", VerticalAlignment = VerticalAlignment.Center });
            var box = new AutoCompleteBox { Width = 240, ItemsSource = GetSoundSetSuggestions(),
                Text = value, FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0,
                IsEnabled = IsModifiedMode };
            EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            box.PropertyChanged += (_, change) =>
            {
                if (change.Property == AutoCompleteBox.TextProperty && IsModifiedMode)
                    SetTempUnitAttribute(create, "createunitsoundset", box.Text?.Trim() ?? "");
            };
            row.Children.Add(box);
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            { create.SetAttributeValue("createunitsoundset", null); RefreshCurrentTempUnit(create); }));
        }
        if (row.Children.Count > 0) card.Children.Add(row);
    }

    private Button CreateTempUnitAddButton(string label, Action action)
    {
        var button = PowerEditorDynamicControls.CreateOptionalButton(label);
        button.Margin = new Thickness(0, 0, 8, 4);
        button.Click += (_, _) => action(); return button;
    }

    private Button CreateTempUnitRemoveButton(Action action)
    {
        var button = PowerEditorDynamicControls.CreateRemoveButton();
        button.Margin = new Thickness(2, 0, 0, 4);
        button.Click += (_, _) => action(); return button;
    }

    private void SetTempUnitAttribute(XElement create, string name, string value)
    {
        if (_isPopulating || !IsModifiedMode) return;
        if (string.Equals(create.Attribute(name)?.Value, value, StringComparison.OrdinalIgnoreCase)) return;
        create.SetAttributeValue(name, value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshCurrentTempUnit(XElement create)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        RefreshTempUnitFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddTempUnitCreate_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var create = PowerCatalog.AddRepeatedField(_currentEntry, "createunit", "");
        create.SetAttributeValue("quantity", "1");
        create.SetAttributeValue("delay", "0");
        RefreshTempUnitFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void AddFreyrsGiftCreate_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("FreyrsGift",
                StringComparison.OrdinalIgnoreCase) != true) return;
        var usedAges = _currentEntry.Elements().Where(element => element.Name.LocalName.Equals(
                "createagespecificunit", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (usedAges.Count >= StandardAges.Length) return;
        var usedAgeNames = usedAges.Select(element => element.Attribute("age")?.Value.Trim() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextAge = StandardAges.FirstOrDefault(age => !usedAgeNames.Contains(age));
        if (nextAge == null) return;
        var create = PowerCatalog.AddRepeatedField(_currentEntry, "createagespecificunit", "");
        create.SetAttributeValue("age", nextAge);
        create.SetAttributeValue("quantity", "1");
        create.SetAttributeValue("delay", "0");
        RefreshTempUnitFields(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshStampedeFields(XElement entry)
    {
        var stampede = PowerCatalog.FindField(entry, "stampede");
        PopulateStrictProtoBox(_stampedeUnitBox, stampede?.Value);
        PopulateStrictActionBox(_stampedeAttackActionBox, stampede?.Attribute("attackaction")?.Value);
        _stampedeScaleBox.Text = EditorNumericFieldStyle.FormatDisplay(stampede?.Attribute("scale")?.Value);
        _stampedeAnimRateBox.Text = EditorNumericFieldStyle.FormatDisplay(stampede?.Attribute("animrate")?.Value);
        _stampedeAttackIntervalBox.Text = EditorNumericFieldStyle.FormatDisplay(
            stampede?.Attribute("attackinterval")?.Value);
        _stampedeFullLengthCheck.IsChecked = PowerCatalog.FindField(entry, "fulllength") != null;
        foreach (var control in new Control[] { _stampedeUnitBox, _stampedeScaleBox,
                     _stampedeAnimRateBox, _stampedeAttackActionBox, _stampedeAttackIntervalBox })
            control.IsEnabled = IsModifiedMode;
        _stampedeFullLengthCheck.IsEnabled = IsModifiedMode;
    }

    private XElement? GetOrCreateStampede()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("Stampede",
                StringComparison.OrdinalIgnoreCase) != true) return null;
        return PowerCatalog.FindField(_currentEntry, "stampede") ??
               PowerCatalog.SetField(_currentEntry, "stampede", "");
    }

    private void CommitStampedeValue(string value)
    {
        if (_isPopulating || !_stampedeUnitBox.IsVisible || GetOrCreateStampede() is not { } stampede) return;
        if (stampede.Value.Trim().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        stampede.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void CommitStampedeAttribute(string name, string value)
    {
        if (_isPopulating || !_stampedeRow.IsVisible || GetOrCreateStampede() is not { } stampede) return;
        var existing = stampede.Attribute(name);
        if (value.Length == 0 && existing == null) return;
        if (existing?.Value.Equals(value, StringComparison.OrdinalIgnoreCase) == true) return;
        stampede.SetAttributeValue(name, value.Length == 0 ? null : value);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void StampedeNumber_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulating || sender is not TextBox box || !_stampedeRow.IsVisible) return;
        var text = box.Text?.Trim() ?? "";
        var name = ReferenceEquals(box, _stampedeScaleBox) ? "scale" :
            ReferenceEquals(box, _stampedeAnimRateBox) ? "animrate" : "attackinterval";
        if (text.Length == 0)
        {
            var existing = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "stampede");
            if (existing?.Attribute(name) == null) return;
            existing.SetAttributeValue(name, null); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); return;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number)) return;
        if (GetOrCreateStampede() is not { } stampede) return;
        if (NumericTextEquals(stampede.Attribute(name)?.Value, text)) return;
        stampede.SetAttributeValue(name, text); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void StampedeFullLength_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("Stampede",
                StringComparison.OrdinalIgnoreCase) != true) return;
        var field = PowerCatalog.FindField(_currentEntry, "fulllength");
        if (_stampedeFullLengthCheck.IsChecked == true)
        {
            if (field == null) PowerCatalog.SetField(_currentEntry, "fulllength", "");
        }
        else if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshTempUnitAttach(XElement entry)
    {
        _tempUnitAttachHost.Children.Clear();
        var attach = PowerCatalog.FindField(entry, "attachproto");
        if (attach == null)
        {
            if (IsModifiedMode) _tempUnitAttachHost.Children.Add(CreateTempUnitAddButton("Attach Unit", () =>
            {
                PowerCatalog.SetField(entry, "attachproto", ""); RefreshTempUnitFields(entry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            return;
        }
        _tempUnitAttachHost.Children.Add(CreateTempUnitProtoBox(attach.Value, value =>
        {
            if (!IsModifiedMode) return; attach.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }));
        if (IsModifiedMode) _tempUnitAttachHost.Children.Add(CreateTempUnitRemoveButton(() =>
        {
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(attach); RefreshTempUnitFields(entry);
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }));
    }

    private void RefreshTempUnitAges(XElement entry)
    {
        _tempUnitAgeRows.Children.Clear(); _tempUnitAgeFields.Clear();
        var isDwarvenMine = entry.Attribute("type")?.Value.Equals("DwarvenMine",
            StringComparison.OrdinalIgnoreCase) == true;
        var adjustments = entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase) &&
                     (isDwarvenMine
                         ? element.Attribute("goldamount") != null || element.Attribute("gatherratemultiplier") != null
                         : element.Attribute("quantityaddon") != null)).ToList();
        foreach (var adjustment in adjustments)
        {
            var usedByOthers = adjustments.Where(item => !ReferenceEquals(item, adjustment))
                .Select(item => item.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentAge = adjustment.Value.Trim();
            var ageOptions = StandardAges.Where(value => !usedByOthers.Contains(value) ||
                value.Equals(currentAge, StringComparison.OrdinalIgnoreCase)).ToList();
            if (currentAge.Length > 0 && !ageOptions.Contains(currentAge, StringComparer.OrdinalIgnoreCase))
                ageOptions.Add(currentAge);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "Age", VerticalAlignment = VerticalAlignment.Center });
            var age = new ComboBox { Width = 140, ItemsSource = ageOptions,
                SelectedItem = ageOptions.FirstOrDefault(value => value.Equals(currentAge,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            row.Children.Add(age);
            row.Children.Add(new TextBlock { Text = isDwarvenMine ? "Gold Amount" : "Quantity",
                VerticalAlignment = VerticalAlignment.Center });
            var quantityAttribute = isDwarvenMine ? "goldamount" : "quantityaddon";
            var quantity = new TextBox { Width = 90, Text = adjustment.Attribute(quantityAttribute)?.Value ?? "",
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(quantity);
            row.Children.Add(quantity);
            TextBox? gatherRate = null;
            if (isDwarvenMine)
            {
                row.Children.Add(new TextBlock { Text = "Gather Rate Multiplier",
                    VerticalAlignment = VerticalAlignment.Center });
                gatherRate = new TextBox { Width = 90,
                    Text = adjustment.Attribute("gatherratemultiplier")?.Value ?? "",
                    IsEnabled = IsModifiedMode };
                EditorNumericFieldStyle.ConfigureNumericTextBox(gatherRate);
                row.Children.Add(gatherRate);
                gatherRate.TextChanged += (_, _) =>
                {
                    if (_isPopulating || !IsModifiedMode || !double.TryParse(gatherRate.Text,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                        !double.IsFinite(value)) return;
                    if (NumericTextEquals(adjustment.Attribute("gatherratemultiplier")?.Value,
                            gatherRate.Text!.Trim())) return;
                    adjustment.SetAttributeValue("gatherratemultiplier", gatherRate.Text!.Trim());
                    MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                };
            }
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(adjustment);
                RefreshTempUnitAges(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            quantity.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode) return;
                var valid = isDwarvenMine
                    ? double.TryParse(quantity.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var value) && double.IsFinite(value)
                    : int.TryParse(quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                if (!valid) return;
                var normalized = quantity.Text!.Trim();
                if (isDwarvenMine
                        ? NumericTextEquals(adjustment.Attribute(quantityAttribute)?.Value, normalized)
                        : adjustment.Attribute(quantityAttribute)?.Value == normalized) return;
                adjustment.SetAttributeValue(quantityAttribute, normalized);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            age.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || age.SelectedItem is not string value) return;
                if (adjustment.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return;
                adjustment.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(adjustment, () => RefreshTempUnitAges(entry));
            };
            _tempUnitAgeRows.Children.Add(row); _tempUnitAgeFields.Add((quantity, age, adjustment));
        }
        _addTempUnitAgeButton.IsVisible = IsModifiedMode && adjustments.Count < StandardAges.Length;
    }

    private void AddTempUnitAge_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var used = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase) &&
                (element.Attribute("quantityaddon") != null || element.Attribute("goldamount") != null ||
                 element.Attribute("gatherratemultiplier") != null)).Select(element => element.Value.Trim()).ToList();
        var age = StandardAges.FirstOrDefault(value => !used.Contains(value, StringComparer.OrdinalIgnoreCase));
        if (age == null) return;
        var adjustment = PowerCatalog.AddRepeatedField(_currentEntry, "ageadjustment", age);
        if (_currentEntry.Attribute("type")?.Value.Equals("DwarvenMine",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            adjustment.SetAttributeValue("goldamount", "0");
            adjustment.SetAttributeValue("gatherratemultiplier", "0");
        }
        else adjustment.SetAttributeValue("quantityaddon", "0");
        RefreshTempUnitAges(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void TempUnitPlacementFlagChanged(CheckBox check, string tag)
    {
        if (_currentEntry == null || _isPopulating || !CanEditProperties(_currentEntry)) return;
        var existing = PowerCatalog.FindField(_currentEntry, tag);
        if (check.IsChecked == true)
        {
            if (existing != null) return;
            PowerCatalog.SetField(_currentEntry, tag, "");
        }
        else
        {
            if (existing == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(existing);
        }
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshVibrantTreeUnits(XElement entry)
    {
        _vibrantTreeUnitsHost.Children.Clear();
        _vibrantTreeUnitFields.Clear();
        var elements = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("treeproto", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var element in elements)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 8, 4) };
            var box = CreateTempUnitProtoBox(element.Value.Trim(), value =>
            {
                if (!IsModifiedMode) return;
                element.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
            group.Children.Add(box);
            if (IsModifiedMode) group.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
                RefreshVibrantTreeUnits(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _vibrantTreeUnitsHost.Children.Add(group);
            _vibrantTreeUnitFields.Add((box, element));
        }
        if (IsModifiedMode)
        {
            if (elements.Count == 0)
            {
                var first = CreateTempUnitProtoBox("", value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    PowerCatalog.AddRepeatedField(entry, "treeproto", value);
                    RefreshVibrantTreeUnits(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                });
                _vibrantTreeUnitsHost.Children.Add(first);
            }
            _vibrantTreeUnitsHost.Children.Add(CreateTempUnitAddButton("Add Tree Unit", () =>
            {
                PowerCatalog.AddRepeatedField(entry, "treeproto", "");
                RefreshVibrantTreeUnits(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
        }
    }

    private void RefreshDroughtTerrainCreep(XElement entry)
    {
        _droughtTerrainCreepHost.Children.Clear();
        var creep = PowerCatalog.FindField(entry, "terraincreep");
        _addTerrainCreepButton.IsVisible = IsModifiedMode && creep == null;
        _terrainCreepLabel.IsVisible = creep != null;
        if (creep == null)
            return;

        var core = new WrapPanel();
        AddTerrainSelector(core, "Creep", "creep", _terrainCreepNames, strict: true);
        AddTerrainNumber(core, "Radius", "maxradius");
        AddTerrainNumber(core, "Growth Rate", "growthrate");
        AddTerrainNumber(core, "Decay Rate", "decayrate");
        AddOptionalTerrainNumber(core, "Strength", "strength");
        var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(core, 0);
        topRow.Children.Add(core);
        if (IsModifiedMode)
        {
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Margin = new Thickness(2, 0, 0, 4);
            remove.Click += (_, _) =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(creep);
                RefreshDroughtTerrainCreep(entry); ArrangeTempestOptionalRows(entry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            Grid.SetColumn(remove, 1);
            topRow.Children.Add(remove);
        }
        _droughtTerrainCreepHost.Children.Add(topRow);
        var intervals = new WrapPanel();
        AddTerrainNumber(intervals, "Min Update (s)", "minupdateinterval");
        AddTerrainNumber(intervals, "Max Update (s)", "maxupdateinterval");
        AddOptionalTerrainChoice(intervals, "Buildability", "buildability",
            MajorGodCatalog.TerrainCreepBuildabilityValues);
        AddOptionalTerrainSelector(intervals, "Prevent Placement of", "preventplacement", _unitOrTypeNames);
        _droughtTerrainCreepHost.Children.Add(intervals);
        BuildTerrainCreepFlags(creep, _droughtTerrainCreepHost);
        void AddTerrainNumber(Panel host, string label, string attribute)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 12, 4) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Width = 90, Text = creep.Attribute(attribute)?.Value ?? "0",
                IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
            box.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || !double.TryParse(box.Text,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    !double.IsFinite(value) || value < 0) return;
                if (double.TryParse(creep.Attribute(attribute)?.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var original) && original.Equals(value)) return;
                creep.SetAttributeValue(attribute, box.Text!.Trim()); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            group.Children.Add(box); host.Children.Add(group);
        }
        void AddTerrainSelector(Panel host, string label, string attribute,
            IReadOnlyList<string> values, bool strict)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                Margin = new Thickness(0, 0, 12, 4) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var box = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
            if (strict) EditorAutoCompleteService.ConfigureStrict(box, values, creep.Attribute(attribute)?.Value ?? "",
                () => _isPopulating, preserveUnknownInitialValue: true, allowEmpty: true,
                commitEmptyAsValid: true, selectAllOnFirstClick: false, valueCommitted: value =>
                {
                    if (string.Equals(creep.Attribute(attribute)?.Value, value,
                            StringComparison.OrdinalIgnoreCase)) return;
                    creep.SetAttributeValue(attribute, value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                });
            else EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating, selectAllOnFirstClick: false);
            box.Text = creep.Attribute(attribute)?.Value ?? "";
            EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, _specificScroll);
            group.Children.Add(box); host.Children.Add(group);
        }
        void AddOptionalTerrainNumber(Panel host, string label, string attribute)
        {
            if (creep.Attribute(attribute) == null)
            {
                if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton(label, () =>
                { creep.SetAttributeValue(attribute, "0"); RefreshDroughtTerrainCreep(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }));
                return;
            }
            AddTerrainNumber(host, label, attribute);
        }
        void AddOptionalTerrainChoice(Panel host, string label, string attribute, IReadOnlyList<string> values)
        {
            if (creep.Attribute(attribute) == null)
            {
                if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton(label, () =>
                { creep.SetAttributeValue(attribute, values[0].ToLowerInvariant()); RefreshDroughtTerrainCreep(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }));
                return;
            }
            var current = creep.Attribute(attribute)?.Value;
            var options = OptionsIncludingCurrent(values, current);
            var combo = new ComboBox { Width = 160, ItemsSource = options,
                SelectedItem = options.FirstOrDefault(value => value.Equals(current,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode, Margin = new Thickness(0,0,8,4) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) SetTerrainAttribute(attribute, value.ToLowerInvariant()); };
            host.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,4) });
            host.Children.Add(combo);
        }
        void AddOptionalTerrainSelector(Panel host, string label, string attribute, IReadOnlyList<string> values)
        {
            if (creep.Attribute(attribute) == null)
            {
                if (IsModifiedMode) host.Children.Add(CreateTempUnitAddButton(label, () =>
                { creep.SetAttributeValue(attribute, ""); RefreshDroughtTerrainCreep(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }));
                return;
            }
            AddTerrainSelector(host, label, attribute, values, strict: true);
        }
        void SetTerrainAttribute(string attribute, string value)
        {
            if (_isPopulating || !IsModifiedMode) return;
            if (string.Equals(creep.Attribute(attribute)?.Value, value,
                    StringComparison.OrdinalIgnoreCase)) return;
            creep.SetAttributeValue(attribute, value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }
    }

    private void AddTerrainCreep_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !IsModifiedMode ||
            PowerCatalog.FindField(_currentEntry, "terraincreep") != null) return;
        var creep = PowerCatalog.SetField(_currentEntry, "terraincreep", "");
        foreach (var (name, value) in new[] { ("creep", ""), ("maxradius", "0"),
                     ("growthrate", "0"), ("decayrate", "0"),
                     ("minupdateinterval", "0"), ("maxupdateinterval", "0") })
            creep.SetAttributeValue(name, value);
        RefreshDroughtTerrainCreep(_currentEntry);
        ArrangeTempestOptionalRows(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void BuildTerrainCreepFlags(XElement creep, Panel host)
    {
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = "Flags", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0,0,8,4) });
        var selected = MajorGodCatalog.TerrainCreepFlags.Where(flag => creep.Attribute(flag.Attribute) != null).ToList();
        if (IsModifiedMode)
        {
            var picker = new AutoCompleteBox { Width = 50, FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0, ItemsSource = MajorGodCatalog.TerrainCreepFlags.Except(selected)
                    .Select(flag => flag.Label).ToList() };
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string label || _currentEntry == null) return;
                var flag = MajorGodCatalog.TerrainCreepFlags.First(item => item.Label == label);
                creep.SetAttributeValue(flag.Attribute, "");
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                var entry = _currentEntry;
                RefreshAfterSelectorEvent(creep, () => RefreshDroughtTerrainCreep(entry));
            };
            row.Children.Add(picker);
        }
        foreach (var flag in selected) row.Children.Add(PowerEditorDynamicControls.CreateChip(flag.Label,
            IsModifiedMode ? () => { creep.SetAttributeValue(flag.Attribute, null); if (_currentEntry != null)
                RefreshDroughtTerrainCreep(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); } : null,
            readOnly: !IsModifiedMode));
        if (IsModifiedMode || selected.Count > 0) host.Children.Add(row);
    }

    private void RefreshDroughtInitialDamages(XElement entry)
    {
        _droughtInitialDamageRows.Children.Clear(); _droughtDamageFields.Clear();
        foreach (var element in entry.Elements().Where(element => element.Name.LocalName.Equals("initialdamage",
                     StringComparison.OrdinalIgnoreCase))) BuildDroughtInitialDamage(entry, element);
        _addDroughtInitialDamageButton.IsVisible = IsModifiedMode;
    }

    private void BuildDroughtInitialDamage(XElement entry, XElement element)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Unit", VerticalAlignment = VerticalAlignment.Center });
        var unit = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        EditorAutoCompleteService.ConfigureStrict(unit, _unitOrTypeNames, element.Value, () => _isPopulating,
            preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return;
                element.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        unit.Text = element.Value;
        row.Children.Add(unit); row.Children.Add(new TextBlock { Text = "Factor", VerticalAlignment = VerticalAlignment.Center });
        var factor = new TextBox { Width = 90, Text = element.Attribute("fraction")?.Value ?? "0",
            IsEnabled = IsModifiedMode }; EditorNumericFieldStyle.ConfigureNumericTextBox(factor);
        factor.TextChanged += (_, _) => { if (double.TryParse(factor.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
            {
                if (double.TryParse(element.Attribute("fraction")?.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var original) && original.Equals(value)) return;
                element.SetAttributeValue("fraction", factor.Text!.Trim()); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } };
        row.Children.Add(factor);
        if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
        { XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element); RefreshDroughtInitialDamages(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }));
        _droughtInitialDamageRows.Children.Add(row); _droughtDamageFields.Add((unit, factor, element));
    }

    private void AddDroughtInitialDamage_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "initialdamage", "");
        element.SetAttributeValue("fraction", "0"); RefreshDroughtInitialDamages(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshGreatFloodDamageCaps(XElement entry)
    {
        _greatFloodDamageCapRows.Children.Clear(); _greatFloodDamageCapFields.Clear();
        foreach (var element in entry.Elements().Where(element => element.Name.LocalName.Equals("damagecap",
                     StringComparison.OrdinalIgnoreCase))) BuildGreatFloodDamageCap(entry, element);
        _addGreatFloodDamageCapButton.IsVisible = IsModifiedMode;
    }

    private void BuildGreatFloodDamageCap(XElement entry, XElement element)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Unit", VerticalAlignment = VerticalAlignment.Center });
        var unit = new AutoCompleteBox { Width = 200, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0, IsEnabled = IsModifiedMode };
        var initial = element.Attribute("unittype")?.Value ?? "";
        EditorAutoCompleteService.ConfigureStrict(unit, _unitOrTypeNames, initial, () => _isPopulating,
            preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (string.Equals(element.Attribute("unittype")?.Value, value,
                        StringComparison.OrdinalIgnoreCase)) return;
                element.SetAttributeValue("unittype", value); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        unit.Text = initial;
        row.Children.Add(unit); row.Children.Add(new TextBlock { Text = "Cap", VerticalAlignment = VerticalAlignment.Center });
        var cap = new TextBox { Width = 90, Text = element.Value, IsEnabled = IsModifiedMode };
        EditorNumericFieldStyle.ConfigureNumericTextBox(cap);
        cap.TextChanged += (_, _) => { if (double.TryParse(cap.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
            {
                if (double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var original) && original.Equals(value)) return;
                element.Value = cap.Text!.Trim(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            } };
        row.Children.Add(cap);
        if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
        { XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element); RefreshGreatFloodDamageCaps(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }));
        _greatFloodDamageCapRows.Children.Add(row); _greatFloodDamageCapFields.Add((unit, cap, element));
    }

    private void AddGreatFloodDamageCap_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "damagecap", "0");
        element.SetAttributeValue("unittype", ""); RefreshGreatFloodDamageCaps(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshBronzeArmors(XElement entry)
    {
        _bronzeArmorRows.Children.Clear();
        foreach (var armor in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("armor", StringComparison.OrdinalIgnoreCase)))
            BuildBronzeArmor(entry, armor);
        _addBronzeArmorButton.IsVisible = IsModifiedMode;
    }

    private void BuildBronzeArmor(XElement entry, XElement armor)
    {
        var content = new StackPanel { Spacing = 6 };
        var top = new WrapPanel();
        top.Children.Add(new TextBlock { Text = "Unit Type", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4) });
        var abstractType = armor.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase));
        var unit = new AutoCompleteBox { Width = 200, MaxWidth = 200, IsEnabled = IsModifiedMode,
            FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, Margin = new Thickness(0, 0, 14, 4) };
        EditorAutoCompleteService.ConfigureStrict(unit, _unitOrTypeNames, abstractType?.Value ?? "", () => _isPopulating,
            preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (!IsModifiedMode) return;
                if (string.Equals(abstractType?.Value.Trim(), value,
                        StringComparison.OrdinalIgnoreCase) || abstractType == null && value.Length == 0) return;
                if (abstractType == null) { abstractType = new XElement("abstracttype"); armor.AddFirst(abstractType); }
                abstractType.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
        unit.Text = abstractType?.Value ?? "";
        top.Children.Add(unit);
        top.Children.Add(new TextBlock { Text = "VFX", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4) });
        var vfxElement = armor.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("vfxproto", StringComparison.OrdinalIgnoreCase));
        var vfx = CreateTempUnitProtoBox(vfxElement?.Value ?? "", value =>
        {
            if (vfxElement == null) { vfxElement = new XElement("vfxproto"); armor.Add(vfxElement); }
            vfxElement.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        });
        top.Children.Add(vfx); content.Children.Add(top);

        var typeRows = new StackPanel { Spacing = 6 };
        foreach (var armorType in armor.Elements().Where(element =>
                     element.Name.LocalName.Equals("armortype", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "Armor Type", VerticalAlignment = VerticalAlignment.Center });
            var current = armorType.Value.Trim();
            var choices = ProtoConstants.KnownArmorTypes.Concat(
                ProtoConstants.KnownArmorTypes.Contains(current, StringComparer.OrdinalIgnoreCase) || current.Length == 0
                    ? [] : [current]).ToList();
            var type = new ComboBox { Width = 120, ItemsSource = choices,
                SelectedItem = choices.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = IsModifiedMode };
            type.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || type.SelectedItem is not string value) return;
                if (armorType.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)) return;
                armorType.Value = value; MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(type);
            row.Children.Add(new TextBlock { Text = "Delta", VerticalAlignment = VerticalAlignment.Center });
            var valueBox = new TextBox { Width = 90,
                Text = armorType.Attribute("vulnerabilitypercentdelta")?.Value ?? "", IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(valueBox);
            valueBox.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || !double.TryParse(valueBox.Text,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return;
                if (double.TryParse(armorType.Attribute("vulnerabilitypercentdelta")?.Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var original) &&
                    original.Equals(number)) return;
                armorType.SetAttributeValue("vulnerabilitypercentdelta", valueBox.Text!.Trim());
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            row.Children.Add(valueBox);
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(armorType);
                PowerCatalog.NormalizeNestedElementIndentation(armor); RefreshBronzeArmors(entry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            typeRows.Children.Add(row);
        }
        content.Children.Add(typeRows);
        var usedArmorTypes = armor.Elements().Where(element => element.Name.LocalName.Equals("armortype",
            StringComparison.OrdinalIgnoreCase)).Select(element => element.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (IsModifiedMode && ProtoConstants.KnownArmorTypes.Any(value => !usedArmorTypes.Contains(value)))
            content.Children.Add(CreateTempUnitAddButton("Add Armor Type", () =>
        {
            var used = armor.Elements().Where(element => element.Name.LocalName.Equals("armortype",
                StringComparison.OrdinalIgnoreCase)).Select(element => element.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var next = ProtoConstants.KnownArmorTypes.FirstOrDefault(value => !used.Contains(value));
            if (next == null) return;
            var element = new XElement("armortype", new XAttribute("vulnerabilitypercentdelta", "0"), next);
            var existingVfx = armor.Elements().FirstOrDefault(item => item.Name.LocalName.Equals("vfxproto", StringComparison.OrdinalIgnoreCase));
            if (existingVfx != null) existingVfx.AddBeforeSelf(element); else armor.Add(element);
            PowerCatalog.NormalizeNestedElementIndentation(armor);
            RefreshBronzeArmors(entry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        }));
        var card = new Border { BorderBrush = Avalonia.Media.Brushes.DarkGoldenrod, BorderThickness = new Thickness(1),
            Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Stretch };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(content);
        if (IsModifiedMode)
        {
            var remove = CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(armor); RefreshBronzeArmors(entry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            });
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(remove, 1); grid.Children.Add(remove);
        }
        card.Child = grid; _bronzeArmorRows.Children.Add(card);
    }

    private void AddBronzeArmor_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var armor = PowerCatalog.AddRepeatedField(_currentEntry, "armor", "");
        armor.Add(new XElement("abstracttype", ""),
            new XElement("armortype", new XAttribute("vulnerabilitypercentdelta", "0"), "Hack"),
            new XElement("vfxproto", ""));
        PowerCatalog.NormalizeNestedElementIndentation(armor);
        RefreshBronzeArmors(_currentEntry); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshGreatHuntMaxFoods(XElement entry)
    {
        _greatHuntMaxFoodRows.Children.Clear(); _greatHuntMaxFoodFields.Clear();
        var elements = entry.Elements().Where(element =>
            element.Name.LocalName.Equals("maxfood", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var element in elements)
        {
            var rawAge = element.Attribute("age")?.Value.Trim() ?? "";
            var currentIndex = int.TryParse(rawAge, out var parsed) && parsed is >= 1 and <= 5
                ? parsed - 1 : Array.FindIndex(StandardAges, value => value.Equals(rawAge,
                    StringComparison.OrdinalIgnoreCase));
            var usedByOthers = elements.Where(item => !ReferenceEquals(item, element)).Select(item =>
                int.TryParse(item.Attribute("age")?.Value, out var age) ? age - 1 : Array.FindIndex(StandardAges,
                    value => value.Equals(item.Attribute("age")?.Value, StringComparison.OrdinalIgnoreCase))).ToHashSet();
            var options = StandardAges.Where((_, index) => index == currentIndex || !usedByOthers.Contains(index)).ToList();
            var selectedAgeOption = currentIndex >= 0 ? StandardAges[currentIndex] : rawAge;
            if (selectedAgeOption.Length > 0 &&
                !options.Contains(selectedAgeOption, StringComparer.OrdinalIgnoreCase))
                options.Add(selectedAgeOption);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "Age", VerticalAlignment = VerticalAlignment.Center });
            var ageBox = new ComboBox { Width = 140, ItemsSource = options,
                SelectedItem = options.FirstOrDefault(value => value.Equals(selectedAgeOption,
                    StringComparison.OrdinalIgnoreCase)), IsEnabled = IsModifiedMode };
            row.Children.Add(ageBox);
            row.Children.Add(new TextBlock { Text = "Food", VerticalAlignment = VerticalAlignment.Center });
            var food = new TextBox { Width = 90, Text = EditorNumericFieldStyle.FormatDisplay(element.Value), IsEnabled = IsModifiedMode };
            EditorNumericFieldStyle.ConfigureNumericTextBox(food); row.Children.Add(food);
            ageBox.SelectionChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || ageBox.SelectedItem is not string selected) return;
                var selectedIndex = Array.IndexOf(StandardAges, selected);
                if (selectedIndex < 0) return;
                var age = selectedIndex + 1;
                if (element.Attribute("age")?.Value == age.ToString(CultureInfo.InvariantCulture)) return;
                element.SetAttributeValue("age", age);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
                RefreshAfterSelectorEvent(element, () => RefreshGreatHuntMaxFoods(entry));
            };
            food.TextChanged += (_, _) =>
            {
                if (_isPopulating || !IsModifiedMode || !double.TryParse(food.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return;
                if (NumericTextEquals(element.Value, food.Text!.Trim())) return;
                element.Value = food.Text!.Trim(); MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            };
            if (IsModifiedMode) row.Children.Add(CreateTempUnitRemoveButton(() =>
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element); RefreshGreatHuntMaxFoods(entry);
                MarkCurrentDirty(); RefreshRawXmlFromCurrent();
            }));
            _greatHuntMaxFoodRows.Children.Add(row); _greatHuntMaxFoodFields.Add((ageBox, food, element));
        }
        var usedAges = elements.Select(element => int.TryParse(element.Attribute("age")?.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : 0)
            .Where(age => age is >= 1 and <= 5).Distinct().Count();
        _addGreatHuntMaxFoodButton.IsVisible = IsModifiedMode && usedAges < StandardAges.Length;
    }

    private void AddGreatHuntMaxFood_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var used = _currentEntry.Elements().Where(element => element.Name.LocalName.Equals("maxfood",
            StringComparison.OrdinalIgnoreCase)).Select(element => int.TryParse(element.Attribute("age")?.Value,
                out var age) ? age : 0).ToHashSet();
        var next = Enumerable.Range(1, StandardAges.Length).FirstOrDefault(age => !used.Contains(age));
        if (next == 0) return;
        var element = PowerCatalog.AddRepeatedField(_currentEntry, "maxfood", "0");
        element.SetAttributeValue("age", next); RefreshGreatHuntMaxFoods(_currentEntry);
        MarkCurrentDirty(); RefreshRawXmlFromCurrent();
    }

    private void RefreshSpecificAttributeRows()
    {
        ResetTempestOptionalFlow();
        var powerType = _currentEntry?.Attribute("type")?.Value.Trim();
        var specs = SpecificAttributeSpecs();
        bool IsEffectiveDefault((string Name, Grid Row, string[] Tags) spec)
            => IsDefaultSpecificAttribute(powerType, spec.Name) ||
               (spec.Name.Equals("Tree Bend", StringComparison.OrdinalIgnoreCase) &&
                powerType?.Equals("Tempest", StringComparison.OrdinalIgnoreCase) == true &&
                !IsModifiedMode && _currentEntry != null &&
                HasOtherAttribute(_currentEntry,
                    ["treebendstrengthmin", "treebendstrengthmax", "treebendperiodmin", "treebendperiodmax"]));
        var defaultTags = specs.Where(IsEffectiveDefault)
            .SelectMany(spec => spec.Tags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var states = specs.ToDictionary(spec => spec.Name, spec =>
        {
            var isDefault = IsEffectiveDefault(spec);
            var shadowedByDefault = !isDefault && spec.Tags.Any(defaultTags.Contains);
            var typeOnlyAttribute = !isDefault && PowerSchemaRegistry.TypeOnlySpecificGroups.Contains(spec.Name);
            var present = !shadowedByDefault && !typeOnlyAttribute && _currentEntry != null &&
                          HasOtherAttribute(_currentEntry, spec.Tags);
            var visible = _currentEntry != null && !shadowedByDefault && !typeOnlyAttribute &&
                (isDefault ? IsModifiedMode || present :
                    _openOtherSpecificAttributes.Contains(spec.Name) || present);
            return (IsDefault: isDefault, Visible: visible);
        }, StringComparer.OrdinalIgnoreCase);
        var fallbackOrder = specs.Select((spec, index) => (spec.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.OrdinalIgnoreCase);
        foreach (var rowGroup in specs.GroupBy(spec => spec.Row)
                     .OrderBy(group => group.Min(spec =>
                         GetSpecificAttributeOrder(powerType, spec.Name, fallbackOrder[spec.Name]))))
        {
            var row = rowGroup.Key;
            if (row.Parent is Panel parent)
            {
                parent.Children.Remove(row);
                if (Equals(parent.Tag, "additional-specific-wrapper") && parent.Parent is Panel wrapperHost)
                    wrapperHost.Children.Remove(parent);
            }
            var visibleSpecs = rowGroup.Where(spec => states[spec.Name].Visible).ToList();
            row.IsVisible = visibleSpecs.Count > 0;
            if (!row.IsVisible) continue;

            if (visibleSpecs.Any(spec => states[spec.Name].IsDefault))
            {
                _specificAttributeRows.Children.Add(row);
                continue;
            }

            var generatedRemoveNames = IsModifiedMode
                ? visibleSpecs.Select(spec => spec.Name)
                    .Where(name => !HasInlineSpecificRemoveButton(row, name)).ToList()
                : [];
            if (generatedRemoveNames.Count == 0)
            {
                _otherSpecificAttributeRows.Children.Add(row);
                continue;
            }

            var wrapper = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Tag = "additional-specific-wrapper"
            };
            Grid.SetColumn(row, 0);
            wrapper.Children.Add(row);
            var removeButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Margin = new Thickness(2, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(removeButtons, 1);
            foreach (var name in generatedRemoveNames)
            {
                var remove = PowerEditorDynamicControls.CreateRemoveButton(name);
                ToolTip.SetTip(remove, $"Remove {name}");
                remove.Click += RemoveSpecificAttribute_Click;
                removeButtons.Children.Add(remove);
            }
            wrapper.Children.Add(removeButtons);
            _otherSpecificAttributeRows.Children.Add(wrapper);
        }

        _removeCracksSoundButton.IsVisible = IsModifiedMode && states["Cracks Sound"].Visible &&
                                              !states["Cracks Sound"].IsDefault;
        _removeTimeFactorButton.IsVisible = IsModifiedMode && states["Time Factor"].Visible &&
                                             !states["Time Factor"].IsDefault;
        var stunVisible = states["Stun lenght"].Visible;
        var shockwaveVfxVisible = states["Shockwave VFX"].Visible;
        _stunLengthLabel.IsVisible = stunVisible || shockwaveVfxVisible;
        _stunLengthLabel.Text = stunVisible ? "Stun lenght (s)" : "Shockwave VFX";
        _stunLengthFields.IsVisible = stunVisible;
        _shockwaveVfxFields.IsVisible = shockwaveVfxVisible;
        _shockwaveVfxInlineLabel.IsVisible = stunVisible && shockwaveVfxVisible;
        _removeStunLengthButton.IsVisible = IsModifiedMode && states["Stun lenght"].Visible &&
                                             !states["Stun lenght"].IsDefault;
        _removeShockwaveVfxButton.IsVisible = IsModifiedMode && states["Shockwave VFX"].Visible &&
                                               !states["Shockwave VFX"].IsDefault;
        _removeThrowPropertiesButton.IsVisible = IsModifiedMode && states["Throw properties"].Visible &&
                                                  !states["Throw properties"].IsDefault;

        _boltStrikeUnitFields.IsVisible = states["Strike Unit"].Visible;
        _boltSparksVfxFields.IsVisible = states["Bolt VFX"].Visible;
        _boltScorchVfxFields.IsVisible = states["Bolt VFX"].Visible;
        _pestilenceRofFields.IsVisible = IsModifiedMode || (_currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "rofpercentpenalty") != null);
        _pestilenceTrainRateFields.IsVisible = IsModifiedMode || (_currentEntry != null && PowerCatalog.FindField(_currentEntry, "trainratepenalty") != null);
        _pestilenceScanFields.IsVisible = IsModifiedMode || (_currentEntry != null && PowerCatalog.FindField(_currentEntry, "rescaninterval") != null);
        var huntSpawnVisible = IsModifiedMode || (_currentEntry != null && PowerCatalog.FindField(_currentEntry, "spawnproto") != null);
        var huntFoodVisible = IsModifiedMode || (_currentEntry != null && PowerCatalog.FindField(_currentEntry, "spawnfoodratio") != null);
        var huntVfxVisible = IsModifiedMode || (_currentEntry != null && PowerCatalog.FindField(_currentEntry, "vfxproto") != null);
        _greatHuntSpawnFields.IsVisible = huntSpawnVisible;
        _greatHuntFoodFields.IsVisible = huntFoodVisible;
        _greatHuntVfxFields.IsVisible = huntVfxVisible;
        _greatHuntExtrasLabel.Text = huntSpawnVisible ? "Spawn if no target" :
            huntFoodVisible ? "Food ratio" : "VFX";
        _greatHuntFoodInlineLabel.IsVisible = huntSpawnVisible;
        _greatHuntVfxInlineLabel.IsVisible = huntSpawnVisible || huntFoodVisible;

        var treeUnitVisible = states["Tree Unit"].Visible;
        var treeAmountVisible = states["Tree amount"].Visible;
        _treeUnitMainLabel.IsVisible = treeUnitVisible || treeAmountVisible;
        _treeUnitMainLabel.Text = treeUnitVisible ? "Tree Unit" : "Tree amount";
        _treeUnitFields.IsVisible = treeUnitVisible;
        _treeAmountFields.IsVisible = treeAmountVisible;
        _treeAmountInlineLabel.IsVisible = treeUnitVisible && treeAmountVisible;
        _removeTreeUnitButton.IsVisible = IsModifiedMode && treeUnitVisible && !states["Tree Unit"].IsDefault;
        _removeTreeAmountButton.IsVisible = IsModifiedMode && treeAmountVisible && !states["Tree amount"].IsDefault;

        var treeSpawnVisible = states["Tree Spawn Interval"].Visible;
        var treeEffectVisible = states["Tree Effect VFX"].Visible;
        _treeSpawnMainLabel.IsVisible = treeSpawnVisible || treeEffectVisible;
        _treeSpawnMainLabel.Text = treeSpawnVisible ? "Tree Spawn Interval" : "Tree Effect VFX";
        _treeSpawnIntervalFields.IsVisible = treeSpawnVisible;
        _treeEffectVfxFields.IsVisible = treeEffectVisible;
        _treeEffectVfxInlineLabel.IsVisible = treeSpawnVisible && treeEffectVisible;
        _removeTreeSpawnIntervalButton.IsVisible = IsModifiedMode && treeSpawnVisible &&
                                                    !states["Tree Spawn Interval"].IsDefault;
        _removeTreeEffectVfxButton.IsVisible = IsModifiedMode && treeEffectVisible &&
                                               !states["Tree Effect VFX"].IsDefault;
        _removeGrowthRadiusButton.IsVisible = IsModifiedMode && states["Growth radius"].Visible &&
                                               !states["Growth radius"].IsDefault;
        _removeEffectVfxButton.IsVisible = IsModifiedMode && states["Effect VFX"].Visible &&
                                            !states["Effect VFX"].IsDefault;
        _removeDamagePercentConversionButton.IsVisible = IsModifiedMode &&
            states["Damage Percent on Conversion"].Visible &&
            !states["Damage Percent on Conversion"].IsDefault;
        _removeVfxProtoButton.IsVisible = IsModifiedMode && states["VFX Proto"].Visible &&
                                           !states["VFX Proto"].IsDefault;
        _removeEffectTimeButton.IsVisible = IsModifiedMode && states["Effect time"].Visible &&
                                             !states["Effect time"].IsDefault;
        var unitEffectTimePresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "uniteffecttimeseconds") != null;
        var sharedEffectTimePresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "sharedeffecttimeseconds") != null;
        _unitEffectTimeLabel.IsVisible = _unitEffectTimeSecondsBox.IsVisible =
            IsModifiedMode || unitEffectTimePresent;
        _sharedEffectTimeLabel.IsVisible = _sharedEffectTimeSecondsBox.IsVisible =
            IsModifiedMode || sharedEffectTimePresent;

        _removeSpiderPropertiesButton.IsVisible = IsModifiedMode && states["Spider properties"].Visible &&
                                                   !states["Spider properties"].IsDefault;
        SetSpecificGroupVisibility(_spiderUnitFields, "spiderproto");
        SetSpecificGroupVisibility(_spiderEggUnitFields, "spidereggproto");
        SetSpecificGroupVisibility(_spiderDoorFields, "spiderdoorproto");
        SetSpecificGroupVisibility(_spiderWarnFields, "spiderwarnproto");
        SetSpecificGroupVisibility(_spiderAttackRangeFields, "spiderattackrange");
        SetSpecificGroupVisibility(_spiderSpawnOffsetFields, "spiderspawnoffset");

        var numberVisible = states["Number to create"].Visible;
        var separationVisible = states["Separation Distance"].Visible;
        _numberToCreateMainLabel.IsVisible = numberVisible || separationVisible;
        _numberToCreateMainLabel.Text = numberVisible ? "Number to create" : "Separation Distance";
        _numberToCreateFields.IsVisible = numberVisible;
        _separationDistanceFields.IsVisible = separationVisible;
        _separationDistanceInlineLabel.IsVisible = numberVisible && separationVisible;
        _removeNumberToCreateButton.IsVisible = IsModifiedMode && numberVisible &&
                                                 !states["Number to create"].IsDefault;
        _removeSeparationDistanceButton.IsVisible = IsModifiedMode && separationVisible &&
                                                     !states["Separation Distance"].IsDefault;

        _removeHatchPropertiesButton.IsVisible = IsModifiedMode && states["Hatch properties"].Visible &&
                                                  !states["Hatch properties"].IsDefault;
        SetSpecificGroupVisibility(_hatchTimeFields, "hatchtime");
        var hatchMinPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "hatchintervalmin") != null;
        var hatchMaxPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "hatchintervalmax") != null;
        _hatchIntervalFields.IsVisible = IsModifiedMode || hatchMinPresent || hatchMaxPresent;
        _hatchIntervalMinLabel.IsVisible = _hatchIntervalMinBox.IsVisible = IsModifiedMode || hatchMinPresent;
        _hatchIntervalMaxLabel.IsVisible = _hatchIntervalMaxBox.IsVisible = IsModifiedMode || hatchMaxPresent;

        var warnTimeVisible = states["Warn time"].Visible;
        var initialDelayVisible = states["Initial Delay"].Visible;
        var unitEffectTimeVisible = states["Unit Effect Time"].Visible;
        _warnTimeMainLabel.IsVisible = warnTimeVisible || initialDelayVisible || unitEffectTimeVisible;
        _warnTimeMainLabel.Text = warnTimeVisible ? "Warn time" :
            initialDelayVisible ? "Initial Delay" : "Unit Effect Time";
        _warnTimeFields.IsVisible = warnTimeVisible;
        _initialDelayFields.IsVisible = initialDelayVisible;
        _initialDelayInlineLabel.IsVisible = warnTimeVisible && initialDelayVisible;
        _unitEffectTimeFields.IsVisible = unitEffectTimeVisible;
        _unitEffectTimeInlineLabel.IsVisible = unitEffectTimeVisible &&
                                               (warnTimeVisible || initialDelayVisible);
        _unitEffectTimeFields.Margin = warnTimeVisible || initialDelayVisible
            ? new Thickness(12, 0, 0, 0) : new Thickness(0);
        _removeWarnTimeButton.IsVisible = IsModifiedMode && warnTimeVisible &&
                                           !states["Warn time"].IsDefault;
        _removeInitialDelayButton.IsVisible = IsModifiedMode && initialDelayVisible &&
                                              !states["Initial Delay"].IsDefault;
        _removeUnitEffectTimeButton.IsVisible = IsModifiedMode && unitEffectTimeVisible &&
                                                 !states["Unit Effect Time"].IsDefault;
        _removeHumanDeathSoundButton.IsVisible = IsModifiedMode && states["Human Death Sound"].Visible &&
                                                  !states["Human Death Sound"].IsDefault;

        _removeVortexPropertiesButton.IsVisible = IsModifiedMode && states["Vortex properties"].Visible &&
                                                   !states["Vortex properties"].IsDefault;
        SetSpecificGroupVisibility(_vortexLandVfxFields, "vortexlandingproto");
        SetSpecificGroupVisibility(_vortexStartVfxFields, "vortexstartproto");
        SetSpecificGroupVisibility(_vortexStartLinkedFields, "vortexstartlinkedproto");
        SetSpecificGroupVisibility(_vortexFinishLinkedFields, "vortexfinishlinkedproto");

        var dropIntervalVisible = states["Drop Interval"].Visible;
        var maxRetryVisible = states["Max Retry"].Visible;
        _dropIntervalMainLabel.IsVisible = dropIntervalVisible || maxRetryVisible;
        _dropIntervalMainLabel.Text = dropIntervalVisible ? "Drop Interval" : "Max Retry";
        _dropIntervalFields.IsVisible = dropIntervalVisible;
        _maxRetryFields.IsVisible = maxRetryVisible;
        _maxRetryInlineLabel.IsVisible = dropIntervalVisible && maxRetryVisible;
        _removeDropIntervalButton.IsVisible = IsModifiedMode && dropIntervalVisible &&
                                               !states["Drop Interval"].IsDefault;
        _removeMaxRetryButton.IsVisible = IsModifiedMode && maxRetryVisible &&
                                           !states["Max Retry"].IsDefault;
        var dropMinPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "dropintervalmin") != null;
        var dropMaxPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "dropintervalmax") != null;
        _dropIntervalMinLabel.IsVisible = _dropIntervalMinBox.IsVisible = IsModifiedMode || dropMinPresent;
        _dropIntervalMaxLabel.IsVisible = _dropIntervalMaxBox.IsVisible = IsModifiedMode || dropMaxPresent;

        _removeMaxUnitHeightButton.IsVisible = IsModifiedMode && states["Max Unit Height"].Visible &&
                                                !states["Max Unit Height"].IsDefault;
        var liftSoundVisible = states["Lift Sound"].Visible;
        var landSoundVisible = states["Land Sound"].Visible;
        _liftSoundMainLabel.IsVisible = liftSoundVisible || landSoundVisible;
        _liftSoundMainLabel.Text = liftSoundVisible ? "Lift Sound" : "Land Sound";
        _liftSoundFields.IsVisible = liftSoundVisible;
        _landSoundFields.IsVisible = landSoundVisible;
        _landSoundInlineLabel.IsVisible = liftSoundVisible && landSoundVisible;
        _removeLiftSoundButton.IsVisible = IsModifiedMode && liftSoundVisible &&
                                            !states["Lift Sound"].IsDefault;
        _removeLandSoundButton.IsVisible = IsModifiedMode && landSoundVisible &&
                                            !states["Land Sound"].IsDefault;

        var spawnVisible = states["Spawn Proto"].Visible;
        var maxSpawnVisible = states["Max Spawn Amount"].Visible;
        _spawnMainLabel.IsVisible = spawnVisible || maxSpawnVisible;
        _spawnMainLabel.Text = spawnVisible ? "Spawn Proto" : "Max Spawn Amount";
        _spawnFields.IsVisible = spawnVisible;
        _maxSpawnNumberFields.IsVisible = maxSpawnVisible;
        _maxSpawnNumberInlineLabel.IsVisible = spawnVisible && maxSpawnVisible;
        _removeSpawnButton.IsVisible = IsModifiedMode && spawnVisible && !states["Spawn Proto"].IsDefault;
        _removeMaxSpawnNumberButton.IsVisible = IsModifiedMode && maxSpawnVisible &&
                                                 !states["Max Spawn Amount"].IsDefault;

        _removeTartarianGateButton.IsVisible = IsModifiedMode && states["Tartarian Gate"].Visible &&
                                                !states["Tartarian Gate"].IsDefault;
        SetSpecificGroupVisibility(_tartarianGateUnitFields, "tartariangateproto");
        SetSpecificGroupVisibility(_tartarianGateBirthFields, "gatebirthproto");
        SetSpecificGroupVisibility(_tartarianGateFlameFields, "gateflameproto");
        SetSpecificGroupVisibility(_sinkHoleTimeFields, "sinkholetime");
        SetSpecificGroupVisibility(_sinkHoleShakeFields, "sinkholeshakestrength");
        _removeRumbleTimeButton.IsVisible = IsModifiedMode && states["Rumble Time"].Visible &&
                                             !states["Rumble Time"].IsDefault;

        var firstSpawnDelayVisible = states["First Spawn delay"].Visible;
        var spawnIntervalVisible = states["Spawn interval"].Visible;
        _firstSpawnDelayMainLabel.IsVisible = firstSpawnDelayVisible || spawnIntervalVisible;
        _firstSpawnDelayMainLabel.Text = firstSpawnDelayVisible ? "First Spawn delay" : "Spawn interval";
        _firstSpawnDelayFields.IsVisible = firstSpawnDelayVisible;
        _spawnIntervalFields.IsVisible = spawnIntervalVisible;
        _spawnIntervalInlineLabel.IsVisible = firstSpawnDelayVisible && spawnIntervalVisible;
        _removeFirstSpawnDelayButton.IsVisible = IsModifiedMode && firstSpawnDelayVisible &&
                                                  !states["First Spawn delay"].IsDefault;
        _removeSpawnIntervalButton.IsVisible = IsModifiedMode && spawnIntervalVisible &&
                                                !states["Spawn interval"].IsDefault;
        var spawnIntervalMinPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "spawnintervalmin") != null;
        var spawnIntervalMaxPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "spawnintervalmax") != null;
        _spawnIntervalMinLabel.IsVisible = _spawnIntervalMinBox.IsVisible =
            IsModifiedMode || spawnIntervalMinPresent;
        _spawnIntervalMaxLabel.IsVisible = _spawnIntervalMaxBox.IsVisible =
            IsModifiedMode || spawnIntervalMaxPresent;

        var farmProtoVisible = states["Farm Proto"].Visible;
        var numberTransformVisible = states["Number to Transform"].Visible;
        _farmProtoMainLabel.Text = farmProtoVisible ? "Farm Proto" : "Number to Transform";
        _farmProtoFields.IsVisible = farmProtoVisible;
        _numberToTransformFields.IsVisible = numberTransformVisible;
        _numberToTransformInlineLabel.IsVisible = farmProtoVisible && numberTransformVisible;
        _removeNumberToTransformButton.IsVisible = IsModifiedMode && numberTransformVisible &&
                                                    !states["Number to Transform"].IsDefault;
        _removeStealthLevelButton.IsVisible = IsModifiedMode && states["Stealth Level"].Visible &&
                                               !states["Stealth Level"].IsDefault;
        _addDamageTierButton.IsVisible = IsModifiedMode && states["Damage Tiers"].Visible;

        var rootedVfxVisible = states["Rooted enemy VFX"].Visible;
        var treeAreaVisible = states["Tree Area VFX"].Visible;
        var treeVfxVisible = states["Tree VFX"].Visible;
        _rootedEnemyVfxFields.IsVisible = rootedVfxVisible;
        _treeAreaVfxFields.IsVisible = treeAreaVisible;
        _treeVfxFields.IsVisible = treeVfxVisible;
        _removeForestTreeVfxButton.IsVisible = IsModifiedMode && treeVfxVisible &&
                                                !states["Tree VFX"].IsDefault;
        var stayTimeVisible = states["Stay Time"].Visible;
        var rootDurationVisible = states["Root duration"].Visible;
        _stayTimeMainLabel.Text = stayTimeVisible ? "Stay Time" : "Root duration";
        _stayTimeFields.IsVisible = stayTimeVisible;
        _rootDurationFields.IsVisible = rootDurationVisible;
        _rootDurationInlineLabel.IsVisible = stayTimeVisible && rootDurationVisible;
        _removeStayTimeButton.IsVisible = IsModifiedMode && stayTimeVisible &&
                                           !states["Stay Time"].IsDefault;
        _addRootDamageButton.IsVisible = IsModifiedMode && states["Root damage"].Visible;
        var healSpeedVisible = states["Heal speed"].Visible;
        var slowHealVisible = states["Slow Heal Multiplier"].Visible;
        var scanVisible = states["Scan interval"].Visible;
        _restorationHealingMainLabel.Text = healSpeedVisible ? "Heal speed" :
            slowHealVisible ? "Slow Heal Multiplier" : "Scan interval";
        _restorationHealSpeedFields.IsVisible = healSpeedVisible;
        _restorationHealSpeedInlineLabel.IsVisible = false;
        _slowHealFields.IsVisible = slowHealVisible;
        _slowHealInlineLabel.IsVisible = healSpeedVisible;
        _restorationScanFields.IsVisible = scanVisible;
        _removeSlowHealMultiplierButton.IsVisible = IsModifiedMode && slowHealVisible &&
                                                     !states["Slow Heal Multiplier"].IsDefault;

        var droughtLandVisible = states["Land VFX"].Visible;
        var droughtWaveVisible = states["Wave Unit"].Visible;
        _droughtVfxMainLabel.Text = droughtLandVisible ? "Land VFX" : "Wave Unit";
        _droughtLandVfxFields.IsVisible = droughtLandVisible;
        _droughtWaveUnitFields.IsVisible = droughtWaveVisible;
        _droughtWaveUnitInlineLabel.IsVisible = droughtLandVisible;

        var attackActionVisible = states["Attack Action"].Visible;
        var attackIntervalVisible = states["Attack Interval"].Visible;
        _greatFloodAttackMainLabel.Text = attackActionVisible ? "Attack" : "Attack Interval";
        _greatFloodAttackActionFields.IsVisible = attackActionVisible;
        _greatFloodAttackIntervalFields.IsVisible = attackIntervalVisible;
        _greatFloodAttackIntervalLabel.IsVisible = attackActionVisible;

        SetSpecificGroupVisibility(_shiftingSandsStartPortalFields, "inportalprotounit");
        SetSpecificGroupVisibility(_shiftingSandsDestinationPortalFields, "outportalprotounit");
        var fadeTimeVisible = IsModifiedMode || HasSpecificField("unitfadetime");
        var spawnTimeVisible = IsModifiedMode || HasSpecificField("outportalspawntime");
        var scanIntervalVisible = IsModifiedMode || HasSpecificField("scaninterval");
        _shiftingSandsTimingMainLabel.Text = fadeTimeVisible ? "Fade Time" :
            spawnTimeVisible ? "Spawn time" : "Scan Interval";
        _shiftingSandsFadeTimeFields.IsVisible = fadeTimeVisible;
        _shiftingSandsSpawnTimeFields.IsVisible = spawnTimeVisible;
        _shiftingSandsSpawnTimeLabel.IsVisible = fadeTimeVisible;
        _shiftingSandsScanIntervalFields.IsVisible = scanIntervalVisible;
        _shiftingSandsScanIntervalLabel.IsVisible = fadeTimeVisible || spawnTimeVisible;
        SetSpecificGroupVisibility(_locustSwarmUnitFields, "swarmprotounit");
        SetSpecificGroupVisibility(_locustSwarmCountFields, "swarmcount");
        SetSpecificGroupVisibility(_locustSwarmRadiusFields, "swarmradius");
        SetSpecificGroupVisibility(_locustWayPointCountFields, "swarmwaypoints");
        SetSpecificGroupVisibility(_locustWayPointOffsetFields, "swarmwaypointoffset");

        var cracks = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "cracksoundset");
        var min = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "crackintervalmin");
        var max = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "crackintervalmax");
        _cracksSoundBox.IsVisible = IsModifiedMode || cracks != null;
        _crackMinLabel.IsVisible = IsModifiedMode || min != null;
        _crackIntervalMinBox.IsVisible = IsModifiedMode || min != null;
        _crackMaxLabel.IsVisible = IsModifiedMode || max != null;
        _crackIntervalMaxBox.IsVisible = IsModifiedMode || max != null;

        SetThrowPairVisibility(_throwDistanceLabel,
            _minThrowDistanceLabel, _minThrowDistanceBox,
            _maxThrowDistanceLabel, _maxThrowDistanceBox);
        SetThrowPairVisibility(_throwHeightLabel,
            _minThrowHeightLabel, _minThrowHeightBox,
            _maxThrowHeightLabel, _maxThrowHeightBox);
        SetThrowPairVisibility(_throwVelocityLabel,
            _minThrowVelocityLabel, _minThrowVelocityBox,
            _maxThrowVelocityLabel, _maxThrowVelocityBox);

        _otherSpecificPickerRow.IsVisible = _currentEntry != null && IsModifiedMode;
        _otherSpecificHeader.IsVisible = _otherSpecificAttributeRows.Children.Count > 0;
        _otherSpecificAttributePicker.IsVisible = _otherSpecificAttributePickerOpen && IsModifiedMode;
        _otherSpecificAttributePicker.ItemsSource = SpecificAttributeSpecs()
            .Select(spec => spec.Name)
            .Where(name => !PowerSchemaRegistry.TypeOnlySpecificGroups.Contains(name) &&
                           !IsDefaultSpecificAttribute(powerType, name) &&
                           !SpecificAttributeSpecs().First(spec => spec.Name == name).Tags.Any(defaultTags.Contains) &&
                           !_openOtherSpecificAttributes.Contains(name))
            .ToList();

        bool HasSpecificField(string tag) => _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, tag) != null;
    }

    private static bool HasInlineSpecificRemoveButton(Control control, string attributeName)
    {
        if (control is Button { Tag: string tag } &&
            tag.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (control is Panel panel)
            return panel.Children.Any(child => HasInlineSpecificRemoveButton(child, attributeName));
        return false;
    }

    private void SetThrowPairVisibility(Control groupLabel, Control minLabel, TextBox minBox,
        Control maxLabel, TextBox maxBox)
    {
        var minPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, (string)minBox.Tag!) != null;
        var maxPresent = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, (string)maxBox.Tag!) != null;
        var groupVisible = IsModifiedMode || minPresent || maxPresent;
        if (groupLabel.Parent is Control group) group.IsVisible = groupVisible;
        groupLabel.IsVisible = groupVisible;
        minLabel.IsVisible = minBox.IsVisible = IsModifiedMode || minPresent;
        maxLabel.IsVisible = maxBox.IsVisible = IsModifiedMode || maxPresent;
    }

    private void SetSpecificGroupVisibility(Control group, string tag)
        => group.IsVisible = IsModifiedMode || (_currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, tag) != null);

    private static int GetSpecificAttributeOrder(string? powerType, string name, int fallback)
        => PowerSchemaRegistry.GetSpecificGroupOrder(powerType, name, fallback);

    private void CommitPowerType(string powerType)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !PowerTypes.Contains(powerType, StringComparer.OrdinalIgnoreCase)) return;
        _powerTypeBox.Classes.Remove("invalid-field");
        var attribute = _currentEntry.Attribute("type");
        if (attribute?.Value.Equals(powerType, StringComparison.Ordinal) == true) return;

        var specs = SpecificAttributeSpecs();
        var knownSpecificTags = specs.SelectMany(spec => spec.Tags)
            .Where(tag => tag is not "cost" and not "repeatcost")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedTags = specs.Where(spec => IsDefaultSpecificAttribute(powerType, spec.Name))
            .SelectMany(spec => spec.Tags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var element in _currentEntry.Elements().Where(element =>
                     knownSpecificTags.Contains(element.Name.LocalName) &&
                     !allowedTags.Contains(element.Name.LocalName)).ToList())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
        if (!powerType.Equals("SingleModify", StringComparison.OrdinalIgnoreCase) &&
            !powerType.Equals("UnitModify", StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.FindField(_currentEntry, "killoneffectend") is { } killOnEffectEnd)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(killOnEffectEnd);
        _currentEntry.SetAttributeValue("godpower",
            powerType.Equals("GeneralEffect", StringComparison.OrdinalIgnoreCase) ? "" : null);
        if (!powerType.Equals("PowerClone", StringComparison.OrdinalIgnoreCase))
        {
            PowerCatalog.FindField(_currentEntry, "cost")?.SetAttributeValue("copycostoftargetpower", null);
            PowerCatalog.FindField(_currentEntry, "repeatcost")
                ?.SetAttributeValue("copyrepeatcostoftargetpower", null);
        }

        var switchingToDwarvenMine = powerType.Equals("DwarvenMine", StringComparison.OrdinalIgnoreCase);
        var switchingToFoxCharm = powerType.Equals("FoxCharm", StringComparison.OrdinalIgnoreCase);
        foreach (var adjustment in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var dwarvenShape = adjustment.Attribute("goldamount") != null ||
                               adjustment.Attribute("gatherratemultiplier") != null;
            var foxCharmShape = FindAttribute(adjustment, "affectedNumber") != null;
            var standardShape = adjustment.Attribute("quantityaddon") != null;
            var shapeMatches = switchingToDwarvenMine ? dwarvenShape :
                switchingToFoxCharm ? foxCharmShape : standardShape;
            if (!shapeMatches)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(adjustment);
        }

        _currentEntry.SetAttributeValue("type", powerType);
        _openOtherSpecificAttributes.Clear();
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        PopulateSpecificFields(_currentEntry);
    }

    private void OtherSpecificAttributes_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _otherSpecificAttributePickerOpen = true;
        RefreshSpecificAttributeRows();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_otherSpecificAttributePickerOpen || !_otherSpecificAttributePicker.IsVisible) return;
            _otherSpecificAttributePicker.Focus();
            _otherSpecificAttributePicker.IsDropDownOpen = true;
        });
    }

    private void OtherSpecificAttributePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _otherSpecificAttributePicker.SelectedItem is not string name ||
            !SpecificAttributeSpecs().Any(spec => spec.Name == name)) return;
        _openOtherSpecificAttributes.Add(name);
        _otherSpecificAttributePickerOpen = false;
        _otherSpecificAttributePicker.IsDropDownOpen = false;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _otherSpecificAttributePicker.SelectedItem = null;
            _otherSpecificAttributePicker.Text = "";
        }
        finally { _isPopulating = wasPopulating; }
        RefreshSpecificAttributeRows();
    }

    private void RemoveSpecificAttribute_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var spec = SpecificAttributeSpecs().FirstOrDefault(spec => spec.Name == name);
        if (spec.Name == null) return;
        var changed = false;
        foreach (var field in _currentEntry.Elements().Where(element =>
                     spec.Tags.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            changed = true;
        }
        _openOtherSpecificAttributes.Remove(name);
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            if (name == "Cracks Sound")
            {
                _cracksSoundBox.Text = "";
                _crackIntervalMinBox.Text = "";
                _crackIntervalMaxBox.Text = "";
            }
            else if (name == "Time Factor") _timeFactorBox.Text = "";
            else if (name == "Stun lenght") _stunLengthBox.Text = "";
            else if (name == "Shockwave VFX")
            {
                _shockwaveVfxBox.SelectedItem = null;
                _shockwaveVfxBox.Text = "";
            }
            else if (name == "Throw properties")
                foreach (var box in ShockwaveNumberBoxes().Where(box => !ReferenceEquals(box, _stunLengthBox)))
                    box.Text = "";
            else if (name == "Tree Unit")
            {
                _treeUnitBox.SelectedItem = null;
                _treeUnitBox.Text = "";
            }
            else if (name == "Tree amount") _treeAmountBox.Text = "";
            else if (name == "Tree Spawn Interval")
            {
                _treeSpawnIntervalMinBox.Text = "";
                _treeSpawnIntervalMaxBox.Text = "";
            }
            else if (name == "Tree Effect VFX")
            {
                _treeEffectVfxBox.SelectedItem = null;
                _treeEffectVfxBox.Text = "";
            }
            else if (name == "Growth radius")
            {
                _growthRadiusStartBox.Text = "";
                _growthRadiusIncreaseRateBox.Text = "";
            }
            else if (name == "Effect VFX")
            {
                _effectVfxBox.SelectedItem = null;
                _effectVfxBox.Text = "";
            }
            else if (name == "Damage Percent on Conversion")
                _damagePercentConversionBox.Text = "";
            else if (name == "VFX Proto")
            {
                _vfxProtoBox.SelectedItem = null;
                _vfxProtoBox.Text = "";
            }
            else if (name == "Effect time")
            {
                _unitEffectTimeSecondsBox.Text = "";
                _sharedEffectTimeSecondsBox.Text = "";
            }
            else if (name == "Spider properties")
            {
                foreach (var box in SpiderProtoBoxes())
                {
                    box.SelectedItem = null;
                    box.Text = "";
                }
                _spiderAttackRangeBox.Text = "";
                _spiderSpawnOffsetBox.Text = "";
            }
            else if (name == "Number to create") _numberToCreateBox.Text = "";
            else if (name == "Separation Distance") _separationDistanceBox.Text = "";
            else if (name == "Hatch properties")
            {
                _hatchTimeBox.Text = "";
                _hatchIntervalMinBox.Text = "";
                _hatchIntervalMaxBox.Text = "";
            }
            else if (name == "Warn time") _warnTimeBox.Text = "";
            else if (name == "Initial Delay") _initialDelayBox.Text = "";
            else if (name == "Human Death Sound") _humanDeathSoundBox.Text = "";
            else if (name == "Unit Effect Time") _unitEffectTimeBox.Text = "";
            else if (name == "Vortex properties")
            {
                foreach (var box in VortexProtoBoxes())
                {
                    box.SelectedItem = null;
                    box.Text = "";
                }
            }
            else if (name == "Drop Interval")
            {
                _dropIntervalMinBox.Text = "";
                _dropIntervalMaxBox.Text = "";
            }
            else if (name == "Max Retry") _maxRetryBox.Text = "";
            else if (name == "Max Unit Height") _maxUnitHeightBox.Text = "";
            else if (name == "Lift Sound") _liftSoundBox.Text = "";
            else if (name == "Land Sound") _landSoundBox.Text = "";
            else if (name == "Spawn Proto")
            {
                _spawnProtoBox.SelectedItem = null;
                _spawnProtoBox.Text = "";
            }
            else if (name == "Max Spawn Amount") _maxSpawnNumberBox.Text = "";
            else if (name == "Tartarian Gate")
            {
                foreach (var box in TartarianProtoBoxes().Where(box =>
                             !ReferenceEquals(box, _spawnProtoBox)))
                {
                    box.SelectedItem = null;
                    box.Text = "";
                }
                _sinkHoleTimeBox.Text = "";
                _sinkHoleShakeBox.Text = "";
            }
            else if (name == "Rumble Time") _rumbleTimeBox.Text = "";
            else if (name == "First Spawn delay") _firstSpawnDelayBox.Text = "";
            else if (name == "Spawn interval")
            {
                _spawnIntervalMinBox.Text = "";
                _spawnIntervalMaxBox.Text = "";
            }
            else if (name == "Number to Transform") _numberToTransformBox.Text = "";
            else if (name == "Stealth Level") _stealthLevelBox.SelectedIndex = -1;
            else if (name == "Tree VFX")
            {
                _forestTreeVfxBox.SelectedItem = null;
                _forestTreeVfxBox.Text = "";
            }
            else if (name == "Stay Time") _stayTimeBox.Text = "";
            else if (name == "Slow Heal Multiplier") _slowHealMultiplierBox.Text = "";
        }
        finally { _isPopulating = wasPopulating; }
        PopulateSpecificFields(_currentEntry);
        if (changed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void CracksSound_TextChanged()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !_cracksSoundRow.IsVisible) return;
        SetOrRemoveSpecificField("cracksoundset", _cracksSoundBox.Text?.Trim() ?? "");
    }

    private void HumanDeathSound_TextChanged()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_humanDeathSoundRow.IsVisible) return;
        SetOrRemoveSpecificField("humandeathsoundset", _humanDeathSoundBox.Text?.Trim() ?? "");
    }

    private void VortexSound_TextChanged(AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        SetOrRemoveSpecificField(ReferenceEquals(box, _liftSoundBox) ? "liftsoundset" : "landsoundset",
            box.Text?.Trim() ?? "");
    }

    private void CommitShockwaveVfx(string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !_shockwaveVfxFields.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField("shockwavevfx", value);
    }

    private void CommitGaiaProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(ReferenceEquals(box, _treeUnitBox) ? "treeproto" : "treeeffectproto", value);
    }

    private void CommitSpecificProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(ReferenceEquals(box, _effectVfxBox) ? "effectproto" : "vfxproto", value);
    }

    private void CommitSpiderProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(SpiderProtoTag(box), value);
    }

    private void CommitVortexProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(VortexProtoTag(box), value);
    }

    private void CommitTartarianProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(TartarianProtoTag(box), value);
    }

    private void RefreshTartarianRevealerFields(XElement entry)
    {
        var isTartarianGate = entry.Attribute("type")?.Value.Trim().Equals("TartarianGate",
            StringComparison.OrdinalIgnoreCase) == true;
        var open = isTartarianGate && PowerCatalog.FindField(entry, "revealerproto") != null;
        _tartarianRevealerRow.IsVisible = open;
        _addTartarianRevealerButton.IsVisible = isTartarianGate && IsModifiedMode && !open;
        _removeTartarianRevealerButton.IsVisible = IsModifiedMode && open;
    }

    private void AddTartarianRevealer_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _currentEntry.Attribute("type")?.Value.Trim().Equals("TartarianGate",
                StringComparison.OrdinalIgnoreCase) != true) return;
        PowerCatalog.SetField(_currentEntry, "revealerproto", "");
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { PopulateStrictProtoBox(_tartarianRevealerBox, ""); }
        finally { _isPopulating = wasPopulating; }
        RefreshTartarianRevealerFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        _tartarianRevealerBox.Focus();
    }

    private void RemoveTartarianRevealer_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (PowerCatalog.FindField(_currentEntry, "revealerproto") is { } field)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { PopulateStrictProtoBox(_tartarianRevealerBox, ""); }
        finally { _isPopulating = wasPopulating; }
        RefreshTartarianRevealerFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void CommitImplodeProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(ImplodeProtoTag(box), value);
    }

    private void ImplodeSound_TextChanged(AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        SetOrRemoveSpecificField(ImplodeSoundTag(box), box.Text?.Trim() ?? "");
    }

    private void MeteorSound_TextChanged(AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        SetOrRemoveSpecificField(MeteorSoundTag(box), box.Text?.Trim() ?? "");
    }

    private void CommitNewPowerProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        if ((ReferenceEquals(box, _typhoonDragonUnitBox) || ReferenceEquals(box, _typhoonUnitBox)) &&
            _currentEntry.Attribute("type")?.Value.Trim().Equals("Typhoon",
                StringComparison.OrdinalIgnoreCase) != true) return;
        if (value.Length > 0 && !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        SetOrRemoveSpecificField(NewPowerProtoTag(box), value);
    }

    private void StealthLevel_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry) ||
            _stealthLevelBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        SetOrRemoveSpecificField("stealthlevel", value);
    }

    private void SpecificNumber_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry) || !box.IsVisible) return;
        var typhoon = _currentEntry.Attribute("type")?.Value.Trim()
            .Equals("Typhoon", StringComparison.OrdinalIgnoreCase) == true;
        if (IsTyphoonNumberBox(box) && !typhoon) return;
        var text = box.Text?.Trim() ?? "";
        if (text.Length > 0)
        {
            if (tag == "totalnumberoftrees")
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
                    return;
            }
            else if (tag is "numbertocreate" or "maxnumspawns" or "numbertotransform" or
                     "wallsegmentcount" or "wavecount" or "swarmcount" or "swarmwaypoints" or
                     "maxresources" or "throwsizelimit" or "amount" or "numberofstrikes" or
                     "numberofprioritystrikes" or "minimumgroupsize" or "numberofwaypoints" or
                     "creepgenerators" or "eidolonmin" or "maxeidoloncount" or "eidolonstep" or
                     "wolfcountsoftcap" or "wolfcounthardcap" or "packsize" or "minipacksize" or
                     "numextramainpackspawns" or "numberofadditionalstrikes" or "strikeunitlimit" or
                     "resourcevaluecap" or "maxfreeresearches")
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                    ((tag is "numbertocreate" or "maxnumspawns" or "wallsegmentcount" or "maxresources" or
                              "numberofstrikes" or "numberofprioritystrikes" or "minimumgroupsize" or
                              "throwsizelimit" or "creepgenerators" or "eidolonmin" or
                              "maxeidoloncount" or "eidolonstep" or "wolfcountsoftcap" or
                              "wolfcounthardcap" or "packsize" or "minipacksize" or
                              "numextramainpackspawns" or "numberofadditionalstrikes" or
                              "strikeunitlimit" or "resourcevaluecap" or "maxfreeresearches") &&
                     count < 0) || (tag == "numberofwaypoints" && count < 0))
                    return;
            }
            else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                         out var value) || !double.IsFinite(value) ||
                     ((tag is "timefactor" or "uniteffecttimeseconds" or "sharedeffecttimeseconds") &&
                      value < 0) || ((tag is "spiderattackrange" or "pullradius" or "staytime") && value < 0))
                return;
        }
        SetOrRemoveSpecificField(tag, text);
        if (tag == "throwsizelimit" && _currentEntry != null)
            RefreshThrowExclusions(_currentEntry);
    }

    private bool IsTyphoonNumberBox(TextBox box)
        => ReferenceEquals(box, _typhoonDragonSpawnDelayBox) ||
           ReferenceEquals(box, _typhoonDragonAttackIntervalBox) ||
           ReferenceEquals(box, _typhoonSpawnDelayBox) || ReferenceEquals(box, _typhoonAttackDelayBox) ||
           ReferenceEquals(box, _typhoonAttackIntervalBox) || ReferenceEquals(box, _typhoonRadiusStartBox) ||
           ReferenceEquals(box, _typhoonRadiusEndBox) || ReferenceEquals(box, _typhoonRadiusExpansionBox);

    private void SetOrRemoveSpecificField(string tag, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (value.Length == 0)
        {
            if (field == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        else
        {
            if (field?.Value == value ||
                (double.TryParse(field?.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                     out var originalNumber) &&
                 double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                     out var editedNumber) && originalNumber.Equals(editedNumber)))
                return;
            PowerCatalog.SetField(_currentEntry, tag, value);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RefreshOtherAttributeRows()
    {
        _otherAttributesPickerRow.IsVisible = _currentEntry != null && IsModifiedMode;
        _otherAttributePicker.IsVisible = _otherAttributePickerOpen && IsModifiedMode;
        _otherAttributePicker.ItemsSource = OtherAttributeSpecs()
            .Select(spec => spec.Name)
            .Where(name => name != "Range Indicator Proto" && !_openOtherAttributes.Contains(name))
            .ToList();
        var anyVisible = false;
        foreach (var (name, row, tags) in OtherAttributeSpecs())
        {
            var visible = _currentEntry != null &&
                (IsModifiedMode ? _openOtherAttributes.Contains(name) :
                    row.IsVisible && HasOtherAttribute(_currentEntry, tags));
            row.IsVisible = visible;
            _removeOtherAttributeButtons[name].IsVisible = visible && IsModifiedMode;
            anyVisible |= visible;
        }
        _otherAttributesRows.IsVisible = anyVisible;
        _addNotificationSectionsRow.IsVisible = false;
    }

    private void OtherAttributes_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _otherAttributePickerOpen = true;
        RefreshOtherAttributeRows();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_otherAttributePickerOpen || !_otherAttributePicker.IsVisible) return;
            _otherAttributePicker.Focus();
            _otherAttributePicker.IsDropDownOpen = true;
        });
    }

    private void OtherAttributePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _otherAttributePicker.SelectedItem is not string name ||
            !OtherAttributeSpecs().Any(spec => spec.Name == name)) return;
        _openOtherAttributes.Add(name);
        if (name == "Notification") _notificationsOpen = true;
        if (name == "Minimap Notification") AddMinimapNotifications_Click(null, new RoutedEventArgs());
        if (name == "Active Power Visibility") _activeVisibilityOpen = true;
        _otherAttributePicker.IsDropDownOpen = false;
        _otherAttributePickerOpen = false;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _otherAttributePicker.SelectedItem = null; _otherAttributePicker.Text = ""; }
        finally { _isPopulating = wasPopulating; }
        RefreshNotificationVisibility();
        RefreshOtherAttributeRows();
    }

    private void RemoveOtherAttribute(string name)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        if (name == "Notification") RemoveNotifications_Click(null, new RoutedEventArgs());
        if (name == "Minimap Notification") RemoveMinimapNotifications_Click(null, new RoutedEventArgs());
        if (name == "Active Power Visibility") RemoveActiveVisibility_Click(null, new RoutedEventArgs());
        var tags = OtherAttributeSpecs().First(spec => spec.Name == name).Tags;
        var removed = false;
        foreach (var element in _currentEntry.Elements().Where(element =>
                     tags.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
            removed = true;
        }
        _openOtherAttributes.Remove(name);
        if (removed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            if (name == "Range Indicator")
            {
                _rangeIndicatorBox.SelectedItem = null;
                foreach (var box in new[] { _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox }) box.Text = "";
                _rangeDecalBox.Text = ""; _isRangeDecalOpen = false;
            }
            else if (name == "Range Indicator Proto")
            {
                _rangeIndicatorProtoBox.Text = "";
                foreach (var box in new[] { _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                             _rangeIndicatorProtoSpeedBox }) box.Text = "";
            }
            else if (name == "Radius") { _radiusBox.Text = ""; _globalCheck.IsChecked = false; }
            else if (name == "Power Player Relation") _powerPlayerRelationBox.SelectedItem = "All";
            else if (name is "Camera Shake" or "Terrain Rumbling")
                foreach (var box in EffectAttributeBoxes().Where(box =>
                             ((string)box.Tag!).StartsWith(name == "Camera Shake" ? "camerashake:" : "terrainrumbling:",
                                 StringComparison.OrdinalIgnoreCase))) box.Text = "";
            if (name == "Sound set") RefreshSoundSets();
            if (name == "Attack target") RefreshAttackTypes();
            PopulateExtraFields(_currentEntry);
        }
        finally { _isPopulating = wasPopulating; }
        RefreshRangeAndTargetVisibility();
        RefreshNotificationVisibility();
        RefreshEffectVisibility();
        RefreshOtherAttributeRows();
    }

    private IEnumerable<TextBox> EffectAttributeBoxes()
    {
        yield return _cameraDurationBox;
        yield return _cameraStrengthBox;
        yield return _cameraRangeBox;
        yield return _terrainHeightBox;
        yield return _terrainWidthBox;
        yield return _terrainSpeedBox;
        yield return _terrainFadeTimeBox;
    }

    private void RefreshEffectVisibility()
    {
        if (_currentEntry == null) return;
        var camera = PowerCatalog.FindField(_currentEntry, "camerashake");
        var terrain = PowerCatalog.FindField(_currentEntry, "terrainrumbling");
        _cameraShakeRow.IsVisible = OtherSelected("Camera Shake") &&
                                    (IsModifiedMode || camera?.HasAttributes == true);
        _terrainRumblingRow.IsVisible = OtherSelected("Terrain Rumbling") &&
                                        (IsModifiedMode || terrain?.HasAttributes == true);
        _cameraDurationField.IsVisible = IsModifiedMode || FindPlacementFlag(camera, "duration") != null;
        _cameraStrengthField.IsVisible = IsModifiedMode || FindPlacementFlag(camera, "strength") != null;
        _cameraRangeField.IsVisible = (IsModifiedMode && _cameraRangeOpen) ||
                                      FindPlacementFlag(camera, "range") != null;
        _addCameraRangeButton.IsVisible = IsModifiedMode && !_cameraRangeField.IsVisible;
        _removeCameraRangeButton.IsVisible = IsModifiedMode;
        foreach (var (field, tag) in new[]
                 { (_terrainHeightField, "height"), (_terrainWidthField, "width"),
                   (_terrainSpeedField, "speed"), (_terrainFadeTimeField, "fadetime") })
            field.IsVisible = IsModifiedMode || FindPlacementFlag(terrain, tag) != null;
    }

    private void RefreshSoundSets()
    {
        _soundSetRows.Children.Clear();
        _soundNumericBoxes.Clear();
        _soundSetsSection.IsVisible = _currentEntry != null && OtherSelected("Sound set") &&
            (IsModifiedMode || _currentEntry.Elements().Any(element =>
                element.Name.LocalName.Equals("soundset", StringComparison.OrdinalIgnoreCase)));
        _addSoundSetButton.IsVisible = IsModifiedMode;
        if (_currentEntry == null) return;
        var suggestions = GetSoundSetSuggestions();
        foreach (var sound in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("soundset", StringComparison.OrdinalIgnoreCase)))
            BuildSoundSetRow(sound, suggestions);
    }

    private IReadOnlyList<string> GetSoundSetSuggestions()
        => _baseEntries.Values
            .Concat(_modDocument?.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
                ?? [])
            .SelectMany(power => power.Elements().Where(element =>
                element.Name.LocalName.EndsWith("soundset", StringComparison.OrdinalIgnoreCase)))
            .Select(sound => sound.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<string> GetPowerFieldSuggestions(string tag)
        => _baseEntries.Values
            .Concat(_modDocument?.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase)) ?? [])
            .Select(power => PowerCatalog.FindField(power, tag)?.Value.Trim() ?? "")
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void PopulateExtraFields(XElement entry)
    {
        foreach (var box in new[]
                 { _dualPowerMinDistanceBox, _powerBlockerBox, _accuracyBox, _allyDamageRatioBox })
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        var reveal = PowerCatalog.FindField(entry, "reveallos");
        _revealRadiusBox.Text = EditorNumericFieldStyle.FormatDisplay(FindPlacementFlag(reveal, "radius")?.Value);
        _revealUnitBox.Text = reveal?.Value.Trim() is { Length: > 0 } unit ? unit : "Revealer";
        _lightingBox.ItemsSource = GetPowerFieldSuggestions("lighting");
        _lightingBox.Text = PowerCatalog.FindField(entry, "lighting")?.Value ?? "";
        _resetAbilityChargeBox.Text = PowerCatalog.FindField(entry,
            "abstractpowerresetsabilitychargetype")?.Value ?? "";
        _playsBattleMusicCheck.IsChecked = PowerCatalog.FindField(entry, "playsbattlemusic") != null;
        RefreshExtraVisibility();
    }

    private void RefreshExtraVisibility()
    {
        if (_currentEntry == null) return;
        foreach (var (row, name, tag) in new[]
                 { (_dualPowerMinDistanceRow, "DualPowerMinDistance", "dualpowermindistance"),
                   (_powerBlockerRow, "Power Blocker", "powerblocker"),
                   (_accuracyRow, "Accuracy", "accuracy"),
                   (_allyDamageRatioRow, "Ally Damage Ratio", "allydamageratio"),
                   (_lightingRow, "Lighting", "lighting"),
                   (_resetAbilityChargeRow, "Reset Ability Charge for", "abstractpowerresetsabilitychargetype") })
            row.IsVisible = OtherSelected(name) &&
                            (IsModifiedMode || !string.IsNullOrWhiteSpace(PowerCatalog.FindField(_currentEntry, tag)?.Value));
        var reveal = PowerCatalog.FindField(_currentEntry, "reveallos");
        _revealLosRow.IsVisible = OtherSelected("Reveal LOS") && (IsModifiedMode || reveal != null);
        _revealRadiusField.IsVisible = IsModifiedMode || FindPlacementFlag(reveal, "radius") != null;
        _revealUnitField.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(reveal?.Value);
        _playsBattleMusicRow.IsVisible = OtherSelected("Plays battle Music") &&
                                         (IsModifiedMode || PowerCatalog.FindField(_currentEntry, "playsbattlemusic") != null);
    }

    private static bool NumericTextEquals(string? existing, string candidate)
        => double.TryParse(existing, NumberStyles.Float, CultureInfo.InvariantCulture, out var existingValue) &&
           double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var candidateValue) &&
           double.IsFinite(existingValue) && double.IsFinite(candidateValue) &&
           existingValue.Equals(candidateValue);

    private void ExtraNumber_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        if (tag == "accuracy") return;
        var text = box.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            var old = PowerCatalog.FindField(_currentEntry, tag);
            if (old == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(old);
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value)) return;
            if (value < 0) return;
            var existing = PowerCatalog.FindField(_currentEntry, tag)?.Value;
            if (double.TryParse(existing, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var existingValue) && existingValue.Equals(value)) return;
            PowerCatalog.SetField(_currentEntry, tag, text);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshExtraVisibility();
    }

    private void Accuracy_TextChanged()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var text = _accuracyBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            var old = PowerCatalog.FindField(_currentEntry, "accuracy");
            if (old == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(old);
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value)) return;
            var clamped = Math.Clamp(value, 0, 1);
            if (clamped != value)
            {
                text = clamped.ToString(CultureInfo.InvariantCulture);
                SetBoxTextWithoutEditing(_accuracyBox, text);
            }
            if (NumericTextEquals(PowerCatalog.FindField(_currentEntry, "accuracy")?.Value, text)) return;
            PowerCatalog.SetField(_currentEntry, "accuracy", text);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshExtraVisibility();
    }

    private void RevealRadius_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var text = _revealRadiusBox.Text?.Trim() ?? "";
        var reveal = PowerCatalog.FindField(_currentEntry, "reveallos");
        if (text.Length == 0)
        {
            var radius = FindPlacementFlag(reveal, "radius");
            if (radius == null) return;
            radius.Remove();
            if (reveal is { HasAttributes: false } && reveal.Value.Trim() == "Revealer")
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(reveal);
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0) return;
            if (NumericTextEquals(FindPlacementFlag(reveal, "radius")?.Value, text)) return;
            reveal ??= PowerCatalog.SetField(_currentEntry, "reveallos", _revealUnitBox.Text?.Trim() is { Length: > 0 } unit
                ? unit : "Revealer");
            SetPlacementAttribute(reveal, "radius", text);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshExtraVisibility();
    }

    private void ExtraSelector_TextChanged(AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var text = box.Text?.Trim() ?? "";
        if (ReferenceEquals(box, _revealUnitBox))
        {
            if (text.Length == 0 || !(text.Equals("Revealer", StringComparison.OrdinalIgnoreCase) ||
                _protoUnitNames.Contains(text, StringComparer.OrdinalIgnoreCase))) return;
            var reveal = PowerCatalog.FindField(_currentEntry, "reveallos");
            if (reveal == null && text.Equals("Revealer", StringComparison.OrdinalIgnoreCase)) return;
            if (reveal?.Value.Trim() == text) return;
            PowerCatalog.SetField(_currentEntry, "reveallos", text);
        }
        else
        {
            var tag = ReferenceEquals(box, _lightingBox) ? "lighting" : "abstractpowerresetsabilitychargetype";
            if (tag != "lighting" && text.Length > 0 &&
                !_unitOrTypeNames.Contains(text, StringComparer.OrdinalIgnoreCase)) return;
            var old = PowerCatalog.FindField(_currentEntry, tag);
            if (text.Length == 0)
            {
                if (old == null) return;
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(old);
            }
            else
            {
                if (old?.Value == text) return;
                PowerCatalog.SetField(_currentEntry, tag, text);
            }
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshExtraVisibility();
    }

    private void ExtraSelector_LostFocus(AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || ReferenceEquals(box, _lightingBox)) return;
        var text = box.Text?.Trim() ?? "";
        var valid = ReferenceEquals(box, _revealUnitBox)
            ? text.Equals("Revealer", StringComparison.OrdinalIgnoreCase) ||
              _protoUnitNames.Contains(text, StringComparer.OrdinalIgnoreCase)
            : text.Length == 0 || _unitOrTypeNames.Contains(text, StringComparer.OrdinalIgnoreCase);
        if (valid) return;
        var saved = ReferenceEquals(box, _revealUnitBox)
            ? PowerCatalog.FindField(_currentEntry, "reveallos")?.Value.Trim() ?? "Revealer"
            : PowerCatalog.FindField(_currentEntry, "abstractpowerresetsabilitychargetype")?.Value ?? "";
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { box.Text = saved; }
        finally { _isPopulating = wasPopulating; }
    }

    private void ExtraFlag_Changed()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var old = PowerCatalog.FindField(_currentEntry, "playsbattlemusic");
        if (_playsBattleMusicCheck.IsChecked == true)
        {
            if (old != null) return;
            PowerCatalog.SetField(_currentEntry, "playsbattlemusic", "");
        }
        else
        {
            if (old == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(old);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshExtraVisibility();
    }

    private void BuildSoundSetRow(XElement sound, IReadOnlyList<string> suggestions)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        _soundSetRows.Children.Add(row);

        StackPanel Field(string label, Control control)
        {
            var field = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 12, 4)
            };
            field.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            field.Children.Add(control);
            row.Children.Add(field);
            return field;
        }

        void ChangeAttribute(string name, string? value)
        {
            if (_currentEntry == null || !CanEditProperties(_currentEntry) || sound.Parent != _currentEntry) return;
            var existing = FindPlacementFlag(sound, name);
            if (value == null)
            {
                if (existing == null) return;
                existing.Remove();
            }
            else
            {
                if (existing?.Value == value) return;
                SetPlacementAttribute(sound, name, value);
            }
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        }

        ComboBox Dropdown(string name, string[] options, string label, double width)
        {
            var current = FindPlacementFlag(sound, name)?.Value;
            var choices = current != null && !options.Contains(current, StringComparer.OrdinalIgnoreCase)
                ? options.Append(current).ToArray() : options;
            var box = new ComboBox { Width = width, MaxWidth = width, ItemsSource = choices,
                IsEnabled = IsModifiedMode };
            box.SelectedItem = choices.FirstOrDefault(option =>
                option.Equals(current, StringComparison.OrdinalIgnoreCase));
            var field = Field(label, box);
            field.IsVisible = IsModifiedMode || current != null;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is string selected) ChangeAttribute(name, selected);
            };
            return box;
        }

        Dropdown("type", SoundSetTypes, "Type", 120);
        var soundBox = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
        {
            Text = sound.Value,
            ItemsSource = suggestions,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        });
        soundBox.Width = soundBox.MaxWidth = 200;
        EditorAutoCompleteService.EnableDropdown(soundBox, () => _isPopulating, selectAllOnFirstClick: false);
        Field("Sound", soundBox).IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(sound.Value);
        void CommitSound()
        {
            if (_currentEntry == null || !CanEditProperties(_currentEntry) || sound.Parent != _currentEntry) return;
            var value = soundBox.Text ?? "";
            if (sound.Value == value) return;
            XmlTriviaPreserver.SetText(sound, value);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        }
        soundBox.TextChanged += (_, _) => CommitSound();
        soundBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == AutoCompleteBox.TextProperty) CommitSound();
        };
        Dropdown("listenertype", SoundSetListeners, "Listener", 190);

        void OptionalNumber(string name, string label)
        {
            var existing = FindPlacementFlag(sound, name);
            var button = PowerEditorDynamicControls.CreateOptionalButton(label);
            button.IsVisible = IsModifiedMode && existing == null;
            button.Margin = new Thickness(0, 0, 6, 4);
            row.Children.Add(button);
            var box = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox
            {
                Text = EditorNumericFieldStyle.FormatDisplay(existing?.Value),
                IsEnabled = IsModifiedMode
            });
            _soundNumericBoxes.Add((box, sound));
            var field = Field(label, box);
            field.IsVisible = existing != null;
            var remove = PowerEditorDynamicControls.CreateRemoveButton();
            remove.IsVisible = IsModifiedMode;
            field.Children.Add(remove);
            button.Click += (_, _) => { button.IsVisible = false; field.IsVisible = true; box.Focus(); };
            remove.Click += (_, _) =>
            {
                ChangeAttribute(name, null);
                box.Text = "";
                field.IsVisible = false;
                button.IsVisible = true;
            };
            box.TextChanged += (_, _) =>
            {
                var text = box.Text?.Trim() ?? "";
                if (text.Length == 0) { ChangeAttribute(name, null); return; }
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                    double.IsFinite(number) && number >= 0 &&
                    !NumericTextEquals(FindPlacementFlag(sound, name)?.Value, text))
                    ChangeAttribute(name, text);
            };
        }

        OptionalNumber("playdelay", "Delay");
        OptionalNumber("fadein", "Fade in");
        foreach (var (name, label) in new[]
                 { ("loop", "Loop"), ("positional", "Positional"),
                   ("fade", "Fade"), ("usesecondlocation", "Use Second Location") })
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = FindPlacementFlag(sound, name) != null,
                IsEnabled = IsModifiedMode,
                IsVisible = IsModifiedMode || FindPlacementFlag(sound, name) != null,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 4)
            };
            check.IsCheckedChanged += (_, _) => ChangeAttribute(name, check.IsChecked == true ? "" : null);
            row.Children.Add(check);
        }
        if (!IsModifiedMode) return;
        var delete = PowerEditorDynamicControls.CreateRemoveButton();
        delete.Margin = new Thickness(0, 0, 0, 4);
        delete.Click += (_, _) =>
        {
            if (_currentEntry == null || !CanEditProperties(_currentEntry) || sound.Parent != _currentEntry) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(sound);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RefreshSoundSets();
        };
        row.Children.Add(delete);
    }

    private void AddSoundSet_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var sound = PowerCatalog.AddRepeatedField(_currentEntry, "soundset", "");
        SetPlacementAttribute(sound, "type", "StartSound");
        SetPlacementAttribute(sound, "listenertype", "All");
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshSoundSets();
    }

    private void EffectAttribute_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var parts = tag.Split(':');
        var value = box.Text?.Trim() ?? "";
        if (value.Length > 0 &&
            (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
             !double.IsFinite(parsed) || parsed < 0)) return;
        var element = PowerCatalog.FindField(_currentEntry, parts[0]);
        var attribute = FindPlacementFlag(element, parts[1]);
        if (value.Length == 0)
        {
            if (attribute == null) return;
            attribute.Remove();
            if (element is { HasAttributes: false })
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(element);
        }
        else
        {
            if (NumericTextEquals(attribute?.Value, value)) return;
            element ??= PowerCatalog.SetField(_currentEntry, parts[0], "");
            SetPlacementAttribute(element, parts[1], value);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshEffectVisibility();
    }

    private void AddCameraRange_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _cameraRangeOpen = true;
        RefreshEffectVisibility();
        _cameraRangeBox.Focus();
    }

    private void RemoveCameraRange_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var camera = PowerCatalog.FindField(_currentEntry, "camerashake");
        var range = FindPlacementFlag(camera, "range");
        range?.Remove();
        if (camera is { HasAttributes: false })
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(camera);
        _cameraRangeOpen = false;
        SetBoxTextWithoutEditing(_cameraRangeBox, "");
        RefreshEffectVisibility();
        if (range != null) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private TextBox GetResolvedStringBox(string tag) => tag.ToLowerInvariant() switch
    {
        "displaynameid" => _displayNameBox,
        "rolloverid" => _rolloverBox,
        "activepowerrolloverid" => _activeRolloverBox,
        "notificationformatid" => _castMessageBox,
        "selfnotificationformatid" => _castSelfMessageBox,
        _ => _exclusiveMessageBox
    };

    private void RefreshOptionalPropertyVisibility()
    {
        if (_currentEntry == null)
            return;
        var orderHint = FindAttribute(_currentEntry, "orderhint") != null;
        var buildUp = PowerCatalog.FindField(_currentEntry, "builduptime") != null;
        var shutDown = PowerCatalog.FindField(_currentEntry, "shutdowntime") != null ||
                       PowerCatalog.FindField(_currentEntry, "shutdownfadetime") != null;
        var useLimit = _isUseLimitOpen || PowerCatalog.FindField(_currentEntry, "limitglobal") != null ||
                       PowerCatalog.FindField(_currentEntry, "limitplayer") != null;
        _orderHintRow.IsVisible = orderHint;
        _addOrderHintButton.IsVisible = IsModifiedMode && !orderHint;
        _removeOrderHintButton.IsVisible = IsModifiedMode && orderHint;
        _buildUpRow.IsVisible = buildUp;
        _addBuildUpButton.IsVisible = IsModifiedMode && !buildUp;
        _removeBuildUpButton.IsVisible = IsModifiedMode && buildUp;
        _shutDownRow.IsVisible = shutDown;
        _addShutDownButton.IsVisible = IsModifiedMode && !shutDown;
        _removeShutDownButton.IsVisible = IsModifiedMode && shutDown;
        _useLimitRow.IsVisible = useLimit;
        _addUseLimitButton.IsVisible = IsModifiedMode && !useLimit;
        _removeUseLimitButton.IsVisible = IsModifiedMode && useLimit;
        _limitLabel.IsVisible = useLimit;
        _singleActivationCheck.IsVisible = IsModifiedMode || _singleActivationCheck.IsChecked == true;
        _exclusiveCheck.IsVisible = IsModifiedMode || _exclusiveCheck.IsChecked == true;
        _activeTimeOnTargetCheck.IsVisible = IsModifiedMode || _activeTimeOnTargetCheck.IsChecked == true;
        _exclusiveMessageRow.IsVisible = PowerCatalog.FindField(_currentEntry, "exclusive") != null;
    }

    private void RefreshNotificationVisibility()
    {
        if (_currentEntry == null) return;
        var cast = PowerCatalog.FindField(_currentEntry, "notificationformatid");
        var selfCast = PowerCatalog.FindField(_currentEntry, "selfnotificationformatid");
        var alert = PowerCatalog.FindField(_currentEntry, "messagealertplayerrelation");
        var displayAlerts = PowerCatalog.FindField(_currentEntry, "displayalerts") != null;
        var hasNotifications = !string.IsNullOrWhiteSpace(cast?.Value) ||
                               !string.IsNullOrWhiteSpace(selfCast?.Value) ||
                               !string.IsNullOrWhiteSpace(alert?.Value) || displayAlerts;
        _notificationsSection.IsVisible = OtherSelected("Notification") &&
                                          ((IsModifiedMode && _notificationsOpen) || hasNotifications);
        _addNotificationsButton.IsVisible = IsModifiedMode && !_notificationsSection.IsVisible;
        _removeNotificationsButton.IsVisible = IsModifiedMode;
        _castMessageRow.IsVisible = (IsModifiedMode && _castMessageOpen) ||
                                    !string.IsNullOrWhiteSpace(cast?.Value);
        _castSelfMessageRow.IsVisible = (IsModifiedMode && _castSelfMessageOpen) ||
                                        !string.IsNullOrWhiteSpace(selfCast?.Value);
        _alertPlayerRow.IsVisible = (IsModifiedMode && _alertPlayerOpen) ||
                                    !string.IsNullOrWhiteSpace(alert?.Value);
        _addCastMessageButton.IsVisible = IsModifiedMode && !_castMessageRow.IsVisible;
        _addCastSelfMessageButton.IsVisible = IsModifiedMode && !_castSelfMessageRow.IsVisible;
        _addAlertPlayerButton.IsVisible = IsModifiedMode && !_alertPlayerRow.IsVisible;
        _removeCastMessageButton.IsVisible = IsModifiedMode;
        _removeCastSelfMessageButton.IsVisible = IsModifiedMode;
        _removeAlertPlayerButton.IsVisible = IsModifiedMode;
        _displayAlertsCheck.IsVisible = IsModifiedMode || displayAlerts;

        var minimap = PowerCatalog.FindField(_currentEntry, "minimapeventtime");
        var recipient = FindPlacementFlag(minimap, "sendalertto")?.Value;
        var hasMinimap = !string.IsNullOrWhiteSpace(recipient) ||
                         !string.IsNullOrWhiteSpace(minimap?.Value);
        _minimapNotificationsSection.IsVisible = OtherSelected("Minimap Notification") &&
                                                 ((IsModifiedMode && _minimapNotificationsOpen) || hasMinimap);
        _addMinimapNotificationsButton.IsVisible = IsModifiedMode && !_minimapNotificationsSection.IsVisible;
        _removeMinimapNotificationsButton.IsVisible = IsModifiedMode;
        _minimapSendToLabel.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(recipient);
        _minimapSendToBox.IsVisible = _minimapSendToLabel.IsVisible;
        _minimapDurationLabel.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(minimap?.Value);
        _minimapDurationBox.IsVisible = _minimapDurationLabel.IsVisible;

        var list = PowerCatalog.FindField(_currentEntry, "activelistvisibility");
        var hidden = PowerCatalog.FindField(_currentEntry, "hideonactivegplist") != null;
        _activeVisibilitySection.IsVisible = OtherSelected("Active Power Visibility") &&
            ((IsModifiedMode && _activeVisibilityOpen) || !string.IsNullOrWhiteSpace(list?.Value) || hidden);
        _addActiveVisibilityButton.IsVisible = IsModifiedMode && !_activeVisibilitySection.IsVisible;
        _removeActiveVisibilityButton.IsVisible = IsModifiedMode;
        _activeListVisibilityFields.IsVisible = (IsModifiedMode && _activeListVisibilityOpen) ||
                                                !string.IsNullOrWhiteSpace(list?.Value);
        _addActiveListVisibilityButton.IsVisible = IsModifiedMode && !_activeListVisibilityFields.IsVisible;
        _removeActiveListVisibilityButton.IsVisible = IsModifiedMode;
        _hideOnActiveListCheck.IsVisible = IsModifiedMode || hidden;
        _addNotificationSectionsRow.IsVisible = false;
    }

    private void AddNotifications_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _notificationsOpen = true;
        RefreshNotificationVisibility();
    }

    private void RemoveNotifications_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var changed = false;
        foreach (var tag in new[] { "notificationformatid", "selfnotificationformatid",
                     "messagealertplayerrelation", "displayalerts" })
        {
            var field = PowerCatalog.FindField(_currentEntry, tag);
            if (field == null) continue;
            if ((tag is "notificationformatid" or "selfnotificationformatid") &&
                field.Value.Trim().Equals(PowerCatalog.BuildStringId(CurrentPowerName!, tag),
                    StringComparison.OrdinalIgnoreCase))
                ScheduleStringRemoval(field.Value.Trim(), CurrentPowerName!);
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            changed = true;
        }
        _notificationsOpen = _castMessageOpen = _castSelfMessageOpen = _alertPlayerOpen = false;
        SetBoxTextWithoutEditing(_castMessageBox, "");
        SetBoxTextWithoutEditing(_castSelfMessageBox, "");
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _alertPlayerBox.SelectedItem = "All"; _displayAlertsCheck.IsChecked = false; }
        finally { _isPopulating = wasPopulating; }
        RefreshNotificationVisibility();
        if (changed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void AddNotificationField_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        if (tag == "messagealertplayerrelation")
        {
            _alertPlayerOpen = true;
            PowerCatalog.SetField(_currentEntry, tag, "All");
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try { _alertPlayerBox.SelectedItem = "All"; }
            finally { _isPopulating = wasPopulating; }
        }
        else
        {
            var id = PowerCatalog.BuildStringId(CurrentPowerName!, tag);
            PowerCatalog.SetField(_currentEntry, tag, id);
            _session.SetPendingString(id, "", CurrentPowerName!, preserveExisting: true);
            var box = GetResolvedStringBox(tag);
            SetBoxTextWithoutEditing(box, "");
            if (tag == "notificationformatid") _castMessageOpen = true;
            else _castSelfMessageOpen = true;
        }
        RefreshNotificationVisibility();
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        (tag == "messagealertplayerrelation" ? (Control)_alertPlayerBox : GetResolvedStringBox(tag)).Focus();
    }

    private void RemoveNotificationField_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (field != null)
        {
            if ((tag is "notificationformatid" or "selfnotificationformatid") &&
                field.Value.Trim().Equals(PowerCatalog.BuildStringId(CurrentPowerName!, tag),
                    StringComparison.OrdinalIgnoreCase))
                ScheduleStringRemoval(field.Value.Trim(), CurrentPowerName!);
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        if (tag == "notificationformatid")
        {
            _castMessageOpen = false;
            SetBoxTextWithoutEditing(_castMessageBox, "");
        }
        else if (tag == "selfnotificationformatid")
        {
            _castSelfMessageOpen = false;
            SetBoxTextWithoutEditing(_castSelfMessageBox, "");
        }
        else
        {
            _alertPlayerOpen = false;
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try { _alertPlayerBox.SelectedItem = "All"; }
            finally { _isPopulating = wasPopulating; }
        }
        RefreshNotificationVisibility();
        if (field != null) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void AlertPlayer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            PowerCatalog.FindField(_currentEntry, "messagealertplayerrelation") == null ||
            _alertPlayerBox.SelectedItem is not string relation ||
            !AlertPlayerRelations.Contains(relation, StringComparer.OrdinalIgnoreCase)) return;
        PowerCatalog.SetField(_currentEntry, "messagealertplayerrelation", relation);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void NotificationFlag_Changed(CheckBox check, string tag)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (check.IsChecked == true)
        {
            if (field != null) return;
            PowerCatalog.SetField(_currentEntry, tag, "");
        }
        else
        {
            if (field == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshNotificationVisibility();
    }

    private void AddMinimapNotifications_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var minimap = PowerCatalog.SetField(_currentEntry, "minimapeventtime", "0");
        SetPlacementAttribute(minimap, "sendalertto", "All");
        _minimapNotificationsOpen = true;
        SetBoxTextWithoutEditing(_minimapDurationBox, "0");
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _minimapSendToBox.SelectedItem = "All"; }
        finally { _isPopulating = wasPopulating; }
        RefreshNotificationVisibility();
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RemoveMinimapNotifications_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var minimap = PowerCatalog.FindField(_currentEntry, "minimapeventtime");
        if (minimap != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(minimap);
        _minimapNotificationsOpen = false;
        SetBoxTextWithoutEditing(_minimapDurationBox, "");
        RefreshNotificationVisibility();
        if (minimap != null) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void MinimapSendTo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _minimapSendToBox.SelectedItem is not string recipient ||
            !MinimapRecipients.Contains(recipient, StringComparer.OrdinalIgnoreCase)) return;
        var minimap = PowerCatalog.FindField(_currentEntry, "minimapeventtime");
        if (minimap == null) return;
        var attribute = FindPlacementFlag(minimap, "sendalertto");
        if (attribute?.Value == recipient) return;
        SetPlacementAttribute(minimap, "sendalertto", recipient);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void MinimapDuration_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var minimap = PowerCatalog.FindField(_currentEntry, "minimapeventtime");
        if (minimap == null) return;
        var text = _minimapDurationBox.Text?.Trim() ?? "";
        if (text.Length > 0 &&
            (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ||
             !double.IsFinite(duration) || duration < 0)) return;
        if (minimap.Value.Trim().Equals(text, StringComparison.Ordinal) ||
            NumericTextEquals(minimap.Value, text)) return;
        XmlTriviaPreserver.SetText(minimap, text);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddActiveVisibility_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _activeVisibilityOpen = true;
        RefreshNotificationVisibility();
    }

    private void RemoveActiveVisibility_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var changed = false;
        foreach (var tag in new[] { "activelistvisibility", "hideonactivegplist" })
        {
            var field = PowerCatalog.FindField(_currentEntry, tag);
            if (field == null) continue;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            changed = true;
        }
        _activeVisibilityOpen = _activeListVisibilityOpen = false;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _activeListVisibilityBox.SelectedItem = null; _hideOnActiveListCheck.IsChecked = false; }
        finally { _isPopulating = wasPopulating; }
        RefreshNotificationVisibility();
        if (changed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void ActiveListVisibility_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _activeListVisibilityBox.SelectedItem is not string visibility ||
            !ActiveListVisibilities.Contains(visibility, StringComparer.OrdinalIgnoreCase)) return;
        var field = PowerCatalog.FindField(_currentEntry, "activelistvisibility");
        if (field?.Value.Trim() == visibility) return;
        PowerCatalog.SetField(_currentEntry, "activelistvisibility", visibility);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshNotificationVisibility();
    }

    private void AddActiveListVisibility_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _activeListVisibilityOpen = true;
        RefreshNotificationVisibility();
        _activeListVisibilityBox.Focus();
    }

    private void RemoveActiveListVisibility_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "activelistvisibility");
        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _activeListVisibilityOpen = false;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { _activeListVisibilityBox.SelectedItem = null; }
        finally { _isPopulating = wasPopulating; }
        RefreshNotificationVisibility();
        if (field != null) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void PlacementBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _placementBox.SelectedItem is not string mode)
            return;
        var placement = PowerCatalog.FindField(_currentEntry, "placement");
        if (placement != null && placement.Value.Trim().Equals(mode, StringComparison.Ordinal))
            return;
        PowerCatalog.SetField(_currentEntry, "placement", mode);
        if (mode.Equals("Skip", StringComparison.OrdinalIgnoreCase))
            ClearPlacementSpecificXml();
        RefreshPlacementRestrictions();
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshPlacementFlags();
        RefreshPlacementTypes();
        RefreshPlacementVisibility();
    }

    private void PlacementFlagPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _placementFlagPicker.SelectedItem is not string flag ||
            !PlacementFlags.Contains(flag, StringComparer.OrdinalIgnoreCase) ||
            flag.Equals("forceOnMap", StringComparison.OrdinalIgnoreCase) ||
            (flag.Equals("notOverlapSelfOrAlly", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(_placementBox.SelectedItem as string, "Overlap", StringComparison.OrdinalIgnoreCase)))
            return;

        var standaloneTag = GetStandalonePlacementFlagTag(flag);
        if (standaloneTag != null)
        {
            if (PowerCatalog.FindField(_currentEntry, standaloneTag) == null)
            {
                PowerCatalog.SetField(_currentEntry, standaloneTag, "");
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            }
        }
        else
        {
            var placement = PowerCatalog.FindField(_currentEntry, "placement") ??
                            PowerCatalog.SetField(_currentEntry, "placement",
                                _placementBox.SelectedItem as string ?? "Full");
            if (FindPlacementFlag(placement, flag) == null)
            {
                placement.Add(new XAttribute(flag, ""));
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            }
        }
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _placementFlagPicker.SelectedItem = null;
            _placementFlagPicker.Text = "";
        }
        finally { _isPopulating = wasPopulating; }
        RefreshPlacementFlags();
    }

    private void RefreshPlacementFlags()
    {
        _placementFlagChips.Children.Clear();
        var placement = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "placement");
        var selected = PlacementFlags.Where(flag => GetStandalonePlacementFlagTag(flag) is { } tag
            ? _currentEntry != null && PowerCatalog.FindField(_currentEntry, tag) != null
            : FindCataloguedPlacementFlag(placement, flag) != null).ToList();
        foreach (var flag in selected)
        {
            var captured = flag;
            _placementFlagChips.Children.Add(PowerEditorDynamicControls.CreateChip(captured,
                IsModifiedMode ? () =>
                {
                    if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                    if (GetStandalonePlacementFlagTag(captured) is { } tag)
                    {
                        var field = PowerCatalog.FindField(_currentEntry, tag);
                        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
                    }
                    else
                        FindCataloguedPlacementFlag(
                            PowerCatalog.FindField(_currentEntry, "placement"), captured)?.Remove();
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    RefreshPlacementFlags();
                } : null, readOnly: !IsModifiedMode));
        }
        _placementFlagPicker.ItemsSource = PlacementFlags
            .Where(flag => !selected.Contains(flag, StringComparer.OrdinalIgnoreCase) &&
                           !flag.Equals("forceOnMap", StringComparison.OrdinalIgnoreCase) &&
                           (!flag.Equals("notOverlapSelfOrAlly", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_placementBox.SelectedItem as string, "Overlap", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _placementFlagPicker.IsVisible = IsModifiedMode;
        _placementFlagsRow.IsVisible = IsModifiedMode || selected.Count > 0;
    }

    private static XAttribute? FindPlacementFlag(XElement? placement, string flag)
        => placement?.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static XAttribute? FindCataloguedPlacementFlag(XElement? placement, string flag)
    {
        var exact = FindPlacementFlag(placement, flag);
        if (exact != null) return exact;
        return flag.Equals("disallowWaterCastRadius", StringComparison.OrdinalIgnoreCase)
            ? FindPlacementFlag(placement, "disallowsWaterCastRadius")
            : null;
    }

    private static string? GetStandalonePlacementFlagTag(string flag)
        => flag.Equals("ExpandCheckPlacementHull", StringComparison.OrdinalIgnoreCase)
            ? "expandcheckplacementhull"
            : flag.Equals("VectorPlacement", StringComparison.OrdinalIgnoreCase)
                ? "vectorplacement"
                : flag.Equals("ignoreattacktypesforplacement", StringComparison.OrdinalIgnoreCase)
                    ? "ignoreattacktypesforplacement" : null;

    private void ClearPlacementSpecificXml()
    {
        if (_currentEntry == null) return;
        PowerCatalog.FindField(_currentEntry, "placement")?.RemoveAttributes();
        foreach (var field in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("expandcheckplacementhull", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("vectorplacement", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("ignoreattacktypesforplacement", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("abstractplacementtargettype", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("explicitlyrestrictedplacementtargettype", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("placementrestriction", StringComparison.OrdinalIgnoreCase))
                 .ToList())
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        _losProtoUnitOpen = false;
        _restrictPlacementOpen = false;
        _overlapTypeOpen = false;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _losProtoUnitBox.Text = "";
            _restrictPlacementBox.Text = "";
            _restrictDistanceBox.Text = "";
            _restrictAddObstructionCheck.IsChecked = false;
            _overlapTypeBox.Text = "";
        }
        finally { _isPopulating = wasPopulating; }
    }

    private void RefreshPlacementTypes()
    {
        RefreshTypeChips("abstractplacementtargettype", _targetTypePicker, _targetTypeChips);
        RefreshTypeChips("explicitlyrestrictedplacementtargettype", _excludeTypePicker, _excludeTypeChips);
        _targetTypeLabel.IsVisible = IsModifiedMode || _targetTypeChips.Children.Count > 0;
        _excludeTypeLabel.IsVisible = IsModifiedMode || _excludeTypeChips.Children.Count > 0;
        _placementTypesRow.IsVisible = _currentEntry != null &&
            (IsModifiedMode || _targetTypeChips.Children.Count > 0 || _excludeTypeChips.Children.Count > 0);
    }

    private void RefreshAttackTypes()
    {
        RefreshTypeChips("abstractattacktargettype", _attackTargetTypePicker, _attackTargetTypeChips);
        RefreshTypeChips("explicitlyrestrictedattacktargettype", _attackExcludeTypePicker,
            _attackExcludeTypeChips);
        _attackTargetTypeLabel.IsVisible = IsModifiedMode || _attackTargetTypeChips.Children.Count > 0;
        _attackExcludeTypeLabel.IsVisible = IsModifiedMode || _attackExcludeTypeChips.Children.Count > 0;
        _attackTargetRow.IsVisible = _currentEntry != null && OtherSelected("Attack target") &&
            (IsModifiedMode || _attackTargetTypeChips.Children.Count > 0 ||
             _attackExcludeTypeChips.Children.Count > 0);
    }

    private void RefreshTypeChips(string tag, AutoCompleteBox picker, WrapPanel chips)
    {
        chips.Children.Clear();
        var values = _currentEntry?.Elements().Where(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        foreach (var value in values)
        {
            var captured = value;
            chips.Children.Add(PowerEditorDynamicControls.CreateChip(captured, IsModifiedMode ? () =>
            {
                if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                var field = _currentEntry.Elements().FirstOrDefault(element =>
                    element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                    element.Value.Trim().Equals(captured, StringComparison.OrdinalIgnoreCase));
                if (field == null) return;
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RefreshPlacementTypes();
                RefreshAttackTypes();
            } : null, readOnly: !IsModifiedMode));
        }
        picker.ItemsSource = _unitOrTypeNames
            .Where(value => !values.Contains(value, StringComparer.OrdinalIgnoreCase)).ToList();
        picker.IsVisible = IsModifiedMode;
    }

    private void PlacementTypePicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: string value } picker)
            CommitPlacementType(picker, value);
    }

    private void PlacementTypePicker_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        CommitPlacementType(picker, picker.Text?.Trim() ?? "");
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { picker.SelectedItem = null; picker.Text = ""; }
        finally { _isPopulating = wasPopulating; }
    }

    private void CommitPlacementType(AutoCompleteBox picker, string typed)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            ((ReferenceEquals(picker, _targetTypePicker) || ReferenceEquals(picker, _excludeTypePicker)) &&
             string.Equals(_placementBox.SelectedItem as string, "Skip", StringComparison.OrdinalIgnoreCase))) return;
        var value = _unitOrTypeNames.FirstOrDefault(name => name.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (value == null) return;
        var tag = ReferenceEquals(picker, _targetTypePicker) ? "abstractplacementtargettype" :
            ReferenceEquals(picker, _excludeTypePicker) ? "explicitlyrestrictedplacementtargettype" :
            ReferenceEquals(picker, _attackTargetTypePicker) ? "abstractattacktargettype" :
            "explicitlyrestrictedattacktargettype";
        if (_currentEntry.Elements().Any(element =>
                element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        var last = _currentEntry.Elements().LastOrDefault(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (last == null)
            PowerCatalog.SetField(_currentEntry, tag, value);
        else
        {
            var indentation = last.PreviousNode is XText whitespace &&
                              string.IsNullOrWhiteSpace(whitespace.Value)
                ? whitespace.Value : "\n    ";
            last.AddAfterSelf(new XText(indentation), new XElement(last.Name, value));
        }
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { picker.SelectedItem = null; picker.Text = ""; }
        finally { _isPopulating = wasPopulating; }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshPlacementTypes();
        RefreshAttackTypes();
    }

    private void RefreshPlacementVisibility()
    {
        var placement = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "placement");
        _placementDetailsHost.IsVisible = _currentEntry != null &&
            (!IsModifiedMode || !string.Equals(_placementBox.SelectedItem as string, "Skip",
                StringComparison.OrdinalIgnoreCase));
        _placementFlagsRow.IsVisible = _currentEntry != null &&
            (IsModifiedMode || _placementFlagChips.Children.Count > 0) &&
            (!IsModifiedMode || !string.Equals(_placementBox.SelectedItem as string, "Skip",
                StringComparison.OrdinalIgnoreCase));
        SetPlacementOptionVisibility(_losProtoUnitRow, _losProtoUnitLabel, _addLosProtoUnitButton,
            _losProtoUnitFields, _removeLosProtoUnitButton, _losProtoUnitOpen ||
            FindPlacementFlag(placement, "losprotounit") != null);
        SetPlacementOptionVisibility(_restrictPlacementRow, _restrictPlacementLabel,
            _addRestrictPlacementButton, _restrictPlacementFields, _removeRestrictPlacementButton,
            _restrictPlacementOpen || FindPlacementFlag(placement, "restrictplacementnear") != null ||
            FindPlacementFlag(placement, "restrictplacementdistance") != null ||
            FindPlacementFlag(placement, "restrictplacementnearaddobstruction") != null);
        SetPlacementOptionVisibility(_overlapTypeRow, _overlapTypeLabel, _addOverlapTypeButton,
            _overlapTypeFields, _removeOverlapTypeButton, _overlapTypeOpen ||
            FindPlacementFlag(placement, "overlaptype") != null);
        var orderedOptions = new List<Control>();
        if (_losProtoUnitRow.IsVisible) orderedOptions.Add(_losProtoUnitRow);
        if (_restrictPlacementRow.IsVisible) orderedOptions.Add(_restrictPlacementRow);
        if (_overlapTypeRow.IsVisible) orderedOptions.Add(_overlapTypeRow);
        if (_placementRestrictionRows.IsVisible) orderedOptions.Add(_placementRestrictionRows);
        if (_addLosProtoUnitButton.IsVisible) orderedOptions.Add(_addLosProtoUnitButton);
        if (_addRestrictPlacementButton.IsVisible) orderedOptions.Add(_addRestrictPlacementButton);
        if (_addOverlapTypeButton.IsVisible) orderedOptions.Add(_addOverlapTypeButton);
        _addPlacementRestrictionButton.IsVisible = IsModifiedMode && _placementDetailsHost.IsVisible;
        if (_addPlacementRestrictionButton.IsVisible) orderedOptions.Add(_addPlacementRestrictionButton);
        _placementOptionsFlow.Children.Clear();
        foreach (var option in orderedOptions)
            _placementOptionsFlow.Children.Add(option);
        _placementOptionButtonsRow.IsVisible = _placementDetailsHost.IsVisible &&
            (IsModifiedMode || _losProtoUnitRow.IsVisible || _restrictPlacementRow.IsVisible ||
             _overlapTypeRow.IsVisible || _placementRestrictionRows.IsVisible);
    }

    private void RefreshPlacementRestrictions()
    {
        _placementRestrictionRows.Children.Clear();
        _placementRestrictionFields.Clear();
        if (_currentEntry == null)
        {
            _placementRestrictionRows.IsVisible = false;
            return;
        }
        foreach (var restriction in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("placementrestriction", StringComparison.OrdinalIgnoreCase)))
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 6, 4)
            };
            row.Children.Add(new TextBlock { Text = "Placement restriction", VerticalAlignment = VerticalAlignment.Center });
            var type = new AutoCompleteBox
            {
                Width = 200, MaxWidth = 200, ItemsSource = _unitOrTypeNames,
                FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0,
                Text = restriction.Value.Trim(), IsEnabled = IsModifiedMode
            };
            EditorAutoCompleteService.EnableDropdown(type, () => _isPopulating, selectAllOnFirstClick: false);
            type.SelectionChanged += (_, _) => CommitPlacementRestrictionType(restriction, type);
            type.LostFocus += (_, _) => CommitPlacementRestrictionType(restriction, type);
            row.Children.Add(type);
            row.Children.Add(new TextBlock { Text = "Radius", VerticalAlignment = VerticalAlignment.Center });
            var radius = new TextBox
            {
                Width = 110, MaxWidth = 110,
                Text = EditorNumericFieldStyle.FormatDisplay(FindPlacementFlag(restriction, "radius")?.Value),
                IsEnabled = IsModifiedMode
            };
            EditorNumericFieldStyle.ConfigureNumericTextBox(radius);
            radius.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty)
                    CommitPlacementRestrictionRadius(restriction, radius);
            };
            row.Children.Add(radius);
            if (IsModifiedMode)
            {
                var remove = PowerEditorDynamicControls.CreateRemoveButton();
                remove.Click += (_, _) =>
                {
                    if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
                        restriction.Parent != _currentEntry) return;
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(restriction);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    RefreshPlacementRestrictions();
                    RefreshPlacementVisibility();
                };
                row.Children.Add(remove);
            }
            _placementRestrictionRows.Children.Add(row);
            _placementRestrictionFields.Add((type, radius, restriction));
        }
        _placementRestrictionRows.IsVisible = _placementRestrictionRows.Children.Count > 0;
    }

    private void AddPlacementRestriction_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            string.Equals(_placementBox.SelectedItem as string, "Skip", StringComparison.OrdinalIgnoreCase)) return;
        var restriction = PowerCatalog.AddRepeatedField(_currentEntry, "placementrestriction", "");
        SetPlacementAttribute(restriction, "radius", "0");
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshPlacementRestrictions();
        RefreshPlacementVisibility();
        _placementRestrictionFields.Last().Type.Focus();
    }

    private void CommitPlacementRestrictionType(XElement restriction, AutoCompleteBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || restriction.Parent != _currentEntry) return;
        var typed = box.Text?.Trim() ?? "";
        var match = _unitOrTypeNames.FirstOrDefault(value => value.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try { box.Text = restriction.Value.Trim(); }
            finally { _isPopulating = wasPopulating; }
            return;
        }
        if (restriction.Value.Trim().Equals(match, StringComparison.Ordinal)) return;
        XmlTriviaPreserver.SetText(restriction, match);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void CommitPlacementRestrictionRadius(XElement restriction, TextBox box)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || restriction.Parent != _currentEntry) return;
        var typed = box.Text?.Trim() ?? "";
        if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < 0) return;
        if (FindPlacementFlag(restriction, "radius")?.Value == typed) return;
        SetPlacementAttribute(restriction, "radius", typed);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void SetPlacementOptionVisibility(Control row, Control label, Control addButton,
        Control fields, Control removeButton, bool open)
    {
        row.IsVisible = _currentEntry != null && open;
        label.IsVisible = open;
        addButton.IsVisible = IsModifiedMode && !open;
        fields.IsVisible = open;
        removeButton.IsVisible = IsModifiedMode && open;
    }

    private void AddPlacementOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        if (tag == "losprotounit") _losProtoUnitOpen = true;
        if (tag == "restrictplacementnear")
        {
            _restrictPlacementOpen = true;
            if (string.IsNullOrWhiteSpace(_restrictDistanceBox.Text))
                SetBoxTextWithoutEditing(_restrictDistanceBox, "0");
        }
        if (tag == "overlaptype") _overlapTypeOpen = true;
        RefreshPlacementVisibility();
        (tag switch
        {
            "losprotounit" => _losProtoUnitBox,
            "restrictplacementnear" => _restrictPlacementBox,
            _ => _overlapTypeBox
        }).Focus();
    }

    private void RemovePlacementOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var placement = PowerCatalog.FindField(_currentEntry, "placement");
        var tags = tag == "restrictplacementnear"
            ? new[] { tag, "restrictplacementdistance", "restrictplacementnearaddobstruction" }
            : new[] { tag };
        var changed = false;
        foreach (var name in tags)
        {
            var attribute = FindPlacementFlag(placement, name);
            if (attribute == null) continue;
            attribute.Remove();
            changed = true;
        }
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            if (tag == "losprotounit") { _losProtoUnitOpen = false; _losProtoUnitBox.Text = ""; }
            if (tag == "restrictplacementnear")
            {
                _restrictPlacementOpen = false;
                _restrictPlacementBox.Text = "";
                _restrictDistanceBox.Text = "";
                _restrictAddObstructionCheck.IsChecked = false;
            }
            if (tag == "overlaptype") { _overlapTypeOpen = false; _overlapTypeBox.Text = ""; }
        }
        finally { _isPopulating = wasPopulating; }
        RefreshPlacementVisibility();
        if (changed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void PlacementOption_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is AutoCompleteBox picker && picker.SelectedItem is string selected)
            CommitPlacementOption(picker, selected);
    }

    private void PlacementOption_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox picker || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var value = picker.Text?.Trim() ?? "";
        if (!CommitPlacementOption(picker, value))
        {
            var placement = PowerCatalog.FindField(_currentEntry, "placement");
            var tag = GetPlacementOptionTag(picker);
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try { picker.Text = FindPlacementFlag(placement, tag)?.Value ?? ""; }
            finally { _isPopulating = wasPopulating; }
        }
    }

    private bool CommitPlacementOption(AutoCompleteBox picker, string typed)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return false;
        var catalog = ReferenceEquals(picker, _losProtoUnitBox) ? _protoUnitNames : _unitOrTypeNames;
        var match = catalog.FirstOrDefault(name => name.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (match == null) return false;
        var tag = GetPlacementOptionTag(picker);
        var placement = PowerCatalog.FindField(_currentEntry, "placement") ??
                        PowerCatalog.SetField(_currentEntry, "placement", _placementBox.SelectedItem as string ?? "Full");
        var attribute = FindPlacementFlag(placement, tag);
        if (attribute != null && attribute.Value.Equals(match, StringComparison.Ordinal)) return true;
        if (attribute == null) placement.Add(new XAttribute(tag, match));
        else attribute.Value = match;
        if (tag == "restrictplacementnear")
        {
            if (double.TryParse(_restrictDistanceBox.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var distance) && double.IsFinite(distance) && distance >= 0)
                SetPlacementAttribute(placement, "restrictplacementdistance", _restrictDistanceBox.Text!.Trim());
            if (_restrictAddObstructionCheck.IsChecked == true)
                SetPlacementAttribute(placement, "restrictPlacementNearAddObstruction", "");
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        return true;
    }

    private string GetPlacementOptionTag(AutoCompleteBox picker)
        => ReferenceEquals(picker, _losProtoUnitBox) ? "losprotounit" :
            ReferenceEquals(picker, _restrictPlacementBox) ? "restrictplacementnear" : "overlaptype";

    private static void SetPlacementAttribute(XElement placement, string tag, string value)
    {
        var existing = FindPlacementFlag(placement, tag);
        if (existing == null) placement.Add(new XAttribute(tag, value));
        else existing.Value = value;
    }

    private void RefreshRangeAndTargetVisibility()
    {
        var indicator = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "rangeindicator");
        var decal = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "rangeindicatorcustomdecal");
        var shape = _rangeIndicatorBox.SelectedItem as string;
        var arrow = string.Equals(shape, "Arrow", StringComparison.OrdinalIgnoreCase);
        var circle = string.Equals(shape, "Circle", StringComparison.OrdinalIgnoreCase);
        var hasIndicator = !string.IsNullOrWhiteSpace(indicator?.Value) || indicator?.HasAttributes == true;
        var hasDecal = !string.IsNullOrWhiteSpace(decal?.Value);
        _rangeIndicatorRow.IsVisible = _currentEntry != null && OtherSelected("Range Indicator") &&
                                       (IsModifiedMode || hasIndicator || hasDecal);
        _rangeIndicatorProtoRow.IsVisible = _currentEntry != null &&
            PowerCatalog.FindField(_currentEntry, "rangeindicatorprotoid") != null;
        _rangeIndicatorBox.IsVisible = IsModifiedMode || !string.IsNullOrWhiteSpace(indicator?.Value);
        SetIndicatorNumberVisibility(_indicatorRangeLabel, _indicatorRangeBox, indicator,
            IsModifiedMode && shape != null, allowed: true);
        SetIndicatorNumberVisibility(_indicatorWidthLabel, _indicatorWidthBox, indicator,
            IsModifiedMode && arrow, allowed: arrow);
        SetIndicatorNumberVisibility(_indicatorSpeedLabel, _indicatorSpeedBox, indicator,
            IsModifiedMode && circle, allowed: circle);
        _rangeDecalFields.IsVisible = hasDecal || (IsModifiedMode && _isRangeDecalOpen);
        _addRangeDecalButton.IsVisible = IsModifiedMode && shape != null && !_rangeDecalFields.IsVisible;
        _removeRangeDecalButton.IsVisible = IsModifiedMode && _rangeDecalFields.IsVisible;

        var radius = _currentEntry == null ? null : PowerCatalog.FindField(_currentEntry, "radius");
        var hasRadius = !string.IsNullOrWhiteSpace(radius?.Value);
        var hasGlobal = _currentEntry != null && PowerCatalog.FindField(_currentEntry, "global") != null;
        _radiusRow.IsVisible = _currentEntry != null && OtherSelected("Radius") &&
                               (IsModifiedMode || hasRadius || hasGlobal);
        _radiusLabel.IsVisible = IsModifiedMode || hasRadius || hasGlobal;
        _radiusBox.IsVisible = _globalCheck.IsChecked != true && (IsModifiedMode || hasRadius);
        _globalCheck.IsVisible = IsModifiedMode || hasGlobal;
        _powerPlayerRelationRow.IsVisible = _currentEntry != null && OtherSelected("Power Player Relation") &&
            (IsModifiedMode || !string.IsNullOrWhiteSpace(
                PowerCatalog.FindField(_currentEntry, "powerplayerrelation")?.Value));
    }

    private static void SetIndicatorNumberVisibility(Control label, TextBox box,
        XElement? indicator, bool offered, bool allowed)
    {
        var present = allowed && !string.IsNullOrWhiteSpace(
            FindPlacementFlag(indicator, (string)box.Tag!)?.Value);
        label.IsVisible = allowed && (offered || present);
        box.IsVisible = allowed && (offered || present);
    }

    private void RangeIndicator_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _rangeIndicatorBox.SelectedItem is not string shape ||
            !RangeIndicatorModes.Contains(shape, StringComparer.OrdinalIgnoreCase)) return;
        var indicator = PowerCatalog.FindField(_currentEntry, "rangeindicator");
        if (indicator == null || !indicator.Value.Trim().Equals(shape, StringComparison.Ordinal))
        {
            indicator = PowerCatalog.SetField(_currentEntry, "rangeindicator", shape);
            if (shape != "Arrow")
            {
                FindPlacementFlag(indicator, "width")?.Remove();
                SetBoxTextWithoutEditing(_indicatorWidthBox, "");
            }
            if (shape != "Circle")
            {
                FindPlacementFlag(indicator, "speed")?.Remove();
                SetBoxTextWithoutEditing(_indicatorSpeedBox, "");
            }
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        }
        RefreshRangeAndTargetVisibility();
    }

    private void RangeIndicatorNumber_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var indicator = PowerCatalog.FindField(_currentEntry, "rangeindicator");
        if (indicator == null) return;
        var text = box.Text?.Trim() ?? "";
        var existing = FindPlacementFlag(indicator, tag);
        if (text.Length == 0)
        {
            if (existing == null) return;
            existing.Remove();
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0) return;
            if (NumericTextEquals(existing?.Value, text)) return;
            SetPlacementAttribute(indicator, tag, text);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddRangeDecal_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _isRangeDecalOpen = true;
        RefreshRangeAndTargetVisibility();
        _rangeDecalBox.Focus();
    }

    private void RemoveRangeDecal_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var decal = PowerCatalog.FindField(_currentEntry, "rangeindicatorcustomdecal");
        if (decal != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(decal);
        _isRangeDecalOpen = false;
        SetBoxTextWithoutEditing(_rangeDecalBox, "");
        RefreshRangeAndTargetVisibility();
        if (decal == null) return;
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RangeDecal_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !_isRangeDecalOpen) return;
        var text = _rangeDecalBox.Text ?? "";
        var decal = PowerCatalog.FindField(_currentEntry, "rangeindicatorcustomdecal");
        if (string.IsNullOrWhiteSpace(text))
        {
            if (decal == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(decal);
        }
        else
        {
            if (decal?.Value == text) return;
            PowerCatalog.SetField(_currentEntry, "rangeindicatorcustomdecal", text);
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RestrictDistance_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var placement = PowerCatalog.FindField(_currentEntry, "placement");
        if (FindPlacementFlag(placement, "restrictplacementnear") == null) return;
        var text = _restrictDistanceBox.Text?.Trim() ?? "";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < 0) return;
        if (NumericTextEquals(FindPlacementFlag(placement, "restrictplacementdistance")?.Value, text)) return;
        SetPlacementAttribute(placement!, "restrictplacementdistance", text);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RestrictAddObstruction_Changed()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var placement = PowerCatalog.FindField(_currentEntry, "placement");
        if (FindPlacementFlag(placement, "restrictplacementnear") == null) return;
        var existing = FindPlacementFlag(placement, "restrictplacementnearaddobstruction");
        if (_restrictAddObstructionCheck.IsChecked == true)
        {
            if (existing != null) return;
            placement!.Add(new XAttribute("restrictPlacementNearAddObstruction", ""));
        }
        else
        {
            if (existing == null) return;
            existing.Remove();
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RefreshActiveRolloverVisibility()
    {
        var present = _currentEntry != null && PowerCatalog.FindField(_currentEntry, "activepowerrolloverid") != null;
        _activeRolloverRow.IsVisible = present;
        _addActiveRolloverButton.IsVisible = IsModifiedMode && _currentEntry != null && !present;
        _removeActiveRolloverButton.IsVisible = IsModifiedMode && present;
    }

    private void RefreshRawXmlFromCurrent()
    {
        if (_currentEntry != null && !_rawEditorDirty)
            SetRawXml(PowerCatalog.FormatPreview(_currentEntry));
    }

    private void MarkCurrentDirty()
    {
        _session.MarkDirty(CurrentPowerName);
        SetDirty(true);
    }

    private void ResolvedStringBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box)
            return;
        if (ConsumeProgrammaticStringBoxChange(box))
            return;
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        if ((tag is "activepowerrolloverid" or "notificationformatid" or "selfnotificationformatid") &&
            PowerCatalog.FindField(_currentEntry, tag) == null)
            return;
        if (tag == "exclusiveerrormsgid" && PowerCatalog.FindField(_currentEntry, "exclusive") == null)
            return;
        var id = PowerCatalog.FindField(_currentEntry, tag)?.Value.Trim() ?? "";
        if (id.Length == 0)
        {
            id = PowerCatalog.BuildStringId(CurrentPowerName!, tag);
            PowerCatalog.SetField(_currentEntry, tag, id);
            RefreshRawXmlFromCurrent();
        }
        _session.SetPendingString(id, box.Text ?? "", CurrentPowerName!);
        MarkCurrentDirty();
    }

    private void NumericBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry))
            return;
        if (tag == "radius" && _globalCheck.IsChecked == true) return;
        var text = box.Text?.Trim() ?? "";
        var originalText = PowerCatalog.FindField(_currentEntry, tag)?.Value.Trim();
        if (string.Equals(originalText, text, StringComparison.Ordinal) ||
            (double.TryParse(originalText, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var originalNumber) &&
             double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var editedNumber) && originalNumber.Equals(editedNumber)))
            return;
        if ((tag is "cost" or "repeatcost" or "radius") && text.Length == 0)
        {
            var existing = PowerCatalog.FindField(_currentEntry, tag);
            if (existing == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(existing);
        }
        else if (tag is "cost" or "repeatcost" or "limitglobal" or "limitplayer")
        {
            var existing = PowerCatalog.FindField(_currentEntry, tag);
            var powerCloneCost = (tag is "cost" or "repeatcost") &&
                _currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                    StringComparison.OrdinalIgnoreCase) == true;
            if (powerCloneCost)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number) || number < 0) return;
                PowerCatalog.SetField(_currentEntry, tag, text);
            }
            else
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
                    return;
                if (value == 0)
                {
                    if (existing == null) return;
                    XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(existing);
                }
                else
                    PowerCatalog.SetField(_currentEntry, tag, value.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || (tag == "radius" && value < 0))
                return;
            PowerCatalog.SetField(_currentEntry, tag, text);
        }
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
    }

    private void PowerPlayerRelation_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            _powerPlayerRelationBox.SelectedItem is not string relation ||
            !PowerPlayerRelations.Contains(relation, StringComparer.OrdinalIgnoreCase)) return;
        var field = PowerCatalog.FindField(_currentEntry, "powerplayerrelation");
        if (field != null && field.Value.Trim().Equals(relation, StringComparison.Ordinal)) return;
        PowerCatalog.SetField(_currentEntry, "powerplayerrelation", relation);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void GlobalCheck_Changed()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "global");
        var changed = false;
        if (_globalCheck.IsChecked == true)
        {
            var radius = PowerCatalog.FindField(_currentEntry, "radius");
            if (radius != null)
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(radius);
                SetBoxTextWithoutEditing(_radiusBox, "");
                changed = true;
            }
            if (field == null)
            {
                PowerCatalog.SetField(_currentEntry, "global", "");
                changed = true;
            }
        }
        else
        {
            if (field != null)
            {
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
                changed = true;
            }
        }
        RefreshRangeAndTargetVisibility();
        if (changed) { MarkCurrentDirty(); RefreshRawXmlFromCurrent(); }
    }

    private void CommitRangeIndicatorProto()
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "rangeindicatorprotoid");
        if (field == null) return;
        var typed = _rangeIndicatorProtoBox.Text?.Trim() ?? "";
        var match = _protoUnitNames.FirstOrDefault(value => value.Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try { _rangeIndicatorProtoBox.Text = field.Value.Trim(); }
            finally { _isPopulating = wasPopulating; }
            return;
        }
        if (field.Value.Trim().Equals(match, StringComparison.Ordinal)) return;
        XmlTriviaPreserver.SetText(field, match);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void RangeIndicatorProtoNumber_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "rangeindicatorprotoid");
        if (field == null) return;
        var typed = box.Text?.Trim() ?? "";
        if (typed.Length > 0 && (tag == "indicatorcount"
                ? !int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0
                : !double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                  !double.IsFinite(number) || number < 0)) return;
        var attribute = FindPlacementFlag(field, tag);
        if (typed.Length == 0)
        {
            if (attribute == null) return;
            attribute.Remove();
        }
        else if (attribute == null) SetPlacementAttribute(field, tag, typed);
        else if (NumericTextEquals(attribute.Value, typed)) return;
        else attribute.Value = typed;
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddOptionalTime_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        PowerCatalog.SetField(_currentEntry, tag, "0");
        var box = tag == "builduptime" ? _buildUpBox : _shutDownBox;
        SetBoxTextWithoutEditing(box, "0");
        if (tag == "shutdowntime")
        {
            PowerCatalog.SetField(_currentEntry, "shutdownfadetime", "0");
            SetBoxTextWithoutEditing(_shutDownFadeBox, "0");
        }
        RefreshOptionalPropertyVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
        box.Focus();
    }

    private void RemoveOptionalTime_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        SetBoxTextWithoutEditing(tag == "builduptime" ? _buildUpBox : _shutDownBox, "");
        if (tag == "shutdowntime")
        {
            var fade = PowerCatalog.FindField(_currentEntry, "shutdownfadetime");
            if (fade != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(fade);
            SetBoxTextWithoutEditing(_shutDownFadeBox, "");
        }
        RefreshOptionalPropertyVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
    }

    private void AddUseLimit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        _isUseLimitOpen = true;
        SetBoxTextWithoutEditing(_limitGlobalBox, "0");
        SetBoxTextWithoutEditing(_limitPlayerBox, "0");
        RefreshOptionalPropertyVisibility();
    }

    private void RemoveUseLimit_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var hadLimits = PowerCatalog.FindField(_currentEntry, "limitglobal") != null ||
                        PowerCatalog.FindField(_currentEntry, "limitplayer") != null;
        _isUseLimitOpen = false;
        foreach (var tag in new[] { "limitglobal", "limitplayer" })
        {
            var field = PowerCatalog.FindField(_currentEntry, tag);
            if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        SetBoxTextWithoutEditing(_limitGlobalBox, "");
        SetBoxTextWithoutEditing(_limitPlayerBox, "");
        RefreshOptionalPropertyVisibility();
        if (hadLimits)
        {
            RefreshRawXmlFromCurrent();
            MarkCurrentDirty();
        }
    }

    private void PowerFlag_Changed(CheckBox check)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        var tag = ReferenceEquals(check, _exclusiveCheck) ? "exclusive" : "singleactivation";
        if (check.IsChecked == true)
        {
            PowerCatalog.SetField(_currentEntry, tag, "");
            if (tag == "exclusive" && PowerCatalog.FindField(_currentEntry, "exclusiveerrormsgid") == null)
            {
                var id = PowerCatalog.BuildStringId(CurrentPowerName!, "exclusiveerrormsgid");
                PowerCatalog.SetField(_currentEntry, "exclusiveerrormsgid", id);
                _session.SetPendingString(id, "", CurrentPowerName!, preserveExisting: true);
                SetBoxTextWithoutEditing(_exclusiveMessageBox, "");
            }
        }
        else
        {
            var field = PowerCatalog.FindField(_currentEntry, tag);
            if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            if (tag == "exclusive")
            {
                var message = PowerCatalog.FindField(_currentEntry, "exclusiveerrormsgid");
                var id = message?.Value.Trim() ?? "";
                if (message != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(message);
                if (id.Equals(PowerCatalog.BuildStringId(CurrentPowerName!, "exclusiveerrormsgid"),
                        StringComparison.OrdinalIgnoreCase))
                    ScheduleStringRemoval(id, CurrentPowerName!);
                SetBoxTextWithoutEditing(_exclusiveMessageBox, "");
            }
        }
        RefreshOptionalPropertyVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
    }

    private void ActiveTimeOnTarget_Changed()
    {
        if (_isPopulating || _currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var field = PowerCatalog.FindField(_currentEntry, "activetimeontarget");
        if (_activeTimeOnTargetCheck.IsChecked == true)
            PowerCatalog.SetField(_currentEntry, "activetimeontarget", "");
        else if (field != null)
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void SetBoxTextWithoutEditing(TextBox box, string text)
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { box.Text = text; }
        finally { _isPopulating = wasPopulating; }
    }

    private void SetStringBoxTextWithoutEditing(TextBox box, string text)
    {
        _programmaticStringBoxValues[box] = text;
        SetBoxTextWithoutEditing(box, text);
    }

    private void OrderHint_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var text = _orderHintBox.Text?.Trim() ?? "";
        var attribute = FindAttribute(_currentEntry, "orderhint");
        if (text.Length == 0)
        {
            if (attribute == null) return;
            attribute.Remove();
            RefreshOptionalPropertyVisibility();
        }
        else
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || NumericTextEquals(attribute?.Value, text)) return;
            if (attribute == null)
                _currentEntry.SetAttributeValue("orderhint", text);
            else
                attribute.Value = text;
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddOrderHint_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            FindAttribute(_currentEntry, "orderhint") != null) return;
        _currentEntry.SetAttributeValue("orderhint", "0");
        SetBoxTextWithoutEditing(_orderHintBox, "0");
        RefreshOptionalPropertyVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
        _orderHintBox.Focus();
    }

    private void RemoveOrderHint_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
        var attribute = FindAttribute(_currentEntry, "orderhint");
        if (attribute == null) return;
        attribute.Remove();
        SetBoxTextWithoutEditing(_orderHintBox, "");
        RefreshOptionalPropertyVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
    }

    private bool ConsumeProgrammaticStringBoxChange(TextBox box)
    {
        if (!_programmaticStringBoxValues.Remove(box, out var expected))
            return false;
        return string.Equals(expected, box.Text ?? "", StringComparison.Ordinal);
    }

    private string? ValidateNumericProperties()
    {
        var orderHint = _currentEntry == null ? null : FindAttribute(_currentEntry, "orderhint");
        if (orderHint != null &&
            (!double.TryParse(orderHint.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var orderHintValue) || !double.IsFinite(orderHintValue)))
            return "orderhint must be a valid number.";
        if (_currentEntry != null && IsSwitchingEffectsPower(_currentEntry))
        {
            var tierType = PowerCatalog.FindField(_currentEntry, "tiertype")?.Value.Trim() ?? "Time";
            var interval = PowerCatalog.FindField(_currentEntry, "interval");
            if (tierType.Equals("Time", StringComparison.OrdinalIgnoreCase) && interval != null &&
                (!double.TryParse(interval.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                     out var intervalValue) || !double.IsFinite(intervalValue)))
                return "Switching Effects Interval must be a valid number.";
            foreach (var effects in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase)))
            {
                var requirement = tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase)
                    ? PowerCatalog.FindField(effects, "resourcetierreq")
                    : tierType.Equals("CombatXP", StringComparison.OrdinalIgnoreCase)
                        ? PowerCatalog.FindField(effects, "combatxptierreq") : null;
                if (requirement != null &&
                    (!int.TryParse(requirement.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                         out var requirementValue) || requirementValue < 0))
                    return "Tier Requirement must be a non-negative whole number.";
            }
        }
        foreach (var box in new[] { _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox })
        {
            var tag = (string)box.Tag!;
            if (PowerCatalog.FindField(_currentEntry!, tag) == null) continue;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value))
                return $"{tag} must be a valid number.";
        }
        foreach (var box in new[] { _costBox, _repeatCostBox, _limitGlobalBox, _limitPlayerBox })
        {
            var tag = (string)box.Tag!;
            if (PowerCatalog.FindField(_currentEntry!, tag) == null && string.IsNullOrWhiteSpace(box.Text))
                continue;
            var powerCloneCost = (tag is "cost" or "repeatcost") &&
                _currentEntry!.Attribute("type")?.Value.Trim().Equals("PowerClone",
                    StringComparison.OrdinalIgnoreCase) == true;
            if (powerCloneCost)
            {
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var number) || !double.IsFinite(number) || number < 0)
                    return $"{tag} must be a non-negative number.";
            }
            else if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                         out var value) || value < 0)
                return $"{tag} must be a non-negative whole number.";
        }
        if (!string.IsNullOrWhiteSpace(_radiusBox.Text) &&
            (!double.TryParse(_radiusBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var radius) || !double.IsFinite(radius) || radius < 0))
            return "radius must be a non-negative number.";
        if (!string.IsNullOrWhiteSpace(_minimapDurationBox.Text) &&
            (!double.TryParse(_minimapDurationBox.Text, NumberStyles.Float,
                 CultureInfo.InvariantCulture, out var minimapDuration) ||
             !double.IsFinite(minimapDuration) || minimapDuration < 0))
            return "Minimap notification duration must be a non-negative number.";
        foreach (var box in EffectAttributeBoxes())
        {
            if (!box.IsVisible || string.IsNullOrWhiteSpace(box.Text)) continue;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0)
                return $"{box.Tag} must be a non-negative number.";
        }
        foreach (var (box, sound) in _soundNumericBoxes)
        {
            if (sound.Parent != _currentEntry || !box.IsVisible || string.IsNullOrWhiteSpace(box.Text)) continue;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0)
                return "Sound set Delay and Fade in must be non-negative numbers.";
        }
        foreach (var box in new[]
                 { _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                   _accuracyBox, _allyDamageRatioBox })
        {
            if (string.IsNullOrWhiteSpace(box.Text)) continue;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0 ||
                (ReferenceEquals(box, _accuracyBox) && value > 1))
                return $"{box.Tag ?? "Reveal LOS radius"} must be a valid non-negative number" +
                       (ReferenceEquals(box, _accuracyBox) ? " between 0 and 1." : ".");
        }
        var resetText = _resetAbilityChargeBox.Text?.Trim() ?? "";
        var savedReset = PowerCatalog.FindField(_currentEntry!, "abstractpowerresetsabilitychargetype")?.Value.Trim() ?? "";
        if (resetText.Length > 0 && !resetText.Equals(savedReset, StringComparison.OrdinalIgnoreCase) &&
            !_unitOrTypeNames.Contains(resetText, StringComparer.OrdinalIgnoreCase))
            return "Reset Ability Charge for must be a valid Protounit or Unit Type.";
        var revealText = _revealUnitBox.Text?.Trim() ?? "";
        var savedReveal = PowerCatalog.FindField(_currentEntry!, "reveallos")?.Value.Trim() ?? "Revealer";
        if (!revealText.Equals(savedReveal, StringComparison.OrdinalIgnoreCase) &&
            !revealText.Equals("Revealer", StringComparison.OrdinalIgnoreCase) &&
            !_protoUnitNames.Contains(revealText, StringComparer.OrdinalIgnoreCase))
            return "Revealer Unit must be a valid Protounit.";
        foreach (var box in new[] { _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox })
        {
            if (!box.IsVisible || string.IsNullOrWhiteSpace(box.Text)) continue;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value) || value < 0)
                return $"Range indicator {box.Tag} must be a non-negative number.";
        }
        foreach (var box in new[] { _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                     _rangeIndicatorProtoSpeedBox })
        {
            if (string.IsNullOrWhiteSpace(box.Text)) continue;
            var valid = (string)box.Tag! == "indicatorcount"
                ? int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
                  (ReferenceEquals(box, _numberToTransformBox) || count >= 0)
                : double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                  double.IsFinite(number) && number >= 0;
            if (!valid) return $"Range Indicator Proto {box.Tag} must be a non-negative number.";
        }
        foreach (var (type, radiusBox, restriction) in _placementRestrictionFields)
        {
            if (restriction.Parent != _currentEntry) continue;
            if (string.IsNullOrWhiteSpace(restriction.Value))
                return "Placement restriction requires a Protounit or Unit Type.";
            if (!double.TryParse(radiusBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value) || value < 0)
                return "Placement restriction radius must be a non-negative number.";
        }
        foreach (var (unit, factor, damage) in _droughtDamageFields)
        {
            if (damage.Parent != _currentEntry) continue;
            if (string.IsNullOrWhiteSpace(unit.Text))
                return "Initial Damage requires a Unit or Unit Type.";
            if (!double.TryParse(factor.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value))
                return "Initial Damage Factor must be a valid number.";
        }
        foreach (var (unit, cap, damageCap) in _greatFloodDamageCapFields)
        {
            if (damageCap.Parent != _currentEntry) continue;
            if (string.IsNullOrWhiteSpace(unit.Text))
                return "Damage Cap requires a Unit or Unit Type.";
            if (!double.TryParse(cap.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value))
                return "Damage Cap must be a valid number.";
        }
        if (_currentEntry!.Attribute("type")?.Value.Equals("SwapUnit",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var swap in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase)))
            {
                if (!_unitOrTypeNames.Contains(swap.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                    return "Swap Target must be a valid Protounit or Unit Type.";
                if (!_protoUnitNames.Contains(swap.Attribute("swapto")?.Value.Trim() ?? "",
                        StringComparer.OrdinalIgnoreCase))
                    return "Swap To must be a valid Protounit.";
            }
            var minMax = PowerCatalog.FindField(_currentEntry, "convertusingminmax");
            if (minMax != null &&
                (!int.TryParse(minMax.Attribute("minswapamount")?.Value, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out _) ||
                 !int.TryParse(minMax.Attribute("maxswapamount")?.Value, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out _)))
                return "Swap Min/Max amounts must be whole numbers.";
            var delay = PowerCatalog.FindField(_currentEntry, "swapdelay")?.Value;
            if (delay != null && (!double.TryParse(delay, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var delayValue) || !double.IsFinite(delayValue) || delayValue < 0))
                return "Swap Delay must be a non-negative number.";
            var chance = PowerCatalog.FindField(_currentEntry, "overallchance")?.Value;
            if (chance != null && (!double.TryParse(chance, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var chanceValue) || !double.IsFinite(chanceValue) || chanceValue is < 0 or > 1))
                return "Swap Chance must be between 0 and 1.";
        }
        if (_currentEntry.Attribute("type")?.Value.Equals("Lure", StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var attractType in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("attracttype", StringComparison.OrdinalIgnoreCase)))
                if (!_unitOrTypeNames.Contains(attractType.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                    return "Lure Attract Type must be a valid Protounit or Unit Type.";
            var usedAges = new HashSet<int>();
            foreach (var ageInfo in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("ageinfo", StringComparison.OrdinalIgnoreCase)))
            {
                if (!int.TryParse(ageInfo.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var age) || age is < 1 or > 5 || !usedAges.Add(age))
                    return "Lure Age info must use each standard Age at most once.";
                if (!int.TryParse(ageInfo.Attribute("maxfood")?.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var maxFood) || maxFood < 0 ||
                    !int.TryParse(ageInfo.Attribute("numspawns")?.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var spawns) || spawns < 0)
                    return "Lure Max Food and Spawn Amount must be non-negative whole numbers.";
            }
        }
        if (_currentEntry.Attribute("type")?.Value.Equals("FoxCharm",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            var adjustments = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("ageadjustment", StringComparison.OrdinalIgnoreCase)).ToList();
            if (adjustments.Count > StandardAges.Length)
                return "Affected Number supports at most one row per Age.";
            var usedAges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var adjustment in adjustments)
            {
                var age = adjustment.Value.Trim();
                if (!StandardAges.Contains(age, StringComparer.OrdinalIgnoreCase) || !usedAges.Add(age))
                    return "Affected Number must use each standard Age at most once.";
                if (!int.TryParse(FindAttribute(adjustment, "affectedNumber")?.Value,
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return "Affected Number must be a whole number.";
            }
        }
        if (_currentEntry.Attribute("type")?.Value.Equals("Undermine", StringComparison.OrdinalIgnoreCase) == true)
            foreach (var target in _currentEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("attacktarget", StringComparison.OrdinalIgnoreCase)))
                if (!_unitOrTypeNames.Contains(target.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                    return "Undermine Attack target must be a valid Protounit or Unit Type.";
        foreach (var box in new[] { _crackIntervalMinBox, _crackIntervalMaxBox, _timeFactorBox }
                     .Concat(ShockwaveNumberBoxes()).Concat(GaiaNumberBoxes())
                     .Concat(ValorTraitorChaosNumberBoxes()).Concat(SpiderNumberBoxes())
                     .Concat(VortexNumberBoxes()).Concat(TartarianNumberBoxes())
                     .Concat(ImplodeNumberBoxes()).Concat(new[]
                     { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                       _rootDurationBox, _slowHealMultiplierBox, _vibrantGenerateIntervalBox,
                       _vibrantRotateMinBox, _vibrantRotateMaxBox, _vibrantGrowthMinBox,
                     _vibrantGrowthMaxBox, _vibrantScaleMinBox, _vibrantScaleMaxBox,
                       _restorationHealSpeedBox, _restorationScanIntervalBox,
                       _droughtBurnTimeBox, _greatFloodWaveCountBox, _greatFloodWaveRadiusBox,
                       _greatFloodSpacingBox, _greatFloodInitialVelocityBox, _greatFloodFinalVelocityBox,
                       _greatFloodRampTimeBox, _greatFloodAttackIntervalBox,
                       _shiftingSandsFadeTimeBox, _shiftingSandsSpawnTimeBox,
                       _shiftingSandsScanIntervalBox, _shiftingSandsPlacementRadiusBox,
                       _locustSwarmCountBox, _locustSwarmRadiusBox, _locustWayPointCountBox,
                       _locustWayPointOffsetBox, _locustDamageIntervalBox,
                       _boltStrikeHeightBox, _boltStrikeMinOffsetBox, _boltStrikeMaxOffsetBox,
                       _pestilenceRofPenaltyBox, _pestilenceTrainRatePenaltyBox,
                       _pestilenceScanIntervalBox, _greatHuntFoodRatioBox }).Concat(NewPowerNumberBoxes()))
        {
            if (!box.IsVisible || string.IsNullOrWhiteSpace(box.Text)) continue;
            var valid = ReferenceEquals(box, _thunderBurstAmountBox)
                ? int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                : ReferenceEquals(box, _treeAmountBox) || ReferenceEquals(box, _numberToCreateBox) ||
                        ReferenceEquals(box, _maxSpawnNumberBox)
                        || ReferenceEquals(box, _numberToTransformBox)
                        || ReferenceEquals(box, _wallSegmentCountBox)
                        || ReferenceEquals(box, _greatFloodWaveCountBox)
                        || ReferenceEquals(box, _locustSwarmCountBox)
                        || ReferenceEquals(box, _locustWayPointCountBox)
                        || ReferenceEquals(box, _curseMaxResourcesBox)
                        || ReferenceEquals(box, _gustThrowSizeBox)
                        || ReferenceEquals(box, _throwSizeLimitBox)
                        || ReferenceEquals(box, _witherCreepGeneratorsBox)
                        || ReferenceEquals(box, _underworldEidolonMinBox)
                        || ReferenceEquals(box, _underworldEidolonMaxBox)
                        || ReferenceEquals(box, _underworldEidolonStepBox)
                        || ReferenceEquals(box, _newMoonMaxFreeBox)
                ? int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0
                : double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) && double.IsFinite(value) &&
                  (!(ReferenceEquals(box, _timeFactorBox) ||
                     ReferenceEquals(box, _unitEffectTimeSecondsBox) ||
                     ReferenceEquals(box, _sharedEffectTimeSecondsBox) ||
                     ReferenceEquals(box, _spiderAttackRangeBox) ||
                     ReferenceEquals(box, _implodePullRadiusBox) ||
                     ReferenceEquals(box, _stayTimeBox)) || value >= 0);
            if (!valid)
                return ReferenceEquals(box, _timeFactorBox)
                    ? "Time Factor must be a non-negative number."
                    : ReferenceEquals(box, _unitEffectTimeSecondsBox) ||
                      ReferenceEquals(box, _sharedEffectTimeSecondsBox)
                        ? "Effect time must be a non-negative number."
                    : ReferenceEquals(box, _treeAmountBox)
                        ? "Tree amount must be a non-negative whole number."
                    : ReferenceEquals(box, _numberToCreateBox)
                        ? "Number to create must be a non-negative whole number."
                    : ReferenceEquals(box, _maxSpawnNumberBox)
                        ? "Max Spawn Amount must be a non-negative whole number."
                    : ReferenceEquals(box, _numberToTransformBox)
                        ? "Number to Transform must be a whole number."
                    : ReferenceEquals(box, _wallSegmentCountBox)
                        ? "Segment counts must be a non-negative whole number."
                    : ReferenceEquals(box, _greatFloodWaveCountBox)
                        ? "Wave Count must be a non-negative whole number."
                    : ReferenceEquals(box, _locustSwarmCountBox)
                        ? "Swarm Count must be a non-negative whole number."
                    : ReferenceEquals(box, _locustWayPointCountBox)
                        ? "Swarm Way Points Number must be a non-negative whole number."
                    : ReferenceEquals(box, _witherCreepGeneratorsBox)
                        ? "Creep Generators Amount must be a non-negative whole number."
                    : ReferenceEquals(box, _underworldEidolonMinBox) ||
                      ReferenceEquals(box, _underworldEidolonMaxBox) ||
                      ReferenceEquals(box, _underworldEidolonStepBox)
                        ? "Eidolon counts must be non-negative whole numbers."
                    : ReferenceEquals(box, _newMoonMaxFreeBox)
                        ? "Max Free Amount must be a non-negative whole number."
                    : ReferenceEquals(box, _spiderAttackRangeBox)
                        ? "Spider Attack Range must be a non-negative number."
                    : ReferenceEquals(box, _implodePullRadiusBox)
                        ? "Pull Radius must be a non-negative number."
                    : ReferenceEquals(box, _stayTimeBox)
                        ? "Stay Time must be a non-negative number."
                    : $"{box.Tag} must be a valid number.";
        }
        var shockwaveVfx = _shockwaveVfxBox.Text?.Trim() ?? "";
        if (_shockwaveVfxFields.IsVisible && shockwaveVfx.Length > 0 &&
            !_protoUnitNames.Contains(shockwaveVfx, StringComparer.OrdinalIgnoreCase))
            return "Shockwave VFX must be a valid Protounit.";
        foreach (var (box, label) in new[]
                 { (_treeUnitBox, "Tree Unit"), (_treeEffectVfxBox, "Tree Effect VFX"),
                   (_effectVfxBox, "Effect VFX"), (_vfxProtoBox, "VFX Proto") })
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return $"{label} must be a valid Protounit.";
        }
        foreach (var box in SpiderProtoBoxes())
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Spider properties must use valid Protounits.";
        }
        foreach (var box in VortexProtoBoxes())
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Vortex properties must use valid Protounits.";
        }
        foreach (var box in TartarianProtoBoxes())
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Tartarian Gate attributes must use valid Protounits.";
        }
        foreach (var box in ImplodeProtoBoxes())
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Implode VFX fields must use valid Protounits.";
        }
        foreach (var box in NewPowerProtoBoxes())
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Specific VFX and unit fields must use valid Protounits.";
        }
        var arcadianPlacement = _arcadianPlacementBox.Text?.Trim() ?? "";
        if (_arcadianPlacementBox.IsVisible && arcadianPlacement.Length > 0 &&
            !_unitOrTypeNames.Contains(arcadianPlacement, StringComparer.OrdinalIgnoreCase))
            return "Terrain Placement Check must be a valid Protounit or Unit Type.";
        var eidolonType = _underworldEidolonTypeBox.Text?.Trim() ?? "";
        if (_underworldEidolonTypeBox.IsVisible && eidolonType.Length > 0 &&
            !_unitOrTypeNames.Contains(eidolonType, StringComparer.OrdinalIgnoreCase))
            return "Eidolon Valid Type must be a valid Protounit or Unit Type.";
        foreach (var techBox in new[] { _underworldTechEffectBox, _underworldOnlyIfTechBox })
        {
            var tech = techBox.Text?.Trim() ?? "";
            if (techBox.IsVisible && tech.Length > 0 &&
                !_techNames.Contains(tech, StringComparer.OrdinalIgnoreCase))
                return "Underworld Invasion Tech Effect fields must use valid Technologies.";
        }
        foreach (var killType in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("killunittype", StringComparison.OrdinalIgnoreCase)))
            if (!_unitOrTypeNames.Contains(killType.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                return "Kill Unit Type must contain valid Protounits or Unit Types.";
        if (PowerCatalog.FindField(_currentEntry, "damagepercentofmaxhp") is { } witherDamage &&
            (!double.TryParse(witherDamage.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                 out var witherDamageValue) || !double.IsFinite(witherDamageValue) ||
             witherDamageValue is < 0 or > 1))
            return "% Max HP Damage must be between 0 and 1.";
        foreach (var (proto, grassRadiusBox, grassThetaBox, grass) in _arcadianGrassFields)
        {
            if (grass.Parent != _currentEntry) continue;
            if (!_protoUnitNames.Contains(proto.Text?.Trim() ?? "", StringComparer.OrdinalIgnoreCase))
                return "VFX Grass must use a valid Protounit.";
            if (!double.TryParse(grassRadiusBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusValue) ||
                !double.IsFinite(radiusValue) ||
                !double.TryParse(grassThetaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var thetaValue) ||
                !double.IsFinite(thetaValue))
                return "VFX Grass r and theta must be valid numbers.";
        }
        if (_currentEntry.Attribute("type")?.Value.Trim().Equals("PowerClone",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            var slot = PowerCatalog.FindField(_currentEntry, "powerslot")?.Value.Trim();
            if (slot != null && (!int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var slotValue) || slotValue is < 1 or > 5))
                return "Power Slot must be between 1 and 5.";
            var powerNames = _baseEntries.Keys.Concat(GetAllModPowerNames())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var replacement = PowerCatalog.FindField(_currentEntry, "duplicatetargetreplacement");
            if (replacement != null)
                foreach (var target in replacement.Elements().Where(element =>
                             element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)))
                    if (!powerNames.Contains(target.Value.Trim()))
                        return "Replacement Power must contain valid power names.";
        }
        foreach (var box in new[] { _gustDamageActionBox, _divineAttackActionBox })
        {
            var value = box.Text?.Trim() ?? "";
            if (box.IsVisible && value.Length > 0 &&
                !_protoActionNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "Damage and attack actions must be valid Protoactions.";
        }
        foreach (var (segment, reduction, tier) in _damageTierFields)
        {
            if (tier.Parent == null) continue;
            if (!int.TryParse(segment.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                !double.TryParse(reduction.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value))
                return "Damage Tiers require an integer Segment number and numeric wall-life reduction.";
        }
        foreach (var (type, damage, element) in _rootDamageFields)
        {
            if (element.Parent == null) continue;
            var typeValue = type.Text?.Trim() ?? "";
            if (!_unitOrTypeNames.Contains(typeValue, StringComparer.OrdinalIgnoreCase))
                return "Root damage Unit Type must be a valid Protounit or Unit Type.";
            if (!double.TryParse(damage.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) || !double.IsFinite(value) || value < 0)
                return "Root damage must be a non-negative number.";
        }
        var placement = PowerCatalog.FindField(_currentEntry!, "placement");
        if (!string.Equals(_placementBox.SelectedItem as string, "Skip", StringComparison.OrdinalIgnoreCase) &&
            FindPlacementFlag(placement, "restrictplacementnear") != null &&
            (FindPlacementFlag(placement, "restrictplacementdistance") != null ||
             !string.IsNullOrWhiteSpace(_restrictDistanceBox.Text)) &&
            (!double.TryParse(_restrictDistanceBox.Text, NumberStyles.Float,
                 CultureInfo.InvariantCulture, out var distance) || !double.IsFinite(distance) || distance < 0))
            return "Restrict placement distance must be a non-negative number.";
        return null;
    }

    private void NameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_currentEntry != null && CanEditProperties(_currentEntry) &&
            !string.Equals(_nameBox.Text, PowerCatalog.GetName(_currentEntry), StringComparison.Ordinal))
            MarkCurrentDirty();
    }

    private async void NameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        var entryThatLostFocus = _currentEntry;
        if (entryThatLostFocus == null)
            return;

        // A newly-created Power is intentionally untyped until the user chooses its kind. Clicking
        // the Type selector moves focus out of the name box; running full commit validation here
        // would open the missing-Type modal before that click can select anything, then restore
        // focus to the name box and repeat the loop. Keep the typed name pending until Type or Save
        // can commit both fields together.
        if (!_rawEditorDirty &&
            string.IsNullOrWhiteSpace(entryThatLostFocus.Attribute("type")?.Value))
            return;

        // LostFocus is async and can be queued behind New/Duplicate. Never let a delayed commit
        // validate whichever Power became current while it was waiting for the command gate.
        await CommitCurrentPowerAsync(entryThatLostFocus);
    }

    private void AddActiveRollover_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        var id = PowerCatalog.BuildStringId(CurrentPowerName!, "activepowerrolloverid");
        PowerCatalog.SetField(_currentEntry, "activepowerrolloverid", id);
        _session.SetPendingString(id, "", CurrentPowerName!, preserveExisting: true);
        _isPopulating = true;
        try { _activeRolloverBox.Text = ""; }
        finally { _isPopulating = false; }
        RefreshActiveRolloverVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
        _activeRolloverBox.Focus();
    }

    private void RemoveActiveRollover_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        var field = PowerCatalog.FindField(_currentEntry, "activepowerrolloverid");
        if (field == null)
            return;
        var id = field.Value.Trim();
        XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        if (id.Equals(PowerCatalog.BuildStringId(CurrentPowerName!, "activepowerrolloverid"),
                StringComparison.OrdinalIgnoreCase))
            ScheduleStringRemoval(id, CurrentPowerName!);
        _isPopulating = true;
        try { _activeRolloverBox.Text = ""; }
        finally { _isPopulating = false; }
        RefreshActiveRolloverVisibility();
        RefreshRawXmlFromCurrent();
        MarkCurrentDirty();
    }

    private void ScheduleStringRemoval(string id, string ownerName)
    {
        if (id.Length == 0)
            return;
        _session.RemovePendingStringUpdate(id);
        var savedOwner = _loadedModDocument == null
            ? null
            : PowerCatalog.Find(_loadedModDocument, ownerName);
        var wasSavedForOwner = savedOwner?.Descendants().Any(field => !field.HasElements &&
            field.Value.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)) == true;
        if (!wasSavedForOwner)
        {
            _session.CancelStringRemoval(id);
            return;
        }
        _session.ScheduleStringRemoval(id, ownerName);
    }

    private Window? GetDialogOwner()
        => TopLevel.GetTopLevel(this) as Window ?? _dialogOwner;

    private async Task ShowCommandErrorAsync(string title, string message)
    {
        _statusMessage.Text = message;
        var owner = GetDialogOwner();
        if (owner == null || !owner.IsVisible)
            return;
        try
        {
            await new Prompt(PromptType.Error, title, message).ShowDialog(owner);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The command failure is already contained. A closing or detached owner must not turn
            // error reporting itself into an unhandled async-void exception.
            _statusMessage.Text = message + $" (The error dialog could not be shown: {exception.Message})";
        }
    }

    private async Task RunNonReentrantCommandAsync(string action, Func<Task> command)
    {
        if (!await _commandGate.WaitAsync(0))
            return;
        try
        {
            await command();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await ShowCommandErrorAsync($"Unable to {action}", exception.Message);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<bool> CommitCurrentPowerAsync()
        => await CommitCurrentPowerWithGateAsync(expectedEntry: null, requireExpectedEntry: false);

    private Task<bool> CommitCurrentPowerAsync(XElement expectedEntry)
        => CommitCurrentPowerWithGateAsync(expectedEntry, requireExpectedEntry: true);

    private async Task<bool> CommitCurrentPowerWithGateAsync(
        XElement? expectedEntry,
        bool requireExpectedEntry)
    {
        await _commandGate.WaitAsync();
        try
        {
            if (requireExpectedEntry && !ReferenceEquals(expectedEntry, _currentEntry))
                return true;
            return await CommitCurrentPowerCoreAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await ShowCommandErrorAsync("Invalid power data", exception.Message);
            return false;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<bool> CommitCurrentPowerCoreAsync()
    {
        if (!IsModifiedMode || _currentEntry == null)
            return true;

        var numericError = _rawEditorDirty ? null : ValidateNumericProperties();
        if (numericError != null)
        {
            _statusMessage.Text = numericError;
            var validationOwner = GetDialogOwner();
            if (validationOwner != null)
                await new Prompt(PromptType.Error, "Invalid power number", numericError).ShowDialog(validationOwner);
            return false;
        }

        if (!_rawEditorDirty && string.IsNullOrWhiteSpace(
                _currentEntry.Attribute("type")?.Value))
        {
            const string message = "Select a Power Type before saving.";
            _powerTypeBox.Classes.Add("invalid-field");
            _powerTypeBox.IsDropDownOpen = false;
            _statusMessage.Text = message;
            var validationOwner = GetDialogOwner();
            if (validationOwner != null)
                await new Prompt(PromptType.Error, "Invalid power data", message).ShowDialog(validationOwner);
            _powerTypeBox.Focus();
            return false;
        }

        var typedName = _nameBox.Text?.Trim() ?? "";
        if (!_rawEditorDirty && typedName.Equals(PowerCatalog.GetName(_currentEntry), StringComparison.Ordinal))
            return true;

        try
        {
            var candidate = _rawEditorDirty
                ? XElement.Parse(_rawXmlEditor.Text ?? "", LoadOptions.PreserveWhitespace)
                : new XElement(_currentEntry);
            if (!_rawEditorDirty)
                PowerCatalog.SetName(candidate, typedName);
            if (!candidate.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Raw XML must have a <power> root element.");
            if (string.IsNullOrWhiteSpace(candidate.Attribute("type")?.Value))
                throw new InvalidDataException("Select a Power Type before saving.");
            _powerTypeBox.Classes.Remove("invalid-field");
            if (!PowerCatalog.IsModCatalogPower(candidate))
            {
                throw new InvalidDataException(
                    "This record is excluded from the Powers catalog. UnitAction entries and GeneralEffect entries without godpower=\"\" belong to the previous ability catalog.");
            }

            var newName = PowerCatalog.GetName(candidate);
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidDataException("Power name cannot be empty.");
            if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, _loadedEntryName))
                throw new InvalidDataException($"Power name can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
            var existingNames = _baseEntries.Keys.Concat(GetAllModPowerNames()).ToList();
            var duplicate = existingNames.FirstOrDefault(name =>
                !name.Equals(_loadedEntryName, StringComparison.OrdinalIgnoreCase) &&
                name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
                throw new InvalidDataException($"Power '{newName}' already exists.");
            var namespaceCollision = TechnologyStringIdPolicy.FindNamespaceCollision(
                newName, existingNames, _loadedEntryName);
            if (namespaceCollision != null)
                throw new InvalidDataException($"Power '{newName}' would share generated string IDs with '{namespaceCollision}'.");

            var oldName = _loadedEntryName ?? PowerCatalog.GetName(_currentEntry);
            if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var tag in StringBackedTags)
                {
                    var field = PowerCatalog.FindField(candidate, tag);
                    var oldId = field?.Value.Trim() ?? "";
                    if (field == null || !oldId.Equals(PowerCatalog.BuildStringId(oldName, tag),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    var newId = PowerCatalog.BuildStringId(newName, tag);
                    var value = await ResolveStringTextAsync(oldId);
                    XmlTriviaPreserver.SetText(field, newId);
                    _session.SetPendingString(newId, value, newName);
                    ScheduleStringRemoval(oldId, oldName);
                }
                if (IsSwitchingEffectsPower(candidate))
                    await RenameSwitchingPowerStringsAsync(candidate, oldName, newName);
                _session.ReassignPendingStringOwner(oldName, newName);
            }
            foreach (var tag in StringBackedTags)
            {
                var oldId = PowerCatalog.FindField(_currentEntry, tag)?.Value.Trim() ?? "";
                if (oldId.Length > 0 &&
                    oldId.Equals(PowerCatalog.BuildStringId(oldName, tag), StringComparison.OrdinalIgnoreCase) &&
                    !candidate.Descendants().Any(element => !element.HasElements &&
                        element.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase)))
                    ScheduleStringRemoval(oldId, oldName);
            }
            if (IsSwitchingEffectsPower(_currentEntry))
            {
                foreach (var oldId in _currentEntry.Descendants().Where(element => !element.HasElements)
                             .Select(element => element.Value.Trim())
                             .Where(id => IsOwnedSwitchingStringId(oldName, id))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var remains = candidate.Descendants().Any(element => !element.HasElements &&
                        element.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase));
                    if (!remains) ScheduleStringRemoval(oldId, oldName);
                }
            }
            XmlTriviaPreserver.Preserve(_currentEntry, candidate);
            _currentEntry.ReplaceWith(candidate);
            _session.AdoptEntry(newName, candidate);
            _applyRawXmlButton.IsVisible = false;
            if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                _session.AddOrCollapseRename(oldName, newName);
            _session.RenameDirty(oldName, newName);
            SetRawXml(PowerCatalog.FormatPreview(candidate));
            SetDirty(true);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            var generation = ++_populateGeneration;
            _populationTask = PopulatePropertiesSafelyAsync(candidate, generation);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_rawEditorDirty && _currentEntry != null)
            {
                var wasPopulating = _isPopulating;
                _isPopulating = true;
                try { _nameBox.Text = PowerCatalog.GetName(_currentEntry); }
                finally { _isPopulating = wasPopulating; }
            }
            _statusMessage.Text = exception.Message;
            var owner = GetDialogOwner();
            if (owner != null)
                await new Prompt(PromptType.Error, "Invalid power data", exception.Message).ShowDialog(owner);
            return false;
        }
    }

    private async Task<string> ResolveStringTextAsync(string id)
    {
        if (_session.TryGetPendingString(id, out var pending))
            return pending;
        return _resolveStringAsync == null ? id : await _resolveStringAsync(id) ?? id;
    }

    public Task AddPowerAsync(bool duplicateSelected = false)
        => RunNonReentrantCommandAsync(duplicateSelected ? "duplicate Power" : "add Power",
            () => AddPowerCoreAsync(duplicateSelected));

    private async Task AddPowerCoreAsync(bool duplicateSelected)
    {
        if (_modDocument?.Root == null)
        {
            if (GetDialogOwner() is { } owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before adding a power.").ShowDialog(owner);
            return;
        }
        if (!await CommitCurrentPowerCoreAsync())
            return;

        var source = duplicateSelected ? _currentEntry : null;
        if (source != null && !PowerCatalog.IsModCatalogPower(source))
        {
            if (GetDialogOwner() is { } owner)
            {
                await new Prompt(
                    PromptType.Error,
                    "Power cannot be copied",
                    "This original record is excluded from powers_mods.xml by the shared Power catalog rule.").ShowDialog(owner);
            }
            return;
        }

        var existingNames = new HashSet<string>(_baseEntries.Keys, StringComparer.OrdinalIgnoreCase);
        existingNames.UnionWith(GetAllModPowerNames());
        var baseName = duplicateSelected && !string.IsNullOrWhiteSpace(CurrentPowerName)
            ? CurrentPowerName + "Copy"
            : "NewPower";
        var name = baseName;
        for (var suffix = 2; existingNames.Contains(name) || existingNames.Any(existing =>
                 TechnologyStringIdPolicy.NormalizeToken(existing).Equals(
                     TechnologyStringIdPolicy.NormalizeToken(name), StringComparison.Ordinal)); suffix++)
            name = baseName + suffix;

        var entry = source == null
            ? new XElement(PowerCatalog.EntryName, new XAttribute("name", name))
            : new XElement(source);
        entry.Name = PowerCatalog.EntryName;
        PowerCatalog.SetName(entry, name);
        var generatedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in StringBackedTags)
        {
            var field = PowerCatalog.FindField(entry, tag);
            if (field == null && OptionalStringTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;
            var oldId = field?.Value.Trim() ?? "";
            var newId = PowerCatalog.BuildStringId(name, tag);
            var value = source == null
                ? tag.Equals("displaynameid", StringComparison.OrdinalIgnoreCase) ? name : ""
                : oldId.Length == 0 ? "" : await ResolveStringTextAsync(oldId);
            generatedStrings[newId] = value;
        }
        PowerCatalog.AddNewEntry(_modDocument, entry, PowerDocumentSection.Powers);
        if (source == null)
        {
            PowerCatalog.SetField(entry, "placement", "Full");
            PowerCatalog.SetField(entry, "activetime", "0");
            PowerCatalog.SetField(entry, "powerplayerrelation", "All");
        }
        foreach (var costTag in new[] { "cost", "repeatcost", "limitglobal", "limitplayer" })
        {
            var costField = PowerCatalog.FindField(entry, costTag);
            if (costField != null && decimal.TryParse(costField.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var cost) && cost == 0)
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(costField);
        }
        foreach (var tag in StringBackedTags)
        {
            var field = PowerCatalog.FindField(entry, tag);
            if (field == null && OptionalStringTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                continue;
            var newId = PowerCatalog.BuildStringId(name, tag);
            PowerCatalog.SetField(entry, tag, newId);
            _session.SetPendingString(newId, generatedStrings[newId], name);
        }
        IsModifiedMode = true;
        _session.AdoptEntry(name, entry);
        _session.MarkDirty(name);
        SetDirty(true);
        SelectPowerFromCommand(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteCurrentPowerAsync()
        => RunNonReentrantCommandAsync("delete Power", DeleteCurrentPowerCoreAsync);

    private async Task DeleteCurrentPowerCoreAsync()
    {
        if (!IsModifiedMode || _currentEntry == null || string.IsNullOrWhiteSpace(CurrentPowerName))
            return;
        var currentName = CurrentPowerName;
        var referenceName = _session.ReferenceNameForDeletion(currentName);
        var deletionOwner = GetDialogOwner();
        if (_ensureCanDeleteAsync != null && deletionOwner != null &&
            !await _ensureCanDeleteAsync(deletionOwner, referenceName))
            return;

        if (deletionOwner != null)
        {
            if (_confirmDeleteAsync != null)
            {
                if (!await _confirmDeleteAsync(deletionOwner, currentName))
                    return;
            }
            else
            {
                // A supplied but detached/closing host cannot own a modal confirmation. Never
                // interpret that UI state as consent to delete.
                if (!deletionOwner.IsVisible)
                {
                    _statusMessage.Text = "Power deletion was cancelled because its confirmation dialog is unavailable.";
                    return;
                }
                var prompt = new Prompt(PromptType.Confirm, "Delete power?",
                    $"Delete '{currentName}' from powers_mods.xml?");
                await prompt.ShowDialog(deletionOwner);
                if (!prompt.Confirmed)
                    return;
            }
        }

        foreach (var tag in StringBackedTags)
        {
            var id = PowerCatalog.FindField(_currentEntry, tag)?.Value.Trim() ?? "";
            if (id.Equals(PowerCatalog.BuildStringId(currentName, tag), StringComparison.OrdinalIgnoreCase))
                ScheduleStringRemoval(id, currentName);
        }
        if (IsSwitchingEffectsPower(_currentEntry))
            RemoveOwnedSwitchingStrings(_currentEntry);
        _currentEntry.Remove();
        _session.MarkDirty(currentName);
        _session.RemoveRenamesForDeletion(referenceName, currentName);
        _session.ClearSelection();
        SetDirty(true);
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public XElement? LoadSavedPowerElement(string name)
    {
        if (string.IsNullOrWhiteSpace(_modPath) || !File.Exists(_modPath))
            return null;
        try
        {
            var saved = PowerCatalog.Find(PowerCatalog.LoadOrCreateModDocument(_modPath), name);
            return saved == null ? null : new XElement(saved);
        }
        catch
        {
            return null;
        }
    }

    public bool IsPowerDirty(string name)
        => _session.IsPowerDirty(name);

    public void DiscardPowerChanges(string name, XElement? savedElement)
    {
        if (_modDocument?.Root == null)
            return;
        var current = FindAnyModPower(name);
        var restored = savedElement == null ? null : new XElement(savedElement);
        var restoredName = restored == null ? "" : PowerCatalog.GetName(restored);
        if (current != null && restored != null)
            current.ReplaceWith(restored);
        else if (current != null)
            current.Remove();
        else if (restored != null)
            _modDocument.Root.Add(restored);

        _session.DiscardPowerState(name, restoredName);
        SetDirty(_session.HasPendingWork);
        if (string.Equals(CurrentPowerName, name, StringComparison.OrdinalIgnoreCase))
            SelectPower(restoredName.Length == 0 ? null : restoredName);
    }

    public async Task<bool> SaveAsync()
    {
        await _commandGate.WaitAsync();
        try
        {
            await _saveGate.WaitAsync();
            try
            {
                return await SaveCoreAsync();
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await ShowCommandErrorAsync("Power save failed", "Save failed: " + exception.Message);
            return false;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public void AcknowledgePowerFileSavedByEditor()
    {
        if (_modDocument == null || _loadedModDocument == null ||
            string.IsNullOrWhiteSpace(_modPath) || !File.Exists(_modPath))
            return;
        var prepared = ModDocumentPersistence.Prepare(_modPath, ModXmlDocumentKind.Powers);
        if (prepared.LoadedState == _loadedFileState)
            return;
        var merge = NamedXmlExternalDocumentMergePolicy.Merge(
            _loadedModDocument,
            _modDocument,
            prepared.Document,
            PowerCatalog.EntryName,
            PowerCatalog.GetName);
        if (merge.ReplacedEditorChanges.Count > 0)
        {
            throw new IOException(
                "powers_mods.xml changed an in-memory Power that also has unsaved edits. " +
                "No Power changes were saved; review the file before trying again.");
        }

        var selectedName = CurrentPowerName;
        _modDocument = merge.Document;
        _loadedModDocument = new XDocument(prepared.Document);
        _loadedFileState = prepared.LoadedState;
        if (!string.IsNullOrWhiteSpace(selectedName) && PowerCatalog.Find(_modDocument, selectedName) != null)
            SelectPowerFromCommand(selectedName);
    }

    public void AcknowledgeStringFileSavedByEditor()
    {
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (!await CommitCurrentPowerCoreAsync())
            return false;
        if (!await ValidateEffectTypesForSaveAsync())
            return false;
        if (!IsDirty)
            return await ValidatePowerDocumentForSaveAsync();
        if (_modDocument == null || string.IsNullOrWhiteSpace(_modPath))
            return false;
        if (!await ReloadExternallyChangedDocumentAsync())
            return false;
        if (!await ValidateEffectTypesForSaveAsync())
            return false;
        if (!await ValidatePowerDocumentForSaveAsync())
            return false;

        try
        {
            var sessionSnapshot = _session.CreateCommitSnapshot();
            var savePreparation = PowerSavePreparation.Prepare(
                _modDocument,
                sessionSnapshot.DirtyNames,
                sessionSnapshot.Renames,
                sessionSnapshot.StringUpdates,
                sessionSnapshot.StringRemovals);
            var renamesToSave = savePreparation.Renames;
            var migration = renamesToSave.Count > 0
                ? _prepareReferenceMigration?.Invoke(renamesToSave)
                : null;
            var stringUpdates = savePreparation.StringUpdates;
            var stringRemovals = savePreparation.StringRemovals;
            var writesStrings = savePreparation.WritesStrings;
            if (writesStrings && (_saveStringsAsync == null || string.IsNullOrWhiteSpace(_modStringsPath)))
                throw new InvalidOperationException("Power strings cannot be saved because the active mod string file is unavailable.");
            var expected = migration?.ExpectedStates.ToDictionary(
                state => state.Key, state => state.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
            if (writesStrings && _loadedStringsFileState != null)
                expected[Path.GetFullPath(_modStringsPath!)] = _loadedStringsFileState;
            var prepared = new PreparedModXmlDocument(
                Path.GetFullPath(_modPath),
                ModXmlDocumentKind.Powers,
                _modDocument,
                _loadedFileState ?? FileIntegrityTransaction.CaptureState(_modPath));
            await ModDocumentPersistence.ExecuteAsync(
                [prepared],
                (migration?.Paths ?? []).Concat(writesStrings ? [_modStringsPath!] : []),
                expected,
                async () =>
                {
                    ProtoEditorWindow.SaveAbilityXmlDocument(_modDocument, _modPath);
                    migration?.Save((document, path) =>
                        ProtoEditorWindow.SaveAbilityXmlDocument(document, path));
                    if (writesStrings)
                        await _saveStringsAsync!(stringUpdates, stringRemovals);
                });

            if (migration != null)
                _adoptReferenceMigration?.Invoke(migration);
            _loadedFileState = FileIntegrityTransaction.CaptureState(_modPath);
            _loadedModDocument = new XDocument(_modDocument);
            if (writesStrings)
                _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath!);
            _session.AcceptCommit();
            SetDirty(false);
            _statusMessage.Text = "Saved powers_mods.xml.";
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await ShowCommandErrorAsync("Power save failed", "Save failed: " + exception.Message);
            return false;
        }
    }

    private async Task<bool> ValidateEffectTypesForSaveAsync()
    {
        if (_modDocument == null) return true;
        var invalid = _modDocument.Descendants().Where(element =>
                element.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase) &&
                PowerCatalog.IsModCatalogPower(element) &&
                PowerTypes.Contains(element.Attribute("type")?.Value.Trim() ?? "",
                    StringComparer.OrdinalIgnoreCase))
            .SelectMany(entry => entry.Descendants().Where(element =>
                element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
                (entry.Attribute("type")?.Value.Trim().Equals("EffectInArea",
                     StringComparison.OrdinalIgnoreCase) == true &&
                 element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))))
            .Where(element =>
                string.IsNullOrWhiteSpace(element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value))
            .ToList();
        if (invalid.Count == 0)
        {
            _invalidEffectTypes.Clear();
            return true;
        }

        _invalidEffectTypes.Clear();
        foreach (var effect in invalid) _invalidEffectTypes.Add(effect);
        if (_currentEntry != null && invalid.Any(effect => effect.AncestorsAndSelf().Contains(_currentEntry)))
            RefreshCurrentEffectRows();
        var containsOnHit = invalid.Any(element =>
            element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase));
        var itemLabel = containsOnHit ? "On Hit Effect" : "Effect";
        var message = invalid.Count == 1
            ? $"Select a Type for the highlighted {itemLabel} before saving."
            : $"Select a Type for each of the {invalid.Count} highlighted {itemLabel}s before saving.";
        _statusMessage.Text = message;
        var owner = TopLevel.GetTopLevel(this) as Window ?? _dialogOwner;
        if (owner != null)
            await new Prompt(PromptType.Error, "Invalid power data", message).ShowDialog(owner);
        return false;
    }

    private async Task<bool> ValidatePowerDocumentForSaveAsync()
    {
        if (_modDocument == null)
            return true;
        var validation = PowerSaveValidator.Validate(
            _modDocument,
            CreatePowerSaveCatalogs());
        if (validation.IsValid)
            return true;

        const int maximumDisplayedErrors = 8;
        var displayed = validation.Errors.Take(maximumDisplayedErrors).ToList();
        var remaining = validation.Errors.Count - displayed.Count;
        var message = "Correct the following Power data before saving:" + Environment.NewLine +
                      Environment.NewLine + string.Join(Environment.NewLine, displayed.Select(error => "• " + error));
        if (remaining > 0)
            message += Environment.NewLine + $"• …and {remaining} more error(s).";
        _statusMessage.Text = validation.Errors[0];
        var owner = TopLevel.GetTopLevel(this) as Window ?? _dialogOwner;
        if (owner != null)
            await new Prompt(PromptType.Error, "Invalid power data", message).ShowDialog(owner);
        return false;
    }

    private PowerSaveCatalogs CreatePowerSaveCatalogs()
    {
        var legacyPowers = _baseEntries.Values
            .Concat(_loadedModDocument?.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals(PowerCatalog.EntryName,
                    StringComparison.OrdinalIgnoreCase)) ?? [])
            .ToList();
        var powerNames = _baseEntries.Keys.Concat(GetAllModPowerNames())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new PowerSaveCatalogs(
            PowerTypes,
            _unitOrTypeNames,
            _protoUnitNames,
            _protoActionNames,
            _techNames,
            _animationNames,
            _boneNames,
            _tacticNames,
            powerNames,
            legacyPowers,
            _terrainCreepNames);
    }

    private async Task<bool> ReloadExternallyChangedDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_modPath) || _loadedFileState == null || _loadedModDocument == null)
            return true;
        var currentState = FileIntegrityTransaction.CaptureState(_modPath);
        if (currentState == _loadedFileState)
            return true;
        var owner = TopLevel.GetTopLevel(this) as Window ?? _dialogOwner;
        if (owner == null)
        {
            _statusMessage.Text = "powers_mods.xml changed outside the editor. Reload it before saving.";
            return false;
        }

        var prompt = new Prompt(
            PromptType.Confirm,
            "Powers changed outside the editor",
            "powers_mods.xml changed after the editor loaded. Reload and merge it with unsaved Power edits?\n\n" +
            "If the same Power changed both externally and in the editor, Reload uses the external version. Other unsaved edits are kept. Saving is paused for review.",
            confirmButtonText: "Reload");
        await prompt.ShowDialog(owner);
        if (!prompt.Confirmed)
            return false;

        try
        {
            var prepared = ModDocumentPersistence.Prepare(_modPath, ModXmlDocumentKind.Powers);
            var external = prepared.Document;
            var merge = NamedXmlExternalDocumentMergePolicy.Merge(
                _loadedModDocument,
                _modDocument!,
                external,
                PowerCatalog.EntryName,
                PowerCatalog.GetName);
            var renamedConflict = _session.FindConflictingRename(merge.ReplacedEditorChanges);
            if (!renamedConflict.Equals(default(KeyValuePair<string, string>)))
            {
                await new Prompt(
                    PromptType.Error,
                    "Unable to merge Power rename",
                    $"'{renamedConflict.Key}' was renamed in the editor while that Power also changed externally. No files were overwritten. Reopen the editor to use the external version.")
                    .ShowDialog(owner);
                return false;
            }
            if (merge.ReplacedEditorChanges.Count > 0)
                _session.AcceptExternalReplacements(merge.ReplacedEditorChanges);
            _modDocument = merge.Document;
            _loadedModDocument = new XDocument(external);
            _loadedFileState = prepared.LoadedState;
            SetDirty(_session.HasDirtyRecords);
            var selectedName = CurrentPowerName;
            if (!string.IsNullOrWhiteSpace(selectedName) && PowerCatalog.Find(_modDocument, selectedName) != null)
                SelectPowerFromCommand(selectedName);
            else
                ShowEmpty();
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            _statusMessage.Text = merge.ReplacedEditorChanges.Count > 0
                ? $"External Powers reloaded. {merge.ReplacedEditorChanges.Count} conflicting edit(s) were replaced; review, then save again."
                : "External Powers reloaded and merged. Review the changes, then save again.";
            return false;
        }
        catch (Exception exception)
        {
            await new Prompt(PromptType.Error, "Unable to reload Powers", "No files were written. " + exception.Message)
                .ShowDialog(owner);
            return false;
        }
    }

    private IEnumerable<string> GetAllModPowerNames()
        => _modDocument?.Root?.Elements().Where(element =>
                element.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(PowerCatalog.GetName)
            .Where(name => name.Length > 0) ?? [];

    private XElement? FindAnyModPower(string name)
        => _modDocument?.Root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(PowerCatalog.EntryName, StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    private async void ApplyRawXml_Click(object? sender, RoutedEventArgs e)
    {
        if (await CommitCurrentPowerAsync())
            _statusMessage.Text = "Raw XML applied.";
    }

    private void RawXmlEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isPopulating || !IsModifiedMode || _currentEntry == null)
            return;
        _rawEditorDirty = true;
        _applyRawXmlButton.IsVisible = true;
        var name = _loadedEntryName ?? CurrentPowerName;
        _session.MarkDirty(name);
        SetDirty(true);
    }

    private void XmlPreviewToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isXmlPreviewCollapsed = !_isXmlPreviewCollapsed;
        _xmlPreviewContent.IsVisible = !_isXmlPreviewCollapsed;
        _previewSplitter.IsVisible = !_isXmlPreviewCollapsed;
        if (_isXmlPreviewCollapsed)
        {
            _mainGrid.ColumnDefinitions[1].Width = new GridLength(0);
            _mainGrid.ColumnDefinitions[2].MinWidth = 28;
            _mainGrid.ColumnDefinitions[2].Width = new GridLength(28, GridUnitType.Pixel);
        }
        else
        {
            _mainGrid.ColumnDefinitions[0].Width = new GridLength(4, GridUnitType.Star);
            _mainGrid.ColumnDefinitions[1].Width = new GridLength(5, GridUnitType.Pixel);
            _mainGrid.ColumnDefinitions[2].MinWidth = 250;
            _mainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        }
        _xmlPreviewToggleButton.Content = _isXmlPreviewCollapsed ? "◀" : "▶";
        ToolTip.SetTip(_xmlPreviewToggleButton,
            _isXmlPreviewCollapsed ? "Restore XML Preview" : "Collapse XML Preview");
    }

    private void SetRawXml(string text)
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _rawXmlEditor.Text = text;
        }
        finally
        {
            _isPopulating = wasPopulating;
        }
    }

    private void ShowEmpty()
    {
        _populateGeneration++;
        _isPopulating = true;
        _openOtherAttributes.Clear();
        _otherAttributePickerOpen = false;
        _otherAttributesPickerRow.IsVisible = false;
        _otherAttributePicker.SelectedItem = null;
        _otherAttributePicker.Text = "";
        _otherAttributesRows.IsVisible = false;
        foreach (var remove in _removeOtherAttributeButtons.Values) remove.IsVisible = false;
        _openOtherSpecificAttributes.Clear();
        _otherSpecificAttributePickerOpen = false;
        _otherSpecificPickerRow.IsVisible = false;
        _otherSpecificHeader.IsVisible = false;
        _otherSpecificAttributePicker.SelectedItem = null;
        _otherSpecificAttributePicker.Text = "";
        _powerTypeBox.SelectedItem = null;
        _cracksSoundBox.Text = "";
        _crackIntervalMinBox.Text = "";
        _crackIntervalMaxBox.Text = "";
        _timeFactorBox.Text = "";
        _shockwaveVfxBox.SelectedItem = null;
        _shockwaveVfxBox.Text = "";
        foreach (var box in ShockwaveNumberBoxes()) box.Text = "";
        _treeUnitBox.SelectedItem = null;
        _treeUnitBox.Text = "";
        _treeEffectVfxBox.SelectedItem = null;
        _treeEffectVfxBox.Text = "";
        foreach (var box in GaiaNumberBoxes()) box.Text = "";
        _effectVfxBox.SelectedItem = null;
        _effectVfxBox.Text = "";
        _vfxProtoBox.SelectedItem = null;
        _vfxProtoBox.Text = "";
        foreach (var box in ValorTraitorChaosNumberBoxes()) box.Text = "";
        foreach (var box in SpiderProtoBoxes())
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in SpiderNumberBoxes()) box.Text = "";
        _humanDeathSoundBox.Text = "";
        foreach (var box in VortexProtoBoxes())
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in VortexNumberBoxes()) box.Text = "";
        _liftSoundBox.Text = "";
        _landSoundBox.Text = "";
        foreach (var box in TartarianProtoBoxes())
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in TartarianNumberBoxes()) box.Text = "";
        foreach (var box in ImplodeProtoBoxes())
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in ImplodeSoundBoxes()) box.Text = "";
        foreach (var box in ImplodeNumberBoxes()) box.Text = "";
        foreach (var box in NewPowerProtoBoxes())
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in NewPowerNumberBoxes()) box.Text = "";
        foreach (var box in new[] { _underworldEidolonTypeBox, _underworldLightSetBox, _underworldTechEffectBox,
                     _underworldOnlyIfTechBox })
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in new[] { _gustDamageActionBox, _divineAttackActionBox })
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox, _vibrantGenerateIntervalBox,
                     _vibrantRotateMinBox, _vibrantRotateMaxBox, _vibrantGrowthMinBox,
                     _vibrantGrowthMaxBox, _vibrantScaleMinBox, _vibrantScaleMaxBox,
                     _restorationHealSpeedBox, _restorationScanIntervalBox,
                     _droughtBurnTimeBox, _greatFloodWaveCountBox, _greatFloodWaveRadiusBox,
                     _greatFloodSpacingBox, _greatFloodInitialVelocityBox, _greatFloodFinalVelocityBox,
                     _greatFloodRampTimeBox, _greatFloodAttackIntervalBox,
                     _shiftingSandsFadeTimeBox, _shiftingSandsSpawnTimeBox,
                     _shiftingSandsScanIntervalBox, _shiftingSandsPlacementRadiusBox,
                     _locustSwarmCountBox, _locustSwarmRadiusBox, _locustWayPointCountBox,
                     _locustWayPointOffsetBox, _locustDamageIntervalBox,
                     _boltStrikeHeightBox, _boltStrikeMinOffsetBox, _boltStrikeMaxOffsetBox,
                     _pestilenceRofPenaltyBox, _pestilenceTrainRatePenaltyBox,
                     _pestilenceScanIntervalBox, _greatHuntFoodRatioBox }) box.Text = "";
        _greatFloodAttackNameBox.Text = "";
        _stealthLevelBox.SelectedIndex = -1;
        _damageTierRows.Children.Clear(); _damageTierFields.Clear();
        _rootDamageRows.Children.Clear(); _rootDamageFields.Clear();
        _tempUnitCreateRows.Children.Clear(); _tempUnitCreateFields.Clear();
        _freyrsGiftCreateRows.Children.Clear();
        _tempUnitSelectionEntries.Clear();
        _tempUnitAttachHost.Children.Clear();
        _tempUnitAgeRows.Children.Clear(); _tempUnitAgeFields.Clear();
        _bronzeArmorRows.Children.Clear();
        _greatHuntMaxFoodRows.Children.Clear(); _greatHuntMaxFoodFields.Clear();
        _swapEntryRows.Children.Clear(); _swapOptionalFields.Children.Clear();
        _swapFlagChips.Children.Clear(); _swapFlagPicker.SelectedItem = null; _swapFlagPicker.Text = "";
        _lureAttractTypeChips.Children.Clear(); _lureAttractTypePicker.SelectedItem = null;
        _lureAttractTypePicker.Text = ""; _lureAgeInfoRows.Children.Clear(); _lureAgeInfoFields.Clear();
        _undermineTargetRows.Children.Clear(); _undermineTargetFields.Clear();
        _tempestLargeTimingRows.Children.Clear(); _tempestAvoidChips.Children.Clear();
        _tempestAvoidPicker.SelectedItem = null; _tempestAvoidPicker.Text = "";
        _tempestStrikeSoundBox.Text = ""; _tempestLargeSoundBox.Text = "";
        _tempestAreaAttackActionBox.SelectedItem = null;
        _tempestAreaAttackActionBox.Text = ""; _tempestRadialTargetingCheck.IsChecked = false;
        _tempestVfxOpen = false;
        _modifyVfxOpen = false;
        _unitModifyKillOnEffectEndCheck.IsChecked = false;
        _switchingVfxAttachOpen = false;
        _switchingStringFields.Clear();
        _switchingEffectContainers.Children.Clear();
        _switchingVfxAttachChips.Children.Clear();
        _switchingVfxAttachPicker.SelectedItem = null;
        _switchingVfxAttachPicker.Text = "";
        foreach (var box in new[] { _modifyVfxTargetBox, _modifyVfxBoneBox, _modifyVfxBirthBox })
        {
            box.SelectedItem = null;
            box.Text = "";
        }
        foreach (var box in MeteorSoundBoxes()) box.Text = "";
        _meteorLargestGroupCheck.IsChecked = false;
        _throwExcludePullChips.Children.Clear(); _throwExcludeThrowChips.Children.Clear();
        _throwExcludePullPicker.SelectedItem = null; _throwExcludePullPicker.Text = "";
        _throwExcludeThrowPicker.SelectedItem = null; _throwExcludeThrowPicker.Text = "";
        _throwExcludePullOpen = false; _throwExcludeThrowOpen = false;
        _vibrantTreeUnitsHost.Children.Clear(); _vibrantTreeUnitFields.Clear();
        _droughtTerrainCreepHost.Children.Clear();
        _droughtInitialDamageRows.Children.Clear(); _droughtDamageFields.Clear();
        _greatFloodDamageCapRows.Children.Clear(); _greatFloodDamageCapFields.Clear();
        _tempUnitSmoothRotationCheck.IsChecked = false;
        _tempUnitForcePlacementCheck.IsChecked = false;
        _tempUnitForceOrientationCheck.IsChecked = false;
        _stampedeUnitBox.SelectedItem = null; _stampedeUnitBox.Text = "";
        _stampedeAttackActionBox.SelectedItem = null; _stampedeAttackActionBox.Text = "";
        _stampedeScaleBox.Text = ""; _stampedeAnimRateBox.Text = "";
        _stampedeAttackIntervalBox.Text = ""; _stampedeFullLengthCheck.IsChecked = false;
        foreach (var row in new Control[]
                 { _cracksSoundRow, _timeFactorRow, _shockwaveTopRow, _throwPropertiesRow,
                   _gaiaTreeTopRow, _gaiaSpawnRow, _growthRadiusRow, _effectVfxRow,
                   _damagePercentConversionRow, _vfxProtoRow, _effectTimeRow,
                   _spiderPropertiesRow, _spiderCountRow, _hatchPropertiesRow,
                   _spiderTimingRow, _humanDeathSoundRow, _vortexPropertiesRow,
                   _vortexDropRow, _maxUnitHeightRow, _vortexSoundsRow,
                   _tartarianSpawnRow, _tartarianRevealerRow, _tartarianGateRow, _rumbleTimeRow,
                   _tartarianSpawnTimingRow, _implodeProtosRow, _implodeSoundsRow,
                   _implodePullRow, _implodeExplosionRow, _implodeSpherePropertiesRow,
                   _implodeDamageIntervalRow, _implodeShearRow, _implodeDelayRow,
                   _implodeTimingRow, _implodeOrbitRadiusRow, _implodeAngularRow,
                   _implodeCameraShakeRow, _pSeedsFarmRow, _pSeedsVfxRow, _stealthLevelRow,
                   _earthWallRow, _damageTiersRow, _stealthVfxRow, _forestVfxRow,
                   _forestTimingRow, _rootDamageRow, _slowHealMultiplierRow,
                   _tempUnitCreateRow, _tempUnitAttachRow, _tempUnitAgeRow,
                   _tempUnitPlacementRow, _freyrsGiftCreateRow, _stampedeRow,
                   _stampedeFullLengthRow, _vibrantGenerationRow, _vibrantRotateRow,
                   _vibrantGrowthRow, _vibrantScaleRow, _restorationVfxRow,
                   _droughtTerrainCreepRow, _droughtVfxRow, _droughtInitialDamageRow,
                   _droughtBurnTimeRow, _greatFloodWaveRow, _greatFloodVelocityRow,
                   _greatFloodAttackRow, _greatFloodDamageCapRow,
                   _shiftingSandsPortalRow, _shiftingSandsTimingRow,
                   _shiftingSandsPlacementRadiusRow, _locustSwarmRow,
                   _locustWayPointsRow, _locustDamageIntervalRow, _boltUnitsRow,
                   _boltStrikePositionRow, _pestilencePenaltyRow, _pestilenceVfxRow,
                   _bronzeArmorRow, _teleportPortalRow, _greatHuntMaxFoodRow,
                   _greatHuntExtrasRow, _spyIndicatorRow, _swapEntriesRow, _swapMinMaxRow,
                   _swapOptionalRow, _swapFlagsRow, _curseResourcesRow, _curseSwapRow,
                   _gustMainRow, _gustTimingRow, _gustDamageRow, _gustThrowSizeRow,
                   _divineAttackRow, _divineDamageRow, _divineCameraDelayRow,
                   _lureUnitRow, _lureAttractTypeRow, _lureAttractionRow, _lureSpawnRow,
                   _lureAgeInfoRow, _undermineAttackRow, _undermineTargetRow, _undermineDeformRow,
                   _forestFireUnitsRow, _forestFireBurnRow, _forestFireSearchRow,
                   _tempestStrikeRow, _tempestLargeStrikeRow, _tempestStrikeIntervalRow,
                   _tempestStrikePositionRow, _tempestSoundRow, _tempestAttackRow, _tempestVfxRow,
                   _tempestCloudsRow, _tempestDriftRow, _tempestTreeBendRow,
                   _lightningVfxRow, _lightningRainRow,
                   _earthquakeAttackRow, _earthquakeRadiusRow, _earthquakeDamageRatioRow,
                   _earthquakeDustVfxRow, _thunderBurstMainRow, _thunderBurstVfxRow,
                   _typhoonDragonRow, _typhoonMainRow,
                   _frostFreezeRow, _frostVfxRow, _frostDriftRow,
                   _witherMainRow, _witherKillTypesRow, _witherDamageRow,
                   _witherCreepGeneratorsRow, _arcadianPlacementRow, _arcadianSpawnRow,
                   _arcadianGrassRow,
                   _underworldEidolonRow, _underworldSpawnRow, _underworldVfxRow, _underworldLightingRow,
                   _underworldTechEffectRow, _underworldSuddenDeathRow,
                   _powerCloneSlotRow, _powerCloneVfxRow,
                   _powerCloneReplacementRow,
                   _switchingTypeRow, _switchingProgressRow, _switchingVfxAttachRow,
                   _switchingEffectsRow,
                   _newMoonResearchRow, _modifyVfxRow, _unitModifyRow,
                   _meteorUnitRow, _meteorStrikesRow, _meteorSplashRow, _meteorSoundsRow,
                   _tornadoMainRow, _tornadoFunnelRow, _tornadoDebrisRow })
        {
            if (row.Parent is Panel parent) parent.Children.Remove(row);
            row.IsVisible = false;
        }
        _session.ClearSelection();
        _nameBox.Text = "";
        _displayNameBox.Text = "";
        _orderHintBox.Text = "";
        _rolloverBox.Text = "";
        _activeRolloverBox.Text = "";
        _exclusiveMessageBox.Text = "";
        _placementBox.SelectedItem = null;
        _placementFlagPicker.SelectedItem = null;
        _placementFlagPicker.Text = "";
        _placementFlagChips.Children.Clear();
        _targetTypePicker.SelectedItem = null;
        _targetTypePicker.Text = "";
        _targetTypeChips.Children.Clear();
        _excludeTypePicker.SelectedItem = null;
        _excludeTypePicker.Text = "";
        _excludeTypeChips.Children.Clear();
        _attackTargetTypePicker.SelectedItem = null;
        _attackTargetTypePicker.Text = "";
        _attackTargetTypeChips.Children.Clear();
        _attackExcludeTypePicker.SelectedItem = null;
        _attackExcludeTypePicker.Text = "";
        _attackExcludeTypeChips.Children.Clear();
        _attackTargetRow.IsVisible = false;
        _rangeIndicatorBox.SelectedItem = null;
        _indicatorRangeBox.Text = "";
        _indicatorWidthBox.Text = "";
        _indicatorSpeedBox.Text = "";
        _rangeDecalBox.Text = "";
        _isRangeDecalOpen = false;
        _rangeIndicatorRow.IsVisible = false;
        _rangeIndicatorProtoRow.IsVisible = false;
        _rangeIndicatorProtoBox.Text = "";
        _rangeIndicatorProtoRadiusBox.Text = "";
        _rangeIndicatorProtoCountBox.Text = "";
        _rangeIndicatorProtoSpeedBox.Text = "";
        _radiusRow.IsVisible = false;
        _powerPlayerRelationRow.IsVisible = false;
        _powerPlayerRelationBox.SelectedItem = null;
        _radiusBox.Text = "";
        _globalCheck.IsChecked = false;
        _notificationsOpen = _minimapNotificationsOpen = _activeVisibilityOpen =
            _activeListVisibilityOpen = false;
        _castMessageOpen = _castSelfMessageOpen = _alertPlayerOpen = false;
        _castMessageBox.Text = "";
        _castSelfMessageBox.Text = "";
        _alertPlayerBox.SelectedItem = null;
        _displayAlertsCheck.IsChecked = false;
        _minimapSendToBox.SelectedItem = null;
        _minimapDurationBox.Text = "";
        _activeListVisibilityBox.SelectedItem = null;
        _hideOnActiveListCheck.IsChecked = false;
        _cameraRangeOpen = false;
        foreach (var box in EffectAttributeBoxes()) box.Text = "";
        _cameraShakeRow.IsVisible = false;
        _terrainRumblingRow.IsVisible = false;
        _soundSetRows.Children.Clear();
        _soundNumericBoxes.Clear();
        _soundSetsSection.IsVisible = false;
        foreach (var box in new[]
                 { _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                   _accuracyBox, _allyDamageRatioBox }) box.Text = "";
        _revealUnitBox.Text = "";
        _lightingBox.Text = "";
        _resetAbilityChargeBox.Text = "";
        _playsBattleMusicCheck.IsChecked = false;
        foreach (var row in new Control[]
                 { _dualPowerMinDistanceRow, _powerBlockerRow, _revealLosRow, _lightingRow,
                   _accuracyRow, _allyDamageRatioRow, _resetAbilityChargeRow, _playsBattleMusicRow })
            row.IsVisible = false;
        _addNotificationSectionsRow.IsVisible = false;
        _notificationsSection.IsVisible = false;
        _minimapNotificationsSection.IsVisible = false;
        _activeVisibilitySection.IsVisible = false;
        _placementFlagsRow.IsVisible = false;
        _placementTypesRow.IsVisible = false;
        _placementOptionButtonsRow.IsVisible = false;
        _placementRestrictionRows.Children.Clear();
        _placementRestrictionFields.Clear();
        _placementDetailsHost.IsVisible = false;
        _losProtoUnitOpen = false;
        _restrictPlacementOpen = false;
        _overlapTypeOpen = false;
        _losProtoUnitBox.Text = "";
        _restrictPlacementBox.Text = "";
        _restrictDistanceBox.Text = "";
        _restrictAddObstructionCheck.IsChecked = false;
        _overlapTypeBox.Text = "";
        foreach (var box in new[]
                 { _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox, _costBox, _repeatCostBox,
                     _limitGlobalBox, _limitPlayerBox, _radiusBox })
            box.Text = "";
        _singleActivationCheck.IsChecked = false;
        _exclusiveCheck.IsChecked = false;
        _activeTimeOnTargetCheck.IsChecked = false;
        _isUseLimitOpen = false;
        _singleActivationCheck.IsVisible = false;
        _exclusiveCheck.IsVisible = false;
        _activeTimeOnTargetCheck.IsVisible = false;
        _orderHintRow.IsVisible = false;
        _buildUpRow.IsVisible = false;
        _shutDownRow.IsVisible = false;
        _useLimitRow.IsVisible = false;
        _limitLabel.IsVisible = false;
        _exclusiveMessageRow.IsVisible = false;
        _addBuildUpButton.IsVisible = false;
        _addShutDownButton.IsVisible = false;
        _addOrderHintButton.IsVisible = false;
        _addUseLimitButton.IsVisible = false;
        _iconEditor.Configure("", _iconPaths, null);
        _ = _iconPreview.ShowAsync(null);
        foreach (var control in new Control[]
                 { _nameBox, _displayNameBox, _orderHintBox, _rolloverBox, _activeRolloverBox, _iconEditor,
                     _placementBox, _placementFlagPicker, _targetTypePicker, _excludeTypePicker,
                     _powerPlayerRelationBox, _globalCheck,
                     _rangeIndicatorBox, _indicatorRangeBox, _indicatorWidthBox,
                     _indicatorSpeedBox, _rangeDecalBox, _rangeIndicatorProtoBox,
                     _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                     _rangeIndicatorProtoSpeedBox,
                     _powerTypeBox, _otherSpecificAttributePicker, _cracksSoundBox,
                     _crackIntervalMinBox, _crackIntervalMaxBox, _timeFactorBox,
                     _stunLengthBox, _shockwaveVfxBox,
                     _minThrowDistanceBox, _maxThrowDistanceBox,
                     _minThrowHeightBox, _maxThrowHeightBox,
                     _minThrowVelocityBox, _maxThrowVelocityBox,
                     _treeUnitBox, _treeAmountBox,
                     _treeSpawnIntervalMinBox, _treeSpawnIntervalMaxBox,
                     _treeEffectVfxBox, _growthRadiusStartBox, _growthRadiusIncreaseRateBox,
                     _effectVfxBox, _damagePercentConversionBox, _vfxProtoBox,
                     _unitEffectTimeSecondsBox, _sharedEffectTimeSecondsBox,
                     _spiderUnitBox, _spiderEggUnitBox, _spiderDoorBox, _spiderWarnBox,
                     _spiderAttackRangeBox, _spiderSpawnOffsetBox, _numberToCreateBox,
                     _separationDistanceBox, _hatchTimeBox, _hatchIntervalMinBox,
                     _hatchIntervalMaxBox, _warnTimeBox, _initialDelayBox,
                     _humanDeathSoundBox,
                     _unitEffectTimeBox, _vortexLandVfxBox, _vortexStartVfxBox,
                     _vortexStartLinkedBox, _vortexFinishLinkedBox,
                     _dropIntervalMinBox, _dropIntervalMaxBox, _maxRetryBox,
                     _maxUnitHeightBox, _liftSoundBox, _landSoundBox,
                     _spawnProtoBox, _maxSpawnNumberBox, _tartarianRevealerBox, _tartarianGateUnitBox,
                     _tartarianGateBirthBox, _tartarianGateFlameBox,
                     _sinkHoleTimeBox, _sinkHoleShakeBox, _rumbleTimeBox,
                     _firstSpawnDelayBox, _spawnIntervalMinBox, _spawnIntervalMaxBox,
                     _castMessageBox, _castSelfMessageBox, _alertPlayerBox,
                     _displayAlertsCheck, _minimapSendToBox, _minimapDurationBox,
                     _activeListVisibilityBox, _hideOnActiveListCheck,
                     _cameraDurationBox, _cameraStrengthBox, _cameraRangeBox,
                     _terrainHeightBox, _terrainWidthBox, _terrainSpeedBox, _terrainFadeTimeBox,
                     _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                     _revealUnitBox, _lightingBox, _accuracyBox, _allyDamageRatioBox,
                     _resetAbilityChargeBox, _playsBattleMusicCheck,
                     _attackTargetTypePicker, _attackExcludeTypePicker,
                     _losProtoUnitBox,
                     _restrictPlacementBox, _overlapTypeBox, _restrictDistanceBox,
                     _restrictAddObstructionCheck,
                     _activeTimeBox, _buildUpBox, _shutDownBox, _shutDownFadeBox, _costBox, _repeatCostBox,
                     _limitGlobalBox, _limitPlayerBox, _radiusBox,
                     _singleActivationCheck, _exclusiveCheck, _activeTimeOnTargetCheck,
                     _exclusiveMessageBox })
            control.IsEnabled = false;
        foreach (var control in ImplodeControls()) control.IsEnabled = false;
        foreach (var control in NewPowerControls()) control.IsEnabled = false;
        RefreshActiveRolloverVisibility();
        SetRawXml("");
        XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, true);
        _applyRawXmlButton.IsVisible = false;
        _rawEditorDirty = false;
        _statusMessage.Text = "Select a power from the list.";
        _isPopulating = false;
    }

    private void SetDirty(bool value)
    {
        if (IsDirty == value)
            return;
        _session.SetDirty(value);
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
