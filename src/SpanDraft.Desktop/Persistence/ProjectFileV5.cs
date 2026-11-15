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
    /// <summary>
    /// The length (m) of the first segment, which for a one-segment project is the beam length. The beam length of
    /// several segments is the sum of the segment lengths and is not stored a second time.
    /// </summary>
    public required double Length { get; init; }

    /// <summary>The identity of the first segment; absent in files from before segments had one.</summary>
    public Guid? SegmentId { get; init; }
    public required string BendingAxis { get; init; }
    public string? SectionDisplayName { get; init; }
    public required MaterialV2 Material { get; init; }
    public required SectionV2 Section { get; init; }
    public required SupportV5[] Supports { get; init; }
    public required PointLoadV5[] PointLoads { get; init; }
    public required DistributedLoadV5[] DistributedLoads { get; init; }
    public required NamingV5 NamingState { get; init; }
    public SelfWeightV2? SelfWeight { get; init; }

    /// <summary>
    /// The segments after the first, in order; absent for a beam of one segment. The first segment is described by
    /// <c>material</c>, <c>section</c>, <c>bendingAxis</c>, <c>sectionDisplayName</c> and <c>length</c>, so a file
    /// without this field is a one-segment project. Each segment follows the previous one.
    /// </summary>
    public SegmentV5[]? FurtherSegments { get; init; }

    /// <summary>
    /// The internal moment releases (hinges); absent for a beam without any, so a file without this field has none.
    /// </summary>
    public MomentReleaseV5[]? MomentReleases { get; init; }
}

/// <summary>
/// An internal moment release strictly inside the beam, at <c>position</c> (m from the beam start) or at a
/// <c>positionRef</c>; exactly one of the two.
/// </summary>
public sealed record MomentReleaseV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public double? Position { get; init; }
    public PositionRefV5? PositionRef { get; init; }
}

/// <summary>
/// A position measured from a segment: <c>offset</c> (m) from the start of that segment, or <c>atEnd</c> for its end.
/// Exactly one of the two is given; a bound end stores no distance of its own. Positions measured from the beam start
/// use the plain position field instead.
/// </summary>
public sealed record PositionRefV5
{
    public required Guid Segment { get; init; }
    public double? Offset { get; init; }
    public bool? AtEnd { get; init; }
}

/// <summary>A point force or moment at <c>position</c> or a <c>positionRef</c>; exactly one of the two.</summary>
public sealed record PointLoadV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public double? Position { get; init; }
    public PositionRefV5? PositionRef { get; init; }
    public required double Value { get; init; }
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

/// <summary>
/// A beam segment after the first, with its <c>length</c> (m) and <c>id</c>. Drafts of this format described each segment by
/// its <c>start</c> (m, strictly inside the beam and ascending) instead; they are still read, and a segment has
/// either one or the other.
/// </summary>
public sealed record SegmentV5
{
    public Guid? Id { get; init; }
    public double? Length { get; init; }
    public double? Start { get; init; }
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
    public double? Position { get; init; }
    public PositionRefV5? PositionRef { get; init; }
    public double? TranslationalStiffness { get; init; }
    public double? RotationalStiffness { get; init; }
    public bool? RigidTranslation { get; init; }
}

/// <summary>A transverse line load whose intensity varies linearly from the start to the end position (N/m).</summary>
public sealed record DistributedLoadV5
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public double? StartPosition { get; init; }
    public PositionRefV5? StartPositionRef { get; init; }
    public double? EndPosition { get; init; }
    public PositionRefV5? EndPositionRef { get; init; }
    public required double StartIntensity { get; init; }
    public required double EndIntensity { get; init; }
}
