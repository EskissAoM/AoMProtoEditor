using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using BCnEncoder.Shared;
using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetPayloadSafetyTests
{
    [Theory]
    [InlineData(-1, false)] [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(-1, true)] [InlineData(0, true)] [InlineData(1, true)]
    public void ActualStreamBytesAreBoundedEvenWhenLengthIsUnavailableOrStale(int delta, bool seekable)
    {
        using var stream = new ReportedLengthStream(new byte[32 + delta], seekable, reportedLength: 1);
        if (delta > 0) Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadStream(stream, 32));
        else Assert.Equal(32 + delta, AssetPayloadReader.ReadStream(stream, 32).Length);
    }

    [Fact]
    public void AdvertisedOversizeIsRejectedBeforeReading()
    {
        using var stream = new ReportedLengthStream([1], true, reportedLength: 33);
        Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadStream(stream, 32));
        Assert.Equal(0, stream.ReadCalls);
    }

    [Theory]
    [InlineData("bytes", -1)] [InlineData("bytes", 0)] [InlineData("bytes", 1)]
    [InlineData("pixels", -1)] [InlineData("pixels", 0)] [InlineData("pixels", 1)]
    [InlineData("decoded", -1)] [InlineData("decoded", 0)] [InlineData("decoded", 1)]
    [InlineData("side", -1)] [InlineData("side", 0)] [InlineData("side", 1)]
    public void PngAdmissionChecksEachBoundaryBeforeDecoding(string boundary, int delta)
    {
        var png = AssetCreateServiceTests.Png(); // Two pixels, 2 x 1, eight RGBA bytes.
        var policy = boundary switch
        {
            "bytes" => new AssetPayloadPolicy(MaximumImageBytes: png.Length + delta),
            "pixels" => new AssetPayloadPolicy(MaximumPixels: 2 + delta),
            "decoded" => new AssetPayloadPolicy(MaximumDecodedBytes: 8 + delta),
            _ => new AssetPayloadPolicy(MaximumDimension: 2 + delta)
        };
        if (delta < 0) Assert.Throws<AssetPayloadLimitException>(() => policy.ValidatePng(png));
        else policy.ValidatePng(png);
    }

    [Theory]
    [InlineData("truncated")] [InlineData("chunk")] [InlineData("duplicate")]
    [InlineData("color")] [InlineData("dimensions")] [InlineData("missing-data")]
    public async Task InvalidPngsReturnDiagnosticsWithoutPublishingPreviewData(string damage)
    {
        var png = AssetCreateServiceTests.Png();
        switch (damage)
        {
            case "truncated": png = png[..^1]; break;
            case "chunk": BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), uint.MaxValue); break;
            case "duplicate": "IHDR"u8.CopyTo(png.AsSpan(37)); break;
            case "color": png[25] = 255; break;
            case "dimensions": BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.MaxValue); break;
            case "missing-data": png = [.. png[..33], .. png[^12..]]; break;
        }
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("broken.png"); File.WriteAllBytes(path, png);
        var result = await new IconPreviewService(() => null, () => workspace.RootPath).LoadResultAsync("broken.png");
        Assert.Null(result.Value); Assert.NotEmpty(result.Error!);
        Assert.Equal(path, result.Source); Assert.Equal(png, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(16, uint.MaxValue)] [InlineData(12, uint.MaxValue)]
    [InlineData(28, uint.MaxValue)] [InlineData(112, 0x200u)]
    [InlineData(24, 2u)] [InlineData(140, uint.MaxValue)]
    [InlineData(136, 4u)] [InlineData(132, 4u)] [InlineData(128, uint.MaxValue)]
    public async Task DangerousDdsHeadersAreRefusedBeforeAllocatingMipOrPixelBuffers(int offset, uint value)
    {
        var bytes = DdsIconDecoderTests.CreateDds(CompressionFormat.Bc7); // DX10 header.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        var result = await DdsIconDecoder.ConvertResultAsync(bytes);
        Assert.Null(result.Value); Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public async Task DecodedArithmeticOverflowIsAReportedFailure()
    {
        var bytes = DdsIconDecoderTests.CreateDds(CompressionFormat.Bc1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), int.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), int.MaxValue);
        var policy = new AssetPayloadPolicy(MaximumDimension: int.MaxValue, MaximumPixels: long.MaxValue, MaximumDecodedBytes: long.MaxValue);
        var result = await DdsIconDecoder.ConvertResultAsync(bytes, policy);
        Assert.Null(result.Value); Assert.NotEmpty(result.Error!);
    }

    [Theory]
    [InlineData(CompressionFormat.Rgba)] [InlineData(CompressionFormat.Bc1)]
    [InlineData(CompressionFormat.Bc3)] [InlineData(CompressionFormat.Bc7)]
    public async Task ValidMipChainsAreAcceptedAndTruncatedMipPayloadsAreRefused(CompressionFormat format)
    {
        var bytes = DdsIconDecoderTests.CreateDds(format, generateMipMaps: true);
        Assert.NotNull((await DdsIconDecoder.ConvertResultAsync(bytes)).Value);
        var truncated = await DdsIconDecoder.ConvertResultAsync(bytes[..^1]);
        Assert.Null(truncated.Value); Assert.Contains("truncated", truncated.Error!);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SparseOversizedLoosePayloadsAreRejectedBeforeBuffering(bool xml)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath(xml ? "large.xml" : "large.png");
        var maximum = xml ? AssetPayloadPolicy.Default.MaximumXmlBytes : AssetPayloadPolicy.Default.MaximumImageBytes;
        using (var file = File.Create(path)) file.SetLength(maximum + 1L);
        var before = new FileInfo(path).Length;
        if (xml)
        {
            var result = await AnimFileCatalog.LoadCustomXmlResultAsync(workspace.RootPath, "large.xml");
            Assert.Null(result.Value); Assert.Contains("limit", result.Error!);
        }
        else
        {
            var result = await new IconPreviewService(() => null, () => workspace.RootPath).LoadResultAsync("large.png");
            Assert.Null(result.Value); Assert.Contains("limit", result.Error!);
        }
        Assert.Equal(before, new FileInfo(path).Length);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OversizedBarMetadataIsRejectedBeforeReading(bool expanded)
    {
        using var stream = new ReportedLengthStream(new byte[32], true, 32);
        var entry = new BarArchiveEntry("test.xml", 0, expanded ? 33 : 16, 16, expanded ? 16 : 33, true);
        Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadEntry(stream, entry, 32));
        Assert.Equal(0, stream.ReadCalls);
    }

    [Theory]
    [InlineData("alz4")] [InlineData("l33t")]
    public void InnerCompressionSizeCannotExceedTheAdmittedDestination(string magic)
    {
        var bytes = new byte[16]; Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 33);
        using var stream = new MemoryStream(bytes);
        var entry = new BarArchiveEntry("test.xml", 0, 16, 16, 16, true);
        Assert.Throws<InvalidDataException>(() => AssetPayloadReader.ReadEntry(stream, entry, 32));
    }

    [Theory]
    [InlineData(false, -1)] [InlineData(false, 0)] [InlineData(false, 1)]
    [InlineData(true, -1)] [InlineData(true, 0)] [InlineData(true, 1)]
    public void PlainAndCompiledXmlRespectCharacterBoundaries(bool compiled, int delta)
    {
        const string xml = "<r>text</r>";
        var bytes = compiled ? XmbReaderTests.BuildDocument(XElement.Parse(xml)) : Encoding.UTF8.GetBytes(xml);
        var policy = new AssetPayloadPolicy(MaximumXmlCharacters: xml.Length + delta);
        if (delta < 0 && compiled) Assert.Throws<InvalidDataException>(() => AssetPayloadReader.ReadXml(bytes, true, policy));
        else if (delta < 0) Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadXml(bytes, false, policy));
        else Assert.Equal(xml, AssetPayloadReader.ReadXml(bytes, compiled, policy));
    }

    [Theory]
    [InlineData("bytes", -1)] [InlineData("bytes", 0)] [InlineData("bytes", 1)]
    [InlineData("nodes", -1)] [InlineData("nodes", 0)] [InlineData("nodes", 1)]
    [InlineData("depth", -1)] [InlineData("depth", 0)] [InlineData("depth", 1)]
    public void XmlBytesNodesAndDepthHaveIndependentLimits(string boundary, int delta)
    {
        const string xml = "<r a='x'><n><x/></n></r>"; // Six nodes including the attribute; depth two.
        var bytes = Encoding.UTF8.GetBytes(xml);
        var policy = boundary switch
        {
            "bytes" => new AssetPayloadPolicy(MaximumXmlBytes: bytes.Length + delta),
            "nodes" => new AssetPayloadPolicy(MaximumXmlNodes: 6 + delta),
            _ => new AssetPayloadPolicy(MaximumXmlDepth: 2 + delta)
        };
        if (delta < 0) Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadXml(bytes, false, policy));
        else Assert.Equal(xml, AssetPayloadReader.ReadXml(bytes, false, policy));
    }

    [Fact]
    public void FormattingExpansionAndDtdsAreRejectedBeforeTheViewer()
    {
        const string xml = "<r><n/><n/><n/></r>";
        Assert.Throws<AssetPayloadLimitException>(() => AssetPayloadReader.ReadXml(Encoding.UTF8.GetBytes(xml), false,
            new AssetPayloadPolicy(MaximumXmlCharacters: xml.Length + 1)));
        Assert.Throws<System.Xml.XmlException>(() => AssetPayloadReader.ReadXml(
            "<!DOCTYPE r [<!ENTITY x 'expanded'>]><r>&x;</r>"u8.ToArray(), false, AssetPayloadPolicy.Default));
    }

    [Theory]
    [InlineData("utf8")] [InlineData("utf16")] [InlineData("utf16be")]
    public async Task LooseXmlRetainsBomEncodingAndUnicode(string encodingName)
    {
        using var workspace = new TestWorkspace();
        const string xml = "<r>é日本語</r>";
        var encoding = encodingName switch { "utf16" => Encoding.Unicode, "utf16be" => Encoding.BigEndianUnicode, _ => Encoding.UTF8 };
        var path = workspace.WriteText("unit.xml", xml, encoding);
        var bytes = File.ReadAllBytes(path);
        var result = await AnimFileCatalog.LoadCustomXmlResultAsync(workspace.RootPath, "unit.xml");
        Assert.Null(result.Error); Assert.Equal(xml, result.Value); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task AnimationAndSoundArchiveResultsPreservePlainAndCompiledXml(bool sound, bool compiled)
    {
        using var workspace = new TestWorkspace();
        const string xml = "<r>é日本語</r>";
        var bytes = compiled ? XmbReaderTests.BuildDocument(XElement.Parse(xml)) : Encoding.UTF8.GetBytes(xml);
        var relative = "test/unit.xml" + (compiled ? ".XMB" : "");
        using var bar = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry(relative, bytes, bytes.Length, false));
        var path = workspace.GetPath(sound ? "Sound.bar" : "ArtTest.bar"); File.WriteAllBytes(path, bar.ToArray());
        var before = File.ReadAllBytes(path);
        var item = new AnimFileCatalogEntry("test/unit.xml", Path.GetFileName(path));
        var result = sound ? await SoundSetCatalog.LoadXmlResultAsync(path, item) :
            await AnimFileCatalog.LoadXmlResultAsync(workspace.RootPath, item);
        Assert.Null(result.Error); Assert.Equal(xml, result.Value);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("icon")] [InlineData("animation")] [InlineData("sound")]
    public async Task RealConsumersRefuseAdvertisedBarExpansionWithoutChangingTheArchive(string kind)
    {
        using var workspace = new TestWorkspace();
        var image = kind == "icon";
        var relative = image ? "resources/test/large.dds" : "test/large.xml.XMB";
        using var bar = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry(relative, new byte[16],
            AssetPayloadPolicy.Default.MaximumXmlBytes + 1, true));
        var path = workspace.GetPath(kind == "sound" ? "Sound.bar" : "ArtTest.bar");
        var before = bar.ToArray(); File.WriteAllBytes(path, before);
        string? error;
        if (image)
        {
            var result = await new IconPreviewService(() => path, () => null).LoadResultAsync("test/large.png");
            Assert.Null(result.Value); error = result.Error;
        }
        else
        {
            var item = new AnimFileCatalogEntry("test/large.xml", Path.GetFileName(path));
            var result = kind == "sound" ? await SoundSetCatalog.LoadXmlResultAsync(path, item) :
                await AnimFileCatalog.LoadXmlResultAsync(workspace.RootPath, item);
            Assert.Null(result.Value); error = result.Error;
        }
        Assert.Contains("Expanded BAR payload", error!);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private sealed class ReportedLengthStream(byte[] bytes, bool seekable, long reportedLength) : MemoryStream(bytes)
    {
        internal int ReadCalls { get; private set; }
        public override bool CanSeek => seekable;
        public override long Length => reportedLength;
        public override int Read(byte[] buffer, int offset, int count)
        { ReadCalls++; return base.Read(buffer, offset, count); }
    }

    [Fact]
    public async Task TruncatedPngIsRejectedWithoutChangingTheAsset()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("resources");
        var path = workspace.GetPath("resources/broken.png");
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10];
        File.WriteAllBytes(path, bytes);
        var service = new IconPreviewService(() => null, () => root);
        Assert.Null(await service.LoadAsync("broken.png"));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
