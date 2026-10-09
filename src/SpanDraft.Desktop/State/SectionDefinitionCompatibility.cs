using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>The canonical definition corresponding to a legacy section's original inputs.</summary>
internal static class SectionDefinitionCompatibility
{
    internal static ISectionDefinition Normalize(ISectionDefinition section) => section switch
    {
        RectangleSection s => new RectangleSectionGeometry(s.Width, s.Height),
        RectangularHollowSection s => new RectangularHollowSectionGeometry(s.Width, s.Height, s.WallThickness,
            Length.FromMeters(0)),
        CircleSection s => new CircleSectionGeometry(s.Diameter),
        CircularHollowSection s => new CircularHollowSectionGeometry(s.OuterDiameter, s.WallThickness),
        CustomSection s => new ManualSectionDefinition(s.Area,
            new(SectionAxisDesignation.Y, s.SecondMomentOfArea, s.SectionModulus)),
        _ => section
    };
}
