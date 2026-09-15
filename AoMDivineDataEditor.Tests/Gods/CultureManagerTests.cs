using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class CultureManagerTests
{
    [Fact]
    public void Catalog_ExtractsFullCultureDefinitions()
    {
        var document = XDocument.Parse("""
            <cultures>
              <culture>
                <name>Greek</name>
                <displaynameid>STR_CULTURE_GREEKS</displaynameid>
                <compendiumimage>greek.png</compendiumimage>
                <compendiumtopic>greek</compendiumtopic>
                <replaypanelbackground>grk_vspanel_{variant}.png</replaypanelbackground>
              </culture>
            </cultures>
            """);

        var definition = Assert.Single(CultureCatalog.ExtractDefinitions(document, isBuiltIn: true));

        Assert.Equal("Greek", definition.Name);
        Assert.True(definition.IsBuiltIn);
        Assert.Equal("STR_CULTURE_GREEKS", CultureCatalog.ReadField(definition.SourceElement, "displaynameid"));
        Assert.Equal("grk_vspanel_{variant}.png", CultureCatalog.ReadField(definition.SourceElement, "replaypanelbackground"));
    }

    [Fact]
    public void Catalog_CreatesCopiesRenamesEditsAndDeletesCustomCultures()
    {
        var document = new XDocument(new XElement(CultureCatalog.ModRootName));
        var created = CultureCatalog.AddDefinition(document, "MyCulture");
        CultureCatalog.SetField(created, "compendiumtopic", "my_topic");
        var copy = CultureCatalog.AddDefinition(document, "MyCultureCopy", created);
        CultureCatalog.SetField(copy, "replaypanelbackground", "custom_panel.png");

        Assert.True(CultureCatalog.RenameDefinition(document, "MyCultureCopy", "RenamedCulture"));
        Assert.Equal("my_topic", CultureCatalog.ReadField(
            CultureCatalog.FindDefinition(document.Root!, "RenamedCulture")!, "compendiumtopic"));
        Assert.True(CultureCatalog.DeleteDefinition(document, "MyCulture"));
        Assert.Equal("RenamedCulture", Assert.Single(CultureCatalog.ExtractNames(document)));
    }

    [Fact]
    public void Catalog_Indents_fields_inside_custom_culture_entries()
    {
        var document = new XDocument(new XElement(CultureCatalog.ModRootName));

        CultureCatalog.AddDefinition(document, "MyCulture");
        var xml = document.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("<culture>\n\t\t<name>MyCulture</name>", xml, StringComparison.Ordinal);
        Assert.Contains("\n\t</culture>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_UsesOnlySingularCultureModsFileNameAndRoot()
    {
        Assert.Equal("culture_mods.xml", CultureCatalog.ModFileName);
        Assert.Equal("culturesmods", CultureCatalog.ModRootName);
        Assert.Null(typeof(CultureCatalog).GetField("AlternateModFileName"));
    }

    [Fact]
    public void CultureReferences_AreMigratedAndBlockDeletion()
    {
        var document = XDocument.Parse("""
            <root>
              <civ><name>MyGod</name><culture>MyCulture</culture></civ>
              <unit name="MyUnit"><icon culture="MyCulture">icon.png</icon></unit>
            </root>
            """);

        var locations = GameIdentifierReferencePolicy.FindDeletionReferenceLocations(
            document, GameIdentifierKind.Culture, "MyCulture");
        Assert.Equal(2, locations.Count(location => location.IsRecognized));
        Assert.Equal(2, GameIdentifierReferencePolicy.RenameReferences(
            document, GameIdentifierKind.Culture, "MyCulture", "RenamedCulture"));
        Assert.DoesNotContain("MyCulture", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CultureModDocumentSafety_RequiresExpectedRootAndUniqueNames()
    {
        var safe = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<culturesmods><culture><name>A</name></culture></culturesmods>"),
            ModXmlDocumentKind.Cultures);
        var duplicate = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<culturesmods><culture><name>A</name></culture><culture><name>a</name></culture></culturesmods>"),
            ModXmlDocumentKind.Cultures);
        var wrongRoot = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<cultures><culture><name>A</name></culture></cultures>"),
            ModXmlDocumentKind.Cultures);

        Assert.True(safe.IsSafeToEdit);
        Assert.False(duplicate.IsSafeToEdit);
        Assert.False(wrongRoot.IsSafeToEdit);
    }

    [Fact]
    public void GodsMenuAndCultureWindows_ExposeSharedManagerInteractionModel()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var manager = File.ReadAllText(Path.Combine(root, "Windows", "CultureManagerWindow.cs"));
        var editor = File.ReadAllText(Path.Combine(root, "Windows", "CultureEditorWindow.cs"));
        var host = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("Content=\"Cultures\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"GodsCultures_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new ManagerListShell", manager, StringComparison.Ordinal);
        Assert.Contains("Copy Culture", manager, StringComparison.Ordinal);
        Assert.Contains("View Culture", manager, StringComparison.Ordinal);
        Assert.Contains("Replay Panel Background", editor, StringComparison.Ordinal);
        Assert.Contains("SaveCultureStringValuesAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain("cultures_mods.xml", host, StringComparison.OrdinalIgnoreCase);
    }
}
