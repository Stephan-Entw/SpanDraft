using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>Immutable properties about one explicitly designated bending axis.</summary>
public sealed record SectionAxisProperties
{
    public SectionAxisProperties(SectionAxisDesignation axisDesignation, SecondMomentOfArea secondMomentOfArea,
        SectionModulus positiveSectionModulus, SectionModulus negativeSectionModulus)
    {
        SectionAxes.ValidateDesignation(axisDesignation);
        DomainGuard.Positive(secondMomentOfArea.MetersToTheFourth, nameof(secondMomentOfArea));
        DomainGuard.Positive(positiveSectionModulus.CubicMeters, nameof(positiveSectionModulus));
        DomainGuard.Positive(negativeSectionModulus.CubicMeters, nameof(negativeSectionModulus));
        AxisDesignation = axisDesignation;
        SecondMomentOfArea = secondMomentOfArea;
        PositiveSectionModulus = positiveSectionModulus;
        NegativeSectionModulus = negativeSectionModulus;
    }

    public SectionAxisDesignation AxisDesignation { get; }
    public SecondMomentOfArea SecondMomentOfArea { get; }
    public SectionModulus PositiveSectionModulus { get; }
    public SectionModulus NegativeSectionModulus { get; }
}
