using System.Globalization;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    // One standalone window owns one definition/document. Use the same controller
    // as the Unit pane, but never treat the synthetic serialization Unit as its XML.
    private XmlEditSession? _standaloneXmlSession;
    private XElement? _standaloneAbilitySource;
    private bool _standaloneXmlHadFormChanges;
    private bool _standaloneXmlClosed;
    private bool _standaloneRawSaveInProgress;
    private readonly Dictionary<string, string> _standaloneStringMigrations = new(StringComparer.OrdinalIgnoreCase);
    private bool IsStandaloneXmlEditor => _isAbilityDefinitionEditorMode || _tacticsEditorSession != null;
    internal bool IsSavingStandaloneRawXml => _standaloneRawSaveInProgress;

    private static string StandaloneXmlPreview(XElement element)
        => XmlSyntaxEditorService.PreparePreviewText(FormatXmlForPreview(element));

    private void BindStandaloneXmlPreview(XElement? preview)
    {
        if (_standaloneXmlClosed) return;
        if (preview == null) { _unitXmlController.Bind(null, true); return; }
        var canEdit = _isAbilityDefinitionEditorMode
            ? !_abilityDefinitionEditorReadOnly && _saveAbilityDefinitionAsync != null
            : _tacticsEditorSession?.CanSave == true;
        var text = StandaloneXmlPreview(preview);
        _standaloneXmlSession ??= new XmlEditSession(text, canEdit);
        _standaloneXmlSession.RefreshFromModel(text);
        _unitXmlController.Bind(_standaloneXmlSession, true, allowEditing: canEdit);
    }

    private bool PreserveStandaloneXmlBuffer()
    {
        if (_standaloneXmlClosed) return true;
        if (_standaloneXmlSession?.IsEditing != true) return false;
        _unitXmlController.Bind(_standaloneXmlSession, true);
        return true;
    }

    private async Task<bool> BeginStandaloneXmlEditingAsync()
    {
        try
        {
            if (_isReadOnly || _standaloneXmlClosed) return false;
            if (_isAbilityDefinitionEditorMode && _pendingAbilityEditorLoadTask != null) await _pendingAbilityEditorLoadTask;
            UpdateXmlPreview(force: true); // Capture pending form input before raw mode locks it.
            _standaloneXmlHadFormChanges = _isDirty;
            return _standaloneXmlSession?.CanEdit == true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await new Prompt(PromptType.Error, "XML editing unavailable", exception.Message).ShowDialog(this);
            return false;
        }
    }

    private void StandaloneXmlChanged()
    {
        _isDirty = _standaloneXmlHadFormChanges || _standaloneXmlSession?.IsDirty == true;
        _allowAbilityDefinitionEditorClose = false;
        _saveFeedback.ClearSuccessOnEdit();
    }

    private void RetireStandaloneXmlEditing()
    {
        _standaloneXmlClosed = true;
        _xmlPreviewRefreshTimer.Stop();
        _standaloneXmlSession?.Reset(_standaloneXmlSession.Text);
    }

    private bool AcceptUnchangedStandaloneXml(XmlEditSession.Snapshot snapshot)
    {
        if (_standaloneXmlSession!.IsDirty || _standaloneXmlHadFormChanges) return false;
        _standaloneXmlSession.AcceptSaved(snapshot, snapshot.Text);
        _isDirty = false;
        ReportSaveFeedback(EditorSaveOutcome.Saved);
        return true;
    }

    private XElement ParseStandaloneAbilityXml(string text)
    {
        var power = XElement.Parse(text, LoadOptions.PreserveWhitespace);
        foreach (var field in power.Elements().Where(field => AbilityStringPolicy.FieldTags.Contains(field.Name.LocalName)))
            if (_standaloneStringMigrations.TryGetValue(field.Value.Trim(), out var next)) XmlTriviaPreserver.SetText(field, next);
        ValidateStandaloneAbilityXml(power);
        return power;
    }

    private void ValidateStandaloneAbilityXml(XElement power)
    {
        if (power.Name != "power") throw new InvalidDataException("Ability XML must have one <power> root element without a namespace.");
        var name = GetAbilityElementName(power);
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(name, _abilityDefinitionEditorOriginalName))
            throw new InvalidDataException($"Ability names can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
        if (_availableAbilityNames.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                !existing.Equals(_abilityDefinitionEditorOriginalName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"An ability named '{name}' already exists.");
        if (!PowerCatalog.IsAbilityPower(power))
            throw new InvalidDataException("Ability Type must be UnitAction or an assignable GeneralEffect. God Powers must be edited in Powers.");
        var legacy = _standaloneAbilitySource == null ? null : new XElement(_standaloneAbilitySource);
        legacy?.SetAttributeValue("name", name);
        var validation = PowerSaveValidator.ValidateAbility(power, new PowerSaveCatalogs(
            ["UnitAction", "GeneralEffect"], ProtoUnitOrTypeNames: GetAvailableBuildLimitTargets(),
            ProtoUnitNames: GetAvailableTrainUnitNames(), LegacyPowers: legacy == null ? [] : [legacy]));
        if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        var draft = new AbilityEditorDraft { Name = name, PowerSource = new XElement(power) };
        LoadAbilityChildren(draft, null, power);
        if (!AbilityRangeIndicatorHasRequiredRange(draft.PowerValues.GetValueOrDefault("rangeindicator"), draft.RangeIndicatorAttributes))
            throw new InvalidDataException("A Range Indicator requires a Range before saving.");
    }

    private async Task SaveStandaloneAbilityXmlAsync()
    {
        if (_standaloneRawSaveInProgress || _standaloneXmlSession?.CaptureForSave() is not { } snapshot) return;
        using var feedback = _saveFeedback.BeginSave();
        var validated = false;
        _standaloneRawSaveInProgress = true;
        try
        {
            if (AcceptUnchangedStandaloneXml(snapshot)) { feedback.Complete(); return; }
            var power = ParseStandaloneAbilityXml(snapshot.Text);
            validated = true;
            var before = new XElement(power);
            var strings = new Dictionary<string, string>(LoadCurrentModStringEntries(requireReadable: true), StringComparer.OrdinalIgnoreCase);
            await _saveAbilityDefinitionAsync!(_abilityDefinitionEditorOriginalName!, power, strings, _loadedModStringsFileState);
            if (_standaloneXmlClosed) return;
            // The owning save callback reconciles generated IDs. Preserve its approved
            // direct-field mappings if a newer raw buffer still contains the old IDs.
            foreach (var tag in AbilityStringPolicy.FieldTags)
                if (before.Element(tag) is { } oldField && power.Element(tag) is { } newField && oldField.Value != newField.Value)
                    _standaloneStringMigrations[oldField.Value.Trim()] = newField.Value;
            InvalidateModStringEntriesCache();
            LoadCurrentModStringEntries(requireReadable: true);
            var oldName = _abilityDefinitionEditorOriginalName;
            var name = GetAbilityElementName(power);
            _abilityDefinitionEditorOriginalName = name;
            _standaloneAbilitySource = new XElement(power);
            _availableAbilityNames = _availableAbilityNames.Where(value => !value.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                .Append(name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            var draft = new AbilityEditorDraft { Name = name, PowerSource = new XElement(power), Modified = false };
            LoadAbilityChildren(draft, null, power);
            _abilityDrafts.Clear(); _abilityDrafts[name] = draft;
            _currentAbilityEditorName = "";
            _captureAbilityEditorCurrent = null;
            BuildEditorPanel(_currentUnitName!, resetScroll: false);
            if (_pendingAbilityEditorLoadTask != null) await _pendingAbilityEditorLoadTask;
            var accepted = _standaloneXmlSession.AcceptSaved(snapshot, StandaloneXmlPreview(power));
            _isDirty = _standaloneXmlSession.IsDirty;
            _standaloneXmlHadFormChanges = false;
            if (accepted == XmlSaveAcceptance.Saved) _standaloneStringMigrations.Clear();
            ReportSaveFeedback(_isDirty ? EditorSaveOutcome.NewerEdits : EditorSaveOutcome.Saved);
            feedback.Complete(_isDirty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportSaveFeedback(validated ? EditorSaveOutcome.Failed : EditorSaveOutcome.Blocked);
            await new Prompt(PromptType.Error, validated ? "Unable to save Ability XML" : "Invalid Ability XML", exception.Message).ShowDialog(this);
        }
        finally { _standaloneRawSaveInProgress = false; }
    }

    private XDocument ParseStandaloneTacticsXml(string text)
    {
        var root = XElement.Parse(text, LoadOptions.PreserveWhitespace);
        if (root.Name != "tactics") throw new InvalidDataException("Tactics XML must have one <tactics> root element without a namespace.");
        // The pane edits the root. Keep document-level comments/declaration from
        // the source, rather than replacing them with a synthetic Unit document.
        var document = _tacticsEditorSession!.CommittedDocument;
        document.Root!.ReplaceWith(root);
        ValidateStandaloneTacticsXml(document);
        return document;
    }

    private async Task SaveStandaloneTacticsXmlAsync()
    {
        if (_standaloneRawSaveInProgress || _standaloneXmlSession?.CaptureForSave() is not { } snapshot) return;
        using var feedback = _saveFeedback.BeginSave();
        var validated = false;
        _standaloneRawSaveInProgress = true;
        try
        {
            if (AcceptUnchangedStandaloneXml(snapshot)) { feedback.Complete(); return; }
            var candidate = ParseStandaloneTacticsXml(snapshot.Text);
            validated = true;
            var outcome = await _tacticsEditorSession!.TrySaveAsync(candidate);
            if (_standaloneXmlClosed || outcome is TacticsEditorSaveResult.Reloaded or TacticsEditorSaveResult.Busy or TacticsEditorSaveResult.Unavailable) return;
            if (outcome == TacticsEditorSaveResult.Cancelled) { ReportSaveFeedback(EditorSaveOutcome.Cancelled); return; }
            var committed = _tacticsEditorSession.CommittedDocument;
            AdoptTacticsActionEditorDocument(committed);
            _standaloneXmlSession.AcceptSaved(snapshot, StandaloneXmlPreview(committed.Root!));
            _isDirty = _standaloneXmlSession.IsDirty;
            _standaloneXmlHadFormChanges = false;
            ReportSaveFeedback(_isDirty ? EditorSaveOutcome.NewerEdits : EditorSaveOutcome.Saved);
            feedback.Complete(_isDirty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportSaveFeedback(validated ? EditorSaveOutcome.Failed : EditorSaveOutcome.Blocked);
            await new Prompt(PromptType.Error, validated ? "Unable to save Tactics XML" : "Invalid Tactics XML", exception.Message).ShowDialog(this);
        }
        finally { _standaloneRawSaveInProgress = false; }
    }

    private void ValidateStandaloneTacticsXml(XDocument document)
    {
        var shape = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.Tactics);
        if (!shape.IsSafeToEdit) throw new InvalidDataException(string.Join(Environment.NewLine, shape.Errors.Select(error => error.Reason)));
        var root = document.Root!;
        var baseline = _tacticsEditorSession!.CommittedDocument.Root!;
        var baselineActions = baseline.Elements("action").ToDictionary(action => action.Element("name")!.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        var actions = root.Elements("action").ToList();
        var names = actions.Select(action => action.Element("name")!.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in actions)
        {
            var name = action.Element("name")!.Value.Trim();
            if (!InternalNamePolicy.IsValidOrUnchangedLegacy(name, baselineActions.ContainsKey(name) ? name : null) ||
                string.IsNullOrWhiteSpace(action.Element("type")?.Value))
                throw new InvalidDataException("Every action requires a valid Name and a Type.");
            foreach (var value in action.Elements().Where(element => element.Name.LocalName is "damage" or "rate" or "damagebonus" or "rof" or "maxrange" or "minrange"))
            {
                if (!value.HasElements && (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
                    throw new InvalidDataException($"Action '{name}' /{value.Name} requires a finite numeric value.");
            }
        }
        var tacticNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var oldTactics = ParseTacticDefinitions(_tacticsEditorSession.CommittedDocument);
        foreach (var tactic in root.Elements("tactic"))
        {
            var definition = ParseTacticDefinition(tactic);
            var name = definition.Name;
            if (!InternalNamePolicy.IsValidOrUnchangedLegacy(name, oldTactics.Any(old => old.Name == name) ? name : null) || !tacticNames.Add(name))
                throw new InvalidDataException("Every tactic requires a valid, unique name.");
            foreach (var reference in tactic.Elements("action"))
            {
                if (!names.Contains(reference.Value.Trim())) throw new InvalidDataException($"Tactic '{name}' references missing action '{reference.Value.Trim()}'. Update or remove that reference before saving.");
                if (reference.Attribute("priority") != null && reference.Attribute("probability") != null)
                    throw new InvalidDataException("An action reference cannot have both Priority and Probability.");
                foreach (var attribute in reference.Attributes().Where(attribute => attribute.Name.LocalName is "priority" or "probability"))
                    if (!int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100)
                        throw new InvalidDataException($"{attribute.Name} must be an integer from 0 to 100.");
            }
            foreach (var priority in definition.AttackPriorities)
                if (string.IsNullOrWhiteSpace(priority.TargetType) || !decimal.TryParse(priority.BonusFactorText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100)
                    throw new InvalidDataException("Attack priority requires a target type and a bonus factor from 0 to 100.");
            foreach (var armor in definition.ArmorOverrides)
                if (string.IsNullOrWhiteSpace(armor.Type) || !decimal.TryParse(armor.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("Armor overrides require a type and a numeric value.");
        }
        static XElement ValidationUnit(XElement source) => new("unit", source.Elements("action").Select(action => new XElement(action) { Name = "protoaction" }));
        var broken = ProtoActionIntegrityPolicy.FindBrokenReferences(ValidationUnit(root), ValidationUnit(baseline));
        if (broken.Count > 0) throw new InvalidDataException(ProtoActionIntegrityPolicy.BuildReferenceValidationReport(_tacticsEditorSession.Name, broken));
        ValidateTacticsMixedCatalogs(root, baseline);
    }

    private void ValidateTacticsMixedCatalogs(XElement root, XElement baseline)
    {
        var options = GetAvailableBuildLimitTargets();
        foreach (var action in root.Elements("action"))
        {
            var name = action.Element("name")!.Value.Trim();
            var old = baseline.Elements("action").FirstOrDefault(item => item.Element("name")?.Value.Trim() == name);
            foreach (var field in action.Elements().Where(field => field.Name.LocalName is "rate" or "damagebonus"))
            {
                var value = (field.Attribute("type") ?? field.Attribute("unittype"))?.Value.Trim() ?? "";
                if (value.Equals("All", StringComparison.OrdinalIgnoreCase)) continue;
                var previous = old?.Elements(field.Name).Select(item => (item.Attribute("type") ?? item.Attribute("unittype"))?.Value.Trim()).FirstOrDefault(item => item == value);
                var valid = StrictReferencePolicy.Validate(value, previous, options, false, $"Action '{name}' {field.Name} target");
                if (!valid.IsValid) throw new InvalidDataException(valid.ErrorMessage);
            }
        }
        foreach (var tactic in root.Elements("tactic"))
        {
            var name = ParseTacticDefinition(tactic).Name;
            var old = baseline.Elements("tactic").FirstOrDefault(item => ParseTacticDefinition(item).Name == name);
            foreach (var group in tactic.Elements().Where(element => element.Name.LocalName is "attackprioritytype" or "attacktype" or "excludeattacktype" or "autoattacktype" or "attackresponsetype" or "attackmovetype").GroupBy(element => element.Name.LocalName))
            {
                var oldValues = old?.Elements(group.Key).Select(element => element.Value.Trim()).ToList() ?? [];
                foreach (var element in group)
                {
                    var result = StrictReferencePolicy.Validate(element.Value, oldValues.FirstOrDefault(value => value == element.Value.Trim()), options, false, group.Key);
                    if (!result.IsValid) throw new InvalidDataException(result.ErrorMessage);
                }
            }
        }
    }
}
