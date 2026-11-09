using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private sealed class UnitXmlDraft(XmlEditSession session)
    {
        internal XmlEditSession Session { get; } = session;
        internal XmlEditSession.Snapshot? Prepared { get; set; }
        internal bool HadFormChanges { get; set; }
        internal Dictionary<(string Label, int Index), string> ReferenceBaselines { get; set; } = [];
        internal Dictionary<string, string> StringMigrations { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, UnitXmlDraft> _unitXmlDrafts = new(StringComparer.OrdinalIgnoreCase);
    private XmlEditController _unitXmlController = null!;
    private string? _unitXmlContext;
    private bool _validatingUnitXml;
    private bool _unitXmlPreparedForGlobalSave;
    private bool _unitXmlSavedWithNewerEdits;
    private UnitXmlDraft? CurrentUnitXmlDraft => _currentUnitName == null ? null : _unitXmlDrafts.GetValueOrDefault(_currentUnitName);
    private bool HasUnitXmlEditingSession => _unitXmlDrafts.Values.Any(draft => draft.Session.IsEditing);

    private void InitializeUnitXmlEditing()
    {
        _unitXmlController = new XmlEditController(_xmlPreviewText, _editUnitXmlButton, _unitStructuredForm,
            () => { if (IsStandaloneXmlEditor) StandaloneXmlChanged(); else UnitXmlChanged(); })
        { BeforeEditAsync = () => IsStandaloneXmlEditor ? BeginStandaloneXmlEditingAsync() : BeginUnitXmlEditingAsync() };
        Closed += (_, _) => { RetireStandaloneXmlEditing(); _unitXmlController.Dispose(); };
    }

    private void EnsureUnitXmlContext()
    {
        if (string.Equals(_unitXmlContext, _modFilePath, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var draft in _unitXmlDrafts.Values) draft.Session.Reset(draft.Session.Text);
        _unitXmlDrafts.Clear();
        _unitXmlContext = _modFilePath;
    }

    private void BindUnitXmlPreview(XElement unit, bool canEdit)
    {
        EnsureUnitXmlContext();
        var text = UnitXmlPreview(unit);
        XmlEditSession session;
        if (canEdit && _currentUnitName != null)
        {
            if (!_unitXmlDrafts.TryGetValue(_currentUnitName, out var draft))
                _unitXmlDrafts[_currentUnitName] = draft = new UnitXmlDraft(new XmlEditSession(text, true));
            session = draft.Session;
            session.RefreshFromModel(text);
        }
        else session = new XmlEditSession(text, false);
        _unitXmlController.Bind(session, formEnabled: true, allowEditing: canEdit);
    }

    private static string UnitXmlPreview(XElement unit)
        => XmlSyntaxEditorService.PreparePreviewText(FormatXmlForPreview(unit));

    private async Task<bool> BeginUnitXmlEditingAsync()
    {
        try
        {
            if (_isReadOnly || !await CaptureCurrentUnitDraftAsync()) return false;
            UpdateXmlPreview(force: true);
            if (CurrentUnitXmlDraft is not { } draft) return false;
            draft.HadFormChanges = _dirtyUnitNames.Contains(_currentUnitName!);
            draft.ReferenceBaselines = ReadUnitXmlReferenceBaselines();
            return true;
        }
        catch (Exception exception)
        {
            await new Prompt(PromptType.Error, "XML editing unavailable", exception.Message).ShowDialog(this);
            return false;
        }
    }

    private Dictionary<(string Label, int Index), string> ReadUnitXmlReferenceBaselines()
        => _statsStrictReferenceEditors.GroupBy(item => item.Label)
            .SelectMany(group => group.Select((item, index) => (Key: (item.Label, index), Value: item.ReadValue())))
            .ToDictionary(item => item.Key, item => item.Value);

    private void UnitXmlChanged()
    {
        if (_currentUnitName == null || CurrentUnitXmlDraft is not { } draft) return;
        // The controller already excludes programmatic events. Unlike form events,
        // actual XML typing must not be ignored by a delayed form-population guard.
        if (draft.Session.IsDirty || draft.HadFormChanges) _dirtyUnitNames.Add(_currentUnitName);
        else _dirtyUnitNames.Remove(_currentUnitName);
        _isDirty = _dirtyUnitNames.Count > 0;
        _saveFeedback.ClearSuccessOnEdit();
        RefreshDocumentTabHeaders();
    }

    private void MoveUnitXmlDraft(string oldName, string newName)
    {
        if (_unitXmlDrafts.Remove(oldName, out var draft)) _unitXmlDrafts[newName] = draft;
    }

    private void ForgetUnitXmlDraft(string? name)
    {
        if (name != null && _unitXmlDrafts.Remove(name, out var draft)) draft.Session.Reset(draft.Session.Text);
    }

    private void ReleaseCleanUnitXmlDraft(string? name)
    {
        if (name != null && _unitXmlDrafts.GetValueOrDefault(name)?.Session.IsDirty == false) ForgetUnitXmlDraft(name);
    }

    private XElement ParseUnitXml(string name, UnitXmlDraft draft, string text)
    {
        XElement unit;
        try { unit = XElement.Parse(text, LoadOptions.PreserveWhitespace); }
        catch (System.Xml.XmlException exception)
        { throw new InvalidDataException($"Unit '{name}' has invalid XML: {exception.Message}", exception); }
        if (unit.Name.Namespace != XNamespace.None || !unit.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unit '{name}' XML must have one <unit> root element.");
        var newName = unit.Attribute("name")?.Value.Trim() ?? "";
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, name))
            throw new InvalidDataException($"Unit names can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
        if (!name.Equals(newName, StringComparison.OrdinalIgnoreCase) && UnitNameExistsAnywhere(newName, name))
            throw new InvalidDataException($"A ProtoUnit named '{newName}' already exists.");
        unit.SetAttributeValue("name", newName);
        // A newer buffer can still contain the old owned IDs after a rename saved.
        // Migrate only known string fields, never arbitrary matching XML text.
        foreach (var field in unit.Elements().Where(field => StringBackedFieldTags.Contains(field.Name.LocalName)))
            if (draft.StringMigrations.TryGetValue(field.Value, out var migrated)) field.Value = migrated;
        return unit;
    }

    private XDocument CreateUnitXmlSnapshot()
    {
        var document = new XDocument(_modXmlDoc!);
        foreach (var (name, draft) in _unitXmlDrafts.Where(entry => entry.Value.Session.IsDirty))
        {
            if (draft.Prepared?.Text == draft.Session.Text) continue;
            var original = document.Root == null ? null : ProtoXmlHandler.GetUnitElement(document.Root, name);
            if (original == null) throw new InvalidDataException($"Unit '{name}' is no longer available. Discard or reload its XML draft.");
            original.ReplaceWith(ParseUnitXml(name, draft, draft.Session.Text));
        }
        return document;
    }

    private async Task<bool> PrepareUnitXmlForSaveAsync()
    {
        if (!HasUnitXmlEditingSession) return true;
        if (_modXmlRoot == null || _modDocumentSafetyError != null) return false;
        var backup = CreateProtoUnitDraftCaptureSnapshot();
        var wasDirty = _isDirty;
        var keys = _unitXmlDrafts.ToArray();
        var migrations = keys.ToDictionary(entry => entry.Value,
            entry => new Dictionary<string, string>(entry.Value.StringMigrations, StringComparer.OrdinalIgnoreCase));
        var tabs = _openDocumentTabs.Select(tab => (Tab: tab, Name: tab.EntityName)).ToArray();
        var selected = _currentUnitName;
        var selectedTab = _editorTabs.SelectedIndex;
        var success = false;
        _validatingUnitXml = true;
        try
        {
            foreach (var (name, draft) in keys.Where(entry => entry.Value.Session.IsEditing))
            {
                if (UsesUnitAdditiveReader && AdditiveXmlReader.Classify(_barXmlRoot, name, true) != AdditiveEntryKind.Custom)
                    throw new InvalidDataException($"Unit '{name}' is an original override or has an unresolved original identity. Additive editing is not enabled in this checkpoint.");
                var snapshot = draft.Session.CaptureForSave()!;
                var original = ProtoXmlHandler.GetUnitElement(_modXmlRoot, name);
                if (original == null)
                {
                    if (!draft.Session.IsDirty) { ForgetUnitXmlDraft(name); continue; }
                    throw new InvalidDataException($"Unit '{name}' is no longer available. Discard or reload its XML draft.");
                }
                if (!draft.Session.IsDirty) { draft.Prepared = snapshot; continue; }
                var before = new XElement(original);
                var candidate = ParseUnitXml(name, draft, snapshot.Text);
                // These three fields use noneditable ComboBoxes rather than the
                // registered searchable reference controls. Loading a candidate
                // adds its stored value as a legacy choice, so validate against
                // the real pre-edit value before constructing that form.
                foreach (var tag in new[] { "movementtype", "impacttype", "unitaitype" })
                {
                    var validation = StrictReferencePolicy.Validate(candidate.Element(tag)?.Value,
                        before.Element(tag)?.Value, ProtoConstants.FieldSuggestions.GetValueOrDefault(tag) ?? [],
                        allowEmpty: true, tag);
                    if (!validation.IsValid) throw new InvalidDataException(validation.ErrorMessage);
                }
                var rawCandidate = new XElement(candidate);
                var newName = candidate.Attribute("name")!.Value;
                // Project the candidate into the existing validators. Keep its old
                // identity until the ordinary rename/string lifecycle has run.
                candidate.SetAttributeValue("name", name);
                original.ReplaceWith(candidate);
                BuildEditorPanel(name, resetScroll: false);
                var originalActions = before.Elements("protoaction").Select(action => action.Element("name")?.Value.Trim() ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var state in _protoActionWidgets)
                    if (!originalActions.Contains(state.Model.Name) && !_currentUnitTacticsActions.ContainsKey(state.Model.Name))
                        state.IsNewCustomAction = true;
                foreach (var group in _statsStrictReferenceEditors.GroupBy(item => item.Label))
                {
                    var index = 0;
                    foreach (var item in group.ToArray())
                    {
                        var at = _statsStrictReferenceEditors.IndexOf(item);
                        _statsStrictReferenceEditors[at] = item with
                        { OriginalValue = draft.ReferenceBaselines.GetValueOrDefault((item.Label, index++), "") };
                    }
                }
                var populating = _isPopulating;
                _isPopulating = true;
                try { ((TextBox)_fieldControls[ProtoUnitNameFieldKey]).Text = newName; }
                finally { _isPopulating = populating; }
                var issues = ProtoActionIntegrityPolicy.FindBrokenReferences(candidate, before, _currentUnitTacticsActions.Keys);
                if (issues.Count > 0)
                    throw new InvalidDataException(ProtoActionIntegrityPolicy.BuildReferenceValidationReport(newName, issues));
                _dirtyUnitNames.Add(name);
                _isDirty = true;
                if (!await CaptureCurrentUnitDraftCoreAsync()) return false;
                var serialized = ProtoXmlHandler.GetUnitElement(_modXmlRoot, newName)!;
                // The form is used for validation/lifecycle preparation, not as a
                // replacement serializer for raw XML. Preserve omissions, unknown
                // attributes, comments and children exactly as the user supplied.
                candidate = rawCandidate;
                candidate.SetAttributeValue("name", newName);
                if (!name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                    foreach (var field in candidate.Elements().Where(field => StringBackedFieldTags.Contains(field.Name.LocalName)))
                    {
                        var renamedId = serialized.Element(field.Name)?.Value;
                        if (renamedId == null || renamedId == field.Value) continue;
                        draft.StringMigrations[field.Value] = renamedId;
                        field.Value = renamedId;
                    }
                serialized.ReplaceWith(candidate);
                draft.Prepared = snapshot;
                if (string.Equals(selected, name, StringComparison.OrdinalIgnoreCase)) selected = newName;
            }
            success = true;
            return true;
        }
        catch (Exception exception)
        {
            await new Prompt(PromptType.Error, "Invalid Unit XML", "Unit changes were not saved:\n\n" + exception.Message).ShowDialog(this);
            return false;
        }
        finally
        {
            _validatingUnitXml = false;
            if (!success)
            {
                RestoreProtoUnitDraftCaptureSnapshot(backup);
                _isDirty = wasDirty;
                _unitXmlDrafts.Clear();
                foreach (var entry in keys)
                {
                    entry.Value.Prepared = null;
                    entry.Value.StringMigrations.Clear();
                    foreach (var migration in migrations[entry.Value]) entry.Value.StringMigrations.Add(migration.Key, migration.Value);
                    _unitXmlDrafts.Add(entry.Key, entry.Value);
                }
                foreach (var tab in tabs) tab.Tab.EntityName = tab.Name;
                selected = backup.CurrentUnitName;
            }
            if (selected != null && ProtoXmlHandler.GetUnitElement(_modXmlRoot!, selected) != null)
                BuildEditorPanel(selected, resetScroll: false);
            _editorTabs.SelectedIndex = selectedTab;
            RefreshDocumentTabHeaders();
        }
    }

    private bool AcknowledgeSavedUnitXml()
    {
        var refreshed = false;
        _unitXmlSavedWithNewerEdits = false;
        foreach (var (name, draft) in _unitXmlDrafts)
        {
            if (draft.Prepared is not { } snapshot) continue;
            draft.Prepared = null;
            var saved = _modXmlRoot == null ? null : ProtoXmlHandler.GetUnitElement(_modXmlRoot, name);
            if (saved == null) continue;
            var result = draft.Session.AcceptSaved(snapshot, UnitXmlPreview(saved));
            refreshed |= result != XmlSaveAcceptance.Rejected;
            draft.HadFormChanges = false;
            if (draft.Session.IsDirty)
            {
                _dirtyUnitNames.Add(name);
                _isDirty = _unitXmlSavedWithNewerEdits = true;
            }
        }
        return refreshed;
    }
}
