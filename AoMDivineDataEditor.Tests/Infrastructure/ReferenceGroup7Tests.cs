using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceGroup7Tests
{
    [Theory]
    [InlineData("unittransforms", "from")]
    [InlineData("unittransforms", "to")]
    [InlineData("unittransforms", "revertothersto")]
    [InlineData("unittransformmods", "from")]
    [InlineData("unittransformmods", "to")]
    [InlineData("unittransformmods", "revertothersto")]
    public void TransformUnitReferencesMigrateWithoutChangingOtherIdentities(string root, string field)
    {
        using var workspace = new TestWorkspace("group7-fields");
        var definition = new ProtoUnitTransformDefinition(XElement.Parse("<transform fullheal='true' extra='preserve'><!--keep--><future>Other</future></transform>"))
        { From = "Source", To = "Target", RevertOthersTo = "Revert", Command = "Old", Tech = "Old", FullHeal = true };
        var document = new XDocument(new XElement(root, definition.ToElement()));
        document.Descendants(field).Single().Value = "Old";
        var path = workspace.WriteText("unit_transform_mods.xml", document.ToString());
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var before = File.ReadAllBytes(path);
        Assert.Contains(ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.ProtoUnit, "Old", workspace.RootPath)
            .References, reference => reference.IsRecognized);
        // These fields are concrete Units, even if a Unit Type has the same spelling.
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(new XDocument(document), GameIdentifierKind.UnitType, "Old", "New"));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: ["Old", "New"]);
        Assert.Single(plan.AffectedReferences);
        Assert.Equal(before, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target)));
        document.Descendants(field).Single().Value = "New";
        Assert.True(XNode.DeepEquals(document, XDocument.Load(path)));
    }

    [Theory]
    [InlineData("<root><transform><from>Old</from></transform></root>")]
    [InlineData("<unittransformmods><future><transform><to>Old</to></transform></future></unittransformmods>")]
    [InlineData("<unittransformmods><transform><future><revertothersto>Old</revertothersto></future></transform></unittransformmods>")]
    [InlineData("<unittransformmods xmlns='urn:extension'><transform><from>Old</from></transform></unittransformmods>")]
    public void UnsupportedTransformContextsRefuseBeforeWrites(string xml)
    {
        using var workspace = new TestWorkspace("group7-context");
        var path = workspace.WriteText("unit_transform_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("Old", "New")]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void OrdinaryFromAndToTextDoNotBecomeUnitReferences()
    {
        var doc = XDocument.Parse("<root><from>Old</from><to>Old</to><revertothersto>Old</revertothersto></root>");
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(doc, GameIdentifierKind.ProtoUnit, "Old"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPlanAfterOwnedLifecyclePreservesBundlesAndRollsBackTogether(bool failSave)
    {
        using var workspace = new TestWorkspace("group7-bundle");
        var unit = XElement.Parse("<unit name='New'><command>OldToTarget</command></unit>");
        var commands = XDocument.Parse("<protounitcommandmods><protounitcommand><name>OldToTarget</name><associatedtech>OldToTarget</associatedtech></protounitcommand></protounitcommandmods>");
        var technologies = XDocument.Parse("<techtreemods><tech name='OldToTarget'><researchpoints>5</researchpoints></tech></techtreemods>");
        var transforms = XDocument.Parse("""
            <unittransformmods>
              <transform><from>Old</from><to>Target</to><command>OldToTarget</command><revertothersto>Old</revertothersto></transform>
              <transform><from>Other</from><to>Old</to><command>OtherToOld</command></transform>
              <transform><from>Old</from><to>OldSuffix</to><command>Independent</command><tech>Unrelated</tech></transform>
            </unittransformmods>
            """);
        var commandPath = workspace.WriteText("proto_unit_command_mods.xml", commands.ToString());
        var techPath = workspace.WriteText("techtree_mods.xml", technologies.ToString());
        var transformPath = workspace.WriteText("unit_transform_mods.xml", transforms.ToString());
        var paths = new[] { commandPath, techPath, transformPath };
        var original = paths.ToDictionary(path => path, File.ReadAllBytes);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var preflight = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit,
            [new("Old", "New")], discovery: discovery);
        Assert.Single(preflight.Paths);
        Assert.Equal(4, preflight.AffectedReferences.Count);
        GameIdentifierReferenceMigrationPlan? migration = null;
        var error = Record.Exception(() => FileIntegrityTransaction.Execute(paths, preflight.ExpectedStates, () =>
        {
            preflight.ValidateDiscovery();
            Assert.Single(TransformCommandBundleLifecycle.RenameUnit(unit, "Old", "New", commands, transforms, technologies));
            PreservedXmlFile.SaveAtomic(commands, commandPath);
            PreservedXmlFile.SaveAtomic(technologies, techPath);
            PreservedXmlFile.SaveAtomic(transforms, transformPath);
            // Match the host: rebuild against the bundle lifecycle's saved documents.
            migration = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("Old", "New")]);
            Assert.Equal(3, migration.AffectedReferences.Count);
            migration.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path));
            if (failSave) throw new IOException("Injected failure after migration");
        }));
        if (failSave)
        {
            Assert.IsType<IOException>(error);
            foreach (var path in paths) Assert.Equal(original[path], File.ReadAllBytes(path));
            return;
        }
        Assert.Null(error);
        var savedTransforms = XDocument.Load(transformPath);
        Assert.Equal(["New", "Other", "New"], savedTransforms.Descendants("from").Select(e => e.Value));
        Assert.Equal(["Target", "New", "OldSuffix"], savedTransforms.Descendants("to").Select(e => e.Value));
        Assert.Equal("New", savedTransforms.Descendants("revertothersto").Single().Value);
        Assert.Equal(["NewToTarget", "OtherToOld", "Independent"], savedTransforms.Descendants("command").Select(e => e.Value));
        Assert.Equal("NewToTarget", unit.Element("command")!.Value);
        Assert.Equal("NewToTarget", XDocument.Load(commandPath).Descendants("name").Single().Value);
        Assert.Equal("NewToTarget", XDocument.Load(commandPath).Descendants("associatedtech").Single().Value);
        Assert.Equal("NewToTarget", (string?)XDocument.Load(techPath).Descendants("tech").Single().Attribute("name"));
        Assert.Empty(GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("Old", "New")]).Paths);
    }

    [Fact]
    public void HostRebuildsSharedMigrationAfterOwnedBundleSave()
    {
        var source = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs"));
        var bundleSave = source.IndexOf("SavePendingOwnedTransformBundleUnitRenames(referenceFiles,", StringComparison.Ordinal);
        var migration = source.IndexOf("identifierMigration = GameIdentifierReferenceMigrationPlan.Prepare(", bundleSave, StringComparison.Ordinal);
        Assert.True(bundleSave > 0 && migration > bundleSave);
    }
}
