using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using AoMDivineDataEditor.Classes;
using SkiaSharp;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetCreateServiceTests
{
    private static AssetDestination Destination(string root, string folder = "nested", string extension = ".xml")
    {
        Assert.True(AssetDestinationPolicy.TryResolve(root, folder, "created", extension, "", out var destination, out var error), error);
        return destination!;
    }

    internal static byte[] Png()
    {
        using var bitmap = new SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, new SKColor(20, 40, 60, 128));
        bitmap.SetPixel(1, 0, SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static void AssertNoStaging(string root)
        => Assert.Empty(Directory.GetFiles(root, ".aom-asset-*.tmp", SearchOption.AllDirectories));

    internal static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(link, target);
        else
        {
            // Junctions exercise the same reparse-point guard without requiring
            // Windows' symbolic-link privilege. Failure is a test failure, not a skip.
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(5000), "Junction creation timed out.");
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }

    [Theory]
    [InlineData("art")]
    [InlineData("sound")]
    public async Task XmlCreatesMissingFoldersAndPreservesTextWithoutBom(string folder)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.GetPath(folder);
        var destination = Destination(root);
        const string xml = "<?xml version=\"1.0\"?>\r\n<asset><!--keep--><future attr='é'>音</future></asset>";
        await AssetCreateService.CreateXmlAsync(root, destination, xml);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), File.ReadAllBytes(destination.AbsolutePath));
        AssertNoStaging(root);
    }

    [Fact]
    public async Task PngValidationPreservesOriginalBytesAndSource()
    {
        using var workspace = new TestWorkspace();
        var bytes = Png();
        var source = workspace.GetPath("source.png");
        File.WriteAllBytes(source, bytes);
        var root = workspace.CreateDirectory("resources");
        var destination = Destination(root, extension: ".png");
        await AssetCreateService.ImportPngAsync(root, destination, source);
        Assert.Equal(bytes, File.ReadAllBytes(destination.AbsolutePath));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        AssertNoStaging(root);
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("truncated")]
    [InlineData("encoded-size")]
    [InlineData("dimensions")]
    [InlineData("pixels")]
    [InlineData("extension")]
    public async Task InvalidOrOversizedPngNeverPublishes(string reason)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.GetPath(reason == "extension" ? "source.jpg" : "source.png");
        var bytes = Png();
        if (reason == "fake") bytes = "This is not a PNG"u8.ToArray();
        if (reason == "truncated") bytes = bytes[..40];
        if (reason is "dimensions" or "pixels")
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), reason == "dimensions" ? 9000 : 5000);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), reason == "dimensions" ? 1 : 5000);
            uint crc = uint.MaxValue;
            foreach (var value in bytes.AsSpan(12, 17))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29), ~crc);
        }
        File.WriteAllBytes(source, bytes);
        if (reason == "encoded-size")
        {
            using var oversized = File.OpenWrite(source);
            oversized.SetLength(AssetCreateService.MaxPngBytes + 1L);
        }
        var root = workspace.CreateDirectory("resources");
        var destination = Destination(root, extension: ".png");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => AssetCreateService.ImportPngAsync(root, destination, source));
        if (reason is "dimensions" or "pixels") Assert.Contains("8192", error.Message);
        Assert.False(File.Exists(destination.AbsolutePath));
        AssertNoStaging(root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingAndLateTargetsAreNeverOverwrittenOrRemoved(bool late)
    {
        using var workspace = new TestWorkspace();
        var destination = Destination(workspace.RootPath, "");
        if (!late) File.WriteAllText(destination.AbsolutePath, "competitor");
        var wrote = false;
        await Assert.ThrowsAsync<IOException>(() => AssetCreateService.CreateAsync(workspace.RootPath, destination.AbsolutePath, async (stream, token) =>
        {
            wrote = true;
            await stream.WriteAsync("our output"u8.ToArray(), token);
            File.WriteAllText(destination.AbsolutePath, "competitor");
        }));
        Assert.Equal(late, wrote);
        Assert.Equal("competitor", File.ReadAllText(destination.AbsolutePath));
        AssertNoStaging(workspace.RootPath);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancel-before")]
    [InlineData("cancel-during")]
    public async Task FailureOrCancellationDoesNotExposePartialOutput(string mode)
    {
        using var workspace = new TestWorkspace();
        var destination = Destination(workspace.RootPath);
        using var cancellation = new CancellationTokenSource();
        if (mode == "cancel-before") cancellation.Cancel();
        var error = await Record.ExceptionAsync(() => AssetCreateService.CreateAsync(workspace.RootPath, destination.AbsolutePath, async (stream, token) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), token);
            Assert.False(File.Exists(destination.AbsolutePath));
            if (mode == "failure") throw new IOException("interrupted copy");
            cancellation.Cancel();
        }, cancellationToken: cancellation.Token));
        if (mode == "failure") Assert.IsType<IOException>(error);
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.False(File.Exists(destination.AbsolutePath));
        AssertNoStaging(workspace.RootPath);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("prefix-sibling")]
    [InlineData("root-file")]
    [InlineData("target-directory")]
    public async Task InvalidDestinationsRefuseBeforeWriting(string mode)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("assets");
        var target = Path.Combine(root, "created.xml");
        if (mode == "outside") target = workspace.GetPath("outside.xml");
        if (mode == "prefix-sibling") target = workspace.GetPath("assets-other/created.xml");
        if (mode == "root-file") { Directory.Delete(root); File.WriteAllText(root, "unchanged"); }
        if (mode == "target-directory") Directory.CreateDirectory(target);
        var wrote = false;
        await Assert.ThrowsAnyAsync<IOException>(() => AssetCreateService.CreateAsync(root, target, (_, _) =>
        {
            wrote = true;
            return Task.CompletedTask;
        }));
        Assert.False(wrote);
        Assert.False(File.Exists(target));
        if (mode == "root-file") Assert.Equal("unchanged", File.ReadAllText(root));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LinkedRootOrNestedDirectoryRefusesCreationAndDialogResolution(bool linkedRoot, bool dangling)
    {
        using var workspace = new TestWorkspace();
        var outside = workspace.CreateDirectory("outside");
        var root = workspace.CreateDirectory("assets");
        var link = linkedRoot ? root : Path.Combine(root, "linked");
        var destination = Destination(root, linkedRoot ? "" : "linked");
        if (linkedRoot) Directory.Delete(root);
        CreateDirectoryLink(link, outside);
        if (dangling) Directory.Delete(outside);
        try
        {
            Assert.False(AssetDestinationPolicy.TryResolve(root, linkedRoot ? "" : "linked", "created", ".xml", "", out _, out _));
            await Assert.ThrowsAsync<IOException>(() => AssetCreateService.CreateAsync(root, destination.AbsolutePath, (_, _) => Task.CompletedTask));
            if (dangling) Assert.False(Directory.Exists(outside));
            else Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally { Directory.Delete(link); }
    }
}
