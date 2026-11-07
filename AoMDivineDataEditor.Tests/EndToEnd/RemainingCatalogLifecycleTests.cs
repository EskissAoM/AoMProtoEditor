using System.Xml.Linq;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void CommandManagerCallbacksRefreshTheRetainedTechnologySelectorAndSaveMembership()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='U'/></protomods>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml", "<protounitcommandmods><protounitcommand><name>Existing</name></protounitcommand></protounitcommandmods>");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='A'><effects><effect type='Data' subtype='CommandAdd' command='Existing' amount='1' relativity='Assign' row='0' column='0'><target type='ProtoUnit'>U</target></effect></effects></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host); host.Call("ApplyEntityKindUi", kind); await AwaitTechnologyBuild(view);
                var selector = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.Text == "Existing");
                await UnlockTechnologyXml(view); Raw(view).Text += "<!-- keep draft -->";
                var raw = Raw(view).Text; var bytes = File.ReadAllBytes(path);
                async Task Mutate(string method, params object[] arguments)
                {
                    Assert.True(await ((Task<bool>)host.Call(method, arguments)!).WaitAsync(TimeSpan.FromSeconds(5)));
                    host.Call("RefreshEditorsAfterCommandManager");
                    Assert.Same(view, HostTechnologyView(host)); Assert.Equal(raw, Raw(view).Text);
                    Assert.True(view.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(path));
                }
                bool Has(string name) => selector.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase);
                await Mutate("CreateProtoUnitCommandAsync", "FreshCommand"); Assert.True(Has("FreshCommand"));
                await Mutate("DuplicateProtoUnitCommandAsync", "FreshCommand", false, "CopiedCommand"); Assert.True(Has("CopiedCommand"));
                await Mutate("RenameProtoUnitCommandAsync", "FreshCommand", "RenamedCommand"); Assert.False(Has("FreshCommand")); Assert.True(Has("RenamedCommand"));
                await Mutate("DeleteProtoUnitCommandAsync", "CopiedCommand"); Assert.False(Has("CopiedCommand"));
                host.Call("ApplyEntityKindUi", kind); host.GodHost.Content = view;
                host.UpdateLayout(); await AwaitTechnologyBuild(view); Dispatcher.UIThread.RunJobs();
                Assert.Same(host, TopLevel.GetTopLevel(view)); Assert.True(await SaveXmlChecked(view));
                selector = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.Text == "Existing");
                await CommitUnitTypeOperand(selector, "RenamedCommand"); Assert.True(await SaveXmlChecked(view));
                Assert.Equal("RenamedCommand", (string?)XDocument.Load(path).Descendants("effect").Single().Attribute("command"));
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void CultureCallbacksRefreshTheActualGodSelectorAndItsSaveMembership()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><culture>Greek</culture><future keep='yes'/></civ></civsmods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost();
            try
            {
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", System.Reflection.BindingFlags.NonPublic)!, "MajorGods");
                host.Set("_activeEntityKind", kind); host.Call("ApplyEntityKindUi", kind);
                host.God!.SetModifiedMode(true); host.God.SelectMajorGod("CheckpointGod"); host.Show();
                AutoCompleteBox Box() => host.God!.FindControl<AutoCompleteBox>("_cultureBox")!;
                async Task Mutate(string method, params object[] arguments)
                {
                    Assert.True(await ((Task<bool>)host.Call(method, arguments)!).WaitAsync(TimeSpan.FromSeconds(5)));
                    host.Call("RefreshEditorsAfterCultureChanges");
                    host.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal("Greek", Box().Text); Assert.False(host.God!.IsDirty);
                }
                bool Has(string name) => Box().ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase);
                await Mutate("CreateCultureAsync", "FreshCulture"); Assert.True(Has("FreshCulture"));
                await Mutate("DuplicateCultureAsync", "FreshCulture", false, "CopiedCulture"); Assert.True(Has("CopiedCulture"));
                await Mutate("RenameCultureAsync", "FreshCulture", "RenamedCulture"); Assert.False(Has("FreshCulture")); Assert.True(Has("RenamedCulture"));
                await Mutate("DeleteCultureAsync", "CopiedCulture"); Assert.False(Has("CopiedCulture"));
                Box().GetVisualDescendants().OfType<TextBox>().First().Text = "RenamedCulture";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(host.God!.IsDirty);
                Assert.True(await host.God!.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("RenamedCulture", XDocument.Load(fixture.GodPath).Descendants("culture").Single().Value);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void GlobalTacticsMetadataIncludesBarAndRetriesPartialLooseSources()
    {
        using var fixture = new BaselineSafetyFixture();
        var bytes = XmbReaderTests.BuildDocument(XElement.Parse("<tactics><action><name>BarOnlyAction</name><type>Attack</type></action><tactic>BarMode</tactic></tactics>"));
        using var encoded = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry("game/data/gameplay/tactics/test.tactics.xmb", bytes, bytes.Length, false));
        var path = fixture.Workspace.GetPath("catalog.bar"); File.WriteAllBytes(path, encoded.ToArray());
        var archive = new BarArchive(encoded); Assert.True(archive.Load(out _));
        var broken = fixture.Workspace.WriteText("TestMod/game/data/gameplay/tactics/broken.tactics", "<tactics><unfinished>");
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_protoDataBarFile", archive); host.Set("_protoDataBarPath", path); host.Call("InvalidateSuggestionCaches", true);
                Assert.Contains("BarOnlyAction", (IReadOnlyList<string>)host.Call("GetTechnologyProtoActionNames")!);
                var partial = host.Call("GetTechnologyTacticDefinitionNames");
                Assert.NotSame(partial, host.Call("GetTechnologyTacticDefinitionNames"));
                File.WriteAllText(broken, "<tactics><action><name>RecoveredAction</name><type>Attack</type></action><tactic>RecoveredMode</tactic></tactics>");
                Assert.Contains("RecoveredAction", (IReadOnlyList<string>)host.Call("GetTechnologyProtoActionNames")!);
                var complete = host.Call("GetTechnologyTacticDefinitionNames");
                Assert.Same(complete, host.Call("GetTechnologyTacticDefinitionNames"));
                Assert.Equal(new[] { "BarMode", "RecoveredMode" }, (IReadOnlyList<string>)complete!);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
