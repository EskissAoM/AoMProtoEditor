using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView : UserControl
{
    private readonly IReadOnlyList<XDocument> _originalBarDocuments = [];
    private readonly string? _baseGameplayDirectory;
    private readonly string? _modTechtreePath;
    private readonly Func<string, Task<string?>>? _resolveStringAsync;
    private readonly Func<IReadOnlyDictionary<string, string>, Task>? _saveStringsAsync;
    private readonly Dictionary<string, string> _pendingStringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<string> _iconPaths = [];
    private readonly Dictionary<string, XElement> _original = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XElement> _modified = new(StringComparer.OrdinalIgnoreCase);
    private XDocument _modDocument = new(new XElement("techtreemods"));
    private XElement? _current;
    private string? _currentOriginalName;
    private bool _loadingUi;
    private bool _dirty;
    private bool _controlsReady;
    private bool _isXmlPreviewCollapsed;
    private GridLength _expandedXmlPreviewWidth = new(3, GridUnitType.Star);

    public TechnologyEditorView()
    {
        InitializeComponent();
        _controlsReady = true;
    }

    public TechnologyEditorView(
        IEnumerable<XDocument>? originalBarDocuments,
        string? baseGameplayDirectory,
        string? modTechtreePath,
        Func<string, Task<string?>>? resolveStringAsync = null,
        Func<IReadOnlyDictionary<string, string>, Task>? saveStringsAsync = null,
        IEnumerable<string>? iconPaths = null)
        : this()
    {
        _originalBarDocuments = originalBarDocuments?.ToList() ?? [];
        _baseGameplayDirectory = baseGameplayDirectory;
        _modTechtreePath = modTechtreePath;
        _resolveStringAsync = resolveStringAsync;
        _saveStringsAsync = saveStringsAsync;
        _iconPaths = iconPaths?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        _techTabs.SelectedIndex = 0;
        LoadAll();
        RefreshList();
    }

    private bool IsModifiedTab => _techTabs.SelectedIndex == 1;

    private void LoadAll()
    {
        LoadOriginalFromLooseFiles();
        LoadOriginalFromBarDocuments();
        LoadModified();
    }

    private void LoadOriginalFromLooseFiles()
    {
        if (string.IsNullOrWhiteSpace(_baseGameplayDirectory)) return;
        foreach (var name in new[] { "techtree.xml", "aotg_techtree.techtree" })
        {
            var path = Path.Combine(_baseGameplayDirectory, name);
            if (!File.Exists(path)) continue;
            try { MergeTechs(XDocument.Load(path, LoadOptions.PreserveWhitespace), _original, overwrite: false); } catch { }
        }
    }

    private void LoadOriginalFromBarDocuments()
    {
        foreach (var document in _originalBarDocuments)
            MergeTechs(document, _original, overwrite: false);
    }

    private void LoadModified()
    {
        if (!string.IsNullOrWhiteSpace(_modTechtreePath) && File.Exists(_modTechtreePath))
        {
            try { _modDocument = XDocument.Load(_modTechtreePath, LoadOptions.PreserveWhitespace); }
            catch { _modDocument = new XDocument(new XElement("techtreemods")); }
        }
        else
        {
            _modDocument = new XDocument(new XElement("techtreemods"));
        }
        if (_modDocument.Root == null) _modDocument.Add(new XElement("techtreemods"));
        MergeTechs(_modDocument, _modified, overwrite: true, clone: false);
    }

    private static void MergeTechs(XDocument doc, Dictionary<string, XElement> destination, bool overwrite, bool clone = true)
    {
        foreach (var tech in doc.Descendants().Where(e => e.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)))
        {
            var name = (string?)tech.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (overwrite || !destination.ContainsKey(name)) destination[name] = clone ? new XElement(tech) : tech;
        }
    }

    private void RefreshList(string? select = null)
    {
        var source = IsModifiedTab ? _modified : _original;
        var query = (_searchBox.Text ?? "").Trim();
        var names = source.Keys.Where(n => query.Length == 0 || n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _techList.ItemsSource = names;
        if (!string.IsNullOrWhiteSpace(select)) _techList.SelectedItem = names.FirstOrDefault(n => n.Equals(select, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_controlsReady) return;
        RefreshList();
    }

    private void TechTab_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_controlsReady) return;
        ClearEditor();
        RefreshList();
    }

    private void TechList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_controlsReady) return;
        if (_techList.SelectedItem is not string name) { ClearEditor(); return; }
        var source = IsModifiedTab ? _modified : _original;
        if (!source.TryGetValue(name, out var tech)) { ClearEditor(); return; }
        _current = tech;
        _currentOriginalName = name;
        _ = BuildEditorAsync();
    }

    private void ClearEditor()
    {
        _loadingUi = true;
        _current = null;
        _currentOriginalName = null;
        _techNameBox.Text = "";
        _propertiesPanel.Children.Clear();
        _effectsPanel.Children.Clear();
        _xmlPreview.Text = "";
        _loadingUi = false;
    }

    private async Task BuildEditorAsync()
    {
        if (_current == null) return;
        var tech = _current;
        _loadingUi = true;
        _propertiesPanel.Children.Clear();
        _effectsPanel.Children.Clear();
        _techNameBox.Text = (string?)tech.Attribute("name") ?? "";
        _techNameBox.IsReadOnly = !IsModifiedTab;
        _techNameBox.IsEnabled = IsModifiedTab;

        AddSectionHeader("Properties");
        await AddKnownPropertyEditorsAsync(tech);

        foreach (var attr in tech.Attributes().Where(a =>
                     !a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                     !a.Name.LocalName.Equals("orderhint", StringComparison.OrdinalIgnoreCase)).ToList())
            AddTextPropertyRow(HumanizeLabel(attr.Name.LocalName), attr.Value, v => attr.Value = v);

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "displaynameid", "rollovertextid", "advancedrollovertextoverrideid",
            "cost", "researchpoints", "status", "techtype", "icon", "flag", "effects", "prereqs"
        };

        foreach (var child in tech.Elements().Where(e => !e.HasElements && !handled.Contains(e.Name.LocalName)).ToList())
        {
            string suffix = string.Join(", ", child.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
            string label = HumanizeLabel(child.Name.LocalName) + (suffix.Length > 0 ? $" [{suffix}]" : "");
            AddTextPropertyRow(label, child.Value, v => child.Value = v);
        }

        AddCostsEditor(tech);
        AddChipListEditor(tech, "techtype", "Technology Types");
        AddChipListEditor(tech, "flag", "Flags");

        var effectsContainer = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
        var effects = effectsContainer?.Elements().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList()
                      ?? tech.Elements().Where(e => e.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)).ToList();
        if (effects.Count == 0)
            _effectsPanel.Children.Add(new TextBlock { Text = "No effects.", Foreground = Brushes.Gray });
        foreach (var effect in effects) AddEffectEditor(effect);

        ApplyReadOnlyVisualState();
        UpdatePreview();
        _loadingUi = false;
    }

    private async Task AddKnownPropertyEditorsAsync(XElement tech)
    {
        var displayName = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase));
        if (displayName != null)
            await AddDisplayNameAndOrderHintRowAsync(displayName, tech.Attribute("orderhint"));
        else if (tech.Attribute("orderhint") is XAttribute orderHintOnly)
            AddCompactNumericPropertyRow("Order hint", orderHintOnly.Value, value => orderHintOnly.Value = value, width: 50);

        var rollover = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("rollovertextid", StringComparison.OrdinalIgnoreCase));
        if (rollover != null)
            await AddStringBackedPropertyRowAsync("Rollover text", rollover, multiline: true);

        var advancedRollover = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("advancedrollovertextoverrideid", StringComparison.OrdinalIgnoreCase));
        if (advancedRollover != null)
            await AddStringBackedPropertyRowAsync("Advanced rollover text override", advancedRollover, multiline: true);

        var icon = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
        if (icon != null)
            AddIconEditor(icon);

        var status = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("status", StringComparison.OrdinalIgnoreCase));
        if (status != null)
            AddStatusEditor(status);

        var researchPoints = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("researchpoints", StringComparison.OrdinalIgnoreCase));
        if (researchPoints != null)
            AddCompactNumericPropertyRow("Research points", researchPoints.Value, value => researchPoints.Value = value);

    }

    private async Task AddDisplayNameAndOrderHintRowAsync(XElement displayNameElement, XAttribute? orderHintAttribute)
    {
        var stringId = displayNameElement.Value.Trim();
        var text = stringId;
        if (_resolveStringAsync != null && !string.IsNullOrWhiteSpace(stringId))
            text = await _resolveStringAsync(stringId) ?? stringId;

        var grid = CreatePropertyGrid("Display name");
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var displayBox = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 12, 4)
        });
        displayBox.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(stringId)) return;
            _pendingStringUpdates[stringId] = displayBox.Text ?? "";
            MarkDirty();
        };
        row.Children.Add(displayBox);

        if (orderHintAttribute != null)
        {
            row.Children.Add(new TextBlock
            {
                Text = "Order hint",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            });
            var orderBox = CreateNumericTextBox(orderHintAttribute.Value, 50);
            orderBox.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                orderHintAttribute.Value = orderBox.Text ?? "";
                MarkDirty();
                UpdatePreview();
            };
            row.Children.Add(orderBox);
        }

        Grid.SetColumn(row, 1);
        grid.Children.Add(row);
        _propertiesPanel.Children.Add(grid);
    }

    private async Task AddStringBackedPropertyRowAsync(string label, XElement element, bool multiline = false)
    {
        var stringId = element.Value.Trim();
        var text = stringId;
        if (_resolveStringAsync != null && !string.IsNullOrWhiteSpace(stringId))
            text = await _resolveStringAsync(stringId) ?? stringId;

        var grid = CreatePropertyGrid(label);
        var box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
        {
            Text = text,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (multiline)
        {
            box.MinHeight = 54;
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
        }
        Grid.SetColumn(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || string.IsNullOrWhiteSpace(stringId)) return;
            _pendingStringUpdates[stringId] = box.Text ?? "";
            MarkDirty();
        };
        grid.Children.Add(box);
        _propertiesPanel.Children.Add(grid);
    }

    private TextBox CreateNumericTextBox(string value, double? width = null)
    {
        var box = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox
        {
            Text = value,
            IsEnabled = IsModifiedTab,
            Margin = new Thickness(0, 4, 0, 4)
        });
        if (width.HasValue)
        {
            box.Width = width.Value;
            box.MaxWidth = width.Value;
        }
        return box;
    }

    private void AddCompactNumericPropertyRow(string label, string value, Action<string> setter, double? width = null)
    {
        var grid = CreatePropertyGrid(label);
        var box = CreateNumericTextBox(value, width);
        Grid.SetColumn(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            setter(box.Text ?? "");
            MarkDirty();
            UpdatePreview();
        };
        grid.Children.Add(box);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddCostsEditor(XElement tech)
    {
        AddSectionHeader("Costs");
        var costs = tech.Elements().Where(e => e.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Attribute("resourcetype") != null)
            .GroupBy(e => (string?)e.Attribute("resourcetype") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var index = Array.IndexOf(ProtoConstants.KnownResourceTypes, resource);
            var label = new TextBlock
            {
                Text = resource,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 10, 4)
            };
            Grid.SetColumn(label, index * 2);
            grid.Children.Add(label);

            costs.TryGetValue(resource, out var existing);
            var box = CreateNumericTextBox(existing?.Value ?? "0");
            box.Margin = new Thickness(0, 4, index < ProtoConstants.KnownResourceTypes.Length - 1 ? 16 : 0, 4);
            Grid.SetColumn(box, index * 2 + 1);
            box.TextChanged += (_, _) =>
            {
                if (_loadingUi || !IsModifiedTab) return;
                if (existing == null)
                {
                    existing = new XElement("cost", new XAttribute("resourcetype", resource), box.Text ?? "0");
                    InsertBeforeEffectsOrAppend(tech, existing);
                }
                else
                {
                    existing.Value = box.Text ?? "0";
                }
                MarkDirty();
                UpdatePreview();
            };
            grid.Children.Add(box);
        }
        _propertiesPanel.Children.Add(grid);
    }

    private void AddStatusEditor(XElement status)
    {
        var grid = CreatePropertyGrid("Status");
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Obtainable", "Unobtainable", "Active" },
            SelectedItem = ToStatusDisplay(status.Value),
            IsEnabled = IsModifiedTab,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetColumn(combo, 1);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab || combo.SelectedItem is not string value) return;
            status.Value = value.ToUpperInvariant();
            MarkDirty();
            UpdatePreview();
        };
        grid.Children.Add(combo);
        _propertiesPanel.Children.Add(grid);
    }

    private static string ToStatusDisplay(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "OBTAINABLE" => "Obtainable",
            "ACTIVE" => "Active",
            _ => "Unobtainable"
        };

    private void AddIconEditor(XElement icon)
    {
        var grid = CreatePropertyGrid("Icon");
        var initial = ProtoEditorWindow.NormalizeIconCatalogValue(icon.Value, _iconPaths);
        var editor = new AssetPathEditor
        {
            IsEnabled = IsModifiedTab,
            Opacity = IsModifiedTab ? 1.0 : 0.55,
            Margin = new Thickness(0, 4, 0, 4)
        };
        editor.CompactPresenter.Background = Brush.Parse(IsModifiedTab ? "#1b1b1b" : "#4a4a4a");
        editor.Configure(initial, _iconPaths, async value =>
        {
            if (!IsModifiedTab) return;
            icon.Value = value;
            MarkDirty();
            UpdatePreview();
            await Task.CompletedTask;
        });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        _propertiesPanel.Children.Add(grid);
    }

    private void AddChipListEditor(XElement tech, string tag, string sectionTitle)
    {
        AddSectionHeader(sectionTitle);
        var content = new StackPanel { Spacing = 4 };
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        content.Children.Add(chips);

        var knownValues = _original.Values.Concat(_modified.Values)
            .SelectMany(t => t.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Render()
        {
            chips.Children.Clear();
            foreach (var element in tech.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var captured = element;
                chips.Children.Add(EditorChipService.CreateBlueChip(
                    captured.Value.Trim(),
                    IsModifiedTab ? () =>
                    {
                        captured.Remove();
                        MarkDirty();
                        Render();
                        UpdatePreview();
                    } : null,
                    readOnly: !IsModifiedTab));
            }
        }

        Render();
        if (IsModifiedTab)
        {
            var picker = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox
            {
                ItemsSource = knownValues,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not string value || string.IsNullOrWhiteSpace(value)) return;
                if (!tech.Elements().Any(e => e.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase) && e.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
                {
                    var element = new XElement(tag, value);
                    InsertBeforeEffectsOrAppend(tech, element);
                    MarkDirty();
                    Render();
                    UpdatePreview();
                }
                picker.SelectedItem = null;
                picker.Text = "";
            };
            content.Children.Add(picker);
        }

        _propertiesPanel.Children.Add(content);
    }

    private void AddTextPropertyRow(string label, string value, Action<string> setter)
    {
        var grid = CreatePropertyGrid(label);
        TextBox box;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            box = CreateNumericTextBox(value);
        }
        else
        {
            box = EditorTextFieldStyle.ConfigureTextBox(new TextBox
            {
                Text = value,
                IsEnabled = IsModifiedTab,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        Grid.SetColumn(box, 1);
        box.TextChanged += (_, _) =>
        {
            if (_loadingUi || !IsModifiedTab) return;
            setter(box.Text ?? "");
            MarkDirty();
            UpdatePreview();
        };
        grid.Children.Add(box);
        _propertiesPanel.Children.Add(grid);
    }

    private static Grid CreatePropertyGrid(string label)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 6, 4) });
        return grid;
    }

    private void AddSectionHeader(string text)
    {
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = $"──── {text} ────",
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = Brush.Parse("#5ba8de"),
            Margin = new Thickness(0, 15, 0, 5)
        });
    }

    private void ApplyReadOnlyVisualState()
    {
        var canEdit = IsModifiedTab;
        _propertiesPanel.IsEnabled = canEdit;
        _effectsPanel.IsEnabled = canEdit;
        _propertiesPanel.Opacity = canEdit ? 1.0 : 0.55;
        _effectsPanel.Opacity = canEdit ? 1.0 : 0.55;
        _xmlPreview.IsEnabled = _current != null;
        _xmlPreview.IsReadOnly = true;
        _xmlPreview.Focusable = canEdit;
        _xmlPreview.IsTabStop = canEdit;
        _xmlPreview.Opacity = canEdit ? 1.0 : 0.55;
        _xmlPreview.Background = Brush.Parse(canEdit ? "#101010" : "#080808");
        _xmlPreview.Foreground = Brush.Parse(canEdit ? "#d9d9d9" : "#8a8a8a");
    }

    private static void InsertBeforeEffectsOrAppend(XElement tech, XElement element)
    {
        var effects = tech.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase));
        if (effects != null) effects.AddBeforeSelf(element);
        else tech.Add(element);
    }

    private void AddEffectEditor(XElement effect)
    {
        var box = new TextBox
        {
            Text = effect.ToString(SaveOptions.DisableFormatting),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 42,
            IsReadOnly = !IsModifiedTab
        };
        box.LostFocus += (_, _) =>
        {
            if (!IsModifiedTab) return;
            try
            {
                var parsed = XElement.Parse(box.Text ?? "");
                if (!parsed.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Root must be <effect>.");
                effect.ReplaceWith(parsed);
                effect = parsed;
                MarkDirty();
                UpdatePreview();
                _statusMessage.Text = "";
            }
            catch (Exception ex) { _statusMessage.Text = "Invalid effect XML: " + ex.Message; }
        };
        _effectsPanel.Children.Add(box);
    }

    private void TechNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_controlsReady || _loadingUi || !IsModifiedTab || _current == null || _currentOriginalName == null) return;
        var newName = (_techNameBox.Text ?? "").Trim();
        if (newName.Length == 0 || (!newName.Equals(_currentOriginalName, StringComparison.OrdinalIgnoreCase) && _modified.ContainsKey(newName))) return;
        _current.SetAttributeValue("name", newName);
        _modified.Remove(_currentOriginalName);
        _modified[newName] = _current;
        _currentOriginalName = newName;
        MarkDirty();
        UpdatePreview();
    }

    private void AddTech_Click(object? sender, RoutedEventArgs e)
    {
        XElement tech;
        string baseName;
        if (_techList.SelectedItem is string selected && (IsModifiedTab ? _modified : _original).TryGetValue(selected, out var source))
        { tech = new XElement(source); baseName = selected + "Copy"; }
        else { baseName = "NewTechnology"; tech = new XElement("tech", new XAttribute("name", baseName)); }
        string name = baseName;
        int i = 2;
        while (_modified.ContainsKey(name) || _original.ContainsKey(name)) name = baseName + i++;
        tech.SetAttributeValue("name", name);
        _modDocument.Root!.Add(tech);
        _modified[name] = tech;
        _techTabs.SelectedIndex = 1;
        MarkDirty();
        RefreshList(name);
    }

    private void DeleteTech_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsModifiedTab || _techList.SelectedItem is not string name || !_modified.TryGetValue(name, out var tech)) return;
        tech.Remove();
        _modified.Remove(name);
        MarkDirty();
        ClearEditor();
        RefreshList();
    }

    private void XmlPreviewToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isXmlPreviewCollapsed = !_isXmlPreviewCollapsed;
        _xmlPreviewContent.IsVisible = !_isXmlPreviewCollapsed;
        _previewSplitter.IsVisible = !_isXmlPreviewCollapsed;
        _mainGrid.ColumnDefinitions[3].Width = new GridLength(_isXmlPreviewCollapsed ? 0 : 5);
        _mainGrid.ColumnDefinitions[4].Width = new GridLength(
            _isXmlPreviewCollapsed ? 28 : _expandedXmlPreviewWidth.Value,
            _isXmlPreviewCollapsed ? GridUnitType.Pixel : _expandedXmlPreviewWidth.GridUnitType);

        _xmlPreviewToggleButton.Content = _isXmlPreviewCollapsed ? "◀" : "▶";
        ToolTip.SetTip(_xmlPreviewToggleButton, _isXmlPreviewCollapsed ? "Restore XML Preview" : "Collapse XML Preview");
    }

    internal static string HumanizeLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var normalized = raw.TrimStart('@');
        var known = normalized.ToLowerInvariant() switch
        {
            "displaynameid" => "Display name",
            "rollovertextid" => "Rollover text",
            "advancedrollovertextoverrideid" => "Advanced rollover text override",
            "valuetext" => "Value text",
            "researchpoints" => "Research points",
            "researchlimit" => "Research limit",
            "techtype" => "Tech type",
            "orderhint" => "Order hint",
            "initialdelay" => "Initial delay",
            "techtage" => "Tech age",
            "combatxptier" => "Combat XP tier",
            "devotioncost" => "Devotion cost",
            _ => ""
        };
        if (known.Length > 0) return known;

        var value = normalized.Replace('_', ' ');
        value = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
        value = Regex.Replace(value, "\\s+", " ").Trim().ToLowerInvariant();
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    public bool IsDirty => _dirty;

    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(_modTechtreePath))
        {
            _statusMessage.Text = "No active mod is loaded.";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_modTechtreePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            _modDocument.Save(_modTechtreePath);
            if (_saveStringsAsync != null && _pendingStringUpdates.Count > 0)
                _saveStringsAsync(_pendingStringUpdates).GetAwaiter().GetResult();
            _pendingStringUpdates.Clear();
            _dirty = false;
            _statusMessage.Text = "Saved successfully.";
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage.Text = "Save failed: " + ex.Message;
            return false;
        }
    }

    private void MarkDirty() { _dirty = true; _statusMessage.Text = "Modified"; }
    private void UpdatePreview()
    {
        if (_current == null)
        {
            _xmlPreview.Text = "";
            return;
        }

        // Reparse a compact copy for preview only so source whitespace cannot
        // leave the closing </tech> over-indented. The backing XML is untouched.
        _xmlPreview.Text = XElement.Parse(
            _current.ToString(SaveOptions.DisableFormatting),
            LoadOptions.None).ToString();
    }
}
