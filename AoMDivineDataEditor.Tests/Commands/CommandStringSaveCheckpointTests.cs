using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CommandStringSaveCheckpointTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string OldId = "STR_PUC_OLDTOKEEP_NAME";
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static string Table(Dictionary<string, string> entries) => "// keep\r\n" + string.Join("", entries.Select(pair => $"ID = \"{pair.Key}\" ; Str = \"{pair.Value}\"\r\n"));
    private static (string Command, string Transform, string Technology, string Strings) Setup(BaselineSafetyFixture fixture, string id = OldId)
    {
        var command = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            $"<protounitcommandmods><!-- keep --><protounitcommand><name>OldToKeep</name><associatedtech>OldToKeep</associatedtech><displaynameid>{id}</displaynameid><rollovertextid>{id}</rollovertextid><transform/><future keep='yes'/></protounitcommand></protounitcommandmods>");
        var transform = fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml",
            "<unittransformmods><transform><command>OldToKeep</command><from>Old</from><to>Keep</to></transform></unittransformmods>");
        var technology = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='OldToKeep'><displaynameid>STR_TECH_OLD_TO_KEEP_NAME</displaynameid><future keep='yes'/></tech></techtreemods>");
        var strings = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", Table(new()
        { [id] = "Éclaireur  ", ["STR_TECH_OLD_TO_KEEP_NAME"] = "Linked text", ["STR_KEEP"] = "Keep" }));
        return (command, transform, technology, strings);
    }

    private static TransformCommandAssignmentState State((string Command, string Transform, string Technology, string Strings) files)
        => new()
        {
            CommandName = "OldToKeep", SourceCommandName = "OldToKeep",
            Definition = ProtoUnitCommandDefinition.FromElement(XDocument.Load(files.Command).Descendants("protounitcommand").Single()),
            TransformDefinition = ProtoUnitTransformDefinition.FromElement(XDocument.Load(files.Transform).Descendants("transform").Single()),
            AssociatedTechnology = TransformAssociatedTechnologyDefinition.FromElement(XDocument.Load(files.Technology).Descendants("tech").Single()),
            LoadedAssociatedTechnologySnapshot = XDocument.Load(files.Technology).Descendants("tech").Single(),
            StringTexts = new(StringComparer.OrdinalIgnoreCase) { ["displaynameid"] = "Éclaireur  ", ["rollovertextid"] = "Éclaireur  " }
        };

    [Theory]
    [InlineData("edit")]
    [InlineData("clear")]
    [InlineData("rename")]
    [InlineData("shared")]
    [InlineData("copy")]
    [InlineData("custom")]
    [InlineData("missing")]
    [InlineData("rollback")]
    [InlineData("collision")]
    [InlineData("external")]
    public void InlineCallbackUsesOneOwnedStringTransaction(string scenario)
    {
        using var fixture = new BaselineSafetyFixture();
        var files = Setup(fixture, scenario == "custom" ? "STR_PUC_UNRELATED_NAME" : OldId);
        if (scenario == "missing") File.WriteAllText(files.Strings, Table(new() { ["STR_KEEP"] = "Keep" }));
        if (scenario == "shared") fixture.Workspace.WriteText("TestMod/shared.txt", OldId);
        if (scenario == "collision") File.AppendAllText(files.Strings, "ID = \"STR_PUC_OLDTONEW_NAME\" ; Str = \"Other\"\r\n");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var state = State(files);
                host.Call("CaptureInlineTransformSourceSnapshots", state);
                if (scenario == "external") File.AppendAllText(files.Strings, "// external\r\n");
                if (scenario is "rename" or "shared" or "collision" or "custom" or "missing")
                {
                    state.CommandName = "OldToNew"; state.TransformDefinition!.To = "New";
                    state.RenameOwnedBundleOnSave = true;
                }
                if (scenario == "copy")
                {
                    state.CommandName = "OldToNew"; state.Definition!.Values["associatedtech"] = "OldToNew";
                    state.TransformDefinition!.To = "New";
                }
                if (scenario == "missing") state.StringTexts = new() { ["displaynameid"] = OldId, ["rollovertextid"] = OldId };
                if (scenario == "clear") state.StringTexts = new() { ["displaynameid"] = "", ["rollovertextid"] = "" };
                if (scenario == "edit") state.StringTexts = new() { ["displaynameid"] = "Édité  ", ["rollovertextid"] = "Édité  " };
                var before = new[] { files.Command, files.Transform, files.Technology, files.Strings }.ToDictionary(path => path, File.ReadAllBytes);
                using var locked = scenario == "rollback" ? new FileStream(files.Technology, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                bool? result = null;
                var error = await Record.ExceptionAsync(async () => result = await (Task<bool>)host.Call("SaveInlineTransformCommandDefinitionAsync", state)!);
                if (scenario is "rollback" or "collision" or "external")
                {
                    Assert.True(error != null || result == false);
                    foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
                    Assert.Equal("OldToKeep", state.SourceCommandName);
                    Assert.Equal("OldToKeep", state.AssociatedTechnology!.Name);
                    return;
                }
                Assert.Null(error); Assert.True(result);
                var saved = StringTableDocument.Parse(File.ReadAllText(files.Strings)).ToDictionary();
                var command = XDocument.Load(files.Command).Descendants("protounitcommand").Last();
                Assert.Equal("yes", command.Element("future")!.Attribute("keep")!.Value);
                Assert.Equal("Keep", saved["STR_KEEP"]);
                if (scenario == "clear") { Assert.Null(command.Element("displaynameid")); Assert.False(saved.ContainsKey(OldId)); }
                else if (scenario == "missing") { Assert.Equal(OldId, command.Element("displaynameid")!.Value); Assert.False(saved.ContainsKey("STR_PUC_OLDTONEW_NAME")); }
                else if (scenario == "custom") { Assert.Equal("STR_PUC_UNRELATED_NAME", command.Element("displaynameid")!.Value); Assert.Equal("Éclaireur  ", saved["STR_PUC_UNRELATED_NAME"]); }
                else
                {
                    var id = scenario is "rename" or "shared" or "copy" ? "STR_PUC_OLDTONEW_NAME" : OldId;
                    Assert.Equal(id, command.Element("displaynameid")!.Value);
                    Assert.Equal(id, command.Element("rollovertextid")!.Value);
                    Assert.Equal(scenario == "edit" ? "Édité  " : "Éclaireur  ", saved[id]);
                    if (scenario is "rename" or "shared" or "copy") Assert.Equal(scenario is "shared" or "copy", saved.ContainsKey(OldId));
                }
                if (scenario is "rename" or "shared" or "copy")
                {
                    Assert.Equal("Linked text", saved["STR_TECH_OLD_TO_NEW_NAME"]);
                    Assert.Equal(scenario == "copy", saved.ContainsKey("STR_TECH_OLD_TO_KEEP_NAME"));
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagerDeletionIncludesLinkedTechnologyStrings(bool shared)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture);
        if (shared) fixture.Workspace.WriteText("TestMod/shared.txt", OldId + " STR_TECH_OLD_TO_KEEP_NAME");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Assert.True(await (Task<bool>)host.Call("DeleteProtoUnitCommandCoreAsync", "OldToKeep", null!)!);
                Assert.Empty(XDocument.Load(files.Command).Descendants("protounitcommand"));
                Assert.Empty(XDocument.Load(files.Transform).Descendants("transform"));
                Assert.Empty(XDocument.Load(files.Technology).Descendants("tech"));
                var saved = StringTableDocument.Parse(File.ReadAllText(files.Strings)).ToDictionary();
                Assert.Equal(shared, saved.ContainsKey(OldId)); Assert.Equal(shared, saved.ContainsKey("STR_TECH_OLD_TO_KEEP_NAME"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitionWindowActualCallbackCleansClearedOrRenamedStrings(bool rename)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Show();
                var opening = (Task<string?>)host.Call("OpenProtoUnitCommandEditorAsync", host, "OldToKeep", false)!;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(static () => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                var editor = Assert.Single(host.OwnedWindows.OfType<ProtoUnitCommandEditorWindow>());
                try
                {
                var save = Field<Func<ProtoUnitCommandDefinition, IReadOnlyDictionary<string, string>, ProtoUnitTransformDefinition?, Task<bool>>>(editor, "_save");
                var updated = State(files).Definition!;
                if (rename) updated.Name = "Renamed";
                var texts = new Dictionary<string, string> { ["displaynameid"] = rename ? "Éclaireur  " : "", ["rollovertextid"] = rename ? "Éclaireur  " : "" };
                var saving = save(updated, texts, State(files).TransformDefinition);
                if (await Task.WhenAny(saving, Task.Delay(2000)) != saving)
                {
                    var prompt = Assert.Single(editor.OwnedWindows.OfType<Prompt>());
                    var message = prompt.PromptText; prompt.Close();
                    Assert.True(await saving, message);
                }
                else Assert.True(await saving);
                Assert.False(StringTableDocument.Parse(File.ReadAllText(files.Strings)).ToDictionary().ContainsKey(OldId));
                if (rename) Assert.Equal("STR_PUC_RENAMED_NAME", XDocument.Load(files.Command).Descendants("displaynameid").Single().Value);
                }
                finally
                {
                    foreach (var prompt in editor.OwnedWindows.OfType<Prompt>().ToArray()) prompt.Close();
                    typeof(ProtoUnitCommandEditorWindow).GetField("_allowClose", Private)!.SetValue(editor, true);
                    editor.Close();
                }
                await opening;
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiesHaveIndependentOwnedStrings(bool unitBundle)
    {
        using var fixture = new BaselineSafetyFixture(); var files = Setup(fixture);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (unitBundle)
                {
                    var source = XElement.Parse("<unit name='Old'><command>OldToKeep</command></unit>");
                    var copy = new XElement(source); copy.SetAttributeValue("name", "Copy");
                    Assert.True(await (Task<bool>)host.Call("DuplicateOwnedTransformBundlesForUnitAsync", source, copy, "Copy")!);
                }
                else Assert.True(await (Task<bool>)host.Call("DuplicateProtoUnitCommandAsync", "OldToKeep", false, "CopyToKeep")!);
                var saved = StringTableDocument.Parse(File.ReadAllText(files.Strings)).ToDictionary();
                var copyCommand = XDocument.Load(files.Command).Descendants("protounitcommand").Last();
                Assert.Equal("STR_PUC_COPYTOKEEP_NAME", copyCommand.Element("displaynameid")!.Value);
                Assert.Equal(copyCommand.Element("displaynameid")!.Value, copyCommand.Element("rollovertextid")!.Value);
                Assert.Equal("Éclaireur  ", saved["STR_PUC_COPYTOKEEP_NAME"]); Assert.True(saved.ContainsKey(OldId));
                if (unitBundle) { Assert.Equal("Linked text", saved["STR_TECH_COPY_TO_KEEP_NAME"]); Assert.True(saved.ContainsKey("STR_TECH_OLD_TO_KEEP_NAME")); }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
