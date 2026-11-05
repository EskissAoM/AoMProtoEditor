using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class StagedEntityDeletionTests
{
    [Theory]
    [InlineData("Technology")]
    [InlineData("Power")]
    [InlineData("MajorGod")]
    public void HostChecksLateReferencesAndRetainsSnapshot(string kind)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.xml", "<root/>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                object? identifierKind = kind == "Technology" ? null : kind == "Power" ? GameIdentifierKind.Ability : GameIdentifierKind.MajorGod;
                var candidates = new Dictionary<string, XDocument>();
                var checks = (IReadOnlyList<ModDeletionInspection>)host.Call("PrepareStagedDeletionChecks",
                    new[] { "Deleted" }, candidates, identifierKind!)!;
                Assert.False(Assert.Single(checks).IsBlocked);
                File.WriteAllText(path, kind == "Technology"
                    ? "<root><futuretech>Deleted</futuretech></root>"
                    : "<root><future>Deleted</future></root>");
                Assert.Throws<IOException>(checks[0].ValidateReadDependencies);
                var exception = Assert.Throws<TargetInvocationException>(() => host.Call("PrepareStagedDeletionChecks",
                    new[] { "Deleted" }, candidates, identifierKind!));
                Assert.Contains("Deleted", exception.InnerException!.Message);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualMajorGodSaveChecksDeletionBeforeWriting(bool referenced)
    {
        using var fixture = new BaselineSafetyFixture();
        var before = File.ReadAllBytes(fixture.GodPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var doc = (XDocument)typeof(MajorGodEditorView).GetField("_modDocument", flags)!.GetValue(host.God)!;
                doc.Root!.Elements().Remove();
                typeof(MajorGodEditorView).GetMethod("SetDirty", flags)!.Invoke(host.God, [true]);
                host.DetachGodDialogOwner();
            });
            if (referenced) fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.xml", "<root><future>CheckpointGod</future></root>");
            Assert.Equal(!referenced, AvaloniaTestHost.RunAsync(host.God!.SaveAsync));
            if (referenced) Assert.Equal(before, File.ReadAllBytes(fixture.GodPath));
            else Assert.Empty(XDocument.Load(fixture.GodPath).Root!.Elements());
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void SaveEntryPointsCapturePendingReferencesBeforeAnyEntitySave()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        foreach (var signature in new[] { "private async void Save_Click", "private async Task<bool> SavePendingChangesBeforeImmediateManagerAsync" })
        {
            var method = ProjectSource.ExtractMethod(source, signature);
            var capture = method.IndexOf("await CommitPendingEntityDraftsAsync()", StringComparison.Ordinal);
            Assert.True(capture >= 0);
            Assert.True(capture < method.IndexOf("await _majorGodView.SaveAsync()", StringComparison.Ordinal));
        }
        var helper = ProjectSource.ExtractMethod(source, "private async Task<bool> CommitPendingEntityDraftsAsync");
        Assert.Contains("CommitCurrentTechnologyAsync()", helper);
        Assert.Contains("CommitCurrentMajorGodAsync()", helper);
        Assert.Contains("CommitCurrentPowerAsync()", helper);
        // Match an actual method identifier, not PrepareTechnologyXmlForSaveAsync.
        Assert.DoesNotMatch(@"\bSaveAsync\s*\(", helper);
    }

    [Fact]
    public void UnsavedReferenceAdditionBlocksDeletion()
        => CheckDraftReferenceSafety(diskHasReference: false);

    [Fact]
    public void UnsavedReferenceRemovalDoesNotHideDiskReference()
        => CheckDraftReferenceSafety(diskHasReference: true);

    private static void CheckDraftReferenceSafety(bool diskHasReference)
    {
        using var fixture = new BaselineSafetyFixture();
        const string reference = "<protomods><unit name='U'><ability>Deleted</ability></unit></protomods>";
        File.WriteAllText(fixture.ProtoPath, diskHasReference ? reference : "<protomods/>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var draft = XDocument.Parse(diskHasReference ? "<protomods/>" : reference);
                host.Set("_modXmlDoc", draft);
                host.Set("_modXmlRoot", draft.Root!);
                host.Set("_isDirty", true);
                var exception = Assert.Throws<TargetInvocationException>(() => host.Call("PrepareStagedDeletionChecks",
                    new[] { "Deleted" }, new Dictionary<string, XDocument>(), GameIdentifierKind.Ability));
                Assert.Contains("Deleted", exception.InnerException!.Message);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void ActualOrphanMinorGodRemovalSucceedsWithoutTechnology()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/minor_gods_mods.xml",
            "<minorgodsmods><HeroicAgeUnused/></minorgodsmods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            Assert.True(AvaloniaTestHost.RunAsync(() => (Task<bool>)host.Call("DeleteMinorGodAsync", "HeroicAgeUnused")!));
            Assert.Empty(XDocument.Load(path).Root!.Elements());
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("Power", false)]
    [InlineData("Power", true)]
    [InlineData("Technology", false)]
    [InlineData("Technology", true)]
    public void ActualPowerAndTechnologySaveChecksDeletionBeforeWriting(string kind, bool referenced)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/" +
            (kind == "Power" ? "powers_mods.xml" : "techtree_mods.xml"), kind == "Power"
            ? "<powersmod><power name='Deleted' type='TempUnit'/></powersmod>"
            : "<techtreemods><tech name='Deleted'><researchpoints>1</researchpoints></tech></techtreemods>");
        var before = File.ReadAllBytes(path);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                Func<Task<bool>> save;
                if (kind == "Power")
                {
                    var view = new PowerEditorView(new PowerEditorComposition([], path));
                    view.SetModifiedMode(true);
                    view.PrepareDeletionChecks = (names, candidates) => (IReadOnlyList<ModDeletionInspection>)host.Call(
                        "PrepareStagedDeletionChecks", names, candidates, GameIdentifierKind.Ability)!;
                    ((XDocument)typeof(PowerEditorView).GetField("_modDocument", flags)!.GetValue(view)!).Root!.Elements().Remove();
                    typeof(PowerEditorView).GetMethod("SetDirty", flags)!.Invoke(view, [true]);
                    save = view.SaveAsync;
                }
                else
                {
                    host.Call("EnsureTechnologyView");
                    var view = (TechnologyEditorView)typeof(ProtoEditorWindow).GetField("_technologyView", flags)!.GetValue(host)!;
                    view.SetModifiedMode(true);
                    host.GodHost.Content = null;
                    ((XDocument)typeof(TechnologyEditorView).GetField("_modDocument", flags)!.GetValue(view)!).Root!.Elements().Remove();
                    ((HashSet<string>)typeof(TechnologyEditorView).GetField("_dirtyTechnologyNames", flags)!.GetValue(view)!).Add("Deleted");
                    save = view.SaveAsync;
                }
                if (referenced) fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.xml", kind == "Technology"
                    ? "<root><futuretech>Deleted</futuretech></root>" : "<root><future>Deleted</future></root>");
                Assert.Equal(!referenced, await save());
            });
            if (referenced) Assert.Equal(before, File.ReadAllBytes(path));
            else Assert.Empty(XDocument.Load(path).Root!.Elements());
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void LinkedMinorGodRemovalUsesCandidateRatherThanSavedDefinition()
    {
        using var workspace = new TestWorkspace("f07-linked-minor");
        var path = workspace.WriteText("minor_gods_mods.xml", "<minorgodsmods><HeroicAgeFoo/></minorgodsmods>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        Assert.True(ModDeletionSafety.InspectTechnology(files, "HeroicAgeFoo", workspace.RootPath).IsBlocked);
        var check = ModDeletionSafety.InspectTechnology(files, "HeroicAgeFoo", workspace.RootPath,
            new Dictionary<string, XDocument> { [path] = XDocument.Parse("<minorgodsmods/>") });
        Assert.False(check.IsBlocked);
        check.ValidateReadDependencies();
        Assert.Contains("HeroicAgeFoo", File.ReadAllText(path));
    }

    [Fact]
    public void RenameIsNotDeletionButRenameThenDeleteChecksBothIdentities()
    {
        var renames = new[] { new KeyValuePair<string, string>("Old", "New") };
        Assert.Empty(ModDeletionSafety.FindDeletedNames(["Old", "New"], ["New"], renames));
        Assert.Equal(new[] { "Old", "New" }, ModDeletionSafety.FindDeletedNames(["Old", "New"], [], renames));
        Assert.Empty(ModDeletionSafety.FindDeletedNames(["Old"], ["Old"], []));
    }

    [Theory]
    [InlineData("TechnologyEditorView.axaml.cs", "SaveAbilityXmlDocument(new XDocument(candidateDocument)")]
    [InlineData("PowerEditorView.axaml.cs", "SaveAbilityXmlDocument(_modDocument, _modPath)")]
    [InlineData("MajorGodEditorView.axaml.cs", "SaveAbilityXmlDocument(_modDocument, _modPath)")]
    public void ViewValidatesSnapshotBeforeSave(string file, string write)
    {
        var source = ProjectSource.Read("Windows", file);
        var guard = source.IndexOf("foreach (var inspection in deletionChecks) inspection.ValidateReadDependencies();", StringComparison.Ordinal);
        Assert.True(guard > source.IndexOf("PrepareDeletionChecks?.Invoke", StringComparison.Ordinal));
        Assert.True(guard < source.IndexOf(write, guard, StringComparison.Ordinal));
        var host = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        Assert.Contains("PrepareDeletionChecks = (names, overrides)", host);
    }
}
