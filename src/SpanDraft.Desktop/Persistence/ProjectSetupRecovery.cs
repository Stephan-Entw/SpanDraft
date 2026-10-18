using System.Text.Json;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public sealed record SetupRecoveryEnvelopeV1
{
    public required int RecoveryVersion { get; init; }
    public required DateTimeOffset WrittenAtUtc { get; init; }
    public required SetupDraftV1 Draft { get; init; }
}

public sealed record SetupDraftV1
{
    public double? Length { get; init; }
    public MaterialV2? Material { get; init; }
    public SectionV2? Section { get; init; }
    public required string BendingAxis { get; init; }
    public string? SectionDisplayName { get; init; }
}

public sealed record SetupRecoverySnapshot(ProjectSetupDraft Draft, DateTimeOffset WrittenAtUtc);

/// <summary>An independent Create-draft slot, using the same publication ordering as ProjectRecovery.</summary>
public sealed class ProjectSetupRecovery
{
    private readonly IProjectFileStore _files;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action? _ensureDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private Task _pending = Task.CompletedTask;

    public ProjectSetupRecovery(IProjectFileStore files, string slotPath,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? utcNow = null,
        Action? ensureDirectory = null)
    {
        _files = files;
        SlotPath = slotPath;
        _delay = delay ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _ensureDirectory = ensureDirectory;
    }

    public static ProjectSetupRecovery Local(IProjectFileStore files)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "setup-recovery.json"), ensureDirectory: () => Directory.CreateDirectory(directory));
    }

    public string SlotPath { get; }
    public event Action<Exception>? Failed;

    public void Schedule(ProjectSetupDraft draft)
    {
        var (generation, token) = Invalidate();
        _pending = ObserveAsync(DebounceAsync(new(draft, _utcNow()), generation, token));
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

    private async Task DebounceAsync(SetupRecoverySnapshot snapshot, long generation, CancellationToken token)
    {
        await _delay(TimeSpan.FromSeconds(1), token);
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

    public async Task DeleteAsync()
    {
        var (generation, _) = Invalidate();
        await _gate.WaitAsync();
        try { if (generation == _generation) await _files.DeleteAsync(SlotPath); }
        finally { _gate.Release(); }
    }

    public Task DrainAsync() => _pending;

    public async Task<SetupRecoverySnapshot?> ReadAsync()
    {
        var bytes = await _files.ReadAsync(SlotPath);
        if (bytes is null) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<SetupRecoveryEnvelopeV1>(bytes, ProjectFileCodec.JsonOptions)
                ?? throw new ProjectFormatException("Setup recovery root must be an object.");
            if (envelope.RecoveryVersion != 1 || envelope.WrittenAtUtc == default || envelope.WrittenAtUtc.Offset != TimeSpan.Zero)
                throw new ProjectFormatException("Invalid setup recovery version or UTC timestamp.");
            var d = ProjectFileCodec.Required(envelope.Draft, "draft");
            var length = d.Length is { } meters ? Length.FromMeters(ProjectFileCodec.Number(meters, "length")) : (Length?)null;
            if (length is { Meters: <= 0 }) throw new ProjectFormatException("Setup length must be positive.");
            var material = d.Material is { } m ? ProjectFileCodec.MaterialFromDto(m) : null;
            var section = d.Section is { } s ? ProjectFileCodec.SectionFromDto(s) : null;
            var axis = ProjectFileCodec.AxisFromWire(d.BendingAxis);
            if (section is not null && !section.Axes.Any(a => a.AxisDesignation == axis))
                throw new ProjectFormatException("The section does not provide the selected bending axis.");
            if (d.SectionDisplayName is { } name && (section is null || string.IsNullOrWhiteSpace(name) || name != name.Trim()))
                throw new ProjectFormatException("Invalid setup section display name.");
            return new(new(length, material, section, axis, d.SectionDisplayName), envelope.WrittenAtUtc);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        { throw new ProjectFormatException("Invalid setup recovery data: " + e.Message, e); }
    }

    public static byte[] Encode(SetupRecoverySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(new SetupRecoveryEnvelopeV1
    {
        RecoveryVersion = 1, WrittenAtUtc = snapshot.WrittenAtUtc,
        Draft = new()
        {
            Length = snapshot.Draft.Length?.Meters,
            Material = snapshot.Draft.Material is { } m ? ProjectFileCodec.MaterialToDto(m) : null,
            Section = snapshot.Draft.Section is { } s ? ProjectFileCodec.SectionToDto(s) : null,
            BendingAxis = ProjectFileCodec.AxisToWire(snapshot.Draft.BendingAxis),
            SectionDisplayName = snapshot.Draft.SectionDisplayName
        }
    }, ProjectFileCodec.JsonOptions);
}
