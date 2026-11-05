using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private const string PresentationTech = "CustomTechnologyWithAnExtremelyLongReferenceNameForPresentation";
    private const string PresentationUnit = "CustomUnitWithAnExtremelyLongReferenceNameForPresentation";
    public static IEnumerable<object[]> TechnologyPresentationCases =>
        TechnologyEffectCatalogInventory.CreatableEffectTypes.Where(type => type != "Data").Select(type => new object[] { type, "" })
        .Concat(TechnologyEffectCatalogInventory.CreatableDataSubtypes.Select(subtype => new object[] { "Data", subtype }));

    private static async Task SettleTechnologyPresentation(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(25);
    }

    private static void WithTechnologyPresentation(string body, Func<TechnologyEditorView, Window, TechnologyEditorFixture, Task> check, bool original = false)
    {
        var xml = $"<techtreemods><tech name='A'>{body}</tech><tech name='B'><researchpoints>1</researchpoints></tech><tech name='{PresentationTech}'><researchpoints>1</researchpoints></tech></techtreemods>";
        using var fixture = new TechnologyEditorFixture(xml);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = new TechnologyEditorView(original ? [new XDocument(new XElement("techtree", XElement.Parse($"<tech name='A'>{body}</tech>")))] : [],
                null, fixture.TechtreePath, resolveStringAsync: id => Task.FromResult<string?>(id),
                prereqUnitNames: ["Unit", "BaseUnit", PresentationUnit], protoUnitNames: ["BaseUnit", PresentationUnit],
                cultureNames: ["Greek"], majorGodNames: ["Zeus"], techTypeNames: ["CustomType"],
                protoActionNames: ["HandAttack"], tacticNames: ["custom.tactics"], protoUnitCommandNames: ["CustomCommand"],
                boneNames: ["CustomBone"], godPowerNames: ["CustomPower"], modStringsPath: fixture.StringsPath);
            var window = new Window { Width = 1400, Height = 900, Content = editor };
            try
            {
                window.Show(); editor.SetModifiedMode(!original); editor.SelectTechnology("A");
                var build = AwaitTechnologyBuild(editor);
                for (var i = 0; !build.IsCompleted && i < 1000; i++)
                {
                    if (window.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup)
                    { var message = popup.PromptText; popup.Close(); Assert.Fail(message); }
                    await Task.Delay(10);
                }
                await build.WaitAsync(TimeSpan.FromSeconds(2)); await SettleTechnologyPresentation(window);
                Assert.Equal("A", editor.CurrentTechnologyName);
                await check(editor, window, fixture);
            }
            finally { foreach (var popup in window.OwnedWindows.ToArray()) popup.Close(); window.Close(); }
        });
    }

    private static void TechnologyTab(TechnologyEditorView editor, int index)
        => GetPrivateField<Control>(editor, "_technologyForm").GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = index;

    [Theory]
    [MemberData(nameof(TechnologyPresentationCases))]
    public void EveryCreatableTechnologyEffectHasBoundedGroupedFieldsWithoutDirtying(string type, string subtype)
        => WithTechnologyPresentation($"<researchpoints>1</researchpoints><effects><effect type='{type}'{(subtype.Length > 0 ? $" subtype='{subtype}'" : "")}/></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var card = Assert.Single(GetPrivateField<StackPanel>(editor, "_effectsPanel").Children.OfType<Border>());
            foreach (var width in new[] { 880d, 560d })
            {
                card.Width = width; await SettleTechnologyPresentation(window);
                FormLayoutAssertions.NoOverlaps(card, $"Technology {type}/{subtype}/{width}");
            }
            var fields = card.GetLogicalDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible &&
                (control is ComboBox or AutoCompleteBox or AssetPathEditor || control is TextBox { AcceptsReturn: false }) &&
                !control.GetLogicalAncestors().TakeWhile(parent => !ReferenceEquals(parent, card)).Any(parent => parent is ComboBox or AutoCompleteBox or AssetPathEditor)).ToArray();
            Assert.NotEmpty(fields);
            foreach (var field in fields)
            {
                Assert.True(EditorFormLayout.TryGetWidthRole(field, out var role), field.GetType().Name);
                Assert.Equal(EditorFormLayout.WidthFor(role), field.Width);
                Assert.Equal(field.Width, field.MinWidth); Assert.Equal(field.Width, field.MaxWidth);
                if (field is not AssetPathEditor) Assert.Equal(30, field.Bounds.Height);
                Assert.True(EditorFieldInteraction.IsConfigured(field));
            }
            Assert.False(editor.IsDirty, FindTechnology(GetDraftDocument(editor), "A").ToString()); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TechnologyPropertiesAndPrerequisitesUseRolesAndWrapWithoutChangingXml(bool original)
        => WithTechnologyPresentation($"<displaynameid>STR_NAME</displaynameid><type>Normal</type><status>UNOBTAINABLE</status><researchpoints>1</researchpoints><cost resourcetype='Food'>12</cost><cost resourcetype='Wood'>13</cost><cost resourcetype='Gold'>14</cost><cost resourcetype='Favor'>15</cost><techtype>CustomType</techtype><flag>MythTech</flag><prereqs><techstatus status='Active'>{PresentationTech}</techstatus><typecount unit='{PresentationUnit}' count='2'/><specificage>ClassicalAge</specificage></prereqs>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            foreach (var (index, name) in new[] { (0, "_propertiesPanel"), (1, "_prereqsPanel") })
            {
                TechnologyTab(editor, index); window.Width = 1000; await SettleTechnologyPresentation(window);
                var panel = GetPrivateField<StackPanel>(editor, name);
                FormLayoutAssertions.NoOverlaps(panel, $"Technology tab {index}");
                foreach (var field in panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Where(field => field.IsEffectivelyVisible))
                {
                    Assert.True(EditorFormLayout.TryGetWidthRole(field, out _));
                    if (field.Text == PresentationTech || field.Text == PresentationUnit) Assert.Equal(200, field.Bounds.Width);
                }
            }
            Assert.Equal(300, GetPrivateField<TextBox>(editor, "_techNameBox").Width);
            Assert.False(editor.IsDirty); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
        }, original);

    [Fact]
    public void TechnologyReferenceShowsItsPrefixSelectsOnEntryAndSavesThroughTheExistingPipeline()
        => WithTechnologyPresentation($"<researchpoints>1</researchpoints><effects><effect type='TechStatus'>{PresentationTech}</effect></effects>", async (editor, window, fixture) =>
        {
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var selector = editor.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.IsEffectivelyVisible && box.Text == PresentationTech);
            Assert.Equal(200, selector.Bounds.Width);
            var input = selector.GetVisualDescendants().OfType<TextBox>().First();
            input.BringIntoView(); GetPrivateField<TextBox>(editor, "_techNameBox").Focus(); await SettleTechnologyPresentation(window);
            var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().First(); Assert.InRange(scroll.Offset.X, 0, .5);
            var tip = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent); selector.RaiseEvent(tip); Assert.False(tip.Cancel);
            Assert.Contains(Assert.IsType<ScrollViewer>(ToolTip.GetTip(selector)).GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == PresentationTech);
            var point = input.TranslatePoint(new Point(30, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await SettleTechnologyPresentation(window);
            Assert.Equal(PresentationTech.Length, Math.Abs(input.SelectionEnd - input.SelectionStart)); Assert.False(editor.IsDirty);
            selector.SelectedItem = "B"; await SettleTechnologyPresentation(window);
            GetPrivateField<TextBox>(editor, "_techNameBox").Focus(); await SettleTechnologyPresentation(window);
            Assert.True(await SaveXmlChecked(editor));
            Assert.Equal("B", XDocument.Load(fixture.TechtreePath).Descendants("effect").Single().Value);
            editor.SelectTechnology("B"); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
            Assert.False(editor.IsDirty);
        });

    [Fact]
    public void RandomTechUsesFiftyPixelSearchAndKeepsChipExclusionRemovalAndSave()
        => WithTechnologyPresentation("<researchpoints>1</researchpoints><effects><effect type='RandomTech' select='1' status='active'><Tech>B</Tech></effect></effects>", async (editor, window, fixture) =>
        {
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var picker = GetPrivateField<StackPanel>(editor, "_effectsPanel").GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box =>
                EditorFormLayout.TryGetWidthRole(box, out var role) && role == EditorFieldWidthRole.ChipSearch);
            Assert.Equal(50, picker.Bounds.Width); Assert.DoesNotContain("B", picker.ItemsSource!.Cast<string>());
            picker.Focus(); picker.SelectedItem = PresentationTech; await SettleTechnologyPresentation(window);
            GetPrivateField<TextBox>(editor, "_techNameBox").Focus(); await SettleTechnologyPresentation(window);
            var effect = FindTechnology(GetDraftDocument(editor), "A").Descendants("effect").Single();
            Assert.Contains(effect.Elements("Tech"), tech => tech.Value == PresentationTech);
            Assert.DoesNotContain(PresentationTech, picker.ItemsSource!.Cast<string>());
            var chip = GetPrivateField<StackPanel>(editor, "_effectsPanel").GetLogicalDescendants()
                .OfType<TextBlock>().Distinct().Single(text => text.Text == "B");
            var remove = Assert.IsType<StackPanel>(chip.Parent).Children.OfType<Button>().Single();
            remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await SettleTechnologyPresentation(window);
            Assert.True(await SaveXmlChecked(editor));
            Assert.Equal(new[] { PresentationTech }, XDocument.Load(fixture.TechtreePath).Descendants("Tech").Select(tech => tech.Value));
        });

    [Fact]
    public void TechnologyOptionalPropertyPickerUsesSchemaRoleAndNumericEditsStillSave()
        => WithTechnologyPresentation("<researchpoints>1</researchpoints><effects><effect type='Data' subtype='CostBuildingAll' delay='5' relativity='Add' resource='Food'><target type='ProtoUnit'>BaseUnit</target></effect></effects>", async (editor, window, fixture) =>
        {
            var properties = GetPrivateField<StackPanel>(editor, "_propertiesPanel");
            var button = properties.GetLogicalDescendants().OfType<Button>().Distinct().Single(control => Equals(control.Content, "Other attributes"));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await SettleTechnologyPresentation(window);
            var picker = properties.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(control => control.IsEffectivelyVisible && control.ItemsSource!.Cast<string>().Contains("Delay"));
            Assert.Equal(180, picker.Bounds.Width); Assert.False(editor.IsDirty);
            picker.SelectedItem = "Delay"; await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            var delayLabel = properties.GetLogicalDescendants().OfType<TextBlock>().Distinct().Single(control => control.Text == "Delay");
            var delay = Assert.IsType<Grid>(delayLabel.Parent).GetLogicalDescendants().OfType<TextBox>().Distinct().Single();
            Assert.Equal(90, delay.Width); delay.Text = "3"; await SettleTechnologyPresentation(window);
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var effectPanel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var effectDelay = effectPanel.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(control => control.Text == "5");
            var delayGroup = Assert.IsType<StackPanel>(effectDelay.Parent);
            FormLayoutAssertions.CrossGap(effectDelay, delayGroup.Children.OfType<Button>().Single(), window);
            var effect = FindTechnology(GetDraftDocument(editor), "A").Descendants("effect").Single();
            var amount = Assert.IsType<TextBox>(EditorValidationBindings.Find(editor,
                new LocatedValidationIssue("Amount", ValidationSourceLocation.From(effect, "@amount"))));
            Assert.Null(effect.Attribute("amount"));
            Assert.True(string.IsNullOrEmpty(amount.Text));
            amount.Text = "2.5"; await SettleTechnologyPresentation(window);
            Assert.True(await SaveXmlChecked(editor));
            var saved = FindTechnology(XDocument.Load(fixture.TechtreePath), "A");
            Assert.Equal("3", saved.Element("delay")!.Value);
            Assert.Equal("2.5", saved.Descendants("effect").Single().Attribute("amount")!.Value);
        });

    [Theory]
    [InlineData("delay", "Delay", "", 90)]
    [InlineData("researchlimit", "Research limit", "7", 90)]
    [InlineData("legacy", "Legacy", "123", 300)]
    [InlineData("legacy", "Legacy", "text", 300)]
    public void TechnologyOptionalPropertyRolesFollowSchemaRatherThanCurrentText(string tag, string label, string value, int width)
        => WithTechnologyPresentation($"<researchpoints>1</researchpoints><{tag}>{value}</{tag}>", async (editor, window, _) =>
        {
            var properties = GetPrivateField<StackPanel>(editor, "_propertiesPanel");
            var title = properties.GetLogicalDescendants().OfType<TextBlock>().Distinct().Single(control => control.Text == label);
            var field = Assert.IsType<Grid>(title.Parent).GetLogicalDescendants().OfType<TextBox>().Distinct().Single();
            Assert.Equal(width, field.Bounds.Width);
            field.Text = value == "" ? "3" : ""; await SettleTechnologyPresentation(window);
            Assert.Equal(width, field.Bounds.Width);
        });

    [Fact]
    public void TechnologyEffectStringRemovalStaysWithItsFieldWhenWrapping()
        => WithTechnologyPresentation("<researchpoints>1</researchpoints><effects><effect type='SetName' proto='BaseUnit' newName='STR_NAME' newRollover='STR_LR'/></effects>", async (editor, window, _) =>
        {
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var card = GetPrivateField<StackPanel>(editor, "_effectsPanel").Children.OfType<Border>().Single();
            card.Width = 560; await SettleTechnologyPresentation(window);
            var field = card.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(control => control.Text == "STR_LR");
            var group = Assert.IsType<RolloverFieldPanel>(field.Parent);
            Assert.Same(field, group.Field);
            FormLayoutAssertions.CrossGap(field, Assert.IsType<Button>(group.Cross), window);
            FormLayoutAssertions.NoOverlaps(card, "Technology effect string"); Assert.False(editor.IsDirty);
        });
}
