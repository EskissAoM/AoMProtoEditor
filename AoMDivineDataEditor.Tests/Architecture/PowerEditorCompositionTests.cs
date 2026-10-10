using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Architecture)]
public sealed class PowerEditorCompositionTests
{
    [Fact]
    public void CatalogsNormalizeSortAndShareTheUnitOrTypeUnion()
    {
        var catalogs = new PowerEditorCatalogs(
            iconPaths: ["ui\\icons\\b.png", "UI/icons/B.png", "a.png"],
            protoUnitNames: ["Zeus", "Ajax", "ajax", ""],
            unitTypeNames: ["Hero", "zeus"],
            terrainCreepNames: ["WitherCreep"],
            protoActionNames: ["Attack"],
            technologyNames: ["Age2"],
            animationNames: ["Idle"],
            boneNames: ["Spine"],
            tacticNames: ["Water", "Land"]);

        Assert.Equal(["Ajax", "Zeus"], catalogs.ProtoUnitNames);
        Assert.Equal(["Hero", "zeus"], catalogs.UnitTypeNames);
        Assert.Equal(["Ajax", "Hero", "Zeus"], catalogs.UnitOrTypeNames);
        Assert.Equal(2, catalogs.IconPaths.Count);
        Assert.Equal(["Land", "Water"], catalogs.TacticNames);
        Assert.Equal("WitherCreep", Assert.Single(catalogs.TerrainCreepNames));
    }

    [Fact]
    public void CompositionMaterializesDefinitionsAndKeepsExplicitBoundaries()
    {
        var source = new List<PowerDefinition>
        {
            new("First", true, XElement.Parse("<power name='First' type='Bolt' />"))
        };
        var catalogs = new PowerEditorCatalogs(protoUnitNames: ["UnitA"]);
        var services = new PowerEditorHostServices();

        var composition = new PowerEditorComposition(
            source, "powers_mods.xml", "strings.txt", catalogs, services);
        source.Clear();

        Assert.Equal("First", Assert.Single(composition.BaseDefinitions).Name);
        Assert.Equal("powers_mods.xml", composition.ModPath);
        Assert.Equal("strings.txt", composition.ModStringsPath);
        Assert.Same(catalogs, composition.Catalogs);
        Assert.Same(services, composition.HostServices);
    }

    [Fact]
    public void ViewConsumesTheCompositionWithoutRediscoveringCatalogs()
    {
        AvaloniaTestHost.Run(() =>
        {
            var composition = new PowerEditorComposition(
                [new PowerDefinition("Base", true,
                    XElement.Parse("<power name='Base' type='Bolt' />"))],
                null,
                catalogs: new PowerEditorCatalogs(
                    protoUnitNames: ["UnitA"], unitTypeNames: ["Hero"]));

            var editor = new PowerEditorView(composition);

            Assert.Equal(["Base"], editor.GetPowerNames(modified: false));
            Assert.Equal(["UnitA"], GetPrivateProperty<IReadOnlyList<string>>(editor, "_protoUnitNames"));
            Assert.Equal(["Hero", "UnitA"],
                GetPrivateProperty<IReadOnlyList<string>>(editor, "_unitOrTypeNames"));
        });
    }

    [Fact]
    public void ProductionCompositionSupportsSelectionRenameAndSaveWorkflow()
    {
        using var workspace = new TestWorkspace(nameof(
            ProductionCompositionSupportsSelectionRenameAndSaveWorkflow));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Original' type='Bolt' /></powersmod>");
        var composition = new PowerEditorComposition(
            [], path, catalogs: new PowerEditorCatalogs(protoUnitNames: ["UnitA"]));
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(composition));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Original");
            GetPrivateField<TextBox>(editor, "_nameBox").Text = "Renamed";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = XDocument.Load(path);
        Assert.NotNull(saved.Descendants("power").SingleOrDefault(power =>
            (string?)power.Attribute("name") == "Renamed"));
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
    }

    private static T GetPrivateProperty<T>(object instance, string name)
        => (T)(instance.GetType().GetProperty(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? throw new MissingMemberException(instance.GetType().FullName, name));

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name,
                   BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));
}
