using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed class CultureEditorWindow : SimpleWindow
{
    private readonly string _originalName;
    private readonly XElement _workingDefinition;
    private readonly bool _isBuiltIn;
    private readonly Func<string, Task<string?>> _resolveStringAsync;
    private readonly Func<string, XElement, string?, Task<bool>> _saveAsync;
    private readonly TextBox _displayNameBox;
    private readonly TextBox _topicBox;
    private readonly TextBox _imageBox;
    private readonly AutoCompleteBox _replayPanelBackgroundBox;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private string _loadedDisplayName = "";

    public CultureEditorWindow(
        CultureDefinition definition,
        IReadOnlyList<string> replayPanelBackgrounds,
        Func<string, Task<string?>> resolveStringAsync,
        Func<string, XElement, string?, Task<bool>> saveAsync)
    {
        _originalName = definition.Name;
        _workingDefinition = new XElement(definition.SourceElement);
        _isBuiltIn = definition.IsBuiltIn;
        _resolveStringAsync = resolveStringAsync;
        _saveAsync = saveAsync;

        Title = definition.IsBuiltIn ? $"View Culture: {definition.Name}" : $"Edit Culture: {definition.Name}";
        Width = 820;
        Height = 330;
        MinWidth = 680;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var root = new Grid
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10
        };

        root.Children.Add(new TextBlock
        {
            Text = definition.Name,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var container = new Border
        {
            BorderBrush = Brush.Parse("#4C402D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Background = Brush.Parse("#191C1A"),
            Padding = new Thickness(14)
        };
        var fields = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 10
        };

        _displayNameBox = CreateTextField(380);
        AddLabeledRow(fields, 0, ("Display name", _displayNameBox));

        _topicBox = CreateTextField(200);
        _topicBox.Text = CultureCatalog.ReadField(_workingDefinition, "compendiumtopic");
        _imageBox = CreateTextField(380);
        _imageBox.Text = CultureCatalog.ReadField(_workingDefinition, "compendiumimage");
        AddLabeledRow(fields, 1,
            ("Compendium", null),
            ("Topic", _topicBox),
            ("Image", _imageBox));

        _replayPanelBackgroundBox = new AutoCompleteBox
        {
            Width = 380,
            MaxWidth = 380,
            ItemsSource = replayPanelBackgrounds,
            Text = CultureCatalog.ReadField(_workingDefinition, "replaypanelbackground"),
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = !definition.IsBuiltIn
        };
        EditorAutoCompleteService.EnableDropdown(_replayPanelBackgroundBox, () => false);
        AddLabeledRow(fields, 2, ("Replay Panel Background", _replayPanelBackgroundBox));
        container.Child = fields;
        Grid.SetRow(container, 1);
        root.Children.Add(container);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
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

        foreach (var field in new[] { _displayNameBox, _topicBox, _imageBox })
            field.IsEnabled = !definition.IsBuiltIn;
        Content = root;
        Opened += async (_, _) => await LoadDisplayNameAsync();
    }

    private async Task LoadDisplayNameAsync()
    {
        var id = CultureCatalog.ReadField(_workingDefinition, "displaynameid");
        _loadedDisplayName = id.Length == 0 ? "" : await _resolveStringAsync(id) ?? "";
        _displayNameBox.Text = _loadedDisplayName;
    }

    private async Task SaveAsync()
    {
        if (_isBuiltIn || !await _saveGate.WaitAsync(0)) return;
        try
        {
            CultureCatalog.SetField(_workingDefinition, "compendiumtopic", _topicBox.Text);
            CultureCatalog.SetField(_workingDefinition, "compendiumimage", _imageBox.Text);
            CultureCatalog.SetField(_workingDefinition, "replaypanelbackground", _replayPanelBackgroundBox.Text);
            var id = CultureCatalog.ReadField(_workingDefinition, "displaynameid");
            var createdGeneratedId = id.Length == 0;
            if (createdGeneratedId)
                CultureCatalog.SetField(_workingDefinition, "displaynameid", CultureCatalog.BuildDisplayNameId(_originalName));
            var displayName = _displayNameBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(displayName))
            {
                await new Prompt(PromptType.Error, "Invalid Culture", "Display name is required.").ShowDialog(this);
                return;
            }
            var displayNameUpdate = createdGeneratedId || !displayName.Equals(_loadedDisplayName, StringComparison.Ordinal)
                ? displayName
                : null;
            if (!await _saveAsync(_originalName, new XElement(_workingDefinition), displayNameUpdate))
                return;
            Close(_originalName);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static StackPanel CreateRow() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBox CreateTextField(double width)
    {
        var box = new TextBox { Width = width, MaxWidth = width };
        EditorTextFieldStyle.ConfigureTextBox(box);
        box.Width = box.MaxWidth = width;
        return box;
    }

    private static void AddLabeledRow(Grid root, int rowIndex, params (string Label, Control? Field)[] values)
    {
        var row = CreateRow();
        foreach (var value in values)
        {
            row.Children.Add(new TextBlock
            {
                Text = value.Label,
                FontWeight = value.Field == null ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (value.Field != null) row.Children.Add(value.Field);
        }
        Grid.SetRow(row, rowIndex);
        root.Children.Add(row);
    }
}
