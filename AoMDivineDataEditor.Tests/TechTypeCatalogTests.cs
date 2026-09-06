using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechTypeCatalogTests
{
    [Fact]
    public void ExtractDefinitions_ReadsTechTypeValues()
    {
        var document = XDocument.Parse("""
            <techtypes>
              <techtype>HeroPromotion</techtype>
              <techtype>ResearchableCommand</techtype>
            </techtypes>
            """);

        var definitions = TechTypeCatalog.ExtractDefinitions(document, isBuiltIn: true);

        Assert.Equal(["HeroPromotion", "ResearchableCommand"], definitions.Select(item => item.Name));
        Assert.All(definitions, item => Assert.True(item.IsBuiltIn));
    }

    [Fact]
    public void ReadModFile_RequiresTechTypesModsRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aom-tech-types-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "tech_types_mods.xml");
            File.WriteAllText(path, "<techtypesmods><techtype>CustomType</techtype></techtypesmods>");
            Assert.Equal("CustomType", Assert.Single(TechTypeCatalog.ReadModFile(path)).Name);

            File.WriteAllText(path, "<techtypes><techtype>WrongRoot</techtype></techtypes>");
            Assert.Empty(TechTypeCatalog.ReadModFile(path));
            Assert.Throws<InvalidDataException>(() => TechTypeCatalog.LoadOrCreateModDocument(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CustomCrud_AddsRenamesAndDeletesValues()
    {
        var document = new XDocument(new XElement(TechTypeCatalog.ModRootName));

        var added = TechTypeCatalog.AddDefinition(document, "CustomType");
        Assert.Equal("techtype", added.Name.LocalName);
        Assert.Equal("CustomType", added.Value);
        Assert.True(TechTypeCatalog.RenameDefinition(document, "CustomType", "RenamedType"));
        Assert.Equal("RenamedType", Assert.Single(document.Root!.Elements()).Value);
        Assert.True(TechTypeCatalog.DeleteDefinition(document, "RenamedType"));
        Assert.Empty(document.Root.Elements());
    }

    [Theory]
    [InlineData("ValidType", true)]
    [InlineData("Valid_Type-2", true)]
    [InlineData("Has Space", false)]
    [InlineData("Invalid.Type", false)]
    [InlineData("", false)]
    public void TechTypeNamesUseInternalNameCharacters(string name, bool expected)
        => Assert.Equal(expected, TechTypeCatalog.IsValidName(name));

    [Fact]
    public void Merge_IsCaseInsensitiveAndKeepsOriginalDefinitionAuthoritative()
    {
        var original = new TechTypeDefinition("AgeUpgrade", true, new XElement("techtype", "AgeUpgrade"));
        var duplicateCustom = new TechTypeDefinition("ageupgrade", false, new XElement("techtype", "ageupgrade"));

        var definition = Assert.Single(TechTypeCatalog.Merge([original], [duplicateCustom]));

        Assert.True(definition.IsBuiltIn);
        Assert.Equal("AgeUpgrade", definition.Name);
    }

    [Fact]
    public void RenameAndDelete_ChangeOnlyDirectTechnologyAssignments()
    {
        var renameDocument = XDocument.Parse("""
            <techtreemods>
              <tech name="One"><techtype>CustomType</techtype><effects><techtype>CustomType</techtype></effects></tech>
              <tech name="Two"><techtype>customtype</techtype></tech>
              <metadata><techtype>CustomType</techtype></metadata>
            </techtreemods>
            """);

        Assert.Equal(2, TechTypeCatalog.RenameTechnologyAssignments(renameDocument, "CustomType", "RenamedType"));
        Assert.Equal(2, TechTypeCatalog.CountTechnologyUsage(renameDocument, "RenamedType"));
        Assert.Equal("CustomType", renameDocument.Root!.Element("metadata")!.Element("techtype")!.Value);
        Assert.Equal("CustomType", renameDocument.Root.Element("tech")!.Element("effects")!.Element("techtype")!.Value);

        Assert.Equal(2, TechTypeCatalog.RemoveTechnologyAssignments(renameDocument, "RenamedType"));
        Assert.DoesNotContain(renameDocument.Descendants("tech").SelectMany(tech => tech.Elements("techtype")), _ => true);
    }

    [Fact]
    public void UsageAndRename_IncludeEveryEditorBackedEffectReference()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="One">
                <techtype>CustomType</techtype>
                <effects>
                  <effect type="Data" subtype="ResearchPoints"><target type="TechType">customtype</target></effect>
                  <effect type="SetOnTechResearchedTech" techType="CustomType">OtherTech</effect>
                  <effect type="Data" subtype="Cost"><target type="techAll" excludetypes="AgeUpgrade|CUSTOMTYPE" /></effect>
                  <effect type="Data" subtype="ResearchPoints"><target type="Tech">CustomType</target></effect>
                </effects>
              </tech>
            </techtreemods>
            """);

        var usage = TechTypeCatalog.GetTechnologyUsage(document, "CustomType");

        Assert.Equal(1, usage.PropertyUsageCount);
        Assert.Equal(3, usage.EffectUsageCount);
        Assert.Equal(4, usage.TotalCount);
        Assert.Equal(3, TechTypeCatalog.RenameTechnologyEffectReferences(document, "CustomType", "RenamedType"));
        Assert.Equal(3, TechTypeCatalog.GetTechnologyUsage(document, "RenamedType").EffectUsageCount);
        Assert.Equal(0, TechTypeCatalog.GetTechnologyUsage(document, "CustomType").EffectUsageCount);
        Assert.Equal("AgeUpgrade|RenamedType", document.Descendants("target")
            .Single(target => (string?)target.Attribute("type") == "techAll")
            .Attribute("excludetypes")!.Value);
        Assert.Equal("CustomType", document.Descendants("target")
            .Single(target => (string?)target.Attribute("type") == "Tech")
            .Value);
    }

    [Fact]
    public void TechTypeWriter_UsesTechTypesModsRootAndIndentedEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aom-tech-type-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "tech_types_mods.xml");
            var document = new XDocument(new XElement(TechTypeCatalog.ModRootName));
            TechTypeCatalog.AddDefinition(document, "First");
            TechTypeCatalog.AddDefinition(document, "Second");

            ProtoEditorWindow.SaveTechTypeXmlDocument(document, path);

            var saved = XDocument.Load(path);
            Assert.Equal("techtypesmods", saved.Root!.Name.LocalName);
            Assert.Equal(["First", "Second"], saved.Root.Elements("techtype").Select(element => element.Value));
            Assert.Contains($"\t<techtype>Second</techtype>{Environment.NewLine}</techtypesmods>", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TechnologyMenuAndManager_MirrorUnitTypeManagerInteractionModel()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var managerCode = File.ReadAllText(Path.Combine(root, "Windows", "TechTypeManagerWindow.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Tech Types" &&
            (string?)element.Attribute("Click") == "TechnologyTechTypes_Click");
        Assert.Contains("await OpenTechTypeManagerAsync();", windowCode, StringComparison.Ordinal);
        Assert.Contains("new ManagerListShell", managerCode, StringComparison.Ordinal);
        Assert.Contains("if (!item.IsBuiltIn)", managerCode, StringComparison.Ordinal);
        Assert.Contains("item.Usage.EffectUsageCount > 0", managerCode, StringComparison.Ordinal);
        Assert.Contains("cannot be removed", managerCode, StringComparison.Ordinal);
        Assert.Contains("Data.bar · Used by", managerCode, StringComparison.Ordinal);
        Assert.Contains("RenameTechnologyEffectReferences", windowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicateButton", managerCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the project root.");
    }
}
