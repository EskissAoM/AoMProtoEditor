using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class ManagerButtonStyleTests
{
    private static Window UsabilityManager(string kind, bool original) => kind switch
    {
        "unit" => new UnitTypeManagerWindow([new("Entry", original, 0)], _ => Task.FromResult(true),
            (_, _) => Task.FromResult(true), _ => Task.FromResult(true), _ => 0),
        "tech" => new TechTypeManagerWindow([new("Entry", original, new TechTypeUsage(0, 0))], _ => Task.FromResult(true),
            (_, _) => Task.FromResult(true), _ => Task.FromResult(true), _ => new TechTypeUsage(0, 0)),
        _ => Manager(kind, original)
    };

    private static async Task SettleUsability(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout(); await Task.Delay(20);
    }

    private static object Item(Window manager) => ((IList)manager.GetType().GetField("_items", Private)!.GetValue(manager)!)[0]!;
    private static Grid ActualRow(Window manager) => (Grid)manager.GetType().GetMethod("CreateRow", Private)!.Invoke(manager, [Item(manager)])!;

    public static IEnumerable<object[]> UsabilityRows => new[] { "ability", "command", "tactics", "culture", "minor", "unit", "tech", "anim", "sound" }
        .SelectMany(kind => new[] { new object[] { kind, 520d }, new object[] { kind, 700d } });

    [Theory]
    [MemberData(nameof(UsabilityRows))]
    public void ManagerActionSlotsStayAlignedAcrossOriginalCustomAndLongStatus(string kind, double width)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            double[]? positions = null;
            foreach (var original in new[] { true, false })
            {
                var manager = UsabilityManager(kind, original);
                try
                {
                    var row = ActualRow(manager);
                    var name = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0);
                    var status = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 1);
                    name.Text = "EntryWithAVeryLongNameThatMustKeepItsBeginningAndExposeTheFullNameOnHover";
                    status.Text = original ? "Data.bar" : "Used by 123456789012345678901234567890";
                    manager.Content = row; manager.Width = width; manager.Show(); await SettleUsability(manager);
                    FormLayoutAssertions.NoOverlaps(row, $"manager/{kind}/{original}/{width}");
                    var current = row.ColumnDefinitions.Skip(2).Select(column => column.ActualWidth).ToArray();
                    Assert.All(current, slot => Assert.InRange(slot, 35.9, 36.1));
                    var starts = Enumerable.Range(2, current.Length).Select(index => row.ColumnDefinitions.Take(index).Sum(column => column.ActualWidth)).ToArray();
                    if (positions != null)
                        foreach (var pair in positions.Zip(starts)) Assert.InRange(Math.Abs(pair.First - pair.Second), 0, .5);
                    positions = starts;
                    foreach (var button in row.Children.OfType<Button>())
                    {
                        Assert.Equal(28, button.Bounds.Width); Assert.Equal(28, button.Bounds.Height);
                        Assert.InRange(Math.Abs(button.Bounds.Center.Y - row.Bounds.Height / 2), 0, .5);
                    }
                    Assert.Equal(name.Text, ToolTip.GetTip(name));
                    Assert.Equal(status.Text, ToolTip.GetTip(status));
                    if (original) Assert.DoesNotContain(row.Children.OfType<Button>(), button => button.Classes.Contains("remove-button"));
                }
                finally { manager.Close(); }
            }
        });

    [Theory]
    [InlineData("ability")] [InlineData("command")] [InlineData("tactics")]
    [InlineData("culture")] [InlineData("minor")] [InlineData("unit")]
    [InlineData("tech")] [InlineData("anim")] [InlineData("sound")]
    public void ManagerSearchAndSourceFiltersShowDistinctEmptyStatesWithoutFakeRows(string kind)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var manager = UsabilityManager(kind, false);
            try
            {
                var shell = Assert.IsType<ManagerListShell>(manager.Content);
                var list = shell.GetLogicalDescendants().OfType<ListBox>().Single();
                manager.Width = 520; manager.Height = 420;
                manager.Show(); await SettleUsability(manager);
                Assert.Equal(1, list.ItemCount); Assert.False(shell.EmptyStateMessage.IsVisible);
                shell.SearchBox.Text = "no-such-entry";
                for (var i = 0; i < 100 && list.ItemCount != 0; i++) await Task.Delay(10);
                Assert.Equal(0, list.ItemCount);
                Assert.True(shell.EmptyStateMessage.IsVisible); Assert.Equal("No matching entries.", shell.EmptyStateMessage.Text);
                shell.SearchBox.Text = ""; shell.FilterComboBox.SelectedItem = "Original";
                await SettleUsability(manager);
                Assert.Equal(0, list.ItemCount); Assert.Equal("No entries in this filter.", shell.EmptyStateMessage.Text);
                shell.FilterComboBox.SelectedItem = "All"; await SettleUsability(manager);
                Assert.Equal(1, list.ItemCount); Assert.False(shell.EmptyStateMessage.IsVisible);
                Assert.Contains("Double-click", shell.FooterTextBlock.Text);
                Assert.Equal(Avalonia.Media.TextWrapping.Wrap, shell.FooterTextBlock.TextWrapping);
                FormLayoutAssertions.NoOverlaps(shell, $"manager shell/{kind}/minimum size");
            }
            finally { manager.Close(); }
        });
}

