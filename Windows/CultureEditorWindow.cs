using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly EditorSaveFeedback _saveFeedback;
    private readonly CompactEditorLifecycle _lifecycle;
    private readonly (string Topic, string Image, string Background) _initialFields;
    private string _loadedDisplayName = "";
    private Task<bool>? _displayNameLoadTask;
    private string? _displayNameLoadError;
    private bool _displayNameEdited;
    private bool _applyingLoadedDisplayName;
    private bool _isClosed;

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
            RowSpacing = EditorFormLayout.GroupGap
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
            RowSpacing = 0
        };

        _displayNameBox = CreateTextField(EditorFieldWidthRole.SingleLineText);
        _displayNameBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && !_applyingLoadedDisplayName)
                _displayNameEdited = true;
        };
        AddLabeledRow(fields, 0, (EditorDisplayText.DisplayName, _displayNameBox));

        _topicBox = CreateTextField(EditorFieldWidthRole.Reference);
        _topicBox.Text = CultureCatalog.ReadField(_workingDefinition, "compendiumtopic");
        _imageBox = CreateTextField(EditorFieldWidthRole.AssetPath);
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
        EditorTextFieldStyle.ConfigureSelector(_replayPanelBackgroundBox, EditorFieldWidthRole.AssetPath);
        _replayPanelBackgroundBox.MinHeight = _replayPanelBackgroundBox.Height = _replayPanelBackgroundBox.MaxHeight = EditorFormLayout.FieldHeight;
        AddLabeledRow(fields, 2, ("Replay Panel Background", _replayPanelBackgroundBox));
        container.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = fields
        };
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
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var saveStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _saveFeedback = new EditorSaveFeedback(saveStatus);
        _saveFeedback.Register(this);
        footer.Children.Add(saveStatus);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        foreach (var field in new[] { _displayNameBox, _topicBox, _imageBox })
            field.IsEnabled = !definition.IsBuiltIn;
        Content = root;
        _initialFields = (_topicBox.Text ?? "", _imageBox.Text ?? "", _replayPanelBackgroundBox.Text ?? "");
        _lifecycle = new CompactEditorLifecycle(this, HasUnsavedChanges, SaveAsync);
        Opened += (_, _) => { _ = EnsureDisplayNameLoadedAsync(); };
        Closed += (_, _) => _isClosed = true;
    }

    private bool HasUnsavedChanges()
        => !_isBuiltIn && ((_topicBox.Text ?? "", _imageBox.Text ?? "", _replayPanelBackgroundBox.Text ?? "") != _initialFields ||
            _displayNameEdited && (_displayNameLoadTask?.IsCompletedSuccessfully != true || !_displayNameLoadTask.Result ||
                !string.Equals(_displayNameBox.Text ?? "", _loadedDisplayName, StringComparison.Ordinal)));

    private Task<bool> EnsureDisplayNameLoadedAsync()
    {
        if (_displayNameLoadTask == null ||
            (_displayNameLoadTask.IsCompletedSuccessfully && !_displayNameLoadTask.Result))
            _displayNameLoadTask = LoadDisplayNameAsync();
        return _displayNameLoadTask;
    }

    private async Task<bool> LoadDisplayNameAsync()
    {
        _displayNameBox.PlaceholderText = "Loading display name…";
        string loaded;
        try
        {
            var id = CultureCatalog.ReadField(_workingDefinition, "displaynameid");
            loaded = id.Length == 0 ? "" : await _resolveStringAsync(id) ?? "";
        }
        catch (Exception exception)
        {
            if (!_isClosed)
            {
                _displayNameLoadError = exception.Message;
                _displayNameBox.PlaceholderText = "Unable to load display name. Save retries.";
            }
            return false;
        }
        if (_isClosed) return false;
        _loadedDisplayName = loaded;
        _displayNameLoadError = null;
        _displayNameBox.PlaceholderText = "";
        // Keep the loaded baseline even when the user has edited or cleared the field.
        if (!_displayNameEdited)
        {
            _applyingLoadedDisplayName = true;
            try { _displayNameBox.Text = loaded; }
            finally { _applyingLoadedDisplayName = false; }
        }
        return true;
    }

    private async Task SaveAsync()
    {
        if (_isBuiltIn || _isClosed || _lifecycle.IsClosePending || !await _saveGate.WaitAsync(0)) return;
        var closeVersion = _lifecycle.CloseVersion;
        using var feedback = _saveFeedback.BeginSave();
        try
        {
            // A pending lookup must establish the baseline before deciding whether
            // to update a potentially shared string ID. Failed loads retry on Save.
            var loaded = await EnsureDisplayNameLoadedAsync();
            if (_isClosed) return;
            if (!_lifecycle.CanCommit(closeVersion))
            { _saveFeedback.Report(EditorSaveOutcome.Cancelled); return; }
            if (!loaded)
            {
                if (!_isClosed)
                    await new Prompt(PromptType.Error, "Unable to load Culture display name",
                        "No changes were saved. Your edits are kept. Use Save to retry loading, or Cancel to close.\n\n" + _displayNameLoadError)
                        .FitToContent(560, 300).ShowDialog(this);
                return;
            }
            if (_isClosed) return;
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
                var failure = EditorValidationNavigation.Mark(this, _displayNameBox,
                    () => !string.IsNullOrWhiteSpace(_displayNameBox.Text));
                await new Prompt(PromptType.Error, "Invalid Culture", "Display name is required.").ShowDialog(this);
                await failure.RevealAsync();
                return;
            }
            var displayNameUpdate = createdGeneratedId || !displayName.Equals(_loadedDisplayName, StringComparison.Ordinal)
                ? displayName
                : null;
            using var commit = _lifecycle.BeginCommit();
            if (!await _saveAsync(_originalName, new XElement(_workingDefinition), displayNameUpdate))
                return;
            _saveFeedback.Report(EditorSaveOutcome.Saved);
            feedback.Complete();
            var feedbackOwner = Owner;
            _lifecycle.CloseAfterSave(() => Close(_originalName));
            if (feedbackOwner is Window owner) EditorSaveFeedback.Publish(owner, EditorSaveOutcome.Saved);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static TextBox CreateTextField(EditorFieldWidthRole role)
    {
        var box = new TextBox();
        EditorTextFieldStyle.ConfigureTextBox(box, role);
        box.MinHeight = box.Height = box.MaxHeight = EditorFormLayout.FieldHeight;
        return box;
    }

    private static void AddLabeledRow(Grid root, int rowIndex, params (string Label, Control? Field)[] values)
    {
        var row = new StackPanel();
        var groups = new WrapPanel();
        foreach (var value in values)
        {
            if (value.Field != null)
            {
                groups.Children.Add(EditorFormLayout.LabeledGroup(value.Label, value.Field));
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = value.Label,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, EditorFormLayout.SectionGap - EditorFormLayout.LabelGap, 0, EditorFormLayout.LabelGap)
                });
            }
        }
        row.Children.Add(groups);
        Grid.SetRow(row, rowIndex);
        root.Children.Add(row);
    }
}
