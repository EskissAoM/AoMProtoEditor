using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    [Fact]
    public void NativeReviewManagedAbilityAdoptsCommittedBaselineButStillRejectsExternalEdits()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            new XElement("powersmod", XElement.Parse(AbilityXml)).ToString());
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            AbilityEditorWindow? editor = null;
            try
            {
                owner.Show();
                ((Dictionary<string, XElement>)Field(owner, "_abilityPowerCatalog")!)["AbilityA"] = XElement.Parse(AbilityXml);
                var opening = (Task<string?>)owner.Call("OpenManagedAbilityEditorAsync", owner, "AbilityA", false)!;
                for (var i = 0; i < 300 && editor == null; i++)
                {
                    editor = owner.OwnedWindows.OfType<AbilityEditorWindow>().FirstOrDefault();
                    if (editor == null) await Task.Delay(10);
                }
                Assert.NotNull(editor);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                editor.UpdateLayout();
                var radius = Assert.Single(editor.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == "10");
                async Task<List<string>> SaveManaged()
                {
                    var errors = new List<string>();
                    var saving = (Task)typeof(ProtoEditorWindow).GetMethod("SaveAbilityDefinitionEditorAsync", Private)!.Invoke(editor, null)!;
                    for (var i = 0; i < 500 && !saving.IsCompleted; i++)
                    {
                        foreach (var prompt in editor.OwnedWindows.OfType<Prompt>().ToArray())
                        { errors.Add(prompt.PromptText); prompt.Close(); }
                        await Task.Delay(10);
                    }
                    await saving.WaitAsync(TimeSpan.FromSeconds(5));
                    return errors;
                }
                radius.Text = "11";
                Assert.Empty(await SaveManaged());
                Assert.Equal("11", XDocument.Load(path).Descendants("radius").Single().Value);
                Assert.Equal("remove", (string?)XDocument.Load(path).Descendants("placement").Single().Attribute("FutureAttribute"));
                var range = Assert.Single(editor.GetLogicalDescendants().OfType<Button>(), button => button.Content as string == "Range Indicator");
                range.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(editor.GetLogicalDescendants().OfType<ComboBox>(), box => box.ItemsSource?.Cast<object>().Contains("Circle") == true).SelectedItem = "Circle";
                var rangeValue = Assert.IsType<TextBox>(typeof(ProtoEditorWindow).GetField("_abilityRangeIndicatorRangeEditorForValidation", Private)!.GetValue(editor));
                rangeValue.Text = "";
                var rangeErrors = await SaveManaged();
                Assert.Contains(rangeErrors, message => message.Contains("range", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(rangeErrors, message => message.Contains("changed after", StringComparison.OrdinalIgnoreCase));
                rangeValue.Text = "2";
                Assert.Empty(await SaveManaged());
                // A second structured save must compare against the preserved, committed XML.
                radius = Assert.Single(editor.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == "11");
                radius.Text = "12";
                Assert.Empty(await SaveManaged());
                Assert.Equal("12", XDocument.Load(path).Descendants("radius").Single().Value);
                var external = XDocument.Load(path);
                external.Descendants("power").Single().SetAttributeValue("external", "keep"); external.Save(path);
                var before = File.ReadAllBytes(path);
                radius = Assert.Single(editor.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == "12");
                radius.Text = "13";
                Assert.Contains(await SaveManaged(), message => message.Contains("changed after this editor was opened", StringComparison.Ordinal));
                Assert.Equal(before, File.ReadAllBytes(path));
                typeof(ProtoEditorWindow).GetField("_allowCloseAfterDiscard", Private)!.SetValue(editor, true);
                editor.Close(); await opening.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                if (editor != null)
                {
                    foreach (var popup in editor.OwnedWindows.ToArray()) popup.Close();
                    typeof(ProtoEditorWindow).GetField("_allowCloseAfterDiscard", Private)!.SetValue(editor, true);
                    editor.Close();
                }
                owner.DisposeFixtureWindow();
            }
        });
    }
}
