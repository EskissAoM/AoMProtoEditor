using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AoMDivineDataEditor.GameData;
using K4os.Compression.LZ4;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class BarArchiveTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Configured_real_data_bar_loads_strings_and_representative_xmb()
    {
        string? dataBarPath = Environment.GetEnvironmentVariable("AOM_DATA_BAR");
        if (string.IsNullOrWhiteSpace(dataBarPath))
            return;

        using var stream = File.OpenRead(dataBarPath);
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out var error), error.ToString());
        Assert.NotNull(archive.Entries);
        Assert.True(archive.Entries.Count > 1_000);

        var stringEntry = Assert.Single(archive.Entries, entry =>
            entry.RelativePath.Replace('\\', '/')
                .EndsWith("strings/english/string_table.txt", StringComparison.OrdinalIgnoreCase));
        string stringTable = Encoding.UTF8.GetString(stringEntry.ReadDataDecompressed(stream));
        Assert.True(StringTableParser.Parse(stringTable).Count > 100);

        var xmbEntry = Assert.IsType<BarArchiveEntry>(archive.Entries.FirstOrDefault(entry =>
            entry.Name.Contains("proto", StringComparison.OrdinalIgnoreCase) &&
            entry.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)));
        Assert.False(string.IsNullOrWhiteSpace(XmbReader.ToFormattedXml(xmbEntry.ReadDataDecompressed(stream))));
    }

    [Fact]
    public void Load_reads_uncompressed_entry_metadata_and_content()
    {
        byte[] expected = "<proto>Villager</proto>"u8.ToArray();
        using var stream = BuildArchive(new TestEntry("game\\data\\proto.xml", expected, expected.Length, false));
        var archive = new BarArchive(stream);

        Assert.True(archive.Load(out var error));
        Assert.Equal(BarArchiveLoadError.None, error);
        Assert.Equal((uint)6, archive.Version);
        Assert.Equal("game\\data", archive.RootPath);

        var entry = Assert.Single(archive.Entries!);
        Assert.Equal("game\\data\\proto.xml", entry.RelativePath);
        Assert.Equal("proto.xml", entry.Name);
        Assert.Equal("game\\data\\", entry.DirectoryPath);

        var output = new byte[entry.SizeUncompressed];
        Assert.Equal(expected.Length, entry.ReadDataDecompressed(stream, output));
        Assert.Equal(expected, output);
        Assert.Equal(expected, entry.ReadDataDecompressed(stream));
    }

    [Fact]
    public void ReadDataDecompressed_decodes_alz4_entry()
    {
        byte[] expected = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Retold-data-", 64)));
        byte[] compressed = CompressAlz4(expected);
        using var stream = BuildArchive(new TestEntry("data\\compressed.xmb", compressed, expected.Length, true));
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out _));

        var output = new byte[expected.Length];
        int bytesRead = archive.Entries![0].ReadDataDecompressed(stream, output);

        Assert.Equal(expected.Length, bytesRead);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void ReadDataDecompressed_decodes_l33t_entry()
    {
        byte[] expected = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("scenario-data-", 32)));
        byte[] compressed = CompressL33t(expected);
        using var stream = BuildArchive(new TestEntry("scenario\\sample.mythscn", compressed, expected.Length, true));
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out _));

        var output = new byte[expected.Length];
        int bytesRead = archive.Entries![0].ReadDataDecompressed(stream, output);

        Assert.Equal(expected.Length, bytesRead);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void Load_rejects_invalid_header()
    {
        using var stream = BuildArchive(new TestEntry("data\\file.txt", "value"u8.ToArray(), 5, false));
        stream.GetBuffer()[0] = (byte)'X';
        var archive = new BarArchive(stream);

        Assert.False(archive.Load(out var error));
        Assert.Equal(BarArchiveLoadError.InvalidHeader, error);
    }

    [Fact]
    public void Load_rejects_entry_payload_outside_archive_data_region()
    {
        using var stream = BuildArchive(new TestEntry("data\\file.txt", "value"u8.ToArray(), 5, false));
        byte[] buffer = stream.GetBuffer();
        long tableOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(288, 8));
        int rootPathBytes = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)tableOffset, 4)) * 2;
        int firstEntryOffsetField = checked((int)tableOffset + 4 + rootPathBytes + 4);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(firstEntryOffsetField, 8), tableOffset + 1);
        var archive = new BarArchive(stream);

        Assert.False(archive.Load(out var error));
        Assert.Equal(BarArchiveLoadError.InvalidFormat, error);
    }

    private static MemoryStream BuildArchive(params TestEntry[] entries)
    {
        const int headerSize = 296;
        var stream = new MemoryStream();
        stream.SetLength(headerSize);
        stream.Position = headerSize;

        var contentOffsets = new long[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            contentOffsets[index] = stream.Position;
            stream.Write(entries[index].ArchiveData);
        }

        long tableOffset = stream.Position;
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            const string rootPath = "game\\data";
            writer.Write(rootPath.Length);
            writer.Write(Encoding.Unicode.GetBytes(rootPath));
            writer.Write(entries.Length);

            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                writer.Write(contentOffsets[index]);
                writer.Write(entry.UncompressedSize);
                writer.Write(entry.ArchiveData.Length);
                writer.Write(entry.ArchiveData.Length);
                writer.Write(entry.RelativePath.Length);
                writer.Write(Encoding.Unicode.GetBytes(entry.RelativePath));
                writer.Write(entry.IsCompressed ? 1u : 0u);
            }
        }

        stream.Position = 0;
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("ESPN"u8);
            writer.Write(6u);
            writer.Write(1_144_201_745u);
            writer.Write(new byte[264]);
            writer.Write(0u);
            writer.Write(entries.Length);
            writer.Write(0u);
            writer.Write(tableOffset);
        }

        stream.Position = 0;
        return stream;
    }

    private static byte[] CompressAlz4(byte[] input)
    {
        byte[] output = new byte[16 + LZ4Codec.MaximumOutputSize(input.Length)];
        "alz4"u8.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), input.Length);
        int compressedSize = LZ4Codec.Encode(input, output.AsSpan(16));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), compressedSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), 1);
        return output[..(16 + compressedSize)];
    }

    private static byte[] CompressL33t(byte[] input)
    {
        using var zlibOutput = new MemoryStream();
        using (var zlib = new ZLibStream(zlibOutput, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(input);

        byte[] zlibData = zlibOutput.ToArray();
        byte[] output = new byte[8 + zlibData.Length];
        "l33t"u8.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), input.Length);
        zlibData.CopyTo(output, 8);
        return output;
    }

    private sealed record TestEntry(string RelativePath, byte[] ArchiveData, int UncompressedSize, bool IsCompressed);
}
