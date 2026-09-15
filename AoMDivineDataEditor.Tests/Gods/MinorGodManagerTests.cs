using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class MinorGodManagerTests
{
    [Fact]
    public void Extract_definitions_uses_age_tech_element_and_displays_god_suffix()
    {
        var document = XDocument.Parse("""
            <minorgods>
              <classicalageathena portraitfilename="resources\athena_portrait.png"
                                    portraiticonfilename="resources\athena_icon.png" />
              <mythicageInariOkami />
            </minorgods>
            """);

        var definitions = MinorGodCatalog.ExtractDefinitions(document.Root!, isBuiltIn: true);

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, item => item.Name == "athena" && item.TechName == "classicalageathena");
        Assert.Contains(definitions, item => item.Name == "InariOkami" && item.TechName == "mythicageInariOkami");
    }

    [Fact]
    public void Canonicalize_restores_technology_casing_and_display_name()
    {
        var source = MinorGodCatalog.ExtractDefinitions(
            XDocument.Parse("<minorgods><classicalageathena /></minorgods>").Root!, true);

        var result = MinorGodCatalog.Canonicalize(source, ["ClassicalAgeAthena"]);

        Assert.Equal("Athena", result[0].Name);
        Assert.Equal("ClassicalAgeAthena", result[0].TechName);
        Assert.Equal("ClassicalAgeAthena", result[0].SourceElement.Name.LocalName);
    }

    [Fact]
    public void Technology_selector_catalog_contains_only_age_upgrade_technologies()
    {
        var documents = new[]
        {
            XDocument.Parse("""
                <techtree>
                  <tech name="ClassicalAgeAthena"><flag>AgeUpgrade</flag></tech>
                  <tech name="AthenaBonus"><flag>Upgrade</flag></tech>
                  <tech name="HeroicAgeApollo"><flag>ageupgrade</flag></tech>
                </techtree>
                """)
        };

        var names = MinorGodCatalog.ExtractAgeUpgradeTechnologyNames(documents);

        Assert.Equal(["ClassicalAgeAthena", "HeroicAgeApollo"], names);
    }

    [Fact]
    public void Saving_custom_age_upgrade_technologies_adds_missing_minor_gods_once()
    {
        var minorGods = XDocument.Parse("<minorgodsmods><classicalageexisting /></minorgodsmods>");
        var technologies = XDocument.Parse("""
            <techtreemods>
              <tech name="ClassicalAgeExisting"><flag>AgeUpgrade</flag></tech>
              <tech name="HeroicAgeExampleGod"><flag>AgeUpgrade</flag></tech>
              <tech name="OrdinaryTech"><flag>AgeUpgrade</flag></tech>
            </techtreemods>
            """);

        var added = MinorGodCatalog.AddMissingDefinitionsForAgeUpgradeTechnologies(minorGods, [technologies]);

        Assert.Equal(1, added);
        Assert.NotNull(MinorGodCatalog.FindDefinition(minorGods.Root!, "HeroicAgeExampleGod"));
        Assert.Equal(2, minorGods.Root!.Elements().Count());
        Assert.Equal(0, MinorGodCatalog.AddMissingDefinitionsForAgeUpgradeTechnologies(minorGods, [technologies]));
    }

    [Fact]
    public void Technology_lifecycle_renames_the_linked_minor_god_and_preserves_its_payload_and_comments()
    {
        var minorGods = XDocument.Parse("""
            <minorgodsmods>
              <!-- keep this note -->
              <classicalageoldgod future="yes" portraitfilename="portrait.png">
                <futuredata>42</futuredata>
              </classicalageoldgod>
            </minorgodsmods>
            """, LoadOptions.PreserveWhitespace);
        var technologies = XDocument.Parse("""
            <techtreemods>
              <tech name="ClassicalAgeNewGod"><flag>AgeUpgrade</flag></tech>
            </techtreemods>
            """);

        var changed = MinorGodCatalog.ApplyTechnologyLifecycle(
            minorGods,
            technologies,
            [new TechnologyRenameOperation("ClassicalAgeOldGod", "ClassicalAgeNewGod")],
            []);

        Assert.Equal(1, changed);
        Assert.Null(MinorGodCatalog.FindDefinition(minorGods.Root!, "ClassicalAgeOldGod"));
        var renamed = Assert.IsType<XElement>(
            MinorGodCatalog.FindDefinition(minorGods.Root!, "ClassicalAgeNewGod"));
        Assert.Equal("yes", (string?)renamed.Attribute("future"));
        Assert.Equal("42", renamed.Element("futuredata")?.Value);
        Assert.Contains(minorGods.Root!.Nodes().OfType<XComment>(), comment => comment.Value.Contains("keep this note"));
    }

    [Fact]
    public void Technology_lifecycle_removes_a_minor_god_when_its_technology_or_age_flag_is_removed()
    {
        var minorGods = XDocument.Parse("""
            <minorgodsmods>
              <heroicagegone portraitfilename="gone.png" />
              <mythicagekept portraitfilename="kept.png" />
            </minorgodsmods>
            """, LoadOptions.PreserveWhitespace);
        var technologies = XDocument.Parse("""
            <techtreemods>
              <tech name="HeroicAgeGone" />
              <tech name="MythicAgeKept"><flag>AgeUpgrade</flag></tech>
            </techtreemods>
            """);

        var changed = MinorGodCatalog.ApplyTechnologyLifecycle(
            minorGods,
            technologies,
            [],
            ["HeroicAgeGone"]);

        Assert.Equal(1, changed);
        Assert.Null(MinorGodCatalog.FindDefinition(minorGods.Root!, "HeroicAgeGone"));
        Assert.NotNull(MinorGodCatalog.FindDefinition(minorGods.Root!, "MythicAgeKept"));
    }

    [Fact]
    public void Add_replace_and_delete_preserve_unknown_minor_god_data()
    {
        var document = XDocument.Parse("<minorgodsmods />", LoadOptions.PreserveWhitespace);
        var template = XElement.Parse("""
            <classicalageathena future="yes" portraitfilename="old.png"
                                      portraiticonfilename="icon.png"><futuredata>42</futuredata></classicalageathena>
            """);

        MinorGodCatalog.AddDefinition(document, "HeroicAgeMyGod", template);
        var entry = MinorGodCatalog.FindDefinition(document.Root!, "heroicagemygod");
        Assert.NotNull(entry);
        Assert.Equal("yes", (string?)entry!.Attribute("future"));
        Assert.Equal("42", entry.Element("futuredata")?.Value);

        MinorGodCatalog.SetAttribute(entry, MinorGodCatalog.PortraitAttribute, "new.png");
        Assert.True(MinorGodCatalog.ReplaceDefinition(document, "HeroicAgeMyGod", entry));
        Assert.Equal("new.png", MinorGodCatalog.ReadAttribute(
            MinorGodCatalog.FindDefinition(document.Root!, "HeroicAgeMyGod")!, MinorGodCatalog.PortraitAttribute));
        Assert.True(MinorGodCatalog.DeleteDefinition(document, "HeroicAgeMyGod"));
        Assert.Empty(document.Root!.Elements());
    }

    [Fact]
    public void Mod_document_requires_exact_minor_gods_root_and_unique_dynamic_entries()
    {
        var valid = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<minorgodsmods><classicalageathena /></minorgodsmods>"),
            ModXmlDocumentKind.MinorGods);
        var wrongRoot = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<minorgods><classicalageathena /></minorgods>"),
            ModXmlDocumentKind.MinorGods);
        var duplicate = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<minorgodsmods><classicalageathena /><ClassicalAgeAthena /></minorgodsmods>"),
            ModXmlDocumentKind.MinorGods);

        Assert.True(valid.IsSafeToEdit);
        Assert.False(wrongRoot.IsSafeToEdit);
        Assert.False(duplicate.IsSafeToEdit);
    }

    [Fact]
    public void Gods_menu_and_editor_expose_minor_god_manager_fields()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var editor = File.ReadAllText(Path.Combine(root, "Windows", "MinorGodEditorWindow.cs"));

        Assert.Contains("Content=\"Minor Gods\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"GodsMinorGods_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Minor god Tech", editor, StringComparison.Ordinal);
        Assert.Contains("Portrait", editor, StringComparison.Ordinal);
        Assert.Contains("Icon", editor, StringComparison.Ordinal);
        Assert.Contains("ShowOptionsAsync", editor, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
