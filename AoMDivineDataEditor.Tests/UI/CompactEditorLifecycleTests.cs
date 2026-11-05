using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CompactEditorLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(Window window, string name) => (T)window.GetType().GetField(name, Private)!.GetValue(window)!;
    private static Task Save(Window window) => (Task)window.GetType().GetMethod("SaveAsync", Private)!.Invoke(window, null)!;
    private static CompactEditorLifecycle Life(Window window) => Field<CompactEditorLifecycle>(window, "_lifecycle");
    private static void Cleanup(Window window)
    {
        foreach (var prompt in window.OwnedWindows.ToArray()) prompt.Close();
        Life(window).CloseAfterSave(window.Close);
    }
    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout(); await Task.Delay(20);
    }
    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 300 && !ready(); i++) await Task.Delay(10);
        Assert.True(ready(), "Compact editor did not reach the expected lifecycle state.");
    }
    private static void Key(Window window, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        if (window.IsVisible) window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }
    private static Control Edit(Window window, bool minor, string? value = null)
    {
        if (minor) { var field = Field<AutoCompleteBox>(window, "_techBox"); field.Focus(); field.Text = value ?? "ClassicalAgeB"; return field; }
        else { var field = Field<TextBox>(window, "_topicBox"); field.Focus(); field.Text = value ?? "Changed topic"; return field; }
    }
    private static void Close(Window window, string route)
    {
        if (route == "cancel") window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel") || Equals(button.Content, "Close"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else if (route == "escape") Key(window, Avalonia.Input.Key.Escape);
        else window.Close();
    }
    private static Button Keep(Prompt prompt) => prompt.FindControl<Button>("_cancelButton")!;
    private static void Discard(Prompt prompt) => prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void WithEditor(bool minor, Func<Window, Func<int>, XElement, Task> check, bool original = false, Func<Task<bool>>? commit = null)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var source = XElement.Parse(minor ? "<ClassicalAgeA portraitfilename='first.png' portraiticonfilename='first.png'><future keep='yes'/></ClassicalAgeA>"
                : "<culture><name>A</name><displaynameid>STR_A</displaynameid><compendiumtopic>Topic</compendiumtopic><future keep='yes'/></culture>");
            var before = source.ToString(); var calls = 0;
            Task<bool> Commit() { calls++; return commit?.Invoke() ?? Task.FromResult(true); }
            Window window = minor
                ? new MinorGodEditorWindow(new("A", "ClassicalAgeA", original, source), ["ClassicalAgeA", "ClassicalAgeB"], ["first.png", "second.png"],
                    new IconPreviewService(() => null, () => null), (_, candidate) => { Assert.Equal("yes", (string?)candidate.Element("future")!.Attribute("keep")); return Commit(); })
                : new CultureEditorWindow(new("A", original, source), ["first", "second"], _ => Task.FromResult<string?>("Name"),
                    (_, candidate, _) => { Assert.Equal("yes", (string?)candidate.Element("future")!.Attribute("keep")); return Commit(); });
            try { window.Show(); await Settle(window); await check(window, () => calls, source); Assert.Equal(before, source.ToString()); }
            finally { Cleanup(window); }
        });

    [Theory]
    [InlineData(false, "cancel")] [InlineData(false, "x")] [InlineData(false, "escape")]
    [InlineData(true, "cancel")] [InlineData(true, "x")] [InlineData(true, "escape")]
    public void DirtyCompactDialogCanKeepEditingThenDiscardThroughEveryExit(bool minor, string route)
        => WithEditor(minor, async (window, calls, _) =>
        {
            var field = Edit(window, minor); await Settle(window);
            // Escape first dismisses an open selector rather than the window.
            if (field is AutoCompleteBox auto) auto.IsDropDownOpen = false;
            Close(window, route); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            var prompt = Assert.Single(window.OwnedWindows.OfType<Prompt>());
            Assert.Equal("Keep editing", Keep(prompt).Content); Assert.True(Keep(prompt).IsDefault);
            Assert.False(prompt.FindControl<Button>("_confirmButton")!.IsDefault);
            window.Close(); Assert.Single(window.OwnedWindows.OfType<Prompt>());
            Keep(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Life(window).IsClosePending); await Settle(window);
            Assert.True(window.IsVisible); Assert.True(field.IsKeyboardFocusWithin); Assert.Equal(0, calls());
            Assert.Equal(minor ? "ClassicalAgeB" : "Changed topic", field is TextBox text ? text.Text : ((AutoCompleteBox)field).Text);
            Close(window, route); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            Discard(Assert.Single(window.OwnedWindows.OfType<Prompt>())); await Until(() => !window.IsVisible);
            Assert.Equal(0, calls());
        });

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void UntouchedAndReadonlyCompactDialogsCloseWithoutPrompts(bool minor, bool original)
        => WithEditor(minor, async (window, calls, _) =>
        {
            Key(window, Avalonia.Input.Key.Enter); await Settle(window); Assert.Equal(0, calls()); Assert.True(window.IsVisible);
            Key(window, Avalonia.Input.Key.Escape); await Until(() => !window.IsVisible);
            Assert.Empty(window.OwnedWindows); Assert.Equal(0, calls());
        }, original);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RevertingACompactFieldReturnsToCleanState(bool minor)
        => WithEditor(minor, async (window, calls, _) =>
        {
            Edit(window, minor); Edit(window, minor, minor ? "ClassicalAgeA" : "Topic");
            await Settle(window); window.Close(); await Until(() => !window.IsVisible);
            Assert.Empty(window.OwnedWindows); Assert.Equal(0, calls());
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EscapeDismissesTheCurrentDropdownBeforeClosingTheCleanDialog(bool minor)
        => WithEditor(minor, async (window, _, _) =>
        {
            var picker = Field<AutoCompleteBox>(window, minor ? "_techBox" : "_replayPanelBackgroundBox");
            picker.Focus(); picker.IsDropDownOpen = true; await Settle(window); Assert.True(picker.IsDropDownOpen);
            Key(window, Avalonia.Input.Key.Escape); await Settle(window);
            Assert.True(window.IsVisible); Assert.False(picker.IsDropDownOpen); Assert.Empty(window.OwnedWindows);
            Key(window, Avalonia.Input.Key.Escape); await Until(() => !window.IsVisible);
        });

    [Theory]
    [InlineData(false, true)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(true, false)]
    public void CtrlSUsesOneExistingSaveAndProtectsTheCommitUntilItFinishes(bool minor, bool success)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        WithEditor(minor, async (window, calls, _) =>
        {
            var field = Edit(window, minor); await Settle(window);
            Key(window, Avalonia.Input.Key.S, RawInputModifiers.Control); await Until(() => calls() == 1);
            Assert.False(field.IsEffectivelyEnabled);
            Key(window, Avalonia.Input.Key.S, RawInputModifiers.Control); window.Close();
            Assert.True(window.IsVisible); Assert.Empty(window.OwnedWindows); Assert.Equal(1, calls());
            completion.SetResult(success);
            await Until(() => success ? !window.IsVisible : field.IsEffectivelyEnabled);
            Assert.Equal(1, calls()); Assert.Empty(window.OwnedWindows);
            if (!success)
            {
                window.Close(); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
                Discard(Assert.Single(window.OwnedWindows.OfType<Prompt>())); await Until(() => !window.IsVisible);
            }
        }, commit: () => completion.Task);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidCompactDraftCanBeDiscardedAfterItsSaveError(bool minor)
        => WithEditor(minor, async (window, calls, _) =>
        {
            if (minor) Edit(window, true, "MissingTech");
            else Field<TextBox>(window, "_displayNameBox").Text = "";
            var saving = Save(window); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            var error = Assert.Single(window.OwnedWindows.OfType<Prompt>()); Assert.True(error.PromptIsError); error.Close();
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close(); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            var discard = Assert.Single(window.OwnedWindows.OfType<Prompt>()); Assert.Equal("Unsaved changes", discard.PromptTitle);
            Discard(discard); await Until(() => !window.IsVisible); Assert.Equal(0, calls());
            await Save(window); Assert.Equal(0, calls()); Assert.Empty(window.OwnedWindows);
        });

    [Theory]
    [InlineData("_portraitEditor")] [InlineData("_iconEditor")]
    public void AssetChangesParticipateInDirtyStateAndCanBeReverted(string name)
        => WithEditor(true, async (window, _, _) =>
        {
            var asset = Field<AssetPathEditor>(window, name);
            asset.Editor.SelectedItem = asset.Suggestions.Single(item => item.FullValue == "second.png"); await Settle(window);
            window.Close(); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            Keep(Assert.Single(window.OwnedWindows.OfType<Prompt>())).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Life(window).IsClosePending);
            Assert.Equal("second.png", asset.FullValue);
            asset.Editor.SelectedItem = asset.Suggestions.Single(item => item.FullValue == "first.png"); await Settle(window);
            Assert.Equal("first.png", asset.FullValue);
            Assert.False((bool)window.GetType().GetMethod("HasUnsavedChanges", Private)!.Invoke(window, null)!);
            window.Close(); await Until(() => !window.IsVisible); Assert.Empty(window.OwnedWindows);
        });

    [Theory]
    [InlineData("_displayNameBox")] [InlineData("_topicBox")] [InlineData("_imageBox")] [InlineData("_replayPanelBackgroundBox")]
    public void EachCultureFieldCanBeKeptAndRevertedWithoutLeavingDirtyState(string name)
        => WithEditor(false, async (window, calls, _) =>
        {
            var field = Field<Control>(window, name);
            string? Read() => field is TextBox text ? text.Text : ((AutoCompleteBox)field).Text;
            void Write(string? value) { if (field is TextBox text) text.Text = value; else ((AutoCompleteBox)field).Text = value; }
            var initial = Read(); Write("Changed"); await Settle(window);
            window.Close(); await Until(() => window.OwnedWindows.OfType<Prompt>().Any());
            Keep(Assert.Single(window.OwnedWindows.OfType<Prompt>())).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Life(window).IsClosePending);
            Assert.Equal("Changed", Read()); Assert.Equal(0, calls());
            Write(initial); await Settle(window); window.Close(); await Until(() => !window.IsVisible);
            Assert.Empty(window.OwnedWindows); Assert.Equal(0, calls());
        });

    [Fact]
    public void PendingCultureLookupDoesNotBlockCleanCloseOrResumeACancelledSave()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var load = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            var source = XElement.Parse("<culture><name>A</name><displaynameid>STR_A</displaynameid></culture>");
            var clean = new CultureEditorWindow(new("A", false, source), [], _ => load.Task, (_, _, _) => { calls++; return Task.FromResult(true); });
            var dirty = new CultureEditorWindow(new("A", false, source), [], _ => load.Task, (_, _, _) => { calls++; return Task.FromResult(true); });
            try
            {
                clean.Show(); clean.Close(); Assert.False(clean.IsVisible); Assert.False(load.Task.IsCompleted);
                dirty.Show(); Field<TextBox>(dirty, "_topicBox").Text = "Changed";
                var saving = Save(dirty); Assert.False(saving.IsCompleted); dirty.Close();
                var prompt = Assert.Single(dirty.OwnedWindows.OfType<Prompt>()); Assert.False(load.Task.IsCompleted);
                Keep(prompt).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => !Life(dirty).IsClosePending);
                load.SetResult("Name"); await saving.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, calls); Assert.True(dirty.IsVisible); Assert.Equal("Changed", Field<TextBox>(dirty, "_topicBox").Text);
                Assert.Null(Field<TextBox>(clean, "_displayNameBox").Text);
                await Save(dirty); Assert.Equal(1, calls); Assert.False(dirty.IsVisible);
            }
            finally { load.TrySetResult("Name"); Cleanup(clean); Cleanup(dirty); }
        });
}
