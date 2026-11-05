using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodPresentationCheckpointTests
{
    [Fact]
    public void EmptyInternalNameStillBlocksSave()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        var bytes = File.ReadAllBytes(fixture.GodPath);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                Dispatcher.UIThread.RunJobs();
                host.DetachGodDialogOwner();
                Field<TextBox>(host.God!, "_nameBox").Text = "";
                Dispatcher.UIThread.RunJobs();
            });
            Assert.False(AvaloniaTestHost.RunAsync(host.God!.SaveAsync));
            Assert.Equal(bytes, File.ReadAllBytes(fixture.GodPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void EmptyOptionalCleanupKeepsZeroAndUnknownMetadata()
    {
        var god = XElement.Parse("<civ><name>G</name><icon> </icon><portrait custom='keep'/><titleid><!--keep--></titleid><buildingefficiency>0</buildingefficiency><extrabuildingefficiency/><future/></civ>");
        MajorGodCatalog.RemoveEmptyOptionalFields(god);
        Assert.Null(god.Element("icon"));
        Assert.Null(god.Element("extrabuildingefficiency"));
        Assert.Equal("0", god.Element("buildingefficiency")?.Value);
        Assert.Equal("keep", (string?)god.Element("portrait")?.Attribute("custom"));
        Assert.Single(god.Element("titleid")!.Nodes().OfType<XComment>());
        Assert.NotNull(god.Element("future"));
    }

    [Fact]
    public void NewGodSavesWithOnlyInternalNameEntered()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.DetachGodDialogOwner();
                await host.God!.AddMajorGodAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.True(await host.God.SaveAsync());
            });
            var god = MajorGodCatalog.Find(XDocument.Load(fixture.GodPath), "NewMajorGod")!;
            Assert.Equal(new[] { "name", "statsid" }, god.Elements().Select(e => e.Name.LocalName));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void ClearingOptionalFormFieldsOmitsThemOnDisk()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = host.God!;
                host.GodHost.Content = null;
                host.Content = view;
                host.Show();
                Dispatcher.UIThread.RunJobs();
                foreach (var name in new[] { "_displayNameBox", "_titleBox", "_rolloverNameBox", "_keyBox" })
                    Field<TextBox>(view, name).Text = "";
                foreach (var name in new[] { "_cultureBox", "_ageTechBox", "_deathmatchTechBox", "_minimapBorderBox", "_backgroundScenarioBox" })
                    Field<AutoCompleteBox>(view, name).Text = "";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("", Field<AutoCompleteBox>(view, "_cultureBox").Text);
                host.Content = null;
                host.DetachGodDialogOwner();
                Assert.True(await view.SaveAsync());
            });
            var god = MajorGodCatalog.Find(XDocument.Load(fixture.GodPath), "CheckpointGod")!;
            foreach (var tag in new[] { "displaynameid", "titleid", "rollovernameid", "key", "culture", "agetech", "deathmatchtech", "minimapborderimage", "backgroundscenario" })
                Assert.Null(god.Element(tag));
            Assert.NotNull(god.Element("future"));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("<key>É</key>")]
    [InlineData("<culture>NotInCatalog</culture>")]
    [InlineData("<agetech age='WrongAge'><tech>X</tech></agetech>")]
    [InlineData("<startingunits><unit count='-1'>Hoplite</unit></startingunits>")]
    [InlineData("<buildingefficiency>-2</buildingefficiency>")]
    public void OptionalDoesNotAllowInvalidPopulatedValues(string invalid)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name>" + invalid + "</civ></civsmods>");
        var bytes = File.ReadAllBytes(fixture.GodPath);
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(() => { Dispatcher.UIThread.RunJobs(); host.DetachGodDialogOwner(); });
            Assert.False(AvaloniaTestHost.RunAsync(host.God!.SaveAsync));
            Assert.Equal(bytes, File.ReadAllBytes(fixture.GodPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    private static T Field<T>(MajorGodEditorView view, string name) =>
        (T)typeof(MajorGodEditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;

    [Fact]
    public void FieldOrderMatchesFormAndPreservesUnknownContent()
    {
        var god = XElement.Parse("<civ><name>G</name><!--keep--><culture>C</culture><key>A</key><displaynameid>D</displaynameid><rollovernameid>R</rollovernameid><titleid>T</titleid><agetech age='ArchaicAge'><tech>X</tech></agetech><startingresources><food>1</food></startingresources><icon custom='yes'>I</icon><portrait>P</portrait><breakoutportrait>B</breakoutportrait><future><nested/></future></civ>");
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(new[] { "name", "displaynameid", "titleid", "rollovernameid", "icon", "portrait", "breakoutportrait", "culture", "key", "agetech", "startingresources", "future" }, god.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("yes", (string?)god.Element("icon")?.Attribute("custom"));
        Assert.NotNull(god.Element("future")?.Element("nested"));
        Assert.Single(god.Nodes().OfType<XComment>());
        var once = god.ToString(SaveOptions.DisableFormatting);
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(once, god.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void ActualSaveUsesFormOrder()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                Dispatcher.UIThread.RunJobs();
                host.DetachGodDialogOwner();
                host.ChangeGodKey();
            });
            Assert.True(AvaloniaTestHost.RunAsync(host.God!.SaveAsync));
            Assert.Equal("Changes saved.", AvaloniaTestHost.Run(() => host.FindControl<TextBlock>("_statusMessage")!.Text));
            Assert.Equal("", AvaloniaTestHost.Run(() => host.GodStatus));
            var god = XDocument.Load(fixture.GodPath).Root!.Element("civ")!;
            var fields = god.Elements().Select(e => e.Name.LocalName).ToList();
            Assert.True(fields.IndexOf("titleid") < fields.IndexOf("rollovernameid"));
            Assert.True(fields.IndexOf("icon") < fields.IndexOf("culture"));
            Assert.True(fields.IndexOf("backgroundscenario") < fields.IndexOf("agetech"));
            Assert.NotNull(god.Element("future"));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void DeleteShowsEmptySelectionAndReselectionRestoresForm()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = host.God!;
                Dispatcher.UIThread.RunJobs();
                var saved = view.LoadSavedMajorGodElement("CheckpointGod");
                host.DetachGodDialogOwner();
                // Component-only draft presentation; immediate host deletion is
                // covered by ImmediateEntityDeletionTests, including confirmations.
                view.ImmediateDeletionRequestedAsync = null;
                await view.DeleteCurrentMajorGodAsync();
                Assert.Null(view.CurrentMajorGodName);
                Assert.False(Field<TabControl>(view, "_majorGodTabs").IsVisible);
                Assert.False(Field<Grid>(view, "_majorGodIdentityRow").IsVisible);
                Assert.True(Field<TextBlock>(view, "_emptySelectionMessage").IsVisible);
                view.DiscardMajorGodChanges("CheckpointGod", saved);
                view.SelectMajorGod("CheckpointGod");
                Dispatcher.UIThread.RunJobs();
                Assert.True(Field<TabControl>(view, "_majorGodTabs").IsVisible);
                Assert.False(Field<TextBlock>(view, "_emptySelectionMessage").IsVisible);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
