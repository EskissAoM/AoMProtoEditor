using System.Collections;
using System.Xml.Linq;
using Avalonia;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static object RemovalAction(BaselineHost host) => Assert.Single(Field<IList>(host, "_protoActionWidgets").Cast<object>());
    private static IList RemovalRows(object action, string name) => (IList)action.GetType().GetProperty(name)!.GetValue(action)!;
    private static async Task WaitRemovalReady(BaselineHost host)
    {
        for (var i = 0; i < 200 && (Field<bool>(host, "_isPopulating") || Field<bool>(host, "_suppressEditorChangeTracking")); i++)
            await Task.Delay(10);
        Assert.False(Field<bool>(host, "_isPopulating"));
        Assert.False(Field<bool>(host, "_suppressEditorChangeTracking"));
    }

    [Fact]
    public void UnitBonusRowAndOnHitCardRemovalSaveOnlyTheirOwnScope()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host);
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 1;
            await WaitRemovalReady(host);
            host.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var action = RemovalAction(host);
            var bonus = Assert.Single(RemovalRows(action, "BonusRows").Cast<object>());
            var row = (Panel)bonus.GetType().GetProperty("RowPanel")!.GetValue(bonus)!;
            var remove = Assert.Single(row.Children.OfType<Button>());
            var value = (TextBox)bonus.GetType().GetProperty("ValTb")!.GetValue(bonus)!;
            var selector = (AutoCompleteBox)bonus.GetType().GetProperty("TypeAcb")!.GetValue(bonus)!;
            Assert.Equal(80, value.Bounds.Width); Assert.Equal(30, value.Bounds.Height);
            Assert.Equal(30, selector.Bounds.Height);
            var valuePoint = value.TranslatePoint(default, host)!.Value;
            var crossPoint = remove.TranslatePoint(default, host)!.Value;
            Assert.InRange(crossPoint.X - valuePoint.X - value.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(crossPoint.Y + remove.Bounds.Height / 2 - valuePoint.Y - value.Bounds.Height / 2), 0, .5);
            var add = (Button)action.GetType().GetProperty("AddDamageBonusButton")!.GetValue(action)!;
            Assert.Equal(30, add.Bounds.Height);
            var addDamage = (Button)action.GetType().GetProperty("AddDamageButton")!.GetValue(action)!;
            Assert.Equal(30, addDamage.Bounds.Height);
            var selectorPoint = selector.TranslatePoint(default, host)!.Value;
            var addPoint = add.TranslatePoint(default, host)!.Value;
            Assert.InRange(Math.Abs(selectorPoint.Y - valuePoint.Y), 0, .5);
            Assert.InRange(Math.Abs(addPoint.Y - selectorPoint.Y), 0, .5);
            Assert.InRange(Math.Abs(crossPoint.Y + remove.Bounds.Height / 2 - selectorPoint.Y - selector.Bounds.Height / 2), 0, .5);
            Assert.Contains("remove-row", remove.Classes);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(RemovalRows(action, "BonusRows").Cast<object>());
            Assert.Single(RemovalRows(action, "OnHitEffectRows").Cast<object>());
            Assert.True(Field<bool>(host, "_isDirty")); Assert.Empty(await Save(host));
            Assert.Empty(XDocument.Load(fixture.ProtoPath).Descendants("damagebonus"));
            Assert.Equal("Stun", (string?)XDocument.Load(fixture.ProtoPath).Descendants("onhiteffect").Single().Attribute("type"));
            await WaitRemovalReady(host);
            action = RemovalAction(host);
            var effect = Assert.Single(RemovalRows(action, "OnHitEffectRows").Cast<object>());
            var card = (OnHitEffectEditor)effect.GetType().GetProperty("Card")!.GetValue(effect)!;
            Assert.Contains("remove-card", card.RemoveButton!.Classes);
            card.RemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(RemovalRows(action, "OnHitEffectRows").Cast<object>());
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            Assert.Empty(saved.Descendants("onhiteffect")); Assert.Empty(saved.Descendants("damagebonus"));
            Assert.Equal("3", saved.Descendants("rate").Single().Value);
            Assert.Equal("20", saved.Root!.Elements("unit").Last().Element("maxhitpoints")!.Value);
            host.Call("BuildEditorPanel", "B", false); host.Call("BuildEditorPanel", "A", false);
            Assert.Empty(RemovalRows(RemovalAction(host), "OnHitEffectRows").Cast<object>());
            Assert.False(Field<bool>(host, "_isDirty"));
        }, Initial.Replace("<unittype>Unit</unittype>",
            "<unittype>Unit</unittype><protoaction><name>HandAttack</name><type>Attack</type><rate type='All'>3</rate><damagebonus type='B'>2</damagebonus><onhiteffect type='Stun' duration='2'/></protoaction>", StringComparison.Ordinal));
}
