namespace SpanDraft.Desktop.Persistence;

// Current envelope. Material, section, point loads, naming and presentation keep their V2 contracts;
// supports may be springs and distributed loads carry a start and an end intensity.
public sealed record ProjectFileV5
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public ProjectInfoV5? Info { get; init; }
    public required DocumentV5 Document { get; init; }
    public required PresentationV2 Presentation { get; init; }
}

public sealed record ProjectInfoV5
{
    public required string CalculationTitle { get; init; }
    public string? ProjectTitle { get; init; }
    public string? Reference { get; init; }
    public string? Author { get; init; }
}

public sealed record DocumentV5
{
    public required double Length { get; init; }
    public required string BendingAxis { get; init; }
    public string? SectionDisplayName { get; init; }
    public required MaterialV2 Material { get; init; }
    public required SectionV2 Section { get; init; }
    public required SupportV5[] Supports { get; init; }
    public required PointLoadV2[] PointLoads { get; init; }
    public required DistributedLoadV5[] DistributedLoads { get; init; }
    public required NamingV5 NamingState { get; init; }
    public SelfWeightV2? SelfWeight { get; init; }

    /// <summary>
    /// The segments after the first, in order; absent for a beam of one segment. The first segment is described by
    /// <c>material</c>, <c>section</c>, <c>bendingAxis</c> and <c>sectionDisplayName</c>, so a file without this field is
    /// a one-segment project. Each segment ends where the next one starts, the last at <c>length</c>.
    /// </summary>
    public SegmentV5[]? FurtherSegments { get; init; }

    /// <summary>
    /// The internal moment releases (hinges); absent for a beam without any, so a file without this field has none.
    /// </summary>
    public MomentReleaseV5[]? MomentReleases { get; init; }
}

/// <summary>An internal moment release at <c>position</c> (m) strictly inside the beam.</summary>
public sealed record MomentReleaseV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required double Position { get; init; }
}

/// <summary>The V2 naming counters plus the optional counter of the moment releases (absent means 1).</summary>
public sealed record NamingV5
{
    public required long NextSupportOrdinal { get; init; }
    public required long NextForceNumber { get; init; }
    public required long NextMomentNumber { get; init; }
    public required long NextDistributedLoadNumber { get; init; }
    public long? NextMomentReleaseNumber { get; init; }
}

/// <summary>A beam segment after the first. <c>start</c> (m) lies strictly inside the beam and ascends over the list.</summary>
public sealed record SegmentV5
{
    public required double Start { get; init; }
    public required MaterialV2 Material { get; init; }
    public required SectionV2 Section { get; init; }
    public required string BendingAxis { get; init; }
    public string? SectionDisplayName { get; init; }
}

/// <summary>
/// A support. Rigid supports use the V2 fields; <c>spring</c> supports add the optional spring stiffnesses (SI: N/m and
/// N·m/rad) and <c>rigidTranslation</c> for a partial restraint. Files without these fields remain valid.
/// </summary>
public sealed record SupportV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
    public double? TranslationalStiffness { get; init; }
    public double? RotationalStiffness { get; init; }
    public bool? RigidTranslation { get; init; }
}

/// <summary>A transverse line load whose intensity varies linearly from the start to the end position (N/m).</summary>
public sealed record DistributedLoadV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required double StartPosition { get; init; }
    public required double EndPosition { get; init; }
    public required double StartIntensity { get; init; }
    public required double EndIntensity { get; init; }
}
