using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections;

/// <summary>Constant section properties about the single centroidal bending axis used by the beam.</summary>
public abstract class Section
{
    private protected Section(Area area, SecondMomentOfArea secondMomentOfArea, SectionModulus sectionModulus)
    {
        DomainGuard.Positive(area.SquareMeters, nameof(area));
        DomainGuard.Positive(secondMomentOfArea.MetersToTheFourth, nameof(secondMomentOfArea));
        DomainGuard.Positive(sectionModulus.CubicMeters, nameof(sectionModulus));

        Area = area;
        SecondMomentOfArea = secondMomentOfArea;
        SectionModulus = sectionModulus;
    }

    /// <summary>Cross-sectional area A.</summary>
    public Area Area { get; }

    /// <summary>Second moment of area I about the considered centroidal bending axis.</summary>
    public SecondMomentOfArea SecondMomentOfArea { get; }

    /// <summary>Elastic section modulus W about the same bending axis.</summary>
    public SectionModulus SectionModulus { get; }
}
