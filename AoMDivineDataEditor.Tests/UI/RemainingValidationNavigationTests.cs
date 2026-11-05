using System.Xml.Linq;
using System.Xml.XPath;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("GodPower", "power='Missing'", "Player", "0", "@power", "CustomPower")]
    [InlineData("Cost", "resource='Missing'", "ProtoUnit", "BaseUnit", "@resource", "Food")]
    [InlineData("CommandAdd", "command='Missing'", "ProtoUnit", "BaseUnit", "@command", "CustomCommand")]
    [InlineData("Damage", "damagetype='Missing' action='HandAttack'", "ProtoUnit", "BaseUnit", "@damagetype", "Hack")]
    [InlineData("ResourceByKBStat", "resource='Food' kbStat='Missing'", "Player", "0", "@kbStat", "unitsLost")]
    [InlineData("ResearchPoints", "", "Tech", "Missing", "target", "B")]
    [InlineData("ResearchPoints", "", "TechType", "Missing", "target", "CustomType")]
    public void RemainingTechnologyFieldRevealsExactControlAndCorrectionPersists(string subtype, string attributes, string targetType, string target, string path, string correction)
        => WithTechnologyPresentation($"<effects><effect type='Data' subtype='{subtype}' amount='1' relativity='Absolute' {attributes}><target type='{targetType}'>{target}</target></effect></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "effect");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var invalid = Assert.Single(editor.GetLogicalDescendants().OfType<Control>().Distinct(), c => c.Classes.Contains("invalid-field"));
            Assert.True(invalid.IsKeyboardFocusWithin);
            var effect = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Same(invalid, EditorValidationBindings.Find(editor, new LocatedValidationIssue("source", ValidationSourceLocation.From(effect, path))));
            switch (invalid)
            {
                case AutoCompleteBox selector: selector.SelectedItem = correction; break;
                case ComboBox selector: selector.SelectedItem = correction; break;
                default: throw new InvalidOperationException($"Unexpected field {invalid.GetType().Name}");
            }
            await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", invalid.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var saved = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Equal(correction, path.StartsWith('@') ? (string?)saved.Attribute(path[1..]) : saved.Element(path)!.Value);
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), c => c.Classes.Contains("invalid-field"));
        });
}

