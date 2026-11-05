using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private static T XmlHostField<T>(BaselineHost host, string name)
        => (T)typeof(ProtoEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static async Task<string?> SaveXmlThroughHost(BaselineHost host, bool expectBlocked = false)
    {
        host.Call("Save_Click", null!, new RoutedEventArgs());
        for (var i = 0; XmlHostField<bool>(host, "_isUnitSaveInProgress") && i < 1000; i++)
        {
            if (host.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup)
            {
                var reason = popup.PromptText; popup.Close();
                Assert.True(expectBlocked, reason);
            }
            await Task.Delay(10);
        }
        Assert.False(XmlHostField<bool>(host, "_isUnitSaveInProgress"));
        return XmlHostField<TextBlock>(host, "_statusMessage").Text;
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void D2FollowUpGlobalXmlRenameMigratesRandomTechAndSelectorReferences(bool randomTech)
    {
        using var fixture = new BaselineSafetyFixture();
        var references = randomTech
            ? "<effects><effect type='RandomTech' select='1' status='active'><Tech>A</Tech></effect></effects>"
            : "<prereqs><techstatus status='Active'>A</techstatus></prereqs>";
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'><future keep='yes'/></tech><tech name='Consumer'>" + references + "</tech></techtreemods>");
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='U'><tech>A</tech><future keep='yes'/></unit></protomods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, _) = AttachXmlTechnologyHost(host);
                await UnlockTechnologyXml(view);
                Raw(view).Text = "<tech name='Renamed'><future keep='yes'/></tech>";
                Assert.Equal("Changes saved.", await SaveXmlThroughHost(host));
                var saved = XDocument.Load(path);
                Assert.Equal("Renamed", view.CurrentTechnologyName);
                var reference = randomTech ? saved.Descendants("Tech").Single().Value : saved.Descendants("techstatus").Single().Value;
                Assert.Equal("Renamed", reference);
                Assert.Equal("Renamed", XDocument.Load(fixture.ProtoPath).Descendants("tech").Single().Value);
                Assert.Equal("yes", (string?)FindTechnology(saved, "Renamed").Element("future")!.Attribute("keep"));
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
                Assert.Equal("", GetPrivateField<TextBlock>(view, "_statusMessage").Text);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    private static async Task SettleXmlPointer()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(30);
    }

    private static async Task ClickXmlTechnology(BaselineHost host, string name)
    {
        var list = XmlHostField<ListBox>(host, "_unitList");
        var items = XmlHostField<IEnumerable>(host, "_filteredUnitNames").Cast<object>().ToArray();
        var index = Array.FindIndex(items, item => (string)item.GetType().GetProperty("UnitName")!.GetValue(item)! == name);
        Assert.True(index >= 0);
        var row = list.ContainerFromIndex(index); Assert.NotNull(row);
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), host)!.Value;
        host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left);
        await SettleXmlPointer();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void D2FollowUpPointerDiscardSynchronizesTheBrowserAndClearsOnlyStaleFeedback(bool blockedFirst)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", XmlInitial);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind); host.Call("RefreshUnitList");
                host.Width = 1440; host.Height = 932; await SettleXmlPointer();
                await UnlockTechnologyXml(view);
                Raw(view).Text = blockedFirst ? "<tech name='A'><broken>" : "<tech name='A'><researchpoints>7</researchpoints></tech>";
                if (blockedFirst) Assert.Equal("Save blocked.", await SaveXmlThroughHost(host, true));
                await ClickXmlTechnology(host, "B");
                var prompt = await XmlPopup(host); Assert.Equal("Unsaved main document", prompt.PromptTitle);
                prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await XmlHostField<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(5));
                await AwaitTechnologyBuild(view); await SettleXmlPointer();
                Assert.Equal("B", view.CurrentTechnologyName);
                var selected = XmlHostField<ListBox>(host, "_unitList").SelectedItem!;
                Assert.Equal("B", selected.GetType().GetProperty("UnitName")!.GetValue(selected));
                Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal("", XmlHostField<TextBlock>(host, "_statusMessage").Text);
                await ClickXmlTechnology(host, "A");
                await XmlHostField<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(host.OwnedWindows.OfType<Prompt>()); Assert.False(view.IsDirty);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void D2FollowUpHostedTechnologyHasOneFooterAndKeepsBlockingFeedbackDuringEdits()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, _) = AttachXmlTechnologyHost(host);
                await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='A'><broken>";
                Assert.Equal("Save blocked.", await SaveXmlThroughHost(host, true));
                Raw(view).Text = "<tech name='A'><researchpoints>4</researchpoints></tech>";
                Assert.Equal("Save blocked.", XmlHostField<TextBlock>(host, "_statusMessage").Text);
                Assert.False(GetPrivateField<TextBlock>(view, "_statusMessage").IsVisible);
                Assert.NotEqual("Modified", GetPrivateField<TextBlock>(view, "_statusMessage").Text);
                Assert.Equal("Changes saved.", await SaveXmlThroughHost(host));
                Assert.False(GetPrivateField<TextBlock>(view, "_statusMessage").IsVisible);
                Assert.False(GetPrivateField<TextBlock>(host.God!, "_statusMessage").IsVisible);
                host.Call("EnsurePowerView");
                var power = XmlHostField<PowerEditorView>(host, "_powerView");
                Assert.False(GetPrivateField<TextBlock>(power, "_statusMessage").IsVisible);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void D2FollowUpDiscardDoesNotClearTheWarningWhileAnotherDraftIsPending()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", XmlInitial);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, _) = AttachXmlTechnologyHost(host);
                await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='A'><broken>";
                Assert.Equal("Save blocked.", await SaveXmlThroughHost(host, true));
                await UnlockTechnologyXml(view, "B"); Raw(view).Text = "<tech name='B'><future changed='yes'/></tech>";
                view.DiscardTechnologyChanges("A", view.LoadSavedTechnologyElement("A"));
                host.Call("ClearFeedbackAfterDiscard");
                Assert.True(view.IsDirty); Assert.Equal("Save blocked.", XmlHostField<TextBlock>(host, "_statusMessage").Text);
                view.DiscardTechnologyChanges("B", view.LoadSavedTechnologyElement("B"));
                host.Call("ClearFeedbackAfterDiscard");
                Assert.False(view.IsDirty); Assert.Equal("", XmlHostField<TextBlock>(host, "_statusMessage").Text);
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void D2FollowUpUnknownRandomTechReferenceStillBlocksWithoutWritingFiles()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'/><tech name='Consumer'><effects><effect type='RandomTech' select='1' status='active'><Tech>MissingTech</Tech></effect></effects></tech></techtreemods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                // Unlike the data-only save checks, this case asserts a native
                // owned popup: display the Technology surface before saving.
                host.Call("ApplyEntityKindUi", kind);
                host.Call("RefreshUnitList");
                Dispatcher.UIThread.RunJobs();
                await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='Renamed'/>";
                host.Call("Save_Click", null!, new RoutedEventArgs());
                var popup = await XmlPopup(host);
                Assert.True(popup.PromptIsError); Assert.Contains("MissingTech", popup.PromptText); popup.Close();
                for (var i = 0; XmlHostField<bool>(host, "_isUnitSaveInProgress") && i < 1000; i++) await Task.Delay(10);
                Assert.False(XmlHostField<bool>(host, "_isUnitSaveInProgress"));
                Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal("<tech name='Renamed'/>", Raw(view).Text);
                Assert.False(Raw(view).IsReadOnly); Assert.True(view.IsDirty);
                Assert.Equal("Save blocked.", XmlHostField<TextBlock>(host, "_statusMessage").Text);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
