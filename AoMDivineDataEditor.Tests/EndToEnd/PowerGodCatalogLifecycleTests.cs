using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void PowerAndGodMutationsRefreshRetainedTechnologyWithoutPublishingUnsavedEntries(bool powers, bool duplicate)
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='P' type='TempUnit'><activetime>1</activetime></power></powersmod>");
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><culture>Greek</culture></civ></civsmods>");
        var techPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", powers
            ? "<techtreemods><tech name='A'><effects><effect type='CreatePower' protoPower='P'/></effects></tech></techtreemods>"
            : "<techtreemods><tech name='A'><prereqs><civilization><civName>CheckpointGod</civName></civilization></prereqs></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, techKind) = AttachXmlTechnologyHost(host); await AwaitTechnologyBuild(view);
                var original = powers ? "P" : "CheckpointGod";
                AutoCompleteBox Selector() => view.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.Text == original);
                var selector = Selector(); await UnlockTechnologyXml(view); Raw(view).Text += "<!-- pending catalog consumer -->";
                var addGod = powers ? null : view.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Add major god"));
                if (addGod != null) Assert.False(addGod.IsEnabled);
                var raw = Raw(view).Text; var bytes = File.ReadAllBytes(techPath);
                var kind = Enum.Parse(techKind.GetType(), powers ? "Powers" : "MajorGods"); host.Call("ApplyEntityKindUi", kind);
                host.Call(powers ? "EnsurePowerView" : "EnsureMajorGodView");
                var power = powers ? XmlHostField<PowerEditorView>(host, "_powerView") : null;
                var god = powers ? null : host.God;
                if (powers) { power!.SetModifiedMode(true); await power.SelectPowerAsync(original); }
                else { god!.SetModifiedMode(true); god.SelectMajorGod(original); }
                Task<bool> Save() => powers ? power!.SaveAsync() : god!.SaveAsync();
                void Has(string name, bool expected)
                {
                    Assert.Same(view, HostTechnologyView(host)); Assert.Equal(raw, Raw(view).Text); Assert.True(view.IsDirty);
                    Assert.Equal(bytes, File.ReadAllBytes(techPath));
                    Assert.Equal(expected, selector.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase));
                    if (addGod != null && expected) Assert.True(addGod.IsEnabled);
                }
                if (powers) await power!.AddPowerAsync(duplicate); else await god!.AddMajorGodAsync(duplicate);
                var created = powers ? power!.CurrentPowerName! : god!.CurrentMajorGodName!;
                Has(created, false);
                if (powers && !duplicate) power!.FindControl<AutoCompleteBox>("_powerTypeBox")!.SelectedItem = "TempUnit";
                Assert.True(await Save().WaitAsync(TimeSpan.FromSeconds(5))); Has(created, true);
                // Exercise the actual raw-identity save route, retaining a foreign raw draft that is not affected.
                if (powers) await PowerXmlTestHelpers.UnlockAsync(power!); else await MajorGodXmlTestHelpers.UnlockAsync(god!);
                var producerRaw = powers ? power!.FindControl<AvaloniaEdit.TextEditor>("_rawXmlEditor")! : god!.FindControl<AvaloniaEdit.TextEditor>("_rawXmlEditor")!;
                var entry = XElement.Parse(producerRaw.Text); var renamed = created + "Renamed";
                if (powers) entry.SetAttributeValue("name", renamed); else entry.Element("name")!.Value = renamed;
                producerRaw.Text = entry.ToString(); Assert.True(await Save().WaitAsync(TimeSpan.FromSeconds(5)));
                Has(created, false); Has(renamed, true);
                var deleting = powers ? power!.DeleteAndSaveCurrentPowerAsync() : god!.DeleteAndSaveCurrentMajorGodAsync(host);
                var prompt = await XmlPopup(host); Assert.True(prompt.PromptIsConfirm, prompt.PromptText);
                typeof(Prompt).GetMethod("ConfirmButton_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(prompt, [null, new RoutedEventArgs()]);
                await deleting.WaitAsync(TimeSpan.FromSeconds(5)); Has(renamed, false);
                if (addGod != null) Assert.False(addGod.IsEnabled);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
