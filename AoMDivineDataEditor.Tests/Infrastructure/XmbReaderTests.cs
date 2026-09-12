using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmbReaderTests
{
    [Fact]
    public void ToFormattedXml_reads_elements_attributes_text_and_children()
    {
        byte[] xmb = BuildSampleXmb();

        string xml = Assert.IsType<string>(XmbReader.ToFormattedXml(xmb));
        var document = XDocument.Parse(xml);

        Assert.Equal("unit", document.Root!.Name.LocalName);
        Assert.Equal("VillagerGreek", document.Root.Attribute("name")?.Value);
        Assert.Equal("nameid", document.Root.Element("nameid")?.Name.LocalName);
        Assert.Equal("STR_UNIT_VILLAGER", document.Root.Element("nameid")?.Value);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { (byte)'X', (byte)'1', 0, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'X', (byte)'2', 0, 0, 0, 0 })]
    public void ToFormattedXml_returns_null_for_invalid_or_truncated_input(byte[] data)
    {
        Assert.Null(XmbReader.ToFormattedXml(data));
    }

    private static byte[] BuildSampleXmb()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write((byte)'X');
        writer.Write((byte)'1');
        writer.Write(0); // data length, filled below
        writer.Write((byte)'X');
        writer.Write((byte)'R');
        writer.Write(4u);
        writer.Write(8u);

        WriteNames(writer, "unit", "nameid");
        WriteNames(writer, "name");

        WriteNode(writer, elementIndex: 0, text: "", attributes: [(0, "VillagerGreek")], childCount: 1);
        WriteNode(writer, elementIndex: 1, text: "STR_UNIT_VILLAGER", attributes: [], childCount: 0);

        long end = stream.Position;
        stream.Position = 2;
        writer.Write(checked((int)end - 6));
        stream.Position = end;
        return stream.ToArray();
    }

    private static void WriteNames(BinaryWriter writer, params string[] names)
    {
        writer.Write(names.Length);
        foreach (string name in names)
        {
            writer.Write(name.Length);
            writer.Write(Encoding.Unicode.GetBytes(name));
        }
    }

    private static void WriteNode(
        BinaryWriter writer,
        int elementIndex,
        string text,
        (int Index, string Value)[] attributes,
        int childCount)
    {
        writer.Write((byte)'X');
        writer.Write((byte)'N');
        writer.Write(0); // node length is informational for the reader
        writer.Write(text.Length);
        writer.Write(Encoding.Unicode.GetBytes(text));
        writer.Write(elementIndex);
        writer.Write(0); // source line
        writer.Write(attributes.Length);
        foreach (var attribute in attributes)
        {
            writer.Write(attribute.Index);
            writer.Write(attribute.Value.Length);
            writer.Write(Encoding.Unicode.GetBytes(attribute.Value));
        }
        writer.Write(childCount);
    }
}
