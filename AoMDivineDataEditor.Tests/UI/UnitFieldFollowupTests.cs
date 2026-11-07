using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static AutoCompleteBox OpenUnitOptionalMenu(BaselineHost host)
    {
        var panel = Field<StackPanel>(host, "_statsEditorPanel");
        var button = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(),
            button => Equals(button.Content, "Other specific attribute"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>(),
            box => box.ItemsSource?.Cast<object>().Contains("Initial Shading") == true && box.Width == 180);
    }

    private static async Task AddUnitOptional(BaselineHost host, string label)
    {
        var picker = OpenUnitOptionalMenu(host);
        Assert.Contains(label, picker.ItemsSource!.Cast<object>());
        picker.SelectedItem = label;
        await RoleSettle(host);
    }

    [Theory]
    [InlineData("Dependent Unit", "Add Dependent Unit", "_dependentUnitRows")]
    [InlineData("Veterancy", "Add Rank", "_veterancyRankRows")]
    [InlineData("Veterancy", "Add Bonus", "_veterancyBonusRows")]
    public void UnitOptionalRowButtonsAddTwiceAndRemoveWithoutDoubleParenting(string label, string buttonLabel, string rowsField)
        => WithHost(async (fixture, host) =>
        {
            await AddUnitOptional(host, label);
            var rows = Field<IList>(host, rowsField);
            var panel = Field<StackPanel>(host, "_statsEditorPanel");
            if (rowsField == "_dependentUnitRows")
            {
                var first = Assert.Single(rows.Cast<object>());
                Assignment<AutoCompleteBox>(first, "ValueAcb").SelectedItem = "B";
            }
            if (rowsField == "_veterancyBonusRows")
            {
                Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Add Rank"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RoleSettle(host);
                var rank = Assert.Single(Field<IList>(host, "_veterancyRankRows").Cast<object>());
                Assignment<ComboBox>(rank, "TypeCb").SelectedItem = "NumKills";
                Assignment<TextBox>(rank, "ValueTb").Text = "2";
            }
            var add = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, buttonLabel));
            var before = rows.Count;
            for (var i = 1; i <= 2; i++)
            {
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
                Assert.Equal(before + i, rows.Count);
                foreach (var row in rows)
                {
                    var rowPanel = Assignment<Panel>(row!, "RowPanel");
                    Assert.NotNull(rowPanel.Parent);
                    Assert.Single(rowPanel.GetVisualAncestors().OfType<Panel>(), ancestor => ReferenceEquals(ancestor, rowPanel.Parent));
                }
            }
            var lastPanel = Assignment<Panel>(rows[^1]!, "RowPanel");
            var remove = Assert.Single(lastPanel.GetLogicalDescendants().OfType<Button>(), button =>
                button.Classes.Contains(rowsField == "_dependentUnitRows" ? "remove-row" : "remove-button"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Equal(before + 1, rows.Count); Assert.Null(lastPanel.Parent);
            var remaining = rows[before]!;
            if (rowsField == "_dependentUnitRows")
            {
                Assignment<AutoCompleteBox>(remaining, "ValueAcb").SelectedItem = RoleUnit;
                Assignment<TextBox>(remaining, "XTb").Text = "1";
                Assignment<TextBox>(remaining, "ZTb").Text = "-1";
            }
            else if (rowsField == "_veterancyRankRows")
            {
                Assignment<ComboBox>(remaining, "TypeCb").SelectedItem = "NumKills";
                Assignment<TextBox>(remaining, "ValueTb").Text = "2";
            }
            else
            {
                Assignment<TextBox>(remaining, "RankIdTb").Text = "0";
                Assignment<StrictSearchableComboBox>(remaining, "ModifyTypeAcb").SelectedItem = "Damage";
                Assignment<TextBox>(remaining, "ValueTb").Text = "1.2";
                var module = (Control)Field<IDictionary>(host, "_otherSpecificAttributeContainers")["veterancydata"]!;
                Assert.Single(module.GetLogicalDescendants().OfType<AutoCompleteBox>(), box => box.Parent is StackPanel row &&
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "Include Types"))
                    .SelectedItem = "CheckpointType";
                await RoleSettle(host);
                Assert.Contains("CheckpointType", Field<HashSet<string>>(host, "_currentVeterancyIncludeTypes"));
            }
            await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Equal("yes", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal(before + 1, Field<IList>(host, rowsField).Count);
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData("Unit")]
    [InlineData("Building")]
    public void EveryOfferedUnitStatsModuleAndItsAddButtonsCanBeConstructed(string unitType)
        => WithHost(async (_, host) =>
        {
            var source = XElement.Parse(RoleDocument()).Elements("unit").First();
            source.Elements("unittype").Remove(); source.Add(new XElement("unittype", unitType));
            // Start with no optional modules. Only the real offered menu is swept;
            // managed/original-only fields retain their existing availability rules.
            source.Elements("socketunittype").Remove(); source.Elements("deadreplacement").Remove();
            source.Elements("carrycapacity").Remove();
            var baseline = new XElement("protomods", source, new XElement("unit", new XAttribute("name", "B")));
            void Reset()
            {
                host.Set("_modXmlRoot", new XElement(baseline));
                host.Set("_isDirty", false); Field<HashSet<string>>(host, "_dirtyUnitNames").Clear();
                host.Call("BuildEditorPanel", "A", true);
                host.Set("_suppressEditorChangeTracking", false);
            }
            Reset(); await RoleSettle(host);
            var labels = OpenUnitOptionalMenu(host).ItemsSource!.Cast<string>().ToArray();
            Assert.True(labels.Length > 35, $"Unexpectedly small {unitType} optional menu: {labels.Length}");
            var errors = new List<Exception>();
            void CheckEmptyHints(Control scope)
            {
                foreach (var control in scope.GetLogicalDescendants().OfType<Control>().Prepend(scope).Distinct())
                {
                    var hint = control switch
                    {
                        AutoCompleteBox selector => selector.PlaceholderText,
                        ComboBox combo => combo.PlaceholderText,
                        TextBox text => text.PlaceholderText,
                        _ => null
                    };
                    Assert.True(string.IsNullOrEmpty(hint), $"{unitType} / {control.GetType().Name}: redundant hint '{hint}'");
                }
            }
            void Capture(object? sender, DispatcherUnhandledExceptionEventArgs args)
            { errors.Add(args.Exception); args.Handled = true; }
            Dispatcher.UIThread.UnhandledException += Capture;
            try
            {
                foreach (var label in labels)
                {
                    Reset(); await RoleSettle(host);
                    await AddUnitOptional(host, label);
                    var containers = Field<IDictionary>(host, "_otherSpecificAttributeContainers");
                    var key = ((Dictionary<string, string>)typeof(ProtoEditorWindow)
                        .GetField("OtherSpecificAttributeLabels", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
                        .Single(pair => pair.Value == label).Key;
                    var container = Assert.IsAssignableFrom<Control>(containers[key]);
                    CheckEmptyHints(Field<StackPanel>(host, "_statsEditorPanel"));
                    var clicked = new HashSet<Button>();
                    var count = 0;
                    while (container.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button =>
                               button.IsEffectivelyVisible && button.IsEnabled && button.Classes.Contains("add-component") && !clicked.Contains(button)) is { } add)
                    {
                        Assert.True(++count <= 80, $"Unbounded Add chain in {label}");
                        clicked.Add(add); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
                        CheckEmptyHints(container);
                        Assert.True(errors.Count == 0, $"{unitType} / {label} / {add.Content}: {string.Join("; ", errors)}");
                    }
                    Assert.True(errors.Count == 0, $"{unitType} / {label}: {string.Join("; ", errors)}");
                }
            }
            finally { Dispatcher.UIThread.UnhandledException -= Capture; }
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData("initialshading")]
    [InlineData("damageshading")]
    public void UnitShadingUsesTheSharedCatalogAndAnEmptyClickOpensItWithoutEditing(string key)
        => WithHost(async (_, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var shading = Assert.IsType<StrictSearchableComboBox>(fields[key + ".type"]);
            Assert.Equal(ProtoConstants.KnownShadingTypeDisplayNames.Order(), shading.Options.Order());
            Assert.Null(shading.PlaceholderText);
            var input = shading.GetVisualDescendants().OfType<TextBox>().First();
            input.BringIntoView(); await RoleSettle(host);
            RoleClick(host, input); await RoleSettle(host);
            Assert.True(shading.IsDropDownOpen);
            Assert.Equal(ProtoConstants.KnownShadingTypeDisplayNames.Order(), shading.ItemsSource!.Cast<string>().Order());
            Assert.False(Field<bool>(host, "_isDirty"));
            shading.IsDropDownOpen = false; await RoleSettle(host);
            var field = key == "initialshading" ? fields["initialshading.factor"] : fields["damageshading.time"];
            var pair = Assert.IsType<StackPanel>(field.Parent);
            var cross = Assert.Single(pair.Children.OfType<Button>());
            var fieldPoint = field.TranslatePoint(default, host)!.Value;
            var crossPoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - fieldPoint.X - field.Bounds.Width, 3.9, 4.1);
            if (key == "initialshading")
            {
                var typePoint = shading.TranslatePoint(default, host)!.Value;
                Assert.True(fieldPoint.Y > typePoint.Y || fieldPoint.X > typePoint.X + shading.Bounds.Width);
            }
        }, RoleDocument("<initialshading factor='1'/><damageshading threshold='1' rate='1' time='1000'/>"), RoleFixture);

    [Fact]
    public void UnitShieldCrossAndChipHeaderPickersHaveMeasuredGapsAndKeepTheirBehavior()
        => WithHost(async (_, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var initial = fields["initialshieldpoints"];
            var pair = Assert.IsType<StackPanel>(initial.Parent!.Parent);
            var cross = Assert.Single(pair.Children.OfType<Button>());
            var point = initial.TranslatePoint(default, host)!.Value;
            var crossPoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - point.X - initial.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - point.Y - initial.Bounds.Height / 2), 0, .5);
            foreach (var title in new[] { "Unit Types", "Flags" })
            {
                var header = Assert.Single(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains($" {title} ", StringComparison.Ordinal) == true);
                var headerRow = Assert.IsType<StackPanel>(header.Parent);
                var picker = Assert.Single(headerRow.GetLogicalDescendants().OfType<AutoCompleteBox>());
                Assert.Equal(50, picker.Bounds.Width);
                var headerPoint = header.TranslatePoint(default, host)!.Value;
                var pickerPoint = picker.TranslatePoint(default, host)!.Value;
                Assert.InRange(pickerPoint.X - headerPoint.X - header.Bounds.Width, 7.9, 8.1);
                Assert.InRange(Math.Abs(pickerPoint.Y + picker.Bounds.Height / 2 - headerPoint.Y - header.Bounds.Height / 2), 0, .5);
            }
            AssertRole(fields["unitaitype"], EditorFieldWidthRole.LongEnum);
            AssertRole(fields["impacttype"], EditorFieldWidthRole.LongEnum);
            AssertRole(fields["initialunitaistance"], EditorFieldWidthRole.LongEnum);
            cross.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.False(fields.ContainsKey("initialshieldpoints")); Assert.False(fields.ContainsKey("unitshieldregen"));
            Assert.Empty(await Save(host));
        }, RoleDocument("<maxshieldpoints>2</maxshieldpoints><initialshieldpoints>2</initialshieldpoints><unitshieldregen>0</unitshieldregen><initialunitaistance>StandGround</initialunitaistance>"), RoleFixture);

    [Fact]
    public void DiscardingUnitStatsDuringNavigationHighlightsTheNewUnitAndStaysClean()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
            host.Set("_suppressUnitListSelectionChange", true);
            Field<TabStrip>(host, "_unitTabs").SelectedIndex = 1;
            host.Set("_allCurrentNames", new List<string> { "A", "B", RoleUnit });
            host.Call("FilterUnitList"); host.Call("SetMainDocumentTarget", kind, true, "A");
            var list = Field<ListBox>(host, "_unitList");
            object Item(string name) => Field<IList>(host, "_filteredUnitNames").Cast<object>()
                .Single(item => Equals(item.GetType().GetProperty("UnitName")!.GetValue(item), name));
            list.SelectedItem = Item("A");
            host.Set("_suppressUnitListSelectionChange", false); await RoleSettle(host);
            ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text = "17";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            list.SelectedItem = Item("B");
            Prompt? prompt = null;
            for (var i = 0; i < 200 && (prompt = host.OwnedWindows.OfType<Prompt>().FirstOrDefault()) == null; i++) await Task.Delay(10);
            Assert.NotNull(prompt); Assert.Contains("Discard", prompt.PromptText);
            prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var navigation = Field<Task>(host, "_unitSelectionChangeTask");
            Assert.Same(navigation, await Task.WhenAny(navigation, Task.Delay(3000))); await navigation;
            await RoleSettle(host);
            Assert.Equal("B", Field<string>(host, "_currentUnitName")); Assert.Same(Item("B"), list.SelectedItem);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            list.SelectedItem = Item("A"); await Field<Task>(host, "_unitSelectionChangeTask"); await RoleSettle(host);
            Assert.Same(Item("A"), list.SelectedItem); Assert.Empty(host.OwnedWindows.OfType<Prompt>());
            Assert.Equal("10", ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text);
        }, RoleDocument(), RoleFixture);
}
