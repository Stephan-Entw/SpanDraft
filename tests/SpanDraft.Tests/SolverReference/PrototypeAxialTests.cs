using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Tests.SolverReference.ReferenceTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>Axial and inclined actions exist only in the frozen prototype; Core cannot express them.</summary>
public class PrototypeAxialTests
{
    private const double X1 = 0.10054, X2 = 0.100540006; // 100.54 mm and 100.540006 mm.

    private static double Ea(BeamModel beam) => E * beam.Section.Area.SquareMeters;
    private static ReferenceBeamSolution Solve(BeamModel beam) => ReferenceBeamSolver.Prototype(beam);
    private static BeamSectionResult Right(ReferenceBeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Right);
    private static BeamSectionResult Left(ReferenceBeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Left);
    private static SolverLoads Axial(params PointAction[] forces) => new([], [], [], forces, []);

    [Fact]
    public void AxialForceAtTheFreeEndStretchesTheBeam()
    {
        BeamModel beam = Cantilever();
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(L, 1000)));
        Close(1000 * L / Ea(beam), At(solution, L).AxialDisplacement.Meters, 1e-15);
        Close(1000, Right(solution, .5).AxialForce.Newtons, 1e-9);
        ForceClose(-1000, At(solution, 0).ReactionX);
    }

    [Fact]
    public void AxialForceExactlyOnARestraintGoesDirectlyIntoTheReaction()
    {
        BeamModel beam = Cantilever();
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(0, 1000)));
        ForceClose(-1000, At(solution, 0).ReactionX);
        Close(0, Right(solution, 0).AxialForce.Newtons, 1e-9);
        Close(0, At(solution, L).AxialDisplacement.Meters, 1e-15);
    }

    [Fact]
    public void AxialForceBetweenTwoRestraintsSplitsByStiffness()
    {
        const double a = .5;
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Pinned)]);
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(a, 1000)));
        Close(1000 * (L - a) / L, Left(solution, a).AxialForce.Newtons, 1e-9);
        Close(-1000 * a / L, Right(solution, a).AxialForce.Newtons, 1e-9);
        Close(1000 * a * (L - a) / (L * Ea(beam)), At(solution, a).AxialDisplacement.Meters, 1e-15);
        ForceClose(-1000 * (L - a) / L, At(solution, 0).ReactionX);
        ForceClose(-1000 * a / L, At(solution, L).ReactionX);
    }

    [Fact]
    public void RollerDoesNotCarryAxialForce()
    {
        const double a = .5;
        BeamModel beam = Beam();
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(a, 1000)));
        Close(1000, Left(solution, a).AxialForce.Newtons, 1e-9);
        Close(0, Right(solution, a).AxialForce.Newtons, 1e-9);
        Assert.Null(At(solution, L).ReactionX);
        ForceClose(-1000, At(solution, 0).ReactionX);
    }

    [Fact]
    public void AxialForceOnALeftOverhangIsCarriedByTheRestraint()
    {
        BeamModel beam = Beam([SupportAt(1, SupportType.Pinned), SupportAt(L, SupportType.Roller)]);
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(.5, 1000)));
        Close(0, Left(solution, .5).AxialForce.Newtons, 1e-9);
        Close(-1000, Right(solution, .5).AxialForce.Newtons, 1e-9);
        Close(1000 * .5 / Ea(beam), At(solution, .5).AxialDisplacement.Meters, 1e-15);
        ForceClose(-1000, At(solution, 1).ReactionX);
    }

    [Fact]
    public void UniformAxialLoadHasLinearForceAndQuadraticDisplacement()
    {
        BeamModel beam = Cantilever();
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, new SolverLoads([], [], [], [], [new(0, L, 500)]));
        Close(500 * (L - .5), Right(solution, .5).AxialForce.Newtons, 1e-9);
        Close(500 * L * L / (2 * Ea(beam)), At(solution, L).AxialDisplacement.Meters, 1e-15);
        ForceClose(-500 * L, At(solution, 0).ReactionX);
    }

    [Fact]
    public void AxialForcesSixNanometersApartStaySeparate()
    {
        BeamModel beam = Cantilever();
        ReferenceBeamSolution solution = ReferenceBeamSolver.Prototype(beam, Axial(new PointAction(X1, 5000), new PointAction(X2, 3000)));
        Close(8000, Left(solution, X1).AxialForce.Newtons, 1e-9);
        Close(3000, Right(solution, X1).AxialForce.Newtons, 1e-9);
        Close(0, Right(solution, X2).AxialForce.Newtons, 1e-9);
        ForceClose(-8000, At(solution, 0).ReactionX);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(120)]
    public void InclinedForceIsTheSumOfItsComponents(double degrees)
    {
        const double force = 4000, x = 1.25;
        double angle = degrees * Math.PI / 180;
        double axial = force * Math.Cos(angle), transverse = -force * Math.Sin(angle);
        Support[] supports = [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)];
        ReferenceBeamSolution inclined = ReferenceBeamSolver.Prototype(Beam(supports),
            new SolverLoads([new(x, transverse)], [], [], [new(x, axial)], []));
        ReferenceBeamSolution bendingOnly = Solve(Beam(supports, [Point(x, transverse)]));
        ReferenceBeamSolution axialOnly = ReferenceBeamSolver.Prototype(Beam(supports), Axial(new PointAction(x, axial)));
        foreach (double position in new[] { .4, 1.25, 1.9 })
        {
            BeamSectionResult sum = Left(inclined, position), bending = Left(bendingOnly, position), stretch = Left(axialOnly, position);
            Close(bending.BendingMoment.NewtonMeters, sum.BendingMoment.NewtonMeters, 1e-9);
            Close(bending.ShearForce.Newtons, sum.ShearForce.Newtons, 1e-9);
            Close(bending.TransverseDisplacement.Meters, sum.TransverseDisplacement.Meters, 1e-15);
            Close(stretch.AxialForce.Newtons, sum.AxialForce.Newtons, 1e-9);
            Close(stretch.AxialDisplacement.Meters, sum.AxialDisplacement.Meters, 1e-15);
        }
        ForceClose(-axial, At(inclined, 0).ReactionX);
    }
}
