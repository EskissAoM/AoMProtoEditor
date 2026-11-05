using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class PowerFieldPresentationTests
{
    [Theory]
    [MemberData(nameof(FixedPowerFamilies))]
    public void NonSwitchingPowersNeverShowTimerAndSwitchingWithoutTimerStartsHidden(string type, bool original)
        => WithPower(type, "<activetime>10</activetime>", original, async (view, window, _) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            Assert.False(Field<Grid>(view, "_switchingTimerRolloverFields").IsEffectivelyVisible);
            Assert.Same(Field<Grid>(view, "_switchingProgressRow"), Field<Grid>(view, "_switchingTimerRolloverFields").Parent);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), label => label.IsEffectivelyVisible && label.Text == "Timer Rollover" &&
                !label.GetVisualAncestors().OfType<Button>().Any());
            Assert.False(view.IsDirty);
        });

    [Fact]
    public void TimerVisibilityFollowsPowerSwitchAndOptionalAddRemove()
        => WithPower("SwitchingEffects", "<timerrolloverid>STR_TIMER</timerrolloverid>", false, async (view, window, _) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var timer = Field<Grid>(view, "_switchingTimerRolloverFields"); Assert.True(timer.IsEffectivelyVisible);
            await view.SelectPowerAsync("B"); await Settle(window); Assert.False(timer.IsEffectivelyVisible);
            await view.SelectPowerAsync("A"); await Settle(window); Assert.True(timer.IsEffectivelyVisible);
            Field<Button>(view, "_removeSwitchingTimerRolloverButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle(window); Assert.False(timer.IsEffectivelyVisible);
            Field<Button>(view, "_addSwitchingTimerRolloverButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle(window); Assert.True(timer.IsEffectivelyVisible);
            await RolloverAssertions.Stress(window, Field<TextBox>(view, "_switchingTimerRolloverBox"));
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PowerNotificationAndRangeRemovalHaveOneScopeAndMeasuredGaps(bool original)
        => WithPower("Bolt", "<rangeindicator>Arrow</rangeindicator><notificationformatid>STR_CAST</notificationformatid><selfnotificationformatid>STR_SELF</selfnotificationformatid><messagealertplayerrelation>All</messagealertplayerrelation><displayalerts/>", original, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            var notification = Field<Grid>(view, "_notificationsSection");
            var wholeRemove = Field<Button>(view, "_removeNotificationsButton");
            Assert.Same(notification, wholeRemove.Parent);
            Assert.Single(notification.Children.OfType<Button>());
            foreach (var width in new[] { 1440d, 1100d, 1440d })
            {
                window.Width = width; await Settle(window);
                FormLayoutAssertions.NoOverlaps(notification, "Notification repeated rows");
                FormLayoutAssertions.NoOverlaps(Field<Grid>(view, "_rangeIndicatorRow"), "Range Indicator");
                var selector = Field<ComboBox>(view, "_rangeIndicatorBox");
                var group = Assert.IsType<StackPanel>(selector.Parent);
                var remove = Assert.Single(group.Children.OfType<Button>());
                if (!original) FormLayoutAssertions.CrossGap(selector, remove, notification);
                Assert.Equal(!original, wholeRemove.IsEffectivelyVisible);
            }
            Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
            foreach (var name in new[] { "_castMessageBox", "_castSelfMessageBox" }) await RolloverAssertions.Stress(window, Field<TextBox>(view, name));
            if (!original)
            {
                wholeRemove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(window);
                Assert.False(notification.IsEffectivelyVisible);
                Assert.Null(FieldEntry(view).Element("notificationformatid")); Assert.Null(FieldEntry(view).Element("selfnotificationformatid"));
                Assert.NotNull(FieldEntry(view).Element("rangeindicator"));
            }
        });
}

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void ActivePowerRolloverIsStableInsideTheCompleteEditorHost()
        => WithHost(async (_, host) =>
        {
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", System.Reflection.BindingFlags.NonPublic)!, "Powers");
            await ((Task)host.Call("ShowEntityKindAsync", kind)!).WaitAsync(TimeSpan.FromSeconds(10));
            var view = Field<PowerEditorView>(host, "_powerView"); view.SetModifiedMode(true);
            await view.SelectPowerAsync("A"); await RoleSettle(host);
            var main = view.FindControl<TextBox>("_rolloverBox")!; var active = view.FindControl<TextBox>("_activeRolloverBox")!;
            var icon = view.FindControl<Border>("_iconPreviewHost")!;
            foreach (var width in new[] { 1440d, 1100d, 1440d })
            {
                host.Width = width; await RoleSettle(host);
                await RolloverAssertions.Stress(host, main); await RolloverAssertions.Stress(host, active);
                IdentityPreviewAssertions.Compact(main);
                foreach (var field in new[] { main, active })
                {
                    var a = new Rect(field.TranslatePoint(default, view)!.Value, field.Bounds.Size);
                    var b = new Rect(icon.TranslatePoint(default, view)!.Value, icon.Bounds.Size);
                    var overlap = a.Intersect(b); Assert.True(overlap.Width <= 1 || overlap.Height <= 1);
                }
                FormLayoutAssertions.NoOverlaps(view.FindControl<Grid>("_mainGrid")!, "Power in full editor host");
            }
        }, setup: fixture => fixture.Workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='A' type='Bolt' godpower=''><displaynameid>STR_NAME</displaynameid><rolloverid>STR_LONG</rolloverid><activepowerrolloverid>STR_ACTIVE</activepowerrolloverid></power></powersmod>"));
}
