using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class RolloverAssertions
{
    internal static async Task Stress(Window window, TextBox field)
    {
        var original = field.Text;
        foreach (var text in new[] { "Short description", string.Join(' ', Enumerable.Repeat("Long rollover sentence", 12)), new string('Z', 2048) })
        {
            field.Text = text;
            double? stableWidth = null;
            for (var pass = 0; pass < 8; pass++)
            {
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(15);
                Assert.InRange(field.Bounds.Width, 1, EditorRolloverField.StandardWidth + .5);
                if (pass >= 4)
                {
                    if (stableWidth is { } previous) Assert.InRange(Math.Abs(field.Bounds.Width - previous), 0, .5);
                    stableWidth = field.Bounds.Width;
                }
                var host = Assert.IsType<AoMDivineDataEditor.Controls.RolloverFieldPanel>(field.Parent);
                FormLayoutAssertions.NoOverlaps(host, "Rollover typing and repeated layout");
                if (host.Cross is { IsEffectivelyVisible: true } cross)
                {
                    Assert.InRange(cross.Bounds.X - field.Bounds.Right, 3.9, 4.1);
                    Assert.InRange(cross.Bounds.Y, 0, 2);
                }
            }
            Assert.Equal(text, field.Text);
        }
        field.Text = original;
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
    }

    internal static void Configured(TextBox field)
    {
        Assert.True(EditorRolloverField.IsLong(field));
        Assert.True(field.AcceptsReturn);
        Assert.Equal(1, field.MinLines); Assert.Equal(6, field.MaxLines);
        Assert.Equal(EditorRolloverField.StandardWidth, field.MaxWidth);
        Assert.IsType<AoMDivineDataEditor.Controls.RolloverFieldPanel>(field.Parent);
        Assert.True(double.IsNaN(field.Width)); Assert.True(double.IsNaN(field.Height));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, field.HorizontalAlignment);
        Assert.False(field.Parent is WrapPanel || field.Parent is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal });
    }
}

