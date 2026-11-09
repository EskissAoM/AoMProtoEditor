using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView
{
    private bool _hasOriginalTechnologyCatalog;
    private AdditiveReadResult? _technologyAdditiveRead;
    private bool _technologyHasSingleEffectiveEntry = true;
    private readonly AdditiveChangeListState _technologyChangeLists = new();
    internal void ResetAdditiveChangeCards(string name, bool modified) => _technologyChangeLists.ResetEntry(name, modified);

    // An explicit empty catalog is authoritative; null means originals are unavailable.
    internal AdditiveEntryKind GetTechnologyEntryKind(string name)
    {
        var kind = AdditiveXmlReader.Classify(_hasOriginalTechnologyCatalog ? _original.ContainsKey(name) : null, inMod: true);
        if (kind != AdditiveEntryKind.Custom) return kind;
        var authored = FindTechnologyXmlElement(_modDocument, name);
        return AdditiveIdentityRules.Attribute(authored ?? new XElement("tech"), "mergeMode")?.ToLowerInvariant() is "remove" or "replace"
            ? AdditiveEntryKind.Unresolved : AdditiveEntryKind.Custom;
    }

    internal bool CanEditTechnology(string name)
        => CanModifyTechnologyDocument && GetTechnologyEntryKind(name) == AdditiveEntryKind.Custom;

    private bool IsCurrentAdditiveTechnology => IsModifiedTab && _currentOriginalName != null &&
        GetTechnologyEntryKind(_currentOriginalName) != AdditiveEntryKind.Custom;

    private XElement? ResolveTechnologyForReading(string name)
    {
        _technologyChangeLists.SelectEntry(name, IsModifiedTab);
        _technologyAdditiveRead = null;
        _technologyHasSingleEffectiveEntry = true;
        if (!IsModifiedTab) return _original.GetValueOrDefault(name);
        if (!_modified.TryGetValue(name, out var authored)) return null;
        if (GetTechnologyEntryKind(name) != AdditiveEntryKind.Modified) return authored;
        var fragments = new XElement("techtreemods", _modDocument.Root!.Elements().Where(element =>
            element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetCaseInsensitiveAttribute(element, "name")?.Value, name, StringComparison.OrdinalIgnoreCase)).Select(element => new XElement(element)));
        _technologyAdditiveRead = AdditiveXmlReader.Evaluate(
            new XElement("techtree", new XElement(_original[name])), fragments, AdditiveFamily.TechTree);
        var entries = _technologyAdditiveRead.Effective.Elements("tech").ToArray();
        _technologyHasSingleEffectiveEntry = entries.Length == 1;
        // Keep the identity/patch available for removed or duplicate results; hide the ordinary form.
        return entries.Length == 1 ? entries[0] : new XElement("tech", new XAttribute("name", name));
    }

    private void ClearTechnologyAdditivePresentation()
    {
        _technologyAdditiveRead = null;
        _technologyHasSingleEffectiveEntry = true;
        _technologyAdditiveSummary.IsVisible = false;
        _technologyAdditiveDiagnostics.IsVisible = false;
        ClearTechnologySectionNotices();
        _technologyEditorTabs.IsVisible = true;
    }

    private void ClearTechnologySectionNotices()
    {
        foreach (var panel in new[] { _technologyPropertyNotices, _technologyPrerequisiteNotices, _technologyEffectNotices })
        { panel.Children.Clear(); panel.IsVisible = false; }
    }

    private StackPanel TechnologyNoticeSection(string path)
        => path.StartsWith("/tech/effects", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/tech/effect", StringComparison.OrdinalIgnoreCase)
            ? _technologyEffectNotices : path.StartsWith("/tech/prereqs", StringComparison.OrdinalIgnoreCase)
            ? _technologyPrerequisiteNotices : _technologyPropertyNotices;

    private void AddTechnologyChangeCards(AdditiveReadResult read)
    {
        foreach (var panel in new[] { _technologyPropertyNotices, _technologyPrerequisiteNotices, _technologyEffectNotices })
        {
            foreach (var kind in new[] { AdditiveChangeListKind.Replaced, AdditiveChangeListKind.Removed })
            {
                var lines = read.Changes.Where(change => change.Before != null && change.Mode ==
                    (kind == AdditiveChangeListKind.Replaced ? "replace" : "remove") &&
                    (change.Path == "/tech" || ReferenceEquals(TechnologyNoticeSection(change.Path), panel)))
                    .Select(change => TechnologyChangeLabel(change, kind == AdditiveChangeListKind.Replaced)).ToArray();
                if (lines.Length == 0) continue;
                var section = ReferenceEquals(panel, _technologyEffectNotices) ? "effects" :
                    ReferenceEquals(panel, _technologyPrerequisiteNotices) ? "prerequisites" : "properties";
                panel.Children.Add(_technologyChangeLists.CreateCard(section, kind, lines));
            }
        }
    }

    private void AddTechnologyMergeNotice(string diagnostic)
    {
        var separator = diagnostic.IndexOf(':');
        var path = separator < 0 ? "" : diagnostic[..separator];
        var detail = separator < 0 ? diagnostic : diagnostic[(separator + 1)..].Trim();
        var panel = TechnologyNoticeSection(path);
        var noun = ReferenceEquals(panel, _technologyEffectNotices) ? "effect" :
            ReferenceEquals(panel, _technologyPrerequisiteNotices) ? "prerequisite" :
            HumanizeLabel(path.Split('/').LastOrDefault() ?? "field").ToLowerInvariant();
        string title;
        if (detail.Contains("identity matches; first sibling selected", StringComparison.Ordinal))
        {
            title = ReferenceEquals(panel, _technologyPropertyNotices) ? "Multiple matches for " + noun : "Several " + noun + "s match";
            detail = "The game applies this change to the first matching " + noun + ".";
        }
        else if (detail.EndsWith("found no match.", StringComparison.Ordinal))
        {
            title = "No matching " + noun;
            detail = "The " + detail.Split(' ')[0] + " operation has no effect because its original " + noun + " could not be found.";
        }
        else if (detail.Contains("tag-only fallback is a prediction", StringComparison.Ordinal))
        {
            title = "Unverified matching rule";
            detail = "The matching rule for this change is not confirmed. Its displayed result is a prediction.";
        }
        else title = "Mod change needs review";
        var notice = AdditiveChangePresentation.CreateNotice(title, detail);
        ToolTip.SetTip(notice, diagnostic);
        panel.Children.Add(notice);
    }

    private void PresentTechnologyAdditiveReading()
    {
        _technologyAdditiveSummary.IsVisible = IsCurrentAdditiveTechnology;
        _technologyAdditiveDiagnostics.IsVisible = false;
        ClearTechnologySectionNotices();
        _technologyEditorTabs.IsVisible = _technologyHasSingleEffectiveEntry;
        if (!IsCurrentAdditiveTechnology) return;
        if (_technologyAdditiveRead == null)
        {
            _technologyAdditiveSummary.Text = "Original unresolved · Read-only";
            _technologyAdditiveDiagnostics.Text = "The original technology cannot be resolved. Source XML is preserved.";
            _technologyAdditiveDiagnostics.IsVisible = true;
            return;
        }

        var read = _technologyAdditiveRead;
        _technologyAdditiveSummary.Text = "Modified original · Read-only";
        AddTechnologyChangeCards(read);
        var diagnostics = new List<string>();
        foreach (var diagnostic in read.Diagnostics)
        {
            if (_technologyHasSingleEffectiveEntry) AddTechnologyMergeNotice(diagnostic);
            else diagnostics.Add(diagnostic);
        }
        if (!_technologyHasSingleEffectiveEntry)
            diagnostics.Add(read.Effective.Elements("tech").Any()
                ? "The patch produces multiple technologies with this identity. Review the source XML."
                : "This technology is removed by the mod.");
        _technologyAdditiveDiagnostics.Text = string.Join("\n", diagnostics);
        _technologyAdditiveDiagnostics.IsVisible = diagnostics.Count > 0;

        foreach (var root in new[] { _propertiesPanel, _prereqsPanel, _effectsPanel })
            AdditiveChangePresentation.DecorateBoundFields(root, read);

        foreach (var panel in new[] { _technologyPropertyNotices, _technologyPrerequisiteNotices, _technologyEffectNotices })
            panel.IsVisible = panel.Children.Count > 0;
    }

    private static string TechnologyParentLabel(string tag) => tag.ToLowerInvariant() switch
    {
        "prereqs" => "Prerequisites", "effects" => "Effects", "effect" => "Effect", "typecount" => "Type count",
        _ => HumanizeLabel(tag)
    };

    private static string TechnologyChangeLabel(AdditiveChange change, bool replacement)
    {
        if (change.Path == "/tech") return "Entire technology";
        if (replacement && change.Path == "/tech/effects") return "Entire effects list";
        if (replacement && change.Path == "/tech/prereqs") return "Entire prerequisites list";
        var node = replacement ? change.Patch : change.Before!;
        var label = DescribeTechnologyNode(node);
        if (node.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase) && change.Path.Count(character => character == '/') == 2)
            label = "Effects → " + label; // The existing direct-effect import route has no wrapper.
        return AdditiveChangePresentation.WithParentPath(change.Path, label, TechnologyParentLabel);
    }

    private static string DescribeTechnologyNode(XElement node)
    {
        if (node.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase))
            return "Flags → " + HumanizeLabel(node.Value);
        if (node.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase))
        {
            var type = GetCaseInsensitiveAttribute(node, "subtype")?.Value ?? GetCaseInsensitiveAttribute(node, "type")?.Value ?? "Effect";
            var target = string.Join(", ", node.Elements().Where(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).Select(e => e.Value));
            var amount = GetCaseInsensitiveAttribute(node, "amount")?.Value;
            return HumanizeLabel(type) + (target.Length > 0 ? " → " + target : "") +
                (amount == null ? "" : ": " + amount) +
                (node.HasElements || string.IsNullOrWhiteSpace(node.Value) ? "" : " → " + node.Value);
        }
        var selector = GetCaseInsensitiveAttribute(node, "resourcetype")?.Value ?? GetCaseInsensitiveAttribute(node, "unit")?.Value ??
            GetCaseInsensitiveAttribute(node, "name")?.Value;
        var value = node.HasElements ? null : !string.IsNullOrWhiteSpace(node.Value) ? node.Value : GetCaseInsensitiveAttribute(node, "count")?.Value;
        return TechnologyParentLabel(node.Name.LocalName) + (selector == null ? "" : " (" + selector + ")") +
            (value == null ? "" : ": " + value);
    }

    private TechnologyDocumentValidation ValidateTechnologySourceStructure(XDocument document)
    {
        var structure = new XDocument(document);
        // Patch selectors and ordered operations are not complete custom definitions.
        // Keep the existing root/identity ownership checks and all custom structure checks.
        foreach (var tech in structure.Root?.Elements().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)) ?? [])
            if (_original.ContainsKey(GetCaseInsensitiveAttribute(tech, "name")?.Value ?? "")) tech.RemoveNodes();
        return TechnologyDocumentSafety.ValidateModDocument(structure);
    }

    private TechnologySaveValidation ValidateTechnologySaveCandidate(XDocument candidate, TechnologySaveCatalogs catalogs)
    {
        var structure = ValidateTechnologySourceStructure(candidate);
        if (!structure.IsValid) return new TechnologySaveValidation(structure.Errors);
        // Read-only original patches must never be regenerated, normalized or renamed
        // by another record's save (including reference migration).
        foreach (var name in _savedModifiedTechnologyXml.Keys.Concat(_modified.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(name => GetTechnologyEntryKind(name) != AdditiveEntryKind.Custom))
        {
            var authored = FindTechnologyXmlElement(candidate, name);
            if (authored == null || !_savedModifiedTechnologyXml.TryGetValue(name, out var saved) ||
                SerializeTechnologyForComparison(authored) != saved)
                return new TechnologySaveValidation([$"Technology '{name}' is read-only in this checkpoint. Its authored patch must remain unchanged."]);
        }
        var custom = new XDocument(candidate);
        foreach (var tech in custom.Root!.Elements().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)).ToArray())
            if (GetTechnologyEntryKind(GetCaseInsensitiveAttribute(tech, "name")?.Value ?? "") != AdditiveEntryKind.Custom) tech.Remove();
        return TechnologySaveValidator.Validate(custom, catalogs);
    }
}
