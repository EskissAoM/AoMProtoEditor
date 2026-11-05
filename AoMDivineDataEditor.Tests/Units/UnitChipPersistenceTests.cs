using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class UnitChipPersistenceTests
{
    private static object Field(BaselineHost host, string name) => typeof(ProtoEditorWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    [Fact]
    public void ActualTabNavigationStillSuppressesInitializationEvents()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Call("InitializeEditorTabs");
                host.Show();
                Dispatcher.UIThread.RunJobs();
                host.Set("_suppressEditorChangeTracking", false);
                ((TabControl)Field(host, "_editorTabs")).SelectedIndex = 1;
                Assert.True((bool)Field(host, "_suppressEditorChangeTracking"));
                host.Call("MarkDirty");
                Assert.False((bool)Field(host, "_isDirty"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("CheckpointType", "unittype")]
    [InlineData("CollidesWithProjectiles", "flag")]
    public void RealChipAdditionRemovalSaveAndDiscard(string value, string tag)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='ChipUnit'><unittype>Unit</unittype><future keep='yes'/></unit></protomods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Call("InitializeEditorTabs");
                host.Call("BuildEditorPanel", "ChipUnit", true);
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.False((bool)Field(host, "_isPopulating"));
                host.Set("_suppressEditorChangeTracking", false);
                var panel = (StackPanel)Field(host, "_statsEditorPanel");
                var picker = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>(),
                    p => p.ItemsSource?.Cast<object>().Contains(value) == true && p.MaxWidth == 50);
                Assert.Equal(50, picker.Bounds.Width);
                picker.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                picker.SelectedItem = value;
                Dispatcher.UIThread.RunJobs();
                Assert.True((bool)Field(host, "_isDirty"));
                Assert.Contains("ChipUnit", (HashSet<string>)Field(host, "_dirtyUnitNames"));
            });
            AvaloniaTestHost.RunAsync(async () => await (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!);
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Contains(saved.Elements(tag), element => element.Value == value);
            Assert.Equal("yes", (string?)saved.Element("future")?.Attribute("keep"));
            var bytes = File.ReadAllBytes(fixture.ProtoPath);
            object? tab = null;
            AvaloniaTestHost.Run(() =>
            {
                host.Call("BuildEditorPanel", "ChipUnit", true);
                Dispatcher.UIThread.RunJobs();
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
                tab = host.Call("CreateDocumentTab", true, kind, true, "ChipUnit")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, new XElement(saved));
                host.Set("_suppressEditorChangeTracking", false);
                RemoveChip(host, value);
                Assert.True((bool)Field(host, "_isDirty"));
                host.Call("DiscardUnitDocumentChanges", tab);
                Assert.Contains(((XElement)Field(host, "_modXmlRoot")).Element("unit")!.Elements(tag), e => e.Value == value);
            });
            Assert.Equal(bytes, File.ReadAllBytes(fixture.ProtoPath));
            AvaloniaTestHost.Run(() =>
            {
                host.Call("BuildEditorPanel", "ChipUnit", true);
                Dispatcher.UIThread.RunJobs();
                host.Set("_suppressEditorChangeTracking", false);
                RemoveChip(host, value);
            });
            AvaloniaTestHost.RunAsync(async () => await (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!);
            Assert.DoesNotContain(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements(tag), e => e.Value == value);
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    private static void RemoveChip(BaselineHost host, string value)
    {
        var panel = (StackPanel)Field(host, "_statsEditorPanel");
        var label = Assert.Single(panel.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == value);
        var stack = Assert.IsType<StackPanel>(label.Parent);
        Assert.Single(stack.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("autocomplete")]
    [InlineData("combo")]
    public void ChildSelectionDoesNotSuppressDirtyTracking(string controlKind)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Call("InitializeEditorTabs");
                var panel = (StackPanel)Field(host, "_statsEditorPanel");
                Control picker;
                if (controlKind == "autocomplete")
                    picker = new AutoCompleteBox { ItemsSource = new[] { "Value" } };
                else
                    picker = new ComboBox { ItemsSource = new[] { "Value" } };
                panel.Children.Add(picker);
                host.Show();
                Dispatcher.UIThread.RunJobs();
                host.Set("_suppressEditorChangeTracking", false);
                if (picker is AutoCompleteBox auto) auto.SelectedItem = "Value";
                else ((ComboBox)picker).SelectedItem = "Value";
                Assert.False((bool)Field(host, "_suppressEditorChangeTracking"));
                host.Call("MarkDirty");
                Assert.True((bool)Field(host, "_isDirty"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
