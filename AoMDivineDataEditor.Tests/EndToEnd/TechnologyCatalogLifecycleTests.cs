using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealTechnologyLifecyclePublishesOnlySuccessfulSavesToRetainedConsumers(bool duplicate)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'><researchpoints>1</researchpoints><future keep='yes'/></tech></techtreemods>");
        var powerPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            "<powersmod><power name='P' type='UnderworldInvasion'><activetime>1</activetime><durationtecheffect>A</durationtecheffect><future keep='yes'/></power></powersmod>");
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><teamtech>A</teamtech><future keep='yes'/></civ></civsmods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host); await AwaitTechnologyBuild(view);
                host.Call("EnsurePowerView"); var power = XmlHostField<PowerEditorView>(host, "_powerView");
                power.SetModifiedMode(true); await power.SelectPowerAsync("P");
                var box = power.FindControl<AutoCompleteBox>("_underworldTechEffectBox")!;
                host.God!.SetModifiedMode(true); host.God.SelectMajorGod("CheckpointGod");
                var godBox = host.God.FindControl<AutoCompleteBox>("_teamTechBox")!;
                void Has(string name, bool expected)
                {
                    Assert.Equal(expected, box.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase));
                    Assert.Equal(expected, godBox.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase));
                }
                async Task Add(string? name)
                {
                    host.Call("ApplyEntityKindUi", kind);
                    view = HostTechnologyView(host); host.GodHost.Content = view;
                    view.SelectTechnology("A"); await AwaitTechnologyBuild(view);
                    host.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Same(host, TopLevel.GetTopLevel(view));
                    var adding = view.AddTechnologyAsync(duplicate);
                    var input = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
                    if (name != null) typeof(InputPromptWindow).GetProperty(nameof(InputPromptWindow.InputText))!.SetValue(input, name);
                    input.Close(); await adding.WaitAsync(TimeSpan.FromSeconds(5)); await AwaitTechnologyBuild(view);
                }
                var original = File.ReadAllBytes(path);
                await Add(null); Assert.Equal(original, File.ReadAllBytes(path)); Has("CatalogTech", false);
                await Add("CatalogTech"); Has("CatalogTech", false);
                await UnlockTechnologyXml(view, "CatalogTech"); var valid = Raw(view).Text; Raw(view).Text = "<tech name='CatalogTech'><unfinished>";
                var failing = view.SaveAsync(); var popup = await XmlPopup(host); popup.Close();
                Assert.False(await failing); Assert.Equal(original, File.ReadAllBytes(path)); Has("CatalogTech", false);
                Raw(view).Text = valid; Assert.True(await SaveXmlChecked(view)); Has("CatalogTech", true);
                Assert.Same(power, XmlHostField<PowerEditorView>(host, "_powerView"));
                box.Text = "CatalogTech"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Assert.True(await power.SaveAsync());
                Assert.Equal("CatalogTech", XDocument.Load(powerPath).Descendants("durationtecheffect").Single().Value);
                box.Text = "A"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent)); Assert.True(await power.SaveAsync());
                view = HostTechnologyView(host); host.GodHost.Content = view; await AwaitTechnologyBuild(view);
                await UnlockTechnologyXml(view, "CatalogTech"); var renamed = XElement.Parse(Raw(view).Text); renamed.SetAttributeValue("name", "CatalogRenamedTech");
                Raw(view).Text = renamed.ToString(); Assert.True(await SaveXmlChecked(view));
                Has("CatalogTech", false); Has("CatalogRenamedTech", true);
                var deleting = view.DeleteTechnologyAsync(); var confirm = await XmlPopup(host);
                Assert.True(confirm.PromptIsConfirm, confirm.PromptText);
                typeof(Prompt).GetMethod("ConfirmButton_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(confirm, [null, new RoutedEventArgs()]);
                await deleting.WaitAsync(TimeSpan.FromSeconds(5)); Has("CatalogRenamedTech", false);
                await Add("CatalogAbandonedTech"); view.DiscardTechnologyChanges("CatalogAbandonedTech", null);
                Has("CatalogAbandonedTech", false); Assert.DoesNotContain("CatalogAbandonedTech", File.ReadAllText(path));
                Assert.Equal("A", box.Text); Assert.Equal("A", godBox.Text); Assert.False(power.IsDirty); Assert.False(host.God.IsDirty);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
