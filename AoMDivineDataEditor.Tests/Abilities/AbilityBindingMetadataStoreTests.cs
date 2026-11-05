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
    public void PreparedUnitReplacementPreservesOtherRecordsAndRefusesInvalidRootWithoutWriting()
    {
        using var workspace = new TestWorkspace("ability-metadata-preparation");
        var mod = workspace.GetPath("proto_mods.xml");
        var original = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject { ["ModPath"] = mod + "other", ["UnitName"] = "Owner", ["AbilityName"] = "Keep", ["Future"] = "unchanged" },
            new System.Text.Json.Nodes.JsonObject { ["ModPath"] = mod, ["UnitName"] = "OtherUnit", ["AbilityName"] = "Keep", ["Future"] = 42 },
            new System.Text.Json.Nodes.JsonObject { ["ModPath"] = mod, ["UnitName"] = "Owner", ["AbilityName"] = "Old" }
        };
        var path = workspace.WriteText("bindings.json", original.ToJsonString());
        var before = File.ReadAllBytes(path);
        var replacements = new Dictionary<string, IReadOnlyList<AbilityBindingMetadataRecord>>
        { ["Owner"] = [new() { AbilityName = "New", MainAction = "Attack" }] };
        var prepared = AbilityBindingMetadataStore.PrepareUnitReplacements(mod, replacements, path);
        var result = System.Text.Json.Nodes.JsonNode.Parse(prepared)!.AsArray();
        Assert.Equal(3, result.Count);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(original[0], result[0]));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(original[1], result[1]));
        Assert.Equal("New", result[2]!["AbilityName"]!.GetValue<string>());
        Assert.Equal(before, File.ReadAllBytes(path));
        File.WriteAllText(path, "{}");
        Assert.Throws<InvalidDataException>(() => AbilityBindingMetadataStore.PrepareUnitReplacements(mod, replacements, path));
        Assert.Equal("{}", File.ReadAllText(path));
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
