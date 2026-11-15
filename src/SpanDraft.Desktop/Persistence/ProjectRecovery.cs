using System.Text.Json;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public sealed record RecoveryEnvelopeV1
{
    public required int RecoveryVersion { get; init; }
    public string? OriginalFilePath { get; init; }
    public required DateTimeOffset WrittenAtUtc { get; init; }
    public required JsonElement Project { get; init; }
}

public sealed record RecoverySnapshot(ProjectState State, string? OriginalFilePath, DateTimeOffset WrittenAtUtc);

/// <summary>The private recovery slot of the open project; see <see cref="DebouncedSlot{TSnapshot}"/> for the ordering rules.</summary>
public sealed class ProjectRecovery
{
    private readonly DebouncedSlot<RecoverySnapshot> _slot;
    private readonly IProjectFileStore _files;
    private readonly Func<DateTimeOffset> _utcNow;

    public ProjectRecovery(IProjectFileStore files, string slotPath,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? utcNow = null,
        Action? ensureDirectory = null)
    {
        _files = files;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _slot = new(files, slotPath, Encode, delay, ensureDirectory);
        _slot.Failed += e => Failed?.Invoke(e);
    }

    public static ProjectRecovery Local(IProjectFileStore files)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "recovery.json"), ensureDirectory: () => Directory.CreateDirectory(directory));
    }

    public string SlotPath => _slot.SlotPath;
    public event Action<Exception>? Failed;

    public void Schedule(ProjectSession session)
    {
        if (session.IsDirty) _slot.Schedule(new(session.CurrentRevision.State, session.FilePath, _utcNow()));
        else _slot.ScheduleDelete();
    }

    public Task FlushAsync(ProjectState state, string? originalFilePath) =>
        _slot.FlushAsync(new(state, originalFilePath, _utcNow()));

    public Task DeleteAsync() => _slot.DeleteAsync();

    public Task DrainAsync() => _slot.DrainAsync();

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
            if (envelope.Project.ValueKind != JsonValueKind.Object) throw new ProjectFormatException("Missing recovery project.");
            return new(ProjectFileCodec.Deserialize(System.Text.Encoding.UTF8.GetBytes(envelope.Project.GetRawText())), envelope.OriginalFilePath, envelope.WrittenAtUtc);
        }
        catch (JsonException e) { throw new ProjectFormatException("Invalid recovery data: " + e.Message, e); }
    }

    public static byte[] Encode(RecoverySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(new RecoveryEnvelopeV1
    {
        RecoveryVersion = 1, OriginalFilePath = snapshot.OriginalFilePath,
        WrittenAtUtc = snapshot.WrittenAtUtc, Project = ProjectFileCodec.RecoveryElement(snapshot.State)
    }, ProjectFileCodec.JsonOptions);
}
