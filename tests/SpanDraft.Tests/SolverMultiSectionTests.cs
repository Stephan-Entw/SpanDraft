using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.MultiSectionTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

internal static class MultiSectionTestSupport
{
    internal static Material Steel(double e = E) =>
        new("Test steel", Pressure.FromPascals(e), Pressure.FromMegapascals(235), MaterialStrengthType.Re,
            MassDensity.FromKilogramsPerCubicMeter(7850));

    internal static ManualSectionDefinition Manual(double i, double area = 0.003) =>
        new(Area.FromSquareMeters(area), new ManualSectionAxis(SectionAxisDesignation.Y,
            SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(i / 0.04)));

    /// <summary>A segment of the standard test material with the second moment of area i (m⁴).</summary>
    internal static BeamSegment Seg(double start, double end, double i, double e = E) =>
        new(M(start), M(end), Steel(e), Manual(i), SectionAxisDesignation.Y);

    internal static BeamModel Stepped(double length, BeamSegment[] segments, IEnumerable<Support> supports,
        IEnumerable<BeamLoad> loads) => new(M(length), segments, supports, loads);

    internal static MechanicalInput Multi(double length, StiffnessSection[] sections, RigidRestraint[]? restraints = null,
        TransversePointForce[]? forces = null, AppliedPointMoment[]? moments = null, TransverseLineLoad[]? lines = null,
        ElasticRestraint[]? elastic = null) =>
        new(length, sections, restraints ?? (elastic is null ? [new(0, U | W | Theta)] : []),
            forces ?? [], moments ?? [], lines ?? [], elastic);

    internal static double Relative(double expected, double actual) =>
        Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
}

/// <summary>
/// Beams with several segments through the public solver against independently derived closed forms.
/// The deflection integrals follow from the unit-load method w = ∫ M m / EI dx on piecewise constant EI.
/// </summary>
public sealed class SolverMultiSectionAnalyticalTests
{
    private const double I1 = 8e-6;

    [Theory]
    [InlineData(0.8, 3.0)]
    [InlineData(0.5, 0.2)]
    [InlineData(1.9, 10.0)]
    public void SteppedCantileverTipMatchesTheUnitLoadIntegral(double l1, double stiffnessRatio)
    {
        // w(L) = -P ∫ (L - x)² / EI(x) dx, θ(L) = -P ∫ (L - x) / EI(x) dx; M(0) = P L independent of EI.
        const double p = 1500;
        double ei1 = E * I1, ei2 = E * I1 / stiffnessRatio, l2 = L - l1;
        BeamSolution solution = Solve(Stepped(L, [Seg(0, l1, I1), Seg(l1, L, I1 / stiffnessRatio)],
            [SupportAt(0, SupportType.Fixed)], [Point(L, -p)]));

        double w = -p * ((L * L * L - l2 * l2 * l2) / (3 * ei1) + l2 * l2 * l2 / (3 * ei2));
        double theta = -p * ((L * L - l2 * l2) / (2 * ei1) + l2 * l2 / (2 * ei2));
        Assert.True(Relative(w, At(solution, L).TransverseDisplacement.Meters) < 1e-11);
        Assert.True(Relative(theta, At(solution, L).RotationRadians) < 1e-11);
        Assert.True(Relative(p, At(solution, 0).ReactionY!.Value.Newtons) < 1e-11);
        Assert.True(Relative(p * L, At(solution, 0).ReactionMoment!.Value.NewtonMeters) < 1e-11);
        Assert.Equal(3, solution.Nodes.Count);
        BeamNodeResult boundary = At(solution, l1);
        Assert.Null(boundary.ReactionY);
        Assert.Null(boundary.ReactionMoment);
        Assert.Null(boundary.ReactionX);
    }

