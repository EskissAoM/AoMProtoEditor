using System.Globalization;
using Avalonia.LogicalTree;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView : UserControl
{
    private EditorSaveFeedback _saveFeedback = null!;
    internal Action<EditorSaveOutcome>? SaveFeedbackReported { get; set; }
    internal void UseHostStatusFooter(Action<string> reportContext) => _saveFeedback.UseHostFooter(reportContext);
    private void ReportSaveFeedback(EditorSaveOutcome outcome)
    {
        _saveFeedback.Report(outcome);
        SaveFeedbackReported?.Invoke(outcome);
    }
    internal Func<IReadOnlyList<string>, IReadOnlyDictionary<string, XDocument>, IReadOnlyList<ModDeletionInspection>>? PrepareDeletionChecks { get; set; }
    internal Func<Task>? ImmediateDeletionRequestedAsync { get; set; }
    internal IEnumerable<string> PendingStringIds => _pendingStringUpdates.Keys;
    internal bool LastSaveWroteStrings { get; private set; }
    internal Func<IEnumerable<string>, IReadOnlyDictionary<string, XDocument>, OwnedStringSaveInspection>? PrepareStringCleanup { get; set; }
    internal IEnumerable<XDocument> CreateStringReferenceDrafts()
    {
        if (CreateModifiedDocumentSnapshot() is { } document) yield return document;
        if (_rawEditorDirty) yield return XDocument.Parse(_rawXmlEditor.Text ?? "", LoadOptions.PreserveWhitespace);
    }
    private IReadOnlyList<ModDeletionInspection>? _immediateDeletionChecks;
    private readonly Dictionary<string, string> _resolvedStringTexts = new(StringComparer.OrdinalIgnoreCase);
    private int _economicEditorGeneration;
    private Window? _immediateDeletionOwner;
    internal bool IsCurrentEntrySaved => _loadedModDocument != null && CurrentMajorGodName != null &&
        MajorGodCatalog.Find(_loadedModDocument, _pendingRenames.LastOrDefault(rename =>
            rename.Value.Equals(CurrentMajorGodName, StringComparison.OrdinalIgnoreCase)).Key ?? CurrentMajorGodName) != null;
    private static readonly string[] ResolvedStringTags = ["displaynameid", "rollovernameid", "titleid"];
    private static readonly string[] ImageTags = ["icon", "portrait", "breakoutportrait"];
    private static readonly string[] StartingModes = ["Supremacy", "Deathmatch", "Lightning"];
    private static readonly (string Label, string Tag)[] BonusGoalFlags =
    [
        ("ForceSpawn", "forcespawn"),
        ("AgeRestricted", "agerestricted"),
        ("CultureRestricted", "culturerestricted"),
        ("ArchaicSpawn", "archaicspawn"),
        ("CurrentAgeOnly", "currentageonly"),
        ("NextAgeOnly", "nextageonly"),
        ("IgnoreAgeRoll", "ignoreageroll")
    ];
    private static readonly (string Label, string Tag)[] SpecialMechanics =
        MajorGodCatalog.SpecialMechanicDefinitions;

    private readonly Dictionary<string, XElement> _baseEntries;
    private readonly string? _modPath;
    private readonly string? _modStringsPath;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync;
    private readonly Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
        _prepareReferenceMigration;
    private readonly Action<GameIdentifierReferenceMigrationPlan>? _adoptReferenceMigration;
    private readonly Func<Window, string, Task<bool>>? _ensureCanDeleteAsync;
    private readonly Window? _dialogOwner;
    private readonly IReadOnlyList<string> _cultureNames;
    private readonly SelectorCatalog _technologyNames;
    private readonly SelectorCatalog _protoUnitNames;
    private readonly SelectorCatalog _unitTypeNames;
    private readonly SelectorCatalog _unitOrTypeNames;
    private readonly SelectorCatalog _technologyFlagNames;
    private readonly SelectorCatalog _techTypeNames;
    private readonly IReadOnlyList<string> _terrainCreepNames;
    private readonly IReadOnlyList<string> _iconPaths;
    private readonly IReadOnlyList<string> _minimapBorderValues;
    private readonly IReadOnlyList<string> _backgroundScenarioValues;
    private readonly XElement? _baseSharedData;
    private readonly IconPreviewControl _iconPreview;
    private readonly Dictionary<string, string> _stringIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingStringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringRemovalOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _pendingRenames = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private XDocument? _modDocument;
    private XDocument? _loadedModDocument;
    private FileIntegrityTransaction.FileState? _loadedFileState;
    private FileIntegrityTransaction.FileState? _loadedStringsFileState;
    private XElement? _currentEntry;
    private string? _loadedEntryName;
    private bool _isPopulating;
    private bool _rawEditorDirty;
    private bool _isXmlPreviewCollapsed;
    private int _populateGeneration;
    private Task _populationTask = Task.CompletedTask;
    private Action? _refreshBonusUnitSpawningEditor;
    private string? _selectedSpecialMechanicTag;

    public bool IsModifiedMode { get; private set; }
    public string? CurrentMajorGodName { get; private set; }
    public bool IsDirty { get; private set; }

    public event EventHandler? BrowserStateChanged;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? Saved;

    public void AcknowledgeStringFileSavedByEditor()
    {
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
    }

    public MajorGodEditorView() : this([], null)
    {
    }

    public MajorGodEditorView(
        IEnumerable<MajorGodDefinition> baseDefinitions,
        string? modPath,
        Func<string, Task<string?>>? resolveStringAsync = null,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
        IEnumerable<string>? cultureNames = null,
        IEnumerable<string>? iconPaths = null,
        IconPreviewService? iconPreviewService = null,
        string? modStringsPath = null,
        IEnumerable<string>? technologyNames = null,
        IEnumerable<string>? protoUnitNames = null,
        XElement? baseSharedData = null,
        IEnumerable<string>? unitTypeNames = null,
        IEnumerable<string>? technologyFlagNames = null,
        IEnumerable<string>? techTypeNames = null,
        IEnumerable<string>? terrainCreepNames = null,
        Func<IReadOnlyList<KeyValuePair<string, string>>, GameIdentifierReferenceMigrationPlan>?
            prepareReferenceMigration = null,
        Action<GameIdentifierReferenceMigrationPlan>? adoptReferenceMigration = null,
        Func<Window, string, Task<bool>>? ensureCanDeleteAsync = null,
        Window? dialogOwner = null)
    {
        InitializeComponent();
        EditorXmlPreviewPane.Configure(_mainGrid, 0, 1, 2, _xmlPreviewContent, _previewSplitter, _xmlPreviewToggleButton);
        _saveFeedback = new EditorSaveFeedback(_statusMessage);
        XmlSyntaxEditorService.Configure(_rawXmlEditor);
        InitializeMajorGodXmlEditing();
        ConfigureFieldControls();

        _baseEntries = baseDefinitions.ToDictionary(
            definition => definition.Name,
            definition => new XElement(definition.SourceElement),
            StringComparer.OrdinalIgnoreCase);
        _modPath = modPath;
        _modStringsPath = modStringsPath;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _prepareReferenceMigration = prepareReferenceMigration;
        _adoptReferenceMigration = adoptReferenceMigration;
        _ensureCanDeleteAsync = ensureCanDeleteAsync;
        _dialogOwner = dialogOwner;
        _cultureNames = (cultureNames ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _technologyNames = new SelectorCatalog(technologyNames);
        _protoUnitNames = new SelectorCatalog(protoUnitNames);
        _unitTypeNames = new SelectorCatalog(unitTypeNames ?? ProtoConstants.KnownUnitTypes);
        _unitOrTypeNames = new SelectorCatalog(_protoUnitNames.Concat(_unitTypeNames));
        _technologyFlagNames = new SelectorCatalog(technologyFlagNames);
        _techTypeNames = new SelectorCatalog(techTypeNames);
        _terrainCreepNames = (terrainCreepNames ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _baseSharedData = baseSharedData == null ? null : new XElement(baseSharedData);
        _cultureBox.ItemsSource = _cultureNames;
        EditorAutoCompleteService.EnableDropdown(_cultureBox, () => _isPopulating);
        SetReadOnlyAge("ArchaicAge");
        foreach (var technologyBox in new[] { _ageTechBox, _deathmatchTechBox, _teamTechBox })
        {
            technologyBox.ItemsSource = _technologyNames;
            EditorAutoCompleteService.EnableDropdown(technologyBox, () => _isPopulating);
        }
        _iconPaths = (iconPaths ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _minimapBorderValues = ExtractBaseValues("minimapborderimage");
        _backgroundScenarioValues = ExtractBaseValues("backgroundscenario");
        _iconPreview = new IconPreviewControl(iconPreviewService);
        _iconPreviewHost.Child = _iconPreview;

        _minimapBorderBox.ItemsSource = _minimapBorderValues;
        _backgroundScenarioBox.ItemsSource = _backgroundScenarioValues;
        EditorAutoCompleteService.EnableDropdown(_minimapBorderBox, () => _isPopulating);
        EditorAutoCompleteService.EnableDropdown(_backgroundScenarioBox, () => _isPopulating);
        EditorAutoCompleteService.EnableDropdown(_specialMechanicSelector, () => _isPopulating, selectAllOnFirstClick: false);

        if (!string.IsNullOrWhiteSpace(_modPath))
        {
            var prepared = ModDocumentPersistence.Prepare(_modPath, ModXmlDocumentKind.MajorGods);
            _modDocument = prepared.Document;
            _loadedFileState = prepared.LoadedState;
            _loadedModDocument = new XDocument(_modDocument);
        }
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
        ShowEmpty();
    }

    private void ConfigureFieldControls()
    {
        ConfigureMajorGodField(_nameBox, EditorFieldWidthRole.SingleLineText);
        ConfigureMajorGodField(_displayNameBox, EditorFieldWidthRole.SingleLineText);
        EditorRolloverField.Configure(_rolloverNameBox);
        EditorRolloverField.ConfigureIdentity(_identityLayout,
            _identityLayout.Children.OfType<StackPanel>().First(), _iconPreviewHost);
        ConfigureMajorGodField(_titleBox, EditorFieldWidthRole.SingleLineText);
        foreach (var reference in new Control[] { _cultureBox, _ageTechBox, _deathmatchTechBox, _teamTechBox,
                     _sharedLightningTechBox, _sharedDeathMatchTechBox, _sharedStoryModeTechBox, _sharedLudicrousModeTechBox })
            ConfigureMajorGodField(reference, EditorFieldWidthRole.Reference);
        foreach (var asset in new Control[] { _minimapBorderBox, _backgroundScenarioBox, _iconEditor, _portraitEditor, _breakoutPortraitEditor })
            ConfigureMajorGodField(asset, EditorFieldWidthRole.AssetPath);
        foreach (var number in new[] { _defaultPopLimitBox, _defaultRepairFactorBox, _defaultContainedRegenRateBox,
                     _sharedArchaicDelayBox, _sharedClassicalDelayBox, _sharedHeroicDelayBox, _sharedMythicDelayBox })
            ConfigureMajorGodField(number, EditorFieldWidthRole.Numeric);
        ConfigureMajorGodField(_ageBox, EditorFieldWidthRole.LongEnum);
        ConfigureMajorGodField(_specialMechanicSelector, EditorFieldWidthRole.LongEnum);
        EditorFormLayout.WrapLabeledGridColumns((Grid)_ageBox.Parent!, [1], [2, 3]);
        EditorFormLayout.WrapLabeledGridColumns((Grid)_cultureBox.Parent!, [1], [2, 3]);
        EditorFormLayout.WrapLabeledGridColumns(_teamTechRow, [1, 2]);
        // Single-character civilisation key: preserve its reviewed 70px text exception.
        EditorFieldInteraction.Configure(_keyBox, EditorFieldWidthRole.SingleLineText, () => _isPopulating);
        _keyBox.MinHeight = _keyBox.Height = _keyBox.MaxHeight = EditorFormLayout.FieldHeight;
    }

    private IReadOnlyList<string> ExtractBaseValues(string tag)
        => _baseEntries.Values
            .Select(entry => MajorGodCatalog.FindDirectChild(entry, tag)?.Value.Trim() ?? "")
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> GetMajorGodNames(bool modified)
    {
        if (!modified)
        {
            var names = _baseEntries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_baseSharedData != null)
                names.Insert(0, MajorGodCatalog.SharedDataDisplayName);
            return names;
        }
        if (_modDocument?.Root == null)
            return [];
        return MajorGodCatalog.ExtractDefinitions(_modDocument, false).Select(definition => definition.Name).ToList();
    }

    public void UpdateTechnologyCatalogs(
        IEnumerable<string> technologyNames,
        IEnumerable<string> technologyFlagNames,
        IEnumerable<string> techTypeNames)
    {
        UpdateReferenceCatalogs(_protoUnitNames, _unitTypeNames, technologyNames, technologyFlagNames, techTypeNames);
    }

    internal void UpdateReferenceCatalogs(IEnumerable<string> units, IEnumerable<string> types,
        IEnumerable<string> technologies, IEnumerable<string> flags, IEnumerable<string> techTypes)
    {
        var populating = _isPopulating;
        _isPopulating = true;
        try
        {
            var changed = _protoUnitNames.ReplaceWith(units) | _unitTypeNames.ReplaceWith(types) |
                          _technologyNames.ReplaceWith(technologies) | _technologyFlagNames.ReplaceWith(flags) |
                          _techTypeNames.ReplaceWith(techTypes);
            changed |= _unitOrTypeNames.ReplaceWith(_protoUnitNames.Concat(_unitTypeNames));
            if (!changed) return;
            foreach (var box in new[] { _ageTechBox, _deathmatchTechBox, _teamTechBox })
                EditorAutoCompleteService.SetCatalogSuggestions(box, _technologyNames);
            SelectorCatalogRefresh.Apply(this);
        }
        finally { _isPopulating = populating; }
    }

    private static IReadOnlyList<string> NormalizeCatalog(IEnumerable<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void SetModifiedMode(bool modified)
    {
        if (IsModifiedMode == modified)
            return;
        IsModifiedMode = modified;
        CurrentMajorGodName = null;
        _currentEntry = null;
        _loadedEntryName = null;
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectMajorGod(string? name)
    {
        EditorValidationNavigation.Clear(this);
        if (!IsModifiedMode && _baseSharedData != null &&
            name?.Equals(MajorGodCatalog.SharedDataDisplayName, StringComparison.OrdinalIgnoreCase) == true)
        {
            ShowSharedData();
            return;
        }

        ShowMajorGodLayout();
        CurrentMajorGodName = name;
        _loadedEntryName = name;
        _currentEntry = string.IsNullOrWhiteSpace(name)
            ? null
            : IsModifiedMode
                ? MajorGodCatalog.Find(_modDocument!, name)
                : _baseEntries.GetValueOrDefault(name);
        _rawEditorDirty = CurrentMajorGodXmlDraft?.Session.IsDirty == true;

        if (_currentEntry == null)
        {
            ShowEmpty();
            return;
        }

        SetRawXml(FormatMajorGodPreview(_currentEntry));
        _statusMessage.Text = IsModifiedMode
            ? ""
            : "Original Data.bar entry (read-only).";
        _populationTask = PopulateStructuredFieldsAsync(_currentEntry);
    }

    private void ShowSharedData()
    {
        if (_baseSharedData == null)
            return;

        CurrentMajorGodName = MajorGodCatalog.SharedDataDisplayName;
        _loadedEntryName = null;
        _currentEntry = null;
        _rawEditorDirty = false;
        _majorGodIdentityRow.IsVisible = false;
        _majorGodTabs.IsVisible = true;
        _emptySelectionMessage.IsVisible = false;
        _majorGodPropertiesPanel.IsVisible = false;
        _sharedDataPropertiesPanel.IsVisible = true;
        _startingAttributesTab.IsVisible = false;
        _specialAttributesTab.IsVisible = false;
        _majorGodTabs.SelectedIndex = 0;

        _defaultPopLimitBox.Text = EditorNumericFieldStyle.FormatDisplay(ReadValue(_baseSharedData, "defaultpoplimit"));
        _defaultRepairFactorBox.Text = EditorNumericFieldStyle.FormatDisplay(ReadValue(_baseSharedData, "defaultrepairfactor"));
        var containedRegen = MajorGodCatalog.FindDirectChild(_baseSharedData, "defaultcontainedregenrate");
        _defaultContainedRegenRow.IsVisible = containedRegen != null;
        _defaultContainedRegenRateBox.Text = EditorNumericFieldStyle.FormatDisplay(containedRegen?.Value.Trim() ?? "");
        _sharedLightningTechBox.Text = ReadValue(_baseSharedData, "lightningtech");
        _sharedDeathMatchTechBox.Text = ReadValue(_baseSharedData, "deathmatchtech");
        _sharedStoryModeTechBox.Text = ReadValue(_baseSharedData, "storymodetech");
        _sharedLudicrousModeTechBox.Text = ReadValue(_baseSharedData, "ludicrousmodetech");
        var delays = MajorGodCatalog.FindDirectChild(_baseSharedData, "deathmatchpowergrantdelay");
        _sharedArchaicDelayBox.Text = EditorNumericFieldStyle.FormatDisplay(delays == null ? "" : ReadValue(delays, "archaicage"));
        _sharedClassicalDelayBox.Text = EditorNumericFieldStyle.FormatDisplay(delays == null ? "" : ReadValue(delays, "classicalage"));
        _sharedHeroicDelayBox.Text = EditorNumericFieldStyle.FormatDisplay(delays == null ? "" : ReadValue(delays, "heroicage"));
        _sharedMythicDelayBox.Text = EditorNumericFieldStyle.FormatDisplay(delays == null ? "" : ReadValue(delays, "mythicage"));

        SetRawXml(XmlSyntaxEditorService.FormatElementForPreview(_baseSharedData));
        _statusMessage.Text = "Original shared Major God data (read-only).";
        FinalizeMajorGodPresentation();
    }

    private void ShowMajorGodLayout()
    {
        _majorGodTabs.IsVisible = true;
        _emptySelectionMessage.IsVisible = false;
        _majorGodIdentityRow.IsVisible = true;
        _majorGodPropertiesPanel.IsVisible = true;
        _sharedDataPropertiesPanel.IsVisible = false;
        _startingAttributesTab.IsVisible = true;
        _specialAttributesTab.IsVisible = true;
    }

    private async Task PopulateStructuredFieldsAsync(XElement entry)
    {
        var generation = ++_populateGeneration;
        _isPopulating = true;
        try
        {
            var editable = IsModifiedMode;
            _nameBox.Text = MajorGodCatalog.GetName(entry);
            _keyBox.Text = ReadValue(entry, "key");

            var culture = ReadValue(entry, "culture");
            _cultureBox.Text = _cultureNames.FirstOrDefault(value =>
                value.Equals(culture, StringComparison.OrdinalIgnoreCase)) ?? culture;

            SetEditorsEnabled(editable);
            ConfigureImageEditor(_iconEditor, entry, "icon");
            ConfigureImageEditor(_portraitEditor, entry, "portrait");
            ConfigureImageEditor(_breakoutPortraitEditor, entry, "breakoutportrait");
            _minimapBorderBox.Text = ReadValue(entry, "minimapborderimage");
            _backgroundScenarioBox.Text = ReadValue(entry, "backgroundscenario");
            var ageTech = MajorGodCatalog.FindDirectChild(entry, "agetech");
            var age = ageTech?.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("age", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "ArchaicAge";
            SetReadOnlyAge(age);
            _ageTechBox.Text = ageTech == null ? "" : ReadValue(ageTech, "tech");
            _deathmatchTechBox.Text = ReadValue(entry, "deathmatchtech");
            _teamTechBox.Text = ReadValue(entry, "teamtech");
            RefreshTeamTechVisibility();
            RebuildEconomicEditor();
            RebuildStartingUnitsEditor();
            RebuildStartingQueueEditor();
            RebuildStartingResourcesEditor();
            RebuildSpecialMechanicsEditor();
            RefreshIconPreview();

            _stringIds.Clear();
            _resolvedStringTexts.Clear();
            var ids = ResolvedStringTags.ToDictionary(tag => tag, tag => ReadValue(entry, tag), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ids)
                _stringIds[pair.Key] = pair.Value;

            var resolutions = await Task.WhenAll(ResolvedStringTags.Select(async tag =>
            {
                var id = ids[tag];
                if (_pendingStringUpdates.TryGetValue(id, out var pending))
                    return (Tag: tag, Id: id, Text: pending);
                var resolved = _resolveStringAsync == null || string.IsNullOrWhiteSpace(id)
                    ? null
                    : await _resolveStringAsync(id);
                return (Tag: tag, Id: id, Text: resolved ?? id);
            }));
            if (generation != _populateGeneration || !ReferenceEquals(entry, _currentEntry))
                return;

            foreach (var resolution in resolutions)
            {
                var box = GetResolvedStringBox(resolution.Tag);
                _resolvedStringTexts[resolution.Tag] = resolution.Text;
                box.Text = resolution.Text;
                ToolTip.SetTip(box, null);
            }
        }
        finally
        {
            if (generation == _populateGeneration)
            {
                FinalizeMajorGodPresentation();
                _isPopulating = false;
            }
        }
    }

    private void SetEditorsEnabled(bool editable)
    {
        foreach (var control in new Control[]
                 {
                     _nameBox, _keyBox, _cultureBox, _displayNameBox, _rolloverNameBox, _titleBox,
                     _iconEditor, _portraitEditor, _breakoutPortraitEditor, _minimapBorderBox, _backgroundScenarioBox,
                     _ageTechBox, _deathmatchTechBox, _teamTechBox
                 })
        {
            control.IsEnabled = editable;
            control.Opacity = editable ? 1.0 : 0.62;
        }
        foreach (var editor in new[] { _iconEditor, _portraitEditor, _breakoutPortraitEditor })
            editor.CompactPresenter.Background = Brush.Parse(editable ? "#0E1110" : "#202220");
        _addTeamTechButton.IsEnabled = editable;
        _removeTeamTechButton.IsEnabled = editable;
        _addStartingUnitsGroupButton.IsEnabled = editable;
        _addStartingQueueGroupButton.IsEnabled = editable;
        _addStartingResourcesGroupButton.IsEnabled = editable;
        _economicPanel.IsEnabled = editable;
        RefreshTeamTechVisibility();
    }

    private void ConfigureImageEditor(AssetPathEditor editor, XElement entry, string tag)
    {
        var initial = ProtoEditorWindow.NormalizeIconCatalogValue(ReadValue(entry, tag), _iconPaths);
        editor.Configure(initial, _iconPaths, value =>
        {
            if (_isPopulating || !IsModifiedMode || !ReferenceEquals(entry, _currentEntry))
                return Task.CompletedTask;
            SetEntryValue(tag, value);
            RefreshIconPreview();
            return Task.CompletedTask;
        });
    }

    private void RefreshIconPreview()
    {
        if (_currentEntry == null)
            return;
        _ = _iconPreview.ShowOptionsAsync(ImageTags.Select(tag =>
            (Path: ReadValue(_currentEntry, tag), Culture: (string?)(tag switch
            {
                "icon" => "Icon",
                "portrait" => "Portrait",
                _ => "Breakout portrait"
            }))));
    }

    public Task<bool> CommitCurrentMajorGodAsync() => CommitMajorGodAsync(validateContent: true);

    internal Task<bool> CaptureCurrentMajorGodDraftAsync() => CommitMajorGodAsync(validateContent: false);

    private async Task<bool> CommitMajorGodAsync(bool validateContent)
    {
        if (!IsModifiedMode || _currentEntry == null)
            return true;
        // Retain raw text on navigation. Only Save parses/validates it, so
        // invalid XML cannot prevent reaching the host's discard decision.
        if (CurrentMajorGodXmlDraft?.Session.IsEditing == true) return true;

        try
        {
            var candidate = _currentEntry;
            var hasPendingEdit = IsMajorGodDirty(CurrentMajorGodName ?? MajorGodCatalog.GetName(candidate));
            var newName = ValidateMajorGodCandidate(candidate, _loadedEntryName, _currentEntry, hasPendingEdit, validateContent);
            if (!hasPendingEdit)
                return true;
            // Validation also runs on focus/navigation. Normalize stored XML
            // only for a real edit, so an untouched entry stays untouched.
            if (hasPendingEdit)
            {
                MajorGodCatalog.NormalizeEditorFieldOrder(candidate);
                var statsBefore = ReadValue(candidate, "statsid");
                MajorGodCatalog.EnsureStatsId(candidate);
                if (!statsBefore.Equals("SP", StringComparison.Ordinal))
                    MarkCurrentDirty();
            }

            var oldName = _loadedEntryName;
            var entryBeingRenamed = _currentEntry;
            var renameGeneration = _populateGeneration;
            try
            {
                if (!await AdoptNameAndGeneratedStringIdsAsync(newName)) return false;
            }
            catch
            {
                // A failed lookup from a discarded edit must not restore its old name
                // into the newly selected/restored entry or open an obsolete error dialog.
                if (!ReferenceEquals(entryBeingRenamed, _currentEntry) || renameGeneration != _populateGeneration ||
                    !string.Equals(_loadedEntryName, oldName, StringComparison.Ordinal) ||
                    !MajorGodCatalog.GetName(entryBeingRenamed).Equals(newName, StringComparison.Ordinal)) return false;
                if (!string.IsNullOrWhiteSpace(oldName))
                {
                    MajorGodCatalog.FindDirectChild(_currentEntry!, "name")!.Value = oldName;
                    var populating = _isPopulating;
                    _isPopulating = true;
                    try { _nameBox.Text = oldName; } finally { _isPopulating = populating; }
                }
                throw;
            }
            if (!string.IsNullOrWhiteSpace(oldName) &&
                !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                AddOrCollapsePendingRename(oldName, newName);
            if (oldName != null) MoveMajorGodXmlDraft(oldName, newName);
            CurrentMajorGodName = newName;
            _loadedEntryName = newName;
            SetRawXml(FormatMajorGodPreview(candidate));
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            var failedEntry = _currentEntry;
            if (failedEntry != null)
            {
                EditorValidationBindings.Bind(_nameBox, failedEntry, "name");
                EditorValidationBindings.Bind(_keyBox, failedEntry, "key");
                EditorValidationBindings.Bind(_cultureBox, failedEntry, "culture");
                BindMajorGodValidationFields(failedEntry);
            }
            var location = ex.Data[typeof(ValidationSourceLocation)] as ValidationSourceLocation;
            if (location != null && failedEntry != null &&
                (location.Record.Equals(MajorGodCatalog.GetName(failedEntry), StringComparison.OrdinalIgnoreCase) ||
                 location.Record.Equals(_nameBox.Text?.Trim(), StringComparison.OrdinalIgnoreCase)))
                location = location with { Record = MajorGodCatalog.GetName(failedEntry) };
            var target = CurrentMajorGodXmlDraft?.Session.IsEditing == true ? null :
                EditorValidationBindings.Find(this, new LocatedValidationIssue(ex.Message, location));
            var failure = target == null ? null : EditorValidationNavigation.Mark(this, target);
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Invalid major god data", ex.Message).ShowDialog(owner);
            if (failure != null && target != null)
                await failure.RevealAsync(() => ReferenceEquals(failedEntry, _currentEntry),
                    () => _majorGodTabs.SelectedIndex = target.GetLogicalAncestors().Contains(_specialMechanicsPanel) ? 2 :
                        target.GetLogicalAncestors().Contains(_startingAttributesTab) ? 1 : 0);
            _statusMessage.Text = ex.Message;
            return false;
        }
    }

    private async Task<bool> AdoptNameAndGeneratedStringIdsAsync(string newName)
    {
        var oldName = _loadedEntryName;
        if (_currentEntry == null || string.IsNullOrWhiteSpace(oldName) || oldName.Equals(newName, StringComparison.Ordinal))
            return true;

        var entry = _currentEntry;
        var generation = _populateGeneration;
        var before = new XElement(_currentEntry);
        MajorGodCatalog.FindDirectChild(before, "name")!.Value = oldName;
        var candidate = new XElement(_currentEntry);
        var changes = MajorGodCatalog.RenameOwnedResolvedStringIds(candidate, oldName, newName);
        var plan = await PrepareStringRenameAsync(before, candidate, changes, MajorGodCatalog.CollectOwnedStringIds(before), newName, reuseSavedOwner: true);
        // Focus/navigation commits can still be awaiting string lookup when a draft is
        // discarded or replaced. Never apply that result to the restored/next God.
        if (!ReferenceEquals(entry, _currentEntry) || generation != _populateGeneration ||
            !string.Equals(_loadedEntryName, oldName, StringComparison.Ordinal) ||
            !MajorGodCatalog.GetName(entry).Equals(newName, StringComparison.Ordinal)) return false;
        OwnedStringSavePreparation.ApplyLocalFieldChanges(_currentEntry, candidate);
        ApplyMajorGodStringRenamePlan(_currentEntry, oldName, newName, plan, updateSelectedFields: true);
        return true;
    }

    private void ApplyMajorGodStringRenamePlan(XElement entry, string oldName, string newName,
        OwnedStringLifecyclePlan plan, bool updateSelectedFields)
    {
        foreach (var rename in plan.ReferenceRenames)
        {
            _pendingStringRemovals.Remove(rename.NewId);
            _pendingStringRemovalOwners.Remove(rename.NewId);
            _pendingStringUpdates[rename.NewId] = plan.StringUpdates[rename.NewId];
            _pendingStringUpdates.Remove(rename.OldId);
            _pendingStringOwners.Remove(rename.OldId);
            _pendingStringOwners[rename.NewId] = newName;
            ScheduleOwnedStringRemoval(rename.OldId, newName);
        }
        if (updateSelectedFields)
            foreach (var tag in ResolvedStringTags)
                _stringIds[tag] = MajorGodCatalog.FindDirectChild(entry, tag)?.Value.Trim() ?? "";

        if (_dirtyNames.Remove(oldName))
            _dirtyNames.Add(newName);
        foreach (var id in _pendingStringOwners.Where(pair =>
                     pair.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList())
            _pendingStringOwners[id] = newName;
        foreach (var id in _pendingStringRemovalOwners.Where(pair =>
                     pair.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList())
            _pendingStringRemovalOwners[id] = newName;
    }

    private async Task<OwnedStringLifecyclePlan> PrepareStringRenameAsync(XElement before, XElement candidate,
        IReadOnlyList<OwnedStringRename> changes, IReadOnlySet<string> owned, string ownerName,
        bool reuseSavedOwner = false, string? sourceMajorGodName = null)
    {
        var entry = _currentEntry;
        var generation = _populateGeneration;
        var entryName = entry == null ? null : MajorGodCatalog.GetName(entry);
        var existing = OwnedStringSavePreparation.ReadEntries(_modStringsPath);
        var sourceName = sourceMajorGodName ?? _loadedEntryName;
        var originalName = _pendingRenames.LastOrDefault(rename => rename.Value.Equals(sourceName, StringComparison.OrdinalIgnoreCase)).Key ?? sourceName;
        if (reuseSavedOwner && _loadedModDocument != null && ownerName.Equals(originalName, StringComparison.OrdinalIgnoreCase) &&
            MajorGodCatalog.Find(_loadedModDocument, originalName!) is { } saved)
            foreach (var id in MajorGodCatalog.CollectOwnedStringIds(saved)) existing.Remove(id);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in changes.Select(change => change.OldId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_pendingStringUpdates.TryGetValue(id, out var pending) && (existing.ContainsKey(id) || !pending.Equals(id, StringComparison.Ordinal))) values[id] = pending;
            else if (_resolveStringAsync != null && await _resolveStringAsync(id) is { } resolved && !resolved.Equals(id, StringComparison.Ordinal)) values[id] = resolved;
            else if (existing.TryGetValue(id, out var value)) values[id] = value;
        }
        var plan = OwnedStringLifecyclePolicy.Prepare(changes, [], owned, existing, values, owned, new HashSet<string>());
        OwnedStringSavePreparation.KeepOnlyApprovedRenames(before, candidate, changes, plan);
        if (!ReferenceEquals(entry, _currentEntry) || generation != _populateGeneration ||
            (entry != null && !MajorGodCatalog.GetName(entry).Equals(entryName, StringComparison.Ordinal))) return plan;
        foreach (var id in changes.Select(change => change.OldId).Where(id => !values.ContainsKey(id)))
            if (_pendingStringUpdates.TryGetValue(id, out var fallback) && fallback.Equals(id, StringComparison.Ordinal))
            { _pendingStringUpdates.Remove(id); _pendingStringOwners.Remove(id); }
        return plan;
    }

    private MajorGodDefinition? FindMajorGodStringNamespaceCollision(
        string candidateName,
        string? excludedMajorGodName = null)
    {
        var definitions = _baseEntries.Select(pair =>
                new MajorGodDefinition(pair.Key, true, pair.Value))
            .Concat(_modDocument == null
                ? []
                : MajorGodCatalog.ExtractDefinitions(_modDocument, false));
        return MajorGodCatalog.FindGeneratedStringNamespaceCollision(
            candidateName,
            definitions,
            excludedMajorGodName);
    }

    private void ScheduleOwnedStringRemoval(string stringId, string ownerName)
    {
        if (string.IsNullOrWhiteSpace(stringId))
            return;
        _pendingStringUpdates.Remove(stringId);
        _pendingStringOwners.Remove(stringId);
        _pendingStringRemovals.Add(stringId);
        _pendingStringRemovalOwners[stringId] = ownerName;
    }

    private void ScheduleOwnedStringRemovals(IEnumerable<string> stringIds, string ownerName)
    {
        foreach (var stringId in stringIds)
            ScheduleOwnedStringRemoval(stringId, ownerName);
    }

    private void PruneUnreferencedPendingStringUpdates(string ownerName, XElement currentEntry)
    {
        var referencedIds = currentEntry.DescendantsAndSelf()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                .Append(element.HasElements ? "" : element.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _pendingStringOwners.Where(pair =>
                     pair.Value.Equals(ownerName, StringComparison.OrdinalIgnoreCase) &&
                     !referencedIds.Contains(pair.Key)).Select(pair => pair.Key).ToList())
        {
            _pendingStringOwners.Remove(id);
            _pendingStringUpdates.Remove(id);
        }
    }

    private void AddOrCollapsePendingRename(string oldName, string newName)
    {
        var chainedIndex = _pendingRenames.FindIndex(rename =>
            rename.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (chainedIndex >= 0)
        {
            var originalName = _pendingRenames[chainedIndex].Key;
            if (originalName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                _pendingRenames.RemoveAt(chainedIndex);
            else
                _pendingRenames[chainedIndex] = new KeyValuePair<string, string>(originalName, newName);
            return;
        }

        _pendingRenames.Add(new KeyValuePair<string, string>(oldName, newName));
    }

    public async Task AddMajorGodAsync(bool duplicateSelected = false)
    {
        if (duplicateSelected && CurrentMajorGodName?.Equals(
                MajorGodCatalog.SharedDataDisplayName, StringComparison.OrdinalIgnoreCase) == true)
            return;

        if (_modDocument?.Root == null)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before adding a major god.").ShowDialog(owner);
            return;
        }
        if (!await CommitCurrentMajorGodAsync())
            return;

        var sourceEntry = duplicateSelected ? _currentEntry : null;
        var existingNames = new HashSet<string>(
            _baseEntries.Keys.Concat(GetMajorGodNames(true)),
            StringComparer.OrdinalIgnoreCase);
        var baseName = duplicateSelected && !string.IsNullOrWhiteSpace(CurrentMajorGodName)
            ? CurrentMajorGodName + "Copy"
            : "NewMajorGod";
        var name = baseName;
        for (var suffix = 2;
             existingNames.Contains(name) || FindMajorGodStringNamespaceCollision(name) != null;
             suffix++)
            name = baseName + suffix;

        XElement entry;
        if (sourceEntry != null)
        {
            entry = new XElement(sourceEntry);
            MajorGodCatalog.SetPass1Value(entry, "name", name);
            MajorGodCatalog.EnsureStatsId(entry);
            foreach (var tag in ResolvedStringTags)
            {
                var oldId = ReadValue(entry, tag);
                var newId = MajorGodCatalog.BuildStringId(name, tag);
                MajorGodCatalog.SetPass1Value(entry, tag, newId);
                var text = _pendingStringUpdates.GetValueOrDefault(oldId);
                if (text == null && _resolveStringAsync != null && oldId.Length > 0)
                    text = await _resolveStringAsync(oldId);
                _pendingStringUpdates[newId] = text ?? "";
                _pendingStringOwners[newId] = name;
            }
        }
        else
        {
            entry = MajorGodCatalog.CreatePass1Entry(name);
        }

        _modDocument.Root.Add(entry);
        MajorGodCatalog.NormalizeEditorFieldOrder(entry);
        IsModifiedMode = true;
        _dirtyNames.Add(name);
        SetDirty(true);
        SelectMajorGod(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteCurrentMajorGodAsync()
        => ImmediateDeletionRequestedAsync?.Invoke() ?? DeleteCurrentMajorGodCoreAsync();

    internal async Task DeleteAndSaveCurrentMajorGodAsync(Window owner)
    {
        if (_currentEntry == null || CurrentMajorGodName == null || _modDocument == null || _modPath == null)
            return;
        var name = CurrentMajorGodName;
        var saved = new XElement(_currentEntry);
        var documentBackup = new XDocument(_modDocument);
        var candidate = new XDocument(_modDocument);
        MajorGodCatalog.Find(candidate, name)?.Remove();
        _immediateDeletionChecks = PrepareDeletionChecks?.Invoke([name],
            new Dictionary<string, XDocument> { [Path.GetFullPath(_modPath)] = candidate }) ?? [];
        _immediateDeletionOwner = owner;
        var removed = false;
        var committed = false;
        try
        {
            await DeleteCurrentMajorGodCoreAsync();
            removed = MajorGodCatalog.Find(_modDocument, name) == null;
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
                DiscardMajorGodChanges(name, saved);
                SelectMajorGod(name);
            }
        }
        if (committed)
        {
            ShowEmpty();
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task DeleteCurrentMajorGodCoreAsync()
    {
        if (!IsModifiedMode || _currentEntry == null || string.IsNullOrWhiteSpace(CurrentMajorGodName))
            return;
        var currentName = CurrentMajorGodName;
        var referenceName = _pendingRenames.LastOrDefault(rename =>
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase)).Key;
        if (string.IsNullOrWhiteSpace(referenceName))
            referenceName = currentName;
        var deletionOwner = _immediateDeletionOwner ?? TopLevel.GetTopLevel(this) as Window;
        if (_ensureCanDeleteAsync != null && deletionOwner != null &&
            !await _ensureCanDeleteAsync(deletionOwner, referenceName))
            return;
        if (deletionOwner != null)
        {
            var prompt = new Prompt(PromptType.Confirm, "Delete major god?",
                _immediateDeletionChecks == null ? $"Delete '{CurrentMajorGodName}' from major_gods_mods.xml?" :
                $"Permanently delete '{CurrentMajorGodName}' and save the deletion now?",
                confirmButtonText: "Delete major god", actionRole: PromptActionRole.Destructive).FitToContent(520, 200);
            await prompt.ShowDialog(deletionOwner);
            if (!prompt.Confirmed)
                return;
        }
        ScheduleOwnedStringRemovals(MajorGodCatalog.CollectOwnedStringIds(_currentEntry), currentName);
        _dirtyNames.Add(CurrentMajorGodName);
        _pendingRenames.RemoveAll(rename =>
            rename.Key.Equals(referenceName, StringComparison.OrdinalIgnoreCase) ||
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase));
        _currentEntry.Remove();
        ForgetMajorGodXmlDraft(currentName);
        _currentEntry = null;
        CurrentMajorGodName = null;
        _loadedEntryName = null;
        SetDirty(true);
        if (_immediateDeletionChecks != null) return;
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public XElement? LoadSavedMajorGodElement(string name)
    {
        if (string.IsNullOrWhiteSpace(_modPath) || !File.Exists(_modPath))
            return null;
        try
        {
            var saved = MajorGodCatalog.Find(MajorGodCatalog.LoadOrCreateModDocument(_modPath), name);
            return saved == null ? null : new XElement(saved);
        }
        catch { return null; }
    }

    public bool IsMajorGodDirty(string name)
        => _dirtyNames.Contains(name) ||
           _majorGodXmlDrafts.GetValueOrDefault(name)?.Session.IsDirty == true ||
           (_modDocument != null && ReferenceEquals(_currentEntry, MajorGodCatalog.Find(_modDocument, name)) && _rawEditorDirty);

    public void DiscardMajorGodChanges(string name, XElement? savedElement)
    {
        var current = MajorGodCatalog.Find(_modDocument!, name);
        var restored = savedElement == null ? null : new XElement(savedElement);
        var restoredName = restored == null ? "" : MajorGodCatalog.GetName(restored);
        var isSelected = string.Equals(CurrentMajorGodName, name, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(_loadedEntryName, name, StringComparison.OrdinalIgnoreCase);
        // The name textbox edits XML before commit adopts the new identity.
        if (current == null && isSelected) current = _currentEntry;
        _pendingRenames.RemoveAll(rename =>
            rename.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            rename.Value.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            restoredName.Length > 0 &&
            (rename.Key.Equals(restoredName, StringComparison.OrdinalIgnoreCase) ||
             rename.Value.Equals(restoredName, StringComparison.OrdinalIgnoreCase)));
        if (current != null && restored != null)
            current.ReplaceWith(restored);
        else if (current != null)
            current.Remove();
        else if (restored != null)
            _modDocument!.Root!.Add(restored);

        var discardedOwnerNames = new[] { name, restoredName }
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _pendingStringOwners.Where(pair => discardedOwnerNames.Contains(pair.Value)).Select(pair => pair.Key).ToList())
        {
            _pendingStringOwners.Remove(id);
            _pendingStringUpdates.Remove(id);
        }
        foreach (var id in _pendingStringRemovalOwners.Where(pair => discardedOwnerNames.Contains(pair.Value)).Select(pair => pair.Key).ToList())
        {
            _pendingStringRemovalOwners.Remove(id);
            _pendingStringRemovals.Remove(id);
        }
        _dirtyNames.ExceptWith(discardedOwnerNames);
        foreach (var discarded in discardedOwnerNames) ForgetMajorGodXmlDraft(discarded);
        if (isSelected)
        {
            _rawEditorDirty = false;
            SelectMajorGod(restored == null ? null : MajorGodCatalog.GetName(restored));
        }
        SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0 || _rawEditorDirty);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> SaveAsync()
    {
        await _saveGate.WaitAsync();
        using var feedback = _saveFeedback.BeginSave();
        try
        {
            var saved = await SaveCoreAsync();
            if (saved) feedback.Complete(IsDirty);
            else SaveFeedbackReported?.Invoke(EditorSaveOutcome.Blocked);
            return saved;
        }
        finally
        {
            _saveGate.Release();
            feedback.Dispose();
            if (SaveFeedbackReported != null) _statusMessage.Text = "";
        }
    }

    internal XDocument? CreateModifiedDocumentSnapshot()
        => CreateMajorGodXmlSnapshot();

    private async Task<bool> SaveCoreAsync()
    {
        if (!await PrepareMajorGodXmlForSaveAsync()) return false;
        if (!await CommitCurrentMajorGodAsync())
            return false;
        if (!IsDirty)
        {
            AcceptMajorGodXmlSaves(CapturePreparedMajorGodXmlSaves());
            return true;
        }
        if (_modDocument == null || string.IsNullOrWhiteSpace(_modPath))
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before saving major gods.").ShowDialog(owner);
            return false;
        }

        if (_immediateDeletionChecks == null && !await ReloadExternallyChangedDocumentAsync())
            return false;

        try
        {
            var xmlSaves = CapturePreparedMajorGodXmlSaves();
            var rawNames = xmlSaves.Select(saved => saved.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _dirtyNames.Where(name => !rawNames.Contains(name)))
                if (MajorGodCatalog.Find(_modDocument, name) is { } edited)
                    MajorGodCatalog.NormalizeEditorFieldOrder(edited);
            var renamesToSave = _pendingRenames.ToList();
            ApplyMajorGodOwnerRenames(_modDocument, renamesToSave);
            var referenceMigration = renamesToSave.Count > 0
                ? _prepareReferenceMigration?.Invoke(renamesToSave)
                : null;
            var stringUpdates = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
            var originalOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _loadedModDocument?.Root?.Elements(MajorGodCatalog.EntryName) ?? [])
                if (_dirtyNames.Contains(MajorGodCatalog.GetName(entry)) || MajorGodCatalog.Find(_modDocument, MajorGodCatalog.GetName(entry)) == null ||
                    renamesToSave.Any(rename => rename.Key.Equals(MajorGodCatalog.GetName(entry), StringComparison.OrdinalIgnoreCase)))
                    originalOwned.UnionWith(MajorGodCatalog.CollectOwnedStringIds(entry));
            var currentOwned = (_modDocument.Root?.Elements(MajorGodCatalog.EntryName) ?? [])
                .Where(entry => _dirtyNames.Contains(MajorGodCatalog.GetName(entry)))
                .SelectMany(MajorGodCatalog.CollectOwnedStringIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var proposed = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase) { [Path.GetFullPath(_modPath)] = _modDocument };
            foreach (var path in referenceMigration?.Paths ?? [])
                if (referenceMigration!.GetDocument(path) is { } document) proposed[Path.GetFullPath(path)] = document;
            var cleanup = OwnedStringSavePreparation.Prepare(_modPath, _modStringsPath, proposed, originalOwned,
                currentOwned, stringUpdates, _pendingStringRemovals, PrepareStringCleanup);
            stringUpdates = new Dictionary<string, string>(cleanup.Updates, StringComparer.OrdinalIgnoreCase);
            var stringRemovals = cleanup.Plan.StringRemovals;
            var writesStrings = cleanup.WritesStrings;
            if (writesStrings && (_saveStringsAsync == null || string.IsNullOrWhiteSpace(_modStringsPath)))
                throw new InvalidOperationException("Major God strings cannot be saved because the active mod string file is unavailable.");
            if (stringUpdates.Count > 0)
                ValidatePendingGeneratedStringClaims();

            var expectedStates = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
            if (_loadedFileState != null)
                expectedStates[Path.GetFullPath(_modPath)] = _loadedFileState;
            if (writesStrings && _loadedStringsFileState != null)
                expectedStates[Path.GetFullPath(_modStringsPath!)] = _loadedStringsFileState;
            if (referenceMigration != null)
            {
                foreach (var state in referenceMigration.ExpectedStates)
                    expectedStates[state.Key] = state.Value;
            }

            var preparedMajorGods = new PreparedModXmlDocument(
                Path.GetFullPath(_modPath),
                ModXmlDocumentKind.MajorGods,
                _modDocument,
                _loadedFileState ?? FileIntegrityTransaction.CaptureState(_modPath));
            expectedStates[Path.GetFullPath(_modPath)] = preparedMajorGods.LoadedState;
            cleanup.Inspection.MergeExpectedStatesInto(expectedStates);
            var deletedNames = ModDeletionSafety.FindDeletedNames(
                (_loadedModDocument?.Root?.Elements().Select(MajorGodCatalog.GetName) ?? []).Concat(_dirtyNames),
                _modDocument.Root?.Elements().Select(MajorGodCatalog.GetName) ?? [], renamesToSave);
            var deletionChecks = PrepareDeletionChecks?.Invoke(deletedNames,
                new Dictionary<string, XDocument> { [Path.GetFullPath(_modPath)] = _modDocument }) ?? [];
            await ModDocumentPersistence.ExecuteAsync(
                [preparedMajorGods],
                (writesStrings ? new[] { _modStringsPath } : [])
                    .Concat(referenceMigration?.Paths ?? []),
                expectedStates,
                async () =>
                {
                    cleanup.Inspection.ValidateReadDependencies();
                    referenceMigration?.ValidateDiscovery();
                    if (_immediateDeletionChecks != null)
                        foreach (var inspection in _immediateDeletionChecks) inspection.ValidateReadDependencies();
                    foreach (var inspection in deletionChecks) inspection.ValidateReadDependencies();
                    ProtoEditorWindow.SaveAbilityXmlDocument(_modDocument, _modPath);
                    referenceMigration?.Save((document, path) =>
                        ProtoEditorWindow.SaveAbilityXmlDocument(document, path));
                    // Host caches are adopted by the Saved event after the complete transaction.
                    if (writesStrings)
                        await _saveStringsAsync!(stringUpdates, stringRemovals);
                });

            if (referenceMigration != null)
                _adoptReferenceMigration?.Invoke(referenceMigration);

            _loadedFileState = FileIntegrityTransaction.CaptureState(_modPath);
            _loadedModDocument = new XDocument(_modDocument);
            if (writesStrings)
                _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath!);
            _pendingStringUpdates.Clear();
            _pendingStringOwners.Clear();
            _pendingStringRemovals.Clear();
            _pendingStringRemovalOwners.Clear();
            _dirtyNames.Clear();
            foreach (var rename in renamesToSave)
                _pendingRenames.Remove(rename);
            AcceptMajorGodXmlSaves(xmlSaves);
            ReportSaveFeedback(EditorSaveOutcome.Saved);
            LastSaveWroteStrings = writesStrings;
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ReportSaveFeedback(EditorSaveOutcome.Failed);
            if ((_immediateDeletionOwner ?? TopLevel.GetTopLevel(this)) is Window owner)
                await new Prompt(PromptType.Error, "Major God save failed", ex.Message).ShowDialog(owner);
            return false;
        }
    }

    private bool IsStringIdReferencedByAnyMajorGod(string id)
        => _modDocument?.Descendants().Any(element =>
            !element.HasElements && element.Value.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)) == true;

    private void ValidatePendingGeneratedStringClaims()
    {
        if (_modDocument == null || _loadedModDocument == null ||
            string.IsNullOrWhiteSpace(_modStringsPath) || !File.Exists(_modStringsPath))
            return;

        var existingStrings = StringTableParser.Parse(
            PreservedTextEncoding.Decode(File.ReadAllBytes(_modStringsPath)).Text);
        foreach (var (stringId, ownerName) in _pendingStringOwners)
        {
            if (!existingStrings.ContainsKey(stringId))
                continue;
            var current = MajorGodCatalog.Find(_modDocument, ownerName);
            if (current == null || !MajorGodCatalog.CollectOwnedStringIds(current).Contains(stringId))
                continue;

            var originalName = _pendingRenames.LastOrDefault(rename =>
                rename.Value.Equals(ownerName, StringComparison.OrdinalIgnoreCase)).Key;
            if (string.IsNullOrWhiteSpace(originalName))
                originalName = ownerName;
            var loaded = MajorGodCatalog.Find(_loadedModDocument, originalName);
            if (loaded != null && MajorGodCatalog.CollectOwnedStringIds(loaded).Contains(stringId))
                continue;

            throw new InvalidOperationException(
                $"String ID '{stringId}' already exists and is not owned by Major God '{ownerName}'. Choose another name.");
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
            _statusMessage.Text = "major_gods_mods.xml changed outside the editor. Reload it before saving.";
            return false;
        }

        var prompt = new Prompt(
            PromptType.Confirm,
            "Major Gods changed outside the editor",
            "major_gods_mods.xml changed after the editor loaded. Reload and merge it with unsaved Major God edits?\n\n" +
            "If the same Major God changed both externally and in the editor, Reload uses the external version. Other unsaved edits are kept. Saving is paused for review.",
            confirmButtonText: "Reload")
        {
            Width = 630,
            Height = 260,
            MinWidth = 560,
            MinHeight = 225
        };
        await prompt.ShowDialog(owner);
        if (!prompt.Confirmed)
        {
            ReportSaveFeedback(EditorSaveOutcome.Cancelled);
            return false;
        }

        try
        {
            var prepared = ModDocumentPersistence.Prepare(_modPath, ModXmlDocumentKind.MajorGods);
            var externalDocument = prepared.Document;
            var stateAfterRead = prepared.LoadedState;

            var merge = NamedXmlExternalDocumentMergePolicy.Merge(
                _loadedModDocument,
                _modDocument!,
                externalDocument,
                MajorGodCatalog.EntryName,
                MajorGodCatalog.GetName);
            var renamedConflict = _pendingRenames.FirstOrDefault(rename =>
                merge.ReplacedEditorChanges.Contains(rename.Key, StringComparer.OrdinalIgnoreCase) ||
                merge.ReplacedEditorChanges.Contains(rename.Value, StringComparer.OrdinalIgnoreCase));
            if (!renamedConflict.Equals(default(KeyValuePair<string, string>)))
            {
                await new Prompt(
                    PromptType.Error,
                    "Unable to merge Major God rename",
                    $"'{renamedConflict.Key}' was renamed in the editor while that Major God also changed externally. No files were overwritten. Reopen the editor to use the external version.")
                    .ShowDialog(owner);
                return false;
            }
            foreach (var conflict in merge.ReplacedEditorChanges)
                _dirtyNames.Remove(conflict);

            var selectedName = CurrentMajorGodName;
            _modDocument = merge.Document;
            _loadedModDocument = new XDocument(externalDocument);
            _loadedFileState = stateAfterRead;
            ReloadMajorGodXmlDrafts(merge.ReplacedEditorChanges);
            ReloadChangedCleanMajorGodXmlDrafts();
            SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0);
            if (!string.IsNullOrWhiteSpace(selectedName) && MajorGodCatalog.Find(_modDocument, selectedName) != null)
                SelectMajorGod(selectedName);
            else
            {
                CurrentMajorGodName = null;
                _currentEntry = null;
                ShowEmpty();
            }
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            ReportSaveFeedback(EditorSaveOutcome.ReviewRequired);
            return false;
        }
        catch (Exception exception)
        {
            await new Prompt(
                PromptType.Error,
                "Unable to reload Major Gods",
                "No files were written. " + exception.Message).ShowDialog(owner);
            return false;
        }
    }

    private void NameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent()) return;
        SetEntryValue("name", _nameBox.Text ?? "");
    }

    private async void NameBox_LostFocus(object? sender, RoutedEventArgs e)
        => await CaptureCurrentMajorGodDraftAsync();

    private void KeyBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulating || !IsModifiedMode)
            return;
        var raw = _keyBox.Text ?? "";
        var normalized = raw.Length == 0 || !char.IsLetter(raw[0])
            ? ""
            : char.ToUpperInvariant(raw[0]).ToString();
        if (!raw.Equals(normalized, StringComparison.Ordinal))
        {
            _isPopulating = true;
            _keyBox.Text = normalized;
            _isPopulating = false;
        }
        if (_currentEntry != null)
            SetEntryValue("key", normalized);
    }

    private void CultureBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent()) return;
        SetEntryValue("culture", _cultureBox.Text ?? "");
    }

    private void ResolvedStringBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent() || sender is not TextBox { Tag: string tag } box)
            return;
        var text = box.Text ?? "";
        if (_resolvedStringTexts.TryGetValue(tag, out var previousText) &&
            previousText.Equals(text, StringComparison.Ordinal))
            return;
        _resolvedStringTexts[tag] = text;
        var id = _stringIds.GetValueOrDefault(tag) ?? "";
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            var owned = MajorGodCatalog.CollectOwnedStringIds(_currentEntry!);
            var field = MajorGodCatalog.FindDirectChild(_currentEntry!, tag);
            if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            _stringIds.Remove(tag);
            if (owned.Contains(id) && !MajorGodCatalog.CollectOwnedStringIds(_currentEntry!).Contains(id))
                ScheduleOwnedStringRemoval(id, CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!));
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            return;
        }
        if (string.IsNullOrWhiteSpace(id))
        {
            id = MajorGodCatalog.BuildStringId(MajorGodCatalog.GetName(_currentEntry!), tag);
            _stringIds[tag] = id;
            MajorGodCatalog.SetPass1Value(_currentEntry!, tag, id);
            RefreshRawXmlFromCurrent();
        }
        _pendingStringRemovals.Remove(id);
        _pendingStringRemovalOwners.Remove(id);
        _pendingStringUpdates[id] = box.Text ?? "";
        _pendingStringOwners[id] = CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!);
        MarkCurrentDirty();
    }

    private void CatalogValueBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent() || sender is not AutoCompleteBox { Tag: string tag } box) return;
        SetEntryValue(tag, box.Text ?? "");
    }

    private void AssociatedTechBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent() || sender is not AutoCompleteBox { Tag: string tag } box)
            return;
        var existing = MajorGodCatalog.FindDirectChild(_currentEntry!, tag);
        var existingValue = tag.Equals("agetech", StringComparison.OrdinalIgnoreCase)
            ? existing == null ? "" : ReadValue(existing, "tech") : existing?.Value.Trim() ?? "";
        if (existingValue.Equals((box.Text ?? "").Trim(), StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(box.Text))
        {
            var field = MajorGodCatalog.FindDirectChild(_currentEntry!, tag);
            if (field != null) XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(field);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            return;
        }
        if (tag.Equals("agetech", StringComparison.OrdinalIgnoreCase))
        {
            var ageTech = MajorGodCatalog.FindDirectChild(_currentEntry!, "agetech");
            var age = ageTech?.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("age", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "ArchaicAge";
            MajorGodCatalog.SetAgeTech(_currentEntry!, age, box.Text ?? "");
        }
        else
        {
            MajorGodCatalog.SetAssociatedTech(_currentEntry!, tag, box.Text ?? "");
        }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void AddTeamTech_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditCurrent())
            return;
        MajorGodCatalog.SetAssociatedTech(_currentEntry!, "teamtech", "");
        _isPopulating = true;
        try { _teamTechBox.Text = ""; }
        finally { _isPopulating = false; }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshTeamTechVisibility();
        _teamTechBox.Focus();
    }

    private void RemoveTeamTech_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditCurrent())
            return;
        MajorGodCatalog.FindDirectChild(_currentEntry!, "teamtech")?.Remove();
        _isPopulating = true;
        try { _teamTechBox.Text = ""; }
        finally { _isPopulating = false; }
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RefreshTeamTechVisibility();
    }

    private void RefreshTeamTechVisibility()
    {
        var hasTeamTech = _currentEntry != null &&
                          MajorGodCatalog.FindDirectChild(_currentEntry, "teamtech") != null;
        _teamTechRow.IsVisible = hasTeamTech;
        _addTeamTechButton.IsVisible = IsModifiedMode && _currentEntry != null && !hasTeamTech;
    }

    private void RebuildEconomicEditor()
    {
        _economicEditorGeneration++;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _economicPanel.Children.Clear();
            if (_currentEntry == null)
                return;

            var efficiencyRow = new WrapPanel();
            efficiencyRow.Children.Add(CreateInlineLabel("Second Builder Efficiency"));
            efficiencyRow.Children.Add(CreateEconomicValueBox("buildingefficiency"));
            efficiencyRow.Children.Add(CreateInlineLabel("Extra Builder Efficiency"));
            efficiencyRow.Children.Add(CreateEconomicValueBox("extrabuildingefficiency"));

            var repairFactor = MajorGodCatalog.FindDirectChild(_currentEntry, "buildingrepairfactor");
            if (repairFactor == null)
            {
                if (IsModifiedMode)
                {
                    var addRepair = CreateEconomicOptionalButton("Repair Factor");
                    addRepair.Click += (_, _) =>
                    {
                        if (!CanEditCurrent()) return;
                        MajorGodCatalog.SetEconomicValue(_currentEntry!, "buildingrepairfactor", "0");
                        MarkCurrentDirty();
                        RefreshRawXmlFromCurrent();
                        RebuildEconomicEditor();
                    };
                    efficiencyRow.Children.Add(addRepair);
                }
            }
            else
            {
                efficiencyRow.Children.Add(CreateInlineLabel("Repair Factor"));
                efficiencyRow.Children.Add(CreateEconomicValueBox("buildingrepairfactor"));
                if (IsModifiedMode)
                    efficiencyRow.Children.Add(CreateEconomicRemoveButton(repairFactor));
            }
            _economicPanel.Children.Add(efficiencyRow);

            var secondaryPrayer = MajorGodCatalog.FindDirectChild(_currentEntry, "secondaryprayerefficiency");
            if (secondaryPrayer == null)
            {
                if (IsModifiedMode)
                {
                    var addSecondary = CreateEconomicOptionalButton("Secondary Prayer Efficiency");
                    addSecondary.Click += (_, _) =>
                    {
                        if (!CanEditCurrent()) return;
                        MajorGodCatalog.AddSecondaryPrayerEfficiency(_currentEntry!);
                        MarkCurrentDirty();
                        RefreshRawXmlFromCurrent();
                        RebuildEconomicEditor();
                    };
                    _economicPanel.Children.Add(addSecondary);
                }
                return;
            }

            var prayerRow = new WrapPanel();
            prayerRow.Children.Add(CreateInlineLabel("Secondary Prayer Efficiency"));
            prayerRow.Children.Add(CreateInlineLabel("Later Growth"));
            prayerRow.Children.Add(CreateEconomicAttributeBox(secondaryPrayer, "g"));
            prayerRow.Children.Add(CreateInlineLabel("Early Income Reduction"));
            prayerRow.Children.Add(CreateEconomicAttributeBox(secondaryPrayer, "v"));
            prayerRow.Children.Add(CreateInlineLabel("Modifier"));
            prayerRow.Children.Add(CreateEconomicAttributeBox(secondaryPrayer, "z"));
            if (IsModifiedMode)
                prayerRow.Children.Add(CreateEconomicRemoveButton(secondaryPrayer));
            _economicPanel.Children.Add(prayerRow);
        }
        finally
        {
            FinalizeMajorGodPresentation();
            _isPopulating = wasPopulating;
        }
    }

    private TextBox CreateEconomicValueBox(string tag)
    {
        var entry = _currentEntry!;
        var generation = _economicEditorGeneration;
        var box = CreateEconomicNumberBox(ReadValue(entry, tag));
        EditorValidationBindings.Bind(box, entry, tag);
        var previousText = box.Text;
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent() || !ReferenceEquals(entry, _currentEntry) || generation != _economicEditorGeneration ||
                string.Equals(previousText, box.Text, StringComparison.Ordinal)) return;
            previousText = box.Text;
            if (MajorGodCatalog.FindDirectChild(entry, tag) == null && string.IsNullOrEmpty(box.Text))
                return;
            MajorGodCatalog.SetEconomicValue(entry, tag, box.Text ?? "");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private TextBox CreateEconomicAttributeBox(XElement element, string attributeName)
    {
        var entry = _currentEntry;
        var generation = _economicEditorGeneration;
        var box = CreateEconomicNumberBox(FindAttribute(element, attributeName)?.Value ?? "");
        EditorValidationBindings.Bind(box, element, "@" + attributeName);
        var previousText = box.Text;
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent() || !ReferenceEquals(entry, _currentEntry) || generation != _economicEditorGeneration ||
                string.Equals(previousText, box.Text, StringComparison.Ordinal)) return;
            previousText = box.Text;
            SetAttribute(element, attributeName, box.Text ?? "");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private TextBox CreateEconomicNumberBox(string value)
    {
        var box = ConfigureMajorGodField(new TextBox
        {
            Text = value,
            Width = 70,
            MaxWidth = 70,
            IsEnabled = IsModifiedMode,
            HorizontalAlignment = HorizontalAlignment.Left
        }, EditorFieldWidthRole.Numeric);
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);
        return box;
    }

    private static Button CreateEconomicOptionalButton(string label)
        => new()
        {
            Content = label,
            Classes = { "add-component" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 3)
        };

    private Button CreateEconomicRemoveButton(XElement element)
    {
        var button = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            MajorGodCatalog.RemoveElementAndAdjacentWhitespace(element);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RebuildEconomicEditor();
        };
        return button;
    }

    private static void ValidateEconomicAttributes(XElement entry)
    {
        ValidateNonNegativeFloat(entry, "buildingefficiency", "Second Builder Efficiency");
        ValidateNonNegativeFloat(entry, "extrabuildingefficiency", "Extra Builder Efficiency");
        ValidateNonNegativeFloat(entry, "buildingrepairfactor", "Repair Factor");

        var secondaryPrayer = MajorGodCatalog.FindDirectChild(entry, "secondaryprayerefficiency");
        if (secondaryPrayer == null)
            return;
        ValidateNonNegativeFloatAttribute(secondaryPrayer, "g", "Secondary Prayer Later Growth");
        ValidateNonNegativeFloatAttribute(secondaryPrayer, "v", "Secondary Prayer Early Income Reduction");
        ValidateNonNegativeFloatAttribute(secondaryPrayer, "z", "Secondary Prayer Modifier");
    }

    private static void ValidateCandidateStructure(XElement candidate)
    {
        var document = new XDocument(new XElement(MajorGodCatalog.ModRootName, new XElement(candidate)));
        var validation = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.MajorGods);
        if (validation.IsSafeToEdit)
            return;
        throw new InvalidDataException(string.Join(
            Environment.NewLine,
            validation.Errors.Select(error => $"{error.ObjectName}: {error.Reason}")));
    }

    private void ValidateSharedFields(XElement entry)
    {
        GodModuleFieldValidation.ValidateMajorGodSharedFields(
            entry,
            _iconPaths,
            _minimapBorderValues,
            _backgroundScenarioValues);
    }

    private static void ValidateNonNegativeFloat(XElement entry, string tag, string label)
    {
        var element = MajorGodCatalog.FindDirectChild(entry, tag);
        if (element != null)
            ValidationSourceLocation.Validate(element, null, () => ValidateNonNegativeFloatValue(element.Value, label));
    }

    private static void ValidateNonNegativeFloatAttribute(XElement element, string attributeName, string label)
    {
        var attribute = FindAttribute(element, attributeName);
        ValidationSourceLocation.Validate(element, "@" + attributeName, () => ValidateNonNegativeFloatValue(attribute?.Value ?? "", label));
    }

    private static void ValidateNonNegativeFloatValue(string raw, string label)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < 0)
        {
            throw new InvalidDataException($"{label} must be a number greater than or equal to zero.");
        }
    }

    private void RebuildSpecialMechanicsEditor()
    {
        _refreshBonusUnitSpawningEditor = null;
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _specialMechanicTabs.Children.Clear();
            _specialMechanicsPanel.Children.Clear();
            if (_currentEntry == null)
            {
                _selectedSpecialMechanicTag = null;
                _partisansCheckBox.IsChecked = false;
                _devotionCommandsCheckBox.IsChecked = false;
                _specialMechanicToggleRow.IsVisible = false;
                _specialMechanicAddRow.IsVisible = false;
                _specialMechanicSelector.ItemsSource = Array.Empty<string>();
                return;
            }


            var hasPartisans = MajorGodCatalog.FindDirectChild(_currentEntry, "partisans") != null;
            var hasDevotionCommands = MajorGodCatalog.FindDirectChild(_currentEntry, "devotioncommands") != null;
            _partisansCheckBox.IsChecked = hasPartisans;
            _devotionCommandsCheckBox.IsChecked = hasDevotionCommands;
            _partisansCheckBox.IsEnabled = IsModifiedMode;
            _devotionCommandsCheckBox.IsEnabled = IsModifiedMode;
            _partisansCheckBox.IsVisible = IsModifiedMode || hasPartisans;
            _devotionCommandsCheckBox.IsVisible = IsModifiedMode || hasDevotionCommands;
            _specialMechanicToggleRow.IsVisible = IsModifiedMode || hasPartisans || hasDevotionCommands;

            var usedTags = _currentEntry.Elements()
                .Select(element => element.Name.LocalName)
                .Where(tag => SpecialMechanics.Any(definition =>
                    definition.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var availableLabels = SpecialMechanics
                .Where(definition => !usedTags.Contains(definition.Tag))
                .Select(definition => definition.Label)
                .ToList();
            _specialMechanicSelector.ItemsSource = availableLabels;
            _specialMechanicSelector.SelectedItem = null;
            _specialMechanicSelector.Text = "";
            _specialMechanicAddRow.IsVisible = IsModifiedMode && availableLabels.Count > 0;
            _specialMechanicSelector.IsEnabled = IsModifiedMode;

            var mechanicElements = _currentEntry.Elements()
                .Where(element => usedTags.Contains(element.Name.LocalName))
                .ToList();
            if (string.IsNullOrWhiteSpace(_selectedSpecialMechanicTag) ||
                !usedTags.Contains(_selectedSpecialMechanicTag))
            {
                _selectedSpecialMechanicTag = mechanicElements.FirstOrDefault()?.Name.LocalName;
            }

            var renderedMechanics = new List<(string Tag, Button Tab, Border Card)>();
            void SelectMechanic(string tag)
            {
                _selectedSpecialMechanicTag = tag;
                foreach (var rendered in renderedMechanics)
                {
                    var selected = rendered.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase);
                    rendered.Card.IsVisible = selected;
                    rendered.Tab.Background = Brush.Parse(selected ? "#393125" : "#252526");
                }
            }

            for (var mechanicIndex = 0; mechanicIndex < mechanicElements.Count; mechanicIndex++)
            {
                var element = mechanicElements[mechanicIndex];
                var definition = SpecialMechanics.First(candidate =>
                    candidate.Tag.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                var tab = new Button
                {
                    Content = definition.Label,
                    Padding = new Thickness(8, 3),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                var card = new Border
                {
                    Classes = { "editor-card" },
                    IsVisible = definition.Tag.Equals(_selectedSpecialMechanicTag, StringComparison.OrdinalIgnoreCase)
                };
                var system = new StackPanel { Spacing = 4 };
                card.Child = system;
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var headerContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerContent.Children.Add(new TextBlock
                {
                    Text = definition.Label,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = definition.Tag.Equals("bountyresourceearning", StringComparison.OrdinalIgnoreCase) ? 0 : 200,
                    Margin = new Thickness(4, 4, 0, 4)
                });
                if (definition.Tag.Equals("bountyresourceearning", StringComparison.OrdinalIgnoreCase))
                    headerContent.Children.Add(CreateBountyActiveCheckBox(element));
                header.Children.Add(headerContent);
                if (IsModifiedMode)
                {
                    var nextSelectedTag = mechanicIndex + 1 < mechanicElements.Count
                        ? mechanicElements[mechanicIndex + 1].Name.LocalName
                        : mechanicIndex > 0
                            ? mechanicElements[mechanicIndex - 1].Name.LocalName
                            : null;
                    var remove = EditorRemoveButtons.Card(description: "Remove special attribute");
                    Grid.SetColumn(remove, 1);
                    remove.Click += (_, _) =>
                    {
                        if (!CanEditCurrent()) return;
                        if (definition.Tag.Equals("bonusunitspawning", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var goal in element.Elements().Where(IsBonusGoal))
                                ScheduleBonusDisplayStringRemoval(goal);
                        }
                        MajorGodCatalog.RemoveElementAndAdjacentWhitespace(element);
                        _selectedSpecialMechanicTag = nextSelectedTag;
                        MarkCurrentDirty();
                        RefreshRawXmlFromCurrent();
                        RebuildSpecialMechanicsEditor();
                    };
                    header.Children.Add(remove);
                }
                system.Children.Add(header);
                if (definition.Tag.Equals("shades", StringComparison.OrdinalIgnoreCase))
                    BuildShadesEditor(system, element);
                else if (definition.Tag.Equals("terraincreeps", StringComparison.OrdinalIgnoreCase))
                    BuildTerrainCreepsEditor(system, element);
                else if (definition.Tag.Equals("timeshifting", StringComparison.OrdinalIgnoreCase))
                    BuildTimeShiftingEditor(system, element);
                else if (definition.Tag.Equals("buildingchain", StringComparison.OrdinalIgnoreCase))
                    BuildBuildingChainEditor(system, element);
                else if (definition.Tag.Equals("combatxp", StringComparison.OrdinalIgnoreCase))
                    BuildCombatXpEditor(system, element);
                else if (definition.Tag.Equals("bountyresourceearning", StringComparison.OrdinalIgnoreCase))
                    BuildBountyResourceEarningEditor(system, element);
                else if (definition.Tag.Equals("bonusunitspawning", StringComparison.OrdinalIgnoreCase))
                    BuildBonusUnitSpawningEditor(system, element);
                else if (definition.Tag.Equals("oncastpowercostfactor", StringComparison.OrdinalIgnoreCase))
                    BuildOnCastPowerCostFactorEditor(system, element);
                renderedMechanics.Add((definition.Tag, tab, card));
                tab.Click += (_, _) => SelectMechanic(definition.Tag);
                _specialMechanicTabs.Children.Add(tab);
                _specialMechanicsPanel.Children.Add(card);
            }

            _specialMechanicTabs.IsVisible = renderedMechanics.Count > 0;
            if (_selectedSpecialMechanicTag != null)
                SelectMechanic(_selectedSpecialMechanicTag);
        }
        finally
        {
            FinalizeMajorGodPresentation();
            _isPopulating = wasPopulating;
        }
    }

    private void SpecialMechanicSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!CanEditCurrent() || _specialMechanicSelector.SelectedItem is not string label)
            return;
        var definition = SpecialMechanics.FirstOrDefault(candidate =>
            candidate.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(definition.Tag) ||
            MajorGodCatalog.FindDirectChild(_currentEntry!, definition.Tag) != null)
            return;

        var mechanic = MajorGodCatalog.AddSpecialMechanic(_currentEntry!, definition.Tag);
        if (definition.Tag.Equals("timeshifting", StringComparison.OrdinalIgnoreCase))
            SetAttribute(mechanic, "maxconcurrenttimeshifts", "1");
        else if (definition.Tag.Equals("terraincreeps", StringComparison.OrdinalIgnoreCase))
            MajorGodCatalog.AddTerrainCreep(mechanic);
        else if (definition.Tag.Equals("buildingchain", StringComparison.OrdinalIgnoreCase))
            MajorGodCatalog.AddBuildingChainAnchor(mechanic);
        else if (definition.Tag.Equals("combatxp", StringComparison.OrdinalIgnoreCase))
            MajorGodCatalog.AddCombatXpTier(mechanic);
        else if (definition.Tag.Equals("bountyresourceearning", StringComparison.OrdinalIgnoreCase))
            MajorGodCatalog.AddBountyElement(mechanic, "active", "true");
        else if (definition.Tag.Equals("bonusunitspawning", StringComparison.OrdinalIgnoreCase))
            MajorGodCatalog.AddBonusUnitGoal(mechanic);
        else if (definition.Tag.Equals("oncastpowercostfactor", StringComparison.OrdinalIgnoreCase))
            mechanic.Value = "0";
        _selectedSpecialMechanicTag = definition.Tag;
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildSpecialMechanicsEditor();
    }

    private void PartisansCheckBox_Changed(object? sender, RoutedEventArgs e)
        => SetSpecialMechanicToggle("partisans", _partisansCheckBox.IsChecked == true, "1");

    private void DevotionCommandsCheckBox_Changed(object? sender, RoutedEventArgs e)
        => SetSpecialMechanicToggle("devotioncommands", _devotionCommandsCheckBox.IsChecked == true);

    private void SetSpecialMechanicToggle(string tag, bool enabled, string value = "")
    {
        if (!CanEditCurrent())
            return;
        MajorGodCatalog.SetSpecialMechanicToggle(_currentEntry!, tag, enabled, value);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildSpecialMechanicsEditor();
    }

    private void ValidateSpecialMechanics(XElement entry)
    {
        var definitions = SpecialMechanics.Concat(
            new (string Label, string Tag)[]
            {
                ("Partisans", "partisans"),
                ("DevotionCommands", "devotioncommands")
            });
        foreach (var definition in definitions)
        {
            if (entry.Elements().Count(element =>
                    element.Name.LocalName.Equals(definition.Tag, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                throw new InvalidDataException($"{definition.Label} can only occur once.");
            }
        }

        if (MajorGodCatalog.FindDirectChild(entry, "shades") is { } shades)
            ValidateShades(shades);
        if (MajorGodCatalog.FindDirectChild(entry, "terraincreeps") is { } terrainCreeps)
            ValidateTerrainCreeps(terrainCreeps);
        if (MajorGodCatalog.FindDirectChild(entry, "timeshifting") is { } timeShifting)
            ValidateTimeShifting(timeShifting);
        if (MajorGodCatalog.FindDirectChild(entry, "buildingchain") is { } buildingChain)
            ValidateBuildingChain(buildingChain);
        if (MajorGodCatalog.FindDirectChild(entry, "combatxp") is { } combatXp)
            ValidateCombatXp(combatXp);
        if (MajorGodCatalog.FindDirectChild(entry, "bountyresourceearning") is { } bounty)
            ValidateBountyResourceEarning(bounty);
        if (MajorGodCatalog.FindDirectChild(entry, "bonusunitspawning") is { } bonusUnitSpawning)
            ValidateBonusUnitSpawning(bonusUnitSpawning);
        if (MajorGodCatalog.FindDirectChild(entry, "oncastpowercostfactor") is { } onCastPowerCostFactor)
            ValidationSourceLocation.Validate(onCastPowerCostFactor, () => ValidateBoundedFloatValue(onCastPowerCostFactor.Value, "Power Cost Reduction Factor on Cast", 0, 1));
    }

    private void BuildShadesEditor(Panel host, XElement shades)
    {
        var row = new WrapPanel();
        var identityFields = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        identityFields.Children.Add(CreateInlineLabel("Unit"));
        identityFields.Children.Add(EditorValidationBindings.Bind(CreateSpecialProtoUnitBox(GetOrCreateSpecialChild(shades, "protounit")), shades, "protounit"));
        identityFields.Children.Add(CreateInlineLabel("From"));

        var numericFields = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        numericFields.Children.Add(CreateInlineLabel("Probability"));
        numericFields.Children.Add(CreateSpecialElementNumberBox(GetOrCreateSpecialChild(shades, "chance"), "0", ProtoUnitNumericKind.ClampZeroToOne));
        numericFields.Children.Add(CreateInlineLabel("Delay"));
        numericFields.Children.Add(CreateSpecialElementNumberBox(GetOrCreateSpecialChild(shades, "delay"), "0", ProtoUnitNumericKind.UnsignedFloat));

        var maxQueued = MajorGodCatalog.FindDirectChild(shades, "maxqueued");
        if (maxQueued == null)
        {
            if (IsModifiedMode)
            {
                var add = CreateAddButton("Max Queue");
                add.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    MajorGodCatalog.AddShadesChild(shades, "maxqueued", "1");
                    MarkAndRebuildSpecialMechanics();
                };
                numericFields.Children.Add(add);
            }
        }
        else
        {
            numericFields.Children.Add(CreateInlineLabel("Max Queue"));
            numericFields.Children.Add(CreateSpecialElementNumberBox(maxQueued, "1", ProtoUnitNumericKind.PositiveInteger));
            if (IsModifiedMode)
                numericFields.Children.Add(CreateSpecialChildRemoveButton(maxQueued));
        }

        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox
            {
                Width = 50,
                MaxWidth = 50,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                HorizontalAlignment = HorizontalAlignment.Left
            }, EditorFieldWidthRole.ChipSearch);
            EditorValidationBindings.Bind(picker, shades, "targetunit", repeated: true);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            identityFields.Children.Add(picker);
        }
        row.Children.Add(identityFields);

        var chips = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(chips);
        row.Children.Add(numericFields);

        void RenderTargets()
        {
            var targets = shades.Elements().Where(child => child.Name.LocalName.Equals("targetunit", StringComparison.OrdinalIgnoreCase)).ToList();
            chips.Children.Clear();
            foreach (var target in targets)
            {
                var captured = target;
                chips.Children.Add(EditorChipService.CreateBlueChip(captured.Value.Trim(), IsModifiedMode ? () =>
                {
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(captured);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    RenderTargets();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
            {
                var used = targets.Select(target => target.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                EditorAutoCompleteService.SetCatalogSuggestions(picker, _unitOrTypeNames.Where(name => !used.Contains(name)));
            }
        }
        if (picker != null)
        {
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string name || string.IsNullOrWhiteSpace(name)) return;
                MajorGodCatalog.AddShadesChild(shades, "targetunit", name);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                RenderTargets();
            };
        }
        RenderTargets();
        host.Children.Add(row);
    }

    private void BuildTerrainCreepsEditor(Panel host, XElement terrainCreeps)
    {
        var creeps = terrainCreeps.Elements().Where(child =>
            child.Name.LocalName.Equals("terraincreep", StringComparison.OrdinalIgnoreCase)).ToList();
        for (var index = 0; index < creeps.Count; index++)
        {
            var creep = creeps[index];
            var body = AddBonusSubcontainer(host, $"Terrain Creep {index + 1}", IsModifiedMode ? () =>
            {
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(creep);
                MarkAndRebuildSpecialMechanics();
            } : null);

            var coreRow = CreateBountyWrappingRow();
            coreRow.Children.Add(CreateBonusFieldPair("Creep",
                CreateSpecialAttributeAutoComplete(creep, "creep", _terrainCreepNames)));
            coreRow.Children.Add(CreateBonusFieldPair("Radius",
                CreateSpecialAttributeNumberBox(creep, "maxradius", "0", ProtoUnitNumericKind.UnsignedFloat)));
            coreRow.Children.Add(CreateBonusFieldPair("Growth Rate",
                CreateSpecialAttributeNumberBox(creep, "growthrate", "0", ProtoUnitNumericKind.UnsignedFloat)));
            coreRow.Children.Add(CreateBonusFieldPair("Decay Rate",
                CreateSpecialAttributeNumberBox(creep, "decayrate", "0", ProtoUnitNumericKind.UnsignedFloat)));
            BuildTerrainCreepOptionalNumber(coreRow, creep, "strength", "Strength");
            body.Children.Add(coreRow);

            var intervalRow = CreateBountyWrappingRow();
            var intervalFields = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = EditorFormLayout.LabelGap,
                Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap),
                VerticalAlignment = VerticalAlignment.Center
            };
            intervalFields.Children.Add(new TextBlock
            {
                Text = "Update Interval (s)",
                VerticalAlignment = VerticalAlignment.Center
            });
            intervalFields.Children.Add(CreateInlineLabel("Min"));
            var minimumInterval = CreateSpecialAttributeNumberBox(
                creep, "minupdateinterval", "0", ProtoUnitNumericKind.UnsignedFloat);
            minimumInterval.VerticalAlignment = VerticalAlignment.Center;
            intervalFields.Children.Add(minimumInterval);
            intervalFields.Children.Add(CreateInlineLabel("Max"));
            var maximumInterval = CreateSpecialAttributeNumberBox(
                creep, "maxupdateinterval", "0", ProtoUnitNumericKind.UnsignedFloat);
            maximumInterval.VerticalAlignment = VerticalAlignment.Center;
            intervalFields.Children.Add(maximumInterval);
            intervalRow.Children.Add(intervalFields);
            BuildTerrainCreepPlacementOptions(intervalRow, creep);
            body.Children.Add(intervalRow);

            BuildTerrainCreepFlags(body, creep);
            BuildTerrainCreepGenerators(body, creep);
        }

        if (IsModifiedMode)
        {
            var add = CreateAddButton("Add terrain creep");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddTerrainCreep(terrainCreeps);
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
        }
    }

    private void BuildTerrainCreepOptionalNumber(Panel row, XElement creep, string attributeName, string label)
    {
        var attribute = FindAttribute(creep, attributeName);
        if (attribute == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton(label);
            add.Margin = new Thickness(0, 0, 4, 4);
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                SetAttribute(creep, attributeName, "0");
                MarkAndRebuildSpecialMechanics();
            };
            row.Children.Add(add);
            return;
        }

        var value = CreateSpecialAttributeNumberBox(creep, attributeName, "0", ProtoUnitNumericKind.UnsignedFloat);
        var fields = EditorFormLayout.LabeledGroup(label, value);
        if (IsModifiedMode)
        {
            var remove = EditorRemoveButtons.Field();
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                RemoveAttribute(creep, attributeName);
                MarkAndRebuildSpecialMechanics();
            };
            fields.Children.Remove(value);
            fields.Children.Add(EditorFormLayout.WithCross(value, remove));
        }
        row.Children.Add(fields);
    }

    private void BuildTerrainCreepFlags(Panel host, XElement creep)
    {
        var row = CreateBountyWrappingRow();
        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox
            {
                Width = 50,
                MaxWidth = 50,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                Margin = new Thickness(0, 0, 4, 4)
            }, EditorFieldWidthRole.ChipSearch);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            row.Children.Add(CreateBonusFieldPair("Flags", picker));
        }

        var chips = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picker == null)
            row.Children.Add(CreateBonusFieldPair("Flags", chips));
        else
            row.Children.Add(chips);

        void Render()
        {
            chips.Children.Clear();
            var selected = MajorGodCatalog.TerrainCreepFlags.Where(definition =>
                FindAttribute(creep, definition.Attribute) != null).ToList();
            foreach (var definition in selected)
            {
                var captured = definition;
                chips.Children.Add(EditorChipService.CreateBlueChip(captured.Label, IsModifiedMode ? () =>
                {
                    RemoveAttribute(creep, captured.Attribute);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    Render();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
            {
                var used = selected.Select(definition => definition.Attribute).ToHashSet(StringComparer.OrdinalIgnoreCase);
                picker.ItemsSource = MajorGodCatalog.TerrainCreepFlags.Where(definition => !used.Contains(definition.Attribute))
                    .Select(definition => definition.Label).ToList();
            }
        }

        if (picker != null)
        {
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string label) return;
                var definition = MajorGodCatalog.TerrainCreepFlags.First(candidate =>
                    candidate.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
                SetAttribute(creep, definition.Attribute, "");
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                Render();
            };
        }
        Render();
        if (IsModifiedMode || chips.Children.Count > 0)
            host.Children.Add(row);
    }

    private void BuildTerrainCreepPlacementOptions(Panel row, XElement creep)
    {
        var buildability = FindAttribute(creep, "buildability");
        if (buildability == null)
        {
            if (IsModifiedMode)
            {
                var add = CreateAddButton("Buildability");
                add.Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap);
                add.VerticalAlignment = VerticalAlignment.Center;
                add.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    SetAttribute(creep, "buildability", "buildable");
                    MarkAndRebuildSpecialMechanics();
                };
                row.Children.Add(add);
            }
        }
        else
        {
            var current = buildability.Value.Trim();
            var options = MajorGodCatalog.TerrainCreepBuildabilityValues.Append(current)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var combo = ConfigureMajorGodField(new ComboBox
            {
                ItemsSource = options,
                SelectedItem = options.First(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)),
                Width = 160,
                IsEnabled = IsModifiedMode
            }, EditorFieldWidthRole.LongEnum);
            EditorValidationBindings.Bind(combo, creep, "@buildability");
            combo.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || combo.SelectedItem is not string selected) return;
                SetAttribute(creep, "buildability", selected.ToLowerInvariant());
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            };
            var fields = EditorFormLayout.LabeledGroup("Buildability", combo);
            if (IsModifiedMode)
            {
                var remove = EditorRemoveButtons.Field();
                remove.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    RemoveAttribute(creep, "buildability");
                    MarkAndRebuildSpecialMechanics();
                };
                fields.Children.Remove(combo);
                fields.Children.Add(EditorFormLayout.WithCross(combo, remove));
            }
            row.Children.Add(fields);
        }

        var preventPlacement = FindAttribute(creep, "preventplacement");
        if (preventPlacement == null)
        {
            if (IsModifiedMode)
            {
                var add = CreateAddButton("Prevent placement");
                add.Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap);
                add.VerticalAlignment = VerticalAlignment.Center;
                add.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    SetAttribute(creep, "preventplacement", "");
                    MarkAndRebuildSpecialMechanics();
                };
                row.Children.Add(add);
            }
        }
        else
        {
            var selector = CreateSpecialAttributeAutoComplete(creep, "preventplacement", _unitOrTypeNames);
            var fields = EditorFormLayout.LabeledGroup("Prevent Placement of", selector);
            if (IsModifiedMode)
            {
                var remove = EditorRemoveButtons.Field();
                remove.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    RemoveAttribute(creep, "preventplacement");
                    MarkAndRebuildSpecialMechanics();
                };
                fields.Children.Remove(selector);
                fields.Children.Add(EditorFormLayout.WithCross(selector, remove));
            }
            row.Children.Add(fields);
        }
    }

    private void BuildTerrainCreepGenerators(Panel host, XElement creep)
    {
        var row = CreateBountyWrappingRow();
        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox
            {
                Width = 50,
                MaxWidth = 50,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                Margin = new Thickness(0, 0, 4, 4)
            }, EditorFieldWidthRole.ChipSearch);
            EditorValidationBindings.Bind(picker, creep, "protounit", repeated: true);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            row.Children.Add(CreateBonusFieldPair("Generator", picker));
        }

        var chips = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picker == null)
            row.Children.Add(CreateBonusFieldPair("Generator", chips));
        else
            row.Children.Add(chips);

        void Render()
        {
            var generators = creep.Elements().Where(child =>
                child.Name.LocalName.Equals("protounit", StringComparison.OrdinalIgnoreCase)).ToList();
            chips.Children.Clear();
            foreach (var generator in generators)
            {
                var captured = generator;
                chips.Children.Add(EditorChipService.CreateBlueChip(captured.Value.Trim(), IsModifiedMode ? () =>
                {
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(captured);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    Render();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
            {
                var used = generators.Select(generator => generator.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                EditorAutoCompleteService.SetCatalogSuggestions(picker, _protoUnitNames.Where(name => !used.Contains(name)));
            }
        }

        if (picker != null)
        {
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string name || string.IsNullOrWhiteSpace(name)) return;
                MajorGodCatalog.AddTerrainCreepGenerator(creep, name);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                Render();
            };
        }
        Render();
        host.Children.Add(row);
    }

    private void BuildTimeShiftingEditor(Panel host, XElement timeShifting)
    {
        var maxRow = new WrapPanel();
        maxRow.Children.Add(CreateInlineLabel("Max Concurrent Time Shifts"));
        maxRow.Children.Add(CreateSpecialAttributeNumberBox(timeShifting, "maxconcurrenttimeshifts", "1", ProtoUnitNumericKind.PositiveInteger));
        host.Children.Add(maxRow);

        foreach (var unit in timeShifting.Elements().Where(child => child.Name.LocalName.Equals("protounit", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var row = new WrapPanel();
            row.Children.Add(CreateInlineLabel("Unit"));
            row.Children.Add(CreateSpecialProtoUnitBox(unit));
            row.Children.Add(CreateInlineLabel("Cost Ratio"));
            row.Children.Add(CreateSpecialAttributeNumberBox(unit, "costratio", "0", ProtoUnitNumericKind.UnsignedFloat));
            row.Children.Add(CreateInlineLabel("Time Ratio"));
            row.Children.Add(CreateSpecialAttributeNumberBox(unit, "timeratio", "0", ProtoUnitNumericKind.UnsignedFloat));
            if (IsModifiedMode)
                row.Children.Add(CreateSpecialChildRemoveButton(unit));
            host.Children.Add(row);
        }
        if (IsModifiedMode)
        {
            var add = CreateAddButton("Add unit");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddTimeShiftingUnit(timeShifting);
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
        }
    }

    private void BuildBuildingChainEditor(Panel host, XElement buildingChain)
    {
        var anchor = MajorGodCatalog.FindDirectChild(buildingChain, "anchor") ?? new XElement("anchor");
        var anchorRow = new WrapPanel();
        anchorRow.Children.Add(CreateInlineLabel("Anchor"));
        Action ensureAnchor = () => EnsureBuildingChainAnchorAttached(buildingChain, anchor);
        anchorRow.Children.Add(CreateSpecialValueAutoComplete(anchor, _unitOrTypeNames, ensureAnchor));
        anchorRow.Children.Add(CreateInlineLabel("VFX"));
        anchorRow.Children.Add(CreateSpecialAttributeAutoComplete(anchor, "vfx", _protoUnitNames, ensureAnchor));
        host.Children.Add(anchorRow);

        BuildOptionalBuildingChainVfxGroup(host, buildingChain, "abundancevfx", "Abundance VFX");
        BuildOptionalBuildingChainVfxGroup(host, buildingChain, "autobuildvfx", "Auto Build VFX");

        host.Children.Add(CreateSpecialSubheading("Resource generation"));
        var resourceGeneration = MajorGodCatalog.FindDirectChild(buildingChain, "resourcegeneration");
        if (resourceGeneration != null)
        {
            foreach (var tier in resourceGeneration.Elements().Where(child =>
                         child.Name.LocalName.Equals("tilerateperminutetier", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var tierRow = new WrapPanel();
                tierRow.Children.Add(CreateInlineLabel("Rate tile/minute"));
                tierRow.Children.Add(CreateSpecialElementNumberBox(tier, "0", ProtoUnitNumericKind.UnsignedFloat));
                tierRow.Children.Add(CreateResourceComboBox(tier, "resource"));
                tierRow.Children.Add(CreateInlineLabel("When number of tiles >"));
                tierRow.Children.Add(CreateSpecialAttributeNumberBox(tier, "mintiles", "0", ProtoUnitNumericKind.UnsignedInteger));
                if (IsModifiedMode)
                {
                    var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
                    remove.Click += (_, _) =>
                    {
                        if (!CanEditCurrent()) return;
                        MajorGodCatalog.RemoveElementAndAdjacentWhitespace(tier);
                        if (!resourceGeneration.Elements().Any())
                            MajorGodCatalog.RemoveElementAndAdjacentWhitespace(resourceGeneration);
                        MarkAndRebuildSpecialMechanics();
                    };
                    tierRow.Children.Add(remove);
                }
                host.Children.Add(tierRow);
            }
        }
        if (IsModifiedMode)
        {
            var addRate = CreateAddButton("Add rate tier");
            addRate.HorizontalAlignment = HorizontalAlignment.Left;
            addRate.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBuildingChainRateTier(buildingChain);
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(addRate);
        }

        host.Children.Add(CreateSpecialSubheading("Chainable buildings"));
        foreach (var building in buildingChain.Elements().Where(child =>
                     child.Name.LocalName.Equals("chainablebuilding", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var buildingRow = new WrapPanel();
            buildingRow.Children.Add(CreateInlineLabel("Building"));
            buildingRow.Children.Add(CreateSpecialValueAutoComplete(building, _protoUnitNames));
            buildingRow.Children.Add(CreateInlineLabel("Chain Radius"));
            buildingRow.Children.Add(CreateSpecialAttributeNumberBox(building, "chainradius", "0", ProtoUnitNumericKind.UnsignedFloat));
            buildingRow.Children.Add(CreateInlineLabel("Generates"));
            buildingRow.Children.Add(CreateResourceComboBox(building, "generationresource"));
            if (IsModifiedMode)
                buildingRow.Children.Add(CreateSpecialChildRemoveButton(building));
            host.Children.Add(buildingRow);
        }
        if (IsModifiedMode)
        {
            var addBuilding = CreateAddButton("Add building");
            addBuilding.HorizontalAlignment = HorizontalAlignment.Left;
            addBuilding.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddChainableBuilding(buildingChain);
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(addBuilding);
        }
    }

    private void BuildOptionalBuildingChainVfxGroup(
        Panel host,
        XElement buildingChain,
        string tag,
        string label)
    {
        var vfxGroup = MajorGodCatalog.FindDirectChild(buildingChain, tag);
        if (vfxGroup == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton(label);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                if (tag.Equals("autobuildvfx", StringComparison.OrdinalIgnoreCase))
                    MajorGodCatalog.AddBuildingChainAutoBuildVfx(buildingChain);
                else
                    MajorGodCatalog.AddBuildingChainAbundanceVfx(buildingChain);
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
            return;
        }

        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel(label));
        foreach (var (attribute, sizeLabel) in new[] { ("small", "Small"), ("medium", "Medium"), ("large", "Large") })
        {
            row.Children.Add(CreateInlineLabel(sizeLabel));
            row.Children.Add(CreateSpecialAttributeAutoComplete(vfxGroup, attribute, _protoUnitNames));
        }
        if (IsModifiedMode)
            row.Children.Add(CreateSpecialChildRemoveButton(vfxGroup));
        host.Children.Add(row);
    }

    private void BuildCombatXpEditor(Panel host, XElement combatXp)
    {
        var tiers = combatXp.Elements().Where(child =>
            child.Name.LocalName.Equals("tier", StringComparison.OrdinalIgnoreCase)).ToList();
        var tierRows = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        for (var index = 0; index < tiers.Count; index++)
        {
            var tier = tiers[index];
            var segment = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Height = EditorFormLayout.FieldHeight,
                Margin = new Thickness(0, 0, 6, 4)
            };
            segment.Children.Add(CreateInlineLabel($"Tier {index + 1}"));
            var tierValue = CreateSpecialElementNumberBox(tier, "0", ProtoUnitNumericKind.UnsignedInteger);
            segment.Children.Add(tierValue);
            if (IsModifiedMode)
                segment.Children.Add(CreateSpecialChildRemoveButton(tier));
            tierRows.Children.Add(segment);
        }

        if (IsModifiedMode)
        {
            var add = CreateAddButton("Add tier");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(4, 0, 6, 4);
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddCombatXpTier(combatXp);
                MarkAndRebuildSpecialMechanics();
            };
            tierRows.Children.Add(add);
        }
        host.Children.Add(tierRows);
    }

    private CheckBox CreateBountyActiveCheckBox(XElement bounty)
    {
        var active = MajorGodCatalog.FindDirectChild(bounty, "active");
        var checkBox = new CheckBox
        {
            Content = "Active",
            IsChecked = active == null || !active.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase),
            IsEnabled = IsModifiedMode,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        EditorValidationBindings.Bind(checkBox, bounty, "active");
        checkBox.IsCheckedChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            var element = MajorGodCatalog.FindDirectChild(bounty, "active") ??
                          MajorGodCatalog.AddBountyElement(bounty, "active");
            element.Value = checkBox.IsChecked == true ? "true" : "false";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return checkBox;
    }

    private void BuildBonusUnitSpawningEditor(Panel host, XElement bonus)
    {
        _refreshBonusUnitSpawningEditor = () =>
        {
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            var wasPopulating = _isPopulating;
            _isPopulating = true;
            try
            {
                while (host.Children.Count > 1)
                    host.Children.RemoveAt(1);
                BuildBonusUnitSpawningEditor(host, bonus);
            }
            finally
            {
                _isPopulating = wasPopulating;
            }
        };
        var goals = bonus.Elements().Where(IsBonusGoal).ToList();
        foreach (var goal in goals)
        {
            var body = AddBonusSubcontainer(host, "Goal", IsModifiedMode ? () =>
            {
                ScheduleBonusDisplayStringRemoval(goal);
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(goal);
                MarkAndRebuildBonusUnitSpawning();
            } : null);
            BuildBonusGoalIdentity(body, goal);
            BuildBonusGoalReward(body, goal);
            BuildBonusRewardPointCosts(body, goal);
            BuildBonusSpawnLocation(body, goal);
            BuildBonusContributors(body, goal);
            if (BonusGoalType(goal) == "Damage")
                BuildBonusChipEditor(body, goal, "excludedtarget", "Excluded Targets");
            BuildBonusGoalFlags(body, goal);
            BuildBonusDisplayInfo(body, goal);
        }
        if (IsModifiedMode)
        {
            var add = CreateAddButton("Add goal");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBonusUnitGoal(bonus);
                MarkAndRebuildBonusUnitSpawning();
            };
            host.Children.Add(add);
        }
    }

    private static bool IsBonusGoal(XElement element)
        => element.Name.LocalName.Equals("damagegoal", StringComparison.OrdinalIgnoreCase) ||
           element.Name.LocalName.Equals("deathcountgoal", StringComparison.OrdinalIgnoreCase) ||
           element.Name.LocalName.Equals("resourcegoal", StringComparison.OrdinalIgnoreCase);

    private static string BonusGoalType(XElement goal)
        => goal.Name.LocalName.Equals("resourcegoal", StringComparison.OrdinalIgnoreCase) ? "Resource" :
           goal.Name.LocalName.Equals("deathcountgoal", StringComparison.OrdinalIgnoreCase) ? "Death" : "Damage";

    private StackPanel AddBonusSubcontainer(Panel host, string title, Action? removeAction)
    {
        var card = new Border { Classes = { "editor-card" }, Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel { Spacing = 6 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (removeAction != null)
        {
            var remove = EditorRemoveButtons.Card();
            remove.Click += (_, _) =>
            {
                if (CanEditCurrent()) removeAction();
            };
            Grid.SetColumn(remove, 1);
            header.Children.Add(remove);
        }
        body.Children.Add(header);
        card.Child = body;
        host.Children.Add(card);
        return body;
    }

    private void BuildBonusGoalIdentity(Panel host, XElement goal)
    {
        var row = CreateBountyWrappingRow();
        var committedGoalName = FindAttribute(goal, "name")?.Value ?? "";
        var name = ConfigureMajorGodField(new TextBox
        {
            Text = FindAttribute(goal, "name")?.Value ?? "",
            Width = 150,
            MaxWidth = 150,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.SingleLineText);
        EditorValidationBindings.Bind(name, goal, "@name");
        name.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            SetAttribute(goal, "name", name.Text ?? "");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        name.LostFocus += async (_, _) =>
        {
            var newName = name.Text?.Trim() ?? "";
            if (newName.Equals(committedGoalName, StringComparison.Ordinal)) return;
            if (await RenameBonusGoalGeneratedStringsAsync(goal, committedGoalName, newName)) committedGoalName = newName;
            else { SetAttribute(goal, "name", committedGoalName); name.Text = committedGoalName; RefreshRawXmlFromCurrent(); }
        };
        row.Children.Add(CreateBonusFieldPair("Name", name));
        var type = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = new[] { "Damage", "Death", "Resource" },
            SelectedItem = BonusGoalType(goal),
            Width = 100,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.ShortEnum);
        type.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || type.SelectedItem is not string selected) return;
            var previousType = BonusGoalType(goal);
            if (previousType.Equals("Resource", StringComparison.OrdinalIgnoreCase) !=
                selected.Equals("Resource", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var contributor in goal.Elements().Where(child =>
                             child.Name.LocalName.Equals("contributor", StringComparison.OrdinalIgnoreCase)).ToList())
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(contributor);
            }
            if (previousType.Equals("Resource", StringComparison.OrdinalIgnoreCase) &&
                !selected.Equals("Resource", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleBonusDisplayStringRemoval(goal);
                foreach (var displayElement in goal.Elements().Where(child =>
                             child.Name.LocalName.Equals("displayinfo", StringComparison.OrdinalIgnoreCase) ||
                             child.Name.LocalName.Equals("displayonhud", StringComparison.OrdinalIgnoreCase)).ToList())
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(displayElement);
            }
            goal.Name = selected switch { "Death" => "deathcountgoal", "Resource" => "resourcegoal", _ => "damagegoal" };
            if (selected.Equals("Resource", StringComparison.OrdinalIgnoreCase))
                SetAttribute(goal, "rewardtracking", "Single");
            if (!selected.Equals("Damage", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var excludedTarget in goal.Elements().Where(child =>
                             child.Name.LocalName.Equals("excludedtarget", StringComparison.OrdinalIgnoreCase)).ToList())
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(excludedTarget);
            }
            var spawn = MajorGodCatalog.FindDirectChild(goal, "spawnlocation");
            if (spawn != null)
            {
                var spawnType = selected switch { "Death" => "DeathLocation", "Resource" => "FromBuilding", _ => "DamageTarget" };
                SetAttribute(spawn, "type", spawnType);
                if (!spawnType.Equals("FromBuilding", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveAttribute(spawn, "landbuilding");
                    RemoveAttribute(spawn, "waterbuilding");
                }
            }
            MarkAndRebuildBonusUnitSpawning();
        };
        row.Children.Add(CreateBonusFieldPair("Type", type));
        var amount = MajorGodCatalog.FindDirectChild(goal, "goalamount") ?? new XElement("goalamount");
        row.Children.Add(CreateBonusFieldPair("Amount",
            CreateBonusElementNumberBox(goal, amount, "1", ProtoUnitNumericKind.PositiveInteger)));
        var goalType = BonusGoalType(goal);
        var trackingValues = goalType == "Resource" ? new[] { "Single" } : new[] { "Single", "PerPossibleReward" };
        var rawTracking = FindAttribute(goal, "rewardtracking")?.Value ?? "Single";
        var tracking = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = trackingValues,
            SelectedItem = trackingValues.FirstOrDefault(value => value.Equals(rawTracking, StringComparison.OrdinalIgnoreCase)) ?? "Single",
            Width = 150,
            IsEnabled = IsModifiedMode && goalType != "Resource",
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(tracking, goal, "@rewardtracking");
        tracking.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || tracking.SelectedItem is not string selected) return;
            SetAttribute(goal, "rewardtracking", selected);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(CreateBonusFieldPair("Tracking", tracking));
        var activeElement = MajorGodCatalog.FindDirectChild(goal, "active");
        var active = new CheckBox
        {
            Content = "Active",
            IsChecked = activeElement == null || !activeElement.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase),
            IsEnabled = IsModifiedMode,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4)
        };
        EditorValidationBindings.Bind(active, goal, "active");
        active.IsCheckedChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            var existing = MajorGodCatalog.FindDirectChild(goal, "active");
            if (active.IsChecked == true)
            {
                if (existing != null) MajorGodCatalog.RemoveElementAndAdjacentWhitespace(existing);
            }
            else (existing ?? MajorGodCatalog.AddBonusGoalChild(goal, "active")).Value = "false";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(active);
        host.Children.Add(row);
    }

    private void BuildBonusGoalReward(Panel host, XElement goal)
    {
        var row = CreateBountyWrappingRow();
        AddBonusChips(row, goal, "reward", "Reward", _unitOrTypeNames);
        AddBonusChips(row, goal, "excludefromreward", "Excluded from reward", _unitOrTypeNames);
        host.Children.Add(row);
    }

    private void BuildBonusRewardPointCosts(Panel host, XElement goal)
    {
        var costs = goal.Elements().Where(child => child.Name.LocalName.Equals("rewardpointcost", StringComparison.OrdinalIgnoreCase)).ToList();
        if (costs.Count == 0)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton("Reward Point Cost");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBonusRewardPointCosts(goal);
                MarkAndRebuildBonusUnitSpawning();
            };
            host.Children.Add(add);
            return;
        }
        var row = CreateBountyWrappingRow();
        row.Children.Add(CreateBountyInlineLabel("Reward Point Cost"));
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var cost = costs.FirstOrDefault(item => (FindAttribute(item, "resourcetype")?.Value ?? "").Equals(resource, StringComparison.OrdinalIgnoreCase));
            if (cost == null && !IsModifiedMode) continue;
            cost ??= new XElement("rewardpointcost", new XAttribute("resourcetype", resource), "0");
            row.Children.Add(CreateBonusFieldPair(resource,
                CreateBonusElementNumberBox(goal, cost, "0", ProtoUnitNumericKind.UnsignedFloat)));
        }
        if (IsModifiedMode)
        {
            var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(0, 0, 4, 4) };
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                foreach (var cost in costs) MajorGodCatalog.RemoveElementAndAdjacentWhitespace(cost);
                MarkAndRebuildBonusUnitSpawning();
            };
            row.Children.Add(remove);
        }
        host.Children.Add(row);
    }

    private void BuildBonusSpawnLocation(Panel host, XElement goal)
    {
        var spawn = MajorGodCatalog.FindDirectChild(goal, "spawnlocation") ?? new XElement("spawnlocation");
        var goalType = BonusGoalType(goal);
        var values = goalType switch
        {
            "Death" => new[] { "FromBuilding", "DeathLocation" },
            "Resource" => new[] { "FromBuilding" },
            _ => new[] { "DamageTarget", "DamageDealer", "FromBuilding" }
        };
        var current = FindAttribute(spawn, "type")?.Value ?? values[0];
        var row = CreateBountyWrappingRow();
        var type = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = values,
            SelectedItem = values.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? current,
            Width = 150,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(type, spawn, "@type");
        type.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || type.SelectedItem is not string selected) return;
            EnsureBonusChildAttached(goal, spawn);
            SetAttribute(spawn, "type", selected);
            if (!selected.Equals("FromBuilding", StringComparison.OrdinalIgnoreCase))
            {
                RemoveAttribute(spawn, "landbuilding");
                RemoveAttribute(spawn, "waterbuilding");
            }
            MarkAndRebuildBonusUnitSpawning();
        };
        row.Children.Add(CreateBonusFieldPair("Spawn Location", type));
        if (current.Equals("FromBuilding", StringComparison.OrdinalIgnoreCase))
        {
            row.Children.Add(CreateBonusFieldPair("Land",
                CreateBonusAttributeAutoComplete(goal, spawn, "landbuilding", _unitOrTypeNames)));
            row.Children.Add(CreateBonusFieldPair("Water",
                CreateBonusAttributeAutoComplete(goal, spawn, "waterbuilding", _unitOrTypeNames)));
        }
        host.Children.Add(row);
    }

    private void BuildBonusContributors(Panel host, XElement goal)
    {
        foreach (var contributor in goal.Elements().Where(child =>
                     child.Name.LocalName.Equals("contributor", StringComparison.OrdinalIgnoreCase) &&
                     (FindAttribute(child, "unit") != null || FindAttribute(child, "type") != null ||
                      (BonusGoalType(goal) == "Resource" && FindAttribute(child, "resource") != null))).ToList())
        {
            var row = CreateBountyWrappingRow();
            var usesUnit = FindAttribute(contributor, "unit") != null;
            var usesResource = FindAttribute(contributor, "resource") != null;
            var contributorModes = BonusGoalType(goal) == "Resource"
                ? new[] { "Unit", "Unit type", "Resource" }
                : new[] { "Unit", "Unit type" };
            var mode = ConfigureMajorGodField(new ComboBox
            {
                ItemsSource = contributorModes, SelectedItem = usesResource ? "Resource" : usesUnit ? "Unit" : "Unit type",
                Width = 115, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 4, 4)
            }, EditorFieldWidthRole.ShortEnum);
            mode.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || mode.SelectedItem is not string selected) return;
                RemoveAttribute(contributor, "unit");
                RemoveAttribute(contributor, "type");
                RemoveAttribute(contributor, "resource");
                if (selected == "Resource") SetAttribute(contributor, "resource", "Food");
                else SetAttribute(contributor, selected == "Unit" ? "unit" : "type", "");
                MarkAndRebuildBonusUnitSpawning();
            };
            row.Children.Add(CreateBonusFieldPair("Contributor", mode));
            if (usesResource)
                row.Children.Add(CreateBonusContributorResourceBox(contributor));
            else
                row.Children.Add(CreateSpecialAttributeAutoComplete(contributor, usesUnit ? "unit" : "type", usesUnit ? _protoUnitNames : _unitTypeNames));
            row.Children.Add(CreateBountyNumberBox(contributor));
            if (IsModifiedMode) row.Children.Add(CreateSpecialChildRemoveButton(contributor, MarkAndRebuildBonusUnitSpawning));
            host.Children.Add(row);
        }
        if (IsModifiedMode)
        {
            var add = CreateAddButton("Add contributor");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBonusGoalChild(goal, "contributor", "0");
                MarkAndRebuildBonusUnitSpawning();
            };
            host.Children.Add(add);
        }
    }

    private ComboBox CreateBonusContributorResourceBox(XElement contributor)
    {
        var raw = FindAttribute(contributor, "resource")?.Value ?? "Food";
        var selected = ProtoConstants.KnownResourceTypes.FirstOrDefault(resource => resource.Equals(raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
        var box = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = ProtoConstants.KnownResourceTypes,
            SelectedItem = selected,
            Width = 100,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.ShortEnum);
        EditorValidationBindings.Bind(box, contributor, "@resource");
        box.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || box.SelectedItem is not string resource) return;
            SetAttribute(contributor, "resource", resource);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void BuildBonusGoalFlags(Panel host, XElement goal)
    {
        var activeFlags = GetActiveBonusGoalFlags(goal);
        if (!IsModifiedMode && activeFlags.Count == 0) return;
        var row = CreateBountyWrappingRow();
        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox
            {
                Width = 150,
                MaxWidth = 150,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                Margin = new Thickness(0, 0, 4, 4)
            }, EditorFieldWidthRole.ChipSearch);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
        }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picker != null)
        {
            row.Children.Add(CreateBonusFieldPair("Flags", picker));
            row.Children.Add(chips);
        }
        else
        {
            chips.MaxWidth = 450;
            row.Children.Add(CreateBonusFieldPair("Flags", chips));
        }
        void Render()
        {
            activeFlags = GetActiveBonusGoalFlags(goal);
            chips.Children.Clear();
            foreach (var flag in activeFlags)
            {
                var captured = flag;
                var displayInfoLocksFlag = captured.Tag.Equals("displayonhud", StringComparison.OrdinalIgnoreCase) &&
                                           MajorGodCatalog.FindDirectChild(goal, "displayinfo") != null;
                chips.Children.Add(EditorChipService.CreateBlueChip(captured.Label, IsModifiedMode && !displayInfoLocksFlag ? () =>
                {
                    var element = MajorGodCatalog.FindDirectChild(goal, captured.Tag);
                    if (element != null) MajorGodCatalog.RemoveElementAndAdjacentWhitespace(element);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    Render();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
                picker.ItemsSource = BonusGoalFlags.Where(flag => activeFlags.All(active =>
                    !active.Tag.Equals(flag.Tag, StringComparison.OrdinalIgnoreCase))).Select(flag => flag.Label).ToList();
        }
        if (picker != null)
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string selected) return;
                var definition = BonusGoalFlags.First(flag => flag.Label.Equals(selected, StringComparison.OrdinalIgnoreCase));
                var existing = MajorGodCatalog.FindDirectChild(goal, definition.Tag);
                (existing ?? MajorGodCatalog.AddBonusGoalChild(goal, definition.Tag)).Value = "true";
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                Render();
            };
        Render();
        host.Children.Add(row);
    }

    private static List<(string Label, string Tag)> GetActiveBonusGoalFlags(XElement goal)
    {
        var flags = BonusGoalFlags.Where(definition =>
        {
            var element = MajorGodCatalog.FindDirectChild(goal, definition.Tag);
            return element != null && element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (MajorGodCatalog.FindDirectChild(goal, "displayonhud") is { } displayOnHud &&
            displayOnHud.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            flags.Add(("DisplayOnHUD", "displayonhud"));
        return flags;
    }

    private void BuildBonusDisplayInfo(Panel host, XElement goal)
    {
        var displayInfo = MajorGodCatalog.FindDirectChild(goal, "displayinfo");
        var displayOnHud = MajorGodCatalog.FindDirectChild(goal, "displayonhud");
        if (displayInfo == null && displayOnHud == null)
        {
            if (!IsModifiedMode || BonusGoalType(goal) != "Resource") return;
            var add = CreateAddButton("Display info");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                var displayInfo = MajorGodCatalog.AddBonusDisplayInfo(goal);
                QueueBonusDisplayStrings(goal, displayInfo);
                MarkAndRebuildBonusUnitSpawning();
            };
            host.Children.Add(add);
            return;
        }
        if (displayInfo == null) return;

        var body = AddBonusSubcontainer(host, "Display info", IsModifiedMode ? () =>
        {
            ScheduleBonusDisplayStringRemoval(goal);
            MajorGodCatalog.RemoveElementAndAdjacentWhitespace(displayInfo);
            if (displayOnHud != null) MajorGodCatalog.RemoveElementAndAdjacentWhitespace(displayOnHud);
            MarkAndRebuildBonusUnitSpawning();
        } : null);
        var identity = CreateBountyWrappingRow();
        identity.Children.Add(CreateBonusFieldPair(EditorDisplayText.DisplayName,
            CreateBonusResolvedStringBox(goal, displayInfo, "displaynameid", "SPAWN_REWARD")));
        body.Children.Add(identity);
        body.Children.Add(EditorRolloverField.CreateRow("Rollover",
            CreateBonusResolvedStringBox(goal, displayInfo, "rolloverid", "SPAWN_REWARD_LR")));

        body.Children.Add(CreateSpecialSubheading("Progress"));
        var progress = CreateBountyWrappingRow();
        progress.Children.Add(CreateBonusFieldPair("Type",
            CreateBonusDisplayComboBox(displayInfo, "progresstype", new[] { "Default", "ShieldBlessing", "SpawnReward" }, 145)));
        progress.Children.Add(CreateBonusFieldPair("Icon",
            CreateBonusDisplayIconEditor(displayInfo)));
        progress.Children.Add(CreateBonusFieldPair("Progress Tooltip",
            CreateBonusResolvedStringBox(goal, displayInfo, "progresstooltipid", "SPAWN_REWARD_PROGRESS")));
        progress.Children.Add(CreateBonusFieldPair("Next Unit tooltip",
            CreateBonusResolvedStringBox(goal, displayInfo, "nextunittooltipid", "SPAWN_NEXT_UNIT")));
        body.Children.Add(progress);

        body.Children.Add(CreateSpecialSubheading("Notification"));
        var notification = CreateBountyWrappingRow();
        notification.Children.Add(CreateBonusFieldPair("Sound",
            CreateBonusDisplayTextBox(displayInfo, "notificationsound", 200)));
        notification.Children.Add(CreateBonusFieldPair("Message",
            CreateBonusResolvedStringBox(goal, displayInfo, "notificationmessageid", "SPAWN_REWARD_NOTIFICATION", 380)));
        body.Children.Add(notification);
    }

    private TextBox CreateBonusResolvedStringBox(XElement goal, XElement displayInfo, string tag, string suffix, double width = 200)
    {
        var element = MajorGodCatalog.FindDirectChild(displayInfo, tag) ?? new XElement(tag);
        var id = element.Value.Trim();
        var box = new TextBox { Text = _pendingStringUpdates.GetValueOrDefault(id) ?? id, Width = width, MaxWidth = width, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 4, 4) };
        if (tag.Equals("rolloverid", StringComparison.OrdinalIgnoreCase))
            EditorRolloverField.Configure(box);
        if (tag.Equals("displaynameid", StringComparison.OrdinalIgnoreCase))
            ConfigureMajorGodField(box, EditorFieldWidthRole.SingleLineText);
        var lastText = box.Text ?? "";
        var loading = true;
        EditorValidationBindings.Bind(box, displayInfo, tag);
        box.TextChanged += (_, _) =>
        {
            if (loading || !CanEditCurrent()) return;
            if (lastText == (box.Text ?? "")) return;
            lastText = box.Text ?? "";
            if (element.Parent == null)
            {
                id = BuildBonusGoalStringId(goal, suffix);
                element.Value = id;
                displayInfo.Add(element);
                MajorGodCatalog.NormalizeDirectChildIndentation(displayInfo);
            }
            if (id.Length == 0) return;
            _pendingStringRemovals.Remove(id);
            _pendingStringUpdates[id] = box.Text ?? "";
            _pendingStringOwners[id] = CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        _ = LoadBonusResolvedStringAsync();
        return box;

        async Task LoadBonusResolvedStringAsync()
        {
            var text = _pendingStringUpdates.GetValueOrDefault(id);
            if (text == null && _resolveStringAsync != null && id.Length > 0)
                text = await _resolveStringAsync(id);
            loading = true;
            lastText = text ?? "";
            box.Text = lastText;
            loading = false;
        }
    }

    private void QueueBonusDisplayStrings(XElement goal, XElement displayInfo)
    {
        foreach (var (tag, suffix) in new[]
                 {
                     ("displaynameid", "SPAWN_REWARD"),
                     ("rolloverid", "SPAWN_REWARD_LR"),
                     ("progresstooltipid", "SPAWN_REWARD_PROGRESS"),
                     ("nextunittooltipid", "SPAWN_NEXT_UNIT"),
                     ("notificationmessageid", "SPAWN_REWARD_NOTIFICATION")
                 })
        {
            var id = MajorGodCatalog.FindDirectChild(displayInfo, tag)?.Value.Trim() ?? "";
            if (id.Length == 0 ||
                !id.Equals(BuildBonusGoalStringId(goal, suffix), StringComparison.OrdinalIgnoreCase))
                continue;
            _pendingStringRemovals.Remove(id);
            _pendingStringRemovalOwners.Remove(id);
            _pendingStringUpdates.TryAdd(id, "");
            _pendingStringOwners[id] = CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!);
        }
    }

    private static string BuildBonusGoalStringId(XElement goal, string suffix)
        => BuildBonusGoalStringId(FindAttribute(goal, "name")?.Value ?? "", suffix);

    private static string BuildBonusGoalStringId(string goalName, string suffix)
        => MajorGodCatalog.BuildBonusGoalStringId(goalName, suffix);

    private async Task<bool> RenameBonusGoalGeneratedStringsAsync(XElement goal, string oldName, string newName)
    {
        try
        {
            var before = new XElement(goal); SetAttribute(before, "name", oldName);
            var candidate = new XElement(goal);
            var changes = MajorGodCatalog.RenameOwnedBonusGoalStringIds(candidate, oldName, newName);
            var owner = CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!);
            var wrapper = new XElement("civ", new XElement("name", owner), new XElement("bonusunitspawning", before));
            var plan = await PrepareStringRenameAsync(before, candidate, changes, MajorGodCatalog.CollectOwnedStringIds(wrapper), owner);
            OwnedStringSavePreparation.ApplyLocalFieldChanges(goal, candidate);
            foreach (var rename in plan.ReferenceRenames)
            {
                _pendingStringUpdates.Remove(rename.OldId);
                _pendingStringOwners.Remove(rename.OldId);
                ScheduleOwnedStringRemoval(rename.OldId, owner);
                _pendingStringRemovals.Remove(rename.NewId);
                _pendingStringRemovalOwners.Remove(rename.NewId);
                _pendingStringUpdates[rename.NewId] = plan.StringUpdates[rename.NewId];
                _pendingStringOwners[rename.NewId] = owner;
            }
            MarkAndRebuildBonusUnitSpawning();
            return true;
        }
        catch (Exception exception)
        {
            SetAttribute(goal, "name", oldName);
            _statusMessage.Text = exception.Message;
            if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } owner)
                await new Prompt(PromptType.Error, "Bonus goal rename blocked", exception.Message).ShowDialog(owner);
            return false;
        }
    }

    private void ScheduleBonusDisplayStringRemoval(XElement goal)
    {
        var displayInfo = MajorGodCatalog.FindDirectChild(goal, "displayinfo");
        if (displayInfo == null) return;
        var goalName = FindAttribute(goal, "name")?.Value ?? "";
        foreach (var (tag, suffix) in new[]
                 {
                     ("displaynameid", "SPAWN_REWARD"),
                     ("rolloverid", "SPAWN_REWARD_LR"),
                     ("progresstooltipid", "SPAWN_REWARD_PROGRESS"),
                     ("nextunittooltipid", "SPAWN_NEXT_UNIT"),
                     ("notificationmessageid", "SPAWN_REWARD_NOTIFICATION")
                 })
        {
            var id = MajorGodCatalog.FindDirectChild(displayInfo, tag)?.Value.Trim() ?? "";
            if (!id.Equals(BuildBonusGoalStringId(goalName, suffix), StringComparison.OrdinalIgnoreCase))
                continue;
            ScheduleOwnedStringRemoval(
                id,
                CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!));
        }
    }

    private ComboBox CreateBonusDisplayComboBox(XElement displayInfo, string tag, IReadOnlyList<string> values, double width)
    {
        var element = MajorGodCatalog.FindDirectChild(displayInfo, tag) ?? new XElement(tag, values[0]);
        var current = values.FirstOrDefault(value => value.Equals(element.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? element.Value.Trim();
        var box = ConfigureMajorGodField(new ComboBox { ItemsSource = values, SelectedItem = current, Width = width, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 4, 4) }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(box, displayInfo, tag);
        box.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || box.SelectedItem is not string selected) return;
            if (element.Parent == null) { displayInfo.Add(element); MajorGodCatalog.NormalizeDirectChildIndentation(displayInfo); }
            element.Value = selected;
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private AssetPathEditor CreateBonusDisplayIconEditor(XElement displayInfo)
    {
        var element = MajorGodCatalog.FindDirectChild(displayInfo, "icon") ?? new XElement("icon");
        var initial = ProtoEditorWindow.NormalizeIconCatalogValue(element.Value, _iconPaths);
        var editor = ConfigureMajorGodField(new AssetPathEditor
        {
            Width = 200,
            MinWidth = 200,
            MaxWidth = 200,
            IsEnabled = IsModifiedMode,
            Opacity = IsModifiedMode ? 1.0 : 0.55
        }, EditorFieldWidthRole.AssetPath);
        EditorValidationBindings.Bind(editor, displayInfo, "icon");
        editor.CompactPresenter.Background = Brush.Parse(IsModifiedMode ? "#0E1110" : "#202220");
        editor.Configure(initial, _iconPaths, value =>
        {
            if (!CanEditCurrent()) return Task.CompletedTask;
            if (element.Parent == null)
            {
                displayInfo.Add(element);
                MajorGodCatalog.NormalizeDirectChildIndentation(displayInfo);
            }
            element.Value = value;
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            return Task.CompletedTask;
        });
        return editor;
    }

    private TextBox CreateBonusDisplayTextBox(XElement displayInfo, string tag, double width)
    {
        var element = MajorGodCatalog.FindDirectChild(displayInfo, tag) ?? new XElement(tag);
        var box = ConfigureMajorGodField(new TextBox { Text = element.Value, Width = width, MaxWidth = width, IsEnabled = IsModifiedMode, Margin = new Thickness(0, 0, 4, 4) }, EditorFieldWidthRole.AssetPath);
        EditorValidationBindings.Bind(box, displayInfo, tag);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            if (element.Parent == null) { displayInfo.Add(element); MajorGodCatalog.NormalizeDirectChildIndentation(displayInfo); }
            element.Value = box.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void BuildBonusChipEditor(Panel host, XElement goal, string tag, string label)
    {
        var values = goal.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!IsModifiedMode && values.Count == 0) return;
        var row = CreateBountyWrappingRow();
        AddBonusChips(row, goal, tag, label, _unitOrTypeNames);
        host.Children.Add(row);
    }

    private void AddBonusChips(Panel row, XElement goal, string tag, string label, IReadOnlyList<string> catalog)
    {
        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox { Width = 50, MaxWidth = 50, ItemsSource = catalog, FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, Margin = new Thickness(0, 0, 4, 4) }, EditorFieldWidthRole.ChipSearch);
            EditorValidationBindings.Bind(picker, goal, tag, repeated: true);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
        }
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picker != null)
        {
            row.Children.Add(CreateBonusFieldPair(label, picker));
            row.Children.Add(chips);
        }
        else
        {
            chips.MaxWidth = 450;
            row.Children.Add(CreateBonusFieldPair(label, chips));
        }
        void Render()
        {
            var elements = goal.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            chips.Children.Clear();
            foreach (var element in elements)
            {
                var captured = element;
                chips.Children.Add(EditorChipService.CreateBlueChip(captured.Value.Trim(), IsModifiedMode ? () =>
                {
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(captured);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    Render();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
            {
                var used = elements.Select(element => element.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                EditorAutoCompleteService.SetCatalogSuggestions(picker, catalog.Where(value => !used.Contains(value)));
            }
        }
        if (picker != null)
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected)) return;
                MajorGodCatalog.AddBonusGoalChild(goal, tag, selected);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                Render();
            };
        Render();
    }

    private static StackPanel CreateBonusFieldPair(string label, Control field)
    {
        field.Margin = new Thickness(0);
        var pair = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = EditorFormLayout.LabelGap,
            Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap),
            VerticalAlignment = VerticalAlignment.Top
        };
        pair.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        pair.Children.Add(field);
        return pair;
    }

    private TextBox CreateBonusElementNumberBox(XElement goal, XElement element, string defaultValue, ProtoUnitNumericKind kind)
    {
        var box = CreateSpecialElementNumberBox(element, defaultValue, kind);
        EditorValidationBindings.Bind(box, goal, element.Name.LocalName);
        box.Margin = new Thickness(0, 0, 4, 4);
        box.TextChanged += (_, _) => EnsureBonusChildAttached(goal, element);
        return box;
    }

    private AutoCompleteBox CreateBonusAttributeAutoComplete(XElement goal, XElement element, string attribute, IReadOnlyList<string> catalog)
        => CreateSpecialAttributeAutoComplete(element, attribute, catalog, () => EnsureBonusChildAttached(goal, element));

    private static void EnsureBonusChildAttached(XElement goal, XElement child)
    {
        if (child.Parent != null) return;
        goal.Add(child);
        MajorGodCatalog.NormalizeDirectChildIndentation(goal);
    }

    private void BuildBountyResourceEarningEditor(Panel host, XElement bounty)
    {
        BuildBountyOptionalNumber(host, bounty, "bountydamagegoal", "Damage Goal", ProtoUnitNumericKind.UnsignedFloat);
        BuildBountyReductionEditor(host, bounty);
        BuildBountyOptionalVfx(host, bounty);
        BuildBountyChipEditor(host, bounty, "excludedtarget", "Excluded target", _unitOrTypeNames);
        BuildBountyChipEditor(host, bounty, "excludedtechflag", "Excluded Techs Flag", _technologyFlagNames);
        BuildBountyRewards(host, bounty);
        BuildBountyTargetMultipliers(host, bounty);
        BuildBountyResearchEntries(host, bounty, "researchreward", "Research Reward");
        BuildBountyResearchEntries(host, bounty, "researchcostmultiplier", "Research Cost Multiplier");
    }

    private void BuildBountyOptionalNumber(
        Panel host,
        XElement bounty,
        string tag,
        string label,
        ProtoUnitNumericKind kind)
    {
        var element = MajorGodCatalog.FindDirectChild(bounty, tag);
        if (element == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton(label);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBountyElement(bounty, tag, "0");
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
            return;
        }

        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel(label));
        row.Children.Add(CreateSpecialElementNumberBox(element, "0", kind));
        if (IsModifiedMode) row.Children.Add(CreateSpecialChildRemoveButton(element));
        host.Children.Add(row);
    }

    private void BuildBountyReductionEditor(Panel host, XElement bounty)
    {
        var definitions = new[]
        {
            (Tag: "bountyreductionstart", Label: "Start at", Kind: ProtoUnitNumericKind.UnsignedInteger),
            (Tag: "bountyreductionend", Label: "End at", Kind: ProtoUnitNumericKind.UnsignedInteger),
            (Tag: "bountyreductionstrength", Label: "Factor", Kind: ProtoUnitNumericKind.UnsignedFloat)
        };
        var elements = definitions.Select(definition => MajorGodCatalog.FindDirectChild(bounty, definition.Tag)).ToList();
        if (elements.All(element => element == null))
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton("Favor Bounty Reduction");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                foreach (var definition in definitions)
                    MajorGodCatalog.AddBountyElement(bounty, definition.Tag, "0");
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
            return;
        }

        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel("Favor Bounty Reduction"));
        for (var index = 0; index < definitions.Length; index++)
        {
            row.Children.Add(CreateInlineLabel(definitions[index].Label));
            var element = elements[index] ?? new XElement(definitions[index].Tag);
            row.Children.Add(CreateSpecialElementNumberBox(element, "0", definitions[index].Kind));
        }
        if (IsModifiedMode)
        {
            var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                foreach (var element in elements.Where(element => element != null))
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(element!);
                MarkAndRebuildSpecialMechanics();
            };
            row.Children.Add(remove);
        }
        host.Children.Add(row);
    }

    private void BuildBountyOptionalVfx(Panel host, XElement bounty)
    {
        var vfx = MajorGodCatalog.FindDirectChild(bounty, "vfx");
        if (vfx == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton("VFX");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddBountyElement(bounty, "vfx");
                MarkAndRebuildSpecialMechanics();
            };
            host.Children.Add(add);
            return;
        }
        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel("VFX"));
        row.Children.Add(CreateSpecialValueAutoComplete(vfx, _protoUnitNames));
        if (IsModifiedMode) row.Children.Add(CreateSpecialChildRemoveButton(vfx));
        host.Children.Add(row);
    }

    private void BuildBountyChipEditor(
        Panel host,
        XElement bounty,
        string tag,
        string label,
        IReadOnlyList<string> catalog)
    {
        var chips = bounty.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!IsModifiedMode && chips.Count == 0) return;
        var row = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(CreateInlineLabel(label));
        AutoCompleteBox? picker = null;
        if (IsModifiedMode)
        {
            picker = ConfigureMajorGodField(new AutoCompleteBox
            {
                Width = 50,
                MaxWidth = 50,
                ItemsSource = catalog,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0
            }, EditorFieldWidthRole.ChipSearch);
            EditorValidationBindings.Bind(picker, bounty, tag, repeated: true);
            EditorAutoCompleteService.EnableDropdown(picker, () => _isPopulating, selectAllOnFirstClick: false);
            row.Children.Add(picker);
        }
        var chipHost = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(chipHost);

        void Render()
        {
            chips = bounty.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            chipHost.Children.Clear();
            foreach (var chipElement in chips)
            {
                var captured = chipElement;
                chipHost.Children.Add(EditorChipService.CreateBlueChip(captured.Value.Trim(), IsModifiedMode ? () =>
                {
                    MajorGodCatalog.RemoveElementAndAdjacentWhitespace(captured);
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    Render();
                } : null, readOnly: !IsModifiedMode));
            }
            if (picker != null)
            {
                var used = chips.Select(chip => chip.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                EditorAutoCompleteService.SetCatalogSuggestions(picker, catalog.Where(value => !used.Contains(value)));
            }
        }
        if (picker != null)
        {
            picker.SelectionChanged += (_, _) =>
            {
                if (!CanEditCurrent() || picker.SelectedItem is not string value || string.IsNullOrWhiteSpace(value)) return;
                MajorGodCatalog.AddBountyElement(bounty, tag, value);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                picker.SelectedItem = null;
                picker.Text = "";
                Render();
            };
        }
        Render();
        host.Children.Add(row);
    }

    private void BuildBountyRewards(Panel host, XElement bounty)
    {
        var rewards = bounty.Elements().Where(child =>
            child.Name.LocalName.Equals("bountyreward", StringComparison.OrdinalIgnoreCase)).ToList();
        if (rewards.Count == 0)
        {
            AddBountyEntryButton(host, bounty, "bountyreward", "Add reward");
            return;
        }
        var body = AddBountySubcontainer(host, "Reward");
        foreach (var reward in rewards)
        {
            var row = CreateBountyWrappingRow();
            AddBountyConditionAndTarget(row, reward);
            var condition = FindAttribute(reward, "condition")?.Value ?? "Damage";
            row.Children.Add(CreateBountyInlineLabel(condition.Equals("Destroy", StringComparison.OrdinalIgnoreCase)
                ? "destroyed grants"
                : "damaging enemy grants"));
            row.Children.Add(CreateBountyNumberBox(reward));
            row.Children.Add(CreateBountyResourceComboBox(reward));
            var multiply = new CheckBox
            {
                Content = "% Unit Cost",
                IsChecked = FindAttribute(reward, "multiplybyunitcost")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
                IsEnabled = IsModifiedMode,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 4)
            };
            EditorValidationBindings.Bind(multiply, reward, "@multiplybyunitcost");
            multiply.IsCheckedChanged += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                if (multiply.IsChecked == true) SetAttribute(reward, "multiplybyunitcost", "true");
                else RemoveAttribute(reward, "multiplybyunitcost");
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
            };
            row.Children.Add(multiply);
            AddBountyOptionalAttributeSelector(row, reward, "asspawnedunit", "Spawned as", _protoUnitNames);
            if (IsModifiedMode)
            {
                var removeReward = CreateSpecialChildRemoveButton(reward);
                removeReward.VerticalAlignment = VerticalAlignment.Center;
                // Match the wrapping field groups' bottom inset when centering.
                removeReward.Margin = new Thickness(EditorFormLayout.CrossGap, 0, 0, 4);
                row.Children.Add(removeReward);
            }
            body.Children.Add(row);
        }
        AddBountyEntryButton(body, bounty, "bountyreward", "Add reward");
    }

    private void BuildBountyTargetMultipliers(Panel host, XElement bounty)
    {
        var multipliers = bounty.Elements().Where(child =>
            child.Name.LocalName.Equals("bountytargetmultiplier", StringComparison.OrdinalIgnoreCase)).ToList();
        if (multipliers.Count == 0)
        {
            AddBountyEntryButton(host, bounty, "bountytargetmultiplier", "Add target multiplier");
            return;
        }
        var body = AddBountySubcontainer(host, "Target Multiplier");
        foreach (var multiplier in multipliers)
        {
            var row = CreateBountyWrappingRow();
            AddBountyConditionAndTarget(row, multiplier);
            row.Children.Add(CreateBountyInlineLabel("Multiplier"));
            row.Children.Add(CreateBountyNumberBox(multiplier));
            row.Children.Add(CreateBountyResourceComboBox(multiplier));
            var condition = FindAttribute(multiplier, "condition")?.Value ?? "Damage";
            if (condition.Equals("Destroy", StringComparison.OrdinalIgnoreCase))
            {
                AddBountyOptionalAttributeSelector(row, multiplier, "attackertype", "If Attacker is", _unitOrTypeNames);
                AddBountyOptionalRelativity(row, multiplier);
            }
            if (IsModifiedMode) row.Children.Add(CreateSpecialChildRemoveButton(multiplier));
            body.Children.Add(row);
        }
        AddBountyEntryButton(body, bounty, "bountytargetmultiplier", "Add target multiplier");
    }

    private void BuildBountyResearchEntries(Panel host, XElement bounty, string tag, string title)
    {
        var entries = bounty.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        var addLabel = title.Equals("Research Reward", StringComparison.OrdinalIgnoreCase)
            ? "Add research reward"
            : "Add research cost multiplier";
        if (entries.Count == 0)
        {
            AddBountyEntryButton(host, bounty, tag, addLabel);
            return;
        }
        var body = AddBountySubcontainer(host, title);
        foreach (var entry in entries)
        {
            var row = CreateBountyWrappingRow();
            row.Children.Add(CreateBountyInlineLabel("Tech type"));
            row.Children.Add(CreateBountyTechTypeSelector(entry));
            row.Children.Add(CreateBountyInlineLabel(tag.Equals("researchcostmultiplier", StringComparison.OrdinalIgnoreCase)
                ? "Multiplier"
                : "Grants"));
            row.Children.Add(CreateBountyNumberBox(entry));
            if (tag.Equals("researchcostmultiplier", StringComparison.OrdinalIgnoreCase))
                row.Children.Add(CreateBountyInlineLabel("For"));
            row.Children.Add(CreateBountyResourceComboBox(entry,
                includeCombatXp: !tag.Equals("researchcostmultiplier", StringComparison.OrdinalIgnoreCase)));
            if (IsModifiedMode) row.Children.Add(CreateSpecialChildRemoveButton(entry));
            body.Children.Add(row);
        }
        AddBountyEntryButton(body, bounty, tag, addLabel);
    }

    private static WrapPanel CreateBountyWrappingRow()
        => new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 2)
        };

    private static TextBlock CreateBountyInlineLabel(string text)
        => new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4)
        };

    private TextBox CreateBountyNumberBox(XElement element)
    {
        var box = CreateSpecialElementNumberBox(element, "0", ProtoUnitNumericKind.UnsignedFloat);
        box.Margin = new Thickness(0, 0, 4, 4);
        box.VerticalAlignment = VerticalAlignment.Top;
        return box;
    }

    private StackPanel AddBountySubcontainer(Panel host, string title)
    {
        var card = new Border { Classes = { "editor-card" }, Margin = new Thickness(0, 4, 0, 0) };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 2) });
        card.Child = body;
        host.Children.Add(card);
        return body;
    }

    private void AddBountyConditionAndTarget(Panel row, XElement entry)
    {
        var condition = FindAttribute(entry, "condition")?.Value ?? "Damage";
        var conditionBox = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = new[] { "Damage", "Destroy" },
            SelectedItem = condition.Equals("Destroy", StringComparison.OrdinalIgnoreCase) ? "Destroy" : "Damage",
            Width = 106,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.ShortEnum);
        EditorValidationBindings.Bind(conditionBox, entry, "@condition");
        conditionBox.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || conditionBox.SelectedItem is not string selected) return;
            SetAttribute(entry, "condition", selected);
            if (selected.Equals("Damage", StringComparison.OrdinalIgnoreCase))
            {
                RemoveAttribute(entry, "attackertype");
                RemoveAttribute(entry, "relativity");
            }
            MarkAndRebuildSpecialMechanics();
        };
        row.Children.Add(conditionBox);

        var usesProtoUnit = FindAttribute(entry, "protounit") != null;
        var targetMode = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = new[] { "Unit", "Unit type" },
            SelectedItem = usesProtoUnit ? "Unit" : "Unit type",
            Width = 100,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.ShortEnum);
        targetMode.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || targetMode.SelectedItem is not string selected) return;
            var oldValue = FindAttribute(entry, usesProtoUnit ? "protounit" : "unittype")?.Value ?? "";
            RemoveAttribute(entry, "protounit");
            RemoveAttribute(entry, "unittype");
            SetAttribute(entry, selected.Equals("Unit", StringComparison.OrdinalIgnoreCase) ? "protounit" : "unittype", oldValue);
            MarkAndRebuildSpecialMechanics();
        };
        row.Children.Add(targetMode);
        var targetAttribute = usesProtoUnit ? "protounit" : "unittype";
        var target = CreateSpecialAttributeAutoComplete(entry, targetAttribute,
            usesProtoUnit ? _protoUnitNames : _unitTypeNames);
        target.Margin = new Thickness(0, 0, 4, 4);
        row.Children.Add(target);
    }

    private ComboBox CreateBountyResourceComboBox(XElement entry, bool includeCombatXp = true)
    {
        var current = FindAttribute(entry, "combatxp") != null
            ? "Combat XP"
            : FindAttribute(entry, "resourcetype")?.Value ?? "Favor";
        var resources = includeCombatXp
            ? ProtoConstants.KnownResourceTypes.Append("Combat XP").ToList()
            : ProtoConstants.KnownResourceTypes.ToList();
        var selected = resources.FirstOrDefault(resource => resource.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? current;
        var box = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = resources,
            SelectedItem = selected,
            Width = 105,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4)
        }, EditorFieldWidthRole.ShortEnum);
        box.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || box.SelectedItem is not string resource) return;
            if (resource.Equals("Combat XP", StringComparison.OrdinalIgnoreCase))
            {
                RemoveAttribute(entry, "resourcetype");
                SetAttribute(entry, "combatxp", "");
            }
            else
            {
                RemoveAttribute(entry, "combatxp");
                SetAttribute(entry, "resourcetype", resource);
            }
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void AddBountyOptionalAttributeSelector(
        Panel row,
        XElement entry,
        string attributeName,
        string label,
        IReadOnlyList<string> catalog)
    {
        if (FindAttribute(entry, attributeName) == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton(label);
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                SetAttribute(entry, attributeName, "");
                MarkAndRebuildSpecialMechanics();
            };
            row.Children.Add(add);
            return;
        }
        var pair = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 0, 4, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        pair.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, EditorFormLayout.LabelGap, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var selector = CreateSpecialAttributeAutoComplete(entry, attributeName, catalog);
        selector.Margin = new Thickness(0);
        pair.Children.Add(selector);
        if (IsModifiedMode)
        {
            var remove = EditorRemoveButtons.Field();
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                RemoveAttribute(entry, attributeName);
                MarkAndRebuildSpecialMechanics();
            };
            pair.Children.Add(remove);
        }
        row.Children.Add(pair);
    }

    private void AddBountyOptionalRelativity(Panel row, XElement entry)
    {
        var relativity = FindAttribute(entry, "relativity");
        if (relativity == null)
        {
            if (!IsModifiedMode) return;
            var add = CreateAddButton("Relativity");
            add.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                SetAttribute(entry, "relativity", "absolute");
                MarkAndRebuildSpecialMechanics();
            };
            row.Children.Add(add);
            return;
        }
        row.Children.Add(CreateBountyInlineLabel("Relativity"));
        var values = new[] { "Absolute", "BasePercent", "Percent" };
        var selected = values.FirstOrDefault(value => value.Equals(relativity.Value, StringComparison.OrdinalIgnoreCase)) ?? "Absolute";
        var box = ConfigureMajorGodField(new ComboBox { ItemsSource = values, SelectedItem = selected, Width = 115, IsEnabled = IsModifiedMode }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(box, entry, "@relativity");
        box.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || box.SelectedItem is not string value) return;
            SetAttribute(entry, "relativity", value.ToLowerInvariant());
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(box);
        if (IsModifiedMode)
        {
            var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                RemoveAttribute(entry, "relativity");
                MarkAndRebuildSpecialMechanics();
            };
            row.Children.Add(remove);
        }
    }

    private AutoCompleteBox CreateBountyTechTypeSelector(XElement entry)
    {
        var values = new[] { "All" }.Concat(_techTypeNames.Where(value =>
            !value.Equals("All", StringComparison.OrdinalIgnoreCase)));
        var raw = FindAttribute(entry, "techtype")?.Value ?? "all";
        var box = ConfigureMajorGodField(new AutoCompleteBox
        {
            Text = raw.Equals("all", StringComparison.OrdinalIgnoreCase) ? "All" : raw,
            ItemsSource = values,
            Width = 170,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode,
            Margin = new Thickness(0, 0, 4, 4),
            ItemTemplate = new FuncDataTemplate<string>((item, _) => new TextBlock
            {
                Text = item ?? "",
                FontWeight = string.Equals(item, "All", StringComparison.OrdinalIgnoreCase) ? FontWeight.Bold : FontWeight.Normal
            })
        }, EditorFieldWidthRole.Reference);
        EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating);
        EditorValidationBindings.Bind(box, entry, "@techtype");
        EditorAutoCompleteService.SetCatalogSuggestions(box, values, preserveOrder: true);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            var value = box.Text?.Trim() ?? "";
            SetAttribute(entry, "techtype", value.Equals("All", StringComparison.OrdinalIgnoreCase) ? "all" : value);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void AddBountyEntryButton(Panel body, XElement bounty, string tag, string label)
    {
        if (!IsModifiedMode) return;
        var add = CreateAddButton(label);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            MajorGodCatalog.AddBountyElement(bounty, tag);
            MarkAndRebuildSpecialMechanics();
        };
        body.Children.Add(add);
    }

    private void BuildOnCastPowerCostFactorEditor(Panel host, XElement factor)
    {
        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel("Power Cost Reduction Factor on Cast"));
        row.Children.Add(CreateSpecialElementNumberBox(factor, "0", ProtoUnitNumericKind.ClampZeroToOne));
        host.Children.Add(row);
    }

    private static TextBlock CreateSpecialSubheading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(4, 6, 0, 0)
    };

    private XElement GetOrCreateSpecialChild(XElement parent, string tag)
        => MajorGodCatalog.FindDirectChild(parent, tag) ?? new XElement(tag);

    private AutoCompleteBox CreateSpecialProtoUnitBox(XElement element)
        => CreateSpecialValueAutoComplete(element, _protoUnitNames);

    private AutoCompleteBox CreateSpecialValueAutoComplete(
        XElement element,
        IReadOnlyList<string> items,
        Action? beforeChange = null)
    {
        var box = ConfigureMajorGodField(new AutoCompleteBox
        {
            ItemsSource = items,
            Text = element.Value.Trim(),
            Width = 200,
            MaxWidth = 200,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.Reference);
        EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating);
        EditorValidationBindings.Bind(box, element);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            if (string.Equals(element.Value, box.Text ?? "", StringComparison.Ordinal)) return;
            beforeChange?.Invoke();
            EnsureSpecialChildAttached(element);
            element.Value = box.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private AutoCompleteBox CreateSpecialAttributeAutoComplete(
        XElement element,
        string attributeName,
        IReadOnlyList<string> items,
        Action? beforeChange = null)
    {
        var box = ConfigureMajorGodField(new AutoCompleteBox
        {
            ItemsSource = items,
            Text = FindAttribute(element, attributeName)?.Value ?? "",
            Width = 200,
            MaxWidth = 200,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.Reference);
        EditorAutoCompleteService.EnableDropdown(box, () => _isPopulating);
        EditorValidationBindings.Bind(box, element, "@" + attributeName);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            if (string.Equals(FindAttribute(element, attributeName)?.Value ?? "", box.Text ?? "", StringComparison.Ordinal)) return;
            beforeChange?.Invoke();
            SetAttribute(element, attributeName, box.Text ?? "");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private ComboBox CreateResourceComboBox(XElement element, string attributeName)
    {
        var current = FindAttribute(element, attributeName)?.Value.Trim() ?? "";
        var options = ProtoConstants.KnownResourceTypes
            .Append(current)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = options.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase));
        var box = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = options,
            SelectedItem = selected,
            Width = 90,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.ShortEnum);
        EditorValidationBindings.Bind(box, element, "@" + attributeName);
        box.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || box.SelectedItem is not string resource) return;
            SetAttribute(element, attributeName, resource);
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private void EnsureBuildingChainAnchorAttached(XElement buildingChain, XElement anchor)
    {
        if (anchor.Parent != null) return;
        buildingChain.AddFirst(anchor);
        MajorGodCatalog.NormalizeDirectChildIndentation(buildingChain);
    }

    private TextBox CreateSpecialElementNumberBox(XElement element, string defaultValue, ProtoUnitNumericKind kind)
    {
        var box = ConfigureMajorGodField(new TextBox { Text = element.Parent == null ? defaultValue : element.Value, Width = 60, MaxWidth = 60, IsEnabled = IsModifiedMode }, EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(box, element);
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorNumericInputBehavior.AttachRule(box, kind);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            EnsureSpecialChildAttached(element);
            element.Value = box.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private TextBox CreateSpecialAttributeNumberBox(XElement element, string attributeName, string defaultValue, ProtoUnitNumericKind kind)
        => CreateStartingUnitNumberBox(element, attributeName, defaultValue, kind);

    private void EnsureSpecialChildAttached(XElement child)
    {
        if (child.Parent != null || _currentEntry == null) return;
        var parentTag = child.Name.LocalName is "chance" or "delay" or "protounit" ? "shades" : "";
        var parent = MajorGodCatalog.FindDirectChild(_currentEntry, parentTag);
        if (parent != null)
            MajorGodCatalog.AddShadesChild(parent, child.Name.LocalName).ReplaceWith(child);
    }

    private Button CreateAddButton(string label) => new()
    {
        Content = label,
        Classes = { "add-component" },
        Margin = new Thickness(4, 0, 0, 0),
        Padding = new Thickness(8, 3)
    };

    private Button CreateSpecialChildRemoveButton(XElement child, Action? refresh = null)
    {
        var remove = EditorRemoveButtons.Field();
        remove.Click += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            MajorGodCatalog.RemoveElementAndAdjacentWhitespace(child);
            if (refresh != null) refresh();
            else MarkAndRebuildSpecialMechanics();
        };
        return remove;
    }

    private void MarkAndRebuildSpecialMechanics()
    {
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildSpecialMechanicsEditor();
    }

    private void MarkAndRebuildBonusUnitSpawning()
    {
        if (_refreshBonusUnitSpawningEditor != null)
            _refreshBonusUnitSpawningEditor();
        else
            MarkAndRebuildSpecialMechanics();
    }

    private void ValidateShades(XElement shades)
    {
        ValidateSingleSpecialProtoUnit(shades, "Shades Unit");
        var targets = shades.Elements().Where(child => child.Name.LocalName.Equals("targetunit", StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0) throw ValidationSourceLocation.Attach(new InvalidDataException("Shades requires at least one From unit."), shades, "targetunit");
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ValidationSourceLocation.Validate(target, () => ValidateUnitOrTypeName(target.Value, "Shades From"));
            if (!usedTargets.Add(target.Value.Trim()))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Shades From '{target.Value.Trim()}' can only occur once."), target);
        }
        ValidateBoundedFloatElement(shades, "chance", "Shades Probability", 0, 1);
        ValidateBoundedFloatElement(shades, "delay", "Shades Delay", 0, double.MaxValue);
        if (MajorGodCatalog.FindDirectChild(shades, "maxqueued") is { } maxQueued &&
            (!int.TryParse(maxQueued.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max < 1))
            throw ValidationSourceLocation.Attach(new InvalidDataException("Shades Max Queue must be an integer greater than or equal to one."), maxQueued);
    }

    private void ValidateTerrainCreeps(XElement terrainCreeps)
    {
        var creeps = terrainCreeps.Elements().Where(child =>
            child.Name.LocalName.Equals("terraincreep", StringComparison.OrdinalIgnoreCase)).ToList();
        if (creeps.Count == 0)
            throw new InvalidDataException("TerrainCreeps requires at least one Terrain Creep entry.");

        foreach (var creep in creeps)
        {
            var creepType = FindAttribute(creep, "creep")?.Value.Trim() ?? "";
            if (creepType.Length == 0 ||
                (_terrainCreepNames.Count > 0 && !_terrainCreepNames.Contains(creepType, StringComparer.OrdinalIgnoreCase)))
            {
                throw ValidationSourceLocation.Attach(new InvalidDataException("Terrain Creep must reference an existing terrain creep type."), creep, "@creep");
            }

            ValidateNonNegativeFloatAttribute(creep, "maxradius", "Terrain Creep Radius");
            ValidateNonNegativeFloatAttribute(creep, "growthrate", "Terrain Creep Growth Rate");
            ValidateNonNegativeFloatAttribute(creep, "decayrate", "Terrain Creep Decay Rate");
            ValidateNonNegativeFloatAttribute(creep, "minupdateinterval", "Terrain Creep Minimum Update Interval");
            ValidateNonNegativeFloatAttribute(creep, "maxupdateinterval", "Terrain Creep Maximum Update Interval");
            var minimumInterval = ParseNonNegativeFloatAttribute(creep, "minupdateinterval");
            var maximumInterval = ParseNonNegativeFloatAttribute(creep, "maxupdateinterval");
            if (minimumInterval > maximumInterval)
                throw ValidationSourceLocation.Attach(new InvalidDataException("Terrain Creep Minimum Update Interval cannot exceed Maximum Update Interval."), creep, "@minupdateinterval");
            if (FindAttribute(creep, "strength") != null)
                ValidateNonNegativeFloatAttribute(creep, "strength", "Terrain Creep Strength");

            if (FindAttribute(creep, "buildability") is { } buildability &&
                !MajorGodCatalog.TerrainCreepBuildabilityValues.Contains(buildability.Value.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw ValidationSourceLocation.Attach(new InvalidDataException("Terrain Creep Buildability must be a valid buildability mode."), creep, "@buildability");
            }
            if (FindAttribute(creep, "preventplacement") is { } preventPlacement)
                ValidationSourceLocation.Validate(preventPlacement, () => ValidateUnitOrTypeName(preventPlacement.Value, "Terrain Creep Prevent placement"));

            var generators = creep.Elements().Where(child =>
                child.Name.LocalName.Equals("protounit", StringComparison.OrdinalIgnoreCase)).ToList();
            if (generators.Count == 0)
                throw ValidationSourceLocation.Attach(new InvalidDataException("Terrain Creep requires at least one Generator."), creep, "protounit");
            var usedGenerators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var generator in generators)
            {
                ValidationSourceLocation.Validate(generator, () => ValidateProtoUnitName(generator.Value, "Terrain Creep Generator"));
                if (!usedGenerators.Add(generator.Value.Trim()))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Terrain Creep Generator '{generator.Value.Trim()}' can only occur once."), generator);
            }
        }
    }

    private void ValidateTimeShifting(XElement timeShifting)
    {
        var maxRaw = FindAttribute(timeShifting, "maxconcurrenttimeshifts")?.Value ?? "";
        if (!int.TryParse(maxRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max < 1)
            throw ValidationSourceLocation.Attach(new InvalidDataException("Max Concurrent time shifts must be an integer greater than or equal to one."), timeShifting, "@maxconcurrenttimeshifts");
        var units = timeShifting.Elements().Where(child => child.Name.LocalName.Equals("protounit", StringComparison.OrdinalIgnoreCase)).ToList();
        if (units.Count == 0) throw new InvalidDataException("TimeShifting requires at least one Unit.");
        foreach (var unit in units)
        {
            ValidationSourceLocation.Validate(unit, () => ValidateProtoUnitName(unit.Value, "TimeShifting Unit"));
            ValidateNonNegativeFloatAttribute(unit, "costratio", "TimeShifting Cost Ratio");
            ValidateNonNegativeFloatAttribute(unit, "timeratio", "TimeShifting Time Ratio");
        }
    }

    private void ValidateBuildingChain(XElement buildingChain)
    {
        var anchors = buildingChain.Elements().Where(child =>
            child.Name.LocalName.Equals("anchor", StringComparison.OrdinalIgnoreCase)).ToList();
        if (anchors.Count != 1)
            throw new InvalidDataException("BuildingChain Anchor must occur exactly once.");
        var anchor = anchors[0];
        ValidationSourceLocation.Validate(anchor, () => ValidateUnitOrTypeName(anchor.Value, "BuildingChain Anchor"));
        ValidationSourceLocation.Validate(anchor, "@vfx", () => ValidateProtoUnitName(FindAttribute(anchor, "vfx")?.Value ?? "", "BuildingChain VFX"));

        ValidateBuildingChainVfxGroup(buildingChain, "abundancevfx", "Abundance VFX");
        ValidateBuildingChainVfxGroup(buildingChain, "autobuildvfx", "Auto Build VFX");

        var resourceGenerationEntries = buildingChain.Elements().Where(child =>
            child.Name.LocalName.Equals("resourcegeneration", StringComparison.OrdinalIgnoreCase)).ToList();
        if (resourceGenerationEntries.Count > 1)
            throw new InvalidDataException("BuildingChain Resource generation can only occur once.");
        if (resourceGenerationEntries.FirstOrDefault() is { } resourceGeneration)
        {
            var tiers = resourceGeneration.Elements().Where(child =>
                child.Name.LocalName.Equals("tilerateperminutetier", StringComparison.OrdinalIgnoreCase)).ToList();
            if (tiers.Count == 0)
                throw new InvalidDataException("BuildingChain Resource generation requires at least one rate tier.");
            foreach (var tier in tiers)
            {
                ValidationSourceLocation.Validate(tier, () => ValidateNonNegativeFloatValue(tier.Value, "Rate tile/minute"));
                ValidateResourceAttribute(tier, "resource", "Resource generation resource");
                var minimumTiles = FindAttribute(tier, "mintiles")?.Value ?? "";
                if (!int.TryParse(minimumTiles, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
                    throw ValidationSourceLocation.Attach(new InvalidDataException("When number of tiles > must be an integer greater than or equal to zero."), tier, "@mintiles");
            }
        }

        foreach (var building in buildingChain.Elements().Where(child =>
                     child.Name.LocalName.Equals("chainablebuilding", StringComparison.OrdinalIgnoreCase)))
        {
            ValidationSourceLocation.Validate(building, () => ValidateProtoUnitName(building.Value, "Chainable Building"));
            ValidateNonNegativeFloatAttribute(building, "chainradius", "Chain Radius");
            ValidateResourceAttribute(building, "generationresource", "Chainable Building Generates");
        }
    }

    private void ValidateBuildingChainVfxGroup(XElement buildingChain, string tag, string label)
    {
        var entries = buildingChain.Elements().Where(child =>
            child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count > 1)
            throw new InvalidDataException($"BuildingChain {label} can only occur once.");
        if (entries.FirstOrDefault() is not { } group) return;
        ValidationSourceLocation.Validate(group, "@small", () => ValidateProtoUnitName(FindAttribute(group, "small")?.Value ?? "", $"{label} Small"));
        ValidationSourceLocation.Validate(group, "@medium", () => ValidateProtoUnitName(FindAttribute(group, "medium")?.Value ?? "", $"{label} Medium"));
        ValidationSourceLocation.Validate(group, "@large", () => ValidateProtoUnitName(FindAttribute(group, "large")?.Value ?? "", $"{label} Large"));
    }

    private static void ValidateCombatXp(XElement combatXp)
    {
        var tiers = combatXp.Elements().Where(child =>
            child.Name.LocalName.Equals("tier", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tiers.Count == 0)
            throw new InvalidDataException("CombatXP requires at least one Tier.");
        int? previousValue = null;
        for (var index = 0; index < tiers.Count; index++)
        {
            if (!int.TryParse(tiers[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw ValidationSourceLocation.Attach(new InvalidDataException($"CombatXP Tier {index + 1} must be an integer greater than or equal to zero."), tiers[index]);
            if (previousValue.HasValue && value <= previousValue.Value)
                throw ValidationSourceLocation.Attach(new InvalidDataException($"CombatXP Tier {index + 1} must be greater than Tier {index}."), tiers[index]);
            previousValue = value;
        }
    }

    private void ValidateBonusUnitSpawning(XElement bonus)
    {
        var goals = bonus.Elements().Where(IsBonusGoal).ToList();
        if (goals.Count == 0)
            throw new InvalidDataException("BonusUnitSpawning requires at least one Goal.");
        var usedGoalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var goal in goals)
        {
            var label = $"Bonus Unit Spawning {BonusGoalType(goal)} Goal";
            var goalName = FindAttribute(goal, "name")?.Value.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(goalName))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} requires a Name."), goal, "@name");
            if (!usedGoalNames.Add(goalName))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Bonus Unit Spawning Goal name '{goalName}' can only occur once."), goal, "@name");
            var tracking = FindAttribute(goal, "rewardtracking")?.Value ?? "Single";
            if (!new[] { "Single", "PerPossibleReward" }.Contains(tracking, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Tracking must be Single or PerPossibleReward."), goal, "@rewardtracking");
            if (BonusGoalType(goal) == "Resource" && !tracking.Equals("Single", StringComparison.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException("Resource Goal Tracking must be Single."), goal, "@rewardtracking");
            if (MajorGodCatalog.FindDirectChild(goal, "active") is { } active &&
                !active.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) &&
                !active.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Active must be true or false."), active);

            var amount = MajorGodCatalog.FindDirectChild(goal, "goalamount") ??
                         throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} requires a Goal Amount."), goal, "goalamount");
            if (!decimal.TryParse(amount.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amountValue) ||
                amountValue < 1 || amountValue != decimal.Truncate(amountValue))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Goal Amount must be an integer greater than or equal to one."), amount);
            var rewards = goal.Elements().Where(child => child.Name.LocalName.Equals("reward", StringComparison.OrdinalIgnoreCase)).ToList();
            if (rewards.Count == 0)
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} requires at least one Reward."), goal, "reward");
            ValidateUniqueBonusChips(goal, "reward", "Reward");
            ValidateUniqueBonusChips(goal, "excludefromreward", "Excluded from reward");
            var excludedTargets = goal.Elements().Where(child => child.Name.LocalName.Equals("excludedtarget", StringComparison.OrdinalIgnoreCase)).ToList();
            if (BonusGoalType(goal) != "Damage" && excludedTargets.Count > 0)
                throw new InvalidDataException($"{label} Excluded Targets can only be used by Damage goals.");
            if (BonusGoalType(goal) == "Damage")
                ValidateUniqueBonusChips(goal, "excludedtarget", "Excluded Targets");

            var spawnLocations = goal.Elements().Where(child => child.Name.LocalName.Equals("spawnlocation", StringComparison.OrdinalIgnoreCase)).ToList();
            if (spawnLocations.Count != 1)
                throw new InvalidDataException($"{label} requires exactly one Spawn Location.");
            ValidateBonusSpawnLocation(goal, spawnLocations[0], label);

            foreach (var contributor in goal.Elements().Where(child => child.Name.LocalName.Equals("contributor", StringComparison.OrdinalIgnoreCase)))
            {
                ValidationSourceLocation.Validate(contributor, () => ValidateNonNegativeFloatValue(contributor.Value, $"{label} Contributor"));
                var unit = FindAttribute(contributor, "unit");
                var type = FindAttribute(contributor, "type");
                var resource = FindAttribute(contributor, "resource");
                var identityCount = new[] { unit, type, resource }.Count(attribute => attribute != null);
                if (identityCount != 1)
                    throw new InvalidDataException($"{label} Contributor must have exactly one Unit, Unit type, or Resource.");
                if (unit != null) ValidationSourceLocation.Validate(unit, () => ValidateProtoUnitName(unit.Value, $"{label} Contributor Unit"));
                if (type != null) ValidationSourceLocation.Validate(type, () => ValidateUnitTypeName(type.Value, $"{label} Contributor Unit type"));
                if (resource != null)
                {
                    if (BonusGoalType(goal) != "Resource")
                        throw new InvalidDataException($"{label} Resource contributors can only be used by Resource goals.");
                    if (!ProtoConstants.KnownResourceTypes.Contains(resource.Value, StringComparer.OrdinalIgnoreCase))
                        throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Contributor Resource must be valid."), contributor, "@resource");
                }
            }

            var seenCosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cost in goal.Elements().Where(child => child.Name.LocalName.Equals("rewardpointcost", StringComparison.OrdinalIgnoreCase)))
            {
                var resource = FindAttribute(cost, "resourcetype")?.Value ?? "";
                if (!ProtoConstants.KnownResourceTypes.Contains(resource, StringComparer.OrdinalIgnoreCase) || !seenCosts.Add(resource))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Reward Point Cost resources must be valid and unique."), cost, "@resourcetype");
                ValidationSourceLocation.Validate(cost, () => ValidateNonNegativeFloatValue(cost.Value, $"{label} {resource} Reward Point Cost"));
            }

            foreach (var flag in BonusGoalFlags)
            {
                if (MajorGodCatalog.FindDirectChild(goal, flag.Tag) is { } element &&
                    !element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{label} {flag.Label} must be true when present.");
            }
            ValidateBonusDisplayInfo(goal, label);
        }
    }

    private void ValidateBonusDisplayInfo(XElement goal, string label)
    {
        var displayInfo = MajorGodCatalog.FindDirectChild(goal, "displayinfo");
        var displayOnHud = MajorGodCatalog.FindDirectChild(goal, "displayonhud");
        if (displayInfo == null && displayOnHud == null) return;
        if (BonusGoalType(goal) != "Resource")
            throw new InvalidDataException($"{label} Display info can only be used by Resource goals.");
        if (displayInfo == null || displayOnHud == null || !displayOnHud.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} Display info requires DisplayOnHUD set to true.");
        foreach (var tag in new[] { "displaynameid", "rolloverid", "progresstooltipid", "nextunittooltipid", "notificationmessageid" })
            if (string.IsNullOrWhiteSpace(MajorGodCatalog.FindDirectChild(displayInfo, tag)?.Value))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Display info requires {tag}."), displayInfo, tag);
        var progressType = MajorGodCatalog.FindDirectChild(displayInfo, "progresstype")?.Value ?? "";
        if (!new[] { "Default", "ShieldBlessing", "SpawnReward" }.Contains(progressType, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Progress Type must be Default, ShieldBlessing, or SpawnReward."), displayInfo, "progresstype");
        ValidationSourceLocation.Validate(displayInfo, "icon", () => GodModuleFieldValidation.ValidateCatalogAsset(
            MajorGodCatalog.FindDirectChild(displayInfo, "icon")?.Value ?? "",
            _iconPaths,
            $"{label} Display info Icon"));
        if (MajorGodCatalog.FindDirectChild(displayInfo, "notificationsound") == null)
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Display info requires a Notification Sound field."), displayInfo, "notificationsound");
    }

    private void ValidateUniqueBonusChips(XElement goal, string tag, string label)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in goal.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationSourceLocation.Validate(element, () => ValidateUnitOrTypeName(element.Value, label));
            if (!used.Add(element.Value.Trim()))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} '{element.Value.Trim()}' can only occur once."), element);
        }
    }

    private void ValidateBonusSpawnLocation(XElement goal, XElement spawn, string label)
    {
        var type = FindAttribute(spawn, "type")?.Value ?? "";
        var allowed = BonusGoalType(goal) switch
        {
            "Death" => new[] { "FromBuilding", "DeathLocation" },
            "Resource" => new[] { "FromBuilding" },
            _ => new[] { "DamageTarget", "DamageDealer", "FromBuilding" }
        };
        if (!allowed.Contains(type, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Spawn Location is not valid for its goal type."), spawn, "@type");
        if (!type.Equals("FromBuilding", StringComparison.OrdinalIgnoreCase)) return;
        ValidationSourceLocation.Validate(spawn, "@landbuilding", () => ValidateUnitOrTypeName(FindAttribute(spawn, "landbuilding")?.Value ?? "", $"{label} Spawn Location Land"));
        ValidationSourceLocation.Validate(spawn, "@waterbuilding", () => ValidateUnitOrTypeName(FindAttribute(spawn, "waterbuilding")?.Value ?? "", $"{label} Spawn Location Water"));
    }

    private void ValidateBountyResourceEarning(XElement bounty)
    {
        if (MajorGodCatalog.FindDirectChild(bounty, "active") is { } active &&
            !active.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) &&
            !active.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException("Bounty Resource Earning Active must be true or false."), active);

        if (MajorGodCatalog.FindDirectChild(bounty, "bountydamagegoal") is { } damageGoal)
            ValidationSourceLocation.Validate(damageGoal, () => ValidateNonNegativeFloatValue(damageGoal.Value, "Bounty Damage Goal"));
        if (MajorGodCatalog.FindDirectChild(bounty, "vfx") is { } vfx)
            ValidationSourceLocation.Validate(vfx, () => ValidateProtoUnitName(vfx.Value, "Bounty VFX"));

        var reduction = new[] { "bountyreductionstart", "bountyreductionend", "bountyreductionstrength" }
            .Select(tag => MajorGodCatalog.FindDirectChild(bounty, tag)).ToList();
        if (reduction.Any(element => element != null))
        {
            if (reduction.Any(element => element == null))
                throw new InvalidDataException("Favor Bounty Reduction requires Start at, End at, and Factor.");
            ValidationSourceLocation.Validate(reduction[0]!, () => ValidateNonNegativeIntegerLike(reduction[0]!.Value, "Favor Bounty Reduction Start at"));
            ValidationSourceLocation.Validate(reduction[1]!, () => ValidateNonNegativeIntegerLike(reduction[1]!.Value, "Favor Bounty Reduction End at"));
            ValidationSourceLocation.Validate(reduction[2]!, () => ValidateNonNegativeFloatValue(reduction[2]!.Value, "Favor Bounty Reduction Factor"));
            var reductionStart = decimal.Parse(reduction[0]!.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            var reductionEnd = decimal.Parse(reduction[1]!.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            if (reductionStart > reductionEnd)
                throw ValidationSourceLocation.Attach(new InvalidDataException("Favor Bounty Reduction Start at cannot exceed End at."), reduction[0]!);
        }

        ValidateUniqueBountyChips(bounty, "excludedtarget", "Excluded target", ValidateUnitOrTypeName);
        ValidateUniqueBountyChips(bounty, "excludedtechflag", "Excluded Techs Flag", ValidateTechnologyFlagName);

        foreach (var reward in bounty.Elements().Where(child =>
                     child.Name.LocalName.Equals("bountyreward", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateBountyConditionAndTarget(reward, "Reward");
            ValidationSourceLocation.Validate(reward, () => ValidateNonNegativeFloatValue(reward.Value, "Reward amount"));
            ValidateBountyResource(reward, "Reward");
            if (FindAttribute(reward, "multiplybyunitcost") is { } multiply &&
                !multiply.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException("Multiply by Unit Cost must be true when present."), reward, "@multiplybyunitcost");
            if (FindAttribute(reward, "asspawnedunit") is { } spawned)
                ValidationSourceLocation.Validate(spawned, () => ValidateProtoUnitName(spawned.Value, "Spawned as"));
        }

        foreach (var multiplier in bounty.Elements().Where(child =>
                     child.Name.LocalName.Equals("bountytargetmultiplier", StringComparison.OrdinalIgnoreCase)))
        {
            var condition = ValidateBountyConditionAndTarget(multiplier, "Target Multiplier");
            ValidationSourceLocation.Validate(multiplier, () => ValidateNonNegativeFloatValue(multiplier.Value, "Target Multiplier"));
            ValidateBountyResource(multiplier, "Target Multiplier");
            if (FindAttribute(multiplier, "attackertype") is { } attacker)
            {
                if (!condition.Equals("Destroy", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("If Attacker is can only be used for Destroy target multipliers.");
                ValidationSourceLocation.Validate(attacker, () => ValidateUnitOrTypeName(attacker.Value, "Target Multiplier If Attacker is"));
            }
            if (FindAttribute(multiplier, "relativity") is { } relativity &&
                !new[] { "absolute", "basepercent", "percent" }.Contains(relativity.Value, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException("Target Multiplier Relativity must be Absolute, BasePercent, or Percent."), multiplier, "@relativity");
        }

        foreach (var tag in new[] { "researchreward", "researchcostmultiplier" })
        {
            var label = tag.Equals("researchreward", StringComparison.OrdinalIgnoreCase)
                ? "Research Reward"
                : "Research Cost Multiplier";
            foreach (var entry in bounty.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                ValidationSourceLocation.Validate(entry, "@techtype", () => ValidateTechType(FindAttribute(entry, "techtype")?.Value ?? "", label));
                ValidationSourceLocation.Validate(entry, () => ValidateNonNegativeFloatValue(entry.Value, label));
                ValidateBountyResource(entry, label);
                if (tag.Equals("researchcostmultiplier", StringComparison.OrdinalIgnoreCase) &&
                    FindAttribute(entry, "combatxp") != null)
                    throw new InvalidDataException("Research Cost Multiplier cannot use Combat XP.");
            }
        }
    }

    private void ValidateUniqueBountyChips(
        XElement bounty,
        string tag,
        string label,
        Action<string, string> validateValue)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in bounty.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
        {
            var value = element.Value.Trim();
            ValidationSourceLocation.Validate(element, () => validateValue(value, label));
            if (!used.Add(value))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} '{value}' can only occur once."), element);
        }
    }

    private string ValidateBountyConditionAndTarget(XElement entry, string label)
    {
        var condition = FindAttribute(entry, "condition")?.Value ?? "";
        if (!new[] { "Damage", "Destroy" }.Contains(condition, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} Type must be Damage or Destroy."), entry, "@condition");
        var protoUnit = FindAttribute(entry, "protounit");
        var unitType = FindAttribute(entry, "unittype");
        if ((protoUnit == null) == (unitType == null))
            throw new InvalidDataException($"{label} must select either Unit or Unit type.");
        if (protoUnit != null) ValidationSourceLocation.Validate(protoUnit, () => ValidateProtoUnitName(protoUnit.Value, $"{label} Unit"));
        else ValidationSourceLocation.Validate(unitType!, () => ValidateUnitTypeName(unitType!.Value, $"{label} Unit type"));
        return condition;
    }

    private static void ValidateBountyResource(XElement entry, string label)
    {
        var combatXp = FindAttribute(entry, "combatxp");
        var resource = FindAttribute(entry, "resourcetype");
        if ((combatXp == null) == (resource == null))
            throw new InvalidDataException($"{label} must grant one resource or Combat XP.");
        if (resource != null && !ProtoConstants.KnownResourceTypes.Contains(resource.Value, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} must grant a valid resource."), entry, "@resourcetype");
    }

    private static void ValidateNonNegativeIntegerLike(string raw, string label)
    {
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ||
            value < 0 || value != decimal.Truncate(value))
            throw new InvalidDataException($"{label} must be an integer greater than or equal to zero.");
    }

    private static double ParseNonNegativeFloatAttribute(XElement element, string attributeName)
        => double.Parse(FindAttribute(element, attributeName)!.Value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private void ValidateTechnologyFlagName(string raw, string label)
    {
        var value = raw.Trim();
        if (value.Length == 0) throw new InvalidDataException($"{label} is required.");
        if (_technologyFlagNames.Count > 0 && !_technologyFlagNames.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} must reference an existing technology flag.");
    }

    private void ValidateTechType(string raw, string label)
    {
        var value = raw.Trim();
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return;
        if (value.Length == 0 || (_techTypeNames.Count > 0 && !_techTypeNames.Contains(value, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"{label} must reference an existing Tech Type or All.");
    }

    private void ValidateUnitTypeName(string raw, string label)
    {
        var name = raw.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"{label} is required.");
        if (_unitTypeNames.Count > 0 && !_unitTypeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} must reference an existing UnitType.");
    }

    private void ValidateUnitOrTypeName(string raw, string label)
    {
        var name = raw.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{label} is required.");
        if (_unitOrTypeNames.Count > 0 && !_unitOrTypeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} must reference an existing ProtoUnit or UnitType.");
    }

    private static void ValidateResourceAttribute(XElement element, string attributeName, string label)
    {
        var resource = FindAttribute(element, attributeName)?.Value.Trim() ?? "";
        if (!ProtoConstants.KnownResourceTypes.Contains(resource, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} must reference a valid resource."), element, "@" + attributeName);
    }

    private void ValidateSingleSpecialProtoUnit(XElement parent, string label)
    {
        var units = parent.Elements().Where(child => child.Name.LocalName.Equals("protounit", StringComparison.OrdinalIgnoreCase)).ToList();
        if (units.Count == 0) throw ValidationSourceLocation.Attach(new InvalidDataException($"{label}: select a Unit, or remove the incomplete section before saving."), parent, "protounit");
        if (units.Count > 1) throw new InvalidDataException($"{label}: only one Unit is allowed; remove the duplicate <protounit> entries in Edit XML.");
        try { ValidationSourceLocation.Validate(units[0], () => ValidateProtoUnitName(units[0].Value, label)); }
        catch (InvalidDataException exception) { throw ValidationSourceLocation.Attach(exception, units[0]); }
    }

    private void ValidateProtoUnitName(string raw, string label)
    {
        var name = raw.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"{label} is required.");
        if (_protoUnitNames.Count > 0 && !_protoUnitNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} must reference an existing ProtoUnit.");
    }

    private static void ValidateBoundedFloatElement(XElement parent, string tag, string label, double minimum, double maximum)
    {
        var raw = MajorGodCatalog.FindDirectChild(parent, tag)?.Value ?? "";
        try { ValidateBoundedFloatValue(raw, label, minimum, maximum); }
        catch (InvalidDataException exception) { throw ValidationSourceLocation.Attach(exception, parent, tag); }
    }

    private static void ValidateBoundedFloatValue(string raw, string label, double minimum, double maximum)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
    }

    private void SetReadOnlyAge(string xmlAge)
    {
        var normalized = string.IsNullOrWhiteSpace(xmlAge) ? "ArchaicAge" : xmlAge.Trim();
        var display = normalized.EndsWith("Age", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3] + " Age"
            : normalized;
        _ageBox.ItemsSource = new[] { display };
        _ageBox.SelectedItem = display;
    }

    private void ValidateAssociatedTechs(XElement entry)
    {
        var ageTech = MajorGodCatalog.FindDirectChild(entry, "agetech");
        if (ageTech != null)
        {
            var age = ageTech.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("age", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
            if (!MajorGodCatalog.AgeValues.Contains(age, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException("Age Tech must use an age from ArchaicAge through WonderAge."), ageTech, "@age");

            var ageTechnologies = ageTech.Elements().Where(element =>
                element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ageTechnologies.Count != 1)
                throw new InvalidDataException("Age Tech requires exactly one Tech field.");
            ValidationSourceLocation.Validate(ageTechnologies[0], () => ValidateTechnology(ageTechnologies[0].Value.Trim(), "Age Tech"));
        }
        if (MajorGodCatalog.FindDirectChild(entry, "deathmatchtech") is { } deathmatchTech)
            ValidationSourceLocation.Validate(deathmatchTech, () => ValidateTechnology(deathmatchTech.Value.Trim(), "Deathmatch Tech"));
        if (MajorGodCatalog.FindDirectChild(entry, "teamtech") is { } teamTech)
            ValidationSourceLocation.Validate(teamTech, () => ValidateTechnology(teamTech.Value.Trim(), "Team Tech"));
    }

    private void ValidateTechnology(string technology, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(technology))
            throw new InvalidDataException($"{fieldLabel} is required.");
        if (_technologyNames.Count > 0 && !_technologyNames.Contains(technology, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{fieldLabel} must reference an existing technology.");
    }

    private void RebuildStartingUnitsEditor()
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _startingUnitsPanel.Children.Clear();
            if (_currentEntry == null)
            {
                _addStartingUnitsGroupButton.IsVisible = false;
                return;
            }

            var groups = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("startingunits", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in groups)
                _startingUnitsPanel.Children.Add(CreateStartingUnitsGroupEditor(group));

            var usedModes = groups.Select(GetStartingUnitsMode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _addStartingUnitsGroupButton.IsVisible = IsModifiedMode && usedModes.Count < 3;
            _addStartingUnitsGroupButton.IsEnabled = IsModifiedMode;
        }
        finally
        {
            FinalizeMajorGodPresentation();
            _isPopulating = wasPopulating;
        }
    }

    private Control CreateStartingUnitsGroupEditor(XElement group)
    {
        var container = new StackPanel { Spacing = 4 };
        var modeRow = new WrapPanel();
        var mode = GetStartingUnitsMode(group);
        var modeOptions = GetAvailableStartingModes("startingunits", group);
        var modeBox = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = modeOptions,
            SelectedItem = modeOptions.First(value => value.Equals(mode, StringComparison.OrdinalIgnoreCase)),
            Width = 140,
            IsEnabled = IsModifiedMode,
            HorizontalAlignment = HorizontalAlignment.Left
        }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(modeBox, group, "@mode");
        modeBox.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || modeBox.SelectedItem is not string selectedMode)
                return;
            if (selectedMode.Equals("Supremacy", StringComparison.OrdinalIgnoreCase))
                RemoveAttribute(group, "mode");
            else
                SetAttribute(group, "mode", selectedMode.ToLowerInvariant());
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RebuildStartingUnitsEditor();
        };
        modeRow.Children.Add(modeBox);

        if (IsModifiedMode)
        {
            var removeGroup = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeGroup.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                group.Remove();
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingUnitsEditor();
            };
            modeRow.Children.Add(removeGroup);
        }
        container.Children.Add(modeRow);

        foreach (var unit in group.Elements().Where(element =>
                     element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)).ToList())
            container.Children.Add(CreateStartingUnitRow(group, unit));

        if (IsModifiedMode)
        {
            var addUnit = new Button
            {
                Content = "Add starting unit",
                Classes = { "add-component" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 4),
                Padding = new Thickness(8, 3)
            };
            addUnit.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddStartingUnit(group);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingUnitsEditor();
            };
            container.Children.Add(addUnit);
        }
        return CreateStartingGroupCard(container);
    }

    private Control CreateStartingUnitRow(XElement group, XElement unit)
    {
        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel("Unit"));
        var unitBox = ConfigureMajorGodField(new AutoCompleteBox
        {
            ItemsSource = _protoUnitNames,
            Text = unit.Value.Trim(),
            Width = 200,
            MaxWidth = 200,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.Reference);
        EditorAutoCompleteService.EnableDropdown(unitBox, () => _isPopulating);
        EditorValidationBindings.Bind(unitBox, unit);
        unitBox.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            unit.Value = unitBox.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(unitBox);

        row.Children.Add(CreateInlineLabel("Count"));
        row.Children.Add(CreateStartingUnitNumberBox(unit, "count", "1", ProtoUnitNumericKind.PositiveInteger));
        row.Children.Add(CreateInlineLabel("Offset"));
        foreach (var axis in new[] { "x", "y", "z" })
        {
            row.Children.Add(CreateInlineLabel(axis.ToUpperInvariant()));
            row.Children.Add(CreateStartingUnitNumberBox(unit, axis, "0", ProtoUnitNumericKind.SignedFloat));
        }

        var delay = FindAttribute(unit, "delay");
        if (delay == null)
        {
            if (IsModifiedMode)
            {
                var addDelay = new Button
                {
                    Content = "Delay",
                    Classes = { "add-component" },
                    Margin = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(8, 3)
                };
                addDelay.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    SetAttribute(unit, "delay", "1");
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    RebuildStartingUnitsEditor();
                };
                row.Children.Add(addDelay);
            }
        }
        else
        {
            row.Children.Add(CreateInlineLabel("Delay"));
            row.Children.Add(CreateStartingUnitNumberBox(unit, "delay", "1", ProtoUnitNumericKind.PositiveFloat));
            if (IsModifiedMode)
            {
                var removeDelay = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
                removeDelay.Click += (_, _) =>
                {
                    if (!CanEditCurrent()) return;
                    RemoveAttribute(unit, "delay");
                    MarkCurrentDirty();
                    RefreshRawXmlFromCurrent();
                    RebuildStartingUnitsEditor();
                };
                row.Children.Add(removeDelay);
            }
        }

        if (IsModifiedMode)
        {
            var removeUnit = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeUnit.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                unit.Remove();
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingUnitsEditor();
            };
            row.Children.Add(removeUnit);
        }
        return row;
    }

    private TextBox CreateStartingUnitNumberBox(
        XElement unit,
        string attributeName,
        string defaultValue,
        ProtoUnitNumericKind numericKind)
    {
        var box = ConfigureMajorGodField(new TextBox
        {
            Text = FindAttribute(unit, attributeName)?.Value ?? defaultValue,
            Width = 55,
            MaxWidth = 55,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.Numeric);
        EditorNumericFieldStyle.ConfigureNumericTextBox(box);
        EditorValidationBindings.Bind(box, unit, "@" + attributeName);
        EditorNumericInputBehavior.AttachRule(box, numericKind);
        box.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            SetAttribute(unit, attributeName, box.Text ?? "");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        return box;
    }

    private static TextBlock CreateInlineLabel(string text)
        => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    private void AddStartingUnitsGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditCurrent()) return;
        var usedModes = _currentEntry!.Elements().Where(element =>
                element.Name.LocalName.Equals("startingunits", StringComparison.OrdinalIgnoreCase))
            .Select(GetStartingUnitsMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mode = StartingModes
            .FirstOrDefault(candidate => !usedModes.Contains(candidate));
        if (mode == null) return;
        var group = MajorGodCatalog.AddStartingUnitsGroup(_currentEntry, mode);
        MajorGodCatalog.AddStartingUnit(group);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildStartingUnitsEditor();
    }

    private IReadOnlyList<string> GetAvailableStartingModes(string elementName, XElement currentGroup)
    {
        if (_currentEntry == null)
            return StartingModes;
        var usedByOtherGroups = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase) &&
                !ReferenceEquals(element, currentGroup))
            .Select(GetStartingUnitsMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentMode = GetStartingUnitsMode(currentGroup);
        return StartingModes
            .Where(mode => !usedByOtherGroups.Contains(mode) || mode.Equals(currentMode, StringComparison.OrdinalIgnoreCase))
            .Append(currentMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RebuildStartingQueueEditor()
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _startingQueuePanel.Children.Clear();
            if (_currentEntry == null)
            {
                _addStartingQueueGroupButton.IsVisible = false;
                return;
            }
            var groups = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("startingqueue", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in groups)
                _startingQueuePanel.Children.Add(CreateStartingQueueGroupEditor(group));
            var usedModes = groups.Select(GetStartingUnitsMode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _addStartingQueueGroupButton.IsVisible = IsModifiedMode && usedModes.Count < StartingModes.Length;
            _addStartingQueueGroupButton.IsEnabled = IsModifiedMode;
        }
        finally
        {
            FinalizeMajorGodPresentation();
            _isPopulating = wasPopulating;
        }
    }

    private Control CreateStartingQueueGroupEditor(XElement group)
    {
        var container = new StackPanel { Spacing = 4 };
        var modeRow = new WrapPanel();
        var mode = GetStartingUnitsMode(group);
        var modeOptions = GetAvailableStartingModes("startingqueue", group);
        var modeBox = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = modeOptions,
            SelectedItem = modeOptions.First(value => value.Equals(mode, StringComparison.OrdinalIgnoreCase)),
            Width = 140,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(modeBox, group, "@mode");
        modeBox.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || modeBox.SelectedItem is not string selectedMode) return;
            if (selectedMode.Equals("Supremacy", StringComparison.OrdinalIgnoreCase)) RemoveAttribute(group, "mode");
            else SetAttribute(group, "mode", selectedMode.ToLowerInvariant());
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RebuildStartingQueueEditor();
        };
        modeRow.Children.Add(modeBox);
        if (IsModifiedMode)
        {
            var removeGroup = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeGroup.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(group);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingQueueEditor();
            };
            modeRow.Children.Add(removeGroup);
        }
        container.Children.Add(modeRow);

        foreach (var unit in group.Elements().Where(element =>
                     element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)).ToList())
            container.Children.Add(CreateStartingQueueUnitRow(group, unit));

        if (IsModifiedMode)
        {
            var addUnit = new Button
            {
                Content = "Add starting queue unit",
                Classes = { "add-component" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 4),
                Padding = new Thickness(8, 3)
            };
            addUnit.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.AddStartingQueueUnit(group);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingQueueEditor();
            };
            container.Children.Add(addUnit);
        }
        return CreateStartingGroupCard(container);
    }

    private Control CreateStartingQueueUnitRow(XElement group, XElement unit)
    {
        var row = new WrapPanel();
        row.Children.Add(CreateInlineLabel("Unit"));
        var unitBox = ConfigureMajorGodField(new AutoCompleteBox
        {
            ItemsSource = _protoUnitNames,
            Text = unit.Value.Trim(),
            Width = 200,
            MaxWidth = 200,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.Reference);
        EditorAutoCompleteService.EnableDropdown(unitBox, () => _isPopulating);
        EditorValidationBindings.Bind(unitBox, unit);
        unitBox.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            unit.Value = unitBox.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(unitBox);
        AddOptionalQueueNumber(row, unit, "delay", "Delay (ms)", "1", ProtoUnitNumericKind.PositiveFloat);
        AddOptionalQueueNumber(row, unit, "count", "Count", "1", ProtoUnitNumericKind.PositiveInteger);

        var hasCount = FindAttribute(unit, "count") != null;
        var autoTrain = new CheckBox
        {
            Content = "Auto train",
            IsChecked = !hasCount && FindAttribute(unit, "autotrain")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = IsModifiedMode && !hasCount,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        EditorValidationBindings.Bind(autoTrain, unit, "@autotrain");
        autoTrain.IsCheckedChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            if (autoTrain.IsChecked == true) SetAttribute(unit, "autotrain", "true");
            else RemoveAttribute(unit, "autotrain");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(autoTrain);
        var prequeue = new CheckBox
        {
            Content = "Prequeue",
            IsChecked = !hasCount && FindAttribute(unit, "prequeue")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsEnabled = IsModifiedMode && !hasCount,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        EditorValidationBindings.Bind(prequeue, unit, "@prequeue");
        prequeue.IsCheckedChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            if (prequeue.IsChecked == true) SetAttribute(unit, "prequeue", "true");
            else RemoveAttribute(unit, "prequeue");
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        row.Children.Add(prequeue);
        if (IsModifiedMode)
        {
            var removeUnit = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeUnit.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(unit);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingQueueEditor();
            };
            row.Children.Add(removeUnit);
        }
        return row;
    }

    private void AddOptionalQueueNumber(
        Panel row,
        XElement unit,
        string attributeName,
        string label,
        string defaultValue,
        ProtoUnitNumericKind numericKind)
    {
        if (FindAttribute(unit, attributeName) == null)
        {
            if (!IsModifiedMode) return;
            var addButton = new Button
            {
                Content = label,
                Classes = { "add-component" },
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(8, 3)
            };
            addButton.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                if (attributeName.Equals("count", StringComparison.OrdinalIgnoreCase))
                    MajorGodCatalog.SetStartingQueueCount(unit, defaultValue);
                else
                    SetAttribute(unit, attributeName, defaultValue);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingQueueEditor();
            };
            row.Children.Add(addButton);
            return;
        }

        row.Children.Add(CreateInlineLabel(label));
        var box = CreateStartingUnitNumberBox(unit, attributeName, defaultValue, numericKind);
        row.Children.Add(box);
        if (IsModifiedMode)
        {
            var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                RemoveAttribute(unit, attributeName);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingQueueEditor();
            };
            row.Children.Add(remove);
        }
    }

    private void AddStartingQueueGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditCurrent()) return;
        var usedModes = _currentEntry!.Elements().Where(element =>
                element.Name.LocalName.Equals("startingqueue", StringComparison.OrdinalIgnoreCase))
            .Select(GetStartingUnitsMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mode = StartingModes.FirstOrDefault(candidate => !usedModes.Contains(candidate));
        if (mode == null) return;
        var group = MajorGodCatalog.AddStartingQueueGroup(_currentEntry, mode);
        MajorGodCatalog.AddStartingQueueUnit(group);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildStartingQueueEditor();
    }

    private void ValidateStartingQueue(XElement entry)
    {
        var usedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("startingqueue", StringComparison.OrdinalIgnoreCase)))
        {
            var mode = GetStartingUnitsMode(group);
            if (!StartingModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Queue mode '{mode}' is not valid."), group, "@mode");
            if (!usedModes.Add(mode))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Queue mode '{mode}' can only be used once."), group, "@mode");
            var units = group.Elements().Where(element =>
                element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)).ToList();
            if (units.Count == 0)
                throw new InvalidDataException($"{mode} Starting Queue requires at least one Unit.");
            foreach (var unit in units)
            {
                var unitName = unit.Value.Trim();
                if (unitName.Length == 0 ||
                    (_protoUnitNames.Count > 0 && !_protoUnitNames.Contains(unitName, StringComparer.OrdinalIgnoreCase)))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"{mode} Starting Queue must reference an existing ProtoUnit."), unit);
                var count = FindAttribute(unit, "count");
                if (count != null &&
                    (!int.TryParse(count.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var countValue) || countValue <= 0))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Queue Unit '{unitName}' Count must be an integer greater than zero."), unit, "@count");
                var autoTrain = FindAttribute(unit, "autotrain");
                var preQueue = FindAttribute(unit, "prequeue");
                ValidateTrueFlagAttribute(autoTrain, $"Starting Queue Unit '{unitName}' Auto Train");
                ValidateTrueFlagAttribute(preQueue, $"Starting Queue Unit '{unitName}' Prequeue");
                if (count != null && (autoTrain != null || preQueue != null))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Queue Unit '{unitName}' cannot use Count with Auto Train or Prequeue."), unit, "@count");
                if (FindAttribute(unit, "delay") is { } delay &&
                    (!double.TryParse(delay.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delayValue) ||
                     !double.IsFinite(delayValue) || delayValue <= 0))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Queue Unit '{unitName}' Delay must be greater than zero."), unit, "@delay");
            }
        }
    }

    private void RebuildStartingResourcesEditor()
    {
        var wasPopulating = _isPopulating;
        _isPopulating = true;
        try
        {
            _startingResourcesPanel.Children.Clear();
            if (_currentEntry == null)
            {
                _addStartingResourcesGroupButton.IsVisible = false;
                return;
            }

            var groups = _currentEntry.Elements().Where(element =>
                element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in groups)
                _startingResourcesPanel.Children.Add(CreateStartingResourcesGroupEditor(group));

            var usedModes = groups.Select(GetStartingUnitsMode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _addStartingResourcesGroupButton.IsVisible = IsModifiedMode && usedModes.Count < StartingModes.Length;
            _addStartingResourcesGroupButton.IsEnabled = IsModifiedMode;
        }
        finally
        {
            FinalizeMajorGodPresentation();
            _isPopulating = wasPopulating;
        }
    }

    private Control CreateStartingResourcesGroupEditor(XElement group)
    {
        var row = new WrapPanel();
        var mode = GetStartingUnitsMode(group);
        var modeOptions = GetAvailableStartingModes("startingresources", group);
        var modeBox = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = modeOptions,
            SelectedItem = modeOptions.First(value => value.Equals(mode, StringComparison.OrdinalIgnoreCase)),
            Width = 140,
            IsEnabled = IsModifiedMode,
            HorizontalAlignment = HorizontalAlignment.Left
        }, EditorFieldWidthRole.LongEnum);
        EditorValidationBindings.Bind(modeBox, group, "@mode");
        modeBox.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || modeBox.SelectedItem is not string selectedMode) return;
            if (selectedMode.Equals("Supremacy", StringComparison.OrdinalIgnoreCase))
                RemoveAttribute(group, "mode");
            else
                SetAttribute(group, "mode", selectedMode.ToLowerInvariant());
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RebuildStartingResourcesEditor();
        };
        row.Children.Add(modeBox);

        foreach (var resource in group.Elements().Where(element =>
                     ProtoConstants.KnownResourceTypes.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList())
            row.Children.Add(CreateStartingResourceSegment(group, resource));

        if (IsModifiedMode)
        {
            var usedResources = group.Elements().Select(element => element.Name.LocalName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var addResource = new Button
            {
                Content = "Add resource",
                Classes = { "add-component" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(8, 3),
                IsVisible = ProtoConstants.KnownResourceTypes.Any(resource => !usedResources.Contains(resource))
            };
            addResource.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                var resourceType = ProtoConstants.KnownResourceTypes.FirstOrDefault(resource => !usedResources.Contains(resource));
                if (resourceType == null) return;
                MajorGodCatalog.AddStartingResource(group, resourceType);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingResourcesEditor();
            };
            row.Children.Add(addResource);

            var removeGroup = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeGroup.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(group);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingResourcesEditor();
            };
            row.Children.Add(removeGroup);
        }
        return CreateStartingGroupCard(row);
    }

    private Control CreateStartingResourceSegment(XElement group, XElement resource)
    {
        var segment = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var usedByOtherRows = group.Elements().Where(element => !ReferenceEquals(element, resource))
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentType = ProtoConstants.KnownResourceTypes.First(type =>
            type.Equals(resource.Name.LocalName, StringComparison.OrdinalIgnoreCase));
        var resourceOptions = ProtoConstants.KnownResourceTypes
            .Where(type => !usedByOtherRows.Contains(type) || type.Equals(currentType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var resourceBox = ConfigureMajorGodField(new ComboBox
        {
            ItemsSource = resourceOptions,
            SelectedItem = currentType,
            Width = 90,
            IsEnabled = IsModifiedMode
        }, EditorFieldWidthRole.ShortEnum);
        resourceBox.SelectionChanged += (_, _) =>
        {
            if (!CanEditCurrent() || resourceBox.SelectedItem is not string selectedType) return;
            resource.Name = selectedType.ToLowerInvariant();
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
            RebuildStartingResourcesEditor();
        };
        segment.Children.Add(resourceBox);
        var amount = ConfigureMajorGodField(new TextBox { Text = resource.Value.Trim(), Width = 60, MaxWidth = 60, IsEnabled = IsModifiedMode }, EditorFieldWidthRole.Numeric);
        EditorValidationBindings.Bind(amount, resource);
        EditorNumericFieldStyle.ConfigureNumericTextBox(amount);
        EditorNumericInputBehavior.AttachRule(amount, ProtoUnitNumericKind.UnsignedFloat);
        amount.TextChanged += (_, _) =>
        {
            if (!CanEditCurrent()) return;
            resource.Value = amount.Text ?? "";
            MarkCurrentDirty();
            RefreshRawXmlFromCurrent();
        };
        segment.Children.Add(amount);
        if (IsModifiedMode)
        {
            var removeResource = new Button { Classes = { "remove-button" }, Margin = new Thickness(4, 0, 0, 0) };
            removeResource.Click += (_, _) =>
            {
                if (!CanEditCurrent()) return;
                MajorGodCatalog.RemoveElementAndAdjacentWhitespace(resource);
                MarkCurrentDirty();
                RefreshRawXmlFromCurrent();
                RebuildStartingResourcesEditor();
            };
            segment.Children.Add(removeResource);
        }
        return segment;
    }

    private void AddStartingResourcesGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditCurrent()) return;
        var usedModes = _currentEntry!.Elements().Where(element =>
                element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase))
            .Select(GetStartingUnitsMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mode = StartingModes.FirstOrDefault(candidate => !usedModes.Contains(candidate));
        if (mode == null) return;
        var group = MajorGodCatalog.AddStartingResourcesGroup(_currentEntry, mode);
        foreach (var resourceType in ProtoConstants.KnownResourceTypes)
            MajorGodCatalog.AddStartingResource(group, resourceType);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
        RebuildStartingResourcesEditor();
    }

    private void ValidateStartingResources(XElement entry)
    {
        var usedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase)))
        {
            var mode = GetStartingUnitsMode(group);
            if (!StartingModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Resources mode '{mode}' is not valid."), group, "@mode");
            if (!usedModes.Add(mode))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Resources mode '{mode}' can only be used once."), group, "@mode");

            var usedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resources = group.Elements().Where(element =>
                ProtoConstants.KnownResourceTypes.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList();
            if (resources.Count == 0)
                throw new InvalidDataException($"{mode} Starting Resources requires at least one Resource.");
            foreach (var resource in resources)
            {
                var resourceType = ProtoConstants.KnownResourceTypes.First(type =>
                    type.Equals(resource.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                if (!usedResources.Add(resourceType))
                    throw new InvalidDataException($"{mode} Starting Resources contains '{resourceType}' more than once.");
                if (!double.TryParse(resource.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
                    !double.IsFinite(amount) || amount < 0)
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"{mode} Starting Resource '{resourceType}' Amount must be zero or greater."), resource);
            }
        }
    }

    private void ValidateStartingUnits(XElement entry)
    {
        var usedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entry.Elements().Where(element =>
                     element.Name.LocalName.Equals("startingunits", StringComparison.OrdinalIgnoreCase)))
        {
            var mode = GetStartingUnitsMode(group);
            if (!StartingModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Units mode '{mode}' is not valid."), group, "@mode");
            if (!usedModes.Add(mode))
                throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Units mode '{mode}' can only be used once."), group, "@mode");

            var units = group.Elements().Where(element =>
                element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)).ToList();
            if (units.Count == 0)
                throw new InvalidDataException($"{mode} Starting Units requires at least one Unit.");

            foreach (var unit in units)
            {
                var unitName = unit.Value.Trim();
                if (unitName.Length == 0 ||
                    (_protoUnitNames.Count > 0 && !_protoUnitNames.Contains(unitName, StringComparer.OrdinalIgnoreCase)))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"{mode} Starting Units must reference an existing ProtoUnit."), unit);
                if (!int.TryParse(FindAttribute(unit, "count")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Unit '{unitName}' Count must be an integer greater than zero."), unit, "@count");
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    if (!double.TryParse(FindAttribute(unit, axis)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset) ||
                        !double.IsFinite(offset))
                        throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Unit '{unitName}' Offset {axis.ToUpperInvariant()} must be a number."), unit, "@" + axis);
                }
                if (FindAttribute(unit, "delay") is { } delay &&
                    (!double.TryParse(delay.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delayValue) ||
                     !double.IsFinite(delayValue) || delayValue <= 0))
                    throw ValidationSourceLocation.Attach(new InvalidDataException($"Starting Unit '{unitName}' Delay must be greater than zero."), unit, "@delay");
            }
        }
    }

    private static string GetStartingUnitsMode(XElement group)
    {
        var mode = FindAttribute(group, "mode")?.Value.Trim() ?? "";
        return mode.Length == 0 ? "Supremacy" : char.ToUpperInvariant(mode[0]) + mode[1..].ToLowerInvariant();
    }

    private static void ValidateTrueFlagAttribute(XAttribute? attribute, string label)
    {
        if (attribute != null && !attribute.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"{label} must be true when present."), attribute!.Parent!, "@" + attribute.Name.LocalName);
    }

    private static XAttribute? FindAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void SetAttribute(XElement element, string name, string value)
    {
        var attribute = FindAttribute(element, name);
        if (attribute == null) element.SetAttributeValue(name, value);
        else attribute.Value = value;
    }

    private static void RemoveAttribute(XElement element, string name)
        => FindAttribute(element, name)?.Remove();

    private void XmlPreviewToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isXmlPreviewCollapsed = !_isXmlPreviewCollapsed;
        EditorXmlPreviewPane.SetCollapsed(_mainGrid, _isXmlPreviewCollapsed);
    }

    private bool CanEditCurrent()
        => !_isPopulating && IsModifiedMode && _currentEntry != null &&
           CurrentMajorGodXmlDraft?.Session.IsEditing != true;

    private void SetEntryValue(string tag, string value)
    {
        if (_currentEntry == null) return;
        if (ReadValue(_currentEntry, tag).Equals(value.Trim(), StringComparison.Ordinal)) return;
        MajorGodCatalog.EnsureStatsId(_currentEntry);
        MajorGodCatalog.SetPass1Value(_currentEntry, tag, value);
        MajorGodCatalog.RemoveEmptyOptionalFields(_currentEntry);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void MarkCurrentDirty()
    {
        var name = _loadedEntryName ?? CurrentMajorGodName ?? (_currentEntry == null ? "" : MajorGodCatalog.GetName(_currentEntry));
        // Routed control events can arrive after population finishes. Reapplying
        // an unchanged value must not manufacture a draft or clear real drafts.
        if (!_rawEditorDirty && !_dirtyNames.Contains(name) && _currentEntry != null && _loadedModDocument != null &&
            !_pendingStringOwners.Values.Contains(name, StringComparer.OrdinalIgnoreCase) &&
            !_pendingStringRemovalOwners.Values.Contains(name, StringComparer.OrdinalIgnoreCase) &&
            XNode.DeepEquals(_currentEntry, MajorGodCatalog.Find(_loadedModDocument, name)))
            return;
        if (!string.IsNullOrWhiteSpace(name))
            _dirtyNames.Add(name);
        SetDirty(true);
    }

    private void RefreshRawXmlFromCurrent()
    {
        FinalizeMajorGodPresentation();
        if (_currentEntry == null) return;
        var preview = new XElement(_currentEntry);
        var saved = _loadedModDocument == null ? null : MajorGodCatalog.Find(_loadedModDocument, MajorGodCatalog.GetName(_currentEntry));
        var protectedContent = EditorXmlOrderPolicy.HasProtectedContent(_currentEntry);
        if (IsModifiedMode && CurrentMajorGodXmlDraft?.Session.IsEditing != true && !protectedContent &&
            (_dirtyNames.Contains(MajorGodCatalog.GetName(_currentEntry)) || !XNode.DeepEquals(_currentEntry, saved)))
            MajorGodCatalog.NormalizeEditorFieldOrder(preview);
        SetRawXml(protectedContent ? preview.ToString(SaveOptions.DisableFormatting) : FormatMajorGodPreview(preview));
    }

    private void SetRawXml(string text)
    {
        BindMajorGodXmlPreview(text);
    }

    private static string FormatMajorGodPreview(XElement entry)
        => EditorXmlOrderPolicy.HasProtectedContent(entry) ? entry.ToString(SaveOptions.DisableFormatting)
            : XmlSyntaxEditorService.FormatElementForPreview(entry);

    private static string ReadValue(XElement entry, string tag)
        => MajorGodCatalog.FindDirectChild(entry, tag)?.Value.Trim() ?? "";

    private TextBox GetResolvedStringBox(string tag) => tag.ToLowerInvariant() switch
    {
        "displaynameid" => _displayNameBox,
        "rollovernameid" => _rolloverNameBox,
        _ => _titleBox
    };

    private void SetDirty(bool value)
    {
        if (value) _saveFeedback.ClearSuccessOnEdit();
        if (IsDirty == value)
            return;
        IsDirty = value;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowEmpty()
    {
        _populateGeneration++;
        _isPopulating = true;
        try
        {
            ShowMajorGodLayout();
            _nameBox.Text = "";
            _keyBox.Text = "";
            _cultureBox.SelectedItem = null;
            _cultureBox.Text = "";
            _displayNameBox.Text = "";
            _rolloverNameBox.Text = "";
            _titleBox.Text = "";
            _minimapBorderBox.Text = "";
            _backgroundScenarioBox.Text = "";
            SetReadOnlyAge("ArchaicAge");
            _ageTechBox.Text = "";
            _deathmatchTechBox.Text = "";
            _teamTechBox.Text = "";
            _economicPanel.Children.Clear();
            _startingUnitsPanel.Children.Clear();
            _addStartingUnitsGroupButton.IsVisible = false;
            _startingQueuePanel.Children.Clear();
            _addStartingQueueGroupButton.IsVisible = false;
            _startingResourcesPanel.Children.Clear();
            _addStartingResourcesGroupButton.IsVisible = false;
            _specialMechanicTabs.Children.Clear();
            _specialMechanicsPanel.Children.Clear();
            _selectedSpecialMechanicTag = null;
            _specialMechanicToggleRow.IsVisible = false;
            _partisansCheckBox.IsChecked = false;
            _devotionCommandsCheckBox.IsChecked = false;
            _specialMechanicSelector.ItemsSource = Array.Empty<string>();
            _specialMechanicSelector.Text = "";
            _specialMechanicAddRow.IsVisible = false;
            _iconEditor.Configure("", _iconPaths, null);
            _portraitEditor.Configure("", _iconPaths, null);
            _breakoutPortraitEditor.Configure("", _iconPaths, null);
            SetEditorsEnabled(false);
            _majorGodXmlController.Bind(null, false);
            _majorGodIdentityRow.IsVisible = false;
            _majorGodTabs.IsVisible = false;
            _emptySelectionMessage.IsVisible = true;
            _statusMessage.Text = "";
            _rawEditorDirty = false;
            _stringIds.Clear();
            _ = _iconPreview.ShowAsync(null);
            RefreshTeamTechVisibility();
        }
        finally
        {
            _isPopulating = false;
        }
    }
}
