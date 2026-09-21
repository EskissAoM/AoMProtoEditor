using System.Xml.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    private static readonly IReadOnlyDictionary<string, string[]> SpecificAttributeLayouts =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["TartarianGate"] =
            [
                "Spawn Proto", "Max Spawn Amount", "Tartarian Gate", "Rumble Time",
                "First Spawn delay", "Spawn interval", "Throw properties"
            ],
            ["Implode"] =
            [
                "Implode Protos", "Implode Sounds", "Pull properties", "Explosion",
                "Sphere properties", "Damage interval", "Shear", "Implode delay",
                "Implode Timing", "Orbit Radius Squared", "Angular Acceleration",
                "Implode Camera Shake", "Throw properties"
            ],
            ["PSeeds"] = ["Farm Proto", "Number to Transform", "Farm VFX", "Stealth Level"],
            ["EarthWall"] = ["Earth Wall", "Damage Tiers"],
            ["Stealth"] = ["Vanish VFX", "Stealth Level"],
            ["FavorForest"] =
            [
                "Rooted enemy VFX", "Tree Area VFX", "Tree VFX", "Stay Time",
                "Root duration", "Root damage", "Slow Heal Multiplier"
            ]
        };
    private static readonly HashSet<string> NonAdditionalSpecificAttributes =
    [
        "Implode Protos", "Implode Sounds", "Pull properties", "Explosion", "Sphere properties",
        "Damage interval", "Shear", "Implode delay", "Implode Timing", "Orbit Radius Squared",
        "Angular Acceleration", "Implode Camera Shake"
        , "Farm Proto", "Farm VFX", "Earth Wall", "Damage Tiers", "Vanish VFX",
        "Rooted enemy VFX", "Tree Area VFX", "Root duration", "Root damage"
    ];
    private static readonly string[] PowerTypes =
    [
        "ArcadianMeadow", "Bolt", "Bronze", "CageUnit", "CeaseFire", "Chaos", "Curse",
        "Deconstruction", "DivineSlash", "DroughtLand", "DwarvenMine", "EarthMonster", "EarthWall",
        "Earthquake", "Eclipse", "EffectInArea", "FavorForest", "Fimbulwinter", "ForestFire",
        "FreyrsGift", "Frost", "GaiaForest", "GeneralEffect", "GreatFlood", "GreatHunt", "Implode",
        "LightningStorm", "Locust", "Lure", "Meteor", "NewMoon", "PSeeds", "Pestilence",
        "PowerClone", "Prosperity", "Rain", "Restoration", "SacredGate", "ShiftingSands", "Shockwave",
        "SingleModify", "SmitingGust", "Spiders", "Spy", "Stampede", "Stealth", "SwapUnit",
        "SwitchingEffects", "TartarianGate", "Teleport", "TempUnit", "Tempest", "ThunderBurst",
        "Tornado", "Traitor", "Typhoon", "Undermine", "UnderworldInvasion", "UnitModify", "Valor",
        "VibrantLand", "Vortex", "Wither"
    ];
    private static readonly string[] PlacementFlags =
    [
        "disallowWaterCast", "disallowsWaterCastRadius", "allowGaia", "noLosExtraLoc",
        "allowSnap", "placeFoundation", "ignoreProtoPlacement", "forceOnMap", "placeInArea",
        "notOverlapSelfOrAlly", "enemy", "ally", "allyOrEnemyNotSelf", "allowNature",
        "natureOnly", "useTargetUnitPositionForPlacement", "fullyBuiltOnly", "allowDeadTarget",
        "ExpandCheckPlacementHull", "VectorPlacement"
    ];
    private readonly Dictionary<string, XElement> _baseEntries;
    private readonly string? _modPath;
    private readonly Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
        _prepareReferenceMigration;
    private readonly Action<GameIdentifierReferenceMigrationPlan>? _adoptReferenceMigration;
    private readonly Func<Window, string, Task<bool>>? _ensureCanDeleteAsync;
    private readonly Window? _dialogOwner;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync;
    private readonly IReadOnlyList<string> _iconPaths;
    private readonly IReadOnlyList<string> _protoUnitNames;
    private readonly IReadOnlyList<string> _unitOrTypeNames;
    private readonly IconPreviewControl _iconPreview;
    private readonly string? _modStringsPath;
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingStringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringRemovalOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _pendingRenames = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly List<(TextBox Box, XElement Sound)> _soundNumericBoxes = [];
    private readonly List<(AutoCompleteBox Type, TextBox Radius, XElement Restriction)> _placementRestrictionFields = [];
    private readonly List<(TextBox Segment, TextBox Reduction, XElement Tier)> _damageTierFields = [];
    private readonly List<(AutoCompleteBox Type, TextBox Damage, XElement RootDamage)> _rootDamageFields = [];
    private readonly HashSet<string> _openOtherAttributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openOtherSpecificAttributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _removeOtherAttributeButtons = new(StringComparer.OrdinalIgnoreCase);

    private XDocument? _modDocument;
    private XDocument? _loadedModDocument;
    private FileIntegrityTransaction.FileState? _loadedFileState;
    private FileIntegrityTransaction.FileState? _loadedStringsFileState;
    private XElement? _currentEntry;
    private string? _loadedEntryName;
    private bool _isPopulating;
    private bool _rawEditorDirty;
    private bool _isUseLimitOpen;
    private bool _losProtoUnitOpen;
    private bool _restrictPlacementOpen;
    private bool _overlapTypeOpen;
    private bool _isRangeDecalOpen;
    private bool _notificationsOpen;
    private bool _minimapNotificationsOpen;
    private bool _activeVisibilityOpen;
    private bool _activeListVisibilityOpen;
    private bool _cameraRangeOpen;
    private bool _otherAttributePickerOpen;
    private bool _otherSpecificAttributePickerOpen;
    private bool _castMessageOpen;
    private bool _castSelfMessageOpen;
    private bool _alertPlayerOpen;
    private bool _isXmlPreviewCollapsed;
    private int _populateGeneration;

    public bool IsModifiedMode { get; private set; }
    public string? CurrentPowerName { get; private set; }
    public bool IsDirty { get; private set; }

    public event EventHandler? BrowserStateChanged;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? Saved;

    public PowerEditorView() : this([], null)
    {
    }

    public PowerEditorView(
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
        IEnumerable<string>? unitTypeNames = null)
    {
        InitializeComponent();
        XmlSyntaxEditorService.Configure(_rawXmlEditor);
        _baseEntries = baseDefinitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new XElement(group.First().SourceElement), StringComparer.OrdinalIgnoreCase);
        _modPath = modPath;
        _prepareReferenceMigration = prepareReferenceMigration;
        _adoptReferenceMigration = adoptReferenceMigration;
        _ensureCanDeleteAsync = ensureCanDeleteAsync;
        _dialogOwner = dialogOwner;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _iconPaths = (iconPaths ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(IconCatalog.ToResourceRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _protoUnitNames = (protoUnitNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        _unitOrTypeNames = _protoUnitNames.Concat(unitTypeNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        _iconPreview = new IconPreviewControl(iconPreviewService);
        _iconPreviewHost.Child = _iconPreview;
        _modStringsPath = modStringsPath;
        EditorTextFieldStyle.ConfigureTextBox(_nameBox);
        EditorTextFieldStyle.ConfigureTextBox(_displayNameBox);
        EditorTextFieldStyle.ConfigureTextBox(_rolloverBox);
        EditorTextFieldStyle.ConfigureTextBox(_activeRolloverBox);
        EditorTextFieldStyle.ConfigureTextBox(_exclusiveMessageBox);
        EditorTextFieldStyle.ConfigureTextBox(_castMessageBox);
        EditorTextFieldStyle.ConfigureTextBox(_castSelfMessageBox);
        foreach (var box in new[]
                 { _activeTimeBox, _buildUpBox, _shutDownBox, _costBox, _repeatCostBox,
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
                     _spawnIntervalMaxBox,
                     _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox,
                     _minimapDurationBox, _cameraDurationBox, _cameraStrengthBox, _cameraRangeBox,
                     _terrainHeightBox, _terrainWidthBox, _terrainSpeedBox, _terrainFadeTimeBox,
                     _dualPowerMinDistanceBox, _powerBlockerBox, _revealRadiusBox,
                     _accuracyBox, _allyDamageRatioBox })
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        foreach (var box in ImplodeNumberBoxes()) EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox })
            EditorNumericFieldStyle.ConfigureNumericTextBox(box);
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
                   _tartarianSpawnRow, _tartarianGateRow, _rumbleTimeRow,
                   _tartarianSpawnTimingRow, _implodeProtosRow, _implodeSoundsRow,
                   _implodePullRow, _implodeExplosionRow, _implodeSpherePropertiesRow,
                   _implodeDamageIntervalRow, _implodeShearRow, _implodeDelayRow,
                   _implodeTimingRow, _implodeOrbitRadiusRow, _implodeAngularRow,
                   _implodeCameraShakeRow, _pSeedsFarmRow, _pSeedsVfxRow, _stealthLevelRow,
                   _earthWallRow, _damageTiersRow, _stealthVfxRow, _forestVfxRow,
                   _forestTimingRow, _rootDamageRow, _slowHealMultiplierRow })
            if (row.Parent is Panel parent) parent.Children.Remove(row);
        _singleActivationCheck.IsCheckedChanged += (_, _) => PowerFlag_Changed(_singleActivationCheck);
        _exclusiveCheck.IsCheckedChanged += (_, _) => PowerFlag_Changed(_exclusiveCheck);
        _globalCheck.IsCheckedChanged += (_, _) => GlobalCheck_Changed();
        _displayAlertsCheck.IsCheckedChanged += (_, _) => NotificationFlag_Changed(
            _displayAlertsCheck, "displayalerts");
        _hideOnActiveListCheck.IsCheckedChanged += (_, _) => NotificationFlag_Changed(
            _hideOnActiveListCheck, "hideonactivegplist");
        _playsBattleMusicCheck.IsCheckedChanged += (_, _) => ExtraFlag_Changed();
        _accuracyBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) Accuracy_TextChanged();
        };
        _placementBox.ItemsSource = PlacementModes;
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
            var remove = new Button { Margin = new Thickness(8, 0, 0, 0) };
            remove.Classes.Add("remove-button");
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
        CurrentPowerName = null;
        _loadedEntryName = null;
        _currentEntry = null;
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPower(string? name)
    {
        CurrentPowerName = name;
        _loadedEntryName = name;
        _currentEntry = string.IsNullOrWhiteSpace(name)
            ? null
            : IsModifiedMode
                ? PowerCatalog.Find(_modDocument!, name)
                : _baseEntries.GetValueOrDefault(name);
        _rawEditorDirty = false;
        _applyRawXmlButton.IsVisible = false;

        if (_currentEntry == null)
        {
            ShowEmpty();
            return;
        }

        SetRawXml(PowerCatalog.FormatPreview(_currentEntry));
        XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, !IsModifiedMode);
        _ = PopulatePropertiesAsync(_currentEntry);
        _statusMessage.Text = IsModifiedMode
            ? "Edit Properties or the raw <power> XML, then use Save."
            : "Original .godpowers entry from Data.bar (read-only).";
    }

    private async Task PopulatePropertiesAsync(XElement entry)
    {
        var generation = ++_populateGeneration;
        _isPopulating = true;
        try
        {
            _nameBox.Text = PowerCatalog.GetName(entry);
            _openOtherAttributes.Clear();
            foreach (var (name, _, tags) in OtherAttributeSpecs())
                if (HasOtherAttribute(entry, tags)) _openOtherAttributes.Add(name);
            _otherAttributePickerOpen = false;
            foreach (var control in new Control[]
                     { _nameBox, _displayNameBox, _rolloverBox, _activeRolloverBox, _iconEditor,
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
                         _spawnProtoBox, _maxSpawnNumberBox, _tartarianGateUnitBox,
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
                         _activeTimeBox, _buildUpBox, _shutDownBox, _costBox, _repeatCostBox,
                         _limitGlobalBox, _limitPlayerBox, _singleActivationCheck, _exclusiveCheck,
                         _exclusiveMessageBox })
                control.IsEnabled = IsModifiedMode;
            foreach (var control in ImplodeControls()) control.IsEnabled = IsModifiedMode;
            foreach (var control in NewPowerControls()) control.IsEnabled = IsModifiedMode;

            foreach (var box in new[]
                     { _activeTimeBox, _buildUpBox, _shutDownBox, _costBox, _repeatCostBox,
                         _limitGlobalBox, _limitPlayerBox, _radiusBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
            _singleActivationCheck.IsChecked = PowerCatalog.FindField(entry, "singleactivation") != null;
            _exclusiveCheck.IsChecked = PowerCatalog.FindField(entry, "exclusive") != null;
            _globalCheck.IsChecked = PowerCatalog.FindField(entry, "global") != null;
            _powerPlayerRelationBox.SelectedItem = PowerPlayerRelations.FirstOrDefault(relation =>
                relation.Equals(PowerCatalog.FindField(entry, "powerplayerrelation")?.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase)) ?? "All";
            var rangeIndicator = PowerCatalog.FindField(entry, "rangeindicator");
            var rangeIndicatorProto = PowerCatalog.FindField(entry, "rangeindicatorprotoid");
            _rangeIndicatorProtoBox.Text = rangeIndicatorProto?.Value.Trim() ?? "";
            foreach (var box in new[] { _rangeIndicatorProtoRadiusBox, _rangeIndicatorProtoCountBox,
                         _rangeIndicatorProtoSpeedBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    FindPlacementFlag(rangeIndicatorProto, (string)box.Tag!)?.Value);
            _rangeIndicatorBox.SelectedItem = RangeIndicatorModes.FirstOrDefault(mode =>
                mode.Equals(rangeIndicator?.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            foreach (var box in new[] { _indicatorRangeBox, _indicatorWidthBox, _indicatorSpeedBox })
                box.Text = EditorNumericFieldStyle.FormatDisplay(
                    FindPlacementFlag(rangeIndicator, (string)box.Tag!)?.Value);
            _rangeDecalBox.Text = PowerCatalog.FindField(entry, "rangeindicatorcustomdecal")?.Value ?? "";
            _isRangeDecalOpen = PowerCatalog.FindField(entry, "rangeindicatorcustomdecal") != null;
            _castMessageOpen = PowerCatalog.FindField(entry, "notificationformatid") != null;
            _castSelfMessageOpen = PowerCatalog.FindField(entry, "selfnotificationformatid") != null;
            _alertPlayerOpen = PowerCatalog.FindField(entry, "messagealertplayerrelation") != null;
            _displayAlertsCheck.IsChecked = PowerCatalog.FindField(entry, "displayalerts") != null;
            _alertPlayerBox.SelectedItem = AlertPlayerRelations.FirstOrDefault(relation =>
                relation.Equals(PowerCatalog.FindField(entry, "messagealertplayerrelation")?.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase)) ?? "All";
            _notificationsOpen = _castMessageOpen || _castSelfMessageOpen || _alertPlayerOpen ||
                                 _displayAlertsCheck.IsChecked == true;
            var minimap = PowerCatalog.FindField(entry, "minimapeventtime");
            _minimapNotificationsOpen = minimap != null;
            _minimapSendToBox.SelectedItem = MinimapRecipients.FirstOrDefault(value =>
                value.Equals(FindPlacementFlag(minimap, "sendalertto")?.Value,
                    StringComparison.OrdinalIgnoreCase)) ?? "All";
            _minimapDurationBox.Text = EditorNumericFieldStyle.FormatDisplay(minimap?.Value);
            _activeListVisibilityBox.SelectedItem = ActiveListVisibilities.FirstOrDefault(value =>
                value.Equals(PowerCatalog.FindField(entry, "activelistvisibility")?.Value.Trim(),
                    StringComparison.OrdinalIgnoreCase));
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
            _placementBox.SelectedItem = PlacementModes.FirstOrDefault(mode => mode.Equals(
                PowerCatalog.FindField(entry, "placement")?.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            var placement = PowerCatalog.FindField(entry, "placement");
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
                if (_pendingStringUpdates.TryGetValue(id, out var pending))
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
                GetResolvedStringBox(resolution.Tag).Text = resolution.Text;
            }
        }
        finally
        {
            if (generation == _populateGeneration)
                _isPopulating = false;
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
             "minthrowvelocity", "maxthrowvelocity"]),
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
        ("Damage interval", _implodeDamageIntervalRow, ["damageintervalseconds"]),
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
        ("Slow Heal Multiplier", _slowHealMultiplierRow, ["slowhealmultiplier"])
    ];

    private static bool IsDefaultSpecificAttribute(string? powerType, string name)
        => (powerType?.Equals("Deconstruction", StringComparison.OrdinalIgnoreCase) == true &&
           name is "Cracks Sound" or "Time Factor") ||
           (powerType?.Equals("Shockwave", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Stun lenght" or "Shockwave VFX" or "Throw properties") ||
           (powerType?.Equals("GaiaForest", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Tree Unit" or "Tree amount" or "Tree Spawn Interval" or "Tree Effect VFX" or
                "Growth radius") ||
           (powerType?.Equals("Valor", StringComparison.OrdinalIgnoreCase) == true &&
            name == "Effect VFX") ||
           (powerType?.Equals("Traitor", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Damage Percent on Conversion" or "VFX Proto") ||
           (powerType?.Equals("Chaos", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Effect time" or "VFX Proto") ||
           (powerType?.Equals("Spiders", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Spider properties" or "Number to create" or "Separation Distance" or
                "Hatch properties" or "Warn time" or "Initial Delay" or "Human Death Sound") ||
           (powerType?.Equals("Vortex", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Initial Delay" or "Unit Effect Time" or "Vortex properties" or
                "Drop Interval" or "Max Retry" or "Max Unit Height" or "Lift Sound" or "Land Sound") ||
           (powerType?.Equals("TartarianGate", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Spawn Proto" or "Max Spawn Amount" or "Tartarian Gate" or "Rumble Time" or
                "First Spawn delay" or "Spawn interval" or "Throw properties") ||
           (powerType?.Equals("Implode", StringComparison.OrdinalIgnoreCase) == true &&
            SpecificAttributeLayouts["Implode"].Contains(name, StringComparer.OrdinalIgnoreCase)) ||
           (powerType?.Equals("PSeeds", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Farm Proto" or "Number to Transform" or "Farm VFX" or "Stealth Level") ||
           (powerType?.Equals("EarthWall", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Earth Wall" or "Damage Tiers") ||
           (powerType?.Equals("Stealth", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Vanish VFX" or "Stealth Level") ||
           (powerType?.Equals("FavorForest", StringComparison.OrdinalIgnoreCase) == true &&
            name is "Rooted enemy VFX" or "Tree Area VFX" or "Tree VFX" or "Stay Time" or
                "Root duration" or "Root damage" or "Slow Heal Multiplier");

    private void PopulateSpecificFields(XElement entry)
    {
        var powerType = entry.Attribute("type")?.Value.Trim() ?? "";
        var knownPowerType = PowerTypes.FirstOrDefault(value =>
            value.Equals(powerType, StringComparison.OrdinalIgnoreCase));
        _powerTypeBox.SelectedItem = knownPowerType;
        _powerTypeBox.Text = knownPowerType ?? powerType;
        _powerTypeBox.IsEnabled = IsModifiedMode;
        _openOtherSpecificAttributes.Clear();
        foreach (var (name, _, tags) in SpecificAttributeSpecs())
            if (HasOtherAttribute(entry, tags)) _openOtherSpecificAttributes.Add(name);
        _otherSpecificAttributePickerOpen = false;
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
        foreach (var box in ImplodeNumberBoxes())
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        foreach (var box in NewPowerProtoBoxes())
            PopulateStrictProtoBox(box, PowerCatalog.FindField(entry, NewPowerProtoTag(box))?.Value);
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox })
            box.Text = EditorNumericFieldStyle.FormatDisplay(
                PowerCatalog.FindField(entry, (string)box.Tag!)?.Value);
        var stealthLevel = PowerCatalog.FindField(entry, "stealthlevel")?.Value.Trim();
        _stealthLevelBox.SelectedIndex = stealthLevel == "1" ? 0 : stealthLevel == "2" ? 1 : -1;
        RefreshDamageTiers(entry);
        RefreshRootDamages(entry);
        RefreshSpecificAttributeRows();
    }

    private void PopulateStrictProtoBox(AutoCompleteBox box, string? value)
    {
        var text = value?.Trim() ?? "";
        box.SelectedItem = _protoUnitNames.FirstOrDefault(name =>
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
        yield return _tartarianGateUnitBox;
        yield return _tartarianGateBirthBox;
        yield return _tartarianGateFlameBox;
    }

    private string TartarianProtoTag(AutoCompleteBox box)
        => ReferenceEquals(box, _spawnProtoBox) ? "spawnproto" :
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
        yield return _forestTreeVfxBox;
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
           ReferenceEquals(box, _treeAreaVfxBox) ? "treeareavfx" : "treevfx";

    private IEnumerable<Control> NewPowerControls()
        => NewPowerProtoBoxes().Cast<Control>().Concat(new Control[]
        {
            _numberToTransformBox, _stealthLevelBox, _wallSegmentCountBox, _addDamageTierButton,
            _stayTimeBox, _rootDurationBox, _addRootDamageButton, _slowHealMultiplierBox
        });

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
        var remove = new Button(); remove.Classes.Add("remove-button");
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
        var type = new AutoCompleteBox { Width = 240, FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0 };
        EditorAutoCompleteService.ConfigureStrict(type, _unitOrTypeNames, damage.Value, () => _isPopulating,
            preserveUnknownInitialValue: false, allowEmpty: false, selectAllOnFirstClick: false,
            valueCommitted: value =>
            {
                if (!IsModifiedMode || value.Length == 0) return;
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
            damage.SetAttributeValue("damage", amount.Text!.Trim());
            MarkCurrentDirty(); RefreshRawXmlFromCurrent();
        };
        row.Children.Add(amount);
        var remove = new Button(); remove.Classes.Add("remove-button"); remove.IsVisible = IsModifiedMode;
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

    private void RefreshSpecificAttributeRows()
    {
        var powerType = _currentEntry?.Attribute("type")?.Value.Trim();
        var specs = SpecificAttributeSpecs();
        var states = specs.ToDictionary(spec => spec.Name, spec =>
        {
            var isDefault = IsDefaultSpecificAttribute(powerType, spec.Name);
            var present = _currentEntry != null && HasOtherAttribute(_currentEntry, spec.Tags);
            var visible = _currentEntry != null &&
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
            if (row.Parent is Panel parent) parent.Children.Remove(row);
            var visibleStates = rowGroup.Select(spec => states[spec.Name]).Where(state => state.Visible).ToList();
            row.IsVisible = visibleStates.Count > 0;
            if (row.IsVisible)
                (visibleStates.Any(state => state.IsDefault)
                    ? _specificAttributeRows : _otherSpecificAttributeRows).Children.Add(row);
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
        _removeSlowHealMultiplierButton.IsVisible = IsModifiedMode &&
            states["Slow Heal Multiplier"].Visible && !states["Slow Heal Multiplier"].IsDefault;

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
            .Where(name => !NonAdditionalSpecificAttributes.Contains(name) &&
                           !IsDefaultSpecificAttribute(powerType, name) &&
                           !_openOtherSpecificAttributes.Contains(name))
            .ToList();
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
    {
        if (powerType != null && SpecificAttributeLayouts.TryGetValue(powerType, out var layout))
        {
            var index = Array.FindIndex(layout,
                value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }
        return 1000 + fallback;
    }

    private void CommitPowerType(string powerType)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) ||
            !PowerTypes.Contains(powerType, StringComparer.OrdinalIgnoreCase)) return;
        var attribute = _currentEntry.Attribute("type");
        if (attribute?.Value.Equals(powerType, StringComparison.Ordinal) == true) return;
        _currentEntry.SetAttributeValue("type", powerType);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshSpecificAttributeRows();
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
        RefreshSpecificAttributeRows();
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

    private void CommitNewPowerProto(AutoCompleteBox box, string value)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry) || !box.IsVisible) return;
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
        var text = box.Text?.Trim() ?? "";
        if (text.Length > 0)
        {
            if (tag == "totalnumberoftrees")
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
                    return;
            }
            else if (tag is "numbertocreate" or "maxnumspawns" or "numbertotransform" or "wallsegmentcount")
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                    ((tag is "numbertocreate" or "maxnumspawns" or "wallsegmentcount") && count < 0))
                    return;
            }
            else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                         out var value) || !double.IsFinite(value) ||
                     ((tag is "timefactor" or "uniteffecttimeseconds" or "sharedeffecttimeseconds") &&
                      value < 0) || ((tag is "spiderattackrange" or "pullradius" or "staytime") && value < 0))
                return;
        }
        SetOrRemoveSpecificField(tag, text);
    }

    private void SetOrRemoveSpecificField(string tag, string value)
    {
        if (_currentEntry == null) return;
        var field = PowerCatalog.FindField(_currentEntry, tag);
        if (value.Length == 0)
        {
            if (field == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
        }
        else
        {
            if (field?.Value == value) return;
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
        if (CurrentPowerName is { } powerName) SelectPower(powerName);
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
            if (PowerCatalog.FindField(_currentEntry, tag)?.Value == text) return;
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
            if (PowerCatalog.FindField(_currentEntry, "accuracy")?.Value == text) return;
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
            if (FindPlacementFlag(reveal, "radius")?.Value == text) return;
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
            var button = new Button { Content = label, IsVisible = IsModifiedMode && existing == null,
                Margin = new Thickness(0, 0, 6, 4) };
            button.Classes.Add("add-component");
            row.Children.Add(button);
            var box = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox
            {
                Text = EditorNumericFieldStyle.FormatDisplay(existing?.Value),
                IsEnabled = IsModifiedMode
            });
            _soundNumericBoxes.Add((box, sound));
            var field = Field(label, box);
            field.IsVisible = existing != null;
            var remove = new Button { IsVisible = IsModifiedMode };
            remove.Classes.Add("remove-button");
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
                    double.IsFinite(number) && number >= 0)
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
        var delete = new Button { Margin = new Thickness(0, 0, 0, 4) };
        delete.Classes.Add("remove-button");
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
            if (attribute?.Value == value) return;
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
        var buildUp = PowerCatalog.FindField(_currentEntry, "builduptime") != null;
        var shutDown = PowerCatalog.FindField(_currentEntry, "shutdowntime") != null;
        var useLimit = _isUseLimitOpen || PowerCatalog.FindField(_currentEntry, "limitglobal") != null ||
                       PowerCatalog.FindField(_currentEntry, "limitplayer") != null;
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
            _pendingStringRemovals.Remove(id);
            _pendingStringRemovalOwners.Remove(id);
            _pendingStringUpdates.TryAdd(id, "");
            _pendingStringOwners[id] = CurrentPowerName!;
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
        if (minimap.Value == text) return;
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
            : FindPlacementFlag(placement, flag) != null).ToList();
        foreach (var flag in selected)
        {
            var captured = flag;
            _placementFlagChips.Children.Add(EditorChipService.CreateBlueChip(captured,
                IsModifiedMode ? () =>
                {
                    if (_currentEntry == null || !CanEditProperties(_currentEntry)) return;
                    if (GetStandalonePlacementFlagTag(captured) is { } tag)
                    {
                        var field = PowerCatalog.FindField(_currentEntry, tag);
                        if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
                    }
                    else
                        FindPlacementFlag(PowerCatalog.FindField(_currentEntry, "placement"), captured)?.Remove();
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

    private static string? GetStandalonePlacementFlagTag(string flag)
        => flag.Equals("ExpandCheckPlacementHull", StringComparison.OrdinalIgnoreCase)
            ? "expandcheckplacementhull"
            : flag.Equals("VectorPlacement", StringComparison.OrdinalIgnoreCase)
                ? "vectorplacement" : null;

    private void ClearPlacementSpecificXml()
    {
        if (_currentEntry == null) return;
        PowerCatalog.FindField(_currentEntry, "placement")?.RemoveAttributes();
        foreach (var field in _currentEntry.Elements().Where(element =>
                     element.Name.LocalName.Equals("expandcheckplacementhull", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("vectorplacement", StringComparison.OrdinalIgnoreCase) ||
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
            chips.Children.Add(EditorChipService.CreateBlueChip(captured, IsModifiedMode ? () =>
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
                var remove = new Button();
                remove.Classes.Add("remove-button");
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
            if (existing?.Value == text) return;
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
        if (!string.IsNullOrWhiteSpace(CurrentPowerName))
            _dirtyNames.Add(CurrentPowerName);
        SetDirty(true);
    }

    private void ResolvedStringBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry))
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
        _pendingStringRemovals.Remove(id);
        _pendingStringRemovalOwners.Remove(id);
        _pendingStringUpdates[id] = box.Text ?? "";
        _pendingStringOwners[id] = CurrentPowerName!;
        MarkCurrentDirty();
    }

    private void NumericBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: string tag } box || _currentEntry == null ||
            !CanEditProperties(_currentEntry))
            return;
        if (tag == "radius" && _globalCheck.IsChecked == true) return;
        var text = box.Text?.Trim() ?? "";
        if ((tag is "cost" or "repeatcost" or "radius") && text.Length == 0)
        {
            var existing = PowerCatalog.FindField(_currentEntry, tag);
            if (existing == null) return;
            XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(existing);
        }
        else if (tag is "cost" or "repeatcost" or "limitglobal" or "limitplayer")
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
                return;
            var existing = PowerCatalog.FindField(_currentEntry, tag);
            if (value == 0 && (tag is "cost" or "repeatcost" or "limitglobal" or "limitplayer"))
            {
                if (existing == null) return;
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(existing);
            }
            else
                PowerCatalog.SetField(_currentEntry, tag, value.ToString(CultureInfo.InvariantCulture));
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
        if (typed.Length == 0) attribute?.Remove();
        else if (attribute == null) SetPlacementAttribute(field, tag, typed);
        else if (attribute.Value == typed) return;
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
                _pendingStringRemovals.Remove(id);
                _pendingStringRemovalOwners.Remove(id);
                _pendingStringUpdates.TryAdd(id, "");
                _pendingStringOwners[id] = CurrentPowerName!;
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

    private void SetBoxTextWithoutEditing(TextBox box, string text)
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try { box.Text = text; }
        finally { _isPopulating = wasPopulating; }
    }

    private string? ValidateNumericProperties()
    {
        foreach (var box in new[] { _activeTimeBox, _buildUpBox, _shutDownBox })
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
            if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
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
        foreach (var box in new[] { _crackIntervalMinBox, _crackIntervalMaxBox, _timeFactorBox }
                     .Concat(ShockwaveNumberBoxes()).Concat(GaiaNumberBoxes())
                     .Concat(ValorTraitorChaosNumberBoxes()).Concat(SpiderNumberBoxes())
                     .Concat(VortexNumberBoxes()).Concat(TartarianNumberBoxes())
                     .Concat(ImplodeNumberBoxes()).Concat(new[]
                     { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                       _rootDurationBox, _slowHealMultiplierBox }))
        {
            if (!box.IsVisible || string.IsNullOrWhiteSpace(box.Text)) continue;
            var valid = ReferenceEquals(box, _treeAmountBox) || ReferenceEquals(box, _numberToCreateBox) ||
                        ReferenceEquals(box, _maxSpawnNumberBox)
                        || ReferenceEquals(box, _numberToTransformBox)
                        || ReferenceEquals(box, _wallSegmentCountBox)
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
        => await CommitCurrentPowerAsync();

    private void AddActiveRollover_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEntry == null || !CanEditProperties(_currentEntry))
            return;
        var id = PowerCatalog.BuildStringId(CurrentPowerName!, "activepowerrolloverid");
        PowerCatalog.SetField(_currentEntry, "activepowerrolloverid", id);
        _pendingStringRemovals.Remove(id);
        _pendingStringRemovalOwners.Remove(id);
        _pendingStringUpdates.TryAdd(id, "");
        _pendingStringOwners[id] = CurrentPowerName!;
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
        _pendingStringUpdates.Remove(id);
        _pendingStringOwners.Remove(id);
        var savedOwner = _loadedModDocument == null
            ? null
            : PowerCatalog.Find(_loadedModDocument, ownerName);
        var wasSavedForOwner = savedOwner?.Elements().Any(field =>
            StringBackedTags.Contains(field.Name.LocalName, StringComparer.OrdinalIgnoreCase) &&
            field.Value.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)) == true;
        if (!wasSavedForOwner)
        {
            _pendingStringRemovals.Remove(id);
            _pendingStringRemovalOwners.Remove(id);
            return;
        }
        _pendingStringRemovals.Add(id);
        _pendingStringRemovalOwners[id] = ownerName;
    }

    public async Task<bool> CommitCurrentPowerAsync()
    {
        if (!IsModifiedMode || _currentEntry == null)
            return true;

        var numericError = _rawEditorDirty ? null : ValidateNumericProperties();
        if (numericError != null)
        {
            _statusMessage.Text = numericError;
            if (TopLevel.GetTopLevel(this) is Window validationOwner)
                await new Prompt(PromptType.Error, "Invalid power number", numericError).ShowDialog(validationOwner);
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
                    _pendingStringUpdates[newId] = value;
                    _pendingStringOwners[newId] = newName;
                    _pendingStringRemovals.Remove(newId);
                    _pendingStringRemovalOwners.Remove(newId);
                    ScheduleStringRemoval(oldId, oldName);
                }
                foreach (var id in _pendingStringOwners.Where(pair =>
                             pair.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                         .Select(pair => pair.Key).ToList())
                    _pendingStringOwners[id] = newName;
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
            XmlTriviaPreserver.Preserve(_currentEntry, candidate);
            _currentEntry.ReplaceWith(candidate);
            _currentEntry = candidate;
            _rawEditorDirty = false;
            _applyRawXmlButton.IsVisible = false;
            if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                AddOrCollapsePendingRename(oldName, newName);
            _dirtyNames.Remove(oldName);
            _dirtyNames.Add(newName);
            CurrentPowerName = newName;
            _loadedEntryName = newName;
            SetRawXml(PowerCatalog.FormatPreview(candidate));
            SetDirty(true);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            _ = PopulatePropertiesAsync(candidate);
            return true;
        }
        catch (Exception exception)
        {
            if (!_rawEditorDirty && _currentEntry != null)
            {
                var wasPopulating = _isPopulating;
                _isPopulating = true;
                try { _nameBox.Text = PowerCatalog.GetName(_currentEntry); }
                finally { _isPopulating = wasPopulating; }
            }
            _statusMessage.Text = exception.Message;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Invalid power data", exception.Message).ShowDialog(owner);
            return false;
        }
    }

    private async Task<string> ResolveStringTextAsync(string id)
    {
        if (_pendingStringUpdates.TryGetValue(id, out var pending))
            return pending;
        return _resolveStringAsync == null ? id : await _resolveStringAsync(id) ?? id;
    }

    public async Task AddPowerAsync(bool duplicateSelected = false)
    {
        if (_modDocument?.Root == null)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before adding a power.").ShowDialog(owner);
            return;
        }
        if (!await CommitCurrentPowerAsync())
            return;

        var source = duplicateSelected ? _currentEntry : null;
        if (source != null && !PowerCatalog.IsModCatalogPower(source))
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
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
            _pendingStringUpdates[newId] = generatedStrings[newId];
            _pendingStringOwners[newId] = name;
        }
        IsModifiedMode = true;
        _currentEntry = entry;
        _loadedEntryName = name;
        CurrentPowerName = name;
        _dirtyNames.Add(name);
        SetDirty(true);
        SelectPower(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteCurrentPowerAsync()
    {
        if (!IsModifiedMode || _currentEntry == null || string.IsNullOrWhiteSpace(CurrentPowerName))
            return;
        var currentName = CurrentPowerName;
        var referenceName = _pendingRenames.LastOrDefault(rename =>
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase)).Key;
        if (string.IsNullOrWhiteSpace(referenceName))
            referenceName = currentName;
        if (_ensureCanDeleteAsync != null && TopLevel.GetTopLevel(this) is Window deletionOwner &&
            !await _ensureCanDeleteAsync(deletionOwner, referenceName))
            return;

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            var prompt = new Prompt(PromptType.Confirm, "Delete power?", $"Delete '{currentName}' from powers_mods.xml?");
            await prompt.ShowDialog(owner);
            if (!prompt.Confirmed)
                return;
        }

        foreach (var tag in StringBackedTags)
        {
            var id = PowerCatalog.FindField(_currentEntry, tag)?.Value.Trim() ?? "";
            if (id.Equals(PowerCatalog.BuildStringId(currentName, tag), StringComparison.OrdinalIgnoreCase))
                ScheduleStringRemoval(id, currentName);
        }
        _currentEntry.Remove();
        _dirtyNames.Add(currentName);
        _pendingRenames.RemoveAll(rename =>
            rename.Key.Equals(referenceName, StringComparison.OrdinalIgnoreCase) ||
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase));
        _currentEntry = null;
        _loadedEntryName = null;
        CurrentPowerName = null;
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
        => _dirtyNames.Contains(name) ||
           (_currentEntry != null &&
            PowerCatalog.GetName(_currentEntry).Equals(name, StringComparison.OrdinalIgnoreCase) &&
            _rawEditorDirty);

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

        _pendingRenames.RemoveAll(rename =>
            rename.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            rename.Value.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            restoredName.Length > 0 &&
            (rename.Key.Equals(restoredName, StringComparison.OrdinalIgnoreCase) ||
             rename.Value.Equals(restoredName, StringComparison.OrdinalIgnoreCase)));
        _dirtyNames.Remove(name);
        if (restoredName.Length > 0)
            _dirtyNames.Remove(restoredName);
        var discardedNames = new[] { name, restoredName }
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _pendingStringOwners.Where(pair => discardedNames.Contains(pair.Value))
                     .Select(pair => pair.Key).ToList())
        {
            _pendingStringOwners.Remove(id);
            _pendingStringUpdates.Remove(id);
        }
        foreach (var id in _pendingStringRemovalOwners.Where(pair => discardedNames.Contains(pair.Value))
                     .Select(pair => pair.Key).ToList())
        {
            _pendingStringRemovalOwners.Remove(id);
            _pendingStringRemovals.Remove(id);
        }
        _rawEditorDirty = false;
        SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0);
        if (string.Equals(CurrentPowerName, name, StringComparison.OrdinalIgnoreCase))
            SelectPower(restoredName.Length == 0 ? null : restoredName);
    }

    public async Task<bool> SaveAsync()
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
            SelectPower(selectedName);
    }

    public void AcknowledgeStringFileSavedByEditor()
    {
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (!await CommitCurrentPowerAsync())
            return false;
        if (!IsDirty)
            return true;
        if (_modDocument == null || string.IsNullOrWhiteSpace(_modPath))
            return false;
        if (!await ReloadExternallyChangedDocumentAsync())
            return false;

        try
        {
            foreach (var name in _dirtyNames)
            {
                var editedEntry = PowerCatalog.Find(_modDocument, name);
                if (editedEntry != null)
                    PowerCatalog.CollapseExcessiveBlankLines(editedEntry);
            }
            var renamesToSave = _pendingRenames.ToList();
            foreach (var rename in renamesToSave)
            {
                var unsupported = GameIdentifierReferencePolicy.FindUnrecognizedReferences(
                    _modDocument, GameIdentifierKind.Ability, rename.Key);
                if (unsupported.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"'{rename.Key}' was not renamed because powers_mods.xml contains unsupported power reference field(s): " +
                        string.Join(", ", unsupported) + ". No files were written.");
                }
                GameIdentifierReferencePolicy.RenameReferences(
                    _modDocument, GameIdentifierKind.Ability, rename.Key, rename.Value);
            }

            var migration = renamesToSave.Count > 0
                ? _prepareReferenceMigration?.Invoke(renamesToSave)
                : null;
            var stringUpdates = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
            var stringRemovals = _pendingStringRemovals.Where(id =>
                !_modDocument.Descendants().Any(element => !element.HasElements &&
                    element.Value.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var writesStrings = stringUpdates.Count > 0 || stringRemovals.Count > 0;
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
            _pendingStringUpdates.Clear();
            _pendingStringOwners.Clear();
            _pendingStringRemovals.Clear();
            _pendingStringRemovalOwners.Clear();
            _dirtyNames.Clear();
            _pendingRenames.Clear();
            SetDirty(false);
            _statusMessage.Text = "Saved powers_mods.xml.";
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _statusMessage.Text = "Save failed: " + exception.Message;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Power save failed", exception.Message).ShowDialog(owner);
            return false;
        }
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
            if (merge.ReplacedEditorChanges.Count > 0)
            {
                foreach (var conflict in merge.ReplacedEditorChanges)
                    _dirtyNames.Remove(conflict);
            }
            _modDocument = merge.Document;
            _loadedModDocument = new XDocument(external);
            _loadedFileState = prepared.LoadedState;
            SetDirty(_dirtyNames.Count > 0);
            var selectedName = CurrentPowerName;
            if (!string.IsNullOrWhiteSpace(selectedName) && PowerCatalog.Find(_modDocument, selectedName) != null)
                SelectPower(selectedName);
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

    private void AddOrCollapsePendingRename(string oldName, string newName)
    {
        var chainedIndex = _pendingRenames.FindIndex(rename =>
            rename.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (chainedIndex >= 0)
        {
            var original = _pendingRenames[chainedIndex].Key;
            if (original.Equals(newName, StringComparison.OrdinalIgnoreCase))
                _pendingRenames.RemoveAt(chainedIndex);
            else
                _pendingRenames[chainedIndex] = new KeyValuePair<string, string>(original, newName);
            return;
        }
        _pendingRenames.Add(new KeyValuePair<string, string>(oldName, newName));
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
        if (!string.IsNullOrWhiteSpace(name))
            _dirtyNames.Add(name);
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
        _isPopulating = true;
        try
        {
            _rawXmlEditor.Text = text;
        }
        finally
        {
            _isPopulating = false;
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
        foreach (var box in new[] { _numberToTransformBox, _wallSegmentCountBox, _stayTimeBox,
                     _rootDurationBox, _slowHealMultiplierBox }) box.Text = "";
        _stealthLevelBox.SelectedIndex = -1;
        _damageTierRows.Children.Clear(); _damageTierFields.Clear();
        _rootDamageRows.Children.Clear(); _rootDamageFields.Clear();
        foreach (var row in new Control[]
                 { _cracksSoundRow, _timeFactorRow, _shockwaveTopRow, _throwPropertiesRow,
                   _gaiaTreeTopRow, _gaiaSpawnRow, _growthRadiusRow, _effectVfxRow,
                   _damagePercentConversionRow, _vfxProtoRow, _effectTimeRow,
                   _spiderPropertiesRow, _spiderCountRow, _hatchPropertiesRow,
                   _spiderTimingRow, _humanDeathSoundRow, _vortexPropertiesRow,
                   _vortexDropRow, _maxUnitHeightRow, _vortexSoundsRow,
                   _tartarianSpawnRow, _tartarianGateRow, _rumbleTimeRow,
                   _tartarianSpawnTimingRow, _implodeProtosRow, _implodeSoundsRow,
                   _implodePullRow, _implodeExplosionRow, _implodeSpherePropertiesRow,
                   _implodeDamageIntervalRow, _implodeShearRow, _implodeDelayRow,
                   _implodeTimingRow, _implodeOrbitRadiusRow, _implodeAngularRow,
                   _implodeCameraShakeRow, _pSeedsFarmRow, _pSeedsVfxRow, _stealthLevelRow,
                   _earthWallRow, _damageTiersRow, _stealthVfxRow, _forestVfxRow,
                   _forestTimingRow, _rootDamageRow, _slowHealMultiplierRow })
        {
            if (row.Parent is Panel parent) parent.Children.Remove(row);
            row.IsVisible = false;
        }
        CurrentPowerName = null;
        _loadedEntryName = null;
        _currentEntry = null;
        _nameBox.Text = "";
        _displayNameBox.Text = "";
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
                 { _activeTimeBox, _buildUpBox, _shutDownBox, _costBox, _repeatCostBox,
                     _limitGlobalBox, _limitPlayerBox, _radiusBox })
            box.Text = "";
        _singleActivationCheck.IsChecked = false;
        _exclusiveCheck.IsChecked = false;
        _isUseLimitOpen = false;
        _singleActivationCheck.IsVisible = false;
        _exclusiveCheck.IsVisible = false;
        _buildUpRow.IsVisible = false;
        _shutDownRow.IsVisible = false;
        _useLimitRow.IsVisible = false;
        _limitLabel.IsVisible = false;
        _exclusiveMessageRow.IsVisible = false;
        _addBuildUpButton.IsVisible = false;
        _addShutDownButton.IsVisible = false;
        _addUseLimitButton.IsVisible = false;
        _iconEditor.Configure("", _iconPaths, null);
        _ = _iconPreview.ShowAsync(null);
        foreach (var control in new Control[]
                 { _nameBox, _displayNameBox, _rolloverBox, _activeRolloverBox, _iconEditor,
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
                     _spawnProtoBox, _maxSpawnNumberBox, _tartarianGateUnitBox,
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
                     _activeTimeBox, _buildUpBox, _shutDownBox, _costBox, _repeatCostBox,
                     _limitGlobalBox, _limitPlayerBox, _radiusBox,
                     _singleActivationCheck, _exclusiveCheck,
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
        IsDirty = value;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
