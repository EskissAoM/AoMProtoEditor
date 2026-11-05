using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TacticsRenamePromptTests
{
    [Theory]
    [InlineData("My.File.tactics", "My.File", "Renamed")]
    [InlineData("My.File.tactics", "My.File", "Renamed.tactics")]
    [InlineData("My.File.tactics", "My.File", null)]
    [InlineData("My.File.tactics", "My.File", "My.File")]
    [InlineData("My.tactics.tactics", "My.tactics", "My.tactics")]
    public void RenameEditsOnlyTheStemAndAppendsTheExtensionOnce(string original, string stem, string? input)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            (string Old, string New)? renamed = null;
            var manager = new TacticsManagerWindow([new TacticsManagerItem(original, false, false, 0)],
                renameTacticsAsync: (oldName, newName) =>
                {
                    renamed = (oldName, newName);
                    return Task.FromResult(true);
                });
            try
            {
                manager.Show();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var item = ((IList)typeof(TacticsManagerWindow).GetField("_items", flags)!.GetValue(manager)!)[0]!;
                var task = (Task)typeof(TacticsManagerWindow).GetMethod("RenameTacticsAsync", flags)!.Invoke(manager, [item])!;
                InputPromptWindow? prompt = null;
                for (var attempt = 0; attempt < 200 && prompt == null; attempt++)
                {
                    prompt = manager.OwnedWindows.OfType<InputPromptWindow>().SingleOrDefault();
                    if (prompt == null) await Task.Delay(10);
                }
                Assert.NotNull(prompt);
                var editor = prompt.FindControl<TextBox>("_inputBox")!;
                Assert.Equal(stem, editor.Text);
                if (input == null) prompt.Close();
                else
                {
                    editor.Text = input;
                    typeof(InputPromptWindow).GetMethod("OK_Click", flags)!.Invoke(prompt, [null, new RoutedEventArgs()]);
                }
                await task;
                if (input == null || input == stem) Assert.Null(renamed);
                else Assert.Equal((original, "Renamed.tactics"), renamed);
            }
            finally { manager.Close(); }
        });
    }
}
