using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverSpringAnalyticalTests;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>Every rejection of a spring model has exactly the code of its cause.</summary>
public sealed class SolverSpringFailureTests
{
    private static BeamSolverException Reject(BeamModel beam) => Assert.Throws<BeamSolverException>(() => Solve(beam));

    [Fact]
    public void ASpringAtARigidSupportOrAnotherSpringIsAnInvalidModelWithItsValidationError()
    {
        foreach (BeamModel beam in new[]
        {
            Beam([SupportAt(1, SupportType.Pinned), Spring(1, 1e5), SupportAt(L, SupportType.Roller)]),
            Beam([Spring(1, 1e5), Spring(1, 2e5), SupportAt(0, SupportType.Pinned)])
        })
        {
            BeamSolverException exception = Reject(beam);
            Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
            Assert.Equal(ValidationErrorCode.SpringSupportCoincidence, Assert.Single(exception.ValidationErrors).Code);
        }
    }

    [Fact]
    public void ASingleTranslationalSpringIsAnUnstableMechanismForEveryStiffness()
    {
        foreach (double k in new[] { 1e-200, 1, 1e12, 1e200 })
        {
            BeamSolverException exception = Reject(Beam([Spring(1, k)], [Point(0.5, -1)]));
            Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
            Assert.False(Assert.IsType<MechanicalMechanismException>(exception.InnerException).Stability.IsStable);
        }
    }

    // k·2^(3·e_L − e_EI) must be a normal number: the soft side underflows for a stiff beam, the stiff side
    // overflows for a beam of tiny bending stiffness.
    private static BeamModel TinyStiffnessBeam(double k) => Beam([SupportAt(0, SupportType.Pinned), Spring(L, k)],
        [Point(1, -1e-290)], material: new Material("Soft", Pressure.FromPascals(1e-150), Pressure.FromPascals(1),
            MaterialStrengthType.Re), section: new CustomSection(Area.FromSquareMeters(1),
            SecondMomentOfArea.FromMetersToTheFourth(1e-150), SectionModulus.FromCubicMeters(1)));

    [Fact]
    public void AStiffnessWhoseScaledCoefficientIsNotNormalIsANumericalRangeFailure()
    {
        foreach (BeamModel beam in new[]
        {
            Beam([SupportAt(0, SupportType.Pinned), Spring(L, 1e-300)], [Point(1, -1000)]),
            TinyStiffnessBeam(1e100),
            // A subnormal input is outside the range contract even where its scaled value would be normal.
            TinyStiffnessBeam(double.Epsilon)
        })
        {
            BeamSolverException exception = Reject(beam);
            Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
            Assert.IsType<NumericalRangeException>(exception.InnerException);
        }
    }

    [Fact]
    public void AnUnstableSpringModelWinsOverTheRangeRulesOfItsStiffness()
    {
        // The mechanism check reads neither the stiffness nor the loads.
        BeamSolverException exception = Reject(Beam([Spring(1, 1e-300)], [Point(0.5, -1)]));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void AnalysisReturnsTheSpringRejectionsAsFailures()
    {
        foreach (var (beam, code) in new[]
        {
            (Beam([Spring(1, 1e5)]), BeamAnalysisFailureCode.UnstableModel),
            (Beam([SupportAt(1, SupportType.Fixed), Spring(1, 1e5)]), BeamAnalysisFailureCode.InvalidModel),
            (Beam([SupportAt(0, SupportType.Pinned), Spring(L, 1e-300)], [Point(1, -1000)]), BeamAnalysisFailureCode.NumericalFailure)
        })
        {
            BeamAnalysisOutcome outcome = BeamAnalysis.Analyze(beam);
            Assert.Null(outcome.Result);
            Assert.Equal(code, outcome.Failure!.Code);
        }
    }

    [Fact]
    public void ASpringBeamOnTwoSoftSpringsIsDeliveredWithinTheDocumentedRange()
    {
        // A beam held only by soft springs is exactly stable; it moves as a nearly rigid body.
        double k = 1e-3 * EI / Math.Pow(L, 3);
        BeamSolution solution = Solve(Beam([Spring(0, k), Spring(L, k)], [Point(1, -1)]));
        Close(0.5, At(solution, 0).ReactionY!.Value.Newtons, 1e-9);
        Close(-0.5 / k, At(solution, 0).TransverseDisplacement.Meters, 1e-6);
    }

    [Fact]
    public void SpringsFarSofterThanTheDocumentedRangeAreRejectedByTheGateNotDeliveredWrongly()
    {
        BeamSolverException exception = Reject(Beam([Spring(0, 1e-3), Spring(L, 1e-3)], [Point(1, -1)]));
        Assert.Equal(SolverErrorCode.IllConditionedSystem, exception.Code);
        Assert.Equal(KernelFailureCause.Gate, Assert.IsType<KernelFailureException>(exception.InnerException).Cause);
    }
}
