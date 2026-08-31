using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView : UserControl
{
    private readonly Dictionary<string, XElement> _baseEntries;
    private readonly string? _modPath;
    private XDocument? _modDocument;
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isPopulating;
    private bool _editorDirty;

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
        string? modPath)
    {
        InitializeComponent();
        XmlSyntaxEditorService.Configure(_rawXmlEditor);
        _baseEntries = baseDefinitions.ToDictionary(
            definition => definition.Name,
            definition => new XElement(definition.SourceElement),
            StringComparer.OrdinalIgnoreCase);
        _modPath = modPath;
        if (!string.IsNullOrWhiteSpace(_modPath))
            _modDocument = MajorGodCatalog.LoadOrCreateModDocument(_modPath);
        ShowEmpty();
    }

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
        ShowEmpty();
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectMajorGod(string? name)
    {
        CurrentMajorGodName = name;
        var entry = string.IsNullOrWhiteSpace(name)
            ? null
            : IsModifiedMode
                ? MajorGodCatalog.Find(_modDocument!, name)
                : _baseEntries.GetValueOrDefault(name);

        _isPopulating = true;
        try
        {
            _nameText.Text = name ?? "";
            _rawXmlEditor.Text = entry?.ToString() ?? "";
            XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, !IsModifiedMode || entry == null);
            _editorDirty = false;
            _statusMessage.Text = IsModifiedMode ? "Edit the <civ> XML, then use Save." : "Original Data.bar entry (read-only).";
        }
        finally
        {
            _isPopulating = false;
        }
    }

    public async Task<bool> CommitCurrentMajorGodAsync()
    {
        if (!IsModifiedMode || !_editorDirty || string.IsNullOrWhiteSpace(CurrentMajorGodName))
            return true;

        XElement parsed;
        try
        {
            parsed = XElement.Parse(_rawXmlEditor.Text ?? "", LoadOptions.PreserveWhitespace);
            if (!parsed.Name.LocalName.Equals(MajorGodCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Raw XML must have a <civ> root element.");
            var newName = MajorGodCatalog.GetName(parsed);
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidDataException("The <civ> entry must contain a non-empty <name> element.");
            var duplicate = MajorGodCatalog.Find(_modDocument!, newName);
            var current = MajorGodCatalog.Find(_modDocument!, CurrentMajorGodName);
            if (duplicate != null && !ReferenceEquals(duplicate, current))
                throw new InvalidDataException($"A modified major god named '{newName}' already exists.");
            if (current == null)
                throw new InvalidDataException($"The modified major god '{CurrentMajorGodName}' no longer exists.");

            var oldName = CurrentMajorGodName;
            current.ReplaceWith(parsed);
            CurrentMajorGodName = newName;
            _dirtyNames.Remove(oldName);
            _dirtyNames.Add(newName);
            _nameText.Text = newName;
            _editorDirty = false;
            SetDirty(true);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new Prompt(PromptType.Error, "Invalid major god XML", ex.Message).ShowDialog(owner);
            return false;
        }
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

        var existingNames = new HashSet<string>(GetMajorGodNames(true), StringComparer.OrdinalIgnoreCase);
        var baseName = duplicateSelected && !string.IsNullOrWhiteSpace(CurrentMajorGodName)
            ? CurrentMajorGodName + "Copy"
            : "NewMajorGod";
        var name = baseName;
        for (var suffix = 2; existingNames.Contains(name); suffix++)
            name = baseName + suffix;

        XElement entry;
        if (duplicateSelected && !string.IsNullOrWhiteSpace(CurrentMajorGodName))
        {
            var source = IsModifiedMode
                ? MajorGodCatalog.Find(_modDocument, CurrentMajorGodName)
                : _baseEntries.GetValueOrDefault(CurrentMajorGodName);
            entry = source == null ? new XElement("civ") : new XElement(source);
            var nameElement = entry.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
            if (nameElement == null)
                entry.AddFirst(new XElement("name", name));
            else
                nameElement.Value = name;
        }
        else
        {
            entry = new XElement("civ", new XElement("name", name));
        }

        _modDocument.Root.Add(entry);
        _dirtyNames.Add(name);
        IsModifiedMode = true;
        CurrentMajorGodName = name;
        SetDirty(true);
        SelectMajorGod(name);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteCurrentMajorGodAsync()
    {
        if (!IsModifiedMode || string.IsNullOrWhiteSpace(CurrentMajorGodName))
            return;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            var prompt = new Prompt(PromptType.Confirm, "Delete major god?", $"Delete '{CurrentMajorGodName}' from major_gods_mods.xml?");
            await prompt.ShowDialog(owner);
            if (!prompt.Confirmed)
                return;
        }
        _dirtyNames.Add(CurrentMajorGodName);
        MajorGodCatalog.Find(_modDocument!, CurrentMajorGodName)?.Remove();
        CurrentMajorGodName = null;
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
        => _dirtyNames.Contains(name);

    public void DiscardMajorGodChanges(string name, XElement? savedElement)
    {
        var current = MajorGodCatalog.Find(_modDocument!, name);
        if (current != null && savedElement != null)
            current.ReplaceWith(new XElement(savedElement));
        else if (current != null)
            current.Remove();
        else if (savedElement != null)
            _modDocument!.Root!.Add(new XElement(savedElement));
        _dirtyNames.Remove(name);
        SetDirty(_dirtyNames.Count > 0);
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

        Directory.CreateDirectory(Path.GetDirectoryName(_modPath)!);
        FileIntegrityTransaction.Execute([_modPath], () => _modDocument.Save(_modPath));
        _dirtyNames.Clear();
        SetDirty(false);
        _statusMessage.Text = "Saved major_gods_mods.xml.";
        return true;
    }

    private void RawXmlEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isPopulating || !IsModifiedMode || string.IsNullOrWhiteSpace(CurrentMajorGodName))
            return;
        _editorDirty = true;
        _dirtyNames.Add(CurrentMajorGodName);
        SetDirty(true);
    }

    private void SetDirty(bool value)
    {
        if (IsDirty == value)
            return;
        IsDirty = value;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowEmpty()
    {
        _isPopulating = true;
        try
        {
            _nameText.Text = "";
            _rawXmlEditor.Text = "";
            XmlSyntaxEditorService.SetReadOnly(_rawXmlEditor, true);
            _statusMessage.Text = "Select a major god from the list.";
            _editorDirty = false;
        }
        finally
        {
            _isPopulating = false;
        }
    }
}
