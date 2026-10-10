using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorFoxCharmTests : IDisposable
{
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void FoxCharmIsAnEditorExtensionWithoutChangingTheOriginalCorpusContract()
    {
        Assert.DoesNotContain("FoxCharm", PowerEditorAuditInventory.KnownPowerTypes);
        Assert.Contains("FoxCharm", PowerEditorAuditInventory.SupportedExtensionPowerTypes);
        Assert.Contains("FoxCharm", PowerEditorAuditInventory.EditorPowerTypes);
    }

    [Fact]
    public void CustomFoxCharmLoadsAllAgeAdjustmentsAndVfxWithoutBecomingDirty()
    {
        using var workspace = new TestWorkspace(nameof(
            CustomFoxCharmLoadsAllAgeAdjustmentsAndVfxWithoutBecomingDirty));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="CharmedFox" type="FoxCharm" godpower="">
                <AreaVFX>VFXFoxCharmArea</AreaVFX>
                <TargetUnitVFX>VFXFoxCharmTarget</TargetUnitVFX>
                <UnitVFX>VFXFoxCharm</UnitVFX>
                <ageadjustment affectedNumber="1">ArchaicAge</ageadjustment>
                <ageadjustment affectedNumber="2">ClassicalAge</ageadjustment>
                <ageadjustment affectedNumber="3">HeroicAge</ageadjustment>
                <ageadjustment affectedNumber="4">MythicAge</ageadjustment>
                <ageadjustment affectedNumber="5">WonderAge</ageadjustment>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXFoxCharm", "VFXFoxCharmTarget", "VFXFoxCharmArea"]));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("CharmedFox"));

        AvaloniaTestHost.Run(() =>
        {
            Assert.False(editor.IsDirty);
            Assert.Equal("FoxCharm", GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").Text);
            Assert.Contains("FoxCharm", GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox")
                .ItemsSource!.Cast<string>());
            var rows = GetPrivateField<StackPanel>(editor, "_foxCharmAgeRows").Children
                .OfType<WrapPanel>().ToList();
            Assert.Equal(5, rows.Count);
            Assert.Equal(["ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"],
                rows.Select(row => row.Children.OfType<ComboBox>().Single().SelectedItem).Cast<string>());
            Assert.Equal(["1", "2", "3", "4", "5"],
                rows.Select(row => row.Children.OfType<TextBox>().Single().Text));
            Assert.False(GetPrivateField<Button>(editor, "_addFoxCharmAgeButton").IsVisible);
            Assert.True(GetPrivateField<WrapPanel>(editor, "_foxCharmVfxFields").IsVisible);
            Assert.Equal("VFXFoxCharm", GetPrivateField<AutoCompleteBox>(editor,
                "_foxCharmUnitVfxBox").Text);
            Assert.Equal("VFXFoxCharmTarget", GetPrivateField<AutoCompleteBox>(editor,
                "_foxCharmTargetVfxBox").Text);
            Assert.Equal("VFXFoxCharmArea", GetPrivateField<AutoCompleteBox>(editor,
                "_foxCharmAreaVfxBox").Text);
        });
    }

    [Fact]
    public void FoxCharmCreatesAtMostFiveAgeRowsAndRoundTripsOptionalVfx()
    {
        using var workspace = new TestWorkspace(nameof(
            FoxCharmCreatesAtMostFiveAgeRowsAndRoundTripsOptionalVfx));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='NewFox' type='FoxCharm' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["UnitVfx", "TargetVfx", "AreaVfx"]));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("NewFox"));
        AvaloniaTestHost.Run(() =>
        {
            var addAge = GetPrivateField<Button>(editor, "_addFoxCharmAgeButton");
            for (var index = 0; index < 6; index++)
                addAge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(5, GetPrivateField<StackPanel>(editor, "_foxCharmAgeRows").Children.Count);
            Assert.False(addAge.IsVisible);

            GetPrivateField<Button>(editor, "_addFoxCharmVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var areaFields = GetPrivateField<StackPanel>(editor, "_foxCharmAreaVfxFields");
            var removeVfx = GetPrivateField<Button>(editor, "_removeFoxCharmVfxButton");
            Assert.Same(areaFields, removeVfx.Parent);
            Assert.Equal(2, removeVfx.Margin.Left);
            CommitProto(editor, "_foxCharmUnitVfxBox", "UnitVfx");
            CommitProto(editor, "_foxCharmTargetVfxBox", "TargetVfx");
            CommitProto(editor, "_foxCharmAreaVfxBox", "AreaVfx");
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = FindPower(XDocument.Load(path), "NewFox");
        var adjustments = saved.Elements("ageadjustment").ToList();
        Assert.Equal(5, adjustments.Count);
        Assert.Equal(["ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"],
            adjustments.Select(element => element.Value));
        Assert.Equal(["1", "2", "3", "4", "5"],
            adjustments.Select(element => element.Attribute("affectedNumber")?.Value));
        Assert.Equal("UnitVfx", saved.Element("unitvfx")?.Value);
        Assert.Equal("TargetVfx", saved.Element("targetunitvfx")?.Value);
        Assert.Equal("AreaVfx", saved.Element("areavfx")?.Value);

        AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor, "_removeFoxCharmVfxButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        saved = FindPower(XDocument.Load(path), "NewFox");
        Assert.Null(saved.Element("unitvfx"));
        Assert.Null(saved.Element("targetunitvfx"));
        Assert.Null(saved.Element("areavfx"));
    }

    private static void CommitProto(PowerEditorView editor, string fieldName, string value)
        => editor.GetType().GetMethod("CommitNewPowerProto",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [GetPrivateField<AutoCompleteBox>(editor, fieldName), value]);

    private static XElement FindPower(XContainer container, string name)
        => container.Descendants().Single(element =>
            element.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));
}
