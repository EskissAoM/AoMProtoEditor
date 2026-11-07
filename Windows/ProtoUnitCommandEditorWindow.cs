using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed class ProtoUnitCommandEditorContext
{
    public IReadOnlyList<string> CommandNames { get; init; } = [];
    public IReadOnlyList<string> TechNames { get; init; } = [];
    public IReadOnlyList<string> ProtoUnitNames { get; init; } = [];
    public IReadOnlyList<string> PowerNames { get; init; } = [];
    public IReadOnlyList<string> ActionNames { get; init; } = [];
    public IReadOnlyList<string> IconPaths { get; init; } = [];
    public IReadOnlyDictionary<string, string> StringTexts { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int ProtoUnitUsageCount { get; init; }
}

internal sealed partial class ProtoUnitCommandEditorWindow : SimpleWindow
{
    private ProtoUnitCommandDefinition _definition;
    private readonly bool _readOnly;
    private readonly Func<ProtoUnitCommandDefinition, IReadOnlyDictionary<string, string>, ProtoUnitTransformDefinition?, Task<bool>> _save;
    private readonly ProtoUnitCommandEditorContext _context;
    private readonly Dictionary<string, Control> _valueEditors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _repeatables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private TextBox _nameBox = null!;
    private readonly TextEditor _xmlPreview;
    private readonly TextBlock _statusText;
    private readonly EditorSaveFeedback _saveFeedback;
    private readonly Dictionary<string, string> _stringTexts = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshingPreview;
    private bool _allowClose;
    private bool _closePromptOpen;
    private ProtoUnitCommandFlagsEditor? _sharedFlagsEditor;
    private ProtoUnitCommandTransformEditor? _sharedTransformEditor;
    private XElement _savedElement;
    private Dictionary<string, string> _savedStringTexts;
    private ProtoUnitTransformDefinition? _transformDefinition;
    private XElement? _savedTransformElement;
    private AutoCompleteBox? _transformFromEditor;
    private AutoCompleteBox? _transformToEditor;
    private AutoCompleteBox? _transformTechEditor;
    private AutoCompleteBox? _transformRevertOthersEditor;
    private CheckBox? _transformFullHealEditor;
    private readonly bool _identityLockedByUsage;
    private CommandEditorMode _openedCommandMode;
    private string _openedTransformFrom = string.Empty;
    private static readonly HashSet<string> StringBackedTags = new(["displaynameid", "rollovertextid", "shortrollovertextid", "activerollovertextid", "disabledrollovertextid", "buildlimittextid"], StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string,string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["command"]="Command", ["secondarycommand"]="Secondary", ["icon"]="Icon", ["controllericon"]="Controller Icon",
        ["associatedtech"]="Associated Tech", ["trainableunitreq"]="Trainable Unit", ["prereqcommand"]="Command",
        ["displaynameid"]="Display Name", ["rollovertextid"]="Rollover", ["shortrollovertextid"]="Short Rollover",
        ["activerollovertextid"]="Active Rollover", ["disabledrollovertextid"]="Disabled Rollover",
        ["buildlimittextid"]="Build Limit Text", ["valuetext"]="Value Text", ["associatedpower"]="Power",
        ["forbidtech"]="Forbid Tech", ["protounit"]="Proto Unit", ["amount"]="Amount", ["costprotounit"]="Cost Proto Unit",
        ["age"]="Age", ["actionforactiveicon"]="Action For Active Icon", ["activeicon"]="Active Icon (deprecated)",
        ["disabledicon"]="Disabled Icon (deprecated)"
    };

    private string _savedName;
    public string ResultName => _savedName;

    public ProtoUnitCommandEditorWindow(ProtoUnitCommandDefinition definition, bool readOnly, ProtoUnitCommandEditorContext context, ProtoUnitTransformDefinition? transformDefinition, Func<ProtoUnitCommandDefinition, IReadOnlyDictionary<string, string>, ProtoUnitTransformDefinition?, Task<bool>> save)
    {
        _definition = definition;
        _readOnly = readOnly;
        _context = context;
        _save = save;
        _identityLockedByUsage = !readOnly && context.ProtoUnitUsageCount > 0;
        _transformDefinition = transformDefinition?.Clone();
        _savedTransformElement = transformDefinition?.ToElement();
        _savedName = definition.Name;
        _savedElement = definition.ToElement();
        _savedStringTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in definition.RepeatableValues)
            _repeatables[pair.Key] = pair.Value.ToList();
        foreach (var flag in definition.Flags)
            _flags.Add(flag);
        foreach (var pair in context.StringTexts)
        {
            _stringTexts[pair.Key] = pair.Value;
            _savedStringTexts[pair.Key] = pair.Value;
        }

        Title = readOnly ? $"View ProtoUnit Command - {definition.Name}" : $"Edit ProtoUnit Command - {definition.Name}";
        Width = 1120;
        Height = 780;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (!_readOnly && e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                await SaveAsync();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        var shell = new DockPanel();

        var toolbarBorder = new Border
        {
            Background = Brush.Parse("#0E1110"),
            BorderBrush = Brush.Parse("#6A5434"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 45
        };
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        var toolbar = new DockPanel { Margin = new Thickness(10, 0) };
        toolbar.Children.Add(new TextBlock
        {
            Text = readOnly ? $"View {definition.Name}" : $"Edit {definition.Name}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var toolbarButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(toolbarButtons, Dock.Right);
        var closeButton = new Button { Content = readOnly ? "Close" : "Cancel", MinWidth = 90 };
        closeButton.Click += (_, _) => Close();
        toolbarButtons.Children.Add(closeButton);
        if (!readOnly)
        {
            var saveButton = new Button
            {
                Content = "Save",
                MinWidth = 90,
                Classes = { "positive" }
            };
            saveButton.Click += async (_, _) => await SaveAsync();
            toolbarButtons.Children.Add(saveButton);
        }
        toolbar.Children.Add(toolbarButtons);
        toolbarBorder.Child = toolbar;
        shell.Children.Add(toolbarBorder);

        _statusText = new TextBlock
        {
            Text = "",
            Foreground = Brush.Parse("#C59A52"),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4)
        };
        var statusBorder = new Border { Height = 28, Child = _statusText };
        _saveFeedback = new EditorSaveFeedback(_statusText);
        _saveFeedback.Register(this);
        AddHandler(InputElement.TextInputEvent, (_, _) => _saveFeedback.ClearSuccessOnEdit(), RoutingStrategies.Tunnel);
        DockPanel.SetDock(statusBorder, Dock.Bottom);
        shell.Children.Add(statusBorder);

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("7*,5,3*"),
            Background = Brush.Parse("#111311")
        };
        root.ColumnDefinitions[2].MinWidth = 28;
        var expandedPreviewWidth = root.ColumnDefinitions[2].Width;
        shell.Children.Add(root);

        var scrollPanel = new StackPanel { Spacing = 8 };
        _structuredForm = scrollPanel;
        var commandCard = new Border
        {
            Background = Brush.Parse("#0E1110"),
            BorderBrush = Brush.Parse("#4C4031"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Margin = new Thickness(16, 24, 14, 16),
            Child = scrollPanel
        };
        var scroll = new ScrollViewer
        {
            Content = commandCard,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);

        BuildStructuredForm();
        _openedCommandMode = _commandMode;
        _openedTransformFrom = _transformDefinition?.From?.Trim() ?? string.Empty;
        if (IsTransformMode && _savedTransformElement == null)
            _savedTransformElement = CaptureTransformDefinition(definition.Name).ToElement();

        var splitter = new GridSplitter
        {
            Width = 5,
            Background = Brush.Parse("#6A5434"),
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);

        var previewHost = new Grid();
        var previewPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(12, 12, 12, 12)
        };
        var previewHeader = new DockPanel();
        _editXmlButton = new Button { Content = "Edit XML", HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(_editXmlButton, Dock.Right);
        previewHeader.Children.Add(_editXmlButton);
        previewHeader.Children.Add(new TextBlock
        {
            Text = "XML Preview",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(28, 0, 0, 8)
        });
        previewPanel.Children.Add(previewHeader);
        _xmlPreview = new TextEditor
        {
            IsReadOnly = true,
            Focusable = true,
            FontFamily = new FontFamily("Consolas"),
            Background = Brush.Parse("#090C0B"),
            Foreground = Brush.Parse("#E8DECC"),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        XmlSyntaxEditorService.Configure(_xmlPreview);
        var previewBorder = new Border
        {
            BorderBrush = Brush.Parse("#4C4031"),
            BorderThickness = new Thickness(1),
            Child = _xmlPreview
        };
        Grid.SetRow(previewBorder, 1);
        previewPanel.Children.Add(previewBorder);
        previewHost.Children.Add(previewPanel);

        var previewToggle = new Button
        {
            Content = "\u25B6",
            Width = 20,
            Height = 24,
            MinWidth = 20,
            MinHeight = 24,
            FontSize = 10,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 8, 0, 0),
            Background = Brush.Parse("#050505"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 1
        };
        ToolTip.SetTip(previewToggle, "Collapse XML Preview");
        var isPreviewCollapsed = false;
        previewToggle.Click += (_, _) =>
        {
            isPreviewCollapsed = !isPreviewCollapsed;
            previewPanel.IsVisible = !isPreviewCollapsed;
            splitter.IsVisible = !isPreviewCollapsed;
            root.ColumnDefinitions[1].Width = new GridLength(isPreviewCollapsed ? 0 : 5);
            root.ColumnDefinitions[2].Width = isPreviewCollapsed
                ? new GridLength(28)
                : expandedPreviewWidth;
            previewToggle.Content = isPreviewCollapsed ? "\u25C0" : "\u25B6";
            ToolTip.SetTip(previewToggle, isPreviewCollapsed ? "Restore XML Preview" : "Collapse XML Preview");
        };
        previewHost.Children.Add(previewToggle);

        Grid.SetColumn(previewHost, 2);
        root.Children.Add(previewHost);

        Content = shell;
        _xmlController = new XmlEditController(_xmlPreview, _editXmlButton, _structuredForm, () => _saveFeedback.ClearSuccessOnEdit());
        _xmlController.BeforeEditAsync = BeginCommandXmlEditingAsync;
        Closed += (_, _) => { _xmlClosed = true; _xmlSession?.Reset(""); _xmlController.Dispose(); };
        RefreshPreview();
    }

    private void ApplyReadOnlyAppearance(Control control)
    {
        if (_readOnly)
            EditorFieldAppearance.ApplyReadOnly(control);
    }

    private void ApplyAbilityEditorFieldAppearance(Control control)
    {
        if (!_readOnly)
            EditorFieldAppearance.ApplyStandard(control);
    }


    private enum CommandEditorMode
    {
        Free,
        TransformUnique,
        TransformMultiple,
        Spawn
    }

    private ComboBox? _commandModeBox;
    private StackPanel? _commandFieldsHost;
    private StackPanel? _prereqFieldsHost;
    private CommandEditorMode _commandMode;

    private CommandEditorMode DetectCommandMode()
        => DetectCommandMode(_definition);

    private static CommandEditorMode DetectCommandMode(ProtoUnitCommandDefinition definition)
    {
        if (definition.Flags.Contains("spawncommand"))
            return CommandEditorMode.Spawn;
        if (definition.Flags.Contains("transform"))
            return CommandEditorMode.TransformMultiple;
        if (definition.Flags.Contains("transformselected") || definition.Flags.Contains("transformvillager"))
            return CommandEditorMode.TransformUnique;
        return CommandEditorMode.Free;
    }

    private static string GetCommandModeDisplayName(CommandEditorMode mode) => mode switch
    {
        CommandEditorMode.TransformUnique => "Transform (Unique)",
        CommandEditorMode.TransformMultiple => "Transform (Multiple)",
        _ => mode.ToString()
    };

    private static bool TryParseCommandMode(string text, out CommandEditorMode mode)
    {
        mode = text switch
        {
            "Transform (Unique)" => CommandEditorMode.TransformUnique,
            "Transform (Multiple)" => CommandEditorMode.TransformMultiple,
            "Spawn" => CommandEditorMode.Spawn,
            _ => CommandEditorMode.Free
        };
        return text is "Free" or "Transform (Unique)" or "Transform (Multiple)" or "Spawn";
    }

    private static void AddHeader(Panel panel, string text) => panel.Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 12, 0, 4)
    });

    private void BuildDisplaySection(Panel panel)
    {
        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("115,240,45,240"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AddInlineValueEditor(topRow, "displaynameid", 0, 1, alwaysShowForCustom: true);
        AddInlineValueEditor(topRow, "icon", 2, 3, alwaysShowForCustom: true);
        WrapCommandGrid(topRow, [0, 1], [2, 3]);
        if (topRow.Children.Count > 0)
            panel.Children.Add(topRow);

        if (!_readOnly || HasValue("rollovertextid"))
            AddValueRow(panel, "rollovertextid");

        var optionalTags = new[]
        {
            "shortrollovertextid", "activerollovertextid", "disabledrollovertextid",
            "buildlimittextid", "valuetext", "activeicon"
        };

        if (_readOnly)
        {
            var visible = optionalTags.Where(HasValue).ToList();
            if (visible.Count > 0)
            {
                AddHeader(panel, "Optional fields");
                foreach (var tag in visible)
                    AddValueRow(panel, tag);
            }
        }
        else
        {
            var activeHost = new StackPanel { Spacing = 4 };
            var header = new TextBlock
            {
                Text = "Optional fields",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 12, 0, 4),
                IsVisible = false
            };
            panel.Children.Add(header);
            panel.Children.Add(activeHost);

            var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(buttonRow);
            var buttons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

            void RemoveOptional(string tag, Control row)
            {
                if (_valueEditors.Remove(tag))
                {
                    if (StringBackedTags.Contains(tag))
                        _stringTexts.Remove(tag);
                    else
                        _definition.Values.Remove(tag);
                }
                activeHost.Children.Remove(row);
                if (buttons.TryGetValue(tag, out var button))
                    button.IsVisible = true;
                header.IsVisible = activeHost.Children.Count > 0;
                RefreshPreview();
            }

            void AddOptional(string tag)
            {
                if (_valueEditors.ContainsKey(tag))
                    return;
                var row = CreateRemovableValueRow(tag, RemoveOptional);
                activeHost.Children.Add(row);
                header.IsVisible = true;
                if (buttons.TryGetValue(tag, out var button))
                    button.IsVisible = false;
            }

            foreach (var tag in optionalTags.Where(tag => !ProtoUnitCommandDefinition.DeprecatedValueFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                var button = new Button
                {
                    Content = Labels.GetValueOrDefault(tag, tag),
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(8, 4),
                    Classes = { "add-component" }
                };
                buttons[tag] = button;
                button.Click += (_, _) => { AddOptional(tag); RefreshPreview(); };
                buttonRow.Children.Add(button);
            }

            foreach (var tag in optionalTags.Where(tag => !ProtoUnitCommandDefinition.DeprecatedValueFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase) && HasValue(tag)))
                AddOptional(tag);
        }

        // Deprecated display fields are visible only when an Original command actually uses them.
        foreach (var tag in new[] { "controllericon", "disabledicon" }.Where(tag => _readOnly && HasValue(tag)))
            AddValueRow(panel, tag);
    }

    private void BuildCommandSection(Panel panel)
    {
        AddHeader(panel, "Command");
        _commandFieldsHost = new StackPanel { Spacing = 4 };
        panel.Children.Add(_commandFieldsHost);

        _prereqFieldsHost = new StackPanel { Spacing = 4 };
        panel.Children.Add(_prereqFieldsHost);

        RebuildCommandModeFields();
    }

    private void OnCommandModeChanged()
    {
        if (_commandModeBox?.SelectedItem is not string text || !TryParseCommandMode(text, out var mode))
            return;
        if (_commandMode == mode)
            return;

        // Persist the currently typed mode fields before rebuilding the controls.
        StoreVisibleModeValues();

        if (mode != CommandEditorMode.Spawn)
            _flags.Remove("spawncommand");
        if (mode is not CommandEditorMode.TransformUnique and not CommandEditorMode.TransformMultiple)
        {
            foreach (var flag in _flags.Where(ProtoUnitCommandTransformRules.IsTransformFamilyFlag).ToList())
                _flags.Remove(flag);
            if (_commandMode is CommandEditorMode.TransformUnique or CommandEditorMode.TransformMultiple)
            {
                _flags.Remove("displayontarget");
                _flags.Remove("researchonselected");
                _flags.Remove("unitcommand");
            }
        }

        _commandMode = mode;
        ApplyStructuralModeFlags();

        RebuildCommandModeFields();
        _sharedFlagsEditor?.Refresh();
        RefreshPreview();
    }

    private string GetModeFieldValue(string tag, Control control)
    {
        var value = GetControlValue(control);
        IReadOnlyList<string>? allowed = tag.ToLowerInvariant() switch
        {
            "associatedtech" or "forbidtech" => _context.TechNames,
            "trainableunitreq" or "protounit" => _context.ProtoUnitNames,
            "prereqcommand" => CurrentCommandNames,
            "associatedpower" => _context.PowerNames,
            _ => null
        };
        if (allowed == null || string.IsNullOrWhiteSpace(value))
            return value;
        return allowed.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private void StoreVisibleModeValues()
    {
        if ((_commandMode is CommandEditorMode.TransformUnique or CommandEditorMode.TransformMultiple) &&
            _transformFromEditor != null && _transformToEditor != null)
        {
            _transformDefinition = CaptureTransformDefinition(_nameBox.Text?.Trim() ?? _definition.Name);
        }

        foreach (var tag in new[] { "command", "secondarycommand", "associatedtech", "associatedpower", "forbidtech", "protounit", "amount", "age", "trainableunitreq", "prereqcommand" })
        {
            if (_valueEditors.TryGetValue(tag, out var control))
                _definition.Values[tag] = GetModeFieldValue(tag, control);
        }
    }

    private bool IsTransformMode => _commandMode is CommandEditorMode.TransformUnique or CommandEditorMode.TransformMultiple;

    private ProtoUnitTransformDefinition CaptureTransformDefinition(string commandName)
    {
        var transform = _transformDefinition?.Clone() ?? new ProtoUnitTransformDefinition();
        transform.Command = commandName.Trim();
        transform.From = _transformFromEditor?.Text?.Trim() ?? transform.From;
        transform.To = _transformToEditor?.Text?.Trim() ?? transform.To;
        transform.Tech = _transformTechEditor?.Text?.Trim() ?? transform.Tech;
        transform.RevertOthersTo = _transformDefinition?.RevertOthersTo ?? transform.RevertOthersTo;
        transform.FullHeal = _transformDefinition?.FullHeal ?? (_transformFullHealEditor?.IsChecked == true);
        return transform;
    }

    private void BuildSharedTransformEditor(Panel panel)
    {
        var transform = _transformDefinition ??= new ProtoUnitTransformDefinition();
        var transformValues = new Dictionary<string, string>(_definition.Values, StringComparer.OrdinalIgnoreCase);
        var shared = new ProtoUnitCommandTransformEditor(
            transform,
            transformValues,
            _context.ProtoUnitNames,
            _context.TechNames,
            _context.PowerNames,
            editable: !_readOnly,
            lockFrom: _identityLockedByUsage,
            changed: RefreshPreview,
            isBusy: () => _refreshingPreview);

        _sharedTransformEditor = shared;
        _transformFromEditor = shared.FromEditor;
        _transformToEditor = shared.ToEditor;
        _transformTechEditor = shared.PrereqTechEditor;
        _transformFullHealEditor = shared.FullHealEditor;
        _transformRevertOthersEditor = shared.RevertOthersEditor;
        _valueEditors["associatedtech"] = shared.AssociatedTechEditor;
        foreach (var pair in shared.OptionalValueEditors)
            _valueEditors[pair.Key] = pair.Value;

        panel.Children.Add(shared);
    }

    private void RebuildCommandModeFields()
    {
        if (_commandFieldsHost == null || _prereqFieldsHost == null)
            return;

        StoreVisibleModeValues();
        foreach (var tag in new[] { "command", "secondarycommand", "associatedtech", "associatedpower", "forbidtech", "protounit", "amount", "age", "trainableunitreq", "prereqcommand" })
            _valueEditors.Remove(tag);

        _commandFieldsHost.Children.Clear();
        _prereqFieldsHost.Children.Clear();
        _transformFromEditor = null;
        _transformToEditor = null;
        _transformTechEditor = null;
        _transformRevertOthersEditor = null;
        _transformFullHealEditor = null;
        _sharedTransformEditor = null;

        switch (_commandMode)
        {
            case CommandEditorMode.Free:
                AddTwoFieldRow(_commandFieldsHost, "command", "secondarycommand", secondOptional: true);
                AddTwoOptionalFieldRow(_commandFieldsHost, "associatedtech", "associatedpower");
                AddOptionalSingleField(_commandFieldsHost, "forbidtech");
                BuildPrereqs(_prereqFieldsHost);
                break;

            case CommandEditorMode.TransformUnique:
            case CommandEditorMode.TransformMultiple:
                BuildSharedTransformEditor(_commandFieldsHost);
                BuildPrereqs(_prereqFieldsHost);
                break;

            case CommandEditorMode.Spawn:
                AddTwoFieldRow(_commandFieldsHost, "protounit", "amount");
                AddTwoFieldRow(_commandFieldsHost, "associatedtech", "associatedpower", secondOptional: true);
                break;
        }
    }

    private void BuildPrereqs(Panel panel)
    {
        var tags = new[] { "age", "trainableunitreq", "prereqcommand" };
        var visibleForOriginal = tags.Where(HasValue).ToList();
        if (_readOnly)
        {
            if (visibleForOriginal.Count == 0)
                return;
            AddHeader(panel, "Prereqs");
            foreach (var tag in visibleForOriginal)
                AddValueRow(panel, tag);
            return;
        }

        var header = new TextBlock
        {
            Text = "Prereqs",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 12, 0, 4),
            IsVisible = false
        };
        var activeHost = new StackPanel { Spacing = 4 };
        var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(header);
        panel.Children.Add(activeHost);
        panel.Children.Add(buttonRow);
        var buttons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        void RemovePrereq(string tag, Control row)
        {
            _valueEditors.Remove(tag);
            _definition.Values.Remove(tag);
            activeHost.Children.Remove(row);
            if (buttons.TryGetValue(tag, out var button))
                button.IsVisible = true;
            header.IsVisible = activeHost.Children.Count > 0;
            RefreshPreview();
        }

        void AddPrereq(string tag)
        {
            if (_valueEditors.ContainsKey(tag))
                return;
            var row = CreateRemovableValueRow(tag, RemovePrereq);
            activeHost.Children.Add(row);
            header.IsVisible = true;
            if (buttons.TryGetValue(tag, out var button))
                button.IsVisible = false;
        }

        foreach (var tag in tags)
        {
            var button = new Button
            {
                Content = tag switch { "age" => "Age", "trainableunitreq" => "Trainable Unit", _ => "Command" },
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 4),
                Classes = { "add-component" }
            };
            buttons[tag] = button;
            button.Click += (_, _) => { AddPrereq(tag); RefreshPreview(); };
            buttonRow.Children.Add(button);
        }

        foreach (var tag in tags.Where(HasValue))
            AddPrereq(tag);
    }

    private Control CreateRemovableValueRow(string tag, Action<string, Control> removeAction)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("115,240,Auto,*"), ColumnSpacing = 8, Margin = new Thickness(0, 2) };
        row.Children.Add(new TextBlock { Text = Labels.GetValueOrDefault(tag, tag), VerticalAlignment = VerticalAlignment.Center });
        var editor = CreateValueEditor(tag);
        _valueEditors[tag] = editor;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        var remove = EditorRemoveButtons.Field();
        remove.Click += (_, _) => removeAction(tag, row);
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        if (editor is TextBox rollover && EditorRolloverField.IsLong(rollover))
        {
            row.Children.Clear();
            var bounded = EditorRolloverField.CreateRow(Labels.GetValueOrDefault(tag, tag), rollover, remove, 115);
            row.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetColumn(bounded, 0); row.Children.Add(bounded);
        }
        else WrapCommandGrid(row, [0, 1, 2]);
        return row;
    }

    private void AddOptionalSingleField(Panel panel, string tag)
    {
        if (_readOnly)
        {
            if (HasValue(tag))
                AddValueRow(panel, tag);
            return;
        }

        var button = new Button
        {
            Content = $"Add {Labels.GetValueOrDefault(tag, tag)}",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4),
            Classes = { "add-component" }
        };
        panel.Children.Add(button);

        void AddField()
        {
            if (_valueEditors.ContainsKey(tag))
                return;
            var row = CreateRemovableValueRow(tag, (_, control) =>
            {
                _valueEditors.Remove(tag);
                _definition.Values.Remove(tag);
                panel.Children.Remove(control);
                button.IsVisible = true;
                RefreshPreview();
            });
            var buttonIndex = panel.Children.IndexOf(button);
            panel.Children.Insert(buttonIndex, row);
            button.IsVisible = false;
        }

        button.Click += (_, _) => { AddField(); RefreshPreview(); };
        if (HasValue(tag))
            AddField();
    }

    private void AddTwoOptionalFieldRow(Panel panel, string firstTag, string secondTag)
    {
        if (_readOnly)
        {
            if (HasValue(firstTag) || HasValue(secondTag))
                AddTwoFieldRow(panel, firstTag, secondTag, secondOptional: true);
            return;
        }

        var row = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2)
        };
        panel.Children.Add(row);

        void BuildOptionalHost(string tag)
        {
            var host = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,240,Auto"),
                ColumnSpacing = 8
            };
            row.Children.Add(host);

            var add = new Button
            {
                Content = Labels.GetValueOrDefault(tag, tag),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4),
                Classes = { "add-component" },
                VerticalAlignment = VerticalAlignment.Center
            };
            host.Children.Add(add);

            void ShowField()
            {
                if (_valueEditors.ContainsKey(tag))
                    return;

                host.Children.Clear();
                var label = new TextBlock
                {
                    Text = Labels.GetValueOrDefault(tag, tag),
                    VerticalAlignment = VerticalAlignment.Center
                };
                host.Children.Add(label);

                var editor = CreateValueEditor(tag);
                _valueEditors[tag] = editor;
                Grid.SetColumn(editor, 1);
                host.Children.Add(editor);

                var remove = EditorRemoveButtons.Field();
                remove.Click += (_, _) =>
                {
                    _valueEditors.Remove(tag);
                    _definition.Values.Remove(tag);
                    host.Children.Clear();
                    host.Children.Add(add);
                    RefreshPreview();
                };
                Grid.SetColumn(remove, 2);
                host.Children.Add(remove);
                WrapCommandGrid(host, [0, 1, 2]);
            }

            add.Click += (_, _) =>
            {
                ShowField();
                RefreshPreview();
            };

            if (HasValue(tag))
                ShowField();
        }

        BuildOptionalHost(firstTag);
        BuildOptionalHost(secondTag);
    }

    private void AddTwoFieldRow(Panel panel, string firstTag, string secondTag, bool secondOptional = false)
    {
        var firstVisible = !_readOnly || HasValue(firstTag);
        var secondVisible = (!_readOnly && !secondOptional) || HasValue(secondTag);
        if (!firstVisible && !secondVisible) return;
        var row = new WrapPanel { Margin = new Thickness(0, 2) };
        if (firstVisible) row.Children.Add(CreateCommandFieldGroup(firstTag, leading: true));
        panel.Children.Add(row);
        if (_readOnly || !secondOptional)
        {
            if (secondVisible) row.Children.Add(CreateCommandFieldGroup(secondTag));
            return;
        }

        // The optional host owns its children. Wrapping never changes the scope
        // removed by this callback or makes it depend on Grid column ancestry.
        var host = new StackPanel();
        row.Children.Add(host);
        var add = new Button
        {
            Content = secondTag.Equals("associatedpower", StringComparison.OrdinalIgnoreCase)
                ? "Associated Power" : $"Add {Labels.GetValueOrDefault(secondTag, secondTag)}",
            Classes = { "add-component" }, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4)
        };
        void RemoveSecondField()
        {
            _valueEditors.Remove(secondTag);
            _definition.Values.Remove(secondTag);
            host.Children.Clear(); host.Children.Add(add);
            RefreshPreview();
        }
        void AddSecondField()
        {
            if (_valueEditors.ContainsKey(secondTag)) return;
            var remove = EditorRemoveButtons.Field();
            remove.Click += (_, _) => RemoveSecondField();
            host.Children.Clear(); host.Children.Add(CreateCommandFieldGroup(secondTag, cross: remove));
        }
        add.Click += (_, _) => { AddSecondField(); RefreshPreview(); };
        if (HasValue(secondTag)) AddSecondField();
        else host.Children.Add(add);
    }

    private void AddInlineValueEditor(Grid row, string tag, int labelColumn, int editorColumn, bool alwaysShowForCustom)
    {
        if (_readOnly && !HasValue(tag))
            return;
        if (!_readOnly && !alwaysShowForCustom && !HasValue(tag))
            return;

        var label = new TextBlock { Text = Labels.GetValueOrDefault(tag, tag), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, labelColumn);
        row.Children.Add(label);
        var editor = CreateValueEditor(tag);
        _valueEditors[tag] = editor;
        Grid.SetColumn(editor, editorColumn);
        row.Children.Add(editor);
    }

    private bool HasValue(string tag)
        => !string.IsNullOrWhiteSpace(_definition.Values.GetValueOrDefault(tag, ""));

    private IEnumerable<string> SuggestionsFor(string tag) => tag.ToLowerInvariant() switch
    {
        "associatedtech" or "forbidtech" => _context.TechNames,
        "trainableunitreq" or "protounit" or "costprotounit" => _context.ProtoUnitNames,
        "prereqcommand" => CurrentCommandNames,
        "associatedpower" => _context.PowerNames,
        "actionforactiveicon" => _context.ActionNames,
        "age" => new[] { "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge" },
        _ => []
    };

    private void AddValueRow(Panel panel, string tag, Control? insertBefore = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("115,240,*"), ColumnSpacing = 8, Margin = new Thickness(0,2) };
        row.Children.Add(new TextBlock { Text = Labels.GetValueOrDefault(tag, tag), VerticalAlignment = VerticalAlignment.Center });
        var editor = CreateValueEditor(tag);
        _valueEditors[tag] = editor;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        if (editor is TextBox rollover && EditorRolloverField.IsLong(rollover))
        {
            row.Children.Clear();
            var bounded = EditorRolloverField.CreateRow(Labels.GetValueOrDefault(tag, tag), rollover, labelWidth: 115);
            row.ColumnDefinitions = new ColumnDefinitions("*"); row.Children.Add(bounded);
        }
        else WrapCommandGrid(row, [0, 1]);
        if (insertBefore != null && panel.Children.IndexOf(insertBefore) is var index && index >= 0)
            panel.Children.Insert(index, row);
        else
            panel.Children.Add(row);
    }

    private Control CreateValueEditor(string tag)
    {
        Control editor;
        var value = StringBackedTags.Contains(tag) ? _stringTexts.GetValueOrDefault(tag, "") : _definition.Values.GetValueOrDefault(tag, "");

        if (tag.Equals("age", StringComparison.OrdinalIgnoreCase))
        {
            var values = new[] { "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge" };
            var cb = new ComboBox
            {
                ItemsSource = values,
                SelectedItem = values.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)),
                IsEnabled = !_readOnly,
                Width = EditorTextFieldStyle.StandardWidth,
                MaxWidth = EditorTextFieldStyle.StandardWidth
            };
            if (_readOnly && cb.SelectedItem == null && !string.IsNullOrWhiteSpace(value))
                cb.ItemsSource = values.Append(value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (_readOnly && cb.SelectedItem == null)
                cb.SelectedItem = value;
            ApplyReadOnlyAppearance(cb);
            cb.SelectionChanged += (_, _) => RefreshPreview();
            editor = cb;
        }
        else if (tag.Equals("icon", StringComparison.OrdinalIgnoreCase) || tag.Equals("controllericon", StringComparison.OrdinalIgnoreCase) || tag.Equals("activeicon", StringComparison.OrdinalIgnoreCase) || tag.Equals("disabledicon", StringComparison.OrdinalIgnoreCase))
        {
            // Reuse the same hardened asset-path control as ProtoUnit/Abilities instead of
            // maintaining a second AutoCompleteBox path implementation here.
            var pathEditor = new AssetPathEditor
            {
                IsEnabled = !_readOnly,
                Opacity = 1.0
            };
            pathEditor.Configure(value, _context.IconPaths, _ =>
            {
                RefreshPreview();
                return Task.CompletedTask;
            });
            editor = pathEditor;
        }
        else if (tag.Equals("amount", StringComparison.OrdinalIgnoreCase))
        {
            var tb = new TextBox { Text = value, IsEnabled = !_readOnly, Width = 240, MaxWidth = 240 };
            ApplyAbilityEditorFieldAppearance(tb);
            ApplyReadOnlyAppearance(tb);
            tb.TextChanged += (_,_) =>
            {
                if (tb.Text?.Any(c => !char.IsDigit(c) && c != '-') == true)
                    tb.Text = new string(tb.Text.Where(c => char.IsDigit(c) || c == '-').ToArray());
                RefreshPreview();
            };
            editor = tb;
        }
        else
        {
            var suggestions = SuggestionsFor(tag).ToList();
            if (suggestions.Count > 0)
            {
                var acb = CreateValidatedAutoComplete(value, suggestions);
                editor = acb;
            }
            else
            {
                var tb = new TextBox { Text = value, IsEnabled = !_readOnly };
                EditorTextFieldStyle.ConfigureTextBox(tb);
                ApplyAbilityEditorFieldAppearance(tb);
                ApplyReadOnlyAppearance(tb);
                editor = tb;
            }
        }
        if (editor is TextBox t)
        {
            t.TextChanged += (_,_) =>
            {
                ClearSpawnValidationIfValid(tag);
                RefreshPreview();
            };
        }
        if (editor is AutoCompleteBox a)
        {
            a.TextChanged += (_,_) =>
            {
                ClearSpawnValidationIfValid(tag);
                if (tag.Equals("associatedtech", StringComparison.OrdinalIgnoreCase))
                {
                    var value = GetControlValue(a);
                    if (string.IsNullOrWhiteSpace(value) || _context.TechNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                        SetValidationBorder(a, false);
                }
                RefreshPreview();
            };
        }
        return ConfigureCommandField(editor, tag);
    }

    private AutoCompleteBox CreateValidatedAutoComplete(string value, IEnumerable<string> source)
    {
        var acb = new AutoCompleteBox
        {
            Text = value?.Trim() ?? "",
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = !_readOnly
        };
        ProtoUnitCommandFieldFactory.ConfigureField(acb, EditorFieldWidthRole.Reference, () => _refreshingPreview);
        ApplyAbilityEditorFieldAppearance(acb);
        ApplyReadOnlyAppearance(acb);
        EditorAutoCompleteService.ConfigureStrict(
            acb,
            source,
            value ?? "",
            () => _refreshingPreview,
            preserveUnknownInitialValue: _readOnly,
            allowEmpty: true,
            commitEmptyAsValid: true,
            deferSelectionCommit: true,
            valueCommitted: _ => RefreshPreview());
        return acb;
    }

    private static string GetControlValue(Control control) => control switch
    {
        AssetPathEditor pathEditor => pathEditor.FullValue.Trim(),
        TextBox tb => tb.Text?.Trim() ?? "",
        AutoCompleteBox acb => acb.Text?.Trim() ?? "",
        ComboBox cb => cb.SelectedItem?.ToString()?.Trim() ?? "",
        _ => ""
    };

    private void AddRepeatableRow(Panel panel, string tag, string label)
    {
        _repeatables.TryAdd(tag, []);
        var holder = new StackPanel { Spacing = 4 };
        void Rebuild()
        {
            holder.Children.Clear();
            for (var index = 0; index < _repeatables[tag].Count; index++)
            {
                var rowIndex = index;
                var value = _repeatables[tag][rowIndex];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("115,240,Auto"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                var acb = CreateValidatedAutoComplete(value, CurrentCommandNames);
                acb.IsEnabled = !_readOnly;
                ApplyReadOnlyAppearance(acb);
                acb.TextChanged += (_,_) =>
                {
                    if (rowIndex < _repeatables[tag].Count) _repeatables[tag][rowIndex] = GetControlValue(acb);
                    RefreshPreview();
                };
                Grid.SetColumn(acb,1); row.Children.Add(acb);
                if (!_readOnly)
                {
                    var remove = EditorRemoveButtons.Row();
                    remove.Click += (_,_) =>
                    {
                        if (rowIndex < _repeatables[tag].Count) _repeatables[tag].RemoveAt(rowIndex);
                        Rebuild();
                        RefreshPreview();
                    };
                    Grid.SetColumn(remove,2); row.Children.Add(remove);
                }
                WrapCommandGrid(row, [0, 1, 2]);
                holder.Children.Add(row);
            }
            if (!_readOnly)
            {
                var add = new Button { Content = $"Add {label}", Classes = { "add-component" }, HorizontalAlignment = HorizontalAlignment.Left };
                add.Click += (_,_) => { _repeatables[tag].Add(""); Rebuild(); RefreshPreview(); };
                holder.Children.Add(add);
            }
        }
        Rebuild(); panel.Children.Add(holder);
    }

    private void AddFlagsEditor(Panel panel)
    {
        ProtoUnitCommandTransformKind? CurrentTransformKind() => _commandMode switch
        {
            CommandEditorMode.TransformUnique => ProtoUnitCommandTransformKind.Unique,
            CommandEditorMode.TransformMultiple => ProtoUnitCommandTransformKind.Multiple,
            _ => null
        };

        _sharedFlagsEditor = new ProtoUnitCommandFlagsEditor(
            _flags,
            editable: !_readOnly,
            transformKind: CurrentTransformKind,
            changed: RefreshPreview,
            isBusy: () => _refreshingPreview,
            flagAdded: selectedFlag =>
            {
                if (!ProtoUnitCommandTransformRules.IsTransformFamilyFlag(selectedFlag))
                    return;

                _commandMode = selectedFlag.Equals("transform", StringComparison.OrdinalIgnoreCase)
                    ? CommandEditorMode.TransformMultiple
                    : CommandEditorMode.TransformUnique;
                ApplyStructuralModeFlags();
                if (_commandModeBox != null)
                    _commandModeBox.SelectedItem = GetCommandModeDisplayName(_commandMode);
                RebuildCommandModeFields();
            });
        panel.Children.Add(_sharedFlagsEditor);
    }

    private string GetEditorValue(string tag) => _valueEditors.TryGetValue(tag, out var control) ? control switch
    {
        AssetPathEditor pathEditor => pathEditor.FullValue.Trim(),
        TextBox tb when StringBackedTags.Contains(tag) && !_rawStringReferenceTags.Contains(tag) => tb.Text ?? "",
        TextBox tb => tb.Text?.Trim() ?? "",
        AutoCompleteBox acb => acb.Text?.Trim() ?? "",
        ComboBox cb => cb.SelectedItem?.ToString()?.Trim() ?? "",
        _ => ""
    } : "";

    private ProtoUnitCommandDefinition Capture()
    {
        // Preview and validation must never mutate the live editor definition. Avalonia can
        // raise TextChanged/SelectionChanged while an AutoCompleteBox is still committing its
        // selection; mutating shared state from those events caused the command editor crashes.
        var captured = ProtoUnitCommandDefinition.FromElement(_definition.ToElement());
        captured.PreserveSourceStructure = _definition.PreserveSourceStructure;
        captured.Name = _nameBox.Text?.Trim() ?? "";

        foreach (var tag in ProtoUnitCommandDefinition.ValueFieldTags)
        {
            if (StringBackedTags.Contains(tag))
                continue;
            if (ProtoUnitCommandDefinition.DeprecatedValueFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase) && !_valueEditors.ContainsKey(tag))
                continue;
            if (_valueEditors.TryGetValue(tag, out var control))
                captured.Values[tag] = GetModeFieldValue(tag, control);
        }

        var allowedModeFields = _commandMode switch
        {
            CommandEditorMode.Free => new HashSet<string>(["command", "secondarycommand", "associatedtech", "associatedpower", "forbidtech", "age", "trainableunitreq", "prereqcommand"], StringComparer.OrdinalIgnoreCase),
            CommandEditorMode.TransformUnique or CommandEditorMode.TransformMultiple => new HashSet<string>(["associatedtech", "associatedpower", "forbidtech", "age", "trainableunitreq", "prereqcommand"], StringComparer.OrdinalIgnoreCase),
            _ => new HashSet<string>(["protounit", "amount", "associatedtech", "associatedpower"], StringComparer.OrdinalIgnoreCase)
        };
        foreach (var tag in new[] { "command", "secondarycommand", "associatedtech", "associatedpower", "forbidtech", "protounit", "amount", "age", "trainableunitreq", "prereqcommand" })
        {
            if (!allowedModeFields.Contains(tag) && (!_definition.PreserveSourceStructure || _commandMode != DetectCommandMode(_definition)))
                captured.Values.Remove(tag);
        }

        foreach (var tag in ProtoUnitCommandDefinition.RepeatableFieldTags)
            captured.RepeatableValues[tag] = _repeatables.GetValueOrDefault(tag, []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

        captured.Flags.Clear();
        foreach (var flag in _flags)
            captured.Flags.Add(flag);

        if (_definition.PreserveSourceStructure && _commandMode == DetectCommandMode(_definition)) return captured;

        captured.Flags.Remove("spawncommand");
        if (_commandMode == CommandEditorMode.Spawn)
        {
            foreach (var flag in captured.Flags.Where(ProtoUnitCommandTransformRules.IsTransformFamilyFlag).ToList())
                captured.Flags.Remove(flag);
            captured.Flags.Add("spawncommand");
        }
        else if (_commandMode == CommandEditorMode.TransformUnique)
            ProtoUnitCommandTransformRules.EnsureStructuralFlag(captured.Flags, ProtoUnitCommandTransformKind.Unique);
        else if (_commandMode == CommandEditorMode.TransformMultiple)
            ProtoUnitCommandTransformRules.EnsureStructuralFlag(captured.Flags, ProtoUnitCommandTransformKind.Multiple);
        else
        {
            foreach (var flag in captured.Flags.Where(ProtoUnitCommandTransformRules.IsTransformFamilyFlag).ToList())
                captured.Flags.Remove(flag);
        }

        return captured;
    }

    private void RefreshPreview()
    {
        if (_refreshingPreview || _xmlClosed || _xmlSession?.IsEditing == true)
            return;
        _saveFeedback.ClearSuccessOnEdit();
        _refreshingPreview = true;
        try
        {
            var captured = Capture();
            var preview = captured.ToElement();
            foreach (var tag in StringBackedTags)
            {
                if (_definition.PreserveSourceStructure && (GetEditorValue(tag) == _savedStringTexts.GetValueOrDefault(tag, "") ||
                    _rawStringReferenceTags.Contains(tag) && GetEditorValue(tag) == captured.Values.GetValueOrDefault(tag, ""))) continue;
                preview.Elements().Where(element => element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).Remove();
                var text = GetEditorValue(tag);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var currentId = captured.Values.GetValueOrDefault(tag, "");
                var id = currentId.StartsWith("STR_PUC_", StringComparison.OrdinalIgnoreCase)
                    ? currentId
                    : BuildPreviewStringId(captured.Name, tag);
                preview.Add(new XElement(tag, id));
            }
            var previewText = _definition.PreserveSourceStructure ? CommandXmlPreview(preview)
                : XmlSyntaxEditorService.PreparePreviewText(FormatPreviewXml(preview));
            _xmlSession ??= new XmlEditSession(previewText, !_readOnly);
            _xmlSession.RefreshFromModel(previewText);
            _xmlController?.Bind(_xmlSession, !_readOnly);
        }
        catch { }
        finally
        {
            _refreshingPreview = false;
        }
    }

    private static string BuildPreviewStringId(string commandName, string tag)
    {
        var normalized = new string(commandName.Trim().ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        normalized = normalized.Trim('_');
        var suffix = tag.ToLowerInvariant() switch
        {
            "displaynameid" => "NAME", "rollovertextid" => "LR", "shortrollovertextid" => "SR",
            "activerollovertextid" => "ACTIVE_LR", "disabledrollovertextid" => "DISABLED_LR", "buildlimittextid" => "BUILD_LIMIT",
            _ => tag.ToUpperInvariant()
        };
        return $"STR_PUC_{normalized}_{suffix}";
    }

    private static string FormatPreviewXml(XElement element)
    {
        var compact = element.ToString(SaveOptions.DisableFormatting);
        compact = System.Text.RegularExpressions.Regex.Replace(compact, @">\s+<", "><");
        return ProtoUnitCommandDefinition.ExpandEmptyFlagElements(XElement.Parse(compact).ToString());
    }

    private IReadOnlyDictionary<string, string> CaptureStringTexts()
        => StringBackedTags.Where(tag => !_rawStringReferenceTags.Contains(tag) ||
                GetEditorValue(tag) != _definition.Values.GetValueOrDefault(tag, ""))
            .ToDictionary(tag => tag, GetEditorValue, StringComparer.OrdinalIgnoreCase);

    private static void SetValidationBorder(Control? control, bool invalid)
    {
        var brush = Brush.Parse(invalid ? "#d64545" : "#4C4031");
        switch (control)
        {
            case AutoCompleteBox autoCompleteBox:
                autoCompleteBox.BorderBrush = brush;
                autoCompleteBox.BorderThickness = new Thickness(1);
                break;
            case TextBox textBox:
                textBox.BorderBrush = brush;
                textBox.BorderThickness = new Thickness(1);
                break;
        }
    }

    private void ClearSpawnValidationIfValid(string tag)
    {
        if (_commandMode != CommandEditorMode.Spawn || !_valueEditors.TryGetValue(tag, out var control))
            return;

        var value = GetControlValue(control);
        var valid = tag.ToLowerInvariant() switch
        {
            "protounit" => !string.IsNullOrWhiteSpace(value) && _context.ProtoUnitNames.Contains(value, StringComparer.OrdinalIgnoreCase),
            "amount" => int.TryParse(value, out _),
            "associatedtech" => !string.IsNullOrWhiteSpace(value) && _context.TechNames.Contains(value, StringComparer.OrdinalIgnoreCase),
            _ => false
        };

        if (valid)
            SetValidationBorder(control, false);
    }

    private void MarkAssociatedTechInvalid()
    {
        if (_valueEditors.TryGetValue("associatedtech", out var control))
            SetValidationBorder(control, true);
    }

    private void ClearAssociatedTechValidation()
    {
        if (_valueEditors.TryGetValue("associatedtech", out var control))
            SetValidationBorder(control, false);
    }

    private void ApplyStructuralModeFlags()
    {
        _flags.Remove("spawncommand");
        foreach (var flag in _flags.Where(ProtoUnitCommandTransformRules.IsTransformFamilyFlag).ToList())
            _flags.Remove(flag);

        if (_commandMode == CommandEditorMode.Spawn)
        {
            _flags.Add("spawncommand");
            _flags.Add("unitcommand");
        }
        else if (_commandMode == CommandEditorMode.TransformUnique)
        {
            ProtoUnitCommandTransformRules.ApplyModeDefaults(_flags, ProtoUnitCommandTransformKind.Unique);
        }
        else if (_commandMode == CommandEditorMode.TransformMultiple)
        {
            ProtoUnitCommandTransformRules.ApplyModeDefaults(_flags, ProtoUnitCommandTransformKind.Multiple);
        }
        _sharedFlagsEditor?.Refresh();
    }

    private async Task ReportCommandFieldErrorAsync(string title, string message, Control? target)
    {
        var failure = target is { IsEffectivelyEnabled: true }
            ? EditorValidationNavigation.Mark(this, target) : null;
        await new Prompt(PromptType.Error, title, message).ShowDialog(this);
        if (failure != null) await failure.RevealAsync();
    }

    private async Task SaveAsync()
    {
        if (_readOnly || _xmlSaving || _xmlClosed) return;
        _xmlSaving = true;
        try { await SaveCoreAsync(); }
        catch (Exception exception)
        {
            if (_xmlClosed) return;
            _saveFeedback.Report(EditorSaveOutcome.Failed);
            await new Prompt(PromptType.Error, "Unable to save command", exception.Message).ShowDialog(this);
        }
        finally { _xmlSaving = false; }
    }

    private async Task SaveCoreAsync()
    {
        using var feedback = _saveFeedback.BeginSave();
        var raw = _xmlSession?.CaptureForSave();
        ProtoUnitCommandDefinition captured;
        try { captured = raw == null ? Capture() : ParseCommandXml(raw.Text); }
        catch (Exception exception)
        {
            await new Prompt(PromptType.Error, "Invalid Command XML", exception.Message).ShowDialog(this);
            return;
        }
        if (raw != null && !_xmlSession!.IsDirty && !HasStructuredChanges())
        {
            _xmlSession.AcceptSaved(raw, raw.Text);
            feedback.Complete();
            return;
        }
        var candidateMode = raw == null ? _commandMode : DetectCommandMode(captured);
        var isTransformMode = candidateMode is CommandEditorMode.TransformUnique or CommandEditorMode.TransformMultiple;
        if (string.IsNullOrWhiteSpace(captured.Name))
        {
            await ReportCommandFieldErrorAsync("Missing name", "A ProtoUnit command must have an internal name.", raw == null ? _nameBox : null);
            return;
        }
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(captured.Name, _savedName))
        {
            await ReportCommandFieldErrorAsync("Invalid command name",
                "Command names can contain only letters, digits, '_' and '-'.", raw == null ? _nameBox : null);
            return;
        }
        if (_identityLockedByUsage)
        {
            if (candidateMode != _openedCommandMode)
            {
                await new Prompt(
                    PromptType.Error,
                    "Command type is locked",
                    "This command is already assigned to a ProtoUnit, so its Type cannot be changed from the standalone Command editor.").ShowDialog(this);
                return;
            }

            if (isTransformMode)
            {
                var currentFrom = CaptureTransformDefinition(captured.Name).From?.Trim() ?? string.Empty;
                if (!currentFrom.Equals(_openedTransformFrom, StringComparison.OrdinalIgnoreCase))
                {
                    await new Prompt(
                        PromptType.Error,
                        "Transform source is locked",
                        "This Transform command is already assigned to a ProtoUnit, so its Transform source cannot be changed from the standalone Command editor.").ShowDialog(this);
                    return;
                }
            }
        }
        var associatedTech = captured.Values.GetValueOrDefault("associatedtech", "");
        if (isTransformMode)
        {
            var transformForValidation = CaptureTransformDefinition(captured.Name);
            if (_transformDefinition != null)
            {
                _transformDefinition.From = transformForValidation.From;
                _transformDefinition.To = transformForValidation.To;
                _transformDefinition.Tech = transformForValidation.Tech;
            }
            var validation = (raw == null ? _sharedTransformEditor?.ValidateRequired() : null)
                             ?? ProtoUnitCommandTransformRules.ValidateRequired(transformForValidation, captured.Values, _context.ProtoUnitNames, _context.TechNames);
            if (!validation.IsValid)
            {
                var target = !validation.FromValid ? _sharedTransformEditor?.FromEditor :
                    !validation.ToValid ? _sharedTransformEditor?.ToEditor :
                    !validation.PrereqTechValid ? _sharedTransformEditor?.PrereqTechEditor : _sharedTransformEditor?.AssociatedTechEditor;
                await ReportCommandFieldErrorAsync("Incomplete Transform",
                    "Transform, To, Prereq Tech and Associated Tech are required and must use existing entries.", raw == null ? target : null);
                return;
            }
        }
        else if (candidateMode == CommandEditorMode.Spawn)
        {
            var protoUnit = captured.Values.GetValueOrDefault("protounit", "");
            var amount = captured.Values.GetValueOrDefault("amount", "");
            var protoUnitValid = !string.IsNullOrWhiteSpace(protoUnit) &&
                                 _context.ProtoUnitNames.Contains(protoUnit, StringComparer.OrdinalIgnoreCase);
            var amountValid = int.TryParse(amount, out _);
            var associatedTechValid = !string.IsNullOrWhiteSpace(associatedTech) &&
                                      _context.TechNames.Contains(associatedTech, StringComparer.OrdinalIgnoreCase);

            var protoUnitControl = _valueEditors.GetValueOrDefault("protounit");
            var amountControl = _valueEditors.GetValueOrDefault("amount");
            var associatedTechControl = _valueEditors.GetValueOrDefault("associatedtech");

            if (!protoUnitValid || !amountValid || !associatedTechValid)
            {
                var target = !protoUnitValid ? protoUnitControl : !amountValid ? amountControl : associatedTechControl;
                await ReportCommandFieldErrorAsync("Incomplete Spawn",
                    "Proto Unit, Amount and Associated Tech are required and must use valid values.", raw == null ? target : null);
                return;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(associatedTech) && !_context.TechNames.Contains(associatedTech, StringComparer.OrdinalIgnoreCase))
            {
                await ReportCommandFieldErrorAsync("Invalid Associated Tech", $"'{associatedTech}' is not an existing technology.", raw == null ? _valueEditors.GetValueOrDefault("associatedtech") : null);
                return;
            }
            ClearAssociatedTechValidation();
        }
        var forbidTech = captured.Values.GetValueOrDefault("forbidtech", "");
        if (!string.IsNullOrWhiteSpace(forbidTech) && !_context.TechNames.Contains(forbidTech, StringComparer.OrdinalIgnoreCase))
        {
            await ReportCommandFieldErrorAsync("Invalid Forbid Tech", $"'{forbidTech}' is not an existing technology.", raw == null ? _valueEditors.GetValueOrDefault("forbidtech") : null);
            return;
        }
        var trainableUnit = captured.Values.GetValueOrDefault("trainableunitreq", "");
        if (!string.IsNullOrWhiteSpace(trainableUnit) && !_context.ProtoUnitNames.Contains(trainableUnit, StringComparer.OrdinalIgnoreCase))
        {
            await ReportCommandFieldErrorAsync("Invalid Trainable Unit", $"'{trainableUnit}' is not an existing ProtoUnit.", raw == null ? _valueEditors.GetValueOrDefault("trainableunitreq") : null);
            return;
        }
        var prereqCommand = captured.Values.GetValueOrDefault("prereqcommand", "");
        if (!string.IsNullOrWhiteSpace(prereqCommand) && !CurrentCommandNames.Contains(prereqCommand, StringComparer.OrdinalIgnoreCase))
        {
            await ReportCommandFieldErrorAsync("Invalid Prereq Command", $"'{prereqCommand}' is not an existing ProtoUnit command.", raw == null ? _valueEditors.GetValueOrDefault("prereqcommand") : null);
            return;
        }
        var associatedPower = captured.Values.GetValueOrDefault("associatedpower", "");
        if (!string.IsNullOrWhiteSpace(associatedPower) && !_context.PowerNames.Contains(associatedPower, StringComparer.OrdinalIgnoreCase))
        {
            await ReportCommandFieldErrorAsync("Invalid Associated Power", $"'{associatedPower}' is not an existing power.", raw == null ? _valueEditors.GetValueOrDefault("associatedpower") : null);
            return;
        }

        ProtoUnitTransformDefinition? capturedTransform = null;
        if (isTransformMode)
        {
            capturedTransform = CaptureTransformDefinition(captured.Name);
            if (!string.IsNullOrWhiteSpace(capturedTransform.RevertOthersTo) && !_context.ProtoUnitNames.Contains(capturedTransform.RevertOthersTo, StringComparer.OrdinalIgnoreCase))
            {
                await ReportCommandFieldErrorAsync("Invalid Revert others to", $"'{capturedTransform.RevertOthersTo}' is not a valid ProtoUnit name.", raw == null ? _sharedTransformEditor?.RevertOthersEditor : null);
                return;
            }
        }

        if (captured.Flags.Contains("commandpassesunitid") && !captured.Values.GetValueOrDefault("command", "").Contains("%d", StringComparison.Ordinal))
        {
            var prompt = new Prompt(PromptType.Confirm, "CommandPassesUnitID", "CommandPassesUnitID is enabled but Command does not contain %d. Save anyway?", confirmButtonText: "Save", actionRole: PromptActionRole.Positive);
            await prompt.ShowDialog(this);
            if (!prompt.Confirmed) return;
        }
        var submittedStrings = raw == null ? CaptureStringTexts() : CaptureRawCommandStringTexts(captured);
        var submittedIds = StringBackedTags.ToDictionary(tag => tag, tag => captured.Values.GetValueOrDefault(tag, ""));
        if (await _save(captured, submittedStrings, capturedTransform))
        {
            if (_xmlClosed) return;
            if (!_savedName.Equals(captured.Name, StringComparison.OrdinalIgnoreCase)) _commandNameMigrations[_savedName] = captured.Name;
            RememberCommandStringMigrations(submittedIds, captured);
            _definition = captured;
            foreach (var pair in submittedStrings)
                _stringTexts[pair.Key] = pair.Value;
            _savedName = captured.Name;
            _savedElement = captured.ToElement();
            _transformDefinition = capturedTransform?.Clone();
            _savedTransformElement = capturedTransform?.ToElement();
            if (raw != null)
            {
                AdoptCommandXml(captured, submittedStrings);
                _xmlSession!.AcceptSaved(raw, CommandXmlPreview(captured.ToElement()));
            }
            _savedStringTexts = CaptureStringTexts().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            RefreshPreview();
            Title = $"Edit ProtoUnit Command - {captured.Name}";
            _saveFeedback.Report(EditorSaveOutcome.Saved);
            feedback.Complete(HasUnsavedChanges());
        }
    }
    private bool HasUnsavedChanges()
    {
        if (_readOnly)
            return false;
        return _xmlSession?.IsDirty == true || HasStructuredChanges();
    }

    private bool HasStructuredChanges()
    {

        try
        {
            if (!XNode.DeepEquals(Capture().ToElement(), _savedElement))
                return true;

            var currentTransform = IsTransformMode ? CaptureTransformDefinition(_nameBox.Text?.Trim() ?? _definition.Name).ToElement() : null;
            if ((_savedTransformElement == null) != (currentTransform == null) ||
                (_savedTransformElement != null && currentTransform != null && !XNode.DeepEquals(currentTransform, _savedTransformElement)))
                return true;

            var currentStrings = CaptureStringTexts();
            foreach (var tag in StringBackedTags)
            {
                if (!string.Equals(currentStrings.GetValueOrDefault(tag, ""), _savedStringTexts.GetValueOrDefault(tag, ""), StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task RequestCloseAsync()
    {
        if (!HasUnsavedChanges())
        {
            _allowClose = true;
            Close();
            return;
        }

        if (_closePromptOpen)
            return;

        _closePromptOpen = true;
        try
        {
            var prompt = new Prompt(PromptType.Confirm, "Discard command changes?", "You have unsaved command changes. Close this window without saving them?", confirmButtonText: "Discard changes", actionRole: PromptActionRole.Destructive);
            await prompt.ShowDialog(this);
            if (!prompt.Confirmed)
                return;

            _allowClose = true;
            Close();
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && HasUnsavedChanges())
        {
            e.Cancel = true;
            await RequestCloseAsync();
            return;
        }

        base.OnClosing(e);
    }

}
