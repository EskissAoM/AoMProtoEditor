using System.Text;

namespace AoMDivineDataEditor.GameData;

/// <summary>Decodes text while retaining its original Unicode encoding and byte-order mark.</summary>
public sealed record PreservedTextEncoding(string Text, Encoding Encoding, byte[] Preamble)
{
    public static PreservedTextEncoding Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return new PreservedTextEncoding(text, encoding, bytes[..preambleLength]);
    }

    public static PreservedTextEncoding Utf8WithoutBom(string text)
        => new(text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), []);

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var payload = Encoding.GetBytes(text);
        if (Preamble.Length == 0)
            return payload;

        var result = new byte[Preamble.Length + payload.Length];
        Preamble.CopyTo(result, 0);
        payload.CopyTo(result, Preamble.Length);
        return result;
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF]))
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        if (StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]))
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true), 4);
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), 3);
        if (StartsWith(bytes, [0xFE, 0xFF]))
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), 2);
        if (StartsWith(bytes, [0xFF, 0xFE]))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), 2);

        // XML and string-table files occasionally declare UTF-16/UTF-32 without
        // writing a BOM. Detect only the unambiguous leading '<' byte layouts;
        // ordinary UTF-8 remains the fallback for every other BOM-less file.
        if (StartsWith(bytes, [0x3C, 0x00, 0x00, 0x00]))
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 0);
        if (StartsWith(bytes, [0x00, 0x00, 0x00, 0x3C]))
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 0);
        if (StartsWith(bytes, [0x3C, 0x00]))
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 0);
        if (StartsWith(bytes, [0x00, 0x3C]))
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 0);
        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0);
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix)
        => bytes.AsSpan().StartsWith(prefix);
}
