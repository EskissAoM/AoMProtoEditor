using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

namespace AoMDivineDataEditor.Classes;

public sealed record AssetPayloadResult<T>(T? Value, string Source, string? Error) where T : class
{
    internal static AssetPayloadResult<T> Success(T value, string source) => new(value, source, null);
    internal static AssetPayloadResult<T> Failure(string source, string error) => new(null, source, error);
}

internal sealed class AssetPayloadLimitException(string message) : IOException(message);

// Consumer limits; the BAR reader's general format limits remain unchanged.
internal sealed record AssetPayloadPolicy(
    int MaximumImageBytes = 64 * 1024 * 1024,
    int MaximumXmlBytes = 64 * 1024 * 1024,
    int MaximumXmlCharacters = 64 * 1024 * 1024,
    int MaximumDimension = 8192,
    long MaximumPixels = 16L * 1024 * 1024,
    long MaximumDecodedBytes = 64L * 1024 * 1024,
    int MaximumXmlNodes = 1_000_000,
    int MaximumXmlDepth = 256,
    bool EnforceViewerLimits = true)
{
    internal static AssetPayloadPolicy Default { get; } = new();
    // Unit animation validation is authoritative, not a display operation.
    // Reuse reference-inspection limits and accounting; viewer indentation and
    // recursion limits must not make a valid animation catalog unavailable.
    internal static AssetPayloadPolicy AnimationValidation { get; } = new(
        MaximumXmlBytes: checked((int)ModReferenceDocumentReader.MaximumFileBytes),
        MaximumXmlCharacters: checked((int)(ModReferenceDocumentReader.MaximumFileBytes * 2)),
        MaximumXmlNodes: 4_000_000, MaximumXmlDepth: int.MaxValue, EnforceViewerLimits: false);

    internal static void RequireBytes(long size, int maximum, string label)
    {
        if (size < 0 || size > maximum)
            throw new AssetPayloadLimitException($"{label} exceeds the {maximum:N0}-byte preview limit.");
    }

    internal void RequireDimensions(uint width, uint height, int bytesPerPixel = 4)
    {
        if (width == 0 || height == 0 || width > MaximumDimension || height > MaximumDimension)
            throw new AssetPayloadLimitException($"Image dimensions must be between 1 and {MaximumDimension} pixels per side.");
        var pixels = checked((long)width * height);
        if (pixels > MaximumPixels || checked(pixels * bytesPerPixel) > MaximumDecodedBytes)
            throw new AssetPayloadLimitException($"Image exceeds the {MaximumPixels:N0}-pixel / {MaximumDecodedBytes:N0}-byte decoded preview limit.");
    }

    internal void ValidatePng(ReadOnlySpan<byte> bytes)
    {
        RequireBytes(bytes.Length, MaximumImageBytes, "PNG input");
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("PNG header is invalid or truncated.");
        var depth = bytes[24];
        var color = bytes[25];
        var validDepth = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8, _ => false };
        if (!validDepth || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1)
            throw new InvalidDataException("PNG color or compression header is invalid.");
        RequireDimensions(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]), depth == 16 ? 8 : 4);
        var offset = 8;
        var imageData = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - 12)
                throw new InvalidDataException("PNG chunk is truncated.");
            var type = bytes.Slice(offset + 4, 4);
            if (offset != 8 && type.SequenceEqual("IHDR"u8))
                throw new InvalidDataException("PNG has multiple image headers.");
            if (type.SequenceEqual("IDAT"u8)) imageData = true;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !imageData) throw new InvalidDataException("PNG image data is missing.");
                return;
            }
            offset = checked(offset + (int)length + 12);
        }
        throw new InvalidDataException("PNG end chunk is missing or truncated.");
    }

    internal void ValidateDds(ReadOnlySpan<byte> bytes)
    {
        RequireBytes(bytes.Length, MaximumImageBytes, "DDS input");
        if (bytes.Length < 128 || !bytes[..4].SequenceEqual("DDS "u8))
            throw new InvalidDataException("DDS header is invalid or truncated.");
        var header = MemoryMarshal.Read<DdsHeader>(bytes[4..128]);
        if (header.dwSize != 124 || header.ddsPixelFormat.dwSize != 32)
            throw new InvalidDataException("DDS header sizes are invalid.");
        RequireDimensions(header.dwWidth, header.dwHeight);
        if (((uint)header.dwCaps2 & 0x20fe00) != 0 || header.dwDepth > 1)
            throw new InvalidDataException("DDS preview supports a single face, not cube or volume textures.");
        var offset = 128;
        var format = header.ddsPixelFormat.DxgiFormat;
        if (header.ddsPixelFormat.IsDxt10Format)
        {
            if (bytes.Length < 148) throw new InvalidDataException("DDS DX10 header is truncated.");
            var dx10 = MemoryMarshal.Read<DdsHeaderDx10>(bytes[128..148]);
            if (dx10.arraySize != 1 || (dx10.miscFlag & 4) != 0 ||
                (dx10.resourceDimension != D3D10ResourceDimension.D3D10ResourceDimensionTexture2D &&
                 !(dx10.resourceDimension == D3D10ResourceDimension.D3D10ResourceDimensionTexture1D && header.dwHeight == 1)))
                throw new InvalidDataException("DDS preview supports one 2D surface, not arrays, cubes or volumes.");
            format = dx10.dxgiFormat;
            offset = 148;
        }
        if (format is DxgiFormat.DxgiFormatUnknown or DxgiFormat.DxgiFormatBc6HUf16 or DxgiFormat.DxgiFormatBc6HSf16)
            throw new InvalidDataException("DDS pixel format is not supported for icon previews.");
        var maximumMips = 1;
        for (var edge = Math.Max(header.dwWidth, header.dwHeight); edge > 1; edge >>= 1) maximumMips++;
        if (header.dwMipMapCount > maximumMips)
            throw new AssetPayloadLimitException($"DDS mip count exceeds the {maximumMips} levels allowed by its dimensions.");
        var count = (header.dwCaps & HeaderCaps.DdscapsMipmap) == 0 ? 1 : Math.Max(1, header.dwMipMapCount);
        var unitSize = format.GetByteSize();
        if (unitSize <= 0 || unitSize > 16) throw new InvalidDataException("DDS pixel format has an invalid storage size.");
        long required = offset;
        long width = header.dwWidth, height = header.dwHeight;
        for (var mip = 0; mip < count; mip++)
        {
            var units = format.IsCompressedFormat() ? checked(((width + 3) / 4) * ((height + 3) / 4)) : checked(width * height);
            required = checked(required + units * unitSize);
            RequireBytes(required, MaximumImageBytes, "DDS mip storage");
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        if (required > bytes.Length) throw new InvalidDataException("DDS pixel or mip data is truncated.");
    }
}
