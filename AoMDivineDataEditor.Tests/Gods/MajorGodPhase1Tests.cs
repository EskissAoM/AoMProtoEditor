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
    public void SharedFieldUi_KeepsFileOrderAndRawXmlEditor()
    {
        var root = ProjectSource.RootPath;
        var xaml = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml"));
        var labels = new[]
        {
            "Text=\"Name\"", "Text=\"Key\"", "Text=\"Culture\"", "Text=\"Display Name\"",
            "Text=\"Rollover Name\"", "Text=\"Title\"", "Text=\"Icon\"", "Text=\"Portrait\"",
            "Text=\"Breakout Portrait\"", "Text=\"Minimap Border Image\"", "Text=\"Background Scenario\""
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
                    modStringsPath: stringsPath);
                view.SetModifiedMode(true);
                view.SelectMajorGod("ZeusMod");
                await Task.Yield();

                Assert.Equal("Resolved Zeus", view.FindControl<TextBox>("_displayNameBox")!.Text);
                Assert.Equal("Resolved rollover", view.FindControl<TextBox>("_rolloverNameBox")!.Text);
                Assert.Equal("Resolved title", view.FindControl<TextBox>("_titleBox")!.Text);
                Assert.Equal("resources\\greek\\zeus_icon.png", view.FindControl<AssetPathEditor>("_iconEditor")!.FullValue);

                view.FindControl<TextBox>("_keyBox")!.Text = "n";
                await Task.Yield();
                Assert.Equal("N", view.FindControl<TextBox>("_keyBox")!.Text);
                var rawXml = view.FindControl<TextEditor>("_rawXmlEditor")!.Text;
                Assert.Contains("<statsid>SP</statsid>", rawXml, StringComparison.Ordinal);
                Assert.Contains("<key>N</key>", rawXml, StringComparison.Ordinal);
                Assert.Contains("<custommechanic foo=\"keep\">value</custommechanic>", rawXml, StringComparison.Ordinal);

                view.FindControl<TextBox>("_displayNameBox")!.Text = "Edited Zeus";
                await Task.Yield();
                Assert.True(await view.SaveAsync());
            });

            Assert.Equal("Edited Zeus", savedStrings?.GetValueOrDefault("STR_CIV_ZEUS_MOD"));
            var savedDocument = XDocument.Load(modPath, LoadOptions.PreserveWhitespace);
            var savedEntry = MajorGodCatalog.Find(savedDocument, "ZeusMod");
            Assert.NotNull(savedEntry);
            Assert.Equal("SP", savedEntry!.Element("statsid")?.Value);
            Assert.Equal("N", savedEntry.Element("key")?.Value);
            Assert.Equal("keep", savedEntry.Element("custommechanic")?.Attribute("foo")?.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MainWindow_WiresBothGodNavigationButtons()
    {
        var root = ProjectSource.RootPath;
        var document = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
        Assert.Contains(buttons, button => (string?)button.Attribute("Content") == "Gods" &&
                                           (string?)button.Attribute("Click") == "Gods_Click");
        Assert.Contains(buttons, button => (string?)button.Attribute(x + "Name") == "_godsEntityButton" &&
                                           (string?)button.Attribute("Click") == "GodsEntity_Click" &&
                                           button.Attribute("IsEnabled") == null);
    }

}
