using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

// Acceptance assertions describe the desired behavior. Failures are checkpoint-B
// evidence, not an accepted baseline; checkpoint C must make them pass.
[Trait("Category", "ReferenceCoverageAudit")]
public sealed class ReferenceCoverageCheckpointBTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> PowerFields()
    {
        yield return Row("RC01", "Shockwave", "<shockwavevfx>Old</shockwavevfx>");
        yield return Row("RC02-tree", "GaiaForest", "<treeproto>Old</treeproto><treeproto>Other</treeproto>");
        yield return Row("RC02-treeeffect", "GaiaForest", "<treeeffectproto>Old</treeeffectproto>");
        yield return Row("RC02-effect", "Valor", "<effectproto>Old</effectproto>");
        yield return Row("RC02-vfx", "Traitor", "<vfxproto>Old</vfxproto>");
        yield return Row("RC02-nested", "Bronze", "<armor><vfxproto>Old</vfxproto></armor>");
        yield return Row("RC03", "SwapUnit", "<swap swapto='Old' amount='2'>Other</swap>");
        yield return Row("RC04", "Rain", "<effects><effect type='AssignOnHitEffect' srcproto='Old' srcaction='Attack'/></effects>");
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            yield return Row("RC05", "Lure", "<attracttype>Old</attracttype><attracttype>Other</attracttype>", kind);
            yield return Row("RC06", "SwitchingEffects", "<modelattachproto>Old</modelattachproto><modelattachproto>Other</modelattachproto>", kind);
            yield return Row("RC07", "Bronze", "<armor><abstracttype>Old</abstracttype></armor>", kind);
            yield return Row("RC08", "Wither", "<killunittype>Old</killunittype><killunittype>Other</killunittype>", kind);
        }
    }

    private static object[] Row(string id, string type, string fields, GameIdentifierKind kind = GameIdentifierKind.ProtoUnit)
        => [id, kind, $"<powersmod><power name='Owner' type='{type}'>{fields}<future keep='yes'/></power></powersmod>"];

    [Theory]
    [MemberData(nameof(PowerFields))]
    public void SupportedPowerReferenceMustMigrate(string id, GameIdentifierKind kind, string xml)
    {
        using var workspace = new TestWorkspace("reference-coverage-rename");
        var path = workspace.WriteXml("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        GameIdentifierReferenceMigrationPlan? plan = null;
        var error = Record.Exception(() => plan = GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: []));
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"{id}/{kind}: rename={(error != null ? "REFUSED" : plan!.Paths.Count == 0 ? "SILENT_OMISSION" : "MIGRATED")}; error={error?.Message}");
        Assert.True(error == null, $"{id}/{kind}: supported editor field refused: {error?.Message}");
        Assert.Single(plan!.Paths);
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((document, target) => PreservedXmlFile.SaveAtomic(document, target)));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("Old", "New", StringComparison.Ordinal)), XDocument.Load(path)),
            "Only the intended identifier may change; unrelated values, attributes, repeats and unknown XML must remain.");
    }

    [Theory]
    [MemberData(nameof(PowerFields))]
    public void DeletionMustDetectPowerDependencyAndIgnoreSubstrings(string id, GameIdentifierKind kind, string xml)
    {
        using var workspace = new TestWorkspace("reference-coverage-delete");
        var path = workspace.WriteXml("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var inspection = ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath);
        output.WriteLine($"{id}/{kind}: deletion blocked={inspection.IsBlocked}; recognized={inspection.References.Count(r => r.IsRecognized)}; possible={inspection.References.Count(r => !r.IsRecognized)}");
        Assert.True(inspection.IsBlocked);
        Assert.Equal(before, File.ReadAllBytes(path));
        File.WriteAllText(path, xml.Replace("Old", "OldSuffix", StringComparison.Ordinal));
        var negativeBytes = File.ReadAllBytes(path);
        Assert.False(ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath).IsBlocked);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")]);
        Assert.Empty(plan.Paths);
        Assert.Equal(negativeBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(GameIdentifierKind.Ability)]
    [InlineData(GameIdentifierKind.Culture)]
    [InlineData(GameIdentifierKind.MajorGod)]
    [InlineData(GameIdentifierKind.ProtoUnitCommand)]
    [InlineData(GameIdentifierKind.Tactics)]
    public void RC10_UnrelatedIdentifierMustNotRewriteProtoUnitTarget(GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("reference-coverage-kind");
        var path = workspace.WriteXml("powers_mods.xml", "<powersmod><power name='P' type='Rain'><effects><effect type='Data'><target type='ProtoUnit'>Old</target></effect></effects></power></powersmod>");
        var before = File.ReadAllBytes(path);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")]);
        var locations = GameIdentifierReferencePolicy.FindDeletionReferenceLocations(XDocument.Load(path), kind, "Old");
        output.WriteLine($"RC10/{kind}: preparedWrites={plan.Paths.Count}; deletion recognized={locations.Count(r => r.IsRecognized)}; candidate={plan.GetDocument(path)}");
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(plan.Paths);
        Assert.DoesNotContain(locations, location => location.IsRecognized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RC09_PowerTechActivateMustBeSeenByTechnologyIndex(bool deletion)
    {
        using var workspace = new TestWorkspace("reference-coverage-tech");
        var path = workspace.WriteXml("powers_mods.xml", "<powersmod><power name='P' type='Rain'><effects><effect type='TechActivate'><target type='Tech'>Old</target></effect></effects></power></powersmod>");
        var before = File.ReadAllBytes(path);
        var source = new TechnologyReferenceSource("Powers", XDocument.Load(path), TechnologyReferenceDocumentKind.Powers, path, CanMigrate: true);
        if (deletion)
        {
            var references = TechnologyReferenceIndex.FindReferencesTo([source], "Old");
            output.WriteLine($"RC09: technology deletion index references={references.Count}");
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.NotEmpty(references);
        }
        else
        {
            var plan = TechnologyReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), [new("Old", "New")]);
            output.WriteLine($"RC09: technology rename writes={plan.Paths.Count}; unknown={plan.UnknownReferences.Count}");
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(plan.UnknownReferences);
            Assert.Single(plan.Paths);
            FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                () => plan.Save((document, target) => PreservedXmlFile.SaveAtomic(document, target)));
            Assert.Equal("New", XDocument.Load(path).Descendants("target").Single().Value);
        }
    }

    [Fact]
    public void ExistingRepeatedAndDelimitedReferencesStillPreserveOtherValues()
    {
        var document = XDocument.Parse("<powersmod><power name='P'><createunit quantity='2'>Other| Old |OldSuffix</createunit><similarunittypes><unittype>Type</unittype><unittype>OtherType</unittype></similarunittypes></power></powersmod>");
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.Equal("Other| New |OldSuffix", document.Descendants("createunit").Single().Value);
        Assert.Equal("2", (string?)document.Descendants("createunit").Single().Attribute("quantity"));
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.UnitType, "Type", "NewType"));
        Assert.Equal(new[] { "NewType", "OtherType" }, document.Descendants("unittype").Select(e => e.Value));
        using var workspace = new TestWorkspace("reference-coverage-unknown");
        var path = workspace.WriteXml("unknown.xml", "<root><futureproto>Old</futureproto></root>");
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("Old", "New")]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
