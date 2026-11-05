using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceActionBindingCaptureTests
{
    [Theory]
    [InlineData("Owner", "Attack", "NewAttack")]
    [InlineData("Other", "Attack", "Attack")]
    [InlineData("Owner", "AttackSuffix", "AttackSuffix")]
    public void CapturedMetadataFollowsOnlyExactOwnedActionAndRollbackRestoresIt(string owner, string action, string expected)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Set("_currentUnitName", "Owner");
                AddDraft(host, action, "Attack");
                var snapshot = host.Call("CreateProtoUnitDraftCaptureSnapshot")!;
                host.Call("RenameCurrentAbilityActionBindings", (object)new List<ProtoActionRenameOperation> { new(owner, "Attack", "NewAttack") });
                var record = Metadata(host);
                Assert.Equal(expected, record.MainAction);
                Assert.Equal(owner == "Owner" ? "NewAttack" : "Attack", record.AuxAction);
                Assert.True(record.MainOwnsChargeAction);
                Assert.False(record.AuxOwnsChargeAction);
                Assert.Equal("Ability", record.AbilityName);
                host.Call("RestoreProtoUnitDraftCaptureSnapshot", snapshot);
                var restored = Metadata(host);
                Assert.Equal(action, restored.MainAction);
                Assert.Equal("Attack", restored.AuxAction);
                Assert.True(restored.MainOwnsChargeAction);
                Assert.False(restored.AuxOwnsChargeAction);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void SimultaneousRenamesDoNotCascadeAcrossBindings()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Set("_currentUnitName", "Owner");
                AddDraft(host, "A", "B");
                host.Call("RenameCurrentAbilityActionBindings", (object)new List<ProtoActionRenameOperation>
                    { new("Owner", "A", "B"), new("Owner", "B", "C") });
                Assert.Equal("B", Metadata(host).MainAction);
                Assert.Equal("C", Metadata(host).AuxAction);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void SuccessfulSaveMakesNewAndPreviouslyRenamedActionsEligibleForNextRename()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var stateType = typeof(ProtoEditorWindow).GetNestedType("ProtoActionWidgetState", BindingFlags.NonPublic)!;
                var state = Activator.CreateInstance(stateType, nonPublic: true)!;
                var model = new ProtoAction { Name = "Original" };
                stateType.GetProperty("Model")!.SetValue(state, model);
                stateType.GetProperty("IsNewCustomAction")!.SetValue(state, true);
                var draft = stateType.GetProperty("Draft")!.GetValue(state)!;
                draft.GetType().GetProperty("Name")!.SetValue(draft, "HandAttackMine");
                ((IList)typeof(ProtoEditorWindow).GetField("_protoActionWidgets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!).Add(state);
                host.Call("AcknowledgeSavedProtoActionNames");
                Assert.Equal("HandAttackMine", model.Name);
                Assert.Equal(false, stateType.GetProperty("IsNewCustomAction")!.GetValue(state));
                draft.GetType().GetProperty("Name")!.SetValue(draft, "HandAttackMine2");
                Assert.Equal("HandAttackMine", model.Name);
                host.Call("AcknowledgeSavedProtoActionNames");
                Assert.Equal("HandAttackMine2", model.Name);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void BindingPickerAndCatalogFollowCapturedRenameWithoutLosingOwnership()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Set("_currentUnitName", "Owner");
                AddDraft(host, "Attack", "Attack");
                var fields = (IDictionary)typeof(ProtoEditorWindow).GetField("_fieldControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                var main = new AutoCompleteBox { Text = "Attack", ItemsSource = new[] { "Attack", "Other" } };
                var aux = new AutoCompleteBox { Text = "Attack", ItemsSource = new[] { "Attack", "Other" } };
                fields["recharge.boundAction"] = main;
                fields["auxrecharge.boundAction"] = aux;
                host.Call("RenameCurrentAbilityActionBindings", (object)new List<ProtoActionRenameOperation> { new("Owner", "Attack", "Attack2") });
                foreach (var picker in new[] { main, aux })
                {
                    Assert.Equal("Attack2", picker.Text);
                    Assert.Equal(new[] { "Attack2", "Other" }, picker.ItemsSource!.Cast<string>());
                }
                Assert.Equal("Attack2", Metadata(host).AuxAction);
                Assert.True(Metadata(host).MainOwnsChargeAction);
                Assert.False(Metadata(host).AuxOwnsChargeAction);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void CaptureCallsBindingRenameAndMetadataSaveUsesCapturedBindings()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        Assert.Contains("RenameCurrentAbilityActionBindings(actionRenames);", source);
        Assert.Contains("if (targetUnit == null)\n            RenameCurrentAbilityActionBindings(actionRenames);", source.Replace("\r\n", "\n"));
        Assert.Contains("_unitAbilityDrafts[capturedName] = CloneAbilityDrafts(_abilityDrafts);", source);
        Assert.Contains("BuildAbilityBindingMetadataRecords(_abilityDrafts.Values)", source);
        var preparation = ProjectSource.Read("Windows", "ProtoEditorWindow.AbilityStrings.cs");
        Assert.Contains("bindings[unitName] = BuildAbilityBindingMetadataRecords(drafts.Values)", preparation);
        Assert.Contains("AbilityBindingMetadataStore.PrepareUnitReplacements", preparation);
        Assert.Contains("AbilityBindingMetadataStore.SavePrepared", preparation);
        Assert.Contains("AbilityBindingMetadataStore.StoragePath", source);
    }

    private static AbilityBindingMetadataRecord Metadata(BaselineHost host)
        => Assert.Single((List<AbilityBindingMetadataRecord>)host.Call("BuildCurrentAbilityBindingMetadataRecords")!);

    private static void AddDraft(BaselineHost host, string main, string aux)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(ProtoEditorWindow).GetNestedType("AbilityEditorDraft", BindingFlags.NonPublic)!;
        var draft = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("Name")!.SetValue(draft, "Ability");
        type.GetProperty("BoundMainRechargeAction")!.SetValue(draft, main);
        type.GetProperty("BoundAuxRechargeAction")!.SetValue(draft, aux);
        type.GetProperty("BoundMainActionFlagManaged")!.SetValue(draft, true);
        var generalFlags = (HashSet<string>)type.GetProperty("GeneralFlags")!.GetValue(draft)!;
        generalFlags.Add("bindtochargeaction");
        generalFlags.Add("bindtoauxchargeaction");
        ((IDictionary)typeof(ProtoEditorWindow).GetField("_abilityDrafts", flags)!.GetValue(host)!)["Ability"] = draft;
    }
}
