using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitCommandRegressionTests
{
    [Fact]
    public void CommandDefinition_PreservesUnknownChildrenWhileUpdatingKnownFields()
    {
        var source = XElement.Parse("""
            <protounitcommand FutureAttribute="keep">
              <name>OldName</name>
              <associatedtech>OldTech</associatedtech>
              <futuretag custom="42">KeepMe</futuretag>
              <activeicon>legacy.png</activeicon>
              <transform></transform>
            </protounitcommand>
            """);

        var definition = ProtoUnitCommandDefinition.FromElement(source);
        definition.Name = "NewName";
        definition.Values["associatedtech"] = "NewTech";
        definition.Flags.Remove("transform");

        var saved = definition.ToElement();

        Assert.Equal("keep", saved.Attribute("FutureAttribute")?.Value);
        var future = Assert.Single(saved.Elements("futuretag"));
        Assert.Equal("42", future.Attribute("custom")?.Value);
        Assert.Equal("KeepMe", future.Value);
        Assert.Equal("legacy.png", saved.Element("activeicon")?.Value);
        Assert.Equal("NewName", saved.Element("name")?.Value);
        Assert.Equal("NewTech", saved.Element("associatedtech")?.Value);
        Assert.Null(saved.Element("transform"));
    }

    [Fact]
    public void CommandDefinition_PreservesRepeatableValuesAndExpandsFlagsToLongForm()
    {
        var definition = new ProtoUnitCommandDefinition { Name = "TestCommand" };
        definition.RepeatableValues["sharedcommand"] = ["One", "Two"];
        definition.RepeatableValues["removecommandprequeueonprequeue"] = ["Three"];
        definition.Flags.UnionWith(["unitcommand", "displayontarget"]);

        var xml = ProtoUnitCommandDefinition.ExpandEmptyFlagElements(definition.ToElement().ToString());
        var saved = XElement.Parse(xml);

        Assert.Equal(new[] { "One", "Two" }, saved.Elements("sharedcommand").Select(element => element.Value).ToArray());
        Assert.Equal("Three", Assert.Single(saved.Elements("removecommandprequeueonprequeue")).Value);
        Assert.Contains("<unitcommand></unitcommand>", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<displayontarget></displayontarget>", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransformDefinition_PreservesUnknownContentAndManagedValues()
    {
        var source = XElement.Parse("""
            <transform FutureAttribute="keep">
              <from>OldFrom</from>
              <to>OldTo</to>
              <futuretag custom="42">KeepMe</futuretag>
              <command>OldCommand</command>
            </transform>
            """);
        var definition = ProtoUnitTransformDefinition.FromElement(source);
        definition.From = "Hoplite";
        definition.To = "Wolf";
        definition.Tech = "ArchaicAgeGreek";
        definition.Command = "HopliteToWolf";
        definition.RevertOthersTo = "Hoplite";
        definition.FullHeal = true;

        var saved = definition.ToElement();

        Assert.Equal("keep", saved.Attribute("FutureAttribute")?.Value);
        Assert.Equal("true", saved.Attribute("fullheal")?.Value);
        Assert.Equal("KeepMe", Assert.Single(saved.Elements("futuretag")).Value);
        Assert.Equal("Hoplite", saved.Element("from")?.Value);
        Assert.Equal("Wolf", saved.Element("to")?.Value);
        Assert.Equal("ArchaicAgeGreek", saved.Element("tech")?.Value);
        Assert.Equal("HopliteToWolf", saved.Element("command")?.Value);
        Assert.Equal("Hoplite", saved.Element("revertothersto")?.Value);
    }

    [Fact]
    public void TransformDefinition_FalseFullHealAndEmptyRevertAreOmitted()
    {
        var source = XElement.Parse("""
            <transform fullheal="true">
              <from>Hoplite</from><to>Wolf</to><command>HopliteToWolf</command>
              <revertothersto>Hoplite</revertothersto>
            </transform>
            """);
        var definition = ProtoUnitTransformDefinition.FromElement(source);
        definition.FullHeal = false;
        definition.RevertOthersTo = "";

        var saved = definition.ToElement();

        Assert.Null(saved.Attribute("fullheal"));
        Assert.Null(saved.Element("revertothersto"));
    }

    [Fact]
    public void NewTransformDefinition_WritesManagedChildrenInCanonicalOrder()
    {
        var definition = new ProtoUnitTransformDefinition
        {
            From = "Hoplite",
            To = "Wolf",
            Tech = "ArchaicAgeGreek",
            Command = "HopliteToWolf",
            RevertOthersTo = "Hoplite"
        };

        Assert.Equal(
            new[] { "from", "to", "tech", "command", "revertothersto" },
            definition.ToElement().Elements().Select(element => element.Name.LocalName).ToArray());
    }

    [Fact]
    public void UniqueTransformAssignment_WritesCommandAndSeparateTransformCommand()
    {
        var unit = new XElement("unit", new XAttribute("name", "Hoplite"));
        ProtoXmlHandler.SetCommandEntries(unit,
        [
            new ProtoCommandEntry { Value = "HopliteToWolf", Row = "2", Column = "4" }
        ]);
        ProtoXmlHandler.SetTransformCommandEntry(unit,
            new ProtoCommandEntry { Value = "HopliteToWolf" });

        var command = Assert.Single(unit.Elements("command"));
        var transformCommand = Assert.Single(unit.Elements("transformcommand"));
        Assert.Equal("2", command.Attribute("row")?.Value);
        Assert.Equal("4", command.Attribute("column")?.Value);
        Assert.Equal("HopliteToWolf", command.Value);
        Assert.Null(transformCommand.Attribute("row"));
        Assert.Null(transformCommand.Attribute("column"));
        Assert.Equal("HopliteToWolf", transformCommand.Value);
    }

    [Fact]
    public void MultipleTransformAssignment_UsesNormalCommandWithoutTransformCommand()
    {
        var unit = new XElement("unit", new XAttribute("name", "Hoplite"));
        ProtoXmlHandler.SetCommandEntries(unit,
        [
            new ProtoCommandEntry { Value = "HopliteTransformMultiple", Row = "1", Column = "3" }
        ]);
        ProtoXmlHandler.SetTransformCommandEntry(unit, null);

        Assert.Single(unit.Elements("command"));
        Assert.Empty(unit.Elements("transformcommand"));
    }

    [Fact]
    public void SemanticComparison_IgnoresFormattingAndAttributeOrderButNotValues()
    {
        var first = XElement.Parse("""
            <protounitcommand b="2" a="1">
              <name>Test</name>
              <unitcommand></unitcommand>
            </protounitcommand>
            """, LoadOptions.PreserveWhitespace);
        var formattedDifferently = XElement.Parse("<protounitcommand a=\"1\" b=\"2\"><name>Test</name><unitcommand /></protounitcommand>", LoadOptions.PreserveWhitespace);
        var changed = XElement.Parse("<protounitcommand a=\"1\" b=\"2\"><name>Changed</name><unitcommand /></protounitcommand>", LoadOptions.PreserveWhitespace);

        Assert.True(XNode.DeepEquals(
            ProtoEditorWindow.NormalizeXmlElementForComparison(first),
            ProtoEditorWindow.NormalizeXmlElementForComparison(formattedDifferently)));
        Assert.False(XNode.DeepEquals(
            ProtoEditorWindow.NormalizeXmlElementForComparison(first),
            ProtoEditorWindow.NormalizeXmlElementForComparison(changed)));
    }

    [Fact]
    public void TransformModRead_MalformedExistingFileThrowsWithoutReplacingIt()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "unit_transform_mods.xml");
        const string malformed = "<unittransformmods><transform></unittransformmods>";
        File.WriteAllText(path, malformed);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ProtoEditorWindow.LoadProtoUnitTransformModDocumentForRead(path));

        Assert.Contains("could not be read safely", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(malformed, File.ReadAllText(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AoMDivineDataEditor.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
