using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Runtime.CompilerServices;

namespace AoMDivineDataEditor.Classes;

/// <summary>Shared measurements and explicit field presentation roles; no persistence.</summary>
internal static class EditorFormLayout
{
    public const double FieldHeight = 30;
    public const double CrossGap = 4;
    public const double LabelGap = 8;
    public const double LabelColumnWidth = 150;
    public const double GroupGap = 12;
    public const double SectionGap = 16;
    public const double ChipPickerWidth = 50;
    public const double NumericWidth = 90;
    public const double DenseNumericWidth = 60;
    public const double TacticsWeightWidth = 48;
    public const double DamageBonusMultiplierWidth = 80;
    public const double ReferenceWidth = 200;
    public const double CompactReferenceWidth = 150;
    public const double WideReferenceWidth = 240;
    public const double ShortEnumWidth = 100;
    public const double LongEnumWidth = 180;
    public const double ComparisonOperatorWidth = 62;
    public const double AssetPathWidth = 240;
    public const double CompactAssetPathWidth = 200;
    public const double AttachmentBoneWidth = 100;
    public const double SingleLineTextWidth = 300;

    private sealed class WidthSelection(EditorFieldWidthRole role)
    {
        public EditorFieldWidthRole Role = role;
    }

    // Only explicitly migrated controls get a role. Legacy controls keep their
    // existing widths, and detached controls are not retained by this table.
    private static readonly ConditionalWeakTable<Control, WidthSelection> WidthSelections = new();
    private static readonly ConditionalWeakTable<StackPanel, object> InlineRows = new();

    public static double WidthFor(EditorFieldWidthRole role) => role switch
    {
        EditorFieldWidthRole.Numeric => NumericWidth,
        EditorFieldWidthRole.DenseNumeric => DenseNumericWidth,
        EditorFieldWidthRole.TacticsWeight => TacticsWeightWidth,
        EditorFieldWidthRole.DamageBonusMultiplier => DamageBonusMultiplierWidth,
        EditorFieldWidthRole.Reference or EditorFieldWidthRole.StringIdReference => ReferenceWidth,
        EditorFieldWidthRole.CompactReference => CompactReferenceWidth,
        EditorFieldWidthRole.WideReference => WideReferenceWidth,
        EditorFieldWidthRole.ChipSearch => ChipPickerWidth,
        EditorFieldWidthRole.ShortEnum => ShortEnumWidth,
        EditorFieldWidthRole.LongEnum => LongEnumWidth,
        EditorFieldWidthRole.ComparisonOperator => ComparisonOperatorWidth,
        EditorFieldWidthRole.AssetPath => AssetPathWidth,
        EditorFieldWidthRole.CompactAssetPath => CompactAssetPathWidth,
        EditorFieldWidthRole.AttachmentBone => AttachmentBoneWidth,
        EditorFieldWidthRole.SingleLineText or EditorFieldWidthRole.ShortRollover => SingleLineTextWidth,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown field width role.")
    };

    public static T ApplyWidth<T>(T editor, EditorFieldWidthRole role) where T : Control
    {
        var width = WidthFor(role);
        WidthSelections.GetValue(editor, _ => new WidthSelection(role)).Role = role;
        ApplyRoleWidth(editor, width);
        EditorFieldInteraction.Configure(editor, role);
        return editor;
    }

    internal static bool TryGetWidthRole(Control editor, out EditorFieldWidthRole role)
    {
        if (WidthSelections.TryGetValue(editor, out var selection))
        {
            role = selection.Role;
            return true;
        }
        role = default;
        return false;
    }

    internal static void ApplyConfiguredWidth(Control editor, double legacyWidth)
    {
        if (WidthSelections.TryGetValue(editor, out var selection))
            ApplyRoleWidth(editor, WidthFor(selection.Role));
        else
        {
            // Preserve the old helper contract, including existing MinWidth.
            editor.Width = legacyWidth;
            editor.MaxWidth = legacyWidth;
        }
    }

