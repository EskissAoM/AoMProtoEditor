using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceSaveEntryGuardTests
{
    [Theory]
    [InlineData("Units", false)]
    [InlineData("Units", true)]
    [InlineData("Technologies", false)]
    [InlineData("Technologies", true)]
    [InlineData("Powers", false)]
    [InlineData("Powers", true)]
    public void GuardProtectsTouchedDirtyDocumentsOnly(string kind, bool touched)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = kind == "Units" ? fixture.ProtoPath : fixture.Workspace.WriteText(
            "TestMod/game/data/gameplay/" + (kind == "Powers" ? "powers_mods.xml" : "techtree_mods.xml"),
            kind == "Powers" ? "<powersmod><power name='P' type='TempUnit'><cost>1</cost></power></powersmod>" :
            "<techtreemods><tech name='T'><researchpoints>1</researchpoints></tech></techtreemods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        var before = File.ReadAllBytes(path);
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (kind == "Units") host.Set("_isDirty", true);
                else if (kind == "Powers")
                {
                    var view = new PowerEditorView(new PowerEditorComposition([], path));
                    host.Set("_powerView", view);
                    view.SetModifiedMode(true);
                    await view.SelectPowerAsync("P");
                    ((TextBox)typeof(PowerEditorView).GetField("_costBox", flags)!.GetValue(view)!).Text = "2";
                    // Avalonia queues TextChanged; committing in the same dispatcher turn
                    // otherwise observes the old XML rather than the simulated edit.
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        static () => { }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                    var draft = (XElement)typeof(PowerEditorView).GetProperty("_currentEntry", flags)!.GetValue(view)!;
                    Assert.Equal("2", draft.Element("cost")!.Value);
                    Assert.True(await view.CommitCurrentPowerAsync());
                    Assert.True(view.IsDirty);
                }
                else
                {
                    host.Call("EnsureTechnologyView");
                    var view = (TechnologyEditorView)typeof(ProtoEditorWindow).GetField("_technologyView", flags)!.GetValue(host)!;
                    view.SetModifiedMode(true);
                    view.SelectTechnology("T");
                    ((TextBox)typeof(TechnologyEditorView).GetField("_techNameBox", flags)!.GetValue(view)!).Text = "Draft";
                    Assert.True(await view.CommitCurrentTechnologyAsync());
                    Assert.True(view.IsDirty);
                }
                var source = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
                var error = Record.Exception(() => host.Call("ValidateReferenceMigrationDrafts",
                    new List<string> { touched ? path : fixture.GodPath }, source, null!));
                if (touched) Assert.IsType<IOException>(Assert.IsType<TargetInvocationException>(error).InnerException);
                else Assert.Null(error);
                Assert.Equal(before, File.ReadAllBytes(path));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TechnologyRenameRefusesDirtyGodThenSucceedsAfterDiscard(bool hidden)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        var before = File.ReadAllBytes(fixture.GodPath);
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.AddGodTab(true, !hidden);
                host.ChangeGodKey();
                // TextChanged is queued: simulate the completed user edit before
                // asking the host to migrate another editor's references.
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(host.God!.IsDirty);
                Assert.Equal("B", host.God.CreateModifiedDocumentSnapshot()!.Descendants("key").Single().Value);
                var error = Assert.Throws<TargetInvocationException>(() => host.Call("PrepareExternalTechnologyReferenceMigration",
                    (object)new List<TechnologyRenameOperation> { new("FixtureAge", "NewAge") }));
                Assert.IsType<IOException>(error.InnerException);
                Assert.True(host.God!.IsDirty);
                Assert.Equal(before, File.ReadAllBytes(fixture.GodPath));
                Assert.Equal("B", ((TextBox)typeof(MajorGodEditorView).GetField("_keyBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host.God)!).Text);
                host.God.DiscardMajorGodChanges("CheckpointGod", host.God.LoadSavedMajorGodElement("CheckpointGod"));
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.False(host.God.IsDirty);
                Assert.Equal(before, File.ReadAllBytes(fixture.GodPath));
                var plan = (TechnologyReferenceMigrationPlan)host.Call("PrepareExternalTechnologyReferenceMigration",
                    (object)new List<TechnologyRenameOperation> { new("FixtureAge", "NewAge") })!;
                Assert.Single(plan.Paths);
                FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                    () => plan.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path)));
                host.Call("AdoptExternalTechnologyReferenceMigration", plan);
                Assert.Equal("NewAge", host.God!.CreateModifiedDocumentSnapshot()!.Descendants("agetech").Single().Element("tech")!.Value);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void UnrelatedDirtyGodDoesNotBlockTechnologyMigration()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            "<powersmod><power name='P' type='Rain'><effect type='TechActivate'><target type='Tech'>Old</target></effect></power></powersmod>");
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.ChangeGodKey();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(host.God!.IsDirty);
                Assert.Equal("B", host.God.CreateModifiedDocumentSnapshot()!.Descendants("key").Single().Value);
                var godBefore = File.ReadAllBytes(fixture.GodPath);
                var powerBefore = File.ReadAllBytes(path);
                var plan = (TechnologyReferenceMigrationPlan)host.Call("PrepareExternalTechnologyReferenceMigration",
                    (object)new List<TechnologyRenameOperation> { new("Old", "New") })!;
                Assert.Equal(path, Assert.Single(plan.Paths));
                Assert.True(host.God!.IsDirty);
                Assert.Equal(godBefore, File.ReadAllBytes(fixture.GodPath));
                Assert.Equal(powerBefore, File.ReadAllBytes(path));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void EveryDirectEditorCallbackValidatesBeforeReturningPlan()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        foreach (var pair in new[] { ("PrepareExternalTechnologyReferenceMigration", "AdoptExternalTechnologyReferenceMigration"),
                     ("PrepareMajorGodReferenceMigration", "AdoptMajorGodReferenceMigration"),
                     ("PreparePowerReferenceMigration", "AdoptPowerReferenceMigration") })
        {
            var begin = source.IndexOf(" " + pair.Item1 + "(", source.IndexOf("private TechnologyReferenceMigrationPlan PrepareExternal", StringComparison.Ordinal), StringComparison.Ordinal);
            var end = source.IndexOf("private void " + pair.Item2, begin, StringComparison.Ordinal);
            var method = source[begin..end];
            Assert.Contains("ValidateReferenceMigrationDrafts(plan.Paths", method);
            Assert.True(method.IndexOf("ValidateReferenceMigrationDrafts", StringComparison.Ordinal) < method.LastIndexOf("return plan;", StringComparison.Ordinal));
        }
    }
}
