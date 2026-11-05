using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Applies the approved outer field order to a structured Unit draft. Formatting
/// remains with the shared preview/file writers, which protect mixed content and
/// inherited xml:space. Unknown content anchors separate ordering runs.
/// </summary>
public static partial class ProtoUnitXmlOrderPolicy
{
    public static void NormalizeForEditor(XElement unit)
        => OrderKnownFieldsForEditor(unit);
}
