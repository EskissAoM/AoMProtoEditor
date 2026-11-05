using System.Text.Json.Nodes;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceBindingMetadataMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataAndXmlCommitOrRollbackTogether(bool fail)
    {
        using var workspace = new TestWorkspace("binding-migration");
        var mod = workspace.GetPath("proto_mods.xml");
        var xml = workspace.WriteText("powers_mods.xml", "<powersmod><power name='P' type='TempUnit'><castpower>Old</castpower></power></powersmod>");
        var records = new JsonArray(Binding(mod, "U", "Old"), Binding(mod, "V", "Old"), Binding(mod + "other", "U", "Old"));
        var path = workspace.WriteText("bindings.json", records.ToJsonString());
        var before = File.ReadAllBytes(path);
        var xmlBefore = File.ReadAllBytes(xml);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.Ability, [new("Old", "Middle"), new("Middle", "New")], bindingModPath: mod, bindingMetadataPath: path);
        Assert.Equal(2, plan.Paths.Count);
        Assert.Equal(before, File.ReadAllBytes(path));
        var error = Record.Exception(() => FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () =>
        {
            plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target));
            if (fail) throw new IOException("Injected after metadata write");
        }));
        if (fail)
        {
            Assert.IsType<IOException>(error);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(xmlBefore, File.ReadAllBytes(xml));
        }
        else
        {
            Assert.Null(error);
            records[0]!["AbilityName"] = "New";
            records[1]!["AbilityName"] = "New";
            Assert.True(JsonNode.DeepEquals(records, JsonNode.Parse(File.ReadAllText(path))));
            Assert.Contains(">New<", File.ReadAllText(xml));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionOrMalformedMetadataRefusesBeforeWrites(bool malformed)
    {
        using var workspace = new TestWorkspace("binding-refusal");
        var mod = workspace.GetPath("proto_mods.xml");
        var path = workspace.WriteText("bindings.json", malformed ? "broken" : new JsonArray(Binding(mod, "U", "Old"), Binding(mod, "U", "New")).ToJsonString());
        var before = File.ReadAllBytes(path);
        Assert.NotNull(Record.Exception(() => GameIdentifierReferenceMigrationPlan.Prepare([], GameIdentifierKind.Ability,
            [new("Old", "New")], bindingModPath: mod, bindingMetadataPath: path)));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void MetadataOnlyRenameIsIncludedAndLateChangesBlockCommit()
    {
        using var workspace = new TestWorkspace("binding-late");
        var mod = workspace.GetPath("proto_mods.xml");
        var path = workspace.WriteText("bindings.json", new JsonArray(Binding(mod, "U", "Old")).ToJsonString());
        var plan = GameIdentifierReferenceMigrationPlan.Prepare([], GameIdentifierKind.Ability,
            [new("Old", "New")], bindingModPath: mod, bindingMetadataPath: path);
        Assert.Single(plan.Paths);
        File.AppendAllText(path, " ");
        var changed = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target))));
        Assert.Equal(changed, File.ReadAllBytes(path));
    }

    [Fact]
    public void AllThreeHostAbilityRenamePathsIncludeMetadata()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        Assert.Equal(3, source.Split("bindingModPath: _modFilePath", StringSplitOptions.None).Length - 1);
    }

    private static JsonObject Binding(string mod, string unit, string ability) => new()
    {
        ["ModPath"] = mod, ["UnitName"] = unit, ["AbilityName"] = ability,
        ["MainAction"] = "Attack", ["MainOwnsChargeAction"] = true,
        ["AuxAction"] = "AuxAttack", ["AuxOwnsChargeAction"] = false,
        ["Future"] = new JsonObject { ["preserve"] = 42 }
    };
}
