using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyReferenceGroup4Tests
{
    [Theory]
    [InlineData("Cost", false)]
    [InlineData("Cost", true)]
    [InlineData("CommandAdd", false)]
    [InlineData("CommandAdd", true)]
    [InlineData("CommandRemove", false)]
    [InlineData("CommandRemove", true)]
    public void SupportedFormsIndexAndCommitOnlyExactTechnologyReferences(string type, bool wrapped)
    {
        using var workspace = new TestWorkspace("group4-technology");
        var power = type == "Cost";
        var effect = power
            ? "<effect type='cOsT' resource='Gold' amount='2'><target type='tEcH'>oLD</target><target type='TechAll'/><target type='ProtoUnit'>Old</target></effect>"
            : $"<effect type='dAtA' subtype='{type}' tech='oLD' proto='Old' command='Old' amount='1'/>";
        var xml = Document(power, wrapped ? $"<effects>{effect}</effects>" : effect);
        var path = workspace.WriteText(power ? "powers_mods.xml" : "techtree_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var source = new TechnologyReferenceSource("fixture", XDocument.Parse(xml), power ? TechnologyReferenceDocumentKind.Powers : TechnologyReferenceDocumentKind.Techtree, CanMigrate: true);
        var reference = Assert.Single(TechnologyReferenceIndex.FindReferencesTo([source], "Old"));
        Assert.True(reference.IsRecognized);
        Assert.Equal("Owner", reference.Owner);
        Assert.Empty(TechnologyReferenceIndex.FindReferencesTo([source], "Ol"));
        var plan = TechnologyReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), [new("Old", "New")]);
        Assert.Empty(plan.UnknownReferences);
        Assert.Single(plan.Paths);
        Assert.Equal(before, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target)));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("oLD", "New")), XDocument.Load(path)));
    }

    [Theory]
    [InlineData("TechAll", "")]
    [InlineData("TechAll", "All")]
    [InlineData("TechAll", "Old")]
    [InlineData("ProtoUnit", "Old")]
    [InlineData("Player", "Old")]
    [InlineData("TechType", "Old")]
    public void CostOtherTargetKindsAndAllSentinelArePreserved(string targetKind, string value)
    {
        var doc = XDocument.Parse(Document(true, $"<effect type='Cost'><target type='{targetKind}'>{value}</target></effect>"));
        var before = new XDocument(doc);
        var source = new TechnologyReferenceSource("Power", doc, TechnologyReferenceDocumentKind.Powers);
        Assert.Empty(TechnologyReferenceIndex.FindReferencesTo([source], value.Length == 0 ? "All" : value));
        Assert.Equal(0, TechnologyReferenceIndex.RenameRecognizedReferences(source, value, "New"));
        Assert.True(XNode.DeepEquals(before, doc));
    }

    [Theory]
    [InlineData("<powersmod><power name='Owner'><future><effect type='Cost'><target type='Tech'>Old</target></effect></future></power></powersmod>", true)]
    [InlineData("<powersmod xmlns='urn:extension'><power name='Owner'><effect type='Cost'><target type='Tech'>Old</target></effect></power></powersmod>", true)]
    [InlineData("<powersmod xmlns:x='urn:extension'><power name='Owner'><effect type='Cost'><target x:type='Tech'>Old</target></effect></power></powersmod>", true)]
    [InlineData("<techtreemods><tech name='Owner'><future><effect type='Data' subtype='CommandAdd' tech='Old'/></future></tech></techtreemods>", false)]
    [InlineData("<techtreemods><tech name='Owner'><effect type='Other' subtype='CommandAdd' tech='Old'/></tech></techtreemods>", false)]
    [InlineData("<techtreemods><tech name='Owner'><effect type='Data' subtype='Unknown' tech='Old'/></tech></techtreemods>", false)]
    [InlineData("<techtreemods xmlns:x='urn:extension'><tech name='Owner'><effect type='Data' subtype='CommandRemove' x:tech='Old'/></tech></techtreemods>", false)]
    [InlineData("<techtreemods xmlns='urn:extension'><tech name='Owner'><effect type='Data' subtype='CommandRemove' tech='Old'/></tech></techtreemods>", false)]
    public void UnsupportedContextsStayUnknownAndNeverWrite(string xml, bool power)
    {
        using var workspace = new TestWorkspace("group4-unknown");
        var path = workspace.WriteText(power ? "powers_mods.xml" : "techtree_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var plan = TechnologyReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), [new("Old", "New")]);
        Assert.Single(plan.UnknownReferences);
        Assert.Empty(plan.Paths);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void UnrelatedPowerEffectDoesNotAcquireCostTechnologySemantics()
    {
        var doc = XDocument.Parse(Document(true, "<effect type='Damage'><target type='Tech'>Old</target></effect>"));
        Assert.Equal(0, TechnologyReferenceIndex.RenameRecognizedReferences(
            new("Power", doc, TechnologyReferenceDocumentKind.Powers), "Old", "New"));
        Assert.Equal("Old", doc.Descendants("target").Single().Value);
    }

    private static string Document(bool power, string contents) => power
        ? $"<powersmod><power name='Owner' type='Rain'>{contents}<!--preserve--><future keep='yes'/></power></powersmod>"
        : $"<techtreemods><tech name='Owner'>{contents}<!--preserve--><future keep='yes'/></tech></techtreemods>";
}
