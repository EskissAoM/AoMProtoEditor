using System.Xml.Linq;
using Avalonia.Controls;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodPhase1Tests
{
    [Fact]
    public void ExtractDefinitions_UsesEachCivAndItsDirectName()
    {
        var document = XDocument.Parse("""
            <civs>
              <civ><name>Zeus</name><minor><name>Athena</name></minor></civ>
              <civ><name>Ra</name></civ>
            </civs>
            """);

        var definitions = MajorGodCatalog.ExtractDefinitions(document, isBuiltIn: true);

        Assert.Equal(["Ra", "Zeus"], definitions.Select(definition => definition.Name));
        Assert.All(definitions, definition => Assert.True(definition.IsBuiltIn));
    }

    [Fact]
    public void LoadOrCreateModDocument_UsesCivsModsRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"major-gods-{Guid.NewGuid():N}.xml");
        try
        {
            var document = MajorGodCatalog.LoadOrCreateModDocument(path);
            Assert.Equal("civsmods", document.Root?.Name.LocalName);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void CreatePass1Entry_UsesCanonicalOrderAndRequiredStatsId()
    {
        var entry = MajorGodCatalog.CreatePass1Entry("New God", "Greek");

        Assert.Equal(MajorGodCatalog.Pass1FieldOrder, entry.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("SP", entry.Element("statsid")?.Value);
        Assert.Equal("G", entry.Element("key")?.Value);
        Assert.Equal("STR_CIV_NEW_GOD", entry.Element("displaynameid")?.Value);
        Assert.Equal("STR_CIV_NEW_GOD_LR", entry.Element("rollovernameid")?.Value);
        Assert.Equal("STR_CIV_NEW_GOD_T", entry.Element("titleid")?.Value);
        Assert.Null(entry.Element("main"));
    }

    [Fact]
    public void UpdatingPass1Field_PreservesUnhandledXmlAndDocumentOrder()
    {
        var entry = XElement.Parse("""
            <civ>
              <name>Zeus</name>
              <statsid>Legacy</statsid>
              <key>G</key>
              <culture>Greek</culture>
              <displaynameid>STR_CIV_ZEUS</displaynameid>
              <custommechanic foo="bar"><value>42</value></custommechanic>
              <icon>old.png</icon>
            </civ>
            """);

        MajorGodCatalog.SetPass1Value(entry, "culture", "Norse");
        MajorGodCatalog.SetPass1Value(entry, "portrait", "portrait.png");
        MajorGodCatalog.EnsureStatsId(entry);

        Assert.Equal("SP", entry.Element("statsid")?.Value);
        Assert.Equal("Norse", entry.Element("culture")?.Value);
        Assert.Equal("bar", entry.Element("custommechanic")?.Attribute("foo")?.Value);
        Assert.Equal("42", entry.Element("custommechanic")?.Element("value")?.Value);
        Assert.True(entry.Elements().ToList().IndexOf(entry.Element("custommechanic")!) <
                    entry.Elements().ToList().IndexOf(entry.Element("icon")!));
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("Z", true)]
    [InlineData("a", false)]
    [InlineData("AB", false)]
    [InlineData("", false)]
    public void MajorGodKey_IsOneCapitalLetter(string value, bool expected)
        => Assert.Equal(expected, MajorGodCatalog.IsValidKey(value));

    [Fact]
    public void SharedFieldUi_MatchesTheCommonEntityEditorLayout()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var labels = new[]
        {
            "Text=\"Major God :\"", "Text=\"──── Properties ────\"", "Text=\"Display Name\"",
            "Text=\"Title\"", "Text=\"Rollover\"", "Text=\"Icon\"", "Text=\"Portrait\"",
            "Text=\"Breakout Portrait\"", "Text=\"Culture\"", "Text=\"Key\"",
            "Text=\"Minimap Border Image\"", "Text=\"Background Scenario\""
        };
        var previous = -1;
        foreach (var label in labels)
        {
            var current = xaml.IndexOf(label, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected {label} after the preceding shared field.");
            previous = current;
        }
        Assert.Contains("x:Name=\"_rawXmlEditor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_applyRawXmlButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_xmlPreviewToggleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"4*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition Width=\"1*\" MinWidth=\"250\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_cultureBox\" Width=\"200\" MaxWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_rolloverNameBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"96\" AcceptsReturn=\"True\" TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EditorAutoCompleteService.EnableDropdown(_cultureBox", File.ReadAllText(
            Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void CultureCatalog_ExtractsOriginalAndModdedCultureNames()
    {
        var original = XDocument.Parse("""
            <cultures>
              <culture><name>Greek</name><displaynameid>STR_CULTURE_GREEK</displaynameid></culture>
              <culture><name>Egyptian</name></culture>
            </cultures>
            """);
        var modded = XDocument.Parse("""
            <culturesmods>
              <culture><name>CustomCulture</name></culture>
              <culture><name>greek</name></culture>
            </culturesmods>
            """);

        var names = CultureCatalog.Merge(
            CultureCatalog.ExtractNames(original),
            CultureCatalog.ExtractNames(modded));

        Assert.Equal(["CustomCulture", "Egyptian", "Greek"], names);
    }

    [Fact]
    public void CultureCatalog_ReadsCultureModsRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"culture-mods-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, "<culturesmods><culture><name>AtlantisReborn</name></culture></culturesmods>");
        try
        {
            Assert.Equal(["AtlantisReborn"], CultureCatalog.ReadModFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StructuredEditor_ResolvesStringsNormalizesKeyAndPreservesUnknownXml()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"major-god-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var modPath = Path.Combine(directory, MajorGodCatalog.ModFileName);
        var stringsPath = Path.Combine(directory, "stringmods.txt");
        Dictionary<string, string>? savedStrings = null;
        File.WriteAllText(modPath, """
            <civsmods>
              <civ>
                <name>ZeusMod</name>
                <statsid>Legacy</statsid>
                <key>G</key>
                <culture>Greek</culture>
                <displaynameid>STR_CIV_ZEUS_MOD</displaynameid>
                <rollovernameid>STR_CIV_ZEUS_MOD_LR</rollovernameid>
                <titleid>STR_CIV_ZEUS_MOD_T</titleid>
                <agetech age="ClassicalAge"><tech>ArchaicAgeZeus</tech></agetech>
                <deathmatchtech>DeathMatchGreek</deathmatchtech>
                <custommechanic foo="keep">value</custommechanic>
                <icon>resources\greek\zeus_icon.png</icon>
                <portrait>resources\greek\zeus_portrait.png</portrait>
                <breakoutportrait>resources\greek\zeus_breakout.png</breakoutportrait>
                <minimapborderimage>ui\ingame\minimap\grk_min_panel</minimapborderimage>
                <backgroundscenario>backgrounds\background_greek.mythscn</backgroundscenario>
              </civ>
            </civsmods>
            """);

        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var baseDefinition = new MajorGodDefinition(
                    "Zeus",
                    true,
                    XElement.Parse("""
                        <civ>
                          <name>Zeus</name><key>G</key><culture>Greek</culture>
                          <minimapborderimage>ui\ingame\minimap\grk_min_panel</minimapborderimage>
                          <backgroundscenario>backgrounds\background_greek.mythscn</backgroundscenario>
                        </civ>
                        """));
                var view = new MajorGodEditorView(
                    [baseDefinition],
                    modPath,
                    id => Task.FromResult<string?>(id switch
                    {
                        "STR_CIV_ZEUS_MOD" => "Resolved Zeus",
                        "STR_CIV_ZEUS_MOD_LR" => "Resolved rollover",
                        "STR_CIV_ZEUS_MOD_T" => "Resolved title",
                        _ => null
                    }),
                    (updates, _) =>
                    {
                        savedStrings = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
                        return Task.CompletedTask;
                    },
                    cultureNames: ["Greek", "Norse"],
                    iconPaths: ["resources\\greek\\zeus_icon.png", "resources\\greek\\zeus_portrait.png", "resources\\greek\\zeus_breakout.png"],
                    modStringsPath: stringsPath,
                    technologyNames: ["ArchaicAgeZeus", "DeathMatchGreek", "OlympianTeamBonus"]);
                view.SetModifiedMode(true);
                view.SelectMajorGod("ZeusMod");
                await Task.Yield();

                Assert.Equal("Resolved Zeus", view.FindControl<TextBox>("_displayNameBox")!.Text);
                Assert.Equal("Resolved rollover", view.FindControl<TextBox>("_rolloverNameBox")!.Text);
                Assert.Equal("Resolved title", view.FindControl<TextBox>("_titleBox")!.Text);
                Assert.Equal("resources\\greek\\zeus_icon.png", view.FindControl<AssetPathEditor>("_iconEditor")!.FullValue);
                Assert.Equal("Classical Age", view.FindControl<ComboBox>("_ageBox")!.SelectedItem);
                Assert.False(view.FindControl<ComboBox>("_ageBox")!.IsEnabled);

                view.FindControl<TextBox>("_keyBox")!.Text = "n";
                await Task.Yield();
                Assert.Equal("N", view.FindControl<TextBox>("_keyBox")!.Text);
                var rawXml = view.FindControl<TextEditor>("_rawXmlEditor")!.Text;
                Assert.Contains("<statsid>SP</statsid>", rawXml, StringComparison.Ordinal);
                Assert.Contains("<key>N</key>", rawXml, StringComparison.Ordinal);
                Assert.Contains("<custommechanic foo=\"keep\">value</custommechanic>", rawXml, StringComparison.Ordinal);

                view.FindControl<TextBox>("_displayNameBox")!.Text = "Edited Zeus";
                await Task.Yield();
                Assert.True(await view.SaveAsync(), view.FindControl<TextBlock>("_statusMessage")!.Text);
            });

            Assert.Equal("Edited Zeus", savedStrings?.GetValueOrDefault("STR_CIV_ZEUS_MOD"));
            var savedDocument = XDocument.Load(modPath, LoadOptions.PreserveWhitespace);
            var savedEntry = MajorGodCatalog.Find(savedDocument, "ZeusMod");
            Assert.NotNull(savedEntry);
            Assert.Equal("SP", savedEntry!.Element("statsid")?.Value);
            Assert.Equal("N", savedEntry.Element("key")?.Value);
            Assert.Equal("keep", savedEntry.Element("custommechanic")?.Attribute("foo")?.Value);
            Assert.Equal("ArchaicAgeZeus", savedEntry.Element("agetech")?.Element("tech")?.Value);
            Assert.Equal("ClassicalAge", savedEntry.Element("agetech")?.Attribute("age")?.Value);
            Assert.Equal("DeathMatchGreek", savedEntry.Element("deathmatchtech")?.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AssociatedTechEditors_PreserveDeprecatedAndUnknownXml()
    {
        var entry = XElement.Parse("""
            <civ>
              <name>Zeus</name>
              <titleid>STR_CIV_ZEUS_T</titleid>
              <custom>keep</custom>
              <empirewarstech>DeprecatedButPreserved</empirewarstech>
              <icon>zeus.png</icon>
            </civ>
            """);

        MajorGodCatalog.SetAgeTech(entry, "ClassicalAge", "ClassicalAgeZeus");
        MajorGodCatalog.SetAssociatedTech(entry, "deathmatchtech", "DeathMatchGreek");
        MajorGodCatalog.SetAssociatedTech(entry, "teamtech", "OlympianTeamBonus");

        Assert.Equal("ClassicalAge", entry.Element("agetech")?.Attribute("age")?.Value);
        Assert.Equal("ClassicalAgeZeus", entry.Element("agetech")?.Element("tech")?.Value);
        Assert.Equal("DeathMatchGreek", entry.Element("deathmatchtech")?.Value);
        Assert.Equal("OlympianTeamBonus", entry.Element("teamtech")?.Value);
        Assert.Equal("DeprecatedButPreserved", entry.Element("empirewarstech")?.Value);
        Assert.Equal("keep", entry.Element("custom")?.Value);
        Assert.True(entry.Elements().ToList().IndexOf(entry.Element("agetech")!) <
                    entry.Elements().ToList().IndexOf(entry.Element("icon")!));
    }

    [Fact]
    public void AssociatedTechUi_UsesAgeAndTechnologyCatalogSelectors()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Text=\"──── Associated Techs ────\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_ageBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_ageTechBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_deathmatchTechBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_addTeamTechButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_teamTechBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("EmpireWarsTech", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EditorAutoCompleteService.EnableDropdown(technologyBox", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_ageBox\" Width=\"140\" MaxWidth=\"140\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_ageTechBox\" Width=\"200\" MaxWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_deathmatchTechBox\" Width=\"200\" MaxWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_teamTechBox\" Width=\"200\" MaxWidth=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Equal(
            ["ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"],
            MajorGodCatalog.AgeValues);
    }

    [Fact]
    public void StartingUnits_DefaultModeIsOmittedAndUnitShapeIsCanonical()
    {
        var entry = XElement.Parse("<civ><titleid>STR_CIV_TEST_T</titleid><icon>test.png</icon></civ>");

        var group = MajorGodCatalog.AddStartingUnitsGroup(entry, "supremacy");
        var unit = MajorGodCatalog.AddStartingUnit(group, "VillagerGreek");

        Assert.Null(group.Attribute("mode"));
        Assert.Equal("VillagerGreek", unit.Value);
        Assert.Equal("1", unit.Attribute("count")?.Value);
        Assert.Equal("0", unit.Attribute("x")?.Value);
        Assert.Equal("0", unit.Attribute("y")?.Value);
        Assert.Equal("0", unit.Attribute("z")?.Value);
        Assert.Null(unit.Attribute("delay"));
        Assert.True(entry.Elements().ToList().IndexOf(group) < entry.Elements().ToList().IndexOf(entry.Element("icon")!));
    }

    [Fact]
    public void StartingUnitsUi_UsesSharedReferenceAndNumericBehaviors()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("x:Name=\"_majorGodTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Properties\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Starting Attributes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"──── Starting Attributes ────\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Special Attributes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_specialAttributesPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Starting Units\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_removeTeamTechButton", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"4,4,0,4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StartingModes = [\"Supremacy\", \"Deathmatch\", \"Lightning\"]", code, StringComparison.Ordinal);
        Assert.Contains("GetAvailableStartingModes(\"startingunits\", group)", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add starting unit\"", code, StringComparison.Ordinal);
        Assert.Contains("EditorAutoCompleteService.EnableDropdown(unitBox", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.PositiveInteger", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.SignedFloat", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.PositiveFloat", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartingResources_DefaultModeIsOmittedAndUsesKnownResourceElements()
    {
        var entry = XElement.Parse("<civ><startingunits/><icon>test.png</icon></civ>");

        var group = MajorGodCatalog.AddStartingResourcesGroup(entry, "supremacy");
        foreach (var resourceType in ProtoConstants.KnownResourceTypes)
            MajorGodCatalog.AddStartingResource(group, resourceType, "10");

        Assert.Null(group.Attribute("mode"));
        Assert.Equal(["food", "wood", "gold", "favor"], group.Elements().Select(element => element.Name.LocalName));
        Assert.All(group.Elements(), resource => Assert.Equal("10", resource.Value));
        Assert.True(entry.Elements().ToList().IndexOf(group) < entry.Elements().ToList().IndexOf(entry.Element("icon")!));
    }

    [Fact]
    public void StartingResourcesUi_UsesSharedResourceCatalogAndUniqueModes()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Text=\"Starting Resources\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_startingResourcesPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_addStartingResourcesGroupButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GetAvailableStartingModes(\"startingresources\", group)", code, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownResourceTypes", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add resource\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateStartingResourceSegment", code, StringComparison.Ordinal);
        Assert.Contains("Starting Resources mode '{mode}' can only be used once.", code, StringComparison.Ordinal);
        Assert.Contains("Starting Units mode '{mode}' can only be used once.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingResourceAlsoRemovesItsIndentationLine()
    {
        var group = XElement.Parse("""
            <startingresources>
              <food>200</food>
              <wood>250</wood>
            </startingresources>
            """, LoadOptions.PreserveWhitespace);

        MajorGodCatalog.RemoveElementAndAdjacentWhitespace(group.Element("food")!);

        var remainingXml = string.Concat(group.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal("\n  <wood>250</wood>\n", remainingXml);
    }

    [Fact]
    public void StartingQueue_DefaultModeIsOmittedAndOptionalAttributesStayOptional()
    {
        var entry = XElement.Parse("<civ><startingresources/><icon>test.png</icon></civ>");

        var group = MajorGodCatalog.AddStartingQueueGroup(entry, "supremacy");
        var unit = MajorGodCatalog.AddStartingQueueUnit(group, "VillagerGreek");

        Assert.Null(group.Attribute("mode"));
        Assert.Equal("VillagerGreek", unit.Value);
        Assert.Empty(unit.Attributes());
        Assert.True(entry.Elements().ToList().IndexOf(group) <
                    entry.Elements().ToList().IndexOf(entry.Element("startingresources")!));
    }

    [Fact]
    public void StartingElements_AreIndentedWhenAddedToWhitespacePreservingXml()
    {
        var entry = XElement.Parse("""
            <civ>
              <startingqueue>
                <unit>VillagerGreek</unit>
              </startingqueue>
              <icon>test.png</icon>
            </civ>
            """, LoadOptions.PreserveWhitespace);
        var queue = entry.Element("startingqueue")!;

        MajorGodCatalog.AddStartingQueueUnit(queue, "Hoplite");
        var resources = MajorGodCatalog.AddStartingResourcesGroup(entry, "deathmatch");
        MajorGodCatalog.AddStartingResource(resources, "Food", "18000");
        MajorGodCatalog.AddStartingResource(resources, "Gold", "18000");

        var xml = entry.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("\n    <unit>Hoplite</unit>\n  </startingqueue>", xml, StringComparison.Ordinal);
        Assert.Contains("\n  <startingresources mode=\"deathmatch\">\n    <food>18000</food>\n    <gold>18000</gold>\n  </startingresources>\n  <icon>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingStartingQueueCountClearsTrainingFlags()
    {
        var unit = XElement.Parse("<unit autotrain=\"true\" prequeue=\"true\">Ajax</unit>");

        MajorGodCatalog.SetStartingQueueCount(unit, "2");

        Assert.Equal("2", unit.Attribute("count")?.Value);
        Assert.Null(unit.Attribute("autotrain"));
        Assert.Null(unit.Attribute("prequeue"));
    }

    [Fact]
    public void StartingQueueUi_UsesProtoUnitsUniqueModesAndOptionalControls()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Text=\"Starting Queue\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_startingQueuePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_addStartingQueueGroupButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GetAvailableStartingModes(\"startingqueue\", group)", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add starting queue unit\"", code, StringComparison.Ordinal);
        Assert.Contains("AddOptionalQueueNumber(row, unit, \"delay\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Delay (ms)\"", code, StringComparison.Ordinal);
        Assert.Contains("AddOptionalQueueNumber(row, unit, \"count\"", code, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = IsModifiedMode && !hasCount", code, StringComparison.Ordinal);
        Assert.Contains("MajorGodCatalog.SetStartingQueueCount", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Auto train\"", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Prequeue\"", code, StringComparison.Ordinal);
        Assert.Contains("Starting Queue mode '{mode}' can only be used once.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EconomicFields_UseCanonicalElementsAttributesAndOrder()
    {
        var entry = XElement.Parse("<civ><deathmatchtech>DeathMatchGreek</deathmatchtech><startingunits/><icon>test.png</icon></civ>");

        MajorGodCatalog.SetEconomicValue(entry, "buildingefficiency", "0.6");
        MajorGodCatalog.SetEconomicValue(entry, "extrabuildingefficiency", "0.3");
        MajorGodCatalog.SetEconomicValue(entry, "buildingrepairfactor", "0.0");
        var prayer = MajorGodCatalog.AddSecondaryPrayerEfficiency(entry, "1", "2", "3");

        Assert.Equal("1", prayer.Attribute("g")?.Value);
        Assert.Equal("2", prayer.Attribute("v")?.Value);
        Assert.Equal("3", prayer.Attribute("z")?.Value);
        Assert.Equal(
            ["deathmatchtech", "secondaryprayerefficiency", "buildingefficiency", "extrabuildingefficiency", "buildingrepairfactor", "startingunits", "icon"],
            entry.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void EconomicUi_UsesSharedNumericAndOptionalControlPatterns()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Text=\"──── Economic ────\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_economicPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Second Builder Efficiency\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Extra Builder Efficiency\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateEconomicOptionalButton(\"Repair Factor\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateEconomicOptionalButton(\"Secondary Prayer Efficiency\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Later Growth\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Early Income Reduction\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Modifier\")", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.UnsignedFloat", code, StringComparison.Ordinal);
        Assert.Contains("ValidateEconomicAttributes(candidate)", code, StringComparison.Ordinal);
        Assert.Contains("must be a number greater than or equal to zero", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedData_IsExtractedWithoutCivilizations()
    {
        var document = XDocument.Parse("""
            <civs>
              <defaultpoplimit>1000</defaultpoplimit>
              <defaultrepairfactor>0.5</defaultrepairfactor>
              <lightningtech>LightningGeneral</lightningtech>
              <civ><name>Zeus</name></civ>
            </civs>
            """);

        var shared = MajorGodCatalog.ExtractSharedData(document);

        Assert.NotNull(shared);
        Assert.Equal("1000", shared!.Element("defaultpoplimit")?.Value);
        Assert.Equal("0.5", shared.Element("defaultrepairfactor")?.Value);
        Assert.Null(shared.Element("civ"));
    }

    [Fact]
    public void SharedData_AppearsFirstAndUsesReadOnlyPropertiesOnlyView()
    {
        var shared = XElement.Parse("""
            <civs>
              <defaultpoplimit>1000</defaultpoplimit>
              <defaultrepairfactor>0.5</defaultrepairfactor>
              <storymodetech>StoryModeGeneral</storymodetech>
              <ludicrousmodetech>LudicrousModeGeneral</ludicrousmodetech>
              <lightningtech>LightningGeneral</lightningtech>
              <deathmatchtech>DeathMatchGeneral</deathmatchtech>
              <deathmatchpowergrantdelay>
                <archaicage>80</archaicage><classicalage>140</classicalage>
                <heroicage>200</heroicage><mythicage>260</mythicage>
              </deathmatchpowergrantdelay>
            </civs>
            """);
        var definitions = new[]
        {
            new MajorGodDefinition("Zeus", true, XElement.Parse("<civ><name>Zeus</name></civ>")),
            new MajorGodDefinition("Amaterasu", true, XElement.Parse("<civ><name>Amaterasu</name></civ>"))
        };

        AvaloniaTestHost.Run(() =>
        {
            var view = new MajorGodEditorView(definitions, null, baseSharedData: shared);
            Assert.Equal(["Shared data", "Amaterasu", "Zeus"], view.GetMajorGodNames(modified: false));

            view.SelectMajorGod("Shared data");

            Assert.False(view.FindControl<Grid>("_majorGodIdentityRow")!.IsVisible);
            Assert.False(view.FindControl<TabItem>("_startingAttributesTab")!.IsVisible);
            Assert.False(view.FindControl<TabItem>("_specialAttributesTab")!.IsVisible);
            Assert.True(view.FindControl<StackPanel>("_sharedDataPropertiesPanel")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("_defaultContainedRegenRow")!.IsVisible);
            Assert.Equal("1000", view.FindControl<TextBox>("_defaultPopLimitBox")!.Text);
            Assert.Equal("DeathMatchGeneral", view.FindControl<TextBox>("_sharedDeathMatchTechBox")!.Text);
            Assert.Equal("260", view.FindControl<TextBox>("_sharedMythicDelayBox")!.Text);
            Assert.True(view.FindControl<TextEditor>("_rawXmlEditor")!.IsReadOnly);
        });
    }

    [Fact]
    public void SharedData_BrowserEntryIsEmphasizedAndCannotBeCopied()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("FontWeight=\"{Binding FontWeight}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("isEmphasized ? FontWeight.Bold : FontWeight.Normal", code, StringComparison.Ordinal);
        Assert.Contains("selectedIndex == 0", code, StringComparison.Ordinal);
        Assert.Contains("entityName.Equals(MajorGodCatalog.SharedDataDisplayName", code, StringComparison.Ordinal);
        Assert.Contains("duplicateSelected && CurrentMajorGodName?.Equals", File.ReadAllText(
            Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void SpecialMechanicToggles_WriteCanonicalXmlAndRemoveWhenUnchecked()
    {
        var entry = XElement.Parse("<civ><name>Test</name></civ>");

        var partisans = MajorGodCatalog.SetSpecialMechanicToggle(entry, "partisans", true, "1");
        var devotion = MajorGodCatalog.SetSpecialMechanicToggle(entry, "devotioncommands", true);

        Assert.Equal("1", partisans?.Value);
        Assert.NotNull(devotion);
        Assert.Contains("<devotioncommands></devotioncommands>", devotion!.ToString(), StringComparison.Ordinal);

        MajorGodCatalog.SetSpecialMechanicToggle(entry, "partisans", false);
        MajorGodCatalog.SetSpecialMechanicToggle(entry, "devotioncommands", false);
        Assert.Null(entry.Element("partisans"));
        Assert.Null(entry.Element("devotioncommands"));
    }

    [Fact]
    public void SpecialMechanicsUi_ExcludesSystemsAlreadyOnTheMajorGod()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"major-god-special-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var modPath = Path.Combine(directory, MajorGodCatalog.ModFileName);
        File.WriteAllText(modPath, "<civsmods><civ><name>Test</name><partisans>1</partisans></civ></civsmods>");
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = new MajorGodEditorView([], modPath);
                view.SetModifiedMode(true);
                view.SelectMajorGod("Test");
                await Task.Yield();

                var selector = view.FindControl<AutoCompleteBox>("_specialMechanicSelector")!;
                var available = selector.ItemsSource!.Cast<string>().ToList();
                Assert.DoesNotContain("Partisans", available);
                Assert.DoesNotContain("DevotionCommands", available);
                Assert.Contains("Shades", available);
                Assert.True(view.FindControl<CheckBox>("_partisansCheckBox")!.IsChecked);
                Assert.Empty(view.FindControl<StackPanel>("_specialMechanicsPanel")!.Children);

                selector.SelectedItem = "Shades";
                await Task.Yield();
                Assert.Contains("<shades", view.FindControl<TextEditor>("_rawXmlEditor")!.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Shades", selector.ItemsSource!.Cast<string>());
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SpecialMechanicsTab_ExposesTogglesAndTheSupportedSelectorList()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Text=\"──── Special mechanics ────\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Add\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Has Partisans\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Has Devotion Commands\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_specialMechanicSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_specialMechanicTabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_selectedSpecialMechanicTag = definition.Tag", code, StringComparison.Ordinal);
        Assert.Contains("rendered.Card.IsVisible = selected", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(remove, 1)", code, StringComparison.Ordinal);
        foreach (var label in new[]
                 {
                     "Shades", "TerrainCreeps", "BonusUnitSpawning", "BountyResourceEarning",
                     "TimeShifting", "BuildingChain", "CombatXP", "OnCastPowerCostFactor"
                 })
            Assert.Contains($"(\"{label}\",", code, StringComparison.Ordinal);
        Assert.Contains("SetSpecialMechanicToggle(\"partisans\"", code, StringComparison.Ordinal);
        Assert.Contains("SetSpecialMechanicToggle(\"devotioncommands\"", code, StringComparison.Ordinal);
        Assert.Contains("can only occur once", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalGod_HidesEmptySpecialToggleRowAndShowsExistingToggle()
    {
        var definitions = new[]
        {
            new MajorGodDefinition("Plain", true, XElement.Parse("<civ><name>Plain</name></civ>")),
            new MajorGodDefinition("Partisan", true, XElement.Parse("<civ><name>Partisan</name><partisans>1</partisans></civ>"))
        };

        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView(definitions, null);
            view.SelectMajorGod("Plain");
            await Task.Yield();
            Assert.False(view.FindControl<StackPanel>("_specialMechanicToggleRow")!.IsVisible);

            view.SelectMajorGod("Partisan");
            await Task.Yield();
            Assert.True(view.FindControl<StackPanel>("_specialMechanicToggleRow")!.IsVisible);
            Assert.True(view.FindControl<CheckBox>("_partisansCheckBox")!.IsChecked);
            Assert.True(view.FindControl<CheckBox>("_partisansCheckBox")!.IsVisible);
            Assert.False(view.FindControl<CheckBox>("_devotionCommandsCheckBox")!.IsVisible);
        });
    }

    [Fact]
    public void ShadesChildren_AreInsertedInCanonicalOrder()
    {
        var shades = new XElement("shades");
        MajorGodCatalog.AddShadesChild(shades, "delay", "10");
        MajorGodCatalog.AddShadesChild(shades, "targetunit", "HumanSoldier");
        MajorGodCatalog.AddShadesChild(shades, "chance", "0.2");
        MajorGodCatalog.AddShadesChild(shades, "protounit", "HadesShade");
        MajorGodCatalog.AddShadesChild(shades, "maxqueued", "50");

        Assert.Equal(
            new[] { "protounit", "targetunit", "chance", "delay", "maxqueued" },
            shades.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void TimeShiftingUnits_UseCanonicalRatioAttributes()
    {
        var timeShifting = new XElement("timeshifting", new XAttribute("maxconcurrenttimeshifts", "2"));
        var unit = MajorGodCatalog.AddTimeShiftingUnit(timeShifting, "Manor");

        Assert.Equal("Manor", unit.Value);
        Assert.Equal("0", unit.Attribute("costratio")?.Value);
        Assert.Equal("0", unit.Attribute("timeratio")?.Value);
    }

    [Fact]
    public void TerrainCreepHelpers_CreateCanonicalEntriesAndGenerators()
    {
        var terrainCreeps = new XElement("terraincreeps");
        var creep = MajorGodCatalog.AddTerrainCreep(terrainCreeps);
        creep.SetAttributeValue("creep", "GaiaCreep");
        var generator = MajorGodCatalog.AddTerrainCreepGenerator(creep, "TownCenter");

        Assert.Equal("terraincreep", creep.Name.LocalName);
        Assert.Equal("GaiaCreep", creep.Attribute("creep")?.Value);
        Assert.Equal("0", creep.Attribute("maxradius")?.Value);
        Assert.Equal("0", creep.Attribute("growthrate")?.Value);
        Assert.Equal("0", creep.Attribute("decayrate")?.Value);
        Assert.Equal("0", creep.Attribute("minupdateinterval")?.Value);
        Assert.Equal("0", creep.Attribute("maxupdateinterval")?.Value);
        Assert.Equal("protounit", generator.Name.LocalName);
        Assert.Equal("TownCenter", generator.Value);
    }

    [Fact]
    public void TerrainCreepEditor_UsesGameAndModCatalogsAndStructuredControls()
    {
        var editor = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "MajorGodEditorView.axaml.cs"));
        var window = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("BuildTerrainCreepsEditor", editor, StringComparison.Ordinal);
        Assert.Contains("CreateSpecialAttributeAutoComplete(creep, \"creep\", _terrainCreepNames)", editor, StringComparison.Ordinal);
        Assert.Contains("\"maxradius\"", editor, StringComparison.Ordinal);
        Assert.Contains("\"allowDisconnectedGrowth\"", editor, StringComparison.Ordinal);
        Assert.Contains("TerrainCreepBuildabilityValues", editor, StringComparison.Ordinal);
        Assert.Contains("Text = \"Update Interval (s)\"", editor, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Prevent Placement of\")", editor, StringComparison.Ordinal);
        Assert.Contains("BuildTerrainCreepPlacementOptions(intervalRow, creep);", editor, StringComparison.Ordinal);
        Assert.Contains("BuildTerrainCreepGenerators", editor, StringComparison.Ordinal);
        Assert.Contains("ValidateTerrainCreeps", editor, StringComparison.Ordinal);
        Assert.Contains("terrain_creep_types.xml.xmb", window, StringComparison.Ordinal);
        Assert.Contains("terrain_creep_types_mods.xml", window, StringComparison.Ordinal);
        Assert.Contains("terraincreepsmods", window, StringComparison.Ordinal);
        Assert.Contains("ReadBarXmbXml(entry, stream)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildingChainHelpers_PreserveCanonicalNestingAndOrdering()
    {
        var chain = new XElement("buildingchain");
        var anchor = MajorGodCatalog.AddBuildingChainAnchor(chain);
        var abundance = MajorGodCatalog.AddBuildingChainAbundanceVfx(chain);
        var autoBuild = MajorGodCatalog.AddBuildingChainAutoBuildVfx(chain);
        var tier = MajorGodCatalog.AddBuildingChainRateTier(chain);
        var building = MajorGodCatalog.AddChainableBuilding(chain);

        Assert.Equal(new[] { "anchor", "abundancevfx", "autobuildvfx", "chainablebuilding", "resourcegeneration" },
            chain.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("", anchor.Attribute("vfx")?.Value);
        Assert.Equal(new[] { "small", "medium", "large" }, abundance.Attributes().Select(attribute => attribute.Name.LocalName));
        Assert.Equal(new[] { "small", "medium", "large" }, autoBuild.Attributes().Select(attribute => attribute.Name.LocalName));
        Assert.Equal("tilerateperminutetier", tier.Name.LocalName);
        Assert.Equal("Favor", tier.Attribute("resource")?.Value);
        Assert.Equal("0", tier.Attribute("mintiles")?.Value);
        Assert.Equal("Favor", building.Attribute("generationresource")?.Value);
        Assert.Equal("0", building.Attribute("chainradius")?.Value);
    }

    [Fact]
    public void BuildingChainAndOnCastEditors_ExposeStructuredControlsAndValidation()
    {
        var code = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("BuildBuildingChainEditor", code, StringComparison.Ordinal);
        Assert.Contains("Anchor", code, StringComparison.Ordinal);
        Assert.Contains("Abundance VFX", code, StringComparison.Ordinal);
        Assert.Contains("Rate tile/minute", code, StringComparison.Ordinal);
        Assert.Contains("When number of tiles >", code, StringComparison.Ordinal);
        Assert.Contains("Chain Radius", code, StringComparison.Ordinal);
        Assert.Contains("Power Cost Reduction Factor on Cast", code, StringComparison.Ordinal);
        Assert.Contains("ValidateBuildingChain", code, StringComparison.Ordinal);
        Assert.Contains("ValidateBoundedFloatValue(onCastPowerCostFactor.Value", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CombatXpTierHelper_AppendsZeroBasedCanonicalTiers()
    {
        var combatXp = new XElement("combatxp");

        MajorGodCatalog.AddCombatXpTier(combatXp);
        MajorGodCatalog.AddCombatXpTier(combatXp, "2200");

        Assert.Equal(new[] { "0", "2200" }, combatXp.Elements("tier").Select(tier => tier.Value));
    }

    [Fact]
    public void BountyHelpers_CreateCanonicalEntriesAndOrdering()
    {
        var bounty = new XElement("bountyresourceearning");
        var reward = MajorGodCatalog.AddBountyElement(bounty, "bountyreward");
        var active = MajorGodCatalog.AddBountyElement(bounty, "active", "true");
        var excluded = MajorGodCatalog.AddBountyElement(bounty, "excludedtarget", "Building");
        var research = MajorGodCatalog.AddBountyElement(bounty, "researchreward");

        Assert.Equal(new[] { "active", "excludedtarget", "bountyreward", "researchreward" },
            bounty.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("true", active.Value);
        Assert.Equal("Building", excluded.Value);
        Assert.Equal("Damage", reward.Attribute("condition")?.Value);
        Assert.Equal("Favor", reward.Attribute("resourcetype")?.Value);
        Assert.Equal("all", research.Attribute("techtype")?.Value);
    }

    [Fact]
    public void BountyEditor_UsesStructuredCardsChipsCatalogsAndCombatXpResourceEncoding()
    {
        var code = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("BuildBountyResourceEarningEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateBountyActiveCheckBox", code, StringComparison.Ordinal);
        Assert.Contains("BuildBountyChipEditor", code, StringComparison.Ordinal);
        Assert.Contains("BuildBountyRewards", code, StringComparison.Ordinal);
        Assert.Contains("BuildBountyTargetMultipliers", code, StringComparison.Ordinal);
        Assert.Contains("BuildBountyResearchEntries", code, StringComparison.Ordinal);
        Assert.Contains("SetAttribute(entry, \"combatxp\", \"\")", code, StringComparison.Ordinal);
        Assert.Contains("new FuncDataTemplate<string>", code, StringComparison.Ordinal);
        Assert.Contains("FontWeight.Bold", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BonusUnitSpawningHelpers_CreateCanonicalGoalContributorsAndCosts()
    {
        var bonus = new XElement("bonusunitspawning");
        var goal = MajorGodCatalog.AddBonusUnitGoal(bonus);
        MajorGodCatalog.AddBonusGoalChild(goal, "reward", "MythUnit");
        MajorGodCatalog.AddBonusGoalChild(goal, "reward", "Hero");
        var contributor = MajorGodCatalog.AddBonusGoalChild(goal, "contributor", "0.5");
        var costs = MajorGodCatalog.AddBonusRewardPointCosts(goal);
        Assert.Equal("damagegoal", goal.Name.LocalName);
        goal.SetAttributeValue("name", "ShennongSpawn");
        goal.Name = "resourcegoal";
        var displayInfo = MajorGodCatalog.AddBonusDisplayInfo(goal);

        Assert.Equal("Single", goal.Attribute("rewardtracking")?.Value);
        Assert.Equal(new[] { "MythUnit", "Hero" }, goal.Elements("reward").Select(reward => reward.Value));
        Assert.Equal("DamageTarget", goal.Element("spawnlocation")?.Attribute("type")?.Value);
        Assert.Equal("", contributor.Attribute("unit")?.Value);
        Assert.Equal(new[] { "Food", "Wood", "Gold", "Favor" },
            costs.Select(cost => cost.Attribute("resourcetype")?.Value));
        Assert.Equal("true", goal.Element("displayonhud")?.Value);
        Assert.Equal("STR_SHENNONGSPAWN_SPAWN_REWARD", displayInfo.Element("displaynameid")?.Value);
        Assert.Equal("STR_SHENNONGSPAWN_SPAWN_REWARD_NOTIFICATION", displayInfo.Element("notificationmessageid")?.Value);
    }

    [Fact]
    public void BonusUnitSpawningEditor_UsesStructuredGoalControlsAndSharedEditors()
    {
        var code = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("BuildBonusUnitSpawningEditor", code, StringComparison.Ordinal);
        Assert.Contains("PerPossibleReward", code, StringComparison.Ordinal);
        Assert.Contains("Excluded from reward", code, StringComparison.Ordinal);
        Assert.Contains("AddBonusChips(row, goal, \"reward\", \"Reward\"", code, StringComparison.Ordinal);
        Assert.Contains("Excluded Targets can only be used by Damage goals", code, StringComparison.Ordinal);
        Assert.Contains("Reward Point Cost", code, StringComparison.Ordinal);
        Assert.Contains("Spawn Location", code, StringComparison.Ordinal);
        Assert.Contains("Add contributor", code, StringComparison.Ordinal);
        Assert.Contains("BonusGoalFlags", code, StringComparison.Ordinal);
        Assert.Contains("BuildBonusDisplayInfo", code, StringComparison.Ordinal);
        Assert.Contains("ShieldBlessing", code, StringComparison.Ordinal);
        Assert.Contains("CreateBonusContributorResourceBox", code, StringComparison.Ordinal);
        Assert.Contains("CreateBonusFieldPair", code, StringComparison.Ordinal);
        Assert.Contains("CreateBonusDisplayIconEditor", code, StringComparison.Ordinal);
        Assert.Contains("NormalizeIconCatalogValue", code, StringComparison.Ordinal);
        Assert.Contains("DisplayOnHUD", code, StringComparison.Ordinal);
        Assert.Contains("RenameBonusGoalGeneratedStringsAsync", code, StringComparison.Ordinal);
        Assert.Contains("ScheduleBonusDisplayStringRemoval", code, StringComparison.Ordinal);
        Assert.Contains("MarkAndRebuildBonusUnitSpawning", code, StringComparison.Ordinal);
        Assert.Contains("_saveStringsAsync!(stringUpdates, stringRemovals)", code, StringComparison.Ordinal);
        Assert.Contains("AddBonusSubcontainer(host, \"Goal\"", code, StringComparison.Ordinal);
        Assert.Contains("AddBonusSubcontainer(host, \"Display info\"", code, StringComparison.Ordinal);
        Assert.Contains("\"rolloverid\", \"SPAWN_REWARD_LR\", 380", code, StringComparison.Ordinal);
        Assert.Contains("\"notificationmessageid\", \"SPAWN_REWARD_NOTIFICATION\", 380", code, StringComparison.Ordinal);
        var identityStart = code.IndexOf("private void BuildBonusGoalIdentity", StringComparison.Ordinal);
        var identityEnd = code.IndexOf("private void BuildBonusGoalReward", identityStart, StringComparison.Ordinal);
        var identityCode = code[identityStart..identityEnd];
        Assert.True(identityCode.IndexOf("CreateBonusFieldPair(\"Type\"", StringComparison.Ordinal) <
                    identityCode.IndexOf("CreateBonusFieldPair(\"Amount\"", StringComparison.Ordinal));
        Assert.True(identityCode.IndexOf("CreateBonusFieldPair(\"Amount\"", StringComparison.Ordinal) <
                    identityCode.IndexOf("CreateBonusFieldPair(\"Tracking\"", StringComparison.Ordinal));
        Assert.Contains("ValidateBonusUnitSpawning", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_WiresGodMenuAndEntityNavigationButtons()
    {
        var root = ProjectSource.RootPath;
        var document = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
        Assert.Contains(buttons, button => (string?)button.Attribute("Content") == "Edit / View" &&
                                           (string?)button.Attribute("Click") == "Gods_Click");
        Assert.Contains(buttons, button => (string?)button.Attribute("Content") == "Cultures" &&
                                           (string?)button.Attribute("Click") == "GodsCultures_Click");
        Assert.Contains(buttons, button => (string?)button.Attribute(x + "Name") == "_godsEntityButton" &&
                                           (string?)button.Attribute("Click") == "GodsEntity_Click" &&
                                           button.Attribute("IsEnabled") == null);
    }

}
