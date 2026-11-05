using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodXmlOrderingWorkflowTests : IDisposable
{
    private const string Initial = "<civsmods><civ><oncastpowercostfactor>1</oncastpowercostfactor><startingresources><food>10</food></startingresources><startingqueue><unit>BaseUnit</unit></startingqueue><name>A</name><statsid>SP</statsid><key>A</key><startingunits><unit count='1' x='0' y='0' z='0'>BaseUnit</unit></startingunits></civ><civ><key>B</key><name>B</name><statsid>SP</statsid></civ></civsmods>";
    private static readonly string[] FormOrder = ["name", "statsid", "key", "startingunits", "startingqueue", "startingresources", "oncastpowercostfactor"];
    private static string[] Tags(XElement entry) => entry.Elements().Select(e => e.Name.LocalName).ToArray();
    private static Task Population(MajorGodEditorView view) => (Task)typeof(MajorGodEditorView).GetField("_populationTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    private static void WithGod(Func<MajorGodEditorView, string, Task> check, string xml = Initial)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodXmlOrderingWorkflowTests));
        var path = workspace.WriteText("major_gods_mods.xml", xml);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, protoUnitNames: ["BaseUnit"]);
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A"); await Population(view);
                await check(view, path);
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    private static async Task ChangeKey(MajorGodEditorView view)
    {
        view.FindControl<TextBox>("_keyBox")!.Text = "Z";
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(view.IsDirty);
    }

    [Fact]
    public void StructuredGodOrderMatchesPreviewDiskAndReopenWithoutChangingSibling()
        => WithGod(async (view, path) =>
        {
            Assert.False(view.IsDirty);
            await ChangeKey(view);
            Assert.Equal(FormOrder, Tags(XElement.Parse(view.FindControl<TextEditor>("_rawXmlEditor")!.Text)));
            Assert.True(await view.SaveAsync());
            Assert.Equal(FormOrder, Tags(MajorGodCatalog.Find(XDocument.Load(path), "A")!));
            Assert.Equal(new[] { "key", "name", "statsid" }, Tags(MajorGodCatalog.Find(XDocument.Load(path), "B")!));
            var once = File.ReadAllBytes(path);
            Assert.True(await view.SaveAsync()); Assert.Equal(once, File.ReadAllBytes(path));
            view.SelectMajorGod("B"); await Population(view); view.SelectMajorGod("A"); await Population(view);
            Assert.False(view.IsDirty);
            Assert.Equal(FormOrder, Tags(XElement.Parse(view.FindControl<TextEditor>("_rawXmlEditor")!.Text)));
        });

    [Fact]
    public void RawGodManualOrderSurvivesThenLaterFormSaveUsesApprovedOrder()
        => WithGod(async (view, path) =>
        {
            await MajorGodXmlTestHelpers.UnlockAsync(view);
            var manual = MajorGodCatalog.Find(XDocument.Parse(Initial), "A")!;
            manual.Element("key")!.Value = "C";
            view.FindControl<TextEditor>("_rawXmlEditor")!.Text = manual.ToString();
            Assert.True(await view.SaveAsync());
            Assert.Equal(Tags(manual), Tags(MajorGodCatalog.Find(XDocument.Load(path), "A")!));
            view.SelectMajorGod("A"); await Population(view);
            await ChangeKey(view);
            Assert.True(await view.SaveAsync());
            Assert.Equal(FormOrder, Tags(MajorGodCatalog.Find(XDocument.Load(path), "A")!));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuredGodSaveProtectsRootAndInheritedPreservedOrder(bool inherited)
    {
        var xml = inherited ? Initial.Replace("<civsmods>", "<civsmods xml:space='preserve'>")
            : Initial.Replace("<civ>", "<civ xml:space='preserve'>");
        WithGod(async (view, path) =>
        {
            await ChangeKey(view);
            Assert.True(await view.SaveAsync());
            Assert.Equal(Tags(MajorGodCatalog.Find(XDocument.Parse(xml), "A")!), Tags(MajorGodCatalog.Find(XDocument.Load(path), "A")!));
        }, xml);
    }
}
