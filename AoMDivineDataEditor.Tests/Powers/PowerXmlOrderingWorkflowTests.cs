using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerXmlOrderingWorkflowTests : IDisposable
{
    private const string Initial = "<powersmod><power name='A' type='Bolt'><placement>Full</placement><cost>2</cost><activetime>1</activetime></power><power name='B' type='Bolt'><placement>Full</placement><activetime>1</activetime></power></powersmod>";
    private static string[] Tags(XElement entry) => entry.Elements().Select(e => e.Name.LocalName).ToArray();
    private static XElement Find(string path, string name) => PowerCatalog.Find(XDocument.Load(path), name)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void StructuredPowerOrderMatchesPreviewDiskAndReopenWithoutRewritingSibling()
    {
        using var workspace = new TestWorkspace(nameof(PowerXmlOrderingWorkflowTests));
        var path = workspace.WriteText("powers_mods.xml", Initial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView([], path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                Assert.False(view.IsDirty);
                view.FindControl<TextBox>("_activeTimeBox")!.Text = "2";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(view.IsDirty);
                string[] expected = ["activetime", "cost", "placement"];
                Assert.Equal(expected, Tags(XElement.Parse(view.FindControl<TextEditor>("_rawXmlEditor")!.Text)));
                Assert.True(await view.SaveAsync());
                Assert.Equal(expected, Tags(Find(path, "A")));
                Assert.Equal(new[] { "placement", "activetime" }, Tags(Find(path, "B")));
                var once = File.ReadAllBytes(path);
                Assert.True(await view.SaveAsync()); Assert.Equal(once, File.ReadAllBytes(path));
                await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
                Assert.False(view.IsDirty);
                Assert.Equal(expected, Tags(XElement.Parse(view.FindControl<TextEditor>("_rawXmlEditor")!.Text)));
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void RawPowerOrderSurvivesThenLaterFormSaveAppliesFormOrder()
    {
        using var workspace = new TestWorkspace(nameof(PowerXmlOrderingWorkflowTests));
        var path = workspace.WriteText("powers_mods.xml", Initial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView([], path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); await PowerXmlTestHelpers.UnlockAsync(view);
                view.FindControl<TextEditor>("_rawXmlEditor")!.Text = "<power name='A' type='Bolt'><placement>Full</placement><cost>3</cost><activetime>2</activetime></power>";
                Assert.True(await view.SaveAsync());
                Assert.Equal(new[] { "placement", "cost", "activetime" }, Tags(Find(path, "A")));
                await view.SelectPowerAsync("A");
                view.FindControl<TextBox>("_activeTimeBox")!.Text = "3";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(view.IsDirty);
                Assert.True(await view.SaveAsync());
                Assert.Equal(new[] { "activetime", "cost", "placement" }, Tags(Find(path, "A")));
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }
}
