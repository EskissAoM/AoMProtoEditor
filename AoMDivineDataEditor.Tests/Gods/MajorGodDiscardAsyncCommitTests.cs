using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodDiscardAsyncCommitTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MajorGodEditorView view, string name) => (T)typeof(MajorGodEditorView).GetField(name, Private)!.GetValue(view)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DiscardInvalidatesRenameWhoseStringResolutionHasNotCompleted(bool switchGod, bool lookupFails)
    {
        using var workspace = new TestWorkspace("god-discard-inflight");
        var id = MajorGodCatalog.BuildStringId("Old", "displaynameid");
        var path = workspace.WriteText("major_gods_mods.xml", $"<civsmods><civ><name>Old</name><statsid>SP</statsid><displaynameid>{id}</displaynameid></civ><civ><name>Other</name><statsid>SP</statsid></civ></civsmods>");
        var stringsPath = workspace.WriteText("stringmods.txt", $"ID = \"{id}\" ; Str = \"Saved display\"\r\n");
        var before = new[] { path, stringsPath }.ToDictionary(file => file, File.ReadAllBytes);
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = false;
        var view = AvaloniaTestHost.Run(() => new MajorGodEditorView([], path,
            resolveStringAsync: _ => delay ? pending.Task : Task.FromResult<string?>("Saved display"), modStringsPath: stringsPath));
        AvaloniaTestHost.RunAsync(async () =>
        {
            view.SetModifiedMode(true); view.SelectMajorGod("Old");
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(Field<bool>(view, "_isPopulating"));
            var saved = view.LoadSavedMajorGodElement("Old");
            Field<TextBox>(view, "_nameBox").Text = "Renamed";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            delay = true;
            var committing = view.CommitCurrentMajorGodAsync();
            Assert.False(committing.IsCompleted, "The test must suspend the rename at string resolution.");
            delay = false;
            view.DiscardMajorGodChanges("Old", saved);
            if (switchGod) view.SelectMajorGod("Other");
            if (lookupFails) pending.SetException(new IOException("Delayed lookup failed after discard."));
            else pending.SetResult("Saved display");
            Assert.False(await committing.WaitAsync(TimeSpan.FromSeconds(3)));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(view.IsDirty);
            Assert.Empty(Field<HashSet<string>>(view, "_dirtyNames"));
            Assert.Empty(Field<Dictionary<string, string>>(view, "_pendingStringUpdates"));
            Assert.Empty(Field<HashSet<string>>(view, "_pendingStringRemovals"));
            Assert.Equal(switchGod ? "Other" : "Old", view.CurrentMajorGodName);
            Assert.Equal(new[] { "Old", "Other" }, view.GetMajorGodNames(true).Order());
            Assert.True(await view.CommitCurrentMajorGodAsync());
            Assert.False(view.IsDirty);
            foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            // The stale rename must not disable subsequent real edits.
            Field<TextBox>(view, "_keyBox").Text = "B";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(view.IsDirty);
        });
    }
}
