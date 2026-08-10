namespace AoMDivineDataEditor.GameData;

public sealed class BarArchiveEntry
{
    internal BarArchiveEntry(
        string relativePath,
        long contentOffset,
        int uncompressedSize,
        int compressedSize,
        int archiveSize,
        bool isCompressed)
    {
        RelativePath = relativePath;
        ContentOffset = contentOffset;
        SizeUncompressed = uncompressedSize;
        SizeCompressed = compressedSize;
        SizeInArchive = archiveSize;
        IsCompressed = isCompressed;

        int separator = relativePath.LastIndexOfAny(['\\', '/']);
        Name = separator >= 0 ? relativePath[(separator + 1)..] : relativePath;
        DirectoryPath = separator >= 0 ? relativePath[..(separator + 1)] : string.Empty;
    }

    public long ContentOffset { get; }
    public int SizeUncompressed { get; }
    public int SizeCompressed { get; }
    public int SizeInArchive { get; }
    public string RelativePath { get; }
    public bool IsCompressed { get; }
    public string Name { get; }
    public string DirectoryPath { get; }

    public byte[] ReadDataRaw(Stream archiveStream)
    {
        var data = new byte[SizeInArchive];
        ReadExactlyAtOffset(archiveStream, data);
        return data;
    }

    public int ReadDataDecompressed(Stream archiveStream, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        int requiredSize = IsCompressed ? SizeUncompressed : SizeInArchive;
        if (requiredSize > destination.Length)
            throw new ArgumentException("The destination buffer is too small for this BAR entry.", nameof(destination));

        if (!IsCompressed)
        {
            ReadExactlyAtOffset(archiveStream, destination[..SizeInArchive]);
            return SizeInArchive;
        }

        byte[] raw = ReadDataRaw(archiveStream);
        return BarDecompressor.Decompress(raw, destination);
    }

    public byte[] ReadDataDecompressed(Stream archiveStream)
    {
        var data = new byte[IsCompressed ? SizeUncompressed : SizeInArchive];
        int bytesRead = ReadDataDecompressed(archiveStream, data);
        return bytesRead == data.Length ? data : data[..bytesRead];
    }

    private void ReadExactlyAtOffset(Stream archiveStream, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!archiveStream.CanSeek)
            throw new ArgumentException("The BAR archive stream must be seekable.", nameof(archiveStream));

        archiveStream.Position = ContentOffset;
        archiveStream.ReadExactly(destination);
    }

    public override string ToString() => $"{RelativePath} ({SizeInArchive} bytes)";
}
