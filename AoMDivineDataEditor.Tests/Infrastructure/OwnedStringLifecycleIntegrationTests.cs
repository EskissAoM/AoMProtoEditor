using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class OwnedStringLifecycleIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) => (T)
        (instance is BaselineHost ? typeof(ProtoEditorWindow) : instance.GetType()).GetField(name, Private)!.GetValue(instance)!;
    private static string StringsPath(BaselineSafetyFixture fixture) => fixture.Workspace.GetPath("TestMod/game/data/strings/English/stringmods.txt");
    private static string Table(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("\r\n", values.Select(entry => $"ID = \"{entry.Key}\" ; Str = \"{entry.Value}\"")) + "\r\n";

    [Theory]
    [InlineData("shared")]
    [InlineData("custom")]
    [InlineData("alias")]
    [InlineData("linked")]
    [InlineData("collision")]
    [InlineData("rollback")]
    public void TechnologyRenameUsesOwnedIdsAndCommitsStringsWithLinkedGodOrRollsBack(string scenario)
    {
        using var fixture = new BaselineSafetyFixture();
        var linked = scenario is "linked" or "rollback";
        var oldName = linked ? "ClassicalAgeOld" : "Old";
        var newName = linked ? "ClassicalAgeNew" : "New";
        var oldId = scenario == "custom" ? "STR_CUSTOM" : TechnologyStringIdPolicy.BuildId(oldName, "displaynameid");
        var nextId = TechnologyStringIdPolicy.BuildId(newName, "displaynameid");
        var tooltip = TechnologyStringIdPolicy.BuildId(oldName, "advancedrollovertextoverrideid");
        var output = TechnologyStringIdPolicy.EffectId(oldName, "OUTPUT1");
        var technologyPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            $"<techtreemods><tech name='{oldName}'><displaynameid>{oldId}</displaynameid>" +
            (scenario == "alias" ? $"<rollovertextid>{oldId}</rollovertextid>" : "") +
            (linked ? "<flag>AgeUpgrade</flag>" : "") +
            $"<effects><effect type='TextOutput' tooltipID='{tooltip}'>{output}</effect></effects></tech></techtreemods>");
        string? minorPath = linked ? fixture.Workspace.WriteText("TestMod/game/data/gameplay/minor_gods_mods.xml",
            $"<minorgodsmods><{oldName} portraitfilename='keep.png'/></minorgodsmods>") : null;
        var values = new Dictionary<string, string> { [oldId] = "Éclaireur  ", [tooltip] = "Tooltip", [output] = "Output", ["STR_KEEP"] = "Keep" };
        if (scenario == "collision") values[nextId] = "Other owner";
        fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(values));
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", oldId);
        var before = new[] { technologyPath, StringsPath(fixture) }.Concat(minorPath == null ? [] : new[] { minorPath })
            .ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Call("EnsureTechnologyView");
                var view = Field<TechnologyEditorView>(host, "_technologyView");
                host.GodHost.Content = null;
                view.SetModifiedMode(true); view.SelectTechnology(oldName);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Field<TextBox>(view, "_techNameBox").Text = newName;
                if (scenario == "collision")
                {
                    Assert.False(await view.CommitCurrentTechnologyAsync());
                    Assert.Contains("already exists", Field<TextBlock>(view, "_statusMessage").Text);
                    foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                    return;
                }
                Assert.True(await view.CommitCurrentTechnologyAsync());
                using var locked = scenario == "rollback" ? new FileStream(minorPath!, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                Assert.Equal(scenario != "rollback", await view.SaveAsync());
                if (scenario == "rollback")
                {
                    foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                    Assert.True(view.IsDirty);
                    Assert.Equal("Éclaireur  ", await (Task<string>)host.Call("ResolveDisplayStringAsync", oldId)!);
                    return;
                }
                var saved = StringTableDocument.Parse(File.ReadAllText(StringsPath(fixture))).ToDictionary();
                var technology = XDocument.Load(technologyPath).Root!.Elements("tech").Single();
                Assert.Equal(newName, (string?)technology.Attribute("name"));
                Assert.Equal(scenario == "custom" ? oldId : nextId, technology.Element("displaynameid")!.Value);
                Assert.Equal("Éclaireur  ", saved[scenario == "custom" ? oldId : nextId]);
                Assert.Equal(scenario is "shared" or "custom", saved.ContainsKey(oldId));
                Assert.False(saved.ContainsKey(tooltip)); Assert.False(saved.ContainsKey(output));
                Assert.Equal("Tooltip", saved[technology.Descendants("effect").Single().Attribute("tooltipID")!.Value]);
                if (scenario == "alias") Assert.Equal(nextId, technology.Element("rollovertextid")!.Value);
                if (linked)
                {
                    var god = XDocument.Load(minorPath!).Root!.Elements().Single();
                    Assert.Equal(newName, god.Name.LocalName);
                    Assert.Equal("keep.png", (string?)god.Attribute("portraitfilename"));
                }
                Assert.False(view.IsDirty);
                Assert.Equal("Keep", saved["STR_KEEP"]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false, "none")]
    [InlineData(false, "disk")]
    [InlineData(false, "draft")]
    [InlineData(true, "none")]
    [InlineData(true, "disk")]
    [InlineData(true, "bundle")]
    [InlineData(false, "bundle")]
    [InlineData(true, "missing")]
    public void UnitLifecycleAutomaticallyCleansOwnedStringsAndProtectsSharedOrPendingReferences(bool rename, string references)
    {
        using var fixture = new BaselineSafetyFixture();
        const string oldId = "STR_UNIT_OLD_NAME";
        var unitXml = $"<unit name='Old'><displaynameid>{oldId}</displaynameid><editornameid>{oldId}</editornameid><unittype>Unit</unittype>" +
                      (references == "bundle" ? "<command>OldToKeep</command>" : "") + "</unit>";
        File.WriteAllText(fixture.ProtoPath, "<protomods>" + unitXml + "<unit name='Keep'/></protomods>");
        var values = new Dictionary<string, string> { [oldId] = "Éclaireur  ", ["STR_KEEP"] = "Keep" };
        if (references == "missing") values.Remove(oldId);
        string? technologyPath = null;
        string? oldTechnologyId = null;
        if (references == "bundle")
        {
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
                "<protounitcommandmods><protounitcommand><name>OldToKeep</name><associatedtech>OldToKeep</associatedtech><displaynameid>STR_PUC_OLDTOKEEP_NAME</displaynameid><rollovertextid>STR_PUC_OLDTOKEEP_NAME</rollovertextid><future>STR_PUC_CUSTOM</future></protounitcommand></protounitcommandmods>");
            values["STR_PUC_OLDTOKEEP_NAME"] = "Command text  ";
            values["STR_PUC_CUSTOM"] = "Custom";
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml",
                "<unittransformmods><transform><command>OldToKeep</command><from>Old</from><to>Keep</to></transform></unittransformmods>");
            oldTechnologyId = TechnologyStringIdPolicy.BuildId("OldToKeep", "displaynameid");
            technologyPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
                $"<techtreemods><tech name='OldToKeep'><displaynameid>{oldTechnologyId}</displaynameid></tech></techtreemods>");
            values[oldTechnologyId] = "Owned Transform";
        }
        fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(values));
        if (references == "disk") fixture.Workspace.WriteText("TestMod/custom.txt", oldId);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (references == "draft")
                {
                    var powerPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='P' type='TempUnit'/></powersmod>");
                    host.Call("EnsurePowerView");
                    var power = Field<PowerEditorView>(host, "_powerView");
                    power.SetModifiedMode(true); await power.SelectPowerAsync("P");
                    await PowerXmlTestHelpers.UnlockAsync(power);
                    Field<AvaloniaEdit.TextEditor>(power, "_rawXmlEditor").Text = $"<power name='P' type='TempUnit'><future>{oldId}</future></power>";
                    Assert.True(power.IsDirty);
                }
                if (rename)
                {
                    host.Call("InitializeEditorTabs"); host.Call("BuildEditorPanel", "Old", true);
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    host.Set("_suppressEditorChangeTracking", false);
                    ((TextBox)Field<IDictionary>(host, "_fieldControls")["__proto_unit_name"]!).Text = "New";
                    host.Call("MarkDirty");
                }
                else
                {
                    var document = Field<XDocument>(host, "_modXmlDoc");
                    if (references == "bundle")
                        Field<Dictionary<string, List<OwnedTransformCommandBundle>>>(host, "_pendingOwnedTransformBundleDeletions")["Old"] =
                            [new OwnedTransformCommandBundle("OldToKeep", "OldToKeep", "Old", "Keep")];
                    document.Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "Old").Remove();
                    Field<HashSet<string>>(host, "_dirtyUnitNames").Add("Old");
                    host.Set("_isDirty", true);
                }
                await (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!;
                var saved = StringTableDocument.Parse(File.ReadAllText(StringsPath(fixture))).ToDictionary();
                Assert.Equal(references is "disk" or "draft", saved.ContainsKey(oldId));
                Assert.Equal("Keep", saved["STR_KEEP"]);
                if (rename)
                {
                    var unit = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "New");
                    Assert.Equal(references == "missing" ? oldId : "STR_UNIT_NEW_NAME", unit.Element("displaynameid")!.Value);
                    Assert.Equal(unit.Element("displaynameid")!.Value, unit.Element("editornameid")!.Value);
                    if (references == "missing") Assert.False(saved.ContainsKey("STR_UNIT_NEW_NAME"));
                    else Assert.Equal("Éclaireur  ", saved["STR_UNIT_NEW_NAME"]);
                    if (technologyPath != null)
                    {
                        var commandPath = fixture.Workspace.GetPath("TestMod/game/data/gameplay/proto_unit_command_mods.xml");
                        var command = XDocument.Load(commandPath).Descendants("protounitcommand").Single();
                        Assert.Equal("STR_PUC_NEWTOKEEP_NAME", command.Element("displaynameid")!.Value);
                        Assert.Equal(command.Element("displaynameid")!.Value, command.Element("rollovertextid")!.Value);
                        Assert.Equal("Command text  ", saved["STR_PUC_NEWTOKEEP_NAME"]);
                        Assert.False(saved.ContainsKey("STR_PUC_OLDTOKEEP_NAME"));
                        var tech = XDocument.Load(technologyPath).Root!.Element("tech")!;
                        Assert.Equal("NewToKeep", (string?)tech.Attribute("name"));
                        Assert.False(saved.ContainsKey(oldTechnologyId!));
                        Assert.Equal("Owned Transform", saved[tech.Element("displaynameid")!.Value]);
                    }
                }
                else
                {
                    Assert.DoesNotContain(XDocument.Load(fixture.ProtoPath).Root!.Elements("unit"), unit => (string?)unit.Attribute("name") == "Old");
                    if (technologyPath != null)
                    {
                        Assert.False(saved.ContainsKey("STR_PUC_OLDTOKEEP_NAME"));
                        Assert.Empty(XDocument.Load(technologyPath).Root!.Elements("tech"));
                        Assert.False(saved.ContainsKey(oldTechnologyId!));
                    }
                }
                Assert.False(Field<bool>(host, "_isDirty"));
                if (technologyPath != null) Assert.Equal("Custom", saved["STR_PUC_CUSTOM"]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void UnitActionRenameUsesOwnedCleanupInTheUnitSaveTransaction()
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = ProtoEditorWindow.BuildProtoActionDisplayNameStringId("Old", "Attack");
        var newId = ProtoEditorWindow.BuildProtoActionDisplayNameStringId("Old", "Attack2");
        File.WriteAllText(fixture.ProtoPath,
            $"<protomods><unit name='Old'><protoaction><name>Attack</name><type>Attack</type><displaynameid>{oldId}</displaynameid></protoaction></unit></protomods>");
        fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
            Table(new Dictionary<string, string> { [oldId] = "Attack text  " }));
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var action = Field<XDocument>(host, "_modXmlDoc").Descendants("protoaction").Single();
                action.Element("name")!.Value = "Attack2";
                action.Element("displaynameid")!.Value = newId;
                Field<List<ProtoActionRenameOperation>>(host, "_pendingProtoActionRenames").Add(new("Old", "Attack", "Attack2"));
                Field<Dictionary<string, string>>(host, "_pendingUnitStringUpdates")[newId] = "Attack text  ";
                Field<HashSet<string>>(host, "_dirtyUnitNames").Add("Old");
                host.Set("_isDirty", true);
                await (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!;
                var saved = StringTableDocument.Parse(File.ReadAllText(StringsPath(fixture))).ToDictionary();
                Assert.False(saved.ContainsKey(oldId));
                Assert.Equal("Attack text  ", saved[newId]);
                Assert.Equal(newId, XDocument.Load(fixture.ProtoPath).Descendants("displaynameid").Single().Value);
                Assert.False(Field<bool>(host, "_isDirty"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("shared")]
    [InlineData("late-reference")]
    [InlineData("discard")]
    public void LinkedTechnologyDeletionCleansOnlyOwnedUnusedKeysAndGuardsLateFiles(string scenario)
    {
        using var fixture = new BaselineSafetyFixture();
        const string name = "ClassicalAgeOld";
        var id = TechnologyStringIdPolicy.BuildId(name, "displaynameid");
        var techPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            $"<techtreemods><tech name='{name}'><displaynameid>{id}</displaynameid><rollovertextid>STR_CUSTOM</rollovertextid><flag>AgeUpgrade</flag></tech></techtreemods>");
        var minorPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/minor_gods_mods.xml",
            $"<minorgodsmods><{name} portraitfilename='keep.png'/></minorgodsmods>");
        fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
            Table(new Dictionary<string, string> { [id] = "Owned", ["STR_CUSTOM"] = "Custom", ["STR_KEEP"] = "Keep" }));
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", id);
        var before = new[] { techPath, minorPath, StringsPath(fixture) }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Call("EnsureTechnologyView");
                var view = Field<TechnologyEditorView>(host, "_technologyView");
                host.GodHost.Content = null;
                view.SetModifiedMode(true);
                var original = new XElement(Field<Dictionary<string, XElement>>(view, "_modified")[name]);
                if (scenario == "discard")
                {
                    view.SelectTechnology(name);
                    Field<TextBox>(view, "_techNameBox").Text = "ClassicalAgeNew";
                    Assert.True(await view.CommitCurrentTechnologyAsync());
                    view.DiscardTechnologyChanges("ClassicalAgeNew", original);
                    Assert.False(view.IsDirty);
                    Assert.Empty(Field<Dictionary<string, string>>(view, "_pendingStringUpdates"));
                }
                else
                {
                    var modified = Field<Dictionary<string, XElement>>(view, "_modified");
                    modified[name].Remove(); modified.Remove(name);
                    Field<HashSet<string>>(view, "_dirtyTechnologyNames").Add(name);
                    Field<HashSet<string>>(view, "_pendingMinorGodRemovals").Add(name);
                    if (scenario == "late-reference")
                        view.PrepareDeletionChecks = (_, _) => { fixture.Workspace.WriteText("TestMod/late.txt", id); return []; };
                    Assert.Equal(scenario != "late-reference", await view.SaveAsync());
                }
                if (scenario is "discard" or "late-reference")
                {
                    foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                    return;
                }
                Assert.Empty(XDocument.Load(techPath).Root!.Elements());
                Assert.Empty(XDocument.Load(minorPath).Root!.Elements());
                var saved = StringTableDocument.Parse(File.ReadAllText(StringsPath(fixture))).ToDictionary();
                Assert.Equal(scenario == "shared", saved.ContainsKey(id));
                Assert.Equal("Custom", saved["STR_CUSTOM"]);
                Assert.Equal("Keep", saved["STR_KEEP"]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void TechnologyOwnershipRequiresCanonicalFieldContextAndPreservesCustomReferencesOnRename()
    {
        var before = XElement.Parse("<tech name='Old'><displaynameid>STR_TECH_OLD_NAME</displaynameid><rollovertextid>STR_CUSTOM</rollovertextid>" +
            "<effects><effect type='TextOutput' tooltipID='STR_TECH_OLD_OVERRIDE2'>STR_TECH_OLD_OUTPUT1</effect>" +
            "<effect type='Data' subtype='BuffIconOverride' pathstrid='STR_BUFFICON_OLD_OVERRIDE3'/>" +
            "<effect type='Data' tooltipID='STR_TECH_OLD_FAKE'/></effects><future>STR_TECH_OLD_NAME</future>" +
            "<custom><effect type='TextOutput'>STR_TECH_OLD_OUTPUT1</effect></custom></tech>");
        var owned = TechnologyStringIdPolicy.CollectOwnedStringIds(before);
        Assert.Equal(4, owned.Count);
        Assert.DoesNotContain("STR_CUSTOM", owned); Assert.DoesNotContain("STR_TECH_OLD_FAKE", owned);
        var after = new XElement(before);
        var changes = TechnologyStringIdPolicy.RenameOwnedStringIds(before, after, "New", owned);
        Assert.Equal(4, changes.Count);
        Assert.Equal("STR_CUSTOM", after.Element("rollovertextid")!.Value);
        Assert.Equal("STR_TECH_OLD_NAME", after.Element("future")!.Value);
        Assert.Equal("STR_TECH_OLD_OUTPUT1", after.Element("custom")!.Element("effect")!.Value);
        Assert.Contains("STR_TECH_NEW_OVERRIDE2", after.ToString());
        Assert.Contains("STR_BUFFICON_NEW_OVERRIDE3", after.ToString());
    }
}
