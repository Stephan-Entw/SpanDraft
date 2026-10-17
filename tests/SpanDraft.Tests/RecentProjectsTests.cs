using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class RecentProjectsTests
{
    private const string Slot = "recent-projects";

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("{")]
    [InlineData("null")] [InlineData("{}")] [InlineData("{\"version\":2,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":null}")]
    [InlineData("{\"version\":1,\"entries\":[null]}")]
    [InlineData("{\"version\":1,\"entries\":[{}]}")]
    public async Task MissingEmptyOrDamagedFileLoadsAnEmptyList(string? json)
    {
        var files = new Files();
        if (json is not null) files.Data[Slot] = Encoding.UTF8.GetBytes(json);
        var store = new RecentProjectsStore(files, Slot);
        await store.LoadAsync();
        Assert.Empty(store.Entries); Assert.Empty(files.Writes);
    }

    [Fact]
    public async Task ReadFailureIsLocalAndDoesNotProbeAnyProjectFiles()
    {
        var files = new Files();
        files.BeforeRead = path => { Assert.Equal(Slot, path); throw new IOException("Unavailable"); };
        var store = new RecentProjectsStore(files, Slot);
        await store.LoadAsync(); Assert.Empty(store.Entries);
        files.BeforeRead = null;
        await store.RecordUseAsync(TestPath("offline.spandraft"));
        Assert.Single(store.Entries);
    }

    [Fact]
    public async Task LoadNormalizesSortsDeduplicatesAndKeepsUnavailablePaths()
    {
        string first = TestPath("first.spandraft"), second = TestPath("second.spandraft");
        var files = new Files();
        files.Data[Slot] = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["version"] = 1,
            ["entries"] = new JsonArray(
                JsonSerializerEntry(first, Now.AddDays(-2)), JsonSerializerEntry(second, Now.AddDays(-1)),
                JsonSerializerEntry(Path.Combine(Path.GetDirectoryName(first)!, ".", "first.spandraft"), Now))
        }.ToJsonString());
        var readPaths = new List<string>(); files.BeforeRead = p => { readPaths.Add(p); return Task.CompletedTask; };
        var store = new RecentProjectsStore(files, Slot);
        await store.LoadAsync();
        Assert.Equal(new[] { first, second }, store.Entries.Select(e => e.Path));
        Assert.Equal(Now, store.Entries[0].LastUsedUtc); Assert.Equal(new[] { Slot }, readPaths);
    }

    [Fact]
    public async Task PathCasingFollowsTheOperatingSystem()
    {
        var store = new RecentProjectsStore(new Files(), Slot, () => Now);
        await store.RecordUseAsync(TestPath("Beam.spandraft"));
        await store.RecordUseAsync(TestPath("BEAM.spandraft"));
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, store.Entries.Count);
        Assert.Equal(TestPath("BEAM.spandraft"), store.Entries[0].Path);
    }

    [Fact]
    public async Task ReuseMovesToTopCapsAtTenAndStoresOnlyNormalizedPathAndUtcTime()
    {
        var files = new Files(); var now = Now;
        var store = new RecentProjectsStore(files, Slot, () => now);
        for (int i = 0; i < 12; i++)
        {
            now = Now.AddMinutes(i);
            await store.RecordUseAsync(TestPath($"{i}.spandraft"));
        }
        Assert.Equal(10, store.Entries.Count);
        Assert.Equal(TestPath("11.spandraft"), store.Entries[0].Path);
        Assert.DoesNotContain(store.Entries, e => e.Path == TestPath("0.spandraft"));
        now = Now.AddHours(1).ToOffset(TimeSpan.FromHours(2));
        await store.RecordUseAsync(Path.Combine(Path.GetDirectoryName(TestPath("5.spandraft"))!, ".", "5.spandraft"));
        Assert.Equal(TestPath("5.spandraft"), store.Entries[0].Path);
        Assert.Equal(now.ToUniversalTime(), store.Entries[0].LastUsedUtc);
        Assert.Equal(TimeSpan.Zero, store.Entries[0].LastUsedUtc.Offset);
        Assert.Single(store.Entries, e => e.Path == TestPath("5.spandraft"));
        var json = JsonNode.Parse(files.Data[Slot])!.AsObject();
        Assert.Equal(new[] { "version", "entries" }, json.Select(p => p.Key));
        Assert.Equal(new[] { "path", "lastUsedUtc" }, json["entries"]![0]!.AsObject().Select(p => p.Key));
        var loaded = new RecentProjectsStore(files, Slot); await loaded.LoadAsync();
        Assert.Equal(store.Entries, loaded.Entries);
    }

    [Fact]
    public async Task SaveAsReplacesOnlyThePreviousActivePathAndDeduplicatesTheTarget()
    {
        var now = Now;
        var store = new RecentProjectsStore(new Files(), Slot, () => now);
        foreach (string name in new[] { "other", "old", "new" }) await store.RecordUseAsync(TestPath(name + ".spandraft"));
        now = now.AddMinutes(1);
        await store.RecordUseAsync(TestPath("new.spandraft"), TestPath("old.spandraft"));
        Assert.Equal(new[] { TestPath("new.spandraft"), TestPath("other.spandraft") }, store.Entries.Select(e => e.Path));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedPublicationRetainsTheInMemoryUpdateAndPriorDiskState(bool directoryFailure)
    {
        var files = new Files(); var original = RecentProjectsCodec.Serialize([new(TestPath("old.spandraft"), Now)]);
        files.Data[Slot] = original;
        var store = new RecentProjectsStore(files, Slot, () => Now.AddMinutes(1),
            directoryFailure ? () => throw new UnauthorizedAccessException() : null);
        if (!directoryFailure) files.FailWritePath = Slot;
        await store.RecordUseAsync(TestPath("new.spandraft"));
        Assert.Equal(TestPath("new.spandraft"), store.Entries[0].Path); Assert.Equal(original, files.Data[Slot]);
    }

    [Theory]
    [InlineData("version")] [InlineData("entries")] [InlineData("path")] [InlineData("lastUsedUtc")]
    [InlineData("relative")] [InlineData("offset")] [InlineData("defaultTime")]
    public async Task InvalidContractFallsBackToEmpty(string corruption)
    {
        var json = JsonNode.Parse(RecentProjectsCodec.Serialize([new(TestPath("valid.spandraft"), Now)]))!.AsObject();
        var entry = json["entries"]![0]!.AsObject();
        if (corruption is "version" or "entries") json.Remove(corruption);
        else if (corruption is "path" or "lastUsedUtc") entry.Remove(corruption);
        else if (corruption == "relative") entry["path"] = "relative.spandraft";
        else entry["lastUsedUtc"] = corruption == "offset" ? "2026-10-07T12:00:00+02:00" : "0001-01-01T00:00:00+00:00";
        var files = new Files(); files.Data[Slot] = Encoding.UTF8.GetBytes(json.ToJsonString());
        var store = new RecentProjectsStore(files, Slot); await store.LoadAsync(); Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task UpdateWaitsForStartupLoadAndPreservesBothEntries()
    {
        var files = new Files(); files.Data[Slot] = RecentProjectsCodec.Serialize([new(TestPath("old.spandraft"), Now)]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeRead = async _ => { entered.SetResult(); await WaitFor(release.Task); };
        var store = new RecentProjectsStore(files, Slot, () => Now.AddMinutes(1));
        var loading = store.LoadAsync(); await WaitForCheckpoint(entered.Task, loading);
        var updating = store.RecordUseAsync(TestPath("new.spandraft"));
        Assert.False(updating.IsCompleted);
        release.SetResult(); await WaitFor(loading); await WaitFor(updating);
        Assert.Equal(new[] { TestPath("new.spandraft"), TestPath("old.spandraft") }, store.Entries.Select(e => e.Path));
    }

    private static JsonObject JsonSerializerEntry(string path, DateTimeOffset time) => new()
    { ["path"] = path, ["lastUsedUtc"] = time.ToString("O") };
}
