using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconPreviewCacheUiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LatestSelectionAndDetachDoNotCancelAnotherControlsSharedLoad(bool detach)
    {
        using var workspace = new TestWorkspace();
        var a = IconPreviewCacheTests.WriteIcon(workspace, "a.png");
        var b = IconPreviewCacheTests.WriteIcon(workspace, "b.png");
        var started = IconPreviewCacheTests.Signal(); var release = IconPreviewCacheTests.Signal(); var loads = 0;
        var service = new IconPreviewService(() => null, () => workspace.RootPath, AssetPayloadPolicy.Default, beforeLoad: source =>
        {
            if (!source.Equals(a, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
            Interlocked.Increment(ref loads); started.TrySetResult(); return release.Task;
        });
        AvaloniaTestHost.RunAsync(async () =>
        {
            var first = new IconPreviewControl(service); var other = new IconPreviewControl(service);
            var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(other);
            var owner = new Window { Content = panel }; owner.Show();
            Task? stale = null, shared = null;
            try
            {
                stale = first.ShowAsync("a.png"); shared = other.ShowAsync("resources/a.png");
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, service.GetCacheState().Pending);
                await first.ShowAsync("b.png").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Contains(b, Assert.IsType<string>(ToolTip.GetTip(first)));
                Assert.NotNull(Bitmap(first));
                if (detach) { panel.Children.Remove(first); Assert.Null(Bitmap(first)); }
                release.TrySetResult();
                await Task.WhenAll(stale, shared).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, loads);
                Assert.Contains(a, Assert.IsType<string>(ToolTip.GetTip(other)));
                Assert.NotNull(Bitmap(other));
                if (detach)
                {
                    Assert.Null(Bitmap(first)); // The late old result cannot resurrect a detached bitmap.
                    panel.Children.Add(first);
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                }
                Assert.Contains(b, Assert.IsType<string>(ToolTip.GetTip(first)));
                Assert.NotNull(Bitmap(first));
                Assert.Equal(0, service.GetCacheState().Pending); Assert.Equal(0, service.GetCacheState().Active);
            }
            finally
            {
                release.TrySetResult();
                if (stale != null) await stale.WaitAsync(TimeSpan.FromSeconds(5));
                if (shared != null) await shared.WaitAsync(TimeSpan.FromSeconds(5));
                owner.Close();
            }
        });
    }

    [Fact]
    public void ActualModRefreshReleasesOldPayloadsEvenWithoutANewPreviewRequest()
    {
        using var fixture = new BaselineSafetyFixture();
        IconPreviewCacheTests.WriteIcon(fixture.Workspace, "TestMod/game/ui_myth/resources/icon.png");
        var other = fixture.Workspace.WriteXml("Other/game/data/gameplay/proto_mods.xml", "<protomods/>");
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("LoadCurrentModAssetCatalogs");
                var service = (IconPreviewService)typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow)
                    .GetField("_iconPreviewService", Private)!.GetValue(host)!;
                var held = await service.LoadAsync("icon.png"); Assert.NotNull(held);
                Assert.Equal(1, service.GetCacheState().Entries);
                host.Set("_modFilePath", other);
                host.Call("LoadCurrentModAssetCatalogs");
                Assert.Equal(new IconPreviewCacheState(0, 0, 0, 0), service.GetCacheState());
                Assert.Equal(AssetCreateServiceTests.Png(), held.PngBytes);
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                Assert.False((bool)typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow).GetField("_isDirty", Private)!.GetValue(host)!);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    private static object? Bitmap(IconPreviewControl preview)
        => typeof(IconPreviewControl).GetField("_bitmap", Private)!.GetValue(preview);
}
