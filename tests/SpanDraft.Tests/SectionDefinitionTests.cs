using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using Xunit;
using static SpanDraft.Tests.ParametricSectionTestSupport;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class SectionDefinitionTests
{
    public static IEnumerable<object[]> Shapes() => Enumerable.Range(0, 8).Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Shapes))]
    public void CommonContractPreservesShapeGeometryAndCompleteProperties(int kind)
    {
        var section = Shape(kind, r: kind is 1 or >= 4 ? 0.5 : 0);
        var expectedKind = kind switch
        {
            0 => SectionShapeKind.Rectangle,
            1 => SectionShapeKind.RectangularHollow,
            2 => SectionShapeKind.Circle,
            3 => SectionShapeKind.CircularHollow,
            4 => SectionShapeKind.ISection,
            5 => SectionShapeKind.USection,
            6 => SectionShapeKind.TSection,
            _ => SectionShapeKind.Angle
        };
        Assert.Equal(expectedKind, section.ShapeKind);
        Assert.Same(section.Geometry, section.Geometry);
        Assert.Same(section.GeometryProperties, section.GeometryProperties);
        SameProperties(section.Geometry.CalculateProperties(), section.GeometryProperties);
        Assert.Equal(section.Geometry.CalculateProperties().Centroid, section.GeometryProperties.Centroid);
        AssertTensor(Reference(kind, r: kind is 1 or >= 4 ? 0.5 : 0), section.GeometryProperties);
        ISectionDefinition definition = section;
        Assert.Equal(section.GeometryProperties.Area, definition.Area);
        foreach (var axis in definition.Axes)
            Assert.Same(axis, definition.GetAxis(axis.AxisDesignation));
        var list = Assert.IsAssignableFrom<IList<SectionAxisProperties>>(definition.Axes);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = definition.Axes[1]);
    }

    public static IEnumerable<object[]> CoordinateShapes()
    {
        for (var kind = 0; kind < 7; kind++)
        foreach (var wide in new[] { false, true })
        foreach (var rounded in new[] { false, true })
        {
            if (rounded && kind is 0 or 2 or 3) continue;
            yield return [kind, wide, rounded];
        }
    }

    [Theory]
    [MemberData(nameof(CoordinateShapes))]
    public void CoordinateAxesMatchIndependentIntegralsAndSignedFiberDistances(int kind, bool wide, bool rounded)
    {
        var b = wide ? 10.0 : 6.0;
        var h = wide ? 4.0 : 10.0;
        var r = rounded ? 0.5 : 0;
        var section = Shape(kind, b, h, r: r);
        Assert.Equal(new[] { SectionAxisDesignation.Y, SectionAxisDesignation.Z },
            section.Axes.Select(axis => axis.AxisDesignation));
        var y = section.GetAxis(SectionAxisDesignation.Y);
        var z = section.GetAxis(SectionAxisDesignation.Z);
        Assert.Equal(section.GeometryProperties.Iy, y.SecondMomentOfArea);
        Assert.Equal(section.GeometryProperties.Iz, z.SecondMomentOfArea);
        var expected = Reference(kind, b, h, r: r);
        var cy = expected.Qy / expected.A;
        var cz = expected.Qz / expected.A;
        var iy = expected.Jy - expected.A * cz * cz;
        var iz = expected.Jz - expected.A * cy * cy;
        var top = kind is 2 or 3 ? b : h;
        Close(iy / (top - cz), y.PositiveSectionModulus.CubicMeters);
        Close(iy / cz, y.NegativeSectionModulus.CubicMeters);
        Close(iz / (b - cy), z.PositiveSectionModulus.CubicMeters);
        Close(iz / cy, z.NegativeSectionModulus.CubicMeters);
        if (wide && kind is not (2 or 3))
        {
            Assert.True(iz > iy);
            Assert.True(z.SecondMomentOfArea.MetersToTheFourth > y.SecondMomentOfArea.MetersToTheFourth);
            Close(section.GeometryProperties.I1.MetersToTheFourth, z.SecondMomentOfArea.MetersToTheFourth);
            Close(section.GeometryProperties.I2.MetersToTheFourth, y.SecondMomentOfArea.MetersToTheFourth);
        }
        if (kind == 6)
            Assert.True(y.PositiveSectionModulus.CubicMeters > y.NegativeSectionModulus.CubicMeters);
        if (kind == 5)
            Assert.True(z.PositiveSectionModulus.CubicMeters < z.NegativeSectionModulus.CubicMeters);
        Assert.Throws<KeyNotFoundException>(() => section.GetAxis(SectionAxisDesignation.U));
        Assert.Throws<KeyNotFoundException>(() => section.GetAxis(SectionAxisDesignation.V));
    }

    [Theory]
    [InlineData(6, 10, 0)]
    [InlineData(6, 10, 0.5)]
    [InlineData(5, 5, 0)]
    [InlineData(5, 5, 0.5)]
    public void AngleUsesOnlyDirectedPrincipalAxesAndRetainsCoordinateTensor(double b, double h, double r)
    {
        var section = new AngleSectionGeometry(M(b), M(h), M(1), M(r));
        Assert.Equal(new[] { SectionAxisDesignation.U, SectionAxisDesignation.V }, section.Axes.Select(axis => axis.AxisDesignation));
        var u = section.GetAxis(SectionAxisDesignation.U);
        var v = section.GetAxis(SectionAxisDesignation.V);
        var p = section.GeometryProperties;
        Assert.Equal(p.I1, u.SecondMomentOfArea);
        Assert.Equal(p.I2, v.SecondMomentOfArea);
        Assert.Equal(p.W1Positive, u.PositiveSectionModulus);
        Assert.Equal(p.W1Negative, u.NegativeSectionModulus);
        Assert.Equal(p.W2Positive, v.PositiveSectionModulus);
        Assert.Equal(p.W2Negative, v.NegativeSectionModulus);
        Assert.True(u.SecondMomentOfArea.MetersToTheFourth > v.SecondMomentOfArea.MetersToTheFourth);
        AssertTensor(Reference(7, b, h, r: r), p);
        Assert.Throws<KeyNotFoundException>(() => section.GetAxis(SectionAxisDesignation.Y));
        Assert.Throws<KeyNotFoundException>(() => section.GetAxis(SectionAxisDesignation.Z));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IsotropicSectionsRetainDeterministicCoordinateAxes(int kind)
    {
        var section = Shape(kind, b: 6, h: 6);
        Assert.Equal(0, section.GeometryProperties.PrincipalAxisAngleRadians);
        var y = section.GetAxis(SectionAxisDesignation.Y);
        var z = section.GetAxis(SectionAxisDesignation.Z);
        Close(y.SecondMomentOfArea.MetersToTheFourth, z.SecondMomentOfArea.MetersToTheFourth);
        Close(y.PositiveSectionModulus.CubicMeters, z.PositiveSectionModulus.CubicMeters);
        Assert.Equal(SectionAxisDesignation.Y, section.Axes[0].AxisDesignation);
        Assert.Equal(SectionAxisDesignation.Z, section.Axes[1].AxisDesignation);
    }

    [Theory]
    [MemberData(nameof(ParametricSectionNumericalTests.Scales), MemberType = typeof(ParametricSectionNumericalTests))]
    public void DomainAxesRetainInertiaAndModulusScaling(int kind, int exponent, double r)
    {
        var k = Math.ScaleB(1, exponent);
        var before = Shape(kind, r: r);
        var after = Shape(kind, 6 * k, 10 * k, k, k, r * k);
        foreach (var axis in before.Axes)
        {
            var actual = after.GetAxis(axis.AxisDesignation);
            Close(Math.ScaleB(axis.SecondMomentOfArea.MetersToTheFourth, 4 * exponent), actual.SecondMomentOfArea.MetersToTheFourth);
            Close(Math.ScaleB(axis.PositiveSectionModulus.CubicMeters, 3 * exponent), actual.PositiveSectionModulus.CubicMeters);
            Close(Math.ScaleB(axis.NegativeSectionModulus.CubicMeters, 3 * exponent), actual.NegativeSectionModulus.CubicMeters);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ParametricLookupRejectsUndefinedDesignations(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape(0).GetAxis((SectionAxisDesignation)value));
}
