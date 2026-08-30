using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AoMDivineDataEditor.Classes;

/// <summary>Editor-owned DDS-to-PNG adapter backed by the managed BCn decoder package.</summary>
internal static class DdsIconDecoder
{
    public static async Task<byte[]?> ConvertToPngBytesAsync(ReadOnlyMemory<byte> data)
    {
        DdsFile dds;
        try
        {
            using var input = new MemoryStream(data.ToArray(), writable: false);
            dds = DdsFile.Load(input);
        }
        catch (Exception exception) when (exception is IOException or FormatException or ArgumentException)
        {
            return null;
        }

        if (dds.Faces.Count != 1)
            return null;

        var format = dds.header.ddsPixelFormat.IsDxt10Format
            ? dds.dx10Header.dxgiFormat
            : dds.header.ddsPixelFormat.DxgiFormat;
        if (format is DxgiFormat.DxgiFormatBc6HUf16 or DxgiFormat.DxgiFormatBc6HSf16)
            return null;

        ColorRgba32[] pixels;
        try
        {
            pixels = await new BcDecoder().DecodeAsync(dds).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }

        try
        {
            var width = checked((int)dds.header.dwWidth);
            var height = checked((int)dds.header.dwHeight);
            using var image = Image.LoadPixelData<Rgba32>(MemoryMarshal.AsBytes(pixels.AsSpan()), width, height);
            using var output = new MemoryStream();
            await image.SaveAsPngAsync(output, new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.BestSpeed
            }).ConfigureAwait(false);
            return output.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
