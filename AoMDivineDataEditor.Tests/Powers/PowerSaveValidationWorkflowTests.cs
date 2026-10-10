using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
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

            var saved = AvaloniaTestHost.RunAsync(editor.SaveAsync);

            Assert.False(saved);
            Assert.Contains("InvalidOther", AvaloniaTestHost.Run(() => editor.StatusMessage),
                StringComparison.Ordinal);
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
