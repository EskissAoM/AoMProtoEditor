using System.Xml.Linq;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceIntegrationG2Tests
{
    [Fact]
    public void DirtyPowerAcknowledgementDetectsSameEntryMigrationConflict()
    {
        using var workspace = new TestWorkspace("g2-dirty-power");
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='P' type='TempUnit'><cost>1</cost><attachproto>Old</attachproto></power></powersmod>");
        var view = AvaloniaTestHost.Run(() => new PowerEditorView(new PowerEditorComposition([], path)));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                view.SetModifiedMode(true);
                view.SelectPower("P");
                ((TextBox)typeof(PowerEditorView).GetField("_costBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!).Text = "2";
            });
            Assert.True(AvaloniaTestHost.RunAsync(view.CommitCurrentPowerAsync));
            Assert.True(AvaloniaTestHost.Run(() => view.IsDirty));
            var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
                GameIdentifierKind.ProtoUnit, [new("Old", "New")]);
            var before = File.ReadAllBytes(path);
            Assert.Throws<IOException>(() => AvaloniaTestHost.Run(() => view.ValidateReferenceMigration(plan.GetDocument(path))));
            Assert.Equal(before, File.ReadAllBytes(path));
            FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                () => plan.Save((doc, target) => PreservedXmlFile.SaveAtomic(doc, target)));
            // Characterizes the existing safe refusal, not an acceptance of the host's ordering.
            Assert.Throws<IOException>(() => AvaloniaTestHost.Run(view.AcknowledgePowerFileSavedByEditor));
            Assert.True(AvaloniaTestHost.Run(() => view.IsDirty));
            Assert.Equal("1", XDocument.Load(path).Descendants("cost").Single().Value);
            Assert.Equal("New", XDocument.Load(path).Descendants("attachproto").Single().Value);
        }
        finally { AvaloniaTestHost.CollectUiGarbage(); }
    }

    [Fact]
    public void UnitSaveValidatesDirtyPowerBeforeEnteringTheFileTransaction()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var prepare = source.IndexOf("var migrationPreflight = GameIdentifierReferenceMigrationPlan.Prepare(", StringComparison.Ordinal);
        var guard = source.IndexOf("_powerView?.ValidateReferenceMigration(migrationPreflight.GetDocument(", prepare, StringComparison.Ordinal);
        var transaction = source.IndexOf("FileIntegrityTransaction.Execute(", prepare, StringComparison.Ordinal);
        Assert.True(prepare >= 0 && guard > prepare && transaction > guard);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TechnologyMigrationRefreshesGodViewAndDiscardSnapshot(bool pinned, bool visible)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var tab = host.AddGodTab(pinned, visible);
                var previousContent = host.GodHost.Content;
                var plan = TechnologyReferenceMigrationPlan.Prepare(
                    [new(fixture.GodPath, TechnologyReferenceDocumentKind.MajorGods, "Gods")],
                    [new("FixtureAge", "NewAge")]);
                Assert.Single(plan.Paths);
                Assert.Empty(plan.UnknownReferences);
                FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                    () => plan.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path)));
                host.Call("AdoptExternalTechnologyReferenceMigration", plan);
                Assert.NotNull(host.God);
                Assert.Equal("CheckpointGod", host.God.CurrentMajorGodName);
                Assert.Same(visible ? host.God : previousContent, host.GodHost.Content);
                var saved = BaselineHost.SavedGod(tab);
                Assert.Equal("NewAge", saved.Element("agetech")!.Element("tech")!.Value);
                host.God.DiscardMajorGodChanges("CheckpointGod", saved);
                Assert.False(host.God.IsDirty);
                Assert.Equal("NewAge", host.God.CreateModifiedDocumentSnapshot()!.Descendants("agetech").Single().Element("tech")!.Value);
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("preserved"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
