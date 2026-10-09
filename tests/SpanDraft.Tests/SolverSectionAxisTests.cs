using SpanDraft.Core.Beams;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SectionAxisTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class SolverSectionAxisTests
{
    [Theory]
    [InlineData("rectangle", SectionAxisDesignation.Y, SectionAxisDesignation.Z)]
    [InlineData("I", SectionAxisDesignation.Y, SectionAxisDesignation.Z)]
    [InlineData("angle", SectionAxisDesignation.U, SectionAxisDesignation.V)]
    [InlineData("manualYZ", SectionAxisDesignation.Y, SectionAxisDesignation.Z)]
    [InlineData("manualUV", SectionAxisDesignation.U, SectionAxisDesignation.V)]
    public void SelectedInertiaControlsKinematicsAndNotDeterminateForces(string shape,
        SectionAxisDesignation first, SectionAxisDesignation second)
    {
        ISectionDefinition section = shape.StartsWith("manual") ? Manual(first, second) : Profile(shape);
        double i1 = section.GetAxis(first).SecondMomentOfArea.MetersToTheFourth;
        double i2 = section.GetAxis(second).SecondMomentOfArea.MetersToTheFourth;
        Assert.NotEqual(i1, i2);
        foreach (bool udl in new[] { false, true })
        foreach (bool cantilever in new[] { false, true })
        {
            var beam1 = AxisBeam(section, first, udl ? [Uniform(0, L, -500)] :
                [Point(cantilever ? L : L / 2, -1000)], cantilever);
            var beam2 = AxisBeam(section, second, beam1.Loads, cantilever);
            var solution1 = Solve(beam1);
            var solution2 = Solve(beam2);
            foreach (var pair in solution1.Nodes.Zip(solution2.Nodes))
            {
                if (pair.First.ReactionY.HasValue) ForceClose(pair.First.ReactionY.Value.Newtons, pair.Second.ReactionY);
                if (pair.First.ReactionMoment.HasValue) MomentClose(pair.First.ReactionMoment.Value.NewtonMeters, pair.Second.ReactionMoment);
            }
            // Include interior evaluations to cover the UDL bubble, M and V reconstruction.
            foreach (double x in new[] { .23, .71, 1.37, L })
            {
                var a = solution1.EvaluateAt(M(x), EvaluationSide.Left);
                var b = solution2.EvaluateAt(M(x), EvaluationSide.Left);
                DisplacementClose(a.TransverseDisplacement.Meters * i1 / i2, b.TransverseDisplacement);
                RotationClose(a.RotationRadians * i1 / i2, b.RotationRadians);
                Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
                Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-7);
                if (cantilever)
                {
                    double ei = E * i2;
                    double w = udl ? -500 * x * x * (6 * L * L - 4 * L * x + x * x) / (24 * ei) :
                        -1000 * x * x * (3 * L - x) / (6 * ei);
                    double theta = udl ? -500 * x * (3 * L * L - 3 * L * x + x * x) / (6 * ei) :
                        -1000 * x * (2 * L - x) / (2 * ei);
                    DisplacementClose(w, b.TransverseDisplacement);
                    RotationClose(theta, b.RotationRadians);
                }
            }
        }
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y)]
    [InlineData(SectionAxisDesignation.Z)]
    public void AxialAssemblyAndFieldsUseAreaForEitherBendingAxis(SectionAxisDesignation axis)
    {
        var beam = AxisBeam(Manual(SectionAxisDesignation.Y, SectionAxisDesignation.Z), axis);
        var model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        var system = BeamAssembly.Assemble(beam, model, dofs);
        system.Loads[3] = 1000;
        var u = ReducedSystemSolver.Solve(system, dofs);
        BeamNodeResult[] nodes = model.Nodes.Select(n => new BeamNodeResult(n.Index, n.Position,
            Displacement.FromMeters(u[3 * n.Index]), default, 0, null, null, null)).ToArray();
        var solution = new BeamSolution(beam, nodes, model);
        foreach (double x in new[] { 0, .37, 1.13, L })
        {
            var result = solution.EvaluateAt(M(x), EvaluationSide.Left);
            DisplacementClose(1000 * x / (E * beam.Section.Area.SquareMeters), result.AxialDisplacement);
            Close(1000, result.AxialForce.Newtons, 1e-7);
        }
    }
}
