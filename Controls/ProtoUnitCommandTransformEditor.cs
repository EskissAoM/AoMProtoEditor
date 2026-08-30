using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared Transform-command field editor used by both the standalone command editor
/// and the inline Transform card. Hosts own persistence, validation prompts and card/window actions.
/// </summary>
public sealed class ProtoUnitCommandTransformEditor : StackPanel
{
    private readonly ProtoUnitTransformDefinition _transform;
    private readonly IDictionary<string, string> _values;
    private readonly IReadOnlyList<string> _protoUnitNames;
    private readonly bool _editable;
    private readonly Func<bool>? _isBusy;
    private readonly Action _changed;
    private readonly StackPanel _revertHost;
    private readonly Button _revertAddButton;
    private readonly WrapPanel _optionalHost;
    private readonly IReadOnlyList<string> _techNames;
    private bool _showValidationErrors;

    public AutoCompleteBox FromEditor { get; }
    public AutoCompleteBox ToEditor { get; }
    public AutoCompleteBox PrereqTechEditor { get; }
    public AutoCompleteBox AssociatedTechEditor { get; }
    public CheckBox FullHealEditor { get; }
    public AutoCompleteBox? RevertOthersEditor { get; private set; }
    public Dictionary<string, Control> OptionalValueEditors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ProtoUnitCommandTransformEditor(
        ProtoUnitTransformDefinition transform,
        IDictionary<string, string> values,
        IReadOnlyList<string> protoUnitNames,
        IReadOnlyList<string> techNames,
        IReadOnlyList<string> powerNames,
        bool editable,
        bool lockFrom,
        Action changed,
        Func<bool>? isBusy = null)
    {
        _transform = transform;
        _values = values;
        _protoUnitNames = protoUnitNames;
        _techNames = techNames;
        _editable = editable;
        _changed = changed;
        _isBusy = isBusy;
        Spacing = 4;

        var transformRow = CreateTwoFieldRow();
        AddLabel(transformRow, "Transform", 0);
        FromEditor = CreateSelector(_transform.From ?? string.Empty, protoUnitNames, editable && !lockFrom, value =>
        {
            if (string.Equals(_transform.From, value, StringComparison.Ordinal)) return;
            _transform.From = value;
            _changed();
        });
        AddControl(transformRow, FromEditor, 1);
        AddLabel(transformRow, "To", 2);
        ToEditor = CreateSelector(_transform.To ?? string.Empty, protoUnitNames, editable, value =>
        {
            if (string.Equals(_transform.To, value, StringComparison.Ordinal)) return;
            _transform.To = value;
            _changed();
        });
        AddControl(transformRow, ToEditor, 3);
        Children.Add(transformRow);

        var optionRow = CreateTwoFieldRow();
        AddLabel(optionRow, "Full heal", 0);
        FullHealEditor = new CheckBox
        {
            IsChecked = _transform.FullHeal,
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2)
        };
        FullHealEditor.IsCheckedChanged += (_, _) =>
        {
            if (!editable || Busy()) return;
            var next = FullHealEditor.IsChecked == true;
            if (_transform.FullHeal == next) return;
            _transform.FullHeal = next;
            _changed();
        };
        AddControl(optionRow, FullHealEditor, 1);

