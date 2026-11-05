using System.Reflection;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CultureManagerOpeningTests
{
    [Fact]
    public void RepeatedOpeningClosesGodsMenuBeforeLoadingAndKeepsManagerModal()
    {
        using var fixture = new BaselineSafetyFixture();
        var before = Directory.GetFiles(fixture.Workspace.ModPath, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            try
            {
                host.Call("InitializeEditorTabs");
                var navigation = host.FindControl<Button>("_godsNavigationButton")!;
                var menu = Assert.IsType<Flyout>(navigation.Flyout);
                for (var index = 0; index < 2; index++)
                {
                    menu.ShowAt(navigation);
                    Assert.True(menu.IsOpen);
                    var opening = (Task)host.Call("OpenCultureManagerAsync")!;
                    Assert.False(menu.IsOpen);
                    Assert.Empty(host.OwnedWindows);
                    Assert.False(opening.IsCompleted);

                    // Render-priority work must run before the background-priority
                    // continuation starts loading/constructing the manager.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Assert.False(menu.IsOpen);
                        Assert.Empty(host.OwnedWindows);
                    }, DispatcherPriority.Render);
                    CultureManagerWindow? manager = null;
                    for (var attempt = 0; attempt < 200 && manager == null; attempt++)
                    {
                        manager = host.OwnedWindows.OfType<CultureManagerWindow>().SingleOrDefault();
                        if (manager == null) await Task.Delay(10);
                    }
                    Assert.NotNull(manager);
                    Assert.Same(host, manager.Owner);
                    Assert.True(manager.IsVisible);
                    // Avalonia disables modal owners through PlatformImpl, not the
                    // public control IsEnabled property. Check its dialog ownership.
                    var children = (IReadOnlyList<(Window child, bool isDialog)>)typeof(Window)
                        .GetField("_children", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var dialog = Assert.Single(children);
                    Assert.Same(manager, dialog.child);
                    Assert.True(dialog.isDialog);
                    Assert.False(opening.IsCompleted);
                    if (index == 0)
                    {
                        // Retain the rename-cancellation check from the removed
                        // diagnostic-only test, without any tracing machinery.
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var items = (System.Collections.IList)typeof(CultureManagerWindow)
                            .GetField("_items", flags)!.GetValue(manager)!;
                        var rename = (Task)typeof(CultureManagerWindow).GetMethod("RenameAsync", flags)!
                            .Invoke(manager, [items[0]])!;
                        var prompt = Assert.Single(manager.OwnedWindows.OfType<InputPromptWindow>());
                        Assert.Equal("Greek", prompt.FindControl<TextBox>("_inputBox")!.Text);
                        Assert.False(rename.IsCompleted);
                        prompt.Close();
                        await rename.WaitAsync(TimeSpan.FromSeconds(3));
                        Assert.True(manager.IsVisible);
                        Assert.False(opening.IsCompleted);
                    }
                    manager.Close();
                    await opening.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Empty(host.OwnedWindows);
                    Assert.Empty(children);
                }
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
        var after = Directory.GetFiles(fixture.Workspace.ModPath, "*", SearchOption.AllDirectories);
        Assert.Equal(before.Keys.Order(), after.Order());
        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
    }
}
