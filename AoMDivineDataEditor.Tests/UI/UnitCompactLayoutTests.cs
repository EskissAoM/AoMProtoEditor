using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private const string CompactStats = """
        <bloodgroupoverride>ExistingGroup</bloodgroupoverride><bloodscalemodify>1</bloodscalemodify><bonescalemodify>1</bonescalemodify>
        <damageshading type="blood" threshold="1" rate="1" time="1000"/>
        <creationfadetime initalpha="0.5">2</creationfadetime><directionalarmor angle="20" value="0.2"/>
        <decay delay="2" duration="3"/><killreward resourcetype="Food">1</killreward>
        <initialunitaistance>Defensive</initialunitaistance><gatherratemultiplier>1</gatherratemultiplier>
        <initialshading type="cocoon" factor="1"/><buildlimit>1</buildlimit><contain>Unit</contain><maxcontained>0</maxcontained>
        <heightbob period="2" magnitude="1"/><partisantype>B</partisantype><partisancount>2</partisancount>
        <placementobstructionradiusx>1</placementobstructionradiusx><placementobstructionradiusz>2</placementobstructionradiusz>
        <stealthdetectionradius>3</stealthdetectionradius><stealthrevealselfradius>4</stealthrevealselfradius><stealthshowsilhouetteradius>5</stealthshowsilhouetteradius>
        """;

    public static IEnumerable<object[]> CompactStatsRows =>
    [
        [new[] { "bloodgroupoverride", "bloodscalemodify", "bonescalemodify" }],
        [new[] { "damageshading.type", "damageshading.threshold", "damageshading.rate", "damageshading.time" }],
        [new[] { "initialshading.type", "initialshading.factor" }],
        [new[] { "creationfadetime.value", "creationfadetime.initalpha" }],
        [new[] { "directionalarmor.angle", "directionalarmor.value" }],
        [new[] { "decay.delay", "decay.duration" }],
        [new[] { "heightbob.period", "heightbob.magnitude" }],
        [new[] { "partisantype", "partisancount" }],
        [new[] { "placementobstructionradiusx", "placementobstructionradiusz" }],
        [new[] { "stealthdetectionradius", "stealthrevealselfradius", "stealthshowsilhouetteradius" }],
        // Resource-row layout is covered for all six families in UnitResourceRowsTests.
    ];

    [Theory]
    [MemberData(nameof(CompactStatsRows))]
    public void UnitCompactStatsRowsFlowTogetherAndWrapCompleteGroupsWithMeasuredGaps(string[] keys)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Width = 1900; host.Height = 900; await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var inputs = keys.Select(key => fields[key]).ToArray();
            var first = inputs[0].TranslatePoint(default, host)!.Value;
            foreach (var input in inputs)
                Assert.InRange(Math.Abs(input.TranslatePoint(default, host)!.Value.Y - first.Y), 0, .5);
            var wrap = Assert.IsType<WrapPanel>(inputs[0].Parent!.Parent);
            var title = Assert.IsType<StackPanel>(wrap.Children[0]);
            Assert.Equal(EditorFormLayout.LabelColumnWidth, title.Bounds.Width);
            for (var i = 1; i < inputs.Length; i++)
            {
                var label = Assert.Single(Assert.IsType<StackPanel>(inputs[i].Parent).Children.OfType<TextBlock>());
                var labelPoint = label.TranslatePoint(default, host)!.Value;
                var previous = inputs[i - 1].TranslatePoint(default, host)!.Value;
                Assert.InRange(labelPoint.X - previous.X - inputs[i - 1].Bounds.Width, 11.9, 12.1);
            }
            var lastPair = Assert.IsType<StackPanel>(inputs[^1].Parent);
            var cross = Assert.Single(lastPair.Children.OfType<Button>());
            var last = inputs[^1].TranslatePoint(default, host)!.Value;
            var crossPoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - last.X - inputs[^1].Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - last.Y - inputs[^1].Bounds.Height / 2), 0, .5);
            host.Width = 1000; await RoleSettle(host);
            foreach (var group in wrap.Children)
                Assert.True(group.Bounds.Right <= wrap.Bounds.Width + .5, $"{keys[0]}: {group.Bounds} outside {wrap.Bounds}");
            Assert.Same(lastPair, cross.Parent);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(CompactStats), RoleFixture);

    [Fact]
    public void UnitSimpleOptionalFieldsShareOneLabelColumnWithoutLengthBasedOffsets()
        => WithHost(async (_, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var stance = fields["initialunitaistance"].TranslatePoint(default, host)!.Value;
            var multiplier = fields["gatherratemultiplier"].TranslatePoint(default, host)!.Value;
            Assert.InRange(Math.Abs(stance.X - multiplier.X), 0, .5);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(CompactStats), RoleFixture);

    [Fact]
    public void UnitShadingAndStructureRowsUseTheSharedLabelColumnAndCompactEnums()
        => WithHost(async (_, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var expected = fields["initialunitaistance"].TranslatePoint(default, host)!.Value.X;
            foreach (var key in new[] { "initialshading.type", "damageshading.type", "buildlimit", "maxcontained" })
            {
                var field = fields[key];
                var group = Assert.IsType<StackPanel>(field.Parent);
                var leading = group.Children.OfType<TextBlock>().SingleOrDefault();
                var start = (Control?)leading ?? field;
                Assert.InRange(Math.Abs(start.TranslatePoint(default, host)!.Value.X - expected), 0, .5);
            }
            foreach (var key in new[] { "initialshading.type", "damageshading.type" })
                AssertRole(fields[key], EditorFieldWidthRole.ShortEnum);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(CompactStats), RoleFixture);

    [Fact]
    public void UnitCompactLayoutKeepsEditAndModuleRemovalSaveReopenBehavior()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var original = XDocument.Load(fixture.ProtoPath);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            ((TextBox)fields["decay.duration"]).Text = "7";
            var killModule = Assert.IsType<Grid>(StatsModule(host, "killrewarddata"));
            Assert.Single(killModule.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RoleSettle(host); Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            var unit = saved.Root!.Element("unit")!;
            Assert.Equal("7", (string?)unit.Element("decay")!.Attribute("duration")); Assert.Empty(unit.Elements("killreward"));
            Assert.Equal("ExistingGroup", unit.Element("bloodgroupoverride")!.Value);
            Assert.True(XNode.DeepEquals(original.Root!.Element("unit")!.Element("future"), unit.Element("future")));
            Assert.True(XNode.DeepEquals(original.Root.Elements("unit").Last(), saved.Root.Elements("unit").Last()));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal("7", ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["decay.duration"]).Text);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(CompactStats), RoleFixture);
}
