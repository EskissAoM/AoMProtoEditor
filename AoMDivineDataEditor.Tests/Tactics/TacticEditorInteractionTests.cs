using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TacticEditorInteractionTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Field(BaselineHost host, string name)
        => typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host)!;
    private static object Static(string name, object argument)
        => typeof(ProtoEditorWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [argument])!;

    private static (BaselineHost Host, StackPanel Panel, IList Definitions) Build(BaselineSafetyFixture fixture)
    {
        var host = fixture.CreateHost(false);
        host.Set("_isPopulating", true);
        var source = XDocument.Parse("""
            <tactics><tactic>Default
              <action priority="100">Throw</action><action probability="50">Stealth</action>
              <attackprioritytype bonusfactor="50">ExistingTarget</attackprioritytype>
              <autoattacktype>ExistingTarget</autoattacktype>
              <future keep="yes"/>
            </tactic><tactic>Water</tactic></tactics>
            """);
        host.Set("_tacticsEditorSession", new TacticsActionEditorSession("Test.tactics", false, source, null));
        host.Set("_standaloneUnitNamesProvider", (Func<IEnumerable<string>>)(() => ["CustomUnit", "customunit"]));
        host.Set("_standaloneUnitTypeNamesProvider", (Func<IEnumerable<string>>)(() => ["CustomType"]));
        host.Set("_cachedTrainUnitNames", null!);
        host.Set("_modXmlRoot", new XElement("proto", new XElement("unit", new XAttribute("name", "__tactics_action_editor__"))));
        var definitions = (IList)Field(host, "_tacticsEditorTacticDefinitions");
        foreach (var item in (IList)Static("ParseTacticDefinitions", source)) definitions.Add(item);
        host.Set("_tacticsEditorTacticDefinition", definitions[0]!);
        var actions = (IList)Field(host, "_tacticsEditorSeedActions");
        foreach (var name in new[] { "Throw", "Stealth", "FreeAction" }) actions.Add(new ProtoAction { Name = name });
        var panel = new StackPanel();
        host.Set("_activeEditorPanel", panel);
        host.GodHost.Content = null;
        host.Content = panel;
        host.Call("BuildBasicTacticEditor");
        host.Show();
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.Set("_isPopulating", false);
        host.Set("_isDirty", false);
        return (host, panel, definitions);
    }

    [Fact]
    public void AllSixTargetSelectorsIncludeCustomUnitsAndTypesWithoutTheSyntheticUnit()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var (host, panel, _) = Build(fixture);
            try
            {
                var selectors = panel.GetVisualDescendants().OfType<AutoCompleteBox>().ToList();
                Assert.Equal(6, selectors.Count);
                foreach (var selector in selectors)
                {
                    var names = selector.ItemsSource!.Cast<string>().ToList();
                    Assert.Single(names, name => name.Equals("CustomUnit", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains("CustomType", names);
                    Assert.DoesNotContain("__tactics_action_editor__", names);
                }
                var single = selectors.Single(selector => selector.Parent is StackPanel owner &&
                    owner.Children.OfType<TextBlock>().Any(label => label.Text == "Auto Attack Type"));
                single.Text = "CustomUnit";
                Dispatcher.UIThread.RunJobs();
                var saved = (XElement)Static("SerializeTacticDefinition", ((IList)Field(host, "_tacticsEditorTacticDefinitions"))[0]!);
                Assert.Equal("CustomUnit", saved.Element("autoattacktype")!.Value);
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void StandardActionSelectorsRefreshChoicesWithoutDirtyingAndPreservePriorityProbability()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var (host, panel, definitions) = Build(fixture);
            try
            {
                var selectors = panel.GetVisualDescendants().OfType<ComboBox>().ToList();
                Assert.Equal(2, selectors.Count);
                var priority = selectors.Single(selector => (string?)selector.SelectedItem == "Throw");
                var probability = selectors.Single(selector => (string?)selector.SelectedItem == "Stealth");
                Assert.DoesNotContain("Stealth", priority.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("Throw", probability.ItemsSource!.Cast<string>());
                ((IList)Field(host, "_tacticsEditorSeedActions")).Add(new ProtoAction { Name = "NewAction" });
                priority.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(priority.IsDropDownOpen);
                Assert.Contains("NewAction", priority.ItemsSource!.Cast<string>());
                Assert.False((bool)Field(host, "_isDirty"));
                priority.SelectedItem = "NewAction";
                priority.IsDropDownOpen = false;
                probability.SelectedItem = "FreeAction";
                Assert.True((bool)Field(host, "_isDirty"));
                var saved = (XElement)Static("SerializeTacticDefinition", definitions[0]!);
                Assert.Contains(saved.Elements("action"), action => action.Value == "NewAction" && (string?)action.Attribute("priority") == "100");
                Assert.Contains(saved.Elements("action"), action => action.Value == "FreeAction" && (string?)action.Attribute("probability") == "50");
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void TacticRemovalUsesSharedCrossAndRemovesOnlyItsDefinition()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var (host, panel, definitions) = Build(fixture);
            try
            {
                var remove = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => (string?)ToolTip.GetTip(button) == "Remove tactic");
                Assert.Contains("remove-button", remove.Classes);
                Assert.Equal("×", remove.Content);
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(definitions.Cast<object>());
                Assert.Equal("Water", ((XElement)Static("SerializeTacticDefinition", definitions[0]!)).Value);
                Assert.True((bool)Field(host, "_isDirty"));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetPopupClosesWhenEitherScrollOwnerMoves(bool outerOwner)
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var editor = (AutoCompleteBox)host.Call("CreateBasicTacticAutoComplete", "Custom", new[] { "CustomUnit" }, true)!;
                var inner = new ScrollViewer
                {
                    Height = 180,
                    Content = new StackPanel { Children = { editor, new Border { Height = 1000 } } }
                };
                var outer = new ScrollViewer
                {
                    Height = 150,
                    Content = new StackPanel { Children = { inner, new Border { Height = 1000 } } }
                };
                host.GodHost.Content = null;
                host.Content = outer;
                host.Show();
                host.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                editor.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsDropDownOpen);
                var owner = outerOwner ? outer : inner;
                owner.Offset = new Vector(0, 40);
                Dispatcher.UIThread.RunJobs();
                Assert.True(owner.Offset.Y > 0);
                Assert.False(editor.IsDropDownOpen);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
