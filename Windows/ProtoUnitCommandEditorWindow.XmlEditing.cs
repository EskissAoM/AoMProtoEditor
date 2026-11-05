using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Windows;

internal sealed partial class ProtoUnitCommandEditorWindow
{
    private readonly StackPanel _structuredForm;
    private readonly Button _editXmlButton;
    private readonly XmlEditController _xmlController;
    private XmlEditSession? _xmlSession;
    private bool _xmlSaving;
    private bool _xmlClosed;
    private readonly HashSet<string> _rawStringReferenceTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _commandStringMigrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _commandNameMigrations = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> CurrentCommandNames => _context.CommandNames.Where(name => !_commandNameMigrations.ContainsKey(name)).Append(_savedName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private IReadOnlyDictionary<string, string> _xmlStartStringIds = new Dictionary<string, string>();

    private Task<bool> BeginCommandXmlEditingAsync()
    {
        RefreshPreview();
        _xmlStartStringIds = ProtoUnitCommandDefinition.FromElement(XElement.Parse(_xmlSession!.Text)).Values;
        return Task.FromResult(true);
    }

    private void BuildStructuredForm()
    {
        _structuredForm.Children.Clear();
        _valueEditors.Clear();
        _sharedTransformEditor = null;
        _sharedFlagsEditor = null;
        _transformFromEditor = _transformToEditor = _transformTechEditor = _transformRevertOthersEditor = null;
        _transformFullHealEditor = null;
        _commandMode = DetectCommandMode();
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("115,240,Auto,240,*"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        nameRow.Children.Add(new TextBlock { Text = "Command Name", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        _nameBox = new TextBox { Text = _definition.Name, IsEnabled = !_readOnly, Width = 240, MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        ProtoUnitCommandFieldFactory.ConfigureField(_nameBox, EditorFieldWidthRole.SingleLineText, () => _refreshingPreview);
        ApplyAbilityEditorFieldAppearance(_nameBox);
        ApplyReadOnlyAppearance(_nameBox);
        _nameBox.TextChanged += (_, _) => RefreshPreview();
        Grid.SetColumn(_nameBox, 1);
        nameRow.Children.Add(_nameBox);
        var typeLabel = new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(typeLabel, 2);
        nameRow.Children.Add(typeLabel);
        _commandModeBox = new ComboBox
        {
            ItemsSource = new[] { "Free", "Transform (Unique)", "Transform (Multiple)", "Spawn" },
            SelectedItem = GetCommandModeDisplayName(_commandMode), Width = 240, MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_readOnly && !_identityLockedByUsage
        };
        ApplyReadOnlyAppearance(_commandModeBox);
        ProtoUnitCommandFieldFactory.ConfigureField(_commandModeBox, EditorFieldWidthRole.LongEnum, () => _refreshingPreview);
        _commandModeBox.SelectionChanged += (_, _) => OnCommandModeChanged();
        Grid.SetColumn(_commandModeBox, 3);
        nameRow.Children.Add(_commandModeBox);
        WrapCommandGrid(nameRow, [0, 1], [2, 3]);
        _structuredForm.Children.Add(nameRow);
        BuildDisplaySection(_structuredForm);
        BuildCommandSection(_structuredForm);
        if (!_readOnly || _definition.RepeatableValues.Values.Any(values => values.Any(value => !string.IsNullOrWhiteSpace(value))))
        {
            AddHeader(_structuredForm, "Shared / Prequeue Commands");
            AddRepeatableRow(_structuredForm, "sharedcommand", "Shared Command");
            AddRepeatableRow(_structuredForm, "removecommandprequeueonprequeue", "Remove prequeued");
        }
        if (!_readOnly || _flags.Count > 0) AddFlagsEditor(_structuredForm);
    }

    private static string CommandXmlPreview(XElement element)
        => XmlSyntaxEditorService.PreparePreviewText(element.ToString());

    private ProtoUnitCommandDefinition ParseCommandXml(string text)
    {
        var element = XElement.Parse(text, LoadOptions.PreserveWhitespace);
        if (element.Name != "protounitcommand") throw new InvalidDataException("Command XML must contain one <protounitcommand> root without a namespace.");
        var validation = ModXmlDocumentSafety.Validate(new XDocument(new XElement("protounitcommandmods", new XElement(element))), ModXmlDocumentKind.ProtoUnitCommands);
        if (validation.Errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors.Select(error => error.Reason)));
        var known = ProtoUnitCommandDefinition.ValueFieldTags.Concat(ProtoUnitCommandDefinition.RepeatableFieldTags)
            .Concat(ProtoUnitCommandDefinition.FlagTags).Append("name").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var child in element.Elements().Where(child => known.Contains(child.Name.LocalName)))
            if (child.HasElements || child.Name.Namespace != XNamespace.None)
                throw new InvalidDataException($"Command field '{child.Name}' must be a plain text element without a namespace.");
        foreach (var field in element.Elements().Where(child => StringBackedTags.Contains(child.Name.LocalName)))
        {
            var id = field.Value.Trim();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (seen.Add(id) && _commandStringMigrations.TryGetValue(id, out var next)) id = next;
            if (id != field.Value.Trim()) XmlTriviaPreserver.SetText(field, id);
        }
        var referenceDocument = new XDocument(new XElement("protounitcommandmods", new XElement(element)));
        foreach (var rename in _commandNameMigrations)
            GameIdentifierReferencePolicy.RenameReferences(referenceDocument, GameIdentifierKind.ProtoUnitCommand, rename.Key, rename.Value);
        var candidate = ProtoUnitCommandDefinition.FromElement(referenceDocument.Root!.Element("protounitcommand")!);
        candidate.PreserveSourceStructure = true;
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(candidate.Name, _savedName) ||
            !candidate.Name.Equals(_savedName, StringComparison.OrdinalIgnoreCase) && !ProtoUnitCommandNamePolicy.IsAvailable(candidate.Name, CurrentCommandNames, _savedName))
            throw new InvalidDataException("Choose a valid, unused internal command name.");
        var transformFlags = candidate.Flags.Count(ProtoUnitCommandTransformRules.IsTransformFamilyFlag);
        if (transformFlags > 1 || transformFlags > 0 && candidate.Flags.Contains("spawncommand"))
            throw new InvalidDataException("A command cannot combine different Transform or Spawn types.");
        foreach (var tag in new[] { "costprotounit", "actionforactiveicon" }.Concat(ProtoUnitCommandDefinition.RepeatableFieldTags))
        {
            var values = candidate.RepeatableValues.TryGetValue(tag, out var repeated) ? repeated : [candidate.Values.GetValueOrDefault(tag, "")];
            var originals = _definition.RepeatableValues.TryGetValue(tag, out var old) ? old : [_definition.Values.GetValueOrDefault(tag, "")];
            var options = tag == "costprotounit" ? _context.ProtoUnitNames : tag == "actionforactiveicon" ? _context.ActionNames : CurrentCommandNames;
            foreach (var value in values)
            {
                var result = StrictReferencePolicy.Validate(value, originals.Contains(value, StringComparer.Ordinal) ? value : "", options, true, Labels.GetValueOrDefault(tag, tag));
                if (!result.IsValid) throw new InvalidDataException(result.ErrorMessage);
            }
        }
        return candidate;
    }

    private IReadOnlyDictionary<string, string> CaptureRawCommandStringTexts(ProtoUnitCommandDefinition candidate)
        => CaptureStringTexts().Where(pair => string.Equals(candidate.Values.GetValueOrDefault(pair.Key, ""),
                _xmlStartStringIds.GetValueOrDefault(pair.Key, ""), StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private void RememberCommandStringMigrations(IReadOnlyDictionary<string, string> submitted, ProtoUnitCommandDefinition saved)
    {
        foreach (var tag in StringBackedTags)
        {
            var old = submitted.GetValueOrDefault(tag, "");
            var next = saved.Values.GetValueOrDefault(tag, "");
            if (old.Length > 0 && next.Length > 0 && old != next) _commandStringMigrations[old] = next;
        }
    }

    private void AdoptCommandXml(ProtoUnitCommandDefinition saved, IReadOnlyDictionary<string, string> submittedStrings)
    {
        _refreshingPreview = true;
        try
        {
            _rawStringReferenceTags.Clear();
            foreach (var tag in StringBackedTags)
            {
                var id = saved.Values.GetValueOrDefault(tag, "");
                _stringTexts[tag] = submittedStrings.GetValueOrDefault(tag, id);
                if (id.Length > 0 && !submittedStrings.ContainsKey(tag)) _rawStringReferenceTags.Add(tag);
            }
            _flags.Clear();
            _flags.UnionWith(saved.Flags);
            _repeatables.Clear();
            foreach (var pair in saved.RepeatableValues) _repeatables[pair.Key] = pair.Value.ToList();
            BuildStructuredForm();
        }
        finally { _refreshingPreview = false; }
    }

    internal void DiscardAndClose()
    {
        _allowClose = true;
        _xmlSession?.Reset("");
        Close();
    }
}
