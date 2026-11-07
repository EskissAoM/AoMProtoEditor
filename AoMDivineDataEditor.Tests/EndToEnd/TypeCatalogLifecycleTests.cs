using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(true, "clean", true)]
    [InlineData(true, "form", true)]
    [InlineData(true, "raw", true)]
    [InlineData(false, "clean", true)]
    [InlineData(false, "form", true)]
    [InlineData(false, "raw", true)]
    [InlineData(true, "clean", false)]
    [InlineData(true, "form", false)]
    [InlineData(true, "raw", false)]
    [InlineData(false, "clean", false)]
    [InlineData(false, "form", false)]
    [InlineData(false, "raw", false)]
    public void RealTypeManagerMutationsRefreshRetainedSelectorsAndSaveMembership(bool unitTypes, string draft, bool visible)
    {
        using var fixture = new BaselineSafetyFixture();
        var technologyPath = PrepareUnitTypeCatalogFixture(fixture);
        var typePath = unitTypes ? fixture.TypePath : fixture.Workspace.GetPath("TestMod/game/data/gameplay/tech_types_mods.xml");
        if (!unitTypes)
        {
            var types = new XDocument(new XElement(TechTypeCatalog.ModRootName));
            TechTypeCatalog.AddDefinition(types, "CheckpointType"); TechTypeCatalog.AddDefinition(types, "OtherCustomType");
            types.Save(typePath);
            File.WriteAllText(technologyPath, "<techtreemods><tech name='A'><effects><effect type='Data' subtype='ResearchPoints' amount='1' relativity='Absolute'><target type='TechType'>CheckpointType</target><future keep='yes'/></effect></effects></tech><tech name='B'/></techtreemods>");
        }
        var originalBytes = File.ReadAllBytes(technologyPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                await AwaitTechnologyBuild(view);
                AutoCompleteBox Operand(string current) => view.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == current);
                var selector = Operand("CheckpointType");
                if (draft == "form") await CommitUnitTypeOperand(selector, "OtherCustomType");
                else
                {
                    // Even a clean unlocked session must survive the manager return.
                    await UnlockTechnologyXml(view);
                    if (draft == "raw") Raw(view).Text += "<!-- pending raw edit -->";
                }
                var raw = Raw(view).Text; var selected = selector.Text; var editable = !Raw(view).IsReadOnly;
                if (!visible)
                {
                    host.Set("_activeEntityKind", Enum.Parse(kind.GetType(), "Units"));
                    view.IsVisible = false;
                }
                Assert.Equal(draft != "clean", view.IsDirty);
                var suffix = unitTypes ? "UnitTypeAsync" : "TechTypeAsync";
                async Task<bool> MutateExpected(bool expected, string verb, params object[] arguments)
                {
                    var pending = (Task<bool>)host.Call(verb + suffix, arguments)!;
                    var errors = new List<string>();
                    for (var i = 0; !pending.IsCompleted && i < 300; i++)
                    {
                        foreach (var popup in host.OwnedWindows.OfType<Prompt>().ToArray()) { errors.Add(popup.PromptText); popup.Close(); }
                        if (!pending.IsCompleted) await Task.Delay(10);
                    }
                    var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
                    Assert.True(result == expected, string.Join("\n", errors));
                    host.Call("RefreshEditorsAfterTypeManager", unitTypes);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Same(view, HostTechnologyView(host));
                    Assert.Equal(raw, Raw(view).Text); Assert.Equal(selected, selector.Text);
                    Assert.Equal(editable, !Raw(view).IsReadOnly); Assert.Equal(draft != "clean", view.IsDirty);
                    Assert.Equal(originalBytes, File.ReadAllBytes(technologyPath));
                    return result;
                }
                Task<bool> Mutate(string verb, params object[] arguments) => MutateExpected(true, verb, arguments);
                void Has(string name, bool expected)
                    => Assert.Equal(expected, selector.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase));
                Has("FreshType", false);
                await Mutate("Create", "FreshType"); Has("FreshType", true);
                var typeBytes = File.ReadAllBytes(typePath);
                await MutateExpected(false, "Create", "FreshType"); // Refused duplicate must not publish changes.
                Assert.Equal(typeBytes, File.ReadAllBytes(typePath)); Has("FreshType", true);
                if (unitTypes)
                {
                    await Mutate("Duplicate", "FreshType", false, "CopiedType"); Has("CopiedType", true);
                    await Mutate("Delete", "CopiedType"); Has("CopiedType", false);
                }
                await Mutate("Rename", "FreshType", "RenamedType"); Has("FreshType", false); Has("RenamedType", true);
                await Mutate("Delete", "RenamedType"); Has("RenamedType", false);
                await Mutate("Create", "FinalType"); Has("FinalType", true);
                Assert.Contains("FinalType", File.ReadAllText(typePath));
                if (draft != "raw")
                {
                    host.Set("_activeEntityKind", kind); view.IsVisible = true;
                    if (draft == "clean") Assert.True(await SaveXmlChecked(view)); // Relock the clean raw session.
                    var current = draft == "form" ? "OtherCustomType" : "CheckpointType";
                    await CommitUnitTypeOperand(Operand(current), "FinalType");
                    Assert.True(await SaveXmlChecked(view));
                    var effect = XDocument.Load(technologyPath).Descendants("effect").Single();
                    Assert.Equal("FinalType", unitTypes ? (string?)effect.Attribute("unittype") : effect.Element("target")!.Value);
                    Assert.Equal("yes", (string?)effect.Element("future")!.Attribute("keep"));
                }
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
