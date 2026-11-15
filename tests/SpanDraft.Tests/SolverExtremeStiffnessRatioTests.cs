using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.MultiSectionTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Stiffness ratios up to the end of the normal range: the weight scales divide by the softest section, so the
/// outcome must be the closed-form result or a documented rejection, never a non-finite value or a foreign exception.
/// </summary>
public sealed class SolverExtremeStiffnessRatioTests
{
    private const double P = 1000, Length = 2, Boundary = 1;

    private static MechanicalInput SteppedCantilever(int ratioExponent) =>
        Multi(Length, [new(0, Boundary, Math.ScaleB(1, 500)), new(Boundary, Length, Math.ScaleB(1, 500 - ratioExponent))],
            forces: [new(Length, -P)]);

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(900)]
    public void AHugeRatioStillMatchesTheUnitLoadIntegral(int ratioExponent)
    {
        double stiff = Math.ScaleB(1, 500), soft = Math.ScaleB(1, 500 - ratioExponent);
        // w(L) = -P ∫ (L - x)² / EI(x) dx with the stiff part on [0, 1] and the soft part on [1, 2].
        double expected = -P * ((Length * Length * Length - 1) / (3 * stiff) + 1 / (3 * soft));

        BendingResult result = SolverKernel.Solve(SteppedCantilever(ratioExponent));

        double tip = result.EvaluateAt(Length, FieldSide.Left).Bending[0].Value;
        Assert.True(Relative(expected, tip) < 1e-9);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1015)]
    [InlineData(1021)]
    public void ARatioAtTheEdgeOfTheNormalRangeEndsInAResultOrARangeRejection(int ratioExponent)
    {
        Exception? failure = Record.Exception(() => SolverKernel.Solve(SteppedCantilever(ratioExponent)));

        Assert.True(failure is null or NumericalRangeException or KernelFailureException,
            $"Unexpected outcome: {failure}");
    }
}
