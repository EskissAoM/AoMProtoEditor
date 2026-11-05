using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class OriginalCorpusPolicyTests
{
    [Fact]
    public void ExplicitMissingCorpusDoesNotFallBackToAnotherArchive()
    {
        using var workspace = new TestWorkspace("corpus-policy");
        var project = workspace.CreateDirectory("Editor");
        workspace.WriteText("Age of Mythology Retold/game/data/Data.bar", "available fallback");
        var missing = workspace.GetPath("missing.bar");
        Assert.Equal(missing, OriginalCorpus.ResolvePath(missing, project));
        Assert.NotNull(OriginalCorpus.GetSkipReason(missing, required: false));
        Assert.Null(OriginalCorpus.GetSkipReason(missing, required: true));
        Assert.Throws<FileNotFoundException>(() => OriginalCorpus.RequirePath(missing, required: true, updateBaseline: false));
    }

    [Fact]
    public void AvailableCorpusRunsInBothModesAndRequiredModeForbidsBaselineUpdates()
    {
        using var workspace = new TestWorkspace("corpus-policy");
        var path = workspace.WriteText("Data.bar", "path-policy fixture, not an archive");
        Assert.Null(OriginalCorpus.GetSkipReason(path, required: false));
        Assert.Null(OriginalCorpus.GetSkipReason(path, required: true));
        Assert.Equal(path, OriginalCorpus.RequirePath(path, required: true, updateBaseline: false));
        Assert.Throws<InvalidOperationException>(() => OriginalCorpus.RequirePath(path, required: true, updateBaseline: true));
        // Actual corpus tests still parse and validate the archive; path existence is not enough.
    }
}
