using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class DeletionCommitCheckpointTests
{
    [Fact]
    public void F07_StagedUnitDeletionMustRejectReferenceAddedBeforeSave()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='DeletedUnit'/></protomods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        var before = File.ReadAllBytes(fixture.ProtoPath);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var candidate = XDocument.Parse("<protomods/>");
                host.Set("_modXmlDoc", candidate);
                host.Set("_modXmlRoot", candidate.Root!);
                host.Set("_isDirty", true);
                var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
                    "<powersmod><power name='Referrer'><attachproto>DeletedUnit</attachproto></power></powersmod>");
                var inspection = ModDeletionSafety.InspectIdentifier(
                    [new(path, TechnologyReferenceDocumentKind.Powers, "Powers")],
                    GameIdentifierKind.ProtoUnit, "DeletedUnit", null);
                Assert.True(inspection.IsBlocked);
                var error = Record.Exception(() => host.Call("PreparePendingUnitDeletionChecks"));
                var actual = error is TargetInvocationException invocation ? invocation.InnerException : error;
                Assert.NotNull(actual);
                Assert.Contains("DeletedUnit", actual.Message);
            });
        }
        finally
        {
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            AvaloniaTestHost.Run(host.DisposeFixtureWindow);
        }
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("new-xml")]
    [InlineData("new-text")]
    [InlineData("deleted")]
    public void DeletionInspectionGuardsReadFilesAndDiscoveryUntilCommit(string change)
    {
        using var workspace = new TestWorkspace("f07-read-snapshot");
        var path = workspace.WriteText("powers_mods.xml", "<powersmod/>");
        var inspection = ModDeletionSafety.InspectIdentifier(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, "DeletedUnit", workspace.RootPath, captureReadDependencies: true);
        Assert.False(inspection.IsBlocked);
        inspection.ValidateReadDependencies();
        switch (change)
        {
            case "edited": File.WriteAllText(path, "<powersmod><power name='P'><attachproto>DeletedUnit</attachproto></power></powersmod>"); break;
            case "new-xml": workspace.WriteText("new.xml", "<root><attachproto>DeletedUnit</attachproto></root>"); break;
            case "new-text": workspace.WriteText("custom.txt", "DeletedUnit"); break;
            default: File.Delete(path); break;
        }
        Assert.Throws<IOException>(inspection.ValidateReadDependencies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingRenameIsNotDeletionUnlessRenamedUnitWasAlsoDeleted(bool deleteRenamed)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Old'/></protomods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var candidate = XDocument.Parse(deleteRenamed ? "<protomods/>" : "<protomods><unit name='New'/></protomods>");
                host.Set("_modXmlDoc", candidate);
                host.Set("_modXmlRoot", candidate.Root!);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow);
                ((Dictionary<string, string>)type.GetField("_pendingUnitRenames", flags)!.GetValue(host)!)["Old"] = "New";
                ((HashSet<string>)type.GetField("_dirtyUnitNames", flags)!.GetValue(host)!).Add("New");
                var deleted = (IReadOnlyList<string>)host.Call("GetPendingDeletedUnitNames")!;
                if (deleteRenamed) Assert.Equal(new[] { "Old", "New" }, deleted);
                else Assert.Empty(deleted);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void HostChecksDeletionInsideTransactionBeforeWritesAndSavesPowerDraftsFirst()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var begin = source.IndexOf("private async void Save_Click", StringComparison.Ordinal);
        var save = source[begin..source.IndexOf("private async void ProtounitEditView_Click", begin, StringComparison.Ordinal)];
        var power = save.IndexOf("GetPendingDeletedUnitNames().Count > 0 && _powerView?.IsDirty == true", StringComparison.Ordinal);
        Assert.True(power >= 0);
        Assert.True(save.IndexOf("await _powerView.SaveAsync()", power, StringComparison.Ordinal) <
                    save.IndexOf("await SaveCurrentUnitCoreAsync", StringComparison.Ordinal));
        var firstBundleWrite = save.IndexOf("SavePendingOwnedTransformBundleDeletions();", StringComparison.Ordinal);
        var dependencyCheck = save.IndexOf("inspection.ValidateReadDependencies();", StringComparison.Ordinal);
        Assert.True(firstBundleWrite >= 0 && dependencyCheck >= 0 && dependencyCheck < firstBundleWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidDeletionAcceptsProspectiveOwnedBundleRemovalWithoutWriting(bool ownedBundle)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='DeletedUnit'><command>DeletedUnitToTarget</command></unit></protomods>");
        var commandPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml",
            "<protounitcommandmods><protounitcommand><name>DeletedUnitToTarget</name><associatedtech>DeletedUnitToTarget</associatedtech></protounitcommand></protounitcommandmods>");
        var transformPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml",
            ownedBundle ? "<unittransformmods><transform><from>DeletedUnit</from><to>Target</to><command>DeletedUnitToTarget</command></transform></unittransformmods>" : "<unittransformmods/>");
        var techPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='DeletedUnitToTarget'/></techtreemods>");
        var paths = new[] { fixture.ProtoPath, commandPath, transformPath, techPath };
        var before = FileIntegrityTransaction.CaptureStates(paths);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var candidate = XDocument.Parse("<protomods/>");
                host.Set("_modXmlDoc", candidate);
                host.Set("_modXmlRoot", candidate.Root!);
                if (ownedBundle)
                {
                    var field = typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow).GetField(
                        "_pendingOwnedTransformBundleDeletions", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    ((Dictionary<string, List<OwnedTransformCommandBundle>>)field.GetValue(host)!)["DeletedUnit"] =
                        [new("DeletedUnitToTarget", "DeletedUnitToTarget", "DeletedUnit", "Target")];
                }
                var checks = (IReadOnlyList<ModDeletionInspection>)host.Call("PreparePendingUnitDeletionChecks")!;
                Assert.Equal(ownedBundle ? 3 : 1, checks.Count);
                Assert.All(checks, check =>
                {
                    Assert.False(check.IsBlocked);
                    check.ValidateReadDependencies();
                });
            });
            foreach (var entry in before)
                Assert.Equal(entry.Value, FileIntegrityTransaction.CaptureState(entry.Key));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
