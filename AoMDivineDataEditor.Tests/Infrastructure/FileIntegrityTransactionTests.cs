using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class FileIntegrityTransactionTests
{
    [Fact]
    public void Execute_RestoresExistingFilesAndRemovesNewFilesWhenCommitFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(directory, "existing.xml");
            var newPath = Path.Combine(directory, "new.xml");
            File.WriteAllText(existingPath, "before");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                FileIntegrityTransaction.Execute([existingPath, newPath], () =>
                {
                    File.WriteAllText(existingPath, "after");
                    File.WriteAllText(newPath, "created");
                    throw new InvalidOperationException("commit failed");
                }));

            Assert.Equal("commit failed", exception.Message);
            Assert.Equal("before", File.ReadAllText(existingPath));
            Assert.False(File.Exists(newPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_RestoresADeletedFileWhenALaterWriteFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var deletedPath = Path.Combine(directory, "custom.tactics");
            var laterPath = Path.Combine(directory, "stringmods.txt");
            File.WriteAllText(deletedPath, "original tactics");
            File.WriteAllText(laterPath, "original strings");

            Assert.Throws<InvalidOperationException>(() =>
                FileIntegrityTransaction.Execute([deletedPath, laterPath], () =>
                {
                    File.Delete(deletedPath);
                    File.WriteAllText(laterPath, "changed strings");
                    throw new InvalidOperationException("later write failed");
                }));

            Assert.Equal("original tactics", File.ReadAllText(deletedPath));
            Assert.Equal("original strings", File.ReadAllText(laterPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_KeepsEveryCommittedFileWhenCommitSucceeds()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var firstPath = Path.Combine(directory, "first.xml");
            var secondPath = Path.Combine(directory, "second.xml");
            File.WriteAllText(firstPath, "before");

            FileIntegrityTransaction.Execute([firstPath, secondPath, firstPath], () =>
            {
                File.WriteAllText(firstPath, "after");
                File.WriteAllText(secondPath, "created");
            });

            Assert.Equal("after", File.ReadAllText(firstPath));
            Assert.Equal("created", File.ReadAllText(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_BlocksAnExternalChangeBeforeRunningTheCommit()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "technology.xml");
            File.WriteAllText(path, "loaded");
            var expected = FileIntegrityTransaction.CaptureState(path);
            File.WriteAllText(path, "changed elsewhere");
            var commitRan = false;

            var exception = Assert.Throws<IOException>(() =>
                FileIntegrityTransaction.Execute(
                    [path],
                    new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Path.GetFullPath(path)] = expected
                    },
                    () => commitRan = true));

            Assert.False(commitRan);
            Assert.Contains("changed outside the editor", exception.Message, StringComparison.Ordinal);
            Assert.Equal("changed elsewhere", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RestoresAllFilesWhenTheSecondWriteFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var technologyPath = Path.Combine(directory, "techtree_mods.xml");
            var stringsPath = Path.Combine(directory, "stringmods.txt");
            File.WriteAllText(technologyPath, "technology before");
            File.WriteAllText(stringsPath, "strings before");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FileIntegrityTransaction.ExecuteAsync([technologyPath, stringsPath], null, async () =>
                {
                    File.WriteAllText(technologyPath, "technology after");
                    File.WriteAllText(stringsPath, "strings after");
                    await Task.Yield();
                    throw new InvalidOperationException("string write failed");
                }));

            Assert.Equal("technology before", File.ReadAllText(technologyPath));
            Assert.Equal("strings before", File.ReadAllText(stringsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CaptureStates_NormalizesPathsAndIncludesMissingFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(directory, "existing.xml");
            var missingPath = Path.Combine(directory, "missing.xml");
            File.WriteAllText(existingPath, "content");

            var states = FileIntegrityTransaction.CaptureStates(
                [existingPath, Path.Combine(directory, ".", "existing.xml"), missingPath, null]);

            Assert.Equal(2, states.Count);
            Assert.True(states[Path.GetFullPath(existingPath)].Exists);
            Assert.False(states[Path.GetFullPath(missingPath)].Exists);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_DoesNotRewriteUntouchedSnapshotsDuringRollback()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var untouchedPath = Path.Combine(directory, "untouched.xml");
            var changedPath = Path.Combine(directory, "changed.xml");
            File.WriteAllText(untouchedPath, "untouched");
            File.WriteAllText(changedPath, "before");
            var untouchedWriteTime = DateTime.UtcNow.AddHours(-2);
            File.SetLastWriteTimeUtc(untouchedPath, untouchedWriteTime);

            Assert.Throws<InvalidOperationException>(() =>
                FileIntegrityTransaction.Execute([untouchedPath, changedPath], () =>
                {
                    File.WriteAllText(changedPath, "after");
                    throw new InvalidOperationException("failed");
                }));

            Assert.Equal("before", File.ReadAllText(changedPath));
            Assert.Equal(untouchedWriteTime, File.GetLastWriteTimeUtc(untouchedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Execute_RestoresEveryPathWhenAnyWritePositionFails(int failingWriteIndex)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var paths = Enumerable.Range(0, 3)
                .Select(index => Path.Combine(directory, $"file-{index}.xml"))
                .ToArray();
            foreach (var path in paths)
                File.WriteAllText(path, "before");

            Assert.Throws<InvalidOperationException>(() =>
                FileIntegrityTransaction.Execute(paths, () =>
                {
                    for (var index = 0; index < paths.Length; index++)
                    {
                        File.WriteAllText(paths[index], $"after-{index}");
                        if (index == failingWriteIndex)
                            throw new InvalidOperationException($"write {index} failed");
                    }
                }));

            Assert.All(paths, path => Assert.Equal("before", File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_BlocksDeletionOfAFileThatExistedWhenLoaded()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "existing.xml");
            File.WriteAllText(path, "loaded");
            var expected = FileIntegrityTransaction.CaptureStates([path]);
            File.Delete(path);

            var commitRan = false;
            Assert.Throws<IOException>(() =>
                FileIntegrityTransaction.Execute([path], expected, () => commitRan = true));

            Assert.False(commitRan);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_BlocksCreationOfAFileThatWasMissingWhenLoaded()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "new.xml");
            var expected = FileIntegrityTransaction.CaptureStates([path]);
            File.WriteAllText(path, "created elsewhere");

            var commitRan = false;
            Assert.Throws<IOException>(() =>
                FileIntegrityTransaction.Execute([path], expected, () => commitRan = true));

            Assert.False(commitRan);
            Assert.Equal("created elsewhere", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AoMDivineDataEditor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
