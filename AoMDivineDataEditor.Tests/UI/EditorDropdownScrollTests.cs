using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorDropdownScrollTests
{
    private static async Task Settle()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(30);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDropdownGuardsNestedOwnersReattachesAndAllowsPopupScrolling(bool registerAfterAttach)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var box = new AutoCompleteBox
            {
                ItemsSource = Enumerable.Range(0, 80).Select(n => $"Choice {n:00}").ToArray(),
                Width = 180, HorizontalAlignment = HorizontalAlignment.Left
            };
            EditorAutoCompleteService.EnableDropdown(box);
            if (!registerAfterAttach) EditorAutoCompleteService.KeepDropDownAnchored(box);
            var ownerContent = new StackPanel { Children = { box, new Border { Height = 900 } } };
            var inner = new ScrollViewer { Content = ownerContent, Height = 180 };
            var outerContent = new StackPanel { Children = { inner, new Border { Height = 800 } } };
            var outer = new ScrollViewer { Content = outerContent };
            var window = new Window { Width = 600, Height = 400, Content = outer };
            try
            {
                window.Show(); await Settle();
                EditorAutoCompleteService.KeepDropDownAnchored(box); // Repeated configuration is harmless.
                async Task Open()
                {
                    box.Text = ""; box.Focus(); box.IsDropDownOpen = true;
                    await Settle(); Assert.True(box.IsDropDownOpen);
                }
                async Task WheelOwner(ScrollViewer owner)
                {
                    var before = owner.Offset;
                    var point = owner.TranslatePoint(new Point(owner.Bounds.Width - 40, 100), window)!.Value;
                    window.MouseMove(point); window.MouseWheel(point, new Vector(0, -1), RawInputModifiers.None);
                    await Settle(); Assert.Equal(before, owner.Offset); Assert.True(box.IsDropDownOpen);
                }
                await Open(); await WheelOwner(inner); await WheelOwner(outer);
                foreach (var owner in new[] { inner, outer })
                {
                    owner.Offset = new Vector(0, 40); await Settle();
                    Assert.False(box.IsDropDownOpen);
                    owner.Offset = default; await Settle(); await Open();
                }
                var popup = Assert.Single(box.GetVisualDescendants().OfType<Popup>());
                var popupScroll = Assert.Single(popup.Child!.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Extent.Height > s.Viewport.Height);
                var popupTop = TopLevel.GetTopLevel(popupScroll)!;
                var popupPoint = popupScroll.TranslatePoint(new Point(30, 30), popupTop)!.Value;
                var popupBefore = popupScroll.Offset;
                popupTop.MouseMove(popupPoint); popupTop.MouseWheel(popupPoint, new Vector(0, -2), RawInputModifiers.None);
                await Settle();
                Assert.True(popupScroll.Offset.Y > popupBefore.Y,
                    $"Popup scroll {popupBefore} -> {popupScroll.Offset}; hit {popupTop.InputHitTest(popupPoint)?.GetType().Name}; root {popupTop.GetType().Name}; extent {popupScroll.Extent}; viewport {popupScroll.Viewport}");
                Assert.True(box.IsDropDownOpen, "Scrolling popup choices closed the dropdown.");

                ownerContent.Children.Remove(box); await Settle(); Assert.False(box.IsDropDownOpen);
                outerContent.Children.Insert(0, box); await Settle(); await Open();
                inner.Offset = new Vector(0, 20); await Settle();
                Assert.True(box.IsDropDownOpen); // The old inner owner no longer controls this field.
                outer.Offset = new Vector(0, 20); await Settle(); Assert.False(box.IsDropDownOpen);
                await Settle();
                Assert.True(string.IsNullOrEmpty(box.Text)); Assert.Null(box.SelectedItem);
            }
            finally { window.Close(); }
        });
}
