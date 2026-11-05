using System.Reflection;
using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Services;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Tests;

// Only the two Pass 0 safety scenarios; reuse TestWorkspace for isolation/cleanup.
internal sealed class BaselineSafetyFixture : IDisposable
{
    public TestWorkspace Workspace { get; } = new("pass-0-safety");
    public string ProtoPath { get; }
    public string GodPath { get; }
    public string TypePath { get; }
    public string RecoveryTarget { get; }
    public byte[] OriginalBytes { get; } = Encoding.UTF8.GetBytes("original recovery bytes\r\n");
    public string GameRoot { get; }

    public BaselineSafetyFixture()
    {
        ProtoPath = Workspace.WriteXml("TestMod/game/data/gameplay/proto_mods.xml", "<protomods />");
        GodPath = Workspace.WriteXml("TestMod/game/data/gameplay/major_gods_mods.xml", """
            <civsmods><!-- preserve fixture comment -->
              <civ><name>CheckpointGod</name><key>A</key><culture>Greek</culture>
                <statsid>SP</statsid><displaynameid>STR_FIXTURE_NAME</displaynameid>
                <rollovernameid>STR_FIXTURE_ROLLOVER</rollovernameid><titleid>STR_FIXTURE_TITLE</titleid>
                <icon>fixture_icon</icon><portrait>fixture_portrait</portrait>
                <breakoutportrait>fixture_breakout</breakoutportrait>
                <minimapborderimage>fixture_border</minimapborderimage><backgroundscenario>fixture_scenario</backgroundscenario>
                <agetech age="ArchaicAge"><tech>FixtureAge</tech></agetech><deathmatchtech>FixtureDeathmatch</deathmatchtech>
                <contributor type="CheckpointType" />
                <future preserved="yes" />
              </civ>
            </civsmods>
            """);
        TypePath = Workspace.WriteXml("TestMod/game/data/gameplay/abstract_unit_types_mods.xml",
            "<abstractunittypesmods><abstractunittype>CheckpointType</abstractunittype></abstractunittypesmods>");
        Workspace.WriteXml("TestMod/game/data/gameplay/culture_mods.xml",
            "<culturesmods><culture><name>Greek</name></culture></culturesmods>");
        RecoveryTarget = Workspace.GetPath("recovery.xml");
        File.WriteAllBytes(RecoveryTarget, OriginalBytes);
        GameRoot = Workspace.CreateDirectory("IsolatedGame");
        // Resolve only into the fixture, never the user's settings or installed archive.
        Workspace.WriteText("IsolatedGame/data/Data.bar", "isolated unavailable archive");
    }

    public BaselineHost CreateHost() => CreateHost(selectGod: true);

    public BaselineHost CreateHost(bool selectGod)
    {
        var host = new BaselineHost(new FixtureGameData(GameRoot));
        host.Set("_modFilePath", ProtoPath);
        host.Call("AdoptSavedProtoDocument", XDocument.Load(ProtoPath, LoadOptions.PreserveWhitespace));
        host.Call("EnsureMajorGodView");
        host.God!.SetModifiedMode(true);
        if (selectGod)
            host.God.SelectMajorGod("CheckpointGod");
        return host;
    }

    public void Dispose() => Workspace.Dispose();

    private sealed class FixtureGameData(string root) : IEditorGameDataService
    {
        public string? RootDirectory => root;
        public ValueTask<string?> LookupStringKeyAsync(string key) => ValueTask.FromResult<string?>(null);
    }
}

internal sealed class BaselineHost(IEditorGameDataService gameData)
    : ProtoEditorWindow(gameData, initializeProtoEditor: false)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public MajorGodEditorView? God => (MajorGodEditorView?)typeof(ProtoEditorWindow).GetField("_majorGodView", Private)!.GetValue(this);
    public string? GodStatus => ((TextBlock)typeof(MajorGodEditorView).GetField("_statusMessage", Private)!.GetValue(God)!).Text;
    public void Set(string field, object value) => typeof(ProtoEditorWindow).GetField(field, Private)!.SetValue(this, value);
    public object? Call(string method, params object[] args) => typeof(ProtoEditorWindow).GetMethod(method, Private)!.Invoke(this, args);
    public Task<bool> PreflightAsync() => (Task<bool>)Call("SavePendingChangesBeforeImmediateManagerAsync")!;
    public Task<bool> RenameAsync() => (Task<bool>)Call("RenameUnitTypeAsync", "CheckpointType", "RenamedType")!;
    public void ChangeGodKey()
        => ((TextBox)typeof(MajorGodEditorView).GetField("_keyBox", Private)!.GetValue(God)!).Text = "B";
    public object AddGodTab(bool pinned, bool visible)
    {
        var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
        var tab = Call("CreateDocumentTab", !pinned, kind, true, "CheckpointGod")!;
        tab.GetType().GetProperty("SavedElement")!.SetValue(tab, God!.LoadSavedMajorGodElement("CheckpointGod"));
        ((System.Collections.IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", Private)!.GetValue(this)!).Add(tab);
        if (visible)
        {
            Set("_activeEntityKind", kind);
            Set("_activeDocumentTab", tab);
        }
        else
            GodHost.Content = new TextBlock { Text = "Other editor" };
        return tab;
    }
    public ContentControl GodHost => (ContentControl)typeof(ProtoEditorWindow).GetField("_technologyHost", Private)!.GetValue(this)!;
    public static XElement SavedGod(object tab) => (XElement)tab.GetType().GetProperty("SavedElement")!.GetValue(tab)!;
    public void SetInvalidGodKey()
        => ((TextBox)typeof(MajorGodEditorView).GetField("_keyBox", Private)!.GetValue(God)!).Text = "É";
    public void DetachGodDialogOwner()
    {
        GodHost.Content = null;
        typeof(MajorGodEditorView).GetField("_dialogOwner", Private)!.SetValue(God, null);
    }
    public void DisposeFixtureWindow()
    {
        // Assertions have finished; don't open a user discard dialog during fixture cleanup.
        Set("_allowCloseAfterDiscard", true);
        Close();
    }
}
