using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalActionCatalogIncludesLooseAndCompiledTacticsAndRefreshesExistingSelector(bool dirtyRaw)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='ConcreteOnly'><protoaction><name>ProtoKnown</name><type>Attack</type></protoaction></unit></protomods>");
        fixture.Workspace.WriteText("IsolatedGame/data/gameplay/tactics/base.tactics", "<tactics><action><name>BaseTacticsOnly</name><type>Attack</type></action><tactic>BaseMode</tactic></tactics>");
        var custom = fixture.Workspace.WriteText("TestMod/game/data/gameplay/tactics/custom.tactics", "<tactics><action><name>CustomTacticsOnly</name><type>Attack</type></action><tactic>CustomMode</tactic></tactics>");
        var compiled = fixture.Workspace.WriteText("TestMod/game/data/gameplay/tactics/compiled.tactics.xmb", "");
        File.WriteAllBytes(compiled, XmbReaderTests.BuildDocument(XElement.Parse("<tactics><action><name>CompiledOnly</name><type>Attack</type></action><tactic>CompiledMode</tactic></tactics>")));
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='A'><effects><effect type='Data' subtype='ActionEnable' action='ProtoKnown' amount='1' relativity='Absolute'><target type='ProtoUnit'>ConcreteOnly</target><future keep='yes'/></effect></effects></tech></techtreemods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host); host.Call("ApplyEntityKindUi", kind); await AwaitTechnologyBuild(view);
                var selector = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == "ProtoKnown");
                var names = selector.ItemsSource!.Cast<string>().ToArray();
                Assert.Contains("BaseTacticsOnly", names); Assert.Contains("CustomTacticsOnly", names); Assert.Contains("CompiledOnly", names);
                Assert.DoesNotContain("CustomMode", names); // Internal Tactic names are another domain.
                Assert.Empty((List<string>)host.Call("GetCurrentUnitProtoActionNames")!); // Global choices must not widen a local picker.
                var modes = (IReadOnlyList<string>)host.Call("GetTechnologyTacticDefinitionNames")!;
                Assert.Contains("CustomMode", modes); Assert.Same(modes, host.Call("GetTechnologyTacticDefinitionNames"));
                await UnlockTechnologyXml(view);
                if (dirtyRaw) Raw(view).Text += "<!-- pending -->";
                var raw = Raw(view).Text;
                File.WriteAllText(custom, "<tactics><action><name>ReplacementAction</name><type>Attack</type></action><tactic>ReplacementMode</tactic></tactics>");
                // Tactics edit/manager completion invalidates this shared source identity.
                host.Call("RefreshEditorsAfterTacticsManager");
                Assert.Same(view, HostTechnologyView(host)); Assert.Equal(raw, Raw(view).Text);
                Assert.Equal(dirtyRaw, view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Contains("ReplacementAction", selector.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("CustomTacticsOnly", selector.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("CustomMode", (IReadOnlyList<string>)host.Call("GetTechnologyTacticDefinitionNames")!);
                if (!dirtyRaw)
                {
                    Assert.True(await SaveXmlChecked(view)); // Close the clean XML session before form editing.
                    selector = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == "ProtoKnown");
                    await CommitUnitTypeOperand(selector, "ReplacementAction"); Assert.True(await SaveXmlChecked(view));
                    Assert.Equal("ReplacementAction", (string?)XDocument.Load(path).Descendants("effect").Single().Attribute("action"));
                }
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void ModSwitchDropsRetainedViewsAndAllModSpecificCatalogMembers()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/tactics/a.tactics", "<tactics><action><name>OnlyModAAction</name><type>Attack</type></action></tactics>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='OnlyModATech'/></techtreemods>");
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='OnlyModAUnit'/></protomods>");
        var other = fixture.Workspace.WriteText("OtherMod/game/data/gameplay/proto_mods.xml", "<protomods><unit name='OnlyModBUnit'/></protomods>");
        fixture.Workspace.WriteText("OtherMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='OnlyModBTech'/></techtreemods>");
        fixture.Workspace.WriteText("OtherMod/game/data/gameplay/tactics/b.tactics", "<tactics><action><name>OnlyModBAction</name><type>Attack</type></action></tactics>");
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                // Keep preference writes isolated; exercise the actual loader/reset used by Switch Mod.
                host.Set("_rememberModSettings", new Func<string, ProtoEditorSettingsSaveResult>(path => new(ProtoEditorSettingsSaveState.Unchanged, path, null, null, [])));
                host.Call("InitializeDocumentTabs"); host.Call("EnsureTechnologyView"); host.Call("EnsurePowerView");
                var oldTech = HostTechnologyView(host); var oldPower = XmlHostField<AoMDivineDataEditor.Windows.PowerEditorView>(host, "_powerView");
                Assert.Contains("OnlyModAAction", (IReadOnlyList<string>)host.Call("GetTechnologyProtoActionNames")!);
                Assert.Contains("OnlyModATech", (List<string>)host.Call("GetAvailableTechNames")!);
                Assert.True((bool)host.Call("TryLoadModFile", other)!); host.Call("ResetEntityBrowserToUnits");
                host.Call("EnsureTechnologyView"); host.Call("EnsurePowerView");
                Assert.NotSame(oldTech, HostTechnologyView(host)); Assert.NotSame(oldPower, XmlHostField<AoMDivineDataEditor.Windows.PowerEditorView>(host, "_powerView"));
                Assert.Equal(new[] { "OnlyModBTech" }, (List<string>)host.Call("GetAvailableTechNames")!);
                var units = (IReadOnlyList<string>)host.Call("GetTechnologyProtoUnitNames")!;
                Assert.Contains("OnlyModBUnit", units); Assert.DoesNotContain("OnlyModAUnit", units);
                var actions = (IReadOnlyList<string>)host.Call("GetTechnologyProtoActionNames")!;
                Assert.Contains("OnlyModBAction", actions); Assert.DoesNotContain("OnlyModAAction", actions);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
