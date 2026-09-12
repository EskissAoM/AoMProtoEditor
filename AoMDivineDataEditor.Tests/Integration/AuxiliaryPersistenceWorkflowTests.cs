using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class AuxiliaryPersistenceWorkflowTests
{
    [Fact]
    public void Managed_children_preserve_ability_wrapper_metadata_comments_and_unknown_elements()
    {
        var wrapper = XElement.Parse(
            "<Unit custom='keep'><!-- before --><ability><name>Old</name><!-- inside --><tech>T</tech></ability>" +
            "<mod-extension enabled='1'/><!-- after --></Unit>",
            LoadOptions.PreserveWhitespace);

        ManagedXmlChildren.Replace(
            wrapper,
            "ability",
            [new XElement("ability", new XElement("name", "Old"), new XElement("rof", "2"))],
            element => element.Element("name")?.Value);

        Assert.Equal("keep", (string?)wrapper.Attribute("custom"));
        Assert.NotNull(wrapper.Element("mod-extension"));
        Assert.Equal("2", wrapper.Element("ability")?.Element("rof")?.Value);
        Assert.Equal(
            new[] { " before ", " inside ", " after " },
            wrapper.DescendantNodes().OfType<XComment>().Select(comment => comment.Value));
    }

    [Fact]
    public void Preserved_xml_file_keeps_auxiliary_document_trivia_encoding_and_newlines()
    {
        using var workspace = new TestWorkspace(nameof(Preserved_xml_file_keeps_auxiliary_document_trivia_encoding_and_newlines));
        var path = workspace.GetPath("powers_mods.xml");
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<?mod keep?>\r\n" +
            "<powersmod custom=\"yes\">\r\n" +
            "  <!-- ability note -->\r\n" +
            "  <power name=\"A\" type=\"UnitAction\"><custom>keep</custom></power>\r\n" +
            "</powersmod>\r\n";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        document.Root!.Element("power")!.SetAttributeValue("type", "GodPower");

        PreservedXmlFile.SaveAtomic(document, path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(encoding.GetPreamble()));
        var text = encoding.GetString(bytes, encoding.GetPreamble().Length, bytes.Length - encoding.GetPreamble().Length);
        Assert.Contains("<?mod keep?>\r\n", text, StringComparison.Ordinal);
        Assert.Contains("<!-- ability note -->\r\n", text, StringComparison.Ordinal);
        Assert.Contains("<custom>keep</custom>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_document_transaction_rejects_an_external_change_before_writing()
    {
        using var workspace = new TestWorkspace(nameof(Shared_document_transaction_rejects_an_external_change_before_writing));
        var path = workspace.GetPath("proto_unit_command_mods.xml");
        File.WriteAllText(path, "<protounitcommandmods><protounitcommand><name>A</name></protounitcommand></protounitcommandmods>");
        var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.ProtoUnitCommands);
        prepared.Document.Root!.Element("protounitcommand")!.Element("name")!.Value = "Editor";
        File.WriteAllText(path, "<protounitcommandmods><protounitcommand><name>External</name></protounitcommand></protounitcommandmods>");
        var commitCalled = false;

        Assert.Throws<IOException>(() => ModDocumentPersistence.Execute([prepared], null, null, () =>
        {
            commitCalled = true;
            PreservedXmlFile.SaveAtomic(prepared.Document, path);
        }));

        Assert.False(commitCalled);
        Assert.Contains("External", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_document_transaction_restores_exact_bytes_when_a_later_write_fails()
    {
        using var workspace = new TestWorkspace(nameof(Shared_document_transaction_restores_exact_bytes_when_a_later_write_fails));
        var commandsPath = workspace.GetPath("proto_unit_command_mods.xml");
        var transformsPath = workspace.GetPath("unit_transform_mods.xml");
        var commandsBytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?><protounitcommandmods><!-- keep --></protounitcommandmods>")).ToArray();
        var transformsBytes = Encoding.UTF8.GetBytes("<unittransformmods><!-- unchanged --></unittransformmods>");
        File.WriteAllBytes(commandsPath, commandsBytes);
        File.WriteAllBytes(transformsPath, transformsBytes);
        var commands = ModDocumentPersistence.Prepare(commandsPath, ModXmlDocumentKind.ProtoUnitCommands);
        var transforms = ModDocumentPersistence.Prepare(transformsPath, ModXmlDocumentKind.UnitTransforms);
        commands.Document.Root!.Add(new XElement("protounitcommand", new XElement("name", "Added")));

        Assert.Throws<InvalidOperationException>(() => ModDocumentPersistence.Execute(
            [commands, transforms], null, null, () =>
            {
                PreservedXmlFile.SaveAtomic(commands.Document, commandsPath);
                throw new InvalidOperationException("simulated later writer failure");
            }));

        Assert.Equal(commandsBytes, File.ReadAllBytes(commandsPath));
        Assert.Equal(transformsBytes, File.ReadAllBytes(transformsPath));
    }

    [Fact]
    public void Auxiliary_editors_use_one_shared_transaction_lifecycle_without_manual_rollback_flags()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("ModDocumentPersistence.Execute", source, StringComparison.Ordinal);
        Assert.Contains("PreservedXmlFile.SaveAtomic", source, StringComparison.Ordinal);
        Assert.Contains("SavePendingChangesBeforeImmediateManagerAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stringsSaved", source, StringComparison.Ordinal);
        Assert.DoesNotContain("commandSaved", source, StringComparison.Ordinal);
        Assert.DoesNotContain("transformSaved", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_ability_strings_are_committed_by_the_same_callback_as_the_power()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var start = source.IndexOf("private async Task SaveAbilityDefinitionEditorAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task RequestCloseAbilityDefinitionEditorAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var saveMethod = source[start..end];

        Assert.Contains("_saveAbilityDefinitionAsync(", saveMethod, StringComparison.Ordinal);
        Assert.Contains("stringEntries", saveMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveCurrentModStringEntries(", saveMethod, StringComparison.Ordinal);
        Assert.Contains("editedStringFileState", source, StringComparison.Ordinal);
    }

}
