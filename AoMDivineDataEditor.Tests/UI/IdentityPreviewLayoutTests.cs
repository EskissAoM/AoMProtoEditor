using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class IdentityPreviewAssertions
{
    internal static void Compact(TextBox rollover)
    {
        var host = Assert.Single(rollover.GetLogicalAncestors().OfType<IdentityFieldPanel>());
        var fieldRect = new Rect(host.Fields.TranslatePoint(default, host)!.Value, host.Fields.Bounds.Size);
        var iconRect = new Rect(host.Icon.TranslatePoint(default, host)!.Value, host.Icon.Bounds.Size);
        Assert.InRange(fieldRect.Right, 1, host.Bounds.Width + .5);
        Assert.InRange(iconRect.Right, 1, host.Bounds.Width + .5);
        if (host.IsStacked)
            Assert.True(fieldRect.Top >= iconRect.Bottom, $"Stacked portrait overlaps fields: {fieldRect}, {iconRect}");
        else
            Assert.InRange(iconRect.Left - fieldRect.Right, 11.9, 12.1);
        FormLayoutAssertions.NoOverlaps(host, "Compact identity preview");
    }
}

public sealed class IdentityPreviewLayoutTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PreviewFollowsCompactFieldsAcrossResizesAndRolloverEdits(bool removable)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var text = new TextBox();
            var cross = removable ? EditorRemoveButtons.Field() : null;
            var fields = new StackPanel { Children = { EditorRolloverField.CreateRow("Rollover", text, cross) } };
            var preview = new Border { Width = 132, Height = 132 };
            var layout = new Grid { Children = { fields, preview } };
            EditorRolloverField.ConfigureIdentity(layout, fields, preview);
            var window = new Window { Content = layout, Width = 1200, Height = 600 };
            try
            {
                window.Show();
                foreach (var width in new[] { 1200d, 760d, 580d, 420d, 1200d })
                foreach (var value in new[] { "Short description", new string('Z', 2048), "short again" })
                {
                    window.Width = width; text.Text = value;
                    for (var pass = 0; pass < 3; pass++)
                    {
                        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
                    }
                    IdentityPreviewAssertions.Compact(text);
                    var host = Assert.IsType<IdentityFieldPanel>(text.GetLogicalAncestors().OfType<IdentityFieldPanel>().Single());
                    Assert.Equal(width < 700, host.IsStacked);
                    if (!host.IsStacked)
                    {
                        var textRight = text.TranslatePoint(default, host)!.Value.X + text.Bounds.Width;
                        var previewLeft = preview.TranslatePoint(default, host)!.Value.X;
                        Assert.InRange(previewLeft - textRight, 12, removable ? 45 : 12.1);
                    }
                    Assert.Equal(value, text.Text);
                }
            }
            finally { window.Close(); }
        });
}
