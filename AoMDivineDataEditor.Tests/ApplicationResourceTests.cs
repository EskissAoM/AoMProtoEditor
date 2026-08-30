using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using AoMDivineDataEditor.Classes;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ApplicationResourceTests
{
    [Fact]
    public void Window_icon_is_embedded_under_the_current_assembly_name()
    {
        EnsureHeadlessApplication();

        string assemblyName = typeof(SimpleWindow).Assembly.GetName().Name!;
        var iconUri = new Uri($"avares://{assemblyName}/Assets/editor_icon.png");

        Assert.True(AssetLoader.Exists(iconUri));
        using var icon = AssetLoader.Open(iconUri);
        Assert.True(icon.Length > 0);
    }

    [Fact]
    public void Application_uses_the_shared_AoM_visual_theme()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var app = XDocument.Load(Path.Combine(root, "App.axaml"));
        var themePath = Path.Combine(root, "Styles", "AoMTheme.axaml");

        Assert.Contains(app.Descendants(), element =>
            element.Name.LocalName == "StyleInclude" &&
            (string?)element.Attribute("Source") == "/Styles/AoMTheme.axaml");
        Assert.True(File.Exists(themePath));

        var theme = File.ReadAllText(themePath);
        Assert.Contains("x:Key=\"AppBackgroundBrush\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MainGoldBrush\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DangerBrush\"", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.positive\"", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.danger\"", theme, StringComparison.Ordinal);
        Assert.Contains("Border.XmlPanel", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.ability-tab.active\"", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.add-item\"", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.add-component\"", theme, StringComparison.Ordinal);
        Assert.Contains("Selector=\"Button.copy-import\"", theme, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"CheckBox:checked\">", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void Creation_component_and_copy_actions_use_distinct_semantic_styles()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var editor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var optionalFields = File.ReadAllText(Path.Combine(root, "Controls", "OptionalFieldHost.cs"));

        AssertButtonClass(editor, "Add Proto Action", "add-item");
        AssertButtonClass(editor, "Add Ability", "add-item");
        AssertButtonClass(editor, "Copy From Another Unit", "copy-import");
        AssertButtonClass(editor, "Add On Hit Effect", "add-component");
        Assert.Contains("Classes = { \"add-component\" }", optionalFields, StringComparison.Ordinal);
    }

    [Fact]
    public void Ability_tabs_actions_and_chips_have_distinct_visual_roles()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var editor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var chips = File.ReadAllText(Path.Combine(root, "Classes", "EditorChipService.cs"));

        var abilitiesStart = editor.IndexOf("private void BuildAbilitiesEditor", StringComparison.Ordinal);
        var abilitiesEnd = editor.IndexOf("internal static bool AbilityRangeIndicatorHasRequiredRange", abilitiesStart, StringComparison.Ordinal);
        Assert.True(abilitiesStart >= 0 && abilitiesEnd > abilitiesStart);
        var abilities = editor[abilitiesStart..abilitiesEnd];

        Assert.Contains("AddSectionHeader(\"Abilities\")", abilities, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"ability-tab\" }", abilities, StringComparison.Ordinal);
        Assert.Contains("button.Classes.Add(\"active\")", abilities, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Ability\"", abilities, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"add-item\" }", abilities, StringComparison.Ordinal);
        Assert.Contains("#193A52", chips, StringComparison.Ordinal);
        Assert.Contains("#3D7898", chips, StringComparison.Ordinal);
        Assert.Contains("#D9EEF7", chips, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_previews_use_the_shared_syntax_highlighting_editor()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var unitView = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var technologyView = XDocument.Load(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var unitCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var technologyCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var commandCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var assetViewerCode = File.ReadAllText(Path.Combine(root, "Windows", "AnimFileViewerWindow.cs"));
        var syntaxEditorCode = File.ReadAllText(Path.Combine(root, "Classes", "XmlSyntaxEditorService.cs"));
        var highlighting = File.ReadAllText(Path.Combine(root, "Assets", "AoMXml.xshd"));

        Assert.Contains(unitView.Descendants(), element =>
            element.Name.LocalName == "TextEditor" && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "_xmlPreviewText"));
        Assert.Contains(technologyView.Descendants(), element =>
            element.Name.LocalName == "TextEditor" && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "_xmlPreview"));
        Assert.Contains("XmlSyntaxEditorService.Configure(_xmlPreviewText)", unitCode, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(_xmlPreview)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(_xmlPreview)", commandCode, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(preview)", assetViewerCode, StringComparison.Ordinal);
        Assert.Contains("var rawXmlTb = new TextEditor", unitCode, StringComparison.Ordinal);
        Assert.Contains("AddRawEffectXmlEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("XmlSyntaxEditorService.Configure(box)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("editor.Options.AllowScrollBelowDocument = false", syntaxEditorCode, StringComparison.Ordinal);
        Assert.Contains("Caret.CaretBrush = isReadOnly", syntaxEditorCode, StringComparison.Ordinal);
        Assert.Contains("Brushes.Transparent", syntaxEditorCode, StringComparison.Ordinal);
        Assert.Contains("name=\"XmlTag\"", highlighting, StringComparison.Ordinal);
        Assert.Contains("name=\"AttributeName\"", highlighting, StringComparison.Ordinal);
        Assert.Contains("name=\"AttributeValue\"", highlighting, StringComparison.Ordinal);
        Assert.Contains("name=\"Comment\"", highlighting, StringComparison.Ordinal);
    }

    private static void EnsureHeadlessApplication()
    {
        if (Application.Current is not null)
            return;

        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }

    private static void AssertButtonClass(string source, string content, string expectedClass)
    {
        var contentIndex = source.IndexOf($"Content = \"{content}\"", StringComparison.Ordinal);
        Assert.True(contentIndex >= 0, $"Could not find the '{content}' button.");

        var snippetLength = Math.Min(240, source.Length - contentIndex);
        var buttonSnippet = source.Substring(contentIndex, snippetLength);
        Assert.Contains($"Classes = {{ \"{expectedClass}\" }}", buttonSnippet, StringComparison.Ordinal);
    }
}
