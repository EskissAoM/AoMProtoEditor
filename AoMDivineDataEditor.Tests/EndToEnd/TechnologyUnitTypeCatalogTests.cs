using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private static string SetUnitTypeTechnology(string type)
        => $"<techtreemods><tech name='A'><effects><effect type='Data' subtype='SetUnitType' unittype='{type}' amount='1' relativity='Assign'><target type='ProtoUnit'>ConcreteOnly</target><future keep='yes'/></effect></effects></tech><tech name='B'/></techtreemods>";

    private static string PrepareUnitTypeCatalogFixture(BaselineSafetyFixture fixture, string type = "CheckpointType")
    {
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='ConcreteOnly'/></protomods>");
        fixture.Workspace.WriteText("IsolatedGame/data/gameplay/abstract_unit_types.xml",
            "<abstractunittypes><abstractunittype>OriginalType</abstractunittype></abstractunittypes>");
        File.WriteAllText(fixture.TypePath,
            "<abstractunittypesmods><abstractunittype>CheckpointType</abstractunittype><abstractunittype>OtherCustomType</abstractunittype></abstractunittypesmods>");
        return fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", SetUnitTypeTechnology(type));
    }

    private static AutoCompleteBox UnitTypeOperand(TechnologyEditorView view, string current)
        => view.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == current);

    private static TechnologyEditorView HostTechnologyView(BaselineHost host)
        => (TechnologyEditorView)typeof(ProtoEditorWindow).GetField("_technologyView",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static async Task CommitUnitTypeOperand(AutoCompleteBox box, string value)
    {
        box.Text = value;
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    [Theory]
    [InlineData("CheckpointType")]
    [InlineData("LegacyLoadedType")]
    public void UnitTypeCatalogSetUnitTypeUsesHostTypesAndSavesCustomSelection(string initial)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = PrepareUnitTypeCatalogFixture(fixture, initial);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind);
                await AwaitTechnologyBuild(view);
                var selector = UnitTypeOperand(view, initial);
                var choices = selector.ItemsSource!.Cast<string>().ToArray();
                Assert.Contains("OriginalType", choices);
                Assert.Contains("CheckpointType", choices);
                Assert.Contains("OtherCustomType", choices);
                Assert.DoesNotContain("ConcreteOnly", choices);
                Assert.Equal(initial, selector.Text);
                Assert.False(view.IsDirty);
                Assert.Equal(before, File.ReadAllBytes(path));

                await CommitUnitTypeOperand(selector, "OtherCustomType");
                Assert.True(view.IsDirty);
                Assert.True(await SaveXmlChecked(view));
                var saved = XDocument.Load(path).Descendants("effect").Single();
                Assert.Equal("OtherCustomType", (string?)saved.Attribute("unittype"));
                Assert.Equal("ConcreteOnly", saved.Element("target")!.Value);
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));

                host.Call("RefreshTechnologyViewAfterTechTypeChanges");
                var reopened = HostTechnologyView(host);
                await AwaitTechnologyBuild(reopened);
                Assert.Equal("OtherCustomType", UnitTypeOperand(reopened, "OtherCustomType").Text);
                Assert.False(reopened.IsDirty);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitTypeCatalogRefreshPreservesCleanFormOrDirtyRawSession(bool dirtyRaw)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = PrepareUnitTypeCatalogFixture(fixture);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind);
                await AwaitTechnologyBuild(view);
                if (dirtyRaw) { await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='A'><unfinished>"; }
                var selector = UnitTypeOperand(view, "CheckpointType");
                var rawBefore = Raw(view).Text;
                // Use the real manager creation callback, then its shared return
                // refresh boundary. No modal or user data is needed in this fixture.
                Assert.True(await (Task<bool>)host.Call("CreateUnitTypeAsync", "CreatedAfterOpening")!);
                host.Call("RefreshRetainedSelectorCatalogs");
                Assert.Same(view, HostTechnologyView(host));
                Assert.Equal(rawBefore, Raw(view).Text);
                Assert.Equal(dirtyRaw, view.IsDirty);
                Assert.Equal(dirtyRaw, !Raw(view).IsReadOnly);
                Assert.Equal("CheckpointType", selector.Text);
                Assert.Contains("CreatedAfterOpening", selector.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("ConcreteOnly", selector.ItemsSource!.Cast<string>());
                Assert.Equal(before, File.ReadAllBytes(path));
                if (!dirtyRaw)
                {
                    await CommitUnitTypeOperand(selector, "CreatedAfterOpening");
                    Assert.True(await SaveXmlChecked(view));
                    Assert.Equal("CreatedAfterOpening", (string?)XDocument.Load(path).Descendants("effect").Single().Attribute("unittype"));
                }
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void UnitTypeCatalogRejectsConcreteUnitWithoutReplacingTheType()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = PrepareUnitTypeCatalogFixture(fixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind);
                await AwaitTechnologyBuild(view);
                var selector = UnitTypeOperand(view, "CheckpointType");
                await CommitUnitTypeOperand(selector, "ConcreteOnly");
                Assert.Equal("CheckpointType", selector.Text);
                Assert.False(view.IsDirty);
                Assert.Equal("CheckpointType", (string?)XDocument.Load(path).Descendants("effect").Single().Attribute("unittype"));
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void UnitTypeCatalogRawXmlCannotUseAConcreteUnitAsTheAssignedType()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = PrepareUnitTypeCatalogFixture(fixture);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new TechnologyEditorView([], null, path,
                prereqUnitNames: ["ConcreteOnly", "CheckpointType", "OtherCustomType"],
                protoUnitNames: ["ConcreteOnly"], unitTypeNames: ["CheckpointType", "OtherCustomType"]);
            await InXmlWindow(view, async owner =>
            {
                await UnlockTechnologyXml(view);
                var xml = XElement.Parse(Raw(view).Text);
                xml.Descendants("effect").Single().SetAttributeValue("unittype", "ConcreteOnly");
                Raw(view).Text = xml.ToString();
                var saving = view.SaveAsync();
                var popup = await XmlPopup(owner);
                Assert.Contains("ConcreteOnly", popup.PromptText, StringComparison.Ordinal);
                Assert.Contains("unknown value", popup.PromptText, StringComparison.OrdinalIgnoreCase);
                popup.Close();
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.True(view.IsDirty);
                Assert.Equal("ConcreteOnly", (string?)XElement.Parse(Raw(view).Text).Descendants("effect").Single().Attribute("unittype"));
            });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitTypeCatalogFormAndXmlSaveFollowedByManagerRenameKeepsTheIndependentUnit(bool rawXml)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = PrepareUnitTypeCatalogFixture(fixture);
        File.WriteAllText(fixture.ProtoPath,
            "<protomods><unit name='ConcreteOnly'/><unit name='OtherCustomType'><displaynameid>OtherCustomTypeSuffix</displaynameid></unit></protomods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind);
                await AwaitTechnologyBuild(view);
                if (rawXml)
                {
                    await UnlockTechnologyXml(view);
                    var xml = XElement.Parse(Raw(view).Text);
                    xml.Descendants("effect").Single().SetAttributeValue("unittype", "OtherCustomType");
                    Raw(view).Text = xml.ToString();
                }
                else await CommitUnitTypeOperand(UnitTypeOperand(view, "CheckpointType"), "OtherCustomType");
                Assert.True(await SaveXmlChecked(view));
                var unitBytes = File.ReadAllBytes(fixture.ProtoPath);
                var rename = (Task<bool>)host.Call("RenameUnitTypeAsync", "OtherCustomType", "RenamedType")!;
                var errors = new List<string>();
                for (var turn = 0; !rename.IsCompleted && turn < 300; turn++)
                {
                    foreach (var popup in host.OwnedWindows.OfType<Prompt>().ToArray())
                    { errors.Add(popup.PromptText); popup.Close(); }
                    if (!rename.IsCompleted) await Task.Delay(10);
                }
                Assert.True(await rename.WaitAsync(TimeSpan.FromSeconds(3)), string.Join("\n", errors));
                var effect = XDocument.Load(path).Descendants("effect").Single();
                Assert.Equal("RenamedType", (string?)effect.Attribute("unittype"));
                Assert.Equal("ConcreteOnly", effect.Element("target")!.Value);
                Assert.Equal("yes", (string?)effect.Element("future")!.Attribute("keep"));
                Assert.True(XNode.DeepEquals(XDocument.Parse(System.Text.Encoding.UTF8.GetString(unitBytes)), XDocument.Load(fixture.ProtoPath)));
                Assert.Contains("OtherCustomType", XDocument.Load(fixture.ProtoPath).Descendants("unit").Select(unit => (string?)unit.Attribute("name")));
                Assert.Contains("RenamedType", UnitTypeCatalog.ExtractDefinitions(XDocument.Load(fixture.TypePath), false).Select(type => type.Name));
                Assert.DoesNotContain("OtherCustomType", UnitTypeCatalog.ExtractDefinitions(XDocument.Load(fixture.TypePath), false).Select(type => type.Name));

                // Manager return refresh/reopen uses the migrated document and
                // the updated type producer, never the stale operand widget.
                host.Call("RefreshTechnologyViewAfterTechTypeChanges");
                host.Call("RefreshRetainedSelectorCatalogs");
                var reopened = HostTechnologyView(host);
                await AwaitTechnologyBuild(reopened);
                var selector = UnitTypeOperand(reopened, "RenamedType");
                Assert.Contains("RenamedType", selector.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("OtherCustomType", selector.ItemsSource!.Cast<string>());
                Assert.False(reopened.IsDirty);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
