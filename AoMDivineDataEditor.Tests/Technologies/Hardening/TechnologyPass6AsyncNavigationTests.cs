using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass6AsyncNavigationTests
{
    [Fact]
    public void EditorBuildsAreCancelledAndFailuresAreSurfaced()
    {
        var source = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("Interlocked.Exchange(ref _activeEditorBuildCancellation, cancellation)", source, StringComparison.Ordinal);
        Assert.Contains("_activeEditorBuildTask = task", source, StringComparison.Ordinal);
        Assert.Contains("_activeUiOperations.Add(operation)", source, StringComparison.Ordinal);
        Assert.Contains("await AwaitActiveEditorBuildCompletionAsync();", source, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", source, StringComparison.Ordinal);
        Assert.Contains("Unable to display technology", source, StringComparison.Ordinal);
        Assert.Contains("Technology editor could not be built", source, StringComparison.Ordinal);
        Assert.Contains("_activeEditorBuildCancellation?.Cancel();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectTypeAndSubtypeChangesRemainDirectUserActions()
    {
        var source = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("ResetEffectForType(effect, value)", source, StringComparison.Ordinal);
        Assert.Contains("ResetDataEffectForSubtype(effect, value)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmDestructiveEffectChangeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HostNavigationCommitsTechnologyNamesBeforeChangingDocuments()
    {
        var source = ReadSource("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("!await _technologyView.CommitCurrentTechnologyAsync()", source, StringComparison.Ordinal);
        Assert.Contains("_technologyView?.SelectTechnology(selectedName);", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([ProjectSource.RootPath, .. relativePath]));

}
