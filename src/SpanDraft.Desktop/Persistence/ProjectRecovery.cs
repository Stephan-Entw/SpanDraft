using System.Text.Json;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public sealed record RecoveryEnvelopeV1
{
    public required int RecoveryVersion { get; init; }
    public string? OriginalFilePath { get; init; }
    public required DateTimeOffset WrittenAtUtc { get; init; }
    public required ProjectFileV1 Project { get; init; }
}

public sealed record RecoverySnapshot(ProjectState State, string? OriginalFilePath, DateTimeOffset WrittenAtUtc);

/// <summary>A single private slot. Generations invalidate stale work; the gate orders publication and deletion.</summary>
public sealed class ProjectRecovery
{
    private readonly IProjectFileStore _files;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action? _ensureDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private Task _pending = Task.CompletedTask;

    public ProjectRecovery(IProjectFileStore files, string slotPath,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? utcNow = null,
        Action? ensureDirectory = null)
    {
        _files = files;
        SlotPath = slotPath;
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _ensureDirectory = ensureDirectory;
    }

    public static ProjectRecovery Local(IProjectFileStore files)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "recovery.json"), ensureDirectory: () => Directory.CreateDirectory(directory));
    }

    public string SlotPath { get; }
    public event Action<Exception>? Failed;

    public void Schedule(ProjectSession session)
    {
        var (generation, token) = Invalidate();
        _pending = ObserveAsync(session.IsDirty
            ? DebounceAsync(new(session.CurrentRevision.State, session.FilePath, _utcNow()), generation, token)
            : DeleteGenerationAsync(generation));
    }

    private (long, CancellationToken) Invalidate()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new();
        return (++_generation, _cancellation.Token);
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFormatException or JsonException)
        { Failed?.Invoke(e); }
    }

    private async Task DebounceAsync(RecoverySnapshot snapshot, long generation, CancellationToken token)
    {
        await _delay(TimeSpan.FromSeconds(1), token);
        await WriteGenerationAsync(snapshot, generation, token);
    }

    private async Task WriteGenerationAsync(RecoverySnapshot snapshot, long generation, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (generation != _generation) return;
            token.ThrowIfCancellationRequested();
            _ensureDirectory?.Invoke();
            await _files.WriteAtomicAsync(SlotPath, Encode(snapshot), token);
        }
        finally { _gate.Release(); }
    }

    private async Task DeleteGenerationAsync(long generation)
    {
        await _gate.WaitAsync();
        try { if (generation == _generation) await _files.DeleteAsync(SlotPath); }
        finally { _gate.Release(); }
    }

    public async Task FlushAsync(ProjectState state, string? originalFilePath)
    {
        var (generation, token) = Invalidate();
        // Gate acquisition also drains any write which was already publishing.
        await WriteGenerationAsync(new(state, originalFilePath, _utcNow()), generation, token);
    }

    public async Task DeleteAsync()
    {
        var (generation, _) = Invalidate();
        await DeleteGenerationAsync(generation);
    }

    public Task DrainAsync() => _pending;

    public async Task<RecoverySnapshot?> ReadAsync()
    {
        var bytes = await _files.ReadAsync(SlotPath);
        if (bytes is null) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<RecoveryEnvelopeV1>(bytes, ProjectFileCodec.JsonOptions)
                ?? throw new ProjectFormatException("Recovery root must be an object.");
            if (envelope.RecoveryVersion != 1 || envelope.WrittenAtUtc == default || envelope.WrittenAtUtc.Offset != TimeSpan.Zero)
                throw new ProjectFormatException("Invalid recovery version or UTC timestamp.");
            if (envelope.OriginalFilePath is { } path && (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
                throw new ProjectFormatException("Invalid recovery file path.");
            if (envelope.Project is null) throw new ProjectFormatException("Missing recovery project.");
            return new(ProjectFileCodec.FromDto(envelope.Project), envelope.OriginalFilePath, envelope.WrittenAtUtc);
        }
        catch (JsonException e) { throw new ProjectFormatException("Invalid recovery data: " + e.Message, e); }
    }

    public static byte[] Encode(RecoverySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(new RecoveryEnvelopeV1
    {
        RecoveryVersion = 1, OriginalFilePath = snapshot.OriginalFilePath,
        WrittenAtUtc = snapshot.WrittenAtUtc, Project = ProjectFileCodec.ToDto(snapshot.State)
    }, ProjectFileCodec.JsonOptions);
}