public sealed partial class PowerFieldPresentationTests
{
    [Theory]
    [InlineData("SwapUnit", "<abstracttype swapto='OtherUnit'>Missing</abstracttype>", "abstracttype", "OtherUnit")]
    [InlineData("SwapUnit", "<abstracttype swapto='Missing'>OtherUnit</abstracttype>", "abstracttype/@swapto", "OtherUnit")]
    [InlineData("SwapUnit", "<swapdelay>bad</swapdelay>", "swapdelay", "2")]
    [InlineData("SwapUnit", "<convertusingminmax minswapamount='bad' maxswapamount='2'/>", "convertusingminmax/@minswapamount", "1")]
    [InlineData("SwapUnit", "<convertusingminmax minswapamount='1' maxswapamount='bad'/>", "convertusingminmax/@maxswapamount", "2")]
    [InlineData("Lure", "<ageinfo maxfood='bad' numspawns='1'>1</ageinfo>", "ageinfo/@maxfood", "5")]
    [InlineData("Lure", "<ageinfo maxfood='5' numspawns='bad'>1</ageinfo>", "ageinfo/@numspawns", "2")]
    [InlineData("FoxCharm", "<ageadjustment affectedNumber='bad'>ArchaicAge</ageadjustment>", "ageadjustment/@affectedNumber", "2")]
    [InlineData("Bolt", "<placementrestriction radius='bad'>Unit</placementrestriction>", "placementrestriction/@radius", "2")]
    [InlineData("UnitModify", "<unitmodify apply='Add' amount='1'>Hitpoints</unitmodify><unitmodify apply='Add' amount='bad'>Hitpoints</unitmodify>", "unitmodify[2]/@amount", "2")]
    [InlineData("EffectInArea", "<onhiteffect type='Boost'><modify type='Damage'>bad</modify></onhiteffect>", "onhiteffect/modify", "2")]
    [InlineData("EffectInArea", "<onhiteffect type='Boost'><modifyramp type='Damage' init='bad' final='1'/></onhiteffect>", "onhiteffect/modifyramp/@init", "2")]
    [InlineData("SwitchingEffects", "<tiertype>Time</tiertype>", "interval", "2")]
    [InlineData("Bolt", "<soundset type='StartSound' listenertype='All' playdelay='1'>first</soundset><soundset type='EndSound' listenertype='All' playdelay='bad'>second</soundset>", "soundset[2]/@playdelay", "2")]
    [InlineData("Bolt", "<soundset type='StartSound' listenertype='All' fadein='1'>first</soundset><soundset type='EndSound' listenertype='All' fadein='bad'>second</soundset>", "soundset[2]/@fadein", "2")]
    public void RemainingPowerGuardTargetsTheActualValueAndCorrectionPersists(string type, string body, string path, string correction)
        => WithPower(type, body, false, async (view, window, file) =>
        {
            var before = File.ReadAllBytes(file);
            var source = XDocument.Load(file).Descendants("power").First();
            var saving = view.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "");
            Assert.False(await saving); await Settle(window);
            Assert.Equal(before, File.ReadAllBytes(file));
            var invalid = Assert.Single(view.GetLogicalDescendants().OfType<Control>().Distinct(), c => c.Classes.Contains("invalid-field"));
            Assert.True(invalid.IsKeyboardFocusWithin);
            var attributeOffset = path.LastIndexOf("/@", StringComparison.Ordinal);
            var sourceElement = source.XPathSelectElement(attributeOffset < 0 ? path : path[..attributeOffset])!;
            var location = sourceElement == null ? ValidationSourceLocation.From(source, path) :
                ValidationSourceLocation.From(sourceElement, attributeOffset < 0 ? null : path[(attributeOffset + 1)..]);
            Assert.Same(invalid, EditorValidationBindings.Find(view, new LocatedValidationIssue("source", location)));
            if (invalid is AutoCompleteBox selector) selector.SelectedItem = correction;
            else Assert.IsType<TextBox>(invalid).Text = correction;
            await Settle(window); Assert.DoesNotContain("invalid-field", invalid.Classes);
            await ValidationPopupTest.CompleteWithoutPopup(window, view.SaveAsync());
            var saved = XDocument.Load(file).Descendants("power").First();
            var element = saved.XPathSelectElement(attributeOffset < 0 ? path : path[..attributeOffset])!;
            Assert.Equal(correction, attributeOffset < 0 ? element.Value : (string?)element.Attribute(path[(attributeOffset + 2)..]));
            await view.SelectPowerAsync("B"); await Settle(window);
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), c => c.Classes.Contains("invalid-field"));
        });

    [Fact]
    public void UnrepresentedSoundDelayKeepsPopupFallbackAndCanBeCorrectedThroughXml()
        => WithPower("Bolt", "<soundset type='StartSound' listenertype='All' delay='1'>first</soundset><soundset type='EndSound' listenertype='All' delay='bad'>second</soundset>", false,
            async (view, window, file) =>
        {
            var before = File.ReadAllBytes(file);
            var source = XDocument.Load(file);
            var secondSound = source.Descendants("soundset").Last();
            var validation = PowerSaveValidator.Validate(source, new PowerSaveCatalogs(["Bolt"]));
            var issue = Assert.Single(validation.Issues, item => item.Message.Contains("soundset/@delay", StringComparison.Ordinal));
            Assert.Equal(ValidationSourceLocation.From(secondSound, "@delay"), issue.Location);
            var saving = view.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "/soundset/@delay");
            Assert.False(await saving); await Settle(window);
            Assert.Equal(before, File.ReadAllBytes(file));
            Assert.Null(EditorValidationBindings.Find(view, issue));
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), field => field.Classes.Contains("invalid-field"));

            await PowerXmlTestHelpers.UnlockAsync(view);
            var raw = view.FindControl<TextEditor>("_rawXmlEditor")!;
            var candidate = XElement.Parse(raw.Text);
            candidate.Elements("soundset").Last().SetAttributeValue("delay", "2");
            raw.Text = candidate.ToString();
            await Settle(window);
            await ValidationPopupTest.CompleteWithoutPopup(window, view.SaveAsync());
            var savedSounds = XDocument.Load(file).Descendants("soundset").ToArray();
            Assert.Equal(new[] { "1", "2" }, savedSounds.Select(sound => (string?)sound.Attribute("delay")));
            Assert.All(savedSounds, sound => Assert.Null(sound.Attribute("playdelay")));
            Assert.True(raw.IsReadOnly); Assert.False(view.IsDirty);
        });
}

