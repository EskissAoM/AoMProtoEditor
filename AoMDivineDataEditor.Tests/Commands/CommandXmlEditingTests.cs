using System.Reflection;
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

public sealed partial class CommandXmlEditingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(ProtoUnitCommandEditorWindow window, string name) => (T)typeof(ProtoUnitCommandEditorWindow).GetField(name, Private)!.GetValue(window)!;
    private static object? Call(ProtoUnitCommandEditorWindow window, string name) => typeof(ProtoUnitCommandEditorWindow).GetMethod(name, Private)!.Invoke(window, null);
    private static TextEditor Raw(ProtoUnitCommandEditorWindow window) => Field<TextEditor>(window, "_xmlPreview");
    private const string Free = "<protounitcommand><name>C</name><command>Do</command><future keep='yes'/></protounitcommand>";
    private const string Transform = "<protounitcommand><name>C</name><associatedtech>Tech</associatedtech><transformselected/><unitcommand/></protounitcommand>";
    private sealed class SaveControl
    {
        internal int Calls;
        internal bool Fail;
        internal bool Cancel;
        internal Action<ProtoUnitCommandEditorWindow>? WhileSaving;
        internal Task? Wait;
        internal bool ReconcileRename;
        internal ProtoUnitTransformDefinition? Transform;
        internal IReadOnlyDictionary<string, string>? Strings;
    }
    private static void WithCommand(Func<ProtoUnitCommandEditorWindow, string, SaveControl, Task> check, bool transform = false, bool readOnly = false, bool assigned = false)
    {
        using var workspace = new TestWorkspace("command-xml");
        var source = XElement.Parse(transform ? Transform : Free);
        var original = new XElement(source);
        var path = workspace.WriteText("commands.xml", new XDocument(new XElement("protounitcommandmods", new XElement(source), new XElement("future-sibling"))).ToString());
        var control = new SaveControl();
        AvaloniaTestHost.RunAsync(async () =>
        {
            ProtoUnitCommandEditorWindow? window = null;
            window = new ProtoUnitCommandEditorWindow(ProtoUnitCommandDefinition.FromElement(source), readOnly,
                new ProtoUnitCommandEditorContext { CommandNames = ["C", "Other"], TechNames = ["Tech", "Prereq", "NewTech"], ProtoUnitNames = ["Base", "Target", "CustomUnit"], PowerNames = ["CustomPower"], ActionNames = ["HandAttack"], ProtoUnitUsageCount = assigned ? 1 : 0 },
                transform ? new ProtoUnitTransformDefinition { From = "Base", To = "Target", Tech = "Prereq", Command = "C" } : null,
                async (definition, strings, linked) =>
                {
                    control.Calls++;
                    if (control.Wait != null) await control.Wait;
                    if (control.Fail) throw new IOException("simulated persistence failure");
                    if (control.Cancel) return false;
                    if (control.ReconcileRename)
                    {
                        var migrated = new XDocument(new XElement("protounitcommandmods", definition.ToElement()));
                        GameIdentifierReferencePolicy.RenameReferences(migrated, GameIdentifierKind.ProtoUnitCommand, "C", definition.Name);
                        definition.AdoptSavedElement(migrated.Root!.Element("protounitcommand")!);
                        definition.Values["displaynameid"] = "STR_PUC_RENAMED_NAME";
                    }
                    var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                    document.Root!.Element("protounitcommand")!.ReplaceWith(definition.ToElement());
                    ProtoEditorWindow.SaveAbilityXmlDocument(document, path);
                    control.Transform = linked; control.Strings = strings;
                    control.WhileSaving?.Invoke(window!);
                    return true;
                });
            try
            {
                window.Show(); window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                await check(window, path, control);
                Assert.True(XNode.DeepEquals(original, source));
                Assert.NotNull(XDocument.Load(path).Root!.Element("future-sibling"));
            }
            finally { foreach (var prompt in window.OwnedWindows.OfType<Prompt>().ToArray()) prompt.Close(); window.DiscardAndClose(); }
        });
    }
    private static async Task Unlock(ProtoUnitCommandEditorWindow window)
    {
        Field<Button>(window, "_editXmlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; Raw(window).IsReadOnly && i < 200; i++) await Task.Delay(10);
        Assert.False(Raw(window).IsReadOnly);
    }
    private static async Task<List<string>> Save(ProtoUnitCommandEditorWindow window)
    {
        var errors = new List<string>();
        var saving = (Task)Call(window, "SaveAsync")!;
        for (var i = 0; !saving.IsCompleted && i < 800; i++)
        {
            foreach (var popup in window.OwnedWindows.OfType<Prompt>().ToArray()) { errors.Add(popup.PromptText); popup.Close(); }
            await Task.Delay(10);
        }
        await saving.WaitAsync(TimeSpan.FromSeconds(2));
        return errors;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CleanUnlockSaveDoesNotWriteAndRelocks(bool transform)
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.True(Raw(window).IsReadOnly); Assert.False((bool)Call(window, "HasUnsavedChanges")!);
            await Unlock(window);
            Assert.False(Field<StackPanel>(window, "_structuredForm").IsEnabled);
            Assert.False((bool)Call(window, "HasUnsavedChanges")!);
            Assert.Empty(await Save(window));
            Assert.True(Raw(window).IsReadOnly); Assert.True(Field<StackPanel>(window, "_structuredForm").IsEnabled);
            Assert.Equal(0, control.Calls); Assert.Equal(before, File.ReadAllBytes(path));
        }, transform);

    [Fact]
    public void OriginalCommandCannotUnlockOrSave()
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.False(Field<Button>(window, "_editXmlButton").IsVisible);
            Field<Button>(window, "_editXmlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Raw(window).IsReadOnly); Assert.Empty(await Save(window));
            Assert.Equal(0, control.Calls); Assert.Equal(before, File.ReadAllBytes(path));
        }, readOnly: true);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RawSavePreservesExactKnownAndUnknownStructureAndAdoptsForm(bool transform)
        => WithCommand(async (window, path, control) =>
        {
            await Unlock(window);
            var candidate = transform ? Transform.Replace("Tech</", "NewTech</") : Free.Replace("Do</", "Changed</");
            candidate = candidate.Replace("<name>", "<!--before--><name future='keep'>").Replace("</protounitcommand>", "<icon custom='blank'/><future2><!--inside--><nested/></future2></protounitcommand>");
            Raw(window).Text = candidate;
            Assert.Empty(await Save(window));
            Assert.True(XNode.DeepEquals(XElement.Parse(candidate), XDocument.Load(path).Root!.Element("protounitcommand")));
            Assert.False((bool)Call(window, "HasUnsavedChanges")!);
            Assert.True(Raw(window).IsReadOnly);
            Assert.Equal(transform ? "NewTech" : "Changed", ProtoUnitCommandDefinition.FromElement(XElement.Parse(Raw(window).Text)).Values[transform ? "associatedtech" : "command"]);
            Assert.Empty(await Save(window));
            Assert.NotNull(XDocument.Load(path).Descendants("name").Single().Attribute("future"));
            if (transform) { Assert.Equal("Base", control.Transform!.From); Assert.Equal("C", control.Transform.Command); }
        }, transform);

    public static IEnumerable<object[]> InvalidXml()
    {
        foreach (var xml in new[] { "<broken", "<unit/>", "<protounitcommand xmlns='future'><name>C</name></protounitcommand>", "<protounitcommand><name>C</name><name>Duplicate</name></protounitcommand>", "<protounitcommand><name>Other</name></protounitcommand>", "<protounitcommand><name>C</name><command><nested/></command></protounitcommand>", "<protounitcommand><name>C</name><transform/><spawncommand/></protounitcommand>" }) yield return [xml];
        foreach (var tag in new[] { "associatedtech", "forbidtech", "trainableunitreq", "prereqcommand", "associatedpower", "sharedcommand", "costprotounit", "actionforactiveicon" }) yield return [$"<protounitcommand><name>C</name><{tag}>Missing</{tag}></protounitcommand>"];
    }
    [Theory]
    [MemberData(nameof(InvalidXml))]
    public void InvalidRawSaveKeepsTextDiskAndDiscardAvailable(string candidate)
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path); await Unlock(window); Raw(window).Text = candidate;
            Assert.NotEmpty(await Save(window)); Assert.Equal(candidate, Raw(window).Text);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            var closing = (Task)Call(window, "RequestCloseAsync")!;
            for (var i = 0; !window.OwnedWindows.OfType<Prompt>().Any() && i < 200; i++) await Task.Delay(10);
            var popup = Assert.Single(window.OwnedWindows.OfType<Prompt>());
            popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing.WaitAsync(TimeSpan.FromSeconds(2)); Assert.False(window.IsVisible);
        });

    [Fact]
    public void AssignedTransformTypeCannotBeBypassedThroughRawXml()
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path); await Unlock(window); Raw(window).Text = Free;
            Assert.Contains("Type cannot be changed", Assert.Single(await Save(window)));
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
        }, transform: true, assigned: true);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedOrCancelledCallbackDoesNotAcknowledgeRawXml(bool failure)
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path); await Unlock(window); var candidate = Free.Replace("Do</", "Changed</"); Raw(window).Text = candidate;
            control.Fail = failure; control.Cancel = !failure;
            var errors = await Save(window); Assert.Equal(failure ? 1 : 0, errors.Count);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(candidate, Raw(window).Text); Assert.False(Raw(window).IsReadOnly);
            control.Fail = control.Cancel = false; Assert.Empty(await Save(window)); Assert.True(Raw(window).IsReadOnly);
        });

    [Fact]
    public void NewerRawTextSurvivesSuccessfulSaveAndCanSaveAgain()
        => WithCommand(async (window, path, control) =>
        {
            await Unlock(window); Raw(window).Text = Free.Replace("Do</", "First</");
            var newer = Free.Replace("Do</", "Second</"); control.WhileSaving = w => Raw(w).Text = newer;
            Assert.Empty(await Save(window)); Assert.Equal(newer, Raw(window).Text); Assert.False(Raw(window).IsReadOnly);
            Assert.Equal("First", XDocument.Load(path).Descendants("command").Single().Value);
            control.WhileSaving = null; Assert.Empty(await Save(window)); Assert.True(Raw(window).IsReadOnly);
            Assert.Equal("Second", XDocument.Load(path).Descendants("command").Single().Value);
        });

    [Fact]
    public void RawStringReferenceIsNotReplacedByOldDisplayTextOnNextSave()
        => WithCommand(async (window, path, control) =>
        {
            await Unlock(window); Raw(window).Text = Free.Replace("</protounitcommand>", "<displaynameid>STR_SHARED_NEW</displaynameid></protounitcommand>");
            Assert.Empty(await Save(window)); Assert.False(control.Strings!.ContainsKey("displaynameid"));
            Assert.Empty(await Save(window)); Assert.False(control.Strings!.ContainsKey("displaynameid"));
            Assert.Equal("STR_SHARED_NEW", XDocument.Load(path).Descendants("displaynameid").Single().Value);
            Assert.Contains("STR_SHARED_NEW", Raw(window).Text);
        });

    [Fact]
    public void ClosedEditorDoesNotAdoptACompletingSaveOrLoseNewerDraftToAnotherSave()
        => WithCommand(async (window, _, control) =>
        {
            await Unlock(window); Raw(window).Text = Free.Replace("Do</", "Submitted</");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.Wait = gate.Task;
            var saving = (Task)Call(window, "SaveAsync")!;
            Assert.Equal(1, control.Calls);
            Assert.Empty(await Save(window)); Assert.Equal(1, control.Calls);
            window.DiscardAndClose(); gate.SetResult();
            await saving.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(window.IsVisible); Assert.True(Field<bool>(window, "_xmlClosed"));
            Assert.Equal("C", window.ResultName);
        });

    [Fact]
    public void RawModelKeepsSourceOrderAttributesEmptyNodesAndOnlyPatchesChangedIds()
    {
        var source = XElement.Parse("<protounitcommand future='yes'>\n<!--before--><name source='keep'> C </name><future>  text  </future><displaynameid source='keep'>OLD<!--inside--></displaynameid><icon custom='blank'/><unitcommand/>\n</protounitcommand>", LoadOptions.PreserveWhitespace);
        var candidate = ProtoUnitCommandDefinition.FromElement(source); candidate.PreserveSourceStructure = true;
        Assert.True(XNode.DeepEquals(source, candidate.ToElement()));
        candidate.Values["displaynameid"] = "NEW";
        var updated = candidate.ToElement();
        Assert.Equal(source.Elements().Select(element => element.Name), updated.Elements().Select(element => element.Name));
        Assert.Equal("keep", (string?)updated.Element("displaynameid")!.Attribute("source"));
        Assert.Single(updated.Element("displaynameid")!.Nodes().OfType<XComment>());
        Assert.Equal("NEW", updated.Element("displaynameid")!.Value);
        Assert.True(XNode.DeepEquals(source.Element("icon"), updated.Element("icon")));
        Assert.Equal(" C ", updated.Element("name")!.Value);
    }

    [Fact]
    public void NewerRawRenameReconcilesOwnedIdsAndLocalReferencesOnNextSave()
        => WithCommand(async (window, path, control) =>
        {
            await Unlock(window);
            var submitted = Free.Replace("<name>C</name>", "<name>Renamed</name>")
                .Replace("</protounitcommand>", "<displaynameid>STR_PUC_C_NAME</displaynameid><sharedcommand>C</sharedcommand></protounitcommand>");
            Raw(window).Text = submitted;
            control.ReconcileRename = true;
            control.WhileSaving = w => Raw(w).Text = submitted.Replace("Do</", "Newer</");
            Assert.Empty(await Save(window)); Assert.False(Raw(window).IsReadOnly);
            control.WhileSaving = null;
            Assert.Empty(await Save(window)); Assert.True(Raw(window).IsReadOnly);
            var saved = XDocument.Load(path).Root!.Element("protounitcommand")!;
            Assert.Equal("Renamed", saved.Element("sharedcommand")!.Value);
            Assert.Equal("STR_PUC_RENAMED_NAME", saved.Element("displaynameid")!.Value);
            Assert.Equal("Newer", saved.Element("command")!.Value);
        });

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void ActualOwnerRawRenameMigratesUnitStringsAndTransformTogether(bool transform, bool external)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Base'><command>C</command></unit><unit name='Target'/></protomods>");
        var commandPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            "<protounitcommandmods><!--sibling-->" + (transform ? Transform : Free).Replace("</protounitcommand>", "<displaynameid>STR_PUC_C_NAME</displaynameid><sharedcommand>C</sharedcommand></protounitcommand>") + "</protounitcommandmods>");
        var transformPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml", transform
            ? "<unittransformmods><transform><from>Base</from><to>Target</to><tech>Prereq</tech><command>C</command></transform></unittransformmods>" : "<unittransformmods/>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='Tech'/><tech name='Prereq'/></techtreemods>");
        var stringPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", "ID = \"STR_PUC_C_NAME\" ; Str = \"Display\"\r\n");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false); host.Show();
            ProtoUnitCommandEditorWindow? editor = null;
            try
            {
                var opening = (Task)host.Call("OpenProtoUnitCommandEditorAsync", host, "C", false)!;
                for (var i = 0; !host.OwnedWindows.OfType<ProtoUnitCommandEditorWindow>().Any() && i < 300; i++) await Task.Delay(10);
                editor = Assert.Single(host.OwnedWindows.OfType<ProtoUnitCommandEditorWindow>());
                await Unlock(editor);
                var candidate = XElement.Parse(Raw(editor).Text);
                candidate.Element("name")!.Value = "Renamed";
                candidate.AddFirst(new XElement("raw-first", new XAttribute("keep", "yes")));
                Raw(editor).Text = candidate.ToString();
                if (external)
                {
                    var outside = XDocument.Load(commandPath); outside.Descendants("protounitcommand").Single().Add(new XElement("external", "keep")); outside.Save(commandPath);
                    var before = File.ReadAllBytes(commandPath);
                    Assert.Contains("changed", Assert.Single(await Save(editor)));
                    Assert.Equal(before, File.ReadAllBytes(commandPath)); Assert.False(Raw(editor).IsReadOnly);
                    Assert.Equal(candidate.ToString(), Raw(editor).Text); Assert.Equal("C", editor.ResultName);
                    editor.DiscardAndClose(); await opening.WaitAsync(TimeSpan.FromSeconds(3));
                    return;
                }
                Assert.Empty(await Save(editor));
                Assert.Equal("Renamed", editor.ResultName);
                Assert.Equal("Renamed", XDocument.Load(fixture.ProtoPath).Descendants("command").Single().Value);
                var saved = XDocument.Load(commandPath).Descendants("protounitcommand").Single();
                Assert.Equal("raw-first", saved.Elements().First().Name.LocalName);
                Assert.Equal("Renamed", saved.Element("sharedcommand")!.Value);
                Assert.Equal("STR_PUC_RENAMED_NAME", saved.Element("displaynameid")!.Value);
                Assert.Equal("Display", StringTableDocument.Parse(File.ReadAllText(stringPath)).ToDictionary()["STR_PUC_RENAMED_NAME"]);
                Assert.DoesNotContain("STR_PUC_C_NAME", File.ReadAllText(stringPath));
                Assert.Equal("Renamed", XElement.Parse(Raw(editor).Text).Element("sharedcommand")!.Value);
                if (transform) Assert.Equal("Renamed", XDocument.Load(transformPath).Descendants("command").Single().Value);
                Assert.False((bool)Call(editor, "HasUnsavedChanges")!);
                editor.DiscardAndClose(); await opening.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally { editor?.DiscardAndClose(); host.DisposeFixtureWindow(); }
        });
    }
}