    private static void ApplyRoleWidth(Control editor, double width)
    {
        // Clear conflicting legacy limits: Width alone can still be enlarged
        // by an earlier MinWidth. The caller allocates/wraps the complete group.
        editor.MinWidth = width;
        editor.Width = width;
        editor.MaxWidth = width;
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        if (editor is TextBox { AcceptsReturn: false } text)
        {
            // Fixed-height identity fields use a larger font. A single text line
            // must not acquire a tiny vertical scrollbar in the trailing corner.
            ScrollViewer.SetVerticalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        }
    }

    // Only the cross contributes the gap; outer wrapping/label groups must not
    // insert another spacing value between these two controls.
    public static StackPanel WithCross(Control field, Button cross) => new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { field, cross }
    };

    // Explicit column groups keep labels, fields and optional crosses together
    // when an existing compound row must wrap. No field classification is inferred.
    public static void WrapGridColumns(Grid grid, params int[][] columnGroups)
    {
        var children = grid.Children.ToArray();
        var groups = columnGroups.Select(columns => children.Where(child => columns.Contains(Grid.GetColumn(child)))
            .OrderBy(Grid.GetColumn).ToArray()).ToArray();
        if (groups.SelectMany(group => group).Distinct().Count() != children.Length ||
            groups.Sum(group => group.Length) != children.Length)
            throw new ArgumentException("Every field must belong to exactly one wrapping group.", nameof(columnGroups));
        grid.Children.Clear();
        grid.ColumnDefinitions = new ColumnDefinitions("*");
        var wrap = new WrapPanel();
        foreach (var fields in groups)
        {
            if (fields.Length == 0) continue;
            var group = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, GroupGap, 0)
            };
            foreach (var field in fields)
            {
                var margin = field.Margin;
                field.Margin = new Thickness(field is Button button && button.Classes.Contains("remove-button") ? CrossGap : 0,
                    margin.Top, field is TextBlock ? LabelGap : 0, margin.Bottom);
                field.VerticalAlignment = VerticalAlignment.Center;
                group.Children.Add(field);
            }
            void RefreshVisibility() => group.IsVisible = fields.Any(field => field.IsVisible);
            foreach (var field in fields)
                field.PropertyChanged += (_, change) => { if (change.Property == Visual.IsVisibleProperty) RefreshVisibility(); };
            RefreshVisibility();
            wrap.Children.Add(group);
        }
        grid.Children.Add(wrap);
    }

    public static void WrapFlowingGridColumns(Grid grid, params int[][] groups)
    {
        WrapGridColumns(grid, groups);
        grid.Margin = new Thickness(0);
        foreach (var group in ((WrapPanel)grid.Children[0]).Children)
            group.Margin = new Thickness(0, 0, GroupGap, LabelGap);
    }

    // Existing state/serialization code can retain the row and its direct children.
    // Only this method supplies the actual field/cross gap, never StackPanel.Spacing.
    public static void NormalizeInlineRow(StackPanel row)
    {
        if (row.Orientation != Orientation.Horizontal) return;
        row.Spacing = 0;
        var children = row.Children.ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var field = children[index];
            var remove = field is Button button && button.Classes.Contains("remove-button");
            var nextRemove = index + 1 < children.Length && children[index + 1] is Button next && next.Classes.Contains("remove-button");
            field.VerticalAlignment = VerticalAlignment.Center;
            field.Margin = new Thickness(remove ? CrossGap : 0, 0,
                !remove && !nextRemove && index + 1 < children.Length ? LabelGap : 0, 0);
        }
    }

    public static void UseInlineRowLayout(StackPanel row)
    {
        if (row.Orientation != Orientation.Horizontal) return;
        if (!InlineRows.TryGetValue(row, out _))
        {
            InlineRows.Add(row, new object());
            row.Children.CollectionChanged += (_, _) => NormalizeInlineRow(row);
        }
        NormalizeInlineRow(row);
    }

    public static int GetContainingGridColumn(Grid grid, Control field)
    {
        var cell = field;
        while (cell.Parent is Control parent && !ReferenceEquals(parent, grid)) cell = parent;
        return Grid.GetColumn(ReferenceEquals(cell.Parent, grid) ? cell : field);
    }

    // A common leading label column followed by independently wrapping fields.
    // Callers declare each label/field/cross group explicitly as with WrapGridColumns.
    public static void WrapLabeledGridColumns(Grid grid, params int[][] fieldColumnGroups)
    {
        WrapGridColumns(grid, [[0], .. fieldColumnGroups]);
        var wrap = (WrapPanel)grid.Children[0];
        var label = (StackPanel)wrap.Children[0];
        label.Width = LabelColumnWidth;
        label.Margin = new Thickness(0);
        foreach (var text in label.Children.OfType<TextBlock>()) text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        wrap.Children[^1].Margin = new Thickness(0);
    }

    public static void GroupFlowingFields(WrapPanel row)
    {
        static bool IsInput(Control control) => control is TextBox or AutoCompleteBox or ComboBox or AoMDivineDataEditor.Controls.AssetPathEditor ||
            control is Grid { ColumnDefinitions.Count: <= 1, RowDefinitions.Count: <= 1 } markerHost &&
            markerHost.Children.Count(child => child is TextBox or AutoCompleteBox or ComboBox or AoMDivineDataEditor.Controls.AssetPathEditor) == 1;
        static bool IsCross(Control control) => control is Button button && button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card");
        // Attachment/timer and optional-number builders already supply a labelled
        // group. Keep its cross inside that group instead of a separate wrap item.
        for (var i = 1; i < row.Children.Count; i++)
            if (IsCross(row.Children[i]) && row.Children[i - 1] is StackPanel { Orientation: Orientation.Horizontal } group &&
                group.Children.Any(IsInput))
            {
                var cross = row.Children[i]; row.Children.RemoveAt(i); group.Children.Add(cross);
                UseInlineRowLayout(group);
                i--;
            }
        var children = row.Children.ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var first = children[index];
            var labels = new List<TextBlock>();
            var end = index;
            while (end < children.Length && children[end] is TextBlock label) { labels.Add(label); end++; }
            var field = end < children.Length ? children[end] : first;
            if (!IsInput(field)) continue;
            var cross = end + 1 < children.Length && IsCross(children[end + 1]) ? (Button)children[++end] : null;
            var at = row.Children.IndexOf(first);
            foreach (var label in labels) row.Children.Remove(label);
            row.Children.Remove(field); if (cross != null) row.Children.Remove(cross);
            var group = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, GroupGap, LabelGap)
            };
            foreach (var label in labels) group.Children.Add(label);
            group.Children.Add(field); if (cross != null) group.Children.Add(cross);
            void RefreshVisibility() => group.IsVisible = group.Children.Any(child => child.IsVisible);
            foreach (var child in group.Children)
                child.PropertyChanged += (_, change) => { if (change.Property == Visual.IsVisibleProperty) RefreshVisibility(); };
            RefreshVisibility();
            UseInlineRowLayout(group);
            row.Children.Insert(at, group); index = end;
        }
        // Standalone group labels such as "Offset" precede X/Y/Z labelled
        // fields; they still need a gap when no input immediately follows them.
        foreach (var label in row.Children.OfType<TextBlock>())
        {
            label.Margin = new Thickness(0, 0, LabelGap, LabelGap);
            label.VerticalAlignment = VerticalAlignment.Center;
        }
        // WrapPanel centers the entire desired size, including margins. Optional
        // buttons/checks need the same bottom rhythm as the adjacent field group.
        foreach (var control in row.Children.Where(child => child is CheckBox ||
                     child is Button button && button.Classes.Contains("add-component")))
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(0, 0, GroupGap, LabelGap);
        }
    }

    // Role widths must also own their grid allocation. A 90px input in an old
    // 80px column otherwise paints over the next label or checkbox. Skip spans
    // and overlay grids (source indicators) whose allocation belongs to a host.
    public static void NormalizeFieldGrid(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 2) return;
        var rolloverColumns = grid.Children.Where(child => EditorRolloverField.IsLong(child) || EditorRolloverField.IsHost(child)).Select(Grid.GetColumn).ToHashSet();
        foreach (var column in rolloverColumns.Where(column => column < grid.ColumnDefinitions.Count))
            grid.ColumnDefinitions[column].Width = new GridLength(1, GridUnitType.Star);
        foreach (var field in grid.Children.Where(child =>
                     Grid.GetColumnSpan(child) == 1 &&
                     child is TextBox or AutoCompleteBox or ComboBox or AoMDivineDataEditor.Controls.AssetPathEditor))
        {
            var column = Grid.GetColumn(field);
            if (column >= grid.ColumnDefinitions.Count) continue;
            if (EditorRolloverField.IsLong(field))
            {
                grid.ColumnDefinitions[column].Width = new GridLength(1, GridUnitType.Star);
                field.HorizontalAlignment = HorizontalAlignment.Stretch;
                field.VerticalAlignment = VerticalAlignment.Top;
                var label = grid.Children.OfType<TextBlock>().FirstOrDefault(candidate =>
                    Grid.GetRow(candidate) == Grid.GetRow(field) && Grid.GetColumn(candidate) == column - 1);
                if (label != null) label.VerticalAlignment = VerticalAlignment.Top;
                var remove = grid.Children.OfType<Button>().FirstOrDefault(candidate =>
                    candidate.Classes.Contains("remove-button") && Grid.GetRow(candidate) == Grid.GetRow(field) && Grid.GetColumn(candidate) == column + 1);
                if (remove != null)
                {
                    field.Margin = new Thickness(field.Margin.Left, field.Margin.Top, 0, field.Margin.Bottom);
                    remove.Margin = new Thickness(CrossGap, field.Margin.Top + 1, 0, 0);
                    remove.VerticalAlignment = VerticalAlignment.Top;
                }
                continue;
            }
            grid.ColumnDefinitions[column].Width = rolloverColumns.Contains(column) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            field.HorizontalAlignment = HorizontalAlignment.Left;
            field.VerticalAlignment = VerticalAlignment.Center;
            var cross = grid.Children.OfType<Button>().FirstOrDefault(button =>
                button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card") &&
                Grid.GetRow(button) == Grid.GetRow(field) && Grid.GetColumn(button) == column + 1);
            if (cross == null) continue;
            field.Margin = new Thickness(field.Margin.Left, field.Margin.Top, 0, field.Margin.Bottom);
            cross.Margin = new Thickness(CrossGap, 0, 0, 0);
            cross.VerticalAlignment = VerticalAlignment.Center;
            if (column + 1 < grid.ColumnDefinitions.Count) grid.ColumnDefinitions[column + 1].Width = GridLength.Auto;
        }
    }

    public static StackPanel LabeledGroup(string label, Control field) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = LabelGap,
        Margin = new Thickness(0, 0, GroupGap, LabelGap),
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
            field
        }
    };

    public static void WrapHeaderColumns(Grid header, params int[] fieldColumns)
    {
        var controls = header.Children.ToArray();
        header.Children.Clear();
        header.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        header.RowDefinitions = new RowDefinitions("Auto");
        header.Width = double.NaN;
        var fields = new WrapPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = LabelGap, VerticalAlignment = VerticalAlignment.Top };
        foreach (var column in fieldColumns)
        {
            var group = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, GroupGap, LabelGap) };
            foreach (var control in controls.Where(control => Grid.GetColumn(control) == column).OrderBy(Grid.GetRow))
            {
                control.Margin = new Thickness(0); group.Children.Add(control);
            }
            fields.Children.Add(group);
        }
        foreach (var action in controls.Where(control => !fieldColumns.Contains(Grid.GetColumn(control))))
        { action.Margin = new Thickness(0); action.VerticalAlignment = VerticalAlignment.Top; actions.Children.Add(action); }
        Grid.SetColumn(actions, 1); header.Children.Add(fields); header.Children.Add(actions);
    }
}
