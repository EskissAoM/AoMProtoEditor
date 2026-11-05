using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ArchiveCatalogCacheTests
{
    [Fact]
    public async Task RevisionsReplaceOneSourceAndLeastRecentSourcesAreBounded()
    {
        using var workspace = new TestWorkspace();
        var loads = 0;
        var cache = new ArchiveCatalogCache<string>(path => { Interlocked.Increment(ref loads); return File.ReadAllText(path); }, capacity: 2);
        var first = workspace.WriteText("first", "a");
        for (var index = 1; index <= 12; index++)
        {
            File.WriteAllText(first, new string('a', index));
            Assert.Equal(index, (await cache.LoadAsync(first)).Length);
            Assert.Equal(1, cache.RetainedCount);
        }
        var second = workspace.WriteText("second", "b");
        var third = workspace.WriteText("third", "c");
        await cache.LoadAsync(second);
        await cache.LoadAsync(first); // Refresh first's recency; second must be evicted.
        await cache.LoadAsync(third);
        Assert.Equal(2, cache.RetainedCount);
        var before = loads;
        await cache.LoadAsync(first);
        Assert.Equal(before, loads);
        await cache.LoadAsync(second);
        Assert.Equal(before + 1, loads);
        Assert.Equal(2, cache.RetainedCount);
        Assert.Equal(0, cache.PendingCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentLoadsShareOneTaskAndFaultsAreNotRetained(bool fail)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("source", "same metadata");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        var cache = new ArchiveCatalogCache<string>(_ =>
        {
            Interlocked.Increment(ref loads); started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            if (fail) throw new IOException("transient read failure");
            return "loaded";
        });
        var first = cache.LoadAsync(path);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var peers = Enumerable.Range(0, 20).Select(_ => cache.LoadAsync(path)).ToArray();
            Assert.All(peers, peer => Assert.Same(first, peer));
            Assert.Equal(1, cache.PendingCount);
            release.Set();
            if (fail) await Assert.ThrowsAsync<IOException>(() => Task.WhenAll(peers));
            else Assert.All(await Task.WhenAll(peers), value => Assert.Equal("loaded", value));
            Assert.Equal(1, loads);
            Assert.Equal(0, cache.PendingCount);
            Assert.Equal(fail ? 0 : 1, cache.RetainedCount);
            var expectedLoads = fail ? 2 : 1;
            fail = false;
            Assert.Equal("loaded", await cache.LoadAsync(path));
            Assert.Equal(expectedLoads, loads);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task OldInFlightRevisionCannotReplaceTheNewSnapshot()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("source", "old");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ArchiveCatalogCache<string>(source =>
        {
            var value = File.ReadAllText(source);
            if (value == "old")
            {
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return value;
        });
        var old = cache.LoadAsync(path);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(path, "new revision");
            Assert.Equal("new revision", await cache.LoadAsync(path));
            release.Set();
            await Assert.ThrowsAsync<IOException>(() => old);
            Assert.Equal("new revision", await cache.LoadAsync(path));
            Assert.Equal(1, cache.RetainedCount);
            Assert.Equal(0, cache.PendingCount);
        }
        finally { release.Set(); }
    }
}
