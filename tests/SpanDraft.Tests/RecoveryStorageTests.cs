using System.Text;
using SpanDraft.Desktop.Persistence;
using Xunit;

namespace SpanDraft.Tests;

public sealed class RecoveryStorageTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Runs one check against every storage implementation, so they cannot drift apart.</summary>
    private static async Task ForEachStorage(Func<IRecoveryStorage, Func<string, string[]>, Task> check)
    {
        await check(new InMemoryRecoveryStorage(), _ => []);
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            await check(new FileRecoveryStorage(directory), relative =>
                Directory.Exists(Path.Combine(directory, relative)) ? Directory.GetFiles(Path.Combine(directory, relative)).Select(f => Path.GetFileName(f)!).ToArray() : []);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SlotNamesAreControlledAndParseBack()
    {
        var id = RecoveryId.New();
        Assert.Equal("project-" + id + ".json", RecoverySlot.Project(id).FileName);
        Assert.Equal("setup-" + id + ".json", RecoverySlot.Setup(id).FileName);
        Assert.Equal("recovery.json", RecoverySlot.LegacyProject.FileName);
        Assert.Equal("setup-recovery.json", RecoverySlot.LegacySetup.FileName);
        Assert.Equal(32, id.ToString().Length); Assert.Equal(id.ToString().ToLowerInvariant(), id.ToString());
        foreach (var slot in new[] { RecoverySlot.Project(id), RecoverySlot.Setup(id), RecoverySlot.LegacyProject, RecoverySlot.LegacySetup })
        {
            Assert.True(RecoverySlot.TryParse(slot.FileName, out var parsed)); Assert.Equal(slot, parsed);
        }
        Assert.True(RecoverySlot.LegacyProject.IsLegacy); Assert.False(RecoverySlot.Project(id).IsLegacy);
    }

    [Theory]
    [InlineData("project-.json")]
    [InlineData("project-1234.json")]
    [InlineData("project-ABCDEF0123456789ABCDEF0123456789.json")]
    [InlineData("project-0123456789abcdef-0123456789abcdef.json")]
    [InlineData("project-0123456789abcdef0123456789abcdef.json.tmp")]
    [InlineData(".project-0123456789abcdef0123456789abcdef.json.1234.tmp")]
    [InlineData("setup-0123456789abcdef0123456789abcdefg.json")]
    [InlineData("other-0123456789abcdef0123456789abcdef.json")]
    [InlineData("recovery.json.bak")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void ForeignTemporaryAndNonCanonicalNamesAreIgnored(string name) =>
        Assert.False(RecoverySlot.TryParse(name, out _));

    [Fact]
    public void RecoveryIdsCompareOrdinally()
    {
        var low = new RecoveryId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = new RecoveryId(Guid.Parse("f0000000-0000-0000-0000-000000000000"));
        Assert.True(low.CompareTo(high) < 0); Assert.True(high.CompareTo(low) > 0); Assert.Equal(0, low.CompareTo(low));
    }

    [Fact]
    public async Task WriteReadDeleteAndEnumerateWorkForEveryStorage() => await ForEachStorage(async (storage, _) =>
    {
        Assert.Empty(await storage.EnumerateAsync(TestContext.Current.CancellationToken));                       // a missing recovery area is empty
        Assert.Null(await storage.ReadAsync(RecoverySlot.Project(RecoveryId.New())));
        var a = RecoverySlot.Project(RecoveryId.New()); var b = RecoverySlot.Setup(RecoveryId.New()); var c = RecoverySlot.Project(RecoveryId.New());
        await storage.WriteAtomicAsync(a, Bytes("a")); await storage.WriteAtomicAsync(b, Bytes("b")); await storage.WriteAtomicAsync(c, Bytes("c"));
        await storage.WriteAtomicAsync(a, Bytes("a2"));                      // overwrite publishes the complete new content
        Assert.Equal(Bytes("a2"), await storage.ReadAsync(a));
        Assert.Equivalent(new[] { a, b, c }, await storage.EnumerateAsync(TestContext.Current.CancellationToken));
        await storage.DeleteAsync(b); await storage.DeleteAsync(b);         // deleting twice is fine
        Assert.Null(await storage.ReadAsync(b));
        Assert.Equivalent(new[] { a, c }, await storage.EnumerateAsync(TestContext.Current.CancellationToken));
    });

    [Fact]
    public async Task ACancelledWriteKeepsThePreviousContent() => await ForEachStorage(async (storage, _) =>
    {
        var slot = RecoverySlot.Project(RecoveryId.New());
        await storage.WriteAtomicAsync(slot, Bytes("old"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.WriteAtomicAsync(slot, Bytes("new"), cancelled.Token));
        Assert.Equal(Bytes("old"), await storage.ReadAsync(slot));
    });

    [Fact]
    public async Task MovingALegacySlotIsAtomicAndNeverOverwrites() => await ForEachStorage(async (storage, _) =>
    {
        var target = RecoverySlot.Project(RecoveryId.New());
        await storage.WriteAtomicAsync(RecoverySlot.LegacyProject, Bytes("legacy"));
        await storage.MoveAsync(RecoverySlot.LegacyProject, target);
        Assert.Null(await storage.ReadAsync(RecoverySlot.LegacyProject)); Assert.Equal(Bytes("legacy"), await storage.ReadAsync(target));
        // Occupied target: the move fails and both stay exactly as they were.
        await storage.WriteAtomicAsync(RecoverySlot.LegacyProject, Bytes("second"));
        await Assert.ThrowsAnyAsync<IOException>(() => storage.MoveAsync(RecoverySlot.LegacyProject, target));
        Assert.Equal(Bytes("second"), await storage.ReadAsync(RecoverySlot.LegacyProject)); Assert.Equal(Bytes("legacy"), await storage.ReadAsync(target));
        // Missing source: nothing appears at the target.
        var other = RecoverySlot.Setup(RecoveryId.New());
        await Assert.ThrowsAnyAsync<IOException>(() => storage.MoveAsync(RecoverySlot.LegacySetup, other));
        Assert.Null(await storage.ReadAsync(other));
    });

    [Fact]
    public async Task DiscardMovesTheSlotToATombstoneInsteadOfDeletingIt() => await ForEachStorage(async (storage, list) =>
    {
        var slot = RecoverySlot.Project(RecoveryId.New());
        await storage.WriteAtomicAsync(slot, Bytes("project"));
        await storage.DiscardAsync(slot);
        Assert.Null(await storage.ReadAsync(slot)); Assert.Empty(await storage.EnumerateAsync(TestContext.Current.CancellationToken));
        if (storage is FileRecoveryStorage) Assert.Equal(slot.FileName, Assert.Single(list(Path.Combine("Recovery", "Discarded"))));
        else Assert.Equal(slot.FileName, Assert.Single(((InMemoryRecoveryStorage)storage).DiscardedNames));
        // A legacy slot gets a unique tombstone name: it has no id of its own.
        await storage.WriteAtomicAsync(RecoverySlot.LegacySetup, Bytes("legacy"));
        await storage.DiscardAsync(RecoverySlot.LegacySetup);
        Assert.Empty(await storage.EnumerateAsync(TestContext.Current.CancellationToken));
        string[] names = storage is FileRecoveryStorage ? list(Path.Combine("Recovery", "Discarded")) : ((InMemoryRecoveryStorage)storage).DiscardedNames.ToArray();
        Assert.Equal(2, names.Length); Assert.Contains(names, n => n.StartsWith("legacy-setup-recovery-", StringComparison.Ordinal));
    });

    [Fact]
    public async Task OnlyAMissingSourceSlotIsReportedAsMissingByTheDiscard() => await ForEachStorage(async (storage, list) =>
    {
        // No recovery area at all, and an area without the slot: both are a missing source.
        var missing = RecoverySlot.Setup(RecoveryId.New());
        var error = await Assert.ThrowsAnyAsync<IOException>(() => storage.DiscardAsync(missing));
        Assert.True(RecoveryErrors.IsMissing(error));
        await storage.WriteAtomicAsync(RecoverySlot.Project(RecoveryId.New()), Bytes("other"));
        Assert.True(RecoveryErrors.IsMissing(await Assert.ThrowsAnyAsync<IOException>(() => storage.DiscardAsync(missing))));
        Assert.True(RecoveryErrors.IsMissing(await Assert.ThrowsAnyAsync<IOException>(() => storage.DiscardAsync(RecoverySlot.LegacyProject))));
    });

    [Fact]
    public async Task AnUnusableDiscardedAreaIsAStorageErrorAndNeverAMissingSlot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileRecoveryStorage(directory);
            var slot = RecoverySlot.Setup(RecoveryId.New());
            await storage.WriteAtomicAsync(slot, Bytes("draft"), TestContext.Current.CancellationToken);
            // A file where the Discarded directory belongs: the target cannot be created, the source is untouched.
            File.WriteAllText(Path.Combine(directory, "Recovery", "Discarded"), "in the way");
            var error = await Assert.ThrowsAnyAsync<IOException>(() => storage.DiscardAsync(slot));
            Assert.False(RecoveryErrors.IsMissing(error));
            Assert.Equal(Bytes("draft"), await storage.ReadAsync(slot));
            // A tombstone of the same name already exists: the discard picks a unique name instead of failing.
            File.Delete(Path.Combine(directory, "Recovery", "Discarded"));
            Directory.CreateDirectory(Path.Combine(directory, "Recovery", "Discarded"));
            File.WriteAllText(Path.Combine(directory, "Recovery", "Discarded", slot.FileName), "older tombstone");
            await storage.DiscardAsync(slot);
            Assert.Null(await storage.ReadAsync(slot));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, "Recovery", "Discarded")).Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task DiscardedAndForeignFilesAreNotEnumeratedAsActiveSlots()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileRecoveryStorage(directory); var slot = RecoverySlot.Project(RecoveryId.New());
            await storage.WriteAtomicAsync(slot, Bytes("x"), TestContext.Current.CancellationToken);
            string recovery = Path.Combine(directory, "Recovery");
            File.WriteAllText(Path.Combine(recovery, "notes.txt"), "foreign");
            File.WriteAllText(Path.Combine(recovery, ".project-" + RecoveryId.New() + ".json.1.tmp"), "temp");
            File.WriteAllText(Path.Combine(recovery, "project-not-an-id.json"), "foreign");
            Directory.CreateDirectory(Path.Combine(recovery, "Discarded"));
            File.WriteAllText(Path.Combine(recovery, "Discarded", RecoverySlot.Setup(RecoveryId.New()).FileName), "old");
            Assert.Equal(new[] { slot }, await storage.EnumerateAsync(TestContext.Current.CancellationToken));
            // A legacy slot in the data directory is listed next to the identified ones.
            File.WriteAllText(Path.Combine(directory, "recovery.json"), "legacy");
            Assert.Equivalent(new[] { slot, RecoverySlot.LegacyProject }, await storage.EnumerateAsync(TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AWriteLeavesNoTemporaryFileBehind()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileRecoveryStorage(directory); var slot = RecoverySlot.Project(RecoveryId.New());
            await storage.WriteAtomicAsync(slot, Bytes("x"), TestContext.Current.CancellationToken); await storage.WriteAtomicAsync(slot, Bytes("y"), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { slot.FileName }, Directory.GetFiles(Path.Combine(directory, "Recovery")).Select(Path.GetFileName));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task TheStorageDoesNotCreateAnythingBeforeTheFirstWrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-storage-" + Guid.NewGuid().ToString("N"));
        var storage = new FileRecoveryStorage(directory);
        await storage.EnumerateAsync(TestContext.Current.CancellationToken); await storage.ReadAsync(RecoverySlot.LegacyProject); await storage.DeleteAsync(RecoverySlot.Project(RecoveryId.New()));
        Assert.False(Directory.Exists(directory));
    }
}
