using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

// Consumer register A02/A03/A07/T01. Derive the typed-field set from the real
// action metadata so adding another typed reference cannot silently omit rename.
public sealed class CatalogActionReferenceContractTests
{
    public static IEnumerable<object[]> TypedFields()
        => ProtoActionMetadataCatalog.GetKnownFieldDefinitions()
            .Where(field => field.XmlAttributeNames?.Contains("type", StringComparer.OrdinalIgnoreCase) == true &&
                field.Tag is not ("damage" or "conditionaltransformrule"))
            .SelectMany(field => new[] { false, true }.SelectMany(tactics =>
                new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType }
                    .Select(kind => new object[] { field.Tag, tactics, kind })));

    public static IEnumerable<object[]> UnitFieldTags()
        => TypedFields().Select(row => (string)row[0]).Distinct().Select(tag => new object[] { tag });

    private static XDocument Serialize(string tag, bool tactics, string value)
    {
        var action = new ProtoAction { Name = "Work", Type = "Convert" };
        var row = new ProtoActionStructuredFieldEntry { Value = tag == "typedanim" ? "UnchangedAnimation" : "2" };
        row.Attributes["type"] = value;
        ProtoXmlHandler.SetProtoActionStructuredFieldEntries(action, tag, [row]);
        var unit = new XElement("unit", new XAttribute("name", "Owner"));
        ProtoXmlHandler.SetProtoActions(unit, [action]);
        var definition = unit.Element("protoaction")!;
        definition.Add(new XComment("preserve source comment"),
            new XElement("future", new XAttribute("keep", "OldSuffix"), "OldSuffix"));
        if (!tactics) return new XDocument(new XElement("protomods", unit));
        definition.Remove(); definition.Name = "action";
        return new XDocument(new XElement("tactics", definition));
    }

    [Fact]
    public void MetadataTypedFieldChoicesComeFromTheMixedHostCatalog()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_cachedBuildLimitTargets", new List<string> { "ConcreteChoice", "AbstractChoice" });
                var tags = TypedFields().Select(row => (string)row[0]).Distinct().ToArray();
                Assert.NotEmpty(tags);
                foreach (var tag in tags)
                {
                    Assert.True(ProtoActionMetadataCatalog.GetFieldDefinition(tag).HasUnitTargetTypeAttribute, tag);
                    var choices = Assert.IsType<List<string>>(host.Call("GetProtoActionStructuredAttributeSuggestions", tag, "type"));
                    Assert.Equal(new[] { "ConcreteChoice", "AbstractChoice" }, choices);
                }
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData("Trade", "minrate")]
    [InlineData("ModifyGather", "rate")]
    public void ResourceModesCannotBecomeUnitReferences(string actionType, string tag)
    {
        foreach (var tactics in new[] { false, true })
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            var document = Serialize(tag, tactics, "Food");
            document.Descendants("type").Single().Value = actionType;
            var original = new XDocument(document);
            Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, kind, "Food"));
            Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, kind, "Food", "RenamedUnit"));
            Assert.Empty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, kind, "Food"));
            Assert.True(XNode.DeepEquals(original, document));
        }
    }

    [Theory, MemberData(nameof(TypedFields))]
    public void SerializedTypedReferenceMigratesAndReopensWithoutSubstringChanges(string tag, bool tactics, GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace(nameof(CatalogActionReferenceContractTests));
        var document = Serialize(tag, tactics, "Old");
        var path = workspace.WriteXml(tactics ? "custom.tactics" : "proto_mods.xml", document.ToString());
        var bytes = File.ReadAllBytes(path);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            kind, [new("Old", "New")], oppositeUnitKindNames: []);
        Assert.Single(plan.Paths);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((candidate, target) => PreservedXmlFile.SaveAtomic(candidate, target)));
        var reopened = XDocument.Load(path);
        var expected = new XDocument(document);
        expected.Descendants(tag).Single().SetAttributeValue("type", "New");
        Assert.True(XNode.DeepEquals(expected, reopened), tag);
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(reopened, kind, "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(reopened, kind, "Old"));
    }

    [Theory]
    [InlineData("Trade", "minrate")]
    [InlineData("ModifyGather", "rate")]
    public void NamespacedModeCannotOverrideThePlainActionDomain(string extensionMode, string tag)
    {
        foreach (var tactics in new[] { false, true })
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            var document = Serialize(tag, tactics, "Old");
            var action = document.Descendants(tactics ? "action" : "protoaction").Single();
            var extension = new XElement(XName.Get("type", "urn:extension"), extensionMode);
            action.AddFirst(extension);
            Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, kind, "Old", "New"));
            Assert.Equal("New", document.Descendants(tag).Single().Attribute("type")!.Value);
            Assert.Equal(extensionMode, extension.Value);
            Assert.Equal("Convert", action.Element("type")!.Value);
        }
    }

    [Theory, MemberData(nameof(TypedFields))]
    public void TypedReferenceRetainsMixedIdentityAndExtensionGuards(string tag, bool tactics, GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("catalog-action-identity");
        var document = Serialize(tag, tactics, "Old");
        var path = workspace.WriteXml(tactics ? "custom.tactics" : "proto_mods.xml", document.ToString());
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        foreach (string[]? opposite in new string[]?[] { null, ["Old"], ["New"] })
            Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
                files, kind, [new("Old", "New")], oppositeUnitKindNames: opposite));
        var extension = new XDocument(document);
        extension.Root!.Name = XName.Get(extension.Root.Name.LocalName, "urn:extension");
        var original = new XDocument(extension);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(extension, kind, "Old", "New"));
        Assert.True(XNode.DeepEquals(original, extension));
    }
}
