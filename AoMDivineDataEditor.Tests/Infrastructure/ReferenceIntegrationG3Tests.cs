using System.Text.Json.Nodes;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceIntegrationG3Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedRenamesSurviveReopenOrRollbackTogether(bool fail)
    {
        using var workspace = new TestWorkspace("g3-combined");
        var proto = workspace.WriteText("proto_mods.xml", "<protomods><unit name='NewUnit'><protoaction><name>NewAction</name><castpower>OldPower</castpower></protoaction></unit></protomods>");
        var powers = workspace.WriteText("powers_mods.xml", "<powersmod><power name='NewPower' type='Rain'><effect type='Damage' action='OldAction'><target type='ProtoUnit'>OldUnit</target></effect><effect type='Cost'><target type='Tech'>OldTech</target></effect><future keep='yes'/></power></powersmod>");
        var tech = workspace.WriteText("techtree_mods.xml", "<techtreemods><tech name='NewTech'><effects><effect type='Data' subtype='ActionEnable' action='OldAction'><target type='ProtoUnit'>OldUnit</target></effect></effects></tech></techtreemods>");
        var transform = workspace.WriteText("unit_transform_mods.xml", "<unittransformmods><transform fullheal='true'><from>OldUnit</from><to>Other</to><revertothersto>OldUnit</revertothersto><command>Independent</command><tech>OldTech</tech></transform></unittransformmods>");
        var tactics = workspace.WriteText("custom.tactics", "<tactics><action><name>Attack</name><projectile>OldUnit</projectile><devotionpower>OldPower</devotionpower></action></tactics>");
        var metadata = workspace.WriteText("bindings.json", new JsonArray(new JsonObject
        {
            ["ModPath"] = proto, ["UnitName"] = "NewUnit", ["AbilityName"] = "OldPower",
            ["MainAction"] = "NewAction", ["MainOwnsChargeAction"] = true, ["Future"] = "keep"
        }).ToJsonString());
        var paths = new[] { proto, powers, tech, transform, tactics, metadata };
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        var states = paths.ToDictionary(path => path, FileIntegrityTransaction.CaptureState);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var error = Record.Exception(() => FileIntegrityTransaction.Execute(paths, states, () =>
        {
            var units = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit,
                [new("OldUnit", "NewUnit")], [new("NewUnit", "OldAction", "NewAction")], []);
            Assert.Equal(4, units.Paths.Count);
            units.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path));
            var technologies = TechnologyReferenceMigrationPlan.Prepare(files, [new("OldTech", "NewTech")]);
            Assert.Empty(technologies.UnknownReferences);
            Assert.Equal(2, technologies.Paths.Count);
            technologies.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path));
            var abilities = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.Ability,
                [new("OldPower", "NewPower")], bindingModPath: proto, bindingMetadataPath: metadata);
            Assert.Equal(3, abilities.Paths.Count);
            abilities.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path));
            if (fail) throw new IOException("Injected after all migrations");
        }));
        if (fail)
        {
            Assert.IsType<IOException>(error);
            foreach (var path in paths) Assert.Equal(before[path], File.ReadAllBytes(path));
            return;
        }
        Assert.Null(error);
        Assert.Equal("NewPower", XDocument.Load(proto).Descendants("castpower").Single().Value);
        Assert.Equal("NewAction", (string?)XDocument.Load(tech).Descendants("effect").Single().Attribute("action"));
        Assert.Equal("NewUnit", XDocument.Load(tactics).Descendants("projectile").Single().Value);
        Assert.Equal("NewPower", XDocument.Load(tactics).Descendants("devotionpower").Single().Value);
        Assert.Equal("NewTech", XDocument.Load(transform).Descendants("tech").Single().Value);
        Assert.Equal("Independent", XDocument.Load(transform).Descendants("command").Single().Value);
        var binding = JsonNode.Parse(File.ReadAllText(metadata))![0]!;
        Assert.Equal("NewPower", binding["AbilityName"]!.GetValue<string>());
        Assert.True(binding["MainOwnsChargeAction"]!.GetValue<bool>());
        Assert.Equal("keep", binding["Future"]!.GetValue<string>());
        AvaloniaTestHost.Run(() =>
        {
            var reopened = new PowerEditorView(new PowerEditorComposition([], powers));
            reopened.SetModifiedMode(true);
            reopened.SelectPower("NewPower");
            var saved = reopened.LoadSavedPowerElement("NewPower")!;
            Assert.Equal("NewUnit", saved.Descendants("target").First().Value);
            Assert.Equal("NewAction", (string?)saved.Elements("effect").First().Attribute("action"));
            Assert.Equal("NewTech", saved.Descendants("target").Last().Value);
            Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            Assert.False(reopened.IsDirty);
        });
    }
}
