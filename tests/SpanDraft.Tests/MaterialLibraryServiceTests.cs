using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class MaterialLibraryServiceTests
{
    private const string Path_ = "materials";

    private static Material Entry(string name, double modulus = 210e9) => LibraryTestSupport.MaterialEntry(name, modulus);

    private static MaterialLibraryService Create(Files files, params Material[] existing)
    {
        if (existing.Length > 0) files.Data[Path_] = MaterialLibraryCodec.Serialize(new(existing));
        var service = new MaterialLibraryService(new MaterialLibraryStore(files, Path_));
        service.LoadAsync().GetAwaiter().GetResult();
        return service;
    }

    [Fact]
    public async Task BeforeTheLoadTheLibraryIsUnavailableAndNothingCanBeChanged()
    {
        var service = new MaterialLibraryService(new MaterialLibraryStore(new Files(), Path_));
        Assert.False(service.State.IsAvailable); Assert.Equal(LibraryFailureKind.NotLoaded, service.State.Failure!.Kind);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
    }

    [Fact]
    public void ALoadedLibraryIsAvailableWithItsEntries()
    {
        var service = Create(new Files(), Entry("A"), Entry("B"));
        Assert.True(service.State.IsAvailable); Assert.True(service.CanMutate);
        Assert.Equal(["A", "B"], service.State.Snapshot!.All.Select(m => m.Name).Order());
    }

    [Fact]
    public async Task ADamagedFileMakesTheLibraryUnavailableAndFailsClosedForEveryMutation()
    {
        var files = new Files(); var damaged = System.Text.Encoding.UTF8.GetBytes("{");
        files.Data[Path_] = damaged;
        var service = Create(files);
        Assert.False(service.State.IsAvailable); Assert.Null(service.State.Snapshot); Assert.False(service.CanMutate);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.DeleteAsync(Entry("A"))).Outcome);
        Assert.Equal(damaged, files.Data[Path_]); Assert.Empty(files.Writes);
    }

    [Fact]
    public async Task AddReplaceRenameAndDeleteCommitWithAnIncreasingRevisionAndOneChangedEach()
    {
        var files = new Files(); var service = Create(files);
        var revisions = new List<long>(); service.Changed += e => revisions.Add(e.Revision);

        Assert.True((await service.AddAsync(Entry("A"))).Succeeded);
        Assert.True((await service.ReplaceAsync(service.State.Snapshot!.Find("A")!, Entry("A", 200e9))).Succeeded);
        Assert.True((await service.RenameAsync(service.State.Snapshot!.Find("A")!, "B")).Succeeded);
        Assert.True((await service.DeleteAsync(service.State.Snapshot!.Find("B")!)).Succeeded);

        Assert.Equal([1, 2, 3, 4], revisions);
        Assert.Equal(4, service.Revision);
        Assert.Empty(service.State.Snapshot!.All);
        Assert.Equal(4, files.Writes.Count);
    }

    [Fact]
    public async Task APublishedSnapshotIsNeverMutated()
    {
        var service = Create(new Files(), Entry("A"));
        var before = service.State.Snapshot!;
        await service.AddAsync(Entry("B"));
        Assert.Single(before.All);
        Assert.NotSame(before, service.State.Snapshot);
        Assert.Equal(2, service.State.Snapshot!.All.Count);
    }

    [Fact]
    public async Task AddingAnExistingNameIsAConflict()
    {
        var service = Create(new Files(), Entry("A"));
        var result = await service.AddAsync(Entry("A", 1e9));
        Assert.Equal(LibraryMutationOutcome.Conflict, result.Outcome); Assert.Equal(LibraryConflictKind.AlreadyExists, result.Conflict);
        Assert.Equal(0, service.Revision);
    }

    [Fact]
    public async Task AStaleOriginalIsAConflictForReplaceRenameAndDelete()
    {
        var service = Create(new Files(), Entry("A"));
        var stale = service.State.Snapshot!.Find("A")!;
        await service.ReplaceAsync(stale, Entry("A", 190e9));       // somebody else changed it
        long revision = service.Revision;
        foreach (var result in new[]
        {
            await service.ReplaceAsync(stale, Entry("A", 180e9)), await service.RenameAsync(stale, "Z"), await service.DeleteAsync(stale)
        })
        { Assert.Equal(LibraryMutationOutcome.Conflict, result.Outcome); Assert.Equal(LibraryConflictKind.OriginalChanged, result.Conflict); }
        Assert.Equal(revision, service.Revision);
        Assert.Equal(190e9, service.State.Snapshot!.Find("A")!.YoungsModulus.Pascals);
    }

    [Fact]
    public async Task AMissingOriginalIsAConflict()
    {
        var service = Create(new Files(), Entry("A"));
        var original = service.State.Snapshot!.Find("A")!;
        await service.DeleteAsync(original);
        foreach (var result in new[] { await service.ReplaceAsync(original, Entry("A")), await service.RenameAsync(original, "Z"), await service.DeleteAsync(original) })
            Assert.Equal(LibraryConflictKind.OriginalMissing, result.Conflict);
    }

    [Fact]
    public async Task ARenameOntoAnotherEntryIsATargetNameConflict()
    {
        var service = Create(new Files(), Entry("A"), Entry("B"));
        var result = await service.RenameAsync(service.State.Snapshot!.Find("A")!, "B");
        Assert.Equal(LibraryConflictKind.TargetNameTaken, result.Conflict);
        Assert.NotNull(service.State.Snapshot!.Find("A"));
    }

    [Fact]
    public async Task AnIndependentChangeOfAnotherEntryIsNoConflict()
    {
        var service = Create(new Files(), Entry("A"), Entry("B"));
        var originalA = service.State.Snapshot!.Find("A")!;
        await service.ReplaceAsync(service.State.Snapshot!.Find("B")!, Entry("B", 150e9));
        Assert.True((await service.ReplaceAsync(originalA, Entry("A", 140e9))).Succeeded);
        Assert.Equal(150e9, service.State.Snapshot!.Find("B")!.YoungsModulus.Pascals);
    }

    [Fact]
    public async Task AnEqualButDistinctInstanceOfTheOriginalIsNotStale()
    {
        var service = Create(new Files(), Entry("A"));
        Assert.True((await service.DeleteAsync(Entry("A"))).Succeeded);
    }

    [Fact]
    public async Task AWriteFailureKeepsSnapshotRevisionAndSilence()
    {
        var files = new Files(); var service = Create(files, Entry("A"));
        var before = service.State.Snapshot; int changed = 0; service.Changed += _ => changed++;
        files.FailWritePath = Path_;
        var result = await service.AddAsync(Entry("B"));
        Assert.Equal(LibraryMutationOutcome.PersistFailed, result.Outcome); Assert.IsType<IOException>(result.Failure);
        Assert.Same(before, service.State.Snapshot); Assert.Equal(0, service.Revision); Assert.Equal(0, changed);
    }

    [Fact]
    public async Task MutationsAreSerializedAndOnlyTheSecondOneSeesTheFirst()
    {
        var files = new Files(); var service = Create(files);
        var release = new TaskCompletionSource();
        files.BeforeWrite = (_, _, _) => release.Task;
        var first = service.AddAsync(Entry("A")); var second = service.AddAsync(Entry("A", 1e9));
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.True((await first).Succeeded);
        Assert.Equal(LibraryConflictKind.AlreadyExists, (await second).Conflict);
        Assert.Equal(1, service.Revision);
    }

    [Fact]
    public async Task TheBuiltInCatalogIsLoadedExactlyOnce()
    {
        int loads = 0;
        var service = new MaterialLibraryService(new MaterialLibraryStore(new Files(), Path_), () => { loads++; return MaterialCatalogCodec.LoadBuiltIn(); });
        await service.LoadAsync(); await service.LoadAsync();
        Assert.Equal(1, loads);
        Assert.NotNull(service.BuiltIns);
    }

    [Fact]
    public async Task LoadingIsIdempotentAndReadsTheFileOnce()
    {
        var files = new Files(); int reads = 0; files.BeforeRead = _ => { reads++; return Task.CompletedTask; };
        var service = new MaterialLibraryService(new MaterialLibraryStore(files, Path_));
        await service.LoadAsync(); await service.LoadAsync();
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task ATransientServiceCannotBeChanged()
    {
        var service = MaterialLibraryService.Transient();
        Assert.True(service.State.IsAvailable); Assert.False(service.CanMutate);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
    }
}
