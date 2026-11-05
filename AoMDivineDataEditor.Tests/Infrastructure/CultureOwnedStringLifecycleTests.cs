using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CultureOwnedStringLifecycleTests
{
    private static string Table(string id) => $"// preserve\r\nID = \"{id}\" ; Str = \"Éclaireur  \"\r\nID = \"STR_KEEP\" ; Str = \"Keep\"\r\n";
    private static T HostField<T>(BaselineHost host, string name) => (T)typeof(ProtoEditorWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    [Theory]
    [InlineData("rename", "exclusive")]
    [InlineData("rename", "shared")]
    [InlineData("rename", "draft")]
    [InlineData("rename", "custom")]
    [InlineData("rename", "missing")]
    [InlineData("delete", "exclusive")]
    [InlineData("delete", "shared")]
    [InlineData("delete", "custom")]
    [InlineData("create", "exclusive")]
    [InlineData("duplicate", "exclusive")]
    [InlineData("edit", "exclusive")]
    [InlineData("detach", "exclusive")]
    public void ActualCultureCallbacksUseOwnedCleanup(string operation, string reference)
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = reference == "custom" ? "STR_CUSTOM" : CultureCatalog.BuildDisplayNameId("Old");
        var culturePath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/culture_mods.xml",
            $"<culturesmods><!-- keep --><culture><name>Old</name><displaynameid>{oldId}</displaynameid><future keep='yes'/></culture></culturesmods>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
            reference == "missing" ? Table("STR_UNRELATED") : Table(oldId));
        if (reference == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", oldId);
        if (operation == "rename") File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><culture>Old</culture></civ></civsmods>");
        var stringsBefore = File.ReadAllBytes(stringsPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (reference == "draft")
                {
                    HostField<XDocument>(host, "_modXmlDoc").Root!.Add(new XElement("unit", new XAttribute("name", "Draft"), new XElement("displaynameid", oldId)));
                    HostField<HashSet<string>>(host, "_dirtyUnitNames").Add("Draft"); host.Set("_isDirty", true);
                }
                Task<bool> action;
                if (operation is "edit" or "detach")
                {
                    var definition = new XElement(XDocument.Load(culturePath).Root!.Element("culture")!);
                    if (operation == "detach")
                    {
                        definition.Element("displaynameid")!.Value = "STR_EXTERNAL_DISPLAY";
                        File.AppendAllText(stringsPath, "ID = \"STR_EXTERNAL_DISPLAY\" ; Str = \"External display\"\r\n");
                    }
                    action = (Task<bool>)host.Call("SaveCultureDefinitionAsync", "Old", definition,
                        operation == "edit" ? "Édité  " : null!, FileIntegrityTransaction.CaptureState(culturePath), FileIntegrityTransaction.CaptureState(stringsPath))!;
                }
                else action = (Task<bool>)(operation switch
                {
                    "rename" => host.Call("RenameCultureAsync", "Old", "New"),
                    "delete" => host.Call("DeleteCultureAsync", "Old"),
                    "create" => host.Call("CreateCultureAsync", "New"),
                    _ => host.Call("DuplicateCultureAsync", "Old", false, "New")
                })!;
                Assert.True(await action);
                var saved = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                var document = XDocument.Load(culturePath);
                Assert.Equal("Keep", saved["STR_KEEP"]);
                Assert.Contains("// preserve", File.ReadAllText(stringsPath));
                if (operation is "rename" or "delete" or "detach")
                    Assert.Equal(reference is "shared" or "draft" or "custom", saved.ContainsKey(oldId));
                if (operation is "rename" or "create" or "duplicate")
                {
                    var added = CultureCatalog.FindDefinition(document.Root!, "New")!;
                    Assert.NotNull(added);
                    var nextId = CultureCatalog.ReadField(added, "displaynameid");
                    if (operation == "rename" && reference is "custom" or "missing")
                    {
                        Assert.Equal(oldId, nextId);
                        Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath));
                    }
                    else Assert.Equal(operation == "create" ? "New" : "Éclaireur  ", saved[nextId]);
                    if (operation != "create") Assert.Equal("yes", (string?)added.Element("future")?.Attribute("keep"));
                    if (operation == "rename") Assert.Equal("New", XDocument.Load(fixture.GodPath).Descendants("culture").Single().Value);
                }
                if (operation == "delete") Assert.Null(CultureCatalog.FindDefinition(document.Root!, "Old"));
                if (operation == "edit") Assert.Equal("Édité  ", saved[oldId]);
                if (operation == "detach") Assert.Equal("External display", saved["STR_EXTERNAL_DISPLAY"]);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CultureAdapterRetainsReferenceEvidenceUntilBeforeFirstWrite(bool newFile)
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = CultureCatalog.BuildDisplayNameId("Old");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/culture_mods.xml",
            $"<culturesmods><culture><name>Old</name><displaynameid>{oldId}</displaynameid></culture></culturesmods>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(oldId));
        var external = fixture.Workspace.WriteText("TestMod/custom.txt", "unrelated");
        var before = new[] { path, stringsPath }.ToDictionary(file => file, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            var cleanup = AvaloniaTestHost.Run(() =>
            {
                var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.Cultures);
                var original = new XElement(prepared.Document.Root!.Element("culture")!);
                original.Parent?.Remove(); prepared.Document.Root!.RemoveNodes();
                return (OwnedStringSavePreparation.Result)host.Call("PrepareCultureStringSave", prepared, original,
                    new Dictionary<string, string>(), new[] { oldId }, null!)!;
            });
            Assert.Contains(oldId, cleanup.Plan.StringRemovals);
            if (newFile) fixture.Workspace.WriteText("TestMod/new.txt", oldId);
            else File.WriteAllText(external, oldId);
            var wrote = false;
            Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([path, stringsPath], () =>
            {
                cleanup.Inspection.ValidateReadDependencies(); wrote = true; File.WriteAllText(path, "<culturesmods/>");
            }));
            Assert.False(wrote);
            foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionOrDuplicateStringRefusalPreservesCultureReferencesStringsAndCache(bool duplicateSource)
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = CultureCatalog.BuildDisplayNameId("Old");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/culture_mods.xml",
            $"<culturesmods><culture><name>Old</name><displaynameid>{oldId}</displaynameid></culture></culturesmods>");
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><culture>Old</culture></civ></civsmods>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(oldId) +
            $"ID = \"{(duplicateSource ? oldId : CultureCatalog.BuildDisplayNameId("New"))}\" ; Str = \"Other\"\r\n");
        var before = new[] { path, fixture.GodPath, stringsPath }.ToDictionary(file => file, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                // This host is deliberately unshown; its existing error-dialog path can throw.
                // Either returned refusal or that detached-owner exception must leave all data intact.
                bool? result = null;
                var exception = await Record.ExceptionAsync(async () => result = await (Task<bool>)host.Call("RenameCultureAsync", "Old", "New")!);
                Assert.True(exception != null || result == false);
                foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
                Assert.Equal(duplicateSource ? "Other" : "Éclaireur  ", await (Task<string>)host.Call("ResolveDisplayStringAsync", oldId)!);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
