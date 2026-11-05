using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class NativeReviewDiscardTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(BaselineHost host, string name) => typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host);

    [Theory]
    [InlineData("Units", "replace")] [InlineData("Units", "tab")] [InlineData("Units", "window")]
    [InlineData("Technologies", "replace")] [InlineData("Technologies", "tab")] [InlineData("Technologies", "window")]
    [InlineData("Powers", "replace")] [InlineData("Powers", "tab")] [InlineData("Powers", "window")]
    public void NativeReviewInvalidDraftCanBeDiscardedWithoutSaveValidation(string kindName, string operation)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='A'><maxhitpoints>10</maxhitpoints></unit></protomods>");
        var techPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='A'/></techtreemods>");
        var powerPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='A' type='Bolt'/></powersmod>");
        var files = new[] { fixture.ProtoPath, techPath, powerPath };
        var before = files.Select(File.ReadAllBytes).ToArray();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Show();
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, kindName);
                host.Set("_suppressUnitListSelectionChange", true);
                ((TabStrip)Field(host, "_unitTabs")!).SelectedIndex = 1;
                host.Call("ApplyEntityKindUi", kind);
                XElement saved;
                if (kindName == "Units")
                {
                    host.Call("BuildEditorPanel", "A", true);
                    saved = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
                }
                else if (kindName == "Technologies")
                {
                    var view = (TechnologyEditorView)Field(host, "_technologyView")!;
                    view.SetModifiedMode(true); view.SelectTechnology("A");
                    await ((Task)typeof(TechnologyEditorView).GetField("_activeEditorBuildTask", Private)!.GetValue(view)!).WaitAsync(TimeSpan.FromSeconds(5));
                    saved = XDocument.Load(techPath).Root!.Element("tech")!;
                }
                else
                {
                    var view = (PowerEditorView)Field(host, "_powerView")!;
                    view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                    saved = XDocument.Load(powerPath).Root!.Element("power")!;
                }
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var tab = host.Call("CreateDocumentTab", operation != "tab", kind, true, "A")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, new XElement(saved));
                ((IList)Field(host, "_openDocumentTabs")!).Add(tab);
                host.Set("_activeDocumentTab", tab);
                if (operation != "tab") host.Set("_mainDocumentTab", tab);
                if (kindName == "Units")
                    ((TextBox)((IDictionary)Field(host, "_fieldControls")!)["maxhitpoints"]!).Text = ".";
                else if (kindName == "Technologies")
                {
                    var name = ((TechnologyEditorView)Field(host, "_technologyView")!).FindControl<TextBox>("_techNameBox")!;
                    name.Focus(); name.Text = "invalid/name";
                }
                else
                    ((PowerEditorView)Field(host, "_powerView")!).FindControl<AutoCompleteBox>("_powerTypeBox")!.Text = "";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True((bool)host.Call("IsDocumentTabDirty", tab)!);
                // Cancel must keep the invalid draft; a second attempt can discard it.
                foreach (var discard in new[] { false, true })
                {
                    Task action;
                    if (operation == "window") { host.Close(); action = Task.CompletedTask; }
                    else action = (Task)host.Call(operation == "tab" ? "CloseDocumentTabAsync" : "ConfirmMainDocumentReplacementAsync",
                        operation == "tab" ? new[] { tab } : new[] { kind, (object)true, "B" })!;
                    for (var i = 0; i < 300 && !host.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                    var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                    Assert.Contains("Discard", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
                    prompt.GetType().GetMethod(discard ? "ConfirmButton_Click" : "CancelButton_Click", Private)!.Invoke(prompt, new object?[] { null, new RoutedEventArgs() });
                    await action.WaitAsync(TimeSpan.FromSeconds(5));
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    if (!discard) { Assert.True(host.IsVisible); Assert.True((bool)host.Call("IsDocumentTabDirty", tab)!); }
                }
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.Empty(host.OwnedWindows.OfType<Prompt>());
                if (operation == "window") Assert.False(host.IsVisible);
                else Assert.False((bool)host.Call("IsDocumentTabDirty", tab)!);
                for (var i = 0; i < files.Length; i++) Assert.Equal(before[i], File.ReadAllBytes(files[i]));
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void NativeReviewInactivePowerViewCannotLeaveItsContextInTheUnitFooter()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Show();
                object Kind(string name) => Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, name);
                host.Call("ApplyEntityKindUi", Kind("Powers"));
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var status = (TextBlock)Field(host, "_statusMessage")!;
                Assert.DoesNotContain("Select a power", status.Text ?? "", StringComparison.OrdinalIgnoreCase);
                var power = (PowerEditorView)Field(host, "_powerView")!;
                ((Dictionary<string, XElement>)typeof(PowerEditorView).GetField("_baseEntries", Private)!.GetValue(power)!)["OriginalPower"] =
                    XElement.Parse("<power name='OriginalPower' type='Bolt'/>");
                await power.SelectPowerAsync("OriginalPower");
                Assert.Contains("Original .godpowers", status.Text);
                host.Call("ApplyEntityKindUi", Kind("Units"));
                Assert.Equal("", status.Text);
                host.Call("ReportContextStatus", "Unit context");
                ((TextBlock)typeof(PowerEditorView).GetField("_statusMessage", Private)!.GetValue(power)!).Text = "Inactive power context";
                Assert.Equal("Unit context", status.Text);
                host.Call("ApplyEntityKindUi", Kind("Powers"));
                await power.SelectPowerAsync("OriginalPower");
                Assert.Contains("Original .godpowers", status.Text);
                host.Call("ApplyEntitySourceUi", true);
                Assert.Equal("", status.Text);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
