using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using SpanDraft.Tests.SolverReference;
using static SpanDraft.Tests.SolverReference.ReferenceTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class PrototypeHingeTests
{
    private static ReferenceBeamSolution SolveWith(BeamModel beam, params double[] hinges) =>
        ReferenceBeamSolver.Prototype(beam, new SolverLoads([], [], [], [], [], hinges));
    private static BeamSectionResult Right(ReferenceBeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Right);
    private static BeamSectionResult Left(ReferenceBeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Left);

    [Fact]
    public void ClampHingeRollerFollowsStatics()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)], [Point(1.5, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, 1);
        ForceClose(500, At(solution, L).ReactionY);
        ForceClose(500, At(solution, 0).ReactionY);
        MomentClose(500, At(solution, 0).ReactionMoment);
        Close(0, Left(solution, 1).BendingMoment.NewtonMeters, 1e-9);
        Close(0, Right(solution, 1).BendingMoment.NewtonMeters, 1e-9);
        // Tip load 500 N on a 1 m cantilever.
        double w = -500.0 * 1 / (3 * EI);
        DisplacementClose(w, At(solution, 1).TransverseDisplacement);
        RotationClose(-500.0 / (2 * EI), Left(solution, 1).RotationRadians);
        Assert.NotEqual(Left(solution, 1).RotationRadians, Right(solution, 1).RotationRadians);
    }

    [Fact]
    public void DropInSpanBetweenTwoClampsIsStaticallyDeterminate()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Fixed)], [Point(1, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, .5, 1.5);
        ForceClose(500, At(solution, 0).ReactionY);
        ForceClose(500, At(solution, L).ReactionY);
        MomentClose(250, At(solution, 0).ReactionMoment);
        MomentClose(-250, At(solution, L).ReactionMoment);
        Close(250, Left(solution, 1).BendingMoment.NewtonMeters, 1e-9);
    }

    [Fact]
    public void HingeOnASupportSplitsTheBeamIntoTwoSingleSpans()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller), SupportAt(L, SupportType.Roller)],
            [Point(.5, -1000), Point(1.5, -400)]);
        ReferenceBeamSolution solution = SolveWith(beam, 1);
        ForceClose(500, At(solution, 0).ReactionY);
        ForceClose(700, At(solution, 1).ReactionY);
        ForceClose(200, At(solution, L).ReactionY);
        Close(0, Left(solution, 1).BendingMoment.NewtonMeters, 1e-9);
        Close(0, Right(solution, 1).BendingMoment.NewtonMeters, 1e-9);
        Close(250, Left(solution, .5).BendingMoment.NewtonMeters, 1e-9);
    }

    [Theory]
    [InlineData(1e-3)]
    [InlineData(1e-6)]
    [InlineData(6e-9)]
    public void HingeBesideAMiddleSupportStaysExact(double gap)
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller), SupportAt(L, SupportType.Roller)],
            [Point(1.5, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, 1 + gap);
        double end = 1000 * (.5 - gap) / (1 - gap);
        double shear = 1000 - end; // Carried by the hinge into the short overhang.
        ForceClose(end, At(solution, L).ReactionY);
        Close(0, Left(solution, 1 + gap).BendingMoment.NewtonMeters, 1e-9);
        Close(-shear * gap, Right(solution, 1).BendingMoment.NewtonMeters, 1e-7);
    }

    [Fact]
    public void LoadSixNanometersBesideAHingeStaysExact()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)], [Point(1 + 6e-9, -1000)]);
        ReferenceBeamSolution solution = SolveWith(beam, 1);
        ForceClose(1000 * (1 + 6e-9 - 1) / 1, At(solution, L).ReactionY);
        Close(0, Right(solution, 1).BendingMoment.NewtonMeters, 1e-9);
    }

    [Theory]
    [InlineData(.5)]      // Free tip beyond the hinge.
    [InlineData(1.5)]
    public void HingeOnACantileverIsAMechanism(double hinge)
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => SolveWith(Cantilever(Point(1.9, -1)), hinge));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void FreePartLeftOfAHingeIsAMechanism()
    {
        BeamModel beam = Beam([SupportAt(L, SupportType.Fixed)], [Point(.2, -1)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => SolveWith(beam, .5));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void HingeBetweenTwoPinsIsAMechanism()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => SolveWith(Beam(loads: [Point(.5, -1)]), 1));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void MomentExactlyOnAHingeIsRejected()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)], [Couple(1, 10)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => SolveWith(beam, 1));
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
    }

    [Fact]
    public void AxialForcePassesThroughAHinge()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)]);
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam,
            new SolverLoads([], [], [], [new PointAction(1.5, 1000)], [], [1.0]));
        Close(1000, Right(solution, 1).AxialForce.Newtons, 1e-9);
        Close(1000, Left(solution, 1).AxialForce.Newtons, 1e-9);
        ForceClose(-1000, At(solution, 0).ReactionX);
    }

    [Fact]
    public void WithoutHingesTheResultIsUnchanged()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(1, SupportType.Roller)], [Point(1.5, -1000), Uniform(0, 2, -100)]);
        SameNodes(Solve(beam).Nodes, SolveWith(beam).Nodes);
    }
}
