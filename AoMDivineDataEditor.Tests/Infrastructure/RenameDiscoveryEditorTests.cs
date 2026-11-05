using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class RenameDiscoveryEditorTests
{
    private static async Task<bool> SaveAndCheckFailurePopupAsync(Control view, Func<Task<bool>> save)
    {
        var owner = new Window { Content = view, Width = 1100, Height = 850 };
        try
        {
            owner.Show();
            var saving = save();
            Prompt? popup = null;
            for (var attempt = 0; attempt < 200 && popup == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                popup = owner.OwnedWindows.OfType<Prompt>().SingleOrDefault();
                if (popup == null) await Task.Delay(10);
            }
            Assert.NotNull(popup);
            Assert.Contains("reference file list changed", popup.PromptText);
            popup.Close();
            return await saving.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            foreach (var popup in owner.OwnedWindows.ToArray()) popup.Close();
            owner.Close();
            owner.Content = null;
        }
    }

    private static T Field<T>(object instance, string name) => (T)
        (instance is BaselineHost ? typeof(ProtoEditorWindow) : instance.GetType())
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    [Theory]
    [InlineData("Technology", false)]
    [InlineData("Technology", true)]
    [InlineData("Power", false)]
    [InlineData("Power", true)]
    [InlineData("God", false)]
    [InlineData("God", true)]
    public void HostedRenameChecksDiscoveryBeforeOwnerSaveAndRetainsDraftOnRefusal(string kind, bool lateFile)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/" + (kind == "Technology" ?
            "techtree_mods.xml" : kind == "Power" ? "powers_mods.xml" : "major_gods_mods.xml"),
            kind == "Technology" ? "<techtreemods><tech name='Old'/></techtreemods>" :
            kind == "Power" ? "<powersmod><power name='Old' type='TempUnit'/></powersmod>" :
            "<civsmods><civ><name>Old</name><statsid>SP</statsid></civ></civsmods>");
        var before = File.ReadAllBytes(path);
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                object view;
                Func<Task<bool>> commit;
                Func<Task<bool>> save;
                Func<bool> dirty;
                var inspected = false;
                IReadOnlyList<ModDeletionInspection> Inspect(IReadOnlyList<string> deleted,
                    IReadOnlyDictionary<string, XDocument> candidates)
                {
                    Assert.Empty(deleted);
                    Assert.Contains(Path.GetFullPath(path), candidates.Keys);
                    inspected = true;
                    if (lateFile) fixture.Workspace.WriteText("TestMod/new-folder/late.xml", "<root/>");
                    return [];
                }

                if (kind == "Technology")
                {
                    host.Call("EnsureTechnologyView");
                    var technology = Field<TechnologyEditorView>(host, "_technologyView");
                    view = technology;
                    host.GodHost.Content = null;
                    technology.SetModifiedMode(true);
                    technology.SelectTechnology("Old");
                    technology.PrepareDeletionChecks = Inspect;
                    commit = technology.CommitCurrentTechnologyAsync;
                    save = technology.SaveAsync;
                    dirty = () => technology.IsDirty;
                }
                else if (kind == "Power")
                {
                    host.Call("EnsurePowerView");
                    var power = Field<PowerEditorView>(host, "_powerView");
                    view = power;
                    host.GodHost.Content = null;
                    power.SetModifiedMode(true);
                    await power.SelectPowerAsync("Old");
                    power.PrepareDeletionChecks = Inspect;
                    commit = power.CommitCurrentPowerAsync;
                    save = power.SaveAsync;
                    dirty = () => power.IsDirty;
                }
                else
                {
                    var god = host.God!;
                    view = god;
                    host.GodHost.Content = null;
                    god.SetModifiedMode(true);
                    god.SelectMajorGod("Old");
                    god.PrepareDeletionChecks = Inspect;
                    commit = god.CommitCurrentMajorGodAsync;
                    save = god.SaveAsync;
                    dirty = () => god.IsDirty;
                }

                Field<TextBox>(view, kind == "Technology" ? "_techNameBox" : "_nameBox").Text = "New";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(await commit());
                Assert.True(dirty());
                Assert.Equal(!lateFile, lateFile
                    ? await SaveAndCheckFailurePopupAsync((Control)view, save)
                    : await save());
                Assert.True(inspected, "The injected change must occur after reference-plan preparation.");
                Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
                if (lateFile)
                {
                    Assert.Equal(before, File.ReadAllBytes(path));
                    Assert.True(dirty());
                    Assert.Equal("", Field<TextBlock>(view, "_statusMessage").Text);
                    Assert.Equal("Save blocked.", Field<TextBlock>(host, "_statusMessage").Text);
                    Assert.Equal("<root/>", File.ReadAllText(fixture.Workspace.GetPath("TestMod/new-folder/late.xml")));
                }
                else
                {
                    Assert.False(dirty());
                    var document = XDocument.Load(path);
                    Assert.True(kind == "God" ? document.Descendants("name").Any(element => element.Value == "New") :
                        document.Descendants().Any(element => (string?)element.Attribute("name") == "New"));
                }
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
