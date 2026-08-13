using System.Xml.Linq;
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
        Assert.DoesNotContain("RemoveNodes", techCode, StringComparison.Ordinal);
    }


    [Fact]
    public void TechnologyEditor_ReusesProtoUnitPresentationPrimitives()
    {
        var root = FindProjectRoot();
        var techXaml = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml"));
        var techCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var mainCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("ColumnDefinitions=\"210,5,7*,5,3*\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"_xmlPreviewToggleButton\"", techXaml, StringComparison.Ordinal);
        Assert.Contains("EditorChipService.CreateBlueChip", techCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownResourceTypes", techCode, StringComparison.Ordinal);
        Assert.Contains("new AssetPathEditor", techCode, StringComparison.Ordinal);
        Assert.Contains("ResolveDisplayStringAsync", mainCode, StringComparison.Ordinal);
        Assert.Contains("_baseGameIconPaths.Concat(_customIconPaths)", mainCode, StringComparison.Ordinal);
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
        Assert.Contains("AddDisplayNameAndOrderHintRowAsync", techCode, StringComparison.Ordinal);
        Assert.Contains("CreateNumericTextBox(orderHintAttribute.Value, 50)", techCode, StringComparison.Ordinal);
        Assert.Contains("box.MinHeight = 54", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Properties\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Costs\")", techCode, StringComparison.Ordinal);
        Assert.Contains("AddChipListEditor(tech, \"techtype\", \"Technology Types\")", techCode, StringComparison.Ordinal);
        Assert.Contains("_propertiesPanel.IsEnabled = canEdit", techCode, StringComparison.Ordinal);
        Assert.Contains("_xmlPreview.Opacity = canEdit ? 1.0 : 0.55", techCode, StringComparison.Ordinal);
        Assert.Contains("SaveOptions.DisableFormatting", techCode, StringComparison.Ordinal);
        Assert.Contains("LoadOptions.None).ToString()", techCode, StringComparison.Ordinal);
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
