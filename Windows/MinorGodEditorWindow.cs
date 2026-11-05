using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record MinorGodEditorResult(string Name, string TechName);

internal sealed class MinorGodEditorWindow : SimpleWindow
{
    private readonly string _originalTechName;
    private readonly XElement _workingDefinition;
    private readonly bool _isBuiltIn;
    private readonly IReadOnlyList<string> _technologyNames;
    private readonly Func<string, XElement, Task<bool>> _saveAsync;
    private readonly AutoCompleteBox _techBox;
    private readonly AssetPathEditor _portraitEditor;
    private readonly AssetPathEditor _iconEditor;
    private readonly IconPreviewControl _preview;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly EditorSaveFeedback _saveFeedback;
    private readonly CompactEditorLifecycle _lifecycle;
    private readonly (string Tech, string Portrait, string Icon) _initialFields;

    public MinorGodEditorWindow(
        MinorGodDefinition definition,
        IReadOnlyList<string> technologyNames,
        IReadOnlyList<string> iconPaths,
        IconPreviewService iconPreviewService,
        Func<string, XElement, Task<bool>> saveAsync)
    {
        _originalTechName = definition.TechName;
        _workingDefinition = new XElement(definition.SourceElement);
        _isBuiltIn = definition.IsBuiltIn;
        _technologyNames = technologyNames;
        _saveAsync = saveAsync;

        Title = definition.IsBuiltIn ? $"View Minor God: {definition.Name}" : $"Edit Minor God: {definition.Name}";
        Width = 900;
        Height = 390;
        MinWidth = 720;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var root = new Grid
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };
        root.Children.Add(new TextBlock
        {
            Text = definition.Name,
            FontSize = 20,
            FontWeight = FontWeight.Bold
        });