public sealed class RolloverPresentationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LongRolloverGrowsFromOneLineCapsAtSixAndWrapsWithoutChangingText(bool original)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var field = new TextBox { IsEnabled = !original };
            var row = EditorRolloverField.CreateRow("Rollover", field);
            var window = new Window { Content = new StackPanel { Children = { row } }, Width = 850, Height = 500 };
            async Task Settle() { await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); }
            try
            {
                window.Show(); await Settle(); RolloverAssertions.Configured(field);
                var empty = field.Bounds.Height; Assert.InRange(empty, 30, 40);
                field.Text = "Short text"; await Settle(); Assert.InRange(Math.Abs(field.Bounds.Height - empty), 0, 1);
                field.Text = "First\nSecond"; await Settle(); Assert.True(field.Bounds.Height > empty);
                field.Text = string.Join('\n', Enumerable.Repeat("line", 6)); await Settle(); var six = field.Bounds.Height;
                field.Text = string.Join('\n', Enumerable.Repeat("line", 12)); await Settle();
                Assert.InRange(Math.Abs(field.Bounds.Height - six), 0, 1);
                var scroll = field.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1);
                var wrapped = string.Join(' ', Enumerable.Repeat("A readable description", 7)) + @" literal\n";
                window.Width = 1000; field.Text = wrapped; await Settle(); var wide = field.Bounds.Height;
                window.Width = 400; await Settle(); Assert.True(field.Bounds.Height > wide);
                Assert.True(field.Bounds.Height <= six + 1); Assert.Equal(wrapped, field.Text);
                FormLayoutAssertions.NoOverlaps(row, "bounded rollover");
                field.Text = "Short again"; await Settle(); Assert.InRange(Math.Abs(field.Bounds.Height - empty), 0, 1);
            }
            finally { window.Close(); }
        });
}

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void TechnologyResolvedEffectRolloversUseLongAndShortPoliciesWithoutDirtying()
        => WithTechnologyPresentation("<effects><effect type='SetName' proto='BaseUnit' newRollover='STR_LONG' newShortRollover='STR_SHORT'/><effect type='Data' subtype='Hitpoints' amount='1' relativity='Absolute' tooltipID='STR_TOOLTIP'><target type='ProtoUnit'>BaseUnit</target></effect></effects>", async (editor, window, fixture) =>
        {
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var panel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var longFields = panel.GetLogicalDescendants().OfType<TextBox>().Where(EditorRolloverField.IsLong).Distinct().ToArray();
            Assert.Equal(2, longFields.Length); Assert.All(longFields, RolloverAssertions.Configured);
            var shortField = panel.GetLogicalDescendants().OfType<TextBox>().Single(field => field.Text == "STR_SHORT");
            Assert.Equal(300, shortField.Width); Assert.False(shortField.AcceptsReturn);
            foreach (var width in new[] { 980d, 680d, 980d })
            { panel.Width = width; await SettleTechnologyPresentation(window); FormLayoutAssertions.NoOverlaps(panel, "Technology resolved effect rollovers"); }
            Assert.False(editor.IsDirty); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            foreach (var field in longFields) await RolloverAssertions.Stress(window, field);
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TechnologyMainAndAdvancedRolloversAreBoundedAndClean(bool original)
        => WithTechnologyPresentation("<displaynameid>STR_NAME</displaynameid><rollovertextid>STR_LONG</rollovertextid><advancedrollovertextoverrideid>STR_ADVANCED</advancedrollovertextoverrideid><icon>custom.png</icon>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var panel = GetPrivateField<StackPanel>(editor, "_propertiesPanel");
            var fields = panel.GetLogicalDescendants().OfType<TextBox>().Where(EditorRolloverField.IsLong).ToArray();
            Assert.Equal(2, fields.Length); Assert.All(fields, RolloverAssertions.Configured);
            foreach (var width in new[] { 980d, 680d, 980d })
            { panel.Width = width; await SettleTechnologyPresentation(window); FormLayoutAssertions.NoOverlaps(panel, "Technology rollover identity"); IdentityPreviewAssertions.Compact(fields[0]); }
            Assert.False(editor.IsDirty); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            foreach (var field in fields) await RolloverAssertions.Stress(window, field);
        }, original);
}

public sealed partial class PowerFieldPresentationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PowerMainActiveAndTimerRolloversHaveBoundedParents(bool original)
        => WithPower("SwitchingEffects", "<rolloverid>STR_LONG</rolloverid><activepowerrolloverid>STR_ACTIVE</activepowerrolloverid><timerrolloverid>STR_TIMER</timerrolloverid><effects><displaynameid>STR_EFFECT_NAME</displaynameid><rolloverid>STR_EFFECT_LR</rolloverid><notificationmessageid>STR_EFFECT_NOTIF</notificationmessageid><extratooltips><string>STR_EXTRA</string></extratooltips></effects>", original, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            foreach (var name in new[] { "_rolloverBox", "_activeRolloverBox", "_switchingTimerRolloverBox" })
                RolloverAssertions.Configured(Field<TextBox>(view, name));
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var dynamicRollovers = Field<StackPanel>(view, "_switchingEffectContainers").GetLogicalDescendants().OfType<TextBox>().Where(EditorRolloverField.IsLong).Distinct().ToArray();
            Assert.Equal(2, dynamicRollovers.Length); Assert.All(dynamicRollovers, RolloverAssertions.Configured);
            foreach (var width in new[] { 1440d, 1100d, 1440d })
            { window.Width = width; await Settle(window); FormLayoutAssertions.NoOverlaps(Field<Grid>(view, "_mainGrid"), "Power rollover"); }
            Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
            if (!original)
            {
                const string edited = "Changed é  \nnext line with literal \\n";
                Field<TextBox>(view, "_switchingTimerRolloverBox").Text = edited;
                await Settle(window); Assert.True(view.IsDirty);
                var session = (PowerEditingSession)typeof(PowerEditorView).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                Assert.Equal(edited, session.CreateCommitSnapshot().StringUpdates["STR_TIMER"]);
            }
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 0; await Settle(window);
            foreach (var name in new[] { "_rolloverBox", "_activeRolloverBox" }) await RolloverAssertions.Stress(window, Field<TextBox>(view, name));
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            await RolloverAssertions.Stress(window, Field<TextBox>(view, "_switchingTimerRolloverBox"));
            foreach (var field in dynamicRollovers) await RolloverAssertions.Stress(window, field);
        });
}

