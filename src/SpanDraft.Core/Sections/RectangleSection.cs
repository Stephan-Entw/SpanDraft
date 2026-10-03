using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>A rectangle bending about its centroidal axis parallel to Width; Height is transverse.</summary>
public sealed class RectangleSection : Section
{
    public RectangleSection(Length width, Length height)
        : base(
            Area.FromSquareMeters(
                DomainGuard.Positive(width.Meters, nameof(width)) * DomainGuard.Positive(height.Meters, nameof(height))),
            SecondMomentOfArea.FromMetersToTheFourth(width.Meters * Math.Pow(height.Meters, 3) / 12),
            SectionModulus.FromCubicMeters(width.Meters * Math.Pow(height.Meters, 2) / 6))
    {
        Width = width;
        Height = height;
    }

    public Length Width { get; }
    public Length Height { get; }
}
