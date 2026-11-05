using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlPreviewPaneTests
{
    [Theory]
    [InlineData("Technology")]
    [InlineData("Power")]
    [InlineData("MajorGod")]
    public void EntityPanesUseSharedMinimumAndRestoreDefaultAfterDragging(string kind)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            Control view = kind switch { "Technology" => new TechnologyEditorView(), "Power" => new PowerEditorView(), _ => new MajorGodEditorView() };
            var window = new Window { Content = view, Width = 1440, Height = 900 };
            try
            {
                window.Show(); await Settle(window);
                await Verify(window, view.FindControl<Grid>("_mainGrid")!, kind == "Technology" ? 2 : 0,
                    view.FindControl<GridSplitter>("_previewSplitter")!, view.FindControl<Button>("_xmlPreviewToggleButton")!);
            }
            finally { window.Close(); }
        });

    internal static async Task Verify(Window window, Grid grid, int formColumn, GridSplitter splitter, Button toggle)
    {
        var preview = grid.ColumnDefinitions[formColumn + 2];
        Assert.Equal(28, preview.MinWidth);
        var formWidth = grid.ColumnDefinitions[formColumn].Width;
        var previewWidth = preview.Width;
        var before = preview.ActualWidth;
        var from = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 80), window)!.Value;
        var to = from + new Vector(Math.Max(20, before - 100), 0);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left); await Settle(window);
        Assert.InRange(preview.ActualWidth, 28, 249);
        Assert.True(preview.ActualWidth < before);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(window);
        Assert.Equal(28, preview.ActualWidth); Assert.False(splitter.IsVisible);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(window);
        Assert.True(splitter.IsVisible);
        Assert.Equal(formWidth, grid.ColumnDefinitions[formColumn].Width);
        Assert.Equal(previewWidth, preview.Width);
        Assert.InRange(Math.Abs(preview.ActualWidth - before), 0, .5);
        window.Width = 1000; await Settle(window);
        Assert.True(preview.ActualWidth >= 28);
    }

    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(30);
    }
}

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void UnitPaneRestoresDefaultWithoutChangingItsXmlDraft()
        => WithHost(async (_, host) =>
        {
            PrepareSnippetActions(host); await RoleSettle(host);
            var preview = Field<AvaloniaEdit.TextEditor>(host, "_xmlPreviewText");
            var text = preview.Text;
            await XmlPreviewPaneTests.Verify(host, Field<Grid>(host, "_mainContentGrid"), 2,
                Field<GridSplitter>(host, "_previewSplitter"), Field<Button>(host, "_xmlPreviewToggleButton"));
            Assert.Equal(text, preview.Text); Assert.False(Field<bool>(host, "_isDirty"));
            await Unlock(host);
            preview.Text = "<unit name='A'><unfinished>";
            var toggle = Field<Button>(host, "_xmlPreviewToggleButton");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Equal("<unit name='A'><unfinished>", preview.Text);
            Assert.False(preview.IsReadOnly); Assert.True(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<protoaction><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic></protoaction>"), RoleFixture);
}
