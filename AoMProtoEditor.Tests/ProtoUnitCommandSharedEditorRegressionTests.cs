using Xunit;

namespace AoMProtoEditor.Tests;

public sealed class ProtoUnitCommandSharedEditorRegressionTests
{
    [Fact]
    public void TransformEditors_StandaloneAndInlineUseTheSameSharedControl()
    {
        var root = FindProjectRoot();
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
        var root = FindProjectRoot();
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));

        Assert.DoesNotContain("BuildTransformDefinitionRows", standalone, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTransformTechRows", standalone, StringComparison.Ordinal);
    }


    [Fact]
    public void TransformFlags_UseSharedEditorInBothHosts()
    {
        var root = FindProjectRoot();
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("new ProtoUnitCommandFlagsEditor(", standalone, StringComparison.Ordinal);
        Assert.Contains("new ProtoUnitCommandFlagsEditor(", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredTransformFlag(string flag)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformValidation_IsDelegatedToSharedEditorInBothHosts()
    {
        var root = FindProjectRoot();
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("_sharedTransformEditor?.ValidateRequired()", standalone, StringComparison.Ordinal);
        Assert.Contains("sharedTransformEditor.ValidateRequired(currentTransformUnitName)", inline, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var direct = Path.Combine(directory.FullName, "AoMProtoEditor.csproj");
            if (File.Exists(direct))
                return directory.FullName;

            var sibling = Path.Combine(directory.FullName, "AoMProtoEditor", "AoMProtoEditor.csproj");
            if (File.Exists(sibling))
                return Path.Combine(directory.FullName, "AoMProtoEditor");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AoMProtoEditor.csproj from the test output directory.");
    }
}
