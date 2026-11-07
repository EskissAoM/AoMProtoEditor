using System.Reflection;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests.UI;

public sealed class AssetPathCatalogRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveRefreshUsesEachFieldsOwnCatalogIncludingDeferredSources(bool deferred)
        => AvaloniaTestHost.Run(() =>
        {
            var icons = new SelectorCatalog(["old.png"]);
            var icon = new AssetPathEditor(); var animation = new AssetPathEditor();
            var changes = 0;
            icon.Configure("old.png", deferred ? icons.Append("legacy.png") : icons,
                _ => { changes++; return Task.CompletedTask; });
            animation.Configure("idle.xml", ["idle.xml"], _ => { changes++; return Task.CompletedTask; });
            var panel = new StackPanel(); panel.Children.Add(icon); panel.Children.Add(animation);
            icons.ReplaceWith(["imported.png"]);
            SelectorCatalogRefresh.Apply(panel); Dispatcher.UIThread.RunJobs();
            Assert.Contains(icon.Suggestions, item => item.FullValue == "imported.png");
            Assert.DoesNotContain(animation.Suggestions, item => item.FullValue == "imported.png");
            Assert.Contains(animation.Suggestions, item => item.FullValue == "idle.xml");
            Assert.Equal("old.png", icon.FullValue); Assert.Equal("idle.xml", animation.FullValue);
            Assert.Equal(0, changes);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshPreservesDraftModeAndOriginalCallback(bool editing)
        => AvaloniaTestHost.Run(() =>
        {
            var changes = new List<string>(); var notifications = 0;
            var editor = new AssetPathEditor();
            editor.Configure("resources/current.png", ["resources/current.png", "resources/removed.png"], value =>
            { changes.Add(value); return Task.CompletedTask; });
            editor.FullValueChanged += (_, _) => notifications++;
            editor.Editor.SelectedItem = editor.Suggestions.Single(item => item.FullValue == "resources/current.png");
            if (editing)
            {
                typeof(AssetPathEditor).GetMethod("BeginEdit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                editor.Editor.Text = "resources/unfinished.png";
            }
            var text = editor.Editor.Text; var value = editor.FullValue;
            var count = changes.Count; var notificationCount = notifications;
            editor.UpdateSuggestions(["resources/current.png", "resources/imported.png"]);
            editor.UpdateSuggestions(["resources/current.png", "resources/imported.png"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(value, editor.FullValue); Assert.Equal(text, editor.Editor.Text);
            Assert.Equal(editing, editor.Editor.IsVisible); Assert.Equal(!editing, editor.CompactPresenter.IsVisible);
            Assert.Equal(count, changes.Count); Assert.Equal(notificationCount, notifications);
            Assert.DoesNotContain(editor.Suggestions, item => item.FullValue == "resources/removed.png");
            var imported = Assert.Single(editor.Suggestions, item => item.FullValue == "resources/imported.png");
            editor.Editor.SelectedItem = imported;
            Assert.Equal("resources/imported.png", editor.FullValue);
            Assert.Equal(count + 1, changes.Count); Assert.Equal(notificationCount + 1, notifications);
            Assert.Equal("resources/imported.png", changes[^1]);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshedMembershipControlsCommitWithoutBlessingRetainedDisplayValue(bool included)
        => AvaloniaTestHost.Run(() =>
        {
            var changes = new List<string>();
            var editor = new AssetPathEditor { RequireSuggestionMatch = true };
            editor.Configure("resources/current.png", ["resources/current.png"], value =>
            { changes.Add(value); return Task.CompletedTask; });
            editor.UpdateSuggestions(included ? ["resources/current.png", "resources/imported.png"] : ["resources/imported.png"]);
            Assert.Empty(changes); Assert.Equal("resources/current.png", editor.FullValue);
            Assert.Contains(editor.Suggestions, item => item.FullValue == "resources/current.png");
            editor.CommitEditing();
            Assert.Equal(included ? "resources/current.png" : "", editor.FullValue);
            Assert.Equal(included ? Array.Empty<string>() : new[] { "" }, changes);
            editor.Editor.SelectedItem = editor.Suggestions.Single(item => item.FullValue == "resources/imported.png");
            editor.CommitEditing();
            Assert.Equal("resources/imported.png", editor.FullValue);
        });

    [Fact]
    public void ConfigureAfterRefreshStartsANewRecordWithItsOwnCallback()
        => AvaloniaTestHost.Run(() =>
        {
            var oldChanges = 0; var newChanges = new List<string>();
            var editor = new AssetPathEditor();
            editor.Configure("old.png", ["old.png"], _ => { oldChanges++; return Task.CompletedTask; });
            editor.Editor.SelectedItem = editor.Suggestions.Single();
            editor.UpdateSuggestions(["old.png", "imported.png"]);
            editor.Configure("next.png", ["next.png", "imported.png"], value =>
            { newChanges.Add(value); return Task.CompletedTask; });
            Assert.Equal("next.png", editor.FullValue); Assert.Equal("next.png", editor.Editor.Text);
            Assert.False(editor.Editor.IsVisible); Assert.True(editor.CompactPresenter.IsVisible);
            Assert.Equal(0, oldChanges); Assert.Empty(newChanges);
            editor.Editor.SelectedItem = editor.Suggestions.Single(item => item.FullValue == "imported.png");
            Assert.Equal(0, oldChanges); Assert.Equal(new[] { "imported.png" }, newChanges);
        });
}
