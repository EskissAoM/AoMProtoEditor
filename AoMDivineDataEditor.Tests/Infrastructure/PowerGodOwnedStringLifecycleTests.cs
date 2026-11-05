using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerGodOwnedStringLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Info(object instance, string name) =>
        (instance is BaselineHost ? typeof(ProtoEditorWindow) : instance.GetType()).GetField(name, Private)!;
    private static T Field<T>(object instance, string name) => (T)(Info(instance, name)?.GetValue(instance) ??
        instance.GetType().GetProperty(name, Private)!.GetValue(instance))!;
    private static object? Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Private)!.Invoke(instance, args);
    private static string Table(Dictionary<string, string> values) => string.Join("\r\n",
        values.Select(value => $"ID = \"{value.Key}\" ; Str = \"{value.Value}\"")) + "\r\n";

    public static IEnumerable<object[]> LifecycleCases()
    {
        foreach (var power in new[] { true, false })
            foreach (var scenario in new[] { "rename", "delete", "shared", "draft", "custom", "custom-delete", "missing", "collision", "reserved", "late", "rollback", "discard", "chain" })
                yield return [power, scenario];
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public void RealSaveUsesOwnedCleanupAndPreservesSharedDraftsAndRollback(bool power, string scenario)
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = scenario.StartsWith("custom", StringComparison.Ordinal) ? "STR_CUSTOM" :
            power ? PowerCatalog.BuildStringId("Old", "displaynameid") : MajorGodCatalog.BuildStringId("Old", "displaynameid");
        var nextId = power ? PowerCatalog.BuildStringId("New", "displaynameid") : MajorGodCatalog.BuildStringId("New", "displaynameid");
        var xml = power ? $"<powersmod><power name='Old' type='TempUnit'><displaynameid>{oldId}</displaynameid><rolloverid>{oldId}</rolloverid></power></powersmod>" :
            $"<civsmods><civ><name>Old</name><displaynameid>{oldId}</displaynameid><rollovernameid>{oldId}</rollovernameid></civ></civsmods>";
        var ownerPath = power ? fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", xml) : fixture.GodPath;
        if (!power) File.WriteAllText(ownerPath, xml);
        var values = new Dictionary<string, string> { [oldId] = "Éclaireur  ", ["STR_KEEP"] = "Keep" };
        if (scenario == "missing") values.Remove(oldId);
        if (scenario == "collision") values[nextId] = "Existing owner";
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(values));
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", oldId);
        var before = new[] { ownerPath, stringsPath }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                object view;
                Func<Task<bool>> save, commit;
                if (power)
                {
                    host.Call("EnsurePowerView");
                    var editor = Field<PowerEditorView>(host, "_powerView");
                    editor.SetModifiedMode(true); await editor.SelectPowerAsync("Old");
                    view = editor; save = editor.SaveAsync; commit = editor.CommitCurrentPowerAsync;
                }
                else
                {
                    var editor = host.God!; editor.SelectMajorGod("Old");
                    view = editor; save = editor.SaveAsync; commit = editor.CommitCurrentMajorGodAsync;
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    Assert.False(Field<bool>(view, "_isPopulating"));
                }
                host.GodHost.Content = null;
                var document = Field<XDocument>(view, "_modDocument");
                var original = new XElement(document.Root!.Elements().Single());
                if (scenario == "draft")
                {
                    Field<XDocument>(host, "_modXmlDoc").Root!.Add(new XElement("unit", new XAttribute("name", "Draft"), new XElement("displaynameid", oldId)));
                    Field<HashSet<string>>(host, "_dirtyUnitNames").Add("Draft"); host.Set("_isDirty", true);
                }
                if (scenario == "reserved") Field<Dictionary<string, string>>(host, "_pendingUnitStringUpdates")[nextId] = "Other pending edit";
                if (scenario == "rollback")
                {
                    var writer = power ? Field<PowerEditorHostServices>(view, "_hostServices").SaveStringsAsync! :
                        Field<Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>>(view, "_saveStringsAsync");
                    Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task> failing = async (updates, removals) =>
                    { await writer(updates, removals); throw new IOException("Injected late string failure"); };
                    if (power) Info(view, "_hostServices").SetValue(view, Field<PowerEditorHostServices>(view, "_hostServices") with { SaveStringsAsync = failing });
                    else Info(view, "_saveStringsAsync").SetValue(view, failing);
                }
                if (scenario is "delete" or "custom-delete" or "late")
                {
                    if (power)
                    {
                        ((PowerEditorView)view).SelectPower(null);
                        Field<PowerEditingSession>(view, "_session").MarkDirty("Old");
                    }
                    else
                    {
                        ((MajorGodEditorView)view).SelectMajorGod(null);
                        Field<HashSet<string>>(view, "_dirtyNames").Add("Old");
                    }
                    document.Root!.Elements().Single().Remove();
                    Call(view, "SetDirty", true);
                    if (scenario == "late")
                    {
                        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, XDocument>, IReadOnlyList<ModDeletionInspection>> late = (_, _) =>
                        { fixture.Workspace.WriteText("TestMod/late.txt", oldId); return []; };
                        if (power) ((PowerEditorView)view).PrepareDeletionChecks = late;
                        else ((MajorGodEditorView)view).PrepareDeletionChecks = late;
                    }
                }
                else
                {
                    Field<TextBox>(view, "_nameBox").Text = scenario == "chain" ? "Middle" : "New";
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    var committed = await commit();
                    Assert.Equal(scenario != "collision", committed);
                    if (scenario == "collision")
                    {
                        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                        Assert.Equal("Old", power ? PowerCatalog.GetName(document.Root!.Elements().Single()) : MajorGodCatalog.GetName(document.Root!.Elements().Single()));
                        return;
                    }
                    if (scenario == "chain")
                    {
                        if (power) await ((PowerEditorView)view).SelectPowerAsync("Middle");
                        Field<TextBox>(view, "_nameBox").Text = "New";
                        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    }
                    if (scenario == "discard")
                    {
                        if (power) ((PowerEditorView)view).DiscardPowerChanges("New", original);
                        else ((MajorGodEditorView)view).DiscardMajorGodChanges("New", original);
                        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                        Assert.False(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
                        return;
                    }
                }
                Assert.Equal(scenario is not ("reserved" or "late" or "rollback"), await save());
                if (scenario is "reserved" or "late" or "rollback")
                {
                    foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                    Assert.True(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
                    Assert.Equal(values[oldId], await (Task<string>)host.Call("ResolveDisplayStringAsync", oldId)!);
                    return;
                }
                var saved = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                Assert.Equal(scenario is "shared" or "draft" or "custom" or "custom-delete", saved.ContainsKey(oldId));
                Assert.Equal("Keep", saved["STR_KEEP"]);
                var owner = XDocument.Load(ownerPath).Root!.Elements().SingleOrDefault();
                if (scenario is "delete" or "custom-delete") Assert.Null(owner);
                else
                {
                    Assert.Equal("New", power ? PowerCatalog.GetName(owner!) : MajorGodCatalog.GetName(owner!));
                    var expectedId = scenario is "custom" or "missing" ? oldId : nextId;
                    Assert.Equal(expectedId, owner!.Element("displaynameid")!.Value);
                    Assert.Equal(expectedId, owner.Element(power ? "rolloverid" : "rollovernameid")!.Value);
                    if (scenario == "missing") Assert.False(saved.ContainsKey(nextId));
                    else Assert.Equal("Éclaireur  ", saved[expectedId]);
                }
                Assert.False(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void SwitchingOwnershipRequiresFieldContextAndAllocationAvoidsReservedKeys()
    {
        var entry = XElement.Parse("<power name='Old' type='SwitchingEffects'><timerrolloverid>STR_SWITCHING_EFFECT_OLD_PROG</timerrolloverid>" +
            "<effects><displaynameid>STR_SWITCHING_EFFECT_OLD_NAME_2</displaynameid><rolloverid>STR_SWITCHING_EFFECT_OLD_NAME_2</rolloverid>" +
            "<extratooltips><string>STR_SWITCHING_EFFECT_OLD_TOOLTIP_3</string></extratooltips></effects>" +
            "<custom>STR_SWITCHING_EFFECT_OLD_FAKE</custom><future><effects><displaynameid>STR_SWITCHING_EFFECT_OLD_NAME_2</displaynameid></effects></future></power>");
        var owned = PowerCatalog.CollectOwnedStringIds(entry);
        Assert.Equal(3, owned.Count);
        Assert.DoesNotContain("STR_SWITCHING_EFFECT_OLD_FAKE", owned);
        var changes = PowerCatalog.RenameOwnedStringIds(entry, "Old", "New");
        Assert.Equal(4, changes.Count);
        Assert.Equal("STR_SWITCHING_EFFECT_NEW_NAME_2", entry.Element("effects")!.Element("rolloverid")!.Value);
        Assert.Equal("STR_SWITCHING_EFFECT_OLD_NAME_2", entry.Element("future")!.Descendants("displaynameid").Single().Value);
        var added = SwitchingEffectsFamilyController.AddEffectContainer(entry, "New", "Time", ["STR_SWITCHING_EFFECT_NEW_NAME_1"]);
        Assert.All(added.StringIds, id => Assert.EndsWith("_3", id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcknowledgeOnlyCommittedStringWritesBetweenEditors(bool externalStrings)
    {
        using var fixture = new BaselineSafetyFixture();
        var godId = MajorGodCatalog.BuildStringId("Old", "displaynameid");
        var powerId = PowerCatalog.BuildStringId("Old", "displaynameid");
        File.WriteAllText(fixture.GodPath, $"<civsmods><civ><name>Old</name><statsid>SP</statsid><key>A</key><displaynameid>{godId}</displaynameid></civ></civsmods>");
        var powerPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            $"<powersmod><power name='Old' type='TempUnit'><displaynameid>{powerId}</displaynameid></power></powersmod>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
            Table(new Dictionary<string, string> { [godId] = "God", [powerId] = "Power" }));
        var powerBefore = File.ReadAllBytes(powerPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Call("EnsurePowerView"); var power = Field<PowerEditorView>(host, "_powerView");
                power.SetModifiedMode(true); await power.SelectPowerAsync("Old");
                var god = host.God!; god.SelectMajorGod("Old");
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                host.GodHost.Content = null;
                Field<TextBox>(power, "_nameBox").Text = "New";
                Assert.True(await power.CommitCurrentPowerAsync());
                Field<TextBox>(god, externalStrings ? "_keyBox" : "_nameBox").Text = externalStrings ? "B" : "New";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(await god.CommitCurrentMajorGodAsync());
                if (externalStrings) File.AppendAllText(stringsPath, "ID = \"STR_EXTERNAL\" ; Str = \"External\"\r\n");
                Assert.Equal(!externalStrings, await host.PreflightAsync());
                Assert.Equal(!externalStrings, god.LastSaveWroteStrings);
                var saved = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                if (externalStrings)
                {
                    Assert.Equal(powerBefore, File.ReadAllBytes(powerPath));
                    Assert.Equal("External", saved["STR_EXTERNAL"]);
                    Assert.True(power.IsDirty);
                }
                else
                {
                    Assert.Equal("Power", saved[PowerCatalog.BuildStringId("New", "displaynameid")]);
                    Assert.Equal("God", saved[MajorGodCatalog.BuildStringId("New", "displaynameid")]);
                    Assert.False(saved.ContainsKey(godId)); Assert.False(saved.ContainsKey(powerId));
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(true, "rename")]
    [InlineData(true, "remove")]
    [InlineData(true, "collision")]
    [InlineData(true, "reindex")]
    [InlineData(false, "rename")]
    [InlineData(false, "remove")]
    [InlineData(false, "collision")]
    public void NestedSwitchingAndBonusGoalStringsUseCanonicalOwnership(bool power, string scenario)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Reward'/></protomods>");
        var tags = power ? new[] { "displaynameid", "rolloverid", "notificationmessageid", "string", "timerrolloverid" } :
            ["displaynameid", "rolloverid", "progresstooltipid", "nextunittooltipid", "notificationmessageid"];
        var kinds = power ? new[] { "NAME", "NAME", "NOTIF", "TOOLTIP", "PROG" } :
            ["SPAWN_REWARD", "SPAWN_REWARD", "SPAWN_REWARD_PROGRESS", "SPAWN_NEXT_UNIT", "SPAWN_REWARD_NOTIFICATION"];
        string Id(string name, int index) => power ? SwitchingEffectsFamilyController.CreateStringId(name, kinds[index], index == 3 ? 3 : index == 4 ? 0 : 1) :
            MajorGodCatalog.BuildBonusGoalStringId(name, kinds[index]);
        var oldName = power ? "Old" : "GoalOld"; var newName = power ? "New" : "GoalNew";
        var values = Enumerable.Range(0, tags.Length).Select(index => Id(oldName, index)).Distinct()
            .ToDictionary(id => id, id => "Text " + id + "  ");
        const string fake = "STR_CUSTOM_UNRECOGNIZED";
        values[fake] = "Keep";
        if (scenario == "collision") values[Id(newName, 0)] = "Existing owner";
        var fields = Enumerable.Range(0, tags.Length).Select(index => new XElement(tags[index], Id(oldName, index))).ToArray();
        var nested = power ? new XElement("effects", fields.Take(3), new XElement("extratooltips", fields[3])) :
            new XElement("bonusunitspawning", new XElement("resourcegoal", new XAttribute("name", oldName),
                new XElement("goalamount", "1"), new XElement("reward", "Reward"),
                new XElement("spawnlocation", new XAttribute("type", "FromBuilding"), new XAttribute("landbuilding", "Reward"), new XAttribute("waterbuilding", "Reward")),
                new XElement("displayonhud", "true"), new XElement("displayinfo", fields,
                    new XElement("progresstype", "Default"), new XElement("icon", "fixture_icon"), new XElement("notificationsound", ""))));
        var entry = power ? new XElement("power", new XAttribute("name", "Old"), new XAttribute("type", "SwitchingEffects"),
                new XElement("tiertype", "Time"), new XElement("interval", "1"), fields[4], nested) :
            new XElement("civ", new XElement("name", "Old"), nested);
        entry.Add(new XElement("custom", fake));
        var ownerPath = power ? fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", new XElement("powersmod", entry).ToString()) : fixture.GodPath;
        if (!power) File.WriteAllText(ownerPath, new XElement("civsmods", entry).ToString());
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(values));
        if (scenario == "reindex") fixture.Workspace.WriteText("TestMod/shared-tier.txt", Id(oldName, 2));
        var before = new[] { ownerPath, stringsPath }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                object view; Func<Task<bool>> save;
                if (power)
                {
                    host.Call("EnsurePowerView"); var editor = Field<PowerEditorView>(host, "_powerView");
                    editor.SetModifiedMode(true); await editor.SelectPowerAsync("Old"); view = editor; save = editor.SaveAsync;
                }
                else { view = host.God!; host.God!.SelectMajorGod("Old"); save = host.God.SaveAsync; }
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                host.GodHost.Content = null;
                var current = Field<XElement>(view, "_currentEntry");
                if (scenario == "reindex")
                {
                    await (Task)Call(view, "ReindexSwitchingStringIdsAsync", current)!;
                    Field<PowerEditingSession>(view, "_session").MarkDirty("Old"); Call(view, "SetDirty", true);
                }
                else if (scenario == "remove")
                {
                    if (power)
                    {
                        current.Element("effects")!.Remove(); current.Element("timerrolloverid")!.Remove();
                        Field<PowerEditingSession>(view, "_session").MarkDirty("Old"); Call(view, "SetDirty", true);
                    }
                    else { current.Element("bonusunitspawning")!.Remove(); Call(view, "MarkCurrentDirty"); }
                }
                else
                {
                    bool committed;
                    if (power)
                    {
                        Field<TextBox>(view, "_nameBox").Text = "New";
                        committed = await ((PowerEditorView)view).CommitCurrentPowerAsync();
                    }
                    else
                    {
                        var goal = current.Descendants("resourcegoal").Single(); goal.Attribute("name")!.Value = newName;
                        committed = await (Task<bool>)Call(view, "RenameBonusGoalGeneratedStringsAsync", goal, oldName, newName)!;
                    }
                    Assert.Equal(scenario != "collision", committed);
                    if (scenario == "collision")
                    {
                        foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
                        return;
                    }
                }
                Assert.True(await save(), Field<Avalonia.Controls.TextBlock>(view, "_statusMessage").Text);
                var saved = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                Assert.Equal("Keep", saved[fake]);
                if (scenario == "reindex")
                {
                    Assert.Equal(Id(oldName, 0), current.Element("effects")!.Element("displaynameid")!.Value);
                    Assert.Equal(Id(oldName, 0), current.Element("effects")!.Element("rolloverid")!.Value);
                    Assert.Equal(values[Id(oldName, 2)], saved[Id(oldName, 2)]);
                    Assert.Equal(values[Id(oldName, 2)], saved[SwitchingEffectsFamilyController.CreateStringId(oldName, "NOTIF")]);
                    Assert.Equal(values[Id(oldName, 3)], saved[SwitchingEffectsFamilyController.CreateStringId(oldName, "TOOLTIP")]);
                    Assert.False(saved.ContainsKey(Id(oldName, 3)));
                    return;
                }
                foreach (var id in Enumerable.Range(0, tags.Length).Select(index => Id(oldName, index)).Distinct()) Assert.False(saved.ContainsKey(id));
                if (scenario == "rename")
                {
                    foreach (var index in Enumerable.Range(0, tags.Length)) Assert.Equal(values[Id(oldName, index)], saved[Id(newName, index)]);
                    var result = XDocument.Load(ownerPath).Descendants(power ? "effects" : "displayinfo").Single();
                    Assert.Equal(result.Element("displaynameid")!.Value, result.Element("rolloverid")!.Value);
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
