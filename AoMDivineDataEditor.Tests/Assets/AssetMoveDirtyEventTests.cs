using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetMoveDirtyEventTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void TechnologyTextPopulationPreservesEmptyXmlAndActualEditsStillApply()
    {
        var element = XElement.Parse("<future keep='yes'/>");
        var editor = AvaloniaTestHost.Run(() => new TechnologyEditorView());
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        TextBox? box = null;
        AvaloniaTestHost.Run(() =>
        {
            box = Assert.IsType<TextBox>(typeof(TechnologyEditorView).GetMethod("AddTextPropertyRow", Private)!.Invoke(editor,
                new object?[] { "Future", element.Value, (Action<string>)(value => element.Value = value), null,
                    EditorFieldWidthRole.SingleLineText }));
            var panel = (StackPanel)typeof(TechnologyEditorView).GetField("_propertiesPanel", Private)!.GetValue(editor)!;
            Assert.Contains(box, panel.GetLogicalDescendants().OfType<TextBox>());
        });
        AvaloniaTestHost.Run(() => Assert.True(element.IsEmpty));
        AvaloniaTestHost.Run(() => box!.Text = "real edit");
        AvaloniaTestHost.Run(() => Assert.Equal("real edit", element.Value));
        AvaloniaTestHost.Run(() => box!.Text = "");
        AvaloniaTestHost.Run(() => { Assert.Equal("", element.Value); Assert.Equal("yes", (string?)element.Attribute("keep")); });
    }

    [Fact]
    public void UnitNamePopulationStaysCleanButRenamingStillBlocksAssetMove()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        try
        {
            var box = AvaloniaTestHost.Run(() => (TextBox)typeof(ProtoEditorWindow).GetField("_unitNameBox", Private)!.GetValue(host)!);
            AvaloniaTestHost.Run(() =>
            {
                host.Set("_suppressUnitNameChange", true);
                host.Set("_currentUnitName", "ExistingUnit");
                box.Text = "ExistingUnit";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                host.Set("_suppressUnitNameChange", false);
                host.Call("UnitNameBox_TextChanged", box, new TextChangedEventArgs(TextBox.TextChangedEvent));
            });
            Assert.False(AvaloniaTestHost.Run(() => (bool)host.Call("HasPendingAssetMoveChanges")!));
            AvaloniaTestHost.Run(() => box.Text = "RenamedUnit");
            Assert.True(AvaloniaTestHost.Run(() => (bool)host.Call("HasPendingAssetMoveChanges")!));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
