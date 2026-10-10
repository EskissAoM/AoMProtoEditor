using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public readonly record struct UnitModifyVfxTags(string Target, string Bone, string? Birth);

public static class UnitModifyFamilyController
{
    public static readonly IReadOnlyList<string> AllVfxTags =
        ["vfxproto", "vfxattachbone", "birthvfx", "modelattachment", "modelattachmentbone"];

    public static bool IsPowerType(string? type)
        => type != null && (type.Equals("UnitModify", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("SingleModify", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("NewMoon", StringComparison.OrdinalIgnoreCase));

    public static bool IsNewMoon(string? type)
        => type?.Equals("NewMoon", StringComparison.OrdinalIgnoreCase) == true;

    public static bool SupportsKillOnEffectEnd(string? type)
        => type != null && (type.Equals("UnitModify", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("SingleModify", StringComparison.OrdinalIgnoreCase));

    public static UnitModifyVfxTags GetVfxTags(string? type)
        => IsNewMoon(type)
            ? new UnitModifyVfxTags("modelattachment", "modelattachmentbone", null)
            : new UnitModifyVfxTags("vfxproto", "vfxattachbone", "birthvfx");

    public static XElement AddDefaultModify(XElement owner)
    {
        var modify = PowerCatalog.AddRepeatedField(owner, "unitmodify", "");
        modify.SetAttributeValue("apply", "Add");
        modify.SetAttributeValue("amount", "0");
        return modify;
    }
}
