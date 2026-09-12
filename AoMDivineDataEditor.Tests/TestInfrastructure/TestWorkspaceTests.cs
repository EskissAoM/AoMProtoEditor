using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class TestWorkspaceTests
{
    [Fact]
    public void Workspace_rejects_paths_that_escape_its_owned_temporary_root()
    {
        using var workspace = new TestWorkspace(nameof(Workspace_rejects_paths_that_escape_its_owned_temporary_root));

        Assert.Throws<ArgumentException>(() => workspace.GetPath("../outside.txt"));
    }
}
