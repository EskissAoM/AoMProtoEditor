using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CompiledTacticsSafetyTests
{
    private static string Write(TestWorkspace workspace, string relative, string xml)
    {
        var path = workspace.GetPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = XmbReaderTests.BuildDocument(XElement.Parse(xml));
        Assert.NotNull(XmbReader.ToFormattedXml(bytes));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("nested/attack.tactics.xmb")]
    [InlineData("game/data/gameplay/tactics/nested/attack.xmb")]
    public void DiscoveryIncludesCompiledTacticsInTheRetainedManifest(string relative)
    {
        using var workspace = new TestWorkspace("f13-discovery");
        var path = Write(workspace, relative, "<tactics/>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);
        Assert.Contains(files, file => file.Path == path && file.Kind == TechnologyReferenceDocumentKind.Tactics);
        Assert.Contains(path, snapshot.Paths);
    }

    [Theory]
    [InlineData("Unit")]
    [InlineData("Technology")]
    [InlineData("Strings")]
    public void CompiledReferencesBlockDeletionOrRetainOwnedStrings(string kind)
    {
        using var workspace = new TestWorkspace("f13-references");
        var path = Write(workspace, "attack.tactics.xmb",
            "<tactics><action><name>Attack</name><unit>OldUnit</unit><tech>OldTech</tech><displaynameid>STR_UNIT_OLD_NAME</displaynameid></action></tactics>");
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        if (kind == "Strings")
            Assert.Contains("STR_UNIT_OLD_NAME", ModDeletionSafety.InspectStringReferences(workspace.RootPath, ["STR_UNIT_OLD_NAME"]).ReferencedIds);
        else
        {
            var check = kind == "Unit"
                ? ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.ProtoUnit, "OldUnit", workspace.RootPath, captureReadDependencies: true)
                : ModDeletionSafety.InspectTechnology(files, "OldTech", workspace.RootPath);
            Assert.True(check.IsBlocked);
            Assert.Contains(check.References, reference => reference.Path == path);
            check.ValidateReadDependencies();
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenameRequiringBinaryChangesRefusesBeforeAnyWrite(bool technology)
    {
        using var workspace = new TestWorkspace("f13-rename");
        var path = Write(workspace, "attack.tactics.xmb", "<tactics><action><unit>OldUnit</unit><tech>OldTech</tech></action></tactics>");
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (technology) TechnologyReferenceMigrationPlan.Prepare(files, [new TechnologyRenameOperation("OldTech", "NewTech")], discovery);
            else GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("OldUnit", "NewUnit")], discovery: discovery);
        });
        Assert.Contains("compiled", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path, error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedCompiledFilesRemainReadDependenciesWithoutBecomingWriteTargets(bool technology)
    {
        using var workspace = new TestWorkspace("f13-unrelated");
        var path = Write(workspace, "attack.tactics.xmb", "<tactics><action><unit>OtherUnit</unit></action></tactics>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var generic = technology ? null : GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("OldUnit", "NewUnit")], discovery: discovery);
        var tech = technology ? TechnologyReferenceMigrationPlan.Prepare(files, [new TechnologyRenameOperation("OldTech", "NewTech")], discovery) : null;
        var states = technology ? tech!.ExpectedStates : generic!.ExpectedStates;
        Assert.Contains(path, states.Keys);
        Assert.Empty(technology ? tech!.Paths : generic!.Paths);
        Write(workspace, "attack.tactics.xmb", "<tactics><action><unit>OldUnit</unit><tech>OldTech</tech></action></tactics>");
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([], states, () => { }));
    }

    [Fact]
    public void MalformedCompiledFilesFailClosedAndNewCompiledFilesInvalidateDiscovery()
    {
        using var workspace = new TestWorkspace("f13-malformed");
        ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);
        var path = workspace.WriteText("bad.tactics.xmb", "invalid XMB");
        Assert.Throws<IOException>(snapshot.Validate);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        Assert.Throws<InvalidOperationException>(() => ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.ProtoUnit, "Old", workspace.RootPath));
        Assert.Equal("invalid XMB", File.ReadAllText(path));
    }

    [Fact]
    public void DecoderBoundsExpandedXmlBeforeReturningIt()
    {
        var bytes = XmbReaderTests.BuildDocument(XElement.Parse("<tactics><action><name>Attack</name></action></tactics>"));
        var xml = XmbReader.ToFormattedXml(bytes)!;
        Assert.Null(XmbReader.ToFormattedXml(bytes, xml.Length - 1));
        Assert.Equal(xml, XmbReader.ToFormattedXml(bytes, xml.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateCompiledChangesInvalidateDeletionAndStringCleanupInspections(bool strings)
    {
        using var workspace = new TestWorkspace("f13-late-inspection");
        var path = Write(workspace, "attack.tactics.xmb", "<tactics/>");
        Action validate = strings
            ? ModDeletionSafety.InspectStringReferences(workspace.RootPath, ["STR_UNIT_OLD_NAME"]).ValidateReadDependencies
            : ModDeletionSafety.InspectIdentifier(ModReferenceFileCatalog.Discover(workspace.RootPath),
                GameIdentifierKind.ProtoUnit, "OldUnit", workspace.RootPath, captureReadDependencies: true).ValidateReadDependencies;
        Write(workspace, "attack.tactics.xmb", "<tactics><action><unit>OldUnit</unit><displaynameid>STR_UNIT_OLD_NAME</displaynameid></action></tactics>");
        var bytes = File.ReadAllBytes(path);
        Assert.Throws<IOException>(validate);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
