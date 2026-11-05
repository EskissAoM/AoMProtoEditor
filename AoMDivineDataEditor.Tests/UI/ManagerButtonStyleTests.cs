using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed partial class ManagerButtonStyleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static IEnumerable<object[]> Rows()
    {
        foreach (var kind in new[] { "ability", "command", "tactics", "culture", "minor", "anim", "sound" })
            foreach (var original in new[] { false, true }) yield return [kind, original];
    }

    private static Window Manager(string kind, bool original) => kind switch
    {
        "ability" => new AbilitiesManagerWindow([new("Entry", original, 0)]),
        "command" => new ProtoUnitCommandsManagerWindow([new("Entry", original, 0)]),
        "tactics" => new TacticsManagerWindow([new("Entry", original, false, 0)]),
        "culture" => new CultureManagerWindow([new("Entry", original, 0)], (_, _, _) => Task.CompletedTask,
            _ => Task.FromResult(true), (_, _, _) => Task.FromResult(true), (_, _) => Task.FromResult(true),
            _ => Task.FromResult(true), _ => 0, (_, _) => Task.FromResult(true)),
        "minor" => new MinorGodManagerWindow([new("Entry", "ClassicalAgeEntry", original, 0)],
            (_, _, _) => Task.FromResult<MinorGodEditorResult?>(null), (_, _) => Task.FromResult<MinorGodManagerItem?>(null),
            (_, _, _) => Task.FromResult<MinorGodManagerItem?>(null), (_, _) => Task.FromResult<MinorGodManagerItem?>(null),
            _ => Task.FromResult(true), _ => 0, (_, _) => Task.FromResult(true)),
        _ => new AnimFileManagerWindow([new("entry.xml", original ? "Data.bar" : "Custom", !original)],
            _ => Task.FromResult<string?>("<asset/>"), null,
            profile: kind == "sound" ? XmlAssetManagerProfile.SoundSet : XmlAssetManagerProfile.AnimFile)
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void ActualManagerRowsShareMeasuredSizesAndRetainOriginalDeletionProtection(string kind, bool original)
        => AvaloniaTestHost.Run(() =>
        {
            var manager = Manager(kind, original);
            try
            {
                var item = ((IList)manager.GetType().GetField("_items", Private)!.GetValue(manager)!)[0]!;
                var row = (Grid)manager.GetType().GetMethod("CreateRow", Private)!.Invoke(manager, [item])!;
                manager.Content = row;
                manager.Show(); manager.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var buttons = row.Children.OfType<Button>().ToArray();
                var hasDelete = !original && kind is not ("anim" or "sound");
                Assert.Equal(hasDelete ? 3 : 2, buttons.Length);
                Assert.Contains("manager-action", buttons[0].Classes);
                Assert.Contains("copy-import", buttons[1].Classes);
                Assert.IsType<PathIcon>(buttons[1].Content);
                var noun = kind switch
                {
                    "ability" => "ability", "command" => "command", "tactics" => "tactics",
                    "culture" => "culture", "minor" => "minor god", "anim" => "animation file", _ => "sound set"
                };
                var asset = kind is "anim" or "sound";
                var editDescription = asset ? $"View{(original ? "" : "/edit")} {noun} XML"
                    : $"{(original ? "View" : "Edit")} {noun}";
                var copyDescription = asset ? $"Duplicate {noun} into this mod" : $"Duplicate {noun}";
                Assert.Equal(editDescription, ToolTip.GetTip(buttons[0]));
                Assert.Equal(copyDescription, ToolTip.GetTip(buttons[1]));
                Assert.Equal(editDescription, AutomationProperties.GetName(buttons[0]));
                Assert.Equal(copyDescription, AutomationProperties.GetName(buttons[1]));
                Assert.All(buttons, button =>
                {
                    Assert.Equal(28, button.Bounds.Width); Assert.Equal(28, button.Bounds.Height);
                    Assert.Equal(28, button.MinHeight); Assert.Equal(new Thickness(4), button.Margin);
                });
                if (hasDelete)
                {
                    Assert.Contains("remove-button", buttons[2].Classes);
                    Assert.Equal("×", buttons[2].Content);
                    Assert.Equal($"Delete {noun}", ToolTip.GetTip(buttons[2]));
                    Assert.Equal($"Delete {noun}", AutomationProperties.GetName(buttons[2]));
                    Assert.Equal(Color.Parse("#7A1F1C"), ColorOf(buttons[2].Background));
                }
            }
            finally { manager.Close(); }
        });

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static ContentPresenter Presenter(Button button) => button.GetVisualDescendants()
        .OfType<ContentPresenter>().First(presenter => presenter.Name == "PART_ContentPresenter");
    private static void Pseudo(Button button, string name, bool enabled)
    {
        var classes = (IPseudoClasses)button.Classes;
        if (enabled) classes.Add(name); else classes.Remove(name);
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("copy", "#24384B", "#304A61", "#1B2C3D")]
    [InlineData("delete", "#7A1F1C", "#992824", "#5B1614")]
    [InlineData("positive", "#163E26", "#205333", "#13321F")]
    [InlineData("edit", null, "#2A2E2B", "#171918")]
    public void SemanticStylesKeepTheirPaletteAndDimWhenDisabledEvenWhileHovered(
        string kind, string? normal, string hover, string pressed)
        => AvaloniaTestHost.Run(() =>
        {
            var button = kind switch
            {
                "copy" => ManagerActionButtons.Copy(), "delete" => ManagerActionButtons.Delete(),
                "edit" => ManagerActionButtons.Edit(),
                _ => new Button { Content = "Create", Classes = { "positive" } }
            };
            var window = new Window { Width = 300, Height = 150, Content = button };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var presenter = Presenter(button);
                var baseline = presenter.Background;
                if (normal == null) Assert.IsType<LinearGradientBrush>(baseline);
                else Assert.Equal(Color.Parse(normal), ColorOf(baseline));
                button.Focus(NavigationMethod.Tab); Assert.True(button.IsFocused);
                Assert.Equal(baseline, presenter.Background);
                Pseudo(button, ":pointerover", true);
                Assert.Equal(Color.Parse(hover), ColorOf(presenter.Background));
                Pseudo(button, ":pressed", true);
                Assert.Equal(Color.Parse(pressed), ColorOf(presenter.Background));
                button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
                Assert.Equal(Color.Parse("#202220"), ColorOf(presenter.Background));
                Assert.Equal(Color.Parse("#706C64"), ColorOf(presenter.Foreground));
                if (button.Content is PathIcon icon) Assert.Equal(Color.Parse("#706C64"), ColorOf(icon.Foreground));
                Pseudo(button, ":pressed", false); Pseudo(button, ":pointerover", false);
                button.IsEnabled = true; Dispatcher.UIThread.RunJobs();
                Assert.Equal(baseline, presenter.Background);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void AssetDestinationKeepsItsValidationGateWithSharedPositiveStyle()
    {
        using var workspace = new TestWorkspace();
        AvaloniaTestHost.Run(() =>
        {
            var window = new AssetDestinationWindow("Create", workspace.RootPath, "art", "", ".xml", "valid");
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var button = (Button)typeof(AssetDestinationWindow).GetField("_confirm", Private)!.GetValue(window)!;
                var name = (TextBox)typeof(AssetDestinationWindow).GetField("_name", Private)!.GetValue(window)!;
                Assert.Contains("positive", button.Classes); Assert.True(button.IsEnabled);
                Assert.Equal(Color.Parse("#163E26"), ColorOf(Presenter(button).Background));
                name.Text = ""; Dispatcher.UIThread.RunJobs(); Assert.False(button.IsEnabled);
                Assert.Equal(Color.Parse("#202220"), ColorOf(Presenter(button).Background));
                name.Text = "valid_again"; Dispatcher.UIThread.RunJobs(); Assert.True(button.IsEnabled);
                Assert.Equal(Color.Parse("#163E26"), ColorOf(Presenter(button).Background));
                Assert.Null(window.Destination); Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.RootPath));
            }
            finally { window.Close(); }
        });
    }
}
