using System.Text;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.LibraryTestSupport;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class LibraryStoreTests
{
    private static (Func<Task<int>> Load, Func<CancellationToken, Task> Save) Store(bool material,
        IProjectFileStore files, string path, Action? ensureDirectory = null)
    {
        if (material)
        {
            var store = new MaterialLibraryStore(files, path, ensureDirectory);
            return (async () => (await store.LoadAsync()).All.Count,
                token => store.SaveAsync(new([MaterialEntry()]), token));
        }
        var sections = new SectionLibraryStore(files, path, ensureDirectory);
        return (async () => (await sections.LoadAsync()).All.Count,
            token => sections.SaveAsync(new([new("Fixture section", ParametricSectionTestSupport.Shape(4, r: .5))]), token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFileLoadsEmptyAndSavingUsesExistingAtomicStore(bool material)
    {
        var files = new Files(); int directories = 0;
        var store = Store(material, files, "library", () => directories++);
        Assert.Equal(0, await store.Load()); Assert.Empty(files.Writes); Assert.Equal(0, directories);
        var token = TestContext.Current.CancellationToken;
        files.BeforeWrite = (path, bytes, actualToken) =>
        {
            Assert.Equal("library", path); Assert.Equal(token, actualToken); Assert.Equal(1, directories);
            if (material) Assert.Single(MaterialLibraryCodec.Deserialize(bytes).All);
            else Assert.Single(SectionLibraryCodec.Deserialize(bytes).All);
            return Task.CompletedTask;
        };
        await store.Save(token);
        Assert.Equal(new[] { "library" }, files.Writes); Assert.Equal(1, await store.Load());
    }

    [Theory]
    [InlineData(false, "broken")]
    [InlineData(true, "broken")]
    [InlineData(false, "version")]
    [InlineData(true, "version")]
    [InlineData(false, "domain")]
    [InlineData(true, "domain")]
    public async Task DamagedFilesAreNeverReplacedEvenAfterFailedLoadOrWithANewStore(bool material, string failure)
    {
        var files = new Files();
        var store = Store(material, files, "library"); await store.Save(TestContext.Current.CancellationToken);
        string json = Encoding.UTF8.GetString(files.Data["library"]);
        files.Data["library"] = failure switch
        {
            "broken" => Encoding.UTF8.GetBytes("{"),
            "version" => Encoding.UTF8.GetBytes(json.Replace("\"formatVersion\": 1", "\"formatVersion\": 99")),
            _ => Encoding.UTF8.GetBytes(material ? json.Replace("\"yieldStrength\": 2345678.901234567", "\"yieldStrength\": -1")
                : json.Replace("\"radius\": 0.5", "\"radius\": -1"))
        };
        byte[] original = files.Data["library"].ToArray(); files.Writes.Clear(); int directories = 0;
        var guarded = Store(material, files, "library", () => directories++);
        await Assert.ThrowsAsync<LibraryFormatException>(() => guarded.Load());
        await Assert.ThrowsAsync<LibraryFormatException>(() => guarded.Save(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<LibraryFormatException>(() => Store(material, files, "library").Save(TestContext.Current.CancellationToken));
        Assert.Equal(original, files.Data["library"]); Assert.Empty(files.Writes); Assert.Equal(0, directories);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReadFailuresPropagateWithoutFallbackOrWrite(bool material, bool unauthorized)
    {
        var files = new Files { BeforeRead = _ => throw (unauthorized
            ? new UnauthorizedAccessException("Injected") : new IOException("Injected")) };
        var store = Store(material, files, "library");
        var load = await Record.ExceptionAsync(() => store.Load());
        var save = await Record.ExceptionAsync(() => store.Save(TestContext.Current.CancellationToken));
        Assert.Equal(unauthorized ? typeof(UnauthorizedAccessException) : typeof(IOException), load!.GetType());
        Assert.Equal(load.GetType(), save!.GetType()); Assert.Empty(files.Writes); Assert.Empty(files.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteFailureAndCancellationKeepPreviousFile(bool material)
    {
        var files = new Files(); var store = Store(material, files, "library");
        await store.Save(TestContext.Current.CancellationToken); byte[] previous = files.Data["library"].ToArray();
        files.Writes.Clear(); files.FailWritePath = "library";
        await Assert.ThrowsAsync<IOException>(() => store.Save(TestContext.Current.CancellationToken));
        Assert.Equal(previous, files.Data["library"]); Assert.Empty(files.Writes);
        files.FailWritePath = null;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Save(cancelled.Token));
        Assert.Equal(previous, files.Data["library"]); Assert.Empty(files.Writes);
        int directories = 0;
        using var duringRead = new CancellationTokenSource();
        files.BeforeRead = _ => { duringRead.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Store(material, files, "library", () => directories++).Save(duringRead.Token));
        Assert.Equal(0, directories); Assert.Empty(files.Writes); Assert.Equal(previous, files.Data["library"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhysicalStoresCreateDirectoryAndPreserveCorruptDataWithoutTemporaryFiles(bool material)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-library-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "library.json");
        try
        {
            var store = Store(material, new ProjectFileStore(), path, () => Directory.CreateDirectory(directory));
            Assert.Equal(0, await store.Load()); Assert.False(Directory.Exists(directory));
            await store.Save(TestContext.Current.CancellationToken); Assert.Equal(1, await store.Load());
            await store.Save(TestContext.Current.CancellationToken); Assert.Equal(1, await store.Load());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await File.WriteAllTextAsync(path, "{", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<LibraryFormatException>(() => store.Load());
            await Assert.ThrowsAsync<LibraryFormatException>(() => store.Save(TestContext.Current.CancellationToken));
            Assert.Equal("{", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void LocalPathsUseTheExistingSpanDraftApplicationDataDirectory()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        Assert.Equal(Path.Combine(directory, "material-library.json"), MaterialLibraryStore.Local(new Files()).FilePath);
        Assert.Equal(Path.Combine(directory, "section-library.json"), SectionLibraryStore.Local(new Files()).FilePath);
    }
}
