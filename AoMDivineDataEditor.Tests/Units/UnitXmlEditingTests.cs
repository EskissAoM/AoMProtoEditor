using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Initial = "<protomods future='root'><unit name='A'><maxhitpoints>10</maxhitpoints><unittype>Unit</unittype><!--keep--><future keep='yes'/></unit><unit name='B'><maxhitpoints>20</maxhitpoints><future sibling='yes'/></unit></protomods>";
    private static T Field<T>(BaselineHost host, string name)
        => (T)typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host)!;
    private static TextEditor Raw(BaselineHost host) => host.FindControl<TextEditor>("_xmlPreviewText")!;

    private static void WithHost(Func<BaselineSafetyFixture, BaselineHost, Task> check, string initial = Initial,
        Action<BaselineSafetyFixture>? setup = null)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, initial);
        setup?.Invoke(fixture);
        // RunAsync blocks the test thread until the dispatched async body and its
        // finally block finish; keep the fixture alive for that entire interval.
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("InitializeEditorTabs");
                host.Call("BuildEditorPanel", "A", true);
                host.Show(); Dispatcher.UIThread.RunJobs();
                host.Set("_suppressEditorChangeTracking", false);
                await check(fixture, host);
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
    }

    private static async Task Unlock(BaselineHost host)
    {
        host.FindControl<Button>("_editUnitXmlButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; Raw(host).IsReadOnly && i < 200; i++) await Task.Delay(10);
        Assert.False(Raw(host).IsReadOnly);
    }

    private static async Task<List<string>> Save(BaselineHost host)
    {
        var errors = new List<string>();
        host.Call("Save_Click", null!, new RoutedEventArgs());
        for (var i = 0; Field<bool>(host, "_isUnitSaveInProgress") && i < 1500; i++)
        {
            if (host.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup)
            { errors.Add(popup.PromptTitle + ": " + popup.PromptText); popup.Close(); }
            await Task.Delay(10);
        }
        Assert.False(Field<bool>(host, "_isUnitSaveInProgress"), "Global Unit Save did not finish.");
        return errors;
    }

    [Fact]
    public void MainXmlStartsLockedAndCleanUnlockDoesNotChangeData()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            Assert.True(Raw(host).IsReadOnly);
            Assert.False(Field<bool>(host, "_isDirty"));
            await Unlock(host);
            Assert.False(host.FindControl<Grid>("_unitStructuredForm")!.IsEnabled);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.True(Raw(host).IsReadOnly);
            Assert.True(host.FindControl<Grid>("_unitStructuredForm")!.IsEnabled);
            Assert.True(XNode.DeepEquals(XDocument.Parse(System.Text.Encoding.UTF8.GetString(before)), XDocument.Load(fixture.ProtoPath)));
        });

    [Fact]
    public void GlobalSaveValidatesRawUnitAndPreservesExactStructureAndSibling()
        => WithHost(async (fixture, host) =>
        {
            await Unlock(host);
            Raw(host).Text = "<unit name='A' future='yes'><maxhitpoints note='keep'>17</maxhitpoints><!--keep--><future keep='yes'><child/></future></unit>";
            Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Contains("A", Field<HashSet<string>>(host, "_dirtyUnitNames"));
            host.Call("UpdateXmlPreview", true);
            Assert.Contains("note='keep'", Raw(host).Text);
            Assert.Empty(await Save(host));
            Assert.True(Raw(host).IsReadOnly);
            Assert.False(Field<bool>(host, "_isDirty"));
            var document = XDocument.Load(fixture.ProtoPath);
            var unit = document.Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "A");
            Assert.Equal("17", unit.Element("maxhitpoints")!.Value);
            Assert.Equal("keep", (string?)unit.Element("maxhitpoints")!.Attribute("note"));
            Assert.NotNull(unit.Element("future")!.Element("child"));
            Assert.Null(unit.Element("unittype")); // No default fields reintroduced by form serialization.
            Assert.Contains(unit.Nodes().OfType<XComment>(), comment => comment.Value == "keep");
            Assert.True(XNode.DeepEquals(XDocument.Parse(Initial).Root!.Elements("unit").Last(), document.Root.Elements("unit").Last()));
            host.Call("BuildEditorPanel", "A", false);
            Assert.Contains("<maxhitpoints note=\"keep\">17</maxhitpoints>", Raw(host).Text);
        });

    [Theory]
    [InlineData("<unit>")]
    [InlineData("<tech name='A'/>")]
    [InlineData("<unit name='B'/>")]
    [InlineData("<unit name='bad name'/>")]
    [InlineData("<unit name='A'><maxhitpoints>-1</maxhitpoints></unit>")]
    [InlineData("<unit name='A'><movementtype>NewUnknownMovement</movementtype></unit>")]
    [InlineData("<unit name='A'><birthprotoaction>MissingAction</birthprotoaction></unit>")]
    [InlineData("<unit name='A'><protoaction><name>NewAction</name></protoaction></unit>")]
    public void InvalidRawSaveKeepsDiskAndBufferAndCanBeDiscarded(string text)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            await Unlock(host); Raw(host).Text = text;
            var errors = await Save(host);
            Assert.NotEmpty(errors);
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.Equal(text, Raw(host).Text);
            Assert.False(Raw(host).IsReadOnly);
            Assert.True(Field<bool>(host, "_isDirty"));
            Assert.True(await (Task<bool>)host.Call("CaptureCurrentUnitDraftAsync")!); // Invalid XML cannot trap navigation.
            var kind = typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!;
            var tab = host.Call("CreateDocumentTab", true, Enum.Parse(kind, "Units"), true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XDocument.Parse(Initial).Root!.Element("unit"));
            host.Call("DiscardUnitDocumentChanges", tab);
            host.Call("BuildEditorPanel", "A", false);
            Assert.True(Raw(host).IsReadOnly);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        });

    [Fact]
    public void SwitchingRecordsRetainsOnlyThatRecordsRawModeAndBuffer()
        => WithHost(async (_, host) =>
        {
            await Unlock(host); Raw(host).Text = "<unit name='A'><unfinished>";
            host.Call("BuildEditorPanel", "B", false);
            Assert.True(Raw(host).IsReadOnly);
            Assert.True(host.FindControl<Grid>("_unitStructuredForm")!.IsEnabled);
            host.Call("BuildEditorPanel", "A", false);
            Assert.False(Raw(host).IsReadOnly);
            Assert.Equal("<unit name='A'><unfinished>", Raw(host).Text);
        });

    [Fact]
    public void UndoRawTextPreservesPreexistingFormDraft()
        => WithHost(async (_, host) =>
        {
            var fields = Field<IDictionary<string, Control>>(host, "_fieldControls");
            ((TextBox)fields["maxhitpoints"]).Text = "12";
            host.Call("MarkDirty");
            await Unlock(host);
            var baseline = Raw(host).Text;
            Raw(host).Text = "broken"; Raw(host).Text = baseline;
            Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.Contains("12", Raw(host).Text);
        });

    [Fact]
    public void RawRootRenameUsesReferenceAndOwnedStringLifecycle()
        => WithHost(async (fixture, host) =>
        {
            await Unlock(host);
            Raw(host).Text = Raw(host).Text.Replace("name=\"A\"", "name=\"Renamed\"", StringComparison.Ordinal);
            Assert.Empty(await Save(host));
            var disk = XDocument.Load(fixture.ProtoPath);
            Assert.Null(disk.Root!.Elements("unit").FirstOrDefault(unit => (string?)unit.Attribute("name") == "A"));
            var renamed = disk.Root.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "Renamed");
            Assert.Equal("STR_UNIT_RENAMED_NAME", renamed.Element("displaynameid")!.Value);
            Assert.Equal("Renamed", XDocument.Load(fixture.GodPath).Descendants("startingunits").Single().Element("unit")!.Value);
            var strings = StringTableDocument.Parse(File.ReadAllText(fixture.Workspace.GetPath("TestMod/game/data/strings/English/stringmods.txt"))).ToDictionary();
            Assert.Equal("Unit A", strings["STR_UNIT_RENAMED_NAME"]);
            Assert.False(strings.ContainsKey("STR_UNIT_A_NAME"));
            Assert.True(Raw(host).IsReadOnly);
        }, Initial.Replace("<maxhitpoints>10", "<displaynameid>STR_UNIT_A_NAME</displaynameid><maxhitpoints>10", StringComparison.Ordinal), fixture =>
        {
            fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", "ID = \"STR_UNIT_A_NAME\" ; Str = \"Unit A\"\r\n");
            var gods = XDocument.Load(fixture.GodPath);
            gods.Root!.Element("civ")!.Add(new XElement("startingunits", new XElement("unit", new XAttribute("count", 1), "A")));
            gods.Save(fixture.GodPath);
        });

    [Fact]
    public void NewerRawTextSurvivesPreparedSaveAndCanBeSavedAgain()
        => WithHost(async (fixture, host) =>
        {
            await Unlock(host); Raw(host).Text = "<unit name='A'><maxhitpoints>12</maxhitpoints></unit>";
            Assert.True(await (Task<bool>)host.Call("PrepareUnitXmlForSaveAsync")!);
            // Global Save prepares Unit intent before awaiting the other editors.
            // Exercise its actual core write after new input at that boundary.
            host.Set("_unitXmlPreparedForGlobalSave", true);
            Raw(host).Text = "<unit name='A'><maxhitpoints>13</maxhitpoints></unit>";
            await ((Task)host.Call("SaveCurrentUnitCoreAsync", null!, new RoutedEventArgs())!).WaitAsync(TimeSpan.FromSeconds(10));
            host.Set("_unitXmlPreparedForGlobalSave", false);
            Assert.Equal("12", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("maxhitpoints")!.Value);
            Assert.Contains(">13<", Raw(host).Text);
            Assert.False(Raw(host).IsReadOnly);
            Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.Equal("13", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("maxhitpoints")!.Value);
            Assert.True(Raw(host).IsReadOnly);
        });

    [Theory]
    [MemberData(nameof(CatalogActionReferenceContractTests.UnitFieldTags), MemberType = typeof(CatalogActionReferenceContractTests))]
    public void RawRootRenameMigratesAnotherUnitsActionTargetOnGlobalSave(string field)
        => WithHost(async (fixture, host) =>
        {
            await Unlock(host);
            Raw(host).Text = Raw(host).Text.Replace("name=\"A\"", "name=\"Renamed\"", StringComparison.Ordinal);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            Assert.DoesNotContain(saved.Root!.Elements("unit"), unit => (string?)unit.Attribute("name") == "A");
            Assert.Contains(saved.Root.Elements("unit"), unit => (string?)unit.Attribute("name") == "Renamed");
            var other = saved.Root.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "B");
            var target = other.Element("protoaction")!.Element(field)!;
            Assert.Equal("Renamed", (string?)target.Attribute("type"));
            Assert.Equal("2", target.Value);
            Assert.Equal("yes", (string?)target.Attribute("keep"));
            Assert.Equal("Attack", other.Element("protoaction")!.Element("type")!.Value);
            Assert.True(Raw(host).IsReadOnly);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, Initial.Replace("<maxhitpoints>20</maxhitpoints>",
            $"<maxhitpoints>20</maxhitpoints><protoaction><name>HandAttack</name><type>Attack</type><{field} type='A' keep='yes'>2</{field}></protoaction>",
            StringComparison.Ordinal), fixture =>
        {
            // Real rename identity checks require a complete opposite catalog,
            // including types observed in every base Proto part. Reuse the BAR/XMB
            // fixtures rather than substituting a partial suggestion cache.
            var proto = XmbReaderTests.BuildDocument(XElement.Parse("<proto><unit name='Base'><unittype>Unit</unittype></unit></proto>"));
            var types = XmbReaderTests.BuildDocument(XElement.Parse("<abstractunittypes><abstractunittype>Unit</abstractunittype></abstractunittypes>"));
            using var archive = BarArchiveTests.BuildArchive(
                new BarArchiveTests.TestEntry("proto.xml.xmb", proto, proto.Length, false),
                new BarArchiveTests.TestEntry("abstract_unit_types.xml.xmb", types, types.Length, false));
            File.WriteAllBytes(Path.Combine(fixture.GameRoot, "data", "Data.bar"), archive.ToArray());
        });

    [Fact]
    public void LegacyClosedOptionCanStayUnchangedWhileOtherRawDataChanges()
        => WithHost(async (_, host) =>
        {
            await Unlock(host);
            Raw(host).Text = Raw(host).Text.Replace(">10</maxhitpoints>", ">11</maxhitpoints>", StringComparison.Ordinal);
            Assert.Empty(await Save(host));
            Assert.Contains("LegacyMovement", Raw(host).Text);
        }, Initial.Replace("<maxhitpoints>10", "<movementtype>LegacyMovement</movementtype><maxhitpoints>10", StringComparison.Ordinal));

    [Fact]
    public void BaseGameXmlRemainsSelectableAndCannotBeUnlocked()
        => WithHost((_, host) =>
        {
            host.Set("_barXmlRoot", XElement.Parse("<proto><unit name='Original'><maxhitpoints>1</maxhitpoints></unit></proto>"));
            host.Call("BuildEditorPanel", "Original", false);
            Assert.True(Raw(host).IsReadOnly);
            Assert.True(Raw(host).IsEnabled);
            Assert.True(Raw(host).Focusable);
            Assert.False(host.FindControl<Button>("_editUnitXmlButton")!.IsVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public void InvalidPinnedXmlCanCloseThroughTheRealDiscardPrompt()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
            var tab = host.Call("CreateDocumentTab", false, kind, true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XDocument.Parse(Initial).Root!.Element("unit"));
            var tabs = Field<IList>(host, "_openDocumentTabs"); tabs.Add(tab);
            Field<IList>(host, "_documentTabItems").Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
            host.Set("_activeDocumentTab", tab);
            await Unlock(host); Raw(host).Text = "<unit name='A'><unfinished>";
            var closing = (Task)host.Call("CloseDocumentTabAsync", tab)!;
            for (var i = 0; !host.OwnedWindows.OfType<Prompt>().Any() && i < 200; i++) await Task.Delay(10);
            var popup = Assert.Single(host.OwnedWindows.OfType<Prompt>());
            Assert.Equal("Unsaved tab", popup.PromptTitle);
            popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain(tab, tabs.Cast<object>());
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        });

    [Fact]
    public void ExternalReloadReplacesConflictingRawDraftAndRelocksIt()
        => WithHost(async (fixture, host) =>
        {
            await Unlock(host); Raw(host).Text = "<unit name='A'><maxhitpoints>12</maxhitpoints></unit>";
            File.WriteAllText(fixture.ProtoPath, Initial.Replace(">10</maxhitpoints>", ">31</maxhitpoints>", StringComparison.Ordinal));
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var reloading = (Task<bool>)host.Call("ReloadExternallyChangedUnitFilesAsync")!;
            for (var i = 0; !host.OwnedWindows.OfType<Prompt>().Any() && i < 200; i++) await Task.Delay(10);
            var popup = Assert.Single(host.OwnedWindows.OfType<Prompt>());
            Assert.Equal("ProtoUnits changed outside the editor", popup.PromptTitle);
            popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await reloading.WaitAsync(TimeSpan.FromSeconds(10))); // Review before retrying Save.
            Assert.True(Raw(host).IsReadOnly);
            Assert.Contains(">31</maxhitpoints>", Raw(host).Text);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        });
}
