using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class SectionLibraryServiceTests
{
    private const string Path_ = "sections";

    private static SectionLibraryEntry Entry(string name, double side = .1) => new(name, new AngleSectionGeometry(M(side), M(.2), M(.005), M(0)));

    private static SectionLibraryService Create(Files files, params SectionLibraryEntry[] existing)
    {
        if (existing.Length > 0) files.Data[Path_] = SectionLibraryCodec.Serialize(new(existing));
        var service = new SectionLibraryService(new SectionLibraryStore(files, Path_));
        service.LoadAsync().GetAwaiter().GetResult();
        return service;
    }

    [Fact]
    public async Task BeforeTheLoadTheLibraryIsUnavailable()
    {
        var service = new SectionLibraryService(new SectionLibraryStore(new Files(), Path_));
        Assert.Equal(LibraryFailureKind.NotLoaded, service.State.Failure!.Kind);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
    }

    [Fact]
    public async Task ADamagedFileMakesTheLibraryUnavailableAndFailsClosed()
    {
        var files = new Files(); var damaged = System.Text.Encoding.UTF8.GetBytes("{");
        files.Data[Path_] = damaged;
        var service = Create(files);
        Assert.False(service.State.IsAvailable); Assert.False(service.CanMutate);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
        Assert.Equal(damaged, files.Data[Path_]); Assert.Empty(files.Writes);
    }

    [Fact]
    public async Task AddReplaceRenameAndDeleteCommitWithOneChangedEach()
    {
        var files = new Files(); var service = Create(files);
        var revisions = new List<long>(); service.Changed += e => revisions.Add(e.Revision);
        Assert.True((await service.AddAsync(Entry("A"))).Succeeded);
        Assert.True((await service.ReplaceAsync(service.State.Snapshot!.Find("A")!, Entry("A", .15))).Succeeded);
        Assert.True((await service.RenameAsync(service.State.Snapshot!.Find("A")!, "B")).Succeeded);
        Assert.True((await service.DeleteAsync(service.State.Snapshot!.Find("B")!)).Succeeded);
        Assert.Equal([1, 2, 3, 4], revisions);
        Assert.Empty(service.State.Snapshot!.All);
    }

    [Fact]
    public async Task APublishedSnapshotIsNeverMutated()
    {
        var service = Create(new Files(), Entry("A"));
        var before = service.State.Snapshot!;
        await service.AddAsync(Entry("B"));
        Assert.Single(before.All); Assert.Equal(2, service.State.Snapshot!.All.Count);
    }

    [Fact]
    public async Task StaleChecksAreStructuralNotReferenceBased()
    {
        var service = Create(new Files(), Entry("A"));
        // A different instance with the same name and definition is the same original.
        Assert.True((await service.ReplaceAsync(Entry("A"), Entry("A", .12))).Succeeded);
        // A different definition under the same name is a changed original.
        var result = await service.DeleteAsync(Entry("A"));
        Assert.Equal(LibraryConflictKind.OriginalChanged, result.Conflict);
        Assert.NotNull(service.State.Snapshot!.Find("A"));
    }

    [Fact]
    public async Task AddingAnExistingNameAndRenamingOntoOneAreConflicts()
    {
        var service = Create(new Files(), Entry("A"), Entry("B", .12));
        Assert.Equal(LibraryConflictKind.AlreadyExists, (await service.AddAsync(Entry("A", .3))).Conflict);
        Assert.Equal(LibraryConflictKind.TargetNameTaken, (await service.RenameAsync(service.State.Snapshot!.Find("A")!, "B")).Conflict);
        Assert.Equal(0, service.Revision);
    }

    [Fact]
    public async Task AMissingOriginalIsAConflictAndAnIndependentChangeIsNot()
    {
        var service = Create(new Files(), Entry("A"), Entry("B", .12));
        var a = service.State.Snapshot!.Find("A")!;
        await service.ReplaceAsync(service.State.Snapshot!.Find("B")!, Entry("B", .13));
        Assert.True((await service.DeleteAsync(a)).Succeeded);
        Assert.Equal(LibraryConflictKind.OriginalMissing, (await service.DeleteAsync(a)).Conflict);
    }

    [Fact]
    public async Task AWriteFailureKeepsSnapshotRevisionAndSilence()
    {
        var files = new Files(); var service = Create(files, Entry("A"));
        var before = service.State.Snapshot; int changed = 0; service.Changed += _ => changed++;
        files.FailWritePath = Path_;
        var result = await service.AddAsync(Entry("B"));
        Assert.Equal(LibraryMutationOutcome.PersistFailed, result.Outcome);
        Assert.Same(before, service.State.Snapshot); Assert.Equal(0, service.Revision); Assert.Equal(0, changed);
    }

    [Fact]
    public async Task ATransientServiceCannotBeChanged()
    {
        var service = SectionLibraryService.Transient();
        Assert.True(service.State.IsAvailable); Assert.False(service.CanMutate);
        Assert.Equal(LibraryMutationOutcome.Unavailable, (await service.AddAsync(Entry("A"))).Outcome);
    }
}
