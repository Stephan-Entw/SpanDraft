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
