using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Analysis;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
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
    public void VeryShortAdjacentElementIsSolvedWithoutMergingItsNodes()
    {
        BeamModel beam = Cantilever(Point(1, -1000), Point(1 + 1e-6, 0));
        BeamSolution solution = Solve(beam);
        Assert.Equal(4, solution.Nodes.Count);
        ForceClose(1000, At(solution, 0).ReactionY);
        MomentClose(1000, At(solution, 0).ReactionMoment);
    }

    [Theory]
    [InlineData(1e300, 1e100)]
    [InlineData(1e-200, 1e-200)]
    public void UnrepresentableBendingRigidityReturnsNumericalFailure(double modulus, double inertia)
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(Extreme(modulus, 1, inertia)));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
        Assert.Equal(KernelFailureCause.NumericalRange, Assert.IsType<NumericalRangeException>(exception.InnerException).Cause);
    }

    [Theory]
    [InlineData(1e300, 1e100)]
    [InlineData(1e-200, 1e-200)]
    public void AxialRigidityIsOutsideTheAxialZeroContract(double modulus, double area)
    {
        // Without axial loads u and N are exact zeros; EA never enters a calculation.
        BeamSolution solution = Solve(Extreme(modulus, area, 1, Point(L, -modulus)));
        Assert.All(solution.Nodes, node => Assert.Equal(0, node.AxialDisplacement.Meters));
        Assert.Equal(0, solution.EvaluateAt(M(1), EvaluationSide.Right).AxialForce.Newtons);
        ForceClose(0, At(solution, 0).ReactionX);
        ForceClose(modulus, At(solution, 0).ReactionY);
    }

    [Fact]
    public void EveryKernelCauseHasExactlyOnePublicCodeAndStaysTheInnerException()
    {
        var expected = new Dictionary<KernelFailureCause, SolverErrorCode>
        {
            [KernelFailureCause.ZeroPivot] = SolverErrorCode.IllConditionedSystem,
            [KernelFailureCause.NotVerified] = SolverErrorCode.IllConditionedSystem,
            [KernelFailureCause.Gate] = SolverErrorCode.IllConditionedSystem,
            [KernelFailureCause.NumericalRange] = SolverErrorCode.NumericalFailure,
            [KernelFailureCause.NonFinite] = SolverErrorCode.NumericalFailure,
            [KernelFailureCause.InternalInconsistency] = SolverErrorCode.NumericalFailure
        };
        Assert.Equal(Enum.GetValues<KernelFailureCause>().Order(), expected.Keys.Order());
        foreach (var (cause, code) in expected)
        {
            var kernel = new KernelFailureException(cause, "cause");
            BeamSolverException mapped = BeamSolverException.Rejected(cause, kernel);
            Assert.Equal(code, mapped.Code);
            Assert.Same(kernel, mapped.InnerException);
            Assert.Empty(mapped.ValidationErrors);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BeamSolverException.Rejected((KernelFailureCause)99, new InvalidOperationException()));
        Assert.Equal(SolverErrorCode.NumericalFailure,
            BeamSolverException.Rejected(new NumericalRangeException("range").Cause, new InvalidOperationException()).Code);
    }

    [Fact]
    public void FailedOutputGateIsReportedAsIllConditionedSystem()
    {
        // The conservative rejection of the M2 boundary family: supports 2^-28 m apart under a symmetric load.
        BeamModel beam = Beam([SupportAt(1, SupportType.Pinned), SupportAt(1 + Math.ScaleB(1, -28), SupportType.Roller)],
            [Uniform(0.25, 1.75, 3)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.IllConditionedSystem, exception.Code);
        Assert.Equal(KernelFailureCause.Gate, Assert.IsType<KernelFailureException>(exception.InnerException).Cause);
        Assert.Empty(exception.ValidationErrors);
    }

    [Fact]
    public void RangeViolationInsideTheKernelIsReportedAsNumericalFailure()
    {
        BeamModel beam = Tiny(SupportType.Fixed);
        Assert.Empty(BeamModelValidator.Validate(beam));
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
        Assert.IsType<NumericalRangeException>(exception.InnerException);
    }

    [Fact]
    public void ValidationPrecedesMechanismAndMechanismPrecedesEveryRangeRule()
    {
        // Unrepresentable EI, a mechanism and a validation error at once: the validation error wins.
        BeamModel invalid = Extreme(1e300, 1, 1e100, supports: [SupportAt(1, SupportType.Roller), SupportAt(1, SupportType.Roller)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(invalid));
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
        Assert.Equal(ValidationErrorCode.DuplicateSupport, Assert.Single(exception.ValidationErrors).Code);
        Assert.Null(exception.InnerException);

        // The same unrepresentable EI with a mechanism is unstable, for the adapter's and the kernel's range rules.
        foreach (BeamModel unstable in new[]
        {
            Extreme(1e300, 1, 1e100, supports: [SupportAt(1, SupportType.Pinned)]),
            Tiny(SupportType.Pinned)
        })
        {
            exception = Assert.Throws<BeamSolverException>(() => Solve(unstable));
            Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
            Assert.False(Assert.IsType<MechanicalMechanismException>(exception.InnerException).Stability.IsStable);
        }
    }

    [Fact]
    public void AnalysisReturnsEveryExpectedSolverRejectionAsAFailure()
    {
        foreach (var (beam, code) in new[]
        {
            (Beam(supports: []), BeamAnalysisFailureCode.InvalidModel),
            (Beam([SupportAt(1, SupportType.Pinned)]), BeamAnalysisFailureCode.UnstableModel),
            (Beam([SupportAt(1, SupportType.Pinned), SupportAt(1 + Math.ScaleB(1, -28), SupportType.Roller)],
                [Uniform(0.25, 1.75, 3)]), BeamAnalysisFailureCode.IllConditionedSystem),
            (Extreme(1e300, 1, 1e100), BeamAnalysisFailureCode.NumericalFailure)
        })
        {
            BeamAnalysisOutcome outcome = BeamAnalysis.Analyze(beam);
            Assert.Null(outcome.Result);
            Assert.Equal(code, outcome.Failure!.Code);
        }
    }

    private static BeamModel Tiny(SupportType type) => new(M(1e-300), Beam().Material,
        new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80)), [new Support(M(0), type)], []);

    private static BeamModel Extreme(double modulus, double area, double inertia, BeamLoad? load = null,
        IEnumerable<Support>? supports = null) =>
        Beam(supports ?? [SupportAt(0, SupportType.Fixed)], load is null ? [] : [load], material:
            new Material("Extreme", Pressure.FromPascals(modulus), Pressure.FromPascals(1), MaterialStrengthType.Re), section:
            new CustomSection(Area.FromSquareMeters(area), SecondMomentOfArea.FromMetersToTheFourth(inertia),
                SectionModulus.FromCubicMeters(1)));

    [Fact]
    public void OverflowingCoincidentLoadsReturnNumericalFailure()
    {
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() =>
            Solve(Cantilever(Point(L, double.MaxValue), Point(L, double.MaxValue))));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
    }

    [Theory]
    [InlineData(1e-100)]
    [InlineData(1e100)]
    public void ScalingModulusAndLoadsTogetherPreservesDisplacements(double factor)
    {
        var material = new Material("Scaled", Pressure.FromPascals(E * factor), Pressure.FromPascals(1), MaterialStrengthType.Re);
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
        Assert.IsType<MechanicalMechanismException>(exception.InnerException);
    }
}
