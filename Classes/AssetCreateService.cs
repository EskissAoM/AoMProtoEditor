using System.Text;
using SkiaSharp;

namespace AoMDivineDataEditor.Classes;

/// <summary>Stages a new asset beside its destination and publishes it without replacement.</summary>
internal static class AssetCreateService
{
    internal const int MaxPngBytes = 32 * 1024 * 1024;
    internal const int MaxPngDimension = 8192;
    internal const long MaxPngPixels = 16 * 1024 * 1024;

    public static Task CreateXmlAsync(string rootDirectory, AssetDestination destination, string xml,
        CancellationToken cancellationToken = default)
        => CreateAsync(rootDirectory, destination.AbsolutePath, async (output, token) =>
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(xml.AsMemory(), token);
            await writer.FlushAsync(token);
        }, cancellationToken: cancellationToken);

    public static Task ImportPngAsync(string rootDirectory, AssetDestination destination, string sourcePath,
        CancellationToken cancellationToken = default)
        => CreateAsync(rootDirectory, destination.AbsolutePath, async (output, token) =>
        {
            if (!Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select an existing PNG image.");
            FileIntegrityTransaction.EnsureRegularFileOrMissing(sourcePath);
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length > MaxPngBytes)
                throw new InvalidDataException("PNG icons must be at most 32 MiB.");
            var buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, token)) != 0)
            {
                copied += count;
                if (copied > MaxPngBytes)
                    throw new InvalidDataException("PNG icons must be at most 32 MiB.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
        }, ValidatePng, cancellationToken);

    private static void ValidatePng(Stream stream)
    {
        stream.Position = 0;
        using var managed = new SKManagedStream(stream, disposeManagedStream: false);
        using var codec = SKCodec.Create(managed);
        if (codec == null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException("The selected file is not a valid PNG image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxPngDimension || info.Height > MaxPngDimension ||
            (long)info.Width * info.Height > MaxPngPixels)
            throw new InvalidDataException("PNG icons must fit within 8192 × 8192 and 16 million pixels.");
        // Decode only after inspecting bounded dimensions, without changing the imported bytes.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The PNG image is incomplete or damaged.");
    }

    internal static async Task CreateAsync(string rootDirectory, string destinationPath,
        Func<Stream, CancellationToken, Task> write, Action<Stream>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(destinationPath);
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The destination must remain inside the mod asset folder.");

        void CheckDestination()
        {
            FileIntegrityTransaction.EnsureRegularFileOrMissing(destination);
            if (File.Exists(destination))
                throw new IOException("A file already exists at this destination. It was not overwritten.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        CheckDestination();
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        CheckDestination();
        var temporary = Path.Combine(directory, ".aom-asset-" + Guid.NewGuid().ToString("N") + ".tmp");
        var ownsTemporary = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await write(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                validate?.Invoke(output);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            CheckDestination();
            FileIntegrityTransaction.EnsureRegularFileOrMissing(temporary);
            File.Move(temporary, destination, overwrite: false);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary)
            {
                try
                {
                    // Never follow a newly linked directory while removing staging output.
                    FileIntegrityTransaction.EnsureRegularFileOrMissing(temporary);
                    File.Delete(temporary);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
