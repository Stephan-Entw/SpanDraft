using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Geometry;

/// <summary>An immutable point: y is horizontal, z is vertical.</summary>
public readonly record struct SectionPoint
{
    public SectionPoint(SectionCoordinate y, SectionCoordinate z)
    {
        Y = y;
        Z = z;
    }

    public SectionCoordinate Y { get; }
    public SectionCoordinate Z { get; }
    public static SectionPoint FromMeters(double y, double z) =>
        new(SectionCoordinate.FromMeters(y), SectionCoordinate.FromMeters(z));

    public static SectionPoint FromMillimeters(double y, double z) =>
        new(SectionCoordinate.FromMillimeters(y), SectionCoordinate.FromMillimeters(z));
}