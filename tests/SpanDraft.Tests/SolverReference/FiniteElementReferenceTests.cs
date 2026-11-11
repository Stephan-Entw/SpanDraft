using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>Internal structure and failure paths of the frozen finite-element reference.</summary>
public class FiniteElementReferenceTests
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
    public void ReducedSystemDetectsSingularityIndependentlyOfSupportPrecheck()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Roller), SupportAt(L, SupportType.Roller)]);
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => ReducedSystemSolver.Solve(system, dofs));
        Assert.Equal(SolverErrorCode.IllConditionedSystem, exception.Code);
    }

    [Fact]
    public void IndefiniteReducedMatrixReturnsTypedFactorizationError()
    {
        BeamModel beam = Cantilever();
        var dofs = new DofMap(beam, SolverModel.Create(beam));
        Matrix<double> matrix = Matrix<double>.Build.DenseIdentity(dofs.Count);
        matrix[3, 4] = matrix[4, 3] = 2;
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() =>
            ReducedSystemSolver.Solve(new BeamSystem(matrix, Vector<double>.Build.Dense(dofs.Count)), dofs));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void WrongSolutionFailsBackwardErrorCheck()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => ReducedSystemSolver.CheckBackwardError(
            Matrix<double>.Build.DenseIdentity(2), Vector<double>.Build.Dense([0, 0]), Vector<double>.Build.Dense([1, 0])));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
    }

    [Fact]
    public void OverflowingEquivalentMomentReturnsNumericalFailure()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() =>
            EulerBernoulliElement.UniformLoad(double.MaxValue / 100, 100));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void AxialResultUsesReducedSystemAndLinearDisplacement(double force)
    {
        BeamModel beam = Cantilever();
        ReferenceBeamSolution solution = AxialSolution(beam, force);
        foreach (double x in new[] { 0, 0.25, 1, 1.75, L })
        {
            BeamSectionResult result = solution.EvaluateAt(M(x), EvaluationSide.Right);
            DisplacementClose(force * x / (E * beam.Section.Area.SquareMeters), result.AxialDisplacement);
            Close(force, result.AxialForce.Newtons, 1e-7);
        }
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y)]
    [InlineData(SectionAxisDesignation.Z)]
    public void AxialAssemblyAndFieldsUseAreaForEitherBendingAxis(SectionAxisDesignation axis)
    {
        BeamModel beam = SectionAxisTestSupport.AxisBeam(
            SectionAxisTestSupport.Manual(SectionAxisDesignation.Y, SectionAxisDesignation.Z), axis);
        ReferenceBeamSolution solution = AxialSolution(beam, 1000);
        foreach (double x in new[] { 0, .37, 1.13, L })
        {
            BeamSectionResult result = solution.EvaluateAt(M(x), EvaluationSide.Left);
            DisplacementClose(1000 * x / (E * beam.Section.Area.SquareMeters), result.AxialDisplacement);
            Close(1000, result.AxialForce.Newtons, 1e-7);
        }
    }

    // Core has no axial load type; the axial equation is exercised through the assembled system.
    private static ReferenceBeamSolution AxialSolution(BeamModel beam, double force)
    {
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        system.Loads[3] = force;
        Vector<double> u = ReducedSystemSolver.Solve(system, dofs);
        return new ReferenceBeamSolution(beam, model.Nodes.Select(n => new BeamNodeResult(n.Index, n.Position,
            Displacement.FromMeters(u[3 * n.Index]), default, 0, null, null, null)), model);
    }
}
