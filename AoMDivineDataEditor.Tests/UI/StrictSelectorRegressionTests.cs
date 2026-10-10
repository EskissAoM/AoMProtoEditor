using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class StrictSelectorRegressionTests
{
    [Fact]
    public void StrictSearchableComboBox_UsesTheNativeComboBoxTheme()
    {
        AvaloniaTestHost.Run(() =>
        {
            var comboBox = new StrictSearchableComboBox(["One", "Two"]);
            var styleKeyProperty = typeof(StrictSearchableComboBox).GetProperty(
                "StyleKeyOverride",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(styleKeyProperty);
            Assert.Equal(typeof(ComboBox), styleKeyProperty.GetValue(comboBox));
        });
    }

    [Fact]
    public void StrictSearchableComboBox_FiltersButCommitsOnlyKnownOptions()
    {
        AvaloniaTestHost.Run(() =>
        {
            var comboBox = new StrictSearchableComboBox(
                ["Camel", "CamelRider", "MythicCamel", "Horse"],
                "Camel");

            comboBox.SelectedItem = null;
            comboBox.Text = "camel";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Camel", "CamelRider", "MythicCamel"], comboBox.ItemsSource!.Cast<string>());
            Assert.Equal("Camel", comboBox.Value);

            comboBox.Text = "camelrider";
            comboBox.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

            Assert.Equal("CamelRider", comboBox.Value);
            Assert.Equal("CamelRider", comboBox.Text);

            comboBox.SelectedItem = "MythicCamel";

            Assert.Equal("MythicCamel", comboBox.Value);
            Assert.Equal("MythicCamel", comboBox.Text);

            comboBox.Text = "not-a-valid-option";
            comboBox.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

            Assert.Equal("MythicCamel", comboBox.Value);
            Assert.Equal("MythicCamel", comboBox.Text);
        });
    }

    [Fact]
    public void StrictSearchableComboBox_PreservesAnExistingLegacyValueUntilUserChangesIt()
    {
        AvaloniaTestHost.Run(() =>
        {
            var comboBox = new StrictSearchableComboBox(
                ["bronze", "gold"],
                "futureShading",
                preserveUnknownInitialValue: true);

            Assert.Equal("futureShading", comboBox.Text);
            Assert.Equal("futureShading", comboBox.Value);

            comboBox.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal("futureShading", comboBox.Text);
            Assert.Equal("futureShading", comboBox.Value);

            comboBox.SelectedItem = "gold";
            Assert.Equal("gold", comboBox.Value);
        });
    }

    [Fact]
    public void ConfiguredStrictSelectorAcceptsAProgrammaticallyPopulatedLegacyBaseline()
    {
        AvaloniaTestHost.Run(() =>
        {
            var isPopulating = false;
            var committed = new List<string>();
            var selector = new AutoCompleteBox();
            EditorAutoCompleteService.ConfigureStrict(selector, ["KnownOne", "KnownTwo"], "",
                () => isPopulating, preserveUnknownInitialValue: false,
                valueCommitted: committed.Add);

            isPopulating = true;
            selector.SelectedItem = null;
            selector.Text = "ModOnlyValue";
            isPopulating = false;

            selector.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("ModOnlyValue", selector.Text);
            Assert.Equal(["ModOnlyValue"], committed);
        });
    }

    [Fact]
    public void ConfiguredStrictSelectorCancelsQueuedCommitWhenItsCardIsDetached()
    {
        AvaloniaTestHost.Run(() =>
        {
            var committed = new List<string>();
            var selector = new AutoCompleteBox { Text = "KnownOne" };
            EditorAutoCompleteService.ConfigureStrict(selector, ["KnownOne", "KnownTwo"], "KnownOne",
                valueCommitted: committed.Add);
            var card = new Border { Child = selector };
            var host = new StackPanel { Children = { card } };
            var window = new Window { Content = host };
            window.Show();
            try
            {
                selector.Text = "invalid";
                selector.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                host.Children.Remove(card);
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(committed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HotkeyContexts_MergeNestedBaseAndModContextsWithoutDuplicates()
    {
        var baseDocument = XDocument.Parse("""
            <unitcontexts>
              <traincontexts>
                <context>TownCenterAccel</context>
                <context>MarketAccel</context>
              </traincontexts>
            </unitcontexts>
            """);
        var modDocument = XDocument.Parse("""
            <unitcontextsmods>
              <context>CustomTempleAccel</context>
              <context>towncenteraccel</context>
            </unitcontextsmods>
            """);

        var merged = HotkeyContextCatalog.Merge(
            HotkeyContextCatalog.ExtractContextValues(baseDocument),
            HotkeyContextCatalog.ExtractContextValues(modDocument));

        Assert.Equal(["CustomTempleAccel", "MarketAccel", "TownCenterAccel"], merged);
    }

}
