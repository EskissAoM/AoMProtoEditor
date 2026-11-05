using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodPointerDiscardTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)(owner is BaselineHost ? typeof(ProtoEditorWindow) : owner.GetType())
        .GetField(name, Private)!.GetValue(owner)!;
    private static async Task Settle()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(40);
    }
    private static void Click(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, "The pointer test needs a laid out control.");
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void RenameWithCaretThenPointerDiscardAllowsNextGodWithoutPrompt(bool strings, bool actualProfile, bool incompleteShades)
    {
        using var fixture = new BaselineSafetyFixture();
        var document = new XDocument(new XElement("civsmods"));
        var table = "";
        foreach (var name in new[] { "GodA", "GodB", "GodC" })
        {
            var entry = new XElement("civ", new XElement("name", name), new XElement("statsid", "SP"), new XElement("culture", "Greek"), new XElement("key", name[^1]));
            if (strings)
                foreach (var field in new[] { "displaynameid", "titleid", "rollovernameid" })
                {
                    var id = MajorGodCatalog.BuildStringId(name, field); entry.Add(new XElement(field, id));
                    table += $"ID = \"{id}\" ; Str = \"{name} {field}\"\r\n";
                }
            document.Root!.Add(entry);
        }
        if (actualProfile)
        {
            document = XDocument.Load(Path.Combine(ProjectSource.RootPath, "AoMDivineDataEditor.Tests", "TestData", "MajorGodRenameDiscard.xml"), LoadOptions.PreserveWhitespace);
            table = string.Join("\r\n", document.Descendants().Where(element => !element.HasElements && element.Value.StartsWith("STR_", StringComparison.Ordinal))
                .Select(element => element.Value).Distinct().Select(id => $"ID = \"{id}\" ; Str = \"Saved {id}\""));
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/culture_mods.xml", "<culturesmods><culture><name>Greek</name></culture><culture><name>Egyptian</name></culture><culture><name>Atlantean</name></culture></culturesmods>");
            File.WriteAllText(fixture.TypePath, "<abstractunittypesmods><abstractunittype>AbstractArmory</abstractunittype><abstractunittype>AbstractArcher</abstractunittype><abstractunittype>AbstractWall</abstractunittype><abstractunittype>AbstractFortress</abstractunittype><abstractunittype>Building</abstractunittype></abstractunittypesmods>");
        }
        var firstGod = actualProfile ? "NewMajorGod" : "GodA";
        var secondGod = actualProfile ? "FuxiCopybaaazd" : "GodB";
        var thirdGod = actualProfile ? firstGod : "GodC";
        File.WriteAllText(fixture.GodPath, document.ToString());
        var loadedBaseline = XDocument.Load(fixture.GodPath, LoadOptions.PreserveWhitespace);
        var stringsPath = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", table);
        var before = new[] { fixture.GodPath, stringsPath }.ToDictionary(path => path, File.ReadAllBytes);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        if (actualProfile) AvaloniaTestHost.Run(() =>
        {
            // The God editor's shared reference catalog reads ProtoUnit XML, not
            // ProtoBarData.UnitNames. Include the copied record's VFX units too.
            string[] unitNames = ["VillagerChinese", "Kuafu", "Pioneer", "Achilles", "AjaxOlder", "Manor", "AgaveBloom", "Hersir", "AbeNoSeimei", "AcidPool", "TownCenter", "CitadelCenter", "VillageCenter", "House", "TentSPC", "Dock", "Temple", "SentryTower", "Armory", "Market", "Wonder", "Silo", "MachineWorkshop", "MachineWorkshopTower", "MachineWorkshopTrainingYard", "MilitaryCamp", "MilitaryCampTower", "MilitaryCampTrainingYard", "ImperialAcademy", "Baolei", "ThePeachBlossomSpring", "VFXCoinBurst", "VFXAbundanceSmall", "VFXAbundanceMedium", "VFXAbundanceLarge"];
            host.Set("_barXmlRoot", new XElement("proto",
                unitNames.Select(name => new XElement("unit", new XAttribute("name", name)))));
            host.Call("InvalidateSuggestionCaches", false);
            host.GodHost.Content = null; host.Set("_majorGodView", null!); host.Call("EnsureMajorGodView");
            var unitCatalog = Field<IReadOnlyList<string>>(host.God!, "_protoUnitNames");
            Assert.Equal(unitNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), unitCatalog);
            Assert.Contains("AbstractWall", Field<IReadOnlyList<string>>(host.God!, "_unitTypeNames"));
        });
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Call("InitializeEditorTabs");
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
                host.Set("_documentTabActivationInProgress", true);
                Field<TabStrip>(host, "_unitTabs").SelectedIndex = 1;
                host.Set("_documentTabActivationInProgress", false);
                host.Call("ApplyEntityKindUi", kind); host.Call("RefreshUnitList");
                host.Width = 1440; host.Height = 932; host.Show(); await Settle();
                var view = host.God!; var list = Field<ListBox>(host, "_unitList");
                var trace = new List<string>();
                string State(string action) => action + " current=" + view.CurrentMajorGodName + " dirty=" + view.IsDirty +
                    " names=" + string.Join(",", Field<HashSet<string>>(view, "_dirtyNames")) +
                    " updates=" + string.Join(",", Field<Dictionary<string, string>>(view, "_pendingStringUpdates").Keys) +
                    " raw=" + Field<bool>(view, "_rawEditorDirty");
                view.DirtyStateChanged += (_, _) =>
                {
                    trace.Add(State("dirty event"));
                    if (view.IsDirty && view.CurrentMajorGodName == secondGod)
                    {
                        var currentXml = Field<XElement>(view, "_currentEntry").ToString(SaveOptions.DisableFormatting);
                        var loadedXml = Field<XDocument>(view, "_loadedModDocument").Root!.Elements("civ").Single(entry => MajorGodCatalog.GetName(entry) == secondGod).ToString(SaveOptions.DisableFormatting);
                        var position = Enumerable.Range(0, Math.Min(currentXml.Length, loadedXml.Length)).FirstOrDefault(index => currentXml[index] != loadedXml[index]);
                        var begin = Math.Max(0, position - 50);
                        trace.Add("First XML difference at " + position + " loaded=" + loadedXml.Substring(begin, Math.Min(180, loadedXml.Length - begin)) + " current=" + currentXml.Substring(begin, Math.Min(180, currentXml.Length - begin)));
                    }
                };
                view.BrowserStateChanged += (_, _) => trace.Add(State("browser event"));
                async Task ClickGod(string name)
                {
                    var items = Field<IEnumerable>(host, "_filteredUnitNames").Cast<object>().ToArray();
                    var index = Array.FindIndex(items, item => (string)item.GetType().GetProperty("UnitName")!.GetValue(item)! == name);
                    Assert.True(index >= 0);
                    var container = list.ContainerFromIndex(index); Assert.NotNull(container);
                    Click(host, container); await Settle(); trace.Add(State("clicked " + name));
                }
                await ClickGod(firstGod);
                Assert.Equal(firstGod, view.CurrentMajorGodName);
                if (incompleteShades) Field<XElement>(view, "_currentEntry").Add(new XElement("shades"));
                var nameBox = Field<TextBox>(view, "_nameBox"); Click(host, nameBox); nameBox.SelectAll();
                host.KeyTextInput("GodARenamed"); await Settle(); Assert.True(nameBox.IsFocused);
                Assert.Equal("GodARenamed", nameBox.Text);
                await ClickGod(secondGod);
                for (var attempt = 0; attempt < 100 && !host.OwnedWindows.OfType<Prompt>().Any(); attempt++) await Task.Delay(10);
                var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                Assert.Equal("Unsaved main document", prompt.PromptTitle);
                await Settle(); Click(prompt, Field<Button>(prompt, "_confirmButton"));
                await Field<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(3)); await Settle();
                trace.Add(State("discard completed"));
                Assert.Equal(secondGod, view.CurrentMajorGodName);
                var savedSecond = document.Root!.Elements("civ").Single(entry => MajorGodCatalog.GetName(entry) == secondGod);
                var currentSecond = Field<XElement>(view, "_currentEntry");
                string[] Values(XElement entry) => entry.DescendantsAndSelf().Select(element => element.Name.LocalName +
                    " " + string.Join(" ", element.Attributes().Select(attribute => attribute.ToString())) +
                    (element.HasElements ? "" : " = " + element.Value)).ToArray();
                trace.Add("changed XML added: " + string.Join(";", Values(currentSecond).Except(Values(savedSecond))));
                trace.Add("changed XML removed: " + string.Join(";", Values(savedSecond).Except(Values(currentSecond))));
                await ClickGod(thirdGod);
                var extra = host.OwnedWindows.OfType<Prompt>().ToArray();
                Assert.True(extra.Length == 0, string.Join("\n", extra.Select(dialog => dialog.PromptTitle + ": " + dialog.PromptText)) + "\n" + string.Join("\n", trace));
                await Field<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(thirdGod, view.CurrentMajorGodName);
                Assert.False(view.IsDirty, string.Join("\n", trace));
                Assert.True(XNode.DeepEquals(loadedBaseline, view.CreateModifiedDocumentSnapshot()), "Discard/navigation must preserve the complete loaded God XML.");
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            });
        }
        finally
        {
            AvaloniaTestHost.Run(() =>
            {
                foreach (var prompt in host.OwnedWindows.OfType<Prompt>().ToArray()) prompt.Close();
                host.DisposeFixtureWindow();
            });
        }
    }
}
