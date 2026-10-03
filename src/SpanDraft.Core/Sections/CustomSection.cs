using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>Supplied section properties A, I and W for one bending axis, without a geometry approximation.</summary>
public sealed class CustomSection : Section
{
    public CustomSection(Area area, SecondMomentOfArea secondMomentOfArea, SectionModulus sectionModulus)
        : base(area, secondMomentOfArea, sectionModulus)
    {
    }
}
