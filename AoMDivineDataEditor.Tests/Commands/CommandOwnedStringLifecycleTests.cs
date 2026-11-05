using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CommandOwnedStringLifecycleTests
{
    private static string Table(string id) => $"// preserve\r\nID = \"{id}\" ; Str = \"Éclaireur  \"\r\nID = \"STR_KEEP\" ; Str = \"Keep\"\r\n";

    [Theory]
    [InlineData("rename", "exclusive")]
    [InlineData("rename", "shared")]
    [InlineData("rename", "draft")]
    [InlineData("rename", "custom")]
    [InlineData("rename", "missing")]
    [InlineData("rename", "numbered")]
    [InlineData("delete", "exclusive")]
    [InlineData("delete", "shared")]
    [InlineData("delete", "draft")]
    [InlineData("delete", "custom")]
    public void ManagerCallbacksCleanOnlyUnusedOwnedKeys(string operation, string reference)
    {
        using var fixture = new BaselineSafetyFixture();
        var oldId = reference == "custom" ? "STR_PUC_OTHER_NAME" : "STR_PUC_OLD_NAME" + (reference == "numbered" ? "_2" : "");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            $"<protounitcommandmods><!-- keep --><protounitcommand><name>Old</name><displaynameid>{oldId}</displaynameid><rollovertextid>{oldId}</rollovertextid><future keep='yes'/></protounitcommand></protounitcommandmods>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(reference == "missing" ? "STR_UNRELATED" : oldId));
        if (reference == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", oldId);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (reference == "draft")
                {
                    var draft = new XElement("unit", new XAttribute("name", "Draft"), new XElement("displaynameid", oldId));
                    ((XDocument)typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow).GetField("_modXmlDoc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!).Root!.Add(draft);
                    var dirty = (HashSet<string>)typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow).GetField("_dirtyUnitNames", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
                    dirty.Add("Draft"); host.Set("_isDirty", true);
                }
                var success = operation == "rename"
                    ? await (Task<bool>)host.Call("RenameProtoUnitCommandAsync", "Old", "New")!
                    : await (Task<bool>)host.Call("DeleteProtoUnitCommandCoreAsync", "Old", null!)!;
                Assert.True(success);
                var saved = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                Assert.Equal("Keep", saved["STR_KEEP"]);
                Assert.Equal(reference is "shared" or "draft" or "custom", saved.ContainsKey(oldId));
                Assert.Contains("// preserve", File.ReadAllText(stringsPath));
                Assert.Contains("<!-- keep -->", File.ReadAllText(path));
                var command = XDocument.Load(path).Root!.Element("protounitcommand");
                if (operation == "delete") Assert.Null(command);
                else
                {
                    var expected = reference is "custom" or "missing" ? oldId : "STR_PUC_NEW_NAME" + (reference == "numbered" ? "_2" : "");
                    Assert.Equal(expected, command!.Element("displaynameid")!.Value);
                    Assert.Equal(expected, command.Element("rollovertextid")!.Value);
                    Assert.Equal("yes", command.Element("future")!.Attribute("keep")!.Value);
                    if (reference is not "custom" and not "missing") Assert.Equal("Éclaireur  ", saved[expected]);
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionOrDuplicateRefusalPreservesFilesAndCache(bool duplicate)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            "<protounitcommandmods><protounitcommand><name>Old</name><displaynameid>STR_PUC_OLD_NAME</displaynameid></protounitcommand></protounitcommandmods>");
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table("STR_PUC_OLD_NAME") +
            $"ID = \"{(duplicate ? "STR_PUC_OLD_NAME" : "STR_PUC_NEW_NAME")}\" ; Str = \"Other\"\r\n");
        var before = new[] { path, fixture.ProtoPath, stringsPath }.ToDictionary(file => file, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                bool? result = null;
                var exception = await Record.ExceptionAsync(async () => result = await (Task<bool>)host.Call("RenameProtoUnitCommandAsync", "Old", "New")!);
                Assert.True(exception != null || result == false);
                foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
                Assert.Equal(duplicate ? "Other" : "Éclaireur  ", await (Task<string>)host.Call("ResolveDisplayStringAsync", "STR_PUC_OLD_NAME")!);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void OwnershipRejectsUnknownNestedQualifiedAndInvalidNumberedFields()
    {
        var command = XElement.Parse("""
            <protounitcommand xmlns:x="urn:future"><name>Old</name>
              <displaynameid>STR_PUC_OLD_NAME_2</displaynameid>
              <rollovertextid>STR_PUC_OLD_LR_02</rollovertextid>
              <shortrollovertextid>STR_PUC_OLD_SR_1</shortrollovertextid>
              <x:activerollovertextid>STR_PUC_OLD_ACTIVE_LR</x:activerollovertextid>
              <unknown>STR_PUC_OLD_DISABLED_LR</unknown>
              <future><buildlimittextid>STR_PUC_OLD_BUILD_LIMIT</buildlimittextid></future>
            </protounitcommand>
            """);
        Assert.Equal("STR_PUC_OLD_NAME_2", Assert.Single(ProtoUnitCommandStringPolicy.CollectOwnedStringIds(command)));
        Assert.Empty(ProtoUnitCommandStringPolicy.CollectOwnedStringIds(new XElement(XName.Get("protounitcommand", "urn:future"), command.Elements())));
    }
}