public sealed partial class GodFieldPresentationTests
{
    [Theory]
    [InlineData("<buildingefficiency>-1</buildingefficiency>", "buildingefficiency", "1", 0)]
    [InlineData("<secondaryprayerefficiency g='-1' v='0' z='0'/>", "secondaryprayerefficiency/@g", "1", 0)]
    [InlineData("<deathmatchtech>Missing</deathmatchtech>", "deathmatchtech", "TechA", 0)]
    [InlineData("<startingunits><unit count='bad'>UnitA</unit></startingunits>", "startingunits/unit/@count", "1", 1)]
    [InlineData("<startingunits><unit count='1' x='bad'>UnitA</unit></startingunits>", "startingunits/unit/@x", "0", 1)]
    [InlineData("<startingqueue><unit delay='bad'>UnitA</unit></startingqueue>", "startingqueue/unit/@delay", "1", 1)]
    [InlineData("<startingresources><food>-1</food></startingresources>", "startingresources/food", "5", 1)]
    [InlineData("<terraincreeps><terraincreep creep='GaiaCreep' maxradius='-1' growthrate='0' decayrate='0' minupdateinterval='0' maxupdateinterval='1'><protounit>UnitA</protounit></terraincreep></terraincreeps>", "terraincreeps/terraincreep/@maxradius", "1", 2)]
    [InlineData("<bountyresourceearning><bountydamagegoal>-1</bountydamagegoal></bountyresourceearning>", "bountyresourceearning/bountydamagegoal", "1", 2)]
    public void RemainingMajorGodFieldRevealsItsTabAndCorrectionCommits(string body, string path, string correction, int tab)
    {
        using var workspace = new TestWorkspace();
        var file = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", "<civsmods><civ><name>A</name>" + body + "</civ><civ><name>B</name></civ></civsmods>");
        var before = File.ReadAllBytes(file);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], file, cultureNames: ["Greek"], technologyNames: ["TechA"], protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA"]);
            var window = new Window { Content = view, Width = 1440, Height = 950 };
            try
            {
                window.Show(); view.SetModifiedMode(true); view.SelectMajorGod("A"); await Settle(window);
                var saving = view.CommitCurrentMajorGodAsync();
                await ValidationPopupTest.Dismiss(window, saving, "");
                Assert.False(await saving); await Settle(window);
                Assert.Equal(before, File.ReadAllBytes(file));
                var invalid = Assert.Single(view.GetLogicalDescendants().OfType<Control>().Distinct(), c => c.Classes.Contains("invalid-field"));
                Assert.True(invalid.IsKeyboardFocusWithin);
                Assert.Equal(tab, view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex);
                var source = XDocument.Load(file).Descendants("civ").First();
                Assert.Same(invalid, EditorValidationBindings.Find(view, new LocatedValidationIssue("source", ValidationSourceLocation.From(source, path))));
                if (invalid is AutoCompleteBox selector) selector.SelectedItem = correction;
                else Assert.IsType<TextBox>(invalid).Text = correction;
                await Settle(window); Assert.DoesNotContain("invalid-field", invalid.Classes);
                Assert.True(await view.CommitCurrentMajorGodAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                view.SelectMajorGod("B"); await Settle(window);
                Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), c => c.Classes.Contains("invalid-field"));
                Assert.Empty(window.OwnedWindows.OfType<Prompt>());
            }
            finally { foreach (var popup in window.OwnedWindows.ToArray()) popup.Close(); window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }
}

