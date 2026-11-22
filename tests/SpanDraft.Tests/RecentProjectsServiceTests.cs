using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class RecentProjectsServiceTests
{
    private static readonly string Path_ = TestPath("recent-projects.json");
    private static string P(string name) => TestPath(name + ".spandraft");

    private static (RecentProjectsService Service, Files Files) Create(Action<Files>? arrange = null)
    {
        var files = new Files(); arrange?.Invoke(files);
        return (new RecentProjectsService(new RecentProjectsStore(files, Path_)), files);
    }

    [Fact]
    public async Task EveryConsumerSeesTheSameSharedList()
    {
        var (service, _) = Create();
        await service.LoadAsync();
        await service.RecordUseAsync(P("a"));
        var first = service.Entries; var second = service.Entries;
        Assert.Same(first, second);
        Assert.Equal(P("a"), Assert.Single(first).Path);
    }

    [Fact]
    public async Task ChangesRaiseChangedAndBumpTheRevision()
    {
        var (service, _) = Create(); int changed = 0; service.Changed += () => changed++;
        await service.LoadAsync();
        await service.RecordUseAsync(P("a"));
        await service.RemoveAsync(P("a"));
        Assert.Empty(service.Entries);
        Assert.True(changed >= 2); Assert.True(service.Revision >= 2);
    }

    [Fact]
    public async Task LoadingTwiceReadsTheFileOnce()
    {
        var (service, files) = Create(); int reads = 0; files.BeforeRead = _ => { reads++; return Task.CompletedTask; };
        await service.LoadAsync(); await service.LoadAsync();
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task ATransientReadErrorNeverLeadsToAnEmptyOverwriteAndALaterReadMerges()
    {
        var disk = RecentProjectsCodec.Serialize([new RecentProjectEntry(P("disk"), DateTimeOffset.UtcNow.AddDays(-1))]);
        var (service, files) = Create(f => f.Data[Path_] = disk);
        files.BeforeRead = _ => throw new IOException("locked");
        await service.LoadAsync();
        await service.RecordUseAsync(P("new"));
        // Nothing was written over the unknown disk content while it could not be read; the entry is shown meanwhile.
        Assert.Equal(disk, files.Data[Path_]);
        Assert.Equal(P("new"), Assert.Single(service.Entries).Path);
        files.BeforeRead = null;
        await service.LoadAsync();
        await service.RecordUseAsync(P("another"));
        var paths = service.Entries.Select(e => e.Path).ToArray();
        Assert.Contains(P("disk"), paths); Assert.Contains(P("new"), paths); Assert.Contains(P("another"), paths);
    }

    [Fact]
    public async Task AServiceWithoutAStoreKeepsTheListInMemory()
    {
        var service = new RecentProjectsService(null);
        await service.LoadAsync(); await service.RecordUseAsync(P("a")); await service.RecordUseAsync(P("b"));
        Assert.Equal([P("b"), P("a")], service.Entries.Select(e => e.Path));
        await service.RemoveAsync(P("b"));
        Assert.Equal(P("a"), Assert.Single(service.Entries).Path);
    }
}
