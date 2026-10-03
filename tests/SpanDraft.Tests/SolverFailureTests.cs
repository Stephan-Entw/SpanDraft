using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverFailureTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(0)]
    [InlineData(0.01)]
    public void DisplacementAcceptsSignedFiniteMetres(double meters)
    {
        Close(meters, Displacement.FromMeters(meters).Meters, 1e-12);
    }

    [Fact]
    public void DefaultDisplacementIsZero()
    {
        Assert.Equal(Displacement.FromMeters(0), default(Displacement));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DisplacementRejectsNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Displacement.FromMeters(value));
    }

    [Fact]
    public void NullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Solve(null!));
    }

    [Fact]
    public void InvalidModelReturnsAllOriginalValidationErrorsBeforeSolving()
    {
        BeamModel beam = Beam([SupportAt(3, SupportType.Fixed), SupportAt(3, SupportType.Fixed)],
            [Point(3, -1), Couple(3, 1), Uniform(0, 3, -1)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
        Assert.Equal(BeamModelValidator.Validate(beam), exception.ValidationErrors);
        Assert.Equal(6, exception.ValidationErrors.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationError>)exception.ValidationErrors).Clear());
    }

    [Fact]
    public void MissingSupportsAreModelErrorsRatherThanNumericalFailures()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(Beam(supports: [])));
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
        Assert.Equal(ValidationErrorCode.MissingSupports, Assert.Single(exception.ValidationErrors).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RollerOnlyBeamIsUnstableRegardlessOfLoad(bool loaded)
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Roller), SupportAt(L, SupportType.Roller)],
            loaded ? [Point(1, -1000)] : []);
        Assert.Empty(BeamModelValidator.Validate(beam));
        AssertUnstable(beam);
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void SingleNonFixedSupportLeavesRigidBodyMotion(SupportType type)
    {
        AssertUnstable(Beam([SupportAt(1, type)]));
    }

    [Fact]
    public void CoincidentPinnedAndRollerDoNotRestrainRigidRotation()
    {
        AssertUnstable(Beam([SupportAt(0, SupportType.Pinned), SupportAt(0, SupportType.Roller)]));
    }

    [Fact]
    public void PinnedRollerUnloadedBeamIsStableAndHasZeroResults()
    {
        BeamSolution solution = Solve(Beam());
        foreach (BeamNodeResult node in solution.Nodes)
        {
            DisplacementClose(0, node.AxialDisplacement);
            DisplacementClose(0, node.TransverseDisplacement);
            RotationClose(0, node.RotationRadians);
            ForceClose(0, node.ReactionY);
        }
        ForceClose(0, At(solution, 0).ReactionX);
    }

    [Fact]
    public void PinnedAtRightAndRollerAtLeftAreAlsoStable()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Roller), SupportAt(L, SupportType.Pinned)],
            [Point(1, -1000)]));
        ForceClose(500, At(solution, 0).ReactionY);
        ForceClose(500, At(solution, L).ReactionY);
        ForceClose(0, At(solution, L).ReactionX);
        Assert.Null(At(solution, 0).ReactionX);
    }

    [Fact]
    public void VeryShortAdjacentElementIsRejectedWithoutMergingItsNodes()
    {
        BeamModel beam = Cantilever(Point(1, -1000), Point(1 + 1e-6, 0));
        Assert.Equal(4, SolverModel.Create(beam).Nodes.Count);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.IllConditionedSystem, exception.Code);
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

    [Theory]
    [InlineData(1e300, 1e100)]
    [InlineData(1e-200, 1e-200)]
    public void UnrepresentableRigidityReturnsNumericalFailure(double modulus, double area)
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed)], material:
            new Material("Extreme", Pressure.FromPascals(modulus), Pressure.FromPascals(1)), section:
            new CustomSection(Area.FromSquareMeters(area), SecondMomentOfArea.FromMetersToTheFourth(1),
                SectionModulus.FromCubicMeters(1)));
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
    }

    [Fact]
    public void OverflowingCoincidentLoadsReturnNumericalFailure()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() =>
            Solve(Cantilever(Point(L, double.MaxValue), Point(L, double.MaxValue))));
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
    [InlineData(1e-100)]
    [InlineData(1e100)]
    public void ScalingModulusAndLoadsTogetherPreservesDisplacements(double factor)
    {
        var material = new Material("Scaled", Pressure.FromPascals(E * factor), Pressure.FromPascals(1));
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Fixed)], [Point(L, -1000 * factor)], material));
        DisplacementClose(-1000 * Math.Pow(L, 3) / (3 * EI), At(solution, L).TransverseDisplacement);
        RotationClose(-1000 * L * L / (2 * EI), At(solution, L).RotationRadians);
    }

    [Fact]
    public void DifferentAxialAndBendingStiffnessScalesDoNotCauseFalseSingularity()
    {
        const double inertia = 1e-80, force = -E * inertia * 1e-3;
        var section = new CustomSection(Area.FromSquareMeters(1e80),
            SecondMomentOfArea.FromMetersToTheFourth(inertia), SectionModulus.FromCubicMeters(1));
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Fixed)], [Point(L, force)], section: section));
        DisplacementClose(-1e-3 * Math.Pow(L, 3) / 3, At(solution, L).TransverseDisplacement);
        RotationClose(-1e-3 * L * L / 2, At(solution, L).RotationRadians);
        DisplacementClose(0, At(solution, L).AxialDisplacement);
    }

    private static void AssertUnstable(BeamModel beam)
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
        Assert.Empty(exception.ValidationErrors);
    }
}
