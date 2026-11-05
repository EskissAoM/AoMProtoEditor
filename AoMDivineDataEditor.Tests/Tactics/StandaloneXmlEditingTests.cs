using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    private const string AbilityXml = "<power name='AbilityA' type='UnitAction' future='keep'><radius>10</radius><placement enemy='' FutureAttribute='remove'>Unit</placement><!--keep--><future keep='yes'/></power>";
    private const string TacticsXml = "<tactics future='keep'><action><name>HandAttack</name><type>Attack</type><rate type='All'>1</rate><future keep='yes'/></action><tactic>Default<action priority='100'>HandAttack</action><attacktype>BaseUnit</attacktype></tactic><!--keep--></tactics>";
    private static TextEditor StandaloneRaw(BaselineHost child) => child.FindControl<TextEditor>("_xmlPreviewText")!;
    private static string RawCandidate(bool ability, string value)
        => ability ? $"<power name='AbilityA' type='UnitAction'><radius>{value}</radius><future keep='yes'/></power>"
            : $"<tactics><action><name>HandAttack</name><type>Attack</type><maxrange>{value}</maxrange><future keep='yes'/></action><tactic>Default<action priority='100'>HandAttack</action></tactic></tactics>";

    private sealed class StandaloneSaveControl
    {
        internal int Calls;
        internal bool Fail;
        internal TacticsDocumentSaveOutcome Outcome = TacticsDocumentSaveOutcome.Saved;
        internal Action<BaselineHost>? WhileSaving;
        internal string? OriginalName;
        internal XElement? Ability;
        internal XDocument? Tactics;
        internal Func<string, XElement, Dictionary<string, string>, FileIntegrityTransaction.FileState?, Task>? AbilitySave;
        internal bool CheckExternalState;
    }

    private static void WithStandaloneXml(bool ability,
        Func<BaselineSafetyFixture, BaselineHost, BaselineHost, string, StandaloneSaveControl, Task> check,
        bool readOnly = false, string? abilitySource = null, string? tacticsSource = null)
    {
        using var fixture = new BaselineSafetyFixture();
        var source = ability ? new XDocument(new XElement("powersmod", XElement.Parse(abilitySource ?? AbilityXml)))
            : XDocument.Parse("<!--document comment-->" + (tacticsSource ?? TacticsXml), LoadOptions.PreserveWhitespace);
        var path = fixture.Workspace.WriteText(ability ? "TestMod/game/data/gameplay/powers_mods.xml" : "TestMod/game/data/tactics/Test.tactics", source.ToString(SaveOptions.DisableFormatting));
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        var original = new XDocument(source);
        var state = FileIntegrityTransaction.CaptureState(path);
        var control = new StandaloneSaveControl();
        if (abilitySource != null)
            fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", "ID = \"STR_ABILITY_ABILITYA\" ; Str = \"Display\"\r\n");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = new BaselineHost(new RootGameData(fixture.GameRoot));
            try
            {
                if (ability)
                    await child.InitializeAbilityDefinitionEditorAsync("AbilityA", readOnly, source.Root!.Element("power")!, fixture.ProtoPath,
                        async (oldName, power, strings, stringState) =>
                        {
                            control.Calls++; control.OriginalName = oldName;
                            if (control.CheckExternalState && state != FileIntegrityTransaction.CaptureState(path)) throw new IOException("Ability changed after this editor was opened. No files were written.");
                            if (control.Fail) throw new IOException("simulated Ability save failure");
                            if (control.AbilitySave != null) await control.AbilitySave(oldName, power, strings, stringState);
                            else
                            {
                                var saved = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                                saved.Root!.Element("power")!.ReplaceWith(new XElement(power));
                                ProtoEditorWindow.SaveAbilityXmlDocument(saved, path);
                            }
                            control.Ability = new XElement(power);
                            control.WhileSaving?.Invoke(child);
                        }, ["AbilityA", "AbilityB"], () => ["BaseUnit", "CustomUnit"], () => ["CustomType"]);
                else
                    await child.InitializeTacticsActionEditorAsync(readOnly ? "Test.tactics.xmb" : "Test.tactics", readOnly, source,
                        document =>
                        {
                            control.Calls++;
                            if (control.Fail) throw new IOException("simulated Tactics save failure");
                            if (control.Outcome == TacticsDocumentSaveOutcome.Reloaded) child.ReloadTacticsActionEditorDocument(XDocument.Load(path, LoadOptions.PreserveWhitespace));
                            if (control.Outcome != TacticsDocumentSaveOutcome.Saved) return Task.FromResult(control.Outcome);
                            ProtoEditorWindow.SaveAbilityXmlDocument(document, path); control.Tactics = new XDocument(document);
                            control.WhileSaving?.Invoke(child);
                            return Task.FromResult(TacticsDocumentSaveOutcome.Saved);
                        }, () => ["BaseUnit", "CustomUnit"], () => ["CustomType"], owner);
                child.Show(); child.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                await check(fixture, owner, child, path, control);
                Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
                Assert.True(XNode.DeepEquals(original, source));
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    private static async Task UnlockStandaloneXml(BaselineHost child)
    {
        child.FindControl<Button>("_editUnitXmlButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; StandaloneRaw(child).IsReadOnly && i < 300; i++) await Task.Delay(10);
        Assert.False(StandaloneRaw(child).IsReadOnly);
    }

    private static async Task<List<string>> SaveStandaloneChecked(BaselineHost child, bool ability)
    {
        var errors = new List<string>();
        var saving = (Task)child.Call(ability ? "SaveAbilityDefinitionEditorAsync" : "SaveTacticsActionEditorAsync")!;
        for (var i = 0; !saving.IsCompleted && i < 1000; i++)
        {
            foreach (var prompt in child.OwnedWindows.OfType<Prompt>().ToArray())
            { errors.Add(prompt.PromptTitle + ": " + prompt.PromptText); prompt.Close(); }
            await Task.Delay(10);
        }
        Assert.True(saving.IsCompleted, "Standalone Save did not finish."); await saving;
        return errors;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneXmlCleanUnlockSaveRelocksWithoutWriting(bool ability)
        => WithStandaloneXml(ability, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.True(StandaloneRaw(child).IsReadOnly); Assert.True(StandaloneRaw(child).IsEnabled);
            await UnlockStandaloneXml(child);
            Assert.False((bool)Field(child, "_isDirty")!);
            Assert.False(child.FindControl<Grid>("_unitStructuredForm")!.IsEnabled);
            Assert.Empty(await SaveStandaloneChecked(child, ability));
            Assert.True(StandaloneRaw(child).IsReadOnly); Assert.True(child.FindControl<Grid>("_unitStructuredForm")!.IsEnabled);
            Assert.Equal(0, control.Calls); Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneXmlOriginalOrCompiledSourceCannotUnlock(bool ability)
        => WithStandaloneXml(ability, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.True(StandaloneRaw(child).IsReadOnly); Assert.True(StandaloneRaw(child).Focusable);
            Assert.False(child.FindControl<Button>("_editUnitXmlButton")!.IsVisible);
            Assert.Empty(await SaveStandaloneChecked(child, ability)); Assert.Equal(0, control.Calls);
            Assert.Equal(before, File.ReadAllBytes(path));
        }, readOnly: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneXmlSaveUsesDefinitionRootAndPreservesUnknownContent(bool ability)
        => WithStandaloneXml(ability, async (_, _, child, path, control) =>
        {
            await UnlockStandaloneXml(child);
            var text = ability
                ? "<power name='AbilityA' type='UnitAction' extra='yes'><radius>17</radius><placement>Unit</placement><!--keep--><future keep='yes'><nested/></future></power>"
                : "<tactics extra='yes'><action><name>HandAttack</name><type>Attack</type><rate type='CustomUnit'>2</rate><damagebonus unittype='CustomUnit'>2</damagebonus><future keep='yes'><nested/></future></action><tactic>Default<action priority='90'>HandAttack</action><attacktype>CustomType</attacktype></tactic><!--keep--></tactics>";
            StandaloneRaw(child).Text = text; child.Call("UpdateXmlPreview", true);
            Assert.Equal(text, StandaloneRaw(child).Text); Assert.True((bool)Field(child, "_isDirty")!);
            Assert.Empty(await SaveStandaloneChecked(child, ability)); Assert.Equal(1, control.Calls);
            Assert.True(StandaloneRaw(child).IsReadOnly); Assert.False((bool)Field(child, "_isDirty")!);
            var saved = XDocument.Load(path); var root = ability ? saved.Root!.Element("power")! : saved.Root!;
            Assert.Equal(ability ? "power" : "tactics", root.Name.LocalName);
            Assert.Equal("yes", (string?)root.Attribute("extra")); Assert.NotNull(root.Descendants("future").Single().Element("nested"));
            Assert.Contains(root.Nodes().OfType<XComment>(), comment => comment.Value == "keep");
            Assert.DoesNotContain("__", File.ReadAllText(path));
            if (ability) { Assert.Null(root.Element("placement")!.Attribute("FutureAttribute")); Assert.Equal("AbilityA", control.OriginalName); }
            else Assert.Contains(saved.Nodes().OfType<XComment>(), comment => comment.Value == "document comment");
            Assert.Empty(await SaveStandaloneChecked(child, ability)); // Forms rebuilt from the saved definition.
            var reopened = XDocument.Load(path);
            Assert.NotNull(reopened.Descendants("future").Single().Element("nested"));
        });

    public static IEnumerable<object[]> InvalidStandaloneXmlCases()
    {
        foreach (var xml in new[]
        {
            "<power>", "<unit name='AbilityA'/>", "<power xmlns='future' name='AbilityA' type='UnitAction'/>",
            "<power type='UnitAction'/>", "<power name='AbilityA'/>", "<power name='AbilityB' type='UnitAction'/>",
            "<power name='AbilityA' type='GodPower'/>", "<power name='AbilityA' type='GeneralEffect' godpower=''/>",
            "<power name='AbilityA' type='UnitAction'><radius>NaN</radius></power>",
            "<power name='AbilityA' type='UnitAction'><rangeindicator>Circle</rangeindicator></power>",
            "<power name='AbilityA' type='UnitAction'><abstractplacementtargettype>Unknown</abstractplacementtargettype></power>"
        }) yield return [true, xml];
        foreach (var xml in new[]
        {
            "<tactics>", "<unit/>", "<tactics xmlns='future'/>",
            "<tactics><action><name>HandAttack</name></action></tactics>",
            "<tactics><action><name>X</name><type>Attack</type></action><action><name>X</name><type>Attack</type></action></tactics>",
            "<tactics><tactic>Default<action>Missing</action></tactic></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type></action><tactic>Default<action priority='101'>HandAttack</action></tactic></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type></action><tactic>Default<action probability='bad'>HandAttack</action></tactic></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type></action><tactic>Default<attackprioritytype bonusfactor='101'>BaseUnit</attackprioritytype></tactic></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type></action><tactic>Default<armoroverride type='Hack' value='bad'/></tactic></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type><rate type='Unknown'>1</rate></action></tactics>",
            "<tactics><action><name>HandAttack</name><type>Attack</type><damage type='Hack'>NaN</damage></action></tactics>"
        }) yield return [false, xml];
    }

    [Theory]
    [MemberData(nameof(InvalidStandaloneXmlCases))]
    public void StandaloneXmlInvalidDraftBlocksWriteAndReachesDiscard(bool ability, string text)
        => WithStandaloneXml(ability, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path); await UnlockStandaloneXml(child); StandaloneRaw(child).Text = text;
            Assert.NotEmpty(await SaveStandaloneChecked(child, ability)); Assert.Equal(0, control.Calls);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(text, StandaloneRaw(child).Text); Assert.False(StandaloneRaw(child).IsReadOnly);
            Assert.True((bool)Field(child, "_isDirty")!);
            var closing = (Task)child.Call(ability ? "RequestCloseAbilityDefinitionEditorAsync" : "RequestCloseTacticsActionEditorAsync")!;
            for (var i = 0; !child.OwnedWindows.OfType<Prompt>().Any() && i < 300; i++) await Task.Delay(10);
            var discard = Assert.Single(child.OwnedWindows.OfType<Prompt>()); Assert.True(discard.PromptIsConfirm);
            discard.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(child.IsVisible); Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StandaloneXmlFailedOrNewerSaveKeepsBufferAndRetries(bool ability, bool newer)
        => WithStandaloneXml(ability, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path); await UnlockStandaloneXml(child);
            StandaloneRaw(child).Text = RawCandidate(ability, "17");
            control.Fail = !newer;
            if (newer) control.WhileSaving = window => StandaloneRaw(window).Text = RawCandidate(ability, "23");
            var errors = await SaveStandaloneChecked(child, ability);
            Assert.Equal(!newer, errors.Count > 0); Assert.False(StandaloneRaw(child).IsReadOnly); Assert.True((bool)Field(child, "_isDirty")!);
            if (!newer) Assert.Equal(before, File.ReadAllBytes(path));
            else { Assert.Contains(">17<", File.ReadAllText(path)); Assert.Contains(">23<", StandaloneRaw(child).Text); }
            control.Fail = false; control.WhileSaving = null;
            Assert.Empty(await SaveStandaloneChecked(child, ability)); Assert.True(StandaloneRaw(child).IsReadOnly);
            Assert.Contains(newer ? ">23<" : ">17<", File.ReadAllText(path)); Assert.False((bool)Field(child, "_isDirty")!);
        });

    [Fact]
    public void StandaloneTacticsXmlCancelledOrReloadedSaveDoesNotAcknowledgeWrite()
        => WithStandaloneXml(false, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path); await UnlockStandaloneXml(child); StandaloneRaw(child).Text = RawCandidate(false, "17");
            control.Outcome = TacticsDocumentSaveOutcome.Cancelled;
            Assert.Empty(await SaveStandaloneChecked(child, false)); Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(StandaloneRaw(child).IsReadOnly); Assert.True((bool)Field(child, "_isDirty")!);
            var external = XDocument.Parse(RawCandidate(false, "31")); external.Save(path); var externalBytes = File.ReadAllBytes(path);
            control.Outcome = TacticsDocumentSaveOutcome.Reloaded;
            Assert.Empty(await SaveStandaloneChecked(child, false)); Assert.Equal(externalBytes, File.ReadAllBytes(path));
            Assert.True(StandaloneRaw(child).IsReadOnly);
            Assert.True(XElement.Parse(StandaloneRaw(child).Text).Descendants("maxrange").Any(),
                "Reload backing: " + Field(child, "_modXmlDoc") + "\nReload preview: " + StandaloneRaw(child).Text);
            Assert.Equal(31m, decimal.Parse(XElement.Parse(StandaloneRaw(child).Text).Descendants("maxrange").Single().Value,
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.False((bool)Field(child, "_isDirty")!); Assert.Equal("Reloaded; review before saving.", child.FindControl<TextBlock>("_statusMessage")!.Text);
        });

    [Fact]
    public void StandaloneAbilityXmlExternalConflictKeepsDraftAndDisk()
        => WithStandaloneXml(true, async (_, _, child, path, control) =>
        {
            await UnlockStandaloneXml(child); StandaloneRaw(child).Text = RawCandidate(true, "17");
            var external = new XDocument(new XElement("powersmod", XElement.Parse(RawCandidate(true, "31")))); external.Save(path);
            var before = File.ReadAllBytes(path); control.CheckExternalState = true;
            Assert.Contains(await SaveStandaloneChecked(child, true), error => error.Contains("changed after"));
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.False(StandaloneRaw(child).IsReadOnly); Assert.Contains(">17<", StandaloneRaw(child).Text);
        });

    [Fact]
    public void StandaloneAbilityRawSaveUsesActualHostTransactionWithoutRestoringRemovedPlacementAttributes()
        => WithStandaloneXml(true, async (_, owner, child, path, control) =>
        {
            control.AbilitySave = async (name, power, strings, state) =>
            {
                Assert.True(child.IsSavingStandaloneRawXml);
                await (Task)owner.Call("SaveManagedAbilityPowerCoreAsync", "AbilityA", power, name,
                    strings, null!, state!, false)!;
            };
            await UnlockStandaloneXml(child);
            StandaloneRaw(child).Text = "<power name='AbilityA' type='UnitAction'><placement>Unit</placement><future keep='yes'/></power>";
            Assert.Empty(await SaveStandaloneChecked(child, true));
            var power = XDocument.Load(path).Root!.Element("power")!;
            Assert.Null(power.Element("placement")!.Attribute("FutureAttribute")); Assert.NotNull(power.Element("future"));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneAbilityXmlRenameDelegatesIdentityAndPreservesReconciledKeysForNewerText(bool newer)
        => WithStandaloneXml(true, async (fixture, _, child, path, control) =>
        {
            var stringsPath = fixture.Workspace.GetPath("TestMod/game/data/strings/English/stringmods.txt");
            var references = fixture.Workspace.WriteText("TestMod/reference.xml", "<protomods><unit name='Owner'><castpower>AbilityA</castpower></unit></protomods>");
            var calls = new List<string>();
            control.AbilitySave = (oldName, power, _, _) =>
            {
                calls.Add(oldName);
                var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                var before = new XElement(document.Root!.Element("power")!);
                var name = (string)power.Attribute("name")!;
                var table = StringTableDocument.Parse(File.ReadAllText(stringsPath)); var entries = table.ToDictionary();
                var owned = AbilityStringPolicy.CollectOwnedStringIds(before);
                var changes = AbilityStringPolicy.RenameOwnedStringIds(before, power, name, entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
                var remaining = power.Descendants().Where(field => !field.HasElements).Select(field => field.Value.Trim())
                    .Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var plan = OwnedStringLifecyclePolicy.Prepare(changes, owned, owned, entries, entries, remaining, new HashSet<string>());
                OwnedStringSavePreparation.KeepOnlyApprovedRenames(before, power, changes, plan);
                document.Root.Element("power")!.ReplaceWith(new XElement(power));
                var migration = GameIdentifierReferenceMigrationPlan.Prepare(
                    [new TechnologyReferenceFile(references, TechnologyReferenceDocumentKind.ProtoUnits, "Reference")],
                    GameIdentifierKind.Ability, oldName == name ? [] : [new KeyValuePair<string, string>(oldName, name)]);
                FileIntegrityTransaction.Execute([path, stringsPath, references], () =>
                {
                    ProtoEditorWindow.SaveAbilityXmlDocument(document, path);
                    migration.Save((updated, destination) => ProtoEditorWindow.SaveAbilityXmlDocument(updated, destination));
                    File.WriteAllText(stringsPath, table.Apply(plan.StringUpdates, plan.StringRemovals, StringTableSection.Abilities));
                });
                return Task.CompletedTask;
            };
            await UnlockStandaloneXml(child);
            const string raw = "<power name='RenamedAbility' type='UnitAction'><displaynameid>STR_ABILITY_ABILITYA</displaynameid><radius>17</radius><future keep='yes'/></power>";
            StandaloneRaw(child).Text = raw;
            if (newer) control.WhileSaving = window => StandaloneRaw(window).Text = raw.Replace(">17<", ">23<", StringComparison.Ordinal);
            var errors = await SaveStandaloneChecked(child, true);
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
            Assert.Equal("AbilityA", Assert.Single(calls)); Assert.Equal("RenamedAbility", child.CurrentAbilityDefinitionName);
            Assert.Equal("RenamedAbility", XDocument.Load(references).Descendants("castpower").Single().Value);
            Assert.Equal("STR_ABILITY_RENAMEDABILITY", XDocument.Load(path).Descendants("displaynameid").Single().Value);
            if (newer)
            {
                Assert.False(StandaloneRaw(child).IsReadOnly); Assert.Contains("STR_ABILITY_ABILITYA", StandaloneRaw(child).Text);
                control.WhileSaving = null; Assert.Empty(await SaveStandaloneChecked(child, true)); Assert.Equal("RenamedAbility", calls[1]);
                Assert.Equal("23", XDocument.Load(path).Descendants("radius").Single().Value);
            }
            Assert.True(StandaloneRaw(child).IsReadOnly); Assert.False((bool)Field(child, "_isDirty")!);
            var savedStrings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
            Assert.Equal("Display", savedStrings["STR_ABILITY_RENAMEDABILITY"]); Assert.DoesNotContain("STR_ABILITY_ABILITYA", savedStrings.Keys);
        }, abilitySource: "<power name='AbilityA' type='UnitAction'><displaynameid>STR_ABILITY_ABILITYA</displaynameid><future keep='yes'/></power>");
}
