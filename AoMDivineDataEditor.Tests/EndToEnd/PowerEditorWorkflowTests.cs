using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorWorkflowTests : IDisposable
{
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void OriginalAndModifiedBrowsersUseTheSharedCatalogRule()
    {
        using var workspace = new TestWorkspace(nameof(OriginalAndModifiedBrowsersUseTheSharedCatalogRule));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="CustomPower" type="GodPower" />
              <power name="AbilityOnly" type="UnitAction" />
              <power name="IncludedGeneral" type="GeneralEffect" godpower="" />
              <power name="ExcludedGeneral" type="GeneralEffect" />
            </powersmod>
            """);
        var originals = PowerCatalog.ExtractDefinitions(
            [XDocument.Parse("<powers><power name='OriginalUnitAction' type='UnitAction' /></powers>")],
            isBuiltIn: true);

        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(originals, path));

        Assert.Equal(["OriginalUnitAction"], AvaloniaTestHost.Run(() => editor.GetPowerNames(modified: false)));
        Assert.Equal(
            ["CustomPower", "IncludedGeneral"],
            AvaloniaTestHost.Run(() => editor.GetPowerNames(modified: true)));
    }

    [Fact]
    public void IconPickerShowsOneShortenedChoiceForAResourceIcon()
    {
        using var workspace = new TestWorkspace(nameof(IconPickerShowsOneShortenedChoiceForAResourceIcon));
        const string icon = @"aztec\static_color\god_powers\agave_bloom_icon.png";
        var path = workspace.WriteText("powers_mods.xml",
            $"<powersmod><power name=\"AgaveBloom\" type=\"GodPower\"><icon>resources\\{icon}</icon></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            iconPaths: [$@"resources\{icon}"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("AgaveBloom");
        });

        var picker = AvaloniaTestHost.Run(() => GetPrivateField<AssetPathEditor>(editor, "_iconEditor"));
        Assert.Equal(icon, picker.FullValue);
        Assert.Equal(icon, Assert.Single(picker.Suggestions).FullValue);
    }

    [Fact]
    public void DuplicatePowerNameRestoresThePreviousNameAndReportsTheDuplicate()
    {
        using var workspace = new TestWorkspace(nameof(DuplicatePowerNameRestoresThePreviousNameAndReportsTheDuplicate));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"First\" type=\"GodPower\" /><power name=\"Second\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("First");
            GetPrivateField<TextBox>(editor, "_nameBox").Text = "Second";
        });

        Assert.False(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));
        Assert.Equal("First", AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text));
        Assert.Contains("already exists", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBlock>(editor, "_statusMessage").Text));
    }

    [Fact]
    public void NewCopyAndSavePreserveUnmanagedPowerXml()
    {
        using var workspace = new TestWorkspace(nameof(NewCopyAndSavePreserveUnmanagedPowerXml));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod custom="keep-root">
              <!-- keep power comment -->
              <power name="Existing" type="GodPower"><future keep="yes" /></power>
              <power name="AbilityOnly" type="UnitAction"><abilitydata /></power>
            </powersmod>
            """);
        var stringsPath = workspace.WriteText("stringmods.txt", "Language = \"English\"\n");
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.AddPowerAsync());
        Assert.Equal("NewPower", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "Rain");
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() => editor.SelectPower("Existing"));
        AvaloniaTestHost.RunAsync(() => editor.AddPowerAsync(duplicateSelected: true));
        Assert.Equal("ExistingCopy", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        Assert.Equal("keep-root", (string?)saved.Root?.Attribute("custom"));
        Assert.Contains(saved.Root!.Nodes().OfType<XComment>(), comment =>
            comment.Value.Contains("keep power comment", StringComparison.Ordinal));
        Assert.NotNull(FindPower(saved, "NewPower"));
        Assert.Equal("All", FindPower(saved, "NewPower").Element("powerplayerrelation")?.Value);
        Assert.Equal("yes", (string?)FindPower(saved, "ExistingCopy").Element("future")?.Attribute("keep"));
        Assert.NotNull(FindPower(saved, "AbilityOnly").Element("abilitydata"));
        var directNodes = saved.Root.Nodes().ToList();
        var powersMarker = directNodes.FindIndex(node => node is XComment comment &&
            comment.Value.Trim() == PowerCatalog.GetSectionMarker(PowerDocumentSection.Powers));
        Assert.True(powersMarker > directNodes.FindIndex(node =>
            node is XElement element && PowerCatalog.GetName(element) == "AbilityOnly"));
        Assert.True(directNodes.FindIndex(node =>
            node is XElement element && PowerCatalog.GetName(element) == "NewPower") > powersMarker);
        Assert.True(directNodes.FindIndex(node =>
            node is XElement element && PowerCatalog.GetName(element) == "ExistingCopy") > powersMarker);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("NewPower", strings[PowerCatalog.BuildStringId("NewPower", "displaynameid")]);
        Assert.Contains(StringTableDocument.GetSectionMarker(StringTableSection.Powers), File.ReadAllText(stringsPath));
    }

    [Fact]
    public void PropertiesResolveAndSavePowerStringsWithOptionalActiveRollover()
    {
        using var workspace = new TestWorkspace(nameof(PropertiesResolveAndSavePowerStringsWithOptionalActiveRollover));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="AgaveBloom" type="TempUnit" godpower="">
                <displaynameid>STR_POWER_AGAVE_BLOOM</displaynameid>
                <rolloverid>STR_POWER_AGAVE_BLOOM_LR</rolloverid>
                <icon>aztec\static_color\god_powers\agave_bloom_icon.png</icon>
                <future keep="yes" />
              </power>
            </powersmod>
            """);
        var stringsPath = workspace.WriteText("stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_AGAVE_BLOOM"   ;   Str = "Agave Bloom"
            ID = "STR_POWER_AGAVE_BLOOM_LR"   ;   Str = "Original rollover"
            """);
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("AgaveBloom");
        });
        Assert.Equal("Agave Bloom", AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_displayNameBox").Text));
        Assert.Equal("Original rollover", AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_rolloverBox").Text));

        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_displayNameBox").Text = "New Bloom";
            GetPrivateField<TextBox>(editor, "_rolloverBox").Text = "Updated rollover";
            InvokePrivateClick(editor, "AddActiveRollover_Click");
            GetPrivateField<TextBox>(editor, "_activeRolloverBox").Text = "Active tooltip";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = FindPower(XDocument.Load(path), "AgaveBloom");
        Assert.Equal("STR_POWER_AGAVE_BLOOM", saved.Element("displaynameid")?.Value);
        Assert.Equal("STR_POWER_AGAVE_BLOOM_LR", saved.Element("rolloverid")?.Value);
        Assert.Equal("STR_POWER_AGAVE_BLOOM_ACTIVE_LR", saved.Element("activepowerrolloverid")?.Value);
        Assert.NotNull(saved.Element("future"));
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("New Bloom", strings["STR_POWER_AGAVE_BLOOM"]);
        Assert.Equal("Updated rollover", strings["STR_POWER_AGAVE_BLOOM_LR"]);
        Assert.Equal("Active tooltip", strings["STR_POWER_AGAVE_BLOOM_ACTIVE_LR"]);
        Assert.Contains(StringTableDocument.GetSectionMarker(StringTableSection.Powers), File.ReadAllText(stringsPath));
    }

    [Fact]
    public void TimingCostsLimitsAndFlagsRoundTripThroughProperties()
    {
        using var workspace = new TestWorkspace(nameof(TimingCostsLimitsAndFlagsRoundTripThroughProperties));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"TestPower\" type=\"GodPower\"><activetime>3.0</activetime><cost>20.0</cost><repeatcost>5.0</repeatcost></power></powersmod>");
        var stringsPath = workspace.WriteText("stringmods.txt", "Language = \"English\"\n");
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("TestPower"));
        AvaloniaTestHost.Run(() =>
        {
            InvokePrivateClick(editor, "AddOrderHint_Click");
            GetPrivateField<TextBox>(editor, "_orderHintBox").Text = "7.5";
            GetPrivateField<TextBox>(editor, "_activeTimeBox").Text = "7.5";
            GetPrivateField<CheckBox>(editor, "_activeTimeOnTargetCheck").IsChecked = true;
            InvokePrivateClick(editor, "AddOptionalTime_Click", new Button { Tag = "builduptime" });
            GetPrivateField<TextBox>(editor, "_buildUpBox").Text = "1.25";
            InvokePrivateClick(editor, "AddOptionalTime_Click", new Button { Tag = "shutdowntime" });
            GetPrivateField<TextBox>(editor, "_shutDownBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_shutDownFadeBox").Text = "0.75";
            GetPrivateField<TextBox>(editor, "_costBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_repeatCostBox").Text = "12";
            InvokePrivateClick(editor, "AddUseLimit_Click");
            GetPrivateField<TextBox>(editor, "_limitGlobalBox").Text = "24";
            GetPrivateField<TextBox>(editor, "_limitPlayerBox").Text = "4";
            GetPrivateField<CheckBox>(editor, "_singleActivationCheck").IsChecked = true;
            GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsChecked = true;
            GetPrivateField<TextBox>(editor, "_exclusiveMessageBox").Text = "Already active";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = FindPower(XDocument.Load(path), "TestPower");
        Assert.Equal("7.5", saved.Attribute("orderhint")?.Value);
        Assert.Equal("7.5", saved.Element("activetime")?.Value);
        Assert.NotNull(saved.Element("activetimeontarget"));
        Assert.Equal("1.25", saved.Element("builduptime")?.Value);
        Assert.Equal("2", saved.Element("shutdowntime")?.Value);
        Assert.Equal("0.75", saved.Element("shutdownfadetime")?.Value);
        Assert.Null(saved.Element("cost"));
        Assert.Equal("12", saved.Element("repeatcost")?.Value);
        Assert.Equal("24", saved.Element("limitglobal")?.Value);
        Assert.Equal("4", saved.Element("limitplayer")?.Value);
        Assert.NotNull(saved.Element("singleactivation"));
        Assert.NotNull(saved.Element("exclusive"));
        Assert.Contains("<singleactivation></singleactivation>", File.ReadAllText(path));
        Assert.Contains("<exclusive></exclusive>", File.ReadAllText(path));
        Assert.Equal("STR_ERROR_POWER_TEST_POWER", saved.Element("exclusiveerrormsgid")?.Value);
        Assert.Equal("Already active", StringTableDocument.Parse(File.ReadAllText(stringsPath))
            .ToDictionary()["STR_ERROR_POWER_TEST_POWER"]);

        AvaloniaTestHost.Run(() =>
        {
            InvokePrivateClick(editor, "RemoveOrderHint_Click");
            InvokePrivateClick(editor, "RemoveOptionalTime_Click", new Button { Tag = "builduptime" });
            InvokePrivateClick(editor, "RemoveOptionalTime_Click", new Button { Tag = "shutdowntime" });
            InvokePrivateClick(editor, "RemoveUseLimit_Click");
            GetPrivateField<CheckBox>(editor, "_singleActivationCheck").IsChecked = false;
            GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsChecked = false;
            GetPrivateField<CheckBox>(editor, "_activeTimeOnTargetCheck").IsChecked = false;
        });
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsChecked == true));
        Assert.Null(AvaloniaTestHost.Run(() =>
            GetPrivateField<XElement>(editor, "_currentEntry").Element("exclusive")));
        Assert.Null(AvaloniaTestHost.Run(() =>
            GetPrivateField<XElement>(editor, "_currentEntry").Element("exclusiveerrormsgid")));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        saved = FindPower(XDocument.Load(path), "TestPower");
        Assert.Null(saved.Attribute("orderhint"));
        foreach (var tag in new[] { "builduptime", "shutdowntime", "shutdownfadetime", "activetimeontarget", "limitglobal", "limitplayer",
                     "singleactivation", "exclusive", "exclusiveerrormsgid" })
            Assert.Null(saved.Element(tag));
        Assert.DoesNotContain("STR_ERROR_POWER_TEST_POWER",
            StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().Keys);
        Assert.DoesNotMatch(new Regex(@"(?:\r?\n[ \t]*){3,}"), File.ReadAllText(path));
    }

    [Fact]
    public void OriginalOrderHintShutdownPairAndSelfPlacementFlagAreDisplayedReadOnly()
    {
        var source = XElement.Parse("""
            <power name="Original" type="UnderworldInvasion" orderhint="3.5">
              <placement self="">Full</placement>
              <shutdowntime>2</shutdowntime>
              <shutdownfadetime>0.75</shutdownfadetime>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Original"));

        AvaloniaTestHost.Run(() =>
        {
            Assert.True(GetPrivateField<Grid>(editor, "_orderHintRow").IsVisible);
            Assert.Equal("3.5", GetPrivateField<TextBox>(editor, "_orderHintBox").Text);
            Assert.False(GetPrivateField<TextBox>(editor, "_orderHintBox").IsEnabled);
            Assert.False(GetPrivateField<Button>(editor, "_addOrderHintButton").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_shutDownRow").IsVisible);
            Assert.Equal("2", GetPrivateField<TextBox>(editor, "_shutDownBox").Text);
            Assert.Equal("0.75", GetPrivateField<TextBox>(editor, "_shutDownFadeBox").Text);
            var chips = GetPrivateField<WrapPanel>(editor, "_placementFlagChips")
                .Children.OfType<Border>().ToList();
            Assert.Contains(chips, chip => ((StackPanel)chip.Child!).Children.OfType<TextBlock>()
                .Any(label => label.Text == "self"));
        });
    }

    [Fact]
    public void ZeroUseLimitsStayVisibleForEditingButAreNotWritten()
    {
        using var workspace = new TestWorkspace(nameof(ZeroUseLimitsStayVisibleForEditingButAreNotWritten));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("P"));
        AvaloniaTestHost.Run(() =>
        {
            InvokePrivateClick(editor, "AddUseLimit_Click");
        });

        Assert.True(AvaloniaTestHost.Run(() => GetPrivateField<StackPanel>(editor, "_useLimitRow").IsVisible));
        Assert.True(AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_limitLabel").IsVisible));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = FindPower(XDocument.Load(path), "P");
        Assert.Null(saved.Element("limitglobal"));
        Assert.Null(saved.Element("limitplayer"));
    }

    [Fact]
    public void OriginalPowerHidesAbsentFlagsAndOptionalRemoveButtons()
    {
        var source = XElement.Parse("""
            <power name="Original" type="GodPower">
              <activepowerrolloverid>STR_POWER_ORIGINAL_ACTIVE_LR</activepowerrolloverid>
              <builduptime>1.0</builduptime>
              <shutdowntime>2.0</shutdowntime>
              <limitplayer>3</limitplayer>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.Run(() => editor.SelectPower("Original"));

        foreach (var name in new[] { "_removeActiveRolloverButton", "_removeBuildUpButton",
                     "_removeShutDownButton", "_removeUseLimitButton" })
            Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor, name).IsVisible));
        Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<CheckBox>(editor, "_singleActivationCheck").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsVisible));
    }

    [Fact]
    public void SavingEditedPowerCollapsesEarlierBlankGapWithoutChangingOtherPower()
    {
        using var workspace = new TestWorkspace(nameof(SavingEditedPowerCollapsesEarlierBlankGapWithoutChangingOtherPower));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Edited" type="GodPower">
                <cost>1</cost>



                <exclusive></exclusive>
              </power>
              <power name="Untouched" type="GodPower">
                <cost>2</cost>



                <exclusive></exclusive>
              </power>
            </powersmod>
            """);
        var before = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var untouchedBefore = FindPower(before, "Untouched").ToString(SaveOptions.DisableFormatting);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Edited");
            GetPrivateField<TextBox>(editor, "_costBox").Text = "3";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var after = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        Assert.DoesNotMatch(new Regex(@"(?:\r?\n[ \t]*){3,}"),
            FindPower(after, "Edited").ToString(SaveOptions.DisableFormatting));
        Assert.Equal(untouchedBefore, FindPower(after, "Untouched").ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void PlacementModeAndFlagChipsPreserveOtherPlacementAttributes()
    {
        using var workspace = new TestWorkspace(nameof(PlacementModeAndFlagChipsPreserveOtherPlacementAttributes));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="P" type="GodPower">
                <placement losprotounit="AgavePreview" forceonmap="1" natureonly="">Full</placement>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        Assert.Equal("Full", AvaloniaTestHost.Run(() =>
            GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem));
        Assert.Equal(2, AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_placementFlagChips").Children.Count));

        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem = "Overlap";
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "allowSnap";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var placement = FindPower(XDocument.Load(path), "P").Element("placement")!;
        Assert.Equal("Overlap", placement.Value);
        Assert.Equal("AgavePreview", (string?)placement.Attribute("losprotounit"));
        Assert.Equal("1", (string?)placement.Attribute("forceonmap"));
        Assert.NotNull(placement.Attribute("natureonly"));
        Assert.Equal("", (string?)placement.Attribute("allowSnap"));

        AvaloniaTestHost.Run(() =>
        {
            var chips = GetPrivateField<WrapPanel>(editor, "_placementFlagChips");
            var chip = chips.Children.OfType<Border>().Single(border =>
                ((StackPanel)border.Child!).Children.OfType<TextBlock>()
                .Any(label => label.Text == "allowSnap"));
            ((StackPanel)chip.Child!).Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        placement = FindPower(XDocument.Load(path), "P").Element("placement")!;
        Assert.Null(placement.Attribute("allowSnap"));
        Assert.Equal("1", (string?)placement.Attribute("forceonmap"));
    }

    [Fact]
    public void OriginalPlacementFlagsAreReadOnlyChips()
    {
        var source = XElement.Parse("""
            <power name="Original" type="GodPower">
              <placement natureonly="" enemy="">Unit</placement>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.Run(() => editor.SelectPower("Original"));

        Assert.Equal("Unit", AvaloniaTestHost.Run(() =>
            GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").IsVisible));
        AvaloniaTestHost.Run(() =>
        {
            var chips = GetPrivateField<WrapPanel>(editor, "_placementFlagChips")
                .Children.OfType<Border>().ToList();
            Assert.Equal(2, chips.Count);
            Assert.All(chips, chip => Assert.Empty(((StackPanel)chip.Child!).Children.OfType<Button>()));
        });
    }

    [Fact]
    public void PlacementSelectorOffersTheRequestedModesAndFlagCatalog()
    {
        using var workspace = new TestWorkspace(nameof(PlacementSelectorOffersTheRequestedModesAndFlagCatalog));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("P"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal(["Full", "Overlap", "InRange", "Unit", "Skip"],
                GetPrivateField<ComboBox>(editor, "_placementBox").ItemsSource!.Cast<string>());
            Assert.Equal(100, GetPrivateField<ComboBox>(editor, "_placementBox").Width);
            Assert.Equal(50, GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").Width);
            Assert.Equal(
            [
                "disallowWaterCast", "disallowWaterCastRadius", "disallowLandObstructedCast",
                "disallowLandObstructedCastRadius", "disallowLandCast", "allowGroundTarget",
                "allowFogOfWarPlacement", "allowGaia", "noLosExtraLoc",
                "allowSnap", "placeFoundation", "ignoreProtoPlacement", "placeInArea",
                "enemy", "ally", "self", "allyOrEnemyNotSelf", "allowNature",
                "natureOnly", "useTargetUnitPositionForPlacement", "fullyBuiltOnly", "allowDeadTarget",
                "ignoreattacktypesforplacement",
                "ExpandCheckPlacementHull", "VectorPlacement"
            ], GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").ItemsSource!.Cast<string>());
            GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem = "Overlap";
            Assert.Contains("notOverlapSelfOrAlly",
                GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").ItemsSource!.Cast<string>());
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "natureOnly";
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "self";
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem =
                "ignoreattacktypesforplacement";
            foreach (var flag in new[] { "disallowWaterCastRadius", "disallowLandObstructedCast",
                         "disallowLandObstructedCastRadius", "disallowLandCast", "allowGroundTarget",
                         "allowFogOfWarPlacement" })
                GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = flag;
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("", (string?)FindPower(XDocument.Load(path), "P")
            .Element("placement")?.Attribute("natureOnly"));
        Assert.Equal("", (string?)FindPower(XDocument.Load(path), "P")
            .Element("placement")?.Attribute("self"));
        Assert.NotNull(FindPower(XDocument.Load(path), "P")
            .Element("ignoreattacktypesforplacement"));
        var placement = FindPower(XDocument.Load(path), "P").Element("placement")!;
        Assert.NotNull(placement.Attribute("disallowWaterCastRadius"));
        Assert.Null(placement.Attribute("disallowsWaterCastRadius"));
        Assert.NotNull(placement.Attribute("disallowLandObstructedCast"));
        Assert.NotNull(placement.Attribute("disallowLandObstructedCastRadius"));
        Assert.NotNull(placement.Attribute("disallowLandCast"));
        Assert.NotNull(placement.Attribute("allowGroundTarget"));
        Assert.NotNull(placement.Attribute("allowFogOfWarPlacement"));
    }

    [Fact]
    public void PlacementSkipClearsPlacementOnlyXml()
    {
        using var workspace = new TestWorkspace(nameof(PlacementSkipClearsPlacementOnlyXml));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="P" type="GodPower">
              <placement forceonmap="1" notOverlapSelfOrAlly="" losprotounit="LegacyUnit">Full</placement>
              <expandcheckplacementhull></expandcheckplacementhull>
              <abstractplacementtargettype>Scout</abstractplacementtargettype>
              <explicitlyrestrictedplacementtargettype>Temple</explicitlyrestrictedplacementtargettype>
              <cost>10</cost>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            var picker = GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker");
            Assert.DoesNotContain("forceOnMap", picker.ItemsSource!.Cast<string>());
            Assert.DoesNotContain("notOverlapSelfOrAlly", picker.ItemsSource!.Cast<string>());
            var mode = GetPrivateField<ComboBox>(editor, "_placementBox");
            mode.SelectedItem = "Overlap";
            Assert.True(GetPrivateField<StackPanel>(editor, "_placementDetailsHost").IsVisible);
            mode.SelectedItem = "Skip";
            Assert.False(GetPrivateField<StackPanel>(editor, "_placementDetailsHost").IsVisible);
            Assert.False(GetPrivateField<WrapPanel>(editor, "_placementFlagsRow").IsVisible);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        var placement = power.Element("placement")!;
        Assert.Equal("Skip", placement.Value);
        Assert.Empty(placement.Attributes());
        Assert.Null(power.Element("expandcheckplacementhull"));
        Assert.Null(power.Element("abstractplacementtargettype"));
        Assert.Null(power.Element("explicitlyrestrictedplacementtargettype"));
        Assert.Equal("10", power.Element("cost")?.Value);
    }

    [Fact]
    public void PlacementTypeChipsWriteRepeatedValidTagsAndCanRemoveOne()
    {
        using var workspace = new TestWorkspace(nameof(PlacementTypeChipsWriteRepeatedValidTagsAndCanRemoveOne));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Scout", "Temple"], unitTypeNames: ["LogicalTypePowerBlock"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.Equal(50, GetPrivateField<AutoCompleteBox>(editor, "_targetTypePicker").Width);
            Assert.Equal(50, GetPrivateField<AutoCompleteBox>(editor, "_excludeTypePicker").Width);
            GetPrivateField<AutoCompleteBox>(editor, "_targetTypePicker").SelectedItem = "Scout";
            GetPrivateField<AutoCompleteBox>(editor, "_targetTypePicker").SelectedItem = "Temple";
            GetPrivateField<AutoCompleteBox>(editor, "_excludeTypePicker").SelectedItem = "LogicalTypePowerBlock";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_statusMessage").Text));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal(["Scout", "Temple"], power.Elements("abstractplacementtargettype")
            .Select(element => element.Value));
        Assert.Equal("LogicalTypePowerBlock", power.Element("explicitlyrestrictedplacementtargettype")?.Value);

        AvaloniaTestHost.Run(() =>
        {
            var chip = GetPrivateField<WrapPanel>(editor, "_targetTypeChips")
                .Children.OfType<Border>().First();
            ((StackPanel)chip.Child!).Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(["Temple"], FindPower(XDocument.Load(path), "P")
            .Elements("abstractplacementtargettype").Select(element => element.Value));
    }

    [Fact]
    public void ExpandCheckPlacementHullIsSiblingOfPlacement()
    {
        using var workspace = new TestWorkspace(nameof(ExpandCheckPlacementHullIsSiblingOfPlacement));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "ExpandCheckPlacementHull";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.NotNull(power.Element("expandcheckplacementhull"));
        Assert.Null(power.Element("placement")!.Attribute("ExpandCheckPlacementHull"));

        AvaloniaTestHost.Run(() =>
        {
            var chip = GetPrivateField<WrapPanel>(editor, "_placementFlagChips")
                .Children.OfType<Border>().Single();
            ((StackPanel)chip.Child!).Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Null(FindPower(XDocument.Load(path), "P").Element("expandcheckplacementhull"));
    }

    [Fact]
    public void PlacementOptionalSelectorsWriteValidatedAttributes()
    {
        using var workspace = new TestWorkspace(nameof(PlacementOptionalSelectorsWriteValidatedAttributes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Scout", "Temple"], unitTypeNames: ["LogicalTypePowerBlock"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<Button>(editor, "_addLosProtoUnitButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<StackPanel>(editor, "_losProtoUnitRow").IsVisible);
            Assert.True(GetPrivateField<Button>(editor, "_addRestrictPlacementButton").IsVisible);
            Assert.True(GetPrivateField<Button>(editor, "_addOverlapTypeButton").IsVisible);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_losProtoUnitRow").Parent,
                GetPrivateField<Button>(editor, "_addRestrictPlacementButton").Parent);
            GetPrivateField<AutoCompleteBox>(editor, "_losProtoUnitBox").SelectedItem = "Scout";
            GetPrivateField<Button>(editor, "_addRestrictPlacementButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_restrictPlacementBox").SelectedItem = "LogicalTypePowerBlock";
            GetPrivateField<TextBox>(editor, "_restrictDistanceBox").Text = "7.5";
            GetPrivateField<CheckBox>(editor, "_restrictAddObstructionCheck").IsChecked = true;
            GetPrivateField<Button>(editor, "_addOverlapTypeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_overlapTypeBox").SelectedItem = "Temple";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var placement = FindPower(XDocument.Load(path), "P").Element("placement")!;
        Assert.Equal("Scout", (string?)placement.Attribute("losprotounit"));
        Assert.Equal("LogicalTypePowerBlock", (string?)placement.Attribute("restrictplacementnear"));
        Assert.Equal("7.5", (string?)placement.Attribute("restrictplacementdistance"));
        Assert.NotNull(placement.Attribute("restrictPlacementNearAddObstruction"));
        Assert.Equal("Temple", (string?)placement.Attribute("overlaptype"));
    }

    [Fact]
    public void OpenedPlacementOptionsComeBeforeUnusedButtons()
    {
        using var workspace = new TestWorkspace(nameof(OpenedPlacementOptionsComeBeforeUnusedButtons));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<Button>(editor, "_addRestrictPlacementButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var flow = GetPrivateField<WrapPanel>(editor, "_placementOptionsFlow");
            Assert.Equal(4, flow.Children.Count);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_restrictPlacementRow"), flow.Children[0]);
            Assert.Same(GetPrivateField<Button>(editor, "_addLosProtoUnitButton"), flow.Children[1]);
            Assert.Same(GetPrivateField<Button>(editor, "_addOverlapTypeButton"), flow.Children[2]);
            Assert.Same(GetPrivateField<Button>(editor, "_addPlacementRestrictionButton"), flow.Children[3]);
            GetPrivateField<Button>(editor, "_addLosProtoUnitButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(GetPrivateField<StackPanel>(editor, "_losProtoUnitRow"), flow.Children[0]);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_restrictPlacementRow"), flow.Children[1]);
            Assert.Same(GetPrivateField<Button>(editor, "_addOverlapTypeButton"), flow.Children[2]);
            Assert.Same(GetPrivateField<Button>(editor, "_addPlacementRestrictionButton"), flow.Children[3]);
        });
    }

    [Fact]
    public void PlacementRestrictionsAreRepeatableAndSkipClearsThem()
    {
        using var workspace = new TestWorkspace(nameof(PlacementRestrictionsAreRepeatableAndSkipClearsThem));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Scout"], unitTypeNames: ["AbstractSettlement"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            var add = GetPrivateField<Button>(editor, "_addPlacementRestrictionButton");
            var rows = GetPrivateField<WrapPanel>(editor, "_placementRestrictionRows");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var first = Assert.IsType<StackPanel>(rows.Children.Single());
            first.Children.OfType<AutoCompleteBox>().Single().SelectedItem = "Scout";
            first.Children.OfType<TextBox>().Single().Text = "5.5";
            Assert.Equal("5.5", (string?)GetPrivateField<XElement>(editor, "_currentEntry")
                .Elements("placementrestriction").First().Attribute("radius"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = Assert.IsType<StackPanel>(rows.Children.Last());
            second.Children.OfType<AutoCompleteBox>().Single().SelectedItem = "AbstractSettlement";
            second.Children.OfType<TextBox>().Single().Text = "10";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal(["Scout", "AbstractSettlement"], power.Elements("placementrestriction")
            .Select(element => element.Value));
        Assert.Equal(["5.5", "10"], power.Elements("placementrestriction")
            .Select(element => (string?)element.Attribute("radius")));
        AvaloniaTestHost.Run(() => GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem = "Skip");
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Empty(FindPower(XDocument.Load(path), "P").Elements("placementrestriction"));
    }

    [Fact]
    public void LegacyRangeIndicatorProtoIsEditableButNotOfferedAsNewAttribute()
    {
        using var workspace = new TestWorkspace(nameof(LegacyRangeIndicatorProtoIsEditableButNotOfferedAsNewAttribute));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><rangeindicatorprotoid radius=\"10\" indicatorcount=\"8\" speed=\"0.2\">VFXA</rangeindicatorprotoid></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXA", "VFXB"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.True(GetPrivateField<Grid>(editor, "_rangeIndicatorProtoRow").IsVisible);
            GetPrivateField<Button>(editor, "_otherAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("Range Indicator Proto",
                GetPrivateField<AutoCompleteBox>(editor, "_otherAttributePicker").ItemsSource!.Cast<string>());
            GetPrivateField<AutoCompleteBox>(editor, "_rangeIndicatorProtoBox").SelectedItem = "VFXB";
            GetPrivateField<TextBox>(editor, "_rangeIndicatorProtoRadiusBox").Text = "12.5";
            GetPrivateField<TextBox>(editor, "_rangeIndicatorProtoCountBox").Text = "9";
            GetPrivateField<TextBox>(editor, "_rangeIndicatorProtoSpeedBox").Text = "0.4";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var field = FindPower(XDocument.Load(path), "P").Element("rangeindicatorprotoid")!;
        Assert.Equal("VFXB", field.Value);
        Assert.Equal("12.5", (string?)field.Attribute("radius"));
        Assert.Equal("9", (string?)field.Attribute("indicatorcount"));
        Assert.Equal("0.4", (string?)field.Attribute("speed"));
    }

    [Fact]
    public void PowerRelationRadiusGlobalAndAttackTargetsRoundTrip()
    {
        using var workspace = new TestWorkspace(nameof(PowerRelationRadiusGlobalAndAttackTargetsRoundTrip));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["AUnit"], unitTypeNames: ["MilitaryUnit", "NavalUnit"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.Equal("All", GetPrivateField<ComboBox>(editor, "_powerPlayerRelationBox").SelectedItem);
            GetPrivateField<ComboBox>(editor, "_powerPlayerRelationBox").SelectedItem = "EnemyNotMotherNature";
            GetPrivateField<TextBox>(editor, "_radiusBox").Text = "12.5";
            GetPrivateField<CheckBox>(editor, "_globalCheck").IsChecked = true;
            Assert.False(GetPrivateField<TextBox>(editor, "_radiusBox").IsVisible);
            GetPrivateField<AutoCompleteBox>(editor, "_attackTargetTypePicker").SelectedItem = "MilitaryUnit";
            GetPrivateField<AutoCompleteBox>(editor, "_attackTargetTypePicker").SelectedItem = "AUnit";
            GetPrivateField<AutoCompleteBox>(editor, "_attackExcludeTypePicker").SelectedItem = "NavalUnit";
            var typeChips = GetPrivateField<WrapPanel>(editor, "_attackTargetTypeChips");
            var excludeChips = GetPrivateField<WrapPanel>(editor, "_attackExcludeTypeChips");
            Assert.Equal(1, Grid.GetColumn(typeChips));
            Assert.Equal(1, Grid.GetColumn(excludeChips));
            Assert.IsType<Grid>(typeChips.Parent);
            Assert.IsType<Grid>(excludeChips.Parent);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("EnemyNotMotherNature", power.Element("powerplayerrelation")?.Value);
        Assert.Null(power.Element("radius"));
        Assert.NotNull(power.Element("global"));
        Assert.Equal(["MilitaryUnit", "AUnit"], power.Elements("abstractattacktargettype")
            .Select(element => element.Value));
        Assert.Equal("NavalUnit", power.Element("explicitlyrestrictedattacktargettype")?.Value);

        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<CheckBox>(editor, "_globalCheck").IsChecked = false;
            GetPrivateField<TextBox>(editor, "_radiusBox").Text = "";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        power = FindPower(XDocument.Load(path), "P");
        Assert.Null(power.Element("global"));
        Assert.Null(power.Element("radius"));
    }

    [Fact]
    public void InvalidRadiusDoesNotWriteXml()
    {
        using var workspace = new TestWorkspace(nameof(InvalidRadiusDoesNotWriteXml));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><radius>2</radius></power></powersmod>");
        var before = File.ReadAllBytes(path);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_radiusBox").Text = "-1";
        });
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingOriginalPowerFieldsAndUncheckedGlobalAreHidden()
    {
        var source = XElement.Parse("<power name=\"Original\" type=\"GodPower\"><placement>Full</placement></power>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            Assert.False(GetPrivateField<Grid>(editor, "_rangeIndicatorRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_radiusRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_powerPlayerRelationRow").IsVisible);
            Assert.False(GetPrivateField<CheckBox>(editor, "_globalCheck").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_attackTargetRow").IsVisible);
        });
    }

    [Fact]
    public void OriginalCircleIndicatorShowsOnlyItsPresentFields()
    {
        var source = XElement.Parse("""
            <power name="Original" type="GodPower">
              <rangeindicator range="10" speed="0.2">Circle</rangeindicator>
              <global></global>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            Assert.True(GetPrivateField<Grid>(editor, "_rangeIndicatorRow").IsVisible);
            Assert.Equal("Circle", GetPrivateField<ComboBox>(editor, "_rangeIndicatorBox").SelectedItem);
            Assert.True(GetPrivateField<TextBox>(editor, "_indicatorRangeBox").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_indicatorWidthBox").IsVisible);
            Assert.True(GetPrivateField<TextBox>(editor, "_indicatorSpeedBox").IsVisible);
            Assert.True(GetPrivateField<CheckBox>(editor, "_globalCheck").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_radiusBox").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_powerPlayerRelationRow").IsVisible);
        });
    }

    [Fact]
    public void ArrowRangeIndicatorAndCustomDecalRoundTrip()
    {
        using var workspace = new TestWorkspace(nameof(ArrowRangeIndicatorAndCustomDecalRoundTrip));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<ComboBox>(editor, "_rangeIndicatorBox").SelectedItem = "Arrow";
            Assert.True(GetPrivateField<TextBox>(editor, "_indicatorWidthBox").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_indicatorSpeedBox").IsVisible);
            GetPrivateField<TextBox>(editor, "_indicatorRangeBox").Text = "30";
            GetPrivateField<TextBox>(editor, "_indicatorWidthBox").Text = "16";
            GetPrivateField<Button>(editor, "_addRangeDecalButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_rangeDecalBox").Text = @"effects\rangedecals\arrow";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        var indicator = power.Element("rangeindicator")!;
        Assert.Equal("Arrow", indicator.Value);
        Assert.Equal("30", (string?)indicator.Attribute("range"));
        Assert.Equal("16", (string?)indicator.Attribute("width"));
        Assert.Null(indicator.Attribute("speed"));
        Assert.Equal(@"effects\rangedecals\arrow", power.Element("rangeindicatorcustomdecal")?.Value);

        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_indicatorWidthBox").Text = "-1");
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_indicatorWidthBox").Text = "0";
            GetPrivateField<Button>(editor, "_removeRangeDecalButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("0", (string?)power.Element("rangeindicator")?.Attribute("width"));
        Assert.Null(power.Element("rangeindicatorcustomdecal"));
    }

    [Fact]
    public void VectorPlacementIsStandaloneAndSkipRemovesIt()
    {
        using var workspace = new TestWorkspace(nameof(VectorPlacementIsStandaloneAndSkipRemovesIt));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><placement>Full</placement></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "VectorPlacement";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.NotNull(power.Element("vectorplacement"));
        Assert.Null(power.Element("placement")?.Attribute("VectorPlacement"));
        AvaloniaTestHost.Run(() => GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem = "Skip");
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Null(FindPower(XDocument.Load(path), "P").Element("vectorplacement"));
    }

    [Fact]
    public void CircleSpeedAndConeCustomDecalAreAvailable()
    {
        using var workspace = new TestWorkspace(nameof(CircleSpeedAndConeCustomDecalAreAvailable));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<ComboBox>(editor, "_rangeIndicatorBox").SelectedItem = "Circle";
            Assert.True(GetPrivateField<TextBox>(editor, "_indicatorSpeedBox").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_indicatorWidthBox").IsVisible);
            Assert.True(GetPrivateField<Button>(editor, "_addRangeDecalButton").IsVisible);
            GetPrivateField<TextBox>(editor, "_indicatorSpeedBox").Text = "0.5";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("0.5", (string?)FindPower(XDocument.Load(path), "P")
            .Element("rangeindicator")?.Attribute("speed"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<ComboBox>(editor, "_rangeIndicatorBox").SelectedItem = "Cone";
            Assert.False(GetPrivateField<TextBox>(editor, "_indicatorSpeedBox").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_indicatorWidthBox").IsVisible);
            Assert.True(GetPrivateField<Button>(editor, "_addRangeDecalButton").IsVisible);
            GetPrivateField<Button>(editor, "_addRangeDecalButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_rangeDecalBox").Text = "ConeDecal";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("Cone", power.Element("rangeindicator")?.Value);
        Assert.Null(power.Element("rangeindicator")?.Attribute("speed"));
        Assert.Equal("ConeDecal", power.Element("rangeindicatorcustomdecal")?.Value);
    }

    [Fact]
    public void NotificationsResolveStringsAndWriteOptionalFields()
    {
        using var workspace = new TestWorkspace(nameof(NotificationsResolveStringsAndWriteOptionalFields));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var stringsPath = workspace.WriteText("stringmods.txt", "Language = \"English\"\n");
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<Button>(editor, "_addNotificationsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<Button>(editor, "_addCastMessageButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_castMessageBox").Text = "The power was cast";
            GetPrivateField<Button>(editor, "_addCastSelfMessageButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_castSelfMessageBox").Text = "You cast it";
            GetPrivateField<Button>(editor, "_addAlertPlayerButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("All", GetPrivateField<ComboBox>(editor, "_alertPlayerBox").SelectedItem);
            GetPrivateField<ComboBox>(editor, "_alertPlayerBox").SelectedItem = "Neutral";
            GetPrivateField<CheckBox>(editor, "_displayAlertsCheck").IsChecked = true;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        var castId = PowerCatalog.BuildStringId("P", "notificationformatid");
        var selfId = PowerCatalog.BuildStringId("P", "selfnotificationformatid");
        Assert.Equal("STR_MSG_INVOKE_POWER_P", castId);
        Assert.Equal("STR_MSG_SELF_INVOKE_POWER_P", selfId);
        Assert.Equal(castId, power.Element("notificationformatid")?.Value);
        Assert.Equal(selfId, power.Element("selfnotificationformatid")?.Value);
        Assert.Equal("Neutral", power.Element("messagealertplayerrelation")?.Value);
        Assert.NotNull(power.Element("displayalerts"));
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("The power was cast", strings[castId]);
        Assert.Equal("You cast it", strings[selfId]);
        AvaloniaTestHost.Run(() =>
        {
            var flow = Assert.IsType<WrapPanel>(GetPrivateField<Grid>(editor, "_castMessageRow").Parent);
            var children = flow.Children.ToList();
            Assert.True(children.IndexOf(GetPrivateField<Grid>(editor, "_castMessageRow")) <
                        children.IndexOf(GetPrivateField<Button>(editor, "_addCastSelfMessageButton")));
            Assert.True(children.IndexOf(GetPrivateField<Grid>(editor, "_castSelfMessageRow")) <
                        children.IndexOf(GetPrivateField<Button>(editor, "_addAlertPlayerButton")));
        });

        AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor, "_removeCastMessageButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Null(FindPower(XDocument.Load(path), "P").Element("notificationformatid"));
        Assert.False(StringTableDocument.Parse(File.ReadAllText(stringsPath))
            .ToDictionary().ContainsKey(castId));
    }

    [Fact]
    public void MinimapAndActiveVisibilityOptionsRoundTrip()
    {
        using var workspace = new TestWorkspace(nameof(MinimapAndActiveVisibilityOptionsRoundTrip));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<Button>(editor, "_addMinimapNotificationsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("All", GetPrivateField<ComboBox>(editor, "_minimapSendToBox").SelectedItem);
            GetPrivateField<ComboBox>(editor, "_minimapSendToBox").SelectedItem = "Enemy";
            GetPrivateField<TextBox>(editor, "_minimapDurationBox").Text = "12.5";
            GetPrivateField<Button>(editor, "_addActiveVisibilityButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<Button>(editor, "_addActiveListVisibilityButton").IsVisible);
            GetPrivateField<Button>(editor, "_addActiveListVisibilityButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<ComboBox>(editor, "_activeListVisibilityBox").SelectedItem = "AlliesOnly";
            GetPrivateField<CheckBox>(editor, "_hideOnActiveListCheck").IsChecked = true;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("12.5", power.Element("minimapeventtime")?.Value);
        Assert.Equal("Enemy", (string?)power.Element("minimapeventtime")?.Attribute("sendalertto"));
        Assert.Equal("AlliesOnly", power.Element("activelistvisibility")?.Value);
        Assert.NotNull(power.Element("hideonactivegplist"));

        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_minimapDurationBox").Text = "-1");
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_minimapDurationBox").Text = "0";
            GetPrivateField<Button>(editor, "_removeMinimapNotificationsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<Button>(editor, "_removeActiveVisibilityButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        power = FindPower(XDocument.Load(path), "P");
        Assert.Null(power.Element("minimapeventtime"));
        Assert.Null(power.Element("activelistvisibility"));
        Assert.Null(power.Element("hideonactivegplist"));
    }

    [Fact]
    public void OriginalNotificationSectionsHideMissingFields()
    {
        var source = XElement.Parse("""
            <power name="Original" type="GodPower">
              <notificationformatid>STR_MSG_INVOKE_POWER_TITAN_GATE</notificationformatid>
              <minimapeventtime sendalertto="None"></minimapeventtime>
              <hideonactivegplist></hideonactivegplist>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null,
            resolveStringAsync: id => Task.FromResult<string?>(
                id == "STR_MSG_INVOKE_POWER_TITAN_GATE" ? "Titan gate cast" : null)));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            Assert.True(GetPrivateField<Grid>(editor, "_notificationsSection").IsVisible);
            Assert.Equal("Titan gate cast", GetPrivateField<TextBox>(editor, "_castMessageBox").Text);
            Assert.False(GetPrivateField<Grid>(editor, "_castSelfMessageRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_alertPlayerRow").IsVisible);
            Assert.False(GetPrivateField<CheckBox>(editor, "_displayAlertsCheck").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_minimapNotificationsSection").IsVisible);
            Assert.False(GetPrivateField<TextBox>(editor, "_minimapDurationBox").IsVisible);
            Assert.Equal("None", GetPrivateField<ComboBox>(editor, "_minimapSendToBox").SelectedItem);
            Assert.True(GetPrivateField<Grid>(editor, "_activeVisibilitySection").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_activeListVisibilityFields").IsVisible);
            Assert.True(GetPrivateField<CheckBox>(editor, "_hideOnActiveListCheck").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_addNotificationSectionsRow").IsVisible);
        });
    }

    [Fact]
    public void CameraShakeAndTerrainRumblingAttributesRoundTrip()
    {
        using var workspace = new TestWorkspace(nameof(CameraShakeAndTerrainRumblingAttributesRoundTrip));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_cameraDurationBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_cameraStrengthBox").Text = "0.25";
            Assert.True(GetPrivateField<Button>(editor, "_addCameraRangeButton").IsVisible);
            GetPrivateField<Button>(editor, "_addCameraRangeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_cameraRangeBox").Text = "40";
            GetPrivateField<TextBox>(editor, "_terrainHeightBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_terrainWidthBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_terrainSpeedBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_terrainFadeTimeBox").Text = "2";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("0.5", (string?)power.Element("camerashake")?.Attribute("duration"));
        Assert.Equal("0.25", (string?)power.Element("camerashake")?.Attribute("strength"));
        Assert.Equal("40", (string?)power.Element("camerashake")?.Attribute("range"));
        Assert.Equal("0.25", (string?)power.Element("terrainrumbling")?.Attribute("height"));
        Assert.Equal("2", (string?)power.Element("terrainrumbling")?.Attribute("width"));
        Assert.Equal("15", (string?)power.Element("terrainrumbling")?.Attribute("speed"));
        Assert.Equal("2", (string?)power.Element("terrainrumbling")?.Attribute("fadetime"));

        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_terrainSpeedBox").Text = "-1");
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_terrainSpeedBox").Text = "15";
            GetPrivateField<Button>(editor, "_removeCameraRangeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Null(FindPower(XDocument.Load(path), "P").Element("camerashake")?.Attribute("range"));
    }

    [Fact]
    public void OriginalEffectsOnlyShowPresentAttributes()
    {
        var original = XElement.Parse("""
            <power name="Original" type="GodPower">
              <camerashake duration="0.5" strength="0.25"></camerashake>
              <terrainrumbling height="0.25" width="2"></terrainrumbling>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, original)], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            Assert.True(GetPrivateField<Grid>(editor, "_cameraShakeRow").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_cameraRangeField").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_addCameraRangeButton").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_terrainRumblingRow").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_terrainSpeedField").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_terrainFadeTimeField").IsVisible);
        });
    }

    [Fact]
    public void MultipleSoundSetsRoundTripWithOptionalAttributes()
    {
        using var workspace = new TestWorkspace(nameof(MultipleSoundSetsRoundTripWithOptionalAttributes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /><power name=\"Other\" type=\"GodPower\"><soundset type=\"StartSound\" listenertype=\"All\">ModSound</soundset></power></powersmod>");
        var original = XElement.Parse("<power name=\"Original\" type=\"GodPower\"><soundset type=\"StartSound\" listenertype=\"All\">OriginalSound</soundset></power>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, original)], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            var add = GetPrivateField<Button>(editor, "_addSoundSetButton");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rows = GetPrivateField<StackPanel>(editor, "_soundSetRows").Children
                .OfType<WrapPanel>().ToList();
            Assert.Equal(2, rows.Count);
            static StackPanel Field(WrapPanel row, string label) => row.Children
                .OfType<StackPanel>().Single(field =>
                    field.Children.OfType<TextBlock>().First().Text == label);
            var firstSound = Field(rows[0], "Sound").Children.OfType<AutoCompleteBox>().Single();
            Assert.Contains("OriginalSound", firstSound.ItemsSource!.Cast<string>());
            Assert.Contains("ModSound", firstSound.ItemsSource!.Cast<string>());
            firstSound.Text = "FirstSound";
            Assert.Equal("FirstSound", firstSound.Text);
            Field(rows[1], "Sound").Children.OfType<AutoCompleteBox>().Single().Text = "SecondSound";
            Field(rows[1], "Type").Children.OfType<ComboBox>().Single().SelectedItem = "ExistSound";
            Field(rows[1], "Listener").Children.OfType<ComboBox>().Single().SelectedItem = "IfOnScreenAlly";
            rows[1].Children.OfType<Button>().Single(button => (string?)button.Content == "Delay")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field(rows[1], "Delay").Children.OfType<TextBox>().Single().Text = "2.5";
            rows[1].Children.OfType<Button>().Single(button => (string?)button.Content == "Fade in")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field(rows[1], "Fade in").Children.OfType<TextBox>().Single().Text = "1";
            foreach (var check in rows[1].Children.OfType<CheckBox>()) check.IsChecked = true;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var sounds = FindPower(XDocument.Load(path), "P").Elements("soundset").ToList();
        Assert.Equal(2, sounds.Count);
        Assert.Equal("FirstSound", sounds[0].Value);
        Assert.Equal("SecondSound", sounds[1].Value);
        Assert.Equal("ExistSound", (string?)sounds[1].Attribute("type"));
        Assert.Equal("IfOnScreenAlly", (string?)sounds[1].Attribute("listenertype"));
        Assert.Equal("2.5", (string?)sounds[1].Attribute("playdelay"));
        Assert.Equal("1", (string?)sounds[1].Attribute("fadein"));
        foreach (var flag in new[] { "loop", "positional", "fade", "usesecondlocation" })
            Assert.Equal("", (string?)sounds[1].Attribute(flag));

        AvaloniaTestHost.Run(() =>
        {
            var rows = GetPrivateField<StackPanel>(editor, "_soundSetRows").Children
                .OfType<WrapPanel>().ToList();
            var delay = rows[1].Children.OfType<StackPanel>().Single(field =>
                field.Children.OfType<TextBlock>().First().Text == "Delay");
            delay.Children.OfType<TextBox>().Single().Text = "-1";
        });
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            var rows = GetPrivateField<StackPanel>(editor, "_soundSetRows").Children
                .OfType<WrapPanel>().ToList();
            var delay = rows[1].Children.OfType<StackPanel>().Single(field =>
                field.Children.OfType<TextBlock>().First().Text == "Delay");
            delay.Children.OfType<TextBox>().Single().Text = "2.5";
            rows[0].Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Single(FindPower(XDocument.Load(path), "P").Elements("soundset"));
    }

    [Fact]
    public void OriginalSoundSetPreservesLegacyFlagValue()
    {
        var original = XElement.Parse("""
            <power name="Original" type="GodPower">
              <soundset type="StartSound" listenertype="IfOnScreenAll" usesecondlocation="1">Birth</soundset>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, original)], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            var row = Assert.IsType<WrapPanel>(Assert.Single(
                GetPrivateField<StackPanel>(editor, "_soundSetRows").Children));
            Assert.False(GetPrivateField<Button>(editor, "_addSoundSetButton").IsVisible);
            Assert.DoesNotContain(row.Children.OfType<Button>().Where(button => button is not CheckBox),
                button => button.IsVisible);
            Assert.True(row.Children.OfType<CheckBox>().Single(check =>
                (string?)check.Content == "Use Second Location").IsChecked);
            Assert.False(row.Children.OfType<CheckBox>().Single(check =>
                (string?)check.Content == "Loop").IsVisible);
        });
        Assert.Equal("1", (string?)original.Element("soundset")?.Attribute("usesecondlocation"));
    }

    [Fact]
    public void ExtraPowerFieldsRoundTripAndValidate()
    {
        using var workspace = new TestWorkspace(nameof(ExtraPowerFieldsRoundTripAndValidate));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var original = XElement.Parse("<power name=\"Original\" type=\"GodPower\"><lighting>gp_tempest</lighting></power>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, original)], path,
            protoUnitNames: ["Revealer", "Scout"], unitTypeNames: ["LogicalTypeTest"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_dualPowerMinDistanceBox").Text = "3.5";
            GetPrivateField<TextBox>(editor, "_powerBlockerBox").Text = "40";
            GetPrivateField<TextBox>(editor, "_revealRadiusBox").Text = "25";
            GetPrivateField<AutoCompleteBox>(editor, "_revealUnitBox").Text = "Scout";
            var lighting = GetPrivateField<AutoCompleteBox>(editor, "_lightingBox");
            Assert.Contains("gp_tempest", lighting.ItemsSource!.Cast<string>());
            lighting.Text = "gp_custom";
            GetPrivateField<TextBox>(editor, "_accuracyBox").Text = "2";
            Assert.Equal("1", GetPrivateField<TextBox>(editor, "_accuracyBox").Text);
            GetPrivateField<TextBox>(editor, "_allyDamageRatioBox").Text = "0.25";
            GetPrivateField<AutoCompleteBox>(editor, "_resetAbilityChargeBox").Text = "LogicalTypeTest";
            GetPrivateField<CheckBox>(editor, "_playsBattleMusicCheck").IsChecked = true;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("3.5", power.Element("dualpowermindistance")?.Value);
        Assert.Equal("40", power.Element("powerblocker")?.Value);
        Assert.Equal("25", (string?)power.Element("reveallos")?.Attribute("radius"));
        Assert.Equal("Scout", power.Element("reveallos")?.Value);
        Assert.Equal("gp_custom", power.Element("lighting")?.Value);
        Assert.Equal("1", power.Element("accuracy")?.Value);
        Assert.Equal("0.25", power.Element("allydamageratio")?.Value);
        Assert.Equal("LogicalTypeTest", power.Element("abstractpowerresetsabilitychargetype")?.Value);
        Assert.NotNull(power.Element("playsbattlemusic"));

        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_powerBlockerBox").Text = "-1");
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_powerBlockerBox").Text = "40";
            GetPrivateField<AutoCompleteBox>(editor, "_resetAbilityChargeBox").Text = "InvalidType";
        });
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
    }

    [Fact]
    public void OriginalExtraPowerFieldsHideMissingValues()
    {
        var source = XElement.Parse("""
            <power name="Original" type="GodPower">
              <reveallos radius="25.0">Revealer</reveallos>
              <lighting>gp_tempest</lighting>
              <playsbattlemusic></playsbattlemusic>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, source)], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Original");
            Assert.True(GetPrivateField<Grid>(editor, "_revealLosRow").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_lightingRow").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_playsBattleMusicRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_accuracyRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_powerBlockerRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_resetAbilityChargeRow").IsVisible);
        });
    }

    [Fact]
    public void OtherAttributesPickerShowsSelectedRowAndRemovalClearsXml()
    {
        using var workspace = new TestWorkspace(nameof(OtherAttributesPickerShowsSelectedRowAndRemovalClearsXml));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.True(GetPrivateField<ComboBox>(editor, "_placementBox").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_lightingRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_rangeIndicatorRow").IsVisible);
            Assert.False(GetPrivateField<AutoCompleteBox>(editor, "_otherAttributePicker").IsVisible);
            var pickerRow = GetPrivateField<Grid>(editor, "_otherAttributesPickerRow");
            Assert.Same(pickerRow, Assert.IsType<StackPanel>(pickerRow.Parent).Children.Last());
            GetPrivateField<Button>(editor, "_otherAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = GetPrivateField<AutoCompleteBox>(editor, "_otherAttributePicker");
            Assert.True(picker.IsVisible);
            Assert.Contains("Lighting", picker.ItemsSource!.Cast<string>());
            picker.SelectedItem = "Lighting";
            Assert.False(picker.IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_lightingRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_rangeIndicatorRow").IsVisible);
            Assert.Equal(HorizontalAlignment.Left,
                GetPrivateField<AutoCompleteBox>(editor, "_lightingBox").HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Left,
                GetPrivateField<AutoCompleteBox>(editor, "_resetAbilityChargeBox").HorizontalAlignment);
            Assert.True(GetPrivateField<Grid>(editor, "_lightingRow").ColumnDefinitions[1].Width.IsAuto);
            GetPrivateField<AutoCompleteBox>(editor, "_lightingBox").Text = "gp_custom";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("gp_custom", FindPower(XDocument.Load(path), "P").Element("lighting")?.Value);
        AvaloniaTestHost.Run(() =>
        {
            var generation = GetPrivateField<int>(editor, "_populateGeneration");
            GetPrivateField<Grid>(editor, "_lightingRow").Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(generation, GetPrivateField<int>(editor, "_populateGeneration"));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Null(FindPower(XDocument.Load(path), "P").Element("lighting"));
        AvaloniaTestHost.Run(() => Assert.False(GetPrivateField<Grid>(editor, "_lightingRow").IsVisible));
    }

    [Fact]
    public void RemovingSoundSetAttributeClearsAllEntriesOnly()
    {
        using var workspace = new TestWorkspace(nameof(RemovingSoundSetAttributeClearsAllEntriesOnly));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="P" type="GodPower">
              <soundset type="StartSound" listenertype="All">One</soundset>
              <soundset type="EndSound" listenertype="All">Two</soundset>
              <camerashake duration="0.5" strength="0.25"></camerashake>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.True(GetPrivateField<Grid>(editor, "_soundSetsSection").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_cameraShakeRow").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_accuracyRow").IsVisible);
            GetPrivateField<Grid>(editor, "_soundSetsSection").Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Empty(power.Elements("soundset"));
        Assert.NotNull(power.Element("camerashake"));
        AvaloniaTestHost.Run(() => Assert.False(GetPrivateField<Grid>(editor, "_soundSetsSection").IsVisible));
    }

    [Fact]
    public void InvalidPowerCostDoesNotWriteXml()
    {
        using var workspace = new TestWorkspace(nameof(InvalidPowerCostDoesNotWriteXml));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><cost>2</cost></power></powersmod>");
        var before = File.ReadAllBytes(path);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_costBox").Text = "-1";
        });

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingPowerTypeBlocksSaveHighlightsSelectorAndCanBeCorrected()
    {
        using var workspace = new TestWorkspace(nameof(
            MissingPowerTypeBlocksSaveHighlightsSelectorAndCanBeCorrected));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Untyped' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Untyped");
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").IsDropDownOpen = true;
        });

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("Select a Power Type before saving.", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBlock>(editor, "_statusMessage").Text));
        Assert.Contains("invalid-field", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").Classes));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").IsDropDownOpen));

        AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "Bolt");
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("Bolt", (string?)FindPower(XDocument.Load(path), "Untyped").Attribute("type"));
        Assert.DoesNotContain("invalid-field", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").Classes));
    }

    [Fact]
    public void ExistingExclusivePowerCanAddItsOptionalErrorMessage()
    {
        using var workspace = new TestWorkspace(nameof(ExistingExclusivePowerCanAddItsOptionalErrorMessage));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><exclusive></exclusive></power></powersmod>");
        var stringsPath = workspace.WriteText("stringmods.txt", "Language = \"English\"\n");
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_exclusiveMessageBox").Text = "Power is already active";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("STR_ERROR_POWER_P", FindPower(XDocument.Load(path), "P")
            .Element("exclusiveerrormsgid")?.Value);
        Assert.Equal("Power is already active", StringTableDocument.Parse(File.ReadAllText(stringsPath))
            .ToDictionary()["STR_ERROR_POWER_P"]);
    }

    [Fact]
    public void RenamingPowerMigratesItsGeneratedStringIdsAndTexts()
    {
        using var workspace = new TestWorkspace(nameof(RenamingPowerMigratesItsGeneratedStringIdsAndTexts));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="OldPower" type="GodPower">
                <displaynameid>STR_POWER_OLD_POWER</displaynameid>
                <rolloverid>STR_POWER_OLD_POWER_LR</rolloverid>
                <activepowerrolloverid>STR_POWER_OLD_POWER_ACTIVE_LR</activepowerrolloverid>
                <exclusive></exclusive>
                <exclusiveerrormsgid>STR_ERROR_POWER_OLD_POWER</exclusiveerrormsgid>
              </power>
            </powersmod>
            """);
        var stringsPath = workspace.WriteText("stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_OLD_POWER"   ;   Str = "Old name"
            ID = "STR_POWER_OLD_POWER_LR"   ;   Str = "Old rollover"
            ID = "STR_POWER_OLD_POWER_ACTIVE_LR"   ;   Str = "Old active"
            ID = "STR_ERROR_POWER_OLD_POWER"   ;   Str = "Old exclusive error"
            """);
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("OldPower");
            GetPrivateField<TextBox>(editor, "_nameBox").Text = "NewPower";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = FindPower(XDocument.Load(path), "NewPower");
        Assert.Equal("STR_POWER_NEW_POWER", saved.Element("displaynameid")?.Value);
        Assert.Equal("STR_POWER_NEW_POWER_LR", saved.Element("rolloverid")?.Value);
        Assert.Equal("STR_POWER_NEW_POWER_ACTIVE_LR", saved.Element("activepowerrolloverid")?.Value);
        Assert.Equal("STR_ERROR_POWER_NEW_POWER", saved.Element("exclusiveerrormsgid")?.Value);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("Old name", strings["STR_POWER_NEW_POWER"]);
        Assert.Equal("Old rollover", strings["STR_POWER_NEW_POWER_LR"]);
        Assert.Equal("Old active", strings["STR_POWER_NEW_POWER_ACTIVE_LR"]);
        Assert.Equal("Old exclusive error", strings["STR_ERROR_POWER_NEW_POWER"]);
        Assert.DoesNotContain("STR_POWER_OLD_POWER", strings.Keys);
        Assert.DoesNotContain("STR_ERROR_POWER_OLD_POWER", strings.Keys);
    }

    [Fact]
    public void DiscardingPowerPropertiesLeavesXmlAndStringsUnchanged()
    {
        using var workspace = new TestWorkspace(nameof(DiscardingPowerPropertiesLeavesXmlAndStringsUnchanged));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="P" type="GodPower"><displaynameid>STR_POWER_P</displaynameid></power>
            </powersmod>
            """);
        var stringsPath = workspace.WriteText("stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_P"   ;   Str = "Original"
            """);
        var xmlBefore = File.ReadAllBytes(path);
        var stringsBefore = File.ReadAllBytes(stringsPath);
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_displayNameBox").Text = "Changed";
            editor.DiscardPowerChanges("P", FindPower(XDocument.Load(path), "P"));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        Assert.Equal(xmlBefore, File.ReadAllBytes(path));
        Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath));
    }

    [Fact]
    public void ExternalStringChangeBlocksPowerAndStringSaveTogether()
    {
        using var workspace = new TestWorkspace(nameof(ExternalStringChangeBlocksPowerAndStringSaveTogether));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\"><displaynameid>STR_POWER_P</displaynameid></power></powersmod>");
        var stringsPath = workspace.WriteText("stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_P"   ;   Str = "Original"
            """);
        var xmlBefore = File.ReadAllBytes(path);
        var editor = AvaloniaTestHost.Run(() => CreateEditorWithStrings(path, stringsPath));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<TextBox>(editor, "_displayNameBox").Text = "Edited";
        });
        var externalStrings = File.ReadAllText(stringsPath).Replace("Original", "External", StringComparison.Ordinal);
        File.WriteAllText(stringsPath, externalStrings);

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(xmlBefore, File.ReadAllBytes(path));
        Assert.Equal(externalStrings, File.ReadAllText(stringsPath));
    }

    [Fact]
    public void AppliedRawXmlRoundTripsUnknownContentAndRenamesThePower()
    {
        using var workspace = new TestWorkspace(nameof(AppliedRawXmlRoundTripsUnknownContentAndRenamesThePower));
        var path = workspace.WriteText(
            "powers_mods.xml",
            "<powersmod><power name=\"OldPower\" type=\"GodPower\"><future old=\"yes\" /></power></powersmod>",
            new UTF8Encoding(false));
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("OldPower");
            GetPrivateField<TextEditor>(editor, "_rawXmlEditor").Text =
                "<power name=\"NewPower\" type=\"GodPower\"><future old=\"yes\"><payload value=\"42\" /></future></power>";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = XDocument.Load(path);
        Assert.Null(PowerCatalog.Find(saved, "OldPower"));
        var renamed = FindPower(saved, "NewPower");
        Assert.Equal("42", (string?)renamed.Element("future")?.Element("payload")?.Attribute("value"));
    }

    [Fact]
    public void StructuredEditPreservesUnknownAndDeprecatedPowerXml()
    {
        using var workspace = new TestWorkspace(nameof(StructuredEditPreservesUnknownAndDeprecatedPowerXml));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="LegacyBolt" type="Bolt" futureRoot="keep">
                <cost>10</cost>
                <strikeproto>KnownBolt</strikeproto>
                <scorchfactor mode="legacy">0.5</scorchfactor>
                <futurecontainer custom="yes"><payload value="42" /></futurecontainer>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["KnownBolt"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("LegacyBolt");
            GetPrivateField<TextBox>(editor, "_costBox").Text = "20";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = FindPower(XDocument.Load(path), "LegacyBolt");
        Assert.Equal("20", saved.Element("cost")?.Value);
        Assert.Equal("keep", (string?)saved.Attribute("futureRoot"));
        Assert.Equal("0.5", saved.Element("scorchfactor")?.Value);
        Assert.Equal("legacy", (string?)saved.Element("scorchfactor")?.Attribute("mode"));
        Assert.Equal("42", (string?)saved.Element("futurecontainer")?.Element("payload")?.Attribute("value"));
    }

    [Fact]
    public void EditorOwnedAbilityWriteMergesWithoutLosingPendingPowerXml()
    {
        using var workspace = new TestWorkspace(nameof(EditorOwnedAbilityWriteMergesWithoutLosingPendingPowerXml));
        var path = workspace.WriteText(
            "powers_mods.xml",
            "<powersmod><power name=\"EditedPower\" type=\"GodPower\"><value old=\"yes\" /></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("EditedPower");
            GetPrivateField<TextEditor>(editor, "_rawXmlEditor").Text =
                "<power name=\"EditedPower\" type=\"GodPower\"><value edited=\"yes\" /></power>";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));

        var abilityWrite = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        abilityWrite.Root!.Add(new XElement("power",
            new XAttribute("name", "ExternalAbility"),
            new XAttribute("type", "UnitAction"),
            new XElement("abilitydata", new XAttribute("keep", "yes"))));
        abilityWrite.Save(path, SaveOptions.DisableFormatting);

        AvaloniaTestHost.Run(editor.AcknowledgePowerFileSavedByEditor);
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var saved = XDocument.Load(path);
        Assert.Equal("yes", (string?)FindPower(saved, "EditedPower").Element("value")?.Attribute("edited"));
        Assert.Equal("yes", (string?)FindPower(saved, "ExternalAbility").Element("abilitydata")?.Attribute("keep"));
    }

    [Fact]
    public void SavingAnUntouchedPowerDocumentDoesNotRewriteIt()
    {
        using var workspace = new TestWorkspace(nameof(SavingAnUntouchedPowerDocumentDoesNotRewriteIt));
        const string xml = "<powersmod>\r\n  <!-- exact -->\r\n  <power name=\"Untouched\" type=\"GodPower\" />\r\n</powersmod>";
        var path = workspace.WriteText("powers_mods.xml", xml, new UTF8Encoding(false));
        var before = File.ReadAllBytes(path);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void DeconstructionTypeShowsAndWritesItsTwoSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(DeconstructionTypeShowsAndWritesItsTwoSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Deconstruction\" /></powersmod>");
        var baseSound = XElement.Parse("""
            <power name="Base" type="Deconstruction">
              <cracksoundset>KnownCrackSound</cracksoundset>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Base", true, baseSound)], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("Deconstruction", GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_cracksSoundRow").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_timeFactorRow").Parent);
            Assert.False(GetPrivateField<Button>(editor, "_removeCracksSoundButton").IsVisible);
            Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
            Assert.Contains("KnownCrackSound",
                GetPrivateField<AutoCompleteBox>(editor, "_cracksSoundBox").ItemsSource!.Cast<string>());
            GetPrivateField<AutoCompleteBox>(editor, "_cracksSoundBox").Text = "CustomCrackSound";
            GetPrivateField<TextBox>(editor, "_crackIntervalMinBox").Text = "2.5";
            GetPrivateField<TextBox>(editor, "_crackIntervalMaxBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_timeFactorBox").Text = "0.4";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("CustomCrackSound", power.Element("cracksoundset")?.Value);
        Assert.Equal("2.5", power.Element("crackintervalmin")?.Value);
        Assert.Equal("4", power.Element("crackintervalmax")?.Value);
        Assert.Equal("0.4", power.Element("timefactor")?.Value);
    }

    [Fact]
    public void OtherSpecificAttributesListsGroupsAndRemovalClearsTheirXml()
    {
        using var workspace = new TestWorkspace(nameof(OtherSpecificAttributesListsGroupsAndRemovalClearsTheirXml));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Bolt\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            Assert.False(GetPrivateField<Grid>(editor, "_cracksSoundRow").IsVisible);
            Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
            Assert.Equal("Other Additional Attributes",
                GetPrivateField<Button>(editor, "_otherSpecificAttributesButton").Content);
            GetPrivateField<Button>(editor, "_otherSpecificAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = GetPrivateField<AutoCompleteBox>(editor, "_otherSpecificAttributePicker");
            Assert.Equal(["Cracks Sound", "Time Factor", "Stun lenght", "Shockwave VFX", "Throw properties",
                    "Tree Unit", "Tree amount", "Tree Spawn Interval", "Tree Effect VFX", "Growth radius",
                    "Effect VFX", "Damage Percent on Conversion", "VFX Proto", "Effect time",
                    "Spider properties", "Number to create", "Separation Distance", "Hatch properties",
                    "Warn time", "Initial Delay", "Human Death Sound", "Unit Effect Time",
                    "Vortex properties", "Drop Interval", "Max Retry", "Max Unit Height",
                    "Lift Sound", "Land Sound", "Spawn Proto", "Max Spawn Amount", "Revealer Unit", "Tartarian Gate",
                    "Rumble Time", "First Spawn delay", "Spawn interval", "Number to Transform",
                    "Stealth Level", "Tree VFX", "Stay Time", "Slow Heal Multiplier",
                    "Create Unit", "Attach Unit", "Age Adjustment", "Create placement",
                    "Terrain Creep", "Wave Unit", "Initial Damage", "Attack Interval", "Damage Cap",
                    "Portal", "Portal timing", "Placement Radius", "Swarm Unit",
                    "Swarm Way Points", "Damage Interval", "Throw Max Size", "Camera Shake Delay", "Tree Bend"],
                picker.ItemsSource!.Cast<string>());
            picker.SelectedItem = "Cracks Sound";
            Assert.False(picker.IsVisible);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows"),
                GetPrivateField<Grid>(editor, "_cracksSoundRow").Parent);
            Assert.True(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
            GetPrivateField<AutoCompleteBox>(editor, "_cracksSoundBox").Text = "ExtraCrack";
            GetPrivateField<TextBox>(editor, "_crackIntervalMinBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_crackIntervalMaxBox").Text = "3";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.NotNull(FindPower(XDocument.Load(path), "P").Element("cracksoundset"));
        AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor, "_removeCracksSoundButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Null(power.Element("cracksoundset"));
        Assert.Null(power.Element("crackintervalmin"));
        Assert.Null(power.Element("crackintervalmax"));
        AvaloniaTestHost.Run(() =>
            Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "Deconstruction";
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_cracksSoundRow").Parent);
            Assert.False(GetPrivateField<Button>(editor, "_removeCracksSoundButton").IsVisible);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("Deconstruction", (string?)FindPower(XDocument.Load(path), "P").Attribute("type"));
    }

    [Fact]
    public void ChangingPowerTypeRemovesSpecificXmlNotSupportedByTheNewType()
    {
        using var workspace = new TestWorkspace(nameof(ChangingPowerTypeRemovesSpecificXmlNotSupportedByTheNewType));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="P" type="DroughtLand">
                <placement>Full</placement>
                <cost>100</cost>
                <terraincreep creep="DroughtLand" maxradius="30" />
                <landprotounit>LandVfx</landprotounit>
                <waveprotounit>SharedWave</waveprotounit>
                <initialdamage fraction="0.1">Building</initialdamage>
                <burntime>5</burntime>
                <damageinterval>0.25</damageinterval>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["SharedWave"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "GreatFlood";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_statusMessage").Text));

        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("GreatFlood", power.Attribute("type")?.Value);
        Assert.Equal("Full", power.Element("placement")?.Value);
        Assert.Equal("100", power.Element("cost")?.Value);
        Assert.Equal("SharedWave", power.Element("waveprotounit")?.Value);
        Assert.Null(power.Element("terraincreep"));
        Assert.Null(power.Element("landprotounit"));
        Assert.Null(power.Element("initialdamage"));
        Assert.Null(power.Element("burntime"));
        Assert.Null(power.Element("damageinterval"));
    }

    [Fact]
    public void EveryAdditionalSpecificGroupGetsARemoveButtonThatClearsItsXml()
    {
        using var workspace = new TestWorkspace(nameof(EveryAdditionalSpecificGroupGetsARemoveButtonThatClearsItsXml));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="P" type="Bolt">
                <terraincreep creep="DroughtLand" maxradius="30" />
                <damageinterval>0.25</damageinterval>
                <damagecap unittype="AbstractArcher">12.5</damagecap>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });

        foreach (var name in new[] { "Terrain Creep", "Damage Interval", "Damage Cap" })
        {
            AvaloniaTestHost.Run(() =>
            {
                var host = GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows");
                var remove = DescendantControls(host).OfType<Button>().Single(button =>
                    button.Tag is string tag && tag.Equals(name, StringComparison.OrdinalIgnoreCase));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
        }
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var power = FindPower(XDocument.Load(path), "P");
        Assert.Null(power.Element("terraincreep"));
        Assert.Null(power.Element("damageinterval"));
        Assert.Null(power.Element("damagecap"));
        AvaloniaTestHost.Run(() =>
            Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible));
    }

    [Fact]
    public void OriginalSpecificAdditionalHeaderOnlyAppearsWhenAnAdditionalGroupExists()
    {
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
        [
            new PowerDefinition("Plain", true, XElement.Parse("<power name=\"Plain\" type=\"Bolt\" />")),
            new PowerDefinition("WithExtra", true, XElement.Parse(
                "<power name=\"WithExtra\" type=\"Bolt\"><timefactor>0.5</timefactor></power>"))
        ], null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Plain");
            Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
            Assert.False(GetPrivateField<Grid>(editor, "_otherSpecificPickerRow").IsVisible);
            Assert.False(GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").IsEnabled);
            editor.SelectPower("WithExtra");
            Assert.True(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_timeFactorRow").IsVisible);
        });
    }

    [Fact]
    public void ShockwaveTypeWritesItsThreeSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(ShockwaveTypeWritesItsThreeSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Shockwave\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXShockwave", "VFXOther"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_shockwaveTopRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_throwPropertiesRow").Parent);
            Assert.Same(GetPrivateField<Grid>(editor, "_throwPropertiesRow"), specific.Children[^1]);
            Assert.True(GetPrivateField<StackPanel>(editor, "_stunLengthFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_shockwaveVfxFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeStunLengthButton").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeShockwaveVfxButton").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeThrowPropertiesButton").IsVisible);
            Assert.Equal(VerticalAlignment.Top,
                GetPrivateField<Grid>(editor, "_throwPropertiesRow").Children.OfType<TextBlock>().Single().VerticalAlignment);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_throwDistanceFields"),
                GetPrivateField<TextBox>(editor, "_minThrowDistanceBox").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_throwDistanceFields"),
                GetPrivateField<TextBox>(editor, "_maxThrowDistanceBox").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_throwHeightFields"),
                GetPrivateField<TextBox>(editor, "_minThrowHeightBox").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_throwVelocityFields"),
                GetPrivateField<TextBox>(editor, "_maxThrowVelocityBox").Parent);
            GetPrivateField<TextBox>(editor, "_stunLengthBox").Text = "1.25";
            GetPrivateField<AutoCompleteBox>(editor, "_shockwaveVfxBox").SelectedItem = "VFXShockwave";
            foreach (var (field, value) in new[]
                     {
                         ("_minThrowDistanceBox", "7"), ("_maxThrowDistanceBox", "9"),
                         ("_minThrowHeightBox", "1"), ("_maxThrowHeightBox", "2"),
                         ("_minThrowVelocityBox", "12"), ("_maxThrowVelocityBox", "13")
                     })
                GetPrivateField<TextBox>(editor, field).Text = value;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("1.25", power.Element("stunlength")?.Value);
        Assert.Equal("VFXShockwave", power.Element("shockwavevfx")?.Value);
        Assert.Equal("7", power.Element("minthrowdistance")?.Value);
        Assert.Equal("9", power.Element("maxthrowdistance")?.Value);
        Assert.Equal("1", power.Element("minthrowheight")?.Value);
        Assert.Equal("2", power.Element("maxthrowheight")?.Value);
        Assert.Equal("12", power.Element("minthrowvelocity")?.Value);
        Assert.Equal("13", power.Element("maxthrowvelocity")?.Value);
    }

    [Fact]
    public void ShockwaveGroupsCanBeAddedAndRemovedIndependentlyOnOtherTypes()
    {
        using var workspace = new TestWorkspace(nameof(ShockwaveGroupsCanBeAddedAndRemovedIndependentlyOnOtherTypes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Bolt\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXShockwave"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            var add = GetPrivateField<Button>(editor, "_otherSpecificAttributesButton");
            var picker = GetPrivateField<AutoCompleteBox>(editor, "_otherSpecificAttributePicker");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            picker.SelectedItem = "Stun lenght";
            GetPrivateField<TextBox>(editor, "_stunLengthBox").Text = "2";
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            picker.SelectedItem = "Shockwave VFX";
            GetPrivateField<AutoCompleteBox>(editor, "_shockwaveVfxBox").SelectedItem = "VFXShockwave";
            Assert.Same(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows"),
                GetPrivateField<Grid>(editor, "_shockwaveTopRow").Parent);
            GetPrivateField<Button>(editor, "_removeStunLengthButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(GetPrivateField<StackPanel>(editor, "_stunLengthFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_shockwaveVfxFields").IsVisible);
            Assert.Equal("Shockwave VFX", GetPrivateField<TextBlock>(editor, "_stunLengthLabel").Text);
            Assert.False(GetPrivateField<TextBlock>(editor, "_shockwaveVfxInlineLabel").IsVisible);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Null(power.Element("stunlength"));
        Assert.Equal("VFXShockwave", power.Element("shockwavevfx")?.Value);
    }

    [Fact]
    public void GaiaForestTypeWritesItsFiveSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(GaiaForestTypeWritesItsFiveSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GaiaForest\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["TreeGaia", "VFXTreeGaia"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_gaiaTreeTopRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_gaiaSpawnRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_growthRadiusRow").Parent);
            GetPrivateField<AutoCompleteBox>(editor, "_treeUnitBox").SelectedItem = "TreeGaia";
            GetPrivateField<TextBox>(editor, "_treeAmountBox").Text = "20";
            GetPrivateField<TextBox>(editor, "_treeSpawnIntervalMinBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_treeSpawnIntervalMaxBox").Text = "5";
            GetPrivateField<AutoCompleteBox>(editor, "_treeEffectVfxBox").SelectedItem = "VFXTreeGaia";
            GetPrivateField<TextBox>(editor, "_growthRadiusStartBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_growthRadiusIncreaseRateBox").Text = "0.02";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("TreeGaia", power.Element("treeproto")?.Value);
        Assert.Equal("20", power.Element("totalnumberoftrees")?.Value);
        Assert.Equal("3", power.Element("treespawnintervalmin")?.Value);
        Assert.Equal("5", power.Element("treespawnintervalmax")?.Value);
        Assert.Equal("VFXTreeGaia", power.Element("treeeffectproto")?.Value);
        Assert.Equal("2", power.Element("growthradiusstart")?.Value);
        Assert.Equal("0.02", power.Element("growthradiusincreaserate")?.Value);
    }

    [Fact]
    public void GaiaSharedRowsPromoteSingleAdditionalAttributeToMainLabel()
    {
        using var workspace = new TestWorkspace(nameof(GaiaSharedRowsPromoteSingleAdditionalAttributeToMainLabel));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Bolt\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXTreeGaia"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
        });
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<Button>(editor, "_otherSpecificAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_otherSpecificAttributePicker").SelectedItem = "Tree Effect VFX";
            Assert.Equal("Tree Effect VFX", GetPrivateField<TextBlock>(editor, "_treeSpawnMainLabel").Text);
            Assert.False(GetPrivateField<TextBlock>(editor, "_treeEffectVfxInlineLabel").IsVisible);
            GetPrivateField<AutoCompleteBox>(editor, "_treeEffectVfxBox").SelectedItem = "VFXTreeGaia";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("VFXTreeGaia", FindPower(XDocument.Load(path), "P").Element("treeeffectproto")?.Value);
    }

    [Fact]
    public void ValorTraitorAndChaosWriteTheirSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(ValorTraitorAndChaosWriteTheirSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"ValorP\" type=\"Valor\" />" +
            "<power name=\"TraitorP\" type=\"Traitor\" />" +
            "<power name=\"ChaosP\" type=\"Chaos\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXHeroBirth", "VFXTraitor", "VFXChaos"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("ValorP");
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_effectVfxRow").Parent);
            Assert.False(GetPrivateField<Button>(editor, "_removeEffectVfxButton").IsVisible);
            GetPrivateField<AutoCompleteBox>(editor, "_effectVfxBox").SelectedItem = "VFXHeroBirth";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("TraitorP");
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_damagePercentConversionRow").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_vfxProtoRow").Parent);
            GetPrivateField<TextBox>(editor, "_damagePercentConversionBox").Text = "0.25";
            GetPrivateField<AutoCompleteBox>(editor, "_vfxProtoBox").SelectedItem = "VFXTraitor";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("ChaosP");
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_effectTimeRow").Parent);
            GetPrivateField<TextBox>(editor, "_unitEffectTimeSecondsBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_sharedEffectTimeSecondsBox").Text = "50";
            GetPrivateField<AutoCompleteBox>(editor, "_vfxProtoBox").SelectedItem = "VFXChaos";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var document = XDocument.Load(path);
        Assert.Equal("VFXHeroBirth", FindPower(document, "ValorP").Element("effectproto")?.Value);
        Assert.Equal("0.25", FindPower(document, "TraitorP").Element("percentdamageonconvert")?.Value);
        Assert.Equal("VFXTraitor", FindPower(document, "TraitorP").Element("vfxproto")?.Value);
        Assert.Equal("2", FindPower(document, "ChaosP").Element("uniteffecttimeseconds")?.Value);
        Assert.Equal("50", FindPower(document, "ChaosP").Element("sharedeffecttimeseconds")?.Value);
        Assert.Equal("VFXChaos", FindPower(document, "ChaosP").Element("vfxproto")?.Value);
    }

    [Fact]
    public void GrowthRadiusRemoveButtonFollowsItsFields()
    {
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("P", true, XElement.Parse(
                "<power name=\"P\" type=\"GaiaForest\"><growthradiusstart>1</growthradiusstart></power>"))],
            null));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("P");
            var row = GetPrivateField<Grid>(editor, "_growthRadiusRow");
            Assert.True(row.ColumnDefinitions[1].Width.IsAuto);
            Assert.Equal(2, Grid.GetColumn(GetPrivateField<Button>(editor, "_removeGrowthRadiusButton")));
        });
    }

    [Fact]
    public void SpidersTypeWritesItsSevenSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(SpidersTypeWritesItsSevenSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Spiders\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Existing", true, XElement.Parse(
                "<power name=\"Existing\"><soundset>SpiderLairDeath</soundset></power>"))],
            path, protoUnitNames: ["Spider", "SpiderEgg", "SpiderDoor", "SpiderWarn"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_spiderPropertiesRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_spiderCountRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_hatchPropertiesRow").Parent);
            Assert.True(GetPrivateField<Grid>(editor, "_hatchPropertiesRow")
                .ColumnDefinitions[1].Width.IsAuto);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_spiderUnitFields"),
                GetPrivateField<AutoCompleteBox>(editor, "_spiderUnitBox").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_spiderAttackRangeFields"),
                GetPrivateField<TextBox>(editor, "_spiderAttackRangeBox").Parent);

            GetPrivateField<AutoCompleteBox>(editor, "_spiderUnitBox").SelectedItem = "Spider";
            GetPrivateField<AutoCompleteBox>(editor, "_spiderEggUnitBox").SelectedItem = "SpiderEgg";
            GetPrivateField<AutoCompleteBox>(editor, "_spiderDoorBox").SelectedItem = "SpiderDoor";
            GetPrivateField<AutoCompleteBox>(editor, "_spiderWarnBox").SelectedItem = "SpiderWarn";
            GetPrivateField<TextBox>(editor, "_spiderAttackRangeBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_spiderSpawnOffsetBox").Text = "-1";
            GetPrivateField<TextBox>(editor, "_numberToCreateBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_separationDistanceBox").Text = "4.5";
            GetPrivateField<TextBox>(editor, "_hatchTimeBox").Text = "5";
            GetPrivateField<TextBox>(editor, "_hatchIntervalMinBox").Text = "0.2";
            GetPrivateField<TextBox>(editor, "_hatchIntervalMaxBox").Text = "0.7";
            GetPrivateField<TextBox>(editor, "_warnTimeBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_initialDelayBox").Text = "0.75";
            var sound = GetPrivateField<AutoCompleteBox>(editor, "_humanDeathSoundBox");
            Assert.Contains("SpiderLairDeath", sound.ItemsSource!.Cast<string>());
            sound.Text = "CustomSpiderDeath";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("Spider", power.Element("spiderproto")?.Value);
        Assert.Equal("SpiderEgg", power.Element("spidereggproto")?.Value);
        Assert.Equal("SpiderDoor", power.Element("spiderdoorproto")?.Value);
        Assert.Equal("SpiderWarn", power.Element("spiderwarnproto")?.Value);
        Assert.Equal("2", power.Element("spiderattackrange")?.Value);
        Assert.Equal("-1", power.Element("spiderspawnoffset")?.Value);
        Assert.Equal("4", power.Element("numbertocreate")?.Value);
        Assert.Equal("4.5", power.Element("separationdistance")?.Value);
        Assert.Equal("5", power.Element("hatchtime")?.Value);
        Assert.Equal("0.2", power.Element("hatchintervalmin")?.Value);
        Assert.Equal("0.7", power.Element("hatchintervalmax")?.Value);
        Assert.Equal("0.5", power.Element("warntime")?.Value);
        Assert.Equal("0.75", power.Element("initialdelay")?.Value);
        Assert.Equal("CustomSpiderDeath", power.Element("humandeathsoundset")?.Value);
    }

    [Fact]
    public void VortexTypeWritesItsSpecificAttributeGroups()
    {
        using var workspace = new TestWorkspace(nameof(VortexTypeWritesItsSpecificAttributeGroups));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Vortex\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Existing", true, XElement.Parse(
                "<power name=\"Existing\"><liftsoundset>VortexLift</liftsoundset>" +
                "<landsoundset>VortexLand</landsoundset></power>"))],
            path, protoUnitNames:
                ["VFXVortexLanding", "VFXVortexStart", "VFXVortexFinish"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_spiderTimingRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_vortexPropertiesRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_vortexDropRow").Parent);
            Assert.False(GetPrivateField<StackPanel>(editor, "_warnTimeFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_initialDelayFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_unitEffectTimeFields").IsVisible);

            GetPrivateField<TextBox>(editor, "_initialDelayBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_unitEffectTimeBox").Text = "1";
            GetPrivateField<AutoCompleteBox>(editor, "_vortexLandVfxBox").SelectedItem = "VFXVortexLanding";
            GetPrivateField<AutoCompleteBox>(editor, "_vortexStartVfxBox").SelectedItem = "VFXVortexStart";
            GetPrivateField<AutoCompleteBox>(editor, "_vortexStartLinkedBox").SelectedItem = "VFXVortexStart";
            GetPrivateField<AutoCompleteBox>(editor, "_vortexFinishLinkedBox").SelectedItem = "VFXVortexFinish";
            GetPrivateField<TextBox>(editor, "_dropIntervalMinBox").Text = "0.01";
            GetPrivateField<TextBox>(editor, "_dropIntervalMaxBox").Text = "0.1";
            GetPrivateField<TextBox>(editor, "_maxRetryBox").Text = "30";
            GetPrivateField<TextBox>(editor, "_maxUnitHeightBox").Text = "25";
            var lift = GetPrivateField<AutoCompleteBox>(editor, "_liftSoundBox");
            var land = GetPrivateField<AutoCompleteBox>(editor, "_landSoundBox");
            Assert.Contains("VortexLift", lift.ItemsSource!.Cast<string>());
            Assert.Contains("VortexLand", land.ItemsSource!.Cast<string>());
            lift.Text = "CustomLift";
            land.Text = "CustomLand";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("0", power.Element("initialdelay")?.Value);
        Assert.Equal("1", power.Element("uniteffecttime")?.Value);
        Assert.Equal("VFXVortexLanding", power.Element("vortexlandingproto")?.Value);
        Assert.Equal("VFXVortexStart", power.Element("vortexstartproto")?.Value);
        Assert.Equal("VFXVortexStart", power.Element("vortexstartlinkedproto")?.Value);
        Assert.Equal("VFXVortexFinish", power.Element("vortexfinishlinkedproto")?.Value);
        Assert.Equal("0.01", power.Element("dropintervalmin")?.Value);
        Assert.Equal("0.1", power.Element("dropintervalmax")?.Value);
        Assert.Equal("30", power.Element("maxretryseconds")?.Value);
        Assert.Equal("25", power.Element("maxunitheight")?.Value);
        Assert.Equal("CustomLift", power.Element("liftsoundset")?.Value);
        Assert.Equal("CustomLand", power.Element("landsoundset")?.Value);
    }

    [Fact]
    public void TartarianGateTypeWritesItsSpecificAttributesAndReusesThrowProperties()
    {
        using var workspace = new TestWorkspace(
            nameof(TartarianGateTypeWritesItsSpecificAttributesAndReusesThrowProperties));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"TartarianGate\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path, protoUnitNames:
            ["TartarianSpawn", "RevealerToAll", "TartarianGate", "TartarianGateBirth", "TartarianGateFlame"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("P"));
        AvaloniaTestHost.Run(() =>
        {
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_tartarianSpawnRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_tartarianGateRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_tartarianSpawnTimingRow").Parent);
            Assert.Same(specific, GetPrivateField<Grid>(editor, "_throwPropertiesRow").Parent);
            Assert.False(GetPrivateField<Button>(editor, "_removeThrowPropertiesButton").IsVisible);

            GetPrivateField<AutoCompleteBox>(editor, "_spawnProtoBox").SelectedItem = "TartarianSpawn";
            GetPrivateField<TextBox>(editor, "_maxSpawnNumberBox").Text = "7";
            Assert.False(GetPrivateField<Grid>(editor, "_tartarianRevealerRow").IsVisible);
            GetPrivateField<Button>(editor, "_addTartarianRevealerButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<Grid>(editor, "_tartarianRevealerRow").IsVisible);
            GetPrivateField<AutoCompleteBox>(editor, "_tartarianRevealerBox").SelectedItem =
                "RevealerToAll";
            GetPrivateField<AutoCompleteBox>(editor, "_tartarianGateUnitBox").SelectedItem = "TartarianGate";
            GetPrivateField<AutoCompleteBox>(editor, "_tartarianGateBirthBox").SelectedItem = "TartarianGateBirth";
            GetPrivateField<AutoCompleteBox>(editor, "_tartarianGateFlameBox").SelectedItem = "TartarianGateFlame";
            GetPrivateField<TextBox>(editor, "_sinkHoleTimeBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_sinkHoleShakeBox").Text = "0.08";
            GetPrivateField<TextBox>(editor, "_rumbleTimeBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_firstSpawnDelayBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_spawnIntervalMinBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_spawnIntervalMaxBox").Text = "2";
            foreach (var (field, value) in new[]
                     {
                         ("_minThrowDistanceBox", "6"), ("_maxThrowDistanceBox", "9"),
                         ("_minThrowHeightBox", "1"), ("_maxThrowHeightBox", "2.5"),
                         ("_minThrowVelocityBox", "11"), ("_maxThrowVelocityBox", "15")
                     })
                GetPrivateField<TextBox>(editor, field).Text = value;
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("TartarianSpawn", power.Element("spawnproto")?.Value);
        Assert.Equal("7", power.Element("maxnumspawns")?.Value);
        Assert.Equal("RevealerToAll", power.Element("revealerproto")?.Value);
        Assert.Equal("TartarianGate", power.Element("tartariangateproto")?.Value);
        Assert.Equal("TartarianGateBirth", power.Element("gatebirthproto")?.Value);
        Assert.Equal("TartarianGateFlame", power.Element("gateflameproto")?.Value);
        Assert.Equal("3", power.Element("sinkholetime")?.Value);
        Assert.Equal("0.08", power.Element("sinkholeshakestrength")?.Value);
        Assert.Equal("3", power.Element("rumbletime")?.Value);
        Assert.Equal("1", power.Element("spawntimedelay")?.Value);
        Assert.Equal("1", power.Element("spawnintervalmin")?.Value);
        Assert.Equal("2", power.Element("spawnintervalmax")?.Value);
        Assert.Equal("6", power.Element("minthrowdistance")?.Value);
        Assert.Equal("15", power.Element("maxthrowvelocity")?.Value);
    }

    [Fact]
    public void ImplodeTypeWritesItsDedicatedGroupsAndKeepsThemOutOfAdditionalAttributes()
    {
        using var workspace = new TestWorkspace(
            nameof(ImplodeTypeWritesItsDedicatedGroupsAndKeepsThemOutOfAdditionalAttributes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"Implode\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Sounds", true, XElement.Parse(
                "<power name=\"Sounds\"><implodestartsoundset>ImplodeBirth</implodestartsoundset>" +
                "<implodeexplodesoundset>ImplodeDeath</implodeexplodesoundset></power>"))],
            path, protoUnitNames: ["ImplodeSphere", "ImplodeShockwave", "VFXImplodePullEffect"]));
        var numericFields = new Dictionary<string, string>
        {
            ["_implodePullRadiusBox"] = "25", ["_implodeEscapeRadiusBox"] = "30",
            ["_implodeStrengthIncrementBox"] = "0.1", ["_implodeMaxStrengthBox"] = "1",
            ["_implodeOverkillIncrementBox"] = "0.1", ["_implodeStrengthAdjustBox"] = "0.06",
            ["_implodeUnitAccelerationBox"] = "4", ["_implodeWindupLengthBox"] = "1.396",
            ["_implodeExplosionRadiusBox"] = "30", ["_implodeAccumulationBox"] = "0.1",
            ["_implodeMaxAccumulatedBox"] = "1", ["_implodeExplosionDelayBox"] = "1.6",
            ["_implodeShockwaveVelocityBox"] = "67.2", ["_implodeSphereScaleBox"] = "1.2",
            ["_implodeSphereHeightBox"] = "3", ["_implodeHeightAdjustBox"] = "2",
            ["_implodeMovementDelayBox"] = "2", ["_implodeFloatAdjustBox"] = "0.3",
            ["_implodeDamageIntervalBox"] = "999", ["_implodeBaseShearIncrementBox"] = "0.1",
            ["_implodeShearMultiplierBox"] = "0.2", ["_implodeBaseShearMaxBox"] = "1",
            ["_implodeDelayMinBox"] = "0.15", ["_implodeDelayMaxBox"] = "0.25",
            ["_implodeEndDelayMinBox"] = "0.01", ["_implodeEndDelayMaxBox"] = "0.1",
            ["_implodeMinStartingBox"] = "3", ["_implodeMinPullingBox"] = "4",
            ["_implodeMinExplodingBox"] = "3", ["_implodeInnerRadiusBox"] = "25",
            ["_implodeMiddleRadiusBox"] = "100", ["_implodeOuterRadiusBox"] = "225",
            ["_implodeInnerAngularBox"] = "14", ["_implodeMiddleAngularBox"] = "2.5",
            ["_implodeOuterAngularBox"] = "0.5", ["_implodeInitialShakeDurationBox"] = "3",
            ["_implodeInitialShakeStrengthBox"] = "0.05", ["_implodeDeathShakeDurationBox"] = "0.1",
            ["_implodeDeathShakeStrengthBox"] = "0.1", ["_implodeExplosionShakeDurationBox"] = "0.8",
            ["_implodeExplosionShakeStrengthBox"] = "0.3"
        };
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            var specific = GetPrivateField<StackPanel>(editor, "_specificAttributeRows");
            Assert.Same(GetPrivateField<Grid>(editor, "_throwPropertiesRow"), specific.Children[^1]);
            GetPrivateField<Button>(editor, "_otherSpecificAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("Pull properties",
                GetPrivateField<AutoCompleteBox>(editor, "_otherSpecificAttributePicker")
                    .ItemsSource!.Cast<string>());

            GetPrivateField<AutoCompleteBox>(editor, "_implodeSphereBox").SelectedItem = "ImplodeSphere";
            GetPrivateField<AutoCompleteBox>(editor, "_implodeShockwaveBox").SelectedItem = "ImplodeShockwave";
            GetPrivateField<AutoCompleteBox>(editor, "_implodePullEffectBox").SelectedItem = "VFXImplodePullEffect";
            GetPrivateField<AutoCompleteBox>(editor, "_implodeStartSoundBox").Text = "ImplodeBirth";
            GetPrivateField<AutoCompleteBox>(editor, "_implodeExplodeSoundBox").Text = "ImplodeDeath";
            GetPrivateField<AutoCompleteBox>(editor, "_implodeWindupSoundBox").Text = "CustomWindup";
            GetPrivateField<AutoCompleteBox>(editor, "_implodeHitSoundBox").Text = "CustomHit";
            foreach (var (field, value) in numericFields)
                GetPrivateField<TextBox>(editor, field).Text = value;
            foreach (var (field, value) in new[]
                     {
                         ("_minThrowDistanceBox", "15"), ("_maxThrowDistanceBox", "25"),
                         ("_minThrowHeightBox", "1"), ("_maxThrowHeightBox", "2"),
                         ("_minThrowVelocityBox", "15"), ("_maxThrowVelocityBox", "22")
                     })
                GetPrivateField<TextBox>(editor, field).Text = value;
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "P");
        Assert.Equal("ImplodeSphere", power.Element("sphereproto")?.Value);
        Assert.Equal("ImplodeShockwave", power.Element("shockwaveproto")?.Value);
        Assert.Equal("VFXImplodePullEffect", power.Element("vfxpulleffectproto")?.Value);
        Assert.Equal("ImplodeBirth", power.Element("implodestartsoundset")?.Value);
        Assert.Equal("CustomHit", power.Element("implodehitsoundset")?.Value);
        AvaloniaTestHost.Run(() =>
        {
            foreach (var (field, value) in numericFields)
            {
                var box = GetPrivateField<TextBox>(editor, field);
                Assert.Equal(value, power.Element((string)box.Tag!)?.Value);
            }
        });
        Assert.Equal("15", power.Element("minthrowdistance")?.Value);
        Assert.Equal("22", power.Element("maxthrowvelocity")?.Value);
    }

    [Fact]
    public void PSeedsEarthWallStealthAndFavorForestWriteTheirSpecificXml()
    {
        using var workspace = new TestWorkspace(nameof(PSeedsEarthWallStealthAndFavorForestWriteTheirSpecificXml));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"Seeds\" type=\"PSeeds\"/><power name=\"Wall\" type=\"EarthWall\"/>" +
            "<power name=\"Vanish\" type=\"Stealth\"/><power name=\"Forest\" type=\"FavorForest\">" +
            "<rooteddamage damage=\"6\">Unit</rooteddamage></power></powersmod>");
        var protos = new[] { "FarmShennong", "VFXSeeds", "VFXTarget", "EarthWall", "VFXCast",
            "VFXUnit", "VFXReappear", "VFXRoot", "VFXArea", "VFXTree" };
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: protos, unitTypeNames: ["Hero", "Unit"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Seeds");
            GetPrivateField<AutoCompleteBox>(editor, "_farmProtoBox").SelectedItem = "FarmShennong";
            GetPrivateField<TextBox>(editor, "_numberToTransformBox").Text = "2";
            GetPrivateField<AutoCompleteBox>(editor, "_pSeedsFarmVfxBox").SelectedItem = "VFXSeeds";
            GetPrivateField<AutoCompleteBox>(editor, "_pSeedsFarmTargetVfxBox").SelectedItem = "VFXTarget";
            GetPrivateField<ComboBox>(editor, "_stealthLevelBox").SelectedIndex = 1;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Wall");
            GetPrivateField<AutoCompleteBox>(editor, "_wallProtoBox").SelectedItem = "EarthWall";
            GetPrivateField<TextBox>(editor, "_wallSegmentCountBox").Text = "42";
            GetPrivateField<Button>(editor, "_addDamageTierButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var row = GetPrivateField<StackPanel>(editor, "_damageTierRows").Children.OfType<StackPanel>().Last();
            var boxes = row.Children.OfType<TextBox>().ToArray(); boxes[0].Text = "1"; boxes[1].Text = "0.125";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Vanish");
            GetPrivateField<AutoCompleteBox>(editor, "_vanishCastVfxBox").SelectedItem = "VFXCast";
            GetPrivateField<AutoCompleteBox>(editor, "_vanishTargetVfxBox").SelectedItem = "VFXUnit";
            GetPrivateField<AutoCompleteBox>(editor, "_reappearVfxBox").SelectedItem = "VFXReappear";
            GetPrivateField<ComboBox>(editor, "_stealthLevelBox").SelectedIndex = 0;
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Forest");
            GetPrivateField<AutoCompleteBox>(editor, "_rootedEnemyVfxBox").SelectedItem = "VFXRoot";
            GetPrivateField<AutoCompleteBox>(editor, "_treeAreaVfxBox").SelectedItem = "VFXArea";
            GetPrivateField<AutoCompleteBox>(editor, "_forestTreeVfxBox").SelectedItem = "VFXTree";
            GetPrivateField<TextBox>(editor, "_stayTimeBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_rootDurationBox").Text = "1.5";
            GetPrivateField<TextBox>(editor, "_slowHealMultiplierBox").Text = "0.5";
            var existing = GetPrivateField<StackPanel>(editor, "_rootDamageRows")
                .Children.OfType<StackPanel>().Single();
            Assert.Equal("Unit", existing.Children.OfType<AutoCompleteBox>().Single().Text);
            Assert.Contains("add-component", GetPrivateField<Button>(editor, "_addRootDamageButton").Classes);
            Assert.Contains("add-component", GetPrivateField<Button>(editor, "_addDamageTierButton").Classes);
            Assert.Equal(VerticalAlignment.Top, GetPrivateField<Grid>(editor, "_forestVfxRow")
                .Children.OfType<TextBlock>().Single().VerticalAlignment);
            GetPrivateField<Button>(editor, "_addRootDamageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var row = GetPrivateField<StackPanel>(editor, "_rootDamageRows").Children.OfType<StackPanel>().Last();
            row.Children.OfType<AutoCompleteBox>().Single().SelectedItem = "Hero";
            row.Children.OfType<TextBox>().Single().Text = "3";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        var seeds = FindPower(document, "Seeds");
        Assert.Equal("FarmShennong", seeds.Element("farmproto")?.Value);
        Assert.Equal("2", seeds.Element("numbertotransform")?.Value);
        Assert.Equal("2", seeds.Element("stealthlevel")?.Value);
        Assert.Equal("VFXTarget", seeds.Element("pseedsfarmtargetvfx")?.Value);
        var wall = FindPower(document, "Wall");
        Assert.Equal("42", wall.Element("wallsegmentcount")?.Value);
        Assert.Equal("1", wall.Element("damagetiers")?.Element("damagetier")?.Attribute("mindestroyed")?.Value);
        Assert.Equal("0.125", wall.Element("damagetiers")?.Element("damagetier")?.Value);
        Assert.Equal("1", FindPower(document, "Vanish").Element("stealthlevel")?.Value);
        var forest = FindPower(document, "Forest");
        Assert.Equal("VFXTree", forest.Element("treevfx")?.Value);
        var rootDamages = forest.Elements("rooteddamage").ToArray();
        Assert.Equal("Unit", rootDamages[0].Value);
        Assert.Equal("6", rootDamages[0].Attribute("damage")?.Value);
        Assert.Equal("Hero", rootDamages[1].Value);
        Assert.Equal("3", rootDamages[1].Attribute("damage")?.Value);
    }

    [Fact]
    public void TempUnitUsesRepeatedCreateAgeAndPlacementXmlWithoutFlatteningLegacyAttributes()
    {
        using var workspace = new TestWorkspace(nameof(
            TempUnitUsesRepeatedCreateAgeAndPlacementXmlWithoutFlatteningLegacyAttributes));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Summon" type="TempUnit">
              <createunit quantity="2" minradius="1" maxradius="4" pattern="symmetric"
                          delay="0.5" lifespan="3" lifespantype="Relative"
                          removesilentlyatlifespanend="1" permanent=""
                          createunitsoundset="CreateSound">Ayotochtli|Tzitzimitl</createunit>
              <attachproto>SentinelBase</attachproto>
              <ageadjustment quantityaddon="2">ClassicalAge</ageadjustment>
              <smoothrotation></smoothrotation>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Ayotochtli", "Tzitzimitl", "SentinelBase"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Summon");
            var container = GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows")
                .Children.OfType<Border>().Single();
            var card = Assert.IsType<StackPanel>(container.Child);
            var top = Assert.IsType<Grid>(card.Children[0]);
            var subjectRow = top.Children.OfType<WrapPanel>().Single();
            Assert.Equal("Selection", subjectRow.Children.OfType<ComboBox>().Single().SelectedItem);
            var subject = subjectRow.Children.OfType<StackPanel>().Single();
            Assert.Equal(2, subject.Children.OfType<WrapPanel>().Single().Children.Count);
            Assert.Contains("add-component", GetPrivateField<Button>(editor,
                "_addTempUnitCreateButton").Classes);
            Assert.Contains("add-component", GetPrivateField<Button>(editor,
                "_addTempUnitAgeButton").Classes);
            GetPrivateField<CheckBox>(editor, "_tempUnitForcePlacementCheck").IsChecked = true;
            GetPrivateField<Button>(editor, "_addTempUnitAgeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<Button>(editor, "_addTempUnitCreateButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var addedContainer = GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows")
                .Children.OfType<Border>().Last();
            var addedCard = Assert.IsType<StackPanel>(addedContainer.Child);
            var addedTop = Assert.IsType<Grid>(addedCard.Children[0]);
            var addedSubjectRow = addedTop.Children.OfType<WrapPanel>().Single();
            var addedSubject = addedSubjectRow.Children.OfType<StackPanel>().Single();
            var addedUnit = addedSubject.Children.OfType<AutoCompleteBox>().Single();
            addedUnit.SelectedItem = "Ayotochtli";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "Summon");
        var first = power.Elements("createunit").First();
        Assert.Equal("Ayotochtli|Tzitzimitl", first.Value);
        Assert.Equal("symmetric", first.Attribute("pattern")?.Value);
        Assert.Equal("1", first.Attribute("removesilentlyatlifespanend")?.Value);
        Assert.Equal("CreateSound", first.Attribute("createunitsoundset")?.Value);
        Assert.Equal(2, power.Elements("createunit").Count());
        Assert.Contains(power.Elements("ageadjustment"), element =>
            element.Value == "ArchaicAge" && element.Attribute("quantityaddon")?.Value == "0");
        var childNames = power.Elements().Select(element => element.Name.LocalName).ToArray();
        var firstAge = Array.IndexOf(childNames, "ageadjustment");
        Assert.Equal("ageadjustment", childNames[firstAge + 1]);
        Assert.NotNull(power.Element("forceplacement"));
        Assert.Matches(@"\r?\n\s+<ageadjustment", File.ReadAllText(path));
    }

    [Fact]
    public void TempUnitOptionalControlsStayReadOnlyAndWaterUnitButtonOpensItsSelector()
    {
        var original = new XElement("power", new XAttribute("name", "Original"),
            new XAttribute("type", "TempUnit"),
            new XElement("createunit", new XAttribute("quantity", "1"), "LandUnit"));
        var originalEditor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Original", true, original)], null,
            protoUnitNames: ["LandUnit", "WaterUnit"]));
        AvaloniaTestHost.Run(() => originalEditor.SelectPower("Original"));
        AvaloniaTestHost.Run(() =>
        {
            var container = GetPrivateField<StackPanel>(originalEditor, "_tempUnitCreateRows")
                .Children.OfType<Border>().Single();
            var card = Assert.IsType<StackPanel>(container.Child);
            Assert.DoesNotContain(DescendantControls(card).OfType<Button>(), button =>
                button.Classes.Contains("remove-button"));
        });

        using var workspace = new TestWorkspace(nameof(
            TempUnitOptionalControlsStayReadOnlyAndWaterUnitButtonOpensItsSelector));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Custom' type='TempUnit'><createunit quantity='1'>LandUnit</createunit></power></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["LandUnit", "WaterUnit"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Custom");
            var container = GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows")
                .Children.OfType<Border>().Single();
            var card = Assert.IsType<StackPanel>(container.Child);
            var top = Assert.IsType<Grid>(card.Children[0]);
            var subject = top.Children.OfType<WrapPanel>().Single()
                .Children.OfType<StackPanel>().Single();
            subject.Children.OfType<Button>().Single(button =>
                    Equals(button.Content, "Water Unit"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            container = GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows")
                .Children.OfType<Border>().Single();
            card = Assert.IsType<StackPanel>(container.Child);
            top = Assert.IsType<Grid>(card.Children[0]);
            subject = top.Children.OfType<WrapPanel>().Single()
                .Children.OfType<StackPanel>().Single();
            Assert.Contains(subject.Children.OfType<TextBlock>(), label => label.Text == "Water Unit");
            Assert.Equal(2, subject.Children.OfType<AutoCompleteBox>().Count());
        });
    }

    [Fact]
    public void VibrantLandAndRestorationWriteTheirSpecificFields()
    {
        using var workspace = new TestWorkspace(nameof(VibrantLandAndRestorationWriteTheirSpecificFields));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Vibrant" type="VibrantLand">
                <treeproto>TreeA</treeproto><treeproto>TreeB</treeproto>
              </power>
              <power name="Restore" type="Restoration" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["TreeA", "TreeB", "TreeC", "TreeVfx", "HealVfx", "AreaVfx"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Vibrant");
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_vibrantTreeUnitsHost")
                .Children.OfType<StackPanel>().Count());
            GetPrivateField<TextBox>(editor, "_vibrantGenerateIntervalBox").Text = "120";
            GetPrivateField<AutoCompleteBox>(editor, "_vibrantTreeVfxBox").SelectedItem = "TreeVfx";
            GetPrivateField<TextBox>(editor, "_vibrantRotateMinBox").Text = "50";
            GetPrivateField<TextBox>(editor, "_vibrantRotateMaxBox").Text = "100";
            GetPrivateField<TextBox>(editor, "_vibrantGrowthMinBox").Text = "30";
            GetPrivateField<TextBox>(editor, "_vibrantGrowthMaxBox").Text = "60";
            GetPrivateField<TextBox>(editor, "_vibrantScaleMinBox").Text = "100";
            GetPrivateField<TextBox>(editor, "_vibrantScaleMaxBox").Text = "125";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Restore");
            GetPrivateField<TextBox>(editor, "_restorationHealSpeedBox").Text = "50";
            GetPrivateField<TextBox>(editor, "_slowHealMultiplierBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_restorationScanIntervalBox").Text = "0.25";
            GetPrivateField<AutoCompleteBox>(editor, "_restorationHealVfxBox").SelectedItem = "HealVfx";
            GetPrivateField<AutoCompleteBox>(editor, "_restorationAreaVfxBox").SelectedItem = "AreaVfx";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        var vibrant = FindPower(document, "Vibrant");
        Assert.Equal(["TreeA", "TreeB"], vibrant.Elements("treeproto").Select(element => element.Value));
        Assert.Equal("TreeVfx", vibrant.Element("treevfx")?.Value);
        Assert.Equal("125", vibrant.Element("maxscale")?.Value);
        var restoration = FindPower(document, "Restore");
        Assert.Equal("50", restoration.Element("healspeed")?.Value);
        Assert.Equal("0.5", restoration.Element("slowhealmultiplier")?.Value);
        Assert.Equal("0.25", restoration.Element("scaninterval")?.Value);
        Assert.Equal("HealVfx", restoration.Element("healvfx")?.Value);
        Assert.Equal("AreaVfx", restoration.Element("areavfx")?.Value);
    }

    [Fact]
    public void VibrantLandStartsWithOneTreeUnitEditorAndHidesDuplicateAdditionalAttributes()
    {
        using var workspace = new TestWorkspace(nameof(VibrantLandStartsWithOneTreeUnitEditorAndHidesDuplicateAdditionalAttributes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"Vibrant\" type=\"VibrantLand\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["TreeA", "TreeB"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Vibrant");
            var host = GetPrivateField<WrapPanel>(editor, "_vibrantTreeUnitsHost");
            Assert.Single(host.Children.OfType<AutoCompleteBox>());
            Assert.Contains(host.Children.OfType<Button>(), button => Equals(button.Content, "Add Tree Unit"));

            GetPrivateField<Button>(editor, "_otherSpecificAttributesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choices = GetPrivateField<AutoCompleteBox>(editor, "_otherSpecificAttributePicker")
                .ItemsSource!.Cast<string>().ToList();
            Assert.DoesNotContain("Tree Unit", choices);
            Assert.DoesNotContain("Tree VFX", choices);
        });
    }

    [Fact]
    public void OriginalSpecificAttributesDoNotReappearAsAdditionalRowsAndPreserveUnitValues()
    {
        using var workspace = new TestWorkspace(nameof(OriginalSpecificAttributesDoNotReappearAsAdditionalRowsAndPreserveUnitValues));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod />");
        var originals = PowerCatalog.ExtractDefinitions([XDocument.Parse("""
            <powers>
              <power name="Vibrant" type="VibrantLand"><treeproto>TreeA</treeproto><treevfx>TreeVfx</treevfx></power>
              <power name="Drought" type="DroughtLand">
                <terraincreep creep="DroughtLand" maxradius="30"><protounit>TownCenter</protounit></terraincreep>
                <waveprotounit>DroughtWave</waveprotounit>
                <initialdamage fraction="0.1">Building</initialdamage>
              </power>
              <power name="Flood" type="GreatFlood">
                <waveprotounit>FloodWave</waveprotounit>
                <damagecap unittype="AbstractArcher">12.5</damagecap>
              </power>
              <power name="Sands" type="ShiftingSands"><scaninterval>0.25</scaninterval></power>
            </powers>
            """)], isBuiltIn: true);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(originals, path));

        AvaloniaTestHost.Run(() =>
        {
            foreach (var name in new[] { "Vibrant", "Drought", "Flood", "Sands" })
            {
                editor.SelectPower(name);
                Assert.False(GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible);
                Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            }

            editor.SelectPower("Drought");
            var damageUnit = GetPrivateField<StackPanel>(editor, "_droughtInitialDamageRows")
                .Children.OfType<StackPanel>().Single().Children.OfType<AutoCompleteBox>().Single();
            Assert.Equal("Building", damageUnit.Text);
            Assert.DoesNotContain(DescendantControls(GetPrivateField<StackPanel>(editor, "_droughtTerrainCreepHost"))
                .OfType<TextBlock>(), label => label.Text == "Generator");

            editor.SelectPower("Flood");
            var capRow = GetPrivateField<StackPanel>(editor, "_greatFloodDamageCapRows")
                .Children.OfType<StackPanel>().Single();
            Assert.Equal("AbstractArcher", capRow.Children.OfType<AutoCompleteBox>().Single().Text);
            Assert.Equal("12.5", capRow.Children.OfType<TextBox>().Single().Text);
        });
    }

    [Fact]
    public void DroughtLandAndGreatFloodWriteTheirSpecificXmlShapes()
    {
        using var workspace = new TestWorkspace(nameof(DroughtLandAndGreatFloodWriteTheirSpecificXmlShapes));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Drought" type="DroughtLand" />
              <power name="Flood" type="GreatFlood" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["LandVfx", "WaveProto", "Building"],
            unitTypeNames: ["Building", "Unit"],
            terrainCreepNames: ["DroughtCreep"],
            protoActionNames: ["FloodAttack"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Drought");
            GetPrivateField<AutoCompleteBox>(editor, "_droughtLandVfxBox").SelectedItem = "LandVfx";
            GetPrivateField<AutoCompleteBox>(editor, "_droughtWaveUnitBox").SelectedItem = "WaveProto";
            GetPrivateField<TextBox>(editor, "_droughtBurnTimeBox").Text = "8.5";
            var creepHost = GetPrivateField<StackPanel>(editor, "_droughtTerrainCreepHost");
            GetPrivateField<Button>(editor, "_addTerrainCreepButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DescendantControls(creepHost).OfType<AutoCompleteBox>().First()
                .SelectedItem = "DroughtCreep";

            GetPrivateField<Button>(editor, "_addDroughtInitialDamageButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var damageRow = GetPrivateField<StackPanel>(editor, "_droughtInitialDamageRows")
                .Children.OfType<StackPanel>().Single();
            damageRow.Children.OfType<AutoCompleteBox>().Single().SelectedItem = "Building";
            damageRow.Children.OfType<TextBox>().Single().Text = "0.10";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Flood");
            GetPrivateField<AutoCompleteBox>(editor, "_greatFloodWaveUnitBox").SelectedItem = "WaveProto";
            GetPrivateField<TextBox>(editor, "_greatFloodWaveCountBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_greatFloodWaveRadiusBox").Text = "12";
            GetPrivateField<TextBox>(editor, "_greatFloodSpacingBox").Text = "4.5";
            GetPrivateField<TextBox>(editor, "_greatFloodInitialVelocityBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_greatFloodFinalVelocityBox").Text = "7";
            GetPrivateField<TextBox>(editor, "_greatFloodRampTimeBox").Text = "2";
            GetPrivateField<AutoCompleteBox>(editor, "_greatFloodAttackNameBox").SelectedItem = "FloodAttack";
            GetPrivateField<TextBox>(editor, "_greatFloodAttackIntervalBox").Text = "0.75";
            GetPrivateField<Button>(editor, "_addGreatFloodDamageCapButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var capRow = GetPrivateField<StackPanel>(editor, "_greatFloodDamageCapRows")
                .Children.OfType<StackPanel>().Single();
            capRow.Children.OfType<AutoCompleteBox>().Single().SelectedItem = "Unit";
            capRow.Children.OfType<TextBox>().Single().Text = "9.5";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Drought");
            editor.SelectPower("Flood");
            var capRow = GetPrivateField<StackPanel>(editor, "_greatFloodDamageCapRows")
                .Children.OfType<StackPanel>().Single();
            Assert.Equal("Unit", capRow.Children.OfType<AutoCompleteBox>().Single().Text);
            Assert.Equal("9.5", capRow.Children.OfType<TextBox>().Single().Text);
        });

        var document = XDocument.Load(path);
        var drought = FindPower(document, "Drought");
        Assert.Equal("LandVfx", drought.Element("landprotounit")?.Value);
        Assert.Equal("WaveProto", drought.Element("waveprotounit")?.Value);
        Assert.Equal("8.5", drought.Element("burntime")?.Value);
        Assert.Equal("DroughtCreep", drought.Element("terraincreep")?.Attribute("creep")?.Value);
        Assert.Equal("Building", drought.Element("initialdamage")?.Value);
        Assert.Equal("0.10", drought.Element("initialdamage")?.Attribute("fraction")?.Value);

        var flood = FindPower(document, "Flood");
        Assert.Equal("WaveProto", flood.Element("waveprotounit")?.Value);
        Assert.Equal("3", flood.Element("wavecount")?.Value);
        Assert.Equal("FloodAttack", flood.Element("initialattackname")?.Value);
        Assert.Equal("0.75", flood.Element("attackinterval")?.Value);
        Assert.Equal("9.5", flood.Element("damagecap")?.Value);
        Assert.Equal("Unit", flood.Element("damagecap")?.Attribute("unittype")?.Value);
    }

    [Fact]
    public void ShiftingSandsAndLocustWriteTheirExistingSpecificXmlTags()
    {
        using var workspace = new TestWorkspace(nameof(ShiftingSandsAndLocustWriteTheirExistingSpecificXmlTags));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Sands" type="ShiftingSands" />
              <power name="Swarm" type="Locust" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["PortalIn", "PortalOut", "LocustSwarm"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Sands");
            GetPrivateField<AutoCompleteBox>(editor, "_shiftingSandsStartPortalBox").SelectedItem = "PortalIn";
            GetPrivateField<AutoCompleteBox>(editor, "_shiftingSandsDestinationPortalBox").SelectedItem = "PortalOut";
            GetPrivateField<TextBox>(editor, "_shiftingSandsFadeTimeBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_shiftingSandsSpawnTimeBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_shiftingSandsScanIntervalBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_shiftingSandsPlacementRadiusBox").Text = "10";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Swarm");
            GetPrivateField<AutoCompleteBox>(editor, "_locustSwarmUnitBox").SelectedItem = "LocustSwarm";
            GetPrivateField<TextBox>(editor, "_locustSwarmCountBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_locustSwarmRadiusBox").Text = "4.5";
            GetPrivateField<TextBox>(editor, "_locustWayPointCountBox").Text = "12";
            GetPrivateField<TextBox>(editor, "_locustWayPointOffsetBox").Text = "6";
            GetPrivateField<TextBox>(editor, "_locustDamageIntervalBox").Text = "0.1";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        var sands = FindPower(document, "Sands");
        Assert.Equal("PortalIn", sands.Element("inportalprotounit")?.Value);
        Assert.Equal("PortalOut", sands.Element("outportalprotounit")?.Value);
        Assert.Equal("2", sands.Element("unitfadetime")?.Value);
        Assert.Equal("0.5", sands.Element("outportalspawntime")?.Value);
        Assert.Equal("0.25", sands.Element("scaninterval")?.Value);
        Assert.Equal("10", sands.Element("placementradius")?.Value);

        var swarm = FindPower(document, "Swarm");
        Assert.Equal("LocustSwarm", swarm.Element("swarmprotounit")?.Value);
        Assert.Equal("3", swarm.Element("swarmcount")?.Value);
        Assert.Equal("4.5", swarm.Element("swarmradius")?.Value);
        Assert.Equal("12", swarm.Element("swarmwaypoints")?.Value);
        Assert.Equal("6", swarm.Element("swarmwaypointoffset")?.Value);
        Assert.Equal("0.1", swarm.Element("damageinterval")?.Value);
    }

    [Fact]
    public void BoltPestilenceAndTeleportWriteTheirSpecificXmlTags()
    {
        using var workspace = new TestWorkspace(nameof(BoltPestilenceAndTeleportWriteTheirSpecificXmlTags));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="BoltPower" type="Bolt" />
              <power name="Pest" type="Pestilence" />
              <power name="Port" type="Teleport" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Strike", "Sparks", "Scorch", "Small", "Large", "PortalA", "PortalB"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("BoltPower"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<AutoCompleteBox>(editor, "_boltStrikeUnitBox").SelectedItem = "Strike";
            GetPrivateField<AutoCompleteBox>(editor, "_boltSparksVfxBox").SelectedItem = "Sparks";
            GetPrivateField<AutoCompleteBox>(editor, "_boltScorchVfxBox").SelectedItem = "Scorch";
            GetPrivateField<TextBox>(editor, "_boltStrikeHeightBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_boltStrikeMinOffsetBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_boltStrikeMaxOffsetBox").Text = "0.1";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Pest"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_pestilenceRofPenaltyBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_pestilenceTrainRatePenaltyBox").Text = "50";
            GetPrivateField<TextBox>(editor, "_pestilenceScanIntervalBox").Text = "5";
            GetPrivateField<AutoCompleteBox>(editor, "_pestilenceSmallVfxBox").SelectedItem = "Small";
            GetPrivateField<AutoCompleteBox>(editor, "_pestilenceLargeVfxBox").SelectedItem = "Large";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Port"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<AutoCompleteBox>(editor, "_teleportStartPortalBox").SelectedItem = "PortalA";
            GetPrivateField<AutoCompleteBox>(editor, "_teleportDestinationPortalBox").SelectedItem = "PortalB";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        var bolt = FindPower(document, "BoltPower");
        Assert.Equal("Strike", bolt.Element("strikeproto")?.Value);
        Assert.Equal("Sparks", bolt.Element("sparksvfxproto")?.Value);
        Assert.Equal("Scorch", bolt.Element("scorchvfxproto")?.Value);
        Assert.Equal("25", bolt.Element("strikeheight")?.Value);
        Assert.Equal("0.1", bolt.Element("strikemaxoffset")?.Value);
        var pest = FindPower(document, "Pest");
        Assert.Equal("25", pest.Element("rofpercentpenalty")?.Value);
        Assert.Null(pest.Element("rofpenalty"));
        Assert.Equal("50", pest.Element("trainratepenalty")?.Value);
        Assert.Equal("5", pest.Element("rescaninterval")?.Value);
        Assert.Equal("Small", pest.Element("smallvfxproto")?.Value);
        Assert.Equal("Large", pest.Element("largevfxproto")?.Value);
        var teleport = FindPower(document, "Port");
        Assert.Equal("PortalA", teleport.Element("inportal")?.Value);
        Assert.Equal("PortalB", teleport.Element("outportal")?.Value);
        Assert.Null(teleport.Element("inportalprotounit"));
    }

    [Fact]
    public void BronzeAndGreatHuntPreserveRepeatedShapesAndUseExclusiveAgeChoices()
    {
        using var workspace = new TestWorkspace(nameof(BronzeAndGreatHuntPreserveRepeatedShapesAndUseExclusiveAgeChoices));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="BronzePower" type="Bronze"><armor><abstracttype>Hero</abstracttype><armortype vulnerabilitypercentdelta="-0.3">Hack</armortype><armortype vulnerabilitypercentdelta="-0.2">Pierce</armortype><armortype vulnerabilitypercentdelta="-0.1">Crush</armortype><vfxproto>Glow</vfxproto></armor></power>
              <power name="Hunt" type="GreatHunt"><maxfood age="1">750</maxfood><maxfood age="2">1000</maxfood></power>
              <power name="Summon" type="TempUnit"><ageadjustment quantityaddon="1">ClassicalAge</ageadjustment><ageadjustment quantityaddon="2">HeroicAge</ageadjustment></power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Glow", "Deer", "HuntVfx"], unitTypeNames: ["Hero"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("BronzePower");
            var bronzeRows = GetPrivateField<StackPanel>(editor, "_bronzeArmorRows");
            Assert.Single(bronzeRows.Children);
            Assert.Equal("Hero", DescendantControls(bronzeRows).OfType<AutoCompleteBox>().First().Text);
            Assert.DoesNotContain(DescendantControls(bronzeRows).OfType<Button>(), button =>
                string.Equals(button.Content?.ToString(), "Add Armor Type", StringComparison.Ordinal));
            Assert.Equal(3, DescendantControls(bronzeRows).OfType<TextBlock>().Count(label => label.Text == "Delta"));
            editor.SelectPower("Hunt");
            var huntRows = GetPrivateField<StackPanel>(editor, "_greatHuntMaxFoodRows").Children
                .OfType<StackPanel>().ToList();
            Assert.Equal(2, huntRows.Count);
            var firstAges = Assert.IsAssignableFrom<IEnumerable<string>>(huntRows[0]
                .Children.OfType<ComboBox>().Single().ItemsSource).ToList();
            Assert.Contains("ArchaicAge", firstAges);
            Assert.DoesNotContain("ClassicalAge", firstAges);
            GetPrivateField<AutoCompleteBox>(editor, "_greatHuntSpawnIfNoTargetBox").SelectedItem = "Deer";
            var foodRatio = GetPrivateField<TextBox>(editor, "_greatHuntFoodRatioBox");
            Assert.True(foodRatio.IsVisible);
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
            Assert.False(GetPrivateField<bool>(editor, "_rawEditorDirty"));
            Assert.Equal("spawnfoodratio", foodRatio.Tag);
            Assert.Equal("", foodRatio.Text);
            foodRatio.Text = "2";
            Assert.NotNull(GetPrivateField<XElement>(editor, "_currentEntry").Element("spawnfoodratio"));
            GetPrivateField<AutoCompleteBox>(editor, "_greatHuntVfxBox").SelectedItem = "HuntVfx";
            var addMaxFood = GetPrivateField<Button>(editor, "_addGreatHuntMaxFoodButton");
            addMaxFood.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addMaxFood.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addMaxFood.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(addMaxFood.IsVisible);
            editor.SelectPower("Summon");
            var ageRows = GetPrivateField<StackPanel>(editor, "_tempUnitAgeRows").Children
                .OfType<StackPanel>().ToList();
            var firstTempAges = Assert.IsAssignableFrom<IEnumerable<string>>(ageRows[0]
                .Children.OfType<ComboBox>().Single().ItemsSource).ToList();
            Assert.Contains("ClassicalAge", firstTempAges);
            Assert.DoesNotContain("HeroicAge", firstTempAges);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var document = XDocument.Load(path);
        var armor = FindPower(document, "BronzePower").Element("armor")!;
        Assert.Equal("Hero", armor.Element("abstracttype")?.Value);
        Assert.Equal(3, armor.Elements("armortype").Count());
        Assert.Equal("-0.3", armor.Elements("armortype").First().Attribute("vulnerabilitypercentdelta")?.Value);
        Assert.Equal(["1", "2", "3", "4", "5"], FindPower(document, "Hunt").Elements("maxfood")
            .Select(element => element.Attribute("age")!.Value).ToArray());
        Assert.NotNull(FindPower(document, "Hunt").Element("spawnproto"));
        Assert.NotNull(FindPower(document, "Hunt").Element("spawnfoodratio"));
        Assert.Null(FindPower(document, "Hunt").Element("spawnifnotarget"));
        Assert.Null(FindPower(document, "Hunt").Element("foodratio"));
    }

    [Fact]
    public void BronzeUnitTypeRemainsVisibleAfterSaveRepopulation()
    {
        using var workspace = new TestWorkspace(nameof(BronzeUnitTypeRemainsVisibleAfterSaveRepopulation));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="BronzePower" type="Bronze"><armor><abstracttype>Hero</abstracttype><armortype vulnerabilitypercentdelta="0">Hack</armortype><vfxproto>Glow</vfxproto></armor></power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Glow"], unitTypeNames: ["Hero", "AbstractArcher"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("BronzePower");
            var unitType = DescendantControls(GetPrivateField<StackPanel>(editor, "_bronzeArmorRows"))
                .OfType<AutoCompleteBox>().First();
            Assert.Equal("Hero", unitType.Text);
            unitType.SelectedItem = "AbstractArcher";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            var unitType = DescendantControls(GetPrivateField<StackPanel>(editor, "_bronzeArmorRows"))
                .OfType<AutoCompleteBox>().First();
            Assert.Equal("AbstractArcher", unitType.Text);
        });
        Assert.Equal("AbstractArcher", FindPower(XDocument.Load(path), "BronzePower")
            .Element("armor")?.Element("abstracttype")?.Value);
    }

    [Fact]
    public void SpySwapAndCurseUseTheExistingGameXmlShapes()
    {
        using var workspace = new TestWorkspace(nameof(SpySwapAndCurseUseTheExistingGameXmlShapes));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="SpyPower" type="Spy" />
              <power name="SwapPower" type="SwapUnit" />
              <power name="CursePower" type="Curse" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["SpyEye", "TargetUnit", "Replacement", "SwapVfx", "CurseVfx"],
            unitTypeNames: ["Hero"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("SpyPower");
            GetPrivateField<AutoCompleteBox>(editor, "_spyIndicatorBox").SelectedItem = "SpyEye";

            editor.SelectPower("SwapPower");
            GetPrivateField<Button>(editor, "_addSwapEntryButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var swapRow = GetPrivateField<StackPanel>(editor, "_swapEntryRows")
                .Children.OfType<WrapPanel>().Single();
            Assert.Equal("Transform", swapRow.Children.OfType<TextBlock>().First().Text);
            var swapBoxes = swapRow.Children.OfType<AutoCompleteBox>().ToList();
            swapBoxes[0].SelectedItem = "Hero";
            swapBoxes[1].SelectedItem = "Replacement";
            GetPrivateField<Button>(editor, "_addSwapMinMaxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
            Assert.True(GetPrivateField<TextBox>(editor, "_swapMinAmountBox").IsVisible);
            GetPrivateField<TextBox>(editor, "_swapMinAmountBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_swapMaxAmountBox").Text = "5";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            GetPrivateField<TextBox>(editor, "_swapMinAmountBox").Text = "2.5";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("2", GetPrivateField<TextBox>(editor, "_swapMinAmountBox").Text);
            Assert.Equal("2", GetPrivateField<XElement>(editor, "_currentEntry")
                .Element("convertusingminmax")?.Attribute("minswapamount")?.Value);
            var options = GetPrivateField<WrapPanel>(editor, "_swapOptionalFields");
            options.Children.OfType<Button>().Single(button => Equals(button.Content, "Delay"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            options.Children.OfType<StackPanel>().Single(group =>
                group.Children.OfType<TextBlock>().Any(label => label.Text == "Delay"))
                .Children.OfType<TextBox>().Single().Text = "1.5";
            options.Children.OfType<Button>().Single(button => Equals(button.Content, "Chance"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            options.Children.OfType<StackPanel>().Single(group =>
                group.Children.OfType<TextBlock>().Any(label => label.Text == "Chance"))
                .Children.OfType<TextBox>().Single().Text = "0.75";
            options.Children.OfType<Button>().Single(button => Equals(button.Content, "VFX"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            options.Children.OfType<StackPanel>().Single(group =>
                group.Children.OfType<TextBlock>().Any(label => label.Text == "VFX"))
                .Children.OfType<AutoCompleteBox>().Single().SelectedItem = "SwapVfx";
            GetPrivateField<AutoCompleteBox>(editor, "_swapFlagPicker").SelectedItem = "Mutate";
            GetPrivateField<AutoCompleteBox>(editor, "_swapFlagPicker").SelectedItem = "ForceFullHP";

            editor.SelectPower("CursePower");
            Assert.Equal("Max Resources", GetPrivateField<Grid>(editor, "_curseResourcesRow")
                .Children.OfType<TextBlock>().First().Text);
            var maxResources = GetPrivateField<TextBox>(editor, "_curseMaxResourcesBox");
            maxResources.Text = "20";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            maxResources.Text = "20.5";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("20", maxResources.Text);
            maxResources.Text = "-1";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("20", maxResources.Text);
            GetPrivateField<TextBox>(editor, "_curseConvertRatioBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_curseSwapDelayBox").Text = "2";
            GetPrivateField<AutoCompleteBox>(editor, "_curseVfxBox").SelectedItem = "CurseVfx";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        Assert.Equal("SpyEye", FindPower(document, "SpyPower").Element("spyprotounit")?.Value);
        var swap = FindPower(document, "SwapPower");
        Assert.Equal("Hero", swap.Element("abstracttype")?.Value);
        Assert.Equal("Replacement", swap.Element("abstracttype")?.Attribute("swapto")?.Value);
        Assert.Equal("2", swap.Element("convertusingminmax")?.Attribute("minswapamount")?.Value);
        Assert.Equal("5", swap.Element("convertusingminmax")?.Attribute("maxswapamount")?.Value);
        Assert.Equal("1.5", swap.Element("swapdelay")?.Value);
        Assert.Equal("0.75", swap.Element("overallchance")?.Value);
        Assert.Equal("SwapVfx", swap.Element("vfxproto")?.Value);
        Assert.NotNull(swap.Element("mutate"));
        Assert.NotNull(swap.Element("forcefullhp"));
        var curse = FindPower(document, "CursePower");
        Assert.Equal("20", curse.Element("maxresources")?.Value);
        Assert.Equal("0.5", curse.Element("resourceconvertratio")?.Value);
        Assert.Equal("2", curse.Element("swapdelay")?.Value);
        Assert.Equal("CurseVfx", curse.Element("vfxproto")?.Value);
    }

    [Fact]
    public void SmitingGustAndDivineSlashWriteDocumentedTagsAndActions()
    {
        using var workspace = new TestWorkspace(nameof(SmitingGustAndDivineSlashWriteDocumentedTagsAndActions));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Gust" type="SmitingGust" />
            <power name="Slash" type="DivineSlash" /></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["GustProto", "DamageProto", "AttackProto", "HitDecal"],
            protoActionNames: ["DamageAction", "SlashAction"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Gust");
            Assert.Contains(DescendantControls(GetPrivateField<Grid>(editor, "_gustTimingRow"))
                .OfType<TextBlock>(), label => label.Text == "Gust Duration");
            GetPrivateField<AutoCompleteBox>(editor, "_gustUnitBox").SelectedItem = "GustProto";
            Assert.False(GetPrivateField<bool>(editor, "_isPopulating"));
            Assert.True(GetPrivateField<TextBox>(editor, "_gustMinFactorBox").IsVisible);
            GetPrivateField<TextBox>(editor, "_gustLengthBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_gustSpeedBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_gustMinFactorBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_gustMaxFactorBox").Text = "1.5";
            Assert.Equal("0.5", GetPrivateField<XElement>(editor, "_currentEntry")
                .Element("gustminspeedfactor")?.Value);
            GetPrivateField<TextBox>(editor, "_gustBlowTimeBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_gustTimeBox").Text = "6";
            GetPrivateField<AutoCompleteBox>(editor, "_gustDamagingUnitBox").SelectedItem = "DamageProto";
            GetPrivateField<AutoCompleteBox>(editor, "_gustDamageActionBox").SelectedItem = "DamageAction";
            GetPrivateField<TextBox>(editor, "_gustThrowSizeBox").Text = "8";

            editor.SelectPower("Slash");
            GetPrivateField<AutoCompleteBox>(editor, "_divineAttackUnitBox").SelectedItem = "AttackProto";
            GetPrivateField<AutoCompleteBox>(editor, "_divineAttackActionBox").SelectedItem = "SlashAction";
            GetPrivateField<TextBox>(editor, "_divineAttackDelayBox").Text = "0.2";
            GetPrivateField<TextBox>(editor, "_divineDamageWidthBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_divineDamageDepthBox").Text = "9";
            GetPrivateField<AutoCompleteBox>(editor, "_divineHitDecalBox").SelectedItem = "HitDecal";
            GetPrivateField<TextBox>(editor, "_divineCameraDelayBox").Text = "0.4";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var document = XDocument.Load(path);
        var gust = FindPower(document, "Gust");
        Assert.Equal("GustProto", gust.Element("gustproto")?.Value);
        Assert.Equal("0.5", gust.Element("gustminspeedfactor")?.Value);
        Assert.Equal("DamageAction", gust.Element("damagingprotoaction")?.Value);
        Assert.Equal("8", gust.Element("throwsizelimit")?.Value);
        var slash = FindPower(document, "Slash");
        Assert.Equal("AttackProto", slash.Element("attackproto")?.Value);
        Assert.Equal("SlashAction", slash.Element("attackaction")?.Value);
        Assert.Equal("0.2", slash.Element("attackdelay")?.Value);
        Assert.Equal("HitDecal", slash.Element("decalproto")?.Value);
        Assert.Equal("0.4", slash.Element("camerashakedelay")?.Value);
    }

    [Fact]
    public void SacredGateReusesTempUnitAndDwarvenMineUsesItsAgeAdjustmentShape()
    {
        using var workspace = new TestWorkspace(nameof(SacredGateReusesTempUnitAndDwarvenMineUsesItsAgeAdjustmentShape));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Gate" type="SacredGate"><createunit quantity="1">GateUnit</createunit></power>
              <power name="Mine" type="DwarvenMine"><createunit quantity="1">MineUnit</createunit>
                <ageadjustment goldamount="250" gatherratemultiplier="1.25">ClassicalAge</ageadjustment>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["GateUnit", "MineUnit"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Gate");
            Assert.Single(GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows").Children);
            editor.SelectPower("Mine");
            Assert.Single(GetPrivateField<StackPanel>(editor, "_tempUnitCreateRows").Children);
            var ageRow = GetPrivateField<StackPanel>(editor, "_tempUnitAgeRows")
                .Children.OfType<StackPanel>().Single();
            var values = ageRow.Children.OfType<TextBox>().ToList();
            Assert.Equal("250", values[0].Text);
            Assert.Equal("1.25", values[1].Text);
            values[0].Text = "300.5";
            values[1].Text = "1.5";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var adjustment = FindPower(XDocument.Load(path), "Mine").Element("ageadjustment")!;
        Assert.Equal("ClassicalAge", adjustment.Value);
        Assert.Equal("300.5", adjustment.Attribute("goldamount")?.Value);
        Assert.Equal("1.5", adjustment.Attribute("gatherratemultiplier")?.Value);
        Assert.Null(adjustment.Attribute("quantityaddon"));
    }

    [Fact]
    public void LureUsesRepeatedAttractTypesExclusiveAgesAndVerifiedSpawnTags()
    {
        using var workspace = new TestWorkspace(nameof(LureUsesRepeatedAttractTypesExclusiveAgesAndVerifiedSpawnTags));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="LurePower" type="Lure" /></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["LureUnit", "Chicken", "LureVfx", "HerdableUnit"],
            unitTypeNames: ["Herdable", "Huntable"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("LurePower");
            GetPrivateField<AutoCompleteBox>(editor, "_lureUnitBox").SelectedItem = "LureUnit";
            var attractPicker = GetPrivateField<AutoCompleteBox>(editor, "_lureAttractTypePicker");
            attractPicker.SelectedItem = "Herdable";
            attractPicker.SelectedItem = "Huntable";
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_lureAttractTypeChips").Children.Count);
            GetPrivateField<TextBox>(editor, "_lureMinRangeBox").Text = "50";
            GetPrivateField<TextBox>(editor, "_lureMaxRangeBox").Text = "200";
            GetPrivateField<TextBox>(editor, "_lureAttractRateBox").Text = "30";
            GetPrivateField<TextBox>(editor, "_lureVarianceBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_lureCapDistanceBox").Text = "40";
            GetPrivateField<Button>(editor, "_addLureSpawnButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_lureSpawnProtoBox").SelectedItem = "Chicken";
            GetPrivateField<AutoCompleteBox>(editor, "_lureSpawnVfxBox").SelectedItem = "LureVfx";

            var addAge = GetPrivateField<Button>(editor, "_addLureAgeInfoButton");
            addAge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addAge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var ageRows = GetPrivateField<StackPanel>(editor, "_lureAgeInfoRows")
                .Children.OfType<StackPanel>().ToList();
            Assert.Equal(2, ageRows.Count);
            var firstAgeOptions = Assert.IsAssignableFrom<IEnumerable<string>>(
                ageRows[0].Children.OfType<ComboBox>().Single().ItemsSource).ToList();
            Assert.DoesNotContain("ClassicalAge", firstAgeOptions);
            var firstValues = ageRows[0].Children.OfType<TextBox>().ToList();
            firstValues[0].Text = "1200";
            firstValues[1].Text = "3";
            var secondValues = ageRows[1].Children.OfType<TextBox>().ToList();
            secondValues[0].Text = "1300";
            secondValues[1].Text = "4";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var lure = FindPower(XDocument.Load(path), "LurePower");
        Assert.Equal("LureUnit", lure.Element("lureproto")?.Value);
        Assert.Equal(["Herdable", "Huntable"], lure.Elements("attracttype").Select(item => item.Value));
        Assert.Equal("50", lure.Element("minrange")?.Value);
        Assert.Equal("200", lure.Element("maxrange")?.Value);
        Assert.Equal("30", lure.Element("attractrate")?.Value);
        Assert.Equal("0.5", lure.Element("variance")?.Value);
        Assert.Equal("40", lure.Element("attractiondistancetocountagainstcap")?.Value);
        Assert.Equal("Chicken", lure.Element("spawnproto")?.Value);
        Assert.Equal("LureVfx", lure.Element("spawnvfx")?.Value);
        var ages = lure.Elements("ageinfo").ToList();
        Assert.Equal(["1", "2"], ages.Select(item => item.Value));
        Assert.Equal("1200", ages[0].Attribute("maxfood")?.Value);
        Assert.Equal("3", ages[0].Attribute("numspawns")?.Value);
        Assert.Equal("1300", ages[1].Attribute("maxfood")?.Value);
        Assert.Equal("4", ages[1].Attribute("numspawns")?.Value);
    }

    [Fact]
    public void UndermineUsesVerifiedAttackTargetAndDeformDepthXmlShapes()
    {
        using var workspace = new TestWorkspace(nameof(UndermineUsesVerifiedAttackTargetAndDeformDepthXmlShapes));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Dig' type='Undermine' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["UndermineDamage", "WallGate"], unitTypeNames: ["Building"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Dig");
            GetPrivateField<AutoCompleteBox>(editor, "_undermineAttackUnitBox").SelectedItem = "UndermineDamage";
            GetPrivateField<TextBox>(editor, "_undermineDamageRadiusBox").Text = "5";
            GetPrivateField<TextBox>(editor, "_undermineAttackIntervalBox").Text = "0.1";
            GetPrivateField<TextBox>(editor, "_undermineDeformBox").Text = "0.10";
            var addTarget = GetPrivateField<Button>(editor, "_addUndermineTargetButton");
            addTarget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addTarget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rows = GetPrivateField<WrapPanel>(editor, "_undermineTargetRows")
                .Children.OfType<StackPanel>().ToList();
            rows[0].Children.OfType<AutoCompleteBox>().Single().SelectedItem = "WallGate";
            rows[0].Children.OfType<CheckBox>().Single().IsChecked = true;
            rows[1].Children.OfType<AutoCompleteBox>().Single().SelectedItem = "Building";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var undermine = FindPower(XDocument.Load(path), "Dig");
        Assert.Equal("UndermineDamage", undermine.Element("attackproto")?.Value);
        Assert.Equal("5", undermine.Element("damageradius")?.Value);
        Assert.Equal("0.1", undermine.Element("attackintervalseconds")?.Value);
        Assert.Equal("0.10", undermine.Element("deformdepth")?.Value);
        var targets = undermine.Elements("attacktarget").ToList();
        Assert.Equal("WallGate", targets[0].Value);
        Assert.Equal("1", targets[0].Attribute("instantkill")?.Value);
        Assert.Equal("Building", targets[1].Value);
        Assert.Null(targets[1].Attribute("instantkill"));
        Assert.Null(undermine.Element("deforminterval"));
    }

    [Fact]
    public void ForestFireWritesItsVerifiedUnitBurnAndSearchTags()
    {
        using var workspace = new TestWorkspace(nameof(ForestFireWritesItsVerifiedUnitBurnAndSearchTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Fire' type='ForestFire' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["ForestFire", "ForestFireEmbers", "VFXForestFire"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Fire");
            var rows = GetPrivateField<StackPanel>(editor, "_specificAttributeRows").Children;
            Assert.True(rows.IndexOf(GetPrivateField<Grid>(editor, "_forestFireUnitsRow")) <
                        rows.IndexOf(GetPrivateField<Grid>(editor, "_tempestStrikeIntervalRow")));
            Assert.True(rows.IndexOf(GetPrivateField<Grid>(editor, "_tempestStrikeIntervalRow")) <
                        rows.IndexOf(GetPrivateField<Grid>(editor, "_forestFireBurnRow")));
            GetPrivateField<AutoCompleteBox>(editor, "_forestFireAttackUnitBox").SelectedItem = "ForestFire";
            GetPrivateField<AutoCompleteBox>(editor, "_forestFireEmbersUnitBox").SelectedItem = "ForestFireEmbers";
            GetPrivateField<AutoCompleteBox>(editor, "_forestFireVfxBox").SelectedItem = "VFXForestFire";
            GetPrivateField<TextBox>(editor, "_tempestSlowestStrikeBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_tempestFastestStrikeBox").Text = "0.1";
            GetPrivateField<TextBox>(editor, "_forestFireBurnLengthBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_forestFireFadeLengthBox").Text = "8";
            GetPrivateField<TextBox>(editor, "_forestFireEmbersLifespanBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_forestFireTreeDestructionBox").Text = "6";
            GetPrivateField<TextBox>(editor, "_forestFireTreeSearchRadiusBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_forestFireDamageSearchRadiusBox").Text = "6";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var fire = FindPower(XDocument.Load(path), "Fire");
        Assert.Equal("ForestFire", fire.Element("attackproto")?.Value);
        Assert.Equal("ForestFireEmbers", fire.Element("embersproto")?.Value);
        Assert.Equal("VFXForestFire", fire.Element("vfxproto")?.Value);
        Assert.Equal("0.5", fire.Element("sloweststriketime")?.Value);
        Assert.Equal("0.1", fire.Element("fasteststriketime")?.Value);
        Assert.Equal("10", fire.Element("burnlength")?.Value);
        Assert.Equal("8", fire.Element("burnfadelength")?.Value);
        Assert.Equal("15", fire.Element("emberlifespan")?.Value);
        Assert.Equal("6", fire.Element("treedestructionseconds")?.Value);
        Assert.Equal("3", fire.Element("treesearchradius")?.Value);
        Assert.Equal("6", fire.Element("damagesearchradius")?.Value);
    }

    [Fact]
    public void OriginalForestFireShowsStandaloneIgnoreAttackTypesPlacementFlag()
    {
        var original = XElement.Parse("""
            <power name="BaseFire" type="ForestFire"><placement>Full</placement>
              <ignoreattacktypesforplacement></ignoreattacktypesforplacement></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseFire", true, original)], null));

        AvaloniaTestHost.Run(() => editor.SelectPower("BaseFire"));

        Assert.Single(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_placementFlagChips").Children));
    }

    [Fact]
    public void TempestWritesVerifiedStrikeLargeStrikeVfxAndWeatherTags()
    {
        using var workspace = new TestWorkspace(nameof(TempestWritesVerifiedStrikeLargeStrikeVfxAndWeatherTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Storm' type='Tempest' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["SmallStrike", "LargeStrike", "EffectVfx", "GroundVfx", "ImpactVfx", "IceVfx"],
            protoActionNames: ["AreaAttack"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Storm");
            GetPrivateField<AutoCompleteBox>(editor, "_tempestStrikeUnitBox").SelectedItem = "SmallStrike";
            GetPrivateField<TextBox>(editor, "_tempestSlowestStrikeBox").Text = "0.2";
            GetPrivateField<TextBox>(editor, "_tempestFastestStrikeBox").Text = "0.02";
            GetPrivateField<TextBox>(editor, "_tempestStrikeHeightBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_tempestStrikeMinOffsetBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_tempestStrikeMaxOffsetBox").Text = "0.1";
            GetPrivateField<AutoCompleteBox>(editor, "_tempestStrikeSoundBox").Text = "StrikeSound";
            GetPrivateField<Button>(editor, "_addTempestAttackButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_tempestAreaAttackActionBox").SelectedItem = "AreaAttack";
            GetPrivateField<CheckBox>(editor, "_tempestRadialTargetingCheck").IsChecked = true;

            GetPrivateField<Button>(editor, "_addTempestLargeStrikeButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_tempestLargeStrikeUnitBox").SelectedItem = "LargeStrike";
            var addTiming = GetPrivateField<Button>(editor, "_addTempestLargeTimingButton");
            addTiming.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addTiming.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var timingBoxes = GetPrivateField<WrapPanel>(editor, "_tempestLargeTimingRows")
                .Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<TextBox>()).ToList();
            timingBoxes[0].Text = "6"; timingBoxes[1].Text = "12";
            var avoids = GetPrivateField<AutoCompleteBox>(editor, "_tempestAvoidPicker");
            avoids.SelectedItem = "Water"; avoids.SelectedItem = "Buildings"; avoids.SelectedItem = "Impassable";
            GetPrivateField<AutoCompleteBox>(editor, "_tempestLargeSoundBox").Text = "LargeImpact";
            Assert.Same(GetPrivateField<StackPanel>(editor, "_tempestLargeStrikeFields"),
                GetPrivateField<WrapPanel>(editor, "_tempestLargeTimingRow").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_tempestLargeStrikeFields"),
                GetPrivateField<WrapPanel>(editor, "_tempestLargeOptionsRow").Parent);

            GetPrivateField<Button>(editor, "_addTempestVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<Button>(editor, "_addTempestEffectButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_tempestEffectBox").SelectedItem = "EffectVfx";
            foreach (var (button, box, value) in new[]
                     {
                         ("_addTempestGroundButton", "_tempestGroundBox", "GroundVfx"),
                         ("_addTempestImpactButton", "_tempestImpactBox", "ImpactVfx"),
                         ("_addTempestIceButton", "_tempestIceBox", "IceVfx")
                     })
            {
                GetPrivateField<Button>(editor, button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                GetPrivateField<AutoCompleteBox>(editor, box).SelectedItem = value;
            }
            GetPrivateField<Button>(editor, "_addTempestCloudsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<TextBlock>(editor, "_tempestCloudsLabel").IsVisible);
            GetPrivateField<TextBox>(editor, "_tempestCloudBuildStartBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_tempestCloudBuildEndBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_tempestCloudFadeStartBox").Text = "17";
            GetPrivateField<TextBox>(editor, "_tempestCloudFadeEndBox").Text = "20";
            GetPrivateField<Button>(editor, "_addTempestDriftButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_tempestSlowestDriftBox").Text = "8";
            GetPrivateField<TextBox>(editor, "_tempestFastestDriftBox").Text = "4";
            GetPrivateField<Button>(editor, "_addTempestTreeBendButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_tempestStrengthMinBox").Text = "0.1";
            Assert.Equal("0.1", GetPrivateField<XElement>(editor, "_currentEntry")
                .Element("treebendminstrength")?.Value);
            GetPrivateField<TextBox>(editor, "_tempestStrengthMaxBox").Text = "0.4";
            GetPrivateField<TextBox>(editor, "_tempestPeriodMinBox").Text = "2.5";
            GetPrivateField<TextBox>(editor, "_tempestPeriodMaxBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_tempestAmplitudeMinBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_tempestAmplitudeMaxBox").Text = "0.7";
            GetPrivateField<TextBox>(editor, "_tempestAngleOffsetBox").Text = "1.55";
            GetPrivateField<TextBox>(editor, "_tempestTreeDurationBox").Text = "16";
            Assert.Equal("0.1", GetPrivateField<XElement>(editor, "_currentEntry")
                .Element("treebendminstrength")?.Value);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var storm = FindPower(XDocument.Load(path), "Storm");
        Assert.Equal("SmallStrike", storm.Element("strikeproto")?.Value);
        Assert.Equal("StrikeSound", storm.Element("strikesoundset")?.Value);
        Assert.Equal("AreaAttack", storm.Element("areaattackaction")?.Value);
        Assert.NotNull(storm.Element("radialtargeting"));
        Assert.Equal("LargeStrike", storm.Element("largestrikeproto")?.Value);
        Assert.Equal(["6", "12"], storm.Element("largestrikeintervaltiming")!.Elements("time").Select(item => item.Value));
        Assert.Equal("true", storm.Element("largestrikeavoidswater")?.Value);
        Assert.Equal("true", storm.Element("largestrikeavoidsbuildings")?.Value);
        Assert.Equal("true", storm.Element("largestrikeavoidslandimpassable")?.Value);
        Assert.Equal("LargeImpact", storm.Element("largestrikesoundset")?.Value);
        Assert.Equal("EffectVfx", storm.Element("effectproto")?.Value);
        Assert.Equal("GroundVfx", storm.Element("groundvfxproto")?.Value);
        Assert.Equal("ImpactVfx", storm.Element("groundimpactvfxproto")?.Value);
        Assert.Equal("IceVfx", storm.Element("icesheetvfxproto")?.Value);
        Assert.Equal("1", storm.Element("cloudbuildupstarttime")?.Value);
        Assert.Equal("20", storm.Element("cloudfadeoutendtime")?.Value);
        Assert.Equal("8", storm.Element("slowestdrifttime")?.Value);
        Assert.Equal("4", storm.Element("fastestdrifttime")?.Value);
        Assert.Equal("0.1", storm.Element("treebendminstrength")?.Value);
        Assert.Equal("16", storm.Element("treebendingduration")?.Value);
        Assert.Null(storm.Element("strikeinterval"));
        Assert.Null(storm.Element("placementavoids"));
    }

    [Fact]
    public void OriginalTempestLoadsNestedTimingsAndHidesAbsentOptionalGroups()
    {
        using var workspace = new TestWorkspace(nameof(OriginalTempestLoadsNestedTimingsAndHidesAbsentOptionalGroups));
        var original = XElement.Parse("""
            <power name="Starfall" type="Tempest"><strikeproto>SmallStrike</strikeproto>
              <largestrikeproto>LargeStrike</largestrikeproto><largestrikeintervaltiming>
                <time>6.0</time><time>12.0</time></largestrikeintervaltiming>
              <largestrikeavoidswater>true</largestrikeavoidswater>
              <effectproto>EffectVfx</effectproto>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Starfall", true, original)], null,
            protoUnitNames: ["SmallStrike", "LargeStrike"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("Starfall"));
        Assert.Equal(2, AvaloniaTestHost.Run(() => GetPrivateField<WrapPanel>(editor,
            "_tempestLargeTimingRows").Children.Count));
        Assert.Single(AvaloniaTestHost.Run(() => GetPrivateField<WrapPanel>(editor,
            "_tempestAvoidChips").Children));
        Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<WrapPanel>(editor,
            "_tempestCloudFields").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor,
            "_addTempestCloudsButton").IsVisible));
        Assert.True(AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor,
            "_tempestVfxLabel").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor,
            "_removeTempestEffectButton").IsVisible));
    }

    [Fact]
    public void LightningStormUsesVerifiedStrikeVfxCloudAndRainTags()
    {
        using var workspace = new TestWorkspace(nameof(LightningStormUsesVerifiedStrikeVfxCloudAndRainTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Lightning' type='LightningStorm' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["LightningStrike", "Sparks", "GroundSparks", "Scorch", "RainVfx", "CloudVfx"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Lightning");
            var optionalFlow = GetPrivateField<WrapPanel>(editor, "_tempestOptionalFlow");
            Assert.Equal(3, optionalFlow.Children.Count);
            Assert.All(optionalFlow.Children, child => Assert.IsType<Button>(child));
            Assert.Contains(GetPrivateField<Button>(editor, "_addTempestSoundButton"),
                optionalFlow.Children);
            Assert.Equal("Min", GetPrivateField<TextBlock>(editor, "_fastestStrikeLabel").Text);
            Assert.Equal("Max", GetPrivateField<TextBlock>(editor, "_slowestStrikeLabel").Text);
            Assert.Same(GetPrivateField<TextBlock>(editor, "_strikeIntervalInlineLabel"),
                GetPrivateField<WrapPanel>(editor, "_strikeIntervalFields").Children[0]);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_fastestStrikeFields"),
                GetPrivateField<WrapPanel>(editor, "_strikeIntervalFields").Children[1]);

            GetPrivateField<AutoCompleteBox>(editor, "_tempestStrikeUnitBox").SelectedItem = "LightningStrike";
            GetPrivateField<TextBox>(editor, "_tempestFastestStrikeBox").Text = "0.1";
            GetPrivateField<TextBox>(editor, "_tempestSlowestStrikeBox").Text = "0.2";
            GetPrivateField<TextBox>(editor, "_tempestStrikeHeightBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_tempestStrikeMinOffsetBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_tempestStrikeMaxOffsetBox").Text = "0.1";
            GetPrivateField<AutoCompleteBox>(editor, "_lightningSparksBox").SelectedItem = "Sparks";
            GetPrivateField<AutoCompleteBox>(editor, "_lightningGroundSparksBox").SelectedItem = "GroundSparks";
            GetPrivateField<AutoCompleteBox>(editor, "_lightningScorchBox").SelectedItem = "Scorch";

            GetPrivateField<Button>(editor, "_addLightningRainVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_lightningRainVfxBox").SelectedItem = "RainVfx";
            GetPrivateField<Button>(editor, "_addLightningCloudVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_lightningCloudVfxBox").SelectedItem = "CloudVfx";

            GetPrivateField<Button>(editor, "_addTempestCloudsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_tempestCloudsRow").Parent);
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_tempestOptionalFlow").Children.Count);
            GetPrivateField<TextBox>(editor, "_tempestCloudBuildStartBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_tempestCloudBuildEndBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_tempestCloudFadeStartBox").Text = "40";
            GetPrivateField<TextBox>(editor, "_tempestCloudFadeEndBox").Text = "43";
            GetPrivateField<Button>(editor, "_addLightningRainButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_lightningRainBuildStartBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_lightningRainBuildEndBox").Text = "7";
            GetPrivateField<TextBox>(editor, "_lightningRainFadeStartBox").Text = "37";
            GetPrivateField<TextBox>(editor, "_lightningRainFadeEndBox").Text = "38";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var lightning = FindPower(XDocument.Load(path), "Lightning");
        Assert.Equal("LightningStrike", lightning.Element("strikeproto")?.Value);
        Assert.Equal("0.1", lightning.Element("fasteststriketime")?.Value);
        Assert.Equal("0.2", lightning.Element("sloweststriketime")?.Value);
        Assert.Equal("25", lightning.Element("strikeheight")?.Value);
        Assert.Equal("0", lightning.Element("strikeminoffset")?.Value);
        Assert.Equal("0.1", lightning.Element("strikemaxoffset")?.Value);
        Assert.Equal("Sparks", lightning.Element("sparksvfxproto")?.Value);
        Assert.Equal("GroundSparks", lightning.Element("groundsparksvfxproto")?.Value);
        Assert.Equal("Scorch", lightning.Element("scorchvfxproto")?.Value);
        Assert.Equal("RainVfx", lightning.Element("rainvfxproto")?.Value);
        Assert.Equal("CloudVfx", lightning.Element("cloudvfxproto")?.Value);
        Assert.Equal("0", lightning.Element("cloudbuildupstarttime")?.Value);
        Assert.Equal("43", lightning.Element("cloudfadeoutendtime")?.Value);
        Assert.Equal("2", lightning.Element("rainbuildupstarttime")?.Value);
        Assert.Equal("38", lightning.Element("rainfadeoutendtime")?.Value);
    }

    [Fact]
    public void OriginalLightningStormHidesOptionalRemoveButtons()
    {
        var original = XElement.Parse("""
            <power name="Lightning" type="LightningStorm"><strikeproto>LightningStrike</strikeproto>
              <rainvfxproto>RainVfx</rainvfxproto><cloudvfxproto>CloudVfx</cloudvfxproto>
              <cloudbuildupstarttime>0</cloudbuildupstarttime><cloudbuildupendtime>1</cloudbuildupendtime>
              <cloudfadeoutstarttime>40</cloudfadeoutstarttime><cloudfadeoutendtime>43</cloudfadeoutendtime>
              <rainbuildupstarttime>2</rainbuildupstarttime><rainbuildupendtime>7</rainbuildupendtime>
              <rainfadeoutstarttime>37</rainfadeoutstarttime><rainfadeoutendtime>38</rainfadeoutendtime>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Lightning", true, original)], null,
            protoUnitNames: ["LightningStrike", "RainVfx", "CloudVfx"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("Lightning"));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeLightningRainVfxButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeLightningCloudVfxButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeTempestCloudsButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeLightningRainButton").IsVisible));
    }

    [Fact]
    public void LightningStormClaimsSharedSoundAndCostDoesNotLeakIntoAdditionalAttributes()
    {
        var original = XElement.Parse("""
            <power name="Lightning" type="LightningStorm"><strikeproto>LightningStrike</strikeproto>
              <strikesoundset>LightningStrike</strikesoundset><cost>350</cost></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Lightning", true, original)], null));

        AvaloniaTestHost.Run(() => editor.SelectPower("Lightning"));

        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBlock>(editor, "_otherSpecificHeader").IsVisible));
        Assert.Empty(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<Grid>(editor, "_tempestSoundRow").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_powerCloneCopyCostCheck").IsVisible));
    }

    [Fact]
    public void TempestSpcShowsLegacyTreeBendButCustomTempestDoesNotProposeItByDefault()
    {
        var legacy = XElement.Parse("""
            <power name="TempestSPC" type="Tempest"><treebendstrengthmin>0.1</treebendstrengthmin>
              <treebendstrengthmax>0.4</treebendstrengthmax><treebendperiodmin>2.5</treebendperiodmin>
              <treebendperiodmax>4</treebendperiodmax></power>
            """);
        var originalEditor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("TempestSPC", true, legacy)], null));
        AvaloniaTestHost.Run(() => originalEditor.SelectPower("TempestSPC"));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<Grid>(originalEditor, "_tempestTreeBendRow").IsVisible));
        Assert.Equal("0.1", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBox>(originalEditor, "_tempestStrengthMinBox").Text));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(originalEditor, "_tempestAmplitudeHost").IsVisible));

        using var workspace = new TestWorkspace(nameof(TempestSpcShowsLegacyTreeBendButCustomTempestDoesNotProposeItByDefault));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='CustomStorm' type='Tempest' /></powersmod>");
        var customEditor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() => { customEditor.SetModifiedMode(true); customEditor.SelectPower("CustomStorm"); });
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Grid>(customEditor, "_tempestTreeBendRow").IsVisible));
    }

    [Fact]
    public void EarthquakeUsesVerifiedAttackZoneAndDustVfxTags()
    {
        using var workspace = new TestWorkspace(nameof(EarthquakeUsesVerifiedAttackZoneAndDustVfxTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Quake' type='Earthquake' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["EarthquakeAttack", "DustSmall", "DustMedium", "DustLarge"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Quake");
            GetPrivateField<AutoCompleteBox>(editor, "_earthquakeAttackUnitBox").SelectedItem =
                "EarthquakeAttack";
            GetPrivateField<TextBox>(editor, "_earthquakeIntervalBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_earthquakeTreeAccuracyBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_earthquakeEpicenterRadiusBox").Text = "7.5";
            GetPrivateField<TextBox>(editor, "_earthquakeMiddleRadiusBox").Text = "17.5";
            GetPrivateField<TextBox>(editor, "_earthquakeOuterRadiusBox").Text = "30";
            GetPrivateField<TextBox>(editor, "_earthquakeEpicenterDamageBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_earthquakeMiddleDamageBox").Text = "0.75";
            GetPrivateField<TextBox>(editor, "_earthquakeOuterDamageBox").Text = "0.5";
            GetPrivateField<Button>(editor, "_addEarthquakeDustVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_earthquakeDustSmallBox").SelectedItem = "DustSmall";
            GetPrivateField<AutoCompleteBox>(editor, "_earthquakeDustMediumBox").SelectedItem = "DustMedium";
            GetPrivateField<AutoCompleteBox>(editor, "_earthquakeDustLargeBox").SelectedItem = "DustLarge";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var quake = FindPower(XDocument.Load(path), "Quake");
        Assert.Equal("EarthquakeAttack", quake.Element("attackproto")?.Value);
        Assert.Equal("0.25", quake.Element("attackinterval")?.Value);
        Assert.Equal("0.25", quake.Element("attacktreeaccuracy")?.Value);
        Assert.Equal("7.5", quake.Element("epicenterzoneradius")?.Value);
        Assert.Equal("17.5", quake.Element("middlezoneradius")?.Value);
        Assert.Equal("30", quake.Element("outerzoneradius")?.Value);
        Assert.Equal("1", quake.Element("epicenterzonedamageratio")?.Value);
        Assert.Equal("0.75", quake.Element("middlezonedamageratio")?.Value);
        Assert.Equal("0.5", quake.Element("outerzonedamageratio")?.Value);
        Assert.Equal("DustSmall", quake.Element("dustsmallvfxproto")?.Value);
        Assert.Equal("DustMedium", quake.Element("dustmediumvfxproto")?.Value);
        Assert.Equal("DustLarge", quake.Element("dustlargevfxproto")?.Value);
    }

    [Fact]
    public void OriginalEarthquakeHidesAbsentFieldsAndDustRemoveButton()
    {
        var original = XElement.Parse("""
            <power name="Quake" type="Earthquake"><attackproto>EarthquakeAttack</attackproto>
              <epicenterzoneradius>7.5</epicenterzoneradius>
              <dustsmallvfxproto>DustSmall</dustsmallvfxproto>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Quake", true, original)], null,
            protoUnitNames: ["EarthquakeAttack", "DustSmall"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("Quake"));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_earthquakeIntervalHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_earthquakeMiddleRadiusHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_earthquakeDustMediumHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeEarthquakeDustVfxButton").IsVisible));
    }

    [Fact]
    public void MeteorUsesVerifiedStrikeSplashSoundAndThrowTags()
    {
        using var workspace = new TestWorkspace(nameof(MeteorUsesVerifiedStrikeSplashSoundAndThrowTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='FallingSky' type='Meteor' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Meteor", "LandSplash", "WaterSplash"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("FallingSky");
            GetPrivateField<AutoCompleteBox>(editor, "_meteorUnitBox").SelectedItem = "Meteor";
            GetPrivateField<TextBox>(editor, "_tempestSlowestStrikeBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_tempestFastestStrikeBox").Text = "-0.5";
            GetPrivateField<TextBox>(editor, "_meteorStandardStrikesBox").Text = "12";
            GetPrivateField<TextBox>(editor, "_meteorPriorityStrikesBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_meteorMinGroupSizeBox").Text = "5";
            GetPrivateField<TextBox>(editor, "_meteorGroupSearchRadiusBox").Text = "6";
            GetPrivateField<CheckBox>(editor, "_meteorLargestGroupCheck").IsChecked = true;
            GetPrivateField<TextBox>(editor, "_meteorSecondaryAccuracyBox").Text = "0.5";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorLandVfxBox").SelectedItem = "LandSplash";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorWaterVfxBox").SelectedItem = "WaterSplash";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorFirstApproachSoundBox").Text = "MeteorApproach";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorFirstStrikeSoundBox").Text = "MeteorBigHit";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorStrikeSoundBox").Text = "MeteorSmallHit";
            GetPrivateField<AutoCompleteBox>(editor, "_meteorApproachSoundBox").Text = "MeteorWhoosh";
            foreach (var (fieldName, label) in new[]
                     {
                         ("_meteorFirstApproachSoundBox", "First Approach"),
                         ("_meteorFirstStrikeSoundBox", "First Strike"),
                         ("_meteorStrikeSoundBox", "Strike"),
                         ("_meteorApproachSoundBox", "Approach")
                     })
            {
                var pair = Assert.IsType<StackPanel>(GetPrivateField<AutoCompleteBox>(editor, fieldName).Parent);
                Assert.Contains(pair.Children.OfType<TextBlock>(), text => text.Text == label);
            }
            GetPrivateField<TextBox>(editor, "_minThrowDistanceBox").Text = "6";
            GetPrivateField<TextBox>(editor, "_maxThrowDistanceBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_minThrowHeightBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_maxThrowHeightBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_minThrowVelocityBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_maxThrowVelocityBox").Text = "20";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var meteor = FindPower(XDocument.Load(path), "FallingSky");
        Assert.Equal("Meteor", meteor.Element("meteorprotounit")?.Value);
        Assert.Equal("0.5", meteor.Element("sloweststriketime")?.Value);
        Assert.Equal("-0.5", meteor.Element("fasteststriketime")?.Value);
        Assert.Equal("12", meteor.Element("numberofstrikes")?.Value);
        Assert.Equal("3", meteor.Element("numberofprioritystrikes")?.Value);
        Assert.Equal("5", meteor.Element("minimumgroupsize")?.Value);
        Assert.Equal("6", meteor.Element("groupsearchradius")?.Value);
        Assert.Equal("1", meteor.Element("uselargestgroupbelowminsize")?.Value);
        Assert.Equal("0.5", meteor.Element("secondaryaccuracy")?.Value);
        Assert.Equal("LandSplash", meteor.Element("landimpactprotounit")?.Value);
        Assert.Equal("WaterSplash", meteor.Element("watersplashprotounit")?.Value);
        Assert.Equal("MeteorApproach", meteor.Element("firstapproachsound")?.Value);
        Assert.Equal("MeteorBigHit", meteor.Element("firststrikesoundset")?.Value);
        Assert.Equal("MeteorSmallHit", meteor.Element("strikesoundset")?.Value);
        Assert.Equal("MeteorWhoosh", meteor.Element("approachsound")?.Value);
        Assert.Equal("6", meteor.Element("minthrowdistance")?.Value);
        Assert.Equal("20", meteor.Element("maxthrowvelocity")?.Value);
        Assert.Null(meteor.Element("meteorunit"));
        Assert.Null(meteor.Element("standardstrikes"));
    }

    [Fact]
    public void MeteorIntegerFieldsRejectDecimalsAndOriginalHidesAbsentLargestGroupFlag()
    {
        var original = XElement.Parse("""
            <power name="BaseMeteor" type="Meteor"><meteorprotounit>Meteor</meteorprotounit>
              <numberofstrikes>12</numberofstrikes><numberofprioritystrikes>3</numberofprioritystrikes>
              <minimumgroupsize>5</minimumgroupsize><groupsearchradius>6.0</groupsearchradius>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseMeteor", true, original)], null, protoUnitNames: ["Meteor"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("BaseMeteor"));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_meteorLargestGroupCheck").IsVisible));

        using var workspace = new TestWorkspace(nameof(MeteorIntegerFieldsRejectDecimalsAndOriginalHidesAbsentLargestGroupFlag));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='CustomMeteor' type='Meteor'><numberofstrikes>4</numberofstrikes></power></powersmod>");
        var custom = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            custom.SetModifiedMode(true); custom.SelectPower("CustomMeteor");
            GetPrivateField<TextBox>(custom, "_meteorStandardStrikesBox").Text = "4.5";
        });
        Assert.True(AvaloniaTestHost.RunAsync(custom.SaveAsync));
        Assert.NotEqual("4.5", FindPower(XDocument.Load(path), "CustomMeteor").Element("numberofstrikes")?.Value);
    }

    [Fact]
    public void TornadoUsesVerifiedFunnelThrowExclusionAndDebrisTags()
    {
        using var workspace = new TestWorkspace(nameof(TornadoUsesVerifiedFunnelThrowExclusionAndDebrisTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Whirlwind' type='Tornado' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Tornado", "Farm", "Building", "Wood", "Gold"],
            unitTypeNames: ["AbstractSettlement", "Fish", "Wave"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("Whirlwind");
            GetPrivateField<AutoCompleteBox>(editor, "_tornadoUnitBox").SelectedItem = "Tornado";
            GetPrivateField<TextBox>(editor, "_tornadoDamageIntervalBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_tornadoGreaterDamageRadiusBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_tornadoWaypointCountBox").Text = "200";
            GetPrivateField<TextBox>(editor, "_tornadoSpiralSizeBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_tornadoFunnelRadiusBox").Text = "4.5";
            GetPrivateField<TextBox>(editor, "_tornadoFunnelHeightBox").Text = "16";
            GetPrivateField<TextBox>(editor, "_minThrowDistanceBox").Text = "9";
            GetPrivateField<TextBox>(editor, "_maxThrowDistanceBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_minThrowHeightBox").Text = "14";
            GetPrivateField<TextBox>(editor, "_maxThrowHeightBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_minThrowVelocityBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_maxThrowVelocityBox").Text = "20";

            GetPrivateField<Button>(editor, "_addThrowExcludePullButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(GetPrivateField<Grid>(editor, "_tornadoFunnelRow").Children.OfType<WrapPanel>().Single(),
                GetPrivateField<WrapPanel>(editor, "_throwExcludePullHost").Parent);
            var excludePull = GetPrivateField<AutoCompleteBox>(editor, "_throwExcludePullPicker");
            excludePull.SelectedItem = "AbstractSettlement";
            excludePull.SelectedItem = "Farm";
            excludePull.SelectedItem = "Building";
            GetPrivateField<Button>(editor, "_addThrowExcludeThrowButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var excludeThrow = GetPrivateField<AutoCompleteBox>(editor, "_throwExcludeThrowPicker");
            excludeThrow.SelectedItem = "Building";
            excludeThrow.SelectedItem = "Wood";
            excludeThrow.SelectedItem = "Fish";

            GetPrivateField<TextBox>(editor, "_tornadoDebrisXzBox").Text = "6.5";
            GetPrivateField<TextBox>(editor, "_tornadoDebrisYBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_tornadoDebrisAngularBox").Text = "3.14";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var tornado = FindPower(XDocument.Load(path), "Whirlwind");
        Assert.Equal("Tornado", tornado.Element("tornadoprotounit")?.Value);
        Assert.Equal("0.5", tornado.Element("damageinterval")?.Value);
        Assert.Equal("15", tornado.Element("greaterdamageradius")?.Value);
        Assert.Equal("200", tornado.Element("numberofwaypoints")?.Value);
        Assert.Equal("1", tornado.Element("spiralsize")?.Value);
        Assert.Equal("4.5", tornado.Element("funnelradius")?.Value);
        Assert.Equal("16", tornado.Element("funnelheight")?.Value);
        Assert.Equal(["AbstractSettlement", "Farm", "Building"],
            tornado.Elements("excludefrompullingintofunnel").Select(item => item.Value));
        Assert.Equal(["Building", "Wood", "Fish"],
            tornado.Elements("excludefromthrow").Select(item => item.Value));
        Assert.Equal("6.5", tornado.Element("debrisxzvelocity")?.Value);
        Assert.Equal("3", tornado.Element("debrisyvelocity")?.Value);
        Assert.Equal("3.14", tornado.Element("debrisangularvelocity")?.Value);
        Assert.Null(tornado.Element("searchanddamagescaleradius"));
        Assert.Null(tornado.Element("excludefrompull"));
    }

    [Fact]
    public void OriginalTornadoLoadsThrowExclusionsAsReadOnlyChips()
    {
        var original = XElement.Parse("""
            <power name="BaseTornado" type="Tornado"><tornadoprotounit>Tornado</tornadoprotounit>
              <minthrowdistance>9</minthrowdistance><maxthrowdistance>15</maxthrowdistance>
              <excludefrompullingintofunnel>Farm</excludefrompullingintofunnel>
              <excludefromthrow>Building</excludefromthrow>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseTornado", true, original)], null,
            protoUnitNames: ["Tornado", "Farm", "Building"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("BaseTornado"));
        Assert.Single(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_throwExcludePullChips").Children));
        Assert.Single(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_throwExcludeThrowChips").Children));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_throwExcludePullPicker").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeThrowExcludeThrowButton").IsVisible));
    }

    [Fact]
    public void ThunderBurstUsesVerifiedTimingThrowSizeAndVfxTags()
    {
        using var workspace = new TestWorkspace(nameof(ThunderBurstUsesVerifiedTimingThrowSizeAndVfxTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Thunder' type='ThunderBurst' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["StartArc", "ChargeArea", "BurstEffect"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("Thunder");
            GetPrivateField<TextBox>(editor, "_thunderBurstAmountBox").Text = "5";
            GetPrivateField<TextBox>(editor, "_thunderBurstDelayBox").Text = "4.5";
            GetPrivateField<TextBox>(editor, "_thunderBurstRadiusIncreaseBox").Text = "5.25";
            GetPrivateField<TextBox>(editor, "_thunderBurstStunDurationBox").Text = "0.75";
            GetPrivateField<TextBox>(editor, "_minThrowDistanceBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_maxThrowDistanceBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_minThrowHeightBox").Text = "6";
            GetPrivateField<TextBox>(editor, "_maxThrowHeightBox").Text = "12";
            GetPrivateField<TextBox>(editor, "_minThrowVelocityBox").Text = "15";
            GetPrivateField<TextBox>(editor, "_maxThrowVelocityBox").Text = "22";
            Assert.True(GetPrivateField<Grid>(editor, "_throwPropertiesRow").IsVisible);
            Assert.Equal("15", GetPrivateField<XElement>(editor, "_currentEntry")
                .Element("minthrowdistance")?.Value);
            Assert.True(GetPrivateField<Button>(editor, "_addThrowSizeLimitButton").IsVisible);
            GetPrivateField<Button>(editor, "_addThrowSizeLimitButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_throwSizeLimitBox").Text = "9";
            GetPrivateField<AutoCompleteBox>(editor, "_thunderBurstStartingVfxBox").SelectedItem = "StartArc";
            GetPrivateField<AutoCompleteBox>(editor, "_thunderBurstChargingVfxBox").SelectedItem = "ChargeArea";
            GetPrivateField<AutoCompleteBox>(editor, "_thunderBurstBurstVfxBox").SelectedItem = "BurstEffect";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var thunder = FindPower(XDocument.Load(path), "Thunder");
        Assert.Equal("5", thunder.Element("amount")?.Value);
        Assert.Equal("4.5", thunder.Element("delay")?.Value);
        Assert.Equal("5.25", thunder.Element("radiusincreasestep")?.Value);
        Assert.Equal("0.75", thunder.Element("stunduration")?.Value);
        Assert.Equal("15", thunder.Element("minthrowdistance")?.Value);
        Assert.Equal("22", thunder.Element("maxthrowvelocity")?.Value);
        Assert.Equal("9", thunder.Element("throwsizelimit")?.Value);
        Assert.Equal("StartArc", thunder.Element("startingvfx")?.Value);
        Assert.Equal("ChargeArea", thunder.Element("chargingvfx")?.Value);
        Assert.Equal("BurstEffect", thunder.Element("burstvfx")?.Value);
        Assert.Null(thunder.Element("radiusincreaseeachstrike"));
    }

    [Fact]
    public void OriginalThunderBurstLoadsSizeLimitWithoutEditableOptionalControls()
    {
        var original = XElement.Parse("""
            <power name="BaseThunder" type="ThunderBurst"><amount>5</amount><delay>5.0</delay>
              <radiusincreasestep>5.0</radiusincreasestep><minthrowdistance>15.0</minthrowdistance>
              <throwsizelimit>9</throwsizelimit><stunduration>0.0</stunduration>
              <startingvfx>StartArc</startingvfx>
              <burstvfx>BurstEffect</burstvfx></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseThunder", true, original)], null,
            protoUnitNames: ["StartArc", "ChargeArea", "BurstEffect"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("BaseThunder"));
        Assert.Equal("5", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBox>(editor, "_thunderBurstAmountBox").Text));
        Assert.Equal("9", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBox>(editor, "_throwSizeLimitBox").Text));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_throwSizeLimitHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_thunderBurstChargingVfxHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeThrowSizeLimitButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_addThrowSizeLimitButton").IsVisible));
    }

    [Fact]
    public void TyphoonUsesVerifiedDragonTyphoonRadiusAndTreeBendTags()
    {
        using var workspace = new TestWorkspace(nameof(TyphoonUsesVerifiedDragonTyphoonRadiusAndTreeBendTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='StormDragon' type='Typhoon' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["TyphoonDragon", "Typhoon", "TyphoonRain"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("StormDragon"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<AutoCompleteBox>(editor, "_typhoonDragonUnitBox").SelectedItem = "TyphoonDragon";
            GetPrivateField<TextBox>(editor, "_typhoonDragonSpawnDelayBox").Text = "1.25";
            GetPrivateField<TextBox>(editor, "_typhoonDragonAttackIntervalBox").Text = "0.05";
            GetPrivateField<AutoCompleteBox>(editor, "_typhoonUnitBox").SelectedItem = "Typhoon";
            GetPrivateField<Button>(editor, "_addTyphoonRainUnitButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_typhoonRainUnitBox").SelectedItem = "TyphoonRain";
            GetPrivateField<TextBox>(editor, "_typhoonSpawnDelayBox").Text = "2";
            GetPrivateField<TextBox>(editor, "_typhoonAttackDelayBox").Text = "2.5";
            GetPrivateField<TextBox>(editor, "_typhoonAttackIntervalBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_typhoonRadiusStartBox").Text = "5";
            GetPrivateField<TextBox>(editor, "_typhoonRadiusEndBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_typhoonRadiusExpansionBox").Text = "5.5";
            Assert.True(GetPrivateField<Button>(editor, "_addTempestTreeBendButton").IsVisible);
            GetPrivateField<Button>(editor, "_addTempestTreeBendButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_tempestPeriodMinBox").Text = "2.5";
            GetPrivateField<TextBox>(editor, "_tempestPeriodMaxBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_tempestStrengthMinBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_tempestStrengthMaxBox").Text = "0.35";
            GetPrivateField<TextBox>(editor, "_tempestAmplitudeMinBox").Text = "0";
            GetPrivateField<TextBox>(editor, "_tempestAmplitudeMaxBox").Text = "0.7";
            GetPrivateField<TextBox>(editor, "_tempestAngleOffsetBox").Text = "0.25";
            GetPrivateField<TextBox>(editor, "_tempestTreeDurationBox").Text = "12";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var typhoon = FindPower(XDocument.Load(path), "StormDragon");
        Assert.Equal("TyphoonDragon", typhoon.Element("dragonprotounit")?.Value);
        Assert.Equal("1.25", typhoon.Element("dragonspawndelayseconds")?.Value);
        Assert.Equal("0.05", typhoon.Element("dragonattackinterval")?.Value);
        Assert.Equal("Typhoon", typhoon.Element("typhoonprotounit")?.Value);
        Assert.Equal("TyphoonRain", typhoon.Element("rainprotounit")?.Value);
        Assert.Equal("2", typhoon.Element("typhoonspawndelayseconds")?.Value);
        Assert.Equal("2.5", typhoon.Element("typhoonattackdelayseconds")?.Value);
        Assert.Equal("0.5", typhoon.Element("typhoonattackinterval")?.Value);
        Assert.Equal("5", typhoon.Element("typhoonradiusstart")?.Value);
        Assert.Equal("25", typhoon.Element("typhoonradiusend")?.Value);
        Assert.Equal("5.5", typhoon.Element("typhoonradiusexpansionseconds")?.Value);
        Assert.Equal("2.5", typhoon.Element("treebendminperiod")?.Value);
        Assert.Equal("4", typhoon.Element("treebendmaxperiod")?.Value);
        Assert.Equal("0.25", typhoon.Element("treebendminstrength")?.Value);
        Assert.Equal("0.35", typhoon.Element("treebendmaxstrength")?.Value);
        Assert.Equal("0", typhoon.Element("treebendminamplitude")?.Value);
        Assert.Equal("0.7", typhoon.Element("treebendmaxamplitude")?.Value);
        Assert.Equal("0.25", typhoon.Element("treebendangleoffset")?.Value);
        Assert.Equal("12", typhoon.Element("treebendingduration")?.Value);
        Assert.Null(typhoon.Element("radiusexpansionrate"));
    }

    [Fact]
    public void OriginalTyphoonHidesAbsentFieldsAndTreeBendRemoveButton()
    {
        var original = XElement.Parse("""
            <power name="BaseTyphoon" type="Typhoon"><dragonprotounit>TyphoonDragon</dragonprotounit>
              <dragonattackinterval>0.05</dragonattackinterval><typhoonprotounit>Typhoon</typhoonprotounit>
              <typhoonradiusstart>5</typhoonradiusstart><treebendminperiod>2.5</treebendminperiod>
              <treebendmaxperiod>4</treebendmaxperiod></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseTyphoon", true, original)], null,
            protoUnitNames: ["TyphoonDragon", "Typhoon"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("BaseTyphoon"));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_typhoonSpawnDelayHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_typhoonRadiusEndHost").IsVisible));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_tempestTreeBendFields").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeTempestTreeBendButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_addTempestTreeBendButton").IsVisible));
    }

    [Fact]
    public void FrostUsesVerifiedFreezeStrikeVfxAndDriftTags()
    {
        using var workspace = new TestWorkspace(nameof(FrostUsesVerifiedFreezeStrikeVfxAndDriftTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Winter' type='Frost' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXIceSheet", "VFXSnowDrift"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("Winter");
            GetPrivateField<TextBox>(editor, "_frostExpansionBox").Text = "3";
            GetPrivateField<TextBox>(editor, "_frostTransitionBox").Text = "1.5";
            GetPrivateField<TextBox>(editor, "_frostDurationBox").Text = "45";
            GetPrivateField<TextBox>(editor, "_frostStrikeMinBox").Text = "0.5";
            GetPrivateField<TextBox>(editor, "_frostStrikeMaxBox").Text = "1";
            GetPrivateField<Button>(editor, "_addFrostArmorButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_frostArmorBox").Text = "1.4";
            GetPrivateField<AutoCompleteBox>(editor, "_frostStormBox").SelectedItem = "VFXIceSheet";
            GetPrivateField<AutoCompleteBox>(editor, "_frostSnowDriftBox").SelectedItem = "VFXSnowDrift";
            GetPrivateField<Button>(editor, "_addFrostDriftButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_frostSlowestDriftBox").Text = "8";
            GetPrivateField<TextBox>(editor, "_frostFastestDriftBox").Text = "4";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var frost = FindPower(XDocument.Load(path), "Winter");
        Assert.Equal("3", frost.Element("freezeexpansiontime")?.Value);
        Assert.Equal("1.5", frost.Element("freezetransitiontime")?.Value);
        Assert.Equal("45", frost.Element("freezeholdtime")?.Value);
        Assert.Equal("0.5", frost.Element("fasteststriketime")?.Value);
        Assert.Equal("1", frost.Element("sloweststriketime")?.Value);
        Assert.Equal("1", frost.Element("armoramount")?.Value);
        Assert.Equal("VFXIceSheet", frost.Element("stormprotounit")?.Value);
        Assert.Equal("VFXSnowDrift", frost.Element("snowdriftprotounit")?.Value);
        Assert.Equal("8", frost.Element("slowestdrifttime")?.Value);
        Assert.Equal("4", frost.Element("fastestdrifttime")?.Value);
        Assert.Null(frost.Element("expansiontime"));
        Assert.Null(frost.Element("snowdrift"));
    }

    [Fact]
    public void OriginalFrostHidesAbsentFieldsAndOptionalRemoveButtons()
    {
        var original = XElement.Parse("""
            <power name="BaseFrost" type="Frost"><freezeexpansiontime>3</freezeexpansiontime>
              <fasteststriketime>0.5</fasteststriketime><stormprotounit>VFXIceSheet</stormprotounit>
              <armoramount>-0.99</armoramount><slowestdrifttime>8</slowestdrifttime></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BaseFrost", true, original)], null,
            protoUnitNames: ["VFXIceSheet"]));
        AvaloniaTestHost.Run(() => editor.SelectPower("BaseFrost"));
        Assert.Equal("-0.99", AvaloniaTestHost.Run(() =>
            GetPrivateField<TextBox>(editor, "_frostArmorBox").Text));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_frostTransitionHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_frostSnowDriftHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_frostFastestDriftHost").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeFrostArmorButton").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Button>(editor, "_removeFrostDriftButton").IsVisible));
    }

    [Fact]
    public void WitherUsesVerifiedWaveKillDamageGeneratorAndTerrainCreepTags()
    {
        using var workspace = new TestWorkspace(nameof(WitherUsesVerifiedWaveKillDamageGeneratorAndTerrainCreepTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='DryWind' type='Wither' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["WitherWave", "Farm"],
            unitTypeNames: ["LogicalTypeWitherable"], terrainCreepNames: ["WitherCreep"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("DryWind");
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_tempestOptionalFlow").Children.Count);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_witherDamageRow").Parent);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_witherCreepGeneratorsRow").Parent);
            GetPrivateField<AutoCompleteBox>(editor, "_witherWaveUnitBox").SelectedItem = "WitherWave";
            GetPrivateField<TextBox>(editor, "_witherDurationBox").Text = "4.25";
            GetPrivateField<TextBox>(editor, "_witherVfxIntervalBox").Text = "0.15";
            GetPrivateField<Button>(editor, "_addWitherKillTypesButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var killPicker = GetPrivateField<AutoCompleteBox>(editor, "_witherKillTypePicker");
            killPicker.SelectedItem = "LogicalTypeWitherable";
            killPicker.SelectedItem = "Farm";
            GetPrivateField<Button>(editor, "_addWitherDamageButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_witherDamageBox").Text = "1.5";
            GetPrivateField<Button>(editor, "_addWitherCreepGeneratorsButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_witherCreepGeneratorsBox").Text = "6";
            var terrainHost = GetPrivateField<StackPanel>(editor, "_droughtTerrainCreepHost");
            GetPrivateField<Button>(editor, "_addTerrainCreepButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DescendantControls(terrainHost).OfType<AutoCompleteBox>().First().SelectedItem =
                "WitherCreep";
            Assert.Contains(DescendantControls(terrainHost).OfType<Button>(),
                button => button.Classes.Contains("remove-button"));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var wither = FindPower(XDocument.Load(path), "DryWind");
        Assert.Equal("WitherWave", wither.Element("waveprotounit")?.Value);
        Assert.Equal("4.25", wither.Element("waveduration")?.Value);
        Assert.Equal("0.15", wither.Element("vfxinterval")?.Value);
        Assert.Equal(["LogicalTypeWitherable", "Farm"],
            wither.Elements("killunittype").Select(element => element.Value));
        Assert.Equal("1", wither.Element("damagepercentofmaxhp")?.Value);
        Assert.Equal("6", wither.Element("creepgenerators")?.Value);
        Assert.NotNull(wither.Element("terraincreep"));
        Assert.Null(wither.Element("maxhpdamage"));
    }

    [Fact]
    public void WitherSpawnProtoRemainsAReusableAdditionalAttribute()
    {
        var original = XElement.Parse("""
            <power name="DryWind" type="Wither"><waveprotounit>WitherWave</waveprotounit>
              <spawnproto>WitherBlocker</spawnproto></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("DryWind", true, original)], null,
            protoUnitNames: ["WitherWave", "WitherBlocker"]));

        AvaloniaTestHost.Run(() => editor.SelectPower("DryWind"));

        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<Grid>(editor, "_tartarianSpawnRow").IsVisible));
        Assert.Contains(AvaloniaTestHost.Run(() =>
            GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children),
            child => ReferenceEquals(child, GetPrivateField<Grid>(editor, "_tartarianSpawnRow")));
        Assert.Equal("WitherBlocker", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_spawnProtoBox").Text));
    }

    [Fact]
    public void TerrainCreepDisplaysAndRemovesTheFirstInstance()
    {
        using var workspace = new TestWorkspace(nameof(TerrainCreepDisplaysAndRemovesTheFirstInstance));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="DryWind" type="Wither">
              <terraincreep creep="FirstCreep" maxradius="4" />
              <terraincreep creep="SecondCreep" maxradius="8" />
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            terrainCreepNames: ["FirstCreep", "SecondCreep"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("DryWind");
            var host = GetPrivateField<StackPanel>(editor, "_droughtTerrainCreepHost");
            Assert.Equal("FirstCreep", DescendantControls(host).OfType<AutoCompleteBox>().First().Text);
            var remove = DescendantControls(host).OfType<Button>()
                .Single(button => button.Classes.Contains("remove-button"));
            Assert.Equal(2, remove.Margin.Left);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("SecondCreep", DescendantControls(host).OfType<AutoCompleteBox>().First().Text);
        });
    }

    [Fact]
    public void ArcadianMeadowUsesVerifiedPlacementSpawnGrassAndTerrainCreepTags()
    {
        using var workspace = new TestWorkspace(nameof(ArcadianMeadowUsesVerifiedPlacementSpawnGrassAndTerrainCreepTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Meadow' type='ArcadianMeadow' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["DamageReceiver", "Blocker", "MeadowVfx", "GreekGrass", "NorseGrass"],
            unitTypeNames: ["LogicalTypeWitherable"], terrainCreepNames: ["ArcadianMeadowCreep"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("Meadow");
            GetPrivateField<AutoCompleteBox>(editor, "_arcadianPlacementBox").SelectedItem =
                "LogicalTypeWitherable";
            GetPrivateField<AutoCompleteBox>(editor, "_arcadianDamageReceiverBox").SelectedItem =
                "DamageReceiver";
            GetPrivateField<AutoCompleteBox>(editor, "_arcadianBlockerBox").SelectedItem = "Blocker";
            GetPrivateField<TextBox>(editor, "_arcadianBlockerDurationBox").Text = "25";
            GetPrivateField<Button>(editor, "_addArcadianVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_arcadianVfxBox").SelectedItem = "MeadowVfx";
            var addGrass = GetPrivateField<Button>(editor, "_addArcadianGrassButton");
            addGrass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addGrass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var grassGroups = GetPrivateField<WrapPanel>(editor, "_arcadianGrassRows")
                .Children.OfType<StackPanel>().ToList();
            Assert.Equal(2, grassGroups.Count);
            Assert.All(grassGroups, group => Assert.IsType<WrapPanel>(group.Parent));
            grassGroups[0].Children.OfType<AutoCompleteBox>().Single().SelectedItem = "GreekGrass";
            var firstNumbers = grassGroups[0].Children.OfType<TextBox>().ToList();
            firstNumbers[0].Text = "0.1358"; firstNumbers[1].Text = "0.8239";
            grassGroups[1].Children.OfType<AutoCompleteBox>().Single().SelectedItem = "NorseGrass";
            var secondNumbers = grassGroups[1].Children.OfType<TextBox>().ToList();
            secondNumbers[0].Text = "0.2963"; secondNumbers[1].Text = "0.4083";
            var terrainHost = GetPrivateField<StackPanel>(editor, "_droughtTerrainCreepHost");
            GetPrivateField<Button>(editor, "_addTerrainCreepButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DescendantControls(terrainHost).OfType<AutoCompleteBox>().First().SelectedItem =
                "ArcadianMeadowCreep";
            Assert.Contains(DescendantControls(terrainHost).OfType<Button>(),
                button => button.Classes.Contains("remove-button"));
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var meadow = FindPower(XDocument.Load(path), "Meadow");
        Assert.Equal("LogicalTypeWitherable", meadow.Element("terrainplacementcheck")?.Value);
        Assert.Equal("DamageReceiver", meadow.Element("damagereceiverproto")?.Value);
        Assert.Equal("Blocker", meadow.Element("spawnproto")?.Value);
        Assert.Equal("25", meadow.Element("blockerradius")?.Value);
        Assert.Equal("MeadowVfx", meadow.Element("vfxproto")?.Value);
        var grasses = meadow.Elements("vfxgrass").ToList();
        Assert.Equal(2, grasses.Count);
        Assert.Equal(("GreekGrass", "0.1358", "0.8239"),
            (grasses[0].Value, grasses[0].Attribute("r")?.Value, grasses[0].Attribute("theta")?.Value));
        Assert.Equal(("NorseGrass", "0.2963", "0.4083"),
            (grasses[1].Value, grasses[1].Attribute("r")?.Value, grasses[1].Attribute("theta")?.Value));
        Assert.NotNull(meadow.Element("terraincreep"));
        Assert.Null(meadow.Element("blockerduration"));
    }

    [Fact]
    public void PowerCloneUsesVerifiedSlotCostAttributesPlacementVfxAndReplacementTargets()
    {
        using var workspace = new TestWorkspace(nameof(PowerCloneUsesVerifiedSlotCostAttributesPlacementVfxAndReplacementTargets));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Mirror' type='PowerClone' /></powersmod>");
        var tailwind = XElement.Parse("<power name='Tailwind' type='UnitModify' />");
        var bloodPact = XElement.Parse("<power name='BloodPact' type='GeneralEffect' />");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Tailwind", true, tailwind), new PowerDefinition("BloodPact", true, bloodPact)],
            path, protoUnitNames: ["VFXObsidianMirror"]));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true); editor.SelectPower("Mirror");
            GetPrivateField<ComboBox>(editor, "_powerCloneSlotBox").SelectedItem = "3";
            GetPrivateField<TextBox>(editor, "_costBox").Text = "1.5";
            GetPrivateField<CheckBox>(editor, "_powerCloneCopyCostCheck").IsChecked = true;
            GetPrivateField<TextBox>(editor, "_repeatCostBox").Text = "2.25";
            GetPrivateField<CheckBox>(editor, "_powerCloneCopyRepeatCostCheck").IsChecked = true;
            GetPrivateField<AutoCompleteBox>(editor, "_powerClonePlacementVfxBox").SelectedItem =
                "VFXObsidianMirror";
            GetPrivateField<CheckBox>(editor, "_powerCloneSeededCheck").IsChecked = true;
            var picker = GetPrivateField<AutoCompleteBox>(editor, "_powerCloneReplacementPicker");
            picker.SelectedItem = "Tailwind";
            picker.SelectedItem = "BloodPact";
            Assert.Equal("1.5", GetPrivateField<TextBox>(editor, "_costBox").Text);
            Assert.Equal("2.25", GetPrivateField<TextBox>(editor, "_repeatCostBox").Text);
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var mirror = FindPower(XDocument.Load(path), "Mirror");
        Assert.Equal("3", mirror.Element("powerslot")?.Value);
        Assert.Equal("1.5", mirror.Element("cost")?.Value);
        Assert.Equal("true", mirror.Element("cost")?.Attribute("copycostoftargetpower")?.Value);
        Assert.Equal("2.25", mirror.Element("repeatcost")?.Value);
        Assert.Equal("true", mirror.Element("repeatcost")
            ?.Attribute("copyrepeatcostoftargetpower")?.Value);
        Assert.Equal("VFXObsidianMirror", mirror.Element("placementvfx")?.Value);
        var replacement = mirror.Element("duplicatetargetreplacement");
        Assert.NotNull(replacement);
        Assert.NotNull(replacement.Attribute("seeded"));
        Assert.Equal(["Tailwind", "BloodPact"], replacement.Elements("target").Select(item => item.Value));
        Assert.Null(mirror.Element("replacementpower"));
    }

    [Fact]
    public void OriginalPowerCloneLoadsReplacementChipsReadOnlyAndHidesEmptySpecificFields()
    {
        var original = XElement.Parse("""
            <power name="Mirror" type="PowerClone"><powerslot>1</powerslot>
              <cost copycostoftargetpower="true">1</cost>
              <repeatcost copyrepeatcostoftargetpower="true">1</repeatcost>
              <duplicatetargetreplacement seeded=""><target>Tailwind</target></duplicatetargetreplacement>
            </power>
            """);
        var tailwind = XElement.Parse("<power name='Tailwind' type='UnitModify' />");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Mirror", true, original), new PowerDefinition("Tailwind", true, tailwind)], null));
        AvaloniaTestHost.Run(() => editor.SelectPower("Mirror"));
        Assert.Equal("1", AvaloniaTestHost.Run(() =>
            GetPrivateField<ComboBox>(editor, "_powerCloneSlotBox").SelectedItem));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_powerCloneCopyCostCheck").IsChecked == true));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_powerCloneSeededCheck").IsChecked == true));
        Assert.Single(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_powerCloneReplacementChips").Children));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_powerCloneReplacementPicker").IsVisible));
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<Grid>(editor, "_powerCloneVfxRow").IsVisible));
    }

    [Fact]
    public void UnderworldInvasionUsesVerifiedEidolonSpawnVfxTechAndSuddenDeathTags()
    {
        using var workspace = new TestWorkspace(nameof(UnderworldInvasionUsesVerifiedEidolonSpawnVfxTechAndSuddenDeathTags));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Invasion' type='UnderworldInvasion' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXUnderworldInvasion", "VFXEidolonSpawn", "VFXTraitor", "VFXUnderworldInvasionLoop"],
            unitTypeNames: ["LogicalTypeValidEidolon"],
            techNames: ["UnderworldInvasionDurationEffect", "ClassicalAgeGeneral"]));
        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Invasion"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal(4, GetPrivateField<WrapPanel>(editor, "_tempestOptionalFlow").Children.Count);
            Assert.Contains(GetPrivateField<Button>(editor, "_addUnderworldVfxButton"),
                GetPrivateField<WrapPanel>(editor, "_tempestOptionalFlow").Children);
            GetPrivateField<TextBox>(editor, "_underworldEidolonMinBox").Text = "25";
            GetPrivateField<TextBox>(editor, "_underworldEidolonMaxBox").Text = "125";
            GetPrivateField<TextBox>(editor, "_underworldEidolonStepBox").Text = "25";
            GetPrivateField<AutoCompleteBox>(editor, "_underworldEidolonTypeBox").SelectedItem =
                "LogicalTypeValidEidolon";
            GetPrivateField<Button>(editor, "_addUnderworldMultipliersButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_underworldMaxHpMultiplierBox").Text = "1.25";
            GetPrivateField<TextBox>(editor, "_underworldMaxSpeedMultiplierBox").Text = "1.5";
            GetPrivateField<TextBox>(editor, "_underworldSpawnRadiusBox").Text = "10";
            GetPrivateField<TextBox>(editor, "_underworldSpawnDurationBox").Text = "5";
            GetPrivateField<Button>(editor, "_addUnderworldVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_underworldStartVfxBox").SelectedItem =
                "VFXUnderworldInvasion";
            GetPrivateField<AutoCompleteBox>(editor, "_underworldEidolonSpawnVfxBox").SelectedItem =
                "VFXEidolonSpawn";
            GetPrivateField<AutoCompleteBox>(editor, "_underworldNoBirthVfxBox").SelectedItem =
                "VFXTraitor";
            GetPrivateField<AutoCompleteBox>(editor, "_underworldEffectVfxBox").SelectedItem =
                "VFXUnderworldInvasionLoop";
            GetPrivateField<Button>(editor, "_addUnderworldLightingButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_underworldLightSetBox").Text =
                "gp_underworldinvasion";
            GetPrivateField<TextBox>(editor, "_underworldLightBuildUpBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_underworldLightDurationBox").Text = "20";
            GetPrivateField<TextBox>(editor, "_underworldLightShutdownBox").Text = "3";
            GetPrivateField<Button>(editor, "_addUnderworldTechEffectButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_underworldTechEffectBox").SelectedItem =
                "UnderworldInvasionDurationEffect";
            GetPrivateField<Button>(editor, "_addUnderworldOnlyIfTechButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_underworldOnlyIfTechBox").SelectedItem =
                "ClassicalAgeGeneral";
            GetPrivateField<Button>(editor, "_addUnderworldSuddenDeathButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<TextBox>(editor, "_underworldSuddenDeathBox").Text = "0.5";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var invasion = FindPower(XDocument.Load(path), "Invasion");
        Assert.Equal("25", invasion.Element("eidolonmin")?.Value);
        Assert.Equal("125", invasion.Element("maxeidoloncount")?.Value);
        Assert.Equal("25", invasion.Element("eidolonstep")?.Value);
        Assert.Equal("LogicalTypeValidEidolon", invasion.Element("eidolontype")?.Value);
        Assert.Equal("1.25", invasion.Element("eidolonmaxhpmultiplier")?.Value);
        Assert.Equal("1.5", invasion.Element("eidolonmaxspeedmultiplier")?.Value);
        Assert.Equal("10", invasion.Element("eidolonspawnradius")?.Value);
        Assert.Equal("5", invasion.Element("eidolonspawnduration")?.Value);
        Assert.Equal("VFXUnderworldInvasion", invasion.Element("vfxproto")?.Value);
        Assert.Equal("VFXEidolonSpawn", invasion.Element("vfxeidolonspawnproto")?.Value);
        Assert.Equal("VFXTraitor", invasion.Element("vfxforunitswithoutbirthanims")?.Value);
        Assert.Equal("VFXUnderworldInvasionLoop", invasion.Element("vfxcurseproto")?.Value);
        Assert.Equal("gp_underworldinvasion", invasion.Element("lightset")?.Value);
        Assert.Equal("1", invasion.Element("lightsetbuilduptime")?.Value);
        Assert.Equal("20", invasion.Element("lightsetduration")?.Value);
        Assert.Equal("3", invasion.Element("lightsetshutdowntime")?.Value);
        Assert.Equal("UnderworldInvasionDurationEffect", invasion.Element("durationtecheffect")?.Value);
        Assert.Equal("ClassicalAgeGeneral",
            invasion.Element("onlyapplydurationtecheffectiftechactive")?.Value);
        Assert.Equal("0.5", invasion.Element("suddendeathhpmultiplier")?.Value);
        Assert.Null(invasion.Element("numberforeachsacrificed"));
        Assert.Null(invasion.Element("effectvfx"));
    }

    [Fact]
    public void OriginalUnderworldInvasionShowsUsedOptionalGroupsWithoutRemoveButtons()
    {
        var original = XElement.Parse("""
            <power name="Invasion" type="UnderworldInvasion">
              <eidolonmin>25</eidolonmin><maxeidoloncount>125</maxeidoloncount><eidolonstep>25</eidolonstep>
              <eidolonmaxhpmultiplier>1</eidolonmaxhpmultiplier><eidolonmaxspeedmultiplier>1</eidolonmaxspeedmultiplier>
              <eidolontype>LogicalTypeValidEidolon</eidolontype><eidolonspawnradius>10</eidolonspawnradius>
              <eidolonspawnduration>5</eidolonspawnduration><vfxproto>VFXUnderworldInvasion</vfxproto>
              <vfxeidolonspawnproto>VFXEidolonSpawn</vfxeidolonspawnproto>
              <vfxforunitswithoutbirthanims>VFXTraitor</vfxforunitswithoutbirthanims>
              <vfxcurseproto>VFXUnderworldInvasionLoop</vfxcurseproto>
              <lightset>gp_underworldinvasion</lightset><lightsetbuilduptime>1</lightsetbuilduptime>
              <lightsetduration>20</lightsetduration><lightsetshutdowntime>3</lightsetshutdowntime>
              <durationtecheffect>UnderworldInvasionDurationEffect</durationtecheffect>
              <onlyapplydurationtecheffectiftechactive>ClassicalAgeGeneral</onlyapplydurationtecheffectiftechactive>
              <suddendeathhpmultiplier>0.5</suddendeathhpmultiplier>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Invasion", true, original)], null,
            protoUnitNames: ["VFXUnderworldInvasion", "VFXEidolonSpawn", "VFXTraitor", "VFXUnderworldInvasionLoop"],
            unitTypeNames: ["LogicalTypeValidEidolon"],
            techNames: ["UnderworldInvasionDurationEffect", "ClassicalAgeGeneral"]));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("Invasion"));

        Assert.Equal("LogicalTypeValidEidolon", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_underworldEidolonTypeBox").Text));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_underworldVfxFields").IsVisible));
        Assert.Equal("VFXUnderworldInvasion", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_underworldStartVfxBox").Text));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_underworldTechEffectFields").IsVisible));
        Assert.True(AvaloniaTestHost.Run(() =>
            GetPrivateField<WrapPanel>(editor, "_underworldLightingFields").IsVisible));
        Assert.Equal("gp_underworldinvasion", AvaloniaTestHost.Run(() =>
            GetPrivateField<AutoCompleteBox>(editor, "_underworldLightSetBox").Text));
        Assert.All(new[] { "_removeUnderworldMultipliersButton", "_removeUnderworldVfxButton",
            "_removeUnderworldLightingButton",
            "_removeUnderworldTechEffectButton", "_removeUnderworldOnlyIfTechButton",
            "_removeUnderworldSuddenDeathButton" }, name =>
            Assert.False(AvaloniaTestHost.Run(() => GetPrivateField<Button>(editor, name).IsVisible)));
    }

    [Fact]
    public void StampedeUsesVerifiedElementAttributesAndFullLengthFlag()
    {
        using var workspace = new TestWorkspace(nameof(StampedeUsesVerifiedElementAttributesAndFullLengthFlag));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='Inferno' type='Stampede' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Fenrir"], protoActionNames: ["HandAttack"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Inferno");
            GetPrivateField<AutoCompleteBox>(editor, "_stampedeUnitBox").SelectedItem = "Fenrir";
            GetPrivateField<TextBox>(editor, "_stampedeScaleBox").Text = "1";
            GetPrivateField<TextBox>(editor, "_stampedeAnimRateBox").Text = "1.5";
            GetPrivateField<AutoCompleteBox>(editor, "_stampedeAttackActionBox").SelectedItem = "HandAttack";
            GetPrivateField<TextBox>(editor, "_stampedeAttackIntervalBox").Text = "200";
            GetPrivateField<CheckBox>(editor, "_stampedeFullLengthCheck").IsChecked = true;
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "Inferno");
        var stampede = Assert.Single(power.Elements("stampede"));
        Assert.Equal("Fenrir", stampede.Value);
        Assert.Equal("1", stampede.Attribute("scale")?.Value);
        Assert.Equal("1.5", stampede.Attribute("animrate")?.Value);
        Assert.Equal("HandAttack", stampede.Attribute("attackaction")?.Value);
        Assert.Equal("200", stampede.Attribute("attackinterval")?.Value);
        Assert.NotNull(power.Element("fulllength"));
    }

    [Fact]
    public void FreyrsGiftUsesExclusiveAgeSpecificTempUnitCardsAndPlacementRefFlag()
    {
        using var workspace = new TestWorkspace(nameof(
            FreyrsGiftUsesExclusiveAgeSpecificTempUnitCardsAndPlacementRefFlag));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Gift" type="FreyrsGift">
              <createagespecificunit age="ArchaicAge" quantity="1" radius="0" delay="0"
                                     placementref="">GullinBurstiArchaic</createagespecificunit>
              <createagespecificunit age="ClassicalAge" quantity="1" radius="0" delay="0">
                GullinBurstiClassical
              </createagespecificunit>
            </power></powersmod>
            """);
        var units = new[] { "GullinBurstiArchaic", "GullinBurstiClassical", "GullinBurstiHeroic",
            "GullinBurstiMythic", "GullinBurstiWonder" };
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path, protoUnitNames: units));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Gift");
            Assert.Contains(DescendantControls(GetPrivateField<Grid>(editor, "_freyrsGiftCreateRow"))
                .OfType<TextBlock>(), label => label.Text == "Create Age Unit");
            Assert.Contains(DescendantControls(GetPrivateField<Grid>(editor, "_tempUnitCreateRow"))
                .OfType<TextBlock>(), label => label.Text == "Create Unit");
            var rows = GetPrivateField<StackPanel>(editor, "_freyrsGiftCreateRows");
            Assert.Equal(2, rows.Children.OfType<Border>().Count());
            var firstCard = Assert.IsType<StackPanel>(rows.Children.OfType<Border>().First().Child);
            var subject = Assert.IsType<Grid>(firstCard.Children[0]).Children.OfType<WrapPanel>().Single();
            Assert.Equal("ArchaicAge", subject.Children.OfType<ComboBox>()
                .Single(combo => combo.Width == 140).SelectedItem);
            Assert.Contains(DescendantControls(firstCard).OfType<TextBlock>(),
                label => label.Text == "placementRef");

            var add = GetPrivateField<Button>(editor, "_addFreyrsGiftCreateButton");
            for (var index = 0; index < 3; index++)
            {
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var addedCard = rows.Children.OfType<Border>().Last();
                DescendantControls(addedCard).OfType<AutoCompleteBox>().Single(control =>
                    control.Width == 200).SelectedItem =
                    units[index + 2];
            }
            Assert.False(add.IsVisible);
            Assert.Equal(5, rows.Children.OfType<Border>().Count());
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => editor.StatusMessage));
        var creates = FindPower(XDocument.Load(path), "Gift").Elements("createagespecificunit").ToList();
        Assert.Equal(5, creates.Count);
        Assert.Equal(5, creates.Select(create => create.Attribute("age")?.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotNull(creates[0].Attribute("placementref"));
    }

    [Fact]
    public void FimbulWinterUsesVerifiedPackWolfSnowDriftAndStrikeFields()
    {
        using var workspace = new TestWorkspace(nameof(
            FimbulWinterUsesVerifiedPackWolfSnowDriftAndStrikeFields));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Winter" type="Fimbulwinter">
              <wolfproto>FimbulwinterWolf</wolfproto><wolfproto>Fenrir</wolfproto>
              <wolfvfx>VFXFimbulwinter</wolfvfx><wolfcountsoftcap>40</wolfcountsoftcap>
              <wolfcounthardcap>60</wolfcounthardcap><packsize>8</packsize>
              <mainpacktargettype>AbstractSettlement</mainpacktargettype>
              <numextramainpackspawns>1</numextramainpackspawns>
              <minipacksize>2</minipacksize><minipacktargettype>VillageCenter</minipacktargettype>
              <packspawnradius>14</packspawnradius><minpacktargetspawnradius>15</minpacktargetspawnradius>
              <maxpacktargetspawnradius>20</maxpacktargetspawnradius>
              <packwanderdistance>25</packwanderdistance><maxpackwanderdistance>45</maxpackwanderdistance>
              <packscaninterval>5</packscaninterval><spawntargetevalradius>20</spawntargetevalradius>
              <packattacksearchradius>30</packattacksearchradius>
              <snowdrift target="AbstractTownCenter">SnowDriftSettlement</snowdrift>
              <snowdrift target="Stable">SnowDriftStable</snowdrift>
              <snowstormsound>FimbulwinterExists</snowstormsound>
              <sloweststriketime>10</sloweststriketime><fasteststriketime>5</fasteststriketime>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["FimbulwinterWolf", "Fenrir", "VFXFimbulwinter",
                "SnowDriftSettlement", "SnowDriftStable", "SnowDriftTower"],
            unitTypeNames: ["AbstractSettlement", "VillageCenter", "AbstractTownCenter", "Stable"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Winter");
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_fimbulWolfChips").Children.Count);
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_fimbulSnowDriftRows").Children.Count);
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulWolfVfxFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulExtraSpawnsFields").IsVisible);
            var driftRows = GetPrivateField<WrapPanel>(editor, "_fimbulSnowDriftRows");
            var firstDrift = Assert.IsType<StackPanel>(driftRows.Children[0]);
            Assert.Equal("AbstractTownCenter", firstDrift.Children.OfType<AutoCompleteBox>().First().Text);
            Assert.Equal("SnowDriftSettlement", firstDrift.Children.OfType<AutoCompleteBox>().Last().Text);
            GetPrivateField<TextBox>(editor, "_fimbulWolfSoftCapBox").Text = "42";
            GetPrivateField<TextBox>(editor, "_fimbulSecondaryPackRadiusBox").Text = "18.5";
            Assert.Equal("20", GetPrivateField<TextBox>(editor,
                "_fimbulSecondaryPackMaxRadiusBox").Text);
            GetPrivateField<TextBox>(editor, "_fimbulSecondaryPackMaxRadiusBox").Text = "24.5";
            GetPrivateField<Button>(editor, "_addFimbulSnowDriftButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, GetPrivateField<WrapPanel>(editor, "_fimbulSnowDriftRows").Children.Count);
            var addedDrift = Assert.IsType<StackPanel>(
                GetPrivateField<WrapPanel>(editor, "_fimbulSnowDriftRows").Children[2]);
            var addedSelectors = addedDrift.Children.OfType<AutoCompleteBox>().ToList();
            addedSelectors[0].SelectedItem = "AbstractSettlement";
            addedSelectors[1].SelectedItem = "SnowDriftTower";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => editor.StatusMessage));
        var power = FindPower(XDocument.Load(path), "Winter");
        Assert.Equal("42", power.Element("wolfcountsoftcap")?.Value);
        Assert.Equal("18.5", power.Element("minpacktargetspawnradius")?.Value);
        Assert.Equal("24.5", power.Element("maxpacktargetspawnradius")?.Value);
        Assert.Equal(2, power.Elements("wolfproto").Count());
        Assert.Equal(3, power.Elements("snowdrift").Count());
        Assert.Equal("AbstractSettlement", power.Elements("snowdrift").Last().Attribute("target")?.Value);
        Assert.Equal("SnowDriftTower", power.Elements("snowdrift").Last().Value);
        Assert.Equal("FimbulwinterExists", power.Element("snowstormsound")?.Value);
        Assert.Equal("10", power.Element("sloweststriketime")?.Value);
        Assert.Equal("5", power.Element("fasteststriketime")?.Value);
    }

    [Fact]
    public void FimbulWinterOriginalOptionalFieldsDoNotShowRemoveButtons()
    {
        var original = XElement.Parse("""
            <power name="Winter" type="Fimbulwinter"><wolfproto>FimbulwinterWolf</wolfproto>
              <wolfvfx>VFXFimbulwinter</wolfvfx><wolfcounthardcap>60</wolfcounthardcap>
              <numextramainpackspawns>1</numextramainpackspawns>
              <maxpacktargetspawnradius>22</maxpacktargetspawnradius>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Winter", true, original)], null,
            protoUnitNames: ["FimbulwinterWolf", "VFXFimbulwinter"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Winter");
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulWolfVfxFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeFimbulWolfVfxButton").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulExtraSpawnsFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeFimbulExtraSpawnsButton").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_fimbulWolfSoftCapFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulWolfHardCapFields").IsVisible);
            Assert.False(GetPrivateField<StackPanel>(editor, "_fimbulMainPackSizeFields").IsVisible);
            Assert.True(GetPrivateField<StackPanel>(editor, "_fimbulSecondaryPackRadiusFields").IsVisible);
            Assert.Equal("22", GetPrivateField<TextBox>(editor,
                "_fimbulSecondaryPackMaxRadiusBox").Text);
            Assert.False(GetPrivateField<StackPanel>(editor, "_fimbulMainPackTargetFields").IsVisible);
        });
    }

    [Fact]
    public void EarthMonsterUsesVerifiedActionsRegurgitateThrowAndSoundFields()
    {
        using var workspace = new TestWorkspace(nameof(
            EarthMonsterUsesVerifiedActionsRegurgitateThrowAndSoundFields));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Monster" type="EarthMonster">
              <earthmonsterprotounit>EarthMonster</earthmonsterprotounit>
              <strikeaction>Strike</strikeaction><crushaction>Crush</crushaction>
              <numberofadditionalstrikes>2</numberofadditionalstrikes>
              <secondsbetweenstrikes>5</secondsbetweenstrikes><strikeunitlimit>20</strikeunitlimit>
              <minimumgroupsize>1</minimumgroupsize><groupsearchradius>5</groupsearchradius>
              <previousstrikeavoidanceradius>6</previousstrikeavoidanceradius>
              <regurgitatetime>7.5</regurgitatetime><regurgitateunitstime>0.7</regurgitateunitstime>
              <regurgitateheight>5</regurgitateheight><regurgitateanim>Regurgitate</regurgitateanim>
              <regurgitateanimtime>3</regurgitateanimtime>
              <chompthrowswallowtimer>1</chompthrowswallowtimer><chompthrowheight>3</chompthrowheight>
              <swallowplayerrelation>Enemy</swallowplayerrelation>
              <throwupwardsradius>4</throwupwardsradius><throwoutwardsradius>6</throwoutwardsradius>
              <minthrowdistance>6</minthrowdistance><maxthrowdistance>10</maxthrowdistance>
              <minthrowheight>1</minthrowheight><maxthrowheight>2</maxthrowheight>
              <minthrowvelocity>10</minthrowvelocity><maxthrowvelocity>20</maxthrowvelocity>
              <landimpactprotounit>VFXExplosion</landimpactprotounit>
              <emergesoundset>EarthMonsterShockwave</emergesoundset>
              <chompsoundset>EarthMonsterChomp</chompsoundset>
              <regurgitatesoundset>EarthMonsterRegurgitate</regurgitatesoundset>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["EarthMonster", "VFXExplosion"],
            protoActionNames: ["Strike", "Crush", "NewStrike"],
            animationNames: ["Regurgitate", "RegurgitateFast"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Monster");
            Assert.True(GetPrivateField<WrapPanel>(editor, "_earthMonsterRegurgitateFields").IsVisible);
            Assert.True(GetPrivateField<Button>(editor, "_removeEarthMonsterRegurgitateButton").IsVisible);
            Assert.Equal("Regurgitate",
                GetPrivateField<AutoCompleteBox>(editor, "_earthMonsterRegurgitateAnimBox").Text);
            Assert.Equal("Enemy", GetPrivateField<ComboBox>(editor,
                "_earthMonsterSwallowRelationBox").SelectedItem);
            Assert.Equal("6", GetPrivateField<TextBox>(editor, "_minThrowDistanceBox").Text);
            Assert.Equal("EarthMonsterRegurgitate",
                GetPrivateField<AutoCompleteBox>(editor, "_earthMonsterRegurgitateSoundBox").Text);

            GetPrivateField<TextBox>(editor, "_earthMonsterAdditionalStrikesBox").Text = "3";
            GetPrivateField<AutoCompleteBox>(editor, "_earthMonsterStrikeActionBox").SelectedItem = "NewStrike";
            GetPrivateField<AutoCompleteBox>(editor, "_earthMonsterRegurgitateAnimBox").SelectedItem =
                "RegurgitateFast";
            GetPrivateField<ComboBox>(editor, "_earthMonsterSwallowRelationBox").SelectedItem = "Ally";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "Monster");
        Assert.Equal("3", power.Element("numberofadditionalstrikes")?.Value);
        Assert.Equal("NewStrike", power.Element("strikeaction")?.Value);
        Assert.Equal("RegurgitateFast", power.Element("regurgitateanim")?.Value);
        Assert.Equal("Ally", power.Element("swallowplayerrelation")?.Value);
        Assert.Equal("6", power.Element("previousstrikeavoidanceradius")?.Value);
        Assert.Equal("VFXExplosion", power.Element("landimpactprotounit")?.Value);
        Assert.Equal("EarthMonsterShockwave", power.Element("emergesoundset")?.Value);
        Assert.Equal("EarthMonsterChomp", power.Element("chompsoundset")?.Value);
        Assert.Equal("EarthMonsterRegurgitate", power.Element("regurgitatesoundset")?.Value);
    }

    [Fact]
    public void EarthMonsterOriginalRegurgitateDoesNotShowRemoveButton()
    {
        var original = XElement.Parse("""
            <power name="Monster" type="EarthMonster"><earthmonsterprotounit>EarthMonster</earthmonsterprotounit>
              <regurgitatetime>7.5</regurgitatetime><regurgitateunitstime>0.7</regurgitateunitstime>
              <regurgitateheight>5</regurgitateheight><regurgitateanim>Regurgitate</regurgitateanim>
              <regurgitateanimtime>3</regurgitateanimtime></power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Monster", true, original)], null,
            protoUnitNames: ["EarthMonster"], animationNames: ["Regurgitate"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Monster");
            Assert.True(GetPrivateField<WrapPanel>(editor, "_earthMonsterRegurgitateFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_removeEarthMonsterRegurgitateButton").IsVisible);
        });
    }

    [Fact]
    public void CageUnitUsesNestedTypesExclusiveResourcesVfxBonesAndSoundset()
    {
        using var workspace = new TestWorkspace(nameof(
            CageUnitUsesNestedTypesExclusiveResourcesVfxBonesAndSoundset));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="Purge" type="CageUnit">
              <resourcevaluecap>1400</resourcevaluecap>
              <resourcevaluemultiplier resourcetype="Favor">10</resourcevaluemultiplier>
              <targetfloatheight>5</targetfloatheight><killsimilartargetsinradius>true</killsimilartargetsinradius>
              <similarunittypes><unittype>Hero</unittype><unittype>AbstractArcherShip</unittype></similarunittypes>
              <killcagedafterend>true</killcagedafterend>
              <vfxarea>VFXPurgeArea</vfxarea><vfxmaintarget>VFXPurgeTarget</vfxmaintarget>
              <vfxmaintargetbone>Spine</vfxmaintargetbone>
              <vfxmarkanim>VFXPurgeMark</vfxmarkanim><vfxmarkanimbone>vfx_top</vfxmarkanimbone>
              <vfxmaintargetimpact>VFXPurgeImpactMain</vfxmaintargetimpact>
              <vfxadditionalimpact>VFXPurgeImpactAdditional</vfxadditionalimpact>
              <additionalimpactsoundset>PurgeImpact</additionalimpactsoundset>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXPurgeArea", "VFXPurgeTarget", "VFXPurgeMark",
                "VFXPurgeImpactMain", "VFXPurgeImpactAdditional"],
            unitTypeNames: ["Hero", "AbstractArcherShip", "InfantryLineUpgraded"],
            boneNames: ["Spine", "vfx_top"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Purge");
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_cageSimilarTypeChips").Children.Count);
            Assert.Single(GetPrivateField<WrapPanel>(editor, "_cageResourceMultiplierRows").Children);
            Assert.True(GetPrivateField<WrapPanel>(editor, "_cageVfxFields").IsVisible);
            Assert.Equal("Spine", GetPrivateField<AutoCompleteBox>(editor,
                "_cageVfxMainTargetBoneBox").Text);
            Assert.Equal("PurgeImpact", GetPrivateField<AutoCompleteBox>(editor,
                "_cageAdditionalImpactSoundBox").Text);
            GetPrivateField<TextBox>(editor, "_cageFloatHeightBox").Text = "6.5";
            GetPrivateField<CheckBox>(editor, "_cageKillAfterEndCheck").IsChecked = false;
            var addResource = GetPrivateField<Button>(editor, "_addCageResourceMultiplierButton");
            addResource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addResource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            addResource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(addResource.IsVisible);
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "Purge");
        Assert.Equal("6.5", power.Element("targetfloatheight")?.Value);
        Assert.Equal("false", power.Element("killcagedafterend")?.Value);
        Assert.Equal("true", power.Element("killsimilartargetsinradius")?.Value);
        Assert.Equal(2, power.Element("similarunittypes")?.Elements("unittype").Count());
        var multipliers = power.Elements("resourcevaluemultiplier").ToList();
        Assert.Equal(4, multipliers.Count);
        Assert.Equal(4, multipliers.Select(element => element.Attribute("resourcetype")?.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("VFXPurgeTarget", power.Element("vfxmaintarget")?.Value);
        Assert.Equal("Spine", power.Element("vfxmaintargetbone")?.Value);
        Assert.Equal("PurgeImpact", power.Element("additionalimpactsoundset")?.Value);
    }

    [Fact]
    public void CageUnitCustomOptionalsOpenAndOriginalRemoveButtonsStayHidden()
    {
        using var workspace = new TestWorkspace(nameof(
            CageUnitCustomOptionalsOpenAndOriginalRemoveButtonsStayHidden));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='CustomPurge' type='CageUnit' /></powersmod>");
        var custom = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXPurgeTarget"], unitTypeNames: ["Hero"], boneNames: ["Spine"]));
        AvaloniaTestHost.Run(() =>
        {
            custom.SetModifiedMode(true);
            custom.SelectPower("CustomPurge");
            Assert.True(GetPrivateField<CheckBox>(custom, "_cageKillAfterEndCheck").IsChecked);
            GetPrivateField<Button>(custom, "_addCageKillSimilarButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(custom, "_cageSimilarTypePicker").SelectedItem = "Hero";
            GetPrivateField<Button>(custom, "_addCageVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<Button>(custom, "_addCageVfxMainTargetButton").IsVisible);
            GetPrivateField<Button>(custom, "_addCageVfxMainTargetButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var vfxFlow = GetPrivateField<WrapPanel>(custom, "_cageVfxFields");
            var visibleVfxControls = vfxFlow.Children.Where(control => control.IsVisible).ToList();
            Assert.Same(GetPrivateField<StackPanel>(custom, "_cageVfxMainTargetFields"),
                visibleVfxControls[0]);
            Assert.All(visibleVfxControls.Skip(1).Take(4), control => Assert.IsType<Button>(control));
            Assert.Same(GetPrivateField<Button>(custom, "_removeCageVfxButton"),
                visibleVfxControls[^1]);
            GetPrivateField<AutoCompleteBox>(custom, "_cageVfxMainTargetBox").SelectedItem =
                "VFXPurgeTarget";
            GetPrivateField<AutoCompleteBox>(custom, "_cageVfxMainTargetBoneBox").SelectedItem = "Spine";
            GetPrivateField<Button>(custom, "_addCageSoundButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(custom, "_cageAdditionalImpactSoundBox").Text = "PurgeImpact";
        });
        Assert.True(AvaloniaTestHost.RunAsync(custom.SaveAsync));
        var saved = FindPower(XDocument.Load(path), "CustomPurge");
        Assert.Equal("true", saved.Element("killsimilartargetsinradius")?.Value);
        Assert.Equal("Hero", saved.Element("similarunittypes")?.Element("unittype")?.Value);
        Assert.Equal("VFXPurgeTarget", saved.Element("vfxmaintarget")?.Value);
        Assert.Equal("Spine", saved.Element("vfxmaintargetbone")?.Value);
        Assert.Equal("PurgeImpact", saved.Element("additionalimpactsoundset")?.Value);

        var original = new PowerDefinition("OriginalPurge", true, XElement.Parse("""
            <power name="OriginalPurge" type="CageUnit"><killsimilartargetsinradius>true</killsimilartargetsinradius>
              <similarunittypes><unittype>Hero</unittype></similarunittypes>
              <vfxarea>VFXPurgeTarget</vfxarea><additionalimpactsoundset>PurgeImpact</additionalimpactsoundset>
            </power>
            """));
        var readOnly = AvaloniaTestHost.Run(() => new PowerEditorView([original], null,
            protoUnitNames: ["VFXPurgeTarget"], unitTypeNames: ["Hero"]));
        AvaloniaTestHost.Run(() =>
        {
            readOnly.SelectPower("OriginalPurge");
            Assert.False(GetPrivateField<Button>(readOnly, "_removeCageKillSimilarButton").IsVisible);
            Assert.False(GetPrivateField<Button>(readOnly, "_removeCageVfxButton").IsVisible);
            Assert.False(GetPrivateField<Button>(readOnly, "_removeCageVfxAreaButton").IsVisible);
            Assert.False(GetPrivateField<Button>(readOnly, "_removeCageSoundButton").IsVisible);
        });
    }

    [Fact]
    public void RainUsesUnitTypeTargetsProtoVfxAndTechnologyEffectFields()
    {
        using var workspace = new TestWorkspace(nameof(
            RainUsesUnitTypeTargetsProtoVfxAndTechnologyEffectFields));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="RainCopy" type="Rain">
              <sfx>VFXRain</sfx><smallsfx>VFXRainSmall</smallsfx>
              <rainspotproto>AbstractSettlement</rainspotproto>
              <smallrainspotproto>AbstractFarm</smallrainspotproto>
              <effect type="WorkRate" action="Gather" unittype="AbstractFarm"
                      relativity="BasePercent" amount="2.5" altamount="1">
                <target type="ProtoUnit">EconomicUpgraded</target>
              </effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXRain", "VFXRainSmall", "EconomicUpgraded", "AbstractVillager"],
            unitTypeNames: ["AbstractSettlement", "AbstractFarm", "FishResource"],
            protoActionNames: ["Gather", "Trade"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("RainCopy");
            Assert.Equal("AbstractSettlement", GetPrivateField<AutoCompleteBox>(editor,
                "_rainLargeTargetBox").Text);
            Assert.Equal("VFXRainSmall", GetPrivateField<AutoCompleteBox>(editor,
                "_rainSmallVfxBox").Text);

            var rows = GetPrivateField<StackPanel>(editor, "_rainEffectRows");
            var card = Assert.IsType<Border>(Assert.Single(rows.Children));
            var controls = DescendantControls(card).ToList();
            Assert.Equal("WorkRate", controls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "type")).Text);
            Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "Unit");
            Assert.DoesNotContain(controls.OfType<TextBlock>(), block => block.Text == "Relativity");
            Assert.Equal("EconomicUpgraded", controls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.Equal("Gather", controls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "action")).Text);
            Assert.Equal("AbstractFarm", controls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "unittype")).Text);
            Assert.Equal("Multiply base", controls.OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "relativity")).SelectedItem);
            Assert.Equal("1", controls.OfType<TextBox>().Single(control =>
                Equals(control.Tag, "altamount")).Text);
            var details = Assert.IsType<WrapPanel>(Assert.IsType<StackPanel>(card.Child).Children.Last());
            var orderedTags = details.Children.SelectMany(DescendantControls)
                .Where(control => control.Tag is string)
                .Select(control => (string)control.Tag!)
                .OfType<string>().ToList();
            Assert.True(orderedTags.IndexOf("target") < orderedTags.IndexOf("action"));
            Assert.True(orderedTags.IndexOf("action") < orderedTags.IndexOf("relativity"));
            Assert.True(orderedTags.IndexOf("relativity") < orderedTags.IndexOf("amount"));
            Assert.True(orderedTags.IndexOf("amount") < orderedTags.IndexOf("unittype"));
            var relativityControl = controls.OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "relativity"));
            var amountControl = controls.OfType<TextBox>().Single(control => Equals(control.Tag, "amount"));
            Assert.Same(relativityControl.Parent, amountControl.Parent?.Parent);
            GetPrivateField<AutoCompleteBox>(editor, "_rainLargeTargetBox").SelectedItem = "FishResource";
            GetPrivateField<AutoCompleteBox>(editor, "_rainLargeVfxBox").SelectedItem = "VFXRainSmall";
            controls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "target"))
                .SelectedItem = "AbstractVillager";
            controls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "action"))
                .SelectedItem = "Trade";
            controls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "unittype"))
                .SelectedItem = "FishResource";
            controls.OfType<TextBox>().Single(control => Equals(control.Tag, "amount")).Text = "3.25";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "RainCopy");
        Assert.Equal("FishResource", power.Element("rainspotproto")?.Value);
        Assert.Equal("VFXRainSmall", power.Element("sfx")?.Value);
        var effect = Assert.Single(power.Elements("effect"));
        Assert.Equal("WorkRate", effect.Attribute("type")?.Value);
        Assert.Equal("Trade", effect.Attribute("action")?.Value);
        Assert.Equal("FishResource", effect.Attribute("unittype")?.Value);
        Assert.Equal("3.25", effect.Attribute("amount")?.Value);
        Assert.Equal("1", effect.Attribute("altamount")?.Value);
        Assert.Equal("AbstractVillager", effect.Element("target")?.Value);

        var original = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("OriginalRain", true, XElement.Parse("""
                <power name="OriginalRain" type="Rain"><effect type="WorkRate"
                  relativity="BasePercent" amount="1"><target type="ProtoUnit">Villager</target></effect></power>
                """))], null, protoUnitNames: ["Villager"]));
        AvaloniaTestHost.Run(() =>
        {
            original.SelectPower("OriginalRain");
            var originalCard = Assert.IsType<Border>(Assert.Single(
                GetPrivateField<StackPanel>(original, "_rainEffectRows").Children));
            Assert.DoesNotContain(DescendantControls(originalCard).OfType<Button>(), button =>
                button.Classes.Contains("remove-button"));
        });
    }

    [Fact]
    public void GeneralEclipseCeaseFireAndProsperityUseSharedEffectCardsAndVerifiedFields()
    {
        using var workspace = new TestWorkspace(nameof(
            GeneralEclipseCeaseFireAndProsperityUseSharedEffectCardsAndVerifiedFields));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="General" type="GeneralEffect" godpower="">
                <effect type="TechActivate"><target type="Tech">FlamingWeaponsActive</target></effect>
              </power>
              <power name="Moon" type="Eclipse">
                <builduptime>6</builduptime><shutdowntime>3</shutdowntime><lighting>gp_eclipse</lighting>
                <effect type="Damage" relativity="BasePercent" amount="1.2">
                  <target type="ProtoUnit">MythUnit</target>
                </effect>
              </power>
              <power name="Peace" type="CeaseFire">
                <vfxproto>VFXDoves</vfxproto><attachvfxto>AbstractSettlement</attachvfxto>
                <effect type="BuildPoints" relativity="BasePercent" amount="2">
                  <target type="ProtoUnit">LogicalTypeAffectedByCeaseFireBuildingSlow</target>
                </effect>
              </power>
              <power name="Gold" type="Prosperity">
                <largesfxprotoid>VFXProsperityLarge</largesfxprotoid>
                <smallsfxprotoid>VFXProsperitySmall</smallsfxprotoid>
                <abstracttype>GoldDropsite</abstracttype><abstracttype>Market</abstracttype>
                <effect type="WorkRate" action="Gather" unittype="GoldResource"
                        relativity="BasePercent" amount="1.5">
                  <target type="ProtoUnit">EconomicUpgraded</target>
                </effect>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXDoves", "VFXProsperityLarge", "VFXProsperitySmall",
                "MythUnit", "LogicalTypeAffectedByCeaseFireBuildingSlow", "EconomicUpgraded"],
            unitTypeNames: ["AbstractSettlement", "GoldDropsite", "Market", "Temple", "GoldResource"],
            protoActionNames: ["Gather"], techNames: ["FlamingWeaponsActive"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("General");
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            var generalCard = Assert.IsType<Border>(Assert.Single(
                GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children));
            var generalControls = DescendantControls(generalCard).ToList();
            Assert.Equal("TechActivate", generalControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "type")).Text);
            Assert.Equal("FlamingWeaponsActive", generalControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.Contains(generalControls.OfType<TextBlock>(), block => block.Text == "Tech");
            Assert.DoesNotContain(generalControls.OfType<TextBox>(), box => Equals(box.Tag, "amount"));

            editor.SelectPower("Moon");
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            Assert.Equal("6", GetPrivateField<TextBox>(editor, "_buildUpBox").Text);
            Assert.Equal("gp_eclipse", GetPrivateField<AutoCompleteBox>(editor, "_lightingBox").Text);
            Assert.Single(GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children);

            editor.SelectPower("Peace");
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            Assert.Equal("VFXDoves", GetPrivateField<AutoCompleteBox>(editor, "_ceaseFireVfxBox").Text);
            Assert.Equal("AbstractSettlement", GetPrivateField<AutoCompleteBox>(editor,
                "_ceaseFireAttachBox").Text);
            Assert.Single(GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children);

            editor.SelectPower("Gold");
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            Assert.Equal("VFXProsperityLarge", GetPrivateField<AutoCompleteBox>(editor,
                "_prosperityLargeVfxBox").Text);
            Assert.Equal(2, GetPrivateField<WrapPanel>(editor, "_prosperityTypeChips").Children.Count);
            GetPrivateField<AutoCompleteBox>(editor, "_prosperityTypePicker").SelectedItem = "Temple";
            Assert.Equal(3, GetPrivateField<WrapPanel>(editor, "_prosperityTypeChips").Children.Count);
            Assert.Single(GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children);
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_statusMessage").Text));
        var document = XDocument.Load(path);
        var eclipse = FindPower(document, "Moon");
        Assert.Equal("6", eclipse.Element("builduptime")?.Value);
        Assert.Equal("gp_eclipse", eclipse.Element("lighting")?.Value);
        Assert.Equal("VFXDoves", FindPower(document, "Peace").Element("vfxproto")?.Value);
        Assert.Equal(3, FindPower(document, "Gold").Elements("abstracttype").Count());
        Assert.Equal("Temple", FindPower(document, "Gold").Elements("abstracttype").Last().Value);
        Assert.Equal("Tech", FindPower(document, "General").Element("effect")?
            .Element("target")?.Attribute("type")?.Value);
    }

    [Fact]
    public void SwitchingEffectPowerTypesKeepsOnlyFieldsSupportedByTheNewType()
    {
        using var workspace = new TestWorkspace(nameof(
            SwitchingEffectPowerTypesKeepsOnlyFieldsSupportedByTheNewType));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="ToGeneral" type="CeaseFire">
                <placement>Full</placement><vfxproto>VFXDoves</vfxproto>
                <attachvfxto>AbstractSettlement</attachvfxto>
                <effect type="Damage" relativity="BasePercent" amount="1">
                  <target type="ProtoUnit">Unit</target>
                </effect>
              </power>
              <power name="ToBolt" type="Prosperity" godpower="stale">
                <largesfxprotoid>Large</largesfxprotoid><smallsfxprotoid>Small</smallsfxprotoid>
                <abstracttype>Market</abstracttype>
                <effect type="WorkRate" relativity="BasePercent" amount="2">
                  <target type="ProtoUnit">Worker</target>
                </effect>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("ToGeneral");
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "GeneralEffect";
            editor.SelectPower("ToBolt");
            GetPrivateField<AutoCompleteBox>(editor, "_powerTypeBox").SelectedItem = "Bolt";
        });
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_statusMessage").Text));

        var document = XDocument.Load(path);
        var general = FindPower(document, "ToGeneral");
        Assert.Equal("GeneralEffect", general.Attribute("type")?.Value);
        Assert.Equal("", general.Attribute("godpower")?.Value);
        Assert.Equal("Full", general.Element("placement")?.Value);
        Assert.NotNull(general.Element("effect"));
        Assert.Null(general.Element("vfxproto"));
        Assert.Null(general.Element("attachvfxto"));

        var bolt = FindPower(document, "ToBolt");
        Assert.Equal("Bolt", bolt.Attribute("type")?.Value);
        Assert.Null(bolt.Attribute("godpower"));
        Assert.Null(bolt.Element("largesfxprotoid"));
        Assert.Null(bolt.Element("smallsfxprotoid"));
        Assert.Null(bolt.Element("abstracttype"));
        Assert.Null(bolt.Element("effect"));
    }

    [Fact]
    public void SharedEffectCardsHandleAssignOnHitAndPlayerVisualTargets()
    {
        using var workspace = new TestWorkspace(nameof(
            SharedEffectCardsHandleAssignOnHitAndPlayerVisualTargets));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="LightningWeapons" type="GeneralEffect" godpower="">
              <effect type="AssignOnHitEffect" allactions="1" srcproto="ChargedModifyContainer"
                      srcaction="LightningWeaponAttack" effecttype="Stun">
                <target type="ProtoUnit">HumanSoldier</target>
              </effect>
              <effect type="UpdateVisuals" unittype="Ship"><target type="Player" /></effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["ChargedModifyContainer", "HumanSoldier"],
            unitTypeNames: ["Ship"], protoActionNames: ["LightningWeaponAttack"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("LightningWeapons");
            var cards = GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children
                .OfType<Border>().ToList();
            Assert.Equal(2, cards.Count);
            var assignControls = DescendantControls(cards[0]).ToList();
            Assert.Equal("All", assignControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "action")).Text);
            Assert.DoesNotContain(assignControls.OfType<CheckBox>(), control =>
                Equals(control.Tag, "allactions"));
            Assert.Equal("ChargedModifyContainer", assignControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "srcproto")).Text);
            Assert.Equal("LightningWeaponAttack", assignControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "srcaction")).Text);
            Assert.DoesNotContain(assignControls.OfType<TextBox>(), box => Equals(box.Tag, "amount"));

            var visualControls = DescendantControls(cards[1]).ToList();
            Assert.Equal("Player", visualControls.OfType<TextBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.Equal("Ship", visualControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "unittype")).Text);
            Assert.DoesNotContain(visualControls.OfType<TextBox>(), box => Equals(box.Tag, "amount"));
        });
    }

    [Fact]
    public void PowerEffectsUseBooleanPlayerVisualAndTechSpecificShapes()
    {
        using var workspace = new TestWorkspace(nameof(
            PowerEffectsUseBooleanPlayerVisualAndTechSpecificShapes));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="EffectShapes" type="GeneralEffect" godpower="">
              <effect type="CannotSnare"><target type="ProtoUnit">Unit</target></effect>
              <effect type="CantBeSlowed"><target type="ProtoUnit">Hero</target></effect>
              <effect type="Invulnerable"><target type="ProtoUnit">MythUnit</target></effect>
              <effect type="CanTargetButTakesNoDamage"><target type="ProtoUnit">Building</target></effect>
              <effect type="BaseSpeedRunAnim" relativity="BasePercent" amount="1.2"><target type="ProtoUnit">Unit</target></effect>
              <effect type="Armor" relativity="BasePercent" amount="1.1"><target type="ProtoUnit">Hero</target></effect>
              <effect type="EqualizeResources"><target type="Player" /></effect>
              <effect type="RevealEnemyLOS"><target type="Player" /></effect>
              <effect type="SharedLOS"><target type="Player" /></effect>
              <effect type="UpdateVisuals" unittype="Ship"><target type="Player" /></effect>
              <effect type="TechActivate"><target type="Tech">EclipseActive</target></effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Unit", "Hero", "MythUnit", "Building", "Ship"],
            unitTypeNames: ["Unit", "Hero", "MythUnit", "Building", "Ship"],
            techNames: ["EclipseActive"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("EffectShapes");
            var cards = GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children
                .OfType<Border>().ToDictionary(card => DescendantControls(card).OfType<AutoCompleteBox>()
                    .Single(control => Equals(control.Tag, "type")).Text!, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(11, cards.Count);

            foreach (var type in new[]
                     { "CannotSnare", "CantBeSlowed", "Invulnerable", "CanTargetButTakesNoDamage" })
            {
                var controls = DescendantControls(cards[type]).ToList();
                Assert.Contains(controls.OfType<AutoCompleteBox>(), control => Equals(control.Tag, "target"));
                Assert.Contains(controls.OfType<TextBlock>(), block =>
                    Equals(block.Tag, "operation") && block.Text == "Enable");
                Assert.DoesNotContain(controls.OfType<ComboBox>(), control => Equals(control.Tag, "state"));
                Assert.DoesNotContain(controls, control => Equals(control.Tag, "relativity"));
                Assert.DoesNotContain(controls, control => Equals(control.Tag, "amount"));
            }
            var speed = DescendantControls(cards["BaseSpeedRunAnim"]).ToList();
            Assert.Contains(speed, control => Equals(control.Tag, "target"));
            Assert.Contains(speed, control => Equals(control.Tag, "relativity"));
            Assert.Contains(speed, control => Equals(control.Tag, "amount"));

            var offeredTypes = Assert.IsAssignableFrom<IEnumerable<string>>(
                DescendantControls(cards["CannotSnare"]).OfType<AutoCompleteBox>()
                    .Single(control => Equals(control.Tag, "type")).ItemsSource);
            Assert.DoesNotContain("Armor", offeredTypes);
            Assert.Equal("Armor", DescendantControls(cards["Armor"]).OfType<AutoCompleteBox>()
                .Single(control => Equals(control.Tag, "type")).Text);

            AssertPlayerOperation("EqualizeResources", "Equalize");
            AssertPlayerOperation("RevealEnemyLOS", "Reveals LOS");
            AssertPlayerOperation("SharedLOS", "Reveals LOS");

            var visuals = DescendantControls(cards["UpdateVisuals"]).ToList();
            Assert.Equal("Player", visuals.OfType<TextBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.Contains(visuals.OfType<TextBlock>(), block => block.Text == "Updates Unit Visuals");
            Assert.Equal("Ship", visuals.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "unittype")).Text);
            Assert.DoesNotContain(visuals, control =>
                Equals(control.Tag, "relativity") || Equals(control.Tag, "amount"));

            var tech = DescendantControls(cards["TechActivate"]).ToList();
            Assert.Contains(tech.OfType<TextBlock>(), block => block.Text == "Tech");
            Assert.Contains(tech.OfType<TextBlock>(), block => block.Text == "Activates");
            Assert.Equal("EclipseActive", tech.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.DoesNotContain(tech, control =>
                Equals(control.Tag, "relativity") || Equals(control.Tag, "amount"));

            void AssertPlayerOperation(string type, string operation)
            {
                var controls = DescendantControls(cards[type]).ToList();
                Assert.Equal("Player", controls.OfType<TextBox>().Single(control =>
                    Equals(control.Tag, "target")).Text);
                Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == operation);
                Assert.DoesNotContain(controls, control =>
                    Equals(control.Tag, "relativity") || Equals(control.Tag, "amount"));
            }
        });
    }

    [Fact]
    public void OriginalEclipseWithAllOfficialEffectShapesOpensReadOnly()
    {
        var eclipse = XElement.Parse("""
            <power name="Eclipse" type="Eclipse" godpower="">
              <builduptime>6.0</builduptime><shutdowntime>3.0</shutdowntime>
              <lighting>gp_eclipse</lighting>
              <effect type="Damage" relativity="BasePercent" amount="1.20">
                <target type="ProtoUnit">MythUnit</target>
              </effect>
              <effect type="OnHitEffectRate" action="BeamAttack" effecttype="DamageOverTime"
                      dmgtype="all" relativity="BasePercent" amount="1.20">
                <target type="ProtoUnit">Petsuchos</target>
              </effect>
              <effect type="MaximumVelocity" amount="1.15" relativity="BasePercent">
                <target type="ProtoUnit">MythUnit</target>
              </effect>
              <effect type="WorkRate" action="AutoGather" amount="1.50" unittype="Favor"
                      relativity="BasePercent">
                <target type="ProtoUnit">AbstractMonument</target>
              </effect>
              <effect type="RechargeTime" amount="0.40" relativity="Percent">
                <target type="ProtoUnit">MythUnit</target>
              </effect>
              <effect type="ArmorVulnerability" armortype="Hack" amount="-0.10"
                      relativity="Percent"><target type="ProtoUnit">MythUnit</target></effect>
              <effect type="ArmorVulnerability" armortype="Pierce" amount="-0.10"
                      relativity="Percent"><target type="ProtoUnit">MythUnit</target></effect>
              <effect type="ArmorVulnerability" armortype="Crush" amount="-0.10"
                      relativity="Percent"><target type="ProtoUnit">MythUnit</target></effect>
              <effect type="TechActivate"><target type="Tech">EclipseActive</target></effect>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("Eclipse", true, eclipse)], null,
            protoUnitNames: ["MythUnit", "Petsuchos"],
            unitTypeNames: ["AbstractMonument"], protoActionNames: ["BeamAttack", "AutoGather"],
            techNames: ["EclipseActive"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SelectPower("Eclipse");
            Assert.Equal(9, GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children.Count);
            Assert.All(GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children,
                child => Assert.DoesNotContain(DescendantControls(child).OfType<Button>(), button =>
                    button.Classes.Contains("remove-button")));
        });
    }

    [Fact]
    public void RainEffectCardsAddRemoveAndUnknownTypesKeepOnlyStandardFields()
    {
        using var workspace = new TestWorkspace(nameof(
            RainEffectCardsAddRemoveAndUnknownTypesKeepOnlyStandardFields));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='CustomRain' type='Rain' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Villager"], unitTypeNames: ["AbstractFarm"],
            protoActionNames: ["Gather"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("CustomRain");
            GetPrivateField<Button>(editor, "_addRainEffectButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rows = GetPrivateField<StackPanel>(editor, "_rainEffectRows");
            var firstCard = Assert.IsType<Border>(Assert.Single(rows.Children));
            var initialControls = DescendantControls(firstCard).ToList();
            var type = initialControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "type"));
            Assert.Equal("", type.Text);
            Assert.DoesNotContain(initialControls, control => Equals(control.Tag, "target"));
            Assert.DoesNotContain(initialControls, control => Equals(control.Tag, "amount"));
            type.SelectedItem = "WorkRate";

            firstCard = Assert.IsType<Border>(Assert.Single(rows.Children));
            initialControls = DescendantControls(firstCard).ToList();
            initialControls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "action"))
                .SelectedItem = "Gather";
            initialControls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "unittype"))
                .SelectedItem = "AbstractFarm";
            type = DescendantControls(firstCard).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "type"));
            Assert.Contains("CannotSnare", Assert.IsAssignableFrom<IEnumerable<string>>(type.ItemsSource));
            type.SelectedItem = "CannotSnare";

            firstCard = Assert.IsType<Border>(Assert.Single(rows.Children));
            var tagged = DescendantControls(firstCard).Where(control => control.Tag is string)
                .Select(control => (string)control.Tag!).ToList();
            Assert.Contains("type", tagged);
            Assert.Contains("target", tagged);
            Assert.Contains("operation", tagged);
            Assert.DoesNotContain("relativity", tagged);
            Assert.DoesNotContain("amount", tagged);
            Assert.Contains("hidetooltip", tagged);
            Assert.Contains("norevert", tagged);
            Assert.Equal(5, tagged.Count);

            var remove = DescendantControls(firstCard).OfType<Button>().Single(button =>
                button.Classes.Contains("remove-button"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(rows.Children);
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Empty(FindPower(XDocument.Load(path), "CustomRain").Elements("effect"));
    }

    [Fact]
    public void RainPlayerEffectsUseDisabledPlayerTargetAndOptionalAltAmount()
    {
        using var workspace = new TestWorkspace(nameof(
            RainPlayerEffectsUseDisabledPlayerTargetAndOptionalAltAmount));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="PlayerRain" type="Rain">
              <effect type="Resource" relativity="Absolute" amount="2" resource="Food">
                <target type="Player" />
              </effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("PlayerRain");
            var rows = GetPrivateField<StackPanel>(editor, "_rainEffectRows");
            var card = Assert.IsType<Border>(Assert.Single(rows.Children));
            var controls = DescendantControls(card).ToList();
            Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "Player");
            var target = controls.OfType<TextBox>().Single(control => Equals(control.Tag, "target"));
            Assert.Equal("Player", target.Text);
            Assert.False(target.IsEnabled);
            Assert.DoesNotContain(controls.OfType<TextBlock>(), block => block.Text == "Relativity");

            var addAltAmount = controls.OfType<Button>().Single(button => Equals(button.Content, "Alt Amount"));
            Assert.Contains("add-component", addAltAmount.Classes);
            addAltAmount.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            card = Assert.IsType<Border>(Assert.Single(rows.Children));
            var altAmount = DescendantControls(card).OfType<TextBox>().Single(control =>
                Equals(control.Tag, "altamount"));
            Assert.Equal(2, altAmount.Margin.Right);
            altAmount.Text = "1.5";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var effect = Assert.Single(FindPower(XDocument.Load(path), "PlayerRain").Elements("effect"));
        Assert.Equal("1.5", effect.Attribute("altamount")?.Value);
        Assert.Equal("Player", effect.Element("target")?.Attribute("type")?.Value);
        Assert.True(string.IsNullOrWhiteSpace(effect.Element("target")?.Value));
    }

    [Fact]
    public void RainTacticAndActionEnableUseFixedEnableShapeAndInternalTacticCatalog()
    {
        using var workspace = new TestWorkspace(nameof(
            RainTacticAndActionEnableUseFixedEnableShapeAndInternalTacticCatalog));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="ToggleRain" type="Rain">
              <effect type="TacticEnable" tactic="Land">
                <target type="ProtoUnit" ignoreNature="">Soldier</target>
              </effect>
              <effect type="ActionEnable" action="Charge">
                <target type="ProtoUnit">Cavalry</target>
              </effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Soldier", "Cavalry"], protoActionNames: ["Charge"],
            tacticNames: ["Land", "Water"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("ToggleRain");
            var cards = GetPrivateField<StackPanel>(editor, "_rainEffectRows").Children
                .OfType<Border>().ToList();
            Assert.Equal(2, cards.Count);

            var tacticControls = DescendantControls(cards[0]).ToList();
            var tactic = tacticControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "tactic"));
            Assert.Equal("Land", tactic.Text);
            Assert.Contains("Water", tactic.ItemsSource!.Cast<string>());
            Assert.Contains(tacticControls.OfType<TextBlock>(), block =>
                Equals(block.Tag, "operation") && block.Text == "Enable");
            Assert.DoesNotContain(tacticControls.OfType<ComboBox>(), control =>
                Equals(control.Tag, "state"));
            Assert.DoesNotContain(tacticControls.OfType<TextBox>(), control =>
                Equals(control.Tag, "amount"));
            Assert.DoesNotContain(tacticControls.OfType<ComboBox>(), control =>
                Equals(control.Tag, "relativity"));
            Assert.True(tacticControls.OfType<CheckBox>().Single(control =>
                Equals(control.Content, "Not Nature")).IsChecked);

            var hideTooltip = tacticControls.OfType<CheckBox>().Single(control =>
                Equals(control.Tag, "hidetooltip"));
            var noRevert = tacticControls.OfType<CheckBox>().Single(control =>
                Equals(control.Tag, "norevert"));
            hideTooltip.IsChecked = true;
            noRevert.IsChecked = true;

            var actionControls = DescendantControls(cards[1]).ToList();
            Assert.Equal("Charge", actionControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "action")).Text);
            Assert.DoesNotContain(actionControls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "action")).ItemsSource!.Cast<string>(), value => value == "All");
            Assert.Contains(actionControls.OfType<TextBlock>(), block =>
                Equals(block.Tag, "operation") && block.Text == "Enable");
            Assert.DoesNotContain(actionControls.OfType<ComboBox>(), control =>
                Equals(control.Tag, "state"));
            Assert.DoesNotContain(actionControls.OfType<TextBox>(), control =>
                Equals(control.Tag, "amount"));
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var effects = FindPower(XDocument.Load(path), "ToggleRain").Elements("effect").ToList();
        Assert.Null(effects[0].Attribute("amount"));
        Assert.Null(effects[0].Attribute("relativity"));
        Assert.NotNull(effects[0].Attribute("hidetooltip"));
        Assert.NotNull(effects[0].Attribute("norevert"));
    }

    [Fact]
    public void RainDamageTypesDefaultToImplicitAllAndHeaderCheckboxesUseRequestedOrder()
    {
        using var workspace = new TestWorkspace(nameof(
            RainDamageTypesDefaultToImplicitAllAndHeaderCheckboxesUseRequestedOrder));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="TypedRain" type="Rain">
              <effect type="Damage" amount="1" relativity="BasePercent">
                <target type="ProtoUnit">Soldier</target>
              </effect>
              <effect type="ArmorVulnerability" armortype="Hack" amount="-0.1" relativity="Percent">
                <target type="ProtoUnit">Soldier</target>
              </effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Soldier"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("TypedRain");
            var cards = GetPrivateField<StackPanel>(editor, "_rainEffectRows").Children
                .OfType<Border>().ToList();
            Assert.Equal(2, cards.Count);

            var damageControls = DescendantControls(cards[0]).ToList();
            Assert.Equal("All", damageControls.OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "damagetype")).SelectedItem);
            var header = Assert.IsType<Grid>(Assert.IsType<StackPanel>(cards[0].Child).Children[0]);
            Assert.Equal(["Not Nature", "No Revert", "Hide Tooltip"],
                DescendantControls(header).OfType<CheckBox>()
                    .Select(control => control.Content!.ToString()!).ToArray());

            var armorType = DescendantControls(cards[1]).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "armortype"));
            Assert.Equal("Hack", armorType.SelectedItem);
            Assert.DoesNotContain("All", armorType.ItemsSource!.Cast<string>());
            var armorRelativity = DescendantControls(cards[1]).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "relativity"));
            Assert.DoesNotContain("Set to", armorRelativity.ItemsSource!.Cast<string>());
            armorType.SelectedItem = "Pierce";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var effects = FindPower(XDocument.Load(path), "TypedRain").Elements("effect").ToList();
        Assert.Null(effects[0].Attribute("damagetype"));
        Assert.Equal("Pierce", effects[1].Attribute("armortype")?.Value);
    }

    [Fact]
    public void PowerEffectsUseSubtypeSpecificAllSemanticsAndStableOnHitSelectors()
    {
        using var workspace = new TestWorkspace(nameof(
            PowerEffectsUseSubtypeSpecificAllSemanticsAndStableOnHitSelectors));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="EffectSemantics" type="GeneralEffect" godpower="">
              <effect type="Cost" resource="Food" relativity="BasePercent" amount="1">
                <target type="Tech">SomeTech</target>
              </effect>
              <effect type="Damage" action="Attack" relativity="BasePercent" amount="2">
                <target type="ProtoUnit">Soldier</target>
              </effect>
              <effect type="OnHitEffectProbability" relativity="BasePercent" amount="0.5">
                <target type="ProtoUnit">Soldier</target>
              </effect>
              <effect type="OnHitEffectRate" action="Attack" relativity="BasePercent" amount="1">
                <target type="ProtoUnit">Soldier</target>
              </effect>
              <effect type="EnemyAttachProtoUnit" attachProtoUnit="WarElephant" unittype="Destroyer">
                <target type="Player"></target>
              </effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Soldier", "WarElephant"], unitTypeNames: ["Destroyer"],
            protoActionNames: ["Attack"], techNames: ["SomeTech"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("EffectSemantics");
            var cards = GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children
                .OfType<Border>().ToList();

            var cost = DescendantControls(cards[0]).ToList();
            Assert.DoesNotContain(cost.OfType<Button>(), button =>
                Equals(button.Content, "Required tech"));
            var tech = cost.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "target"));
            Assert.Contains("All", tech.ItemsSource!.Cast<string>());
            tech.SelectedItem = "All";

            var damageAction = DescendantControls(cards[1]).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "action"));
            damageAction.SelectedItem = "All";

            DescendantControls(cards[2]).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "effecttype")).SelectedItem = "Stun";
            DescendantControls(cards[3]).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "effecttype")).SelectedItem = "DamageOverTime";

            var attach = DescendantControls(cards[4]).ToList();
            Assert.Equal("Player", attach.OfType<TextBox>().Single(control =>
                Equals(control.Tag, "target")).Text);
            Assert.Equal("WarElephant", attach.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "attachProtoUnit")).Text);
            Assert.Equal("Destroyer", attach.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "unittype")).Text);
            Assert.DoesNotContain(attach, control =>
                Equals(control.Tag, "relativity") || Equals(control.Tag, "amount"));
        });

        AvaloniaTestHost.Run(() =>
        {
            var rateCard = GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children
                .OfType<Border>().ElementAt(3);
            var damageType = DescendantControls(rateCard).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "dmgtype"));
            Assert.Equal("All", damageType.SelectedItem);
            damageType.SelectedItem = "Pierce";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var effects = FindPower(XDocument.Load(path), "EffectSemantics").Elements("effect").ToList();
        Assert.Equal("TechAll", effects[0].Element("target")?.Attribute("type")?.Value);
        Assert.True(string.IsNullOrWhiteSpace(effects[0].Element("target")?.Value));
        Assert.Null(effects[1].Attribute("action"));
        Assert.Null(effects[1].Attribute("allactions"));
        Assert.Equal("Stun", effects[2].Attribute("effecttype")?.Value);
        Assert.Equal("DamageOverTime", effects[3].Attribute("effecttype")?.Value);
        Assert.Equal("Pierce", effects[3].Attribute("dmgtype")?.Value);
        Assert.Equal("Player", effects[4].Element("target")?.Attribute("type")?.Value);
        Assert.Equal("WarElephant", effects[4].Attribute("attachProtoUnit")?.Value);
        Assert.Null(effects[4].Attribute("proto"));
        Assert.Equal("Destroyer", effects[4].Attribute("unittype")?.Value);
    }

    [Fact]
    public void EffectWithoutTypeBlocksSaveMarksSelectorAndEffectChildrenAreIndented()
    {
        using var workspace = new TestWorkspace(nameof(
            EffectWithoutTypeBlocksSaveMarksSelectorAndEffectChildrenAreIndented));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="InvalidEffect" type="GeneralEffect" godpower="">
              <effect><target type="ProtoUnit">Soldier</target></effect>
              <effect type="WorkRate" relativity="BasePercent" amount="1"><target type="ProtoUnit">Soldier</target></effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Soldier"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("InvalidEffect");
        });
        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Equal("Select a Type for the highlighted Effect before saving.",
                GetPrivateField<TextBlock>(editor, "_statusMessage").Text);
            var card = Assert.IsType<Border>(
                GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children[0]);
            Assert.Contains("invalid-field", DescendantControls(card).OfType<AutoCompleteBox>()
                .Single(control => Equals(control.Tag, "type")).Classes);
        });

        AvaloniaTestHost.Run(() =>
        {
            var card = Assert.IsType<Border>(
                GetPrivateField<StackPanel>(editor, "_sharedEffectRows").Children[0]);
            DescendantControls(card).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "type")).SelectedItem = "CannotSnare";
        });
        var savedSuccessfully = AvaloniaTestHost.RunAsync(editor.SaveAsync);
        Assert.True(savedSuccessfully, AvaloniaTestHost.Run(() => editor.StatusMessage));
        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("><target", saved, StringComparison.Ordinal);
        Assert.Matches("<effect type=\\\"WorkRate\\\"[^>]*>\\r?\\n\\s+<target type=\\\"ProtoUnit\\\">", saved);
    }

    [Fact]
    public void RainEffectFieldOrderMatchesTechnologyEditorForTrickleAndProtoActionAdd()
    {
        using var workspace = new TestWorkspace(nameof(
            RainEffectFieldOrderMatchesTechnologyEditorForTrickleAndProtoActionAdd));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="OrderedRain" type="Rain">
              <effect type="MinimumResourceTrickleRate" resource="Food"
                      relativity="Absolute" amount="1"><target type="Player" /></effect>
              <effect type="ProtoActionAdd" protoaction="Gather" unittype="Villager"
                      addToTactics="0"><target type="ProtoUnit">Worker</target></effect>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Worker", "Villager"], protoActionNames: ["Gather"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("OrderedRain");
            var cards = GetPrivateField<StackPanel>(editor, "_rainEffectRows").Children
                .OfType<Border>().ToList();
            Assert.Equal(2, cards.Count);

            static List<string> OrderedTags(Border card)
            {
                var details = Assert.IsType<WrapPanel>(
                    Assert.IsType<StackPanel>(card.Child).Children.Last());
                return details.Children.SelectMany(DescendantControls)
                    .Where(control => control.Tag is string)
                    .Select(control => (string)control.Tag!)
                    .OfType<string>().ToList();
            }

            Assert.Equal(["target", "resource", "relativity", "amount"], OrderedTags(cards[0]));
            var protoTags = OrderedTags(cards[1]);
            Assert.Equal(["target", "protoaction", "unittype", "addToTactics"],
                protoTags);
            Assert.DoesNotContain(DescendantControls(cards[1]), control =>
                Equals(control.Tag, "amount") || Equals(control.Tag, "relativity"));
        });
    }

    [Fact]
    public void UnitModifyAndSingleModifyUseRepeatedVerifiedUnitModifyCards()
    {
        using var workspace = new TestWorkspace(nameof(
            UnitModifyAndSingleModifyUseRepeatedVerifiedUnitModifyCards));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="TailwindCopy" type="UnitModify">
                <unitmodify apply="Multiply" amount="1.5" nostack="" nostackignorepuid=""
                  age="HeroicAge" excludetypes="Hero|AbstractTitan">Speed</unitmodify>
                <unitmodify apply="Add" amount="25" damagetype="Pierce">DamageSpecific</unitmodify>
              </power>
              <power name="ShieldCopy" type="SingleModify">
                <killoneffectend>true</killoneffectend>
                <unitmodify apply="Add" amount="300" age="ArchaicAge" damagetype="Hack">ArmorSpecific</unitmodify>
              </power>
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["Ajax"], unitTypeNames: ["Hero", "AbstractTitan", "MythUnit"]));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("TailwindCopy"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.Empty(GetPrivateField<StackPanel>(editor, "_otherSpecificAttributeRows").Children);
            var unitModifyKillOnEnd = GetPrivateField<CheckBox>(editor,
                "_unitModifyKillOnEffectEndCheck");
            Assert.True(unitModifyKillOnEnd.IsVisible);
            Assert.False(unitModifyKillOnEnd.IsChecked);
            unitModifyKillOnEnd.IsChecked = true;
            var rows = GetPrivateField<StackPanel>(editor, "_unitModifyRows");
            Assert.Equal(2, rows.Children.Count);
            var first = Assert.IsType<Border>(rows.Children[0]);
            var controls = DescendantControls(first).ToList();
            Assert.Equal("Multiply", controls.OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "apply")).SelectedItem);
            Assert.Equal("Speed", controls.OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "modifytype")).Text);
            Assert.Equal("1.5", controls.OfType<TextBox>().Single(control =>
                Equals(control.Tag, "amount")).Text);
            Assert.Equal("HeroicAge", controls.OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "age")).SelectedItem);
            var ageControl = controls.OfType<ComboBox>().Single(control => Equals(control.Tag, "age"));
            var noStackControl = controls.OfType<CheckBox>().Single(control => Equals(control.Tag, "nostack"));
            Assert.True(noStackControl.IsChecked);
            Assert.True(controls.OfType<CheckBox>().Single(control =>
                Equals(control.Tag, "nostackignorepuid")).IsChecked);
            Assert.True(controls.IndexOf(ageControl) < controls.IndexOf(noStackControl));
            Assert.Equal(2, controls.OfType<Button>().Count(button =>
                button.Classes.Contains("chip-remove-button")));

            controls.OfType<ComboBox>().Single(control => Equals(control.Tag, "apply"))
                .SelectedItem = "Set";
            controls.OfType<AutoCompleteBox>().Single(control => Equals(control.Tag, "modifytype"))
                .SelectedItem = "Redirect Damage";
            controls.OfType<TextBox>().Single(control => Equals(control.Tag, "amount")).Text = "0.4";
            Assert.Equal("0.4", GetPrivateField<XElement?>(editor, "_currentEntry")!
                .Elements("unitmodify").First().Attribute("amount")?.Value);
            controls.OfType<CheckBox>().Single(control => Equals(control.Tag, "nostackignorepuid"))
                .IsChecked = false;

            var second = Assert.IsType<Border>(rows.Children[1]);
            Assert.Equal("Pierce", DescendantControls(second).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "damagetype")).SelectedItem);
            DescendantControls(second).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "damagetype")).SelectedItem = "Crush";
            var secondButtons = DescendantControls(second).OfType<Button>().ToList();
            secondButtons.Single(button => Equals(button.Content, "Age"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            second = Assert.IsType<Border>(rows.Children[1]);
            DescendantControls(second).OfType<ComboBox>().Single(control => Equals(control.Tag, "age"))
                .SelectedItem = "WonderAge";
            DescendantControls(second).OfType<Button>().Single(button =>
                    Equals(button.Content, "Exclude Type"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            second = Assert.IsType<Border>(rows.Children[1]);
            DescendantControls(second).OfType<AutoCompleteBox>().Single(control =>
                    Equals(control.Tag, "excludetypes"))
                .SelectedItem = "MythUnit";

            GetPrivateField<Button>(editor, "_addUnitModifyButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, rows.Children.Count);
            var added = Assert.IsType<Border>(rows.Children[2]);
            DescendantControls(added).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "modifytype")).SelectedItem = "Speed";
        });

        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("ShieldCopy"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.True(GetPrivateField<CheckBox>(editor,
                "_unitModifyKillOnEffectEndCheck").IsChecked);
            GetPrivateField<CheckBox>(editor, "_unitModifyKillOnEffectEndCheck").IsChecked = false;
            Assert.Null(GetPrivateField<XElement?>(editor, "_currentEntry")!
                .Element("killoneffectend"));
            GetPrivateField<CheckBox>(editor, "_unitModifyKillOnEffectEndCheck").IsChecked = true;
            Assert.Single(GetPrivateField<StackPanel>(editor, "_unitModifyRows").Children);
            var armorCard = Assert.IsType<Border>(Assert.Single(
                GetPrivateField<StackPanel>(editor, "_unitModifyRows").Children));
            Assert.Equal("Hack", DescendantControls(armorCard).OfType<ComboBox>().Single(control =>
                Equals(control.Tag, "damagetype")).SelectedItem);
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync),
            AvaloniaTestHost.Run(() => editor.StatusMessage));
        var saved = FindPower(XDocument.Load(path), "TailwindCopy").Elements("unitmodify").ToList();
        Assert.Equal(3, saved.Count);
        Assert.Equal("Shield", saved[0].Value);
        Assert.Equal("Set", saved[0].Attribute("apply")?.Value);
        Assert.Equal("0.4", saved[0].Attribute("amount")?.Value);
        Assert.NotNull(saved[0].Attribute("nostack"));
        Assert.Null(saved[0].Attribute("nostackignorepuid"));
        Assert.Equal("WonderAge", saved[1].Attribute("age")?.Value);
        Assert.Equal("Crush", saved[1].Attribute("damagetype")?.Value);
        Assert.Equal("MythUnit", saved[1].Attribute("excludetypes")?.Value);
        Assert.Equal("Add", saved[2].Attribute("apply")?.Value);
        Assert.Equal("0", saved[2].Attribute("amount")?.Value);
        Assert.Equal("Speed", saved[2].Value);
        Assert.Equal("true", FindPower(XDocument.Load(path), "TailwindCopy")
            .Element("killoneffectend")?.Value);
        Assert.Equal("true", FindPower(XDocument.Load(path), "ShieldCopy")
            .Element("killoneffectend")?.Value);

        var original = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("OriginalTailwind", true, XElement.Parse("""
                <power name="OriginalTailwind" type="UnitModify">
                  <unitmodify apply="Multiply" amount="1.5">Speed</unitmodify>
                </power>
                """))], null));
        AvaloniaTestHost.RunAsync(() => original.SelectPowerAsync("OriginalTailwind"));
        AvaloniaTestHost.Run(() =>
        {
            Assert.False(GetPrivateField<Button>(original, "_addUnitModifyButton").IsVisible);
            var card = Assert.IsType<Border>(Assert.Single(
                GetPrivateField<StackPanel>(original, "_unitModifyRows").Children));
            Assert.DoesNotContain(DescendantControls(card).OfType<Button>(), button =>
                button.Classes.Contains("remove-button") || button.Classes.Contains("add-component"));
        });
    }

    [Fact]
    public void UnitModifyVfxAndNewMoonResearchUseTheirOwnXmlTags()
    {
        using var workspace = new TestWorkspace(nameof(UnitModifyVfxAndNewMoonResearchUseTheirOwnXmlTags));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="TailwindCopy" type="UnitModify">
                <vfxproto>VFXTailwind</vfxproto>
                <vfxattachbone>Root</vfxattachbone>
                <birthvfx>VFXTailwindBirth</birthvfx>
                <unitmodify apply="Multiply" amount="1.5">Speed</unitmodify>
              </power>
              <power name="MoonCopy" type="NewMoon">
                <maxfreeresearches>3</maxfreeresearches>
                <freeresearchrate>30</freeresearchrate>
                <modelattachment>VFXNewMoon</modelattachment>
                <modelattachmentbone>vfx_top</modelattachmentbone>
                <unitmodify apply="Multiply" amount="5">MilitaryTrainingRate</unitmodify>
              </power>
              <power name="ShieldCopy" type="SingleModify" />
            </powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXTailwind", "VFXTailwindBirth", "VFXNewMoon", "VFXShield", "VFXShieldBirth"],
            boneNames: ["Root", "vfx_top", "Spine"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("TailwindCopy");
            Assert.Equal("VFXTailwind", GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxTargetBox").Text);
            Assert.Equal("Root", GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxBoneBox").Text);
            Assert.Equal("VFXTailwindBirth", GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxBirthBox").Text);
            Assert.True(GetPrivateField<Control>(editor, "_modifyVfxBirthFields").IsVisible);
            Assert.Equal(2, GetPrivateField<Button>(editor, "_removeModifyVfxBirthButton").Margin.Left);
            Assert.Equal(VerticalAlignment.Center,
                GetPrivateField<Button>(editor, "_removeModifyVfxButton").VerticalAlignment);

            editor.SelectPower("MoonCopy");
            Assert.Equal("3", GetPrivateField<TextBox>(editor, "_newMoonMaxFreeBox").Text);
            Assert.Equal("30", GetPrivateField<TextBox>(editor, "_newMoonRateBox").Text);
            Assert.Equal("VFXNewMoon", GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxTargetBox").Text);
            Assert.Equal("vfx_top", GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxBoneBox").Text);
            Assert.False(GetPrivateField<Control>(editor, "_modifyVfxBirthFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_addModifyVfxBirthButton").IsVisible);
            Assert.Single(GetPrivateField<StackPanel>(editor, "_unitModifyRows").Children);
            Assert.True(GetPrivateField<Grid>(editor, "_newMoonResearchRow").IsVisible);
            GetPrivateField<TextBox>(editor, "_newMoonMaxFreeBox").Text = "4";
            GetPrivateField<TextBox>(editor, "_newMoonRateBox").Text = "25.5";
            Assert.Equal("4", GetPrivateField<XElement?>(editor, "_currentEntry")!
                .Element("maxfreeresearches")?.Value);

            editor.SelectPower("ShieldCopy");
            GetPrivateField<Button>(editor, "_addModifyVfxButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<Button>(editor, "_addModifyVfxTargetButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxTargetBox").SelectedItem = "VFXShield";
            GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxBoneBox").SelectedItem = "Spine";
            GetPrivateField<Button>(editor, "_addModifyVfxBirthButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetPrivateField<AutoCompleteBox>(editor, "_modifyVfxBirthBox").SelectedItem = "VFXShieldBirth";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = XDocument.Load(path);
        var moon = FindPower(saved, "MoonCopy");
        Assert.Equal("4", moon.Element("maxfreeresearches")?.Value);
        Assert.Equal("25.5", moon.Element("freeresearchrate")?.Value);
        Assert.Equal("VFXNewMoon", moon.Element("modelattachment")?.Value);
        Assert.Equal("vfx_top", moon.Element("modelattachmentbone")?.Value);
        Assert.Null(moon.Element("vfxproto"));
        Assert.Null(moon.Element("birthvfx"));
        var shield = FindPower(saved, "ShieldCopy");
        Assert.Equal("VFXShield", shield.Element("vfxproto")?.Value);
        Assert.Equal("Spine", shield.Element("vfxattachbone")?.Value);
        Assert.Equal("VFXShieldBirth", shield.Element("birthvfx")?.Value);
        Assert.Null(shield.Element("modelattachment"));
    }

    [Fact]
    public void SwitchingEffectsEditsReindexAndDeleteTheirOwnedStrings()
    {
        using var workspace = new TestWorkspace(nameof(SwitchingEffectsEditsReindexAndDeleteTheirOwnedStrings));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod>
              <power name="Cycle" type="SwitchingEffects">
                <tiertype>TotalResources</tiertype>
                <progresstype>Default</progresstype>
                <timerrolloverid>STR_SWITCHING_EFFECT_CYCLE_PROG</timerrolloverid>
                <skipfirstnotification></skipfirstnotification>
                <modelattachproto>Hero</modelattachproto>
                <effects>
                  <displaynameid>STR_SWITCHING_EFFECT_CYCLE_NAME</displaynameid>
                  <icon>icons\first.png</icon>
                  <rolloverid>STR_SWITCHING_EFFECT_CYCLE_LR</rolloverid>
                  <extratooltips><string>STR_SWITCHING_EFFECT_CYCLE_TOOLTIP</string></extratooltips>
                  <notificationmessageid>STR_SWITCHING_EFFECT_CYCLE_NOTIF</notificationmessageid>
                  <notificationsound>FirstSound</notificationsound>
                  <resourcetierreq type="Favor">10</resourcetierreq>
                  <effect type="CannotSnare"><target type="ProtoUnit">Hero</target></effect>
                </effects>
                <effects>
                  <displaynameid>STR_SWITCHING_EFFECT_CYCLE_NAME_1</displaynameid>
                  <icon>icons\second.png</icon>
                  <rolloverid>STR_SWITCHING_EFFECT_CYCLE_LR_1</rolloverid>
                  <extratooltips><string>STR_SWITCHING_EFFECT_CYCLE_TOOLTIP_1</string></extratooltips>
                  <notificationmessageid>STR_SWITCHING_EFFECT_CYCLE_NOTIF_1</notificationmessageid>
                  <notificationsound>SecondSound</notificationsound>
                  <resourcetierreq type="Food">20</resourcetierreq>
                  <effect type="Invulnerable"><target type="ProtoUnit">Villager</target></effect>
                </effects>
              </power>
            </powersmod>
            """);
        var stringsPath = workspace.WriteText("stringmods.txt", """
            Language = "English"
            ID = "STR_SWITCHING_EFFECT_CYCLE_PROG"      ; Str = "Timer"
            ID = "STR_SWITCHING_EFFECT_CYCLE_NAME"      ; Str = "First name"
            ID = "STR_SWITCHING_EFFECT_CYCLE_LR"        ; Str = "First rollover"
            ID = "STR_SWITCHING_EFFECT_CYCLE_TOOLTIP"   ; Str = "First tooltip"
            ID = "STR_SWITCHING_EFFECT_CYCLE_NOTIF"     ; Str = "First notification"
            ID = "STR_SWITCHING_EFFECT_CYCLE_NAME_1"    ; Str = "Second name"
            ID = "STR_SWITCHING_EFFECT_CYCLE_LR_1"      ; Str = "Second rollover"
            ID = "STR_SWITCHING_EFFECT_CYCLE_TOOLTIP_1" ; Str = "Second tooltip"
            ID = "STR_SWITCHING_EFFECT_CYCLE_NOTIF_1"   ; Str = "Second notification"
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            resolveStringAsync: id => Task.FromResult(
                StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().GetValueOrDefault(id)),
            saveStringsAsync: (updates, removals) =>
            {
                var document = StringTableDocument.Parse(File.ReadAllText(stringsPath));
                File.WriteAllText(stringsPath, document.Apply(updates, removals, StringTableSection.Powers));
                return Task.CompletedTask;
            },
            iconPaths: [@"icons\first.png", @"icons\second.png"], modStringsPath: stringsPath,
            protoUnitNames: ["Hero", "Villager"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("Cycle");
            Assert.Equal("TotalResources", GetPrivateField<ComboBox>(editor, "_switchingTierTypeBox").SelectedItem);
            Assert.Equal("Default", GetPrivateField<ComboBox>(editor, "_switchingProgressTypeBox").SelectedItem);
            Assert.Equal(2, GetPrivateField<StackPanel>(editor, "_switchingEffectContainers").Children.Count);
            var timerRollover = GetPrivateField<TextBox>(editor, "_switchingTimerRolloverBox");
            Assert.True(timerRollover.AcceptsReturn);
            Assert.Equal(TextWrapping.Wrap, timerRollover.TextWrapping);
            Assert.Equal(VerticalAlignment.Center,
                GetPrivateField<Button>(editor, "_removeSwitchingTimerRolloverButton").VerticalAlignment);
            var progressRow = GetPrivateField<Grid>(editor, "_switchingProgressRow");
            Assert.Equal(VerticalAlignment.Center,
                progressRow.Children.OfType<TextBlock>().Single().VerticalAlignment);
            var initialFirstCard = Assert.IsType<Border>(
                GetPrivateField<StackPanel>(editor, "_switchingEffectContainers").Children[0]);
            Assert.Equal(250, DescendantControls(initialFirstCard).OfType<TextBox>()
                .Single(control => control.Text == "First name").Width);
            var extraRemove = DescendantControls(initialFirstCard).OfType<Button>().Single(button =>
                button.Tag is XElement element && element.Name.LocalName == "string");
            Assert.Equal(2, extraRemove.Margin.Left);
            Assert.Equal(VerticalAlignment.Center, extraRemove.VerticalAlignment);
            var sound = DescendantControls(initialFirstCard).OfType<AutoCompleteBox>().Single(control =>
                Equals(control.Tag, "notificationsound"));
            Assert.Equal(VerticalAlignment.Center, Assert.IsType<StackPanel>(sound.Parent).VerticalAlignment);

            GetPrivateField<Button>(editor, "_addSwitchingEffectContainerButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var current = GetPrivateField<XElement>(editor, "_currentEntry");
            Assert.Equal(3, current.Elements("effects").Count());
            Assert.Equal("STR_SWITCHING_EFFECT_CYCLE_NAME_2",
                current.Elements("effects").Last().Element("displaynameid")?.Value);
            var addedCard = Assert.IsType<Border>(
                GetPrivateField<StackPanel>(editor, "_switchingEffectContainers").Children[2]);
            var addedGrid = Assert.IsType<Grid>(addedCard.Child);
            Assert.IsType<Button>(addedGrid.Children.Single(control => Grid.GetColumn(control) == 1))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, current.Elements("effects").Count());

            GetPrivateField<ComboBox>(editor, "_switchingTierTypeBox").SelectedItem = "CombatXP";
            GetPrivateField<ComboBox>(editor, "_switchingProgressTypeBox").SelectedItem = "Hide";
            Assert.Null(current.Element("timerrolloverid"));
            Assert.False(GetPrivateField<Control>(editor, "_switchingTimerRolloverFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor, "_addSwitchingTimerRolloverButton").IsVisible);
            GetPrivateField<ComboBox>(editor, "_switchingProgressTypeBox").SelectedItem = "Default";
            Assert.True(GetPrivateField<Button>(editor, "_addSwitchingTimerRolloverButton").IsVisible);
            Assert.Equal(VerticalAlignment.Center,
                GetPrivateField<Button>(editor, "_addSwitchingTimerRolloverButton").VerticalAlignment);
            GetPrivateField<ComboBox>(editor, "_switchingProgressTypeBox").SelectedItem = "Hide";

            var firstCard = Assert.IsType<Border>(
                GetPrivateField<StackPanel>(editor, "_switchingEffectContainers").Children[0]);
            var cardGrid = Assert.IsType<Grid>(firstCard.Child);
            var remove = Assert.IsType<Button>(cardGrid.Children.Single(control => Grid.GetColumn(control) == 1));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.Null(AvaloniaTestHost.Run(() =>
            GetPrivateField<XElement>(editor, "_currentEntry").Element("timerrolloverid")));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var saved = FindPower(XDocument.Load(path), "Cycle");
        Assert.Equal("CombatXP", saved.Element("tiertype")?.Value);
        Assert.NotNull(saved.Element("hidewheelview"));
        Assert.Null(saved.Element("progresstype"));
        Assert.Null(saved.Element("timerrolloverid"));
        var remaining = Assert.Single(saved.Elements("effects"));
        Assert.Equal("STR_SWITCHING_EFFECT_CYCLE_NAME", remaining.Element("displaynameid")?.Value);
        Assert.Equal("STR_SWITCHING_EFFECT_CYCLE_LR", remaining.Element("rolloverid")?.Value);
        Assert.Equal("STR_SWITCHING_EFFECT_CYCLE_NOTIF", remaining.Element("notificationmessageid")?.Value);
        Assert.Equal("STR_SWITCHING_EFFECT_CYCLE_TOOLTIP",
            remaining.Element("extratooltips")?.Element("string")?.Value);
        Assert.Equal("0", remaining.Element("combatxptierreq")?.Value);
        Assert.Null(remaining.Element("resourcetierreq"));

        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("Second name", strings["STR_SWITCHING_EFFECT_CYCLE_NAME"]);
        Assert.Equal("Second rollover", strings["STR_SWITCHING_EFFECT_CYCLE_LR"]);
        Assert.Equal("Second tooltip", strings["STR_SWITCHING_EFFECT_CYCLE_TOOLTIP"]);
        Assert.Equal("Second notification", strings["STR_SWITCHING_EFFECT_CYCLE_NOTIF"]);
        Assert.DoesNotContain("STR_SWITCHING_EFFECT_CYCLE_NAME_1", strings.Keys);
        Assert.DoesNotContain("STR_SWITCHING_EFFECT_CYCLE_PROG", strings.Keys);
        Assert.DoesNotContain(strings.Keys,
            id => id.EndsWith("_2", StringComparison.OrdinalIgnoreCase));

        AvaloniaTestHost.RunAsync(editor.DeleteCurrentPowerAsync);
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Empty(XDocument.Load(path).Descendants("power"));
        Assert.DoesNotContain(StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().Keys,
            id => id.StartsWith("STR_SWITCHING_EFFECT_CYCLE_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OriginalSwitchingEffectsShowsLegacyBushidoContainersReadOnly()
    {
        var source = XElement.Parse("""
            <power name="BushidoAmaterasu" type="SwitchingEffects">
              <timerrolloverid>STR_ABILITY_BUSHIDO_PROG</timerrolloverid>
              <tiertype>CombatXP</tiertype>
              <progresstype>SpawnReward</progresstype>
              <effects>
                <effect type="ResourceTrickleRate" amount="0.0" resource="Gold" relativity="Absolute" hidetooltip=""><target type="Player"></target></effect>
                <combatxptierreq>0</combatxptierreq>
                <icon>japanese\static_color\veterancy\amaterasu_tier_0_icon.png</icon>
                <displaynameid>STR_ABILITY_BUSHIDO_0</displaynameid>
                <rolloverid>STR_ABILITY_BUSHIDO_AMATERASU</rolloverid>
              </effects>
              <effects>
                <effect type="TechActivate" norevert=""><target type="Tech">MediumDojoSoldiers</target></effect>
                <combatxptierreq>1</combatxptierreq>
                <icon>japanese\static_color\veterancy\amaterasu_tier_1_icon.png</icon>
                <displaynameid>STR_ABILITY_BUSHIDO_1</displaynameid>
                <rolloverid>STR_ABILITY_BUSHIDO_AMATERASU</rolloverid>
                <notificationsound>GodBlessingCircleComplete</notificationsound>
                <notificationmessageid>STR_ABILITY_BUSHIDO_AMATERASU_NOTIFICATION_1</notificationmessageid>
                <extratooltips><string>STR_ABILITY_BUSHIDO_TROOPS_CHAMPION</string></extratooltips>
              </effects>
            </power>
            """);
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STR_ABILITY_BUSHIDO_PROG"] = "Bushido progress",
            ["STR_ABILITY_BUSHIDO_0"] = "Initiate",
            ["STR_ABILITY_BUSHIDO_1"] = "Veteran",
            ["STR_ABILITY_BUSHIDO_AMATERASU"] = "Amaterasu blessing",
            ["STR_ABILITY_BUSHIDO_AMATERASU_NOTIFICATION_1"] = "Tier reached",
            ["STR_ABILITY_BUSHIDO_TROOPS_CHAMPION"] = "Champion troops"
        };
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("BushidoAmaterasu", true, source)], null,
            resolveStringAsync: id => Task.FromResult(strings.GetValueOrDefault(id)),
            iconPaths:
            [
                @"japanese\static_color\veterancy\amaterasu_tier_0_icon.png",
                @"japanese\static_color\veterancy\amaterasu_tier_1_icon.png"
            ], techNames: ["MediumDojoSoldiers"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(false);
            editor.SelectPower("BushidoAmaterasu");
            Assert.Equal("CombatXP", GetPrivateField<ComboBox>(editor, "_switchingTierTypeBox").SelectedItem);
            Assert.Equal("SpawnReward", GetPrivateField<ComboBox>(editor, "_switchingProgressTypeBox").SelectedItem);
            Assert.True(GetPrivateField<Grid>(editor, "_switchingTypeRow").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_switchingProgressRow").IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_switchingEffectsRow").IsVisible);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_specificAttributeRows"),
                GetPrivateField<Grid>(editor, "_switchingEffectsRow").Parent);
            var containers = GetPrivateField<StackPanel>(editor, "_switchingEffectContainers");
            Assert.Equal(2, containers.Children.Count);
            var resolvedTexts = DescendantControls(containers).OfType<TextBox>()
                .Select(box => box.Text).ToList();
            Assert.Contains("Initiate", resolvedTexts);
            Assert.Contains("Veteran", resolvedTexts);
            Assert.Contains("Champion troops", resolvedTexts);
            foreach (var text in new[] { "Amaterasu blessing", "Tier reached" })
            {
                var box = DescendantControls(containers).OfType<TextBox>().First(control => control.Text == text);
                Assert.True(box.AcceptsReturn);
                Assert.Equal(TextWrapping.Wrap, box.TextWrapping);
                Assert.Equal(32, box.MinHeight);
            }
            var icons = DescendantControls(containers).OfType<AssetPathEditor>().ToList();
            Assert.Equal(2, icons.Count);
            Assert.All(icons, icon => Assert.EndsWith(".png", icon.FullValue, StringComparison.OrdinalIgnoreCase));
            Assert.All(icons, icon => Assert.EndsWith("_icon.png", icon.CompactPresenter.Text,
                StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void EffectInAreaUsesProtoActionOnHitCardsAndPreservesNestedFilters()
    {
        using var workspace = new TestWorkspace(nameof(
            EffectInAreaUsesProtoActionOnHitCardsAndPreservesNestedFilters));
        var path = workspace.WriteText("powers_mods.xml", """
            <powersmod><power name="CorruptedGroundCopy" type="EffectInArea">
              <repeatdelay>1.0</repeatdelay><areavfx>VFXCorruptedGroundBirth</areavfx>
              <onhiteffect type="Reincarnation" proto="TlacanexquimilliCG" duration="15">
                <target attacktype="Unit"/><target ignoretype="AbstractSiegeWeapon"/>
              </onhiteffect>
              <onhiteffect type="DamageOverTime" duration="15"><damage type="Divine">1</damage>
                <target attacktype="Unit"/><target ignoretype="AbstractSiegeWeapon"/>
              </onhiteffect>
              <onhiteffect type="Attach" proto="VFXCorruptedGroundCorruption" attachbone="spine" duration="15"/>
            </power></powersmod>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path,
            protoUnitNames: ["VFXCorruptedGroundBirth", "TlacanexquimilliCG", "VFXCorruptedGroundCorruption"],
            unitTypeNames: ["Unit", "AbstractSiegeWeapon"], boneNames: ["spine"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("CorruptedGroundCopy");
            var mainRow = GetPrivateField<Grid>(editor, "_effectInAreaMainRow");
            Assert.True(mainRow.IsVisible);
            Assert.True(GetPrivateField<Grid>(editor, "_effectInAreaOnHitRow").IsVisible);
            var mainFlow = Assert.IsType<WrapPanel>(mainRow.Children[1]);
            Assert.Contains(GetPrivateField<AutoCompleteBox>(editor, "_effectInAreaAreaVfxBox"),
                Assert.IsType<StackPanel>(mainFlow.Children[0]).Children);
            Assert.Same(GetPrivateField<StackPanel>(editor, "_effectInAreaRepeatDelayFields"),
                mainFlow.Children[1]);
            Assert.True(GetPrivateField<StackPanel>(editor,
                "_effectInAreaRepeatDelayFields").IsVisible);
            Assert.False(GetPrivateField<Button>(editor,
                "_addEffectInAreaRepeatDelayButton").IsVisible);
            Assert.True(GetPrivateField<Button>(editor,
                "_removeEffectInAreaRepeatDelayButton").IsVisible);
            var cards = GetPrivateField<StackPanel>(editor, "_effectInAreaOnHitRows").Children;
            Assert.Equal(3, cards.Count);
            Assert.All(cards, card => Assert.IsType<OnHitEffectEditor>(card));
            Assert.All(cards.Cast<OnHitEffectEditor>(), card =>
            {
                Assert.Contains("editor-card", card.CardSurface.Classes);
                Assert.DoesNotContain(card.ActiveField, card.Header.Children);
                Assert.DoesNotContain(card.Header.Children.OfType<TextBlock>(),
                    label => label.Text == "Active:");
            });
            Assert.Equal(["Reincarnation", "DamageOverTime", "Attach"], cards
                .Cast<OnHitEffectEditor>().Select(card => card.TypeField.Text ?? "").ToArray());
            var filterLabels = cards.Cast<OnHitEffectEditor>()
                .SelectMany(card => DescendantControls(card).OfType<TextBlock>())
                .Select(label => label.Text).ToList();
            Assert.Contains("Include Type", filterLabels);
            Assert.Contains("Exclude Type", filterLabels);
            var note = DescendantControls(GetPrivateField<Grid>(editor, "_effectInAreaMainRow"))
                .OfType<TextBlock>().Single(text => text.Text?.StartsWith("*Requires", StringComparison.Ordinal) == true);
            Assert.Equal(FontStyle.Italic, note.FontStyle);
            GetPrivateField<TextBox>(editor, "_effectInAreaRepeatDelayBox").Text = "2.5";
            GetPrivateField<AutoCompleteBox>(editor, "_effectInAreaAreaVfxBox").SelectedItem =
                "VFXCorruptedGroundBirth";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        var power = FindPower(XDocument.Load(path), "CorruptedGroundCopy");
        Assert.Equal("2.5", power.Element("repeatdelay")?.Value);
        Assert.Equal("VFXCorruptedGroundBirth", power.Element("areavfx")?.Value);
        Assert.Equal(3, power.Elements("onhiteffect").Count());
        var damage = power.Elements("onhiteffect").Single(effect =>
            effect.Attribute("type")?.Value == "DamageOverTime");
        Assert.Equal("1", damage.Element("damage")?.Value);
        Assert.Equal("Divine", damage.Element("damage")?.Attribute("type")?.Value);
        Assert.Equal("Unit", damage.Elements("target").First().Attribute("attacktype")?.Value);
        Assert.Equal("AbstractSiegeWeapon",
            damage.Elements("target").Last().Attribute("ignoretype")?.Value);
    }

    [Fact]
    public void EffectInAreaReadOnlyCardsKeepContainersAndFilterLabelsWithoutActiveToggle()
    {
        var source = XElement.Parse("""
            <power name="OriginalArea" type="EffectInArea">
              <onhiteffect type="DamageOverTime" duration="15">
                <damage type="Divine">1</damage>
                <target attacktype="Unit"/><target ignoretype="AbstractSiegeWeapon"/>
              </onhiteffect>
            </power>
            """);
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(
            [new PowerDefinition("OriginalArea", true, source)], null,
            unitTypeNames: ["Unit", "AbstractSiegeWeapon"]));

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(false);
            editor.SelectPower("OriginalArea");
            var card = Assert.IsType<OnHitEffectEditor>(Assert.Single(
                GetPrivateField<StackPanel>(editor, "_effectInAreaOnHitRows").Children));

            Assert.Contains("editor-card", card.CardSurface.Classes);
            Assert.DoesNotContain(card.ActiveField, card.Header.Children);
            Assert.DoesNotContain(card.Header.Children.OfType<TextBlock>(),
                label => label.Text == "Active:");
            var labels = DescendantControls(card).OfType<TextBlock>()
                .Select(label => label.Text).ToList();
            Assert.Contains("Include Type", labels);
            Assert.Contains("Exclude Type", labels);
        });
    }

    [Fact]
    public void EffectInAreaBlocksSavingAnOnHitEffectWithoutAType()
    {
        using var workspace = new TestWorkspace(nameof(EffectInAreaBlocksSavingAnOnHitEffectWithoutAType));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name='BlankArea' type='EffectInArea' /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("BlankArea");
            Assert.False(GetPrivateField<StackPanel>(editor,
                "_effectInAreaRepeatDelayFields").IsVisible);
            var addRepeat = GetPrivateField<Button>(editor, "_addEffectInAreaRepeatDelayButton");
            Assert.True(addRepeat.IsVisible);
            addRepeat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(GetPrivateField<StackPanel>(editor,
                "_effectInAreaRepeatDelayFields").IsVisible);
            Assert.Equal("0", GetPrivateField<TextBox>(editor,
                "_effectInAreaRepeatDelayBox").Text);
            GetPrivateField<Button>(editor, "_removeEffectInAreaRepeatDelayButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(GetPrivateField<StackPanel>(editor,
                "_effectInAreaRepeatDelayFields").IsVisible);
            GetPrivateField<Button>(editor, "_addEffectInAreaOnHitButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("Select a Type for the highlighted On Hit Effect before saving.",
            AvaloniaTestHost.Run(() => GetPrivateField<TextBlock>(editor, "_statusMessage").Text));
        var type = AvaloniaTestHost.Run(() => GetPrivateField<StackPanel>(editor,
            "_effectInAreaOnHitRows").Children.Cast<OnHitEffectEditor>().Single().TypeField);
        Assert.Contains("invalid-field", type.Classes);
    }

    private static XElement FindPower(XContainer container, string name)
        => container.Descendants().Single(element =>
            element.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));

    private static IEnumerable<Control> DescendantControls(Control root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children)
                foreach (var descendant in DescendantControls(child)) yield return descendant;
        else if (root is Border { Child: Control child })
            foreach (var descendant in DescendantControls(child)) yield return descendant;
        else if (root is ContentControl { Content: Control content })
            foreach (var descendant in DescendantControls(content)) yield return descendant;
    }

    private static void InvokePrivateClick(object instance, string methodName, object? sender = null)
        => instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, [sender, new RoutedEventArgs()]);

    private static PowerEditorView CreateEditorWithStrings(string path, string stringsPath)
        => new([], path,
            resolveStringAsync: id => Task.FromResult(
                StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().GetValueOrDefault(id)),
            saveStringsAsync: (updates, removals) =>
            {
                var source = File.ReadAllText(stringsPath);
                var updated = StringTableDocument.Parse(source).Apply(updates, removals, StringTableSection.Powers);
                File.WriteAllText(stringsPath, updated);
                return Task.CompletedTask;
            },
            modStringsPath: stringsPath);
}
