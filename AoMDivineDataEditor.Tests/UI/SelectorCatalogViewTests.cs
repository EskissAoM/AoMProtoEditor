using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class SelectorCatalogViewTests : IDisposable
{
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();
    private static T Field<T>(object owner, string name) => (T)
        (owner is BaselineHost ? typeof(ProtoEditorWindow) : owner.GetType())
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static async Task<T> CompleteSave<T>(Window owner, Task<T> save)
    {
        var errors = new List<string>();
        for (var i = 0; !save.IsCompleted && i < 1000; i++)
        {
            foreach (var popup in owner.OwnedWindows.OfType<Prompt>().ToArray())
            { errors.Add(popup.PromptTitle + ": " + popup.PromptText); popup.Close(); }
            await Task.Delay(10);
        }
        var result = await save.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(errors.Count == 0, string.Join("\n", errors)); return result;
    }

    [Fact]
    public void PowerRefreshKeepsRecordCleanAndNewUnitPassesRealSaveValidation()
    {
        using var workspace = new TestWorkspace(nameof(PowerRefreshKeepsRecordCleanAndNewUnitPassesRealSaveValidation));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='P' type='TempUnit'><activetime>1</activetime><placement losprotounit='BaseUnit'>Full</placement><createunit quantity='1'>BaseUnit</createunit><future keep='yes'/></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView(new PowerEditorComposition([], path,
                catalogs: new PowerEditorCatalogs(protoUnitNames: ["BaseUnit", "Removed"], unitTypeNames: ["BaseType"])));
            var owner = new Window { Content = view, Width = 1200, Height = 800 }; owner.Show();
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("P"); Dispatcher.UIThread.RunJobs();
                var box = Field<AutoCompleteBox>(view, "_losProtoUnitBox");
                var before = File.ReadAllBytes(path);
                view.UpdateReferenceCatalogs(new PowerEditorCatalogs(protoUnitNames: ["BaseUnit", "CustomUnit"],
                    unitTypeNames: ["BaseType", "CustomType"], technologyNames: ["CustomTech"]));
                Assert.False(view.IsDirty); Assert.Equal("BaseUnit", box.Text);
                Assert.Contains("CustomUnit", box.ItemsSource!.Cast<string>());
                Assert.DoesNotContain("Removed", box.ItemsSource!.Cast<string>());
                Assert.Equal(before, File.ReadAllBytes(path));
                box.Text = "CustomUnit"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(await CompleteSave(owner, view.SaveAsync()));
                var saved = XDocument.Load(path).Root!.Element("power")!;
                Assert.Equal("CustomUnit", (string?)saved.Element("placement")!.Attribute("losprotounit"));
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            }
            finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void PowerRefreshPreservesDirtyRawXmlExactly()
    {
        using var workspace = new TestWorkspace(nameof(PowerRefreshPreservesDirtyRawXmlExactly));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='P' type='TempUnit'/></powersmod>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView([], path, protoUnitNames: ["BaseUnit"]);
            view.SetModifiedMode(true); await view.SelectPowerAsync("P");
            await PowerXmlTestHelpers.UnlockAsync(view);
            var raw = Field<TextEditor>(view, "_rawXmlEditor"); raw.Text = "<power name='P'><unfinished>";
            Assert.True(view.IsDirty);
            view.UpdateReferenceCatalogs(new PowerEditorCatalogs(protoUnitNames: ["BaseUnit", "CustomUnit"]));
            Assert.Equal("<power name='P'><unfinished>", raw.Text); Assert.True(view.IsDirty);
            Assert.Equal(before, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void MajorGodRefreshPreservesDirtyFormAndNewUnitPassesSaveValidation()
    {
        using var fixture = new BaselineSafetyFixture();
        var document = XDocument.Load(fixture.GodPath);
        document.Root!.Element("civ")!.Add(new XElement("startingunits",
            new XElement("unit", new XAttribute("count", 1), "BaseUnit")));
        document.Save(fixture.GodPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost();
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
            host.Set("_activeEntityKind", kind);
            host.Call("ApplyEntityKindUi", kind);
            host.God!.SetModifiedMode(true); host.God.SelectMajorGod("CheckpointGod");
            host.Show();
            try
            {
                var view = host.God!;
                Field<TabControl>(view, "_majorGodTabs").SelectedIndex = 1;
                host.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var box = Field<StackPanel>(view, "_startingUnitsPanel").GetLogicalDescendants()
                    .OfType<AutoCompleteBox>().Distinct().Single(item => item.Text == "BaseUnit");
                host.ChangeGodKey(); Assert.True(view.IsDirty);
                var before = File.ReadAllBytes(fixture.GodPath);
                view.UpdateReferenceCatalogs(["BaseUnit", "CustomUnit"], ["CheckpointType", "CustomType"],
                    ["FixtureAge", "FixtureDeathmatch", "CustomTech"], [], ["CustomTechType"]);
                Assert.True(view.IsDirty); Assert.Equal("B", Field<TextBox>(view, "_keyBox").Text);
                Assert.Equal("BaseUnit", box.Text); Assert.Contains("CustomUnit", box.ItemsSource!.Cast<string>());
                Assert.Equal(before, File.ReadAllBytes(fixture.GodPath));
                var input = box.GetVisualDescendants().OfType<TextBox>().First();
                input.Text = "CustomUnit";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal("CustomUnit", Field<XElement>(view, "_currentEntry").Element("startingunits")!.Element("unit")!.Value);
                Assert.True(await CompleteSave(host, view.SaveAsync()));
                var saved = XDocument.Load(fixture.GodPath).Root!.Element("civ")!;
                Assert.Equal("CustomUnit", saved.Element("startingunits")!.Element("unit")!.Value);
                Assert.Equal("B", saved.Element("key")!.Value);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void MajorGodRefreshPreservesDirtyRawXmlAndReopeningUsesFreshCatalogs()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(); host.Show();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var god = host.God!; var raw = Field<TextEditor>(god, "_rawXmlEditor");
                await MajorGodXmlTestHelpers.UnlockAsync(god);
                raw.Text = "<civ><unfinished>"; Assert.True(god.IsDirty);
                Field<XElement>(host, "_modXmlRoot").Add(new XElement("unit", new XAttribute("name", "CustomUnit")));
                host.Call("InvalidateSuggestionCaches", true);
                host.Call("EnsureMajorGodView");
                Assert.Same(god, host.God);
                Assert.Equal("<civ><unfinished>", raw.Text); Assert.True(god.IsDirty);
                Assert.Contains("CustomUnit", Field<IReadOnlyList<string>>(god, "_protoUnitNames"));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void RetainedPowerUsesRefreshedHostUnitTypeAndTechnologyCatalogs()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='P' type='TempUnit'/></powersmod>");
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("EnsurePowerView"); var view = Field<PowerEditorView>(host, "_powerView");
                Field<XElement>(host, "_modXmlRoot").Add(new XElement("unit", new XAttribute("name", "CustomUnit")));
                fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='CustomTech'/></techtreemods>");
                File.WriteAllText(fixture.TypePath, "<abstractunittypesmods><abstractunittype>CustomType</abstractunittype></abstractunittypesmods>");
                host.Call("InvalidateSuggestionCaches", true); host.Call("EnsurePowerView");
                Assert.Same(view, Field<PowerEditorView>(host, "_powerView"));
                var catalogs = Field<PowerEditorCatalogs>(view, "_catalogs");
                Assert.Contains("CustomUnit", catalogs.ProtoUnitNames);
                Assert.Contains("CustomType", catalogs.UnitTypeNames);
                Assert.Contains("CustomTech", catalogs.TechnologyNames);
                Assert.DoesNotContain("CheckpointType", catalogs.UnitTypeNames);
                Assert.False(view.IsDirty);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
