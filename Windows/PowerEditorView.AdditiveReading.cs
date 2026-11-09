using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Windows;

public partial class PowerEditorView
{
    private readonly bool _basePowerCatalogAvailable;
    private AdditiveReadResult? _powerAdditiveRead;
    private bool _powerHasSingleEffectiveEntry = true;
    private string? _powerDocumentSafetyError;
    private readonly AdditiveChangeListState _powerChangeLists = new();
    internal void ResetAdditiveChangeCards(string name, bool modified) => _powerChangeLists.ResetEntry(name, modified);

    public bool CanModifyPowerDocument => _basePowerCatalogAvailable && _powerDocumentSafetyError == null;
    internal AdditiveEntryKind GetPowerEntryKind(string name)
    {
        var kind = AdditiveXmlReader.Classify(_basePowerCatalogAvailable ? _baseEntries.ContainsKey(name) : null, true);
        if (kind != AdditiveEntryKind.Custom) return kind;
        var authored = _modDocument == null ? null : PowerCatalog.Find(_modDocument, name);
        return authored != null && AdditiveIdentityRules.Attribute(authored, "mergeMode")?.ToLowerInvariant() is "remove" or "replace"
            ? AdditiveEntryKind.Unresolved : AdditiveEntryKind.Custom;
    }
    internal bool CanEditPower(string name) => CanModifyPowerDocument && GetPowerEntryKind(name) == AdditiveEntryKind.Custom;
    private bool IsEditablePower => IsModifiedMode && CanModifyPowerDocument &&
        (CurrentPowerName == null || GetPowerEntryKind(CurrentPowerName) == AdditiveEntryKind.Custom);
    private bool IsCurrentAdditivePower => IsModifiedMode && CurrentPowerName != null && GetPowerEntryKind(CurrentPowerName) != AdditiveEntryKind.Custom;

