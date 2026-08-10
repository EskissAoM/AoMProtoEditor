using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace AoMDivineDataEditor.GameData;

public enum BarArchiveLoadError
{
    None,
    AlreadyLoaded,
    StreamNotSeekable,
    FileTooSmall,
    InvalidHeader,
    UnsupportedVersion,
    InvalidFormat,
    InvalidEntryCount,
    InvalidEntryName
}

/// <summary>
/// Read-only reader for the version 6 BAR archives used by Age of Mythology: Retold.
/// </summary>
public sealed class BarArchive
{
    private const int HeaderSize = 296;
    private const int MaximumTextBytes = 4096;
    private const int MaximumEntryCount = 1_000_000;
    internal const int MaximumEntrySize = 500_000_000;

    private readonly Stream _stream;
    private List<BarArchiveEntry>? _entries;

    public BarArchive(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public bool IsLoaded { get; private set; }
    public uint Version { get; private set; }
    public string? RootPath { get; private set; }
    public IReadOnlyList<BarArchiveEntry>? Entries => _entries;

    [MemberNotNullWhen(true, nameof(RootPath), nameof(_entries))]
    public bool Load(out BarArchiveLoadError error)
    {
        if (IsLoaded)
        {
            error = BarArchiveLoadError.AlreadyLoaded;
            return false;
        }

        if (!_stream.CanSeek)
        {
            error = BarArchiveLoadError.StreamNotSeekable;
            return false;
        }

        if (_stream.Length <= HeaderSize)
        {
            error = BarArchiveLoadError.FileTooSmall;
            return false;
        }

        try
        {
            _stream.Position = 0;
            Span<byte> header = stackalloc byte[HeaderSize];
            _stream.ReadExactly(header);

            if (!header[..4].SequenceEqual("ESPN"u8))
            {
                error = BarArchiveLoadError.InvalidHeader;
                return false;
            }

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
            if (version != 6)
            {
                error = BarArchiveLoadError.UnsupportedVersion;
                return false;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4)) != 1_144_201_745)
            {
                error = BarArchiveLoadError.InvalidFormat;
                return false;
            }

            int entryCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(280, 4));
            if (entryCount < 0 || entryCount > MaximumEntryCount)
            {
                error = BarArchiveLoadError.InvalidEntryCount;
                return false;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(284, 4)) != 0)
            {
                error = BarArchiveLoadError.InvalidFormat;
                return false;
            }

            long tableOffset = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(288, 8));
            if (tableOffset < HeaderSize || tableOffset >= _stream.Length)
            {
                error = BarArchiveLoadError.InvalidFormat;
                return false;
            }

            _stream.Position = tableOffset;
            int rootPathBytes = checked(ReadInt32(_stream) * 2);
            if (!IsValidTextLength(rootPathBytes))
            {
                error = BarArchiveLoadError.InvalidFormat;
                return false;
            }

            string rootPath = ReadUtf16(_stream, rootPathBytes);
            if (ReadInt32(_stream) != entryCount)
            {
                error = BarArchiveLoadError.InvalidFormat;
                return false;
            }

            var entries = new List<BarArchiveEntry>(entryCount);
            for (int index = 0; index < entryCount; index++)
            {
                long contentOffset = ReadInt64(_stream);
                int uncompressedSize = ReadInt32(_stream);
                int compressedSize = ReadInt32(_stream);
                int archiveSize = ReadInt32(_stream);
                int pathBytes = checked(ReadInt32(_stream) * 2);

                if (!IsValidTextLength(pathBytes))
                {
                    error = BarArchiveLoadError.InvalidEntryName;
                    return false;
                }

                string relativePath = ReadUtf16(_stream, pathBytes);
                bool isCompressed = ReadUInt32(_stream) == 1;

                if (!IsValidEntryBounds(contentOffset, archiveSize, uncompressedSize, tableOffset))
                {
                    error = BarArchiveLoadError.InvalidFormat;
                    return false;
                }

                entries.Add(new BarArchiveEntry(
                    relativePath,
                    contentOffset,
                    uncompressedSize,
                    compressedSize,
                    archiveSize,
                    isCompressed));
            }

            RootPath = rootPath;
            Version = version;
            _entries = entries;
            IsLoaded = true;
            error = BarArchiveLoadError.None;
#pragma warning disable CS8775 // The public Entries property returns the now-populated backing field.
            return true;
#pragma warning restore CS8775
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or OverflowException or ArgumentException)
        {
            error = BarArchiveLoadError.InvalidFormat;
            return false;
        }
    }

    private bool IsValidEntryBounds(long offset, int archiveSize, int uncompressedSize, long tableOffset)
    {
        if (offset < HeaderSize || archiveSize < 0 || uncompressedSize < 0 ||
            archiveSize > MaximumEntrySize || uncompressedSize > MaximumEntrySize)
        {
            return false;
        }

        // BAR payloads precede the file table. Checking without addition avoids overflow.
        return offset <= tableOffset && archiveSize <= tableOffset - offset;
    }

    private static bool IsValidTextLength(int byteLength)
        => byteLength > 0 && byteLength <= MaximumTextBytes && (byteLength & 1) == 0;

    private static string ReadUtf16(Stream stream, int byteLength)
    {
        var bytes = new byte[byteLength];
        stream.ReadExactly(bytes);
        return Encoding.Unicode.GetString(bytes);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static long ReadInt64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }
}
