using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>Tabulated I and a single W. The domain explicitly assumes W+ = W- = W.</summary>
public sealed record ManualSectionAxis
{
    public ManualSectionAxis(SectionAxisDesignation axisDesignation, SecondMomentOfArea secondMomentOfArea,
        SectionModulus sectionModulus)
    {
        SectionAxes.ValidateDesignation(axisDesignation);
        DomainGuard.Positive(secondMomentOfArea.MetersToTheFourth, nameof(secondMomentOfArea));
        DomainGuard.Positive(sectionModulus.CubicMeters, nameof(sectionModulus));
        AxisDesignation = axisDesignation;
        SecondMomentOfArea = secondMomentOfArea;
        SectionModulus = sectionModulus;
    }

    public SectionAxisDesignation AxisDesignation { get; }
    public SecondMomentOfArea SecondMomentOfArea { get; }
    public SectionModulus SectionModulus { get; }

    internal SectionAxisProperties ToProperties() =>
        new(AxisDesignation, SecondMomentOfArea, SectionModulus, SectionModulus);
}
