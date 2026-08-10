using System.Buffers.Binary;
using System.IO.Compression;
using K4os.Compression.LZ4;

namespace AoMDivineDataEditor.GameData;

internal static class BarDecompressor
{
    private static ReadOnlySpan<byte> Alz4Magic => "alz4"u8;
    private static ReadOnlySpan<byte> L33tMagic => "l33t"u8;

    public static int Decompress(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (data.Length < 8)
            throw new InvalidDataException("The compressed BAR entry is too short.");

        if (data[..4].SequenceEqual(Alz4Magic))
            return DecompressAlz4(data, destination);
        if (data[..4].SequenceEqual(L33tMagic))
            return DecompressL33t(data, destination);

        throw new InvalidDataException("The BAR entry uses an unsupported compression format.");
    }

    private static int DecompressAlz4(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (data.Length < 16)
            throw new InvalidDataException("The ALZ4 header is incomplete.");

        int outputSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        int compressedSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        if (!IsValidOutputSize(outputSize, destination) || compressedSize < 0 || compressedSize > data.Length - 16)
            throw new InvalidDataException("The ALZ4 size fields are invalid.");

        int decoded = LZ4Codec.Decode(data.Slice(16, compressedSize), destination[..outputSize]);
        if (decoded != outputSize)
            throw new InvalidDataException("The ALZ4 payload could not be fully decompressed.");

        return decoded;
    }

    private static int DecompressL33t(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int outputSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        if (!IsValidOutputSize(outputSize, destination) || data.Length < 14)
            throw new InvalidDataException("The L33T size fields are invalid.");

        // L33T wraps raw deflate in a two-byte zlib header and a four-byte game checksum.
        byte[] deflatePayload = data.Slice(10, data.Length - 14).ToArray();
        using var input = new MemoryStream(deflatePayload, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        deflate.ReadExactly(destination[..outputSize]);
        return outputSize;
    }

    private static bool IsValidOutputSize(int size, Span<byte> destination)
        => size > 0 && size <= BarArchive.MaximumEntrySize && size <= destination.Length;
}
