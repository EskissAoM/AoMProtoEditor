using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitStringRenameTests
{
    [Fact]
    public void BuildRenamedUnitStringIds_ChangesIdsWithoutChangingFieldValues()
    {
        var oldIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displaynameid"] = "STR_UNIT_OLD_NAME",
            ["editornameid"] = "STR_UNIT_OLD_EDITOR",
            ["rollovertextid"] = "STR_UNIT_OLD_LR",
            ["shortrollovertextid"] = "STR_UNIT_OLD_SR"
        };
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displaynameid"] = "Caladria",
            ["editornameid"] = "Caladria editor",
            ["rollovertextid"] = "The original long rollover.",
            ["shortrollovertextid"] = "Original short rollover."
        };

        var renamed = ProtoEditorWindow.BuildRenamedUnitStringIds("CaladriaBzzz", oldIds, values);

        Assert.Equal("STR_UNIT_CALADRIABZZZ_NAME", renamed["displaynameid"]);
        Assert.Equal("STR_UNIT_CALADRIABZZZ_EDITOR", renamed["editornameid"]);
        Assert.Equal("STR_UNIT_CALADRIABZZZ_LR", renamed["rollovertextid"]);
        Assert.Equal("STR_UNIT_CALADRIABZZZ_SR", renamed["shortrollovertextid"]);
        Assert.Equal("Caladria", values["displaynameid"]);
        Assert.Equal("The original long rollover.", values["rollovertextid"]);
    }

    [Fact]
    public void BuildRenamedUnitStringIds_PreservesSharedEditorNameAndMissingOptionalFields()
    {
        var oldIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displaynameid"] = "STR_UNIT_OLD_NAME",
            ["editornameid"] = "STR_UNIT_OLD_NAME"
        };
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displaynameid"] = "Shared name",
            ["editornameid"] = "Shared name"
        };

        var renamed = ProtoEditorWindow.BuildRenamedUnitStringIds("NewUnit", oldIds, values);

        Assert.Equal("STR_UNIT_NEWUNIT_NAME", renamed["displaynameid"]);
        Assert.Equal(renamed["displaynameid"], renamed["editornameid"]);
        Assert.DoesNotContain("rollovertextid", renamed.Keys);
        Assert.DoesNotContain("shortrollovertextid", renamed.Keys);
    }
}
