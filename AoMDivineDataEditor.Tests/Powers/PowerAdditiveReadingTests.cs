using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed class PowerAdditiveReadingTests : IDisposable
{
    private const string Original = """
        <powers><power name="A" type="GodPower"><activetime>10</activetime><cost>3</cost>
          <soundset type="EndSound" listenertype="All">GodPowerEnd</soundset>
          <soundset type="StartSound" listenertype="All">GodPowerStart</soundset>
          <future keep="yes"><payload>original</payload></future>
        </power></powers>
        """;
    private const string Patch = """
        <powersmod><!--keep--><power name="A"><activetime>20</activetime><global/>
          <soundset type="EndSound" listenertype="All" mergeMode="remove">GodPowerEnd</soundset>
          <soundset type="EndSound" listenertype="Caster" mergeMode="add">GodPowerEnd</soundset>
        </power><power name="Custom" type="GodPower"><activetime>7</activetime><future keep="custom"/></power></powersmod>
        """;
    private static T Field<T>(PowerEditorView view, string name) => (T)typeof(PowerEditorView)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
    private static TextEditor Raw(PowerEditorView view) => view.FindControl<TextEditor>("_rawXmlEditor")!;
    private static PowerEditorView Create(string path, string original = Original)
        => new(PowerCatalog.ExtractDefinitions([XDocument.Parse(original)], true), path);
    private static string Comparable(XElement element) => TechnologyEditorView.SerializeTechnologyForComparison(element);
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Theory]
    [InlineData("remove", "Effects → Notification sound: FirstSound")]
    [InlineData("replace", "Effects → Notification sound")]
    public void NestedPowerSummaryShowsItsEffectsParentAndPreservesPatchBytes(string mode, string label)
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", $"<powersmod><power name='A'><effects><notificationsound mergeMode='{mode}'>NewSound</notificationsound></effects></power></powersmod>");
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path, "<powers><power name='A' type='SwitchingEffects'><tiertype>TotalResources</tiertype><effects><notificationsound>FirstSound</notificationsound><effect type='CannotSnare'><target type='ProtoUnit'>Hoplite</target></effect></effects></power></powers>");
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                var panel = view.FindControl<StackPanel>("_powerSpecificNotices")!;
                var list = Assert.Single(panel.GetLogicalDescendants().OfType<Expander>());
                Assert.Equal(label, Assert.Single(Assert.IsType<StackPanel>(list.Content).Children.OfType<TextBlock>()).Text);
                Assert.False(view.IsDirty); Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangeCardArrowCaptionAndHeaderSpaceToggleWithoutMovingWhilePressed(bool removed)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var card = AdditiveChangePresentation.CreateChangeList(removed ? AdditiveChangeListKind.Removed : AdditiveChangeListKind.Replaced, ["A changed field"]);
            var window = new Window { Content = new StackPanel { Children = { card } }, Width = 650, Height = 250 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var expander = Assert.Single(card.GetLogicalDescendants().OfType<Expander>());
                var caption = Assert.IsType<TextBlock>(expander.Header);
                var arrow = Assert.Single(expander.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "▸");
                var toggle = Assert.Single(expander.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>());
                async Task Click(Point point, bool expected)
                {
                    window.MouseMove(point); window.UpdateLayout();
                    var captionX = caption.TranslatePoint(default, window)!.Value.X;
                    var arrowX = arrow.TranslatePoint(default, window)!.Value.X;
                    window.MouseDown(point, MouseButton.Left); await Task.Delay(80); window.UpdateLayout();
                    Assert.Equal(captionX, caption.TranslatePoint(default, window)!.Value.X);
                    Assert.Equal(arrowX, arrow.TranslatePoint(default, window)!.Value.X);
                    window.MouseUp(point, MouseButton.Left); await Task.Delay(80); window.UpdateLayout();
                    Assert.Equal(expected, expander.IsExpanded);
                    Assert.Equal(captionX, caption.TranslatePoint(default, window)!.Value.X);
                    Assert.Equal(arrowX, arrow.TranslatePoint(default, window)!.Value.X);
                }
                await Click(arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), window)!.Value, true);
                await Click(arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), window)!.Value, false);
                await Click(caption.TranslatePoint(new Point(20, caption.Bounds.Height / 2), window)!.Value, true);
                await Click(toggle.TranslatePoint(new Point(toggle.Bounds.Width - 12, toggle.Bounds.Height / 2), window)!.Value, false);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("add")]
    [InlineData("modify")]
    public void ReadOnlyProjectionKeepsAuthoredXmlAndClearsReusedFieldDecorationsOnOriginalAndCustom(string mode)
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", Patch.Replace("mergeMode=\"add\"", $"mergeMode=\"{mode}\"")); var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path); var window = new Window { Content = view, Width = 1300, Height = 850 };
            try
            {
                window.Show(); view.SetModifiedMode(true); await view.SelectPowerAsync("A"); window.UpdateLayout();
                var current = Field<PowerEditingSession>(view, "_session").CurrentEntry!;
                Assert.Equal("3", current.Element("cost")!.Value); Assert.NotNull(current.Element("future")!.Element("payload"));
                Assert.Equal(new[] { "StartSound", "EndSound" }, current.Elements("soundset").Select(sound => (string?)sound.Attribute("type")));
                Assert.Equal("Caster", (string?)current.Elements("soundset").Last().Attribute("listenertype"));
                Assert.Contains("mergeMode=\"remove\"", Raw(view).Text); Assert.DoesNotContain("<cost>", Raw(view).Text);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.FindControl<Button>("_editXmlButton")!.IsVisible);
                Assert.False(view.FindControl<TextBox>("_nameBox")!.IsEnabled);
                Assert.DoesNotContain("mod-changed", view.FindControl<TextBox>("_nameBox")!.Classes);
                var active = view.FindControl<TextBox>("_activeTimeBox")!;
                Assert.Equal("20", active.Text); Assert.Contains("mod-changed", active.Classes);
                Assert.Equal("Original: 10\nModified: 20", ToolTip.GetTip(active));
                Assert.Contains(active.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_BorderElement" &&
                    border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A78BFA"));
                Assert.Contains("mod-changed", view.FindControl<CheckBox>("_globalCheck")!.Classes);
                var listener = view.FindControl<Panel>("_soundSetRows")!;
                Assert.Contains(listener.GetLogicalDescendants().OfType<ComboBox>(), box => Equals(box.SelectedItem, "Caster") && box.Classes.Contains("mod-changed"));
                var addedSound = listener.Children.OfType<Panel>().Single(row => row.GetLogicalDescendants().OfType<ComboBox>().Any(box => Equals(box.SelectedItem, "Caster")));
                foreach (var box in addedSound.GetLogicalDescendants().OfType<ComboBox>())
                {
                    Assert.Contains("mod-changed", box.Classes);
                    Assert.True(box.GetVisualDescendants().OfType<Border>().Any(border =>
                        border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#A78BFA")),
                        "Rendered selector borders: " + string.Join(", ", box.GetVisualDescendants().OfType<Border>().Select(border => border.Name + "=" + border.BorderBrush)));
                }
                Assert.Contains("mod-changed", Assert.Single(addedSound.GetLogicalDescendants().OfType<AutoCompleteBox>()).Classes);
                var unchangedSound = listener.Children.OfType<Panel>().Single(row => row.GetLogicalDescendants().OfType<ComboBox>().Any(box => Equals(box.SelectedItem, "StartSound")));
                Assert.All(unchangedSound.GetLogicalDescendants().OfType<ComboBox>(), box => Assert.DoesNotContain("mod-changed", box.Classes));
                var removedCard = Assert.Single(view.FindControl<StackPanel>("_powerPropertyNotices")!.Children, card => card.Classes.Contains("additive-removed-list"));
                var removedList = Assert.Single(removedCard.GetLogicalDescendants().OfType<Expander>());
                Assert.Equal("Removed (1)", Assert.IsType<TextBlock>(removedList.Header).Text);
                Assert.Contains("All", Assert.Single(Assert.IsType<StackPanel>(removedList.Content).Children.OfType<TextBlock>()).Text);
                Assert.True(await view.CommitCurrentPowerAsync()); await view.DeleteCurrentPowerAsync();
                Assert.False(view.IsDirty); Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
                view.SetModifiedMode(false); await view.SelectPowerAsync("A");
                Assert.Equal("10", active.Text); Assert.DoesNotContain("mod-changed", active.Classes);
                Assert.NotEqual("Original: 10\nModified: 20", ToolTip.GetTip(active));
                Assert.Null(view.FindControl<CheckBox>("_globalCheck")!.GetValue(Avalonia.Controls.Primitives.AdornerLayer.AdornerProperty));
                view.SetModifiedMode(true); await view.SelectPowerAsync("Custom");
                Assert.Equal("7", active.Text); Assert.True(active.IsEnabled); Assert.DoesNotContain("mod-changed", active.Classes);
                Assert.False(view.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleasePowerXmlEditing(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomFormAndRawSavesPreserveOriginalOrderedSelectorsCommentsAndMergeModes(bool raw)
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='A'><!--patch--><activetime mergeMode='remove'/><activetime mergeMode='add'>12</activetime></power><power name='Custom' type='GodPower'><activetime>7</activetime></power></powersmod>");
        var before = new XElement(XDocument.Load(path, LoadOptions.PreserveWhitespace).Root!.Element("power")!);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); Assert.Equal("12", view.FindControl<TextBox>("_activeTimeBox")!.Text);
                await view.SelectPowerAsync("Custom");
                if (raw)
                { await PowerXmlTestHelpers.UnlockAsync(view); Raw(view).Text = Raw(view).Text.Replace(">7<", ">9<"); }
                else { view.FindControl<TextBox>("_activeTimeBox")!.Text = "9"; await Task.Delay(30); Dispatcher.UIThread.RunJobs(); }
                Assert.True(await view.SaveAsync(), view.StatusMessage);
                var saved = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                Assert.True(XNode.DeepEquals(before, Field<XDocument>(view, "_modDocument").Root!.Element("power")));
                // The established file writer may add indentation to compact records.
                // Authored operations, order, comments and payload must remain identical.
                Assert.Equal(Comparable(before), Comparable(saved.Root!.Element("power")!));
                Assert.Equal("9", PowerCatalog.Find(saved, "Custom")!.Element("activetime")!.Value);
                await view.SelectPowerAsync("A"); Assert.Equal("12", view.FindControl<TextBox>("_activeTimeBox")!.Text);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void VerifiedPowerFixturesRenderTheExpectedGameProjectionAndPreserveNoOpSaveBytes()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Additive");
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", File.ReadAllText(Path.Combine(folder, "Powers.patch.xml")));
        var original = XDocument.Load(Path.Combine(folder, "Powers.base.xml")); var expected = XDocument.Load(Path.Combine(folder, "Powers.expected.xml"));
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView(PowerCatalog.ExtractDefinitions([original], true), path);
            try
            {
                view.SetModifiedMode(true); Assert.Equal(3, view.GetPowerNames(true).Count);
                foreach (var name in view.GetPowerNames(true))
                {
                    await view.SelectPowerAsync(name);
                    Assert.Equal(Comparable(PowerCatalog.Find(expected, name)!), Comparable(Field<PowerEditingSession>(view, "_session").CurrentEntry!));
                    Assert.True(Raw(view).IsReadOnly); Assert.Equal(AdditiveEntryKind.Modified, view.GetPowerEntryKind(name));
                    Assert.False(view.IsDirty);
                }
                Assert.True(await view.SaveAsync(), view.StatusMessage); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Theory]
    [InlineData("remove", false)]
    [InlineData("add", false)]
    [InlineData("replace", true)]
    public void TopLevelPowerOperationsAreLockedAndDoNotFabricateAnOrdinaryForm(string mode, bool single)
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", $"<powersmod><power name='A' mergeMode='{mode}' type='GodPower'><activetime>3</activetime></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                Assert.Equal(single, view.FindControl<TabControl>("_powerTabs")!.IsVisible);
                Assert.False(view.CanEditPower("A")); Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
                if (single) Assert.Contains(view.FindControl<StackPanel>("_powerPropertyNotices")!.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Entire Power");
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void MissingPowerCatalogIsUnresolvedAndReadOnlyRatherThanGuessedCustom()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", Patch);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView(new PowerEditorComposition([], path, baseCatalogAvailable: false));
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                Assert.Equal(AdditiveEntryKind.Unresolved, view.GetPowerEntryKind("A"));
                Assert.False(view.CanModifyPowerDocument); Assert.True(Raw(view).IsReadOnly);
                Assert.Contains("unresolved", view.FindControl<TextBlock>("_powerAdditiveSummary")!.Text);
                Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void OriginalPatchesAreResolvedBeforeCustomAbilityFiltering()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='A' type='GeneralEffect'><activetime>20</activetime></power><power name='Ability' type='UnitAction'/></powersmod>");
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); Assert.Equal(new[] { "A" }, view.GetPowerNames(true));
                await view.SelectPowerAsync("A");
                Assert.Equal("20", view.FindControl<TextBox>("_activeTimeBox")!.Text);
                Assert.Contains("GeneralEffect", Raw(view).Text); Assert.True(Raw(view).IsReadOnly);
                Assert.True(await view.SaveAsync(), view.StatusMessage); Assert.Equal(bytes, File.ReadAllBytes(path));
                await view.SelectPowerAsync("Ability"); Assert.Null(Field<PowerEditingSession>(view, "_session").CurrentEntry);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void ExternalOriginalPatchRefreshPreservesTheCustomRawDraftAndItsNextSave()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", Patch);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("Custom"); await PowerXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = Raw(view).Text.Replace(">7<", ">9<"); var raw = Raw(view).Text;
                File.WriteAllText(path, Patch.Replace("<activetime>20", "<activetime>23"));
                view.AcknowledgePowerFileSavedByEditor(); await Field<Task>(view, "_populationTask");
                Assert.Equal(raw, Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
                Assert.True(await view.SaveAsync(), view.StatusMessage);
                await view.SelectPowerAsync("A"); Assert.Equal("23", view.FindControl<TextBox>("_activeTimeBox")!.Text);
                Assert.Equal("9", PowerCatalog.Find(XDocument.Load(path), "Custom")!.Element("activetime")!.Value);
                Assert.Contains("mergeMode=\"remove\"", Raw(view).Text); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void FamilyFieldReplacementIsPurpleAndExplainedInSpecificTab()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='A'><healspeed mergeMode='replace'>75</healspeed></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path, "<powers><power name='A' type='Restoration'><healspeed>50</healspeed></power></powers>");
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); view.FindControl<TabControl>("_powerTabs")!.SelectedIndex = 1;
                var box = view.FindControl<TextBox>("_restorationHealSpeedBox")!;
                Assert.Equal("75", box.Text); Assert.Contains("mod-changed", box.Classes);
                Assert.Equal("Original: 50\nModified: 75", ToolTip.GetTip(box)); Assert.False(box.IsEnabled);
                Assert.Contains(view.FindControl<StackPanel>("_powerSpecificNotices")!.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Heal speed");
                Assert.Empty(view.FindControl<StackPanel>("_powerPropertyNotices")!.Children); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void ChangeCardsGroupByOperationAndTabAndRetainCollapseStateWithoutEditing()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="A">
              <activetime mergeMode="replace">12</activetime><cost mergeMode="replace">90</cost>
              <healspeed mergeMode="replace">75</healspeed><scaninterval mergeMode="remove"/>
              <soundset type="EndSound" listenertype="All" mergeMode="remove">GodPowerEnd</soundset>
            </power><power name="Custom" type="Restoration"><healspeed>20</healspeed></power></powersmod>
            """);
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path, Original.Replace("type=\"GodPower\"", "type=\"Restoration\"")
                .Replace("<cost>3</cost>", "<cost>3</cost><healspeed>50</healspeed><scaninterval>0.25</scaninterval>"));
            var window = new Window { Content = view, Width = 1300, Height = 850 };
            try
            {
                window.Show(); view.SetModifiedMode(true); await view.SelectPowerAsync("A"); window.UpdateLayout();
                var properties = view.FindControl<StackPanel>("_powerPropertyNotices")!;
                var specific = view.FindControl<StackPanel>("_powerSpecificNotices")!;
                static Expander Card(StackPanel panel, string kind) => Assert.Single(Assert.Single(panel.Children,
                    child => child.Classes.Contains("additive-" + kind + "-list")).GetLogicalDescendants().OfType<Expander>());
                static string?[] Lines(Expander card) => Assert.IsType<StackPanel>(card.Content).Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
                var replaced = Card(properties, "replaced"); var removed = Card(properties, "removed");
                Assert.Equal(2, properties.Children.Count); Assert.Equal(2, specific.Children.Count);
                Assert.Equal("Replaced (2)", Assert.IsType<TextBlock>(replaced.Header).Text);
                Assert.Equal(new[] { "Active time", "Cost" }, Lines(replaced));
                Assert.Contains("EndSound, All", Assert.Single(Lines(removed)));
                Assert.Equal(new[] { "Heal speed" }, Lines(Card(specific, "replaced")));
                Assert.Contains("0.25", Assert.Single(Lines(Card(specific, "removed"))));
                Assert.All(Assert.IsType<StackPanel>(removed.Content).Children.OfType<TextBlock>(), text =>
                    Assert.Null(text.TextDecorations));
                Assert.True(replaced.IsEnabled); Assert.True(removed.IsEnabled);
                Assert.False(replaced.IsExpanded); Assert.False(removed.IsExpanded);
                Assert.DoesNotContain(replaced.GetVisualDescendants().OfType<Border>(), border => border.BorderThickness != default);
                var headerToggle = Assert.Single(replaced.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>());
                var collapsedHeight = replaced.Bounds.Height;
                Assert.InRange(collapsedHeight, 15, 40);
                headerToggle.IsChecked = true; removed.IsExpanded = true; window.UpdateLayout();
                Assert.True(replaced.IsExpanded); Assert.True(replaced.Bounds.Height > collapsedHeight);
                Assert.All(Assert.IsType<StackPanel>(replaced.Content).Children.OfType<TextBlock>(), text =>
                    Assert.True(text.Bounds.Width > replaced.Bounds.Width * 0.9));
                Assert.Same(view.FindControl<ScrollViewer>("_propertiesScroll"), properties.GetLogicalAncestors().OfType<ScrollViewer>().First());
                Assert.Empty(properties.GetLogicalDescendants().OfType<ScrollViewer>());
                view.FindControl<TabControl>("_powerTabs")!.SelectedIndex = 1;
                Assert.False(Card(specific, "replaced").IsExpanded); Assert.False(Card(specific, "removed").IsExpanded);
                view.FindControl<TabControl>("_powerTabs")!.SelectedIndex = 0;
                Assert.True(replaced.IsExpanded); Assert.True(removed.IsExpanded);
                view.SetModifiedMode(false); await view.SelectPowerAsync("A");
                Assert.Empty(properties.Children); Assert.Empty(specific.Children);
                view.SetModifiedMode(true); await view.SelectPowerAsync("Custom");
                Assert.Empty(properties.Children); Assert.Empty(specific.Children);
                await view.SelectPowerAsync("A");
                Assert.True(Card(properties, "replaced").IsExpanded); Assert.True(Card(properties, "removed").IsExpanded);
                Assert.False(Card(specific, "replaced").IsExpanded);
                Assert.False(view.IsDirty); Assert.True(await view.SaveAsync()); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleasePowerXmlEditing(); window.Close(); }
        });
    }

    [Fact]
    public void CustomRawDraftThatBecomesAnUnresolvedOperationCanStillBeDiscarded()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", Patch); var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("Custom"); var saved = view.LoadSavedPowerElement("Custom");
                await PowerXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = "<power name='Custom' type='GodPower' mergeMode='remove'><activetime>9</activetime></power>";
                Assert.False(await view.SaveAsync()); Assert.True(view.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(path));
                view.DiscardPowerChanges("Custom", saved); await view.SelectPowerAsync("Custom");
                Assert.False(view.IsDirty); Assert.True(view.CanEditPower("Custom"));
                Assert.Equal("7", view.FindControl<TextBox>("_activeTimeBox")!.Text); Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void IconPlacementAttributesAndTypedChipsUseTheirActualXmlOwners()
    {
        using var workspace = new TestWorkspace(nameof(PowerAdditiveReadingTests));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='A'><icon>changed.png</icon><placement allowFogOfWarPlacement=''>Full</placement><abstractplacementtargettype>House</abstractplacementtargettype></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = Create(path, "<powers><power name='A' type='GodPower'><icon>original.png</icon><placement>Full</placement><abstractplacementtargettype>Hero</abstractplacementtargettype></power></powers>");
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                var icon = view.FindControl<AoMDivineDataEditor.Controls.AssetPathEditor>("_iconEditor")!;
                Assert.Contains("mod-changed", icon.CompactPresenter.Classes);
                Assert.Equal("Original: original.png\nModified: changed.png", ToolTip.GetTip(icon.CompactPresenter));
                Assert.Contains("mod-changed", view.FindControl<ComboBox>("_placementBox")!.Classes);
                Assert.Contains(view.FindControl<WrapPanel>("_placementFlagChips")!.Children, chip => chip.Classes.Contains("mod-changed"));
                var types = view.FindControl<WrapPanel>("_targetTypeChips")!.Children;
                Assert.Equal(2, types.Count); Assert.Single(types, chip => chip.Classes.Contains("mod-changed"));
                view.SetModifiedMode(false); await view.SelectPowerAsync("A");
                Assert.DoesNotContain("mod-changed", icon.CompactPresenter.Classes); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }
}
