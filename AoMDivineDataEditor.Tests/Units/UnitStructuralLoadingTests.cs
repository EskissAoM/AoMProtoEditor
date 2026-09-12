using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class UnitStructuralLoadingTests
{
    [Fact]
    public void Unit_string_cleanup_only_offers_ids_that_exist_in_stringmods()
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STR_UNIT_REAL"] = "Real"
        };

        var result = ProtoEditorWindow.GetExistingStringCleanupCandidates(
            ["STR_UNIT_MISSING", "str_unit_real", "STR_UNIT_REAL"],
            entries);

        Assert.Equal(["str_unit_real"], result);
    }

    [Fact]
    public void Mod_selection_awaits_the_validation_error_dialog()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("await TryLoadModFileWithFeedbackAsync(config.LastModFilePath)", source, StringComparison.Ordinal);
        Assert.Contains("await TryLoadModFileWithFeedbackAsync(path)", source, StringComparison.Ordinal);
        Assert.Contains("DocumentSafetyDialog.ShowAsync(this, \"ProtoUnit\"", source, StringComparison.Ordinal);
        Assert.Contains("allowUnsafeStructureForReadOnly: true", source, StringComparison.Ordinal);
        Assert.Contains("_isReadOnly = _modDocumentSafetyError != null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Structurally_unsafe_proto_can_be_loaded_for_read_only_inspection()
    {
        using var workspace = new TestWorkspace(nameof(Structurally_unsafe_proto_can_be_loaded_for_read_only_inspection));
        var path = workspace.GetPath("proto_mods.xml");
        File.WriteAllText(path,
            "<protomods><unit name='Test'><cost resourcetype='Food'>1</cost><cost resourcetype='food'>2</cost></unit></protomods>");

        var result = ModXmlDocumentSafety.LoadExisting(
            path,
            ModXmlDocumentKind.ProtoUnits,
            allowUnsafeStructureForReadOnly: true);

        Assert.False(result.Validation.IsSafeToEdit);
        Assert.Equal("Test", result.Document.Root?.Descendants("unit").Single().Attribute("name")?.Value);
    }

}
