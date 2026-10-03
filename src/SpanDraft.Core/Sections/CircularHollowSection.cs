using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>A circular tube of uniform wall thickness, bending about a centroidal diameter.</summary>
public sealed class CircularHollowSection : Section
{
    public CircularHollowSection(Length outerDiameter, Length wallThickness)
        : this(outerDiameter, wallThickness, CalculateProperties(outerDiameter, wallThickness))
    {
    }

    private CircularHollowSection(Length outerDiameter, Length wallThickness,
        (Area Area, SecondMomentOfArea Inertia, SectionModulus Modulus) properties)
        : base(properties.Area, properties.Inertia, properties.Modulus)
    {
        OuterDiameter = outerDiameter;
        WallThickness = wallThickness;
    }

    public Length OuterDiameter { get; }
    public Length WallThickness { get; }

    private static (Area, SecondMomentOfArea, SectionModulus) CalculateProperties(
        Length outerDiameter, Length wallThickness)
    {
        double d = DomainGuard.Positive(outerDiameter.Meters, nameof(outerDiameter));
        double t = DomainGuard.Positive(wallThickness.Meters, nameof(wallThickness));
        if (t >= d / 2)
            throw new ArgumentOutOfRangeException(nameof(wallThickness), "The inner diameter must be positive.");

        double innerDiameter = d - 2 * t;
        double area = Math.PI * 2 * t * (d + innerDiameter) / 4;
        double inertia = Math.PI * 2 * t * (d + innerDiameter) *
            (d * d + innerDiameter * innerDiameter) / 64;
        return (Area.FromSquareMeters(area), SecondMomentOfArea.FromMetersToTheFourth(inertia),
            SectionModulus.FromCubicMeters(inertia / (d / 2)));
    }
}