public sealed class ManagerShellStateTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void EmptyCatalogAndOverlappingActivityPreserveSaveWarningsAndExistingCapabilities()
        => AvaloniaTestHost.Run(() =>
        {
            var shell = new ManagerListShell("Search", ["All", "Original", "Custom"], "Double-click to rename.", addEnabled: false);
            var list = ManagerListShell.CreateVirtualizedList<string>(name => new TextBlock { Text = name });
            shell.ReplaceItemsHost(list); list.ItemsSource = Array.Empty<string>();
            var window = new Window { Content = shell, Width = 520, Height = 420 };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.Equal("No entries available.", shell.EmptyStateMessage.Text); Assert.True(shell.EmptyStateMessage.IsVisible);
                var first = shell.BeginActivity(); var second = shell.BeginActivity();
                Assert.False(shell.EmptyStateMessage.IsVisible); Assert.False(shell.AddButton.IsEnabled); Assert.True(list.IsEnabled);
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
                first.Dispose(); first.Dispose(); Assert.False(shell.EmptyStateMessage.IsVisible);
                EditorSaveFeedback.Publish(window, EditorSaveOutcome.Blocked);
                second.Dispose(); Assert.True(shell.EmptyStateMessage.IsVisible);
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Save blocked.");
                Assert.False(shell.AddButton.IsEnabled); Assert.Equal(0, list.ItemCount);
                list.ItemsSource = new[] { "Entry" }; Assert.False(shell.EmptyStateMessage.IsVisible);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(520)] [InlineData(700)]
    public void IconRowsShareAvailableWidthAndExposeIdentityWithoutAddingActions(int width)
    {
        using var workspace = new TestWorkspace();
        var resources = workspace.CreateDirectory("resources");
        workspace.WriteText("resources/deep/folder/averylongcustomiconfilename.png", "isolated source identity");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var manager = new IconManagerWindow([], [@"resources\deep\folder\averylongcustomiconfilename.png"], resources);
            try
            {
                var shell = Assert.IsType<ManagerListShell>(manager.Content);
                var list = shell.GetLogicalDescendants().OfType<ListBox>().Single();
                manager.Width = width; manager.Show();
                shell.SearchBox.Text = "no-such-icon";
                for (var i = 0; i < 100 && list.ItemCount != 0; i++) await Task.Delay(10);
                Assert.Equal(0, list.ItemCount); Assert.True(shell.EmptyStateMessage.IsVisible);
                Assert.Equal("No matching entries.", shell.EmptyStateMessage.Text);
                shell.SearchBox.Text = ""; shell.FilterComboBox.SelectedItem = "Original";
                Assert.Equal("No entries in this filter.", shell.EmptyStateMessage.Text);
                shell.FilterComboBox.SelectedItem = "All"; Assert.Equal(1, list.ItemCount);
                var item = ((IList)typeof(IconManagerWindow).GetField("_items", Private)!.GetValue(manager)!)[0]!;
                var row = (Grid)typeof(IconManagerWindow).GetMethod("CreateRow", Private)!.Invoke(manager, [item])!;
                manager.Content = row; manager.Width = width; manager.Show();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); manager.UpdateLayout();
                FormLayoutAssertions.NoOverlaps(row, "icon source identity");
                Assert.Empty(row.Children.OfType<Button>());
                var name = row.Children.OfType<TextBlock>().First(); var source = row.Children.OfType<TextBlock>().Last();
                Assert.True(name.Bounds.Width > 100); Assert.True(source.Bounds.Width > 100);
                Assert.Equal(name.Text, ToolTip.GetTip(name)); Assert.Contains("averylongcustomiconfilename.png", Assert.IsType<string>(ToolTip.GetTip(source)));
            }
            finally { manager.Close(); }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RealCultureAndMinorGodGatesKeepTheirExistingConcurrencyWhileShowingActivity(bool minor)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            Window manager = minor
                ? new MinorGodManagerWindow([], (_, _, _) => Task.FromResult<MinorGodEditorResult?>(null),
                    (_, _) => Task.FromResult<MinorGodManagerItem?>(null), (_, _, _) => Task.FromResult<MinorGodManagerItem?>(null),
                    (_, _) => Task.FromResult<MinorGodManagerItem?>(null), _ => Task.FromResult(true), _ => 0, (_, _) => Task.FromResult(true))
                : new CultureManagerWindow([], (_, _, _) => Task.CompletedTask, _ => Task.FromResult(true),
                    (_, _, _) => Task.FromResult(true), (_, _) => Task.FromResult(true), _ => Task.FromResult(true), _ => 0, (_, _) => Task.FromResult(true));
            try
            {
                manager.Show(); var shell = Assert.IsType<ManagerListShell>(manager.Content);
                var run = manager.GetType().GetMethod("RunExclusiveAsync", Private)!;
                Func<Task> operation = () => { calls++; return completion.Task; };
                var running = (Task)run.Invoke(manager, [operation])!;
                await (Task)run.Invoke(manager, [operation])!;
                Assert.Equal(1, calls);
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
                Assert.False(shell.EmptyStateMessage.IsVisible);
                completion.SetResult(); await running;
                Assert.True(shell.EmptyStateMessage.IsVisible);
                Assert.DoesNotContain(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
                var failed = (Task)run.Invoke(manager, [(Func<Task>)(() => Task.FromException(new IOException("isolated manager failure")))])!;
                for (var i = 0; i < 300 && !manager.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                var prompt = Assert.Single(manager.OwnedWindows.OfType<Prompt>());
                Assert.Contains("isolated manager failure", prompt.PromptText); prompt.Close(); await failed.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False((bool)manager.GetType().GetField("_operationInProgress", Private)!.GetValue(manager)!);
                await (Task)run.Invoke(manager, [(Func<Task>)(() => { calls++; return Task.CompletedTask; })])!;
                Assert.Equal(2, calls);
                Assert.DoesNotContain(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
            }
            finally { completion.TrySetResult(); manager.Close(); }
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AssetLoadFailureKeepsItsPopupAndRestoresTheExistingButtonState(bool sound)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var completion = new TaskCompletionSource<AssetPayloadResult<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = new AnimFileManagerWindow([new("entry.xml", "Data.bar", false)], _ => Task.FromResult<string?>(null), null,
                profile: sound ? XmlAssetManagerProfile.SoundSet : XmlAssetManagerProfile.AnimFile, loadXmlResultAsync: _ => completion.Task);
            try
            {
                manager.Show(); var shell = Assert.IsType<ManagerListShell>(manager.Content);
                var item = ((IList)typeof(AnimFileManagerWindow).GetField("_items", Private)!.GetValue(manager)!)[0]!;
                var row = (Grid)typeof(AnimFileManagerWindow).GetMethod("CreateRow", Private)!.Invoke(manager, [item])!;
                var button = row.Children.OfType<Button>().First();
                var opening = (Task)typeof(AnimFileManagerWindow).GetMethod("OpenViewerAsync", Private)!.Invoke(manager, [item, button])!;
                Assert.False(button.IsEnabled);
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
                completion.SetResult(AssetPayloadResult<string>.Failure("entry.xml", "isolated payload failure"));
                for (var i = 0; i < 300 && !manager.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                var prompt = Assert.Single(manager.OwnedWindows.OfType<Prompt>()); Assert.Contains("isolated payload failure", prompt.PromptText);
                prompt.Close(); await opening.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(button.IsEnabled);
                Assert.DoesNotContain(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Working…");
            }
            finally { completion.TrySetResult(AssetPayloadResult<string>.Failure("entry.xml", "cleanup")); foreach (var prompt in manager.OwnedWindows.ToArray()) prompt.Close(); manager.Close(); }
        });
}
