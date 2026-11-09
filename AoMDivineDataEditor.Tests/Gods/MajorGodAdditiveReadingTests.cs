using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed class MajorGodAdditiveReadingTests : IDisposable
{
    private const string Original = """
        <civs><defaultpoplimit>300</defaultpoplimit><civ><name>A</name><statsid>SP</statsid><key>G</key><culture>Greek</culture>
          <buildingefficiency>0.6</buildingefficiency><deathmatchtech>DeathMatchGreek</deathmatchtech>
          <startingunits><unit count="4" x="2">VillagerGreek</unit><unit count="1">Kataskopos</unit></startingunits>
          <startingunits mode="deathmatch"><unit count="9">VillagerGreek</unit></startingunits>
          <startingresources><food>200</food><gold>50</gold></startingresources>
          <shades><protounit>HadesShade</protounit><chance>0.2</chance><delay>10</delay><maxqueued>50</maxqueued><targetunit>HumanSoldier</targetunit></shades>
          <future keep="yes"><payload>base-only</payload></future>
        </civ><civ><name>Other</name><key>O</key></civ></civs>
        """;
    private const string Patch = """
        <civsmods><!--keep--><civ><name>A</name><key>Z</key><buildingefficiency>0.75</buildingefficiency>
          <deathmatchtech mergeMode="remove"/>
          <startingunits><unit count="6">VillagerGreek</unit><unit mergeMode="remove">Kataskopos</unit></startingunits>
          <startingresources><food mergeMode="replace">300</food></startingresources>
          <shades><chance>0.3</chance><targetunit mergeMode="remove">HumanSoldier</targetunit><targetunit>AbstractCavalry</targetunit></shades><partisans/>
        </civ><civ><name>Custom</name><statsid>SP</statsid><key>A</key><culture>Greek</culture><future keep="custom"/></civ></civsmods>
        """;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(MajorGodEditorView view, string name) => (T)typeof(MajorGodEditorView).GetField(name, Private)!.GetValue(view)!;
    private static TextEditor Raw(MajorGodEditorView view) => view.FindControl<TextEditor>("_rawXmlEditor")!;
    private static async Task Select(MajorGodEditorView view, string name)
    { view.SelectMajorGod(name); await Field<Task>(view, "_populationTask"); Dispatcher.UIThread.RunJobs(); }
    private static MajorGodEditorView Create(string path, bool available = true) => new(
        MajorGodCatalog.ExtractDefinitions(XDocument.Parse(Original), true), path, cultureNames: ["Greek"],
        technologyNames: ["DeathMatchGreek"], protoUnitNames: ["VillagerGreek", "Kataskopos", "HadesShade"],
        baseSharedData: XElement.Parse("<civs><defaultpoplimit>300</defaultpoplimit></civs>"),
        unitTypeNames: ["HumanSoldier", "AbstractCavalry"], baseCatalogAvailable: available);
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void ReaderMatchesCivilizationsByChildNameAndRegisteredModeAndUnitTextKeys()
    {
        var original = XElement.Parse(Original); var before = new XElement(original);
        var patch = XElement.Parse("<civsmods><civ><name>Other</name><key>X</key></civ><civ><name>A</name><startingunits><unit count='6'>VillagerGreek</unit><unit mergeMode='remove'>Kataskopos</unit></startingunits></civ></civsmods>");
        var source = new XElement(patch); var read = AdditiveXmlReader.Evaluate(original, patch, AdditiveFamily.MajorGods);
        var a = read.Effective.Elements("civ").Single(entry => MajorGodCatalog.GetName(entry) == "A");
        Assert.Equal("G", a.Element("key")!.Value);
        Assert.Equal("X", read.Effective.Elements("civ").Single(entry => MajorGodCatalog.GetName(entry) == "Other").Element("key")!.Value);
        Assert.Equal("6", (string?)a.Elements("startingunits").First().Element("unit")!.Attribute("count"));
        Assert.Equal("2", (string?)a.Elements("startingunits").First().Element("unit")!.Attribute("x"));
        Assert.Equal("9", (string?)a.Elements("startingunits").Last().Element("unit")!.Attribute("count"));
        Assert.Equal("Kataskopos", Assert.Single(read.Changes, change => change.Mode == "remove").Before!.Value);
        Assert.True(XNode.DeepEquals(before, original)); Assert.True(XNode.DeepEquals(source, patch));
        Assert.Empty(read.Diagnostics);
    }

    [Fact]
    public void ActualMergedFormKeepsPatchPreviewLockedAndClearsReusedPresentationOnOriginalCustomAndSharedData()
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", Patch); var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path); var window = new Window { Content = view, Width = 1300, Height = 850 };
            try
            {
                window.Show(); view.SetModifiedMode(true); await Select(view, "A"); window.UpdateLayout();
                var current = Field<XElement>(view, "_currentEntry");
                Assert.Equal("Greek", current.Element("culture")!.Value); Assert.NotNull(current.Element("future"));
                Assert.Equal("6", (string?)current.Element("startingunits")!.Element("unit")!.Attribute("count"));
                Assert.Null(current.Element("deathmatchtech")); Assert.Equal("300", current.Element("startingresources")!.Element("food")!.Value);
                Assert.Equal("9", (string?)current.Elements("startingunits").Last().Element("unit")!.Attribute("count"));
                Assert.Equal("AbstractCavalry", Assert.Single(current.Element("shades")!.Elements("targetunit")).Value);
                Assert.True(Raw(view).IsReadOnly); Assert.DoesNotContain("base-only", Raw(view).Text); Assert.Contains("mergeMode=\"remove\"", Raw(view).Text);
                Assert.False(view.FindControl<Button>("_editXmlButton")!.IsVisible);
                Assert.False(view.FindControl<TextBox>("_nameBox")!.IsEnabled); Assert.DoesNotContain("mod-changed", view.FindControl<TextBox>("_nameBox")!.Classes);
                Assert.Equal("Original: G\nModified: Z", ToolTip.GetTip(view.FindControl<TextBox>("_keyBox")!));
                var economic = view.FindControl<StackPanel>("_economicPanel")!.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Text == "0.75");
                Assert.Contains("mod-changed", economic.Classes);
                Assert.Contains(economic.GetVisualDescendants().OfType<Border>(), border => border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A78BFA"));
                var villager = current.Element("startingunits")!.Element("unit")!;
                var unitFields = view.FindControl<StackPanel>("_startingUnitsPanel")!.GetLogicalDescendants().OfType<Control>().Where(control =>
                    EditorValidationBindings.TryGetXmlSource(control, out var source, out _) && ReferenceEquals(source, villager)).ToArray();
                Assert.DoesNotContain("mod-changed", Assert.Single(unitFields.OfType<AutoCompleteBox>()).Classes);
                var count = Assert.Single(unitFields.OfType<TextBox>(), box => EditorValidationBindings.TryGetXmlSource(box, out _, out var field) && field == "@count");
                Assert.Contains("mod-changed", count.Classes); Assert.Equal("Original: 4\nModified: 6", ToolTip.GetTip(count));
                Assert.All(unitFields.OfType<TextBox>().Where(box => !ReferenceEquals(box, count)), box => Assert.DoesNotContain("mod-changed", box.Classes));
                var food = Assert.Single(view.FindControl<StackPanel>("_startingResourcesPanel")!.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == "300");
                Assert.Equal("Original: 200\nModified: 300", ToolTip.GetTip(food));
                foreach (var name in new[] { "_startingUnitsPanel", "_startingResourcesPanel", "_specialMechanicsPanel" })
                    Assert.All(view.FindControl<StackPanel>(name)!.Children.OfType<Border>(), card =>
                    { Assert.DoesNotContain("mod-changed", card.Classes); Assert.Null(ToolTip.GetTip(card)); });
                Assert.Contains("mod-changed", view.FindControl<CheckBox>("_partisansCheckBox")!.Classes);
                Assert.Contains(view.FindControl<StackPanel>("_specialMechanicsPanel")!.GetLogicalDescendants().OfType<Border>(), border =>
                    border.Classes.Contains("mod-changed") && ToolTip.GetTip(border)?.ToString()?.Contains("AbstractCavalry") == true);
                string[] panels = ["_majorGodPropertyNotices", "_majorGodStartingNotices", "_majorGodSpecialNotices"];
                var props = view.FindControl<StackPanel>(panels[0])!; var starting = view.FindControl<StackPanel>(panels[1])!;
                Assert.Single(props.Children); Assert.Equal(2, starting.Children.Count); Assert.Single(view.FindControl<StackPanel>(panels[2])!.Children);
                var startingLabels = starting.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains("Starting resources → Food", startingLabels);
                Assert.Contains("Starting units → Unit: Kataskopos", startingLabels);
                Assert.Contains(view.FindControl<StackPanel>(panels[2])!.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Shades → Target unit: HumanSoldier");
                Assert.All(panels.SelectMany(name => view.FindControl<StackPanel>(name)!.GetLogicalDescendants().OfType<Expander>()), card =>
                {
                    Assert.False(card.IsExpanded); Assert.True(card.IsEffectivelyEnabled);
                    Assert.All(Assert.IsType<StackPanel>(card.Content).Children.OfType<TextBlock>(), text => Assert.Null(text.TextDecorations));
                });
                Assert.True(await view.CommitCurrentMajorGodAsync()); await view.DeleteCurrentMajorGodAsync(); Assert.False(view.IsDirty);
                Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.DoesNotContain("base-only", Raw(view).Text);
                view.SetModifiedMode(false); await Select(view, "A");
                Assert.Equal("G", view.FindControl<TextBox>("_keyBox")!.Text); Assert.DoesNotContain("mod-changed", view.FindControl<TextBox>("_keyBox")!.Classes);
                Assert.All(panels, name => Assert.Empty(view.FindControl<StackPanel>(name)!.Children));
                await Select(view, MajorGodCatalog.SharedDataDisplayName); Assert.Contains("300", Raw(view).Text); Assert.False(view.FindControl<Grid>("_majorGodIdentityRow")!.IsVisible);
                view.SetModifiedMode(true); await Select(view, "Custom"); Assert.True(view.FindControl<TextBox>("_keyBox")!.IsEnabled);
                Assert.DoesNotContain("mod-changed", view.FindControl<TextBox>("_keyBox")!.Classes); Assert.False(view.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedCustomFormOrRawSavePreservesOrderedOriginalOperationsCommentsAndUnknownData(bool raw)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var patch = Patch.Replace("<buildingefficiency>0.75</buildingefficiency>", "<buildingefficiency mergeMode='remove'/><buildingefficiency mergeMode='add'>0.75</buildingefficiency>");
        var path = workspace.WriteText("major_gods_mods.xml", patch);
        var before = new XElement(XDocument.Load(path, LoadOptions.PreserveWhitespace).Root!.Element("civ")!);
        Assert.False(ModXmlDocumentSafety.Validate(XDocument.Load(path), ModXmlDocumentKind.MajorGods).IsSafeToEdit);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A"); Assert.True(view.CanModifyMajorGodDocument);
                await Select(view, "Custom");
                if (raw) { await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = Raw(view).Text.Replace("<key>A</key>", "<key>B</key>"); }
                else { view.FindControl<TextBox>("_keyBox")!.Text = "B"; await Task.Delay(30); Dispatcher.UIThread.RunJobs(); }
                Assert.True(view.IsDirty);
                Assert.True(await view.SaveAsync(), view.FindControl<TextBlock>("_statusMessage")!.Text);
                var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                Assert.Equal("B", MajorGodCatalog.Find(document, "Custom")!.Element("key")!.Value);
                Assert.Contains(document.Root!.Nodes().OfType<XComment>(), comment => comment.Value == "keep");
                Assert.Equal("custom", (string?)MajorGodCatalog.Find(document, "Custom")!.Element("future")!.Attribute("keep"));
                Assert.Equal(TechnologyEditorView.SerializeTechnologyForComparison(before), TechnologyEditorView.SerializeTechnologyForComparison(document.Root!.Element("civ")!));
                Assert.True(XNode.DeepEquals(before, Field<XDocument>(view, "_modDocument").Root!.Element("civ")));
                await Select(view, "A"); Assert.Contains("mergeMode=\"add\"", Raw(view).Text); Assert.False(view.IsDirty);
                Assert.DoesNotContain("base-only", Raw(view).Text);
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Theory]
    [InlineData("remove", false)]
    [InlineData("add", false)]
    [InlineData("replace", true)]
    public void RootOperationsStayLockedAndExplainRemovedOrDuplicateResults(string mode, bool single)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", $"<civsmods><civ mergeMode='{mode}'><name>A</name><key>Z</key></civ></civsmods>");
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A"); Assert.Equal(single, view.FindControl<TabControl>("_majorGodTabs")!.IsVisible);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.CanEditMajorGod("A")); Assert.False(view.IsDirty);
                if (single) foreach (var name in new[] { "_majorGodPropertyNotices", "_majorGodStartingNotices", "_majorGodSpecialNotices" })
                    Assert.Contains(view.FindControl<StackPanel>(name)!.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Entire Major God");
                Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void ExternalOriginalPatchRefreshRetainsCustomRawDraftAndItsNextSave()
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", Patch);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path); var owner = new Window { Content = view, Width = 1300, Height = 850 };
            try
            {
                owner.Show(); view.SetModifiedMode(true); await Select(view, "Custom");
                await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = Raw(view).Text.Replace("<key>A</key>", "<key>B</key>");
                File.WriteAllText(path, Patch.Replace("<key>Z</key>", "<key>X</key>")); var externalBytes = File.ReadAllBytes(path);
                var saving = view.SaveAsync();
                for (var i = 0; i < 500 && !owner.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                var prompt = Assert.Single(owner.OwnedWindows.OfType<Prompt>());
                Assert.Equal("Major Gods changed outside the editor", prompt.PromptTitle);
                prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10))); Assert.Equal(externalBytes, File.ReadAllBytes(path));
                Assert.Contains("<key>B</key>", Raw(view).Text); Assert.True(view.IsDirty);
                Assert.True(await view.SaveAsync());
                var saved = XDocument.Load(path);
                Assert.Equal("B", MajorGodCatalog.Find(saved, "Custom")!.Element("key")!.Value);
                Assert.Equal("X", MajorGodCatalog.Find(saved, "A")!.Element("key")!.Value);
                await Select(view, "A"); Assert.Equal("X", view.FindControl<TextBox>("_keyBox")!.Text); Assert.False(view.IsDirty);
                Assert.Contains("mergeMode=\"remove\"", Raw(view).Text); Assert.DoesNotContain("base-only", Raw(view).Text);
            }
            finally { foreach (var popup in owner.OwnedWindows.ToArray()) popup.Close(); view.ReleaseMajorGodXmlEditing(); owner.Close(); }
        });
    }

    [Fact]
    public void MissingBaseCatalogIsUnresolvedAndDuplicateSourceIdentitiesBlockWriting()
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", Patch); var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path, available: false);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A"); Assert.Equal(AdditiveEntryKind.Unresolved, view.GetMajorGodEntryKind("A"));
                Assert.False(view.CanModifyMajorGodDocument); Assert.True(Raw(view).IsReadOnly); Assert.False(await view.SaveAsync());
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
            File.WriteAllText(path, "<civsmods><civ><name>A</name><key>Z</key></civ><civ><name>A</name><key>X</key></civ></civsmods>");
            var duplicateBytes = File.ReadAllBytes(path); view = Create(path);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A"); Assert.Equal("X", view.FindControl<TextBox>("_keyBox")!.Text);
                Assert.False(view.CanModifyMajorGodDocument); Assert.False(await view.SaveAsync()); Assert.Equal(duplicateBytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void ReaderUsesModeIdentityAndDiagnosesChangedKeyReplacementWithoutAddingIt()
    {
        var read = AdditiveXmlReader.Evaluate(XElement.Parse(Original), XElement.Parse("<civsmods><civ><name>A</name><startingunits mode='Lightning' mergeMode='replace'><unit>Ajax</unit></startingunits></civ></civsmods>"), AdditiveFamily.MajorGods);
        Assert.Equal(2, read.Effective.Elements("civ").First().Elements("startingunits").Count());
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Contains("replace found no match"));
    }

    [Theory]
    [InlineData("<startingunits mode='Lightning'><unit count='2'>VillagerGreek</unit></startingunits>", "Added section")]
    [InlineData("<startingunits mergeMode='replace'><unit count='2'>VillagerGreek</unit></startingunits>", "Replaced section")]
    public void WholeStartingGroupAddOrReplacementKeepsItsFrameAndConciseScopeTooltip(string operation, string tooltip)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", "<civsmods><civ><name>A</name>" + operation + "</civ></civsmods>");
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A");
                var card = Assert.Single(view.FindControl<StackPanel>("_startingUnitsPanel")!.Children.OfType<Border>(), border => border.Classes.Contains("mod-changed"));
                Assert.Equal(tooltip, ToolTip.GetTip(card)); Assert.False(view.IsDirty);
                Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("add")]
    public void ImportedNameOperationsCannotFabricateAValidOrdinaryIdentity(string mode)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", $"<civsmods><civ><name mergeMode='{mode}'>A</name></civ></civsmods>");
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await Select(view, "A");
                Assert.False(view.FindControl<TabControl>("_majorGodTabs")!.IsVisible);
                Assert.Contains("internal name", view.FindControl<TextBlock>("_majorGodAdditiveDiagnostics")!.Text);
                Assert.True(Raw(view).IsReadOnly); Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void ReducedDecodedGameCivsMatchTheProbeProjectionAndKeepAllOtherOriginalFields()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Additive");
        var original = XDocument.Load(Path.Combine(folder, "MajorGods.base.xml"), LoadOptions.PreserveWhitespace);
        var originalBefore = new XDocument(original);
        using var workspace = new TestWorkspace(nameof(MajorGodAdditiveReadingTests));
        var path = workspace.WriteText("major_gods_mods.xml", File.ReadAllText(Path.Combine(folder, "MajorGods.patch.xml")));
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView(MajorGodCatalog.ExtractDefinitions(original, true), path,
                baseSharedData: MajorGodCatalog.ExtractSharedData(original));
            try
            {
                view.SetModifiedMode(true);
                foreach (var name in new[] { "Zeus", "Hades", "Poseidon" })
                {
                    var expected = new XElement(MajorGodCatalog.Find(original, name)!);
                    if (name == "Zeus")
                    {
                        expected.Element("buildingefficiency")!.Value = "0.75"; expected.Element("deathmatchtech")!.Remove();
                        var units = expected.Elements("startingunits").Single(group => group.Attribute("mode") == null);
                        units.Elements("unit").Single(unit => unit.Value == "VillagerGreek").SetAttributeValue("count", "6");
                        units.Elements("unit").Single(unit => unit.Value == "Kataskopos").Remove();
                        expected.Elements("startingresources").Single(group => group.Attribute("mode") == null).Element("food")!.Value = "300";
                    }
                    else if (name == "Hades")
                    { expected.Element("shades")!.Element("chance")!.Value = "0.35"; expected.Element("shades")!.Element("targetunit")!.Remove(); }
                    else expected.Element("partisans")!.Remove();
                    await Select(view, name);
                    Assert.Equal(AdditiveEntryKind.Modified, view.GetMajorGodEntryKind(name));
                    Assert.Equal(TechnologyEditorView.SerializeTechnologyForComparison(expected),
                        TechnologyEditorView.SerializeTechnologyForComparison(Field<XElement>(view, "_currentEntry")));
                    Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
                }
                await Select(view, "ADM_Additive_CustomGod"); Assert.True(view.FindControl<TextBox>("_keyBox")!.IsEnabled);
                Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.True(XNode.DeepEquals(originalBefore, original));
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }
}