public sealed partial class TacticsStartupTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StandaloneAbilityRolloverUsesBoundedColumnAndKeepsNoTouchClean(bool original)
        => WithStandaloneXml(true, async (_, _, child, path, _) =>
        {
            var before = File.ReadAllBytes(path);
            var field = child.GetVisualDescendants().OfType<TextBox>().Single(box => EditorRolloverField.IsLong(box) && box.IsEffectivelyVisible);
            RolloverAssertions.Configured(field);
            foreach (var width in new[] { 1120d, 1000d, 1120d })
            {
                child.Width = width;
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); child.UpdateLayout();
                Assert.True(field.Bounds.Width > 100);
                var parent = Assert.IsType<AoMDivineDataEditor.Controls.RolloverFieldPanel>(field.Parent);
                FormLayoutAssertions.NoOverlaps(parent, "Ability rollover");
            }
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False((bool)typeof(ProtoEditorWindow).GetField("_isDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(child)!);
            await RolloverAssertions.Stress(child, field);
        }, readOnly: original, abilitySource: "<power name='AbilityA' type='UnitAction'><unitaction>HandAttack</unitaction><displaynameid>STR_ABILITY_ABILITYA</displaynameid><rolloverid>STR_ABILITY_LR</rolloverid><placement>Unit</placement></power>");
}

