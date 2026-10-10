using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ModXmlDocumentSafetyTests
{
    [Fact]
    public void Proto_rejects_incorrect_root()
    {
        var result = Validate("<proto><unit name='Test'/></proto>", ModXmlDocumentKind.ProtoUnits);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("<protomods>", StringComparison.Ordinal));
    }

    [Fact]
    public void Proto_rejects_duplicate_units_case_insensitively()
    {
        var result = Validate("<protomods><unit name='Test'/><unit name='test'/></protomods>", ModXmlDocumentKind.ProtoUnits);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.ObjectName.Contains("Test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Proto_rejects_nested_unit_definitions()
    {
        var result = Validate("<protomods><group><unit name='Hidden'/></group></protomods>", ModXmlDocumentKind.ProtoUnits);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("outside the root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Proto_rejects_duplicate_costs_actions_and_managed_values()
    {
        var result = Validate("""
            <protomods>
              <unit name="Test">
                <maxhitpoints>10</maxhitpoints><maxhitpoints>20</maxhitpoints>
                <cost resourcetype="Food">1</cost><cost resourcetype="food">2</cost>
                <protoaction><name>Attack</name><type>HandAttack</type></protoaction>
                <protoaction><name>attack</name><type>RangedAttack</type></protoaction>
              </unit>
            </protomods>
            """, ModXmlDocumentKind.ProtoUnits);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("maxhitpoints", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("<cost>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, issue => issue.ObjectName.Contains("action", StringComparison.OrdinalIgnoreCase) && issue.Reason.Contains("defined 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ability_associations_reject_duplicate_and_nested_ability_names()
    {
        var duplicate = Validate("<abilitiesmods><Test><ability>A</ability><ability name='a'/></Test></abilitiesmods>", ModXmlDocumentKind.Abilities);
        var nested = Validate("<abilitiesmods><Test><group><ability>A</ability></group></Test></abilitiesmods>", ModXmlDocumentKind.Abilities);

        Assert.False(duplicate.IsSafeToEdit);
        Assert.False(nested.IsSafeToEdit);
    }

    [Fact]
    public void Ability_and_power_documents_reject_duplicate_managed_values()
    {
        var abilities = Validate(
            "<abilitiesmods><Test><ability>A<rof>1</rof><rof>2</rof></ability></Test></abilitiesmods>",
            ModXmlDocumentKind.Abilities);
        var powers = Validate(
            "<powersmod><power name='A'><placement>Unit</placement><placement>Ground</placement></power></powersmod>",
            ModXmlDocumentKind.Powers);

        Assert.False(abilities.IsSafeToEdit);
        Assert.False(powers.IsSafeToEdit);
    }

    [Fact]
    public void Commands_reject_duplicate_names_and_duplicate_managed_elements()
    {
        var result = Validate("""
            <protounitcommandmods>
              <protounitcommand><name>Build</name><icon>a</icon><icon>b</icon></protounitcommand>
              <protounitcommand><name>build</name></protounitcommand>
            </protounitcommandmods>
            """, ModXmlDocumentKind.ProtoUnitCommands);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("<icon>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("defined 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Commands_allow_repeatable_shared_and_prequeue_command_elements()
    {
        var result = Validate("""
            <protounitcommandmods>
              <protounitcommand>
                <name>TransformBuilding</name>
                <sharedcommand>TransformLumber</sharedcommand>
                <sharedcommand>TransformLivestock</sharedcommand>
                <removecommandprequeueonprequeue>BuildLumber</removecommandprequeueonprequeue>
                <removecommandprequeueonprequeue>BuildLivestock</removecommandprequeueonprequeue>
              </protounitcommand>
            </protounitcommandmods>
            """, ModXmlDocumentKind.ProtoUnitCommands);

        Assert.True(result.IsSafeToEdit);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Tactics_rejects_duplicate_and_nested_named_action_definitions()
    {
        var result = Validate("""
            <tactics>
              <action><name>Attack</name><type>HandAttack</type></action>
              <action><name>attack</name><type>RangedAttack</type></action>
              <tactic>Default<rule><action><name>NestedDefinition</name></action></rule></tactic>
            </tactics>
            """, ModXmlDocumentKind.Tactics);

        Assert.False(result.IsSafeToEdit);
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("nested", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, issue => issue.Reason.Contains("defined 2", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ModXmlDocumentKind.UnitTypes, "<abstractunittypesmods><abstractunittype>A</abstractunittype><abstractunittype>a</abstractunittype></abstractunittypesmods>")]
    [InlineData(ModXmlDocumentKind.TechTypes, "<techtypesmods><techtype>A</techtype><techtype>a</techtype></techtypesmods>")]
    public void Type_catalogs_reject_duplicate_definitions(ModXmlDocumentKind kind, string xml)
    {
        Assert.False(Validate(xml, kind).IsSafeToEdit);
    }

    [Fact]
    public void Unknown_but_safe_elements_are_warnings_and_do_not_block_editing()
    {
        var result = Validate("<protomods><futuredata keep='yes'/><unit name='Test'><futurefield>42</futurefield></unit></protomods>", ModXmlDocumentKind.ProtoUnits);

        Assert.True(result.IsSafeToEdit);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, issue => issue.ObjectName.Contains("futuredata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Loader_rejects_malformed_encoding_declaration_without_changing_file()
    {
        using var temp = new TestWorkspace(nameof(ModXmlDocumentSafetyTests));
        var path = temp.GetPath("proto_mods.xml");
        var bytes = Encoding.UTF8.GetBytes("<?xml version='1.0' encoding='utf-16'?><protomods/>");
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<InvalidDataException>(() =>
            ModXmlDocumentSafety.LoadExisting(path, ModXmlDocumentKind.ProtoUnits));

        Assert.Contains(Path.GetFullPath(path), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Loader_rejects_truncated_document_without_changing_file()
    {
        using var temp = new TestWorkspace(nameof(ModXmlDocumentSafetyTests));
        var path = temp.GetPath("proto_mods.xml");
        const string truncated = "<protomods><unit name='Incomplete'>";
        File.WriteAllText(path, truncated);

        var error = Assert.Throws<InvalidDataException>(() =>
            ModXmlDocumentSafety.LoadExisting(path, ModXmlDocumentKind.ProtoUnits));

        Assert.Contains("could not be read exactly", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(truncated, File.ReadAllText(path));
    }

    [Fact]
    public void Loader_prohibits_dtds_and_external_entities_without_changing_file()
    {
        using var temp = new TestWorkspace(nameof(ModXmlDocumentSafetyTests));
        var path = temp.GetPath("powers_mods.xml");
        const string xml = "<!DOCTYPE powersmod [<!ENTITY external SYSTEM 'file:///does-not-exist'>]>" +
                           "<powersmod><power name='P' type='Rain'>&external;</power></powersmod>";
        File.WriteAllText(path, xml);

        var error = Assert.Throws<InvalidDataException>(() =>
            ModXmlDocumentSafety.LoadExisting(path, ModXmlDocumentKind.Powers));

        Assert.Contains("could not be read exactly", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(xml, File.ReadAllText(path));
    }

    [Fact]
    public void Proto_editor_loader_rejects_ambiguous_document_without_changing_file()
    {
        using var temp = new TestWorkspace(nameof(ModXmlDocumentSafetyTests));
        var path = temp.GetPath("proto_mods.xml");
        const string ambiguous = "<protomods><unit name='Same'/><unit name='same'/></protomods>";
        File.WriteAllText(path, ambiguous);

        Assert.Throws<InvalidDataException>(() => ProtoXmlHandler.ParseProtoXml(path));
        Assert.Equal(ambiguous, File.ReadAllText(path));
    }

    [Fact]
    public void Companion_editor_loader_rejects_wrong_root_and_duplicate_definitions()
    {
        using var temp = new TestWorkspace(nameof(ModXmlDocumentSafetyTests));
        var wrongRootPath = temp.GetPath("powers_wrong.xml");
        var duplicatePath = temp.GetPath("commands_duplicate.xml");
        File.WriteAllText(wrongRootPath, "<powers><power name='A'/></powers>");
        File.WriteAllText(duplicatePath,
            "<protounitcommandmods><protounitcommand><name>A</name></protounitcommand><protounitcommand><name>a</name></protounitcommand></protounitcommandmods>");

        Assert.Throws<InvalidOperationException>(() =>
            ProtoEditorWindow.LoadAbilityXmlDocumentForUpdate(wrongRootPath, "powersmod"));
        Assert.Throws<InvalidOperationException>(() =>
            ProtoEditorWindow.LoadAbilityXmlDocumentForUpdate(duplicatePath, "protounitcommandmods"));
    }

    private static ModXmlDocumentValidation Validate(string xml, ModXmlDocumentKind kind)
        => ModXmlDocumentSafety.Validate(XDocument.Parse(xml, LoadOptions.PreserveWhitespace), kind);

}
