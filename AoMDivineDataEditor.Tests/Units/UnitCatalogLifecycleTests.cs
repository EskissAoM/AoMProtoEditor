using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedUnitLifecycleRefreshesRetainedPowerAndDiscardDoesNotLeaveChoices(bool duplicate)
        => WithHost(async (fixture, host) =>
        {
            var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
                "<powersmod><power name='P' type='TempUnit'><activetime>1</activetime><placement losprotounit='A'>Full</placement><abstractplacementtargettype>A</abstractplacementtargettype><future keep='yes'/></power></powersmod>");
            host.Call("EnsurePowerView"); var power = Field<PowerEditorView>(host, "_powerView");
            power.SetModifiedMode(true); await power.SelectPowerAsync("P");
            var box = power.FindControl<AutoCompleteBox>("_losProtoUnitBox")!;
            var chips = power.FindControl<AutoCompleteBox>("_targetTypePicker")!;
            bool Has(string name)
            {
                var available = box.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(available, chips.ItemsSource!.Cast<string>().Contains(name, StringComparer.OrdinalIgnoreCase));
                Assert.DoesNotContain("A", chips.ItemsSource!.Cast<string>()); // Selected chip stays excluded during every refresh.
                return available;
            }
            async Task Add(string name)
            {
                host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
                var creating = (Task)host.Call("AddUnitAsync", duplicate)!;
                var input = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
                typeof(InputPromptWindow).GetProperty(nameof(InputPromptWindow.InputText))!.SetValue(input, name); input.Close();
                if (!duplicate)
                {
                    for (var i = 0; i < 200 && !host.OwnedWindows.OfType<NewUnitTypeWindow>().Any(); i++) await Task.Delay(10);
                    var type = Assert.Single(host.OwnedWindows.OfType<NewUnitTypeWindow>());
                    typeof(NewUnitTypeWindow).GetMethod("OK_Click", Private)!.Invoke(type, new object?[] { null, new RoutedEventArgs() });
                }
                await creating.WaitAsync(TimeSpan.FromSeconds(5)); await RoleSettle(host);
                var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
                Assert.IsType<TextBox>(fields["maxhitpoints"]).Text = "40";
                Assert.IsType<TextBox>(fields["initialhitpoints"]).Text = "40";
            }
            async Task Delete(string title, bool confirm)
            {
                var deleting = (Task)host.Call("DeleteSelectedUnitAsync")!;
                for (var i = 0; i < 200 && !host.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
                var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                Assert.Equal(title, prompt.PromptTitle);
                if (confirm) typeof(Prompt).GetMethod("ConfirmButton_Click", Private)!.Invoke(prompt, [null, new RoutedEventArgs()]);
                else prompt.Close();
                await deleting.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Add("CatalogCreated"); Assert.Empty(await Save(host)); Assert.True(Has("CatalogCreated"));
            box.Text = "CatalogCreated"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            await RoleSettle(host); Assert.True(await power.SaveAsync());
            Assert.Equal("CatalogCreated", (string?)XDocument.Load(path).Descendants("placement").Single().Attribute("losprotounit"));
            box.Text = "A"; box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            await RoleSettle(host); Assert.True(await power.SaveAsync());
            await Unlock(host); var renamed = XElement.Parse(Raw(host).Text); renamed.SetAttributeValue("name", "CatalogRenamed");
            Raw(host).Text = renamed.ToString(); Assert.Empty(await Save(host));
            Assert.False(Has("CatalogCreated")); Assert.True(Has("CatalogRenamed"));
            var beforeCancel = File.ReadAllBytes(fixture.ProtoPath);
            await Delete("Delete Unit", false); Assert.Equal(beforeCancel, File.ReadAllBytes(fixture.ProtoPath)); Assert.True(Has("CatalogRenamed"));
            await Delete("Delete Unit", true); Assert.False(Has("CatalogRenamed"));
            await Add("CatalogAbandoned");
            var beforeDiscard = File.ReadAllBytes(fixture.ProtoPath);
            await Delete("Discard new Unit", true);
            host.Call("EnsurePowerView"); // Actual navigation-entry refresh, not manual cache injection.
            Assert.Same(power, Field<PowerEditorView>(host, "_powerView")); Assert.False(Has("CatalogAbandoned"));
            Assert.Equal(beforeDiscard, File.ReadAllBytes(fixture.ProtoPath)); Assert.False(power.IsDirty);
        });
}
