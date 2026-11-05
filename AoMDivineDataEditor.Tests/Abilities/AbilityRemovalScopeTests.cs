using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    private static StackPanel CursorTypeHost(BaselineHost child) => child.GetLogicalDescendants().OfType<StackPanel>()
        .Single(panel => panel.IsEffectivelyVisible && panel.Children.OfType<TextBlock>().Any(label => label.Text == "Cursor Type"));

    [Fact]
    public void AbilityCursorTypeRemovalRestoresItsAddActionAndSaveReopensTheCorrectValue()
        => WithStandaloneXml(true, async (fixture, _, child, path, control) =>
        {
            var host = CursorTypeHost(child);
            var remove = Assert.Single(host.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-field"));
            Assert.Equal("×", remove.Content); Assert.Equal(28, remove.Bounds.Height);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            child.Call("UpdateXmlPreview", true);
            Assert.True((bool)Field(child, "_isDirty")!);
            Assert.Null(XElement.Parse(StandaloneRaw(child).Text).Element("cursortype"));
            Assert.NotNull(XElement.Parse(StandaloneRaw(child).Text).Element("abstractplacementtargettype"));
            Assert.Empty(await SaveStandaloneChecked(child, true));
            Assert.Null(XDocument.Load(path).Descendants("cursortype").FirstOrDefault());

            var add = child.GetLogicalDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Cursor Type"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            var cursor = Assert.Single(CursorTypeHost(child).GetLogicalDescendants().OfType<ComboBox>());
            cursor.SelectedItem = "Heal";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(await SaveStandaloneChecked(child, true));
            Assert.Equal("Heal", XDocument.Load(path).Descendants("cursortype").Single().Value);
            Assert.Equal("BaseUnit", XDocument.Load(path).Descendants("abstractplacementtargettype").Single().Value);
            Assert.NotNull(XDocument.Load(path).Descendants("future").Single());

            var reopened = new BaselineHost(new RootGameData(fixture.GameRoot));
            try
            {
                await reopened.InitializeAbilityDefinitionEditorAsync("AbilityA", false, control.Ability!, fixture.ProtoPath,
                    (_, _, _, _) => Task.CompletedTask, ["AbilityA"], () => ["BaseUnit"], () => []);
                reopened.Show(); reopened.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal("Heal", Assert.Single(CursorTypeHost(reopened).GetLogicalDescendants().OfType<ComboBox>()).SelectedItem);
                Assert.False((bool)Field(reopened, "_isDirty")!);
            }
            finally { reopened.DisposeFixtureWindow(); }
        }, abilitySource: AbilityXml.Replace("<radius>", "<cursortype>Heal</cursortype><abstractplacementtargettype>BaseUnit</abstractplacementtargettype><radius>", StringComparison.Ordinal));
}
