using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class NavigationRegressionTests
{
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
    public void ProtoUnitMenu_ExposesUnitTypeManagerWithoutAnEditButton()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var managerCode = File.ReadAllText(Path.Combine(root, "Windows", "UnitTypeManagerWindow.cs"));

        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Content") == "Unit Type" &&
            (string?)element.Attribute("Click") == "ProtounitUnitType_Click");
        Assert.DoesNotContain("editButton", managerCode, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duplicate Unit Type", managerCode, StringComparison.Ordinal);
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
        Assert.Contains("item.IsCustom ? \"Custom\" : \"UITextureCache.bar\"", managerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("deleteButton", managerCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duplicateButton", managerCode, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains(": base(initializeProtoEditor: false)", tacticsCode, StringComparison.Ordinal);
        Assert.Contains(": base(gameData, initializeProtoEditor: false)", abilitiesCode, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherSpecificAutocomplete_KeepsContainsFilteringAndDoesNotInterceptPopupScrolling()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        var helper = Regex.Match(code,
            @"AutoCompleteBox CreateOtherSuggestionBox\(.*?(?=\n\s*AutoCompleteBox CreateValidatedOtherSuggestionBox)",
            RegexOptions.Singleline);

        Assert.True(helper.Success, "Could not find the Other Specific autocomplete helper.");
        Assert.Contains("FilterMode = AutoCompleteFilterMode.Contains", helper.Value, StringComparison.Ordinal);
        Assert.Contains("selectAllOnFirstClick: false", helper.Value, StringComparison.Ordinal);
        Assert.Contains("PointerWheelChangedEvent", helper.Value, StringComparison.Ordinal);
        Assert.Contains("sourceIsInsideEditor", helper.Value, StringComparison.Ordinal);
        Assert.Contains("GetVisualAncestors()", helper.Value, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true", helper.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("acb.IsDropDownOpen = false", helper.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollChanged +=", helper.Value, StringComparison.Ordinal);
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
