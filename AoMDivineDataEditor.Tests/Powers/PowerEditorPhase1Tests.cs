using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorPhase1Tests
{
    [Fact]
    public void SharedCatalogKeepsEveryOriginalPowerAndFiltersOnlyModEntries()
    {
        var originals = new[]
        {
            XDocument.Parse("""
                <powers>
                  <power name="OriginalGodPower" type="GodPower" />
                  <power name="OriginalUnitAction" type="UnitAction" />
                  <power name="OriginalGeneralEffect" type="GeneralEffect" />
                </powers>
                """)
        };
        var mods = new[]
        {
            XDocument.Parse("""
                <powersmod>
                  <power name="CustomGodPower" type="GodPower" />
                  <power name="CustomUnitAction" type="UnitAction" />
                  <power name="IncludedGeneralEffect" type="GeneralEffect" godpower="" />
                  <power name="ExcludedGeneralEffect" type="GeneralEffect" />
                </powersmod>
                """)
        };

        Assert.Equal(
            ["CustomGodPower", "IncludedGeneralEffect", "OriginalGeneralEffect", "OriginalGodPower", "OriginalUnitAction"],
            PowerCatalog.ExtractCatalogNames(originals, mods));
    }

    [Fact]
    public void PreviewFormattingAlignsTheClosingTagWithoutMutatingSourceTrivia()
    {
        var source = XElement.Parse("<power name=\"P\">\n  <value>1</value>\n  </power>", LoadOptions.PreserveWhitespace);
        var original = source.ToString(SaveOptions.DisableFormatting);

        var preview = PowerCatalog.FormatPreview(source).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal("<power name=\"P\">", preview.Split('\n')[0]);
        Assert.Equal("</power>", preview.Split('\n')[^1]);
        Assert.Equal(original, source.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void NewEntriesUseSeparateSectionsWithoutMovingExistingRecords()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <!-- user metadata -->
              <power name="ExistingPower" type="GodPower" />
              <power name="ExistingAbility" type="UnitAction" />
            </powersmod>
            """, LoadOptions.PreserveWhitespace);
        var existingOrder = document.Root!.Elements("power")
            .Select(PowerCatalog.GetName)
            .ToList();

        PowerCatalog.AddNewEntry(document,
            new XElement("power", new XAttribute("name", "NewPower")),
            PowerDocumentSection.Powers);
        PowerCatalog.AddNewEntry(document,
            new XElement("power", new XAttribute("name", "NewAbility"), new XAttribute("type", "UnitAction")),
            PowerDocumentSection.Abilities);
        PowerCatalog.AddNewEntry(document,
            new XElement("power", new XAttribute("name", "SecondPower")),
            PowerDocumentSection.Powers);

        var nodes = document.Root.Nodes().ToList();
        var powersMarker = nodes.FindIndex(node => node is XComment comment &&
            comment.Value.Trim() == PowerCatalog.GetSectionMarker(PowerDocumentSection.Powers));
        var abilitiesMarker = nodes.FindIndex(node => node is XComment comment &&
            comment.Value.Trim() == PowerCatalog.GetSectionMarker(PowerDocumentSection.Abilities));
        var names = nodes.OfType<XElement>().Select(PowerCatalog.GetName).ToList();

        Assert.Equal(["ExistingPower", "ExistingAbility"], existingOrder);
        Assert.Equal(["ExistingPower", "ExistingAbility", "NewPower", "SecondPower", "NewAbility"], names);
        Assert.True(powersMarker > nodes.FindIndex(node => node is XElement element && PowerCatalog.GetName(element) == "ExistingAbility"));
        Assert.True(abilitiesMarker > nodes.FindIndex(node => node is XElement element && PowerCatalog.GetName(element) == "SecondPower"));
        Assert.Single(nodes.OfType<XComment>(), comment =>
            comment.Value.Trim() == PowerCatalog.GetSectionMarker(PowerDocumentSection.Powers));
        Assert.Single(nodes.OfType<XComment>(), comment =>
            comment.Value.Trim() == PowerCatalog.GetSectionMarker(PowerDocumentSection.Abilities));
    }

    [Theory]
    [InlineData(PowerDocumentSection.Abilities)]
    [InlineData(PowerDocumentSection.Powers)]
    public void SavedNewSectionAndPowerAlignWithExistingEntriesAndRootClose(PowerDocumentSection section)
    {
        using var workspace = new TestWorkspace(nameof(SavedNewSectionAndPowerAlignWithExistingEntriesAndRootClose));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Existing" type="GodPower">
                <value>1</value>
              </power>
            </powersmod>
            """);
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        PowerCatalog.AddNewEntry(document,
            XElement.Parse("<power name=\"Added\">\n    <value>2</value>\n  </power>", LoadOptions.PreserveWhitespace),
            section);

        PreservedXmlFile.SaveAtomic(document, path);

        var output = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("\n  <!-- " + PowerCatalog.GetSectionMarker(section) + " -->\n", output, StringComparison.Ordinal);
        Assert.Equal("  <power name=\"Added\">", output.Split('\n').Single(line => line.Contains("name=\"Added\"", StringComparison.Ordinal)));
        Assert.Contains("\n  </power>\n</powersmod>", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PowerDocumentSection.Abilities)]
    [InlineData(PowerDocumentSection.Powers)]
    public void TabIndentedDocumentsAlignNewEntryAndExistingEditorMarker(PowerDocumentSection section)
    {
        using var workspace = new TestWorkspace(nameof(TabIndentedDocumentsAlignNewEntryAndExistingEditorMarker));
        var marker = PowerCatalog.GetSectionMarker(section);
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod>\n" +
            "\t<power name=\"Existing\" type=\"GodPower\">\n" +
            "\t\t<value>1</value>\n" +
            "\t</power>\n\n" +
            "  <!-- " + marker + " -->\n" +
            "  <power name=\"EarlierAddition\" />\n" +
            "</powersmod>\n");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);

        PowerCatalog.AddNewEntry(document,
            XElement.Parse("<power name=\"Added\">\n\t\t<value>2</value>\n\t</power>", LoadOptions.PreserveWhitespace),
            section);
        PreservedXmlFile.SaveAtomic(document, path);

        var lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Contains("\t<!-- " + marker + " -->", lines);
        Assert.Contains("\t<power name=\"Added\">", lines);
        Assert.Contains("\t</power>", lines);
        Assert.Contains("</powersmod>", lines);
        Assert.Contains("  <power name=\"EarlierAddition\" />", lines);
    }

    [Theory]
    [InlineData(PowerDocumentSection.Abilities)]
    [InlineData(PowerDocumentSection.Powers)]
    public void TabIndentedDocumentsCreateAlignedSection(PowerDocumentSection section)
    {
        using var workspace = new TestWorkspace(nameof(TabIndentedDocumentsCreateAlignedSection));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod>\n\t<power name=\"Existing\" type=\"GodPower\" />\n</powersmod>\n");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);

        PowerCatalog.AddNewEntry(document,
            new XElement("power", new XAttribute("name", "Added")), section);
        PreservedXmlFile.SaveAtomic(document, path);

        var lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Contains("\t<!-- " + PowerCatalog.GetSectionMarker(section) + " -->", lines);
        Assert.Contains("\t<power name=\"Added\" />", lines);
        Assert.Contains("</powersmod>", lines);
    }

    [Fact]
    public void PowerEditorUsesSharedBrowserDocumentAndRawXmlSystems()
    {
        var mainXaml = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml");
        var mainCode = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var viewXaml = ProjectSource.Read("Windows", "PowerEditorView.axaml");
        var viewCode = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");

        Assert.Contains("Content=\"Powers\" Click=\"PowersEntity_Click\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("ShowEntityKindAsync(EditorEntityKind.Powers)", mainCode, StringComparison.Ordinal);
        Assert.Contains("GetPowerNames(modified: selectedIndex == 1)", mainCode, StringComparison.Ordinal);
        Assert.Contains("SelectPower(selectedName)", mainCode, StringComparison.Ordinal);
        Assert.Contains("AddPowerAsync(duplicateSelected: true)", mainCode, StringComparison.Ordinal);
        Assert.Contains("DeleteCurrentPowerAsync", mainCode, StringComparison.Ordinal);
        Assert.Contains("await _powerView.SaveAsync()", mainCode, StringComparison.Ordinal);
        Assert.Contains("PowerCatalog.ModFileName", mainCode, StringComparison.Ordinal);
        Assert.Equal(4, mainCode.Split(
            "PowerCatalog.AddNewEntry(doc, replacement, PowerDocumentSection.Abilities)",
            StringSplitOptions.None).Length - 1
            + mainCode.Split(
                "PowerCatalog.AddNewEntry(doc, power, PowerDocumentSection.Abilities)",
                StringSplitOptions.None).Length - 1
            + mainCode.Split(
                "PowerCatalog.AddNewEntry(powerFile.Document, clone, PowerDocumentSection.Abilities)",
                StringSplitOptions.None).Length - 1);

        Assert.Contains("Text=\"XML Preview\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Properties\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Specific\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("<TextBox Grid.Column=\"1\" x:Name=\"_nameBox\" Width=\"380\"", viewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"Name\"", viewXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"96\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("_displayNameBox", viewXaml, StringComparison.Ordinal);
        Assert.Contains("_rolloverBox", viewXaml, StringComparison.Ordinal);
        Assert.Contains("_activeRolloverBox", viewXaml, StringComparison.Ordinal);
        Assert.Contains("_iconEditor", viewXaml, StringComparison.Ordinal);
        Assert.Contains("_iconPreviewHost", viewXaml, StringComparison.Ordinal);
        foreach (var field in new[] { "_activeTimeBox", "_buildUpBox", "_shutDownBox", "_costBox",
                     "_repeatCostBox", "_limitGlobalBox", "_limitPlayerBox", "_singleActivationCheck",
                     "_exclusiveCheck", "_exclusiveMessageBox" })
            Assert.Contains(field, viewXaml, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(_rawXmlEditor)", viewCode, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> CommitCurrentPowerAsync()", viewCode, StringComparison.Ordinal);
        Assert.Contains("XElement.Parse(_rawXmlEditor.Text", viewCode, StringComparison.Ordinal);
        Assert.Contains("ModXmlDocumentKind.Powers", viewCode, StringComparison.Ordinal);
        Assert.Contains("NamedXmlExternalDocumentMergePolicy.Merge", viewCode, StringComparison.Ordinal);
        Assert.Contains("PowerDocumentSection.Powers", viewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip.SetTip(box, id)", viewCode, StringComparison.Ordinal);
    }
}
