using System.Reflection;
using System.Collections.Concurrent;
using AoMDivineDataEditor.Classes;
using BCnEncoder.Shared;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconPreviewCacheTests
{
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public async Task RetainedBytesRespectExactBudgetBoundaries(int delta)
    {
        using var workspace = new TestWorkspace();
        var png = AssetCreateServiceTests.Png();
        WriteIcon(workspace, "a.png"); WriteIcon(workspace, "b.png");
        var service = Create(workspace, new IconPreviewCacheOptions(2L * png.Length + delta, 8));
        Assert.NotNull(await service.LoadAsync("a.png"));
        Assert.NotNull(await service.LoadAsync("b.png"));
        var state = service.GetCacheState();
        Assert.Equal(delta < 0 ? 1 : 2, state.Entries);
        Assert.Equal(state.Entries * png.Length, state.Bytes);
        Assert.Equal(0, state.Pending); Assert.Equal(0, state.Active);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NonRetainedResultsRemainUsableByTheirCaller(bool disableRetention)
    {
        using var workspace = new TestWorkspace();
        var path = WriteIcon(workspace, "a.png");
        var png = File.ReadAllBytes(path);
        var loads = 0;
        var service = Create(workspace, disableRetention ? new IconPreviewCacheOptions(MaximumEntries: 0) :
            new IconPreviewCacheOptions(MaximumBytes: png.Length - 1), _ => { Interlocked.Increment(ref loads); return Task.CompletedTask; });
        var first = await service.LoadAsync("a.png");
        Assert.NotNull(first); Assert.Equal(png, first.PngBytes);
        Assert.NotNull(await service.LoadAsync("a.png"));
        Assert.Equal(2, loads); Assert.Equal(0, service.GetCacheState().Entries);
        Assert.Equal(0, service.GetCacheState().Bytes);
        Assert.Equal(png, first.PngBytes); // Removing cache ownership does not dispose caller data.
    }

    [Fact]
    public async Task EntryLimitEvictsTheLeastRecentlyUsedSource()
    {
        using var workspace = new TestWorkspace();
        foreach (var name in new[] { "a.png", "b.png", "c.png" }) WriteIcon(workspace, name);
        var loads = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var service = Create(workspace, new IconPreviewCacheOptions(MaximumEntries: 2), source =>
        { loads.AddOrUpdate(Path.GetFileName(source), 1, (_, count) => count + 1); return Task.CompletedTask; });
        var a = await service.LoadAsync("a.png");
        var b = await service.LoadAsync("b.png");
        Assert.Same(a, await service.LoadAsync("a.png"));
        await service.LoadAsync("c.png");
        Assert.Same(a, await service.LoadAsync("a.png"));
        Assert.NotSame(b, await service.LoadAsync("b.png"));
        Assert.Equal(1, loads["a.png"]); Assert.Equal(2, loads["b.png"]);
        Assert.Equal(2, service.GetCacheState().Entries);
        Assert.Equal(2L * AssetCreateServiceTests.Png().Length, service.GetCacheState().Bytes);
        Assert.Equal(AssetCreateServiceTests.Png(), b!.PngBytes);
    }

    [Fact]
    public async Task ConcurrentAliasesShareOneLiveLoadAndOneResult()
    {
        using var workspace = new TestWorkspace(); WriteIcon(workspace, "a.png");
        var started = Signal(); var release = Signal(); var loads = 0;
        var service = Create(workspace, beforeLoad: _ =>
        { Interlocked.Increment(ref loads); started.TrySetResult(); return release.Task; });
        var tasks = new List<Task<AssetPayloadResult<IconPreviewData>>> { service.LoadResultAsync("a.png") };
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tasks.AddRange(Enumerable.Range(0, 20).Select(index => service.LoadResultAsync(index % 2 == 0 ? "resources/a.png" : "a.png")));
            Assert.Equal(1, service.GetCacheState().Pending); Assert.Equal(1, service.GetCacheState().Active);
            release.TrySetResult();
            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(results[0].Value);
            Assert.All(results, result => Assert.Same(results[0].Value, result.Value));
            Assert.Equal(1, loads); Assert.Equal(0, service.GetCacheState().Pending);
        }
        finally { release.TrySetResult(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task DifferentFilesQueueBeforePayloadLoadingAndRespectTheConcurrencyLimit()
    {
        using var workspace = new TestWorkspace();
        foreach (var name in new[] { "a.png", "b.png", "c.png" }) WriteIcon(workspace, name);
        var twoStarted = Signal(); var release = Signal(); var started = 0; var peak = 0;
        IconPreviewService service = null!;
        service = Create(workspace, new IconPreviewCacheOptions(MaximumConcurrentLoads: 2), _ =>
        {
            peak = Math.Max(peak, service.GetCacheState().Active);
            if (Interlocked.Increment(ref started) == 2) twoStarted.TrySetResult();
            return release.Task;
        });
        var tasks = new[] { service.LoadAsync("a.png"), service.LoadAsync("b.png"), service.LoadAsync("c.png") };
        try
        {
            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, started); Assert.Equal(2, service.GetCacheState().Active);
            Assert.Equal(3, service.GetCacheState().Pending);
            release.TrySetResult();
            Assert.All(await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)), Assert.NotNull);
            Assert.Equal(2, peak); Assert.Equal(3, started);
            Assert.Equal(0, service.GetCacheState().Pending); Assert.Equal(0, service.GetCacheState().Active);
        }
        finally { release.TrySetResult(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task FaultedTasksAreRemovedAndTheSameSourceCanRetry()
    {
        using var workspace = new TestWorkspace(); WriteIcon(workspace, "a.png"); var loads = 0;
        var service = Create(workspace, beforeLoad: _ => Interlocked.Increment(ref loads) == 1
            ? Task.FromException(new TestPreviewFault()) : Task.CompletedTask);
        await Assert.ThrowsAsync<TestPreviewFault>(() => service.LoadResultAsync("a.png"));
        Assert.Equal(new IconPreviewCacheState(0, 0, 0, 0), service.GetCacheState());
        Assert.NotNull(await service.LoadAsync("a.png")); Assert.Equal(2, loads);
    }

    [Fact]
    public async Task UnsuccessfulResultsRetryAtTheSameIdentityAndInvalidNewRevisionsReleaseOldBytes()
    {
        using var workspace = new TestWorkspace();
        var path = WriteIcon(workspace, "a.png"); var png = File.ReadAllBytes(path); var bad = (byte[])png.Clone(); bad[0] = 0;
        var stamp = File.GetLastWriteTimeUtc(path); File.WriteAllBytes(path, bad); File.SetLastWriteTimeUtc(path, stamp);
        var service = Create(workspace);
        Assert.Null((await service.LoadResultAsync("a.png")).Value);
        Assert.Equal(new IconPreviewCacheState(0, 0, 0, 0), service.GetCacheState());
        File.WriteAllBytes(path, png); File.SetLastWriteTimeUtc(path, stamp);
        var held = await service.LoadAsync("a.png"); Assert.NotNull(held);
        File.WriteAllBytes(path, bad); File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
        Assert.Null((await service.LoadResultAsync("a.png")).Value);
        Assert.Equal(new IconPreviewCacheState(0, 0, 0, 0), service.GetCacheState());
        Assert.Equal(png, held.PngBytes);
    }

    [Fact]
    public async Task OldInFlightRevisionCannotReplaceTheLatestPreview()
    {
        using var workspace = new TestWorkspace(); var path = WriteIcon(workspace, "a.png");
        var stamp = File.GetLastWriteTimeUtc(path); var started = Signal(); var release = Signal(); var loads = 0;
        var service = Create(workspace, beforeLoad: _ =>
        { if (Interlocked.Increment(ref loads) != 1) return Task.CompletedTask; started.TrySetResult(); return release.Task; });
        var old = service.LoadResultAsync("a.png");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
            var current = await service.LoadAsync("a.png"); Assert.NotNull(current);
            release.TrySetResult();
            Assert.Contains("changed", (await old.WaitAsync(TimeSpan.FromSeconds(5))).Error!);
            Assert.Same(current, await service.LoadAsync("a.png"));
            Assert.Equal(1, service.GetCacheState().Entries); Assert.Equal(2, loads);
        }
        finally { release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task RevisedQueuedSourcesAreRefusedBeforePayloadWorkAndCanRetry()
    {
        using var workspace = new TestWorkspace(); WriteIcon(workspace, "a.png"); var path = WriteIcon(workspace, "b.png");
        var started = Signal(); var release = Signal(); var loads = 0;
        var service = Create(workspace, new IconPreviewCacheOptions(MaximumConcurrentLoads: 1), _ =>
        { Interlocked.Increment(ref loads); started.TrySetResult(); return release.Task; });
        var first = service.LoadResultAsync("a.png");
        Task<AssetPayloadResult<IconPreviewData>>? queued = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queued = service.LoadResultAsync("b.png");
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
            release.TrySetResult();
            Assert.NotNull((await first).Value);
            Assert.Contains("changed while queued", (await queued.WaitAsync(TimeSpan.FromSeconds(5))).Error!);
            Assert.Equal(1, loads);
            Assert.NotNull(await service.LoadAsync("b.png")); Assert.Equal(2, loads);
        }
        finally
        {
            release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (queued != null) await queued.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SwitchingModsReleasesRetentionWithoutCancellingOtherCallers()
    {
        using var workspace = new TestWorkspace(); WriteIcon(workspace, "first/a.png"); WriteIcon(workspace, "second/a.png");
        var root = workspace.GetPath("first"); var oldRoot = root;
        var started = Signal(); var release = Signal();
        var service = new IconPreviewService(() => null, () => root, AssetPayloadPolicy.Default, beforeLoad: source =>
        { if (!source.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask; started.TrySetResult(); return release.Task; });
        var old = service.LoadAsync("a.png");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            root = workspace.GetPath("second");
            var current = await service.LoadAsync("a.png"); Assert.NotNull(current);
            release.TrySetResult();
            Assert.StartsWith(oldRoot, (await old.WaitAsync(TimeSpan.FromSeconds(5)))!.ResolvedSource);
            Assert.Same(current, await service.LoadAsync("a.png")); Assert.Equal(1, service.GetCacheState().Entries);
            root = oldRoot; service.RefreshSourceContext();
            Assert.Equal(new IconPreviewCacheState(0, 0, 0, 0), service.GetCacheState());
            Assert.NotNull(current.PngBytes); // Existing consumers keep their payload.
        }
        finally { release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RevisedOrMalformedArchivesReleaseEveryOldEntry(bool malformed)
    {
        using var workspace = new TestWorkspace(); var path = WriteArchive(workspace, "one.bar");
        var stamp = File.GetLastWriteTimeUtc(path); var service = new IconPreviewService(() => path, () => null);
        var held = await service.LoadAsync("test/a.png"); Assert.NotNull(held);
        Assert.NotNull(await service.LoadAsync("test/b.png")); Assert.Equal(2, service.GetCacheState().Entries);
        if (malformed) File.WriteAllText(path, "malformed archive");
        else WriteArchive(workspace, "one.bar");
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
        var current = await service.LoadAsync("test/a.png");
        Assert.Equal(malformed ? 0 : 1, service.GetCacheState().Entries);
        if (malformed) { Assert.Null(current); Assert.Equal(0, service.GetCacheState().Bytes); }
        else Assert.NotSame(held, current);
        Assert.NotEmpty(held.PngBytes);
        if (!malformed)
        {
            path = WriteArchive(workspace, "two.bar"); service.RefreshSourceContext();
            Assert.Equal(0, service.GetCacheState().Entries); Assert.Equal(0, service.GetCacheState().Bytes);
            Assert.NotNull(await service.LoadAsync("test/a.png"));
        }
    }

    private static IconPreviewService Create(TestWorkspace workspace, IconPreviewCacheOptions? options = null, Func<string, Task>? beforeLoad = null)
        => new(() => null, () => workspace.RootPath, AssetPayloadPolicy.Default, options, beforeLoad);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static string WriteIcon(TestWorkspace workspace, string relative)
    {
        var path = workspace.GetPath(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, AssetCreateServiceTests.Png()); return path;
    }
    private static string WriteArchive(TestWorkspace workspace, string name)
    {
        var dds = DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba);
        using var bar = BarArchiveTests.BuildArchive(new("resources/test/a.dds", dds, dds.Length, false),
            new("resources/test/b.dds", dds, dds.Length, false));
        var path = workspace.GetPath(name); File.WriteAllBytes(path, bar.ToArray()); return path;
    }
    private sealed class TestPreviewFault : Exception;

    [Fact]
    public async Task SupersededLooseRevisionsDoNotAccumulatePayloads()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("icon.png");
        File.WriteAllBytes(path, AssetCreateServiceTests.Png());
        var stamp = File.GetLastWriteTimeUtc(path);
        var service = new IconPreviewService(() => null, () => workspace.RootPath);
        for (var revision = 0; revision < 4; revision++)
        {
            File.SetLastWriteTimeUtc(path, stamp.AddSeconds(revision * 2));
            Assert.NotNull(await service.LoadAsync("icon.png"));
        }
        var cache = typeof(IconPreviewService).GetField("_previewCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Equal(1, (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!);
    }
}
