using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ImmediateUnitDeletionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(BaselineHost host, string name)
        => typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host)!;

    private static BaselineHost CreateHost(BaselineSafetyFixture fixture, Action<BaselineHost>? beforeShow = null)
    {
        var host = fixture.CreateHost(false);
        host.Call("InitializeEditorTabs");
        host.Call("BuildEditorPanel", "DeletedUnit", true);
        host.Set("_isReadOnly", false);
        beforeShow?.Invoke(host);
        host.Show();
        Dispatcher.UIThread.RunJobs();
        return host;
    }

    private static async Task<Prompt> PromptAsync(BaselineHost host, string title)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var prompt = host.OwnedWindows.OfType<Prompt>().SingleOrDefault();
            if (prompt != null)
            {
                Assert.Equal(title, prompt.PromptTitle);
                return prompt;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected dialog: " + title);
    }

    private static void Confirm(Prompt prompt)
        => typeof(Prompt).GetMethod("ConfirmButton_Click", Private)!.Invoke(prompt, [null, new RoutedEventArgs()]);

    private static Task DeleteAsync(BaselineHost host)
        => (Task)host.Call("DeleteSelectedUnitAsync")!;

    private static void WriteUnit(BaselineSafetyFixture fixture)
        => File.WriteAllText(fixture.ProtoPath,
            "<protomods><!-- keep --><unit name='DeletedUnit'/><unit name='Keep'><future value='yes'/></unit></protomods>");

    private static object AddTab(BaselineHost host, string kindName, string name, bool main = false)
    {
        var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, kindName);
        var tab = host.Call("CreateDocumentTab", main, kind, true, name)!;
        ((IList)Field(host, "_openDocumentTabs")).Add(tab);
        ((IList)Field(host, "_documentTabItems")).Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
        return tab;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedTransformDeletionCommitsTogetherOrRollsBackEveryFile(bool failWrite)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='DeletedUnit'><command>DeletedUnitToKeep</command></unit><unit name='Keep'/></protomods>");
        var commands = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            "<protounitcommandmods><protounitcommand><name>DeletedUnitToKeep</name><associatedtech>DeletedUnitToKeep</associatedtech></protounitcommand><protounitcommand><name>KeepCommand</name><future>preserved</future></protounitcommand></protounitcommandmods>");
        var transforms = fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml",
            "<unittransformmods><transform><from>DeletedUnit</from><to>Keep</to><command>DeletedUnitToKeep</command></transform></unittransformmods>");
        var technologies = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='DeletedUnitToKeep'/><tech name='KeepTech'><future>preserved</future></tech></techtreemods>");
        var before = new[] { fixture.ProtoPath, commands, transforms, technologies }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var task = DeleteAsync(host);
                var prompt = await PromptAsync(host, "Delete Unit");
                Assert.Contains("DeletedUnitToKeep", prompt.PromptText);
                using var lockedFile = failWrite ? new FileStream(fixture.ProtoPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                Confirm(prompt);
                if (failWrite) (await PromptAsync(host, "Unable to delete Unit")).Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                if (failWrite)
                {
                    foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
                    Assert.Equal("DeletedUnit", Field(host, "_currentUnitName"));
                    Assert.Empty((IDictionary)Field(host, "_pendingOwnedTransformBundleDeletions"));
                }
                else
                {
                    Assert.Empty(XDocument.Load(transforms).Root!.Elements());
                    Assert.DoesNotContain("DeletedUnitToKeep", File.ReadAllText(commands));
                    Assert.DoesNotContain("DeletedUnitToKeep", File.ReadAllText(technologies));
                    Assert.Contains("preserved", File.ReadAllText(commands));
                    Assert.Contains("preserved", File.ReadAllText(technologies));
                    Assert.DoesNotContain(XDocument.Load(fixture.ProtoPath).Root!.Elements("unit"), unit => (string?)unit.Attribute("name") == "DeletedUnit");
                }
                Assert.False((bool)Field(host, "_isDirty"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencedOwnedTransformDefinitionsFailExistingCommitGuardWithoutWriting(bool technologyReference)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='DeletedUnit'><command>DeletedUnitToKeep</command></unit><unit name='Keep'>" +
            (technologyReference ? "" : "<command>DeletedUnitToKeep</command>") + "</unit></protomods>");
        var commands = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            "<protounitcommandmods><protounitcommand><name>DeletedUnitToKeep</name><associatedtech>DeletedUnitToKeep</associatedtech></protounitcommand></protounitcommandmods>");
        var transforms = fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml",
            "<unittransformmods><transform><from>DeletedUnit</from><to>Keep</to><command>DeletedUnitToKeep</command></transform></unittransformmods>");
        var technologies = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='DeletedUnitToKeep'/>" + (technologyReference
                ? "<tech name='Referrer'><prereqs><techstatus status='Active'>DeletedUnitToKeep</techstatus></prereqs></tech>" : "") + "</techtreemods>");
        var before = new[] { fixture.ProtoPath, commands, transforms, technologies }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            // Reuse DeletionCommitCheckpointTests' commit-guard fixture. The
            // displayed shared-command fixture could not be stabilized within
            // two attempts; detailed dialog behavior remains a manual check.
            AvaloniaTestHost.Run(() =>
            {
                var candidate = XDocument.Load(fixture.ProtoPath);
                candidate.Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "DeletedUnit").Remove();
                host.Set("_modXmlDoc", candidate);
                host.Set("_modXmlRoot", candidate.Root);
                ((Dictionary<string, List<OwnedTransformCommandBundle>>)Field(host, "_pendingOwnedTransformBundleDeletions"))["DeletedUnit"] =
                    [new("DeletedUnitToKeep", "DeletedUnitToKeep", "DeletedUnit", "Keep")];
                var error = Assert.Throws<TargetInvocationException>(() => host.Call("PreparePendingUnitDeletionChecks"));
                Assert.IsType<InvalidOperationException>(error.InnerException);
                Assert.Contains("DeletedUnitToKeep", error.InnerException.Message);
                foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingUnitRenameIsSavedBeforeDeletingAndTabsStayConsistent(bool delete)
    {
        using var fixture = new BaselineSafetyFixture();
        WriteUnit(fixture);
        object? main = null, pinned = null, keep = null;
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, value =>
        {
            main = AddTab(value, "Units", "DeletedUnit", true);
            pinned = AddTab(value, "Units", "DeletedUnit");
            keep = AddTab(value, "Units", "Keep");
            value.Set("_mainDocumentTab", main);
            value.Set("_activeDocumentTab", main);
        }));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Set("_suppressEditorChangeTracking", false);
                ((TextBox)((IDictionary)Field(host, "_fieldControls"))["__proto_unit_name"]!).Text = "RenamedUnit";
                host.Call("MarkDirty");
                var task = DeleteAsync(host);
                Confirm(await PromptAsync(host, "Save changes before deletion"));
                var final = await PromptAsync(host, "Delete Unit");
                Assert.Contains("RenamedUnit", final.PromptText);
                Assert.Contains("RenamedUnit", File.ReadAllText(fixture.ProtoPath));
                if (delete) Confirm(final); else final.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                var tabs = ((IList)Field(host, "_openDocumentTabs")).Cast<object>().ToList();
                Assert.Contains(keep!, tabs);
                Assert.Equal(!delete, tabs.Contains(pinned!));
                Assert.Equal(delete ? null : "RenamedUnit", main!.GetType().GetProperty("EntityName")!.GetValue(main));
                Assert.False((bool)Field(host, "_isDirty"));
                Assert.DoesNotContain("DeletedUnit", File.ReadAllText(fixture.ProtoPath));
                Assert.Equal(!delete, XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").Any(unit => (string?)unit.Attribute("name") == "RenamedUnit"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingPowerReferenceIsSavedBeforeDeletionDecision(bool addReference)
    {
        using var fixture = new BaselineSafetyFixture();
        WriteUnit(fixture);
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            "<powersmod><power name='P' type='TempUnit'>" + (addReference ? "" : "<attachproto>DeletedUnit</attachproto>") + "</power></powersmod>");
        PowerEditorView? power = null;
        object? powerTab = null;
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, value =>
        {
            power = new PowerEditorView(new PowerEditorComposition([], path));
            power.SetModifiedMode(true);
            var document = (XDocument)typeof(PowerEditorView).GetField("_modDocument", Private)!.GetValue(power)!;
            document.Root!.Element("power")!.SetElementValue("attachproto", addReference ? "DeletedUnit" : null);
            typeof(PowerEditorView).GetMethod("SetDirty", Private)!.Invoke(power, [true]);
            value.Set("_powerView", power);
            powerTab = AddTab(value, "Powers", "P");
        }));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Assert.True(power!.IsDirty);
                var task = DeleteAsync(host);
                Confirm(await PromptAsync(host, "Save changes before deletion"));
                var next = await PromptAsync(host, addReference ? "Unable to delete Unit" : "Delete Unit");
                if (addReference) next.Close(); else Confirm(next);
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(power.IsDirty);
                Assert.Equal(addReference, XDocument.Load(path).Root!.Element("power")!.Element("attachproto") != null);
                var savedPower = (XElement?)powerTab!.GetType().GetProperty("SavedElement")!.GetValue(powerTab);
                Assert.NotNull(savedPower);
                Assert.Equal(addReference, savedPower.Element("attachproto") != null);
                Assert.Equal(addReference, XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").Any(unit => (string?)unit.Attribute("name") == "DeletedUnit"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedDeletionSavesImmediatelyAndCancelKeepsSelection(bool confirm)
    {
        using var fixture = new BaselineSafetyFixture();
        WriteUnit(fixture);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var task = DeleteAsync(host);
                var prompt = await PromptAsync(host, "Delete Unit");
                Assert.Equal("DeletedUnit", Field(host, "_currentUnitName"));
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                // A second request cannot open another confirmation or start a save.
                await DeleteAsync(host);
                Assert.Single(host.OwnedWindows);
                if (confirm) Confirm(prompt); else prompt.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False((bool)Field(host, "_isDirty"));
                Assert.False((bool)Field(host, "_isUnitSaveInProgress"));
                Assert.True(((Button)Field(host, "_saveButton")).IsEnabled);
                var saved = XDocument.Load(fixture.ProtoPath);
                Assert.Equal(!confirm, saved.Root!.Elements("unit").Any(u => (string?)u.Attribute("name") == "DeletedUnit"));
                Assert.Equal("yes", (string?)saved.Root.Elements("unit").Single(u => (string?)u.Attribute("name") == "Keep").Element("future")?.Attribute("value"));
                var baseline = (XDocument)Field(host, "_loadedModXmlBaseline");
                Assert.Equal(saved.ToString(), baseline.ToString());
                if (confirm)
                {
                    Assert.Null(typeof(ProtoEditorWindow).GetField("_currentUnitName", Private)!.GetValue(host));
                    Assert.Empty((HashSet<string>)Field(host, "_dirtyUnitNames"));
                    Assert.False(((TextBox)Field(host, "_unitNameBox")).IsEnabled);
                }
                else Assert.Equal("DeletedUnit", Field(host, "_currentUnitName"));
            });
            if (!confirm) Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("save")]
    [InlineData("invalid")]
    public void PendingChangesRequireConsentAndFailedSavePreventsDeletion(string outcome)
    {
        using var fixture = new BaselineSafetyFixture();
        WriteUnit(fixture);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var godBefore = File.ReadAllBytes(fixture.GodPath);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, value =>
        {
            value.God!.SelectMajorGod("CheckpointGod");
            if (outcome == "invalid")
            {
                value.SetInvalidGodKey();
                value.DetachGodDialogOwner();
            }
            else value.ChangeGodKey();
        }));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Assert.True(host.God!.IsDirty);
                var task = DeleteAsync(host);
                var prompt = await PromptAsync(host, "Save changes before deletion");
                Assert.Equal(godBefore, File.ReadAllBytes(fixture.GodPath));
                if (outcome == "cancel") prompt.Close(); else Confirm(prompt);
                if (outcome == "save")
                {
                    var final = await PromptAsync(host, "Delete Unit");
                    Assert.Equal("B", XDocument.Load(fixture.GodPath).Root!.Element("civ")!.Element("key")!.Value);
                    final.Close();
                }
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(outcome != "save", host.God.IsDirty);
                Assert.Equal("DeletedUnit", Field(host, "_currentUnitName"));
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                Assert.Empty(host.OwnedWindows);
            });
            if (outcome != "save") Assert.Equal(godBefore, File.ReadAllBytes(fixture.GodPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void NeverSavedUnitIsDiscardedWithoutSavingOtherDrafts()
    {
        using var fixture = new BaselineSafetyFixture();
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var godBefore = File.ReadAllBytes(fixture.GodPath);
        var host = AvaloniaTestHost.Run(() =>
        {
            var value = fixture.CreateHost(false);
            ((XDocument)Field(value, "_modXmlDoc")).Root!.Add(new XElement("unit", new XAttribute("name", "DeletedUnit")));
            value.Call("InitializeEditorTabs");
            value.Call("BuildEditorPanel", "DeletedUnit", true);
            value.Set("_isReadOnly", false);
            value.Set("_isDirty", true);
            ((HashSet<string>)Field(value, "_dirtyUnitNames")).Add("DeletedUnit");
            value.God!.SelectMajorGod("CheckpointGod");
            value.ChangeGodKey();
            value.Show();
            return value;
        });
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var task = DeleteAsync(host);
                Confirm(await PromptAsync(host, "Discard new Unit"));
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(host.God!.IsDirty);
                Assert.False((bool)Field(host, "_isDirty"));
                Assert.Empty(((XDocument)Field(host, "_modXmlDoc")).Root!.Elements("unit"));
            });
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.Equal(godBefore, File.ReadAllBytes(fixture.GodPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("manifest")]
    [InlineData("write-failure")]
    [InlineData("source-change")]
    public void LateChangeOrWriteFailureRestoresUnitAndLeavesNoStagedDeletion(string failure)
    {
        using var fixture = new BaselineSafetyFixture();
        WriteUnit(fixture);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var task = DeleteAsync(host);
                var confirmation = await PromptAsync(host, "Delete Unit");
                using var lockedFile = failure == "write-failure"
                    ? new FileStream(fixture.ProtoPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                if (failure == "reference") fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
                    "<powersmod><power name='P' type='GeneralEffect'><attachproto>DeletedUnit</attachproto></power></powersmod>");
                if (failure == "manifest") fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.txt", "unrelated");
                if (failure == "source-change") File.WriteAllText(fixture.ProtoPath,
                    "<protomods><unit name='DeletedUnit'/><unit name='ExternalUnit'/></protomods>");
                var expectedBytes = File.ReadAllBytes(fixture.ProtoPath);
                Confirm(confirmation);
                var error = await PromptAsync(host, "Unable to delete Unit");
                Assert.Contains("not deleted", error.PromptText);
                error.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(expectedBytes, File.ReadAllBytes(fixture.ProtoPath));
                Assert.Equal("DeletedUnit", Field(host, "_currentUnitName"));
                Assert.False((bool)Field(host, "_isDirty"));
                Assert.Empty((HashSet<string>)Field(host, "_dirtyUnitNames"));
                Assert.Contains(((XDocument)Field(host, "_modXmlDoc")).Root!.Elements("unit"), u => (string?)u.Attribute("name") == "DeletedUnit");
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