    [Theory]
    [InlineData(1.0, 1.0, 1.0, 1.0)]
    [InlineData(1.0, 1.0, 3.0, 1.0)]
    [InlineData(1.2, 0.8, 1.0, 5.0)]
    [InlineData(0.7, 1.3, 20.0, 1.0)]
    public void TwoSpansWithTheStiffnessChangeOnTheMiddleSupportFollowTheThreeMomentEquation(
        double l1, double l2, double i1Factor, double i2Factor)
    {
        // M_B = -q (L1³/I1 + L2³/I2) / (8 (L1/I1 + L2/I2)); the reactions follow from the span equilibria.
        const double q = 800;
        double i1 = I1 * i1Factor, i2 = I1 * i2Factor, length = l1 + l2;
        BeamSolution solution = Solve(Stepped(length, [Seg(0, l1, i1), Seg(l1, length, i2)],
            [SupportAt(0, SupportType.Pinned), SupportAt(l1, SupportType.Roller), SupportAt(length, SupportType.Roller)],
            [Uniform(0, length, -q)]));

        double mB = -q * (l1 * l1 * l1 / i1 + l2 * l2 * l2 / i2) / (8 * (l1 / i1 + l2 / i2));
        double rA = q * l1 / 2 + mB / l1, rC = q * l2 / 2 + mB / l2, rB = q * length - rA - rC;
        Assert.True(Relative(mB, solution.EvaluateAt(M(l1), EvaluationSide.Left).BendingMoment.NewtonMeters) < 1e-10);
        Assert.True(Relative(mB, solution.EvaluateAt(M(l1), EvaluationSide.Right).BendingMoment.NewtonMeters) < 1e-10);
        Assert.True(Relative(rA, At(solution, 0).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(rB, At(solution, l1).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(rC, At(solution, length).ReactionY!.Value.Newtons) < 1e-10);
        Assert.Equal(0, At(solution, l1).TransverseDisplacement.Meters);
    }

    [Theory]
    [InlineData(0.5, 4.0)]
    [InlineData(0.9, 0.25)]
    public void SteppedShaftMidspanDeflectionMatchesTheSymmetricUnitLoadIntegral(double a, double middleFactor)
    {
        // Ends [0, a] and [L - a, L] carry EI1, the middle EI2: w(L/2) = -(P / 2) (a³ / (3 EI1) + (L³ / 8 - a³) / (3 EI2)).
        const double p = 2400;
        double ei1 = E * I1, ei2 = E * I1 * middleFactor;
        BeamSolution solution = Solve(Stepped(L, [Seg(0, a, I1), Seg(a, L - a, I1 * middleFactor), Seg(L - a, L, I1)],
            [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], [Point(L / 2, -p)]));

        double w = -p / 2 * (a * a * a / (3 * ei1) + (L * L * L / 8 - a * a * a) / (3 * ei2));
        Assert.True(Relative(w, At(solution, L / 2).TransverseDisplacement.Meters) < 1e-11);
        Assert.True(Relative(p / 2, At(solution, 0).ReactionY!.Value.Newtons) < 1e-11);
        Assert.Equal(0, At(solution, L / 2).RotationRadians, 1e-15);
    }

    [Fact]
    public void ABoundaryOnAFixedSupportDecouplesTheSidesWithTheirOwnStiffness()
    {
        // The support at x1 separates two independent cantilevers with EI1 (left) and EI2 (right).
        const double x1 = 0.7, pLeft = 900, pRight = 1300, ratio = 6;
        double ei1 = E * I1, ei2 = E * I1 / ratio;
        BeamSolution solution = Solve(Stepped(L, [Seg(0, x1, I1), Seg(x1, L, I1 / ratio)],
            [SupportAt(x1, SupportType.Fixed)], [Point(0, -pLeft), Point(L, -pRight)]));

        Assert.True(Relative(-pLeft * x1 * x1 * x1 / (3 * ei1), At(solution, 0).TransverseDisplacement.Meters) < 1e-11);
        Assert.True(Relative(-pRight * Math.Pow(L - x1, 3) / (3 * ei2), At(solution, L).TransverseDisplacement.Meters) < 1e-11);
        Assert.True(Relative(pLeft + pRight, At(solution, x1).ReactionY!.Value.Newtons) < 1e-11);
        Assert.True(Relative(-pLeft * x1 + pRight * (L - x1), At(solution, x1).ReactionMoment!.Value.NewtonMeters) < 1e-10);
    }

    [Theory]
    [InlineData(1e3)]
    [InlineData(1e5)]
    [InlineData(1e8)]
    public void ASpringOnTheBoundaryAndTheLoadActsThroughTheSteppedFlexibility(double k)
    {
        // Simply supported, segments [0, L/2] (EI1) and [L/2, L] (EI2), spring and load P at the boundary:
        // f = (L/2)³ / 12 (1/EI1 + 1/EI2) / 2 · ... = L³ (1/EI1 + 1/EI2) / 96, w = -P f / (1 + k f).
        const double p = 1800;
        double ei1 = E * I1, ei2 = E * I1 / 4;
        double f = L * L * L * (1 / ei1 + 1 / ei2) / 96, w = -p * f / (1 + k * f);
        BeamSolution solution = Solve(Stepped(L, [Seg(0, L / 2, I1), Seg(L / 2, L, I1 / 4)],
            [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
                SolverSpringAnalyticalTests.Spring(L / 2, k)], [Point(L / 2, -p)]));

        Assert.True(Relative(w, At(solution, L / 2).TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-k * w, At(solution, L / 2).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative((p + k * w) / 2, At(solution, 0).ReactionY!.Value.Newtons) < 1e-9);
    }

    [Fact]
    public void ATranslationalSpringOnTheBoundaryIsNotACoincidenceError()
    {
        // The coincidence rule of spring supports concerns supports only; a segment boundary is none.
        BeamModel beam = Stepped(L, [Seg(0, L / 2, I1), Seg(L / 2, L, I1 / 2)],
            [SupportAt(0, SupportType.Fixed), SolverSpringAnalyticalTests.Spring(L / 2, 1e5),
                SolverSpringAnalyticalTests.Spring(L, 2e5)], [Point(L, -500)]);
        Assert.Empty(SpanDraft.Core.Validation.BeamModelValidator.Validate(beam));
        Assert.Contains(Solve(beam).Nodes, node => node.Position == M(L / 2) && node.ReactionY is not null);
    }

    [Fact]
    public void PointForceAndMomentOnTheBoundaryAreStationJumpsOfTheStationState()
    {
        // The jumps of V and M at the boundary equal the applied force and the negative applied couple;
        // w and θ stay continuous.
        const double p = -700, c = 260;
        BeamSolution solution = Solve(Stepped(L, [Seg(0, 1.1, I1), Seg(1.1, L, I1 / 3)],
            [SupportAt(0, SupportType.Fixed)], [Point(1.1, p), Couple(1.1, c)]));
        BeamSectionResult left = solution.EvaluateAt(M(1.1), EvaluationSide.Left);
        BeamSectionResult right = solution.EvaluateAt(M(1.1), EvaluationSide.Right);

        Assert.Equal(p, right.ShearForce.Newtons - left.ShearForce.Newtons, 1e-9);
        Assert.Equal(-c, right.BendingMoment.NewtonMeters - left.BendingMoment.NewtonMeters, 1e-9);
        Assert.Equal(left.TransverseDisplacement, right.TransverseDisplacement);
        Assert.Equal(left.RotationRadians, right.RotationRadians);
    }
}
