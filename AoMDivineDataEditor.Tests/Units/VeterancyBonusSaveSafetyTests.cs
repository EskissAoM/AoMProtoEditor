using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private const string VeterancyBonusBody = "<veterancyranks><rank><numkills>2</numkills></rank></veterancyranks>" +
        "<veterancybonus><rank id='0'><veterancymodify modifytype='Damage'>1.2</veterancymodify>" +
        "<veterancymodify modifytype='Speed'>1.3</veterancymodify></rank>{0}</veterancybonus>";
    private static string BonusDocument(bool include = false)
        => RoleDocument(string.Format(VeterancyBonusBody, include ? "<includetypes><unittype>Unit</unittype></includetypes>" : ""));
    private static AutoCompleteBox BonusIncludePicker(BaselineHost host)
        => Assert.IsType<AutoCompleteBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["veterancybonus.includetypes"]);

    [Fact]
    public void VeterancyRanksWithoutBonusesCanSaveAndReopenWithoutIncludeTypes()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); await AddUnitOptional(host, "Veterancy");
            var add = Assert.Single(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Add Rank"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var rank = RoleAssignment(host, "_veterancyRankRows");
            Assignment<TextBox>(rank, "ValueTb").Text = "3"; await RoleSettle(host);
            Assert.Empty(Field<IList>(host, "_veterancyBonusRows"));
            Assert.Empty(Field<HashSet<string>>(host, "_currentVeterancyIncludeTypes"));
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First();
            Assert.Equal("3", saved.Element("veterancyranks")!.Element("rank")!.Element("numkills")!.Value);
            Assert.Null(saved.Element("veterancybonus"));
            host.Call("BuildEditorPanel", "B", true); host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal("3", Assignment<TextBox>(RoleAssignment(host, "_veterancyRankRows"), "ValueTb").Text);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VeterancyBonusMissingIncludeBlocksSaveAndCorrectionPreservesEveryBonus(bool newlyAdded)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = Directory.GetFiles(Path.Combine(fixture.Workspace.RootPath, "TestMod"), "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            if (newlyAdded)
            {
                await AddUnitOptional(host, "Veterancy");
                var add = Assert.Single(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Content, "Add Bonus"));
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RoleSettle(host);
                var row = RoleAssignment(host, "_veterancyBonusRows");
                Assignment<StrictSearchableComboBox>(row, "ModifyTypeAcb").SelectedItem = "Damage";
                Assignment<TextBox>(row, "ValueTb").Text = "1.2";
            }
            else
                Assignment<TextBox>(Field<IList>(host, "_veterancyBonusRows")[0]!, "ValueTb").Text = "1.4";
            await RoleSettle(host);
            var count = Field<IList>(host, "_veterancyBonusRows").Count;
            host.Call("ApplyCurrentEdits", (object)null!);
            var draft = Field<XElement>(host, "_modXmlRoot").Elements("unit").First();
            Assert.Equal(count, draft.Descendants("veterancymodify").Count());
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 1;
            var errors = await Save(host); await RoleSettle(host);
            Assert.Contains(errors, message => message.Contains("select at least one Include Type", StringComparison.Ordinal));
            var report = Assert.Single(errors);
            Assert.Contains("Invalid ProtoUnit Stats — A", report, StringComparison.Ordinal);
            Assert.Equal(1, report.Split("before saving", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("ProtoUnit: A", report, StringComparison.Ordinal);
            Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
            Assert.True(Field<bool>(host, "_isDirty"));
            var picker = BonusIncludePicker(host);
            Assert.Equal(0, Field<TabControl>(host, "_editorTabs").SelectedIndex);
            Assert.Contains("invalid-field", picker.Classes); Assert.True(picker.IsKeyboardFocusWithin);
            picker.Text = "Checkp"; await RoleSettle(host);
            Assert.Empty(Field<HashSet<string>>(host, "_currentVeterancyIncludeTypes"));
            Assert.Contains("invalid-field", picker.Classes);
            Assert.Contains(await Save(host), message => message.Contains("Include Type", StringComparison.Ordinal));
            picker.SelectedItem = "CheckpointType"; await RoleSettle(host);
            Assert.DoesNotContain("invalid-field", picker.Classes);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First();
            Assert.Equal(count, saved.Descendants("veterancymodify").Count());
            Assert.Equal("CheckpointType", saved.Element("veterancybonus")!.Element("includetypes")!.Element("unittype")!.Value);
            Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "B", true); host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal(count, Field<IList>(host, "_veterancyBonusRows").Count);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, newlyAdded ? RoleDocument() : BonusDocument(), RoleFixture);

    [Fact]
    public void VeterancyBonusRemovingLastIncludeChipPreservesDraftAndCanRemoveBonusesInstead()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var picker = BonusIncludePicker(host);
            var includeGroup = picker.Parent!.Parent!;
            var remove = Assert.Single(includeGroup.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("chip-remove-button"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Empty(Field<HashSet<string>>(host, "_currentVeterancyIncludeTypes"));
            Assert.Equal(2, Field<XElement>(host, "_modXmlRoot").Descendants("veterancymodify").Count());
            Assert.Contains(await Save(host), message => message.Contains("Include Type", StringComparison.Ordinal));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            foreach (var row in Field<IList>(host, "_veterancyBonusRows").Cast<object>().ToArray())
            {
                var panel = Assignment<Panel>(row, "RowPanel");
                Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-button"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RoleSettle(host);
            }
            Assert.DoesNotContain("invalid-field", picker.Classes);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First();
            Assert.Null(saved.Element("veterancybonus")); Assert.NotNull(saved.Element("veterancyranks"));
        }, BonusDocument(true), RoleFixture);

    [Fact]
    public void VeterancyBonusRawXmlCannotBypassTheIncludeRequirementAndCanBeCorrected()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); await Unlock(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var candidate = XElement.Parse(Raw(host).Text);
            candidate.Add(XElement.Parse("<unit>" + string.Format(VeterancyBonusBody, "") + "</unit>").Elements());
            Raw(host).Text = candidate.ToString(); await RoleSettle(host);
            var draft = Raw(host).Text;
            Assert.Contains(await Save(host), message => message.Contains("Include Type", StringComparison.Ordinal));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.False(Raw(host).IsReadOnly); Assert.Equal(draft, Raw(host).Text);
            candidate.Element("veterancybonus")!.Add(new XElement("includetypes", new XElement("unittype", "CheckpointType")));
            Raw(host).Text = candidate.ToString(); await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.True(Raw(host).IsReadOnly);
            Assert.Equal(2, XDocument.Load(fixture.ProtoPath).Descendants("veterancymodify").Count());
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void VeterancyBonusEmptyModifyTypeBlocksSaveRatherThanDroppingTheRow()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var row = Field<IList>(host, "_veterancyBonusRows")[0]!;
            var type = Assignment<StrictSearchableComboBox>(row, "ModifyTypeAcb");
            type.Focus(); type.Text = "";
            Assignment<TextBox>(row, "ValueTb").Focus(); await RoleSettle(host);
            Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Contains(await Save(host), message => message.Contains("Modify Type is required", StringComparison.Ordinal));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.Contains("invalid-field", type.Classes);
            type.SelectedItem = "Damage"; await RoleSettle(host);
            Assert.DoesNotContain("invalid-field", type.Classes);
            Assert.Empty(await Save(host));
            Assert.Equal(2, XDocument.Load(fixture.ProtoPath).Descendants("veterancymodify").Count());
        }, BonusDocument(true), RoleFixture);

    [Theory]
    [InlineData("replace")]
    [InlineData("tab")]
    [InlineData("window")]
    public void VeterancyBonusInvalidDraftCanKeepEditingOrDiscardWithoutValidation(string operation)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", System.Reflection.BindingFlags.NonPublic)!, "Units");
            host.Set("_suppressUnitListSelectionChange", true);
            Field<TabStrip>(host, "_unitTabs").SelectedIndex = 1;
            await RoleSettle(host);
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            host.Set("_suppressEditorChangeTracking", false);
            var tab = host.Call("CreateDocumentTab", operation != "tab", kind, true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First());
            Field<IList>(host, "_openDocumentTabs").Add(tab); host.Set("_activeDocumentTab", tab);
            if (operation != "tab") host.Set("_mainDocumentTab", tab);
            Assignment<TextBox>(Field<IList>(host, "_veterancyBonusRows")[0]!, "ValueTb").Text = "1.7";
            await RoleSettle(host);
            Assert.True((bool)host.Call("IsDocumentTabDirty", tab)!);
            foreach (var discard in new[] { false, true })
            {
                Task action;
                if (operation == "window") { host.Close(); action = Task.CompletedTask; }
                else action = (Task)host.Call(operation == "tab" ? "CloseDocumentTabAsync" : "ConfirmMainDocumentReplacementAsync",
                    operation == "tab" ? new[] { tab } : new[] { kind, (object)true, "B" })!;
                for (var i = 0; i < 300 && !host.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                Assert.Contains("Discard", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
                prompt.GetType().GetMethod(discard ? "ConfirmButton_Click" : "CancelButton_Click", Private)!
                    .Invoke(prompt, new object?[] { null, new RoutedEventArgs() });
                await action.WaitAsync(TimeSpan.FromSeconds(5)); await RoleSettle(host);
                if (!discard)
                {
                    Assert.True((bool)host.Call("IsDocumentTabDirty", tab)!);
                    Assert.Equal("1.7", Assignment<TextBox>(Field<IList>(host, "_veterancyBonusRows")[0]!, "ValueTb").Text);
                }
            }
            Assert.Empty(host.OwnedWindows.OfType<Prompt>());
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            if (operation == "window") Assert.False(host.IsVisible);
            else Assert.False((bool)host.Call("IsDocumentTabDirty", tab)!);
        }, BonusDocument(), RoleFixture);
}
