using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    private static void CatalogMod(BaselineSafetyFixture fixture)
    {
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Owner'><protoaction><name>Attack</name><type>Attack</type><damagebonus type='BaseUnit'>2</damagebonus></protoaction></unit><unit name='CustomUnit'/></protomods>");
        File.WriteAllText(fixture.TypePath, "<abstractunittypesmods><abstractunittype>CustomType</abstractunittype></abstractunittypesmods>");
    }

    private static AutoCompleteBox DamageBonusTarget(BaselineHost host)
    {
        var widget = ((IList)Field(host, "_protoActionWidgets")!)[0]!;
        var row = ((IList)widget.GetType().GetProperty("BonusRows")!.GetValue(widget)!)[0]!;
        return (AutoCompleteBox)row.GetType().GetProperty("TypeAcb")!.GetValue(row)!;
    }

    private static void AssertMixedCatalog(IEnumerable<string> values)
    {
        var names = values.ToArray();
        foreach (var name in new[] { "BaseUnit", "CustomUnit", "AbstractBase", "CustomType" }) Assert.Contains(name, names);
        Assert.DoesNotContain(names, name => name.StartsWith("__", StringComparison.Ordinal));
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static async Task FinishCatalogSave(BaselineHost host, Task save)
    {
        var errors = new List<string>();
        for (var i = 0; !save.IsCompleted && i < 1000; i++)
        {
            foreach (var popup in host.OwnedWindows.OfType<Prompt>().ToArray())
            { errors.Add(popup.PromptText); popup.Close(); }
            await Task.Delay(10);
        }
        await save.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(errors);
    }

    [Fact]
    public void SelectorCatalogMainDamageBonusUsesSharedTargetsAndSavesCustomUnit()
    {
        using var fixture = new BaselineSafetyFixture(); CatalogMod(fixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = SeedOwner(fixture);
            try
            {
                // SeedOwner installs BAR data after the baseline host's God
                // setup; expire that setup's earlier empty type lookup.
                host.Call("InvalidateSuggestionCaches", false);
                host.Call("InitializeEditorTabs"); host.Call("BuildEditorPanel", "Owner", true);
                host.Show(); Dispatcher.UIThread.RunJobs(); host.Set("_suppressEditorChangeTracking", false);
                var target = DamageBonusTarget(host);
                AssertMixedCatalog(target.ItemsSource!.Cast<string>());
                target.SelectedItem = "CustomUnit"; target.Text = "CustomUnit"; host.Call("MarkDirty");
                await FinishCatalogSave(host, (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!);
                var saved = XDocument.Load(fixture.ProtoPath);
                Assert.Equal("CustomUnit", (string?)saved.Descendants("damagebonus").Single().Attribute("type"));
                Assert.Equal("2", saved.Descendants("damagebonus").Single().Value);
                host.Call("BuildEditorPanel", "Owner", false);
                Assert.Equal("CustomUnit", DamageBonusTarget(host).Text);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void SelectorCatalogTacticsMixedFieldsAndDamageBonusUseHostContext()
    {
        using var fixture = new BaselineSafetyFixture(); CatalogMod(fixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = new BaselineHost(new RootGameData(fixture.GameRoot));
            var source = XDocument.Parse("<tactics><action><name>HandAttack</name><type>Attack</type><damagebonus type='BaseUnit'>2</damagebonus></action><tactic>Default<action priority='100'>HandAttack</action></tactic></tactics>");
            var original = new XDocument(source); XDocument? saved = null;
            try
            {
                await child.InitializeTacticsActionEditorAsync("Catalog.tactics", false, source,
                    document => { saved = new XDocument(document); return Task.FromResult(TacticsDocumentSaveOutcome.Saved); },
                    () => (IEnumerable<string>)owner.Call("GetAvailableTrainUnitNames")!,
                    () => (IEnumerable<string>)owner.Call("GetKnownUnitTypeNames")!, owner);
                child.Show(); Dispatcher.UIThread.RunJobs();
                AssertMixedCatalog((IEnumerable<string>)child.Call("GetAvailableBuildLimitTargets")!);
                AssertMixedCatalog((IEnumerable<string>)child.Call("GetProtoActionStructuredAttributeSuggestions", "rate", "type")!);
                var target = DamageBonusTarget(child); AssertMixedCatalog(target.ItemsSource!.Cast<string>());
                target.SelectedItem = "CustomUnit"; target.Text = "CustomUnit";
                await FinishCatalogSave(child, (Task)child.Call("SaveTacticsActionEditorAsync")!);
                Assert.NotNull(saved);
                Assert.Equal("CustomUnit", (string?)saved.Descendants("damagebonus").Single().Attribute("type"));
                Assert.True(XNode.DeepEquals(original, source));
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void SelectorCatalogStandaloneAbilityExcludesSyntheticUnitAndSavesCustomTarget()
    {
        using var fixture = new BaselineSafetyFixture(); CatalogMod(fixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = new BaselineHost(new RootGameData(fixture.GameRoot));
            var source = XElement.Parse("<power name='CatalogAbility' type='GeneralEffect'><placement>Unit</placement><abstractplacementtargettype>BaseUnit</abstractplacementtargettype><future keep='yes'/></power>");
            var original = new XElement(source); XElement? saved = null;
            try
            {
                await child.InitializeAbilityDefinitionEditorAsync("CatalogAbility", false, source, fixture.ProtoPath,
                    (_, power, _, _) => { saved = new XElement(power); return Task.CompletedTask; }, ["CatalogAbility"],
                    () => (IEnumerable<string>)owner.Call("GetAvailableTrainUnitNames")!,
                    () => (IEnumerable<string>)owner.Call("GetKnownUnitTypeNames")!);
                child.Show(); child.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                AssertMixedCatalog((IEnumerable<string>)child.Call("GetAvailableBuildLimitTargets")!);
                var target = child.GetVisualDescendants().OfType<AutoCompleteBox>().First(box =>
                    box.ItemsSource?.Cast<object>().Contains("CustomUnit") == true);
                Assert.DoesNotContain(target.ItemsSource!.Cast<string>(), name => name.StartsWith("__", StringComparison.Ordinal));
                target.SelectedItem = "CustomUnit";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                await FinishCatalogSave(child, (Task)child.Call("SaveAbilityDefinitionEditorAsync")!);
                Assert.NotNull(saved);
                Assert.Equal("GeneralEffect", (string?)saved.Attribute("type"));
                Assert.Contains(saved.Elements(), field =>
                    field.Name.LocalName is "abstractplacementtargettype" or "explicitlyrestrictedplacementtargettype" && field.Value == "CustomUnit");
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
                Assert.True(XNode.DeepEquals(original, source));
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }
}
