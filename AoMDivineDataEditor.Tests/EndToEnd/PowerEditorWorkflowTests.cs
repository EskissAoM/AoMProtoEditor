using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorWorkflowTests
{
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

        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("TestPower");
            GetPrivateField<TextBox>(editor, "_activeTimeBox").Text = "7.5";
            InvokePrivateClick(editor, "AddOptionalTime_Click", new Button { Tag = "builduptime" });
            GetPrivateField<TextBox>(editor, "_buildUpBox").Text = "1.25";
            InvokePrivateClick(editor, "AddOptionalTime_Click", new Button { Tag = "shutdowntime" });
            GetPrivateField<TextBox>(editor, "_shutDownBox").Text = "2";
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
        Assert.Equal("7.5", saved.Element("activetime")?.Value);
        Assert.Equal("1.25", saved.Element("builduptime")?.Value);
        Assert.Equal("2", saved.Element("shutdowntime")?.Value);
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
            InvokePrivateClick(editor, "RemoveOptionalTime_Click", new Button { Tag = "builduptime" });
            InvokePrivateClick(editor, "RemoveOptionalTime_Click", new Button { Tag = "shutdowntime" });
            InvokePrivateClick(editor, "RemoveUseLimit_Click");
            GetPrivateField<CheckBox>(editor, "_singleActivationCheck").IsChecked = false;
            GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsChecked = false;
        });
        Assert.False(AvaloniaTestHost.Run(() =>
            GetPrivateField<CheckBox>(editor, "_exclusiveCheck").IsChecked == true));
        Assert.Null(AvaloniaTestHost.Run(() =>
            GetPrivateField<XElement>(editor, "_currentEntry").Element("exclusive")));
        Assert.Null(AvaloniaTestHost.Run(() =>
            GetPrivateField<XElement>(editor, "_currentEntry").Element("exclusiveerrormsgid")));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        saved = FindPower(XDocument.Load(path), "TestPower");
        foreach (var tag in new[] { "builduptime", "shutdowntime", "limitglobal", "limitplayer",
                     "singleactivation", "exclusive", "exclusiveerrormsgid" })
            Assert.Null(saved.Element(tag));
        Assert.DoesNotContain("STR_ERROR_POWER_TEST_POWER",
            StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().Keys);
        Assert.DoesNotMatch(new Regex(@"(?:\r?\n[ \t]*){3,}"), File.ReadAllText(path));
    }

    [Fact]
    public void ZeroUseLimitsStayVisibleForEditingButAreNotWritten()
    {
        using var workspace = new TestWorkspace(nameof(ZeroUseLimitsStayVisibleForEditingButAreNotWritten));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"P\" type=\"GodPower\" /></powersmod>");
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], path));
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
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
        AvaloniaTestHost.Run(() =>
        {
            editor.SetModifiedMode(true);
            editor.SelectPower("P");
            Assert.Equal(["Full", "Overlap", "InRange", "Unit", "Skip"],
                GetPrivateField<ComboBox>(editor, "_placementBox").ItemsSource!.Cast<string>());
            Assert.Equal(100, GetPrivateField<ComboBox>(editor, "_placementBox").Width);
            Assert.Equal(50, GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").Width);
            Assert.Equal(
            [
                "disallowWaterCast", "disallowsWaterCastRadius", "allowGaia", "noLosExtraLoc",
                "allowSnap", "placeFoundation", "ignoreProtoPlacement", "placeInArea",
                "enemy", "ally", "allyOrEnemyNotSelf", "allowNature",
                "natureOnly", "useTargetUnitPositionForPlacement", "fullyBuiltOnly", "allowDeadTarget",
                "ExpandCheckPlacementHull", "VectorPlacement"
            ], GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").ItemsSource!.Cast<string>());
            GetPrivateField<ComboBox>(editor, "_placementBox").SelectedItem = "Overlap";
            Assert.Contains("notOverlapSelfOrAlly",
                GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").ItemsSource!.Cast<string>());
            GetPrivateField<AutoCompleteBox>(editor, "_placementFlagPicker").SelectedItem = "natureOnly";
        });

        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal("", (string?)FindPower(XDocument.Load(path), "P")
            .Element("placement")?.Attribute("natureOnly"));
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
        AvaloniaTestHost.Run(() => GetPrivateField<Grid>(editor, "_lightingRow")
            .Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
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
                    "Lift Sound", "Land Sound"],
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

    private static XElement FindPower(XContainer container, string name)
        => container.Descendants().Single(element =>
            element.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase) &&
            PowerCatalog.GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));

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
