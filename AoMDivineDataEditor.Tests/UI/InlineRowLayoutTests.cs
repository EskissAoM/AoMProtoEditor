using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class InlineRowLayoutTests
{
    [Fact]
    public void CompoundLabelsAndOptionalButtonsShareTheFieldCenterAndWrapTogether()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var title = new TextBlock { Text = "Offset" }; var axis = new TextBlock { Text = "X" };
            var field = EditorFormLayout.ApplyWidth(new TextBox { Text = "1", Height = 30 }, EditorFieldWidthRole.Numeric);
            var count = new Button { Content = "Count", Classes = { "add-component" } };
            var flow = new WrapPanel { Children = { title, axis, field, count } };
            EditorFormLayout.GroupFlowingFields(flow); EditorFormLayout.GroupFlowingFields(flow);
            var window = new Window { Width = 700, Height = 240, Content = flow };
            try
            {
                window.Show();
                foreach (var width in new[] { 650d, 260d, 650d })
                {
                    flow.Width = width; await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    Assert.Same(title.Parent, axis.Parent); Assert.Same(axis.Parent, field.Parent);
                    double Center(Control control) => control.TranslatePoint(default, flow)!.Value.Y + control.Bounds.Height / 2;
                    Assert.InRange(Math.Abs(Center(title) - Center(field)), 0, .5);
                    Assert.InRange(Math.Abs(Center(axis) - Center(field)), 0, .5);
                    if (width > 300) Assert.InRange(Math.Abs(Center(count) - Center(field)), 0, .5);
                    FormLayoutAssertions.NoOverlaps(flow, "compound labels and optional button");
                }
            }
            finally { window.Close(); }
        });

    [Fact]
    public void MarkerWrappedFieldsAndAttachmentCrossesRemainAtomicWhenNarrowed()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var field = EditorFormLayout.ApplyWidth(new TextBox { Text = "Impact" }, EditorFieldWidthRole.Reference);
            var marker = new Grid { Children = { field, new Border { Width = 10, Height = 10 } } };
            var label = new TextBlock { Text = "Impact Effect" };
            var timer = EditorFormLayout.LabeledGroup("Timer", EditorFormLayout.ApplyWidth(new TextBox { Text = "1" }, EditorFieldWidthRole.Numeric));
            var cross = EditorRemoveButtons.Field();
            var wholeAttachment = EditorRemoveButtons.Row();
            var flow = new WrapPanel { Children = { label, marker, timer, cross, wholeAttachment } };
            EditorFormLayout.GroupFlowingFields(flow);
            EditorFormLayout.GroupFlowingFields(flow);
            var window = new Window { Width = 700, Height = 260, Content = flow };
            try
            {
                window.Show();
                foreach (var width in new[] { 650d, 320d, 650d })
                {
                    flow.Width = width;
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    Assert.Same(label.Parent, marker.Parent);
                    Assert.Same(timer, cross.Parent);
                    Assert.Same(timer, wholeAttachment.Parent);
                    FormLayoutAssertions.CrossGap(cross, wholeAttachment, flow);
                    FormLayoutAssertions.NoOverlaps(flow, "marker and attachment groups");
                }
            }
            finally { window.Close(); }
        });

    [Fact]
    public void InlineRowsKeepOneCrossGapAfterDynamicInsertRemoveAndRepeatedConfiguration()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var field = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox { Text = "2" }, EditorFieldWidthRole.Numeric);
            var cross = EditorRemoveButtons.Field();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "Value" }, field, cross } };
            EditorFormLayout.UseInlineRowLayout(row);
            EditorFormLayout.UseInlineRowLayout(row);
            var window = new Window { Width = 600, Height = 200, Content = row };
            try
            {
                window.Show();
                async Task Check()
                {
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    FormLayoutAssertions.CrossGap(field, cross, window);
                    Assert.Equal("2", field.Text);
                    Assert.Contains(field, row.Children); Assert.Contains(cross, row.Children);
                }
                await Check();
                var optional = new TextBox { Width = 90 };
                var optionalCross = EditorRemoveButtons.Field();
                row.Children.Add(optional); row.Children.Add(optionalCross);
                await Check(); FormLayoutAssertions.CrossGap(optional, optionalCross, window);
                row.Children.Remove(optionalCross); row.Children.Remove(optional);
                row.Children.Insert(1, new TextBlock { Text = "Amount" });
                await Check();
            }
            finally { window.Close(); }
        });
}
