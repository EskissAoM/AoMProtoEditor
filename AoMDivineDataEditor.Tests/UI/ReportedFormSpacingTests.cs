using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void HotkeyAndResourceMapUseTheSameFourPixelGapWithoutChangingData()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            await WaitRemovalReady(host);
            host.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var hotkey = fields["hotkeycontext"];
            var hotkeyRow = Assert.IsType<Grid>(hotkey.Parent);
            var hotkeyCross = Assert.Single(hotkeyRow.Children.OfType<Button>());
            var favor = fields["carrycapacity:Favor"];
            var resourcePair = Assert.IsType<StackPanel>(favor.Parent);
            var resourceCross = Assert.Single(resourcePair.Children.OfType<Button>());
            Assert.IsType<WrapPanel>(resourcePair.Parent);
            foreach (var (field, cross) in new[] { (hotkey, hotkeyCross), (favor, resourceCross) })
            {
                Assert.True(field.Bounds.Width > 0);
                var point = field.TranslatePoint(default, host)!.Value;
                var crossPoint = cross.TranslatePoint(default, host)!.Value;
                Assert.InRange(crossPoint.X - point.X - field.Bounds.Width, 3.9, 4.1);
                Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - point.Y - field.Bounds.Height / 2), 0, .5);
            }
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, Initial.Replace("<unittype>Unit</unittype>",
            "<unittype>Unit</unittype><hotkeycontext>BuildAztecAccel</hotkeycontext><carrycapacity resourcetype='Food'>15</carrycapacity><carrycapacity resourcetype='Wood'>10</carrycapacity><carrycapacity resourcetype='Gold'>10</carrycapacity><carrycapacity resourcetype='Favor'>0</carrycapacity>", StringComparison.Ordinal));
}

public sealed class TerrainCreepWrappingTests
{
    [Fact]
    public void ExpandedPlacementOptionsWrapTogetherAndKeepTheirFieldCrossGapAndData()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("major_gods_mods.xml", "<civsmods><civ><name>A</name><terraincreeps><terraincreep creep='GaiaCreep' maxradius='2' growthrate='10' decayrate='2' strength='3' minupdateinterval='10' maxupdateinterval='100' buildability='unbuildableenemies' preventplacement='AbstractArmory' future='keep'><protounit>BaseUnit</protounit></terraincreep></terraincreeps></civ></civsmods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, protoUnitNames: ["BaseUnit"],
                unitTypeNames: ["AbstractArmory", "CustomType"], terrainCreepNames: ["GaiaCreep"]);
            var window = new Window { Width = 1100, Height = 850, Content = view };
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A");
                window.Show(); view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex = 2;
                foreach (var width in new[] { 1100, 850, 1100 })
                {
                    window.Width = width;
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var selector = view.GetVisualDescendants().OfType<AutoCompleteBox>()
                        .Single(box => box.IsEffectivelyVisible && box.Text == "AbstractArmory");
                    var pair = Assert.IsType<StackPanel>(selector.Parent);
                    var group = Assert.IsType<StackPanel>(pair.Parent);
                    Assert.Contains(group.Children.OfType<TextBlock>(), label => label.Text == "Prevent Placement of");
                    var wrap = Assert.IsType<WrapPanel>(group.Parent);
                    var interval = wrap.Children[0];
                    Assert.True(group.Bounds.Y >= interval.Bounds.Bottom);
                    foreach (var child in wrap.Children)
                    {
                        Assert.True(child.Bounds.Width > 0);
                        Assert.InRange(child.Bounds.X, 0, wrap.Bounds.Width);
                        Assert.True(child.Bounds.Right <= wrap.Bounds.Width + .5);
                    }
                    var cross = Assert.Single(pair.Children.OfType<Button>());
                    var point = selector.TranslatePoint(default, window)!.Value;
                    var crossPoint = cross.TranslatePoint(default, window)!.Value;
                    Assert.InRange(crossPoint.X - point.X - selector.Bounds.Width, 3.9, 4.1);
                    Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - point.Y - selector.Bounds.Height / 2), 0, .5);
                    Assert.False(view.IsDirty);
                    Assert.Equal(before, File.ReadAllBytes(path));
                }
                var errors = new List<string>();
                var saving = view.SaveAsync();
                for (var i = 0; !saving.IsCompleted && i < 500; i++)
                {
                    foreach (var prompt in window.OwnedWindows.OfType<Prompt>().ToArray())
                    {
                        errors.Add(prompt.PromptTitle + ": " + prompt.PromptText);
                        prompt.Close();
                    }
                    await Task.Delay(10);
                }
                Assert.Empty(errors);
                Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal("keep", (string?)XDocument.Load(path).Descendants("terraincreep").Single().Attribute("future"));
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }
}
