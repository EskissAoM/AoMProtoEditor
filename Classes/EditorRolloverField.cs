using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Presentation only: text, string IDs and edit callbacks stay with their owner.</summary>
internal static class EditorRolloverField
{
    public const double StandardWidth = 380;
    private static readonly ConditionalWeakTable<TextBox, object> LongFields = new();
    public static bool IsLong(Control field) => field is TextBox text && LongFields.TryGetValue(text, out _);
    public static bool IsHost(Control control) => control is RolloverFieldPanel;

    public static TextBox Configure(TextBox field)
    {
        LongFields.GetValue(field, _ => new object());
        field.AcceptsReturn = true;
        field.TextWrapping = TextWrapping.Wrap;
        field.MinLines = 1;
        field.MaxLines = 6;
        field.MinHeight = EditorFormLayout.FieldHeight;
        field.Height = double.NaN;
        field.MaxHeight = double.PositiveInfinity;
        field.MinWidth = 0;
        field.Width = double.NaN;
        field.MaxWidth = StandardWidth;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.VerticalAlignment = VerticalAlignment.Top;
        ScrollViewer.SetHorizontalScrollBarVisibility(field, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(field, ScrollBarVisibility.Auto);
        Constrain(field);
        return field;
    }

    public static void Constrain(TextBox field)
    {
        if (field.Parent is RolloverFieldPanel || field.Parent is not Panel parent) return;
        if (parent is not Grid && parent is not StackPanel { Orientation: Orientation.Vertical }) return;
        var row = Grid.GetRow(field); var column = Grid.GetColumn(field); var span = Grid.GetColumnSpan(field);
        var cross = parent is Grid ? parent.Children.OfType<Button>().FirstOrDefault(button =>
            button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card") &&
            Grid.GetRow(button) == row && Grid.GetColumn(button) == column + span) : null;
        var index = parent.Children.IndexOf(field);
        var margin = field.Margin;
        parent.Children.Remove(field);
        if (cross != null) parent.Children.Remove(cross);
        field.Margin = new Thickness(0);
        if (cross != null) cross.Margin = new Thickness(0);
        var host = new RolloverFieldPanel(field, cross) { Margin = new Thickness(margin.Left, margin.Top, 0, margin.Bottom), HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetRow(host, row); Grid.SetColumn(host, column);
        Grid.SetColumnSpan(host, span + (cross == null ? 0 : 1)); Grid.SetRowSpan(host, Grid.GetRowSpan(field));
        parent.Children.Insert(Math.Min(index, parent.Children.Count), host);
        if (parent is Grid grid && column < grid.ColumnDefinitions.Count)
            grid.ColumnDefinitions[column].Width = new GridLength(1, GridUnitType.Star);
    }

    public static Grid CreateRow(string label, TextBox field, Button? cross = null, double labelWidth = EditorFormLayout.LabelColumnWidth)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*,Auto"), Margin = new Thickness(0, 0, 0, EditorFormLayout.LabelGap) };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, EditorFormLayout.LabelGap, 0) });
        Configure(field); field.Margin = new Thickness(0); Grid.SetRow(field, 0); Grid.SetColumn(field, 1); row.Children.Add(field);
        if (cross != null)
        {
            cross.Margin = new Thickness(EditorFormLayout.CrossGap, 1, 0, 0);
            cross.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(cross, 2); row.Children.Add(cross);
        }
        Constrain(field);
        return row;
    }

    public static void ConfigureIdentity(Grid layout, Control fields, Control icon)
    {
        // Measure a compact finite identity block. A stretching star column alone
        // moves the icon to the far edge even when every text field is capped.
        layout.Children.Remove(fields);
        layout.Children.Remove(icon);
        layout.ColumnDefinitions = new ColumnDefinitions("*");
        layout.RowDefinitions = new RowDefinitions("Auto");
        layout.HorizontalAlignment = fields.HorizontalAlignment = HorizontalAlignment.Stretch;
        icon.Margin = new Thickness(EditorFormLayout.GroupGap, 4, 0, 4);
        var compact = new IdentityFieldPanel(fields, icon) { HorizontalAlignment = HorizontalAlignment.Left };
        compact.Initialize();
        layout.Children.Add(compact);
    }
}
