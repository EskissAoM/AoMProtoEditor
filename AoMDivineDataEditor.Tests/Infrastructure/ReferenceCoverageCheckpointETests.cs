using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

// Acceptance tests: failures record defects to fix, never an accepted baseline.
// No production files are used. Fixtures use the serialized shapes inventoried in D.
[Trait("Category", "ReferenceCoverageAudit")]
public sealed class ReferenceCoverageCheckpointETests(ITestOutputHelper output)
{
    private const string PowerMapTags = "areavfx attackproto burstvfx chargingvfx cloudvfxproto damagereceiverproto damagingproto decalproto dragonprotounit dustlargevfxproto dustmediumvfxproto dustsmallvfxproto earthmonsterprotounit effectproto embersproto farmproto gatebirthproto gateflameproto groundimpactvfxproto groundsparksvfxproto groundvfxproto gustproto healvfx icesheetvfxproto inportal inportalprotounit landimpactprotounit landprotounit largesfxprotoid largestrikeproto largevfxproto lureproto meteorprotounit outportal outportalprotounit placementvfx pseedsfarmtargetvfx pseedsfarmvfx rainprotounit rainvfxproto reappearunitvfx revealerproto rootedenemyvfx scorchvfxproto sfx shockwaveproto smallsfx smallsfxprotoid smallvfxproto snowdriftprotounit sparksvfxproto spawnproto spawnvfx sphereproto spiderdoorproto spidereggproto spiderproto spiderwarnproto spyprotounit startingvfx stormprotounit strikeproto swarmprotounit targetunitvfx tartariangateproto tornadoprotounit treeareavfx treevfx typhoonprotounit unitvfx vanishunitvfx vanishvfx vfxadditionalimpact vfxarea vfxcurseproto vfxeidolonspawnproto vfxforunitswithoutbirthanims vfxmaintarget vfxmaintargetimpact vfxmarkanim vfxproto vfxpulleffectproto vortexfinishlinkedproto vortexlandingproto vortexstartlinkedproto vortexstartproto wallproto watersplashprotounit waveprotounit wolfvfx";
    private static readonly GameIdentifierKind[] Mixed = [GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType];
    // Power types traced through the existing layout groups; shared global fields use TempUnit.
    private static readonly Dictionary<string, string> PowerTypes = new()
    {
        ["abstractattacktargettype"] = "TempUnit",
        ["abstractplacementtargettype"] = "TempUnit",
        ["abstractpowerresetsabilitychargetype"] = "TempUnit",
        ["abstracttype"] = "SwapUnit",
        ["areavfx"] = "Restoration",
        ["attachvfxto"] = "CeaseFire",
        ["attackproto"] = "DivineSlash",
        ["attacktarget"] = "Undermine",
        ["birthvfx"] = "UnitModify",
        ["burstvfx"] = "ThunderBurst",
        ["chargingvfx"] = "ThunderBurst",
        ["cloudvfxproto"] = "LightningStorm",
        ["createunit"] = "TempUnit",
        ["damagecap"] = "GreatFlood",
        ["damagereceiverproto"] = "ArcadianMeadow",
        ["damagingproto"] = "SmitingGust",
        ["decalproto"] = "DivineSlash",
        ["dragonprotounit"] = "Typhoon",
        ["duplicatetargetreplacement"] = "PowerClone",
        ["dustlargevfxproto"] = "Earthquake",
        ["dustmediumvfxproto"] = "Earthquake",
        ["dustsmallvfxproto"] = "Earthquake",
        ["earthmonsterprotounit"] = "EarthMonster",
        ["effect"] = "Rain",
        ["effectproto"] = "Valor",
        ["eidolontype"] = "UnderworldInvasion",
        ["embersproto"] = "ForestFire",
        ["excludefrompullingintofunnel"] = "Tornado",
        ["excludefromthrow"] = "Shockwave",
        ["explicitlyrestrictedattacktargettype"] = "TempUnit",
        ["explicitlyrestrictedplacementtargettype"] = "TempUnit",
        ["farmproto"] = "PSeeds",
        ["gatebirthproto"] = "TartarianGate",
        ["gateflameproto"] = "TartarianGate",
        ["groundimpactvfxproto"] = "Tempest",
        ["groundsparksvfxproto"] = "LightningStorm",
        ["groundvfxproto"] = "Tempest",
        ["gustproto"] = "SmitingGust",
        ["healvfx"] = "Restoration",
        ["icesheetvfxproto"] = "Tempest",
        ["initialdamage"] = "DroughtLand",
        ["inportal"] = "Teleport",
        ["inportalprotounit"] = "ShiftingSands",
        ["landimpactprotounit"] = "EarthMonster",
        ["landprotounit"] = "DroughtLand",
        ["largesfxprotoid"] = "Prosperity",
        ["largestrikeproto"] = "Tempest",
        ["largevfxproto"] = "Pestilence",
        ["lureproto"] = "Lure",
        ["mainpacktargettype"] = "Fimbulwinter",
        ["meteorprotounit"] = "Meteor",
        ["minipacktargettype"] = "Fimbulwinter",
        ["modelattachment"] = "NewMoon",
        ["onhiteffect"] = "EffectInArea",
        ["outportal"] = "Teleport",
        ["outportalprotounit"] = "ShiftingSands",
        ["placement"] = "TempUnit",
        ["placementrestriction"] = "TempUnit",
        ["placementvfx"] = "PowerClone",
        ["pseedsfarmtargetvfx"] = "PSeeds",
        ["pseedsfarmvfx"] = "PSeeds",
        ["rainprotounit"] = "Typhoon",
        ["rainspotproto"] = "Rain",
        ["rainvfxproto"] = "LightningStorm",
        ["reappearunitvfx"] = "Stealth",
        ["revealerproto"] = "TartarianGate",
        ["rooteddamage"] = "FavorForest",
        ["rootedenemyvfx"] = "FavorForest",
        ["scorchvfxproto"] = "Bolt",
        ["sfx"] = "Rain",
        ["shockwaveproto"] = "Implode",
        ["similarunittypes"] = "CageUnit",
        ["smallrainspotproto"] = "Rain",
        ["smallsfx"] = "Rain",
        ["smallsfxprotoid"] = "Prosperity",
        ["smallvfxproto"] = "Pestilence",
        ["snowdrift"] = "Fimbulwinter",
        ["snowdriftprotounit"] = "Frost",
        ["sparksvfxproto"] = "Bolt",
        ["spawnproto"] = "TartarianGate",
        ["spawnvfx"] = "Lure",
        ["sphereproto"] = "Implode",
        ["spiderdoorproto"] = "Spiders",
        ["spidereggproto"] = "Spiders",
        ["spiderproto"] = "Spiders",
        ["spiderwarnproto"] = "Spiders",
        ["spyprotounit"] = "Spy",
        ["startingvfx"] = "ThunderBurst",
        ["stormprotounit"] = "Frost",
        ["strikeproto"] = "Bolt",
        ["swarmprotounit"] = "Locust",
        ["targetunitvfx"] = "FoxCharm",
        ["tartariangateproto"] = "TartarianGate",
        ["terraincreep"] = "DroughtLand",
        ["tornadoprotounit"] = "Tornado",
        ["treeareavfx"] = "FavorForest",
        ["treevfx"] = "FavorForest",
        ["typhoonprotounit"] = "Typhoon",
        ["unitmodify"] = "UnitModify",
        ["unitvfx"] = "FoxCharm",
        ["vanishunitvfx"] = "Stealth",
        ["vanishvfx"] = "Stealth",
        ["vfxadditionalimpact"] = "CageUnit",
        ["vfxarea"] = "CageUnit",
        ["vfxcurseproto"] = "UnderworldInvasion",
        ["vfxeidolonspawnproto"] = "UnderworldInvasion",
        ["vfxforunitswithoutbirthanims"] = "UnderworldInvasion",
        ["vfxgrass"] = "ArcadianMeadow",
        ["vfxmaintarget"] = "CageUnit",
        ["vfxmaintargetimpact"] = "CageUnit",
        ["vfxmarkanim"] = "CageUnit",
        ["vfxproto"] = "Traitor",
        ["vfxpulleffectproto"] = "Implode",
        ["vortexfinishlinkedproto"] = "Vortex",
        ["vortexlandingproto"] = "Vortex",
        ["vortexstartlinkedproto"] = "Vortex",
        ["vortexstartproto"] = "Vortex",
        ["wallproto"] = "EarthWall",
        ["watersplashprotounit"] = "Meteor",
        ["waveprotounit"] = "DroughtLand",
        ["wolfproto"] = "Fimbulwinter",
        ["wolfvfx"] = "Fimbulwinter",
    };
    private static string Power(string fields)
    {
        var firstTag = XElement.Parse("<fields>" + fields + "</fields>").Elements().First().Name.LocalName;
        return $"<powersmod><power name='Owner' type='{PowerTypes[firstTag]}'>{fields}<future keep='yes'/></power></powersmod>";
    }
    private static string Unit(string fields) => $"<protomods><unit name='Owner'><protoaction><name>Action</name>{fields}</protoaction></unit></protomods>";
    private static string Tech(string fields) => $"<techtreemods><tech name='Owner'><effects>{fields}</effects></tech></techtreemods>";
    private static object[] Row(string id, GameIdentifierKind kind, string file, string xml) => [id, kind, file, xml];