    private static XElement? FindPowerPatch(XDocument document, string name)
        => document.Root?.Elements(PowerCatalog.EntryName).FirstOrDefault(entry => PowerCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase));

    private bool HasReadablePower(string name) => _modDocument != null && FindPowerPatch(_modDocument, name) is { } entry &&
        (_baseEntries.ContainsKey(name) || PowerCatalog.IsModCatalogPower(entry));

    private HashSet<string> ReadOnlyPowerNames(XDocument document)
        => (document.Root?.Elements(PowerCatalog.EntryName) ?? [])
            .Select(PowerCatalog.GetName).Where(name => GetPowerEntryKind(name) != AdditiveEntryKind.Custom)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private PreparedModXmlDocument LoadPowerSource()
    {
        if (!File.Exists(_modPath)) return ModDocumentPersistence.Prepare(_modPath!, ModXmlDocumentKind.Powers);
        var loaded = ModXmlDocumentSafety.LoadExisting(_modPath!, ModXmlDocumentKind.Powers, allowUnsafeStructureForReadOnly: true);
        var names = loaded.Document.Root?.Elements(PowerCatalog.EntryName).Select(PowerCatalog.GetName)
            .Where(name => !_basePowerCatalogAvailable || _baseEntries.ContainsKey(name)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var validation = ModXmlDocumentSafety.Validate(loaded.Document, ModXmlDocumentKind.Powers, names);
        _powerDocumentSafetyError = validation.IsSafeToEdit ? null : validation.ToDisplayMessage(_modPath);
        return new(Path.GetFullPath(_modPath!), ModXmlDocumentKind.Powers, loaded.Document, loaded.FileState, names);
    }

    private XElement? ResolvePowerForReading(string name)
    {
        _powerChangeLists.SelectEntry(name, IsModifiedMode);
        _powerAdditiveRead = null;
        _powerHasSingleEffectiveEntry = true;
        if (!IsModifiedMode) return _baseEntries.GetValueOrDefault(name);
        var authored = _modDocument == null ? null : FindPowerPatch(_modDocument, name);
        var kind = GetPowerEntryKind(name);
        if (kind == AdditiveEntryKind.Custom) return _modDocument == null ? null : PowerCatalog.Find(_modDocument, name);
        if (authored == null || kind != AdditiveEntryKind.Modified) return authored;
        var fragments = new XElement(PowerCatalog.ModRootName, _modDocument!.Root!.Elements(PowerCatalog.EntryName)
            .Where(entry => PowerCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)).Select(entry => new XElement(entry)));
        _powerAdditiveRead = AdditiveXmlReader.Evaluate(new XElement("powers", new XElement(_baseEntries[name])), fragments, AdditiveFamily.Powers);
        var entries = _powerAdditiveRead.Effective.Elements(PowerCatalog.EntryName).ToArray();
        _powerHasSingleEffectiveEntry = entries.Length == 1;
        return entries.Length == 1 ? entries[0] : new XElement(PowerCatalog.EntryName, new XAttribute("name", name));
    }

    private void ClearPowerAdditivePresentation()
    {
        AdditiveChangePresentation.Clear(_powerStructuredForm);
        _powerAdditiveRead = null;
        _powerHasSingleEffectiveEntry = true;
        _powerAdditiveSummary.IsVisible = _powerAdditiveDiagnostics.IsVisible = false;
        _powerPropertyNotices.Children.Clear(); _powerSpecificNotices.Children.Clear();
        _powerPropertyNotices.IsVisible = _powerSpecificNotices.IsVisible = false;
        _powerTabs.IsVisible = true;
    }

    private StackPanel PowerNoticeSection(string path)
    {
        var tag = path.Split('/').Skip(2).FirstOrDefault() ?? "";
        return SpecificAttributeSpecs().Any(spec => spec.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            ? _powerSpecificNotices : _powerPropertyNotices;
    }

    private void PresentPowerAdditiveReading()
    {
        _powerPropertyNotices.Children.Clear(); _powerSpecificNotices.Children.Clear();
        _powerPropertyNotices.IsVisible = _powerSpecificNotices.IsVisible = false;
        _powerAdditiveSummary.IsVisible = IsCurrentAdditivePower;
        _powerAdditiveDiagnostics.IsVisible = false;
        _powerTabs.IsVisible = _powerHasSingleEffectiveEntry;
        if (!IsModifiedMode) return;
        if (_powerDocumentSafetyError != null)
        { _powerAdditiveDiagnostics.Text = _powerDocumentSafetyError; _powerAdditiveDiagnostics.IsVisible = true; }
        if (!IsCurrentAdditivePower) return;
        _powerAdditiveSummary.Text = _powerAdditiveRead == null ? "Original unresolved · Read-only" : "Modified original · Read-only";
        if (_powerAdditiveRead == null)
        {
            _powerAdditiveDiagnostics.Text = "The original Power cannot be resolved. Source XML is preserved.";
            _powerAdditiveDiagnostics.IsVisible = true; return;
        }
        var read = _powerAdditiveRead;
        if (!_powerHasSingleEffectiveEntry)
        {
            _powerAdditiveDiagnostics.Text = read.Effective.Elements(PowerCatalog.EntryName).Any()
                ? "The patch produces multiple Powers with this identity. Review the source XML." : "This Power is removed by the mod.";
            _powerAdditiveDiagnostics.IsVisible = true;
        }
        foreach (var panel in new[] { _powerPropertyNotices, _powerSpecificNotices })
        {
            foreach (var kind in new[] { AdditiveChangeListKind.Replaced, AdditiveChangeListKind.Removed })
            {
                var mode = kind == AdditiveChangeListKind.Replaced ? "replace" : "remove";
                var lines = read.Changes.Where(change => change.Mode == mode && change.Before != null &&
                    (change.Path == "/power" || ReferenceEquals(PowerNoticeSection(change.Path), panel)))
                    .Select(change => PowerChangeLabel(change, kind == AdditiveChangeListKind.Replaced)).ToArray();
                if (lines.Length == 0) continue;
                panel.Children.Add(_powerChangeLists.CreateCard(ReferenceEquals(panel, _powerSpecificNotices) ? "specific" : "properties", kind, lines));
            }
        }
        foreach (var diagnostic in read.Diagnostics)
        {
            var split = diagnostic.IndexOf(':');
            var path = split < 0 ? "" : diagnostic[..split];
            var detail = split < 0 ? diagnostic : diagnostic[(split + 1)..].Trim();
            var title = "Mod change needs review";
            if (detail.Contains("identity matches; first sibling selected", StringComparison.Ordinal))
            { title = "Several entries match"; detail = "The game applies this change to the first matching entry."; }
            else if (detail.EndsWith("found no match.", StringComparison.Ordinal))
            { title = "No matching entry"; detail = "The " + detail.Split(' ')[0] + " operation has no effect because its original entry could not be found."; }
            var notice = AdditiveChangePresentation.CreateNotice(title, detail);
            ToolTip.SetTip(notice, diagnostic); PowerNoticeSection(path).Children.Add(notice);
        }
        BindPowerAdditiveFields();
        AdditiveChangePresentation.DecorateBoundFields(_powerStructuredForm, read);
        _powerPropertyNotices.IsVisible = _powerPropertyNotices.Children.Count > 0;
        _powerSpecificNotices.IsVisible = _powerSpecificNotices.Children.Count > 0;
    }

    private static string PowerFieldLabel(string tag) => tag.ToLowerInvariant() switch
        {
            "soundset" => "Sound set", "activetime" => "Active time", "repeatcost" => "Repeat cost",
            "healspeed" => "Heal speed", "createunit" => "Create unit", "effectinarea" => "Effect in area",
            "unitmodify" => "Unit modification", "switchingeffects" => "Switching effects", "tempunit" => "Temporary unit",
            "onhiteffect" => "On hit effect", "extratooltips" => "Extra tooltips", "notificationsound" => "Notification sound",
            "notificationmessageid" => "Notification message", "resourcetierreq" => "Resource tier requirement",
            "combatxptierreq" => "Combat XP tier requirement", _ => TechnologyEditorView.HumanizeLabel(tag)
        };

    private static string PowerChangeLabel(AdditiveChange change, bool replacement)
    {
        if (change.Path == "/power") return "Entire Power";
        var entry = replacement ? change.Patch : change.Before!;
        var label = AdditiveChangePresentation.WithParentPath(change.Path, PowerFieldLabel(entry.Name.LocalName), PowerFieldLabel);
        var selectors = string.Join(", ", entry.Attributes().Where(attribute => !attribute.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Value));
        return label + (selectors.Length == 0 ? "" : " (" + selectors + ")") +
            (replacement || entry.HasElements || string.IsNullOrWhiteSpace(entry.Value) ? "" : ": " + entry.Value);
    }

    private void BindPowerAdditiveFields()
    {
        if (_currentEntry == null) return;
        BindPowerValidationFields();
        foreach (var control in _powerStructuredForm.GetLogicalDescendants().OfType<Control>())
        {
            if (control is not (TextBox or AutoCompleteBox or ComboBox or CheckBox)) continue;
            if (control.Tag is XObject owner) AdditiveChangePresentation.BindXml(control, owner);
            var hasCurrentBinding = EditorValidationBindings.TryGetXmlSource(control, out var bound, out _) &&
                bound?.AncestorsAndSelf().Any(owner => ReferenceEquals(owner, _currentEntry)) == true;
            if (!hasCurrentBinding && !string.IsNullOrWhiteSpace(control.Name) && control.Tag is string tag && PowerCatalog.FindField(_currentEntry, tag) is { } source)
                AdditiveChangePresentation.Bind(control, source);
        }
        void Root(Control control, string tag, string? field = null)
        { if (PowerCatalog.FindField(_currentEntry, tag) is { } source) AdditiveChangePresentation.Bind(control, source, field); }
        Root(_placementBox, "placement"); Root(_powerPlayerRelationBox, "powerplayerrelation");
        Root(_rangeIndicatorBox, "rangeindicator"); Root(_indicatorRangeBox, "rangeindicator", "@range");
        Root(_indicatorWidthBox, "rangeindicator", "@width"); Root(_indicatorSpeedBox, "rangeindicator", "@speed");
        Root(_rangeDecalBox, "rangedecal"); Root(_rangeIndicatorProtoBox, "rangeindicatorproto");
        Root(_lightingBox, "lighting"); Root(_displayAlertsCheck, "displayalerts");
        EditorValidationBindings.Bind(_orderHintBox, _currentEntry, "@orderhint");
        EditorValidationBindings.Bind(_powerTypeBox, _currentEntry, "@type");
        if (PowerCatalog.FindField(_currentEntry, "icon") is { } icon) AdditiveChangePresentation.Bind(_iconEditor.CompactPresenter, icon);
        foreach (var (box, tag) in new[] { (_singleActivationCheck, "singleactivation"), (_exclusiveCheck, "exclusive"),
                     (_activeTimeOnTargetCheck, "activetimeontarget"), (_globalCheck, "global") })
            if (PowerCatalog.FindField(_currentEntry, tag) is { } source) AdditiveChangePresentation.Bind(box, source);
    }

    private Border BindPowerPlacementFlagChip(Border chip, string flag)
    {
        if (_currentEntry == null) return chip;
        XObject? source = GetStandalonePlacementFlagTag(flag) is { } tag
            ? PowerCatalog.FindField(_currentEntry, tag) : FindCataloguedPlacementFlag(PowerCatalog.FindField(_currentEntry, "placement"), flag);
        return source == null ? chip : AdditiveChangePresentation.BindXml(chip, source);
    }

    private Border BindPowerTypeChip(Border chip, string tag, string value)
    {
        var source = _powerAdditiveRead?.Changes.LastOrDefault(change => change.Result is { Parent: { } parent } result &&
            ReferenceEquals(parent, _currentEntry) && result.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
            result.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase))?.Result ??
            _currentEntry?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
        return source == null ? chip : AdditiveChangePresentation.Bind(chip, source);
    }

    private PowerSaveValidation ValidatePowerCandidate(XDocument document, PowerSaveCatalogs catalogs)
    {
        var names = ReadOnlyPowerNames(document);
        var error = ReadOnlyPowerChangeError(document);
        return error == null ? PowerSaveValidator.Validate(document, catalogs, names) : new PowerSaveValidation([error]);
    }

    private string? ReadOnlyPowerChangeError(XDocument document)
    {
        foreach (var entry in _loadedModDocument?.Root?.Elements(PowerCatalog.EntryName) ?? [])
        {
            var name = PowerCatalog.GetName(entry);
            if (GetPowerEntryKind(name) == AdditiveEntryKind.Custom) continue;
            if (!XNode.DeepEquals(entry, FindPowerPatch(document, name)))
                return $"Power '{name}' is read-only in this checkpoint. Its authored patch must remain unchanged.";
        }
        return null;
    }
}
