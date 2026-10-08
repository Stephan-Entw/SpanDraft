using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Geometry;

/// <summary>Area and centroidal section properties, independent of the beam's existing Section API.</summary>
public sealed class SectionGeometryProperties
{
    internal SectionGeometryProperties(Area area, SectionPoint centroid,
        SecondMomentOfArea iy, SecondMomentOfArea iz, ProductMomentOfArea iyz,
        SecondMomentOfArea i1, SecondMomentOfArea i2, double angle,
        Length d1Positive, Length d1Negative, Length d2Positive, Length d2Negative,
        SectionModulus w1Positive, SectionModulus w1Negative, SectionModulus w2Positive, SectionModulus w2Negative)
    {
        Area = area;
        Centroid = centroid;
        Iy = iy;
        Iz = iz;
        Iyz = iyz;
        I1 = i1;
        I2 = i2;
        PrincipalAxisAngleRadians = angle;
        Axis1PositiveDistance = d1Positive;
        Axis1NegativeDistance = d1Negative;
        Axis2PositiveDistance = d2Positive;
        Axis2NegativeDistance = d2Negative;
        W1Positive = w1Positive;
        W1Negative = w1Negative;
        W2Positive = w2Positive;
        W2Negative = w2Negative;
    }

    public Area Area { get; }
    public SectionPoint Centroid { get; }
    public SecondMomentOfArea Iy { get; }
    public SecondMomentOfArea Iz { get; }
    public ProductMomentOfArea Iyz { get; }
    public SecondMomentOfArea I1 { get; }
    public SecondMomentOfArea I2 { get; }

    /// <summary>
    /// Angle of axis 1 from +y towards +z in [-pi/2, pi/2). Axis 2 is (-sin(angle), cos(angle)).
    /// Roundoff-equal principal moments use angle zero.
    /// </summary>
    public double PrincipalAxisAngleRadians { get; }

    /// <summary>Positive/negative distances along axis 2, normal to principal axis 1.</summary>
    public Length Axis1PositiveDistance { get; }
    public Length Axis1NegativeDistance { get; }

    /// <summary>Positive/negative distances along axis 1, normal to principal axis 2.</summary>
    public Length Axis2PositiveDistance { get; }
    public Length Axis2NegativeDistance { get; }

    public SectionModulus W1Positive { get; }
    public SectionModulus W1Negative { get; }
    public SectionModulus W2Positive { get; }
    public SectionModulus W2Negative { get; }

}