        _revertHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2)
        };
        Grid.SetColumn(_revertHost, 2);
        Grid.SetColumnSpan(_revertHost, 2);
        optionRow.Children.Add(_revertHost);
        _revertAddButton = new Button
        {
            Content = "Revert others to",
            Classes = { "add-component" },
            Padding = new Thickness(8, 4),
            IsVisible = editable && string.IsNullOrWhiteSpace(_transform.RevertOthersTo)
        };
        _revertAddButton.Click += (_, _) =>
        {
            ShowRevertEditor(string.Empty);
            _changed();
        };
        _revertHost.Children.Add(_revertAddButton);
        if (!string.IsNullOrWhiteSpace(_transform.RevertOthersTo))
            ShowRevertEditor(_transform.RevertOthersTo);
        Children.Add(optionRow);

        var techRow = CreateTwoFieldRow();
        AddLabel(techRow, "Prereq Tech", 0);
        PrereqTechEditor = CreateSelector(_transform.Tech ?? string.Empty, techNames, editable, value =>
        {
            if (string.Equals(_transform.Tech, value, StringComparison.Ordinal)) return;
            _transform.Tech = value;
            _changed();
        });
        AddControl(techRow, PrereqTechEditor, 1);
        AddLabel(techRow, "Associated Tech", 2);
        AssociatedTechEditor = CreateSelector(GetValue("associatedtech"), techNames, editable, value => SetValue("associatedtech", value));
        AddControl(techRow, AssociatedTechEditor, 3);
        Children.Add(techRow);

        _optionalHost = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(115, 2, 0, 2)
        };
        AddOptionalValue("forbidtech", "Forbid Tech", "Add Forbid Tech", techNames);
        AddOptionalValue("associatedpower", "Power", "Associated Power", powerNames);
        Children.Add(_optionalHost);
    }

    private static Grid CreateTwoFieldRow() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("115,240,Auto,240,*"),
        ColumnSpacing = 8,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 2)
    };

    private static void AddLabel(Grid row, string text, int column)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, column);
        row.Children.Add(label);
    }

    private static void AddControl(Grid row, Control control, int column)
    {
        Grid.SetColumn(control, column);
        row.Children.Add(control);
    }

    private AutoCompleteBox CreateSelector(string value, IReadOnlyList<string> suggestions, bool enabled, Action<string> commit)
    {
        var editor = ProtoUnitCommandFieldFactory.CreateStrictSelector(value, suggestions, enabled, 240, commit, _isBusy);
        editor.TextChanged += (_, _) =>
        {
            if (!enabled || Busy()) return;
            commit(editor.Text?.Trim() ?? string.Empty);
            if (_showValidationErrors)
                ApplyValidation(ProtoUnitCommandTransformRules.ValidateRequired(_transform, _values, _protoUnitNames, _techNames));
        };
        return editor;
    }

    public ProtoUnitCommandTransformValidation ValidateRequired(string? expectedFrom = null, bool showErrors = true)
    {
        var result = ProtoUnitCommandTransformRules.ValidateRequired(_transform, _values, _protoUnitNames, _techNames, expectedFrom);
        _showValidationErrors = showErrors;
        if (showErrors)
            ApplyValidation(result);
        return result;
    }

    public void ClearValidation()
    {
        _showValidationErrors = false;
        ApplyValidation(new ProtoUnitCommandTransformValidation(true, true, true, true));
    }

    private void ApplyValidation(ProtoUnitCommandTransformValidation result)
    {
        SetValidationBorder(FromEditor, !result.FromValid);
        SetValidationBorder(ToEditor, !result.ToValid);
        SetValidationBorder(PrereqTechEditor, !result.PrereqTechValid);
        SetValidationBorder(AssociatedTechEditor, !result.AssociatedTechValid);
    }

    private static void SetValidationBorder(Control control, bool invalid)
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

    private void ShowRevertEditor(string value)
    {
        _revertHost.Children.Clear();
        var label = new TextBlock { Text = "Revert others to", VerticalAlignment = VerticalAlignment.Center };
        _revertHost.Children.Add(label);
        RevertOthersEditor = CreateSelector(value, _protoUnitNames, _editable, committed =>
        {
            if (string.Equals(_transform.RevertOthersTo, committed, StringComparison.Ordinal)) return;
            _transform.RevertOthersTo = committed;
            _changed();
        });
        _revertHost.Children.Add(RevertOthersEditor);
        if (_editable)
        {
            var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(2, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                _transform.RevertOthersTo = string.Empty;
                RevertOthersEditor = null;
                _revertHost.Children.Clear();
                _revertHost.Children.Add(_revertAddButton);
                _revertAddButton.IsVisible = true;
                _changed();
            };
            _revertHost.Children.Add(remove);
        }
    }

    private void AddOptionalValue(string tag, string labelText, string addText, IReadOnlyList<string> suggestions)
    {
        var initialValue = GetValue(tag);
        var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var editor = CreateSelector(initialValue, suggestions, _editable, value => SetValue(tag, value));
        var remove = new Button { Classes = { "remove-button" }, Margin = new Thickness(2, 0, 8, 0) };
        var add = new Button
        {
            Content = addText,
            Classes = { "add-component" },
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 8, 0)
        };

        void SetVisible(bool visible)
        {
            label.IsVisible = visible;
            editor.IsVisible = visible;
            remove.IsVisible = _editable && visible;
            add.IsVisible = _editable && !visible;
        }

        SetVisible(!string.IsNullOrWhiteSpace(initialValue));
        add.Click += (_, _) =>
        {
            SetVisible(true);
            _changed();
        };
        remove.Click += (_, _) =>
        {
            _values.Remove(tag);
            editor.Text = string.Empty;
            SetVisible(false);
            _changed();
        };

        OptionalValueEditors[tag] = editor;
        _optionalHost.Children.Add(label);
        _optionalHost.Children.Add(editor);
        _optionalHost.Children.Add(remove);
        _optionalHost.Children.Add(add);
    }

    private string GetValue(string tag) => _values.TryGetValue(tag, out var value) ? value ?? string.Empty : string.Empty;

    private void SetValue(string tag, string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (string.Equals(GetValue(tag), value, StringComparison.Ordinal)) return;
        if (string.IsNullOrWhiteSpace(value))
            _values.Remove(tag);
        else
            _values[tag] = value;
        _changed();
    }

    private bool Busy() => _isBusy?.Invoke() == true;
}


