using AoMDivineDataEditor.Classes;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using SkiaSharp;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class DdsIconDecoderTests
{
    [Theory]
    [InlineData(CompressionFormat.Rgba)]
    [InlineData(CompressionFormat.Bc1)]
    [InlineData(CompressionFormat.Bc2)]
    [InlineData(CompressionFormat.Bc3)]
    [InlineData(CompressionFormat.Bc4)]
    [InlineData(CompressionFormat.Bc5)]
    [InlineData(CompressionFormat.Bc7)]
    public async Task ConvertsDecodedPixelsToPngWithoutChangingColorsOrAlpha(CompressionFormat format)
    {
        var dds = CreateDds(format);
        using var source = new MemoryStream(dds);
        var expected = await new BcDecoder().DecodeAsync(BCnEncoder.Shared.ImageFiles.DdsFile.Load(source));
        var png = await DdsIconDecoder.ConvertToPngBytesAsync(dds);
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        using var decoded = SKBitmap.Decode(png, new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var pixel = expected[y * 4 + x];
            var actual = decoded.GetPixel(x, y);
            Assert.Equal(pixel.a, actual.Alpha);
            Assert.Equal(pixel.r, actual.Red);
            Assert.Equal(pixel.g, actual.Green);
            Assert.Equal(pixel.b, actual.Blue);
        }
    }

    [Fact]
    public async Task LooseDdsPreviewUsesPngAliasAndMalformedDdsIsRejected()
    {
        using var workspace = new TestWorkspace("dds-preview");
        var resources = workspace.CreateDirectory("resources");
        var file = Path.Combine(resources, "fixture.dds");
        File.WriteAllBytes(file, CreateDds(CompressionFormat.Rgba));
        var service = new IconPreviewService(() => null, () => resources);
        var result = await service.LoadAsync("fixture.png");
        Assert.NotNull(result);
        Assert.Equal(file, result.ResolvedSource);
        using var bitmap = SKBitmap.Decode(result.PngBytes);
        Assert.Equal(4, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
        Assert.Null(await DdsIconDecoder.ConvertToPngBytesAsync(new byte[] { 1, 2, 3 }));
    }

    internal static byte[] CreateDds(CompressionFormat format, bool generateMipMaps = false)
    {
        var rgba = new byte[4 * 4 * 4];
        for (var index = 0; index < 16; index++)
        {
            rgba[index * 4] = (byte)(index % 2 == 0 ? 255 : 0);
            rgba[index * 4 + 1] = (byte)(index % 3 == 0 ? 255 : 0);
            rgba[index * 4 + 2] = 64;
            rgba[index * 4 + 3] = (byte)(index % 3 == 0 ? 0 : index % 3 == 1 ? 128 : 255);
        }
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = generateMipMaps;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;
        using var output = new MemoryStream();
        encoder.EncodeToStream(rgba, 4, 4, PixelFormat.Rgba32, output);
        return output.ToArray();
    }
}
