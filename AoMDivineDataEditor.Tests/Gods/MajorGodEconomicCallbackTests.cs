using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodEconomicCallbackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static TextBox Box(MajorGodEditorView view, bool attribute) => (TextBox)typeof(MajorGodEditorView)
        .GetMethod(attribute ? "CreateEconomicAttributeBox" : "CreateEconomicValueBox", Private)!.Invoke(view,
            attribute ? [Current(view).Element("secondaryprayerefficiency")!, "g"] : ["buildingefficiency"])!;
    private static XElement Current(MajorGodEditorView view) => (XElement)typeof(MajorGodEditorView).GetField("_currentEntry", Private)!.GetValue(view)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReplacedEconomicControlsCannotDirtyOrModifyCurrentGod(bool attribute, bool rebuildSameGod)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>Old</name><statsid>SP</statsid><buildingefficiency>0.60000</buildingefficiency><secondaryprayerefficiency g='2.0000' v='2' z='2'/></civ><civ><name>Other</name><statsid>SP</statsid><buildingefficiency>0</buildingefficiency><secondaryprayerefficiency g='3' v='3' z='3'/></civ></civsmods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var view = host.God!; view.SelectMajorGod("Old");
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var baseline = view.CreateModifiedDocumentSnapshot()!.ToString(SaveOptions.DisableFormatting);
                var old = Box(view, attribute);
                if (rebuildSameGod) typeof(MajorGodEditorView).GetMethod("RebuildEconomicEditor", Private)!.Invoke(view, null);
                else view.SelectMajorGod("Other");
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                old.Text = "7";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.False(view.IsDirty);
                Assert.Equal(baseline, view.CreateModifiedDocumentSnapshot()!.ToString(SaveOptions.DisableFormatting));
                var current = Box(view, attribute); current.Text = "4";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(view.IsDirty);
                Assert.Equal("4", attribute ? Current(view).Element("secondaryprayerefficiency")!.Attribute("g")!.Value : Current(view).Element("buildingefficiency")!.Value);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
