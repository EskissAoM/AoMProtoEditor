using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public static partial class ProtoUnitXmlOrderPolicy
{
    /// <summary>Place newly introduced fields before preservation makes their position authoritative.</summary>
    internal static void InsertNewField(XElement unit, XElement field)
    {
        if (unit.Name == "unit") EditorXmlOrderPolicy.InsertNewField(unit, field, ProtoUnitFieldOrder.Ranks);
        else unit.Add(field);
    }

    internal static int FindNewFieldInsertionIndex(IReadOnlyList<XNode> nodes, XElement field)
        => EditorXmlOrderPolicy.FindNewFieldInsertionIndex(nodes, field, ProtoUnitFieldOrder.Ranks);

    /// <summary>
    /// Approved structured-save order, shared by preview and actual draft capture.
    /// Does not format, rebuild children, change values, or sort repeated entries.
    /// </summary>
    internal static void OrderKnownFieldsForEditor(XElement unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Name.NamespaceName.Length != 0 || !unit.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a game ProtoUnit <unit> element.", nameof(unit));
        EditorXmlOrderPolicy.Normalize(unit, ProtoUnitFieldOrder.Ranks);
    }

    internal static bool PreservesSpace(XElement element)
        => EditorXmlOrderPolicy.PreservesSpace(element);
}
