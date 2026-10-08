using Xunit;
using static SpanDraft.Tests.ParametricSectionTestSupport;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class ParametricSectionNumericalTests
{
    public static IEnumerable<object[]> Scales()
    {
        for (var kind = 0; kind < 8; kind++)
        foreach (var exponent in new[] { -260, -40, 40, 240 })
        {
            yield return [kind, exponent, 0.0];
            if (kind is 1 or >= 4)
                yield return [kind, exponent, 1.5];
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void VerySmallAndLargeRepresentableSectionsRetainScaleLaws(int kind, int exponent, double r)
    {
        var k = Math.ScaleB(1, exponent);
        var expected = Geometry(Shape(kind, r: r)).CalculateProperties();
        var actual = Geometry(Shape(kind, 6 * k, 10 * k, k, k, r * k)).CalculateProperties();
        // The unscaled properties are independently checked by the analytical suite.
        Close(Math.ScaleB(expected.Area.SquareMeters, 2 * exponent), actual.Area.SquareMeters);
        Close(Math.ScaleB(expected.Centroid.Y.Meters, exponent), actual.Centroid.Y.Meters);
        Close(Math.ScaleB(expected.Centroid.Z.Meters, exponent), actual.Centroid.Z.Meters);
        var scale = Math.ScaleB(expected.I1.MetersToTheFourth, 4 * exponent);
        Close(Math.ScaleB(expected.Iy.MetersToTheFourth, 4 * exponent), actual.Iy.MetersToTheFourth, scale);
        Close(Math.ScaleB(expected.Iz.MetersToTheFourth, 4 * exponent), actual.Iz.MetersToTheFourth, scale);
        Close(Math.ScaleB(expected.Iyz.MetersToTheFourth, 4 * exponent), actual.Iyz.MetersToTheFourth, scale);
        Close(scale, actual.I1.MetersToTheFourth);
        Close(Math.ScaleB(expected.I2.MetersToTheFourth, 4 * exponent), actual.I2.MetersToTheFourth);
        Close(expected.PrincipalAxisAngleRadians, actual.PrincipalAxisAngleRadians, 1);
        Close(Math.ScaleB(expected.W1Positive.CubicMeters, 3 * exponent), actual.W1Positive.CubicMeters);
        Close(Math.ScaleB(expected.W1Negative.CubicMeters, 3 * exponent), actual.W1Negative.CubicMeters);
        Close(Math.ScaleB(expected.W2Positive.CubicMeters, 3 * exponent), actual.W2Positive.CubicMeters);
        Close(Math.ScaleB(expected.W2Negative.CubicMeters, 3 * exponent), actual.W2Negative.CubicMeters);
    }

    public static IEnumerable<object[]> UnrepresentableScales()
    {
        for (var kind = 0; kind < 8; kind++)
        foreach (var k in new[] { 1e100, 1e-100 })
            yield return [kind, k];
    }

    [Theory]
    [MemberData(nameof(UnrepresentableScales))]
    public void UnrepresentablePropertiesAreRejectedInTheShapeConstructor(int kind, double k)
    {
        Assert.ThrowsAny<ArgumentException>(() => Shape(kind, 6 * k, 10 * k, k, k));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void LostPositiveWallOrCentredWebThicknessIsRejectedWithoutCorrection(int kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => Shape(kind, t: 1e-18));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void LostPositiveRadiiAreRejectedWithoutSnapping(int kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => Shape(kind, r: double.Epsilon));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void APositiveResidualEdgeLostToRoundingIsNotTreatedAsAnExactLimit(int kind)
    {
        // 3.5 + BitDecrement(2.5) rounds to 6, although the flange still has a positive rest edge.
        Assert.ThrowsAny<ArgumentException>(() => Shape(kind, r: Math.BitDecrement(2.5)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void LostFlangeThicknessIsRejectedWithoutCorrection(int kind)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Shape(kind, tf: 1e-18));
        Assert.Equal("flangeThickness", error.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ThinButRepresentableWallsHaveIndependentFactoredReferences(int kind)
    {
        var t = Math.ScaleB(1, -20);
        var actual = Geometry(Shape(kind, b: 2, h: 2, t: t)).CalculateProperties();
        var inner = 2 - 2 * t;
        var area = kind == 1 ? 4 * t * (2 - t) : Math.PI * t * (2 - t);
        var inertia = kind == 1 ? t * (2 - t) * (4 + inner * inner) / 3 :
            area * (1 + Math.Pow(1 - t, 2)) / 4;
        Close(area, actual.Area.SquareMeters);
        Close(1, actual.Centroid.Y.Meters);
        Close(1, actual.Centroid.Z.Meters);
        Close(inertia, actual.Iy.MetersToTheFourth);
        Close(inertia, actual.Iz.MetersToTheFourth);
        Close(0, actual.Iyz.MetersToTheFourth, inertia);
        Close(inertia, actual.I1.MetersToTheFourth);
        Close(inertia, actual.I2.MetersToTheFourth);
        foreach (var modulus in new[] { actual.W1Positive, actual.W1Negative, actual.W2Positive, actual.W2Negative })
            Close(inertia, modulus.CubicMeters);
    }
}