public sealed class CommandRolloverPresentationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CommandRolloverFamilyIsBoundedAndSaveKeepsExactText(bool original)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            const string payload = "Éclaireur  \nLine with literal \\n";
            var source = System.Xml.Linq.XElement.Parse("<protounitcommand><name>C</name><command>Do</command><rollovertextid>STR_LR</rollovertextid><shortrollovertextid>STR_SR</shortrollovertextid><activerollovertextid>STR_ACTIVE</activerollovertextid><disabledrollovertextid>STR_DISABLED</disabledrollovertextid><future keep='yes'/></protounitcommand>");
            var definition = ProtoUnitCommandDefinition.FromElement(source);
            var texts = new Dictionary<string, string> { ["rollovertextid"] = payload, ["shortrollovertextid"] = "Short", ["activerollovertextid"] = payload, ["disabledrollovertextid"] = payload };
            IReadOnlyDictionary<string, string>? saved = null;
            var window = new ProtoUnitCommandEditorWindow(definition, original, new ProtoUnitCommandEditorContext { CommandNames = ["Do"], StringTexts = texts }, null,
                (_, strings, _) => { saved = strings; return Task.FromResult(true); });
            try
            {
                window.Show();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var fields = (Dictionary<string, Control>)typeof(ProtoUnitCommandEditorWindow).GetField("_valueEditors", flags)!.GetValue(window)!;
                foreach (var tag in new[] { "rollovertextid", "activerollovertextid", "disabledrollovertextid" })
                { var field = Assert.IsType<TextBox>(fields[tag]); RolloverAssertions.Configured(field); Assert.Equal(payload, field.Text); }
                Assert.Equal(300, fields["shortrollovertextid"].Width);
                foreach (var width in new[] { 1400d, window.MinWidth, 1400d })
                {
                    window.Width = width; await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var form = (StackPanel)typeof(ProtoUnitCommandEditorWindow).GetField("_structuredForm", flags)!.GetValue(window)!;
                    FormLayoutAssertions.NoOverlaps(form, "Command rollover");
                }
                Assert.False((bool)typeof(ProtoUnitCommandEditorWindow).GetMethod("HasUnsavedChanges", flags)!.Invoke(window, null)!);
                Assert.Null(saved);
                foreach (var tag in new[] { "rollovertextid", "activerollovertextid", "disabledrollovertextid" })
                    await RolloverAssertions.Stress(window, (TextBox)fields[tag]);
                if (!original)
                {
                    var edited = payload + " edited  "; ((TextBox)fields["rollovertextid"]).Text = edited;
                    await (Task)typeof(ProtoUnitCommandEditorWindow).GetMethod("SaveAsync", flags)!.Invoke(window, null)!;
                    Assert.NotNull(saved); Assert.Equal(edited, saved["rollovertextid"]);
                    Assert.Equal(payload, saved["activerollovertextid"]);
                }
            }
            finally { window.DiscardAndClose(); }
        });
}

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void InlineTransformRolloverWrapsWithIdentityWithoutDirtying()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 2;
            TextBox? rollover = null;
            for (var attempt = 0; attempt < 200 && rollover == null; attempt++)
            {
                await RoleSettle(host);
                rollover = host.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsEffectivelyVisible && EditorRolloverField.IsLong(box));
            }
            Assert.NotNull(rollover); RolloverAssertions.Configured(rollover);
            var card = rollover.GetVisualAncestors().OfType<Border>().First(border => border.Child is StackPanel);
            foreach (var width in new[] { 940d, 680d, 940d })
            { card.Width = width; await RoleSettle(host); FormLayoutAssertions.NoOverlaps(card, "Inline Transform rollover"); }
            Assert.False(Field<bool>(host, "_isDirty"));
            await RolloverAssertions.Stress(host, rollover);
        }, RoleDocument("<transformcommand row='0' column='0'>AToB</transformcommand>"), fixture =>
        {
            RoleFixture(fixture);
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/proto_unit_command_mods.xml", "<protounitcommandmods><protounitcommand><name>AToB</name><associatedtech>AToB</associatedtech><transform/><rollovertextid>STR_TRANSFORM_LR</rollovertextid></protounitcommand></protounitcommandmods>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/unit_transform_mods.xml", "<unittransformmods><transform><command>AToB</command><from>A</from><to>B</to><tech>AToB</tech></transform></unittransformmods>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='AToB'><researchpoints>1</researchpoints><status>UNOBTAINABLE</status></tech></techtreemods>");
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnitLongAndShortRolloverRolesDoNotDirtyOrOverlapIdentity(bool original)
        => WithHost(async (fixture, host) =>
        {
            if (original)
            {
                host.Set("_barXmlRoot", System.Xml.Linq.XElement.Parse(RoleDocument("<rollovertextid>STR_LONG</rollovertextid><shortrollovertextid>STR_SHORT</shortrollovertextid>")));
                host.Set("_modXmlRoot", System.Xml.Linq.XElement.Parse("<protomods><unit name='B'/></protomods>"));
                host.Call("BuildEditorPanel", "A", true);
            }
            else PrepareSnippetActions(host);
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var text = Assert.IsType<TextBox>(fields["rollovertextid"]);
            RolloverAssertions.Configured(text);
            Assert.Equal(!original, text.IsEnabled);
            Assert.Equal(300, fields["shortrollovertextid"].Width);
            var layout = text.GetLogicalAncestors().OfType<AoMDivineDataEditor.Controls.IdentityFieldPanel>().Single();
            foreach (var width in new[] { 980d, 680d, 580d, 980d })
            { layout.Width = width; await RoleSettle(host); FormLayoutAssertions.NoOverlaps(layout, "Unit rollover identity"); IdentityPreviewAssertions.Compact(text); }
            Assert.False(Field<bool>(host, "_isDirty"));
            await RolloverAssertions.Stress(host, text);
        }, RoleDocument("<rollovertextid>STR_LONG</rollovertextid><shortrollovertextid>STR_SHORT</shortrollovertextid>"), RoleFixture);
}
