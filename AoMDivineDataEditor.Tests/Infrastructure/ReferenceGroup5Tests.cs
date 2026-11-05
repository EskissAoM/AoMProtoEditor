using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceGroup5Tests
{
    [Theory]
    [InlineData("proto_mods.xml", "<protomods><unit name='U'><protoaction><name>Devote</name><devotionpower>Old</devotionpower><devotionpower>OldSuffix</devotionpower></protoaction></unit></protomods>")]
    [InlineData("owner.tactics", "<tactics><action><name>Devote</name><devotionpower>Old</devotionpower><devotionpower>OldSuffix</devotionpower></action></tactics>")]
    [InlineData("proto_unit_command_mods.xml", "<protounitcommands><protounitcommand><name>C</name><associatedpower>Old</associatedpower><future keep='yes'/></protounitcommand></protounitcommands>")]
    [InlineData("powers_mods.xml", "<powersmod><power name='Clone' type='PowerClone'><duplicatetargetreplacement><target>Old</target><target>Other</target></duplicatetargetreplacement></power><power name='Old' type='UnitAction'/></powersmod>")]
    public void PowerReferencesMigrateAndBlockDeletionWithoutRenamingDefinitions(string file, string xml)
    {
        using var workspace = new TestWorkspace("group5-power");
        var path = workspace.WriteText(file, xml);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var before = File.ReadAllBytes(path);
        var deletion = ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.Ability, "Old", workspace.RootPath);
        Assert.True(deletion.IsBlocked);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.Ability, [new("Old", "New")]);
        Assert.Single(plan.Paths);
        Assert.Single(plan.AffectedReferences);
        Assert.Equal(before, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target)));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace(">Old<", ">New<")), XDocument.Load(path)));
        Assert.False(ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.Ability, "Old", workspace.RootPath).IsBlocked);
        Assert.True(ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.Ability, "New", workspace.RootPath).IsBlocked);
    }

    [Theory]
    [InlineData("trainableunitreq")]
    [InlineData("protounit")]
    [InlineData("costprotounit")]
    public void CommandUnitFieldsIgnoreSameNameUnitTypes(string tag)
    {
        var xml = $"<protounitcommands><protounitcommand><name>C</name><{tag}>Old</{tag}></protounitcommand></protounitcommands>";
        var doc = XDocument.Parse(xml);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.UnitType, "Old", "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(doc, GameIdentifierKind.UnitType, "Old"));
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(doc, GameIdentifierKind.UnitType, "Old"));
        GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(doc, GameIdentifierKind.ProtoUnit, "Old", "New", ["Old", "New"], "command");
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.Equal("New", doc.Descendants(tag).Single().Value);
    }

    [Theory]
    [InlineData("<powersmod><power type='Unknown'><duplicatetargetreplacement><target>Old</target></duplicatetargetreplacement></power></powersmod>")]
    [InlineData("<powersmod><power type='PowerClone'><future><duplicatetargetreplacement><target>Old</target></duplicatetargetreplacement></future></power></powersmod>")]
    [InlineData("<powersmod xmlns='urn:extension'><power type='PowerClone'><duplicatetargetreplacement><target>Old</target></duplicatetargetreplacement></power></powersmod>")]
    [InlineData("<protomods><unit name='U'><future><devotionpower>Old</devotionpower></future></unit></protomods>")]
    [InlineData("<protounitcommands><protounitcommand><future><associatedpower>Old</associatedpower></future></protounitcommand></protounitcommands>")]
    public void UnknownPowerReferenceContextsRefuseBeforeWrites(string xml)
    {
        using var workspace = new TestWorkspace("group5-unknown");
        var path = workspace.WriteText("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.Ability, [new("Old", "New")]));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(ModDeletionSafety.InspectIdentifier(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.Ability, "Old", workspace.RootPath).IsBlocked);
    }

    [Fact]
    public void LocalPowerPreparationPreservesCloneDefinitionsAndOtherTargets()
    {
        var doc = XDocument.Parse("<powersmod><power name='Old' type='PowerClone'><duplicatetargetreplacement><target>Old</target><target>OldSuffix</target></duplicatetargetreplacement></power></powersmod>");
        PowerSavePreparation.Prepare(doc, [], [new("Old", "New")], new Dictionary<string, string>(), []);
        Assert.Equal("Old", (string?)doc.Root!.Element("power")!.Attribute("name"));
        Assert.Equal(["New", "OldSuffix"], doc.Descendants("target").Select(e => e.Value));
    }

    [Fact]
    public void BothSelectorsReuseCastPowerProviderAndItsCatalogMembership()
    {
        var source = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs"));
        Assert.Contains("PowerNames = GetTechnologyGodPowerNames(),", source, StringComparison.Ordinal);
        Assert.Contains("if (tag.Equals(\"devotionpower\", StringComparison.OrdinalIgnoreCase))\n            return GetTechnologyGodPowerNames().ToList();", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(editor, GetTechnologyGodPowerNames(), initialValue);", source, StringComparison.Ordinal);
        Assert.Contains("castPowerEditor,\n                        GetTechnologyGodPowerNames(),", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
        var names = ProtoEditorWindow.ExtractTechnologyGodPowerNames(
            [XDocument.Parse("<powers><power name='OriginalAction' type='UnitAction'/><power name='OriginalGod' type='Rain'/></powers>")],
            [XDocument.Parse("<powersmod><power name='CustomGod' type='Rain'/><power name='CustomGeneral' type='GeneralEffect' godpower=''/></powersmod>")]);
        Assert.Equal(["CustomGeneral", "CustomGod", "OriginalAction", "OriginalGod"], names);
    }
}
