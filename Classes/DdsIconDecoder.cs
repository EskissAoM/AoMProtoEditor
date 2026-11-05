using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using SkiaSharp;

namespace AoMDivineDataEditor.Classes;

/// <summary>Editor-owned DDS-to-PNG adapter backed by the managed BCn decoder package.</summary>
internal static class DdsIconDecoder
{
    public static async Task<byte[]?> ConvertToPngBytesAsync(ReadOnlyMemory<byte> data)
        => (await ConvertResultAsync(data).ConfigureAwait(false)).Value;

    internal static async Task<AssetPayloadResult<byte[]>> ConvertResultAsync(ReadOnlyMemory<byte> data, AssetPayloadPolicy? policy = null)
    {
        policy ??= AssetPayloadPolicy.Default;
        DdsFile dds;
        try
        {
            // DdsFile.Load allocates mip storage, so admission must precede it.
            policy.ValidateDds(data.Span);
            using var input = new MemoryStream(data.ToArray(), writable: false);
            dds = DdsFile.Load(input);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        {
            return AssetPayloadResult<byte[]>.Failure("DDS", exception.Message);
        }

        if (dds.Faces.Count != 1)
            return AssetPayloadResult<byte[]>.Failure("DDS", "DDS preview requires one face.");

        var format = dds.header.ddsPixelFormat.IsDxt10Format
            ? dds.dx10Header.dxgiFormat
            : dds.header.ddsPixelFormat.DxgiFormat;
        if (format is DxgiFormat.DxgiFormatBc6HUf16 or DxgiFormat.DxgiFormatBc6HSf16)
            return AssetPayloadResult<byte[]>.Failure("DDS", "DDS HDR format is not supported for icon previews.");

        ColorRgba32[] pixels;
        try
        {
            pixels = await new BcDecoder().DecodeAsync(dds).ConfigureAwait(false);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        {
            return AssetPayloadResult<byte[]>.Failure("DDS", exception.Message);
        }

        try
        {
            var width = checked((int)dds.header.dwWidth);
            var height = checked((int)dds.header.dwHeight);
            // Reuse Avalonia's existing Skia dependency without requiring a UI dispatcher.
            // BCn supplies straight RGBA; preserve alpha instead of premultiplying it.
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            if (pixels.LongLength != checked((long)width * height))
                return AssetPayloadResult<byte[]>.Failure("DDS", "DDS decoded dimensions are inconsistent.");
            using var image = SKImage.FromPixelCopy(info, MemoryMarshal.AsBytes(pixels.AsSpan()), checked(width * 4));
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            if (png == null) return AssetPayloadResult<byte[]>.Failure("DDS", "DDS preview could not be encoded.");
            AssetPayloadPolicy.RequireBytes(png.Size, policy.MaximumImageBytes, "Encoded PNG output");
            return AssetPayloadResult<byte[]>.Success(png.ToArray(), "DDS");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return AssetPayloadResult<byte[]>.Failure("DDS", exception.Message);
        }
    }
}
