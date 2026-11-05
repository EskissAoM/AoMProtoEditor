using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private const string RoleUnit = "CustomUnitWithAnExtremelyLongReferenceName";
    private const string RoleTech = "CustomTechnologyWithAnExtremelyLongReferenceName";
    private const string RoleCommand = "CustomCommandWithAnExtremelyLongReferenceName";
    private static string RoleDocument(string extra = "") => $$"""
        <protomods future="root"><unit name="A" future="owner">
        <maxhitpoints>10</maxhitpoints><initialhitpoints>10</initialhitpoints>
        <obstructionradiusx>2.5</obstructionradiusx><obstructionradiusz>3</obstructionradiusz><turnrate>4</turnrate>
        <trainpoints>12</trainpoints><populationcount>2</populationcount><los>14</los>
        <unitaitype>Civilian</unitaitype><impacttype>Unarmored</impacttype><weightclass>1</weightclass>
        <movementtype>land</movementtype><maxvelocity>4</maxvelocity><maxrunvelocity>5</maxrunvelocity>
        <icon>resources/deep/custom_icon.png</icon><animfile>custom_model.xml</animfile><soundsetfile>custom_sound.xml</soundsetfile>
        <unittype>Unit</unittype><flag>CollidesWithProjectiles</flag>
        <socketunittype>CheckpointType</socketunittype><deadreplacement>{{RoleUnit}}</deadreplacement>
        <train row="0" column="1">{{RoleUnit}}</train><tech row="0" column="2">{{RoleTech}}</tech>
        <command row="0" column="3">{{RoleCommand}}</command>
        <carrycapacity resourcetype="Food">15</carrycapacity><carrycapacity resourcetype="Wood">10</carrycapacity>
        <carrycapacity resourcetype="Gold">10</carrycapacity><carrycapacity resourcetype="Favor">0</carrycapacity>
        {{extra}}<!--keep--><future keep="yes"><child arbitrary="kept"/></future></unit>
        <unit name="{{RoleUnit}}"><maxhitpoints>9</maxhitpoints></unit>
        <unit name="B"><maxhitpoints>20</maxhitpoints><future sibling="yes"/></unit></protomods>
        """;
    private static void RoleFixture(BaselineSafetyFixture fixture)
        => fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml",
            $"<techtreemods><tech name='{RoleTech}'><researchpoints>0</researchpoints></tech></techtreemods>");
    private static T Assignment<T>(object row, string property) => (T)row.GetType().GetProperty(property)!.GetValue(row)!;
    private static object RoleAssignment(BaselineHost host, string field)
        => Assert.Single(Field<IList>(host, field).Cast<object>());
    private static async Task RoleSettle(BaselineHost host)
    {
        // WithHost builds directly; normal document activation also dismisses
        // this overlay before a user can interact with the structured editor.
        host.FindControl<Border>("_emptyDocumentOverlay")!.IsVisible = false;
        await WaitRemovalReady(host);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); host.UpdateLayout();
        await Task.Delay(30);
    }
    private static void AssertRole(Control control, EditorFieldWidthRole role)
    {
        Assert.Equal(EditorFormLayout.WidthFor(role), control.Width);
        Assert.Equal(control.Width, control.MinWidth); Assert.Equal(control.Width, control.MaxWidth);
        Assert.Equal(control.Width, control.Bounds.Width);
        Assert.Equal(EditorFormLayout.FieldHeight, control.Bounds.Height);
        Assert.True(EditorFieldInteraction.IsConfigured(control));
    }
    private static void RoleClick(BaselineHost host, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width * .6, control.Bounds.Height / 2), host)!.Value;
        host.MouseMove(point); host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitFieldRolesLoadOriginalAndCustomRecordsWithoutChangingThem(bool original)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            if (original)
            {
                host.Set("_barXmlRoot", XElement.Parse(RoleDocument()));
                host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
                host.Call("BuildEditorPanel", "A", true);
            }
            host.Width = 1100; host.Height = 850; await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            foreach (var key in new[] { "maxhitpoints", "obstructionradiusx", "obstructionradiusz", "turnrate", "trainpoints", "populationcount", "los", "maxvelocity", "maxrunvelocity", "weightclass" })
                AssertRole(fields[key], EditorFieldWidthRole.Numeric);
            foreach (var key in new[] { "displaynameid", "editornameid" })
                if (fields.TryGetValue(key, out var control))
                {
                    var width = EditorFormLayout.WidthFor(EditorFieldWidthRole.SingleLineText);
                    Assert.Equal(width, control.Width);
                    Assert.Equal(0, control.MinWidth);
                    Assert.InRange(control.MaxWidth, 1, width);
                    Assert.InRange(control.Bounds.Width, 1, control.MaxWidth);
                    Assert.Equal(EditorFormLayout.FieldHeight, control.Bounds.Height);
                    Assert.True(EditorFieldInteraction.IsConfigured(control));
                    IdentityPreviewAssertions.Compact((TextBox)fields["rollovertextid"]);
                }
            AssertRole(fields["movementtype"], EditorFieldWidthRole.ShortEnum);
            AssertRole(fields["impacttype"], EditorFieldWidthRole.LongEnum);
            AssertRole(fields["unitaitype"], EditorFieldWidthRole.LongEnum);
            AssertRole(fields["icon"], EditorFieldWidthRole.AssetPath);
            AssertRole(fields["deadreplacement"], EditorFieldWidthRole.Reference);
            Assert.Equal(RoleUnit, ((AutoCompleteBox)fields["deadreplacement"]).Text);
            Assert.Equal(original, Field<bool>(host, "_isReadOnly"));
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            host.Call("BuildEditorPanel", "B", true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void ManagedActionReferencesAndTacticsUseTheirSharedRolesWithoutDirtying()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Call("SyncManagedProtoActionStatsEditor", "transformcommand", RoleCommand);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            Assert.Equal(200, fields["transformcommand"].Width); Assert.False(fields["transformcommand"].IsEnabled);
            host.Call("SyncManagedProtoActionStatsEditor", "transformcommand", RoleCommand + "Renamed");
            Assert.Equal(200, fields["transformcommand"].Width);
            Assert.Equal(240, fields["tactics"].Width); Assert.True(EditorFieldInteraction.IsConfigured(fields["tactics"]));
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument("<tactics>test.tactics</tactics>"), RoleFixture);

    [Fact]
    public void UnitNumericFirstClickSelectsAllWithoutDirtyingAndARealEditStillSaves()
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1280; host.Height = 900;
            await RoleSettle(host);
            var before = XDocument.Load(fixture.ProtoPath);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var input = Assert.IsType<TextBox>(fields["maxhitpoints"]);
            host.FindControl<TextBox>("_unitNameBox")!.Focus();
            input.BringIntoView(); await RoleSettle(host);
            RoleClick(host, input); await RoleSettle(host);
            Assert.Equal(2, Math.Abs(input.SelectionEnd - input.SelectionStart));
            Assert.False(Field<bool>(host, "_isDirty"));
            host.KeyTextInput("17"); await RoleSettle(host);
            Assert.True(Field<bool>(host, "_isDirty")); Assert.Empty(await Save(host));
            var after = XDocument.Load(fixture.ProtoPath);
            Assert.Equal("17", after.Root!.Element("unit")!.Element("maxhitpoints")!.Value);
            Assert.True(XNode.DeepEquals(before.Root!.Element("unit")!.Element("future"), after.Root.Element("unit")!.Element("future")));
            Assert.True(XNode.DeepEquals(before.Root.Elements("unit").Last(), after.Root.Elements("unit").Last()));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal("17", ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData("_trainCommandRows", 4)]
    [InlineData("_techCommandRows", 4)]
    [InlineData("_unitCommandRows", 2)]
    public void UnitAssignmentsWrapAsWholeGroupsWithLongReferencesAndFourPixelCrossGaps(string rows, int tab)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Width = 1000; host.Height = 850;
            Field<TabControl>(host, "_editorTabs").SelectedIndex = tab;
            await RoleSettle(host); Assert.True(Raw(host).Bounds.Width > 0);
            var row = RoleAssignment(host, rows);
            var panel = Assert.IsType<WrapPanel>(Assignment<Panel>(row, "RowPanel"));
            var value = Assignment<AutoCompleteBox>(row, "ValueAcb");
            AssertRole(value, EditorFieldWidthRole.Reference);
            AssertRole(Assignment<Control>(row, "RowEditor"), EditorFieldWidthRole.Numeric);
            AssertRole(Assignment<Control>(row, "ColumnEditor"), EditorFieldWidthRole.Numeric);
            var pair = Assert.IsType<StackPanel>(value.Parent);
            var cross = Assert.Single(pair.Children.OfType<Button>());
            var point = value.TranslatePoint(default, host)!.Value;
            var crossPoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - point.X - value.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - point.Y - value.Bounds.Height / 2), 0, .5);
            foreach (var group in panel.Children)
            {
                Assert.True(group.Bounds.Right <= panel.Bounds.Width + .5);
                Assert.True(group.Bounds.Width > 0);
            }
            value.RaiseEvent(new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent));
            var tip = Assert.IsType<ScrollViewer>(ToolTip.GetTip(value));
            Assert.Equal(value.Text, Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(tip.Content).Children[0]).Text);
            var input = value.GetVisualDescendants().OfType<TextBox>().First();
            input.BringIntoView(); await RoleSettle(host);
            host.FindControl<TextBox>("_unitNameBox")!.Focus();
            RoleClick(host, input); await RoleSettle(host);
            Assert.Equal(value.Text!.Length, Math.Abs(input.SelectionEnd - input.SelectionStart));
            Assert.InRange(input.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.X, 0, .5);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void UnitAssignmentEditRemoveSaveAndReopenPreserveOtherXml()
        => WithHost(async (fixture, host) =>
        {
            var before = XDocument.Load(fixture.ProtoPath);
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 4; await RoleSettle(host);
            var row = RoleAssignment(host, "_trainCommandRows");
            Assignment<AutoCompleteBox>(row, "ValueAcb").Text = "B";
            ((TextBox)Assignment<Control>(row, "RowEditor")).Text = "2";
            await RoleSettle(host); Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            Assert.Equal("B", saved.Root!.Element("unit")!.Element("train")!.Value);
            Assert.Equal("2", (string?)saved.Root.Element("unit")!.Element("train")!.Attribute("row"));
            Assert.True(XNode.DeepEquals(before.Root!.Element("unit")!.Element("tech"), saved.Root.Element("unit")!.Element("tech")));
            Assert.True(XNode.DeepEquals(before.Root.Element("unit")!.Element("future"), saved.Root.Element("unit")!.Element("future")));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = RoleAssignment(host, "_trainCommandRows");
            var value = Assignment<AutoCompleteBox>(row, "ValueAcb"); Assert.Equal("B", value.Text);
            Assert.Single(Assert.IsType<StackPanel>(value.Parent).Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Null(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("train"));
            Assert.True(XNode.DeepEquals(before.Root.Elements("unit").Last(), XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").Last()));
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void UnitRegenModifierWrapsWithItsCrossAndRemovalPreservesTheOtherModifier()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var idle = Assert.IsType<TextBox>(fields["unitregen.idleTimeout"]);
            var pair = Assert.IsType<StackPanel>(idle.Parent!.Parent);
            var cross = Assert.Single(pair.Children.OfType<Button>());
            Assert.IsType<WrapPanel>(pair.Parent);
            var point = idle.TranslatePoint(default, host)!.Value;
            var crossPoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - point.X - idle.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - point.Y - idle.Bounds.Height / 2), 0, .5);
            Assert.False(Field<bool>(host, "_isDirty"));
            cross.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.False(fields.ContainsKey("unitregen.idleTimeout")); Assert.Empty(await Save(host));
            var regen = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("unitregen")!;
            Assert.DoesNotContain(regen.Attributes(), attribute => attribute.Name.LocalName.Equals("idletimeout", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(regen.Attributes(), attribute => attribute.Name.LocalName.Equals("damagetimeout", StringComparison.OrdinalIgnoreCase) && attribute.Value == "3");
        }, RoleDocument("<unitregen idletimeout='2' damagetimeout='3'>1</unitregen>"), RoleFixture);

    [Fact]
    public void StructuredUnitStatsBuildWithExplicitRolesAndDoNotDirtyTheRecord()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            AssertRole(fields["recharge.value"], EditorFieldWidthRole.Numeric);
            AssertRole(fields["auxrecharge.value"], EditorFieldWidthRole.Numeric);
            AssertRole(fields["respawntraindata.mode"], EditorFieldWidthRole.LongEnum);
            foreach (var key in new[] { "recharge.mode", "auxrecharge.mode" })
                Assert.IsType<StackPanel>(fields[key].Parent);
            var socket = fields["socketunittype"];
            var socketPair = Assert.IsType<StackPanel>(socket.Parent);
            var socketCross = Assert.Single(socketPair.Children.OfType<Button>());
            var socketPoint = socket.TranslatePoint(default, host)!.Value;
            var socketCrossPoint = socketCross.TranslatePoint(default, host)!.Value;
            Assert.InRange(socketCrossPoint.X - socketPoint.X - socket.Bounds.Width, 3.9, 4.1);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument($"""
            <dependentunit x="1" z="-1">{RoleUnit}</dependentunit><spawn type="birth" count="1">{RoleUnit}</spawn>
            <resourceconversion fromresourcetype="Food" toresourcetype="Gold">1</resourceconversion>
            <buildlimit>3</buildlimit><sharedbuildlimitunittypes><unittype weight="1">{RoleUnit}</unittype></sharedbuildlimitunittypes>
            <maxcontained>3</maxcontained><contain>CheckpointType</contain>
            <veterancyranks><rank><numkills>2</numkills></rank></veterancyranks>
            <veterancybonus><rank id="1"><veterancymodify modifytype="Damage" damagetype="Hack">1.2</veterancymodify></rank></veterancybonus>
            <OnDamageModifiers><OnDamageModify modifyType="Damage" damageType="Hack">2</OnDamageModify></OnDamageModifiers>
            <replacement type="dead">{RoleUnit}</replacement>
            <recharge type="Damage" init="1">5</recharge><auxrecharge type="Kills" init="1">2</auxrecharge>
            <respawntraindata><targettype>CheckpointType</targettype><trainproto>{RoleUnit}</trainproto><respawntime>3</respawntime></respawntraindata>
            """), RoleFixture);
}
