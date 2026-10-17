using System.Text.Json;

namespace SpanDraft.Desktop.Persistence;

public sealed record RecentProjectEntry(string Path, DateTimeOffset LastUsedUtc);

public sealed class RecentProjectsStore(IProjectFileStore files, string path,
    Func<DateTimeOffset>? utcNow = null, Action? ensureDirectory = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    public string FilePath { get; } = path;
    public IReadOnlyList<RecentProjectEntry> Entries { get; private set; } = [];

    public static RecentProjectsStore Local(IProjectFileStore files)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "recent-projects.json"), ensureDirectory: () => Directory.CreateDirectory(directory));
    }

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try { await LoadLockedAsync(); }
        finally { _gate.Release(); }
    }

    private async Task LoadLockedAsync()
    {
        if (_loaded) return;
        try
        {
            var bytes = await files.ReadAsync(FilePath);
            Entries = bytes is null || bytes.Length == 0 ? [] : RecentProjectsCodec.Deserialize(bytes);
        }
        catch (Exception e) when (IsLocalStateError(e)) { Entries = []; }
        _loaded = true;
    }

    public async Task RecordUseAsync(string projectPath, string? replacedPath = null)
    {
        await _gate.WaitAsync();
        try
        {
            await LoadLockedAsync();
            string fullPath = Path.GetFullPath(projectPath);
            string? previous = replacedPath is null ? null : Path.GetFullPath(replacedPath);
            var entry = new RecentProjectEntry(fullPath, (utcNow?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime());
            Entries = RecentProjectsCodec.Normalize(new[] { entry }.Concat(Entries.Where(e =>
                !RecentProjectsCodec.PathComparer.Equals(e.Path, fullPath)
                && !RecentProjectsCodec.PathComparer.Equals(e.Path, previous))));
            // Comfort state is published in memory before its best-effort disk write.
            ensureDirectory?.Invoke();
            await files.WriteAtomicAsync(FilePath, RecentProjectsCodec.Serialize(Entries));
        }
        catch (Exception e) when (IsLocalStateError(e)) { }
        finally { _gate.Release(); }
    }

    private static bool IsLocalStateError(Exception e) => e is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or InvalidOperationException or NotSupportedException
        or System.Security.SecurityException;
}

public static class RecentProjectsCodec
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        RespectRequiredConstructorParameters = true
    };
    private sealed record RecentProjectsV1(int Version, RecentProjectEntry[] Entries);

    public static byte[] Serialize(IReadOnlyList<RecentProjectEntry> entries) =>
        JsonSerializer.SerializeToUtf8Bytes(new RecentProjectsV1(1, Normalize(entries)), Options);

    public static IReadOnlyList<RecentProjectEntry> Deserialize(byte[] bytes)
    {
        var dto = JsonSerializer.Deserialize<RecentProjectsV1>(bytes, Options) ?? throw new JsonException("Missing recent projects.");
        if (dto.Version != 1 || dto.Entries is null) throw new JsonException("Invalid recent projects version or entries.");
        return Normalize(dto.Entries);
    }

    internal static RecentProjectEntry[] Normalize(IEnumerable<RecentProjectEntry> entries) => entries.Select(entry =>
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Path) || !Path.IsPathFullyQualified(entry.Path)
            || entry.LastUsedUtc == default || entry.LastUsedUtc.Offset != TimeSpan.Zero)
            throw new JsonException("Invalid recent project path or UTC timestamp.");
        return entry with { Path = Path.GetFullPath(entry.Path) };
    }).OrderByDescending(e => e.LastUsedUtc).DistinctBy(e => e.Path, PathComparer).Take(10).ToArray();
}
