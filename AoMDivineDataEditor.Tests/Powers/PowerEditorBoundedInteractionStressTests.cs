using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorBoundedInteractionStressTests : IDisposable
{
    private const int CycleCount = 8;

    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    [Trait("Category", TestCategories.Ui)]
    public void RepresentativeDynamicInteractionsRemainStableAcrossBoundedCycles()
    {
        using var workspace = new TestWorkspace(
            nameof(RepresentativeDynamicInteractionsRemainStableAcrossBoundedCycles));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="RainStress" type="Rain" />
              <power name="AreaStress" type="EffectInArea"><areavfx>AreaVfx</areavfx></power>
              <power name="ModifyStress" type="UnitModify" />
              <power name="SwitchStress" type="SwitchingEffects">
                <tiertype>Time</tiertype><progresstype>Default</progresstype>
              </power>
            </powersmod>
            """);

        AvaloniaTestHost.Run(() =>
        {
            var editor = new PowerEditorView([], path, protoUnitNames: ["AreaVfx"]);
            var window = new Window { Content = editor };
            window.Show();
            try
            {
                editor.SetModifiedMode(true);
                for (var cycle = 0; cycle < CycleCount; cycle++)
                {
                    CycleDynamicCard(editor, "RainStress", "_addRainEffectButton",
                        "_rainEffectRows", "type", "TacticEnable", "effect");
                    CycleDynamicCard(editor, "AreaStress", "_addEffectInAreaOnHitButton",
                        "_effectInAreaOnHitRows", null, "Reincarnation", "onhiteffect");
                    CycleDynamicCard(editor, "ModifyStress", "_addUnitModifyButton",
                        "_unitModifyRows", "modifytype", "DamageSpecific", "unitmodify");
                    CycleSwitchingContainer(editor);
                }

                var draft = GetPrivateField<XDocument>(editor, "_modDocument");
                Assert.Empty(FindPower(draft, "RainStress").Elements("effect"));
                Assert.Empty(FindPower(draft, "AreaStress").Elements("onhiteffect"));
                Assert.Empty(FindPower(draft, "ModifyStress").Elements("unitmodify"));
                Assert.Empty(FindPower(draft, "SwitchStress").Elements("effects"));
                var session = GetPrivateField<PowerEditingSession>(editor, "_session");
                var sessionSnapshot = session.CreateCommitSnapshot();
                Assert.Empty(sessionSnapshot.StringUpdates);
                Assert.Empty(sessionSnapshot.StringRemovals);
                Assert.DoesNotContain("Unable", editor.StatusMessage, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void CycleDynamicCard(
        PowerEditorView editor,
        string powerName,
        string addButtonField,
        string rowsField,
        string? selectorTag,
        string selectorValue,
        string xmlName)
    {
        editor.SelectPower(powerName);
        Dispatcher.UIThread.RunJobs();

        GetPrivateField<Button>(editor, addButtonField)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var rows = GetPrivateField<StackPanel>(editor, rowsField);
        Assert.Single(rows.Children);

        var selectors = rows.GetVisualDescendants().OfType<AutoCompleteBox>();
        (selectorTag == null
                ? selectors.Single()
                : selectors.Single(control => Equals(control.Tag, selectorTag)))
            .SelectedItem = selectorValue;
        Dispatcher.UIThread.RunJobs();

        rows = GetPrivateField<StackPanel>(editor, rowsField);
        Assert.Single(rows.Children);
        rows.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("remove-button"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(rows.Children);
        Assert.Empty(GetPrivateField<XElement>(editor, "_currentEntry").Elements(xmlName));
    }

    private static void CycleSwitchingContainer(PowerEditorView editor)
    {
        editor.SelectPower("SwitchStress");
        Dispatcher.UIThread.RunJobs();

        GetPrivateField<Button>(editor, "_addSwitchingEffectContainerButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var rows = GetPrivateField<StackPanel>(editor, "_switchingEffectContainers");
        Assert.Single(rows.Children);

        rows.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("remove-button") &&
                button.Tag is XElement element &&
                element.Name.LocalName.Equals("effects", StringComparison.OrdinalIgnoreCase))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(rows.Children);
        Assert.Empty(GetPrivateField<XElement>(editor, "_currentEntry").Elements("effects"));
    }

    private static XElement FindPower(XDocument document, string name)
        => document.Descendants("power").Single(power =>
            ((string?)power.Attribute("name"))?.Equals(name,
                StringComparison.OrdinalIgnoreCase) == true);

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? instance.GetType().GetProperty(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? throw new InvalidOperationException($"Missing field or property {name}."));
}
