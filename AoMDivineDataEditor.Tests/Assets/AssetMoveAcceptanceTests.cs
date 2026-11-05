using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetMoveAcceptanceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)(owner is BaselineHost
        ? typeof(ProtoEditorWindow) : owner.GetType()).GetField(name, Private)!.GetValue(owner)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TechnologyAndPowerDraftsBlockThenMoveSurvivesSaveAndReopen(bool power, bool visible)
    {
        using var fixture = new BaselineSafetyFixture();
        var fileName = power ? "powers_mods.xml" : "techtree_mods.xml";
        var xml = power
            ? "<powersmod><power name='Entry' type='GodPower'><icon>shared.png</icon><future keep='yes'/></power></powersmod>"
            : "<techtreemods><tech name='Entry'><researchpoints>10</researchpoints><status>OBTAINABLE</status><icon>shared.png</icon><future keep='yes'/></tech></techtreemods>";
        var path = fixture.Workspace.WriteXml("TestMod/game/data/gameplay/" + fileName, xml);
        var source = fixture.Workspace.WriteText("TestMod/game/resources/shared.png", "icon bytes");
        var destination = fixture.Workspace.GetPath("TestMod/game/resources/moved.png");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        object Editor() => power ? Field<PowerEditorView>(host, "_powerView") : Field<TechnologyEditorView>(host, "_technologyView");
        void Initialize()
        {
            AvaloniaTestHost.Run(() => host.Call(power ? "EnsurePowerView" : "EnsureTechnologyView"));
            if (power)
            {
                AvaloniaTestHost.Run(() => ((PowerEditorView)Editor()).SetModifiedMode(true));
                AvaloniaTestHost.RunAsync(() => ((PowerEditorView)Editor()).SelectPowerAsync("Entry"));
            }
            else
            {
                AvaloniaTestHost.Run(() => { var editor = (TechnologyEditorView)Editor(); editor.SetModifiedMode(true); editor.SelectTechnology("Entry"); });
                AvaloniaTestHost.RunAsync(() => (Task)typeof(TechnologyEditorView).GetMethod("AwaitActiveEditorBuildCompletionAsync", Private)!.Invoke(Editor(), null)!);
            }
        }
        bool Dirty() => power ? ((PowerEditorView)Editor()).IsDirty : ((TechnologyEditorView)Editor()).IsDirty;
        Task<bool> Save() => power ? ((PowerEditorView)Editor()).SaveAsync() : ((TechnologyEditorView)Editor()).SaveAsync();
        try
        {
            Initialize();
            var saved = new XElement(XDocument.Load(path).Root!.Elements().Single());
            var files = AvaloniaTestHost.Run(() => (IReadOnlyList<TechnologyReferenceFile>)host.Call("GetGameIdentifierReferenceFiles")!);
            var plan = new AssetMovePlan(source, destination, files, AssetReferenceKind.Icon, "shared.png", "moved.png");
            AvaloniaTestHost.Run(() =>
            {
                if (power) Field<TextBox>(Editor(), "_nameBox").Text = "DraftName";
                else Field<StackPanel>(Editor(), "_propertiesPanel").GetLogicalDescendants()
                    .OfType<TextBox>().Single(box => box.Text == "10").Text = "11";
            });
            Assert.True(AvaloniaTestHost.Run(Dirty));
            var error = Assert.Throws<TargetInvocationException>(() => AvaloniaTestHost.Run(() => host.Call("CommitAssetMove", plan)));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(destination));
            Assert.Equal("shared.png", XDocument.Load(path).Descendants("icon").Single().Value);
            Assert.True(AvaloniaTestHost.Run(Dirty));
            // The clean-adoption scenario starts with a fresh host; discard/rename lifecycle
            // is separate from asset migration and must not become test setup for it.
            AvaloniaTestHost.Run(host.DisposeFixtureWindow);
            host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
            Initialize();
            var tab = AvaloniaTestHost.Run(() =>
            {
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, power ? "Powers" : "Technologies");
                var tab = host.Call("CreateDocumentTab", false, kind, true, "Entry")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, saved);
                Field<IList>(host, "_openDocumentTabs").Add(tab);
                if (visible) { host.Set("_activeEntityKind", kind); host.Set("_activeDocumentTab", tab); }
                host.GodHost.Content = visible ? Editor() : new TextBlock { Text = "Other editor" };
                return tab;
            });
            var previousContent = AvaloniaTestHost.Run(() => host.GodHost.Content);
            Assert.False(AvaloniaTestHost.Run(Dirty), AvaloniaTestHost.Run(() => power ? "Power unexpectedly dirty" :
                "Current technologies: " + string.Join(";", Field<Dictionary<string, XElement>>(Editor(), "_modified").Values) +
                " Saved: " + string.Join(";", Field<Dictionary<string, string>>(Editor(), "_savedModifiedTechnologyXml").Values)));
            Assert.False(AvaloniaTestHost.Run(() => (bool)host.Call("HasPendingAssetMoveChanges")!),
                AvaloniaTestHost.Run(() => $"UnitDirty={Field<bool>(host, "_isDirty")}; dirtyUnits={Field<HashSet<string>>(host, "_dirtyUnitNames").Count}; GodDirty={host.God?.IsDirty}; editorDirty={Dirty()}"));
            AvaloniaTestHost.Run(() => { host.Call("CommitAssetMove", plan); host.Call("AdoptAssetMove", plan); });
            AvaloniaTestHost.Run(() =>
            {
                Assert.Same(visible ? Editor() : previousContent, host.GodHost.Content);
                Assert.Equal("Entry", power ? ((PowerEditorView)Editor()).CurrentPowerName : ((TechnologyEditorView)Editor()).CurrentTechnologyName);
                Assert.Equal("moved.png", BaselineHost.SavedGod(tab).Element("icon")!.Value);
                Assert.False(Dirty());
            });
            Assert.True(AvaloniaTestHost.RunAsync(Save));
            Assert.Equal("moved.png", XDocument.Load(path).Descendants("icon").Single().Value);
            Assert.Equal("yes", (string?)XDocument.Load(path).Descendants("future").Single().Attribute("keep"));
            AvaloniaTestHost.Run(host.DisposeFixtureWindow);
            host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
            Initialize();
            Assert.False(AvaloniaTestHost.Run(Dirty));
            Assert.True(AvaloniaTestHost.RunAsync(Save));
            Assert.Equal("moved.png", XDocument.Load(path).Descendants("icon").Single().Value);
            Assert.Equal("icon bytes", File.ReadAllText(destination));
            Assert.False(File.Exists(source));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); AvaloniaTestHost.CollectUiGarbage(); }
    }

    [Theory]
    [InlineData(AssetReferenceKind.Animation, "animfile", "art")]
    [InlineData(AssetReferenceKind.SoundSet, "soundsetfile", "sound")]
    public void XmlAssetsMoveWithoutChangingTheirBytes(AssetReferenceKind kind, string tag, string folder)
    {
        using var workspace = new TestWorkspace("asset-xml-acceptance");
        var source = workspace.WriteXml(folder + "/old.xml", "<asset><!--preserve--><future value='1'/></asset>");
        var original = File.ReadAllBytes(source);
        var destination = workspace.GetPath(folder + "/new.xml");
        var proto = workspace.WriteXml("proto_mods.xml", $"<protomods><unit name='U'><{tag} culture='Greek'>old.xml</{tag}></unit></protomods>");
        var plan = new AssetMovePlan(source, destination, ModReferenceFileCatalog.Discover(workspace.RootPath), kind, "old.xml", "new.xml");
        plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (document, path) => PreservedXmlFile.SaveAtomic(document, path));
        Assert.False(File.Exists(source));
        Assert.Equal(original, File.ReadAllBytes(destination));
        var element = XDocument.Load(proto).Descendants(tag).Single();
        Assert.Equal("new.xml", element.Value);
        Assert.Equal("Greek", (string?)element.Attribute("culture"));
    }

    [Fact]
    public void SourceDocumentRequiringMigrationRefusesWithoutWrites()
    {
        using var workspace = new TestWorkspace("asset-self-reference");
        var source = workspace.WriteXml("old.xml", "<protomods><unit><animfile>old.xml</animfile></unit></protomods>");
        var original = File.ReadAllBytes(source);
        var destination = workspace.GetPath("new.xml");
        var error = Assert.Throws<InvalidOperationException>(() => new AssetMovePlan(source, destination,
            ModReferenceFileCatalog.Discover(workspace.RootPath), AssetReferenceKind.Animation, "old.xml", "new.xml"));
        Assert.Contains("asset itself", error.Message);
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.False(File.Exists(destination));
    }
}
