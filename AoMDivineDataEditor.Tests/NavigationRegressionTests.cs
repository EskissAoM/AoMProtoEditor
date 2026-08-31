using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class NavigationRegressionTests
{
    [Fact]
    public void EntityBrowser_ExposesUnitsAndTechnologiesWithoutCategoryControls()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        foreach (var entity in new[] { "Units", "Technologies", "Gods", "Powers" })
        {
            Assert.Contains(xaml.Descendants(), element =>
                element.Name.LocalName == "Button" &&
                (string?)element.Attribute("Content") == entity);
        }

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Gods" &&
            (string?)element.Attribute("Click") == "GodsEntity_Click" &&
            element.Attribute("IsEnabled") == null);
        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Powers" &&
            (string?)element.Attribute("IsEnabled") == "False");

        var selectorContainer = xaml.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "_entitySelectorContainer"));
        var selectorGrid = selectorContainer.Elements().Single(element => element.Name.LocalName == "Grid");
        Assert.Equal("*,*", (string?)selectorGrid.Attribute("ColumnDefinitions"));
        Assert.Equal("Auto,Auto", (string?)selectorGrid.Attribute("RowDefinitions"));

        var selectorButtons = selectorGrid.Elements().Where(element => element.Name.LocalName == "Button").ToDictionary(
            element => (string)element.Attribute("Content")!,
            element => element);
        Assert.DoesNotContain(selectorButtons["Units"].Attributes(), attribute => attribute.Name.LocalName.EndsWith(".Row", StringComparison.Ordinal));
        Assert.Equal("1", selectorButtons["Technologies"].Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".Column", StringComparison.Ordinal)).Value);
        Assert.Equal("1", selectorButtons["Gods"].Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".Row", StringComparison.Ordinal)).Value);
        Assert.Equal("1", selectorButtons["Powers"].Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".Row", StringComparison.Ordinal)).Value);
        Assert.Equal("1", selectorButtons["Powers"].Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".Column", StringComparison.Ordinal)).Value);

        Assert.DoesNotContain(xaml.Descendants(), element =>
            element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value
                ?.Contains("category", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain("GetCategoryForUnit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoryFilter_SelectionChanged", code, StringComparison.Ordinal);
        Assert.DoesNotContain("-- Uncategorized --", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityBrowser_TechnologiesUseTheSharedSidebarAndKeepTheirExistingEditor()
    {
        var root = FindProjectRoot();
        var mainXaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var technologyXaml = XDocument.Load(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var technologyCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        var technologyHost = mainXaml.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "_technologyHost"));
        Assert.Equal("2", technologyHost.Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".Column", StringComparison.Ordinal)).Value);
        Assert.Equal("3", technologyHost.Attributes().Single(attribute => attribute.Name.LocalName.EndsWith(".ColumnSpan", StringComparison.Ordinal)).Value);

        Assert.Contains("ShowEntityKindAsync(EditorEntityKind.Technologies)", mainCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyNames(modified: selectedIndex == 1)", mainCode, StringComparison.Ordinal);
        Assert.Contains("SelectTechnology(selectedName)", mainCode, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<string> GetTechnologyNames", technologyCode, StringComparison.Ordinal);
        Assert.Contains("public void SelectTechnology", technologyCode, StringComparison.Ordinal);

        var hiddenTechnologyBrowser = technologyXaml.Descendants().Single(element =>
            element.Name.LocalName == "Border" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName.EndsWith(".Column", StringComparison.Ordinal) && attribute.Value == "0"));
        Assert.Equal("False", (string?)hiddenTechnologyBrowser.Attribute("IsVisible"));
    }

    [Fact]
    public void EntityBrowser_InitialTabEventIsIgnoredUntilNamedControlsAreReady()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var refresh = Regex.Match(
            code,
            @"private void RefreshUnitList\(\)\s*\{(?<body>.*?)(?=\n\s*private void FilterUnitList)",
            RegexOptions.Singleline);

        Assert.True(refresh.Success, "Could not locate RefreshUnitList.");
        var guardIndex = refresh.Groups["body"].Value.IndexOf("if (_unitTabs is null)", StringComparison.Ordinal);
        var accessIndex = refresh.Groups["body"].Value.IndexOf("_unitTabs.SelectedIndex", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && accessIndex > guardIndex,
            "The initial TabStrip event must return before accessing controls still being assigned by XAML.");
    }

    [Fact]
    public void DocumentTabs_ExposeMainDoubleClickContextMenuAndSafeCloseWorkflow()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var technologyCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            element.Name.LocalName == "TabStrip" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "_documentTabs") &&
            (string?)element.Attribute("SelectionChanged") == "DocumentTabs_SelectionChanged");

        var entityList = xaml.Descendants().Single(element =>
            element.Name.LocalName == "ListBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "_unitList"));
        Assert.Null((string?)entityList.Attribute("Tapped"));
        Assert.Null((string?)entityList.Attribute("DoubleTapped"));
        Assert.Equal("UnitList_ContextRequested", (string?)entityList.Attribute("ContextRequested"));

        Assert.Contains("CreateDocumentTab(isMain: true", code, StringComparison.Ordinal);
        Assert.Contains("if (_documentTabs is null || _suppressDocumentTabSelection", code, StringComparison.Ordinal);
        Assert.Contains("OpenPinnedDocumentTabAsync", code, StringComparison.Ordinal);
        Assert.Contains("InputElement.PointerPressedEvent", code, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", code, StringComparison.Ordinal);
        Assert.Contains("EntitySecondClickWindowMilliseconds = 500", code, StringComparison.Ordinal);
        Assert.Contains("e.Timestamp - _lastEntityPointerPressTimestamp", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(EntitySecondClickWindowMilliseconds", code, StringComparison.Ordinal);
        Assert.Contains("await _unitSelectionChangeTask;", code, StringComparison.Ordinal);
        Assert.Contains("RunUnitListSelectionChangeAsync", code, StringComparison.Ordinal);
        Assert.Contains("HandleUnitListSelectionChangedAsync", code, StringComparison.Ordinal);
        Assert.Contains("FindPinnedDocumentTab", code, StringComparison.Ordinal);
        Assert.Contains("Open in new tab", code, StringComparison.Ordinal);
        Assert.Contains("Header = \"Copy\"", code, StringComparison.Ordinal);
        Assert.Contains("Header = \"Delete\"", code, StringComparison.Ordinal);
        Assert.Contains("Foreground = Brushes.OrangeRed", code, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = isModified", code, StringComparison.Ordinal);
        Assert.Contains("ActivateContextEntityAsync", code, StringComparison.Ordinal);
        Assert.Contains("AddUnitAsync(duplicateSelected: true)", code, StringComparison.Ordinal);
        Assert.Contains("AddTechnologyAsync(duplicateSelected: true)", code, StringComparison.Ordinal);
        Assert.Contains("DeleteSelectedUnitAsync", code, StringComparison.Ordinal);
        Assert.Contains("DeleteSelectedTechnologyAsync", code, StringComparison.Ordinal);
        Assert.Contains("public async Task AddTechnologyAsync(bool duplicateSelected = false)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("var wasRenamed = changedDocument", code, StringComparison.Ordinal);
        Assert.Contains("That is navigation,", code, StringComparison.Ordinal);
        Assert.Contains("if (tab.IsMain)", code, StringComparison.Ordinal);
        Assert.Contains("if (IsDocumentTabDirty(tab))", code, StringComparison.Ordinal);
        Assert.Contains("Discard those changes and close this tab", code, StringComparison.Ordinal);
        Assert.Contains("DiscardUnitDocumentChanges(tab)", code, StringComparison.Ordinal);
        Assert.Contains("DiscardTechnologyChanges(tab.EntityName, tab.SavedElement)", code, StringComparison.Ordinal);
        Assert.Contains("Pending save — close this tab to cancel its changes.", code, StringComparison.Ordinal);
        Assert.Contains("await CaptureCurrentUnitDraftAsync()", code, StringComparison.Ordinal);
        Assert.Contains("RenameOpenDocumentTabs", code, StringComparison.Ordinal);
        Assert.Contains("RemoveDeletedDocumentTabs", code, StringComparison.Ordinal);
        Assert.Contains("ResetDocumentTabs", code, StringComparison.Ordinal);
        Assert.Contains("RefreshDocumentTabHeaders();", code, StringComparison.Ordinal);
        Assert.Contains("IsTechnologyDirty", technologyCode, StringComparison.Ordinal);
        Assert.Contains("public void DiscardTechnologyChanges", technologyCode, StringComparison.Ordinal);
        Assert.Contains("_dirtyTechnologyNames.Clear();", technologyCode, StringComparison.Ordinal);
        Assert.Contains("SelectDocumentTabWithoutActivation(_activeDocumentTab)", code, StringComparison.Ordinal);
        Assert.Contains("_documentTabs.IsEnabled = false", code, StringComparison.Ordinal);
        Assert.Contains("_entitySourceChangeInProgress", code, StringComparison.Ordinal);
        Assert.Contains("_unitTabs.IsEnabled = false", code, StringComparison.Ordinal);
        Assert.Contains("await _technologyView.CommitCurrentTechnologyAsync()", code, StringComparison.Ordinal);
        Assert.Contains("public async Task<bool> CommitCurrentTechnologyAsync()", technologyCode, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _technologyNameCommitGate", technologyCode, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _editorBuildGate", technologyCode, StringComparison.Ordinal);
        Assert.Contains("generation != _editorBuildGeneration", technologyCode, StringComparison.Ordinal);
        Assert.Contains("RestorePendingTechnologyRenameState", technologyCode, StringComparison.Ordinal);
        Assert.Contains("IsUnitStringIdOwnedByAnotherDirtyDocument", code, StringComparison.Ordinal);
        Assert.Contains("IsTechnologyStringIdOwnedByAnotherDirtyDocument", technologyCode, StringComparison.Ordinal);
        Assert.Contains("if (_technologyView?.IsDirty == true)", code, StringComparison.Ordinal);
        Assert.Contains("await _technologyView.SaveAsync()", code, StringComparison.Ordinal);
        Assert.Contains("ConfirmMainDocumentReplacementAsync", code, StringComparison.Ordinal);
        Assert.Contains("Unsaved main document", code, StringComparison.Ordinal);
        Assert.Contains("DiscardUnitDocumentChanges(main)", code, StringComparison.Ordinal);
        Assert.Contains("DiscardTechnologyChanges(main.EntityName, main.SavedElement)", code, StringComparison.Ordinal);
        Assert.Contains("_mainDocumentTab.SavedElement", code, StringComparison.Ordinal);
        Assert.Contains("Duplicate technology name", technologyCode, StringComparison.Ordinal);
        Assert.Contains("ShowTechnologyNameErrorAsync", technologyCode, StringComparison.Ordinal);

        var keyboardHandler = Regex.Match(
            code,
            @"private void OnWindowKeyDown\(.*?(?=\n\s*private void PageSearchBox_KeyDown)",
            RegexOptions.Singleline);
        Assert.True(keyboardHandler.Success, "Could not locate the window keyboard handler.");
        Assert.Contains("Save_Click(this, e)", keyboardHandler.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveActiveTransformCommandAndUnitAsync", keyboardHandler.Value, StringComparison.Ordinal);

        var xamlText = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13\" />", xamlText, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"40\" />", xamlText, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"ScrollBar:horizontal\">", xamlText, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"6\" />", xamlText, StringComparison.Ordinal);
        Assert.Contains("<Border Padding=\"0,0,0,14\">", xamlText, StringComparison.Ordinal);
        Assert.Contains("EditorEntityKind.MajorGods => \"Major God\"", code, StringComparison.Ordinal);
        Assert.Contains("var source = tab.IsModified ? \"Custom\" : \"Original\";", code, StringComparison.Ordinal);
        Assert.Contains("$\"{kind}: {tab.EntityName} ({source})\"", code, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 170", code, StringComparison.Ordinal);

        var sectionsButton = xaml.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "_sectionsButton"));
        Assert.Equal("2", sectionsButton.Attributes().Single(attribute =>
            attribute.Name.LocalName.EndsWith(".Column", StringComparison.Ordinal)).Value);
        Assert.Equal("Right", (string?)sectionsButton.Attribute("HorizontalAlignment"));
        Assert.Equal("0,0,10,0", (string?)sectionsButton.Attribute("Margin"));

        var emptyDocumentOverlay = xaml.Descendants().Single(element =>
            element.Name.LocalName == "Border" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "_emptyDocumentOverlay"));
        Assert.Equal("True", (string?)emptyDocumentOverlay.Attribute("IsVisible"));
        Assert.Contains("ShowEmptyDocumentState(EditorEntityKind.Units);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PreloadInitialVisibleUnitAsync", code, StringComparison.Ordinal);
        Assert.Contains("Click on a technology to start", code, StringComparison.Ordinal);

        var unitSelection = Regex.Match(
            code,
            @"private async Task HandleUnitListSelectionChangedAsync\(\)\s*\{(?<body>.*?)(?=\n\s*private async void UnitNameBox_TextChanged)",
            RegexOptions.Singleline);
        Assert.True(unitSelection.Success, "Could not locate the shared entity selection handler.");
        Assert.DoesNotContain("RefreshUnitList();", unitSelection.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitMenu_TacticsAndAbilitiesRemainConnectedToTheirManagers()
    {
        var root = FindProjectRoot();
        var xamlPath = Path.Combine(root, "Windows", "ProtoEditorWindow.axaml");
        var codePath = Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs");
        var xaml = XDocument.Load(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains(xaml.Descendants(), element =>
            string.Equals((string?)element.Attribute("Content"), "Tactics", StringComparison.Ordinal) &&
            string.Equals(element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Click")?.Value, "ProtounitTactics_Click", StringComparison.Ordinal));
        Assert.Contains(xaml.Descendants(), element =>
            string.Equals((string?)element.Attribute("Content"), "Abilities", StringComparison.Ordinal) &&
            string.Equals(element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Click")?.Value, "ProtounitAbilities_Click", StringComparison.Ordinal));

        AssertHandlerCalls(code, "ProtounitTactics_Click", "OpenTacticsManagerAsync");
        AssertHandlerCalls(code, "ProtounitAbilities_Click", "OpenAbilitiesManagerAsync");
    }

    [Fact]
    public void ProtoUnitEditor_StillContainsAllFiveEditorTabsInOrder()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var headers = xaml.Descendants()
            .Where(e => e.Name.LocalName == "TabItem")
            .Select(e => (string?)e.Attribute("Header"))
            .Where(header => header is "Stats" or "Actions" or "Commands" or "Abilities" or "Train/Research")
            .ToList();

        Assert.Equal(["Stats", "Actions", "Commands", "Abilities", "Train/Research"], headers);
    }

    [Fact]
    public void ProtoUnitEditor_DoesNotExposeTheRetiredSaveAsWorkflow()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.DoesNotContain(xaml.Descendants(), element =>
            ((string?)element.Attribute("Content"))?.Contains("Save as", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain("SaveAs_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsCoreAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitMenu_ExposesUnitTypeManagerWithoutEditOrDuplicateButtons()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var managerCode = File.ReadAllText(Path.Combine(root, "Windows", "UnitTypeManagerWindow.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Unit Type" &&
            (string?)element.Attribute("Click") == "ProtounitUnitType_Click");
        Assert.DoesNotContain("editButton", managerCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duplicateButton", managerCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Duplicate Unit Type", managerCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitMenu_ExposesIconManagerWithPngImportOnly()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var managerCode = File.ReadAllText(Path.Combine(root, "Windows", "IconManagerWindow.cs"));
        var unitTypeManagerCode = File.ReadAllText(Path.Combine(root, "Windows", "UnitTypeManagerWindow.cs"));
        var sharedShellCode = File.ReadAllText(Path.Combine(root, "Controls", "ManagerListShell.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Icon" &&
            (string?)element.Attribute("Click") == "ProtounitIcon_Click");
        AssertHandlerCalls(code, "ProtounitIcon_Click", "OpenIconManagerAsync");
        Assert.Contains("new ManagerListShell", managerCode, StringComparison.Ordinal);
        Assert.Contains("new ManagerListShell", unitTypeManagerCode, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = addEnabled", sharedShellCode, StringComparison.Ordinal);
        Assert.Contains("OpenFilePickerAsync", managerCode, StringComparison.Ordinal);
        Assert.Contains("*.png", managerCode, StringComparison.Ordinal);
        Assert.Contains("AssetDestinationWindow", managerCode, StringComparison.Ordinal);
        Assert.Contains("item.IsCustom ? $\"Custom · {item.DisplayPath}\" : item.DisplayPath", managerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("deleteButton", managerCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duplicateButton", managerCode, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtoUnitMenu_ExposesSoundSetManagerUsingTheAnimFileManagerProfile()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var managerCode = File.ReadAllText(Path.Combine(root, "Windows", "AnimFileManagerWindow.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Sound Set" &&
            (string?)element.Attribute("Click") == "ProtounitSoundSet_Click");
        AssertHandlerCalls(code, "ProtounitSoundSet_Click", "OpenSoundSetManagerAsync");
        Assert.Contains("XmlAssetManagerProfile.SoundSet", code, StringComparison.Ordinal);
        Assert.Contains(@"game\\sound", managerCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitSave_RestoresThePreviouslySelectedEditorTab()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("var editorTabIndexBeforeSave = _editorTabs.SelectedIndex;", code, StringComparison.Ordinal);
        Assert.Contains("_editorTabs.SelectedIndex = editorTabIndexBeforeSave;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitSwitch_CapturesDraftAndGlobalSaveCommitsEveryDirtyUnit()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("await CaptureCurrentUnitDraftAsync()", code, StringComparison.Ordinal);
        Assert.Contains("_dirtyUnitNames.Add(capturedName)", code, StringComparison.Ordinal);
        Assert.Contains("_unitAbilityDrafts[capturedName] = CloneAbilityDrafts(_abilityDrafts)", code, StringComparison.Ordinal);
        Assert.Contains("SavePendingUnitStringDrafts();", code, StringComparison.Ordinal);
        Assert.Contains("SaveAllPendingAbilityDrafts();", code, StringComparison.Ordinal);
        Assert.Contains("foreach (var unitName in _dirtyUnitNames.OrderBy", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitRename_AppliesNewIdsBeforeCapturingStringValues()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var captureMethod = Regex.Match(
            code,
            @"private async Task<bool> CaptureCurrentUnitDraftAsync\(\).*?(?=\n\s*private async void Save_Click)",
            RegexOptions.Singleline);

        Assert.True(captureMethod.Success, "Could not locate CaptureCurrentUnitDraftAsync.");
        var applyIndex = captureMethod.Value.IndexOf("ApplyCurrentEdits();", StringComparison.Ordinal);
        var stringCaptureIndex = captureMethod.Value.IndexOf("CaptureCurrentUnitStringDraft();", StringComparison.Ordinal);
        Assert.True(applyIndex >= 0 && stringCaptureIndex > applyIndex,
            "A rename must generate and apply its new string IDs before the visible values are captured.");
    }

    [Fact]
    public void SpecializedEditors_DoNotStartTheNormalProtoUnitInitializationPass()
    {
        var root = FindProjectRoot();
        var tacticsCode = File.ReadAllText(Path.Combine(root, "Windows", "TacticsManagerWindow.cs"));
        var abilitiesCode = File.ReadAllText(Path.Combine(root, "Windows", "AbilitiesManagerWindow.cs"));
        var editorCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains(": base(initializeProtoEditor: false)", tacticsCode, StringComparison.Ordinal);
        Assert.Contains(": base(gameData, initializeProtoEditor: false)", abilitiesCode, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(editorCode, @"HideEmptyDocumentPlaceholder\(\);").Count);
        Assert.Contains("private void HideEmptyDocumentPlaceholder()", editorCode, StringComparison.Ordinal);
        Assert.Contains("_emptyDocumentOverlay.IsVisible = false;", editorCode, StringComparison.Ordinal);
        Assert.Contains("await _pendingAbilityEditorLoadTask;", editorCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AbilityManager_LoadsTheGlobalCatalogWhenTheMainDocumentIsStillEmpty()
    {
        var root = FindProjectRoot();
        var editorCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("EnsureAbilityCatalogLoadedForManager();", editorCode, StringComparison.Ordinal);
        Assert.Contains("LoadAbilitySources(\"__ability_manager_catalog__\")", editorCode, StringComparison.Ordinal);
        Assert.Contains("var preservedDrafts = CloneAbilityDrafts(_abilityDrafts);", editorCode, StringComparison.Ordinal);
        Assert.Contains("foreach (var entry in preservedDrafts)", editorCode, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneTacticsMode_UsesCompactActionAndTacticTabs()
    {
        var root = FindProjectRoot();
        var editorCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var theme = File.ReadAllText(Path.Combine(root, "Styles", "AoMTheme.axaml"));

        Assert.Equal(2, Regex.Matches(editorCode, "Classes = \\{ \"compact-mode-tab\" \\}").Count);
        Assert.Contains("Selector=\"TabItem.compact-mode-tab\"", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"17\" />", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagerLists_ReserveSharedClearanceForTheVerticalScrollbar()
    {
        var root = FindProjectRoot();
        var sharedShell = File.ReadAllText(Path.Combine(root, "Controls", "ManagerListShell.cs"));
        var managerFiles = new[]
        {
            "AbilitiesManagerWindow.cs",
            "TacticsManagerWindow.cs",
            "ProtoUnitCommandsManagerWindow.cs"
        };

        Assert.Contains("ScrollBarClearance = 20", sharedShell, StringComparison.Ordinal);
        Assert.Contains("new Thickness(0, 0, ScrollBarClearance, 0)", sharedShell, StringComparison.Ordinal);
        foreach (var managerFile in managerFiles)
        {
            var managerCode = File.ReadAllText(Path.Combine(root, "Windows", managerFile));
            Assert.Contains("ManagerListShell.ScrollBarClearance", managerCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManagerHeadersAndEntityCountFootersUseSharedFormatting()
    {
        var root = FindProjectRoot();
        var sharedShell = File.ReadAllText(Path.Combine(root, "Controls", "ManagerListShell.cs"));
        var managerFiles = new[]
        {
            "AbilitiesManagerWindow.cs",
            "AnimFileManagerWindow.cs",
            "IconManagerWindow.cs",
            "ProtoUnitCommandsManagerWindow.cs",
            "TacticsManagerWindow.cs",
            "UnitTypeManagerWindow.cs"
        };

        Assert.Contains("HeaderControlHeight = 36", sharedShell, StringComparison.Ordinal);
        Assert.Contains("Height = HeaderControlHeight", sharedShell, StringComparison.Ordinal);
        Assert.Contains("FormatEntityCountFooter", sharedShell, StringComparison.Ordinal);
        foreach (var managerFile in managerFiles)
        {
            var managerCode = File.ReadAllText(Path.Combine(root, "Windows", managerFile));
            Assert.Contains("FormatEntityCountFooter", managerCode, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LargeAssetManagers_UseTheOptInVirtualizedListWithoutSearchGate()
    {
        var root = FindProjectRoot();
        var sharedShell = File.ReadAllText(Path.Combine(root, "Controls", "ManagerListShell.cs"));
        var animManager = File.ReadAllText(Path.Combine(root, "Windows", "AnimFileManagerWindow.cs"));
        var iconManager = File.ReadAllText(Path.Combine(root, "Windows", "IconManagerWindow.cs"));

        Assert.Contains("CreateVirtualizedList<T>", sharedShell, StringComparison.Ordinal);
        Assert.Contains("new VirtualizingStackPanel", sharedShell, StringComparison.Ordinal);
        Assert.Contains("CacheLength = 0.5", sharedShell, StringComparison.Ordinal);
        Assert.Contains("supportsRecycling: false", sharedShell, StringComparison.Ordinal);
        Assert.Contains("item is null", sharedShell, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = false", sharedShell, StringComparison.Ordinal);

        Assert.Contains("CreateVirtualizedList<AnimFileCatalogEntry>(CreateRow)", animManager, StringComparison.Ordinal);
        Assert.Contains("CreateVirtualizedList<IconManagerItem>(CreateRow)", iconManager, StringComparison.Ordinal);
        Assert.Contains("shell.ReplaceItemsHost(_itemsList)", animManager, StringComparison.Ordinal);
        Assert.Contains("shell.ReplaceItemsHost(_itemsList)", iconManager, StringComparison.Ordinal);
        Assert.DoesNotContain("Type at least 3 characters", animManager, StringComparison.Ordinal);
        Assert.DoesNotContain("Type at least 3 characters", iconManager, StringComparison.Ordinal);
        Assert.DoesNotContain("_itemsPanel.Children", animManager, StringComparison.Ordinal);
        Assert.DoesNotContain("_itemsPanel.Children", iconManager, StringComparison.Ordinal);

        // Search results must correspond to the visible name, not hidden path,
        // archive, or source metadata (Anim File and Sound Set share this code).
        Assert.Contains("GetFileName(item.Path).Contains(search", animManager, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Path.Contains(search", animManager, StringComparison.Ordinal);
        Assert.DoesNotContain("item.ArchiveName.Contains(search", animManager, StringComparison.Ordinal);
        Assert.Contains("item.Name.Contains(search", iconManager, StringComparison.Ordinal);
        Assert.DoesNotContain("item.DisplayPath.Contains(search", iconManager, StringComparison.Ordinal);
        Assert.DoesNotContain("UITextureCache.bar", iconManager, StringComparison.Ordinal);

        // Anim File and Sound Set intentionally share this window/profile, so their
        // edit, duplicate and custom move actions must remain in the virtualized row.
        Assert.Contains("CreateEditButton(item)", animManager, StringComparison.Ordinal);
        Assert.Contains("CreateDuplicateButton(item)", animManager, StringComparison.Ordinal);
        Assert.Contains("OpenMoveDialogAsync(item)", animManager, StringComparison.Ordinal);
        Assert.Contains("OpenMoveDialogAsync(item)", iconManager, StringComparison.Ordinal);
    }

    [Fact]
    public void UnitAndTechnologyEditors_UseTheSharedPngAndDdsIconPreview()
    {
        var root = FindProjectRoot();
        var project = File.ReadAllText(Path.Combine(root, "AoMDivineDataEditor.csproj"));
        var unitEditor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var technologyEditor = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var previewService = File.ReadAllText(Path.Combine(root, "Classes", "IconPreviewService.cs"));
        var ddsDecoder = File.ReadAllText(Path.Combine(root, "Classes", "DdsIconDecoder.cs"));
        var previewControl = File.ReadAllText(Path.Combine(root, "Controls", "IconPreviewControl.cs"));

        Assert.Contains("BCnEncoder.Net", project, StringComparison.Ordinal);
        Assert.DoesNotContain("CryBarEditor", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new IconPreviewControl(_iconPreviewService)", unitEditor, StringComparison.Ordinal);
        Assert.Contains("new IconPreviewControl(_iconPreviewService)", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("DdsIconDecoder.ConvertToPngBytesAsync", previewService, StringComparison.Ordinal);
        Assert.Contains("new BcDecoder().DecodeAsync(dds)", ddsDecoder, StringComparison.Ordinal);
        Assert.Contains("ChangeExtension(normalizedPath, \".dds\")", previewService, StringComparison.Ordinal);
        Assert.Contains("Width = 132", previewControl, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(2)", previewControl, StringComparison.Ordinal);
        Assert.Contains("Bitmap.DecodeToWidth(stream, 128", previewControl, StringComparison.Ordinal);
        Assert.Contains("ShowOptionsAsync", previewControl, StringComparison.Ordinal);
        Assert.Contains("_cycleButton.Click", previewControl, StringComparison.Ordinal);
        Assert.Contains("_cycleButton.IsVisible = _options.Count > 1", previewControl, StringComparison.Ordinal);
        Assert.Contains("AttachedToVisualTree", previewControl, StringComparison.Ordinal);
        Assert.Contains("_ = ShowCurrentOptionAsync()", previewControl, StringComparison.Ordinal);
        Assert.Contains("propertiesLayout.Children.Add(propertiesGrid)", unitEditor, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(IconPreviewControl.PropertyGridLeftOffset", unitEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumn(iconPreview, 1)", unitEditor, StringComparison.Ordinal);
        Assert.Contains("RefreshCultureIconPreview()", unitEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("displayRow.Children.Add(iconPreview)", unitEditor, StringComparison.Ordinal);
        Assert.Contains("var identityFields = new StackPanel", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(IconPreviewControl.PropertyGridLeftOffset", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("_propertiesPanel.IsEnabled = _current != null", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("AddPrimaryTechnologyRowAsync(tech, displayName, identityFields)", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("target.Children.Add(displayGrid)", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("target.Children.Add(metadataGrid)", technologyEditor, StringComparison.Ordinal);
        Assert.Contains("metadataRow.Children.Add(segment)", technologyEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"Add Editor Name\"", unitEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("removeEditorNameButton", unitEditor, StringComparison.Ordinal);
        Assert.Contains("IsVisible = !_isReadOnly || !string.IsNullOrWhiteSpace(editorNameId)", unitEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherSpecificAutocomplete_UsesSharedAnchoredPopupScrolling()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var autoCompleteCode = File.ReadAllText(Path.Combine(root, "Classes", "EditorAutoCompleteService.cs"));

        var helper = Regex.Match(code,
            @"AutoCompleteBox CreateOtherSuggestionBox\(.*?(?=\n\s*AutoCompleteBox CreateValidatedOtherSuggestionBox)",
            RegexOptions.Singleline);

        Assert.True(helper.Success, "Could not find the Other Specific autocomplete helper.");
        Assert.Contains("FilterMode = AutoCompleteFilterMode.Contains", helper.Value, StringComparison.Ordinal);
        Assert.Contains("selectAllOnFirstClick: false", helper.Value, StringComparison.Ordinal);
        Assert.Contains("FreezeEditorScrollWhileDropDownIsOpen(acb, _editorScroll);", helper.Value, StringComparison.Ordinal);
        Assert.Contains("autoCompleteBox.IsDropDownOpen && sourceIsInsideEditor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepDropDownAnchoredDuringOwnerScroll", autoCompleteCode, StringComparison.Ordinal);
    }

    private static void AssertHandlerCalls(string code, string handler, string target)
    {
        var match = Regex.Match(code,
            $@"{Regex.Escape(handler)}\s*\([^)]*\)\s*\{{(?<body>.*?)\n\s*\}}",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"Could not find handler {handler}.");
        Assert.Contains(target, match.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var direct = Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj");
            if (File.Exists(direct))
                return directory.FullName;

            // Normal test output is .../AoMDivineDataEditor/AoMDivineDataEditor.Tests/bin/...;
            // this handles running the tests from any build configuration.
            var sibling = Path.Combine(directory.FullName, "AoMDivineDataEditor", "AoMDivineDataEditor.csproj");
            if (File.Exists(sibling))
                return Path.Combine(directory.FullName, "AoMDivineDataEditor");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj from the test output directory.");
    }
}
