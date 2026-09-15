using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
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
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 12
        };

        var techRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        techRow.Children.Add(new TextBlock
        {
            Text = "Minor god Tech",
            VerticalAlignment = VerticalAlignment.Center
        });
        _techBox = new AutoCompleteBox
        {
            Width = 260,
            ItemsSource = technologyNames,
            Text = definition.TechName,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = !definition.IsBuiltIn
        };
        EditorTextFieldStyle.ConfigureSelector(_techBox);
        EditorAutoCompleteService.EnableDropdown(_techBox, () => false);
        techRow.Children.Add(_techBox);
        Grid.SetColumnSpan(techRow, 2);
        content.Children.Add(techRow);

        var imageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        imageRow.Children.Add(new TextBlock { Text = "Portrait", VerticalAlignment = VerticalAlignment.Center });
        _portraitEditor = CreateImageEditor(
            MinorGodCatalog.ReadAttribute(_workingDefinition, MinorGodCatalog.PortraitAttribute), iconPaths);
        imageRow.Children.Add(_portraitEditor);
        imageRow.Children.Add(new TextBlock { Text = "Icon", VerticalAlignment = VerticalAlignment.Center });
        _iconEditor = CreateImageEditor(
            MinorGodCatalog.ReadAttribute(_workingDefinition, MinorGodCatalog.IconAttribute), iconPaths);
        imageRow.Children.Add(_iconEditor);
        Grid.SetRow(imageRow, 1);
        Grid.SetColumnSpan(imageRow, 2);
        content.Children.Add(imageRow);

        _preview = new IconPreviewControl(iconPreviewService)
        {
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(_preview, 2);
        Grid.SetRowSpan(_preview, 2);
        content.Children.Add(_preview);
        container.Child = content;
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
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => RefreshPreview();
    }

    private AssetPathEditor CreateImageEditor(string value, IReadOnlyList<string> iconPaths)
    {
        var editor = new AssetPathEditor
        {
            Width = 250,
            IsEnabled = !_isBuiltIn,
            RequireSuggestionMatch = true
        };
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
        if (_isBuiltIn || !await _saveGate.WaitAsync(0)) return;
        try
        {
            _portraitEditor.CommitEditing();
            _iconEditor.CommitEditing();
            var requestedTech = _techBox.Text?.Trim() ?? "";
            var technology = _technologyNames.FirstOrDefault(name =>
                name.Equals(requestedTech, StringComparison.OrdinalIgnoreCase));
            if (technology == null)
            {
                await new Prompt(PromptType.Error, "Invalid Minor god Tech",
                    "Choose an existing technology from the list.").ShowDialog(this);
                return;
            }

            _workingDefinition.Name = technology;
            MinorGodCatalog.SetAttribute(_workingDefinition, MinorGodCatalog.PortraitAttribute, _portraitEditor.FullValue);
            MinorGodCatalog.SetAttribute(_workingDefinition, MinorGodCatalog.IconAttribute, _iconEditor.FullValue);
            if (!await _saveAsync(_originalTechName, new XElement(_workingDefinition))) return;
            Close(new MinorGodEditorResult(MinorGodCatalog.GetDisplayName(technology), technology));
        }
        finally
        {
            _saveGate.Release();
        }
    }

}
