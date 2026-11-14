using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Tests.MultiSectionTestSupport;
using static SpanDraft.Tests.SolverTestSupport;
using static SpanDraft.Tests.SolverSpringAnalyticalTests;

namespace SpanDraft.Tests;

/// <summary>Mechanical invariants of multi-segment beams, the frozen prototype as comparison and rejections.</summary>
public sealed class SolverMultiSectionInvariantTests
{
    private const double I1 = 8e-6;

    private static readonly double[] Samples = [0, 0.1, 0.45, 0.8, 1.0, 1.25, 1.6, 1.9, L];

    private static Support[] Supports() =>
        [SupportAt(0, SupportType.Pinned), SupportAt(1.25, SupportType.Roller), Spring(L, 3e6)];

    private static BeamLoad[] Loads() =>
        [Point(0.4, -1200), Couple(1.0, 350), Uniform(0.6, 1.8, -500), Linear(1.1, 1.9, 200, -900)];

    private static BeamSegment[] Single() => [Seg(0, L, I1)];

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.4)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.8)]
    [InlineData(2.0 - 1e-9)]
    public void SplittingAtIdenticalPropertiesLeavesAllResultsUnchangedWithinTheBounds(double boundary)
    {
        BeamSolution whole = Solve(Stepped(L, Single(), Supports(), Loads()));
        BeamSolution split = Solve(Stepped(L, [Seg(0, boundary, I1), Seg(boundary, L, I1)], Supports(), Loads()));

        foreach (double x in Samples)
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = whole.EvaluateAt(M(x), side), b = split.EvaluateAt(M(x), side);
                DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
                RotationClose(a.RotationRadians, b.RotationRadians);
                Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-6);
                Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-6);
            }
        foreach (double x in new[] { 0, 1.25, L })
            ForceClose(At(whole, x).ReactionY!.Value.Newtons, At(split, x).ReactionY);
        Close(whole.Extrema.MaximumBendingMoment.Value.NewtonMeters, split.Extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-6);
        Close(whole.Extrema.MinimumTransverseDisplacement.Value.Meters, split.Extrema.MinimumTransverseDisplacement.Value.Meters, 1e-12);
    }

    [Fact]
    public void EqualStiffnessProductsFromDifferentModulusAndInertiaGiveBitIdenticalResults()
    {
        // E/2 and 2 I have the same exact product; the kernel sees the same input.
        BeamSolution same = Solve(Stepped(L, [Seg(0, 0.9, I1), Seg(0.9, L, I1 / 2)], Supports(), Loads()));
        BeamSolution other = Solve(Stepped(L, [Seg(0, 0.9, I1), Seg(0.9, L, I1, E / 2)], Supports(), Loads()));

        foreach (double x in Samples)
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = same.EvaluateAt(M(x), side), b = other.EvaluateAt(M(x), side);
                Assert.Equal(a.TransverseDisplacement.Meters, b.TransverseDisplacement.Meters);
                Assert.Equal(a.RotationRadians, b.RotationRadians);
                Assert.Equal(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters);
                Assert.Equal(a.ShearForce.Newtons, b.ShearForce.Newtons);
            }
    }

    [Fact]
    public void DeflectionRotationMomentAndShearAreContinuousAtABoundaryWithoutALoadThere()
    {
        double[] boundaries = [0.5, 1.5];
        BeamSolution solution = Solve(Stepped(L, [Seg(0, 0.5, I1), Seg(0.5, 1.5, I1 / 7), Seg(1.5, L, I1 * 3)],
            [SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Roller)],
            [Uniform(0.2, 1.7, -400), Point(1.0, -800)]));
        foreach (double x in boundaries)
        {
            BeamSectionResult left = solution.EvaluateAt(M(x), EvaluationSide.Left);
            BeamSectionResult right = solution.EvaluateAt(M(x), EvaluationSide.Right);
            Assert.Equal(left.TransverseDisplacement.Meters, right.TransverseDisplacement.Meters);
            Assert.Equal(left.RotationRadians, right.RotationRadians);
            Close(left.BendingMoment.NewtonMeters, right.BendingMoment.NewtonMeters, 1e-8);
            Close(left.ShearForce.Newtons, right.ShearForce.Newtons, 1e-8);
        }
    }

    [Fact]
    public void ScalingAllStiffnessesByAPowerOfTwoScalesOnlyTheKinematics()
    {
        // Springs would make the forces depend on EI: only a spring-free model keeps them invariant.
        Support[] supports = [SupportAt(0, SupportType.Pinned), SupportAt(1.25, SupportType.Roller), SupportAt(L, SupportType.Roller)];
        BeamSolution a = Solve(Stepped(L, [Seg(0, 0.9, I1), Seg(0.9, L, I1 / 3)], supports, Loads()));
        BeamSolution b = Solve(Stepped(L, [Seg(0, 0.9, I1 * 8), Seg(0.9, L, I1 * 8 / 3)], supports, Loads()));

        foreach (double x in Samples)
        {
            BeamSectionResult p = a.EvaluateAt(M(x), EvaluationSide.Right), q = b.EvaluateAt(M(x), EvaluationSide.Right);
            Close(p.TransverseDisplacement.Meters / 8, q.TransverseDisplacement.Meters, 1e-15);
            Close(p.RotationRadians / 8, q.RotationRadians, 1e-15);
            Close(p.BendingMoment.NewtonMeters, q.BendingMoment.NewtonMeters, 1e-7);
            Close(p.ShearForce.Newtons, q.ShearForce.Newtons, 1e-7);
        }
        foreach (double x in new[] { 0, 1.25, L })
            ForceClose(At(a, x).ReactionY!.Value.Newtons, At(b, x).ReactionY);
    }

    [Fact]
    public void MirroringASteppedBeamMirrorsItsResults()
    {
        BeamSolution left = Solve(Stepped(L, [Seg(0, 0.7, I1), Seg(0.7, 1.3, I1 / 5), Seg(1.3, L, I1 * 2)],
            [SupportAt(0, SupportType.Fixed), SupportAt(1.3, SupportType.Roller)],
            [Point(1.9, -1000), Uniform(0.3, 1.1, -300)]));
        BeamSolution right = Solve(Stepped(L, [Seg(0, 0.7, I1 * 2), Seg(0.7, 1.3, I1 / 5), Seg(1.3, L, I1)],
            [SupportAt(L, SupportType.Fixed), SupportAt(0.7, SupportType.Roller)],
            [Point(0.1, -1000), Uniform(0.9, 1.7, -300)]));
        foreach (double x in new[] { 0, 0.35, 0.7, 1.0, 1.3, 1.6, L })
        {
            BeamSectionResult a = left.EvaluateAt(M(x), EvaluationSide.Right), b = right.EvaluateAt(M(L - x), EvaluationSide.Left);
            Close(a.TransverseDisplacement.Meters, b.TransverseDisplacement.Meters, 1e-13);
            Close(a.RotationRadians, -b.RotationRadians, 1e-13);
            Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
        }
        ForceClose(At(left, 0).ReactionY!.Value.Newtons, At(right, L).ReactionY);
        ForceClose(At(left, 1.3).ReactionY!.Value.Newtons, At(right, 0.7).ReactionY);
    }

    public static TheoryData<string> PrototypeCases => new() { "cantilever", "two-span", "mixed-spring", "three-sections" };

    private static (BeamModel Beam, BeamModel Single, PointAction[] Steps, ElasticSupport[] Springs) Prototype(string name)
    {
        Support[] fixedSupport = [SupportAt(0, SupportType.Fixed)];
        Support[] two = [SupportAt(0, SupportType.Pinned), SupportAt(1.25, SupportType.Roller), SupportAt(L, SupportType.Roller)];
        double ei = E * I1;
        return name switch
        {
            "cantilever" => Case([Seg(0, 0.8, I1), Seg(0.8, L, I1 / 3)], fixedSupport, [Point(L, -1500), Uniform(0.2, 1.4, -250)],
                [new(0.8, ei / 3)], []),
            "two-span" => Case([Seg(0, 1.25, I1), Seg(1.25, L, I1 * 6)], two, [Uniform(0, L, -800), Point(0.5, -300)],
                [new(1.25, ei * 6)], []),
            "mixed-spring" => Case([Seg(0, 0.5, I1), Seg(0.5, 1.5, I1 / 4), Seg(1.5, L, I1 * 2)],
                [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller), Spring(0.5, 2e5)],
                [Couple(1.5, 400), Linear(0.3, 1.9, -700, 300), Point(1.0, -900)],
                [new(0.5, ei / 4), new(1.5, ei * 2)], [new(0.5, 2e5)]),
            "three-sections" => Case([Seg(0, 0.6, I1 * 20), Seg(0.6, 1.4, I1), Seg(1.4, L, I1 / 30)], two,
                [Point(1.4, -1000), Uniform(0.1, 1.9, -150)], [new(0.6, ei), new(1.4, ei / 30)], []),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        (BeamModel, BeamModel, PointAction[], ElasticSupport[]) Case(BeamSegment[] segments, Support[] supports,
            BeamLoad[] loads, PointAction[] steps, ElasticSupport[] springs)
        {
            // The prototype keeps the first segment as the beam's own EI and takes later ones as steps.
            // Springs are solver components there, not Core supports.
            Support[] rigid = supports.Where(s => s.Type != SupportType.Spring).ToArray();
            return (Stepped(L, segments, supports, loads),
                new BeamModel(M(L), segments[0].Material, segments[0].Section, SectionAxisDesignation.Y, rigid.Length == 0 ? [SupportAt(0, SupportType.Pinned)] : rigid,
                    loads), steps, springs);
        }
    }

    [Theory]
    [MemberData(nameof(PrototypeCases))]
    public void ResultsAgreeWithTheFrozenPrototypeUsingEiStepsAtTheSameBoundaries(string name)
    {
        var (beam, single, steps, springs) = Prototype(name);
        BeamSolution product = Solve(beam);
        // The mixed-spring case has rigid supports at both ends, so the prototype needs no further support.
        ReferenceBeamSolution reference = ReferenceBeamSolver.Prototype(single,
            new SolverLoads([], [], [], [], [], [], springs, steps));

        foreach (double x in new[] { 0, 0.1, 0.5, 0.6, 0.8, 1.0, 1.25, 1.4, 1.5, 1.9, L })
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = product.EvaluateAt(M(x), side), b = reference.EvaluateAt(M(x), side);
                Close(b.TransverseDisplacement.Meters, a.TransverseDisplacement.Meters, 1e-10);
                Close(b.RotationRadians, a.RotationRadians, 1e-10);
                Close(b.BendingMoment.NewtonMeters, a.BendingMoment.NewtonMeters, 1e-5);
                Close(b.ShearForce.Newtons, a.ShearForce.Newtons, 1e-5);
            }
        foreach (BeamNodeResult node in product.Nodes.Where(n => n.ReactionY is not null))
            ForceClose(node.ReactionY!.Value.Newtons, reference.Nodes.Single(n => n.Position == node.Position).ReactionY);
    }

    [Fact]
    public void SegmentsThatDoNotTileTheBeamAreAnInvalidModelBeforeAnyNumerics()
    {
        BeamModel gap = Stepped(L, [Seg(0, 0.8, I1), Seg(1.0, L, I1)], Supports(), Loads());
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(gap));
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
        Assert.Equal(ValidationErrorCode.SegmentsNotContiguous, Assert.Single(exception.ValidationErrors).Code);
    }

    [Fact]
    public void ARigidBodyModeIsStillAMechanismWhenSegmentsDiffer()
    {
        BeamModel beam = Stepped(L, [Seg(0, 1, I1), Seg(1, L, I1 / 2)], [SupportAt(0, SupportType.Pinned)], [Point(L, -1)]);
        Assert.Equal(SolverErrorCode.UnstableModel, Assert.Throws<BeamSolverException>(() => Solve(beam)).Code);
    }

    [Fact]
    public void AnExtremeStiffnessRatioIsRejectedNumericallyInsteadOfDeliveredInaccurately()
    {
        // The softer section's normalized EI would be subnormal.
        BeamModel beam = Stepped(L, [Seg(0, 1, 1e100, 1e200), Seg(1, L, 1e-12, 1e-5)],
            [SupportAt(0, SupportType.Fixed)], [Point(L, -1)]);
        BeamSolverException exception = Assert.Throws<BeamSolverException>(() => Solve(beam));
        Assert.Equal(SolverErrorCode.NumericalFailure, exception.Code);
        Assert.IsType<NumericalRangeException>(exception.InnerException);
    }

    [Fact]
    public void TheKernelRejectsSectionsThatDoNotTileTheBeamExactly()
    {
        StiffnessSection[][] invalid =
        [
            [new(0, 0.8, 1), new(0.9, 2, 1)],
            [new(0, 1.2, 1), new(1.0, 2, 1)],
            [new(0, 0.8, 1), new(0.8, 1.9, 1)],
            [new(0.1, 0.8, 1), new(0.8, 2, 1)],
            [new(0, 1, 1), new(1, 1, 1), new(1, 2, 1)],
            [new(0, 1, 1), new(Math.BitIncrement(1), 2, 1)]
        ];
        foreach (StiffnessSection[] sections in invalid)
            Assert.Throws<InvalidMechanicalModelException>(() => SolverKernel.BuildSystem(Multi(2, sections)));
    }

    [Fact]
    public void ABoundaryOnASpringIsAcceptedByTheKernelWhileASecondSupportOnTheSpringIsNot()
    {
        StiffnessSection[] sections = [new(0, 1, 1), new(1, 2, 2)];
        SolverKernel.BuildSystem(Multi(2, sections, [new(0, RestrainedDirections.U | RestrainedDirections.W)],
            elastic: [new(1, 5, 0, false)]));
        Assert.Throws<InvalidMechanicalModelException>(() => SolverKernel.BuildSystem(Multi(2, sections,
            [new(0, RestrainedDirections.U | RestrainedDirections.W), new(1, RestrainedDirections.W)],
            elastic: [new(1, 5, 0, false)])));
    }
}
