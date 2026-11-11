using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using SpanDraft.Tests.SolverReference;
using static SpanDraft.Tests.SolverReference.ReferenceTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Solver-only checks for features Core cannot express yet: elastic supports, changes of EI,
/// linearly varying loads and imposed curvature. Expected values follow from statics and
/// closed-form beam formulas unless a comment names another source.
/// </summary>
public class PrototypeFutureFeatureTests
{
    private static ReferenceBeamSolution Solve(BeamModel beam) => ReferenceBeamSolver.Prototype(beam);
    private static ReferenceBeamSolution SolveWith(BeamModel beam, ElasticSupport[]? springs = null, double[]? hinges = null,
        PointAction[]? rigidity = null, LinearAction[]? linear = null, UniformAction[]? curvature = null) =>
        ReferenceBeamSolver.Prototype(beam, new SolverLoads([], [], [], [], [], hinges ?? [], springs ?? [],
            rigidity, linear, curvature));
    private static BeamSectionResult Right(ReferenceBeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Right);
    private static void Relative(double expected, double actual, double tolerance = 1e-12) =>
        Assert.True(Math.Abs(actual - expected) <= tolerance * Math.Abs(expected), $"Expected {expected:R}, got {actual:R}.");

    [Fact]
    public void CentralSpringTakesItsShareOfAMidspanLoad()
    {
        double flexibility = L * L * L / (48 * EI), k = 3 / flexibility, load = -1000;
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], [Point(L / 2, load)]);
        ReferenceBeamSolution solution = SolveWith(beam, [new ElasticSupport(L / 2, k)]);
        double w = load * flexibility / (1 + k * flexibility);
        Relative(w, At(solution, L / 2).TransverseDisplacement.Meters);
        Relative(-k * w, At(solution, L / 2).ReactionY!.Value.Newtons);
        Relative(-(load - k * w) / 2, At(solution, 0).ReactionY!.Value.Newtons);
    }

    [Fact]
    public void RotationalSpringAddsRigidBodyRotationToACantilever()
    {
        double kTheta = 4e5, load = -1000;
        BeamModel beam = Beam([], [Point(L, load)]);
        ReferenceBeamSolution solution = SolveWith(beam, [new ElasticSupport(0, double.PositiveInfinity, kTheta, AxialRestraint: true)]);
        Relative(-load * L, At(solution, 0).ReactionMoment!.Value.NewtonMeters);
        Relative(load * L / kTheta, At(solution, 0).RotationRadians);
        Relative(load * L * L / kTheta + load * L * L * L / (3 * EI), At(solution, L).TransverseDisplacement.Meters);
    }

    [Fact]
    public void ChangeOfStiffnessFollowsTheCantileverIntegral()
    {
        double outer = 5 * EI, load = -1000, b = L / 2;
        BeamModel beam = Cantilever(Point(L, load));
        ReferenceBeamSolution solution = SolveWith(beam, rigidity: [new PointAction(L / 2, outer)]);
        // w(L) = P * ∫ (L-x)²/EI(x) dx.
        Relative(load * ((L * L * L - b * b * b) / (3 * EI) + b * b * b / (3 * outer)), At(solution, L).TransverseDisplacement.Meters);
        Relative(-load * L, At(solution, 0).ReactionMoment!.Value.NewtonMeters);
    }

    [Fact]
    public void ChangeOfStiffnessOnAClampSumsBothFaces()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Fixed), SupportAt(L, SupportType.Roller)],
            [Point(.5, -1000), Point(1.5, -1000)]);
        ReferenceBeamSolution uniform = Solve(beam);
        ReferenceBeamSolution changed = SolveWith(beam, rigidity: [new PointAction(1, 2 * EI)]);
        // Each span is clamped at x = 1, so EI does not change any force.
        Relative(uniform.Nodes.Single(n => n.Position.Meters == 1).ReactionMoment!.Value.NewtonMeters,
            At(changed, 1).ReactionMoment!.Value.NewtonMeters);
        Relative(At(uniform, 1.5).TransverseDisplacement.Meters / 2, At(changed, 1.5).TransverseDisplacement.Meters);
    }

    [Fact]
    public void TriangularLoadHasItsMaximumMomentAtOneOverRootThree()
    {
        double q0 = -3000;
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)]);
        ReferenceBeamSolution solution = SolveWith(beam, linear: [new LinearAction(0, L, 0, q0)]);
        Relative(-q0 * L * L / (9 * Math.Sqrt(3)), solution.Extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-13);
        Relative(L / Math.Sqrt(3), solution.Extrema.MaximumBendingMoment.Position.Meters, 1e-9);
        Relative(-q0 * L / 6, At(solution, 0).ReactionY!.Value.Newtons);
    }

    [Fact]
    public void LoadWithSignChangeHasAnInteriorShearMaximum()
    {
        double q0 = 3000;
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)]);
        ReferenceBeamSolution solution = SolveWith(beam, linear: [new LinearAction(0, L, q0, -q0)]);
        Relative(q0 * L / 12, solution.Extrema.MaximumShearForce.Value.Newtons);
        Relative(L / 2, solution.Extrema.MaximumShearForce.Position.Meters, 1e-12);
    }

    [Fact]
    public void ShortTriangularLoadKeepsItsResultantAndCentroid()
    {
        double end = 1 + 6e-9, peak = -1e9;
        // The load length is the stored difference, not 6e-9: 1 + 6e-9 is not representable exactly.
        double a = end - 1;
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)]);
        ReferenceBeamSolution solution = SolveWith(beam, linear: [new LinearAction(1, end, 0, peak)]);
        double resultant = peak * a / 2, centroid = 1 + 2 * a / 3;
        Relative(-resultant * (L - centroid) / L, At(solution, 0).ReactionY!.Value.Newtons);
        Relative(-resultant * centroid / L, At(solution, L).ReactionY!.Value.Newtons);
    }

    [Fact]
    public void ImposedCurvatureIsFreeOnASimpleSpanAndRestrainedBetweenClamps()
    {
        double kappa = 1e-3;
        ReferenceBeamSolution free = SolveWith(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)]),
            curvature: [new UniformAction(0, L, kappa)]);
        Relative(-kappa * L * L / 8, Right(free, L / 2).TransverseDisplacement.Meters);
        Close(0, Right(free, L / 2).BendingMoment.NewtonMeters, 1e-9);

        ReferenceBeamSolution held = SolveWith(Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Fixed)]),
            curvature: [new UniformAction(0, L, kappa)]);
        Relative(-EI * kappa, Right(held, L / 2).BendingMoment.NewtonMeters);
        Close(0, Right(held, L / 2).TransverseDisplacement.Meters, 1e-18);
    }

    [Fact]
    public void StiffSpringsSixNanometresApartMatchExactArithmetic()
    {
        // Expected values from the exact rational force-method reference used for the spring study.
        double k = 4.480000000000001e16;
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], [Point(.6, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, [new ElasticSupport(1, k), new ElasticSupport(1 + 6e-9, k)]);
        Relative(12395.99564491011, At(solution, 1).ReactionY!.Value.Newtons, 1e-10);
        Relative(-11603.995645505118, At(solution, 1 + 6e-9).ReactionY!.Value.Newtons, 1e-10);
        Relative(303.999965485517, At(solution, 0).ReactionY!.Value.Newtons);
        Relative(-3.642856543128868e-05, Right(solution, .6).TransverseDisplacement.Meters);
        Relative(-96.00003451448303, Right(solution, 1).BendingMoment.NewtonMeters);
    }

    [Fact]
    public void SpringsAloneCarryABeamWhenOneRestrainsAxialMotion()
    {
        BeamModel beam = Beam([], [Point(L / 2, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, [new ElasticSupport(0, 1e6, AxialRestraint: true), new ElasticSupport(L, 1e6)]);
        Relative(-1000.0 / 2 / 1e6, At(solution, 0).TransverseDisplacement.Meters);
        Assert.Throws<BeamSolverException>(() => SolveWith(beam, [new ElasticSupport(0, 1e6), new ElasticSupport(L, 1e6)]));
    }

    [Fact]
    public void HingeBetweenTwoSpringsIsAMechanism()
    {
        BeamModel beam = Beam([], [Point(.5, -1000)]);
        var exception = Assert.Throws<BeamSolverException>(() =>
            SolveWith(beam, [new ElasticSupport(0, 1e6, AxialRestraint: true), new ElasticSupport(L, 1e6)], [1]));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void AmbiguousSpringPositionsAreRejected()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], [Point(.5, -1000)]);
        Assert.Equal(SolverErrorCode.InvalidModel,
            Assert.Throws<BeamSolverException>(() => SolveWith(beam, [new ElasticSupport(0, 1e6)])).Code);
        Assert.Equal(SolverErrorCode.InvalidModel,
            Assert.Throws<BeamSolverException>(() => SolveWith(beam, [new ElasticSupport(1, 1e6, 1e4)], [1])).Code);
    }
}
