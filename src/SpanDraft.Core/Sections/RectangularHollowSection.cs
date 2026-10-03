using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>A sharp-cornered rectangular tube of uniform wall thickness, bending about the centroidal axis parallel to Width.</summary>
public sealed class RectangularHollowSection : Section
{
    public RectangularHollowSection(Length width, Length height, Length wallThickness)
        : this(width, height, wallThickness, CalculateProperties(width, height, wallThickness))
    {
    }

    private RectangularHollowSection(Length width, Length height, Length wallThickness,
        (Area Area, SecondMomentOfArea Inertia, SectionModulus Modulus) properties)
        : base(properties.Area, properties.Inertia, properties.Modulus)
    {
        Width = width;
        Height = height;
        WallThickness = wallThickness;
    }

    public Length Width { get; }
    public Length Height { get; }
    public Length WallThickness { get; }

    private static (Area, SecondMomentOfArea, SectionModulus) CalculateProperties(
        Length width, Length height, Length wallThickness)
    {
        double b = DomainGuard.Positive(width.Meters, nameof(width));
        double h = DomainGuard.Positive(height.Meters, nameof(height));
        double t = DomainGuard.Positive(wallThickness.Meters, nameof(wallThickness));
        if (t >= Math.Min(b, h) / 2)
            throw new ArgumentOutOfRangeException(nameof(wallThickness), "The inner width and height must be positive.");

        double innerWidth = b - 2 * t;
        double innerHeight = h - 2 * t;
        // Factored differences avoid subtracting nearly equal outer/inner section properties.
        double area = 2 * t * (b + innerHeight);
        double inertia = 2 * t * (Math.Pow(h, 3) + innerWidth *
            (h * h + h * innerHeight + innerHeight * innerHeight)) / 12;
        return (Area.FromSquareMeters(area), SecondMomentOfArea.FromMetersToTheFourth(inertia),
            SectionModulus.FromCubicMeters(inertia / (h / 2)));
    }
}
