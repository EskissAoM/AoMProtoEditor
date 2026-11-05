using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ManagedDeletionSnapshotTests
{
    [Theory]
    [InlineData("Command")]
    [InlineData("Ability")]
    [InlineData("Tactics")]
    public void ActualCallbacksStillDeleteUnreferencedCustomDefinitions(string kind)
    {
        using var fixture = new BaselineSafetyFixture();
        var relative = kind switch
        {
            "Command" => "proto_unit_command_mods.xml",
            "Ability" => "powers_mods.xml",
            _ => "tactics/Unused.tactics"
        };
        var xml = kind switch
        {
            "Command" => "<protounitcommandmods><protounitcommand><name>Unused</name></protounitcommand></protounitcommandmods>",
            "Ability" => "<powersmod><power name='Unused' type='GeneralEffect'/></powersmod>",
            _ => "<tactics/>"
        };
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/" + relative, xml);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (kind == "Command")
                    Assert.True(await (Task<bool>)host.Call("DeleteProtoUnitCommandCoreAsync", "Unused", null!)!);
                else if (kind == "Ability")
                {
                    var result = new AbilityManagerResult();
                    result.DeletedNames.Add("Unused");
                    await (Task)host.Call("ApplyAbilityManagerChangesAsync", result)!;
                }
                else
                {
                    var result = new TacticsManagerResult();
                    result.DeletedNames.Add("Unused.tactics");
                    await (Task)host.Call("ApplyTacticsManagerChangesAsync", result)!;
                }
            });
            if (kind == "Tactics") Assert.False(File.Exists(path));
            else Assert.DoesNotContain("Unused", File.ReadAllText(path));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(GameIdentifierKind.ProtoUnitCommand, false)]
    [InlineData(GameIdentifierKind.ProtoUnitCommand, true)]
    [InlineData(GameIdentifierKind.Ability, false)]
    [InlineData(GameIdentifierKind.Ability, true)]
    [InlineData(GameIdentifierKind.Tactics, false)]
    [InlineData(GameIdentifierKind.Tactics, true)]
    public void HostSnapshotRejectsLateChangesBeforeAnyMutation(GameIdentifierKind kind, bool newFile)
    {
        using var fixture = new BaselineSafetyFixture();
        var unrelated = fixture.Workspace.WriteText("TestMod/game/data/gameplay/other.xml", "<root/>");
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            ModDeletionInspection? check = null;
            Assert.True(AvaloniaTestHost.RunAsync(() => (Task<bool>)host.Call("EnsureIdentifierCanBeDeletedAsync",
                kind, "Unused", kind.ToString(), null!, null!, null!,
                (Action<ModDeletionInspection>)(inspection => check = inspection))!));
            Assert.NotNull(check);
            if (newFile) fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.txt", "Unused");
            else File.WriteAllText(unrelated, "<root><future>Unused</future></root>");
            var wrote = false;
            Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([fixture.ProtoPath], () =>
            {
                check.ValidateReadDependencies();
                wrote = true;
                File.WriteAllText(fixture.ProtoPath, "<protomods/>");
            }));
            Assert.False(wrote);
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void ExcludedTacticsTargetReplacementIsNotDeleted()
    {
        using var workspace = new TestWorkspace("f07-tactics-target");
        var path = workspace.WriteText("Unused.tactics", "<tactics/>");
        var targetState = FileIntegrityTransaction.CaptureState(path);
        var check = ModDeletionSafety.InspectIdentifier(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.Tactics, "Unused.tactics", workspace.RootPath,
            excludedPaths: [path], captureReadDependencies: true);
        Assert.False(check.IsBlocked);
        File.WriteAllText(path, "<tactics><!--external replacement--></tactics>");
        var externalBytes = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([path],
            new Dictionary<string, FileIntegrityTransaction.FileState> { [path] = targetState }, () =>
            {
                check.ValidateReadDependencies();
                File.Delete(path);
            }));
        Assert.Equal(externalBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("private async Task<bool> DeleteProtoUnitCommandCoreAsync", "SaveProtoUnitCommandMods(doc);")]
    [InlineData("private async Task<bool> ApplyTacticsManagerChangesAsync", "foreach (var (oldPath, newPath) in preparedRenames)")]
    public void CallersValidateSnapshotInsideTransactionBeforeMutation(string signature, string mutation)
    {
        var body = ProjectSource.ExtractMethod(ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs"), signature);
        Assert.Contains("captureInspection:", body);
        var guard = body.IndexOf(".ValidateReadDependencies();", StringComparison.Ordinal);
        Assert.True(guard > body.IndexOf(".Execute(", StringComparison.Ordinal));
        Assert.True(guard < body.IndexOf(mutation, StringComparison.Ordinal));
        if (signature.Contains("Tactics"))
        {
            Assert.Contains("preparedStates[Path.GetFullPath(deletedPath)] = FileIntegrityTransaction.CaptureState(deletedPath);", body);
            Assert.Contains("preparedStates.TryAdd(state.Key, state.Value);", body);
        }
    }

    [Fact]
    public void AbilityCallerPassesRetainedDeletionChecksIntoSharedTransaction()
    {
        var caller = ProjectSource.ExtractMethod(ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs"), "private async Task ApplyAbilityManagerChangesAsync");
        Assert.Contains("captureInspection: deletionChecks.Add", caller);
        Assert.Contains("PersistAbilityChangesAsync(powerFile, cleanup, stringsState, migration, deletionChecks)", caller);
        var persistence = ProjectSource.ExtractMethod(ProjectSource.Read("Windows", "ProtoEditorWindow.AbilityStrings.cs"), "private async Task PersistAbilityChangesAsync");
        var guard = persistence.IndexOf("inspection.ValidateReadDependencies();", StringComparison.Ordinal);
        var transaction = persistence.IndexOf("ModDocumentPersistence.ExecuteAsync", StringComparison.Ordinal);
        var write = persistence.IndexOf("SaveAbilityXmlDocument(", StringComparison.Ordinal);
        Assert.True(transaction >= 0 && guard > transaction && write > guard);
    }
}
