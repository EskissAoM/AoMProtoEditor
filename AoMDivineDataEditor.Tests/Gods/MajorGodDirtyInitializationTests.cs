using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodDirtyInitializationTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(MajorGodEditorView view, string name)
        => (T)typeof(MajorGodEditorView).GetField(name, Private)!.GetValue(view)!;
    private static async Task SettleAsync()
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenAndSwitchSavedGodsStaysCleanButRealEditsRemainDirty(bool populated)
    {
        AvaloniaTestHost.CollectUiGarbage();
        using var fixture = new BaselineSafetyFixture();
        var document = populated ? XDocument.Load(fixture.GodPath, LoadOptions.PreserveWhitespace) :
            XDocument.Parse("<civsmods><civ><name>CheckpointGod</name><statsid>SP</statsid><portrait/><culture> </culture><titleid/></civ></civsmods>");
        if (populated) document.Root!.Element("civ")!.Add(
            XElement.Parse("<startingunits><unit count='1' x='0' y='0' z='0'>Hoplite</unit></startingunits>"),
            XElement.Parse("<startingqueue><unit>Hoplite</unit></startingqueue>"),
            XElement.Parse("<startingresources><food>100</food></startingresources>"));
        document.Root!.Add(XElement.Parse("<civ><name>OtherGod</name><statsid>SP</statsid></civ>"));
        document.Save(fixture.GodPath);
        var bytes = File.ReadAllBytes(fixture.GodPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = host.God!;
                host.GodHost.Content = null;
                host.Content = view;
                host.Show();
                var before = view.CreateModifiedDocumentSnapshot()!.ToString(SaveOptions.DisableFormatting);
                var dirtyTransitions = new List<bool>();
                view.DirtyStateChanged += (_, _) => dirtyTransitions.Add(view.IsDirty);
                foreach (var name in new[] { "CheckpointGod", "OtherGod", "CheckpointGod" })
                {
                    view.SelectMajorGod(name);
                    await SettleAsync();
                    Assert.False(view.IsDirty, "Opening " + name + " must not create a pending edit.");
                    Assert.True(await view.CommitCurrentMajorGodAsync());
                    await SettleAsync();
                    Assert.False(view.IsDirty, "Committing untouched controls must remain clean.");
                    Assert.Equal(before, view.CreateModifiedDocumentSnapshot()!.ToString(SaveOptions.DisableFormatting));
                }
                Assert.DoesNotContain(true, dirtyTransitions);
                Assert.Empty(Field<Dictionary<string, string>>(view, "_pendingStringUpdates"));
                Field<TextBox>(view, "_keyBox").Text = "B";
                await SettleAsync();
                Assert.True(view.IsDirty);
                Field<TextBox>(view, "_titleBox").Text = "A deliberate title edit";
                await SettleAsync();
                Assert.NotEmpty(Field<Dictionary<string, string>>(view, "_pendingStringUpdates"));
                view.SelectMajorGod("OtherGod");
                await SettleAsync();
                view.SelectMajorGod("CheckpointGod");
                await SettleAsync();
                Assert.True(view.IsDirty);
                Assert.Equal("B", view.CreateModifiedDocumentSnapshot()!.Root!.Elements("civ").First().Element("key")!.Value);
                Assert.Equal(bytes, File.ReadAllBytes(fixture.GodPath));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
