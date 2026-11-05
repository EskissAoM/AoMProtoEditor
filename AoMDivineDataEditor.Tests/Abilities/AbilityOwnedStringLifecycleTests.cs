using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AbilityOwnedStringLifecycleTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly string _metadataPath = AbilityBindingMetadataStore.StoragePath;
    private readonly byte[]? _metadataBefore;
    public AbilityOwnedStringLifecycleTests()
    {
        _metadataBefore = File.Exists(_metadataPath) ? File.ReadAllBytes(_metadataPath) : null;
        File.WriteAllText(_metadataPath, "[]");
    }
    public void Dispose()
    {
        if (_metadataBefore == null) File.Delete(_metadataPath);
        else File.WriteAllBytes(_metadataPath, _metadataBefore);
    }
    private static T Field<T>(BaselineHost host, string name) => (T)typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host)!;
    private static Dictionary<string, string> Entries(string path) => StringTableDocument.Parse(File.ReadAllText(path)).ToDictionary();
    private static (string Power, string Strings, string Id) Setup(BaselineSafetyFixture fixture, string scenario)
    {
        var id = scenario == "custom" ? "STR_ABILITY_UNRELATED" : "STR_ABILITY_OLD" + (scenario == "suffix" ? "_2" : "");
        var rollover = scenario == "alias" ? id : id + "_LR";
        var power = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            $"<powersmod><!-- preserve --><power name='Old' type='UnitAction'><displaynameid>{id}</displaynameid><rolloverid>{rollover}</rolloverid><future keep='yes'/></power></powersmod>");
        var table = "// preserve\r\nID = \"STR_KEEP\" ; Str = \"Keep\"\r\n";
        if (scenario != "missing")
        {
            table += $"ID = \"{id}\" ; Str = \"Éclaireur  \"\r\n";
            if (rollover != id) table += $"ID = \"{rollover}\" ; Str = \"Rollover  \"\r\n";
        }
        var strings = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", table);
        return (power, strings, id);
    }
    private static void Cache(BaselineHost host, string path) => Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog")["Old"]
        = new XElement(XDocument.Load(path).Root!.Element("power")!);

    [Theory]
    [InlineData("exclusive")]
    [InlineData("suffix")]
    [InlineData("shared")]
    [InlineData("draft")]
    [InlineData("custom")]
    [InlineData("missing")]
    [InlineData("alias")]
    [InlineData("batch")]
    public void RenameUsesFinalOwnedKeysAndMigratesGeneralAndMetadata(string scenario)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, scenario);
        var general = fixture.Workspace.WriteText("TestMod/game/data/gameplay/abilities/abilities_mods.xml", "<abilitiesmods><Owner><ability>Old<future keep='yes'/></ability></Owner></abilitiesmods>");
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", files.Id);
        AbilityBindingMetadataStore.ReplaceUnit(fixture.ProtoPath, "Owner", [new() { AbilityName = "Old", MainAction = "Attack" }]);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                if (scenario == "draft")
                {
                    Field<XDocument>(host, "_modXmlDoc").Root!.Add(new XElement("unit", new XAttribute("name", "Draft"), new XElement("displaynameid", files.Id)));
                    host.Set("_isDirty", true);
                }
                if (scenario == "batch")
                {
                    var result = new AbilityManagerResult(); result.Renames.Add(new("Old", "Middle")); result.Renames.Add(new("Middle", "New"));
                    await (Task)host.Call("ApplyAbilityManagerChangesAsync", result)!;
                }
                else
                {
                    var updated = new XElement(XDocument.Load(files.Power).Root!.Element("power")!); updated.SetAttributeValue("name", "New");
                    await (Task)host.Call("SaveManagedAbilityPowerAsync", "New", updated, "Old", null!, null!, null!)!;
                }
                var saved = XDocument.Load(files.Power).Root!.Element("power")!;
                var next = saved.Element("displaynameid")!.Value;
                Assert.Equal("New", (string?)saved.Attribute("name")); Assert.Equal("yes", (string?)saved.Element("future")?.Attribute("keep"));
                Assert.Equal(scenario is "custom" or "missing" ? files.Id : "STR_ABILITY_NEW" + (scenario == "suffix" ? "_2" : ""), next);
                var table = Entries(files.Strings);
                Assert.Equal("Keep", table["STR_KEEP"]); Assert.Contains("// preserve", File.ReadAllText(files.Strings));
                Assert.Equal(scenario is "shared" or "draft" or "custom", table.ContainsKey(files.Id));
                if (scenario != "missing") Assert.Equal("Éclaireur  ", table[next]);
                if (scenario == "alias") Assert.Equal(next, saved.Element("rolloverid")!.Value);
                Assert.Equal("New", XDocument.Load(general).Descendants("ability").Single().Nodes().OfType<XText>().Single().Value.Trim());
                Assert.NotNull(AbilityBindingMetadataStore.Get(fixture.ProtoPath, "Owner", "New"));
                Assert.DoesNotContain(table.Keys, key => key.StartsWith("STR_ABILITY_MIDDLE", StringComparison.Ordinal));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("exclusive")]
    [InlineData("suffix")]
    [InlineData("shared")]
    [InlineData("draft")]
    [InlineData("custom")]
    [InlineData("base")]
    public void DeleteAutomaticallyCleansOnlyUnreferencedOwnedKeys(string scenario)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, scenario);
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", files.Id);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                if (scenario == "base") Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog")["Base"] = XElement.Parse($"<power name='Base'><displaynameid>{files.Id}</displaynameid></power>");
                if (scenario == "draft") { Field<XDocument>(host, "_modXmlDoc").Root!.Add(XElement.Parse($"<unit name='Draft'><displaynameid>{files.Id}</displaynameid></unit>")); host.Set("_isDirty", true); }
                var result = new AbilityManagerResult(); result.DeletedNames.Add("Old");
                await (Task)host.Call("ApplyAbilityManagerChangesAsync", result)!;
                Assert.Empty(XDocument.Load(files.Power).Root!.Elements("power"));
                Assert.False(Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog").ContainsKey("Old"));
                Assert.Equal(scenario is "shared" or "draft" or "custom" or "base", Entries(files.Strings).ContainsKey(files.Id));
                Assert.Equal("Keep", Entries(files.Strings)["STR_KEEP"]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("clear")]
    [InlineData("copy")]
    [InlineData("alias")]
    [InlineData("missing")]
    public void EditClearAndDuplicateUseExistingTransactionalWriters(string scenario)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, scenario);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                if (scenario is "copy" or "alias" or "missing")
                    Assert.True(await (Task<bool>)host.Call("DuplicateManagedAbilityAsync", "Old", false, "New")!);
                else
                {
                    var updated = new XElement(XDocument.Load(files.Power).Root!.Element("power")!);
                    if (scenario == "clear") updated.Elements().Where(field => AbilityStringPolicy.FieldTags.Contains(field.Name.LocalName)).Remove();
                    var entries = Entries(files.Strings); if (scenario == "edit") entries[files.Id] = "Édité  ";
                    await (Task)host.Call("SaveManagedAbilityPowerAsync", "Old", updated, "Old", entries, null!, null!)!;
                }
                var table = Entries(files.Strings);
                Assert.Equal(scenario is not "clear" and not "missing", table.ContainsKey(files.Id));
                if (scenario == "edit") Assert.Equal("Édité  ", table[files.Id]);
                if (scenario is "copy" or "alias")
                {
                    var copy = XDocument.Load(files.Power).Root!.Elements("power").Single(power => (string?)power.Attribute("name") == "New");
                    Assert.Equal("Éclaireur  ", table[copy.Element("displaynameid")!.Value]);
                    Assert.Equal("Éclaireur  ", table[files.Id]);
                    if (scenario == "alias") Assert.Equal(copy.Element("displaynameid")!.Value, copy.Element("rolloverid")!.Value);
                }
                if (scenario == "missing")
                    Assert.All(XDocument.Load(files.Power).Root!.Elements("power"), power => Assert.Equal(files.Id, power.Element("displaynameid")!.Value));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("collision")]
    [InlineData("external")]
    [InlineData("rollback")]
    public void RefusedOrFailedSavePreservesFilesDraftAndCatalog(string scenario)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "exclusive");
        if (scenario == "collision") File.AppendAllText(files.Strings, "ID = \"STR_ABILITY_NEW\" ; Str = \"Occupied\"\r\n");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                var state = (FileIntegrityTransaction.FileState)host.Call("CaptureCurrentModStringsStateForEdit")!;
                if (scenario == "external") File.AppendAllText(files.Strings, "// external\r\n");
                var before = new[] { files.Power, files.Strings }.ToDictionary(path => path, File.ReadAllBytes);
                var updated = new XElement(XDocument.Load(files.Power).Root!.Element("power")!); updated.SetAttributeValue("name", "New");
                var draftBefore = updated.ToString();
                using var locked = scenario == "rollback" ? new FileStream(files.Strings, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                Assert.NotNull(await Record.ExceptionAsync(async () => await (Task)host.Call("SaveManagedAbilityPowerAsync", "New", updated, "Old", null!, null!, state)!));
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
                Assert.Equal(draftBefore, updated.ToString());
                Assert.True(Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog").ContainsKey("Old"));
                Assert.False(Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog").ContainsKey("New"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedStringInspectionRejectsLateReferenceBeforeFirstWrite(bool newFile)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "exclusive");
        var other = fixture.Workspace.WriteText("TestMod/other.txt", "unrelated");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var prepared = ModDocumentPersistence.Prepare(files.Power, ModXmlDocumentKind.Powers);
                var deleted = prepared.Document.Root!.Element("power")!; deleted.Remove();
                var state = FileIntegrityTransaction.CaptureState(files.Strings);
                var cleanup = (OwnedStringSavePreparation.Result)host.Call("PrepareAbilityStringSave", prepared,
                    Array.Empty<(XElement, XElement)>(), new[] { deleted }, null!, null!)!;
                if (newFile) fixture.Workspace.WriteText("TestMod/late.txt", files.Id);
                else File.WriteAllText(other, files.Id);
                var before = new[] { files.Power, files.Strings }.ToDictionary(path => path, File.ReadAllBytes);
                await Assert.ThrowsAsync<IOException>(async () => await (Task)host.Call("PersistAbilityChangesAsync", prepared, cleanup, state, null!, null!)!);
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("edit")]
    [InlineData("copy")]
    [InlineData("shared")]
    [InlineData("rollback")]
    [InlineData("attribute")]
    [InlineData("removePlacement")]
    public void UnitSavePreparesGeneralPowerStringsAndMetadataTogether(string scenario)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "exclusive");
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Owner'/></protomods>");
        var general = fixture.Workspace.WriteText("TestMod/game/data/gameplay/abilities/abilities_mods.xml", "<abilitiesmods><Owner><ability>Old</ability></Owner></abilitiesmods>");
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", files.Id);
        if (scenario == "removePlacement")
        {
            var opening = XDocument.Load(files.Power);
            opening.Root!.Element("power")!.Add(new XElement("placement", new XAttribute("FutureAttribute", "42"), "Unit"));
            opening.Save(files.Power);
        }
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                var source = XElement.Load(files.Power).Element("power")!;
                var name = scenario == "copy" ? "New" : "Old";
                var draft = AddDraft(host, name, source);
                var type = draft.GetType();
                var values = (Dictionary<string, string>)type.GetProperty("PowerValues")!.GetValue(draft)!;
                var texts = (Dictionary<string, string>)type.GetProperty("PowerDisplayTexts")!.GetValue(draft)!;
                var tags = (HashSet<string>)type.GetProperty("ModifiedPowerDisplayTextTags")!.GetValue(draft)!;
                foreach (var tag in AbilityStringPolicy.FieldTags)
                {
                    values[tag] = scenario == "copy" ? "" : source.Element(tag)!.Value;
                    texts[tag] = scenario is "clear" or "shared" ? "" : "Edited  "; tags.Add(tag);
                }
                var units = Field<IDictionary>(host, "_unitAbilityDrafts");
                units["Owner"] = typeof(ProtoEditorWindow).GetMethod("CloneAbilityDrafts", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [Field<IDictionary>(host, "_abilityDrafts")])!;
                if (scenario == "attribute")
                {
                    var external = XDocument.Load(files.Power);
                    external.Root!.Element("power")!.Add(new XElement("placement", new XAttribute("FutureAttribute", "42"), "Unit"));
                    external.Save(files.Power);
                }
                Field<HashSet<string>>(host, "_dirtyUnitNames").Add("Owner"); host.Set("_isDirty", true);
                if (scenario == "copy")
                {
                    Field<XDocument>(host, "_modXmlDoc").Root!.Element("unit")!.Add(new XElement("displaynameid", "STR_UNIT_OWNER_NAME"));
                    Field<Dictionary<string, string>>(host, "_pendingUnitStringUpdates")["STR_UNIT_OWNER_NAME"] = "Owner";
                }
                var before = new[] { fixture.ProtoPath, files.Power, general, files.Strings, _metadataPath }.ToDictionary(path => path, File.ReadAllBytes);
                using var locked = scenario == "rollback" ? new FileStream(_metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                var error = await Record.ExceptionAsync(async () => await (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!);
                if (scenario == "rollback")
                {
                    Assert.True(error != null || Field<bool>(host, "_isDirty"));
                    foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
                    Assert.True((bool)draft.GetType().GetProperty("Modified")!.GetValue(draft)!);
                    return;
                }
                Assert.Null(error);
                Assert.False(Field<bool>(host, "_isDirty"));
                var saved = XDocument.Load(files.Power).Root!.Elements("power").Single(power => (string?)power.Attribute("name") == name);
                Assert.Equal("yes", (string?)saved.Element("future")?.Attribute("keep"));
                var cached = Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog")[name];
                // The saved file and cached tree may have different structural indentation.
                Assert.True(XNode.DeepEquals(XElement.Parse(saved.ToString()), XElement.Parse(cached.ToString())));
                if (scenario == "attribute") Assert.Equal("42", (string?)cached.Element("placement")?.Attribute("FutureAttribute"));
                if (scenario == "removePlacement") Assert.Null(saved.Element("placement"));
                var table = Entries(files.Strings);
                if (scenario == "copy")
                {
                    Assert.Equal("Owner", table["STR_UNIT_OWNER_NAME"]);
                    Assert.Contains(StringTableDocument.GetSectionMarker(StringTableSection.Units), File.ReadAllText(files.Strings));
                    Assert.Contains(StringTableDocument.GetSectionMarker(StringTableSection.Abilities), File.ReadAllText(files.Strings));
                }
                Assert.Equal(scenario is "copy" or "shared" or "edit" or "attribute" or "removePlacement", table.ContainsKey(files.Id));
                if (scenario is "clear" or "shared") Assert.Null(saved.Element("displaynameid"));
                else Assert.Equal("Edited  ", table[saved.Element("displaynameid")!.Value]);
                Assert.Equal(name, XDocument.Load(general).Descendants("ability").Single().Value.Trim());
                Assert.IsType<JsonArray>(JsonNode.Parse(File.ReadAllText(_metadataPath)));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneSaveCallbackClearsOrRenamesAndAdoptsSavedKeys(bool rename)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "exclusive");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        var manager = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                Cache(manager, files.Power);
                var source = XElement.Load(files.Power).Element("power")!;
                var name = rename ? "New" : "Old";
                var draft = AddDraft(host, name, source);
                var type = draft.GetType();
                foreach (var tag in AbilityStringPolicy.FieldTags)
                {
                    ((Dictionary<string, string>)type.GetProperty("PowerValues")!.GetValue(draft)!)[tag] = source.Element(tag)!.Value;
                    ((Dictionary<string, string>)type.GetProperty("PowerDisplayTexts")!.GetValue(draft)!)[tag] = rename ? (tag == "displaynameid" ? "Éclaireur  " : "Rollover  ") : "";
                    ((HashSet<string>)type.GetProperty("ModifiedPowerDisplayTextTags")!.GetValue(draft)!).Add(tag);
                }
                host.Set("_currentAbilityEditorName", name); host.Set("_abilityDefinitionEditorOriginalName", "Old");
                host.Set("_isAbilityDefinitionEditorMode", true);
                host.Set("_saveAbilityDefinitionAsync", (Func<string, XElement, Dictionary<string, string>, FileIntegrityTransaction.FileState?, Task>)
                    ((original, power, strings, state) => (Task)manager.Call("SaveManagedAbilityPowerAsync", name, power, original, strings, null!, state!)!));
                await (Task)host.Call("SaveAbilityDefinitionEditorAsync")!;
                Assert.Equal("Changes saved.", Field<Avalonia.Controls.TextBlock>(host, "_statusMessage").Text);
                Assert.False(Entries(files.Strings).ContainsKey(files.Id));
                var saved = XElement.Load(files.Power).Element("power")!;
                Assert.Equal(rename ? "STR_ABILITY_NEW" : null, saved.Element("displaynameid")?.Value);
                Assert.Equal(rename ? "STR_ABILITY_NEW" : null,
                    ((Dictionary<string, string>)Field<IDictionary>(host, "_abilityDrafts")[name]!.GetType().GetProperty("PowerValues")!
                        .GetValue(Field<IDictionary>(host, "_abilityDrafts")[name])!).GetValueOrDefault("displaynameid"));
                Assert.DoesNotContain(files.Id, Field<Dictionary<string, string>>(host, "_cachedModStringEntries").Keys);
            });
        }
        finally { AvaloniaTestHost.Run(() => { host.DisposeFixtureWindow(); manager.DisposeFixtureWindow(); }); }
    }

    [Fact]
    public void FailedDuplicateDoesNotPublishAllocatedStringKeysIntoCache()
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "exclusive");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Cache(host, files.Power);
                var before = new[] { files.Power, files.Strings }.ToDictionary(path => path, File.ReadAllBytes);
                using var locked = new FileStream(files.Strings, FileMode.Open, FileAccess.Read, FileShare.Read);
                var error = await Record.ExceptionAsync(async () => await (Task<bool>)host.Call("DuplicateManagedAbilityAsync", "Old", false, "New")!);
                Assert.NotNull(error);
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
                Assert.DoesNotContain("STR_ABILITY_NEW", Field<Dictionary<string, string>>(host, "_cachedModStringEntries").Keys);
                Assert.False(Field<Dictionary<string, XElement>>(host, "_abilityPowerCatalog").ContainsKey("New"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void StandaloneConflictingAliasEditsRefuseWithoutChangingFilesOrCache()
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture, "alias");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var draft = AddDraft(host, "Old", XElement.Load(files.Power).Element("power")!);
                var type = draft.GetType();
                foreach (var tag in AbilityStringPolicy.FieldTags)
                {
                    ((Dictionary<string, string>)type.GetProperty("PowerValues")!.GetValue(draft)!)[tag] = files.Id;
                    ((Dictionary<string, string>)type.GetProperty("PowerDisplayTexts")!.GetValue(draft)!)[tag] = tag;
                    ((HashSet<string>)type.GetProperty("ModifiedPowerDisplayTextTags")!.GetValue(draft)!).Add(tag);
                }
                host.Set("_currentAbilityEditorName", "Old"); host.Set("_abilityDefinitionEditorOriginalName", "Old");
                var called = false;
                host.Set("_saveAbilityDefinitionAsync", (Func<string, XElement, Dictionary<string, string>, FileIntegrityTransaction.FileState?, Task>)
                    ((_, _, _, _) => { called = true; return Task.CompletedTask; }));
                var before = new[] { files.Power, files.Strings }.ToDictionary(path => path, File.ReadAllBytes);
                Assert.NotNull(await Record.ExceptionAsync(async () => await (Task)host.Call("SaveAbilityDefinitionEditorAsync")!));
                Assert.False(called);
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
                Assert.Equal("Éclaireur  ", Field<Dictionary<string, string>>(host, "_cachedModStringEntries")[files.Id]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    private static object AddDraft(BaselineHost host, string name, XElement source)
    {
        var type = typeof(ProtoEditorWindow).GetNestedType("AbilityEditorDraft", BindingFlags.NonPublic)!;
        var draft = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("Name")!.SetValue(draft, name);
        type.GetProperty("Modified")!.SetValue(draft, true);
        type.GetProperty("PowerSource")!.SetValue(draft, new XElement(source));
        Field<IDictionary>(host, "_abilityDrafts")[name] = draft;
        return draft;
    }

    [Theory]
    [InlineData("STR_ABILITY_OLD", true)]
    [InlineData("STR_ABILITY_OLD_2", true)]
    [InlineData("STR_ABILITY_OLD_2_LR", true)]
    [InlineData("STR_ABILITY_OLD_02", false)]
    [InlineData("STR_ABILITY_OLD_1", false)]
    [InlineData("STR_ABILITY_OTHER", false)]
    public void OwnershipRequiresGeneratedFieldAndExactSuffix(string id, bool owned)
    {
        var tag = id.EndsWith("_LR", StringComparison.Ordinal) ? "rolloverid" : "displaynameid";
        var power = XElement.Parse($"<power name='Old'><{tag}>{id}</{tag}><future>STR_ABILITY_OLD_3</future></power>");
        Assert.Equal(owned, AbilityStringPolicy.CollectOwnedStringIds(power).Contains(id));
        Assert.DoesNotContain("STR_ABILITY_OLD_3", AbilityStringPolicy.CollectOwnedStringIds(power));
    }
}
