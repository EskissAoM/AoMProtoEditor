using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

public sealed class PlacementRuleCatalogTests(ITestOutputHelper output)
{
    [CorpusFact]
    public void OriginalPlacementReferencesAndFolderMetadata()
    {
        using var stream = File.OpenRead(OriginalCorpus.RequirePath());
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out _));
        var rules = PlacementRuleCatalog.FromArchive(archive);
        output.WriteLine("Archive root: " + archive.RootPath);
        output.WriteLine("Archive samples: " + string.Join("; ", rules.Take(4)));
        Assert.NotEmpty(rules);
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries!.Where(entry => entry.Name.Equals("proto.xml.xmb", StringComparison.OrdinalIgnoreCase)))
        {
            var xml = XmbReader.ToFormattedXml(entry.ReadDataDecompressed(stream));
            if (xml != null)
                foreach (var value in XDocument.Parse(xml).Descendants("placementfile").Select(element => element.Value))
                    references.Add(value);
        }
        output.WriteLine("References: " + string.Join("; ", references.Take(12)));
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.Contains(reference, rules, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlacementRuleCatalogUsesOnlyTheDedicatedFolderAndNormalizesCompiledXml()
    {
        var names = PlacementRuleCatalog.FilterArchivePaths("game\\data\\", [
            "gameplay/placement_rules/wall.xml.XMB", "game/data/gameplay/placement_rules/TownCenter.xml",
            "gameplay/placement_rules/nested/future.XML", "gameplay/placement_rules/wall.xml",
            "gameplay/placement_rules/texture.png", "gameplay/placement_rules_other/wrong.xml",
            "art/placement_rules/wrong.xml", "../gameplay/placement_rules/escape.xml",
            "C:/game/data/gameplay/placement_rules/absolute.xml"]);
        Assert.Equal(new[] { "nested\\future.XML", "TownCenter.xml", "wall.xml" }, names);
    }

    [Fact]
    public void PlacementRuleCatalogFindsCustomXmlAndRescansNewOrRemovedFiles()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("placement_rules");
        workspace.WriteText("placement_rules/WALL.xml", "metadata only");
        workspace.WriteText("placement_rules/nested/custom.xml", "metadata only");
        workspace.WriteText("placement_rules/compiled.xml.xmb", "skip");
        workspace.WriteText("elsewhere/wrong.xml", "skip");
        Assert.Equal(new[] { "nested\\custom.xml", "WALL.xml" }, PlacementRuleCatalog.WithCustomFiles(["wall.xml"], directory));
        var added = workspace.WriteText("placement_rules/new.xml", "metadata only");
        Assert.Contains("new.xml", PlacementRuleCatalog.WithCustomFiles(["wall.xml"], directory));
        File.Delete(added);
        Assert.DoesNotContain("new.xml", PlacementRuleCatalog.WithCustomFiles(["wall.xml"], directory));
    }
}
