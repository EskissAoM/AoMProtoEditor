using System.Buffers.Binary;
using System.Text;
using System.Xml;

namespace AoMDivineDataEditor.GameData;

/// <summary>Converts Retold XMB binary XML documents into regular XML text.</summary>
public static class XmbReader
{
    private const int MaximumNameBytes = 4096;
    private const int MaximumNameCount = 1_000_000;
    private const int MaximumTreeDepth = 256;

    public static string? ToFormattedXml(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ToFormattedXml(data.AsSpan());
    }

    public static string? ToFormattedXml(ReadOnlySpan<byte> data)
    {
        try
        {
            var reader = new XmbSpanReader(data);
            if (!reader.TryReadMarker((byte)'X', (byte)'1') || !reader.TryReadInt32(out int dataLength) ||
                dataLength < 0 || dataLength > data.Length - 6 ||
                !reader.TryReadMarker((byte)'X', (byte)'R') ||
                !reader.TryReadUInt32(out uint identifier) || identifier != 4 ||
                !reader.TryReadUInt32(out uint version) || version != 8 ||
                !TryReadNames(ref reader, allowEmpty: false, out var elements) ||
                !TryReadNames(ref reader, allowEmpty: true, out var attributes))
            {
                return null;
            }

            var output = new StringBuilder(Math.Min(reader.Remaining * 3, 4_000_000));
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                OmitXmlDeclaration = true
            };

            using (var writer = XmlWriter.Create(output, settings))
            {
                if (!TryWriteNode(writer, ref reader, elements, attributes, 0))
                    return null;
            }

            return output.ToString();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or XmlException)
        {
            return null;
        }
    }

    private static bool TryReadNames(ref XmbSpanReader reader, bool allowEmpty, out List<string> names)
    {
        names = [];
        if (!reader.TryReadInt32(out int count) || count < (allowEmpty ? 0 : 1) || count > MaximumNameCount)
            return false;

        // Every item requires at least a four-byte length, so this also prevents huge allocations.
        if (count > reader.Remaining / 4)
            return false;

        names = new List<string>(count);
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadUnicodeString(MaximumNameBytes, allowEmpty: false, out string? name))
                return false;
            names.Add(name);
        }

        return true;
    }

    private static bool TryWriteNode(
        XmlWriter writer,
        ref XmbSpanReader reader,
        IReadOnlyList<string> elements,
        IReadOnlyList<string> attributes,
        int depth)
    {
        if (depth > MaximumTreeDepth || !reader.TryReadMarker((byte)'X', (byte)'N') ||
            !reader.TryReadInt32(out int nodeLength) || nodeLength < 0 ||
            !reader.TryReadUnicodeString(MaximumNameBytes, allowEmpty: true, out string? text) ||
            !reader.TryReadInt32(out int elementIndex) || elementIndex < 0 || elementIndex >= elements.Count ||
            !reader.TryReadInt32(out _))
        {
            return false;
        }

        writer.WriteStartElement(elements[elementIndex]);

        if (!reader.TryReadInt32(out int attributeCount) || attributeCount < 0 ||
            attributeCount > MaximumNameCount || attributeCount > reader.Remaining / 8)
        {
            return false;
        }

        var parsedAttributes = new List<(int Index, string Value)>(attributeCount);
        for (int index = 0; index < attributeCount; index++)
        {
            if (!reader.TryReadInt32(out int attributeIndex) || attributeIndex < 0 || attributeIndex >= attributes.Count ||
                !reader.TryReadUnicodeString(MaximumNameBytes, allowEmpty: true, out string? value))
            {
                return false;
            }

            int duplicate = parsedAttributes.FindIndex(item => item.Index == attributeIndex);
            if (duplicate >= 0)
                parsedAttributes[duplicate] = (attributeIndex, value);
            else
                parsedAttributes.Add((attributeIndex, value));
        }

        foreach (var attribute in parsedAttributes)
            writer.WriteAttributeString(attributes[attribute.Index], attribute.Value);

        if (text.Length > 0)
            writer.WriteString(text);

        if (!reader.TryReadInt32(out int childCount) || childCount < 0 ||
            childCount > MaximumNameCount || childCount > reader.Remaining / 2)
        {
            return false;
        }

        for (int index = 0; index < childCount; index++)
        {
            if (!TryWriteNode(writer, ref reader, elements, attributes, depth + 1))
                return false;
        }

        writer.WriteFullEndElement();
        return true;
    }

    private ref struct XmbSpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public XmbSpanReader(ReadOnlySpan<byte> data) => _data = data;
        public int Remaining => _data.Length - _offset;

        public bool TryReadMarker(byte first, byte second)
        {
            if (Remaining < 2 || _data[_offset] != first || _data[_offset + 1] != second)
                return false;
            _offset += 2;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (Remaining < 4)
            {
                value = default;
                return false;
            }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            if (Remaining < 4)
            {
                value = default;
                return false;
            }
            value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return true;
        }

        public bool TryReadUnicodeString(int maximumBytes, bool allowEmpty, out string value)
        {
            value = string.Empty;
            if (!TryReadInt32(out int characterCount) || characterCount < (allowEmpty ? 0 : 1) ||
                characterCount > maximumBytes / 2)
            {
                return false;
            }

            int byteCount = characterCount * 2;
            if (byteCount > Remaining)
                return false;

            value = byteCount == 0 ? string.Empty : Encoding.Unicode.GetString(_data.Slice(_offset, byteCount));
            _offset += byteCount;
            return true;
        }
    }
}
