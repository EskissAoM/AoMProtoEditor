using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceGroup6Tests
{
    [Theory]
    [InlineData(false, false, "type='Data' subtype='ActionEnable' action='Old'", "Owner", "action")]
    [InlineData(false, true, "type='Data' subtype='SelfDestructProtoAction' protoaction='Old'", "Owner", "protoaction")]
    [InlineData(false, true, "type='Data' subtype='ProtoActionAdd' unittype='Owner' protoaction='Old'", "Other", "protoaction")]
    [InlineData(true, false, "type='Damage' action='Old'", "Owner", "action")]
    [InlineData(true, true, "type='ActionEnable' action='Old'", "Owner", "action")]
    [InlineData(true, true, "type='ProtoActionAdd' unittype='Owner' protoaction='Old'", "Other", "protoaction")]
    [InlineData(true, false, "type='AssignOnHitEffect' srcproto='Owner' srcaction='Old' action='Old'", "Other", "srcaction")]
    [InlineData(true, true, "type='AssignOnHitEffect' srcproto='Other' srcaction='Old' action='Old'", "Owner", "action")]
    public void OnlyTheOwnedActionChangesAndCommits(bool power, bool wrapped, string attributes, string target, string field)
    {
        using var workspace = new TestWorkspace("group6-owned");
        var xml = Document(power, wrapped, $"<effect {attributes} keep='yes'><target type='ProtoUnit'>{target}</target><!--preserve--><future>Old</future></effect>");
        var path = workspace.WriteText(power ? "powers_mods.xml" : "techtree_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var plan = Prepare(workspace, []);
        Assert.Single(plan.Paths);
        Assert.Single(plan.AffectedReferences);
        Assert.Equal(before, File.ReadAllBytes(path));
        var expected = XDocument.Parse(xml);
        expected.Descendants("effect").Single().SetAttributeValue(field, "New");
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((doc, destination) => PreservedXmlFile.SaveAtomic(doc, destination)));
        Assert.True(XNode.DeepEquals(expected, XDocument.Load(path)));
    }

    [Theory]
    [InlineData("<effect type='Damage' action='Old'><target type='ProtoUnit'>Other</target></effect>")]
    [InlineData("<effect type='Damage' action='OldSuffix'><target type='ProtoUnit'>Owner</target></effect>")]
    [InlineData("<effect type='Damage' allactions='1'><target type='ProtoUnit'>Owner</target></effect>")]
    [InlineData("<effect type='DamageBonus' action='Old' unittype='Owner'><target type='ProtoUnit'>Other</target></effect>")]
    [InlineData("<effect type='ProtoActionAdd' protoaction='Old' unittype='Other'><target type='ProtoUnit'>Owner</target></effect>")]
    public void OtherOwnersAndAllActionsStayUnchanged(string effect)
    {
        using var workspace = new TestWorkspace("group6-unrelated");
        var path = workspace.WriteText("powers_mods.xml", Document(true, false, effect));
        var before = File.ReadAllBytes(path);
        Assert.Empty(Prepare(workspace, []).Paths);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("<effect type='Damage' action='Old'/>")]
    [InlineData("<effect type='ProtoActionAdd' protoaction='Old'><target type='ProtoUnit'>Owner</target></effect>")]
    [InlineData("<effect type='Damage' action='Old'><target type='ProtoUnit'>Owner</target><target type='ProtoUnit'>Other</target></effect>")]
    [InlineData("<effect type='Damage' action='Old'><target type='Player'>Owner</target></effect>")]
    [InlineData("<future><effect type='Damage' action='Old'><target type='ProtoUnit'>Owner</target></effect></future>")]
    [InlineData("<effect xmlns='urn:extension' type='Damage' action='Old'><target type='ProtoUnit'>Owner</target></effect>")]
    [InlineData("<effect xmlns:e='urn:extension' type='Damage' e:action='Old'><target type='ProtoUnit'>Owner</target></effect>")]
    public void UnresolvedOwnersRefuseWithoutPartialMutation(string effect)
    {
        using var workspace = new TestWorkspace("group6-unsafe");
        var xml = Document(true, false, "<effect type='Damage' action='Old'><target type='ProtoUnit'>Owner</target></effect>" + effect);
        var path = workspace.WriteText("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => Prepare(workspace, []));
        Assert.Equal(before, File.ReadAllBytes(path));
        var doc = XDocument.Parse(xml);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferencePolicy.RenameEffectProtoActionReferences(doc, new("Owner", "Old", "New"), []));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), doc));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedTargetRequiresAvailableCollisionFreeCatalog(bool collision)
    {
        using var workspace = new TestWorkspace("group6-catalog");
        var path = workspace.WriteText("powers_mods.xml", Document(true, false,
            "<effect type='Damage' action='Old'><target type='ProtoUnit'>Owner</target></effect>"));
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => Prepare(workspace, collision ? ["owner"] : null));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void UnitOnlySourceDoesNotNeedMixedTargetCatalog()
    {
        using var workspace = new TestWorkspace("group6-source");
        workspace.WriteText("powers_mods.xml", Document(true, false,
            "<effect type='AssignOnHitEffect' srcproto='Owner' srcaction='Old'><target type='ProtoUnit'>Other</target></effect>"));
        Assert.Single(Prepare(workspace, null).Paths);
    }

    [Fact]
    public void UnitAndActionRenamesUseTheUpdatedOwnerIdentity()
    {
        using var workspace = new TestWorkspace("group6-combined");
        var path = workspace.WriteText("powers_mods.xml", Document(true, false,
            "<effect type='AssignOnHitEffect' srcproto='Owner' srcaction='Old' action='Old'><target type='ProtoUnit'>Owner</target></effect>"));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, [new("Owner", "RenamedOwner")], [new("RenamedOwner", "Old", "New")], []);
        var effect = plan.GetDocument(path)!.Descendants("effect").Single();
        Assert.Equal("RenamedOwner", (string?)effect.Attribute("srcproto"));
        Assert.Equal("RenamedOwner", effect.Element("target")!.Value);
        Assert.Equal("New", (string?)effect.Attribute("srcaction"));
        Assert.Equal("New", (string?)effect.Attribute("action"));
    }

    [Fact]
    public void ActionOnlyHostSavesLoadTheUnitTypeCatalog()
    {
        var source = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs")).Replace("\r\n", "\n");
        Assert.Contains("var oppositeUnitTypeNames = _pendingUnitRenames.Count == 0 && _pendingProtoActionRenames.Count == 0\n                    ? null : GetUnitTypeNamesForMigration();", source);
    }

    private static GameIdentifierReferenceMigrationPlan Prepare(TestWorkspace workspace, IEnumerable<string>? unitTypes)
        => GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, [], [new("Owner", "Old", "New")], unitTypes);

    private static string Document(bool power, bool wrapped, string effects)
    {
        if (wrapped) effects = "<effects>" + effects + "</effects>";
        return power ? "<powersmod><power name='P' type='Rain'>" + effects + "</power></powersmod>" :
            "<techtreemods><tech name='T'>" + effects + "</tech></techtreemods>";
    }
}
