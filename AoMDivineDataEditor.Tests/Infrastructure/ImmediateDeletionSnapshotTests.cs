using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ImmediateDeletionSnapshotTests
{
    [Theory]
    [InlineData("DeleteCultureAsync", "culture_mods.xml", "<culturesmods><culture><name>Unused</name></culture></culturesmods>")]
    [InlineData("DeleteUnitTypeAsync", "abstract_unit_types_mods.xml", "<abstractunittypesmods><abstractunittype>Unused</abstractunittype></abstractunittypesmods>")]
    [InlineData("DeleteTechTypeAsync", "tech_types_mods.xml", "<techtypesmods><techtype>Unused</techtype></techtypesmods>")]
    public void ActualManagerCallbackStillDeletesUnreferencedDefinition(string method, string file, string xml)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/" + file, xml);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            Assert.True(AvaloniaTestHost.RunAsync(() => (Task<bool>)host.Call(method, "Unused")!));
            Assert.DoesNotContain("Unused", File.ReadAllText(path));
            Assert.NotNull(XDocument.Load(path).Root);
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(GameIdentifierKind.Culture, false)]
    [InlineData(GameIdentifierKind.Culture, true)]
    [InlineData(GameIdentifierKind.UnitType, false)]
    [InlineData(GameIdentifierKind.UnitType, true)]
    [InlineData(GameIdentifierKind.TechType, false)]
    [InlineData(GameIdentifierKind.TechType, true)]
    public void CapturedHostCheckRejectsLateContentOrManifestChangesBeforeDelete(GameIdentifierKind kind, bool newFile)
    {
        using var fixture = new BaselineSafetyFixture();
        var unrelated = fixture.Workspace.WriteText("TestMod/game/data/gameplay/other.xml", "<root/>");
        var before = File.ReadAllBytes(fixture.TypePath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            ModDeletionInspection? check = null;
            Assert.True(AvaloniaTestHost.RunAsync(() => (Task<bool>)host.Call("EnsureIdentifierCanBeDeletedAsync",
                kind, "Unused", kind.ToString(), null!, null!, null!,
                (Action<ModDeletionInspection>)(inspection => check = inspection))!));
            Assert.NotNull(check);
            if (newFile) fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.xml", "<root><future>Unused</future></root>");
            else File.WriteAllText(unrelated, "<root><future>Unused</future></root>");
            var wrote = false;
            Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([fixture.TypePath], () =>
            {
                check.ValidateReadDependencies();
                wrote = true;
                File.WriteAllText(fixture.TypePath, "<abstractunittypesmods/>");
            }));
            Assert.False(wrote);
            Assert.Equal(before, File.ReadAllBytes(fixture.TypePath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("DeleteCultureAsync", "SaveCultureXmlDocument")]
    [InlineData("DeleteUnitTypeAsync", "SaveUnitTypeXmlDocument")]
    [InlineData("DeleteTechTypeAsync", "SaveTechTypeXmlDocument")]
    public void ManagerValidatesCapturedInspectionBeforeFirstWrite(string method, string writer)
    {
        var body = ProjectSource.ExtractMethod(ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs"),
            "private async Task<bool> " + method);
        Assert.Contains("captureInspection: inspection => deletionCheck = inspection", body);
        var guard = body.IndexOf("deletionCheck!.ValidateReadDependencies();", StringComparison.Ordinal);
        Assert.True(guard > body.IndexOf(".Execute", StringComparison.Ordinal));
        Assert.True(guard < body.IndexOf(writer + "(", StringComparison.Ordinal));
    }
}
