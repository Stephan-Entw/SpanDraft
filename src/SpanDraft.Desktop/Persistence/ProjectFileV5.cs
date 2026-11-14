namespace SpanDraft.Desktop.Persistence;

// Current envelope. Material, section, supports, point loads, naming and presentation keep their V2 contracts;
// distributed loads carry a start and an end intensity.
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
    public required SupportV2[] Supports { get; init; }
    public required PointLoadV2[] PointLoads { get; init; }
    public required DistributedLoadV5[] DistributedLoads { get; init; }
    public required NamingV2 NamingState { get; init; }
    public SelfWeightV2? SelfWeight { get; init; }
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
