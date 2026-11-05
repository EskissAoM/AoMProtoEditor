using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TacticTargetReferenceTests
{
    private static readonly string[] Fields =
        ["attackprioritytype", "attacktype", "excludeattacktype", "autoattacktype", "attackresponsetype", "attackmovetype"];

    private static string Document(string fields)
        => $"<tactics><action><name>Attack</name></action><tactic>Default{fields}<action priority='80'>Attack</action><future keep='yes'/></tactic></tactics>";

    [Theory]
    [InlineData("attackprioritytype")]
    [InlineData("attacktype")]
    [InlineData("excludeattacktype")]
    [InlineData("autoattacktype")]
    [InlineData("attackresponsetype")]
    [InlineData("attackmovetype")]
    public void UnitRenameRecognizesEachDirectTacticTarget(string field)
    {
        var document = XDocument.Parse(Document($"<{field}>OldUnit</{field}>"));
        Assert.Contains($"<{field}>", GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.ProtoUnit, "OldUnit"));
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.ProtoUnit, "OldUnit", "NewUnit"));
        Assert.Equal("NewUnit", document.Descendants(field).Single().Value);
    }

    [Theory]
    [InlineData(GameIdentifierKind.ProtoUnit)]
    [InlineData(GameIdentifierKind.UnitType)]
    public void FileMigrationAndDeletionShareAllSixTargetFields(GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("tactic-target-migration");
        var path = workspace.WriteXml("nested/Targets.tactics", Document(string.Concat(Fields.Select(field =>
            $"<{field} bonusfactor='25' future='preserve'>Old</{field}>"))));
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var deletion = ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath);
        Assert.True(deletion.IsBlocked);
        Assert.Equal(6, deletion.References.Count);
        Assert.All(deletion.References, reference => Assert.True(reference.IsRecognized));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")],
            oppositeUnitKindNames: [], discovery: discovery);
        Assert.Equal(path, Assert.Single(plan.Paths));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () =>
        {
            plan.ValidateDiscovery();
            plan.Save((document, target) => document.Save(target));
        });
        var saved = XDocument.Load(path);
        foreach (var field in Fields)
        {
            var target = saved.Descendants(field).Single();
            Assert.Equal("New", target.Value);
            Assert.Equal("25", (string?)target.Attribute("bonusfactor"));
            Assert.Equal("preserve", (string?)target.Attribute("future"));
        }
        Assert.Equal("Default", saved.Descendants("tactic").Single().Nodes().OfType<XText>().First().Value);
        Assert.Equal("Attack", saved.Descendants("tactic").Single().Element("action")!.Value);
        Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
        Assert.False(ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath).IsBlocked);
        Assert.True(ModDeletionSafety.InspectIdentifier(files, kind, "New", workspace.RootPath).IsBlocked);
    }

    [Theory]
    [InlineData(GameIdentifierKind.ProtoUnit, "Old")]
    [InlineData(GameIdentifierKind.ProtoUnit, "New")]
    [InlineData(GameIdentifierKind.UnitType, "Old")]
    [InlineData(GameIdentifierKind.UnitType, "New")]
    public void AmbiguousUnitAndTypeNamesRefuseBeforeWriting(GameIdentifierKind kind, string collision)
    {
        using var workspace = new TestWorkspace("tactic-target-collision");
        var path = workspace.WriteXml("Targets.tactics", Document("<attacktype>Old</attacktype>"));
        var bytes = File.ReadAllBytes(path);
        var error = Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: [collision]));
        Assert.Contains("mixed Unit/Unit Type", error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(GameIdentifierKind.ProtoUnit)]
    [InlineData(GameIdentifierKind.UnitType)]
    public void CompiledTargetReferencesBlockRenameWithoutRewritingBinary(GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("tactic-target-compiled");
        var path = workspace.GetPath("Targets.tactics.xmb");
        var bytes = XmbReaderTests.BuildDocument(XElement.Parse(Document("<attacktype>Old</attacktype>")));
        File.WriteAllBytes(path, bytes);
        var error = Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: []));
        Assert.Contains("compiled tactics", error.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void SimilarNamesOutsideTheDirectTacticSchemaAreNeverRewritten()
    {
        var documents = new[]
        {
            "<root><tactic><attacktype>Old</attacktype></tactic></root>",
            "<tactics><action><attacktype>Old</attacktype></action></tactics>",
            "<tactics><tactic><future><attacktype>Old</attacktype></future></tactic></tactics>",
            "<tactics xmlns='urn:extension'><tactic><attacktype>Old</attacktype></tactic></tactics>"
        };
        foreach (var xml in documents)
        {
            var document = XDocument.Parse(xml);
            var before = new XDocument(document);
            Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.ProtoUnit, "Old", "New"));
            Assert.True(XNode.DeepEquals(before, document));
        }
    }
}
