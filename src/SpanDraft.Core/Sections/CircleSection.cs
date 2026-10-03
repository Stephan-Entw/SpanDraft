using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>A solid circle bending about a centroidal diameter.</summary>
public sealed class CircleSection : Section
{
    public CircleSection(Length diameter)
        : base(
            Area.FromSquareMeters(Math.PI * Math.Pow(DomainGuard.Positive(diameter.Meters, nameof(diameter)), 2) / 4),
            SecondMomentOfArea.FromMetersToTheFourth(Math.PI * Math.Pow(diameter.Meters, 4) / 64),
            SectionModulus.FromCubicMeters(Math.PI * Math.Pow(diameter.Meters, 3) / 32))
    {
        Diameter = diameter;
    }

    public Length Diameter { get; }
}
