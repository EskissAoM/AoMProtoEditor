using System;
using System.IO;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitActionsPolishRegressionTests
{
    [Fact]
    public void ActionsTab_UsesSectionHeaderForTactics()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("AddSectionHeader(\"Tactics\");", source, StringComparison.Ordinal);
        Assert.Contains("AddSectionHeader(\"Proto Actions\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoActionSelector_KeepsStableUnnamedLabel()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains(
            "actionName = string.IsNullOrWhiteSpace(actionName) ? \"Unnamed action\" : actionName.Trim();",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("actionName = \"Unnamed Action\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateProtoActionName_BlocksTheCardUntilRenamedOrRemoved()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("private bool RefreshProtoActionNameValidation(ProtoActionWidgetState state)", source, StringComparison.Ordinal);
        Assert.Contains("IsProtoActionNameInUse(actionName, state)", source, StringComparison.Ordinal);
        Assert.Contains("state.NameValidationFrame.BorderBrush = Brush.Parse(\"#d64545\")", source, StringComparison.Ordinal);
        Assert.Contains("state.NameValidationFrame.BorderThickness = new Thickness(0);", source, StringComparison.Ordinal);
        Assert.Contains("state.TypeAcb.IsEnabled = !_isReadOnly && !identityIsLocked && !hasDuplicate && hasActionName;", source, StringComparison.Ordinal);
        Assert.Contains("state.BodyContainer.IsEnabled = !hasDuplicate;", source, StringComparison.Ordinal);
        Assert.Contains("BlockForDuplicateSelectedActionName()", source, StringComparison.Ordinal);
        Assert.Contains("RefreshAllProtoActionNameValidation();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("You can continue editing, but saving is blocked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateDraftName_DoesNotAcquireTacticsIdentity()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("var renderedNameCount = _protoActionWidgets.Count", source, StringComparison.Ordinal);
        Assert.Contains("renderedNameCount <= 1", source, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Windows",
            "ProtoEditorWindow.axaml.cs"));
        return File.ReadAllText(path);
    }
}
