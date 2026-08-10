using CryBarEditor.Classes;
using Xunit;

namespace AoMProtoEditor.Tests;

public sealed class ProtoUnitCommandHardeningPass678Tests
{
    [Fact]
    public void NamePolicy_RejectsDataBarOrCustomDuplicateNames()
    {
        var existing = new[] { "OriginalCommand", "CustomCommand" };

        Assert.False(ProtoUnitCommandNamePolicy.IsAvailable("OriginalCommand", existing));
        Assert.False(ProtoUnitCommandNamePolicy.IsAvailable("customcommand", existing));
        Assert.True(ProtoUnitCommandNamePolicy.IsAvailable("NewCommand", existing));
    }

    [Fact]
    public void NamePolicy_AllowsCurrentNameDuringRenameOnly()
    {
        var existing = new[] { "CommandA", "CommandB" };

        Assert.True(ProtoUnitCommandNamePolicy.IsAvailable("CommandA", existing, "CommandA"));
        Assert.False(ProtoUnitCommandNamePolicy.IsAvailable("CommandB", existing, "CommandA"));
    }

    [Fact]
    public void ManagerCreationAndDuplication_UseGlobalNamePolicy()
    {
        var root = FindProjectRoot();
        var protoEditor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("IsProtoUnitCommandNameAvailable(name)", protoEditor, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitCommandNamePolicy.IsAvailable(newName, _protoUnitCommandCatalog.Keys)", protoEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformAssignmentController_BuildsUniqueAndMultipleCommandEntries()
    {
        var controller = new ProtoUnitTransformAssignmentController
        {
            new TransformCommandAssignmentState { CommandName = "UniqueTransform", IsMultiple = false, Row = "1", Column = "2" },
            new TransformCommandAssignmentState { CommandName = "MultipleTransform", IsMultiple = true, Row = "3", Column = "4" },
            new TransformCommandAssignmentState { CommandName = "WrongKind", IsMultiple = true, Row = "5", Column = "6" }
        };

        var entries = controller.BuildCommandEntries(
            name => name.Equals("UniqueTransform", StringComparison.OrdinalIgnoreCase),
            name => name.Equals("MultipleTransform", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Value == "UniqueTransform" && entry.Row == "1" && entry.Column == "2");
        Assert.Contains(entries, entry => entry.Value == "MultipleTransform" && entry.Row == "3" && entry.Column == "4");
        Assert.DoesNotContain(entries, entry => entry.Value == "WrongKind");
        Assert.Equal("UniqueTransform", controller.UniqueAssignment?.CommandName);
    }

    [Fact]
    public void TransformAssignmentStateAndRemovalDialog_AreExtractedFromProtoEditorWindow()
    {
        var root = FindProjectRoot();
        var protoEditor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.DoesNotContain("private sealed class TransformCommandAssignmentState", protoEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowTransformRemovalChoiceAsync", protoEditor, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitTransformAssignmentController", protoEditor, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitTransformRemovalDialog.ShowAsync", protoEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandStringCleanupFailures_AreReportedInsteadOfSilentlyIgnored()
    {
        var root = FindProjectRoot();
        var protoEditor = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("CleanupProtoUnitCommandStringsWithWarningAsync", protoEditor, StringComparison.Ordinal);
        Assert.Contains("String cleanup incomplete", protoEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("try { CleanupUnreferencedProtoUnitCommandStrings", protoEditor, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var direct = Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj");
            if (File.Exists(direct))
                return directory.FullName;

            var sibling = Path.Combine(directory.FullName, "AoMDivineDataEditor", "AoMDivineDataEditor.csproj");
            if (File.Exists(sibling))
                return Path.Combine(directory.FullName, "AoMDivineDataEditor");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj from the test output directory.");
    }
}
