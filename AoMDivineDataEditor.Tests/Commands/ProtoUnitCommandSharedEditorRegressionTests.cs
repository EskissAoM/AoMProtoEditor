using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitCommandSharedEditorRegressionTests
{
    [Fact]
    public void TransformEditors_StandaloneAndInlineUseTheSameSharedControl()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var shared = File.ReadAllText(Path.Combine(root, "Controls", "ProtoUnitCommandTransformEditor.cs"));

        Assert.Contains("new ProtoUnitCommandTransformEditor(", standalone, StringComparison.Ordinal);
        Assert.Contains("new ProtoUnitCommandTransformEditor(", inline, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"115,240,Auto,240,*\")", shared, StringComparison.Ordinal);
        Assert.Contains("\"Transform\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"To\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Full heal\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Revert others to\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Prereq Tech\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Associated Tech\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"forbidtech\"", shared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"associatedpower\"", shared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standalone_NoLongerOwnsDuplicateTransformRowBuilders()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));

        Assert.DoesNotContain("BuildTransformDefinitionRows", standalone, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTransformTechRows", standalone, StringComparison.Ordinal);
    }


    [Fact]
    public void TransformFlags_UseSharedEditorInBothHosts()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("new ProtoUnitCommandFlagsEditor(", standalone, StringComparison.Ordinal);
        Assert.Contains("new ProtoUnitCommandFlagsEditor(", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredTransformFlag(string flag)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformValidation_IsDelegatedToSharedEditorInBothHosts()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("_sharedTransformEditor?.ValidateRequired()", standalone, StringComparison.Ordinal);
        Assert.Contains("sharedTransformEditor.ValidateRequired(currentTransformUnitName)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void AllCommandPositions_UseStrictNonNegativeIntegerFields()
    {
        var root = ProjectSource.RootPath;
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("EditorNumericInputBehavior.AttachRule(textBox, ProtoUnitNumericKind.UnsignedInteger)", inline, StringComparison.Ordinal);
        Assert.Contains("_trainCommandRows, useNumericPositionFields: true", inline, StringComparison.Ordinal);
        Assert.Contains("_techCommandRows, useNumericPositionFields: true", inline, StringComparison.Ordinal);
        Assert.Contains("regularCommandNames, _unitCommandRows, useNumericPositionFields: true);", inline, StringComparison.Ordinal);
        Assert.Contains("regularCommandNames, _optionalCommandRows, useNumericPositionFields: true);", inline, StringComparison.Ordinal);
    }

}
