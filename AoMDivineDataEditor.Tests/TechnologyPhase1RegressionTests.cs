using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPhase1RegressionTests
{
    [Fact]
    public void TechnologyMenu_ExposesEditViewAndDisabledTechTypes()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var buttons = xaml.Descendants().Where(e => e.Name.LocalName == "Button").ToList();

        Assert.Contains(buttons, b => (string?)b.Attribute("Content") == "Edit / View" && (string?)b.Attribute("Click") == "TechnologyEditView_Click");
        Assert.Contains(buttons, b => (string?)b.Attribute("Content") == "Tech Types" && string.Equals((string?)b.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TechnologyEditor_KeepsOriginalModifiedPropertiesEffectsAndXmlPreview()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));

        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TabStripItem" && e.Value.Trim() == "Original");
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TabStripItem" && e.Value.Trim() == "Modified");
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TabItem" && (string?)e.Attribute("Header") == "Properties");
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TabItem" && (string?)e.Attribute("Header") == "Effects");
        Assert.Contains(xaml.Descendants(), e => (string?)e.Attribute("Name") == "_xmlPreview" || (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "_xmlPreview");
    }

    [Fact]
    public void TechnologyEditor_HasPublicParameterlessConstructorForAvaloniaLoader()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("public TechnologyEditorView()", techCode, StringComparison.Ordinal);
        Assert.Contains(": this()", techCode, StringComparison.Ordinal);
    }



    [Fact]
    public void TechnologyEditor_GuardsEventsRaisedDuringAvaloniaInitialization()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("private bool _controlsReady;", techCode, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();\n        _controlsReady = true;", techCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("if (!_controlsReady) return;", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_ReusesLoadedDataBarInsteadOfOpeningItsOwnArchive()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.DoesNotContain("new BarArchive", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("File.OpenRead(_dataBarPath)", techCode, StringComparison.Ordinal);
        Assert.Contains("GetBaseTechtreeDocumentsFromLoadedBar()", windowCode, StringComparison.Ordinal);
        Assert.Contains("ExtractTechtreeDocumentsFromBar(_protoDataBarFile, _protoDataBarPath)", windowCode, StringComparison.Ordinal);
        Assert.Contains("private static string? ReadBarXmbXml(BarArchiveEntry entry, Stream archiveStream)", windowCode, StringComparison.Ordinal);
        Assert.Contains("var xml = ReadBarXmbXml(entry, tempStream);", windowCode, StringComparison.Ordinal);
        Assert.Contains("entry.ReadDataDecompressed(archiveStream, decompressed)", windowCode, StringComparison.Ordinal);
        Assert.Contains("XmbReader.ToFormattedXml(decompressed.AsSpan(0, readBytes))", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_UsesEstablishedModTechtreeFileAndPreservesUnknownXml()
    {
        var root = FindProjectRoot();
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("GetCurrentModGameplayFilePath(\"techtree_mods.xml\")", mainCode, StringComparison.Ordinal);
        Assert.Contains("LoadOptions.PreserveWhitespace", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(source)", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_currentTech.RemoveNodes", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("tech.RemoveNodes", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_ReusesProtoUnitPresentationPrimitives()
    {
        var root = FindProjectRoot();
        var techXaml = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("<ColumnDefinition Width=\"0\"/>", techXaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Column=\"0\" IsVisible=\"False\">", techXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"4*\"/>", techXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"1*\" MinWidth=\"250\"/>", techXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_xmlPreviewToggleButton\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("EditorChipService.CreateBlueChip", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownResourceTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("new AssetPathEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ResolveDisplayStringAsync", mainCode, StringComparison.Ordinal);
        Assert.Contains("_baseGameIconPaths.Concat(_customIconPaths)", mainCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyNames(modified: selectedIndex == 1)", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_UsesFriendlyLabelsAndStatusDropdown()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("\"researchpoints\" => \"Research points\"", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Obtainable\", \"Unobtainable\", \"Active\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("AddChipListEditor(tech, \"techtype\", \"Technology Types\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddChipListEditor(tech, \"flag\", \"Flags\")", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_MatchesProtoUnitPropertySizingFormattingAndReadOnlyPresentation()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"150,*\")", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorTextFieldStyle.ConfigureTextBox", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorNumericFieldStyle.ConfigureNumericTextBox", techCode, StringComparison.Ordinal);
        Assert.Contains("AddPrimaryTechnologyRowAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateNumericTextBox(tech.Attribute(\"orderhint\")?.Value ?? \"\", 50)", techCode, StringComparison.Ordinal);
        Assert.Contains("box.MinHeight = 32", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Properties\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Costs\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddChipListEditor(tech, \"techtype\", \"Technology Types\")", techCode, StringComparison.Ordinal);
        Assert.Contains("_propertiesPanel.IsEnabled = _current != null", techCode, StringComparison.Ordinal);
        Assert.Contains("_xmlPreview.Opacity = _current != null ? 1.0 : 0.55", techCode, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(_xmlPreview)", techCode, StringComparison.Ordinal);
        Assert.Contains("SaveOptions.DisableFormatting", techCode, StringComparison.Ordinal);
        Assert.Contains("XElement.Parse(_current.ToString(SaveOptions.DisableFormatting), LoadOptions.None)", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_UsesRequestedCorePropertyOrderAndSpecialDevotionCostEditor()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        var displayIndex = techCode.IndexOf("AddPrimaryTechnologyRowAsync(tech, displayName, identityFields)", StringComparison.Ordinal);
        var rolloverIndex = techCode.IndexOf("AddStringBackedPropertyRowAsync(\"Rollover text\"", StringComparison.Ordinal);
        var advancedIndex = techCode.IndexOf("AddStringBackedPropertyRowAsync(\"Advanced rollover\"", StringComparison.Ordinal);
        var iconIndex = techCode.IndexOf("AddIconEditor(icon, identityFields)", StringComparison.Ordinal);
        var statusIndex = techCode.IndexOf("AddStatusEditor(status, identityFields)", StringComparison.Ordinal);
        var researchIndex = techCode.IndexOf("AddResearchPointsEditor(tech, researchPoints, devotionCost)", StringComparison.Ordinal);

        Assert.True(displayIndex < rolloverIndex && rolloverIndex < advancedIndex && advancedIndex < iconIndex);
        Assert.True(iconIndex < statusIndex && statusIndex < researchIndex);
        Assert.Contains("if (devotionCost != null)\n            AddDevotionCostEditor(devotionCost);", techCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("Text = \"Type\"", techCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"Order hint\"", techCode, StringComparison.Ordinal);
        Assert.Contains("typeSelector.Width = 130", techCode, StringComparison.Ordinal);
        Assert.Contains("Text = \"Number\"", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.UnsignedInteger", techCode, StringComparison.Ordinal);
        Assert.Contains("\"Livestock\"", techCode, StringComparison.Ordinal);
        Assert.Contains("\"LogicalTypeValidCosmicGuardSacrifice\"", techCode, StringComparison.Ordinal);
        Assert.Contains("\"Villager\"", techCode, StringComparison.Ordinal);
        Assert.Contains("\"WarriorPriest\"", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_ChipSelectorsReuseSharedOpenOnClickBehaviorAndCompactWidth()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("picker.Width = 200", techCode, StringComparison.Ordinal);
        Assert.Contains("picker.MaxWidth = 200", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorAutoCompleteService.EnableDropdown(picker, () => _loadingUi, selectAllOnFirstClick: false)", techCode, StringComparison.Ordinal);
        Assert.True(
            techCode.IndexOf("content.Children.Add(picker)", StringComparison.Ordinal) <
            techCode.IndexOf("content.Children.Add(chips)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("RheiasGiftCopy", "displaynameid", "STR_TECH_RHEIAS_GIFT_COPY_NAME")]
    [InlineData("RheiasGiftCopy", "rollovertextid", "STR_TECH_RHEIAS_GIFT_COPY_LR")]
    [InlineData("RheiasGiftCopy", "advancedrollovertextoverrideid", "STR_TECH_RHEIAS_GIFT_COPY_OVERRIDE")]
    public void TechnologyEditor_GeneratesTechnologySpecificStringIds(string name, string tag, string expected)
    {
        Assert.Equal(expected, TechnologyEditorView.BuildTechnologyStringId(name, tag));
    }

    [Fact]
    public void TechnologyEditor_AddFlowMatchesProtoUnitDuplicateOrBlankChoice()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("Do you want to DUPLICATE the selected technology", techCode, StringComparison.Ordinal);
        Assert.Contains("Cancel to create a blank technology instead", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(source)", techCode, StringComparison.Ordinal);
        Assert.Contains("RegenerateDuplicatedTechnologyStringsAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(\"displaynameid\", displayId)", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(\"rollovertextid\", rolloverId)", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_PolishesOptionalFieldsCopyStringsAndCustomDefaults()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("\"techage\" => \"Tech Age\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Type\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Order hint\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Devotion cost\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Other attributes\")", techCode, StringComparison.Ordinal);
        Assert.Contains("ResolveTechnologyStringValueAsync(oldId)", techCode, StringComparison.Ordinal);
        Assert.Contains("_pendingStringUpdates.TryGetValue(stringId", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(\"icon\", \"\")", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(\"status\", \"UNOBTAINABLE\")", techCode, StringComparison.Ordinal);
        Assert.Contains("new XElement(\"researchpoints\", \"0\")", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_FiltersExistingChipsAndDeletesTechnologyStringsOnSave()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("RefreshPickerItems()", techCode, StringComparison.Ordinal);
        Assert.Contains("knownValues.Where(value => !present.Contains(value))", techCode, StringComparison.Ordinal);
        Assert.Contains("PromptType.Confirm, \"Delete Technology\"", techCode, StringComparison.Ordinal);
        Assert.Contains("_pendingStringRemovals.Add(stringId)", techCode, StringComparison.Ordinal);
        Assert.Contains("_saveStringsAsync(_pendingStringUpdates, _pendingStringRemovals)", techCode, StringComparison.Ordinal);
        Assert.Contains("foreach (var removal in removals)", mainCode, StringComparison.Ordinal);
        Assert.Contains("entries.Remove(removal)", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_SavesTechtreeModsWithoutDeclarationUsingSharedIndentedWriter()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("ProtoEditorWindow.SaveAbilityXmlDocument(_modDocument, _modTechtreePath)", techCode, StringComparison.Ordinal);
        Assert.Contains("OmitXmlDeclaration = true", mainCode, StringComparison.Ordinal);
        Assert.Contains("IndentChars = \"\\t\"", mainCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_ExposesStructuredPrerequisiteEditors()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        var tabs = xaml.Descendants().Where(e => e.Name.LocalName == "TabItem").Select(e => (string?)e.Attribute("Header")).ToList();
        Assert.Equal(new[] { "Properties", "Prereqs", "Effects" }, tabs);
        Assert.Contains(xaml.Descendants(), e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "_prereqsPanel");
        Assert.Contains("Text = \"──── Prerequisites ────\"", techCode, StringComparison.Ordinal);
        Assert.Contains("PrerequisiteTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"TechStatus\", \"SpecificAge\", \"TypeCount\", \"Culture\", \"Civilization\", \"KBStat\"", techCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyAges", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOperatorCombo(prereq)", techCode, StringComparison.Ordinal);
        Assert.Contains("OperatorToSymbol", techCode, StringComparison.Ordinal);
        Assert.Contains("_prereqUnitNames", techCode, StringComparison.Ordinal);
        Assert.Contains("_majorGodNames", techCode, StringComparison.Ordinal);
        Assert.Contains("KbStatsUsingResourceParameter", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownResourceTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyPrerequisiteUnitNames()", mainCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyPrerequisiteMajorGodNames()", mainCode, StringComparison.Ordinal);
        Assert.Contains("major_gods_mods.xml", mainCode, StringComparison.Ordinal);
        Assert.Contains("_prereqsPanel.IsEnabled = canEdit", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_TechAgeIsACompactDropdownAndOptionalPropertiesCanBeRemoved()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("\"ArchaicAge\", \"ClassicalAge\", \"HeroicAge\", \"MythicAge\", \"WonderAge\"", techCode, StringComparison.Ordinal);
        Assert.Contains("private void AddTechAgeEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 150", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateRemoveButton", techCode, StringComparison.Ordinal);
        Assert.Contains("RemoveOptionalElement", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_PrerequisitePolishUsesCompactSelectorsSharedRemoveStyleAndStableAutocomplete()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("CreateInlineLabel(\"Unit\")", techCode, StringComparison.Ordinal);
        Assert.Contains("prereq.Value.Trim(),\n            200,", techCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("CreateStrictPrereqSelector(options, entry.Value.Trim(), 150", techCode, StringComparison.Ordinal);
        Assert.Contains("AddPrerequisiteButton(tech);", techCode, StringComparison.Ordinal);
        Assert.Contains("if (prereqs.Count == 0 && !IsModifiedTab)", techCode, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"remove-button\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(2, 0, 0, 0)", techCode, StringComparison.Ordinal);

        var strictSelectorStart = techCode.IndexOf("private AutoCompleteBox CreateStrictPrereqSelector", StringComparison.Ordinal);
        var operatorStart = techCode.IndexOf("private ComboBox CreateOperatorCombo", strictSelectorStart, StringComparison.Ordinal);
        var strictSelectorCode = techCode[strictSelectorStart..operatorStart];
        Assert.Contains("EditorAutoCompleteService.ConfigureStrict", strictSelectorCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorAutoCompleteService.EnableDropdown(selector", strictSelectorCode, StringComparison.Ordinal);
        var prereqStart = techCode.IndexOf("private void AddPrereqEditor", StringComparison.Ordinal);
        var prereqEnd = techCode.IndexOf("private void AddEffectsHeader", prereqStart, StringComparison.Ordinal);
        var prereqCode = techCode[prereqStart..prereqEnd];
        Assert.DoesNotContain("Text = \"Type\"", prereqCode, StringComparison.Ordinal);
        Assert.Contains("new XAttribute(\"kbStat\", \"\")", techCode, StringComparison.Ordinal);
        Assert.Contains("deferSelectionCommit: true", techCode, StringComparison.Ordinal);
        Assert.Contains("primaryLabel.VerticalAlignment = VerticalAlignment.Center", techCode, StringComparison.Ordinal);
        Assert.Contains("selector.Width = width;", techCode, StringComparison.Ordinal);
        Assert.Contains("selector.MaxWidth = width;", techCode, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = ProtoConstants.KnownResourceTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 100,", techCode, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 100,", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_RenamesCustomTechStringsAndStructuresEffectHeaderSelectors()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var techXaml = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var autoCompleteCode = File.ReadAllText(Path.Combine(root, "Classes", "EditorAutoCompleteService.cs"));

        Assert.Contains("LostFocus=\"TechNameBox_LostFocus\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("CommitPendingTechnologyNameAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("BuildTechnologyStringId(newName, tag)", techCode, StringComparison.Ordinal);
        Assert.Contains("_pendingStringRemovals.Add(oldId)", techCode, StringComparison.Ordinal);
        Assert.Contains("_pendingStringUpdates[newId] = text", techCode, StringComparison.Ordinal);

        Assert.Contains("Text = \"──── Effects ────\"", techCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyDataEffectSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateStrictEffectSelector", techCode, StringComparison.Ordinal);
        Assert.Contains("if (currentType.Equals(\"Data\", StringComparison.OrdinalIgnoreCase))", techCode, StringComparison.Ordinal);
        Assert.Contains("Text = effect.ToString(SaveOptions.DisableFormatting)", techCode, StringComparison.Ordinal);

        Assert.Contains("bool selectAllOnFirstClick = true", autoCompleteCode, StringComparison.Ordinal);
        Assert.Contains("EnableDropdown(autoCompleteBox, isBusy, selectAllOnFirstClick)", autoCompleteCode, StringComparison.Ordinal);
        Assert.Contains("selectAllOnFirstClick: false", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_EffectMetadataTooltipStringsAndPreviewRestoreFollowSharedRules()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var autoCompleteCode = File.ReadAllText(Path.Combine(root, "Classes", "EditorAutoCompleteService.cs"));

        Assert.Contains("keepStartVisibleAfterCommit: true", techCode, StringComparison.Ordinal);
        Assert.Contains("scrollViewer.Offset = new Vector(0, scrollViewer.Offset.Y)", autoCompleteCode, StringComparison.Ordinal);
        Assert.Contains("Content = \"Hide tooltip\"", techCode, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"hideTooltip\", \"\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Delay\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Tooltip override\")", techCode, StringComparison.Ordinal);
        Assert.Contains("BuildNextEffectTooltipStringId", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 380", techCode, StringComparison.Ordinal);
        Assert.Contains("RegenerateEffectTooltipStringsAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("_mainGrid.ColumnDefinitions[2].Width = new GridLength(4, GridUnitType.Star)", techCode, StringComparison.Ordinal);
        Assert.Contains("_mainGrid.ColumnDefinitions[4].MinWidth = 250", techCode, StringComparison.Ordinal);
        Assert.Contains("_mainGrid.ColumnDefinitions[4].Width = new GridLength(1, GridUnitType.Star)", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorNumericInputBehavior.AttachRule(delayBox, ProtoUnitNumericKind.UnsignedFloat)", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Tooltip override\",\n                Width = 150", techCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_ValueTextKeepsUsesValueTextFlagInSync()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("EnsureTechnologyFlag(tech, \"UsesValueText\")", techCode, StringComparison.Ordinal);
        Assert.Contains("RemoveTechnologyFlag(technology, \"UsesValueText\")", techCode, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"valuetext\", StringComparison.OrdinalIgnoreCase)", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_OrdersTechTypesBeforeFlagsAndStructuresFirstNonDataEffects()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("NormalizeTechnologyChildOrder", techCode, StringComparison.Ordinal);
        Assert.Contains("properties.Concat(techTypes).Concat(flags).Concat(prereqs).Concat(effects)", techCode, StringComparison.Ordinal);
        Assert.Contains("if (IsModifiedTab || hideTooltipAttribute != null)", techCode, StringComparison.Ordinal);
        Assert.Contains("StructuredTechnologyEffectTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSetNameEffectEditorAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTextOutputEffectEditorAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSetAgeEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTechStatusEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSharedLosEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddModifyProtoUnitEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTransformUnitEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddResourceExchangeEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("BuildTechnologyEffectStringId", techCode, StringComparison.Ordinal);
        Assert.Contains("OUTPUTALL", techCode, StringComparison.Ordinal);
        Assert.Contains("NormalizeTechnologyStringToken", techCode, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"tech\")", techCode, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"proto\")", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_ValidatesResearchAndCostsAndStructuresSecondNonDataEffectBatch()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedFloat);", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorNumericInputBehavior.AttachRule(box, ProtoUnitNumericKind.UnsignedInteger);", techCode, StringComparison.Ordinal);
        Assert.Contains("value.Equals(currentType, StringComparison.OrdinalIgnoreCase)", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSetOnBuildingDeathTechEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSimpleEffectValueEditor(effect, content, \"Console command\", 200)", techCode, StringComparison.Ordinal);
        Assert.Contains("AddCreatePowerEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTextOutputEffectEditorAsync(effect, content, allIsIntrinsic: false)", techCode, StringComparison.Ordinal);
        Assert.Contains("AddRandomTechEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTextEffectOutputEditorAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("EditorChipService.CreateBlueChip", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 380", techCode, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateInlineLabel(\"Tech\")", techCode, StringComparison.Ordinal);
        Assert.Contains("Set status to", techCode, StringComparison.Ordinal);
        Assert.Contains("SELFMSG", techCode, StringComparison.Ordinal);
        Assert.Contains("PLAYERMSG", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_EffectCreationResetAndThirdNonDataBatchFollowRequestedRules()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("container.Add(new XElement(\"effect\"))", techCode, StringComparison.Ordinal);
        Assert.Contains("ResetEffectForType(effect, value)", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.RemoveAttributes()", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.RemoveNodes()", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.SetAttributeValue(\"status\", \"obtainable\")", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.SetAttributeValue(\"status\", \"active\")", techCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyProtoUnitNames()", windowCode, StringComparison.Ordinal);
        Assert.Contains("_protoUnitNames", techCode, StringComparison.Ordinal);
        Assert.Contains("AddResourceInventoryExchangeEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddTrickleByResourceEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddResourceExchange2EffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddReplaceUnitEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("Content = \"Keep alive\"", techCode, StringComparison.Ordinal);
        Assert.Contains("srcResource2", techCode, StringComparison.Ordinal);
        Assert.Contains("toResource2", techCode, StringComparison.Ordinal);
        Assert.Contains("multiplier2", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_UsesSharedTechTypeCatalogAndStructuresFourthNonDataBatch()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("GetTechnologyTechTypeNames()", windowCode, StringComparison.Ordinal);
        Assert.Contains("entry.Name.Contains(\"tech_types\"", windowCode, StringComparison.Ordinal);
        Assert.Contains("GetCurrentModGameplayFilePath(\"tech_types_mods.xml\")", windowCode, StringComparison.Ordinal);
        Assert.Contains("_techTypeNames", techCode, StringComparison.Ordinal);
        Assert.Contains("AddForbidTechEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSetOnTechResearchedTechEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddUiAlertEffectEditorAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Forbid\", \"Unforbid\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Activates\", \"Disable\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Self\", \"Ally\", \"Enemy\", \"All\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("AddUiAlertEffectEditorAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("SELFMSG", techCode, StringComparison.Ordinal);
        Assert.Contains("PLAYERMSG", techCode, StringComparison.Ordinal);
        Assert.Contains("Content = \"Include player name\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Duration (ms)\"", techCode, StringComparison.Ordinal);
        Assert.Contains("var row = new WrapPanel", techCode, StringComparison.Ordinal);
        Assert.Contains("newHpSpacer", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_CreateUnitEffectUsesStructuredPatternAndGeneratorControls()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("\"CreateUnit\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddCreateUnitEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Creates\"", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.PositiveInteger", techCode, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"generator\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateCreateUnitPresenceCheckBox(effect, \"allgenerators\", \"All generators\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateCreateUnitPresenceCheckBox(effect, \"mute\", \"Mute\"", techCode, StringComparison.Ordinal);
        Assert.Contains("Content = \"Queue\"", techCode, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"queue\", \"false\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateCreateUnitPresenceCheckBox(effect, \"ignorerally\", \"Ignore Rally\"", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Simple\", \"Leaving\", \"Scatter\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("AddOptionalPatternFloat", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Offset\")", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.SignedFloat", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_EffectTypeAndWrappingPolishUsesRequestedRules()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var techXaml = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var windowXaml = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));

        Assert.DoesNotContain("\"ShowWorldView\"", techCode, StringComparison.Ordinal);
        Assert.Contains("}, 180);", techCode, StringComparison.Ordinal);
        Assert.Contains("new XAttribute(\"type\", \"Leaving\")", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Simple\", \"Leaving\", \"Scatter\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("var patternRow = new WrapPanel", techCode, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml")), StringComparison.Ordinal);
        Assert.Contains("UIALERT_SELFMSG", techCode, StringComparison.Ordinal);
        Assert.Contains("UIALERT_PLAYERMSG", techCode, StringComparison.Ordinal);
        Assert.Contains("Width=\"4*\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1*\" MinWidth=\"250\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"1440\" Height=\"900\"", windowXaml, StringComparison.Ordinal);
        Assert.Contains("content.Children.Add(firstRow);", techCode, StringComparison.Ordinal);
        Assert.Contains("content.Children.Add(patternRow);", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataSimpleUnitAmountSubtypesUseStructuredSharedPresentation()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("SimpleUnitAmountDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"AdditionalScale\", \"AutoBuildRate\", \"AuxRechargeTime\"", techCode, StringComparison.Ordinal);
        Assert.Contains("\"Hitpoints\", \"InitialResource\", \"LOS\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSimpleUnitAmountDataEffectEditor(effect, content)", techCode, StringComparison.Ordinal);
        Assert.Contains("ActionUnitAmountDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"Accuracy\", \"DamageArea\", \"DisplayedNumberProjectiles\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddActionUnitAmountDataEffectEditor(effect, content)", techCode, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Add\", \"Multiply\", \"Multiply base\", \"Set to\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 132", techCode, StringComparison.Ordinal);
        Assert.Contains("Content = \"Not Nature\"", techCode, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(currentTarget, \"ignoreNature\", \"\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateDataActionSelector", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"All\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("FontWeight.Bold", techCode, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"allactions\", \"1\")", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.SignedFloat", techCode, StringComparison.Ordinal);
        Assert.Contains("new XAttribute(\"type\", \"ProtoUnit\")", techCode, StringComparison.Ordinal);
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        Assert.Contains("GetTechnologyProtoActionNames()", windowCode, StringComparison.Ordinal);
        Assert.Contains("CreateStrictPrereqSelector(\n            _original.Keys.Concat(_modified.Keys),\n            prereq.Value.Trim(),\n            200,", techCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("removePrereq.HorizontalAlignment = HorizontalAlignment.Right", techCode, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(removePrereq, 1)", techCode, StringComparison.Ordinal);
        Assert.Contains("EnableDisableUnitDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"AuxRechargeInit\", \"RechargeInit\", \"RespawnTrainActive\", \"VeterancyEnable\"", techCode, StringComparison.Ordinal);
        Assert.Contains("\"EnableDodge\", \"EnableSharedBuildLimit\"", techCode, StringComparison.Ordinal);
        Assert.Contains("EnableDisableActionUnitDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("AddDataIgnoreNatureEditor(effect, row);", techCode, StringComparison.Ordinal);
        Assert.Contains("}, 165, preserveSuggestionOrder: true);", techCode, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"relativity\", \"Assign\")", techCode, StringComparison.Ordinal);
        Assert.Contains("selected == \"Disable\" ? \"0\" : \"1\"", techCode, StringComparison.Ordinal);
        Assert.Contains("NormalizeEffectAttributeOrder", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\"InvestmentAmount\"", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\"InvestmentCap\"", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\"InvestmentEnable\"", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataRound5PolishKeepsPairedFieldsAndSharedSizingRules()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("Width = 132", techCode, StringComparison.Ordinal);
        Assert.Contains("}, 165, preserveSuggestionOrder: true);", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Amount\", amount)", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Bonus against\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(typeLabel, typeCombo, leftSpacing: 8)", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(label, box)", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateFreeTextEffectAttributeBox(effect, \"tactic\", 100)", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 135", techCode, StringComparison.Ordinal);
        Assert.Contains("Tooltip override\", 380, removable: true, multiline: true", techCode, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 32", techCode, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = multiline", techCode, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.RemoveAttributes();", techCode, StringComparison.Ordinal);
        Assert.Contains("effect.RemoveNodes();", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataRound6StructuresCommandDamageFlagsShadingAndChargedModifyEffects()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var constantsCode = File.ReadAllText(Path.Combine(root, "Classes", "ProtoConstants.cs"));

        Assert.Contains("ChargedModifyAdjustDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("AddChargedModifyAdjustDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownModifyTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("CommandDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("AddCommandDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Unit\", \"Tech\", \"Command\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("_protoUnitCommandNames", techCode, StringComparison.Ordinal);
        Assert.Contains("GetAvailableCommandNames(),", windowCode, StringComparison.Ordinal);
        Assert.Contains("_iconPreviewService);", windowCode, StringComparison.Ordinal);
        Assert.Contains("CreateUnsignedIntegerEffectBox(effect, \"row\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateUnsignedIntegerEffectBox(effect, \"column\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddDamageByCostDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"resource\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddDamageFlagsDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("KnownDamageAreaTargetFlags", constantsCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownDamageAreaTargetFlags", techCode, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"amount\", \"1\")", techCode, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddDamageShadingDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownShadingTypeDisplayNames", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.GetShadingTypeXmlValue(selected)", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.ClampZeroToOne", techCode, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Percent\")", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Interval (ms)\"", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_CloseAfterDiscardBypassesRepeatedDirtyPrompt()
    {
        var root = FindProjectRoot();
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("private bool _allowCloseAfterDiscard;", windowCode, StringComparison.Ordinal);
        Assert.Contains("if (_allowCloseAfterDiscard)", windowCode, StringComparison.Ordinal);
        Assert.Contains("_allowCloseAfterDiscard = true;\n                Close();", windowCode.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_UsesEightPixelInterGroupSpacing()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("Margin = new Thickness(leftSpacing > 0 ? 8 : 0, 0, 0, 0)", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin = new Thickness(leftSpacing > 0 ? 8 : 0, 0, 8, 0)", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("leftSpacing: 10", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("leftSpacing: 12", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("leftSpacing: 16", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thickness(12, 4, 8, 4)", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thickness(16, 4, 8, 4)", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataRound7StructuresFlagsLifespanMinWorkRateAndReplacement()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var constantsCode = File.ReadAllText(Path.Combine(root, "Classes", "ProtoConstants.cs"));

        Assert.Contains("Content = \"Not Nature\"", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"Ignore Nature\"", techCode, StringComparison.Ordinal);
        Assert.Contains("Width = 110", techCode, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Enable\", \"Flag\", \"FullCapacityMultiplier\"", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitFlagDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"ProtoUnitFlag\", \"Flag\"", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownFlags", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoActionFlagDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoActionMetadataCatalog.GetKnownFlagTags()", techCode, StringComparison.Ordinal);
        Assert.Contains("Update lifespan as percent", techCode, StringComparison.Ordinal);
        Assert.Contains("updateLifespanAsPercent", techCode, StringComparison.Ordinal);
        Assert.Contains("AddMinWorkRateDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Unit\", \"Resource\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateResourceValueCombo(effect, \"unittype\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddModifyReplacementDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownReplacementTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("public static readonly string[] KnownReplacementTypes", constantsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] KnownReplacementTypes", windowCode, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(parsed, 0d, 1d)", techCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataRound8StructuresSpawnDamageProjectileRechargeSelfDestructSetUnitTypeAndWorkRate()
    {
        var root = FindProjectRoot();
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var constantsCode = File.ReadAllText(Path.Combine(root, "Classes", "ProtoConstants.cs"));

        Assert.Contains("CreateLabeledEffectSegment(\"Type\", CreateStrictEffectSelector(", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownReplacementTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("            150), leftSpacing: 8));", techCode, StringComparison.Ordinal);

        Assert.Contains("ModifySpawnDataSubtypes", techCode, StringComparison.Ordinal);
        Assert.Contains("AddModifySpawnDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddOptionalEffectAttribute(row, effect, \"Chance\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddOptionalEffectAttribute(row, effect, \"Lifespan\"", techCode, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Default\", \"TerrainOnly\" }", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownSpawnTypes", techCode, StringComparison.Ordinal);

        Assert.Contains("AddOnDamageModifyDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("currentModifyType is \"DamageSpecific\" or \"ArmorSpecific\"", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateRequiredDataTypeCombo", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownDamageTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownArmorTypes", techCode, StringComparison.Ordinal);

        Assert.Contains("AddProjectileDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("AddRechargeTypeDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("(new[] { \"Time\" }).Concat(ProtoConstants.KnownRechargeTypes)", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSelfDestructProtoActionDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("\"protoaction\"", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSetUnitTypeDataEffectEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownUnitTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("\"MinWorkRate\", \"WorkRate\"", techCode, StringComparison.Ordinal);

        Assert.Contains("public static readonly string[] KnownSpawnTypes", constantsCode, StringComparison.Ordinal);
        Assert.Contains("public static readonly string[] KnownRechargeTypes", constantsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] KnownSpawnTypes", windowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] KnownRechargeTypes", windowCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownSpawnTypes", windowCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownRechargeTypes", windowCode, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AoMDivineDataEditor.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj.");
    }
}
