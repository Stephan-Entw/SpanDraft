using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverStructureTests
{
    [Fact]
    public void MeshSortsAndExactlyDeduplicatesEveryRelevantPosition()
    {
        BeamModel beam = Beam([SupportAt(1.5, SupportType.Roller), SupportAt(0.5, SupportType.Pinned)],
            [Point(1, -1), Couple(1, 2), Uniform(0.25, 1.75, -3), Point(0.5, 4), Couple(2, 5)]);
        SolverModel model = SolverModel.Create(beam);

        Assert.Equal(new double[] { 0, 0.25, 0.5, 1, 1.5, 1.75, 2 }, model.Nodes.Select(n => n.Position.Meters));
        Assert.Equal(Enumerable.Range(0, model.Nodes.Count), model.Nodes.Select(n => n.Index));
        Assert.Equal(model.Nodes.Count - 1, model.Elements.Count);
        for (int i = 0; i < model.Elements.Count; i++)
        {
            Assert.Same(model.Nodes[i], model.Elements[i].Left);
            Assert.Same(model.Nodes[i + 1], model.Elements[i].Right);
            Assert.True(model.Elements[i].LengthMeters > 0);
        }
    }

    [Fact]
    public void DistinctNearbyPositionsAreNotMerged()
    {
        SolverModel model = SolverModel.Create(Cantilever(Point(1, 0), Point(Math.BitIncrement(1), 0)));
        Assert.Equal(4, model.Nodes.Count);
        Assert.All(model.Elements, element => Assert.True(element.LengthMeters > 0));
    }

    [Fact]
    public void SingleFullLengthUniformLoadDoesNotIntroduceMidpoint()
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, -1000)]));
        Assert.Equal(new double[] { 0, L }, solution.Nodes.Select(n => n.Position.Meters));
    }

    [Fact]
    public void ElementMatrixContainsAllClassicalCoefficientsAndIsSymmetric()
    {
        Matrix<double> matrix = EulerBernoulliElement.Stiffness(2, 3, 5, 2);
        double[,] expected =
        {
            { 3, 0, 0, -3, 0, 0 },
            { 0, 15, 15, 0, -15, 15 },
            { 0, 15, 20, 0, -15, 10 },
            { -3, 0, 0, 3, 0, 0 },
            { 0, -15, -15, 0, 15, -15 },
            { 0, 15, 10, 0, -15, 20 }
        };
        Assert.Equal(6, matrix.RowCount);
        Assert.Equal(6, matrix.ColumnCount);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
            {
                Close(expected[i, j], matrix[i, j], 1e-12);
                Close(matrix[i, j], matrix[j, i], 1e-12);
            }
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 0)]
    [InlineData(0, 1, 0, 0, 1, 0)]
    [InlineData(0, 0, 1, 0, 2, 1)]
    public void ElementHasExactlyExpectedRigidBodyMotions(double u1, double w1, double t1, double u2, double w2, double t2)
    {
        Vector<double> forces = EulerBernoulliElement.Stiffness(2, 3, 5, 2) *
            Vector<double>.Build.Dense([u1, w1, t1, u2, w2, t2]);
        foreach (double force in forces) Close(0, force, 1e-12);
    }

    [Fact]
    public void ConstantCurvatureHasCorrectStrainEnergy()
    {
        var q = Vector<double>.Build.Dense([0, 0, 0, 0, 4, 4]); // w=x², θ=2x, L=2.
        Matrix<double> matrix = EulerBernoulliElement.Stiffness(2, 3, 5, 2);
        Close(40, q.DotProduct(matrix * q) / 2, 1e-12); // (1/2)∫ EI*(w'')² dx.
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(12)]
    [InlineData(0)]
    public void ConsistentUniformLoadHasCorrectNodalSignsAndResultant(double intensity)
    {
        Vector<double> vector = EulerBernoulliElement.UniformLoad(intensity, 2);
        double[] expected = [0, intensity, intensity / 3, 0, intensity, -intensity / 3];
        for (int i = 0; i < 6; i++) Close(expected[i], vector[i], 1e-12);
        Close(intensity * 2, vector[1] + vector[4], 1e-12);
        Close(intensity * 2, vector[2] + 2 * vector[4] + vector[5], 1e-12);
    }

    [Fact]
    public void AssemblyAddsSharedStiffnessAndLoadsWithExpectedDimensions()
    {
        BeamModel beam = Cantilever(Point(1, -100), Point(1, -200), Couple(1, 20), Couple(1, 30), Uniform(0, 2, -600));
        SolverModel mesh = SolverModel.Create(beam);
        var dofs = new DofMap(beam, mesh);
        BeamSystem system = BeamAssembly.Assemble(beam, mesh, dofs);
        Assert.Equal(9, system.Stiffness.RowCount);
        Assert.Equal(9, system.Stiffness.ColumnCount);
        Assert.Equal(9, system.Loads.Count);
        Matrix<double> element = EulerBernoulliElement.Stiffness(E, beam.Section.Area.SquareMeters, I, 1);
        Close(2 * element[0, 0], system.Stiffness[3, 3], 1e-7);
        Close(2 * element[1, 1], system.Stiffness[4, 4], 1e-7);
        Close(2 * element[2, 2], system.Stiffness[5, 5], 1e-7);
        Close(-900, system.Loads[4], 1e-7);
        Close(50, system.Loads[5], 1e-7); // Adjacent UDL nodal moments cancel.
        for (int i = 0; i < dofs.Count; i++)
            for (int j = 0; j < dofs.Count; j++)
                Close(system.Stiffness[i, j], system.Stiffness[j, i], 1e-7);
    }

    [Theory]
    [InlineData(SupportType.Fixed, true, true, true)]
    [InlineData(SupportType.Pinned, true, true, false)]
    [InlineData(SupportType.Roller, false, true, false)]
    public void SupportTypesRestrainCorrectDofs(SupportType type, bool u, bool w, bool theta)
    {
        BeamModel beam = Beam([SupportAt(0, type)]);
        var dofs = new DofMap(beam, SolverModel.Create(beam));
        Assert.Equal(u, dofs.IsConstrained(0));
        Assert.Equal(w, dofs.IsConstrained(1));
        Assert.Equal(theta, dofs.IsConstrained(2));
        Assert.All(new[] { 3, 4, 5 }, index => Assert.False(dofs.IsConstrained(index)));
        Assert.Equal(Enumerable.Range(0, 6).Where(i => !dofs.IsConstrained(i)), dofs.FreeDofs);
    }

    [Fact]
    public void CoincidentDifferentSupportsMergeRestraintsWithoutDuplicatingNodes()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(0, SupportType.Pinned), SupportAt(0, SupportType.Roller)]);
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        Assert.Equal(2, model.Nodes.Count);
        Assert.All(new[] { 0, 1, 2 }, index => Assert.True(dofs.IsConstrained(index)));
        SameResults(Solve(Cantilever()), Solve(beam));
    }

    [Fact]
    public void AxialDofRespondsWithEAStiffnessInReducedSystem()
    {
        BeamModel beam = Cantilever();
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        // The Core has no axial load type yet; exercise the internal axial equation directly.
        system.Loads[3] = 1000;
        Vector<double> displacements = ReducedSystemSolver.Solve(system, dofs);
        Close(1000 * L / (E * 0.04 * 0.08), displacements[3], 1e-12);
        Vector<double> residual = system.Stiffness * displacements - system.Loads;
        Close(-1000, residual[0], 1e-7);
        foreach (int free in dofs.FreeDofs) Close(0, residual[free], 1e-7);
    }

    [Fact]
    public void ReducedSolvePreservesFullSystemAndFreeDofsHaveNegligibleResiduals()
    {
        BeamModel beam = Cantilever(Point(1, -300), Couple(2, 70), Uniform(0.5, 2, -150));
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        Matrix<double> originalK = system.Stiffness.Clone();
        Vector<double> originalF = system.Loads.Clone();
        Vector<double> q = ReducedSystemSolver.Solve(system, dofs);
        Assert.Equal(originalK, system.Stiffness);
        Assert.Equal(originalF, system.Loads);
        Vector<double> residual = system.Stiffness * q - system.Loads;
        foreach (int free in dofs.FreeDofs) Close(0, residual[free], 1e-7);
        for (int i = 0; i < dofs.Count; i++)
            if (dofs.IsConstrained(i)) Close(0, q[i], 1e-12);
    }

    [Fact]
    public void SolverPreservesInputAndReturnsReadOnlyPositionMappedResults()
    {
        BeamModel beam = Cantilever(Point(1, -100), Uniform(0, 2, -500));
        var supports = beam.Supports.ToArray();
        var loads = beam.Loads.ToArray();
        var material = beam.Material;
        var section = beam.Section;
        BeamSolution result = Solve(beam);
        Assert.Same(beam, result.Beam);
        Assert.Equal(M(2), beam.Length);
        Assert.Same(material, beam.Material);
        Assert.Same(section, beam.Section);
        Assert.Equal(supports, beam.Supports);
        Assert.Equal(loads, beam.Loads);
        Assert.All(supports.Zip(beam.Supports), pair => Assert.Same(pair.First, pair.Second));
        Assert.All(loads.Zip(beam.Loads), pair => Assert.Same(pair.First, pair.Second));
        Assert.Equal(new double[] { 0, 1, 2 }, result.Nodes.Select(n => n.Position.Meters));
        Assert.Equal(new[] { 0, 1, 2 }, result.Nodes.Select(n => n.NodeIndex));
        Assert.Throws<NotSupportedException>(() => ((IList<BeamNodeResult>)result.Nodes).Clear());
        Assert.All(result.Nodes.Skip(1), node =>
        {
            Assert.Null(node.ReactionX);
            Assert.Null(node.ReactionY);
            Assert.Null(node.ReactionMoment);
        });
    }

    [Fact]
    public void SolverInstanceCanBeReusedWithoutRetainingPreviousModelState()
    {
        var solver = new EulerBernoulliBeamSolver();
        BeamModel first = Cantilever(Point(2, -1000));
        BeamModel second = Beam(loads: [Point(1, -1000)]);
        BeamSolution before = solver.Solve(first);
        SameResults(Solve(second), solver.Solve(second));
        SameResults(before, solver.Solve(first));
    }
}
