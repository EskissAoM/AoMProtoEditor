using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoXmlPreservationTests
{
    [Fact]
    public void SaveProtoXml_preserves_root_and_unit_comments_processing_instructions_and_unknown_nodes()
    {
        using var temp = new TestWorkspace(nameof(ProtoXmlPreservationTests));
        var path = temp.GetPath("proto_mods.xml");
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<?dde preserve-me?>\r\n" +
            "<protomods custom=\"root-attribute\">\r\n" +
            "  <!-- GREEK -->\r\n" +
            "  <customroot enabled=\"1\">keep me</customroot>\r\n" +
            "  <unit name=\"TestUnit\" custom=\"unit-attribute\">\r\n" +
            "    <!-- before hitpoints -->\r\n" +
            "    <maxhitpoints>10</maxhitpoints> <!-- explain hitpoints -->\r\n" +
            "    <!-- <armor type=\"Hack\" value=\"0.5\" /> -->\r\n" +
            "    <customfield mode=\"manual\">unchanged</customfield>\r\n" +
            "  </unit>\r\n" +
            "  <!-- END -->\r\n" +
            "</protomods>\r\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));

        var (document, root) = ProtoXmlHandler.ParseProtoXml(path);
        ProtoXmlHandler.SetSimpleField(root.Element("unit")!, "maxhitpoints", "20");
        ProtoXmlHandler.SaveProtoXml(document, path);

        var savedText = File.ReadAllText(path, Encoding.UTF8);
        var savedDocument = XDocument.Parse(savedText, LoadOptions.PreserveWhitespace);
        var savedRoot = savedDocument.Root!;
        var savedUnit = savedRoot.Element("unit")!;

        Assert.NotNull(savedDocument.Declaration);
        Assert.Equal("root-attribute", (string?)savedRoot.Attribute("custom"));
        Assert.Equal("keep me", savedRoot.Element("customroot")?.Value);
        Assert.Equal("unit-attribute", (string?)savedUnit.Attribute("custom"));
        Assert.Equal("20", savedUnit.Element("maxhitpoints")?.Value);
        Assert.Equal("unchanged", savedUnit.Element("customfield")?.Value);
        Assert.Contains(savedDocument.Nodes().OfType<XProcessingInstruction>(), instruction =>
            instruction.Target == "dde" && instruction.Data == "preserve-me");
        Assert.Equal(
            new[] { " GREEK ", " before hitpoints ", " explain hitpoints ", " <armor type=\"Hack\" value=\"0.5\" /> ", " END " },
            savedDocument.DescendantNodes().OfType<XComment>().Select(comment => comment.Value).ToArray());

        Assert.True(savedText.IndexOf("<!-- GREEK -->", StringComparison.Ordinal) <
                    savedText.IndexOf("<customroot", StringComparison.Ordinal));
        Assert.True(savedText.IndexOf("<customroot", StringComparison.Ordinal) <
                    savedText.IndexOf("<unit", StringComparison.Ordinal));
        Assert.Contains("<!-- <armor type=\"Hack\" value=\"0.5\" /> -->", savedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", savedText.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_preserves_utf16_bom_and_crlf()
    {
        using var temp = new TestWorkspace(nameof(ProtoXmlPreservationTests));
        var path = temp.GetPath("proto_mods.xml");
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
            "<protomods>\r\n" +
            "  <!-- préserver -->\r\n" +
            "  <unit name=\"Test\"><maxhitpoints>10</maxhitpoints></unit>\r\n" +
            "</protomods>\r\n";
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());

        var (document, root) = ProtoXmlHandler.ParseProtoXml(path);
        ProtoXmlHandler.SetSimpleField(root.Element("unit")!, "maxhitpoints", "11");
        ProtoXmlHandler.SaveProtoXml(document, path);

        var savedBytes = File.ReadAllBytes(path);
        Assert.True(savedBytes.AsSpan().StartsWith(encoding.GetPreamble()));
        var savedText = encoding.GetString(savedBytes, encoding.GetPreamble().Length, savedBytes.Length - encoding.GetPreamble().Length);
        Assert.Contains("<!-- préserver -->\r\n", savedText, StringComparison.Ordinal);
        Assert.Contains("<maxhitpoints>11</maxhitpoints>", savedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", savedText.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_preserves_bomless_utf16()
    {
        using var temp = new TestWorkspace(nameof(ProtoXmlPreservationTests));
        var path = temp.GetPath("proto_mods.xml");
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>\n" +
            "<protomods><!-- keep --><unit name=\"Test\"><maxhitpoints>10</maxhitpoints></unit></protomods>";
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
        File.WriteAllBytes(path, encoding.GetBytes(source));

        var (document, root) = ProtoXmlHandler.ParseProtoXml(path);
        ProtoXmlHandler.SetSimpleField(root.Element("unit")!, "maxhitpoints", "12");
        ProtoXmlHandler.SaveProtoXml(document, path);

        var savedBytes = File.ReadAllBytes(path);
        Assert.False(savedBytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()));
        Assert.True(savedBytes.AsSpan().StartsWith(new byte[] { 0x3C, 0x00 }));
        var savedText = encoding.GetString(savedBytes);
        Assert.Contains("<!-- keep -->", savedText, StringComparison.Ordinal);
        Assert.Contains("<maxhitpoints>12</maxhitpoints>", savedText, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_atomic_temporary_output_uses_the_destination_format_source()
    {
        using var temp = new TestWorkspace(nameof(ProtoXmlPreservationTests));
        var destinationPath = temp.GetPath("proto_mods.xml");
        var temporaryPath = destinationPath + ".tmp";
        const string source = "<protomods>\r\n  <!-- keep -->\r\n</protomods>\r\n";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllBytes(destinationPath, encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        document.Root!.Add(new XElement("unit", new XAttribute("name", "NewUnit")));

        ProtoXmlHandler.SaveProtoXml(document, temporaryPath, formattingSourcePath: destinationPath);

        var savedBytes = File.ReadAllBytes(temporaryPath);
        Assert.True(savedBytes.AsSpan().StartsWith(encoding.GetPreamble()));
        var savedText = Encoding.UTF8.GetString(savedBytes, encoding.GetPreamble().Length, savedBytes.Length - encoding.GetPreamble().Length);
        Assert.Contains("<!-- keep -->\r\n", savedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", savedText.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_does_not_modify_the_in_memory_document_to_expand_armor_tags()
    {
        using var temp = new TestWorkspace(nameof(ProtoXmlPreservationTests));
        var path = temp.GetPath("proto_mods.xml");
        var armor = new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.2"));
        var document = new XDocument(new XElement("protomods",
            new XElement("unit", new XAttribute("name", "Test"), armor)));

        ProtoXmlHandler.SaveProtoXml(document, path);

        Assert.True(armor.IsEmpty);
        Assert.Empty(armor.Nodes());
        Assert.Contains("<armor type=\"Hack\" value=\"0.2\"></armor>", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SetSimpleField_keeps_comments_inside_the_edited_element()
    {
        var unit = XElement.Parse(
            "<unit><maxhitpoints><!-- keep before -->10<!-- keep after --></maxhitpoints></unit>");

        ProtoXmlHandler.SetSimpleField(unit, "maxhitpoints", "20");

        var hitpoints = unit.Element("maxhitpoints")!;
        Assert.Equal("20", hitpoints.Value);
        Assert.Equal(
            new[] { " keep before ", " keep after " },
            hitpoints.Nodes().OfType<XComment>().Select(comment => comment.Value).ToArray());
    }

    [Fact]
    public void StatsPreserver_keeps_comments_inside_rebuilt_managed_elements()
    {
        var original = XElement.Parse(
            "<unit><cost resourcetype=\"Food\"><!-- keep cost note -->10</cost></unit>",
            LoadOptions.PreserveWhitespace);
        var updated = new XElement("unit",
            new XElement("cost", new XAttribute("resourcetype", "Food"), "20"));

        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        var cost = Assert.Single(updated.Elements("cost"));
        Assert.Equal("20", cost.Value);
        Assert.Equal(" keep cost note ", Assert.Single(cost.Nodes().OfType<XComment>()).Value);
    }

    [Fact]
    public void StatsPreserver_restores_the_relative_order_of_surviving_and_unknown_elements()
    {
        var original = XElement.Parse(
            "<unit>\n" +
            "  <cost resourcetype=\"Food\">10</cost>\n" +
            "  <!-- custom content remains here -->\n" +
            "  <customfield>keep</customfield>\n" +
            "  <cost resourcetype=\"CustomResource\">7</cost>\n" +
            "  <armor type=\"Hack\" value=\"0.1\" />\n" +
            "</unit>",
            LoadOptions.PreserveWhitespace);
        var updated = new XElement("unit",
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.2")),
            new XElement("customfield", "keep"),
            new XElement("cost", new XAttribute("resourcetype", "Food"), "15"));

        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        Assert.Equal(
            new[] { "cost", "customfield", "cost", "armor" },
            updated.Elements().Select(element => element.Name.LocalName).ToArray());
        Assert.Equal(
            "CustomResource",
            (string?)updated.Elements("cost").ElementAt(1).Attribute("resourcetype"));
        var nodes = updated.Nodes().ToList();
        Assert.True(nodes.IndexOf(updated.Elements("cost").First()) <
                    nodes.IndexOf(updated.Nodes().OfType<XComment>().Single()));
        Assert.True(nodes.IndexOf(updated.Nodes().OfType<XComment>().Single()) <
                    nodes.IndexOf(updated.Element("customfield")!));
    }

    [Fact]
    public void SetProtoActions_keeps_source_comments_processing_instructions_and_unknown_content()
    {
        var sourceAction = XElement.Parse(
            "<protoaction custom=\"preserved\">\n" +
            "  <name>Attack</name>\n" +
            "  <!-- between name and type -->\n" +
            "  <type>HandAttack</type>\n" +
            "  <?dde inside-action?>\n" +
            "  <customfield mode=\"manual\">keep</customfield>\n" +
            "</protoaction>",
            LoadOptions.PreserveWhitespace);
        var action = ProtoXmlHandler.ParseProtoActionLikeElement(sourceAction);
        action.Rof = "1.5";
        var unit = new XElement("unit");

        ProtoXmlHandler.SetProtoActions(unit, [action]);

        var savedAction = Assert.Single(unit.Elements("protoaction"));
        Assert.Equal("preserved", (string?)savedAction.Attribute("custom"));
        Assert.Equal("Attack", savedAction.Element("name")?.Value);
        Assert.Equal("HandAttack", savedAction.Element("type")?.Value);
        Assert.Equal("1.5", savedAction.Element("rof")?.Value);
        Assert.Equal("keep", savedAction.Element("customfield")?.Value);
        Assert.Equal(" between name and type ", Assert.Single(savedAction.Nodes().OfType<XComment>()).Value);
        Assert.Contains(savedAction.Nodes().OfType<XProcessingInstruction>(), instruction =>
            instruction.Target == "dde" && instruction.Data == "inside-action");
        Assert.Contains(savedAction.Nodes().OfType<XText>(), text => text.Value.Contains('\n'));

        var nodes = savedAction.Nodes().ToList();
        Assert.True(nodes.IndexOf(savedAction.Element("name")!) <
                    nodes.IndexOf(savedAction.Nodes().OfType<XComment>().Single()));
        Assert.True(nodes.IndexOf(savedAction.Nodes().OfType<XComment>().Single()) <
                    nodes.IndexOf(savedAction.Element("type")!));
    }

    [Fact]
    public void XmlTriviaPreserver_does_not_duplicate_trivia_already_present()
    {
        var original = XElement.Parse(
            "<unit>\n  <!-- once -->\n  <maxhitpoints>10</maxhitpoints>\n</unit>",
            LoadOptions.PreserveWhitespace);
        var updated = new XElement(original);

        XmlTriviaPreserver.Preserve(original, updated);
        XmlTriviaPreserver.Preserve(original, updated);

        Assert.Single(updated.Nodes().OfType<XComment>());
        Assert.Equal(
            original.Nodes().OfType<XText>().Select(text => text.Value),
            updated.Nodes().OfType<XText>().Select(text => text.Value));
    }

    [Fact]
    public void XmlTriviaPreserver_keeps_container_trivia_when_all_managed_children_are_removed()
    {
        var original = XElement.Parse(
            "<container>\n  <!-- keep explanation -->\n  <managed>remove</managed>\n</container>",
            LoadOptions.PreserveWhitespace);
        var updated = new XElement("container");

        XmlTriviaPreserver.Preserve(original, updated);

        Assert.Empty(updated.Elements());
        Assert.Single(updated.Nodes().OfType<XComment>());
        Assert.Equal(
            original.Nodes().OfType<XText>().Select(text => text.Value),
            updated.Nodes().OfType<XText>().Select(text => text.Value));
    }

}
