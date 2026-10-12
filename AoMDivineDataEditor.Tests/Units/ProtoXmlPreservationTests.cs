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

    [Theory]
    [InlineData("protomods", "unit", "name")]
    [InlineData("techtreemods", "tech", "name")]
    [InlineData("protounitcommandmods", "protounitcommand", "name")]
    [InlineData("unittransformmods", "transform", "command")]
    public void SaveProtoXml_indents_new_custom_entry_trees(
        string rootName,
        string entryName,
        string childName)
    {
        using var temp = new TestWorkspace(nameof(SaveProtoXml_indents_new_custom_entry_trees));
        var path = temp.GetPath(rootName + ".xml");
        var source = $"<{rootName}>\n  <!-- existing content -->\n</{rootName}>\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        document.Root!.Add(new XElement(entryName,
            new XAttribute("name", "Custom"),
            new XElement(childName, "Custom"),
            new XElement("flag", "enabled")));

        ProtoXmlHandler.SaveProtoXml(document, path);

        var saved = File.ReadAllText(path);
        Assert.Contains($"\n  <{entryName}", saved, StringComparison.Ordinal);
        Assert.Contains($"\n    <{childName}>Custom</{childName}>", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <flag>enabled</flag>", saved, StringComparison.Ordinal);
        Assert.Contains($"\n  </{entryName}>", saved, StringComparison.Ordinal);
        Assert.DoesNotContain($"<{entryName} name=\"Custom\"><{childName}", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_repairs_existing_custom_entries_that_lost_structural_indentation()
    {
        using var temp = new TestWorkspace(nameof(SaveProtoXml_repairs_existing_custom_entries_that_lost_structural_indentation));
        var path = temp.GetPath("techtree_mods.xml");
        const string source =
            "<techtreemods>\n" +
            "  <tech name=\"Existing\">\n" +
            "    <name>Existing</name>\n" +
            "  </tech>\n" +
            "<tech name=\"Broken\"><name>Broken</name><researchpoints>0</researchpoints></tech>\n" +
            "</techtreemods>\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

        ProtoXmlHandler.SaveProtoXml(document, path);

        var saved = File.ReadAllText(path);
        Assert.Contains("\n  <tech name=\"Broken\">", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <name>Broken</name>", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <researchpoints>0</researchpoints>", saved, StringComparison.Ordinal);
        Assert.Contains("\n  </tech>\n", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_reindents_an_edited_replacement_and_removes_orphaned_whitespace()
    {
        using var temp = new TestWorkspace(nameof(SaveProtoXml_reindents_an_edited_replacement_and_removes_orphaned_whitespace));
        var path = temp.GetPath("proto_unit_command_mods.xml");
        const string source =
            "<protounitcommandmods>\n" +
            "  <protounitcommand>\n" +
            "    <name>ArcusToHero</name>\n" +
            "    <icon>old.png</icon>\n" +
            "    <associatedtech>ArcusToHero</associatedtech>\n" +
            "    <unitcommand></unitcommand>\n" +
            "  </protounitcommand>\n" +
            "</protounitcommandmods>\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var existing = document.Root!.Element("protounitcommand")!;
        var command = ProtoUnitCommandDefinition.FromElement(existing);
        command.Values["icon"] = "new.png";
        existing.ReplaceWith(command.ToElement());

        ProtoXmlHandler.SaveProtoXml(document, path, formattingSourcePath: path);

        var saved = File.ReadAllText(path);
        Assert.Contains("\n  <protounitcommand>\n", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <name>ArcusToHero</name>\n", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <icon>new.png</icon>\n", saved, StringComparison.Ordinal);
        Assert.Contains("\n    <associatedtech>ArcusToHero</associatedtech>\n", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("<name>ArcusToHero</name><icon>", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveProtoXml_collapses_adjacent_root_whitespace_after_replacing_an_entry()
    {
        using var temp = new TestWorkspace(nameof(SaveProtoXml_collapses_adjacent_root_whitespace_after_replacing_an_entry));
        var path = temp.GetPath("unit_transform_mods.xml");
        const string source =
            "<unittransformmods>\n" +
            "  <transform><from>First</from><to>FirstTarget</to><command>FirstToFirstTarget</command></transform>\n" +
            "  <transform><from>Second</from><to>OldTarget</to><command>SecondToOldTarget</command></transform>\n" +
            "</unittransformmods>\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var oldEntry = document.Root!.Elements("transform").Last();
        oldEntry.Remove();
        document.Root.Add(new XElement("transform",
            new XElement("from", "Second"),
            new XElement("to", "NewTarget"),
            new XElement("command", "SecondToNewTarget")));

        ProtoXmlHandler.SaveProtoXml(document, path, formattingSourcePath: path);

        var saved = File.ReadAllText(path);
        Assert.Contains("</transform>\n  <transform>\n", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("</transform>\n\n  <transform>", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("protomods", "unit")]
    [InlineData("techtreemods", "tech")]
    [InlineData("protounitcommandmods", "protounitcommand")]
    [InlineData("unittransformmods", "transform")]
    public void SaveProtoXml_collapses_orphaned_structural_whitespace_after_deletions(
        string rootName,
        string entryName)
    {
        using var temp = new TestWorkspace(nameof(SaveProtoXml_collapses_orphaned_structural_whitespace_after_deletions));
        var path = temp.GetPath(rootName + ".xml");

        XElement Entry(string id) => entryName switch
        {
            "unit" => new XElement(entryName, new XAttribute("name", id), new XElement("value", id)),
            "tech" => new XElement(entryName, new XAttribute("name", id), new XElement("researchpoints", id.Length)),
            "protounitcommand" => new XElement(entryName, new XElement("name", id)),
            "transform" => new XElement(entryName, new XElement("command", id)),
            _ => throw new ArgumentOutOfRangeException(nameof(entryName))
        };

        var source = new XDocument(new XElement(rootName,
            Entry("One"), Entry("Two"), Entry("Three"), Entry("Four")))
            .ToString(SaveOptions.None) + "\n";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var entries = document.Root!.Elements().ToList();
        entries[1].Remove();
        entries[3].Remove();

        ProtoXmlHandler.SaveProtoXml(document, path, formattingSourcePath: path);

        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("\n\n", saved, StringComparison.Ordinal);
        Assert.Equal(["One", "Three"], document.Root.Elements().Select(element => entryName switch
        {
            "unit" or "tech" => (string?)element.Attribute("name") ?? "",
            "protounitcommand" => element.Element("name")?.Value ?? "",
            "transform" => element.Element("command")?.Value ?? "",
            _ => ""
        }).ToArray());
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
