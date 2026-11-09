using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView
{
    private readonly bool _baseCatalogAvailable;
    private AdditiveReadResult? _majorGodAdditiveRead;
    private bool _majorGodHasSingleEffectiveEntry = true;
    private string? _majorGodDocumentSafetyError;
    private readonly AdditiveChangeListState _majorGodChangeLists = new();
    public bool CanModifyMajorGodDocument => _baseCatalogAvailable && _majorGodDocumentSafetyError == null;
    internal bool CanEditMajorGod(string name) => CanModifyMajorGodDocument && GetMajorGodEntryKind(name) == AdditiveEntryKind.Custom;
    private bool IsEditableMajorGod => IsModifiedMode && CanModifyMajorGodDocument &&
        (CurrentMajorGodName == null || GetMajorGodEntryKind(CurrentMajorGodName) == AdditiveEntryKind.Custom);
    private bool IsCurrentAdditiveMajorGod => IsModifiedMode && CurrentMajorGodName != null && GetMajorGodEntryKind(CurrentMajorGodName) != AdditiveEntryKind.Custom;
    internal void ResetAdditiveChangeCards(string name, bool modified) => _majorGodChangeLists.ResetEntry(name, modified);
    internal AdditiveEntryKind GetMajorGodEntryKind(string name)
    {
        var kind = AdditiveXmlReader.Classify(_baseCatalogAvailable ? _baseEntries.ContainsKey(name) : null, true);
        if (kind != AdditiveEntryKind.Custom) return kind;
        var patch = _modDocument == null ? null : FindMajorGodPatch(_modDocument, name);
        return patch != null && AdditiveIdentityRules.Attribute(patch, "mergeMode")?.ToLowerInvariant() is "remove" or "replace"
            ? AdditiveEntryKind.Unresolved : kind;
    }

    private static XElement? FindMajorGodPatch(XDocument document, string name) => document.Root?.Elements()
        .FirstOrDefault(entry => entry.Name.LocalName.Equals(MajorGodCatalog.EntryName, StringComparison.OrdinalIgnoreCase) &&
            MajorGodCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase));
    private XElement MajorGodPreviewEntry(XElement entry) => IsCurrentAdditiveMajorGod && _modDocument != null && CurrentMajorGodName != null
        ? FindMajorGodPatch(_modDocument, CurrentMajorGodName) ?? entry : entry;
    private HashSet<string> ReadOnlyMajorGodNames(XDocument document) => (document.Root?.Elements(MajorGodCatalog.EntryName) ?? [])
        .Select(MajorGodCatalog.GetName).Where(name => GetMajorGodEntryKind(name) != AdditiveEntryKind.Custom).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private PreparedModXmlDocument LoadMajorGodSource()
    {
        _majorGodDocumentSafetyError = null;
        if (!File.Exists(_modPath)) return ModDocumentPersistence.Prepare(_modPath!, ModXmlDocumentKind.MajorGods);
        var loaded = ModXmlDocumentSafety.LoadExisting(_modPath!, ModXmlDocumentKind.MajorGods, allowUnsafeStructureForReadOnly: true);
        var names = (loaded.Document.Root?.Elements(MajorGodCatalog.EntryName) ?? []).Select(MajorGodCatalog.GetName)
            .Where(name => !_baseCatalogAvailable || _baseEntries.ContainsKey(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validation = ModXmlDocumentSafety.Validate(loaded.Document, ModXmlDocumentKind.MajorGods, names);
        _majorGodDocumentSafetyError = validation.IsSafeToEdit ? null : validation.ToDisplayMessage(_modPath!);
        return new(Path.GetFullPath(_modPath!), ModXmlDocumentKind.MajorGods, loaded.Document, loaded.FileState, names);
    }

    private XElement? ResolveMajorGodForReading(string name)
    {
        _majorGodChangeLists.SelectEntry(name, IsModifiedMode);
        if (!IsModifiedMode) return _baseEntries.GetValueOrDefault(name);
        var patch = _modDocument == null ? null : FindMajorGodPatch(_modDocument, name);
        if (patch == null || GetMajorGodEntryKind(name) != AdditiveEntryKind.Modified) return patch;
        var fragments = new XElement(MajorGodCatalog.ModRootName, _modDocument!.Root!.Elements(MajorGodCatalog.EntryName)
            .Where(entry => MajorGodCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)).Select(entry => new XElement(entry)));
        _majorGodAdditiveRead = AdditiveXmlReader.Evaluate(new XElement("civs", new XElement(_baseEntries[name])), fragments, AdditiveFamily.MajorGods);
        var entries = _majorGodAdditiveRead.Effective.Elements(MajorGodCatalog.EntryName).ToArray();
        _majorGodHasSingleEffectiveEntry = entries.Length == 1 && entries[0].Elements().Count(element => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)) == 1 &&
            MajorGodCatalog.GetName(entries[0]).Equals(name, StringComparison.OrdinalIgnoreCase);
        return _majorGodHasSingleEffectiveEntry ? entries[0] : new XElement(MajorGodCatalog.EntryName, new XElement("name", name));
    }

    private StackPanel[] MajorGodNoticePanels => [_majorGodPropertyNotices, _majorGodStartingNotices, _majorGodSpecialNotices];
    private void ClearMajorGodAdditivePresentation()
    {
        AdditiveChangePresentation.Clear(_majorGodStructuredForm);
        _majorGodAdditiveRead = null; _majorGodHasSingleEffectiveEntry = true;
        _majorGodAdditiveSummary.IsVisible = _majorGodAdditiveDiagnostics.IsVisible = false;
        foreach (var panel in MajorGodNoticePanels) { panel.Children.Clear(); panel.IsVisible = false; }
    }

    private static int MajorGodChangeTab(string path)
    {
        var tag = path.Split('/').Skip(2).FirstOrDefault() ?? "";
        if (tag.Equals("startingunits", StringComparison.OrdinalIgnoreCase) || tag.Equals("startingqueue", StringComparison.OrdinalIgnoreCase) || tag.Equals("startingresources", StringComparison.OrdinalIgnoreCase)) return 1;
        return SpecialMechanics.Any(mechanic => mechanic.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) || tag is "partisans" or "devotioncommands" ? 2 : 0;
    }

    private static string MajorGodFieldLabel(string tag) => tag.ToLowerInvariant() switch
        {
            "buildingrepairfactor" => "Building repair factor", "buildingefficiency" => "Building efficiency",
            "secondaryprayerefficiency" => "Secondary prayer efficiency", "startingunits" => "Starting units",
            "startingqueue" => "Starting queue", "startingresources" => "Starting resources",
            "agetech" => "Age technology", "teamtech" => "Team technology", "deathmatchtech" => "Deathmatch technology",
            "targetunit" => "Target unit", "protounit" => "Proto unit", "bonusunitspawning" => "Bonus unit spawning",
            "bountyresourceearning" => "Bounty resource earning", "terraincreeps" => "Terrain creeps", "terraincreep" => "Terrain creep",
            "damagegoal" => "Damage goal", "deathcountgoal" => "Death count goal", "resourcegoal" => "Resource goal",
            "buildingchain" => "Building chain", "timeshifting" => "Time shifting", "combatxp" => "Combat XP",
            _ => TechnologyEditorView.HumanizeLabel(tag)
        };

    private static string MajorGodChangeLabel(AdditiveChange change, bool replacement)
    {
        if (change.Path == "/civ") return "Entire Major God";
        var node = replacement ? change.Patch : change.Before!;
        var label = AdditiveChangePresentation.WithParentPath(change.Path, MajorGodFieldLabel(node.Name.LocalName), MajorGodFieldLabel);
        // Coordinates, counts and delays describe the removed row; they do not
        // identify its parent or help the user locate the change.
        var selectors = string.Join(", ", node.Attributes().Where(attribute => attribute.Name.LocalName.ToLowerInvariant() is
            "mode" or "age" or "type" or "unit" or "creep" or "resourcetype" or "name").Select(attribute => attribute.Value));
        return label + (selectors.Length == 0 ? "" : " (" + selectors + ")") +
            ((replacement && !node.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase)) || node.HasElements || string.IsNullOrWhiteSpace(node.Value) ? "" : ": " + node.Value);
    }

    private void PresentMajorGodAdditiveReading()
    {
        foreach (var panel in MajorGodNoticePanels) { panel.Children.Clear(); panel.IsVisible = false; }
        _majorGodAdditiveSummary.IsVisible = IsCurrentAdditiveMajorGod;
        _majorGodAdditiveDiagnostics.IsVisible = false;
        if (!IsModifiedMode) return;
        if (_majorGodDocumentSafetyError != null)
        { _majorGodAdditiveDiagnostics.Text = _majorGodDocumentSafetyError; _majorGodAdditiveDiagnostics.IsVisible = true; }
        if (!IsCurrentAdditiveMajorGod) return;
        _majorGodAdditiveSummary.Text = _majorGodAdditiveRead == null ? "Original unresolved · Read-only" : "Modified original · Read-only";
        if (_majorGodAdditiveRead is not { } read)
        {
            _majorGodAdditiveDiagnostics.Text = "The original Major God cannot be resolved. Source XML is preserved.";
            _majorGodAdditiveDiagnostics.IsVisible = true; return;
        }
        _majorGodTabs.IsVisible = _majorGodHasSingleEffectiveEntry;
        if (!_majorGodHasSingleEffectiveEntry)
        {
            _majorGodAdditiveDiagnostics.Text = read.Effective.Elements(MajorGodCatalog.EntryName).Count() switch
            {
                0 => "This Major God is removed by the mod.",
                1 => "The result does not retain exactly one matching internal name. Review the source XML.",
                _ => "The patch produces multiple Major Gods with this identity. Review the source XML."
            };
            _majorGodAdditiveDiagnostics.IsVisible = true;
        }
        var panels = MajorGodNoticePanels;
        for (var index = 0; index < panels.Length; index++)
        {
            foreach (var kind in new[] { AdditiveChangeListKind.Replaced, AdditiveChangeListKind.Removed })
            {
                var lines = read.Changes.Where(change => change.Before != null && change.Mode ==
                    (kind == AdditiveChangeListKind.Replaced ? "replace" : "remove") && (change.Path == "/civ" || MajorGodChangeTab(change.Path) == index))
                    .Select(change => MajorGodChangeLabel(change, kind == AdditiveChangeListKind.Replaced)).ToArray();
                if (lines.Length > 0) panels[index].Children.Add(_majorGodChangeLists.CreateCard(index.ToString(), kind, lines));
            }
        }
        foreach (var diagnostic in read.Diagnostics)
        {
            var split = diagnostic.IndexOf(':'); var path = split < 0 ? "" : diagnostic[..split];
            var detail = split < 0 ? diagnostic : diagnostic[(split + 1)..].Trim();
            var title = "Mod change needs review";
            if (detail.Contains("identity matches; first sibling selected", StringComparison.Ordinal))
            { title = "Several entries match"; detail = "The game applies this change to the first matching entry."; }
            else if (detail.EndsWith("found no match.", StringComparison.Ordinal))
            { title = "No matching entry"; detail = "The operation has no effect because its original entry could not be found."; }
            var notice = AdditiveChangePresentation.CreateNotice(title, detail); ToolTip.SetTip(notice, diagnostic);
            panels[MajorGodChangeTab(path)].Children.Add(notice);
        }
        foreach (var panel in panels) panel.IsVisible = panel.Children.Count > 0;
        DecorateMajorGodAdditiveFields();
    }

    private void DecorateMajorGodAdditiveFields()
    {
        if (_majorGodAdditiveRead == null || _currentEntry == null) return;
        BindMajorGodValidationFields(_currentEntry);
        foreach (var (control, tag) in new (Control, string)[]
        {
            (_keyBox, "key"), (_cultureBox, "culture"), (_displayNameBox, "displaynameid"), (_titleBox, "titleid"), (_rolloverNameBox, "rollovernameid"),
            (_iconEditor.CompactPresenter, "icon"), (_portraitEditor.CompactPresenter, "portrait"), (_breakoutPortraitEditor.CompactPresenter, "breakoutportrait")
        }) if (MajorGodCatalog.FindDirectChild(_currentEntry, tag) is { } source) AdditiveChangePresentation.Bind(control, source);
        AdditiveChangePresentation.DecorateBoundFields(_majorGodStructuredForm, _majorGodAdditiveRead);
    }

    private static Border BindMajorGodChip(Border chip, XObject? source) => source == null ? chip : AdditiveChangePresentation.BindXml(chip, source);

    private void RequireUnchangedMajorGodPatches(XDocument document)
    {
        foreach (var name in (_loadedModDocument?.Root?.Elements(MajorGodCatalog.EntryName) ?? []).Select(MajorGodCatalog.GetName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GetMajorGodEntryKind(name) == AdditiveEntryKind.Custom) continue;
            var before = _loadedModDocument!.Root!.Elements(MajorGodCatalog.EntryName).Where(entry => MajorGodCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var after = document.Root?.Elements(MajorGodCatalog.EntryName).Where(entry => MajorGodCatalog.GetName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (before.Length != after.Length || before.Where((entry, index) => !XNode.DeepEquals(entry, after[index])).Any())
                throw new InvalidDataException($"Major God '{name}' is read-only in this checkpoint. Its authored patch must remain unchanged.");
        }
    }
}
