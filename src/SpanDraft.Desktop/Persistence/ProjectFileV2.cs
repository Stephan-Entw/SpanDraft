using System.Text.Json.Serialization;

namespace SpanDraft.Desktop.Persistence;

// Current wire contract. Domain and editor objects are never serialized directly.
public sealed record ProjectFileV2
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required DocumentV2 Document { get; init; }
    public required PresentationV2 Presentation { get; init; }
}

public sealed record DocumentV2
{
    public required double Length { get; init; }
    public required string BendingAxis { get; init; }
    public required MaterialV2 Material { get; init; }
    public required SectionV2 Section { get; init; }
    public required SupportV2[] Supports { get; init; }
    public required PointLoadV2[] PointLoads { get; init; }
    public required DistributedLoadV2[] DistributedLoads { get; init; }
    public required NamingV2 NamingState { get; init; }
}

public sealed record MaterialV2
{
    public required string Name { get; init; }
    public required double YoungsModulus { get; init; }
    public required double YieldStrength { get; init; }
    public double? Density { get; init; }
    public double? PoissonRatio { get; init; }
}

public sealed record SupportV2
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
}

public sealed record PointLoadV2
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required double Position { get; init; }
    public required double Value { get; init; }
}

public sealed record DistributedLoadV2
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required double StartPosition { get; init; }
    public required double EndPosition { get; init; }
    public required double Intensity { get; init; }
}

public sealed record NamingV2
{
    public required long NextSupportOrdinal { get; init; }
    public required long NextForceNumber { get; init; }
    public required long NextMomentNumber { get; init; }
    public required long NextDistributedLoadNumber { get; init; }
}

public sealed record PresentationV2
{
    public required OffsetV2[] AnnotationOffsets { get; init; }
}

public sealed record OffsetV2
{
    public required Guid EntityId { get; init; }
    public required double Dx { get; init; }
    public required double Dy { get; init; }
}

[JsonConverter(typeof(SectionV2Converter))]
public abstract record SectionV2
{
    public abstract string Type { get; }
}

public sealed record RectangleSectionV2 : SectionV2
{
    public override string Type => "rectangle";
    public required double Width { get; init; }
    public required double Height { get; init; }
}

public sealed record RectangularHollowSectionV2 : SectionV2
{
    public override string Type => "rectangularHollow";
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double WallThickness { get; init; }
    public required double OuterRadius { get; init; }
}

public sealed record CircleSectionV2 : SectionV2
{
    public override string Type => "circle";
    public required double Diameter { get; init; }
}

public sealed record CircularHollowSectionV2 : SectionV2
{
    public override string Type => "circularHollow";
    public required double OuterDiameter { get; init; }
    public required double WallThickness { get; init; }
}

public sealed record ISectionV2 : SectionV2
{
    public override string Type => "iSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

public sealed record USectionV2 : SectionV2
{
    public override string Type => "uSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

public sealed record TSectionV2 : SectionV2
{
    public override string Type => "tSection";
    public required double Height { get; init; }
    public required double Width { get; init; }
    public required double WebThickness { get; init; }
    public required double FlangeThickness { get; init; }
    public required double Radius { get; init; }
}

public sealed record AngleSectionV2 : SectionV2
{
    public override string Type => "angle";
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double Thickness { get; init; }
    public required double InnerRadius { get; init; }
}

public sealed record ManualSectionV2 : SectionV2
{
    public override string Type => "manual";
    public required double Area { get; init; }
    public required ManualAxisV2[] Axes { get; init; }
}

public sealed record ManualAxisV2
{
    public required string Designation { get; init; }
    public required double SecondMomentOfArea { get; init; }
    public required double SectionModulus { get; init; }
}
