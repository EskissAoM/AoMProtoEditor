using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

public sealed class BaselineSafetyTests(ITestOutputHelper output)
{
    [Fact]
    public void FixtureRoundTripAndSuccessfulRollbackPreserveOriginalData()
    {
        string root;
        using (var fixture = new BaselineSafetyFixture())
        {
            root = fixture.Workspace.RootPath;
            var expected = XDocument.Load(fixture.GodPath);
            var copy = fixture.Workspace.GetPath("roundtrip.xml");
            expected.Save(copy);
            Assert.True(XNode.DeepEquals(expected, XDocument.Load(copy)));
            Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([fixture.RecoveryTarget], () =>
            {
                File.WriteAllText(fixture.RecoveryTarget, "changed");
                throw new IOException("injected commit failure");
            }));
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.RecoveryTarget));
        }
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void CleanGodRenameUsesActualHostAndPreservesUnknownXml()
    {
        using var fixture = new BaselineSafetyFixture();
        // A manager can rename references without opening a god for editing first.
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        try
        {
            Assert.False(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.True(AvaloniaTestHost.RunAsync(host.PreflightAsync), AvaloniaTestHost.Run(() => host.GodStatus));
            Assert.True(AvaloniaTestHost.RunAsync(host.RenameAsync));
            var god = XDocument.Load(fixture.GodPath).Root!.Element("civ")!;
            Assert.Equal("RenamedType", (string?)god.Element("contributor")!.Attribute("type"));
            Assert.Equal("yes", (string?)god.Element("future")!.Attribute("preserved"));
            Assert.Equal("A", god.Element("key")!.Value);
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void AbandoningManagerAfterPreflightKeepsSavedGodEditWithoutRenaming()
    {
        using var fixture = new BaselineSafetyFixture();
        var before = File.ReadAllBytes(fixture.TypePath);
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(host.ChangeGodKey);
            Assert.True(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.True(AvaloniaTestHost.RunAsync(host.PreflightAsync), AvaloniaTestHost.Run(() => host.GodStatus));
            // No mutation callback: this models abandoning the manager, not clicking its dialog.
            Assert.Equal("B", AvaloniaTestHost.Run(() => host.God!.CreateModifiedDocumentSnapshot()!.Root!.Element("civ")!.Element("key")!.Value));
            Assert.Equal("B", XDocument.Load(fixture.GodPath).Root!.Element("civ")!.Element("key")!.Value);
            Assert.False(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.Equal(before, File.ReadAllBytes(fixture.TypePath));
        }
        finally
        {
            AvaloniaTestHost.Run(host.DisposeFixtureWindow);

        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F02_FailedRollbackRetainsOriginalRecoveryBytes(bool asynchronous)
    {
        using var fixture = new BaselineSafetyFixture();
        var transactions = Path.Combine(Path.GetTempPath(), "AoMDivineDataEditor", "transactions");
        var before = Directory.Exists(transactions) ? Directory.GetDirectories(transactions).ToHashSet() : [];
        string? backup = null;
        var other = fixture.Workspace.WriteText("other.xml", "other original");
        var created = fixture.Workspace.GetPath("created.xml");
        var commitFailure = new IOException("injected commit failure");
        void FailCommit()
        {
            backup = Assert.Single(Directory.GetDirectories(transactions), path => !before.Contains(path));
            File.WriteAllText(other, "changed");
            File.WriteAllText(created, "new");
            File.Delete(fixture.RecoveryTarget); // The fixture's own file only.
            Directory.CreateDirectory(fixture.RecoveryTarget); // Obstruct rollback deterministically.
            throw commitFailure;
        }
        try
        {
            string[] paths = [other, created, fixture.RecoveryTarget];
            var failure = asynchronous
                ? await Assert.ThrowsAsync<AggregateException>(() => FileIntegrityTransaction.ExecuteAsync(paths, null, async () =>
                {
                    await Task.Yield();
                    FailCommit();
                }))
                : Assert.Throws<AggregateException>(() => FileIntegrityTransaction.Execute(paths, FailCommit));
            output.WriteLine($"Errors={failure.InnerExceptions.Count}; snapshotDirectoryRetained={Directory.Exists(backup)}; originalRestored={File.Exists(fixture.RecoveryTarget)}");
            Assert.True(backup != null && Directory.Exists(backup), "F02: rollback failed and its recovery directory was deleted.");
            Assert.Contains(Directory.GetFiles(backup!, "*", SearchOption.AllDirectories), path => File.ReadAllBytes(path).SequenceEqual(fixture.OriginalBytes));
            Assert.Same(commitFailure, failure.InnerExceptions[0]);
            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.Contains(backup!, failure.Message);
            Assert.Equal("other original", File.ReadAllText(other));
            Assert.False(File.Exists(created));
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(backup!, "recovery.json")));
            var entries = manifest.RootElement.EnumerateArray().ToArray();
            Assert.Equal(3, entries.Length);
            var original = Assert.Single(entries, entry => entry.GetProperty("OriginalPath").GetString() == fixture.RecoveryTarget);
            var snapshotPath = Path.Combine(backup!, original.GetProperty("SnapshotFile").GetString()!);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(snapshotPath));
            Assert.Equal(FileIntegrityTransaction.CaptureState(snapshotPath).Sha256, original.GetProperty("Sha256").GetString());
            var missing = Assert.Single(entries, entry => entry.GetProperty("OriginalPath").GetString() == created);
            Assert.False(missing.GetProperty("Existed").GetBoolean());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, missing.GetProperty("SnapshotFile").ValueKind);
        }
        finally
        {
            // Only remove the transaction directory discovered during this fixture's callback.
            if (backup != null && Path.GetDirectoryName(backup) == transactions && Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompletedTransactionCleansRecoveryFiles(bool asynchronous, bool failCommit)
    {
        using var fixture = new BaselineSafetyFixture();
        var transactions = Path.Combine(Path.GetTempPath(), "AoMDivineDataEditor", "transactions");
        var before = Directory.Exists(transactions) ? Directory.GetDirectories(transactions).ToHashSet() : [];
        string? backup = null;
        var failure = new IOException("injected failure");
        void Commit()
        {
            backup = Assert.Single(Directory.GetDirectories(transactions), path => !before.Contains(path));
            File.WriteAllText(fixture.RecoveryTarget, "changed");
            if (failCommit) throw failure;
        }
        async Task Execute()
        {
            if (asynchronous)
                await FileIntegrityTransaction.ExecuteAsync([fixture.RecoveryTarget], null, async () =>
                {
                    await Task.Yield();
                    Commit();
                });
            else
                FileIntegrityTransaction.Execute([fixture.RecoveryTarget], Commit);
        }
        if (failCommit)
        {
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(Execute));
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.RecoveryTarget));
        }
        else
        {
            await Execute();
            Assert.Equal("changed", File.ReadAllText(fixture.RecoveryTarget));
        }
        Assert.NotNull(backup);
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void F12_ManagerRenameRetainsDirtyGodDraft()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(host.ChangeGodKey);
            Assert.True(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.Equal("B", AvaloniaTestHost.Run(() => host.God!.CreateModifiedDocumentSnapshot()!.Root!.Element("civ")!.Element("key")!.Value));
            Assert.Equal("A", XDocument.Load(fixture.GodPath).Root!.Element("civ")!.Element("key")!.Value);
            Assert.True(AvaloniaTestHost.RunAsync(host.PreflightAsync), AvaloniaTestHost.Run(() => host.GodStatus));
            Assert.True(AvaloniaTestHost.RunAsync(host.RenameAsync));
            var diskGod = XDocument.Load(fixture.GodPath).Root!.Element("civ")!;
            Assert.Equal("RenamedType", (string?)diskGod.Element("contributor")!.Attribute("type"));
            output.WriteLine($"After manager rename: viewRetained={AvaloniaTestHost.Run(() => host.God != null)}; diskKey={diskGod.Element("key")!.Value}; expectedDraftKey=B");
            AvaloniaTestHost.Run(() => host.Call("EnsureMajorGodView"));
            var current = AvaloniaTestHost.Run(() => host.God!.CreateModifiedDocumentSnapshot()!);
            Assert.True(current.Root!.Element("civ")!.Element("key")!.Value == "B",
                "F12: the real host preflight/rename/adoption/recreation path lost the unsaved Major God key B.");
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ManagerRenameRefreshesGodViewAndTabDiscardSnapshot(bool pinned, bool visible)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            var tab = AvaloniaTestHost.Run(() => host.AddGodTab(pinned, visible));
            var previousContent = AvaloniaTestHost.Run(() => host.GodHost.Content);
            AvaloniaTestHost.Run(host.ChangeGodKey);
            Assert.True(AvaloniaTestHost.RunAsync(host.PreflightAsync), AvaloniaTestHost.Run(() => host.GodStatus));
            Assert.Equal("B", AvaloniaTestHost.Run(() => BaselineHost.SavedGod(tab).Element("key")!.Value));
            Assert.True(AvaloniaTestHost.RunAsync(host.RenameAsync));
            AvaloniaTestHost.Run(() =>
            {
                Assert.Equal("CheckpointGod", host.God!.CurrentMajorGodName);
                Assert.True(host.God.IsModifiedMode);
                Assert.Same(visible ? host.God : previousContent, host.GodHost.Content);
                var saved = BaselineHost.SavedGod(tab);
                Assert.Equal("B", saved.Element("key")!.Value);
                Assert.Equal("RenamedType", (string?)saved.Element("contributor")!.Attribute("type"));
                host.God.DiscardMajorGodChanges("CheckpointGod", saved);
                var restored = host.God.CreateModifiedDocumentSnapshot()!.Root!.Element("civ")!;
                Assert.Equal("B", restored.Element("key")!.Value);
                Assert.Equal("RenamedType", (string?)restored.Element("contributor")!.Attribute("type"));
                Assert.Equal("yes", (string?)restored.Element("future")!.Attribute("preserved"));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedGodSaveStopsManagerPreflightAndRetainsDraft(bool invalidKey)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        var originalGod = File.ReadAllBytes(fixture.GodPath);
        var originalType = File.ReadAllBytes(fixture.TypePath);
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                host.ChangeGodKey();
                host.GodHost.Content = null; // Exercise save refusal without a modal error UI.
                if (invalidKey) host.SetInvalidGodKey();
            });
            // Allow reads/fingerprints but deny replacement by the atomic writer.
            using var locked = invalidKey ? null : new FileStream(fixture.GodPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.False(AvaloniaTestHost.RunAsync(host.PreflightAsync));
            Assert.True(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.Equal(invalidKey ? "É" : "B", AvaloniaTestHost.Run(() => host.God!.CreateModifiedDocumentSnapshot()!.Root!.Element("civ")!.Element("key")!.Value));
            Assert.Equal(originalGod, File.ReadAllBytes(fixture.GodPath));
            Assert.Equal(originalType, File.ReadAllBytes(fixture.TypePath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Fact]
    public void ExternalGodChangeWithoutReloadStopsManagerAndKeepsDraft()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        try
        {
            AvaloniaTestHost.Run(host.ChangeGodKey);
            File.AppendAllText(fixture.GodPath, "<!-- external change -->");
            var external = File.ReadAllBytes(fixture.GodPath);
            var types = File.ReadAllBytes(fixture.TypePath);
            AvaloniaTestHost.Run(host.DetachGodDialogOwner);
            Assert.False(AvaloniaTestHost.RunAsync(host.PreflightAsync));

            Assert.True(AvaloniaTestHost.Run(() => host.God!.IsDirty));
            Assert.Equal("B", AvaloniaTestHost.Run(() => host.God!.CreateModifiedDocumentSnapshot()!.Root!.Element("civ")!.Element("key")!.Value));
            Assert.Equal(external, File.ReadAllBytes(fixture.GodPath));
            Assert.Equal(types, File.ReadAllBytes(fixture.TypePath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

}
