using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    [Theory]
    [InlineData("type='UnitAction'", true)]
    [InlineData("TYPE='unitaction' godpower=''", true)]
    [InlineData("type='GeneralEffect'", true)]
    [InlineData("type='GeneralEffect' godpower='OtherPower'", true)]
    [InlineData("type='GeneralEffect' godpower=''", false)]
    [InlineData("type=' GeneralEffect ' GodPower='  '", false)]
    [InlineData("type='SolarShield'", false)]
    [InlineData("type='GodPower'", false)]
    [InlineData("type='FutureType'", false)]
    [InlineData("type=''", false)]
    [InlineData("", false)]
    public void AbilityClassificationUsesTheExistingPowerBoundary(string attributes, bool ability)
    {
        var power = XElement.Parse($"<power name='Example' {attributes}/>");
        Assert.Equal(ability, PowerCatalog.IsAbilityPower(power));
        Assert.Equal(!ability, PowerCatalog.IsModCatalogPower(power));
        var names = PowerCatalog.ExtractCatalogNames([], [new XDocument(new XElement("powersmod", power))]);
        Assert.Equal(ability ? Array.Empty<string>() : ["Example"], names);
    }

    private const string ClassificationPowers = """
        <powersmod><!-- keep -->
          <power name='SolarShieldCopy' type='SolarShield'><future keep='yes'/></power>
          <power name='GodGeneral' type='GeneralEffect' godpower=''/>
          <power name='CustomAction' type='UnitAction'/>
          <power name='CustomGeneral' type='GeneralEffect'/>
          <power name='RelatedGeneral' type='GeneralEffect' godpower='SolarShieldCopy'/>
          <power name='FuturePower' type='FutureType'/>
          <power name='MissingType'/>
          <power name='BaseOverride' type='SolarShield'/>
          <power name='OriginalAbility' type='UnitAction'><radius>15</radius></power>
        </powersmod>
        """;

    [Fact]
    public void AbilityCatalogMembershipAndCustomNamesExcludeGodPowersWithoutWriting()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", ClassificationPowers);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.Run(() =>
        {
            var owner = SeedOwner(fixture);
            try
            {
                owner.Set("_cachedBaseAbilityDocuments", new List<XDocument> { XDocument.Parse("<powers><power name='OriginalAbility' type='UnitAction'/><power name='OriginalGeneral' type='GeneralEffect'/><power name='BaseOverride' type='UnitAction'/></powers>") });
                owner.Call("LoadAbilitySources", "__classification_test__");
                var expected = new[] { "CustomAction", "CustomGeneral", "OriginalAbility", "OriginalGeneral", "RelatedGeneral" };
                var catalog = (Dictionary<string, XElement>)Field(owner, "_abilityPowerCatalog")!;
                Assert.Equal(expected, catalog.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                Assert.Equal(expected, (List<string>)Field(owner, "_availableAbilityNames")!);
                Assert.Equal("15", catalog["OriginalAbility"].Element("radius")!.Value);
                Assert.Equal(expected.Except(["OriginalGeneral"]), ((HashSet<string>)owner.Call("GetCustomAbilityPowerNames")!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { owner.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void AbilityManagerRefreshTracksCreateDuplicateRenameAndReclassification()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", ClassificationPowers);
        AvaloniaTestHost.Run(() =>
        {
            var owner = SeedOwner(fixture);
            try
            {
                owner.Call("EnsureAbilityCatalogLoadedForManager");
                var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                document.Root!.Add(new XElement("power", new XAttribute("name", "NewGod"), new XAttribute("type", "SolarShield")));
                document.Root.Add(new XElement("power", new XAttribute("name", "DuplicatedGod"), new XAttribute("type", "SolarShield")));
                document.Root.Add(new XElement("power", new XAttribute("name", "NewAbility"), new XAttribute("type", "UnitAction")));
                document.Root.Elements("power").Single(power => PowerCatalog.GetName(power) == "CustomGeneral").SetAttributeValue("name", "RenamedAbility");
                document.Root.Elements("power").Single(power => PowerCatalog.GetName(power) == "CustomAction").SetAttributeValue("type", "SolarShield");
                document.Save(path);
                var before = File.ReadAllBytes(path);
                owner.Call("EnsureAbilityCatalogLoadedForManager");
                var expected = new[] { "NewAbility", "OriginalAbility", "RelatedGeneral", "RenamedAbility" };
                Assert.Equal(expected, ((Dictionary<string, XElement>)Field(owner, "_abilityPowerCatalog")!).Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                Assert.Equal(expected, (List<string>)Field(owner, "_availableAbilityNames")!);
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { owner.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData("SolarShield")]
    [InlineData("GeneralEffect")]
    public void AbilityEditorRejectsGodPowerBeforeInitializationOrSave(string type)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", $"<powersmod><power name='God' type='{type}' godpower=''/></powersmod>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var child = new BaselineHost(new RootGameData(fixture.GameRoot));
            var calls = 0;
            try
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => child.InitializeAbilityDefinitionEditorAsync("God", false,
                    XDocument.Load(path).Root!.Element("power")!, fixture.ProtoPath,
                    (_, _, _, _) => { calls++; return Task.CompletedTask; }));
                Assert.False((bool)Field(child, "_isAbilityDefinitionEditorMode")!);
                Assert.Equal(0, calls);
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { child.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void AbilitySaveCannotOverwriteAnExternallyReclassifiedGodPower()
        => WithStandaloneXml(true, async (_, owner, _, path, _) =>
        {
            var god = XDocument.Parse("<powersmod><!-- keep --><power name='AbilityA' type='SolarShield'><future/></power></powersmod>");
            god.Save(path);
            var before = File.ReadAllBytes(path);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => (Task)owner.Call("SaveManagedAbilityPowerAsync",
                "AbilityA", XElement.Parse(AbilityXml), "AbilityA", null!, null!, null!)!);
            Assert.Contains("God Powers cannot be saved", error.Message);
            Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbilityManagerCannotRenameOrDeleteAGodPowerFromAStaleEntry(bool rename)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", ClassificationPowers);
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            try
            {
                owner.Show();
                var result = new AbilityManagerResult();
                if (rename) result.Renames.Add(new AbilityRenameOperation("SolarShieldCopy", "RenamedGod"));
                else result.DeletedNames.Add("SolarShieldCopy");
                var task = (Task)owner.Call("ApplyAbilityManagerChangesAsync", result)!;
                var errors = new List<string>();
                for (var i = 0; !task.IsCompleted && i < 500; i++)
                {
                    foreach (var prompt in owner.OwnedWindows.OfType<Prompt>().ToArray())
                    {
                        errors.Add(prompt.PromptText);
                        prompt.Close();
                    }
                    await Task.Delay(10);
                }
                Assert.True(task.IsCompleted, "Manager guard did not finish.");
                await task;
                Assert.Contains("is a God Power", Assert.Single(errors));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { owner.DisposeFixtureWindow(); }
        });
    }
}
