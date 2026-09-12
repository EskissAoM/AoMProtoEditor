using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AbilityBindingMetadataStoreTests : IDisposable
{
    private readonly string _metadataPath = ProtoEditorSettings.GetAppDataPath("aom_editor_ability_bindings.json");
    private readonly string? _backup;

    public AbilityBindingMetadataStoreTests()
    {
        _backup = File.Exists(_metadataPath) ? File.ReadAllText(_metadataPath) : null;
        try { File.Delete(_metadataPath); } catch { }
    }

    [Fact]
    public void ReplaceUnit_PersistsIndependentMainAndAuxOwnershipAcrossReloads()
    {
        var modPath = Path.Combine(Path.GetTempPath(), "AoMTestMod", Guid.NewGuid().ToString("N"));

        AbilityBindingMetadataStore.ReplaceUnit(modPath, "TestUnit",
        [
            new AbilityBindingMetadataRecord
            {
                AbilityName = "AbilityA",
                MainAction = "ActionMain",
                MainOwnsChargeAction = true,
                AuxAction = "ActionAux",
                AuxOwnsChargeAction = false,
            }
        ]);

        var loaded = AbilityBindingMetadataStore.Get(modPath, "TestUnit", "AbilityA");
        Assert.NotNull(loaded);
        Assert.Equal("ActionMain", loaded.MainAction);
        Assert.True(loaded.MainOwnsChargeAction);
        Assert.Equal("ActionAux", loaded.AuxAction);
        Assert.False(loaded.AuxOwnsChargeAction);
    }

    [Fact]
    public void ReplaceUnit_RemovesStaleAbilityOwnershipRecords()
    {
        var modPath = Path.Combine(Path.GetTempPath(), "AoMTestMod", Guid.NewGuid().ToString("N"));
        AbilityBindingMetadataStore.ReplaceUnit(modPath, "TestUnit",
        [
            new AbilityBindingMetadataRecord { AbilityName = "OldAbility", MainAction = "Attack", MainOwnsChargeAction = true }
        ]);

        AbilityBindingMetadataStore.ReplaceUnit(modPath, "TestUnit", []);

        Assert.Null(AbilityBindingMetadataStore.Get(modPath, "TestUnit", "OldAbility"));
    }

    [Fact]
    public void CopyUnit_CopiesBindingProvenanceWithoutChangingTheSource()
    {
        var modPath = Path.Combine(Path.GetTempPath(), "AoMTestMod", Guid.NewGuid().ToString("N"));
        AbilityBindingMetadataStore.ReplaceUnit(modPath, "SourceUnit",
        [
            new AbilityBindingMetadataRecord
            {
                AbilityName = "AbilityA",
                MainAction = "HandAttack",
                MainOwnsChargeAction = true,
                AuxAction = "RangedAttack",
                AuxOwnsChargeAction = true,
            }
        ]);

        AbilityBindingMetadataStore.CopyUnit(modPath, "SourceUnit", "CopiedUnit");

        var source = AbilityBindingMetadataStore.Get(modPath, "SourceUnit", "AbilityA");
        var copy = AbilityBindingMetadataStore.Get(modPath, "CopiedUnit", "AbilityA");
        Assert.NotNull(source);
        Assert.NotNull(copy);
        Assert.Equal(source.MainAction, copy.MainAction);
        Assert.Equal(source.MainOwnsChargeAction, copy.MainOwnsChargeAction);
        Assert.Equal(source.AuxAction, copy.AuxAction);
        Assert.Equal(source.AuxOwnsChargeAction, copy.AuxOwnsChargeAction);
    }

    [Fact]
    public void RenameUnit_MovesBindingProvenanceWithoutLeavingTheOldKey()
    {
        var modPath = Path.Combine(Path.GetTempPath(), "AoMTestMod", Guid.NewGuid().ToString("N"));
        AbilityBindingMetadataStore.ReplaceUnit(modPath, "OldUnit",
        [
            new AbilityBindingMetadataRecord
            {
                AbilityName = "AbilityA",
                MainAction = "Attack",
                MainOwnsChargeAction = true
            }
        ]);

        AbilityBindingMetadataStore.RenameUnit(modPath, "OldUnit", "NewUnit");

        Assert.Null(AbilityBindingMetadataStore.Get(modPath, "OldUnit", "AbilityA"));
        var renamed = AbilityBindingMetadataStore.Get(modPath, "NewUnit", "AbilityA");
        Assert.NotNull(renamed);
        Assert.Equal("Attack", renamed!.MainAction);
        Assert.True(renamed.MainOwnsChargeAction);
    }

    public void Dispose()
    {
        try
        {
            if (_backup == null)
                File.Delete(_metadataPath);
            else
                File.WriteAllText(_metadataPath, _backup);
        }
        catch { }
    }
}
