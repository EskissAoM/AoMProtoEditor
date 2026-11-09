using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private AdditiveReadResult? _unitAdditiveRead;
    private AdditiveEntryKind _unitAdditiveKind;
    private readonly AdditiveChangeListState _unitChangeLists = new();

    private void ResetAdditiveCardsForBrowserEntry(EditorEntityKind kind, bool modified, string name)
    {
        if (kind == EditorEntityKind.Units) _unitChangeLists.ResetEntry(name, modified);
        else if (kind == EditorEntityKind.Technologies) _technologyView?.ResetAdditiveChangeCards(name, modified);
        else if (kind == EditorEntityKind.Powers) _powerView?.ResetAdditiveChangeCards(name, modified);
        else if (kind == EditorEntityKind.MajorGods) _majorGodView?.ResetAdditiveChangeCards(name, modified);
    }
    private StackPanel[] UnitNoticePanels => [_unitStatsNotices, _unitActionNotices, _unitCommandNotices, _unitAbilityNotices, _unitTrainResearchNotices];

    private void ClearUnitSectionNotices()
    {
        foreach (var panel in UnitNoticePanels) { panel.Children.Clear(); panel.IsVisible = false; }
    }

    private static int UnitChangeTab(AdditiveChange change)
    {
        var tag = change.Path.Split('/').Skip(2).FirstOrDefault()?.ToLowerInvariant() ?? "";
        return tag switch
        {
            "protoaction" or "tactics" => 1,
            "command" or "optionalcommand" or "transformcommand" => 2,
            "ability" or "abilities" or "rechargetime" or "recharge" or "auxrechargetime" or "auxrecharge" or
                "rechargeincludetypes" or "rechargeexcludetypes" or "auxrechargeincludetypes" or "auxrechargeexcludetypes" => 3,
            "train" or "tech" => 4,
            _ => 0
        };
    }

    private static bool UnitChangeAppliesToTab(AdditiveChange change, int index)
    {
        if (change.Path == "/unit") return true;
        var tag = change.Path.Split('/').Skip(2).FirstOrDefault() ?? "";
        // Recharge fields are intentionally mirrored in Stats and Abilities.
        return tag.StartsWith("recharge", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("auxrecharge", StringComparison.OrdinalIgnoreCase)
            ? index is 0 or 3 : UnitChangeTab(change) == index;
    }
    private readonly HashSet<AdditiveEntryKind> _collapsedUnitGroups = [];
    private readonly HashSet<AdditiveEntryKind> _collapsedTechnologyGroups = [];
    private readonly HashSet<AdditiveEntryKind> _collapsedPowerGroups = [];
    private readonly HashSet<AdditiveEntryKind> _collapsedMajorGodGroups = [];
    private HashSet<AdditiveEntryKind> CurrentCollapsedAdditiveGroups => _activeEntityKind switch
    { EditorEntityKind.Technologies => _collapsedTechnologyGroups, EditorEntityKind.Powers => _collapsedPowerGroups,
        EditorEntityKind.MajorGods => _collapsedMajorGodGroups, _ => _collapsedUnitGroups };

    private string GetDocumentTabSource(EditorDocumentTabState tab)
    {
        if (!tab.IsModified) return "Original";
        if (string.IsNullOrWhiteSpace(tab.EntityName)) return "Mod";
        return tab.EntityKind switch
        {
            EditorEntityKind.Units => AdditiveXmlReader.Classify(_barXmlRoot, tab.EntityName, true).ToString(),
            EditorEntityKind.Technologies => (_technologyView?.GetTechnologyEntryKind(tab.EntityName) ?? AdditiveEntryKind.Unresolved).ToString(),
            EditorEntityKind.Powers => (_powerView?.GetPowerEntryKind(tab.EntityName) ?? AdditiveEntryKind.Unresolved).ToString(),
            EditorEntityKind.MajorGods => (_majorGodView?.GetMajorGodEntryKind(tab.EntityName) ?? AdditiveEntryKind.Unresolved).ToString(),
            _ => "Custom"
        };
    }

    private void ToggleUnitGroup(AdditiveEntryKind kind)
    {
        if (!CurrentCollapsedAdditiveGroups.Add(kind)) CurrentCollapsedAdditiveGroups.Remove(kind);
        _suppressUnitListSelectionChange = true;
        try { FilterUnitList(); RestoreEntityBrowserSelection(); }
        finally { _suppressUnitListSelectionChange = false; }
    }

    private void UnitGroupToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: UnitListItem { GroupKind: { } kind } }) ToggleUnitGroup(kind);
        e.Handled = true;
    }

    private bool UsesUnitAdditiveReader => !_isAbilityDefinitionEditorMode && !_protoActionHostAdapter.Context.UsesStandaloneDocumentLifecycle;

    private XElement? ResolveUnitForReading(string name)
    {
        _unitChangeLists.SelectEntry(name, _unitTabs.SelectedIndex == 1);
        _unitAdditiveRead = null;
        _unitChangeSummaryText.IsVisible = false;
        ClearUnitSectionNotices();
        var authored = _modXmlRoot == null ? null : ProtoXmlHandler.GetUnitElement(_modXmlRoot, name);
        var original = _barXmlRoot == null ? null : ProtoXmlHandler.GetUnitElement(_barXmlRoot, name);
        _unitAdditiveKind = AdditiveXmlReader.Classify(_barXmlRoot, name, authored != null);
        if (!UsesUnitAdditiveReader) return authored ?? original;
        if (original == null && authored != null && AdditiveIdentityRules.Attribute(authored, "mergeMode")?.ToLowerInvariant() is "remove" or "replace")
        {
            _unitAdditiveKind = AdditiveEntryKind.Unresolved;
            return authored;
        }
        if (_unitTabs.SelectedIndex == 0 && original != null)
        { _unitAdditiveKind = AdditiveEntryKind.Original; return original; }
        if (_unitAdditiveKind != AdditiveEntryKind.Modified) return authored ?? original;
        var fragments = new XElement("protomods", _modXmlRoot!.Elements("unit").Where(u =>
            string.Equals((string?)u.Attribute("name"), name, StringComparison.OrdinalIgnoreCase)).Select(u => new XElement(u)));
        _unitAdditiveRead = AdditiveXmlReader.Evaluate(new XElement("proto", new XElement(original!)), fragments, AdditiveFamily.Proto);
        var units = _unitAdditiveRead.Effective.Elements("unit").ToArray();
        return units.Length == 1 ? units[0] : null;
    }

    private void PresentUnitAdditiveReading()
    {
        if (!UsesUnitAdditiveReader) return;
        if (_unitAdditiveKind == AdditiveEntryKind.Unresolved)
            _statsEditorPanel.Children.Insert(0, new TextBlock { Text = "Original identity cannot be resolved. Source is preserved; editing is unavailable.", TextWrapping = TextWrapping.Wrap });
        if (_unitAdditiveRead == null) return;
        _unitChangeSummaryText.IsVisible = true;
        _unitChangeSummaryText.Text = "Modified original · Read-only";
        foreach (var diagnostic in _unitAdditiveRead.Diagnostics)
            _statsEditorPanel.Children.Insert(1, new TextBlock { Text = diagnostic, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange });
        static void Mark(Control control, AdditiveChange change) => AdditiveChangePresentation.Decorate(control, change);
        foreach (var change in _unitAdditiveRead.Changes)
        {
            if (change.Path.Count(c => c == '/') == 2 && _fieldControls.TryGetValue(change.Patch.Name.LocalName, out var control)) Mark(control, change);
        }
        foreach (var state in _protoActionWidgets) DecorateAdditiveActionFields(state);
        var disabled = FindDisabledOriginalActionFlags();
        var panels = UnitNoticePanels;
        for (var index = 0; index < panels.Length; index++)
        {
            var panel = panels[index]; panel.Children.Clear();
            foreach (var kind in new[] { AdditiveChangeListKind.Replaced, AdditiveChangeListKind.Removed })
            {
                var lines = _unitAdditiveRead.Changes.Where(change => change.Before != null && change.Mode ==
                    (kind == AdditiveChangeListKind.Replaced ? "replace" : "remove") &&
                    UnitChangeAppliesToTab(change, index))
                    .Select(change => UnitChangeLabel(change, kind == AdditiveChangeListKind.Replaced)).ToArray();
                if (lines.Length > 0) panel.Children.Add(_unitChangeLists.CreateCard(index.ToString(), kind, lines));
            }
            if (index == 1 && disabled.Count > 0)
                panel.Children.Add(_unitChangeLists.CreateCard("1", AdditiveChangeListKind.Disabled, disabled));
            panel.IsVisible = panel.Children.Count > 0;
        }
    }

    private Dictionary<string, ProtoAction> LoadOriginalUnitTactics(string tacticsName)
    {
        // The baseline must not pick an active mod's replacement tactics file.
        var gameplay = ResolveBaseGameplayDirectory();
        if (gameplay != null)
        {
            foreach (var relative in BuildTacticsCandidateRelativePaths(tacticsName))
            {
                var path = ResolveContainedTacticsPath(Path.Combine(gameplay, "tactics"), relative);
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    var xml = path.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)
                        ? XmbReader.ToFormattedXml(File.ReadAllBytes(path)) : File.ReadAllText(path);
                    return string.IsNullOrWhiteSpace(xml) ? new(StringComparer.OrdinalIgnoreCase) : ParseTacticsActions(xml);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
                { return new(StringComparer.OrdinalIgnoreCase); }
            }
        }
        return LoadProtoActionsFromBarTactics(tacticsName);
    }

    private IReadOnlyList<string> FindDisabledOriginalActionFlags()
    {
        if (_unitAdditiveRead == null || _barXmlRoot == null || _currentUnitName == null) return [];
        var original = ProtoXmlHandler.GetUnitElement(_barXmlRoot, _currentUnitName);
        var current = _unitAdditiveRead.Effective.Element("unit");
        if (original == null || current == null) return [];
        var originalTactics = LoadOriginalUnitTactics(ProtoXmlHandler.GetSimpleField(original, "tactics") ?? "");
        var originalActions = ProtoXmlHandler.GetProtoActions(original);
        var labels = new List<string>();
        foreach (var action in ProtoXmlHandler.GetProtoActions(current))
        {
            var baseline = originalActions.FirstOrDefault(a => a.Name.Equals(action.Name, StringComparison.OrdinalIgnoreCase));
            if (baseline == null && !originalTactics.ContainsKey(action.Name)) continue; // A newly added action is not a disabled original.
            baseline ??= new ProtoAction { Name = action.Name };
            var before = CreateEffectiveProtoActionSnapshot(baseline, action.Name, baseline.Type, originalTactics);
            var after = CreateEffectiveProtoActionSnapshot(action, action.Name, action.Type, _currentUnitTacticsActions);
            foreach (var tag in ProtoActionMetadataCatalog.GetKnownFlagTags())
            {
                var value = ProtoXmlHandler.GetProtoActionSimpleFieldValue(after, tag).Trim();
                if (value != "0") continue;
                var old = ProtoXmlHandler.GetProtoActionSimpleFieldValue(before, tag);
                var wasEnabled = IsProtoActionFlagEnabledValue(old) ||
                    (GetProtoActionAdditionalElement(before, tag) != null && old.Trim() != "0") ||
                    (string.IsNullOrWhiteSpace(old) && (tag.Equals("active", StringComparison.OrdinalIgnoreCase) || IsInvertedDefaultProtoActionFlag(tag) ||
                        (IsMaintainActionType(before.Type) && tag is "pausable" or "showqueuewhilewaiting")));
                if (wasEnabled) labels.Add($"Actions → {action.Name} → {ProtoActionMetadataCatalog.GetKnownFlagLabel(tag)}");
            }
        }
        return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string UnitChangeLabel(AdditiveChange change, bool replacement)
    {
        static string Words(string text)
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=[a-z0-9])(?=[A-Z])", " ");
            return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
        }
        if (change.Path == "/unit") return "Entire unit";
        var node = replacement ? change.Patch : change.Before!;
        var tag = node.Name.LocalName;
        string ParentLabel(string parent) => parent.ToLowerInvariant() switch
        {
            "protoaction" => string.IsNullOrWhiteSpace(change.ActionName) ? "Actions" : "Actions → " + change.ActionName,
            "onhiteffect" => "On hit effect", "rechargeincludetypes" => "Recharge include types", "rechargeexcludetypes" => "Recharge exclude types",
            "auxrechargeincludetypes" => "Auxiliary recharge include types", "auxrechargeexcludetypes" => "Auxiliary recharge exclude types",
            _ => ProtoConstants.SimpleFields.FirstOrDefault(field => field.Tag.Equals(parent, StringComparison.OrdinalIgnoreCase))?.Label ?? Words(parent)
        };
        string WithParents(string leaf) => AdditiveChangePresentation.WithParentPath(change.Path, leaf, ParentLabel);
        if (tag == "flag") return WithParents("Flags → " + Words(node.Value));
        if (tag == "unittype") return WithParents("Unit types → " + Words(node.Value));
        if (tag == "protoaction") return WithParents("Actions → " + node.Element("name")?.Value);
        if (tag is "train" or "tech") return WithParents("Train / Research → " + node.Value);
        if (tag is "command" or "optionalcommand" or "transformcommand") return WithParents("Commands → " + node.Value);
        var selector = (string?)node.Attribute("type") ?? (string?)node.Attribute("resourcetype");
        var label = change.ActionName == null
            ? ProtoConstants.SimpleFields.FirstOrDefault(field => field.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))?.Label ?? Words(tag)
            : ProtoActionMetadataCatalog.GetFieldDefinition(tag).Label;
        var leaf = label + (selector == null ? "" : " (" + Words(selector) + ")") +
            (replacement || node.HasElements || string.IsNullOrWhiteSpace(node.Value) ? "" : ": " + node.Value);
        return change.Path.Count(character => character == '/') == 2 ? "Stats → " + leaf : WithParents(leaf);
    }

    private bool KeepAdditiveActionFieldVisible(ProtoAction action, string tag)
        => UsesUnitAdditiveReader && _unitAdditiveRead != null &&
           !string.IsNullOrWhiteSpace(GetProtoActionSimpleSourceValue(action, tag));

    private void DecorateAdditiveActionFields(ProtoActionWidgetState state)
    {
        if (!UsesUnitAdditiveReader || _unitAdditiveRead == null) return;
        foreach (var change in _unitAdditiveRead.Changes.Where(c => c.Path.Count(x => x == '/') == 3 &&
                     c.Result?.Parent?.Element("name")?.Value == state.Model.Name))
        {
            var tag = change.Patch.Name.LocalName;
            if (tag == "rof") AdditiveChangePresentation.Decorate(state.RofTb, change);
            else if (tag == "maxrange") AdditiveChangePresentation.Decorate(state.MaxRangeTb, change);
            else if (state.AdditionalFieldControls.TryGetValue(tag, out var field)) AdditiveChangePresentation.Decorate(field, change);
            foreach (var (control, mirroredTag) in state.MirroredSimpleSourceControls)
                if (mirroredTag.Equals(tag, StringComparison.OrdinalIgnoreCase)) AdditiveChangePresentation.Decorate(control, change);
            if (state.StructuredFieldRows.TryGetValue(tag, out var rows) && change.Result is { } result)
            {
                foreach (var row in rows.Where(row => result.Attributes().All(attribute =>
                    row.AttributeEditors.TryGetValue(attribute.Name.LocalName, out var editor)
                        ? string.Equals(ReadTextLikeControlValue(editor).Trim(), attribute.Value, StringComparison.OrdinalIgnoreCase)
                        : row.PreservedAttributes.TryGetValue(attribute.Name.LocalName, out var stored) && stored == attribute.Value)))
                    AdditiveChangePresentation.Decorate(row.ValueTb, change);
            }
            foreach (var (control, tags) in state.CustomFlagSourceControls)
                if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) AdditiveChangePresentation.Decorate(control, change);
        }
    }

    private void DecorateAdditiveChip(Border chip, string text, string tag)
    {
        if (!UsesUnitAdditiveReader || _unitAdditiveRead == null) return;
        var change = _unitAdditiveRead.Changes.LastOrDefault(c => c.Path == "/unit/" + tag && !c.Patch.HasElements && c.Mode != "remove" && c.Patch.Value == text);
        if (change == null) return;
        AdditiveChangePresentation.Decorate(chip, change);
    }

    private void DecorateAdditiveActionChip(Border chip, ProtoActionWidgetState state, string tag)
    {
        if (!UsesUnitAdditiveReader || _unitAdditiveRead == null) return;
        var change = _unitAdditiveRead.Changes.LastOrDefault(c => c.Mode != "remove" &&
            c.Patch.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) &&
            c.Result?.Parent?.Element("name")?.Value == state.Model.Name);
        if (change != null) AdditiveChangePresentation.Decorate(chip, change);
    }
}
