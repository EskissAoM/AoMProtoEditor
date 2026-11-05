using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetReferenceMigrationPlanTests
{
    [Fact]
    public void SharedIconPreparesAllSupportedConsumersWithoutWriting()
    {
        using var workspace = new TestWorkspace("asset-plan");
        string[] xml = [
            "<protomods><unit name='U'><icon>resources/custom/old.png</icon><future keep='yes'/></unit></protomods>",
            "<techtreemods><tech name='T'><icon>CUSTOM\\OLD.PNG</icon></tech></techtreemods>",
            "<civsmods><civ><name>G</name><portrait>custom/old</portrait><breakoutportrait>custom/old.png</breakoutportrait></civ></civsmods>",
            "<minorgodsmods><ClassicalAgeG portraitfilename='custom/old.png' portraiticonfilename='resources/custom/old.png'/></minorgodsmods>",
            "<powersmod><power name='P'><icon>custom/old.png</icon></power></powersmod>",
            "<abilities><ability name='A'><icon>custom/old.png</icon></ability></abilities>"
        ];
        var paths = xml.Select((value, index) => workspace.WriteXml($"game/data/{index}.xml", value)).ToArray();
        var originals = paths.ToDictionary(path => path, File.ReadAllBytes);
        var plan = Prepare(workspace);
        Assert.Equal(6, plan.Paths.Count);
        Assert.Equal(8, plan.ReferenceCount);
        foreach (var path in paths)
        {
            Assert.Equal(originals[path], File.ReadAllBytes(path));
            Assert.Contains("resources\\custom\\new.png", plan.GetDocument(path)!.ToString());
        }
        Assert.Equal("yes", (string?)plan.GetDocument(paths[0])!.Descendants("future").Single().Attribute("keep"));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () => plan.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path)));
        foreach (var path in paths) Assert.True(XNode.DeepEquals(plan.GetDocument(path), XDocument.Load(path)));
    }

    [Theory]
    [InlineData("<root><future>custom/old.png</future></root>")]
    [InlineData("<root future='custom/old.png'/>")]
    [InlineData("<culturesmods><culture><compendiumimage>custom/old.png</compendiumimage></culture></culturesmods>")]
    [InlineData("<protomods><unit><icon>custom/old.dds</icon></unit></protomods>")]
    public void UnsupportedOrAmbiguousConsumersBlockWithoutWriting(string xml)
    {
        using var workspace = new TestWorkspace("asset-block");
        var path = workspace.WriteXml("game/data/custom.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => Prepare(workspace));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(AssetReferenceKind.Animation, "animfile", "soundsetfile")]
    [InlineData(AssetReferenceKind.SoundSet, "soundsetfile", "animfile")]
    public void AnimationAndSoundHaveSeparateRules(AssetReferenceKind kind, string tag, string otherTag)
    {
        using var workspace = new TestWorkspace("asset-kinds");
        var path = workspace.WriteXml("game/data/proto_mods.xml", $"<protomods><unit><{tag} culture='Greek'>custom/old.xml</{tag}></unit></protomods>");
        var plan = AssetReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), kind, "custom\\old.xml", "custom\\new.xml");
        Assert.Equal("custom\\new.xml", plan.GetDocument(path)!.Descendants(tag).Single().Value);
        Assert.Equal("Greek", (string?)plan.GetDocument(path)!.Descendants(tag).Single().Attribute("culture"));
        File.WriteAllText(path, $"<protomods><unit><{otherTag}>custom/old.xml</{otherTag}></unit></protomods>");
        Assert.Throws<InvalidOperationException>(() => AssetReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), kind, "custom/old.xml", "custom/new.xml"));
    }

    [Fact]
    public void UnchangedReadDependenciesBlockLateEditsAndWriteFailureRollsBack()
    {
        using var workspace = new TestWorkspace("asset-stale");
        var path = workspace.WriteXml("game/data/proto_mods.xml", "<protomods><unit><icon>custom/old.png</icon></unit></protomods>");
        var other = workspace.WriteXml("game/data/other.xml", "<root />");
        var before = File.ReadAllBytes(path);
        var plan = Prepare(workspace);
        File.WriteAllText(other, "<root changed='yes'/>");
        var wrote = false;
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () => wrote = true));
        Assert.False(wrote);
        plan = Prepare(workspace);
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () =>
        {
            plan.Save((document, target) => { PreservedXmlFile.SaveAtomic(document, target); throw new IOException("injected"); });
        }));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static AssetReferenceMigrationPlan Prepare(TestWorkspace workspace)
        => AssetReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            AssetReferenceKind.Icon, "resources/custom/old.png", "resources\\custom\\new.png");
}
