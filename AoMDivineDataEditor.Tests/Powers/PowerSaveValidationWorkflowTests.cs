using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerSaveValidationWorkflowTests
{
    [Fact]
    public void SaveValidatesUnselectedPowerRecordsAndLeavesTheFileUntouched()
    {
        using var workspace = new TestWorkspace(nameof(
            SaveValidatesUnselectedPowerRecordsAndLeavesTheFileUntouched));
        var original = """
            <powersmod>
              <power name="Selected" type="Bolt"><radius>10</radius></power>
              <power name="InvalidOther" type="UnitModify">
                <unitmodify apply="Add" amount="not-a-number"></unitmodify>
              </power>
            </powersmod>
            """;
        var path = workspace.WriteText("powers_mods.xml", original);
        var before = XDocument.Load(path, LoadOptions.PreserveWhitespace);

        AvaloniaTestHost.EnsureStarted();
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                editor.SetModifiedMode(true);
                editor.SelectPower("Selected");
            });

            var saved = AvaloniaTestHost.RunAsync(async () =>
            {
                var owner = new Window { Content = editor };
                owner.Show();
                try
                {
                    var saving = editor.SaveAsync();
                    Dispatcher.UIThread.RunJobs();
                    var popup = Assert.Single(owner.OwnedWindows.OfType<Prompt>());
                    Assert.Contains("InvalidOther", popup.PromptText, StringComparison.Ordinal);
                    Assert.True(popup.IsVisible);
                    popup.Close();
                    var result = await saving.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), field => field.Classes.Contains("invalid-field"));
                    return result;
                }
                finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
            });

            Assert.False(saved);
            Assert.Equal("Save blocked.", AvaloniaTestHost.Run(() => editor.StatusMessage));
            Assert.True(XNode.DeepEquals(before,
                XDocument.Load(path, LoadOptions.PreserveWhitespace)));
        }
        finally
        {
            AvaloniaTestHost.Run(() => editor.SelectPower(null));
            editor = null!;
            AvaloniaTestHost.CollectUiGarbage();
        }
    }
}
