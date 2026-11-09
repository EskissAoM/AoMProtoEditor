using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AdditiveReadingTests
{
    [Fact]
    public void RemovedActionItemRetainsItsActionNameForTheSharedRemovalsView()
    {
        var result = AdditiveXmlReader.Evaluate(
            XElement.Parse("<proto><unit name='A'><protoaction><name>HandAttack</name><damagebonus type='AbstractCavalry'>1.25</damagebonus></protoaction></unit></proto>"),
            XElement.Parse("<protomods><unit name='A'><protoaction><name>HandAttack</name><damagebonus type='AbstractCavalry' mergeMode='remove'/></protoaction></unit></protomods>"), AdditiveFamily.Proto);
        var removed = Assert.Single(result.Changes, c => c.Mode == "remove");
        Assert.Equal("HandAttack", removed.ActionName);
        var label = (string)typeof(Windows.ProtoEditorWindow).GetMethod("UnitChangeLabel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [removed, false])!;
        Assert.Contains("Actions → HandAttack", label);
        Assert.DoesNotContain("<", label);
    }

    [Fact]
    public void ModCheckboxFrameDoesNotReparentTheTacticsOwnedControl()
        => AvaloniaTestHost.Run(() =>
        {
            var host = new Grid();
            var checkbox = new CheckBox { Content = "Default attack", IsChecked = false };
            Grid.SetColumn(checkbox, 2);
            host.Children.Add(checkbox);
            var change = new AdditiveChange(Guid.NewGuid(), "/unit/protoaction/defaultattack", "modify",
                XElement.Parse("<defaultattack>1</defaultattack>"), XElement.Parse("<defaultattack>0</defaultattack>"), XElement.Parse("<defaultattack>0</defaultattack>"));
            AdditiveChangePresentation.Decorate(checkbox, change);
            var frame = checkbox.GetValue(AdornerLayer.AdornerProperty);
            Assert.IsType<Border>(frame);
            AdditiveChangePresentation.Decorate(checkbox, change);
            Assert.Same(frame, checkbox.GetValue(AdornerLayer.AdornerProperty));
            Assert.Same(host, checkbox.Parent);
            Assert.Single(host.Children);
            Assert.Equal(2, Grid.GetColumn(checkbox));
        });

    private static XElement Normalize(XElement node) => new(node.Name, node.Attributes().OrderBy(a => a.Name.ToString()),
        node.HasElements ? node.Elements().Select(Normalize) : node.Value.Trim());

    [Theory]
    [InlineData(AdditiveFamily.Proto)]
    [InlineData(AdditiveFamily.TechTree)]
    [InlineData(AdditiveFamily.Powers)]
    public void MatchesActualGameOutputWithoutMutatingSources(AdditiveFamily family)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Additive");
        var original = XElement.Load(Path.Combine(path, family + ".base.xml"));
        var patch = XElement.Load(Path.Combine(path, family + ".patch.xml"));
        var expected = XElement.Load(Path.Combine(path, family + ".expected.xml"));
        var oldOriginal = original.ToString(); var oldPatch = patch.ToString();
        var result = AdditiveXmlReader.Evaluate(original, patch, family);
        // The game also orders technology definitions during data loading. Compare
        // by immutable entry identity, preserving every sibling order inside entries.
        Assert.Equal(expected.Elements().Count(), result.Effective.Elements().Count());
        foreach (var entry in expected.Elements())
        {
            var actual = result.Effective.Elements().Single(e => (string?)e.Attribute("name") == (string?)entry.Attribute("name"));
            Assert.True(XNode.DeepEquals(Normalize(entry), Normalize(actual)), actual.ToString());
        }
        Assert.Equal(oldOriginal, original.ToString()); Assert.Equal(oldPatch, patch.ToString());
        Assert.NotEmpty(result.Changes);
        Assert.Equal(result.Changes.Count, result.Changes.Select(c => c.Handle).Distinct().Count());
        if (family == AdditiveFamily.TechTree) Assert.Contains(result.Diagnostics, d => d.Contains("first sibling"));
    }

    [Fact]
    public void ClassificationDoesNotGuessWhenBaseIsUnavailable()
    {
        var original = XElement.Parse("<proto><unit name='A'/></proto>");
        Assert.Equal(AdditiveEntryKind.Unresolved, AdditiveXmlReader.Classify(null, "A", true));
        Assert.Equal(AdditiveEntryKind.Modified, AdditiveXmlReader.Classify(original, "a", true));
        Assert.Equal(AdditiveEntryKind.Custom, AdditiveXmlReader.Classify(original, "B", true));
    }

    [Theory]
    [InlineData("remove", 0)]
    [InlineData("add", 2)]
    [InlineData("replace", 1)]
    public void WholeEntryOperationsAreNotFlattened(string mode, int count)
    {
        var r = AdditiveXmlReader.Evaluate(XElement.Parse("<proto><unit name='A'><los>10</los></unit></proto>"),
            XElement.Parse($"<protomods><unit name='A' mergeMode='{mode}'><los>20</los></unit></protomods>"), AdditiveFamily.Proto);
        Assert.Equal(count, r.Effective.Elements().Count());
        if (mode == "replace") Assert.Equal("20", r.Effective.Element("unit")!.Element("los")!.Value);
    }

    [Fact]
    public void InvalidDirectiveIsPreservedAndDiagnosedAndEqualOverridesRetainProvenance()
    {
        var patch = XElement.Parse("<protomods><unit name='A'><los>10</los><future mergeMode='bad'>x</future></unit></protomods>");
        var r = AdditiveXmlReader.Evaluate(XElement.Parse("<proto><unit name='A'><los>10</los></unit></proto>"),patch,AdditiveFamily.Proto);
        Assert.Contains(r.Diagnostics, d => d.Contains("unsupported mergeMode"));
        Assert.Contains(r.Changes, c => c.Patch.Name == "los");
        Assert.Equal("bad", (string?)patch.Descendants("future").Single().Attribute("mergeMode"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualUnitFormReadsProjectionWhileRawPaneKeepsPatchAndCustomStaysWritable(bool cached)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><!--keep--><unit name='A'><trainpoints>20</trainpoints><flag mergeMode='remove'>CreateUnitGroupAutomatically</flag></unit><unit name='B'><trainpoints>7</trainpoints></unit></protomods>");
        var bytes = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                // Exercise the actual extractor shape used for both fresh BAR loading and cache loading.
                var combined = "<protos><proto><unit name='A'><trainpoints>15</trainpoints><maxhitpoints>99</maxhitpoints><flag>CreateUnitGroupAutomatically</flag></unit></proto><proto><unit name='Other'/></proto><protounitcommands><protounitcommand name='NotAUnit'/></protounitcommands></protos>";
                var extracted = ProtoDataExtractor.ExtractProtoData(combined);
                if (cached) extracted = ProtoDataExtractor.ExtractProtoData(extracted.Root.ToString());
                host.Set("_barData", extracted.Data);
                host.Set("_barXmlRoot", extracted.Root);
                host.Call("InitializeEditorTabs");
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("BuildEditorPanel", "A", true);
                host.Show(); Dispatcher.UIThread.RunJobs(); await Task.Delay(50); Dispatcher.UIThread.RunJobs();
                var raw = host.FindControl<TextEditor>("_xmlPreviewText")!;
                Assert.Contains("20", raw.Text); Assert.DoesNotContain("99", raw.Text); Assert.True(raw.IsReadOnly);
                Assert.False(host.FindControl<Button>("_editUnitXmlButton")!.IsEnabled);
                var fields = (Dictionary<string, Avalonia.Controls.Control>)typeof(Windows.ProtoEditorWindow)
                    .GetField("_fieldControls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host)!;
                Assert.Equal("99", Assert.IsType<TextBox>(fields["maxhitpoints"]).Text);
                host.Call("RefreshUnitList");
                var items = host.FindControl<ListBox>("_unitList")!.Items.Cast<object>().ToArray();
                var list = host.FindControl<ListBox>("_unitList")!;
                Assert.Equal(new[] { "Custom", "B", "Modified", "A" }, items.Select(i => (string?)i.GetType().GetProperty("DisplayName")!.GetValue(i)));
                host.UpdateLayout();
                var header = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
                Assert.True(header.IsEnabled); Assert.False(header.Focusable); Assert.True(row.IsEnabled);
                list.SelectedItem = items[3];
                var point = header.TranslatePoint(new Point(12, header.Bounds.Height / 2), host)!.Value;
                host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.False(header.IsSelected);
                Assert.Equal("A", list.SelectedItem!.GetType().GetProperty("UnitName")!.GetValue(list.SelectedItem));
                Assert.Equal(3, list.Items.Count); // Custom header remains, its unit is collapsed.
                host.FindControl<TextBox>("_searchBox")!.Text = "B"; host.Call("FilterUnitList");
                Assert.Single(list.Items); // Searching does not expand Custom.
                host.FindControl<TextBox>("_searchBox")!.Text = ""; host.Call("FilterUnitList");
                Assert.Equal(3, list.Items.Count);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Custom);
                Assert.Equal(4, list.Items.Count);
                Assert.Equal(Color.Parse("#A78BFA"), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<TextBox>(fields["trainpoints"]).BorderBrush).Color);
                var points = Assert.IsType<TextBox>(fields["trainpoints"]);
                Assert.Equal("Original: 15\nModified: 20", ToolTip.GetTip(points));
                Assert.Contains(points.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_BorderElement" &&
                    border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A78BFA") && border.BorderThickness.Left > 0);
                var removedPanel = host.FindControl<StackPanel>("_unitStatsNotices")!;
                var removed = Assert.Single(removedPanel.GetLogicalDescendants().OfType<Expander>());
                Assert.True(removedPanel.IsEffectivelyVisible);
                Assert.Equal("Removed (1)", Assert.IsType<TextBlock>(removed.Header).Text);
                Assert.False(removed.IsExpanded);
                Assert.Equal(new[] { "Flags → Create unit group automatically" }, Assert.IsType<StackPanel>(removed.Content).Children.OfType<TextBlock>().Select(row => row.Text));
                host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
                Assert.Empty(host.FindControl<StackPanel>("_unitActionNotices")!.Children);
                host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                var read = Assert.IsType<AdditiveReadResult>(typeof(Windows.ProtoEditorWindow).GetField("_unitAdditiveRead", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host));
                Assert.DoesNotContain(read.Effective.Descendants("flag"), f => f.Value == "CreateUnitGroupAutomatically");
                Assert.Equal(AdditiveEntryKind.Modified, typeof(Windows.ProtoEditorWindow).GetField("_unitAdditiveKind", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host));
                var projection = Assert.IsType<XElement>(host.Call("ResolveUnitForReading", "A"));
                Assert.Equal("99", projection.Element("maxhitpoints")!.Value);
                host.Call("BuildEditorPanel", "B", false); Dispatcher.UIThread.RunJobs();
                Assert.True(host.FindControl<Button>("_editUnitXmlButton")!.IsEnabled);
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 0;
                host.Call("BuildEditorPanel", "A", false); Dispatcher.UIThread.RunJobs();
                Assert.Contains("15", raw.Text); Assert.Contains("99", raw.Text);
                Assert.Equal(bytes, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
