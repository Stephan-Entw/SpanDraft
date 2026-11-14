using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class PolynomialRootsTests
{
    [Fact]
    public void ZeroAndNonzeroConstantsHaveNoIsolatedRoots()
    {
        Assert.Empty(PolynomialRoots.InUnitInterval(0, 0, 0, 0));
        Assert.Empty(PolynomialRoots.InUnitInterval(4));
    }

    [Fact]
    public void LeadingZerosReduceDegreeAndLinearRootIsExact()
    {
        Assert.Equal(new[] { 0.25 }, PolynomialRoots.InUnitInterval(-1, 4, 0, 0));
    }

    [Fact]
    public void QuadraticFindsBothRealRootsAndRejectsComplexRoots()
    {
        Roots([0.16, -1, 1], [0.2, 0.8]);
        Assert.Empty(PolynomialRoots.InUnitInterval(1, 0, 1));
    }

    [Fact]
    public void QuadraticAvoidsCancellationForSmallRoot()
    {
        Roots([1e-10, -1, 1], [1.0000000001e-10, 0.9999999999]);
    }

    [Fact]
    public void DoubleAndTripleRootsAreReportedOnce()
    {
        Roots([0.25, -1, 1], [0.5]);
        Roots([-0.125, 0.75, -1.5, 1], [0.5]);
        // (t-.25)^2*(t-.75), including a root with no sign change.
        Roots([-0.046875, 0.4375, -1.25, 1], [0.25, 0.75]);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    public void NonbinaryMultipleRootsAreNotDuplicatedByRoundoff(double root)
    {
        Roots([root * root, -2 * root, 1], [root]);
        Roots([-root * root * root, 3 * root * root, -3 * root, 1], [root]);
    }

    [Fact]
    public void CubicFindsAllThreeRootsThroughDerivativeIntervals()
    {
        Roots([-0.08, 0.66, -1.5, 1], [0.2, 0.5, 0.8]);
        Assert.Equal(PolynomialRoots.InUnitInterval(-0.08, 0.66, -1.5, 1),
            PolynomialRoots.InUnitInterval(-0.08, 0.66, -1.5, 1));
    }

    [Fact]
    public void CubicWithOnlyOneRealRootUsesItsMonotoneInterval()
    {
        // (t-.4)*(t²+1)
        Roots([-0.4, 1, -0.4, 1], [0.4]);
    }

    [Fact]
    public void EndpointsAreDeduplicatedAndOutsideRootsAreRejected()
    {
        Roots([0, -1, 1], [0, 1]);
        Roots([0, -1, 0, 1], [0, 1]);
        Assert.Empty(PolynomialRoots.InUnitInterval(0.01, 1));
        Assert.Empty(PolynomialRoots.InUnitInterval(-1.01, 1));
        Roots([1e-16, 1], [0]);
        Roots([-1 - 1e-15, 1], [1]);
    }

    [Fact]
    public void QuarticWithThreeStationaryPointsFindsAllFourRoots()
    {
        // (t - .1)(t - .35)(t - .6)(t - .9): the derivative has three roots inside the unit interval.
        double[] c = Expand(0.1, 0.35, 0.6, 0.9);
        Roots(c, [0.1, 0.35, 0.6, 0.9]);
        Roots(c.Select(v => -v).ToArray(), [0.1, 0.35, 0.6, 0.9]);
    }

    [Fact]
    public void QuarticFindsRootsOnBothSidesOfAnInteriorPlateau()
    {
        // t (t - .5)² (t - 1): double root between two simple roots at the interval ends.
        Roots(Expand(0, 0.5, 0.5, 1), [0, 0.5, 1]);
        // (t - .25)² (t - .75)²: two double roots without any sign change.
        Roots(Expand(0.25, 0.25, 0.75, 0.75), [0.25, 0.75]);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    public void QuadrupleRootIsReportedOnce(double root) => Roots(Expand(root, root, root, root), [root]);

    [Fact]
    public void QuarticWithTwoRealRootsIgnoresTheComplexPair()
    {
        // (t - .2)(t - .8)(t² + 1)
        double[] c = Multiply(Expand(0.2, 0.8), [1, 0, 1]);
        Roots(c, [0.2, 0.8]);
    }

    [Theory]
    [InlineData(1e-150)]
    [InlineData(1e150)]
    public void QuarticScaleDoesNotChangeItsRoots(double scale) =>
        Roots(Expand(0.1, 0.35, 0.6, 0.9).Select(v => v * scale).ToArray(), [0.1, 0.35, 0.6, 0.9]);

    [Fact]
    public void QuarticDegeneratesToLowerDegreeWhenTheLeadingCoefficientVanishes()
    {
        Roots([-0.08, 0.66, -1.5, 1, 0], [0.2, 0.5, 0.8]);
        Roots([0.16, -1, 1, 0, 0], [0.2, 0.8]);
    }

    [Theory]
    [InlineData(0.5, 1e-3)]
    [InlineData(0.5, 1e-5)]
    public void NearbyRootPairsOfAQuarticAreResolvedAsTwoRoots(double center, double gap)
    {
        // Pairs wider than the clustering tolerance stay two roots.
        double[] c = Expand(center - gap, center + gap, 0.1, 0.9);
        IReadOnlyList<double> roots = PolynomialRoots.InUnitInterval(c);
        Assert.Equal(4, roots.Count);
        Close(center - gap, roots[1], 1e-9);
        Close(center + gap, roots[2], 1e-9);
    }

    private static double[] Expand(params double[] roots)
    {
        double[] result = [1];
        foreach (double root in roots)
            result = Multiply(result, [-root, 1]);
        return result;
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        double[] result = new double[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++)
                result[i + j] += a[i] * b[j];
        return result;
    }

    [Theory]
    [InlineData(1e-200)]
    [InlineData(1e200)]
    [InlineData(-1e200)]
    public void CoefficientScaleDoesNotChangeRoots(double scale)
    {
        Roots(new[] { -0.08, 0.66, -1.5, 1 }.Select(v => v * scale).ToArray(), [0.2, 0.5, 0.8]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfinitePolynomialIsTypedNumericalFailure(double value)
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => PolynomialRoots.InUnitInterval(1, value));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
    }

    private static void Roots(double[] coefficients, double[] expected)
    {
        IReadOnlyList<double> actual = PolynomialRoots.InUnitInterval(coefficients);
        Assert.Equal(expected.Length, actual.Count);
        foreach (var pair in expected.Zip(actual)) Close(pair.First, pair.Second, 1e-12);
    }
}
