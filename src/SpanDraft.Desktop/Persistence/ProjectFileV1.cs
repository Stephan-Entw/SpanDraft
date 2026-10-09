namespace SpanDraft.Desktop.Persistence;

// Frozen historical wire contract. Read-only migration is implemented in ProjectFileV1Reader.
public sealed record ProjectFileV1
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required DocumentV1 Document { get; init; }
    public required PresentationV1 Presentation { get; init; }
}

public sealed record DocumentV1
{
    public required double Length { get; init; }
    public required MaterialV1 Material { get; init; }
    public required SectionV1 Section { get; init; }
    public required SupportV1[] Supports { get; init; }
    public required PointLoadV1[] PointLoads { get; init; }
    public required DistributedLoadV1[] DistributedLoads { get; init; }
    public required NamingV1 NamingState { get; init; }
}

public sealed record MaterialV1
{
    public required string Name { get; init; }
    public required double YoungsModulus { get; init; }
    public required double YieldStrength { get; init; }
}

public sealed record SectionV1
{
    public required string Type { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? WallThickness { get; init; }
    public double? Diameter { get; init; }
    public double? OuterDiameter { get; init; }
    public double? Area { get; init; }
    public double? SecondMomentOfArea { get; init; }
    public double? SectionModulus { get; init; }
}

public sealed record SupportV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
}

public sealed record PointLoadV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
    public required double Value { get; init; }
}

public sealed record DistributedLoadV1
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required double StartPosition { get; init; }
    public required double EndPosition { get; init; }
    public required double Intensity { get; init; }
}

public sealed record NamingV1
{
    public required long NextSupportOrdinal { get; init; }
    public required long NextForceNumber { get; init; }
    public required long NextMomentNumber { get; init; }
    public required long NextDistributedLoadNumber { get; init; }
}

public sealed record PresentationV1
{
    public required OffsetV1[] AnnotationOffsets { get; init; }
}

public sealed record OffsetV1
{
    public required Guid EntityId { get; init; }
    public required double Dx { get; init; }
    public required double Dy { get; init; }
}
