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
