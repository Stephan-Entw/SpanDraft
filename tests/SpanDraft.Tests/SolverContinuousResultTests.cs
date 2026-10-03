using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverContinuousResultTests
{
    private static BeamSectionResult Section(BeamSolution solution, double x,
        EvaluationSide side = EvaluationSide.Right) => solution.EvaluateAt(M(x), side);

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void MidspanForceHasCorrectOneSidedShearAndContinuousMoment(double force)
    {
        BeamSolution solution = Solve(Beam(loads: [Point(1, force)]));
        BeamSectionResult left = Section(solution, 1, EvaluationSide.Left), right = Section(solution, 1);
        Close(-force / 2, left.ShearForce.Newtons, 1e-7);
        Close(force / 2, right.ShearForce.Newtons, 1e-7);
        Close(force, right.ShearForce.Newtons - left.ShearForce.Newtons, 1e-7);
        Close(-force * L / 4, left.BendingMoment.NewtonMeters, 1e-7);
        Close(left.BendingMoment.NewtonMeters, right.BendingMoment.NewtonMeters, 1e-7);
        Close(0, Section(solution, 0).BendingMoment.NewtonMeters, 1e-7);
        Close(0, Section(solution, L).BendingMoment.NewtonMeters, 1e-7);
        foreach (double x in new[] { 0.25, 0.5, 0.75 })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(force * x * (3 * L * L - 4 * x * x) / (48 * EI), result.TransverseDisplacement);
            RotationClose(force * (3 * L * L - 12 * x * x) / (48 * EI), result.RotationRadians);
            Close(-force * x / 2, result.BendingMoment.NewtonMeters, 1e-7);
            Close(-force / 2, result.ShearForce.Newtons, 1e-7);
        }
        ContinuousKinematics(left, right);
        Balanced(solution);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void CantileverTipForceHasAnalyticalFieldsThroughoutElement(double force)
    {
        BeamSolution solution = Solve(Cantilever(Point(L, force)));
        foreach (double x in new[] { 0, 0.25, 0.75, 1, 1.5, L })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(force * x * x * (3 * L - x) / (6 * EI), result.TransverseDisplacement);
            RotationClose(force * x * (2 * L - x) / (2 * EI), result.RotationRadians);
            Close(force * (L - x), result.BendingMoment.NewtonMeters, 1e-7);
            Close(-force, result.ShearForce.Newtons, 1e-7);
            DisplacementClose(0, result.AxialDisplacement);
            Close(0, result.AxialForce.Newtons, 1e-7);
        }
        Balanced(solution);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void CantileverFullUdlHasQuarticDeflectionAndEquilibriumFields(double q)
    {
        BeamSolution solution = Solve(Cantilever(Uniform(0, L, q)));
        Assert.Equal(2, solution.Nodes.Count);
        foreach (double x in new[] { 0, 0.5, 0.75, 1, 1.5, L })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(q * x * x * (6 * L * L - 4 * L * x + x * x) / (24 * EI), result.TransverseDisplacement);
            RotationClose(q * x * (3 * L * L - 3 * L * x + x * x) / (6 * EI), result.RotationRadians);
            Close(q * (L - x) * (L - x) / 2, result.BendingMoment.NewtonMeters, 1e-7);
            Close(q * (x - L), result.ShearForce.Newtons, 1e-7);
        }
        Balanced(solution);
        Derivatives(solution, 0.75, q);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void SingleSimplySupportedUdlNeedsNoMidpointForExactDeflection(double q)
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, q)]));
        Assert.Equal(new[] { 0.0, L }, solution.Nodes.Select(n => n.Position.Meters));
        foreach (double x in new[] { 0, L / 4, L / 2, 3 * L / 4, L })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(q * x * (L * L * L - 2 * L * x * x + x * x * x) / (24 * EI), result.TransverseDisplacement);
            RotationClose(q * (L * L * L - 6 * L * x * x + 4 * x * x * x) / (24 * EI), result.RotationRadians);
            Close(-q * x * (L - x) / 2, result.BendingMoment.NewtonMeters, 1e-7);
            Close(q * (x - L / 2), result.ShearForce.Newtons, 1e-7);
        }
        DisplacementClose(5 * q * Math.Pow(L, 4) / (384 * EI), Section(solution, L / 2).TransverseDisplacement);
        Balanced(solution);
        Derivatives(solution, 0.5, q);
    }

    [Theory]
    [InlineData(-400)]
    [InlineData(400)]
    public void InnerPointMomentJumpsMomentWithOppositeSignAndLeavesShearContinuous(double couple)
    {
        const double a = 0.75;
        BeamSolution solution = Solve(Beam(loads: [Couple(a, couple)]));
        BeamSectionResult left = Section(solution, a, EvaluationSide.Left), right = Section(solution, a);
        ForceClose(couple / L, At(solution, 0).ReactionY);
        ForceClose(-couple / L, At(solution, L).ReactionY);
        Close(couple / L, left.ShearForce.Newtons, 1e-7);
        Close(left.ShearForce.Newtons, right.ShearForce.Newtons, 1e-7);
        Close(couple * a / L, left.BendingMoment.NewtonMeters, 1e-7);
        Close(couple * a / L - couple, right.BendingMoment.NewtonMeters, 1e-7);
        Close(-couple, right.BendingMoment.NewtonMeters - left.BendingMoment.NewtonMeters, 1e-7);
        Close(0, Section(solution, 0).BendingMoment.NewtonMeters, 1e-7);
        Close(0, Section(solution, L).BendingMoment.NewtonMeters, 1e-7);
        ContinuousKinematics(left, right);
        Balanced(solution);
    }

    [Fact]
    public void PartiallyOverlappingUdlsSumIntensityWithoutBoundaryJumps()
    {
        BeamSolution solution = Solve(Cantilever(Uniform(0.25, 1.25, -400), Uniform(0.75, 1.75, -600)));
        Assert.Equal(new[] { 0.0, 0.25, 0.75, 1.25, 1.75, L }, solution.Nodes.Select(n => n.Position.Meters));
        foreach ((double x, double q) in new[] { (0.125, 0.0), (0.5, -400.0), (1.0, -1000.0), (1.5, -600.0), (1.875, 0.0) })
            Derivatives(solution, x, q);
        foreach (double x in new[] { 0.25, 0.75, 1.25, 1.75 })
        {
            BeamSectionResult left = Section(solution, x, EvaluationSide.Left), right = Section(solution, x);
            ContinuousKinematics(left, right);
            Close(left.ShearForce.Newtons, right.ShearForce.Newtons, 1e-7);
            Close(left.BendingMoment.NewtonMeters, right.BendingMoment.NewtonMeters, 1e-7);
        }
        BeamSolution summed = Solve(Cantilever(Uniform(0.25, 0.75, -400), Uniform(0.75, 1.25, -1000), Uniform(1.25, 1.75, -600)));
        foreach (double x in new[] { 0.1, 0.5, 1, 1.5, 1.9 }) SameSection(Section(summed, x), Section(solution, x));
        Balanced(solution);
    }

    [Fact]
    public void SupportReactionProducesShearJumpWhileMomentRemainsContinuous()
    {
        BeamSolution solution = Solve(Beam(
            [SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller), SupportAt(2, SupportType.Roller)],
            [Uniform(0, L, -1000)]));
        BeamSectionResult left = Section(solution, 1, EvaluationSide.Left), right = Section(solution, 1);
        Close(-625, left.ShearForce.Newtons, 1e-7);
        Close(625, right.ShearForce.Newtons, 1e-7);
        Close(At(solution, 1).ReactionY!.Value.Newtons, right.ShearForce.Newtons - left.ShearForce.Newtons, 1e-7);
        Close(-125, left.BendingMoment.NewtonMeters, 1e-7);
        Close(left.BendingMoment.NewtonMeters, right.BendingMoment.NewtonMeters, 1e-7);
        ContinuousKinematics(left, right);
        Balanced(solution);
    }

    [Fact]
    public void PointLoadsAndReactionsAtSameInnerSupportBothContributeToJumps()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Fixed), SupportAt(1, SupportType.Fixed)],
            [Uniform(0, 2, -300), Point(1, -700), Couple(1, 200)]));
        BeamSectionResult left = Section(solution, 1, EvaluationSide.Left), right = Section(solution, 1);
        BeamNodeResult node = At(solution, 1);
        Close(-700 + node.ReactionY!.Value.Newtons, right.ShearForce.Newtons - left.ShearForce.Newtons, 1e-7);
        Close(-200 - node.ReactionMoment!.Value.NewtonMeters, right.BendingMoment.NewtonMeters - left.BendingMoment.NewtonMeters, 1e-7);
        ContinuousKinematics(left, right);
        Balanced(solution);
    }

    [Fact]
    public void ZeroLoadNodeDoesNotChangeContinuousFields()
    {
        BeamSolution original = Solve(Cantilever(Uniform(0, L, -1000), Point(L, -500)));
        BeamSolution subdivided = Solve(Cantilever(Uniform(0, L, -1000), Point(L, -500), Point(0.75, 0)));
        foreach (double x in new[] { 0, 0.25, 0.75, 1, 1.75, L })
            SameSection(Section(original, x), Section(subdivided, x));
        SameSection(Section(subdivided, 0.75, EvaluationSide.Left), Section(subdivided, 0.75));
    }

    [Fact]
    public void OppositeUdlsCancelToUnloadedElementFields()
    {
        BeamSolution solution = Solve(Cantilever(Uniform(0, L, -1000), Uniform(0, L, 1000)));
        foreach (double x in new[] { 0, 0.25, 1, 1.75, L })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(0, result.TransverseDisplacement);
            RotationClose(0, result.RotationRadians);
            Close(0, result.ShearForce.Newtons, 1e-7);
            Close(0, result.BendingMoment.NewtonMeters, 1e-7);
        }
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(500)]
    public void EndMomentHasConstantMomentZeroShearAndQuadraticDeflection(double moment)
    {
        BeamSolution solution = Solve(Cantilever(Couple(L, moment)));
        foreach (double x in new[] { 0, 0.25, 1, 1.75, L })
        {
            BeamSectionResult result = Section(solution, x);
            DisplacementClose(moment * x * x / (2 * EI), result.TransverseDisplacement);
            RotationClose(moment * x / EI, result.RotationRadians);
            Close(0, result.ShearForce.Newtons, 1e-7);
            Close(moment, result.BendingMoment.NewtonMeters, 1e-7);
        }
        Balanced(solution);
    }

    [Fact]
    public void BothOverhangsUseTheirInteriorFieldsAndSupportJumps()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0.5, SupportType.Pinned), SupportAt(1.5, SupportType.Roller)],
            [Point(0, -100), Point(L, -300)]));
        foreach (double x in new[] { 0, 0.25, 0.5, 1, 1.5 })
        {
            BeamSectionResult result = Section(solution, x, EvaluationSide.Left);
            Close(-100, result.ShearForce.Newtons, 1e-7);
            Close(-100 * x, result.BendingMoment.NewtonMeters, 1e-7);
        }
        foreach (double x in new[] { 1.5, 1.75, L })
        {
            BeamSectionResult result = Section(solution, x);
            Close(300, result.ShearForce.Newtons, 1e-7);
            Close(-150 + 300 * (x - 1.5), result.BendingMoment.NewtonMeters, 1e-7);
        }
        Close(-150, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-7);
        Assert.Equal(M(1.5), solution.Extrema.MinimumBendingMoment.Position);
        Assert.Equal(EvaluationSide.Left, solution.Extrema.MinimumBendingMoment.Side);
        Balanced(solution);
    }

    [Fact]
    public void ReusingSolverDoesNotAlterExistingFieldsOrExtrema()
    {
        var solver = new EulerBernoulliBeamSolver();
        BeamSolution first = solver.Solve(Cantilever(Uniform(0, L, -1000)));
        BeamSectionResult before = Section(first, 0.75);
        BeamExtrema extrema = first.Extrema;
        solver.Solve(Beam(loads: [Point(1, 500)]));
        SameSection(before, Section(first, 0.75));
        Assert.Same(extrema, first.Extrema);
    }

    [Fact]
    public void FixedFixedUdlHasNonzeroInteriorDeflectionDespiteZeroNodalDofs()
    {
        const double q = -1000;
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Fixed)], [Uniform(0, L, q)]));
        Assert.All(solution.Nodes, n => DisplacementClose(0, n.TransverseDisplacement));
        DisplacementClose(q * Math.Pow(L, 4) / (384 * EI), Section(solution, 1).TransverseDisplacement);
        Close(q * L * L / 12, Section(solution, 0).BendingMoment.NewtonMeters, 1e-7);
        Close(q * L * L / 12, Section(solution, L).BendingMoment.NewtonMeters, 1e-7);
        Close(-q * L * L / 24, Section(solution, 1).BendingMoment.NewtonMeters, 1e-7);
        Balanced(solution);
    }

    [Fact]
    public void ApiRetainsExactNodalKinematicsAndUsesPhysicalEndpointLimits()
    {
        BeamSolution solution = Solve(Cantilever(Point(1, -1000), Couple(L, 200), Uniform(0, L, -300)));
        foreach (BeamNodeResult node in solution.Nodes)
            foreach (EvaluationSide side in Enum.GetValues<EvaluationSide>())
            {
                BeamSectionResult result = solution.EvaluateAt(node.Position, side);
                Assert.Equal(node.Position, result.Position);
                Assert.Equal(node.AxialDisplacement, result.AxialDisplacement);
                Assert.Equal(node.TransverseDisplacement, result.TransverseDisplacement);
                Assert.Equal(node.RotationRadians, result.RotationRadians);
            }
        foreach (double x in new[] { 0, 0.25, 0.75, 1.5, L })
            SameSection(Section(solution, x, EvaluationSide.Left), Section(solution, x));
        Assert.Throws<ArgumentOutOfRangeException>(() => Section(solution, Math.BitIncrement(L)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Section(solution, 1, (EvaluationSide)99));
        // Exact lookup: representably different positions adjacent to a jump are not snapped.
        Close(Section(solution, 1, EvaluationSide.Left).ShearForce.Newtons,
            Section(solution, Math.BitDecrement(1)).ShearForce.Newtons, 1e-7);
        Close(Section(solution, 1).ShearForce.Newtons,
            Section(solution, Math.BitIncrement(1), EvaluationSide.Left).ShearForce.Newtons, 1e-7);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void AxialResultUsesExistingReducedSystemAndLinearDisplacement(double force)
    {
        BeamModel beam = Cantilever();
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        system.Loads[3] = force;
        Vector<double> u = ReducedSystemSolver.Solve(system, dofs);
        BeamNodeResult[] nodes = model.Nodes.Select(n => new BeamNodeResult(n.Index, n.Position,
            Displacement.FromMeters(u[3 * n.Index]), default, 0, null, null, null)).ToArray();
        var solution = new BeamSolution(beam, nodes, model);
        foreach (double x in new[] { 0, 0.25, 1, 1.75, L })
        {
            DisplacementClose(force * x / (E * beam.Section.Area.SquareMeters), Section(solution, x).AxialDisplacement);
            Close(force, Section(solution, x).AxialForce.Newtons, 1e-7);
        }
    }

    internal static void SameSection(BeamSectionResult expected, BeamSectionResult actual)
    {
        Assert.Equal(expected.Position, actual.Position);
        ContinuousKinematics(expected, actual);
        Close(expected.AxialForce.Newtons, actual.AxialForce.Newtons, 1e-7);
        Close(expected.ShearForce.Newtons, actual.ShearForce.Newtons, 1e-7);
        Close(expected.BendingMoment.NewtonMeters, actual.BendingMoment.NewtonMeters, 1e-7);
    }

    private static void ContinuousKinematics(BeamSectionResult left, BeamSectionResult right)
    {
        DisplacementClose(left.AxialDisplacement.Meters, right.AxialDisplacement);
        DisplacementClose(left.TransverseDisplacement.Meters, right.TransverseDisplacement);
        RotationClose(left.RotationRadians, right.RotationRadians);
    }

    private static void Derivatives(BeamSolution solution, double x, double q)
    {
        const double h = 0.01;
        BeamSectionResult left = Section(solution, x - h), right = Section(solution, x + h), middle = Section(solution, x);
        Close(middle.ShearForce.Newtons, (right.BendingMoment.NewtonMeters - left.BendingMoment.NewtonMeters) / (2 * h), 1e-7);
        Close(q, (right.ShearForce.Newtons - left.ShearForce.Newtons) / (2 * h), 1e-7);
        const double slopeStep = 1e-5;
        RotationClose(middle.RotationRadians, (Section(solution, x + slopeStep).TransverseDisplacement.Meters -
            Section(solution, x - slopeStep).TransverseDisplacement.Meters) / (2 * slopeStep));
    }

    private static void Balanced(BeamSolution solution)
    {
        double vertical = 0, moment = 0;
        foreach (BeamNodeResult node in solution.Nodes)
        {
            vertical += node.ReactionY?.Newtons ?? 0;
            moment += (node.ReactionY?.Newtons ?? 0) * node.Position.Meters + (node.ReactionMoment?.NewtonMeters ?? 0);
        }
        foreach (BeamLoad load in solution.Beam.Loads)
            switch (load)
            {
                case PointForce force:
                    vertical += force.Force.Newtons;
                    moment += force.Force.Newtons * force.Position.Meters;
                    break;
                case PointMoment couple:
                    moment += couple.Moment.NewtonMeters;
                    break;
                case UniformDistributedLoad udl:
                    double resultant = udl.Intensity.NewtonsPerMeter * (udl.EndPosition.Meters - udl.StartPosition.Meters);
                    vertical += resultant;
                    moment += resultant * (udl.StartPosition.Meters + udl.EndPosition.Meters) / 2;
                    break;
            }
        Close(0, vertical, 1e-7);
        Close(0, moment, 1e-7);
    }
}
