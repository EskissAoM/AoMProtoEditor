using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class FileDiscoverySnapshotTests
{
    [Theory]
    [InlineData("new.xml")]
    [InlineData("nested/new/actions.tactics")]
    public void NewlyDiscoveredReferenceFileIsRejected(string relativePath)
    {
        using var workspace = new TestWorkspace("f06-discovery-added");
        var original = workspace.WriteText("proto_mods.xml", "<proto/>");
        var originalBytes = File.ReadAllBytes(original);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);
        var added = workspace.WriteText(relativePath, "<root><tech>Old</tech></root>");
        var addedBytes = File.ReadAllBytes(added);

        var error = Assert.Throws<IOException>(snapshot.Validate);

        Assert.Contains("Retry", error.Message);
        Assert.Equal(original, Assert.Single(files).Path);
        Assert.Equal(original, Assert.Single(snapshot.Paths));
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(addedBytes, File.ReadAllBytes(added));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedOrMovedReferenceFileIsRejected(bool move)
    {
        using var workspace = new TestWorkspace("f06-discovery-removed");
        var original = workspace.WriteText("reference.xml", "<root/>");
        ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);
        if (move)
            File.Move(original, workspace.GetPath("renamed.xml"));
        else
            File.Delete(original);

        Assert.Throws<IOException>(snapshot.Validate);
    }

    [Fact]
    public void StableDiscoveryRetainsTheCompleteCatalogAndDoesNotWriteFiles()
    {
        using var workspace = new TestWorkspace("f06-discovery-stable");
        var owner = workspace.WriteText("techtree_mods.xml", "<techtreemods/>");
        var custom = workspace.WriteText("nested/custom.xml", "<custom />");
        var tactics = workspace.WriteText("nested/actions.tactics", "<tactics/>");
        var states = FileIntegrityTransaction.CaptureStates([owner, custom, tactics]);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);

        // Filtering the migration inputs must not shrink the original discovery snapshot.
        var migrationInputs = files.Where(file => file.Path != owner).ToList();
        snapshot.Validate();
        snapshot.Validate();

        Assert.Equal(2, migrationInputs.Count);
        Assert.Equal(3, snapshot.Paths.Count);
        Assert.Contains(owner, snapshot.Paths);
        Assert.Contains(files, file => file.Path == custom && file.Kind == TechnologyReferenceDocumentKind.Other);
        Assert.Contains(files, file => file.Path == tactics && file.Kind == TechnologyReferenceDocumentKind.Tactics);
        foreach (var entry in states)
            Assert.Equal(entry.Value, FileIntegrityTransaction.CaptureState(entry.Key));
    }

    [Fact]
    public void UnrelatedFilesOutsideReferencePolicyDoNotInvalidateDiscovery()
    {
        using var workspace = new TestWorkspace("f06-discovery-unrelated");
        ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);
        workspace.WriteText("notes.txt", "Old");
        workspace.WriteText("images/icon.png", "unrelated");

        snapshot.Validate();

        Assert.Empty(snapshot.Paths);
    }

    [Fact]
    public void EmptyExistingModRootIsValid()
    {
        using var workspace = new TestWorkspace("f06-discovery-empty");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var snapshot);

        snapshot.Validate();

        Assert.Empty(files);
        Assert.Empty(snapshot.Paths);
    }

    [Fact]
    public void MissingRootCannotBeCapturedAsAnEmptyCatalog()
    {
        using var workspace = new TestWorkspace("f06-discovery-missing-root");
        Assert.ThrowsAny<IOException>(() =>
            ModReferenceFileCatalog.Discover(workspace.GetPath("missing"), out _));
    }

    [Fact]
    public void DisappearedEmptyRootInvalidatesAnEmptySnapshot()
    {
        using var workspace = new TestWorkspace("f06-discovery-root-lost");
        var root = workspace.CreateDirectory("mod");
        ModReferenceFileCatalog.Discover(root, out var snapshot);
        Directory.Delete(root);

        Assert.ThrowsAny<IOException>(snapshot.Validate);
    }

    [Fact]
    public void AFileCannotBeUsedAsTheModRoot()
    {
        using var workspace = new TestWorkspace("f06-discovery-file-root");
        var file = workspace.WriteText("not-a-directory.xml", "<root/>");

        Assert.Throws<IOException>(() => ModReferenceFileCatalog.Discover(file, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyRootIsRejected(string root)
    {
        Assert.Throws<ArgumentException>(() => ModReferenceFileCatalog.Discover(root, out _));
    }

    [Fact]
    public void MembershipIsNormalizedCaseInsensitiveAndIndependentOfInputMutation()
    {
        using var workspace = new TestWorkspace("f06-discovery-normalized");
        var path = workspace.GetPath("reference.xml");
        var initial = new List<string> { path, workspace.GetPath("nested/../reference.xml") };
        var snapshot = FileDiscoverySnapshot.Capture(initial, () => [path.ToUpperInvariant()]);
        initial.Clear();

        snapshot.Validate();

        Assert.Equal(path, Assert.Single(snapshot.Paths));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Paths).Clear());
    }

    [Fact]
    public void RediscoveryFailureIsPropagatedInsteadOfAcceptingPartialResults()
    {
        using var workspace = new TestWorkspace("f06-discovery-unreadable");
        var path = workspace.GetPath("reference.xml");
        var failure = new UnauthorizedAccessException("The remaining directory cannot be read.");
        IEnumerable<string> Rediscover()
        {
            yield return path;
            throw failure;
        }
        var snapshot = FileDiscoverySnapshot.Capture([path], Rediscover);

        Assert.Same(failure, Assert.Throws<UnauthorizedAccessException>(snapshot.Validate));
    }

    [Fact]
    public void CallersCanReuseMembershipChecksWithABroaderTextPolicy()
    {
        using var workspace = new TestWorkspace("f06-discovery-text-policy");
        workspace.WriteText("notes.txt", "Old");
        IEnumerable<string> DiscoverText() => Directory.EnumerateFiles(workspace.RootPath, "*.txt");
        var snapshot = FileDiscoverySnapshot.Capture(DiscoverText(), DiscoverText);
        workspace.WriteText("new.txt", "Old");

        Assert.Throws<IOException>(snapshot.Validate);
    }
}
