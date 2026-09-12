using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.EndToEnd)]
public sealed class AlphaModLifecycleWorkflowTests
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    [Fact]
    public void Ability_rename_strings_and_cross_module_references_commit_and_reopen_together()
    {
        using var workspace = CreateAbilityMod();
        var powersPath = workspace.GetPath("TestMod/game/data/gameplay/powers_mods.xml");
        var stringsPath = workspace.GetPath("TestMod/game/data/strings/stringmods.txt");
        var powers = ModDocumentPersistence.Prepare(powersPath, ModXmlDocumentKind.Powers);
        var power = powers.Document.Root!.Elements("power").Single();
        power.SetAttributeValue("name", "NewPower");
        power.Element("displaynameid")!.Value = "STR_POWER_NEW_NAME";

        var referenceFiles = ModReferenceFileCatalog.Discover(workspace.ModPath)
            .Where(file => !Path.GetFullPath(file.Path).Equals(powers.Path, StringComparison.OrdinalIgnoreCase));
        var migration = GameIdentifierReferenceMigrationPlan.Prepare(
            referenceFiles,
            GameIdentifierKind.Ability,
            [new KeyValuePair<string, string>("OldPower", "NewPower")]);
        var stringsBefore = File.ReadAllText(stringsPath);
        var updatedStrings = StringTableDocument.Parse(stringsBefore).Apply(
            new Dictionary<string, string>
            {
                ["STR_EXISTING"] = "Existing updated",
                ["STR_POWER_NEW_NAME"] = "New Power"
            },
            [],
            StringTableSection.Abilities);
        var additionalStates = migration.ExpectedStates.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
        additionalStates[stringsPath] = FileIntegrityTransaction.CaptureState(stringsPath);

        ModDocumentPersistence.Execute(
            [powers],
            migration.Paths.Append(stringsPath),
            additionalStates,
            () =>
            {
                PreservedXmlFile.SaveAtomic(powers.Document, powers.Path);
                migration.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path));
                File.WriteAllText(stringsPath, updatedStrings, Utf8WithoutBom);
            });

        var reopenedPower = XDocument.Load(powersPath, LoadOptions.PreserveWhitespace);
        Assert.Equal("NewPower", (string?)reopenedPower.Root!.Element("power")!.Attribute("name"));
        Assert.Equal("STR_POWER_NEW_NAME", reopenedPower.Root.Element("power")!.Element("displaynameid")?.Value);
        Assert.Contains(reopenedPower.DescendantNodes().OfType<XComment>(), comment => comment.Value.Contains("keep power", StringComparison.Ordinal));

        foreach (var relativePath in new[]
                 {
                     "TestMod/game/data/gameplay/proto_mods.xml",
                     "TestMod/game/data/gameplay/techtree_mods.xml",
                     "TestMod/game/data/tactics/hero.tactics"
                 })
        {
            var reopened = XDocument.Load(workspace.GetPath(relativePath), LoadOptions.PreserveWhitespace);
            Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(
                reopened, GameIdentifierKind.Ability, "OldPower"));
            Assert.NotEmpty(GameIdentifierReferencePolicy.FindRecognizedReferences(
                reopened, GameIdentifierKind.Ability, "NewPower"));
        }

        var reopenedStrings = File.ReadAllText(stringsPath);
        Assert.Contains("// user comment must survive", reopenedStrings, StringComparison.Ordinal);
        Assert.Contains("STR_EXISTING", reopenedStrings, StringComparison.Ordinal);
        Assert.Contains("Existing updated", reopenedStrings, StringComparison.Ordinal);
        Assert.Contains("STR_POWER_NEW_NAME", reopenedStrings, StringComparison.Ordinal);
        Assert.True(
            reopenedStrings.IndexOf(StringTableDocument.GetSectionMarker(StringTableSection.Abilities), StringComparison.Ordinal) >
            reopenedStrings.IndexOf("STR_EXISTING", StringComparison.Ordinal));
        Assert.DoesNotContain("\n", reopenedStrings.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void External_change_after_preparation_aborts_the_entire_cross_module_commit()
    {
        using var workspace = CreateAbilityMod();
        var powersPath = workspace.GetPath("TestMod/game/data/gameplay/powers_mods.xml");
        var tacticsPath = workspace.GetPath("TestMod/game/data/tactics/hero.tactics");
        var stringsPath = workspace.GetPath("TestMod/game/data/strings/stringmods.txt");
        var powersBefore = File.ReadAllBytes(powersPath);
        var stringsBefore = File.ReadAllBytes(stringsPath);
        var powers = ModDocumentPersistence.Prepare(powersPath, ModXmlDocumentKind.Powers);
        powers.Document.Root!.Element("power")!.SetAttributeValue("name", "NewPower");
        var migration = GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.ModPath)
                .Where(file => !Path.GetFullPath(file.Path).Equals(powers.Path, StringComparison.OrdinalIgnoreCase)),
            GameIdentifierKind.Ability,
            [new KeyValuePair<string, string>("OldPower", "NewPower")]);
        var externalTactics = "<tactics><!-- external --><action power=\"ExternalPower\" /></tactics>";
        File.WriteAllText(tacticsPath, externalTactics, Utf8WithoutBom);
        var commitCalled = false;

        Assert.Throws<IOException>(() => ModDocumentPersistence.Execute(
            [powers],
            migration.Paths.Append(stringsPath),
            migration.ExpectedStates,
            () =>
            {
                commitCalled = true;
                PreservedXmlFile.SaveAtomic(powers.Document, powers.Path);
                migration.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path));
            }));

        Assert.False(commitCalled);
        Assert.Equal(powersBefore, File.ReadAllBytes(powersPath));
        Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath));
        Assert.Equal(externalTactics, File.ReadAllText(tacticsPath));
    }

    private static TestWorkspace CreateAbilityMod()
    {
        var workspace = new TestWorkspace(nameof(AlphaModLifecycleWorkflowTests));
        workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", """
            <powersmod>
              <!-- keep power metadata -->
              <power name="OldPower" type="GodPower"><displaynameid>STR_POWER_OLD_NAME</displaynameid><future keep="yes" /></power>
            </powersmod>
            """);
        workspace.WriteXml("TestMod/game/data/gameplay/proto_mods.xml", """
            <protomods><unit name="Hero"><protoaction><name>Cast</name><castpower>OldPower</castpower></protoaction></unit></protomods>
            """);
        workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml", """
            <techtreemods><tech name="Unlock"><effect type="Data" power="OldPower" /></tech></techtreemods>
            """);
        workspace.WriteXml("TestMod/game/data/tactics/hero.tactics", """
            <tactics><!-- keep tactics --><action power="OldPower" /></tactics>
            """);
        workspace.WriteText("TestMod/game/data/strings/stringmods.txt",
            "Language = \"English\"\r\n\r\n" +
            "// user comment must survive\r\n" +
            "ID = \"STR_EXISTING\"   ;   Str = \"Existing\"\r\n");
        return workspace;
    }
}
