using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Services;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(BaselineHost host, string name)
        => typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host);
    private static object? CallStatic(string name, params object[] args)
        => typeof(ProtoEditorWindow).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
    private const string BaseXml = "<proto><unit name='BaseUnit'><unittype>AbstractBase</unittype><protoaction><name>HandAttack</name><type>Attack</type><anim>attack_anim</anim></protoaction></unit></proto>";

    private static void Seed(BaselineHost owner, string path, BarArchive archive, ProtoBarData data, XElement root)
    {
        owner.Set("_barData", data);
        owner.Set("_barXmlRoot", root);
        owner.Set("_protoDataBarFile", archive);
        owner.Set("_protoDataBarPath", path);
        owner.Set("_loadedTacticsBaseSource", CallStatic("CaptureTacticsBaseSource", path)!);
        owner.Set("_barProtoActionAnimationNames", new List<string> { "attack_anim" });
        owner.Set("_barProtoActionAttachmentPaths", new List<string> { "attach.xml" });
        owner.Set("_barProtoActionModelAttachmentBones", new List<string> { "hand" });
        owner.Set("_barProtoActionSuggestionDataLoaded", true);
        ((Dictionary<string, string>)Field(owner, "_globalTacticsActionTypeMap")!)["HandAttack"] = "Attack";
    }

    private static BaselineHost SeedOwner(BaselineSafetyFixture fixture)
    {
        var bytes = XmbReaderTests.BuildDocument(XElement.Parse(BaseXml));
        using var encoded = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry("proto.xml.xmb", bytes, bytes.Length, false));
        var path = Path.Combine(fixture.GameRoot, "data", "Data.bar");
        File.WriteAllBytes(path, encoded.ToArray());
        var archive = new BarArchive(encoded);
        Assert.True(archive.Load(out _));
        var owner = fixture.CreateHost(false);
        var parsed = ProtoDataExtractor.ExtractProtoData(BaseXml);
        Seed(owner, path, archive, parsed.Data, parsed.Root);
        return owner;
    }

    [Fact]
    public void ProtoDataCloneIsIndependentAtEveryMutableCollectionLevel()
    {
        var data = new ProtoBarData();
        foreach (var property in typeof(ProtoBarData).GetProperties())
            if (property.GetValue(data) is IList list) list.Add(property.Name);
        data.ProtoActionTypes["Attack"] = "Attack";
        data.ProtoActionFieldUsageByType["Attack"] = new(StringComparer.OrdinalIgnoreCase) { ["rate"] = 2 };
        data.ProtoActionNamesByType["Attack"] = ["HandAttack"];
        var before = JsonSerializer.Serialize(data);
        var clone = data.Clone();
        Assert.Equal(before, JsonSerializer.Serialize(clone));
        foreach (var property in typeof(ProtoBarData).GetProperties())
            Assert.NotSame(property.GetValue(data), property.GetValue(clone));
        clone.ProtoActionFieldUsageByType["attack"]["rate"] = 99;
        clone.ProtoActionNamesByType["attack"].Clear();
        foreach (var property in typeof(ProtoBarData).GetProperties())
        {
            if (property.GetValue(clone) is IList list) list.Clear();
            else if (property.GetValue(clone) is IDictionary dictionary) dictionary.Clear();
            else throw new InvalidOperationException($"Add clone coverage for {property.Name}.");
        }
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void WarmReuseCopiesBaseDataAndSuggestionsWithoutCopyingDraftsOrStaleAssetLists()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = fixture.CreateHost(false);
            var modBytes = File.ReadAllBytes(fixture.ProtoPath);
            owner.Set("_baseGameIconPaths", new[] { "stale.png" });
            owner.Set("_baseGameAnimFiles", new[] { new AnimFileCatalogEntry("stale.xml", "missing.bar") });
            try
            {
                var childDraft = Field(child, "_modXmlDoc");
                Assert.True(await (Task<bool>)child.Call("LoadTacticsEditorGameDataAsync", owner)!);
                Assert.Same(childDraft, Field(child, "_modXmlDoc"));
                Assert.NotSame(Field(owner, "_barData"), Field(child, "_barData"));
                Assert.NotSame(Field(owner, "_barXmlRoot"), Field(child, "_barXmlRoot"));
                Assert.Same(Field(owner, "_protoDataBarFile"), Field(child, "_protoDataBarFile"));
                Assert.True((bool)Field(child, "_barProtoActionSuggestionDataLoaded")!);
                Assert.Equal(new[] { "attack_anim" }, (List<string>)Field(child, "_barProtoActionAnimationNames")!);
                Assert.Empty((IReadOnlyList<string>)Field(child, "_baseGameIconPaths")!);
                Assert.Empty((IReadOnlyList<AnimFileCatalogEntry>)Field(child, "_baseGameAnimFiles")!);
                ((ProtoBarData)Field(child, "_barData")!).UnitNames.Clear();
                ((XElement)Field(child, "_barXmlRoot")!).RemoveNodes();
                ((List<string>)Field(child, "_barProtoActionAnimationNames")!).Clear();
                Assert.Contains("BaseUnit", ((ProtoBarData)Field(owner, "_barData")!).UnitNames);
                Assert.NotEmpty(((XElement)Field(owner, "_barXmlRoot")!).Elements());
                Assert.NotEmpty((List<string>)Field(owner, "_barProtoActionAnimationNames")!);
                Assert.Equal(modBytes, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("unprepared")]
    [InlineData("different-game")]
    [InlineData("mismatched-index")]
    [InlineData("missing-proto")]
    [InlineData("missing-root")]
    [InlineData("unloaded-index")]
    public void UnusableOrDifferentSourceRefusesReuseWithoutAdoptingState(string reason)
    {
        using var fixture = new BaselineSafetyFixture();
        using var other = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var owner = SeedOwner(fixture);
            var child = (reason == "different-game" ? other : fixture).CreateHost(false);
            try
            {
                var path = Path.Combine(fixture.GameRoot, "data", "Data.bar");
                if (reason == "changed") File.AppendAllText(path, "changed");
                if (reason == "missing") File.Delete(path);
                if (reason == "unprepared") owner.Set("_loadedTacticsBaseSource", null!);
                if (reason == "mismatched-index") owner.Set("_protoDataBarPath", Path.Combine(other.GameRoot, "data", "Data.bar"));
                if (reason == "missing-proto") owner.Set("_barData", null!);
                if (reason == "missing-root") owner.Set("_barXmlRoot", null!);
                if (reason == "unloaded-index") owner.Set("_protoDataBarFile", new BarArchive(new MemoryStream()));
                var previous = Field(child, "_barData");
                Assert.False((bool)child.Call("TryReuseTacticsBaseData", owner)!);
                Assert.Same(previous, Field(child, "_barData"));
                Assert.Null(Field(child, "_loadedTacticsBaseSource"));
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void RejectedReuseStillRunsTheExistingLoader()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = fixture.CreateHost(false);
            try
            {
                File.WriteAllText(Path.Combine(fixture.GameRoot, "data", "Data.bar"), "invalid archive");
                Assert.False(await (Task<bool>)child.Call("LoadTacticsEditorGameDataAsync", owner)!);
                Assert.Null(Field(child, "_protoDataBarFile"));
                Assert.Null(Field(child, "_loadedTacticsBaseSource"));
                Assert.Equal("Failed to load Data.bar.", ((TextBlock)Field(child, "_statusMessage")!).Text);
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData("", "unchanged")]
    [InlineData("payload", "unchanged")]
    [InlineData("", "remove")]
    [InlineData("payload", "clear")]
    public void FullTacticsInitializationPreservesTheDocumentAndCustomCatalogs(string value, string operation)
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = SeedOwner(fixture);
            var child = new BaselineHost(new RootGameData(fixture.GameRoot));
            var source = XDocument.Parse("<tactics><action><name>HandAttack</name><type>Attack</type><rate type='All'>1</rate><future keep='yes'/></action><tactic>Default<action priority='100'>HandAttack</action><attacktype>CustomUnit</attacktype></tactic></tactics>");
            source.Descendants("future").Single().Value = value;
            var before = new XDocument(source);
            XDocument? committed = null;
            try
            {
                await child.InitializeTacticsActionEditorAsync("Test.tactics", false, source,
                    document => { committed = new XDocument(document); return Task.FromResult(TacticsDocumentSaveOutcome.Saved); }, () => ["CustomUnit"], () => ["CustomType"], owner);
                child.Show();
                child.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(Field(owner, "_loadedTacticsBaseSource"), Field(child, "_loadedTacticsBaseSource"));
                Assert.False((bool)Field(child, "_isDirty")!);
                Assert.NotEmpty(((IList)Field(child, "_protoActionWidgets")!).Cast<object>());
                if (operation != "unchanged")
                {
                    var widget = ((IList)Field(child, "_protoActionWidgets")!)[0]!;
                    var controls = (Dictionary<string, Control>)widget.GetType().GetProperty("AdditionalFieldControls")!.GetValue(widget)!;
                    var editor = Assert.IsType<TextBox>(controls["future"]);
                    if (operation == "remove")
                    {
                        var row = Assert.IsType<Grid>(editor.Parent);
                        row.Children.OfType<Button>().Single(button => button.Classes.Contains("remove-button"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else editor.Text = "";
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True((bool)Field(child, "_isDirty")!);
                }
                var saved = (XDocument)child.Call("BuildTacticsActionEditorDocument", true)!;
                if (operation == "unchanged")
                {
                    Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
                    Assert.Equal(value, saved.Descendants("future").Single().Value);
                }
                else Assert.Empty(saved.Descendants("future"));
                Assert.Equal("CustomUnit", saved.Descendants("attacktype").Single().Value);
                Assert.Equal("HandAttack", saved.Descendants("tactic").Single().Element("action")!.Value);
                Assert.Contains("CustomUnit", (List<string>)child.Call("GetAvailableTrainUnitNames")!);
                Assert.Contains("CustomType", (List<string>)child.Call("GetKnownUnitTypeNames")!);
                Assert.True(XNode.DeepEquals(before, source));
                await (Task)child.Call("SaveTacticsActionEditorAsync")!;
                Assert.NotNull(committed);
                Assert.True(XNode.DeepEquals(saved, committed));
                Assert.Equal("Changes saved.", child.FindControl<TextBlock>("_statusMessage")!.Text);
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    [CorpusFact]
    public void ReadOnlyRealArchiveProfileComparesRepeatedWorkWithWarmReuse()
    {
        var path = OriginalCorpus.RequirePath();
        var timer = Stopwatch.StartNew();
        using var stream = File.OpenRead(path);
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out _));
        var headerMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var parts = archive.Entries!.Where(entry => entry.Name.Contains("proto", StringComparison.OrdinalIgnoreCase) &&
            entry.Name.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)).Select(entry =>
            (string?)CallStatic("ReadBarXmbXml", entry, stream)).Where(xml => xml != null).Select(xml =>
            xml!.StartsWith("<?xml", StringComparison.Ordinal) ? xml[(xml.IndexOf("?>", StringComparison.Ordinal) + 2)..] : xml);
        var combinedXml = "<protos>" + string.Concat(parts) + "</protos>";
        var decodeMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var parsed = ProtoDataExtractor.ExtractProtoData(combinedXml);
        var parseMs = timer.Elapsed.TotalMilliseconds;
        Assert.NotEmpty(parsed.Data.UnitNames);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var service = new RootGameData(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName);
            var owner = new BaselineHost(service);
            var child = new BaselineHost(service);
            try
            {
                Seed(owner, path, archive, parsed.Data, parsed.Root);
                owner.Set("_barProtoActionSuggestionDataLoaded", false);
                timer.Restart();
                owner.Call("EnsureBarProtoActionSuggestionDataLoaded");
                var suggestionsMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                await (Task)owner.Call("LoadBaseGameIconCatalogAsync", path)!;
                await (Task)owner.Call("LoadBaseGameAnimFileCatalogAsync", path)!;
                await (Task)owner.Call("LoadBaseGameSoundSetCatalogAsync", path)!;
                var catalogsMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                Assert.True(await (Task<bool>)child.Call("LoadTacticsEditorGameDataAsync", owner)!);
                var warmMs = timer.Elapsed.TotalMilliseconds;
                Assert.Equal(parsed.Data.UnitNames, ((ProtoBarData)Field(child, "_barData")!).UnitNames);
                Assert.True(XNode.DeepEquals(parsed.Root, (XElement)Field(child, "_barXmlRoot")!));
                Console.WriteLine($"Read-only real archive profile: header={headerMs:F1}ms; proto decode={decodeMs:F1}ms; proto parse/extract={parseMs:F1}ms; suggestions={suggestionsMs:F1}ms; cold catalogs={catalogsMs:F1}ms; warm reuse with catalog checks={warmMs:F1}ms. This is data preparation only, not full window opening; the disk-cache path avoids decoding but still parses proto data. No timing threshold is asserted.");
            }
            finally { child.DisposeFixtureWindow(); owner.DisposeFixtureWindow(); }
        });
    }

    private sealed class RootGameData(string root) : IEditorGameDataService
    {
        public string? RootDirectory => root;
        public ValueTask<string?> LookupStringKeyAsync(string key) => ValueTask.FromResult<string?>(null);
    }
}
