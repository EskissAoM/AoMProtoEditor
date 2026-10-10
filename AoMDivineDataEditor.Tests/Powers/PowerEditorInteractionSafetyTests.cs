using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorInteractionSafetyTests
{
    [Fact]
    public void UntypedPowerNameLosingFocusDefersCommitSoTypeCanBeSelected()
    {
        using var workspace = new TestWorkspace(
            nameof(UntypedPowerNameLosingFocusDefersCommitSoTypeCanBeSelected));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"NewPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("NewPower");
            var name = GetPrivateField<TextBox>(editor, "_nameBox");
            name.Text = "RenamedPower";
            name.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

            Assert.Equal("RenamedPower", name.Text);
            Assert.Equal("NewPower", editor.CurrentPowerName);
            Assert.NotEqual("Select a Power Type before saving.", editor.StatusMessage);

            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "Bolt";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => editor.StatusMessage));
        var saved = XDocument.Load(path).Root!.Elements("power").Single();
        Assert.Equal("RenamedPower", saved.Attribute("name")?.Value);
        Assert.Equal("Bolt", saved.Attribute("type")?.Value);
    }

    [Fact]
    public void QueuedNameFocusCommitCannotValidateTheNewlySelectedUntypedPower()
    {
        using var workspace = new TestWorkspace(
            nameof(QueuedNameFocusCommitCannotValidateTheNewlySelectedUntypedPower));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Source" type="Rain" />
              <power name="NewPower" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Source");
        });

        var delayedCommit = AvaloniaTestHost.Run(() =>
        {
            var source = GetPrivateField<XElement>(editor, "_currentEntry");
            editor.SelectPower("NewPower");
            var commit = editor.GetType().GetMethod("CommitCurrentPowerAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(XElement)], modifiers: null)
                ?? throw new InvalidOperationException("Missing entry-scoped commit overload.");
            return (Task<bool>)(commit.Invoke(editor, [source])
                ?? throw new InvalidOperationException("Entry-scoped commit did not return a task."));
        });

        Assert.True(AvaloniaTestHost.RunAsync(() => delayedCommit));
        Assert.Equal("NewPower", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.DoesNotContain("Select a Power Type", AvaloniaTestHost.Run(() => editor.StatusMessage),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownModdedSelectorValuesRemainVisibleAndRoundTripUnchanged()
    {
        using var workspace = new TestWorkspace(
            nameof(UnknownModdedSelectorValuesRemainVisibleAndRoundTripUnchanged));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Area" type="EffectInArea">
                <onhiteffect type="StatModify">
                  <modify type="DamageSpecific" applytype="FutureApply" dmgtype="FutureDamage">1</modify>
                </onhiteffect>
              </power>
              <power name="Switch" type="SwitchingEffects">
                <tiertype>FutureTier</tiertype><progresstype>FutureProgress</progresstype>
              </power>
              <power name="Summon" type="TempUnit">
                <createunit quantity="1" radius="1" pattern="futurePattern" lifespan="1"
                            lifespantype="FutureLifespan">FutureUnit</createunit>
                <ageadjustment quantityaddon="1">FutureAge</ageadjustment>
              </power>
              <power name="General" type="Rain">
                <placement>FuturePlacement</placement>
                <powerplayerrelation>FutureRelation</powerplayerrelation>
                <rangeindicator>FutureShape</rangeindicator>
                <minimapeventtime sendalertto="FutureAudience">1</minimapeventtime>
                <activelistvisibility>FutureVisibility</activelistvisibility>
              </power>
            </powersmod>
            """);
        var before = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["KnownUnit"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Area"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.DoesNotContain("Unable to fully display", editor.StatusMessage,
                StringComparison.Ordinal);
            var areaRows = GetPrivateField<StackPanel>(editor, "_effectInAreaOnHitRows");
            Assert.Contains(areaRows.GetVisualDescendants().OfType<ComboBox>(), combo =>
                Equals(combo.Tag, "applytype") && Equals(combo.SelectedItem, "FutureApply"));
            Assert.Contains(areaRows.GetVisualDescendants().OfType<ComboBox>(), combo =>
                Equals(combo.SelectedItem, "FutureDamage"));
        });

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Switch"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("FutureTier", GetPrivateField<ComboBox>(editor,
                "_switchingTierTypeBox").SelectedItem);
            Assert.Equal("FutureProgress", GetPrivateField<ComboBox>(editor,
                "_switchingProgressTypeBox").SelectedItem);
        });

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Summon"));
        AvaloniaTestHost.Run(() =>
        {
            var createRows = GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows");
            Assert.Contains(createRows.GetVisualDescendants().OfType<ComboBox>(), combo =>
                Equals(combo.SelectedItem, "futurePattern"));
            Assert.Contains(createRows.GetVisualDescendants().OfType<ComboBox>(), combo =>
                Equals(combo.SelectedItem, "FutureLifespan"));
            Assert.Contains(GetPrivateField<StackPanel>(editor, "_tempUnitAgeRows")
                .GetVisualDescendants().OfType<ComboBox>(), combo =>
                Equals(combo.SelectedItem, "FutureAge"));
        });

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("General"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("FuturePlacement", GetPrivateField<ComboBox>(editor,
                "_placementBox").SelectedItem);
            Assert.Equal("FutureRelation", GetPrivateField<ComboBox>(editor,
                "_powerPlayerRelationBox").SelectedItem);
            Assert.Equal("FutureShape", GetPrivateField<ComboBox>(editor,
                "_rangeIndicatorBox").SelectedItem);
            Assert.Equal("FutureAudience", GetPrivateField<ComboBox>(editor,
                "_minimapSendToBox").SelectedItem);
            Assert.Equal("FutureVisibility", GetPrivateField<ComboBox>(editor,
                "_activeListVisibilityBox").SelectedItem);
            Assert.False(editor.IsDirty);
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => editor.StatusMessage));
        Assert.True(XNode.DeepEquals(before,
            XDocument.Load(path, LoadOptions.PreserveWhitespace)));
    }

    [Fact]
    public void MalformedKnownStructuresAreDisplayedButBlockedWithoutChangingTheFile()
    {
        using var workspace = new TestWorkspace(
            nameof(MalformedKnownStructuresAreDisplayedButBlockedWithoutChangingTheFile));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="BrokenArea" type="EffectInArea"><onhiteffect /></power>
              <power name="BrokenModify" type="UnitModify">
                <unitmodify apply="FutureApply" amount="not-a-number">FutureModify</unitmodify>
              </power>
              <power name="BrokenRain" type="Rain"><radius>not-a-number</radius></power>
            </powersmod>
            """);
        var before = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        foreach (var name in new[] { "BrokenArea", "BrokenModify", "BrokenRain" })
        {
            var exception = Record.Exception(() =>
                AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync(name)));
            Assert.Null(exception);
            Assert.DoesNotContain("Unable to fully display",
                AvaloniaTestHost.Run(() => editor.StatusMessage), StringComparison.Ordinal);
        }

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Contains("radius", AvaloniaTestHost.Run(() => editor.StatusMessage),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(XNode.DeepEquals(before,
            XDocument.Load(path, LoadOptions.PreserveWhitespace)));
    }

    [Fact]
    public void QueuedCommitFromDetachedCardCannotDirtyTheNextSelectedPower()
    {
        using var workspace = new TestWorkspace(
            nameof(QueuedCommitFromDetachedCardCannotDirtyTheNextSelectedPower));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Effects" type="GeneralEffect" godpower="">
                <effect type="WorkRate" action="All" relativity="BasePercent" amount="1">
                  <target type="ProtoUnit">Soldier</target>
                </effect>
              </power>
              <power name="Healthy" type="Rain" />
            </powersmod>
            """);

        AvaloniaTestHost.Run(() =>
        {
            var editor = new PowerEditorView([], path, protoUnitNames: ["Soldier"]);
            var window = new Window { Content = editor };
            window.Show();
            try
            {
                editor.SetModifiedMode(true);
                editor.SelectPower("Effects");
                Assert.False(editor.IsDirty, "Selecting the source Power dirtied the editor.");
                var target = GetPrivateField<StackPanel>(editor, "_sharedEffectRows")
                    .GetVisualDescendants().OfType<AutoCompleteBox>().Single(control =>
                        Equals(control.Tag, "target"));
                target.Text = "invalid";
                Assert.False(editor.IsDirty, "Typing an uncommitted strict value dirtied the editor.");
                target.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Assert.False(editor.IsDirty, "Queuing lost-focus validation dirtied the editor synchronously.");

                editor.SelectPower("Healthy");
                Assert.False(editor.IsDirty, "Selecting the replacement Power dirtied the editor synchronously.");
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Healthy", editor.CurrentPowerName);
                Assert.False(editor.IsDirty);
                Assert.DoesNotContain("Unable", editor.StatusMessage, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RepeatedDynamicCardAddRemoveCyclesLeaveNoCardsOrXmlBehind()
    {
        using var workspace = new TestWorkspace(
            nameof(RepeatedDynamicCardAddRemoveCyclesLeaveNoCardsOrXmlBehind));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="RainCards" type="Rain" />
              <power name="AreaCards" type="EffectInArea" />
              <power name="ModifyCards" type="UnitModify" />
            </powersmod>
            """);

        AvaloniaTestHost.Run(() =>
        {
            var editor = new PowerEditorView([], path);
            var window = new Window { Content = editor };
            window.Show();
            try
            {
                editor.SetModifiedMode(true);
                Cycle("RainCards", "_addRainEffectButton", "_rainEffectRows", "effect");
                Cycle("AreaCards", "_addEffectInAreaOnHitButton", "_effectInAreaOnHitRows", "onhiteffect");
                Cycle("ModifyCards", "_addUnitModifyButton", "_unitModifyRows", "unitmodify");
            }
            finally
            {
                window.Close();
            }

            void Cycle(string powerName, string addButtonName, string rowsName, string xmlName)
            {
                editor.SelectPower(powerName);
                var add = GetPrivateField<Button>(editor, addButtonName);
                var rows = GetPrivateField<StackPanel>(editor, rowsName);
                for (var index = 0; index < 8; index++)
                {
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Single(rows.Children);
                    rows.GetVisualDescendants().OfType<Button>().Single(button =>
                        button.Classes.Contains("remove-button"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Empty(rows.Children);
                }

                var current = GetPrivateField<System.Xml.Linq.XElement>(editor, "_currentEntry");
                Assert.Empty(current.Elements(xmlName));
            }
        });
    }

    [Fact]
    public void SelectorDrivenCardRebuildsRemainUsableInAnAttachedEditor()
    {
        using var workspace = new TestWorkspace(
            nameof(SelectorDrivenCardRebuildsRemainUsableInAnAttachedEditor));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Effects" type="GeneralEffect" godpower="">
                <effect type="OnHitEffectRate" action="Attack" effecttype="Stun"
                        relativity="BasePercent" amount="1">
                  <target type="ProtoUnit">Soldier</target>
                </effect>
              </power>
              <power name="Modify" type="UnitModify">
                <unitmodify apply="Add" amount="1">Speed</unitmodify>
              </power>
            </powersmod>
            """);

        AvaloniaTestHost.Run(() =>
        {
            var editor = new PowerEditorView([], path,
                protoUnitNames: ["Soldier"], protoActionNames: ["Attack"]);
            var window = new Window { Content = editor };
            window.Show();
            try
            {
                editor.SetModifiedMode(true);
                editor.SelectPower("Effects");

                var effectRows = GetPrivateField<StackPanel>(editor, "_sharedEffectRows");
                effectRows.GetVisualDescendants().OfType<AutoCompleteBox>().Single(control =>
                    Equals(control.Tag, "effecttype")).SelectedItem = "DamageOverTime";
                Dispatcher.UIThread.RunJobs();

                var damageType = effectRows.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                    Equals(control.Tag, "dmgtype"));
                damageType.SelectedItem = "Pierce";
                effectRows.GetVisualDescendants().OfType<AutoCompleteBox>().Single(control =>
                    Equals(control.Tag, "type")).SelectedItem = "Market";
                Dispatcher.UIThread.RunJobs();

                var component = effectRows.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                    Equals(control.Tag, "component"));
                component.SelectedItem = "BuyFactorSpecific";
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(effectRows.GetVisualDescendants().OfType<ComboBox>(), control =>
                    Equals(control.Tag, "resource"));

                editor.SelectPower("Modify");
                var modifyRows = GetPrivateField<StackPanel>(editor, "_unitModifyRows");
                modifyRows.GetVisualDescendants().OfType<AutoCompleteBox>().Single(control =>
                    Equals(control.Tag, "modifytype")).SelectedItem = "DamageSpecific";
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(modifyRows.GetVisualDescendants().OfType<ComboBox>(), control =>
                    Equals(control.Tag, "damagetype"));

                modifyRows.GetVisualDescendants().OfType<AutoCompleteBox>().Single(control =>
                    Equals(control.Tag, "modifytype")).SelectedItem = "Speed";
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(modifyRows.GetVisualDescendants().OfType<ComboBox>(), control =>
                    Equals(control.Tag, "damagetype"));
                Assert.DoesNotContain("Unable to refresh Power", editor.StatusMessage,
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task LateFailureFromAnEarlierSelectionCannotOverwriteTheCurrentPowerState()
    {
        using var workspace = new TestWorkspace(
            nameof(LateFailureFromAnEarlierSelectionCannotOverwriteTheCurrentPowerState));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Slow" type="Rain"><displaynameid>STR_SLOW</displaynameid></power>
              <power name="Current" type="Rain"><displaynameid>STR_CURRENT</displaynameid></power>
            </powersmod>
            """);
        var slowResolution = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            resolveStringAsync: id => id == "STR_SLOW"
                ? slowResolution.Task
                : Task.FromResult<string?>("Current display name")));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        var supersededPopulation = AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Slow");
            return GetPrivateField<Task>(editor, "_populationTask");
        });
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Current"));

        slowResolution.SetException(new InvalidDataException("late resolver failure"));
        await supersededPopulation;

        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("Current", editor.CurrentPowerName);
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
            Assert.DoesNotContain("late resolver failure", editor.StatusMessage,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FailedAsynchronousPopulationIsContainedAndTheNextPowerCanBeSelected()
    {
        using var workspace = new TestWorkspace(
            nameof(FailedAsynchronousPopulationIsContainedAndTheNextPowerCanBeSelected));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Broken" type="Rain">
                <displaynameid>STR_BROKEN</displaynameid>
              </power>
              <power name="Healthy" type="Rain" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            resolveStringAsync: _ => Task.FromException<string?>(new InvalidDataException("resolver failed"))));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        var exception = Record.Exception(() =>
            AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Broken")));

        Assert.Null(exception);
        AvaloniaTestHost.Run(() =>
        {
            Assert.Contains("Unable to fully display Power 'Broken'", editor.StatusMessage,
                StringComparison.Ordinal);
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
        });

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Healthy"));

        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("Healthy", editor.CurrentPowerName);
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
            Assert.DoesNotContain("Unable to fully display", editor.StatusMessage,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RepeatedDuplicateCommandIsIgnoredWhileTheFirstCommandIsAwaitingStrings()
    {
        using var workspace = new TestWorkspace(
            nameof(RepeatedDuplicateCommandIsIgnoredWhileTheFirstCommandIsAwaitingStrings));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Source" type="Rain"><displaynameid>STR_SOURCE</displaynameid></power>
              <power name="Other" type="Rain" />
            </powersmod>
            """);
        var delayedResolution = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delayDuplicate = false;
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            resolveStringAsync: _ => delayDuplicate
                ? delayedResolution.Task
                : Task.FromResult<string?>("Source display name")));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Source"));
        delayDuplicate = true;

        var commands = AvaloniaTestHost.Run(() =>
            (First: editor.AddPowerAsync(duplicateSelected: true),
             Second: editor.AddPowerAsync(duplicateSelected: true)));

        Assert.True(commands.Second.IsCompleted);
        AvaloniaTestHost.Run(() => editor.SelectPower("Other"));
        Assert.Equal("Source", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        delayedResolution.SetResult("Source display name");
        await Task.WhenAll(commands.First, commands.Second);

        var draft = AvaloniaTestHost.Run(() => GetPrivateField<XDocument>(editor, "_modDocument"));
        Assert.Equal(new[] { "Source", "Other", "SourceCopy" }, draft.Descendants("power")
            .Select(power => (string?)power.Attribute("name")).ToArray());
        Assert.Equal("SourceCopy", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
    }

    [Fact]
    public void CancelledDeleteLeavesTheDraftSelectionAndDirtyStateUntouched()
    {
        using var workspace = new TestWorkspace(
            nameof(CancelledDeleteLeavesTheDraftSelectionAndDirtyStateUntouched));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"KeepMe\" type=\"Rain\" /></powersmod>");
        var owner = AvaloniaTestHost.Run(() => new Window());
        Window? confirmedOwner = null;
        string? confirmedName = null;
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            dialogOwner: owner,
            confirmDeleteAsync: (actualOwner, name) =>
            {
                confirmedOwner = actualOwner;
                confirmedName = name;
                return Task.FromResult(false);
            }));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("KeepMe"));

        AvaloniaTestHost.RunAsync(editor.DeleteCurrentPowerAsync);

        Assert.Same(owner, confirmedOwner);
        Assert.Equal("KeepMe", confirmedName);
        Assert.Equal("KeepMe", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.NotNull(AvaloniaTestHost.Run(() =>
            GetPrivateField<XDocument>(editor, "_modDocument").Descendants("power").SingleOrDefault()));
    }

    [Fact]
    public void DeleteValidationFailureIsContainedAndDoesNotDeleteThePower()
    {
        using var workspace = new TestWorkspace(
            nameof(DeleteValidationFailureIsContainedAndDoesNotDeleteThePower));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"KeepMe\" type=\"Rain\" /></powersmod>");
        var owner = AvaloniaTestHost.Run(() => new Window());
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            dialogOwner: owner,
            ensureCanDeleteAsync: (_, _) =>
                Task.FromException<bool>(new InvalidOperationException("validation exploded"))));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("KeepMe"));

        var exception = Record.Exception(() =>
            AvaloniaTestHost.RunAsync(editor.DeleteCurrentPowerAsync));

        Assert.Null(exception);
        Assert.Equal("KeepMe", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.Contains("validation exploded", AvaloniaTestHost.Run(() => editor.StatusMessage),
            StringComparison.Ordinal);
    }

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? instance.GetType().GetProperty(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? throw new InvalidOperationException($"Missing field or property {name}."));
}
