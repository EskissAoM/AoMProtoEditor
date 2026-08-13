using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class UnitTypeCatalogTests
{
    [Fact]
    public void ExtractDefinitions_ReadsBaseEntriesAndPreservesAttributes()
    {
        var document = XDocument.Parse("""
            <abstractunittypes>
              <abstractunittype spatialmapped="true">AbstractArcher</abstractunittype>
              <abstractunittype>Building</abstractunittype>
            </abstractunittypes>
            """);

        var definitions = UnitTypeCatalog.ExtractDefinitions(document, isBuiltIn: true);

        Assert.Equal(["AbstractArcher", "Building"], definitions.Select(item => item.Name));
        Assert.All(definitions, item => Assert.True(item.IsBuiltIn));
        Assert.Equal("true", (string?)definitions[0].SourceElement.Attribute("spatialmapped"));
    }

    [Fact]
    public void ReadModFile_RequiresAbstractUnitTypesModsRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aom-unit-types-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var validPath = Path.Combine(directory, "abstract_unit_types_mods.xml");
            File.WriteAllText(validPath, "<abstractunittypesmods><abstractunittype>CustomType</abstractunittype></abstractunittypesmods>");
            Assert.Equal("CustomType", Assert.Single(UnitTypeCatalog.ReadModFile(validPath)).Name);

            File.WriteAllText(validPath, "<abstractunittypes><abstractunittype>WrongRoot</abstractunittype></abstractunittypes>");
            Assert.Empty(UnitTypeCatalog.ReadModFile(validPath));
            Assert.Throws<InvalidDataException>(() => UnitTypeCatalog.LoadOrCreateModDocument(validPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CustomCrud_PreservesDuplicatedEntryAttributes()
    {
        var document = new XDocument(new XElement(UnitTypeCatalog.ModRootName));
        var template = new XElement(UnitTypeCatalog.EntryName,
            new XAttribute("spatialmapped", "true"), "OriginalType");

        var added = UnitTypeCatalog.AddDefinition(document, "CustomType", template);
        Assert.Equal("true", (string?)added.Attribute("spatialmapped"));
        Assert.Equal("CustomType", added.Value);

        Assert.True(UnitTypeCatalog.RenameDefinition(document, "CustomType", "RenamedType"));
        Assert.Equal("RenamedType", Assert.Single(document.Root!.Elements()).Value);
        Assert.True(UnitTypeCatalog.DeleteDefinition(document, "RenamedType"));
        Assert.Empty(document.Root.Elements());
    }

    [Theory]
    [InlineData("ValidType", true)]
    [InlineData("Valid_Type-2", true)]
    [InlineData("Has Space", false)]
    [InlineData("Invalid.Type", false)]
    [InlineData("", false)]
    public void UnitTypeNamesUseInternalNameCharacters(string name, bool expected)
        => Assert.Equal(expected, UnitTypeCatalog.IsValidName(name));

    [Fact]
    public void Merge_IsCaseInsensitiveAndKeepsBaseDefinitionAuthoritative()
    {
        var baseDefinition = new UnitTypeDefinition("Building", true,
            new XElement(UnitTypeCatalog.EntryName, new XAttribute("spatialmapped", "true"), "Building"));
        var duplicateCustom = new UnitTypeDefinition("building", false,
            new XElement(UnitTypeCatalog.EntryName, "building"));

        var merged = UnitTypeCatalog.Merge([baseDefinition], [duplicateCustom]);

        var definition = Assert.Single(merged);
        Assert.True(definition.IsBuiltIn);
        Assert.Equal("true", (string?)definition.SourceElement.Attribute("spatialmapped"));
    }

    [Fact]
    public void ProtoUsage_AddsArbitraryBuiltInUnitTypesWithoutReadingProtoMods()
    {
        var proto = XDocument.Parse("""
            <proto>
              <unit name="One"><unittype>Unit</unittype><unittype>ArbitraryType</unittype></unit>
              <unit name="Two"><unittype>unit</unittype></unit>
              <metadata><unittype>IgnoredMetadataType</unittype></metadata>
            </proto>
            """);

        var definitions = UnitTypeCatalog.ExtractUsedDefinitionsFromProto(proto);

        Assert.Equal(["ArbitraryType", "Unit"], definitions.Select(item => item.Name));
        Assert.All(definitions, item => Assert.True(item.IsBuiltIn));
    }

    [Fact]
    public void Delete_RemovesEveryMatchingDirectUnitTypeAssignmentOnly()
    {
        var document = XDocument.Parse("""
            <proto>
              <unit name="One"><unittype>CustomType</unittype><unittype>Building</unittype></unit>
              <unit name="Two"><unittype>customtype</unittype></unit>
              <metadata><unittype>CustomType</unittype></metadata>
            </proto>
            """);

        var removed = UnitTypeCatalog.RemoveUnitAssignments(document, "CustomType");

        Assert.Equal(2, removed);
        Assert.Equal("Building", Assert.Single(document.Root!.Element("unit")!.Elements("unittype")).Value);
        Assert.Equal("CustomType", document.Root.Element("metadata")!.Element("unittype")!.Value);
    }

    [Fact]
    public void UnitTypeWriter_NormalizesPreservedWhitespaceAndIndentsClosingRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aom-unit-type-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "abstract_unit_types_mods.xml");
            var document = XDocument.Parse(
                "<abstractunittypesmods>\n\t<abstractunittype>First</abstractunittype></abstractunittypesmods>",
                LoadOptions.PreserveWhitespace);
            UnitTypeCatalog.AddDefinition(document, "Second");

            ProtoEditorWindow.SaveUnitTypeXmlDocument(document, path);

            var text = File.ReadAllText(path);
            Assert.Contains($"\t<abstractunittype>First</abstractunittype>{Environment.NewLine}", text);
            Assert.Contains($"\t<abstractunittype>Second</abstractunittype>{Environment.NewLine}</abstractunittypesmods>", text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