/// <summary>
/// Shared flags editor for ProtoUnit commands. Transform hosts provide the current
/// transform kind so the structural Transform/TransformSelected chip cannot be removed.
/// </summary>
public sealed class ProtoUnitCommandFlagsEditor : Grid
{
    private readonly ISet<string> _flags;
    private readonly bool _editable;
    private readonly Func<ProtoUnitCommandTransformKind?> _transformKind;
    private readonly Action _changed;
    private readonly Func<bool>? _isBusy;
    private readonly Action<string>? _flagAdded;
    private readonly AutoCompleteBox? _picker;
    private readonly StackPanel _chipRows;

    public ProtoUnitCommandFlagsEditor(
        ISet<string> flags,
        bool editable,
        Func<ProtoUnitCommandTransformKind?> transformKind,
        Action changed,
        Func<bool>? isBusy = null,
        Action<string>? flagAdded = null)
    {
        _flags = flags;
        _editable = editable;
        _transformKind = transformKind;
        _changed = changed;
        _isBusy = isBusy;
        _flagAdded = flagAdded;

        ColumnDefinitions = new ColumnDefinitions("115,240,*");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        ColumnSpacing = 8;
        Margin = new Thickness(0, 8, 0, 0);

        Children.Add(new TextBlock
        {
            Text = "Flags",
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        _chipRows = new StackPanel
        {
            Spacing = 4,
            Margin = editable ? new Thickness(0, 4, 0, 0) : new Thickness(0)
        };
        Grid.SetColumn(_chipRows, 1);
        Grid.SetRow(_chipRows, editable ? 1 : 0);
        Children.Add(_chipRows);

        if (editable)
        {
            _picker = new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains,
                Width = 240,
                MaxWidth = 240
            };
            EditorTextFieldStyle.ConfigureSelector(_picker);
            EditorFieldAppearance.ApplyStandard(_picker);
            EditorAutoCompleteService.EnableDropdown(_picker, Busy, selectAllOnFirstClick: false);
            _picker.SelectionChanged += (_, _) =>
            {
                if (_picker.SelectedItem is not string flag || Busy())
                    return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_flags.Add(flag))
                    {
                        _flagAdded?.Invoke(flag);
                        _changed();
                    }
                    _picker.SelectedItem = null;
                    _picker.Text = string.Empty;
                    Refresh();
                }, DispatcherPriority.Background);
            };
            Grid.SetColumn(_picker, 1);
            Children.Add(_picker);
        }

        Refresh();
    }

    public void Refresh()
    {
        _chipRows.Children.Clear();
        var visible = _flags
            .Where(flag => !flag.Equals("spawncommand", StringComparison.OrdinalIgnoreCase))
            .Where(flag => !_editable || !ProtoUnitCommandDefinition.DeprecatedFlagTags.Contains(flag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < visible.Count; index += 3)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var flag in visible.Skip(index).Take(3))
            {
                var kind = _transformKind();
                var structural = kind.HasValue && ProtoUnitCommandTransformRules.IsRequiredFlag(flag, kind.Value);
                Action? remove = _editable && !structural ? () =>
                {
                    _flags.Remove(flag);
                    _changed();
                    Refresh();
                } : null;
                row.Children.Add(EditorChipService.CreateBlueChip(flag, remove, readOnly: remove == null));
            }
            _chipRows.Children.Add(row);
        }

        if (_picker != null)
        {
            var transformKind = _transformKind();
            _picker.ItemsSource = ProtoUnitCommandDefinition.FlagTags
                .Where(flag => !ProtoUnitCommandDefinition.DeprecatedFlagTags.Contains(flag, StringComparer.OrdinalIgnoreCase))
                .Where(flag => !flag.Equals("spawncommand", StringComparison.OrdinalIgnoreCase))
                .Where(flag => !transformKind.HasValue || !ProtoUnitCommandTransformRules.IsTransformFamilyFlag(flag))
                .Where(flag => !_flags.Contains(flag))
                .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private bool Busy() => _isBusy?.Invoke() == true;
}
