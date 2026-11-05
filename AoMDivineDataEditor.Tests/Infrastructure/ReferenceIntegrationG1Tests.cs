using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceIntegrationG1Tests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void SharedMigrationRefreshesViewAndDiscardSnapshot(bool power, bool pinned, bool visible, bool technologyRename = false)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = Path.Combine(Path.GetDirectoryName(fixture.ProtoPath)!, power ? "powers_mods.xml" : "techtree_mods.xml");
        File.WriteAllText(path, power
            ? "<powersmod><power name='P' type='TempUnit'><attachproto>Old</attachproto><future keep='yes'/></power></powersmod>"
            : "<techtreemods><tech name='T'><effects><effect type='Data' subtype='Damage' amount='1' relativity='Absolute'><target type='ProtoUnit'>Old</target></effect></effects><future keep='yes'/></tech></techtreemods>");
        if (technologyRename)
            File.WriteAllText(path, "<powersmod><power name='P' type='Rain'><effect type='TechActivate'><target type='Tech'>Old</target></effect><future keep='yes'/></power></powersmod>");
        var referenceTag = technologyRename ? "target" : power ? "attachproto" : "target";
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(ProtoEditorWindow).GetField(name, flags)!.GetValue(host);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.Call(power ? "EnsurePowerView" : "EnsureTechnologyView");
                var name = power ? "P" : "T";
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, power ? "Powers" : "Technologies");
                if (power)
                {
                    var view = (PowerEditorView)Field("_powerView")!;
                    view.SetModifiedMode(true);
                    view.SelectPower(name);
                }
                else
                {
                    var view = (TechnologyEditorView)Field("_technologyView")!;
                    view.SetModifiedMode(true);
                    view.SelectTechnology(name);
                }
                var tab = host.Call("CreateDocumentTab", !pinned, kind, true, name)!;
                var snapshot = tab.GetType().GetProperty("SavedElement")!;
                snapshot.SetValue(tab, power ? ((PowerEditorView)Field("_powerView")!).LoadSavedPowerElement(name)
                    : ((TechnologyEditorView)Field("_technologyView")!).LoadSavedTechnologyElement(name));
                ((IList)Field("_openDocumentTabs")!).Add(tab);
                if (visible)
                {
                    host.Set("_activeEntityKind", kind);
                    host.Set("_activeDocumentTab", tab);
                }
                else host.GodHost.Content = new TextBlock { Text = "Other editor" };
                var previousContent = host.GodHost.Content;
                var files = ModReferenceFileCatalog.Discover(Path.Combine(fixture.Workspace.RootPath, "TestMod"));
                if (technologyRename)
                {
                    var plan = TechnologyReferenceMigrationPlan.Prepare(files, [new("Old", "New")]);
                    Assert.Empty(plan.UnknownReferences);
                    FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                        () => plan.Save((doc, destination) => PreservedXmlFile.SaveAtomic(doc, destination)));
                    host.Call("AdoptExternalTechnologyReferenceMigration", plan);
                }
                else
                {
                    var plan = GameIdentifierReferenceMigrationPlan.Prepare(files,
                        GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: []);
                    FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
                        () => plan.Save((doc, destination) => PreservedXmlFile.SaveAtomic(doc, destination)));
                    host.Call("AdoptGameIdentifierReferenceMigration", plan);
                }
                var saved = Assert.IsType<XElement>(snapshot.GetValue(tab));
                Assert.Equal("New", saved.Descendants(referenceTag).Single().Value);
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
                if (power)
                {
                    var view = Assert.IsType<PowerEditorView>(Field("_powerView"));
                    Assert.Equal(name, view.CurrentPowerName);
                    view.DiscardPowerChanges(name, saved);
                    Assert.False(view.IsDirty);
                    Assert.Same(visible ? view : previousContent, host.GodHost.Content);
                }
                else
                {
                    var view = Assert.IsType<TechnologyEditorView>(Field("_technologyView"));
                    Assert.Equal(name, view.CurrentTechnologyName);
                    view.DiscardTechnologyChanges(name, saved);
                    Assert.False(view.IsDirty);
                    Assert.Same(visible ? view : previousContent, host.GodHost.Content);
                }
                Assert.Equal("New", XDocument.Load(path).Descendants(referenceTag).Single().Value);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
