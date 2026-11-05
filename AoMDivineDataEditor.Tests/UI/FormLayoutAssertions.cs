using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class FormLayoutAssertions
{
    internal static void NoOverlaps(Control root, string context)
    {
        static bool Field(Control control) => control is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor or Button or CheckBox or TextEditor;
        var controls = root.GetVisualDescendants().OfType<Control>().Where(control =>
            control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
            (Field(control) || control is TextBlock { Text: { Length: > 0 } }) &&
            !control.GetVisualAncestors().TakeWhile(parent => !ReferenceEquals(parent, root)).OfType<Control>().Any(Field))
            .Select(control => (Control: control, Rect: new Rect(control.TranslatePoint(default, root)!.Value, control.Bounds.Size))).ToArray();
        Assert.NotEmpty(controls);
        string Name(Control control) => control switch
        {
            TextBlock text => text.Text ?? "label", TextBox text => $"value:{text.Text}",
            AutoCompleteBox box => $"catalog:{box.Text}", ComboBox combo => $"choice:{combo.SelectedItem}",
            CheckBox check => $"check:{check.Content}", Button button => $"button:{button.Content}", _ => control.GetType().Name
        };
        foreach (var item in controls)
            Assert.True(item.Rect.X >= -.5 && item.Rect.Right <= root.Bounds.Width + .5,
                $"{context}: {Name(item.Control)} {item.Rect} outside width {root.Bounds.Width}; parents: " +
                string.Join(" / ", item.Control.GetVisualAncestors().OfType<Control>().TakeWhile(parent => !ReferenceEquals(parent, root))
                    .Select(parent => $"{parent.GetType().Name}#{parent.Name}({parent.Bounds})")) +
                (item.Control.Parent is Panel panel ? "; siblings: " + string.Join(" / ", panel.Children.Select(child => $"{Name(child)}({child.Bounds},margin={child.Margin})")) : ""));
        for (var i = 0; i < controls.Length; i++)
        for (var j = i + 1; j < controls.Length; j++)
        {
            var overlap = controls[i].Rect.Intersect(controls[j].Rect);
            Assert.True(overlap.Width <= 1 || overlap.Height <= 1,
                $"{context}: {Name(controls[i].Control)} {controls[i].Rect} overlaps {Name(controls[j].Control)} {controls[j].Rect}");
        }
        // Measure rendered field/cross pairs across every exercised family,
        // including original (disabled) inputs and dynamically opened rows.
        foreach (var cross in controls.Select(item => item.Control).OfType<Button>().Where(button =>
                     button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card")))
        {
            if (cross.Parent is not StackPanel panel || panel.Orientation != Avalonia.Layout.Orientation.Horizontal) continue;
            var index = panel.Children.IndexOf(cross);
            var previous = panel.Children.Take(index).LastOrDefault(child => child.IsEffectivelyVisible);
            if (previous is TextBox { AcceptsReturn: false } or AutoCompleteBox or ComboBox or AssetPathEditor)
                CrossGap(previous, cross, root);
        }
    }

    internal static void CrossGap(Control field, Button cross, Visual root)
    {
        var value = field.TranslatePoint(default, root)!.Value;
        var remove = cross.TranslatePoint(default, root)!.Value;
        Assert.InRange(remove.X - value.X - field.Bounds.Width, 3.9, 4.1);
        Assert.InRange(Math.Abs(remove.Y + cross.Bounds.Height / 2 - value.Y - field.Bounds.Height / 2), 0, .5);
    }
}