        var container = new Border
        {
            BorderBrush = Brush.Parse("#4C402D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Background = Brush.Parse("#191C1A"),
            Padding = new Thickness(14)
        };
        var content = new StackPanel();
        _techBox = new AutoCompleteBox
        {
            Width = 260,
            ItemsSource = technologyNames,
            Text = definition.TechName,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = !definition.IsBuiltIn
        };
        EditorTextFieldStyle.ConfigureSelector(_techBox, EditorFieldWidthRole.Reference);
        _techBox.MinHeight = _techBox.Height = _techBox.MaxHeight = EditorFormLayout.FieldHeight;
        EditorAutoCompleteService.EnableDropdown(_techBox, () => false);
        content.Children.Add(new WrapPanel
        {
            Children = { EditorFormLayout.LabeledGroup("Minor god Tech", _techBox) }
        });

        var imageRow = new WrapPanel
        {
            Margin = new Thickness(0, EditorFormLayout.SectionGap - EditorFormLayout.LabelGap, 0, 0)
        };
        _portraitEditor = CreateImageEditor(
            MinorGodCatalog.ReadAttribute(_workingDefinition, MinorGodCatalog.PortraitAttribute), iconPaths);
        imageRow.Children.Add(EditorFormLayout.LabeledGroup("Portrait", _portraitEditor));
        _iconEditor = CreateImageEditor(
            MinorGodCatalog.ReadAttribute(_workingDefinition, MinorGodCatalog.IconAttribute), iconPaths);
        imageRow.Children.Add(EditorFormLayout.LabeledGroup("Icon", _iconEditor));
        content.Children.Add(imageRow);

        _preview = new IconPreviewControl(iconPreviewService)
        {
            Margin = new Thickness(0, 0, 0, EditorFormLayout.LabelGap),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        imageRow.Children.Add(_preview);
        container.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
        Grid.SetRow(container, 1);
        root.Children.Add(container);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var close = new Button { Content = definition.IsBuiltIn ? "Close" : "Cancel", MinWidth = 90 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        if (!definition.IsBuiltIn)
        {
            var save = new Button { Content = "Save", Classes = { "positive" }, MinWidth = 90 };
            save.Click += async (_, _) => await SaveAsync();
            buttons.Children.Add(save);
        }
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var saveStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _saveFeedback = new EditorSaveFeedback(saveStatus);
        _saveFeedback.Register(this);
        footer.Children.Add(saveStatus);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        _initialFields = (_techBox.Text ?? "", _portraitEditor.FullValue, _iconEditor.FullValue);
        _lifecycle = new CompactEditorLifecycle(this, HasUnsavedChanges, SaveAsync);
        Opened += (_, _) => RefreshPreview();
    }

    private bool HasUnsavedChanges()
        => !_isBuiltIn && ((_techBox.Text ?? "", DraftPath(_portraitEditor), DraftPath(_iconEditor)) != _initialFields);

    private static string DraftPath(AssetPathEditor editor)
        => editor.Editor.IsVisible ? editor.Editor.Text?.Trim() ?? "" : editor.FullValue;

    private AssetPathEditor CreateImageEditor(string value, IReadOnlyList<string> iconPaths)
    {
        var editor = new AssetPathEditor
        {
            Width = 250,
            IsEnabled = !_isBuiltIn,
            RequireSuggestionMatch = true
        };
        EditorFormLayout.ApplyWidth(editor, EditorFieldWidthRole.AssetPath);
        EditorFieldInteraction.Configure(editor, EditorFieldWidthRole.AssetPath);
        editor.Configure(ProtoEditorWindow.NormalizeIconCatalogValue(value, iconPaths), iconPaths, _ =>
        {
            RefreshPreview();
            return Task.CompletedTask;
        });
        return editor;
    }

    private void RefreshPreview()
    {
        if (_preview == null) return;
        _ = _preview.ShowOptionsAsync([
            (Path: _portraitEditor.FullValue, Culture: (string?)"Portrait"),
            (Path: _iconEditor.FullValue, Culture: (string?)"Icon")
        ]);
    }

    private async Task SaveAsync()
    {
        if (_isBuiltIn || !_lifecycle.CanCommit(_lifecycle.CloseVersion) || !await _saveGate.WaitAsync(0)) return;
        var closeVersion = _lifecycle.CloseVersion;
        using var feedback = _saveFeedback.BeginSave();
        try
        {
            _portraitEditor.CommitEditing();
            _iconEditor.CommitEditing();
            var requestedTech = _techBox.Text?.Trim() ?? "";
            var technology = _technologyNames.FirstOrDefault(name =>
                name.Equals(requestedTech, StringComparison.OrdinalIgnoreCase));
            if (technology == null)
            {
                var failure = EditorValidationNavigation.Mark(this, _techBox,
                    () => _technologyNames.Contains(_techBox.Text?.Trim() ?? "", StringComparer.OrdinalIgnoreCase));
                await new Prompt(PromptType.Error, "Invalid Minor god Tech",
                    "Choose an existing technology from the list.").ShowDialog(this);
                await failure.RevealAsync();
                return;
            }

            _workingDefinition.Name = technology;
            MinorGodCatalog.SetAttribute(_workingDefinition, MinorGodCatalog.PortraitAttribute, _portraitEditor.FullValue);
            MinorGodCatalog.SetAttribute(_workingDefinition, MinorGodCatalog.IconAttribute, _iconEditor.FullValue);
            if (!_lifecycle.CanCommit(closeVersion))
            { _saveFeedback.Report(EditorSaveOutcome.Cancelled); return; }
            using var commit = _lifecycle.BeginCommit();
            if (!await _saveAsync(_originalTechName, new XElement(_workingDefinition))) return;
            _saveFeedback.Report(EditorSaveOutcome.Saved);
            feedback.Complete();
            var feedbackOwner = Owner;
            _lifecycle.CloseAfterSave(() => Close(new MinorGodEditorResult(MinorGodCatalog.GetDisplayName(technology), technology)));
            if (feedbackOwner is Window owner) EditorSaveFeedback.Publish(owner, EditorSaveOutcome.Saved);
        }
        finally
        {
            _saveGate.Release();
        }
    }

}
