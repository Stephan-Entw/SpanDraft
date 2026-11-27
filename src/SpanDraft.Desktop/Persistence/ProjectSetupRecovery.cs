using System.Text.Json;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public sealed record SetupRecoveryEnvelope
{
    public required int RecoveryVersion { get; init; }
    public required DateTimeOffset WrittenAtUtc { get; init; }
    public required SetupDraftData Draft { get; init; }
}

public sealed record SetupDraftData
{
    public string? CalculationTitle { get; init; }
    public string? ProjectTitle { get; init; }
    public string? Reference { get; init; }
    public string? Author { get; init; }
    public double? Length { get; init; }
    public MaterialV2? Material { get; init; }
    public SectionV2? Section { get; init; }
    public required string BendingAxis { get; init; }
    public string? SectionDisplayName { get; init; }

    /// <summary>The segments after the first; absent for a beam of one segment.</summary>
    public SetupSegmentData[]? FurtherSegments { get; init; }
}

public sealed record SetupSegmentData
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
    private readonly DebouncedSlot<SetupRecoverySnapshot> _slot;
    private readonly IRecoveryStorage _storage;
    private readonly Func<DateTimeOffset> _utcNow;

    public ProjectSetupRecovery(IRecoveryStorage storage, RecoverySlot slot,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? utcNow = null)
        : this(storage, slot, delay, utcNow, published: false) { }

    internal ProjectSetupRecovery(IRecoveryStorage storage, RecoverySlot slot,
        Func<TimeSpan, CancellationToken, Task>? delay, Func<DateTimeOffset>? utcNow, bool published)
    {
        if (slot.Kind != RecoverySlotKind.Setup) throw new ArgumentException("Expected a setup slot.", nameof(slot));
        _storage = storage;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _slot = new(storage, slot, Encode, delay, published: published);
        _slot.Failed += e => Failed?.Invoke(e);
    }

    public RecoverySlot Slot => _slot.Slot;
    public event Action<Exception>? Failed;

    public void Schedule(ProjectSetupDraft draft) => _slot.Schedule(new(draft, _utcNow()));

    public Task DeleteAsync() => _slot.DeleteAsync();

    /// <summary>Publishes the draft now (no debounce), e.g. to restore its protection after an aborted discard.</summary>
    internal Task FlushAsync(ProjectSetupDraft draft) => _slot.FlushAsync(new(draft, _utcNow()));

    internal Task CheckpointIfIdleAsync(ProjectSetupDraft draft) => _slot.CheckpointIfIdleAsync(new(draft, _utcNow()));

    /// <inheritdoc cref="DebouncedSlot{TSnapshot}.DiscardAsync"/>
    internal Task<SlotDiscardOutcome> DiscardAsync() => _slot.DiscardAsync();

    /// <inheritdoc cref="DebouncedSlot{TSnapshot}.IsPublished"/>
    internal bool IsPublished => _slot.IsPublished;

    public Task DrainAsync() => _slot.DrainAsync();

    public async Task<SetupRecoverySnapshot?> ReadAsync()
    {
        var bytes = await _storage.ReadAsync(Slot);
        return bytes is null ? null : Decode(bytes);
    }

    /// <summary>Validates and reads one slot's bytes; every defect is a <see cref="ProjectFormatException"/>.</summary>
    public static SetupRecoverySnapshot Decode(byte[] bytes)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<SetupRecoveryEnvelope>(bytes, ProjectFileCodec.JsonOptions)
                ?? throw new ProjectFormatException("Setup recovery root must be an object.");
            if (envelope.RecoveryVersion is not (1 or 2) || envelope.WrittenAtUtc == default || envelope.WrittenAtUtc.Offset != TimeSpan.Zero)
                throw new ProjectFormatException("Invalid setup recovery version or UTC timestamp.");
            var d = ProjectFileCodec.Required(envelope.Draft, "draft");
            var (length, material, section, axis) = ReadSegment(d.Length, d.Material, d.Section, d.BendingAxis, d.SectionDisplayName);
            var further = (d.FurtherSegments ?? []).Select(entry =>
            {
                var x = ProjectFileCodec.Required(entry, "segment");
                var (segmentLength, segmentMaterial, segmentSection, segmentAxis) =
                    ReadSegment(x.Length, x.Material, x.Section, x.BendingAxis, x.SectionDisplayName);
                return new SetupSegmentDraft(segmentLength, segmentMaterial, segmentSection, segmentAxis, x.SectionDisplayName);
            }).ToArray();
            return new(new(length, material, section, axis, d.SectionDisplayName,
                envelope.RecoveryVersion >= 2 ? ProjectInfo.Optional(d.CalculationTitle) : null,
                envelope.RecoveryVersion >= 2 ? ProjectInfo.Optional(d.ProjectTitle) : null,
                envelope.RecoveryVersion >= 2 ? ProjectInfo.Optional(d.Reference) : null,
                envelope.RecoveryVersion >= 2 ? ProjectInfo.Optional(d.Author) : null, further), envelope.WrittenAtUtc);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        { throw new ProjectFormatException("Invalid setup recovery data: " + e.Message, e); }
    }

    // The checks every segment of a draft passes, the first one and the further ones alike. Fields may be absent while
    // the user is still typing, but what is present must be valid.
    private static (Length? Length, Material? Material, ISectionDefinition? Section, SectionAxisDesignation Axis) ReadSegment(
        double? lengthMeters, MaterialV2? materialDto, SectionV2? sectionDto, string axisWire, string? displayName)
    {
        var length = lengthMeters is { } meters ? Length.FromMeters(ProjectFileCodec.Number(meters, "length")) : (Length?)null;
        if (length is { Meters: <= 0 }) throw new ProjectFormatException("Setup length must be positive.");
        var material = materialDto is { } m ? ProjectFileCodec.MaterialFromDto(m) : null;
        var section = sectionDto is { } s ? ProjectFileCodec.SectionFromDto(s) : null;
        var axis = SectionAxisWire.Read(axisWire);
        if (section is not null && !section.Axes.Any(a => a.AxisDesignation == axis))
            throw new ProjectFormatException("The section does not provide the selected bending axis.");
        if (displayName is { } name && (section is null || string.IsNullOrWhiteSpace(name) || name != name.Trim()))
            throw new ProjectFormatException("Invalid setup section display name.");
        return (length, material, section, axis);
    }

    public static byte[] Encode(SetupRecoverySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(new SetupRecoveryEnvelope
    {
        RecoveryVersion = 2, WrittenAtUtc = snapshot.WrittenAtUtc,
        Draft = new()
        {
            CalculationTitle = snapshot.Draft.CalculationTitle, ProjectTitle = snapshot.Draft.ProjectTitle,
            Reference = snapshot.Draft.Reference, Author = snapshot.Draft.Author,
            Length = snapshot.Draft.Length?.Meters,
            Material = snapshot.Draft.Material is { } m ? ProjectFileCodec.MaterialToDto(m) : null,
            Section = snapshot.Draft.Section is { } s ? ProjectFileCodec.SectionToDto(s) : null,
            BendingAxis = SectionAxisWire.Write(snapshot.Draft.BendingAxis),
            SectionDisplayName = snapshot.Draft.SectionDisplayName,
            FurtherSegments = snapshot.Draft.FurtherSegments is { Count: > 0 } further ? further.Select(g => new SetupSegmentData
            {
                Length = g.Length?.Meters,
                Material = g.Material is { } material ? ProjectFileCodec.MaterialToDto(material) : null,
                Section = g.Section is { } section ? ProjectFileCodec.SectionToDto(section) : null,
                BendingAxis = SectionAxisWire.Write(g.BendingAxis), SectionDisplayName = g.SectionDisplayName
            }).ToArray() : null
        }
    }, ProjectFileCodec.JsonOptions);
}