    public static IEnumerable<object[]> References()
    {
        foreach (var tag in PowerMapTags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            yield return Row("RC11-" + tag, GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power($"<{tag}>Old</{tag}>"));
        foreach (var tag in new[] { "birthvfx", "modelattachment", "wolfproto", "vfxgrass", "snowdrift" })
            yield return Row("RC12-24-" + tag, GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power($"<{tag}>Old</{tag}>"));
        yield return Row("RC03-actual-swapto", GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power("<abstracttype swapto='Old' amount='2'>Other</abstracttype>"));
        foreach (var kind in Mixed)
        {
            foreach (var tag in new[] { "mainpacktargettype", "minipacktargettype", "attachvfxto", "eidolontype", "attacktarget", "abstracttype", "rooteddamage", "initialdamage", "excludefrompullingintofunnel", "excludefromthrow", "abstractplacementtargettype", "explicitlyrestrictedplacementtargettype", "abstractattacktargettype", "explicitlyrestrictedattacktargettype", "placementrestriction", "abstractpowerresetsabilitychargetype" })
                yield return Row("RC13-24-" + tag, kind, "powers_mods.xml", Power($"<{tag}>Old</{tag}><{tag}>Other</{tag}>"));
            foreach (var field in new[] { "<snowdrift target='Old'>Other</snowdrift>", "<damagecap unittype='Old' value='2'/>", "<placement restrictplacementnear='Old'>Full</placement>", "<placement overlaptype='Old'>Full</placement>", "<terraincreep preventplacement='Old'/>", "<unitmodify excludetypes='Other| Old |OldSuffix'>Damage</unitmodify>", "<onhiteffect type='InstantKillablePercentChance' byunittype='Old'/>", "<onhiteffect type='Stun'><target attacktype='Old'/></onhiteffect>", "<onhiteffect type='Stun'><target ignoretype='Old'/></onhiteffect>", "<effect type='DamageBonus' unittype='Old'><target type='ProtoUnit'>Other</target></effect>", "<effect type='OnHitEffectRate' targettype='Old'><target type='ProtoUnit'>Other</target></effect>" })
                yield return Row("RC17-22-25-" + field, kind, "powers_mods.xml", Power(field));
            foreach (var tag in new[] { "restrictempowertype", "modifyabstracttype", "modifyunittype", "modifydamagetargettype" })
                yield return Row("RC27-" + tag, kind, "proto_mods.xml", Unit($"<{tag}>Old</{tag}>"));
        }
        foreach (var tag in new[] { "rainspotproto", "smallrainspotproto" })
            yield return Row("RC14-" + tag, GameIdentifierKind.UnitType, "powers_mods.xml", Power($"<{tag}>Old</{tag}>"));
        yield return Row("RC24-los", GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power("<placement losprotounit='Old'>Full</placement>"));
        yield return Row("RC25-attach", GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power("<effect type='EnemyAttachProtoUnit' attachProtoUnit='Old'><target type='Player'/></effect>"));
        yield return Row("RC25-protoaction-source", GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power("<effect type='ProtoActionAdd' unittype='Old' protoaction='Attack'><target type='ProtoUnit'>Other</target></effect>"));
        yield return Row("RC23-powerclone", GameIdentifierKind.Ability, "powers_mods.xml", Power("<duplicatetargetreplacement><target>Old</target><target>Other</target></duplicatetargetreplacement>"));
        foreach (var tag in new[] { "splashvfxproto", "trailprotounit" })
        foreach (var file in new[] { "proto_mods.xml", "owner.tactics" })
            yield return Row("RC27-" + tag + "-" + file, GameIdentifierKind.ProtoUnit, file,
                file.EndsWith(".tactics") ? $"<tactics><action><name>Action</name><{tag}>Old</{tag}></action></tactics>" : Unit($"<{tag}>Old</{tag}>"));
        yield return Row("RC27-srctype", GameIdentifierKind.ProtoUnit, "proto_mods.xml", Unit("<conversionprotoid srctype='Old'>Other</conversionprotoid>"));
        yield return Row("RC27-modifyciv", GameIdentifierKind.MajorGod, "proto_mods.xml", Unit("<modifyciv>Old</modifyciv>"));
        foreach (var tag in new[] { "trainableunitreq", "protounit", "costprotounit", "associatedpower" })
            yield return Row("RC28-" + tag, tag == "associatedpower" ? GameIdentifierKind.Ability : GameIdentifierKind.ProtoUnit,
                "proto_unit_command_mods.xml", $"<protounitcommands><protounitcommand><name>Owner</name><{tag}>Old</{tag}></protounitcommand></protounitcommands>");
        yield return Row("RC29-generator", GameIdentifierKind.UnitType, "techtree_mods.xml", Tech("<effect type='CreateUnit' unit='Other' generator='Old'/>"));
        // Positive producer-shaped controls, deliberately retained alongside failures.
        yield return Row("control-createunit", GameIdentifierKind.ProtoUnit, "powers_mods.xml", Power("<createunit quantity='2'>Other| Old |OldSuffix</createunit>"));
        yield return Row("control-cage", GameIdentifierKind.UnitType, "powers_mods.xml", Power("<similarunittypes><unittype>Old</unittype><unittype>Other</unittype></similarunittypes>"));
    }

    [Theory, MemberData(nameof(References))]
    public void ProducerReferenceMustMigrate(string id, GameIdentifierKind kind, string file, string xml)
    {
        using var workspace = new TestWorkspace("coverage-e-rename");
        var path = workspace.WriteXml(file, xml);
        var before = File.ReadAllBytes(path);
        GameIdentifierReferenceMigrationPlan? plan = null;
        var error = Record.Exception(() => plan = GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: []));
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"E|{id}|{kind}|rename|{(error != null ? "REFUSED" : plan!.Paths.Count == 0 ? "OMITTED" : "PREPARED")}|{error?.Message}");
        Assert.True(error == null, error?.Message);
        Assert.Single(plan!.Paths);
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((document, target) => PreservedXmlFile.SaveAtomic(document, target)));
        var expected = xml.Replace("Old", "New", StringComparison.Ordinal).Replace("NewSuffix", "OldSuffix", StringComparison.Ordinal);
        Assert.True(XNode.DeepEquals(XDocument.Parse(expected), XDocument.Load(path)), "Only intended references may change.");
    }

    [Theory, MemberData(nameof(References))]
    public void DeletionMustSeeDependencyAndSubstringMustRemainUnchanged(string id, GameIdentifierKind kind, string file, string xml)
    {
        using var workspace = new TestWorkspace("coverage-e-delete");
        var path = workspace.WriteXml(file, xml);
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var blocked = ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath).IsBlocked;
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"E|{id}|{kind}|deletion|{(blocked ? "BLOCKED" : "MISSED")}");
        // Run negative controls even when the dependency check will fail.
        File.WriteAllText(path, xml.Replace("Old", "OldSuffix", StringComparison.Ordinal));
        var negativeBytes = File.ReadAllBytes(path);
        Assert.False(ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath).IsBlocked);
        Assert.Empty(GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: []).Paths);
        Assert.Equal(negativeBytes, File.ReadAllBytes(path));
        Assert.True(blocked, "A genuine dependency must prevent deletion.");
    }

    public static IEnumerable<object[]> WrongKinds()
    {
        yield return Row("RC22-techtype", GameIdentifierKind.TechType, "powers_mods.xml", Power("<unitmodify excludetypes='Other|Old|OldSuffix'>Damage</unitmodify>"));
        yield return Row("RC18-reward-resource", GameIdentifierKind.UnitType, "powers_mods.xml", Power("<onhiteffect type='KillReward' rewardtype='Food'/>"));
        foreach (var kind in Mixed)
            yield return Row("RC25-workrate-resource", kind, "techtree_mods.xml", Tech("<effect type='Data' subtype='WorkRate' unittype='Food'><target type='ProtoUnit'>Other</target></effect>"));
        yield return Row("RC29-unit-only", GameIdentifierKind.UnitType, "techtree_mods.xml", Tech("<effect type='CreateUnit' unit='Old' generator='Other'/>"));
        yield return Row("RC30-chainable", GameIdentifierKind.UnitType, "majorgods_mods.xml", "<civs><civ><name>Owner</name><buildingchain><chainablebuilding>Old</chainablebuilding></buildingchain></civ></civs>");
        yield return Row("RC31-internal-tactic-tech", GameIdentifierKind.Tactics, "techtree_mods.xml", Tech("<effect type='Data' subtype='TacticEnable' tactic='Old'><target type='ProtoUnit'>Other</target></effect>"));
        yield return Row("RC31-internal-tactic-power", GameIdentifierKind.Tactics, "powers_mods.xml", Power("<effect type='TacticEnable' tactic='Old'><target type='ProtoUnit'>Other</target></effect>"));
    }

    [Theory, MemberData(nameof(WrongKinds))]
    public void UnrelatedIdentityMustNotBeRewritten(string id, GameIdentifierKind kind, string file, string xml)
    {
        using var workspace = new TestWorkspace("coverage-e-wrong-kind");
        var path = workspace.WriteXml(file, xml);
        var before = File.ReadAllBytes(path);
        var oldName = xml.Contains("Food", StringComparison.Ordinal) ? "Food" : "Old";
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new(oldName, "New")], oppositeUnitKindNames: []);
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"E|{id}|{kind}|wrong-kind|{(plan.Paths.Count == 0 ? "PRESERVED" : "WRONG_WRITE")}");
        Assert.Empty(plan.Paths); // Deliberately never commit an incorrect candidate.
    }

    [Theory]
    [InlineData("Old")]
    [InlineData("New")]
    public void MixedTechnologyTargetMustRefuseOppositeKindCollision(string collision)
    {
        using var workspace = new TestWorkspace("coverage-e-collision");
        var path = workspace.WriteXml("techtree_mods.xml", Tech("<effect type='Data' subtype='Damage'><target type='ProtoUnit'>Old</target></effect>"));
        var before = File.ReadAllBytes(path);
        var error = Record.Exception(() => GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: [collision]));
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"E|RC19-{collision}|ProtoUnit|collision|{(error == null ? "NOT_REFUSED" : "REFUSED")}");
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData("Cost")]
    [InlineData("CommandAdd")]
    [InlineData("CommandRemove")]
    public void TechnologyReferencesMustBeIndexedAndMigrated(string type)
    {
        using var workspace = new TestWorkspace("coverage-e-tech");
        var power = type == "Cost";
        var xml = power ? Power("<effect type='Cost'><target type='Tech'>Old</target></effect>") : Tech($"<effect type='Data' subtype='{type}' tech='Old'><target type='ProtoUnit'>Other</target></effect>");
        var path = workspace.WriteXml(power ? "powers_mods.xml" : "techtree_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var source = new TechnologyReferenceSource("Owner", XDocument.Parse(xml), power ? TechnologyReferenceDocumentKind.Powers : TechnologyReferenceDocumentKind.Techtree, path, CanMigrate: true);
        var references = TechnologyReferenceIndex.FindReferencesTo([source], "Old");
        var plan = TechnologyReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), [new("Old", "New")]);
        Assert.Equal(before, File.ReadAllBytes(path));
        output.WriteLine($"E|RC15-16-{type}|Technology|index|references={references.Count};unknown={plan.UnknownReferences.Count};writes={plan.Paths.Count}");
        Assert.NotEmpty(references);
        Assert.Empty(plan.UnknownReferences);
        Assert.Single(plan.Paths);
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("Old", "New", StringComparison.Ordinal)), plan.GetDocument(path)));
    }

    [Fact]
    public void PowerCloneLocalSaveMustUpdateItsPowerReference()
    {
        var xml = Power("<duplicatetargetreplacement><target>Old</target></duplicatetargetreplacement>");
        var document = XDocument.Parse(xml);
        PowerSavePreparation.Prepare(document, [], [new("Old", "New")], new Dictionary<string, string>(), []);
        Assert.Equal("New", document.Descendants("target").Single().Value);
    }
}
