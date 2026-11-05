using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ImmediateEntityDeletionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object value, string name)
        => (value is BaselineHost ? typeof(ProtoEditorWindow) : value.GetType()).GetField(name, Private)!.GetValue(value)!;
    private static string Name(string kind) => kind == "Technology" ? "DeletedTech" : kind == "Power" ? "DeletedPower" : "DeletedGod";
    private static string Kind(string kind) => kind == "Technology" ? "Technologies" : kind == "Power" ? "Powers" : "MajorGods";
    private static object View(BaselineHost host, string kind)
        => Field(host, kind == "Technology" ? "_technologyView" : kind == "Power" ? "_powerView" : "_majorGodView");
    private static string WriteDefinitions(BaselineSafetyFixture fixture, string kind, string? name = null)
    {
        name ??= Name(kind);
        return fixture.Workspace.WriteText("TestMod/game/data/gameplay/" + (kind == "Technology" ? "techtree_mods.xml" : kind == "Power" ? "powers_mods.xml" : "major_gods_mods.xml"),
            kind == "Technology" ? $"<techtreemods><tech name='{name}'/><tech name='Keep'><future preserved='yes'/></tech></techtreemods>" :
            kind == "Power" ? $"<powersmod><power name='{name}' type='TempUnit'/><power name='Keep' type='TempUnit'><future preserved='yes'/></power></powersmod>" :
            $"<civsmods><civ><name>{name}</name><statsid>SP</statsid></civ><civ><name>Keep</name><statsid>SP</statsid><future preserved='yes'/></civ></civsmods>");
    }

    private static BaselineHost CreateHost(BaselineSafetyFixture fixture, string kind, string? name = null)
    {
        var host = fixture.CreateHost(false);
        if (kind == "Technology") host.Call("EnsureTechnologyView");
        if (kind == "Power") host.Call("EnsurePowerView");
        var entityKind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, Kind(kind));
        host.Set("_activeEntityKind", entityKind);
        if (kind != "Power")
        {
            if (typeof(ProtoEditorWindow).GetField("_mainDocumentTab", Private)!.GetValue(host) == null)
                host.Call("InitializeDocumentTabs");
            host.Set("_documentTabActivationInProgress", true);
            host.Set("_suppressUnitListSelectionChange", true);
            ((Avalonia.Controls.Primitives.TabStrip)Field(host, "_unitTabs")).SelectedIndex = 1;
            host.Call("SetMainDocumentTarget", entityKind, true, name ?? Name(kind));
        }
        var view = View(host, kind);
        host.GodHost.Content = view;
        if (view is TechnologyEditorView technology) { technology.SetModifiedMode(true); technology.SelectTechnology(name ?? Name(kind)); }
        if (view is PowerEditorView power) { power.SetModifiedMode(true); power.SelectPower(name ?? Name(kind)); }
        if (view is MajorGodEditorView god) { god.SetModifiedMode(true); god.SelectMajorGod(name ?? Name(kind)); }
        host.Show();
        Dispatcher.UIThread.RunJobs();
        host.Set("_documentTabActivationInProgress", false);
        host.Set("_suppressUnitListSelectionChange", false);
        return host;
    }

    private static Task DeleteAsync(BaselineHost host, string kind) => View(host, kind) switch
    {
        TechnologyEditorView technology => technology.DeleteTechnologyAsync(),
        PowerEditorView power => power.DeleteCurrentPowerAsync(),
        MajorGodEditorView god => god.DeleteCurrentMajorGodAsync(),
        _ => throw new InvalidOperationException()
    };

    private static async Task<Prompt> PromptAsync(BaselineHost host)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var prompt = host.OwnedWindows.OfType<Prompt>().SingleOrDefault();
            if (prompt != null) return prompt;
            await Task.Delay(10);
        }
        var technology = typeof(ProtoEditorWindow).GetField("_technologyView", Private)!.GetValue(host) as TechnologyEditorView;
        throw new TimeoutException($"Expected deletion dialog. God={host.God?.CurrentMajorGodName}, dirty={host.God?.IsDirty}, status={host.GodStatus}; " +
            $"Technology={technology?.CurrentTechnologyName}, mode={technology?.IsModifiedMode}, status={(technology == null ? "none" : ((TextBlock)Field(technology, "_statusMessage")).Text)}; " +
            $"UnitDirty={Field(host, "_isDirty")}, hostStatus={((TextBlock)Field(host, "_statusMessage")).Text}");
    }

    private static void Confirm(Prompt prompt)
        => typeof(Prompt).GetMethod("ConfirmButton_Click", Private)!.Invoke(prompt, [null, new RoutedEventArgs()]);
    private static async Task<Prompt> FinalConfirmationAsync(BaselineHost host)
    {
        var prompt = await PromptAsync(host);
        if (prompt.PromptTitle == "Save changes before deletion")
        {
            Confirm(prompt);
            prompt = await PromptAsync(host);
        }
        Assert.True(prompt.PromptIsConfirm, prompt.PromptText);
        Assert.Contains("save the deletion now", prompt.PromptText);
        return prompt;
    }

    private static object AddPinnedTab(BaselineHost host, string kind, string name)
    {
        var entityKind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, Kind(kind));
        var tab = host.Call("CreateDocumentTab", false, entityKind, true, name)!;
        ((IList)Field(host, "_openDocumentTabs")).Add(tab);
        ((IList)Field(host, "_documentTabItems")).Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
        return tab;
    }

    [Theory]
    [InlineData("Technology", "save")]
    [InlineData("Technology", "cancel")]
    [InlineData("Technology", "write-failure")]
    [InlineData("Technology", "late-reference")]
    [InlineData("Power", "save")]
    [InlineData("Power", "cancel")]
    [InlineData("Power", "write-failure")]
    [InlineData("Power", "late-reference")]
    [InlineData("God", "save")]
    [InlineData("God", "cancel")]
    [InlineData("God", "write-failure")]
    [InlineData("God", "late-reference")]
    public void HostedDeletionPersistsOrRestoresEntityAndTabs(string kind, string outcome)
    {
        AvaloniaTestHost.CollectUiGarbage();
        using var fixture = new BaselineSafetyFixture();
        var path = WriteDefinitions(fixture, kind);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, kind));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var tab = AddPinnedTab(host, kind, Name(kind));
                var task = DeleteAsync(host, kind);
                var prompt = await FinalConfirmationAsync(host);
                var before = File.ReadAllBytes(path);
                var documentBefore = ((XDocument)Field(View(host, kind), "_modDocument")).ToString(SaveOptions.DisableFormatting);
                using var lockedFile = outcome == "write-failure" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                if (outcome == "late-reference") fixture.Workspace.WriteText("TestMod/game/data/gameplay/late.xml",
                    kind == "Technology" ? $"<root><futuretech>{Name(kind)}</futuretech></root>" : $"<root><future>{Name(kind)}</future></root>");
                if (outcome == "cancel") prompt.Close(); else Confirm(prompt);
                if (outcome is "write-failure" or "late-reference")
                {
                    var error = await PromptAsync(host);
                    Assert.True(error.PromptIsError, error.PromptText);
                    error.Close();
                }
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                var deleted = outcome == "save";
                Assert.Equal(!deleted, File.ReadAllText(path).Contains(Name(kind), StringComparison.Ordinal));
                Assert.Contains("preserved", File.ReadAllText(path));
                Assert.Equal(!deleted, ((IList)Field(host, "_openDocumentTabs")).Contains(tab));
                Assert.False((bool)Field(host, "_isUnitSaveInProgress"));
                Assert.True(((Button)Field(host, "_saveButton")).IsEnabled);
                if (!deleted) Assert.Equal(before, File.ReadAllBytes(path));
                if (outcome is "write-failure" or "late-reference")
                    Assert.Equal(documentBefore, ((XDocument)Field(View(host, kind), "_modDocument")).ToString(SaveOptions.DisableFormatting));
                var view = View(host, kind);
                var current = view is TechnologyEditorView technology ? technology.CurrentTechnologyName :
                    view is PowerEditorView power ? power.CurrentPowerName : ((MajorGodEditorView)view).CurrentMajorGodName;
                Assert.Equal(deleted ? null : Name(kind), current);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("write-failure")]
    public void TechnologyAndLinkedMinorGodDeleteAtomically(string outcome)
    {
        AvaloniaTestHost.CollectUiGarbage();
        using var fixture = new BaselineSafetyFixture();
        const string name = "HeroicAgeDeletedGod";
        var path = WriteDefinitions(fixture, "Technology", name);
        var minorPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/minor_gods_mods.xml",
            $"<minorgodsmods><{name}/><HeroicAgeKeep custom='preserved'/></minorgodsmods>");
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, "Technology", name));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var task = DeleteAsync(host, "Technology");
                var prompt = await FinalConfirmationAsync(host);
                Assert.Contains("Minor God", prompt.PromptText);
                var beforeTech = File.ReadAllBytes(path);
                var beforeMinor = File.ReadAllBytes(minorPath);
                using var lockedFile = outcome == "write-failure" ? new FileStream(minorPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                if (outcome == "cancel") prompt.Close(); else Confirm(prompt);
                if (outcome == "write-failure")
                {
                    var error = await PromptAsync(host);
                    Assert.True(error.PromptIsError, error.PromptText);
                    error.Close();
                }
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(outcome != "save", File.ReadAllText(path).Contains(name, StringComparison.Ordinal));
                Assert.Equal(outcome != "save", File.ReadAllText(minorPath).Contains(name, StringComparison.Ordinal));
                Assert.Contains("preserved", File.ReadAllText(minorPath));
                if (outcome != "save")
                {
                    Assert.Equal(beforeTech, File.ReadAllBytes(path));
                    Assert.Equal(beforeMinor, File.ReadAllBytes(minorPath));
                    Assert.Equal(name, ((TechnologyEditorView)View(host, "Technology")).CurrentTechnologyName);
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("Technology")]
    [InlineData("Power")]
    [InlineData("God")]
    public void NeverSavedDraftDiscardKeepsOtherPendingChangesAndAllFiles(string kind)
    {
        AvaloniaTestHost.CollectUiGarbage();
        using var fixture = new BaselineSafetyFixture();
        var path = WriteDefinitions(fixture, kind);
        var before = File.ReadAllBytes(path);
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, kind));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = View(host, kind);
                var document = (XDocument)Field(view, "_modDocument");
                var draft = kind == "God" ? XElement.Parse("<civ><name>Draft</name><statsid>SP</statsid></civ>") :
                    new XElement(kind == "Power" ? "power" : "tech", new XAttribute("name", "Draft"));
                if (kind == "Power") draft.SetAttributeValue("type", "TempUnit");
                document.Root!.Add(draft);
                if (view is TechnologyEditorView technology)
                {
                    ((Dictionary<string, XElement>)Field(view, "_modified"))["Draft"] = draft;
                    typeof(TechnologyEditorView).GetMethod("RefreshList", Private)!.Invoke(view, ["Draft"]);
                    technology.SelectTechnology("Draft");
                }
                else if (view is PowerEditorView power) power.SelectPower("Draft");
                else ((MajorGodEditorView)view).SelectMajorGod("Draft");
                // A separate Unit draft must never be saved just to discard this record.
                ((XDocument)Field(host, "_modXmlDoc")).Root!.Add(new XElement("unit", new XAttribute("name", "OtherDraft")));
                ((HashSet<string>)Field(host, "_dirtyUnitNames")).Add("OtherDraft");
                host.Set("_isDirty", true);
                var task = DeleteAsync(host, kind);
                var prompt = await PromptAsync(host);
                Assert.StartsWith("Discard new", prompt.PromptTitle);
                Confirm(prompt);
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
                Assert.True((bool)Field(host, "_isDirty"));
                Assert.Contains("OtherDraft", (HashSet<string>)Field(host, "_dirtyUnitNames"));
                Assert.DoesNotContain(document.Root.Elements(), element => (string?)element.Attribute("name") == "Draft" || element.Element("name")?.Value == "Draft");
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("Technology")]
    [InlineData("Power")]
    [InlineData("God")]
    public void CancelPendingSaveKeepsTargetAndOtherDraft(string kind)
    {
        AvaloniaTestHost.CollectUiGarbage();
        using var fixture = new BaselineSafetyFixture();
        var path = WriteDefinitions(fixture, kind);
        var before = File.ReadAllBytes(path);
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => CreateHost(fixture, kind));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var tab = AddPinnedTab(host, kind, Name(kind));
                host.Set("_isDirty", true);
                var task = DeleteAsync(host, kind);
                var prompt = await PromptAsync(host);
                Assert.Equal("Save changes before deletion", prompt.PromptTitle);
                prompt.Close();
                await task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
                Assert.True((bool)Field(host, "_isDirty"));
                Assert.Contains(tab, ((IList)Field(host, "_openDocumentTabs")).Cast<object>());
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
