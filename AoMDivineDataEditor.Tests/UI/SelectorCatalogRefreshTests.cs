using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class SelectorCatalogRefreshTests
{
    [Fact]
    public void UnrefreshedAutocompleteRestorationHasSelectionAndLostFocusCommits()
        => AvaloniaTestHost.Run(() =>
        {
            var box = new AutoCompleteBox { Text = "Custom" };
            var commits = new List<string>();
            EditorAutoCompleteService.ConfigureStrict(box, ["Base", "Custom"], "Custom", valueCommitted: commits.Add);
            box.SelectedItem = "Custom";
            Dispatcher.UIThread.RunJobs(); commits.Clear();
            box.Text = "not in catalog";
            box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Dispatcher.UIThread.RunJobs();
            // Restoring Text also selects its matching item. These are two
            // existing event paths, even when no catalog refresh has happened.
            Assert.Equal(["Custom", "Custom"], commits);
            Assert.Equal("Custom", box.Text);
        });

    [Fact]
    public void SharedCatalogOwnsNormalizedValuesAndRetainsItsIdentity()
    {
        var input = new List<string> { "Base", " Custom ", "custom", "" };
        var catalog = new SelectorCatalog(input);
        input.Add("Outside");
        Assert.Equal(["Base", "Custom"], catalog);
        var retainedQuery = catalog.Where(value => value != "Base");
        Assert.True(catalog.ReplaceWith(["Base", "Renamed"]));
        Assert.Equal(["Renamed"], retainedQuery);
        Assert.False(catalog.ReplaceWith(["Renamed", "Base"]));
    }

    [Fact]
    public void StrictAutocompleteRefreshUpdatesCommitOptionsWithoutDuplicatingHandlers()
        => AvaloniaTestHost.Run(() =>
        {
            var catalog = new SelectorCatalog(["Base", "Removed"]);
            var box = new AutoCompleteBox { Text = "Base" };
            var panel = new StackPanel(); panel.Children.Add(box);
            var busy = false; var commits = new List<string>();
            EditorAutoCompleteService.ConfigureStrict(box, catalog, "Base", () => busy,
                valueCommitted: commits.Add);
            box.Text = "unfinished input";
            busy = true; catalog.ReplaceWith(["Base", "Custom"]); SelectorCatalogRefresh.Apply(panel); busy = false;
            Assert.Equal("unfinished input", box.Text); Assert.Empty(commits);
            Assert.Contains("Custom", box.ItemsSource!.Cast<string>());
            Assert.DoesNotContain("Removed", box.ItemsSource!.Cast<string>());
            box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Base", box.Text); // Refresh must not bless unfinished text as a legacy baseline.
            commits.Clear();
            box.Text = "custom"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(commits); Assert.All(commits, value => Assert.Equal("Custom", value));
            Assert.Equal("Custom", box.Text);
            var count = commits.Count;
            busy = true; SelectorCatalogRefresh.Apply(panel); SelectorCatalogRefresh.Apply(panel); busy = false;
            Assert.Equal(count, commits.Count);
            box.Text = "not in catalog"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Dispatcher.UIThread.RunJobs();
            // Match the exact two-event baseline tested above without refresh.
            Assert.Equal(count + 2, commits.Count); Assert.Equal("Custom", box.Text);
            Assert.All(commits.Skip(count), value => Assert.Equal("Custom", value));
        });

    [Fact]
    public void StrictComboRefreshPreservesInputAndAcceptsNewCatalogNames()
        => AvaloniaTestHost.Run(() =>
        {
            var catalog = new SelectorCatalog(["Base", "Removed"]);
            var box = new StrictSearchableComboBox(catalog, "Base");
            box.Text = "unfinished";
            catalog.ReplaceWith(["Base", "Custom"]); box.RefreshCatalogOptions();
            Assert.Equal("unfinished", box.Text);
            Assert.Contains("Custom", box.Options); Assert.DoesNotContain("Removed", box.Options);
            box.Text = "custom"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal("Custom", box.Value);
            box.Text = "unknown"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal("Custom", box.Value);
        });

    [Fact]
    public void FilteredChipSuggestionsRefreshWithoutClearingTextOrSelectedExclusions()
        => AvaloniaTestHost.Run(() =>
        {
            var catalog = new SelectorCatalog(["AlreadySelected"]);
            var box = new AutoCompleteBox { Text = "typed filter", IsVisible = false };
            var panel = new StackPanel(); panel.Children.Add(box);
            EditorAutoCompleteService.SetCatalogSuggestions(box, catalog.Where(value => value != "AlreadySelected"),
                isVisible: () => catalog.Count > 1);
            catalog.ReplaceWith(["AlreadySelected", "Custom"]); SelectorCatalogRefresh.Apply(panel);
            Assert.Equal(["Custom"], box.ItemsSource!.Cast<string>());
            Assert.Equal("typed filter", box.Text); Assert.True(box.IsVisible);
        });

    [Fact]
    public void DynamicPowerSelectorUsesTheSameRefreshableCatalogAndStrictValidation()
        => AvaloniaTestHost.Run(() =>
        {
            var catalog = new SelectorCatalog(["Base"]);
            var busy = true; var commits = new List<string>();
            var box = PowerEditorDynamicControls.CreateStrictSelector(catalog, "Base", true,
                () => busy, () => true, commits.Add, "unit", new ScrollViewer());
            var panel = new StackPanel(); panel.Children.Add(box);
            Dispatcher.UIThread.RunJobs();
            busy = true; catalog.ReplaceWith(["Base", "Custom"]); SelectorCatalogRefresh.Apply(panel); busy = false;
            Assert.Equal("Base", box.Text); Assert.Empty(commits);
            box.Text = "Custom"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent)); Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(commits); Assert.All(commits, value => Assert.Equal("Custom", value));
        });
}