public sealed partial class CommandXmlEditingTests
{
    [Fact]
    public void NewlyAddedCommandReferenceIsRegisteredAndPersists()
        => WithCommand(async (window, path, control) =>
        {
            var add = Assert.Single(window.GetLogicalDescendants().OfType<Button>().Distinct(), b => Equals(b.Content, "Add Forbid Tech"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selector = Assert.IsType<AutoCompleteBox>(Field<Dictionary<string, Control>>(window, "_valueEditors")["forbidtech"]);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); await Task.Delay(25);
            selector.SelectedItem = "Tech";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal("Tech", selector.Text);
            Assert.Equal("Tech", Assert.IsType<ProtoUnitCommandDefinition>(Call(window, "Capture")).Values["forbidtech"]);
            Assert.Empty(await Save(window)); Assert.Equal(1, control.Calls);
            Assert.Equal("Tech", XDocument.Load(path).Descendants("forbidtech").Single().Value);
        });

    [Fact]
    public void NewlyBuiltSpawnFieldsRevealTheMissingValueAndCorrectionSaves()
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            Field<ComboBox>(window, "_commandModeBox").SelectedItem = "Spawn";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); await Task.Delay(25);
            var fields = Field<Dictionary<string, Control>>(window, "_valueEditors");
            var unit = Assert.IsType<AutoCompleteBox>(fields["protounit"]);
            Assert.Contains(await Save(window), error => error.Contains("Proto Unit, Amount and Associated Tech", StringComparison.Ordinal));
            Assert.Contains("invalid-field", unit.Classes); Assert.True(unit.IsKeyboardFocusWithin);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            unit.SelectedItem = "Base";
            Assert.IsType<TextBox>(fields["amount"]).Text = "1";
            Assert.IsType<AutoCompleteBox>(fields["associatedtech"]).SelectedItem = "Tech";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.DoesNotContain("invalid-field", unit.Classes);
            Assert.Empty(await Save(window)); Assert.Equal(1, control.Calls);
        });
}

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void UnitStatsSummaryRevealsItsActualFieldAndCorrectionSaves()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var field = Assert.IsType<TextBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["ondiscoverlos"]);
            field.Text = "."; await RoleSettle(host);
            host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 1;
            Assert.NotEmpty(await Save(host)); await RoleSettle(host);
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.Contains("invalid-field", field.Classes); Assert.True(field.IsKeyboardFocusWithin);
            Assert.Equal(0, host.FindControl<TabControl>("_editorTabs")!.SelectedIndex);
            field.Text = "3"; await RoleSettle(host);
            Assert.DoesNotContain("invalid-field", field.Classes);
            Assert.Empty(await Save(host));
            Assert.Equal("3", XDocument.Load(fixture.ProtoPath).Descendants("ondiscoverlos").Single().Value);
            host.Call("BuildEditorPanel", "B", true); await RoleSettle(host);
            Assert.DoesNotContain(host.GetLogicalDescendants().OfType<Control>(), c => c.Classes.Contains("invalid-field"));
        }, RoleDocument("<ondiscoverlos>2.5</ondiscoverlos>"), RoleFixture);
}

public sealed partial class TacticsStartupTests
{
    [Fact]
    public void AbilityRangeFailureRevealsRangeAndCorrectionSaves()
        => WithStandaloneXml(true, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            var field = Assert.IsType<TextBox>(child.GetType().BaseType!.GetField("_abilityRangeIndicatorRangeEditorForValidation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(child));
            field.Text = "";
            Assert.Contains(await SaveStandaloneChecked(child, true), error => error.Contains("Range value", StringComparison.Ordinal));
            Assert.Contains("invalid-field", field.Classes); Assert.True(field.IsKeyboardFocusWithin);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            field.Text = "2";
            Assert.DoesNotContain("invalid-field", field.Classes);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(await SaveStandaloneChecked(child, true)); Assert.Equal(1, control.Calls);
        }, abilitySource: "<power name='AbilityA' type='UnitAction'><unitaction>HandAttack</unitaction><rangeindicator range='1'>Circle</rangeindicator></power>");

    [Fact]
    public void InvalidActionNameRevealsTheActionTabAndCorrectionSaves()
        => WithStandaloneXml(false, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            var tabs = child.GetLogicalDescendants().OfType<TabControl>().Distinct().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Tactic")));
            var name = child.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(c => c.Text == "HandAttack");
            name.Text = "Invalid Name";
            tabs.SelectedIndex = 1;
            Assert.Contains(await SaveStandaloneChecked(child, false), error => error.Contains("invalid", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("invalid-field", name.Classes); Assert.True(name.IsKeyboardFocusWithin);
            Assert.Equal(0, tabs.SelectedIndex); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            name.Text = "HandAttack";
            Assert.DoesNotContain("invalid-field", name.Classes);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(await SaveStandaloneChecked(child, false)); Assert.Equal(1, control.Calls);
        });
}
