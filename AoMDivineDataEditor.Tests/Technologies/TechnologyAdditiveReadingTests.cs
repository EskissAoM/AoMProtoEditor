using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void TechnologyChangeCardsGroupByTabAndRetainIndependentExpansionWithoutSaving()
    {
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods><tech name="A"><researchpoints mergeMode="replace">12</researchpoints>
              <cost resourcetype="Food" mergeMode="replace">80</cost><flag mergeMode="remove">CountsTowardEconomicScore</flag>
              <prereqs mergeMode="replace"><typecount unit="House" count="2" state="aliveState" operator="gte"/></prereqs>
              <effects><effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute" mergeMode="remove"><target type="ProtoUnit">VillagerGreek</target></effect></effects>
            </tech><tech name="Custom"><researchpoints>7</researchpoints></tech></techtreemods>
            """);
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            await InXmlWindow(editor, async window =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor); window.UpdateLayout();
                string[] names = ["_technologyPropertyNotices", "_technologyPrerequisiteNotices", "_technologyEffectNotices"];
                Expander Card(int index, string header) => Assert.Single(GetPrivateField<StackPanel>(editor, names[index]).GetLogicalDescendants().OfType<Expander>(),
                    card => Assert.IsType<TextBlock>(card.Header).Text == header);
                var replaced = Card(0, "Replaced (2)"); Card(0, "Removed (1)"); Card(1, "Replaced (1)"); Card(2, "Removed (1)");
                Assert.False(replaced.IsExpanded); Assert.True(replaced.IsEffectivelyEnabled);
                replaced.IsExpanded = true;
                TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
                Assert.False(Card(1, "Replaced (1)").IsExpanded); Assert.True(Card(1, "Replaced (1)").IsEffectivelyEnabled);
                TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
                Assert.False(Card(2, "Removed (1)").IsExpanded); Assert.True(Card(2, "Removed (1)").IsEffectivelyEnabled);
                foreach (var name in names)
                {
                    var panel = GetPrivateField<StackPanel>(editor, name);
                    Assert.Single(panel.GetLogicalAncestors().OfType<ScrollViewer>());
                    Assert.Empty(panel.GetLogicalDescendants().OfType<ScrollViewer>());
                }
                editor.SelectTechnology("Custom"); await AwaitTechnologyBuild(editor);
                Assert.All(names, name => Assert.Empty(GetPrivateField<StackPanel>(editor, name).Children));
                editor.SetModifiedMode(false); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.All(names, name => Assert.Empty(GetPrivateField<StackPanel>(editor, name).Children));
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.True(Card(0, "Replaced (2)").IsExpanded); Assert.False(Card(1, "Replaced (1)").IsExpanded);
                Assert.False(editor.IsDirty); Assert.True(await editor.SaveAsync()); Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }

    private static string TechnologyNoticeText(TechnologyEditorView editor, string panel)
        => string.Join("\n", GetPrivateField<StackPanel>(editor, panel).GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text));

    private const string AdditiveOriginal = """
        <techtree><tech name="A" type="Normal">
          <displaynameid>STR_ORIGINAL</displaynameid><researchpoints>30.0000</researchpoints>
          <cost resourcetype="Food">120.0000</cost><status>UNOBTAINABLE</status><flag>CountsTowardEconomicScore</flag>
          <prereqs><typecount unit="House" count="1" state="aliveState" operator="gte"/></prereqs>
          <effects>
            <effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute"><target type="ProtoUnit">VillagerGreek</target></effect>
            <effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute"><target type="ProtoUnit">VillagerNorse</target></effect>
            <effect type="FutureEffect" future="preserved"><payload>untouched</payload></effect>
          </effects>
        </tech></techtree>
        """;

    [Fact]
    public void RemovedPrerequisiteSummaryKeepsItsParentAndActualCondition()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><prereqs><typecount unit='House' count='1' state='aliveState' operator='gte' mergeMode='remove'/></prereqs></tech></techtreemods>");
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            var panel = GetPrivateField<StackPanel>(editor, "_technologyPrerequisiteNotices");
            var list = Assert.Single(panel.GetLogicalDescendants().OfType<Expander>());
            Assert.Equal("Prerequisites → Type count (House): 1", Assert.Single(Assert.IsType<StackPanel>(list.Content).Children.OfType<TextBlock>()).Text);
            Assert.False(editor.IsDirty); Assert.True(await editor.SaveAsync()); Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath));
        });
    }

    [Fact]
    public void VerifiedRuntimeTechnologyFixturesDisplayTheirExpectedEffectiveEntriesAndPreservePatchBytes()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Additive");
        using var fixture = new TechnologyEditorFixture(File.ReadAllText(Path.Combine(folder, "TechTree.patch.xml")));
        var original = XDocument.Load(Path.Combine(folder, "TechTree.base.xml"));
        var expected = XDocument.Load(Path.Combine(folder, "TechTree.expected.xml"));
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [original]); editor.SetModifiedMode(true);
            foreach (var name in editor.GetTechnologyNames(true))
            {
                editor.SelectTechnology(name); await AwaitTechnologyBuild(editor);
                var actual = GetPrivateField<XElement>(editor, "_current");
                Assert.Equal(TechnologyEditorView.SerializeTechnologyForComparison(FindTechnology(expected, name)),
                    TechnologyEditorView.SerializeTechnologyForComparison(actual));
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
            }
            Assert.True(await editor.SaveAsync());
            Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath));
        });
    }

    [Fact]
    public void AdditiveTechnologyDisplaysFullProjectionAuthoredPreviewAndRenderedMarkersWithoutDirtying()
    {
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods><!--source stays authored--><tech name="A">
              <researchpoints>20</researchpoints>
              <effects>
                <effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute"><target type="ProtoUnit">Hoplite</target></effect>
                <effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute" mergeMode="add"><target type="ProtoUnit">Ajax</target></effect>
              </effects>
            </tech><tech name="Custom"><researchpoints>7</researchpoints></tech></techtreemods>
            """);
        var sourceBytes = File.ReadAllBytes(fixture.TechtreePath);
        var original = XDocument.Parse(AdditiveOriginal);
        var originalXml = original.ToString(SaveOptions.DisableFormatting);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [original]);
            await InXmlWindow(editor, async window =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                await SettleTechnologyPresentation(window);
                var current = GetPrivateField<XElement>(editor, "_current");
                Assert.Equal("STR_ORIGINAL", current.Element("displaynameid")!.Value);
                Assert.Equal("120.0000", current.Element("cost")!.Value);
                Assert.NotNull(current.Element("prereqs"));
                Assert.Equal(new[] { "Hoplite", "VillagerNorse", "untouched", "Ajax" },
                    current.Element("effects")!.Elements().Select(effect => effect.Value));
                Assert.False(GetPrivateField<TextBlock>(editor, "_technologyAdditiveDiagnostics").IsVisible);
                Assert.Contains("Several effects match", TechnologyNoticeText(editor, "_technologyEffectNotices"));
                Assert.DoesNotContain("STR_ORIGINAL", Raw(editor).Text);
                Assert.Contains("mergeMode=\"add\"", Raw(editor).Text);
                Assert.True(Raw(editor).IsReadOnly);
                Assert.False(GetPrivateField<Button>(editor, "_editXmlButton").IsVisible);
                Assert.False(GetPrivateField<TextBox>(editor, "_techNameBox").IsEnabled);
                Assert.Equal(AdditiveEntryKind.Modified, editor.GetTechnologyEntryKind("A"));
                Assert.Equal(AdditiveEntryKind.Custom, editor.GetTechnologyEntryKind("Custom"));
                var research = GetPrivateField<StackPanel>(editor, "_propertiesPanel").GetLogicalDescendants().OfType<TextBox>()
                    .Single(field => field.Classes.Contains("mod-changed") && field.Text == "20");
                Assert.False(research.IsEnabled);
                Assert.Equal("Original: 30\nModified: 20", ToolTip.GetTip(research));
                Assert.Contains(research.GetVisualDescendants().OfType<Border>(), border =>
                    border.Name == "PART_BorderElement" && border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A78BFA"));
                var cards = GetPrivateField<StackPanel>(editor, "_effectsPanel").Children.OfType<Border>().ToArray();
                Assert.Equal(new[] { true, false, false, true }, cards.Select(card => card.Classes.Contains("mod-changed")));
                Assert.False(editor.IsDirty);
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal(sourceBytes, File.ReadAllBytes(fixture.TechtreePath));
                editor.SetModifiedMode(false); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.Equal("30.0000", GetPrivateField<XElement>(editor, "_current").Element("researchpoints")!.Value);
                Assert.Contains("STR_ORIGINAL", Raw(editor).Text);
                Assert.False(GetPrivateField<TextBlock>(editor, "_technologyAdditiveSummary").IsVisible);
                Assert.False(editor.IsDirty);
                Assert.Equal(originalXml, original.ToString(SaveOptions.DisableFormatting));
            });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavingCustomTechnologyPreservesPartialRemovalSelectorsAndReplacementIntent(bool raw)
    {
        const string patch = """
            <techtreemods custom="root"><!--keep-->
              <tech name="A"><cost resourcetype="Food" mergeMode="remove"/><cost resourcetype="Food" mergeMode="add">80</cost>
                <effects mergeMode="replace"><effect type="FutureEffect"><unknown keep="yes"/></effect></effects>
              </tech>
              <tech name="Custom"><researchpoints>7</researchpoints><future keep="yes"/></tech>
            </techtreemods>
            """;
        using var fixture = new TechnologyEditorFixture(patch);
        var savedPatch = XDocument.Load(fixture.TechtreePath, LoadOptions.PreserveWhitespace).Root!.Element("tech")!.ToString(SaveOptions.DisableFormatting);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            await InXmlWindow(editor, async _ =>
            {
                Assert.True(editor.CanModifyTechnologyDocument);
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.Contains("Entire effects list", TechnologyNoticeText(editor, "_technologyEffectNotices"));
                Assert.DoesNotContain("replaced", GetPrivateField<TextBlock>(editor, "_technologyAdditiveSummary").Text);
                Assert.Equal("80", GetPrivateField<XElement>(editor, "_current").Element("cost")!.Value);
                if (raw)
                {
                    await UnlockTechnologyXml(editor, "Custom");
                    Raw(editor).Text = Raw(editor).Text.Replace("<researchpoints>7</researchpoints>", "<researchpoints>9</researchpoints>");
                }
                else
                {
                    editor.SelectTechnology("Custom"); await AwaitTechnologyBuild(editor);
                    var research = GetPrivateField<StackPanel>(editor, "_propertiesPanel").GetLogicalDescendants().OfType<TextBox>().Single(field => field.Text == "7");
                    Assert.True(research.IsEnabled); research.Text = "9"; await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                }
                Assert.True(await SaveXmlChecked(editor));
                var saved = XDocument.Load(fixture.TechtreePath, LoadOptions.PreserveWhitespace);
                Assert.Equal(savedPatch, saved.Root!.Element("tech")!.ToString(SaveOptions.DisableFormatting));
                Assert.Equal("9", FindTechnology(saved, "Custom").Element("researchpoints")!.Value);
                editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.Contains("Entire effects list", TechnologyNoticeText(editor, "_technologyEffectNotices"));
                Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void AdditiveTechnologyMetadataPrerequisitesAndChipsRetainExplicitProvenance()
    {
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods><tech name="A" type="Normal" orderhint="5">
              <displaynameid>STR_CHANGED</displaynameid><status>ACTIVE</status>
              <techage>HeroicAge</techage><devotioncost devotiontype="Livestock">0</devotioncost>
              <flag mergeMode="add">MythTech</flag>
              <prereqs><typecount unit="House" count="2" state="aliveState" operator="gte"/></prereqs>
            </tech></techtreemods>
            """);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            var fields = editor.GetLogicalDescendants().OfType<Control>().Where(field => field.Classes.Contains("mod-changed")).ToArray();
            void Has(string original, string modified) => Assert.Contains(fields, field =>
                Equals(ToolTip.GetTip(field), $"Original: {original}\nModified: {modified}"));
            Assert.DoesNotContain(fields, field => Equals(ToolTip.GetTip(field), "Original: Normal\nModified: Normal"));
            Has("Absent", "5"); Has("STR_ORIGINAL", "STR_CHANGED");
            Has("UNOBTAINABLE", "ACTIVE"); Has("Absent", "HeroicAge"); Has("Absent", "0");
            Has("1", "2"); Has("Absent", "MythTech");
            Assert.DoesNotContain(fields, field => ToolTip.GetTip(field)?.ToString()?.Contains("CountsTowardEconomicScore") == true);
            Assert.False(editor.IsDirty);
        });
    }

    [Fact]
    public void RemovedTechnologyEffectShowsActualFirstMatchInEffectsAndCannotBeRenamedOrDeleted()
    {
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods><tech name="A"><effects>
              <effect type="Data" amount="1.00" subtype="Enable" relativity="Absolute" mergeMode="remove"><target type="ProtoUnit">VillagerNorse</target></effect>
            </effects></tech></techtreemods>
            """);
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            var panel = GetPrivateField<StackPanel>(editor, "_technologyEffectNotices");
            var list = Assert.Single(panel.GetLogicalDescendants().OfType<Expander>());
            var row = Assert.Single(Assert.IsType<StackPanel>(list.Content).Children.OfType<TextBlock>());
            Assert.Equal("Removed (1)", Assert.IsType<TextBlock>(list.Header).Text); Assert.False(list.IsExpanded);
            Assert.StartsWith("Effects → Enable → VillagerGreek", row.Text); Assert.DoesNotContain("VillagerNorse", row.Text);
            Assert.DoesNotContain("Effects → Effects", row.Text);
            Assert.Null(row.TextDecorations);
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_technologyPropertyNotices").Children);
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_technologyPrerequisiteNotices").Children);
            TechnologyTab(editor, 2); Assert.True(panel.IsVisible);
            Assert.False(await editor.RenameTechnologyAsync("A", "Other"));
            await editor.DeleteTechnologyAsync();
            Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath)); Assert.False(editor.IsDirty);
        });
    }

    [Theory]
    [InlineData("remove", false)]
    [InlineData("add", false)]
    [InlineData("replace", true)]
    public void TopLevelTechnologyOperationsDoNotFabricateAnOrdinaryEditableEntry(string mode, bool single)
    {
        using var fixture = new TechnologyEditorFixture($"<techtreemods><tech name='A' mergeMode='{mode}'><researchpoints>9</researchpoints></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            Assert.Equal(single, GetPrivateField<TabControl>(editor, "_technologyEditorTabs").IsVisible);
            Assert.False(editor.CanEditTechnology("A")); Assert.True(Raw(editor).IsReadOnly);
            if (single)
                foreach (var panel in new[] { "_technologyPropertyNotices", "_technologyPrerequisiteNotices", "_technologyEffectNotices" })
                    Assert.Contains("Entire technology", TechnologyNoticeText(editor, panel));
            Assert.False(editor.IsDirty);
        });
    }

    [Fact]
    public void MissingTechnologyOriginalCatalogIsUnresolvedAndReadOnly()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><researchpoints>9</researchpoints></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = new TechnologyEditorView(null, null, fixture.TechtreePath);
            editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            Assert.Equal(AdditiveEntryKind.Unresolved, editor.GetTechnologyEntryKind("A"));
            Assert.False(editor.CanEditTechnology("A")); Assert.True(Raw(editor).IsReadOnly);
            Assert.Contains("unresolved", GetPrivateField<TextBlock>(editor, "_technologyAdditiveSummary").Text);
            Assert.False(editor.IsDirty);
        });
    }

    [Fact]
    public void CustomTechnologyDiscardRemainsAvailableWhenFurtherWritesAreBlocked()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><researchpoints>7</researchpoints></tech></techtreemods>");
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor);
                var saved = editor.LoadSavedTechnologyElement("A");
                Raw(editor).Text = Raw(editor).Text.Replace(">7<", ">9<"); Assert.True(editor.IsDirty);
                typeof(TechnologyEditorView).GetField("_modDocumentSafetyError", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(editor, "Fixture simulates further writes being blocked.");
                editor.DiscardTechnologyChanges("A", saved);
                Assert.False(editor.IsDirty); Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }

    [Theory]
    [InlineData("<effects mergeMode='replace'><effect type='Data' amount='1.00' subtype='Enable' relativity='Absolute'><target type='ProtoUnit'>Ajax</target></effect></effects>", "_technologyEffectNotices", "Entire effects list", 2)]
    [InlineData("<effects><effect type='Data' amount='0.00' subtype='Enable' relativity='Absolute' mergeMode='replace'><target type='ProtoUnit'>Hoplite</target></effect></effects>", "_technologyEffectNotices", "No matching effect", 2)]
    [InlineData("<prereqs mergeMode='replace'><typecount unit='House' count='2' state='aliveState' operator='gte'/></prereqs>", "_technologyPrerequisiteNotices", "Entire prerequisites list", 1)]
    [InlineData("<cost resourcetype='Wood' mergeMode='replace'>50</cost>", "_technologyPropertyNotices", "No matching cost", 0)]
    public void TechnologyModNoticesArePurpleAndPlacedInTheirRelatedTab(string patch, string panelName, string title, int index)
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'>" + patch + "</tech><tech name='Custom'><researchpoints>7</researchpoints></tech></techtreemods>");
        var source = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse(AdditiveOriginal)]);
            await InXmlWindow(editor, async window =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                TechnologyTab(editor, index); await SettleTechnologyPresentation(window);
                var panel = GetPrivateField<StackPanel>(editor, panelName);
                Assert.Contains(title, TechnologyNoticeText(editor, panelName));
                Assert.True(panel.IsEffectivelyVisible);
                var border = Assert.Single(panel.Children.OfType<Border>());
                Assert.True(border.IsEffectivelyEnabled); Assert.Equal(1, border.Opacity);
                Assert.Equal(Color.Parse("#A78BFA"), Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color);
                Assert.DoesNotContain("replaced", GetPrivateField<TextBlock>(editor, "_technologyAdditiveSummary").Text);
                Assert.False(GetPrivateField<TextBlock>(editor, "_technologyAdditiveDiagnostics").IsVisible);
                foreach (var other in new[] { "_technologyPropertyNotices", "_technologyPrerequisiteNotices", "_technologyEffectNotices" }.Where(name => name != panelName))
                    Assert.Empty(GetPrivateField<StackPanel>(editor, other).Children);
                editor.SelectTechnology("Custom"); await AwaitTechnologyBuild(editor);
                Assert.Empty(panel.Children); Assert.False(editor.IsDirty);
                Assert.Equal(source, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }
}
