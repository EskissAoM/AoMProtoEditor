using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PopupConventionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        // Enter/Escape can dispose the native popup on key-down itself.
        if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData((int)PromptActionRole.Destructive, "Delete unit", "danger", false)]
    [InlineData((int)PromptActionRole.Positive, "Save", "positive", true)]
    [InlineData((int)PromptActionRole.Neutral, "Reload", null, true)]
    public void RolesUseThemedActionsAndSafeEnterDefaults(int role, string label, string? css, bool enterConfirms)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = new Window { Width = 700, Height = 500 };
            owner.Show();
            var prompt = new Prompt(PromptType.Confirm, "Action", "Detailed explanation.", confirmButtonText: label, actionRole: (PromptActionRole)role);
            try
            {
                var showing = prompt.ShowDialog(owner);
                Dispatcher.UIThread.RunJobs();
                var confirm = prompt.FindControl<Button>("_confirmButton")!;
                var cancel = prompt.FindControl<Button>("_cancelButton")!;
                Assert.Equal(label, confirm.Content);
                Assert.Equal(enterConfirms, confirm.IsDefault);
                Assert.Equal(!enterConfirms, cancel.IsDefault);
                Assert.True(cancel.IsCancel);
                if (css != null) Assert.Contains(css, confirm.Classes);
                else { Assert.DoesNotContain("positive", confirm.Classes); Assert.DoesNotContain("danger", confirm.Classes); }
                Assert.True(confirm.Bounds.Height >= 31);
                Press(prompt, Key.Enter);
                await showing.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(enterConfirms, prompt.Confirmed);
            }
            finally { prompt.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData((int)PromptActionRole.Destructive)]
    [InlineData((int)PromptActionRole.Positive)]
    [InlineData((int)PromptActionRole.Neutral)]
    public void EscapeCancelsEveryConfirmationRole(int role)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = new Window(); owner.Show();
            var prompt = new Prompt(PromptType.Confirm, "Action", "Reason", actionRole: (PromptActionRole)role);
            try
            {
                var showing = prompt.ShowDialog(owner);
                Press(prompt, Key.Escape);
                await showing.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(prompt.Confirmed);
            }
            finally { prompt.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData("ability", false)] [InlineData("ability", true)]
    [InlineData("command", false)] [InlineData("command", true)]
    [InlineData("tactics", false)] [InlineData("tactics", true)]
    public void ManagerDeletionRequiresExplicitConfirmationAndCallsTheOriginalCallback(string kind, bool accept)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var deleted = new List<string>();
            Task<bool> Delete(string name) { deleted.Add(name); return Task.FromResult(true); }
            Window manager = kind switch
            {
                "ability" => new AbilitiesManagerWindow([new("Custom", false, 0)], deleteAbilityAsync: Delete),
                "command" => new ProtoUnitCommandsManagerWindow([new("Custom", false, 0)], deleteCommandAsync: Delete),
                _ => new TacticsManagerWindow([new("Custom", false, false, 0)], deleteTacticsAsync: Delete)
            };
            try
            {
                manager.Show();
                var items = (IList)manager.GetType().GetField("_items", Private)!.GetValue(manager)!;
                var method = kind == "ability" ? "DeleteAbilityAsync" : kind == "command" ? "DeleteCommandAsync" : "DeleteTacticsAsync";
                var deleting = (Task)manager.GetType().GetMethod(method, Private)!.Invoke(manager, [items[0]])!;
                var prompt = Assert.Single(manager.OwnedWindows.OfType<Prompt>());
                var confirm = prompt.FindControl<Button>("_confirmButton")!;
                Assert.Equal("Delete " + kind, confirm.Content);
                Assert.Equal(PromptActionRole.Destructive, prompt.ActionRole);
                Assert.Empty(deleted);
                if (accept) confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else Press(prompt, Key.Enter);
                await deleting.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(accept ? new[] { "Custom" } : [], deleted);
                Assert.Equal(accept ? 0 : 1, items.Count);
            }
            finally { foreach (var child in manager.OwnedWindows.ToArray()) child.Close(); manager.Close(); }
        });

    [Fact]
    public void ShortPromptsShrinkAndLongDetailsScrollInsideTheOwnerWithActionsReachable()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var shortPrompt = new Prompt(PromptType.Confirm, "Delete", "Delete 'Example'?", confirmButtonText: "Delete", actionRole: PromptActionRole.Destructive).FitToContent(680, 400);
            var shortSize = new Size(shortPrompt.Width, shortPrompt.Height);
            Assert.True(shortSize.Width < 680 && shortSize.Height < 400);
            var details = string.Join("\n\n", Enumerable.Repeat(@"C:\very-long-path\references\mod-data\game\data\gameplay\powers_mods.xml — TargetOne and TargetTwo", 40));
            var prompt = new Prompt(PromptType.Error, "References prevent deletion", details).FitToContent(680, 400);
            var owner = new Window { Width = 500, Height = 320 }; owner.Show();
            try
            {
                var showing = prompt.ShowDialog(owner);
                Dispatcher.UIThread.RunJobs();
                Assert.True(prompt.Width <= owner.ClientSize.Width - 32);
                Assert.True(prompt.Height <= owner.ClientSize.Height - 32);
                Assert.NotEqual(WindowState.Maximized, prompt.WindowState);
                var scroll = prompt.GetLogicalDescendants().OfType<ScrollViewer>().Single();
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 0);
                Assert.Equal(details, prompt.PromptText);
                var close = prompt.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Close"));
                var point = close.TranslatePoint(default, prompt)!.Value;
                Assert.InRange(point.Y + close.Bounds.Height, 1, prompt.ClientSize.Height);
                Press(prompt, Key.Escape);
                await showing.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally { prompt.Close(); owner.Close(); }
        });

    [Fact]
    public void InputEscapePreservesNullAndLongMinorGodNameFitsAboveTheActions()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = new Window { Width = 550, Height = 420 }; owner.Show();
            var input = new InputPromptWindow(string.Join(" ", Enumerable.Repeat("A long field instruction", 15)), "KeepMe");
            var age = new MinorGodAgePromptWindow(new string('G', 5000));
            try
            {
                var showing = input.ShowDialog(owner);
                Press(input, Key.Escape);
                await showing.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Null(input.InputText);
                var ageShowing = age.ShowDialog(owner);
                Dispatcher.UIThread.RunJobs();
                var scroll = age.GetLogicalDescendants().OfType<ScrollViewer>().Single();
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                var create = age.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Create technology"));
                var point = create.TranslatePoint(default, age)!.Value;
                Assert.InRange(point.Y + create.Bounds.Height, 1, age.ClientSize.Height);
                Press(age, Key.Escape);
                await ageShowing.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Null(age.SelectedAgePrefix);
            }
            finally { input.Close(); age.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData("Cancel", (int)ProtoUnitTransformRemovalChoice.Cancel)]
    [InlineData("Remove from this Unit", (int)ProtoUnitTransformRemovalChoice.OnlyThisUnit)]
    [InlineData("Delete command", (int)ProtoUnitTransformRemovalChoice.RemoveTotally)]
    public void TransformChoicesKeepTheirDistinctResults(string label, int expected)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = new Window { Width = 700, Height = 500 }; owner.Show();
            try
            {
                var choosing = ProtoUnitTransformRemovalDialog.ShowAsync(owner, new string('T', 300));
                var dialog = Assert.Single(owner.OwnedWindows);
                Dispatcher.UIThread.RunJobs();
                var button = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
                if (label == "Cancel") Press(dialog, Key.Enter);
                else { Assert.Contains("danger", button.Classes); Assert.False(button.IsDefault); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                Assert.Equal((ProtoUnitTransformRemovalChoice)expected, await choosing.WaitAsync(TimeSpan.FromSeconds(3)));
            }
            finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        });

    [Fact]
    public void NeutralFeedbackNeverRestoresThePreThemeBlackBrush()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock { Foreground = Brushes.Black };
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Report(EditorSaveOutcome.Blocked);
            status.Text = "Select a Power Type before saving.";
            Assert.Equal(Color.Parse("#E8DECC"), ((ISolidColorBrush)status.Foreground!).Color);
            feedback.Report(EditorSaveOutcome.Cancelled);
            Assert.Equal(Color.Parse("#E8DECC"), ((ISolidColorBrush)status.Foreground!).Color);
        });
}
