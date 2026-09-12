using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView : UserControl
{
    private static readonly string[] ResolvedStringTags = ["displaynameid", "rollovernameid", "titleid"];
    private static readonly string[] ImageTags = ["icon", "portrait", "breakoutportrait"];

    private readonly Dictionary<string, XElement> _baseEntries;
    private readonly string? _modPath;
    private readonly string? _modStringsPath;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? _saveStringsAsync;
    private readonly IReadOnlyList<string> _cultureNames;
    private readonly IReadOnlyList<string> _iconPaths;
    private readonly IReadOnlyList<string> _minimapBorderValues;
    private readonly IReadOnlyList<string> _backgroundScenarioValues;
    private readonly IconPreviewControl _iconPreview;
    private readonly Dictionary<string, string> _stringIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingStringOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);

    private XDocument? _modDocument;
    private XDocument? _loadedModDocument;
    private FileIntegrityTransaction.FileState? _loadedFileState;
    private FileIntegrityTransaction.FileState? _loadedStringsFileState;
    private XElement? _currentEntry;
    private string? _loadedEntryName;
    private bool _isPopulating;
    private bool _rawEditorDirty;
    private int _populateGeneration;

    public bool IsModifiedMode { get; private set; }
    public string? CurrentMajorGodName { get; private set; }
    public bool IsDirty { get; private set; }

    public event EventHandler? BrowserStateChanged;
    public event EventHandler? DirtyStateChanged;

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
        string? modStringsPath = null)
    {
        InitializeComponent();
        XmlSyntaxEditorService.Configure(_rawXmlEditor);
        ConfigureFieldControls();

        _baseEntries = baseDefinitions.ToDictionary(
            definition => definition.Name,
            definition => new XElement(definition.SourceElement),
            StringComparer.OrdinalIgnoreCase);
        _modPath = modPath;
        _modStringsPath = modStringsPath;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _cultureNames = (cultureNames ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (!string.IsNullOrWhiteSpace(_modPath))
        {
            var stateBeforeRead = FileIntegrityTransaction.CaptureState(_modPath);
            _modDocument = MajorGodCatalog.LoadOrCreateModDocument(_modPath);
            var stateAfterRead = FileIntegrityTransaction.CaptureState(_modPath);
            if (stateBeforeRead != stateAfterRead)
                throw new IOException($"'{_modPath}' changed while it was being opened.");
            _loadedFileState = stateAfterRead;
            _loadedModDocument = new XDocument(_modDocument);
        }
        if (!string.IsNullOrWhiteSpace(_modStringsPath))
            _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath);
        ShowEmpty();
    }

    private void ConfigureFieldControls()
    {
        EditorTextFieldStyle.ConfigureTextBox(_nameBox);
        EditorTextFieldStyle.ConfigureTextBox(_displayNameBox);
        EditorTextFieldStyle.ConfigureTextBox(_rolloverNameBox);
        EditorTextFieldStyle.ConfigureTextBox(_titleBox);
        EditorTextFieldStyle.ConfigureSelector(_minimapBorderBox);
        EditorTextFieldStyle.ConfigureSelector(_backgroundScenarioBox);
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
            return _baseEntries.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_modDocument?.Root == null)
            return [];
        return MajorGodCatalog.ExtractDefinitions(_modDocument, false).Select(definition => definition.Name).ToList();
    }

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
        CurrentMajorGodName = name;
        _loadedEntryName = name;
        _currentEntry = string.IsNullOrWhiteSpace(name)
            ? null
            : IsModifiedMode
                ? MajorGodCatalog.Find(_modDocument!, name)
                : _baseEntries.GetValueOrDefault(name);
        _rawEditorDirty = false;
        _applyRawXmlButton.IsVisible = false;

        if (_currentEntry == null)
        {
            ShowEmpty();
            return;
        }

        SetRawXml(_currentEntry.ToString());
        XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, !IsModifiedMode);
        _statusMessage.Text = IsModifiedMode
            ? "Edit the shared fields or raw <civ> XML, then use Save."
            : "Original Data.bar entry (read-only).";
        _ = PopulateStructuredFieldsAsync(_currentEntry);
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
            _cultureBox.ItemsSource = _cultureNames
                .Append(culture)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _cultureBox.SelectedItem = _cultureNames.FirstOrDefault(value =>
                value.Equals(culture, StringComparison.OrdinalIgnoreCase)) ?? culture;

            SetEditorsEnabled(editable);
            ConfigureImageEditor(_iconEditor, entry, "icon");
            ConfigureImageEditor(_portraitEditor, entry, "portrait");
            ConfigureImageEditor(_breakoutPortraitEditor, entry, "breakoutportrait");
            _minimapBorderBox.Text = ReadValue(entry, "minimapborderimage");
            _backgroundScenarioBox.Text = ReadValue(entry, "backgroundscenario");
            RefreshIconPreview();

            _stringIds.Clear();
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
                box.Text = resolution.Text;
                ToolTip.SetTip(box, string.IsNullOrWhiteSpace(resolution.Id)
                    ? "The string ID will be generated from Name when this value is edited."
                    : $"String ID: {resolution.Id}");
            }
        }
        finally
        {
            if (generation == _populateGeneration)
                _isPopulating = false;
        }
    }

    private void SetEditorsEnabled(bool editable)
    {
        foreach (var control in new Control[]
                 {
                     _nameBox, _keyBox, _cultureBox, _displayNameBox, _rolloverNameBox, _titleBox,
                     _iconEditor, _portraitEditor, _breakoutPortraitEditor, _minimapBorderBox, _backgroundScenarioBox
                 })
        {
            control.IsEnabled = editable;
            control.Opacity = editable ? 1.0 : 0.62;
        }
        foreach (var editor in new[] { _iconEditor, _portraitEditor, _breakoutPortraitEditor })
            editor.CompactPresenter.Background = Brush.Parse(editable ? "#0E1110" : "#202220");
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

    public async Task<bool> CommitCurrentMajorGodAsync()
    {
        if (!IsModifiedMode || _currentEntry == null)
            return true;

        try
        {
            var candidate = _currentEntry;
            var appliedRawXml = _rawEditorDirty;
            if (appliedRawXml)
            {
                candidate = XElement.Parse(_rawXmlEditor.Text ?? "", LoadOptions.PreserveWhitespace);
                if (!candidate.Name.LocalName.Equals(MajorGodCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Raw XML must have a <civ> root element.");
            }

            var newName = MajorGodCatalog.GetName(candidate);
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidDataException("Name cannot be empty.");
            var duplicate = MajorGodCatalog.Find(_modDocument!, newName);
            if (duplicate != null && !ReferenceEquals(duplicate, _currentEntry))
                throw new InvalidDataException($"A modified major god named '{newName}' already exists.");

            var key = ReadValue(candidate, "key");
            if (!MajorGodCatalog.IsValidKey(key))
                throw new InvalidDataException("Key must be exactly one capital letter from A to Z.");
            var culture = ReadValue(candidate, "culture");
            if (_cultureNames.Count > 0 && !_cultureNames.Contains(culture, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Culture must be selected from the supported culture list.");

            var statsBefore = ReadValue(candidate, "statsid");
            MajorGodCatalog.EnsureStatsId(candidate);
            if (!statsBefore.Equals("SP", StringComparison.Ordinal))
                MarkCurrentDirty();

            if (appliedRawXml)
            {
                _currentEntry.ReplaceWith(candidate);
                _currentEntry = candidate;
                _rawEditorDirty = false;
                _applyRawXmlButton.IsVisible = false;
                MarkCurrentDirty();
            }

            AdoptNameAndGeneratedStringIds(newName);
            CurrentMajorGodName = newName;
            _loadedEntryName = newName;
            SetRawXml(candidate.ToString());
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            if (appliedRawXml)
                await PopulateStructuredFieldsAsync(candidate);
            return true;
        }
        catch (Exception ex)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Invalid major god data", ex.Message).ShowDialog(owner);
            _statusMessage.Text = ex.Message;
            return false;
        }
    }

    private void AdoptNameAndGeneratedStringIds(string newName)
    {
        var oldName = _loadedEntryName;
        if (_currentEntry == null || string.IsNullOrWhiteSpace(oldName) || oldName.Equals(newName, StringComparison.Ordinal))
            return;

        foreach (var tag in ResolvedStringTags)
        {
            var element = MajorGodCatalog.FindDirectChild(_currentEntry, tag);
            if (element == null)
                continue;
            var oldId = MajorGodCatalog.BuildStringId(oldName, tag);
            if (!element.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))
                continue;
            var newId = MajorGodCatalog.BuildStringId(newName, tag);
            element.Value = newId;
            _stringIds[tag] = newId;
            if (_pendingStringUpdates.Remove(oldId, out var text))
            {
                _pendingStringUpdates[newId] = text;
                _pendingStringOwners.Remove(oldId);
                _pendingStringOwners[newId] = newName;
            }
        }

        if (_dirtyNames.Remove(oldName))
            _dirtyNames.Add(newName);
        foreach (var id in _pendingStringOwners.Where(pair =>
                     pair.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList())
            _pendingStringOwners[id] = newName;
    }

    public async Task AddMajorGodAsync(bool duplicateSelected = false)
    {
        if (_modDocument?.Root == null)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before adding a major god.").ShowDialog(owner);
            return;
        }
        if (!await CommitCurrentMajorGodAsync())
            return;

        var sourceEntry = duplicateSelected ? _currentEntry : null;
        var existingNames = new HashSet<string>(GetMajorGodNames(true), StringComparer.OrdinalIgnoreCase);
        var baseName = duplicateSelected && !string.IsNullOrWhiteSpace(CurrentMajorGodName)
            ? CurrentMajorGodName + "Copy"
            : "NewMajorGod";
        var name = baseName;
        for (var suffix = 2; existingNames.Contains(name); suffix++)
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
            entry = MajorGodCatalog.CreatePass1Entry(name, _cultureNames.FirstOrDefault() ?? "Greek");
            _pendingStringUpdates[MajorGodCatalog.BuildStringId(name, "displaynameid")] = name;
            _pendingStringUpdates[MajorGodCatalog.BuildStringId(name, "rollovernameid")] = "";
            _pendingStringUpdates[MajorGodCatalog.BuildStringId(name, "titleid")] = "";
            foreach (var tag in ResolvedStringTags)
                _pendingStringOwners[MajorGodCatalog.BuildStringId(name, tag)] = name;
        }

        _modDocument.Root.Add(entry);
        IsModifiedMode = true;
        _dirtyNames.Add(name);
        SetDirty(true);
        SelectMajorGod(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteCurrentMajorGodAsync()
    {
        if (!IsModifiedMode || _currentEntry == null || string.IsNullOrWhiteSpace(CurrentMajorGodName))
            return;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            var prompt = new Prompt(PromptType.Confirm, "Delete major god?", $"Delete '{CurrentMajorGodName}' from major_gods_mods.xml?");
            await prompt.ShowDialog(owner);
            if (!prompt.Confirmed)
                return;
        }
        _dirtyNames.Add(CurrentMajorGodName);
        _currentEntry.Remove();
        _currentEntry = null;
        CurrentMajorGodName = null;
        _loadedEntryName = null;
        SetDirty(true);
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
           (_modDocument != null && ReferenceEquals(_currentEntry, MajorGodCatalog.Find(_modDocument, name)) && _rawEditorDirty);

    public void DiscardMajorGodChanges(string name, XElement? savedElement)
    {
        var current = MajorGodCatalog.Find(_modDocument!, name);
        var restored = savedElement == null ? null : new XElement(savedElement);
        if (current != null && restored != null)
            current.ReplaceWith(restored);
        else if (current != null)
            current.Remove();
        else if (restored != null)
            _modDocument!.Root!.Add(restored);

        foreach (var id in _pendingStringOwners.Where(pair => pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList())
        {
            _pendingStringOwners.Remove(id);
            _pendingStringUpdates.Remove(id);
        }
        _dirtyNames.Remove(name);
        _rawEditorDirty = false;
        SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0);
        if (string.Equals(CurrentMajorGodName, name, StringComparison.OrdinalIgnoreCase))
            SelectMajorGod(restored == null ? null : MajorGodCatalog.GetName(restored));
    }

    public async Task<bool> SaveAsync()
    {
        if (!await CommitCurrentMajorGodAsync())
            return false;
        if (!IsDirty)
            return true;
        if (_modDocument == null || string.IsNullOrWhiteSpace(_modPath))
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "No active mod", "Create or switch to a local mod before saving major gods.").ShowDialog(owner);
            return false;
        }

        if (!await ReloadExternallyChangedDocumentAsync())
            return false;

        try
        {
            var stringUpdates = new Dictionary<string, string>(_pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
            var writesStrings = stringUpdates.Count > 0;
            if (writesStrings && (_saveStringsAsync == null || string.IsNullOrWhiteSpace(_modStringsPath)))
                throw new InvalidOperationException("Major God strings cannot be saved because the active mod string file is unavailable.");

            var expectedStates = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
            if (_loadedFileState != null)
                expectedStates[Path.GetFullPath(_modPath)] = _loadedFileState;
            if (writesStrings && _loadedStringsFileState != null)
                expectedStates[Path.GetFullPath(_modStringsPath!)] = _loadedStringsFileState;

            await FileIntegrityTransaction.ExecuteAsync(
                writesStrings ? [_modPath, _modStringsPath] : [_modPath],
                expectedStates,
                async () =>
                {
                    ProtoEditorWindow.SaveAbilityXmlDocument(_modDocument, _modPath);
                    if (writesStrings)
                        await _saveStringsAsync!(stringUpdates, []);
                });

            _loadedFileState = FileIntegrityTransaction.CaptureState(_modPath);
            _loadedModDocument = new XDocument(_modDocument);
            if (writesStrings)
                _loadedStringsFileState = FileIntegrityTransaction.CaptureState(_modStringsPath!);
            _pendingStringUpdates.Clear();
            _pendingStringOwners.Clear();
            _dirtyNames.Clear();
            SetDirty(false);
            _statusMessage.Text = "Saved major_gods_mods.xml and resolved strings.";
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage.Text = "Save failed: " + ex.Message;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Major God save failed", ex.Message).ShowDialog(owner);
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
        if (TopLevel.GetTopLevel(this) is not Window owner)
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
            _statusMessage.Text = "Save cancelled; external Major God changes were not overwritten.";
            return false;
        }

        try
        {
            var stateBeforeRead = FileIntegrityTransaction.CaptureState(_modPath);
            var externalDocument = MajorGodCatalog.LoadOrCreateModDocument(_modPath);
            var stateAfterRead = FileIntegrityTransaction.CaptureState(_modPath);
            if (stateBeforeRead != stateAfterRead)
                throw new IOException("major_gods_mods.xml changed again while it was being reloaded.");

            var merge = NamedXmlExternalDocumentMergePolicy.Merge(
                _loadedModDocument,
                _modDocument!,
                externalDocument,
                MajorGodCatalog.EntryName,
                MajorGodCatalog.GetName);
            foreach (var conflict in merge.ReplacedEditorChanges)
                _dirtyNames.Remove(conflict);

            var selectedName = CurrentMajorGodName;
            _modDocument = merge.Document;
            _loadedModDocument = new XDocument(externalDocument);
            _loadedFileState = stateAfterRead;
            SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0);
            if (!string.IsNullOrWhiteSpace(selectedName) && MajorGodCatalog.Find(_modDocument, selectedName) != null)
                SelectMajorGod(selectedName);
            else
            {
                CurrentMajorGodName = null;
                _currentEntry = null;
                ShowEmpty();
            }
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            _statusMessage.Text = merge.ReplacedEditorChanges.Count > 0
                ? $"External Major Gods reloaded. {merge.ReplacedEditorChanges.Count} conflicting edit(s) were replaced; review, then save again."
                : "External Major Gods reloaded and merged. Review the changes, then save again.";
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
        => await CommitCurrentMajorGodAsync();

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

    private void CultureBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!CanEditCurrent() || _cultureBox.SelectedItem is not string culture) return;
        SetEntryValue("culture", culture);
    }

    private void ResolvedStringBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent() || sender is not TextBox { Tag: string tag } box)
            return;
        var id = _stringIds.GetValueOrDefault(tag) ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            id = MajorGodCatalog.BuildStringId(MajorGodCatalog.GetName(_currentEntry!), tag);
            _stringIds[tag] = id;
            MajorGodCatalog.SetPass1Value(_currentEntry!, tag, id);
            ToolTip.SetTip(box, $"String ID: {id}");
            RefreshRawXmlFromCurrent();
        }
        _pendingStringUpdates[id] = box.Text ?? "";
        _pendingStringOwners[id] = CurrentMajorGodName ?? MajorGodCatalog.GetName(_currentEntry!);
        MarkCurrentDirty();
    }

    private void CatalogValueBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!CanEditCurrent() || sender is not AutoCompleteBox { Tag: string tag } box) return;
        SetEntryValue(tag, box.Text ?? "");
    }

    private async void ApplyRawXml_Click(object? sender, RoutedEventArgs e)
    {
        var applied = await CommitCurrentMajorGodAsync();
        if (applied && _currentEntry != null)
        {
            await PopulateStructuredFieldsAsync(_currentEntry);
            _statusMessage.Text = "Raw XML applied.";
        }
    }

    private void RawXmlEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isPopulating || !IsModifiedMode || _currentEntry == null)
            return;
        _rawEditorDirty = true;
        _applyRawXmlButton.IsVisible = true;
        MarkCurrentDirty();
    }

    private bool CanEditCurrent()
        => !_isPopulating && IsModifiedMode && _currentEntry != null;

    private void SetEntryValue(string tag, string value)
    {
        if (_currentEntry == null) return;
        MajorGodCatalog.EnsureStatsId(_currentEntry);
        MajorGodCatalog.SetPass1Value(_currentEntry, tag, value);
        MarkCurrentDirty();
        RefreshRawXmlFromCurrent();
    }

    private void MarkCurrentDirty()
    {
        var name = _loadedEntryName ?? CurrentMajorGodName ?? (_currentEntry == null ? "" : MajorGodCatalog.GetName(_currentEntry));
        if (!string.IsNullOrWhiteSpace(name))
            _dirtyNames.Add(name);
        SetDirty(true);
    }

    private void RefreshRawXmlFromCurrent()
    {
        if (_currentEntry == null) return;
        SetRawXml(_currentEntry.ToString());
        _rawEditorDirty = false;
        _applyRawXmlButton.IsVisible = false;
    }

    private void SetRawXml(string text)
    {
        _isPopulating = true;
        try { _rawXmlEditor.Text = text; }
        finally { _isPopulating = false; }
    }

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
            _nameBox.Text = "";
            _keyBox.Text = "";
            _cultureBox.SelectedItem = null;
            _displayNameBox.Text = "";
            _rolloverNameBox.Text = "";
            _titleBox.Text = "";
            _minimapBorderBox.Text = "";
            _backgroundScenarioBox.Text = "";
            _iconEditor.Configure("", _iconPaths, null);
            _portraitEditor.Configure("", _iconPaths, null);
            _breakoutPortraitEditor.Configure("", _iconPaths, null);
            SetEditorsEnabled(false);
            _rawXmlEditor.Text = "";
            XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, true);
            _applyRawXmlButton.IsVisible = false;
            _statusMessage.Text = "Select a major god from the list.";
            _rawEditorDirty = false;
            _stringIds.Clear();
            _ = _iconPreview.ShowAsync(null);
        }
        finally
        {
            _isPopulating = false;
        }
    }
}
