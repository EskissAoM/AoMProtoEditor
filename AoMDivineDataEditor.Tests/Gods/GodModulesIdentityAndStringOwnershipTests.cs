using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class GodModulesIdentityAndStringOwnershipTests
{
    [Fact]
    public void MajorGod_generated_string_namespace_detects_normalized_and_explicit_collisions()
    {
        var normalizedOwner = new MajorGodDefinition(
            "Sun-God",
            false,
            XElement.Parse("<civ><name>Sun-God</name></civ>"));
        var explicitOwner = new MajorGodDefinition(
            "Other",
            false,
            XElement.Parse("<civ><name>Other</name><titleid>STR_CIV_NEW_GOD_T</titleid></civ>"));

        Assert.Equal(
            "Sun-God",
            MajorGodCatalog.FindGeneratedStringNamespaceCollision("Sun_God", [normalizedOwner])?.Name);
        Assert.Equal(
            "Other",
            MajorGodCatalog.FindGeneratedStringNamespaceCollision("New-God", [explicitOwner])?.Name);
        Assert.Null(MajorGodCatalog.FindGeneratedStringNamespaceCollision(
            "Sun_God",
            [normalizedOwner],
            excludedMajorGodName: "Sun-God"));
    }

    [Fact]
    public void MajorGod_owned_strings_include_only_canonical_generated_ids()
    {
        var entry = XElement.Parse("""
            <civ>
              <name>MyGod</name>
              <displaynameid>STR_CIV_MYGOD</displaynameid>
              <rollovernameid>STR_SHARED_ROLLOVER</rollovernameid>
              <titleid>STR_CIV_MYGOD_T</titleid>
              <bonusunitspawning>
                <resourcegoal name="MyGoal">
                  <displayinfo>
                    <displaynameid>STR_MYGOAL_SPAWN_REWARD</displaynameid>
                    <rolloverid>STR_SHARED_GOAL_ROLLOVER</rolloverid>
                  </displayinfo>
                </resourcegoal>
              </bonusunitspawning>
            </civ>
            """);

        var owned = MajorGodCatalog.CollectOwnedStringIds(entry);

        Assert.Contains("STR_CIV_MYGOD", owned);
        Assert.Contains("STR_CIV_MYGOD_T", owned);
        Assert.Contains("STR_MYGOAL_SPAWN_REWARD", owned);
        Assert.DoesNotContain("STR_SHARED_ROLLOVER", owned);
        Assert.DoesNotContain("STR_SHARED_GOAL_ROLLOVER", owned);
    }

    [Fact]
    public void MajorGod_identity_rename_moves_only_owned_ids()
    {
        var entry = XElement.Parse("""
            <civ>
              <name>OldGod</name>
              <displaynameid>STR_CIV_OLDGOD</displaynameid>
              <rollovernameid>STR_SHARED_ROLLOVER</rollovernameid>
              <titleid>STR_CIV_OLDGOD_T</titleid>
            </civ>
            """);

        var changes = MajorGodCatalog.RenameOwnedResolvedStringIds(entry, "OldGod", "NewGod");

        Assert.Equal(2, changes.Count);
        Assert.Equal("STR_CIV_NEWGOD", entry.Element("displaynameid")?.Value);
        Assert.Equal("STR_SHARED_ROLLOVER", entry.Element("rollovernameid")?.Value);
        Assert.Equal("STR_CIV_NEWGOD_T", entry.Element("titleid")?.Value);
    }

    [Fact]
    public void BonusGoal_identity_rename_preserves_custom_string_ids()
    {
        var goal = XElement.Parse("""
            <resourcegoal name="OldGoal">
              <displayinfo>
                <displaynameid>STR_OLDGOAL_SPAWN_REWARD</displaynameid>
                <rolloverid>STR_SHARED_ROLLOVER</rolloverid>
              </displayinfo>
            </resourcegoal>
            """);

        var changes = MajorGodCatalog.RenameOwnedBonusGoalStringIds(goal, "OldGoal", "NewGoal");

        Assert.Single(changes);
        Assert.Equal("STR_NEWGOAL_SPAWN_REWARD", goal.Element("displayinfo")?.Element("displaynameid")?.Value);
        Assert.Equal("STR_SHARED_ROLLOVER", goal.Element("displayinfo")?.Element("rolloverid")?.Value);
    }

    [Fact]
    public void Culture_generated_string_namespace_detects_normalized_and_explicit_collisions()
    {
        var normalizedOwner = new CultureDefinition(
            "Sea-Culture",
            false,
            XElement.Parse("<culture><name>Sea-Culture</name></culture>"));
        var explicitOwner = new CultureDefinition(
            "Legacy",
            true,
            XElement.Parse("<culture><name>Legacy</name><displaynameid>STR_CULTURE_NEW_CULTURE</displaynameid></culture>"));

        Assert.Equal(
            "Sea-Culture",
            CultureCatalog.FindGeneratedDisplayNameIdCollision("Sea_Culture", [normalizedOwner])?.Name);
        Assert.Equal(
            "Legacy",
            CultureCatalog.FindGeneratedDisplayNameIdCollision("New-Culture", [explicitOwner])?.Name);
        Assert.True(CultureCatalog.OwnsDisplayNameId("Sea-Culture", "str_culture_sea_culture"));
        Assert.False(CultureCatalog.OwnsDisplayNameId("Sea-Culture", "STR_SHARED"));
    }

    [Fact]
    public void Culture_identity_rename_moves_generated_id_but_preserves_custom_id()
    {
        var owned = XElement.Parse(
            "<culture><name>OldCulture</name><displaynameid>STR_CULTURE_OLDCULTURE</displaynameid></culture>");
        var shared = XElement.Parse(
            "<culture><name>OldCulture</name><displaynameid>STR_SHARED</displaynameid></culture>");

        var rename = CultureCatalog.RenameOwnedDisplayNameId(owned, "OldCulture", "NewCulture");
        var sharedRename = CultureCatalog.RenameOwnedDisplayNameId(shared, "OldCulture", "NewCulture");

        Assert.NotNull(rename);
        Assert.Equal("STR_CULTURE_NEWCULTURE", owned.Element("displaynameid")?.Value);
        Assert.Null(sharedRename);
        Assert.Equal("STR_SHARED", shared.Element("displaynameid")?.Value);
    }

    [Fact]
    public void MinorGod_identity_is_unique_after_removing_the_age_prefix()
    {
        var unsafeDocument = XDocument.Parse(
            "<minorgodsmods><ClassicalAgeAthena/><HeroicAgeAthena/></minorgodsmods>");
        var safeDocument = XDocument.Parse(
            "<minorgodsmods><ClassicalAgeAthena/><HeroicAgeApollo/></minorgodsmods>");

        Assert.False(ModXmlDocumentSafety.Validate(
            unsafeDocument,
            ModXmlDocumentKind.MinorGods).IsSafeToEdit);
        Assert.True(ModXmlDocumentSafety.Validate(
            safeDocument,
            ModXmlDocumentKind.MinorGods).IsSafeToEdit);
        Assert.Throws<InvalidDataException>(() => MinorGodCatalog.EnsureUniqueDisplayNames(unsafeDocument));
    }

    [Fact]
    public void AutomaticMinorGod_sync_leaves_a_second_age_technology_without_an_ambiguous_entry()
    {
        var minorGods = XDocument.Parse("<minorgodsmods><ClassicalAgeAthena/></minorgodsmods>");
        var technologies = XDocument.Parse("""
            <techtreemods>
              <tech name="ClassicalAgeAthena"><flag>AgeUpgrade</flag></tech>
              <tech name="HeroicAgeAthena"><flag>AgeUpgrade</flag></tech>
            </techtreemods>
            """);

        var added = MinorGodCatalog.AddMissingDefinitionsForAgeUpgradeTechnologies(minorGods, [technologies]);

        Assert.Equal(0, added);
        Assert.Single(minorGods.Root!.Elements());
    }

    [Fact]
    public void Editor_routes_migrate_and_remove_only_owned_strings()
    {
        var root = ProjectSource.RootPath;
        var majorGodEditor = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));
        var cultureHost = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var cultureEditor = File.ReadAllText(Path.Combine(root, "Windows", "CultureEditorWindow.cs"));

        Assert.Contains("AdoptNameAndGeneratedStringIdsAsync", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("MajorGodCatalog.CollectOwnedStringIds", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("ScheduleOwnedStringRemoval", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("ValidatePendingGeneratedStringClaims", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("CultureCatalog.OwnsDisplayNameId", cultureHost, StringComparison.Ordinal);
        Assert.Contains("EnsureCultureGeneratedStringNamespaceAvailable", cultureHost, StringComparison.Ordinal);
        Assert.Contains("SaveCultureStringValuesAsync(stringUpdates, stringRemovals)", cultureHost, StringComparison.Ordinal);
        Assert.Contains("displayNameUpdate", cultureEditor, StringComparison.Ordinal);
    }
}
