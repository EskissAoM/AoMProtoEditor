using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
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
    public void MainWindow_WiresBothGodNavigationButtons()
    {
        var root = FindProjectRoot();
        var document = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
        Assert.Contains(buttons, button => (string?)button.Attribute("Content") == "Gods" &&
                                           (string?)button.Attribute("Click") == "Gods_Click");
        Assert.Contains(buttons, button => (string?)button.Attribute(x + "Name") == "_godsEntityButton" &&
                                           (string?)button.Attribute("Click") == "GodsEntity_Click" &&
                                           button.Attribute("IsEnabled") == null);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
