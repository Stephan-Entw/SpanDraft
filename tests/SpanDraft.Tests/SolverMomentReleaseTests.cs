using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Releases;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Internal moment releases through the public solver: closed-form statics, the frozen solver-rework prototype as an
/// independent differential reference, exact failure codes and the public rotation limits.
/// </summary>
public sealed class SolverMomentReleaseTests
{
    private static Material Steel() => new("Test steel", Pressure.FromPascals(E), Pressure.FromMegapascals(235), MaterialStrengthType.Re);
    private static Section Rectangle() => new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80));

    internal static BeamModel Released(double length, IEnumerable<Support> supports, IEnumerable<BeamLoad> loads,
        params double[] releases) =>
        new(M(length), [new BeamSegment(default, M(length), Steel(), Rectangle(), SectionAxisDesignation.Y)], supports, loads,
            releases.Select(x => new MomentRelease(M(x))));

    private static BeamSolution Run(double length, Support[] supports, BeamLoad[] loads, params double[] releases) =>
        Solve(Released(length, supports, loads, releases));

    private static Support Pin(double x) => SupportAt(x, SupportType.Pinned);
    private static Support Roller(double x) => SupportAt(x, SupportType.Roller);
    private static Support Clamp(double x) => SupportAt(x, SupportType.Fixed);

    private static BeamSolverException Reject(BeamModel beam) => Assert.Throws<BeamSolverException>(() => Solve(beam));

    // Statics

    [Fact]
    public void AGerberBeamFollowsClosedFormStatics()
    {
        BeamSolution solution = Run(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3)], 6);

        ForceClose(5e3, At(solution, 8).ReactionY);
        ForceClose(7.5e3, At(solution, 4).ReactionY);
        ForceClose(-2.5e3, At(solution, 0).ReactionY);
        Close(-10e3, solution.EvaluateAt(M(4), EvaluationSide.Right).BendingMoment.NewtonMeters, 1e-6);
        Assert.Equal(-10e3, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-6);
        Assert.Equal(4, solution.Extrema.MinimumBendingMoment.Position.Meters);
        Close(5e3, solution.EvaluateAt(M(6), EvaluationSide.Left).ShearForce.Newtons, 1e-6);
        Close(5e3, solution.EvaluateAt(M(6), EvaluationSide.Right).ShearForce.Newtons, 1e-6);
        // The left part overhangs the roller at 4 by l = 2 m under the hinge shear V = 5 kN and the support
        // moment M_B = -10 kNm: θ_B = M_B L_AB / (3 EI) for the simply supported span, then
        // θ_G = θ_B + (M_B l + V l²/2)/EI and w_G = θ_B l + (M_B l²/2 + V l³/6)/EI.
        BeamNodeResult hinge = At(solution, 6);
        BeamSectionResult left = solution.EvaluateAt(M(6), EvaluationSide.Left), right = solution.EvaluateAt(M(6), EvaluationSide.Right);
        const double v = 5e3, l = 2, mb = -10e3, lab = 4;
        double thetaB = mb * lab / (3 * EI);
        Assert.Equal(0, left.BendingMoment.NewtonMeters);
        Assert.Equal(0, right.BendingMoment.NewtonMeters);
        Assert.Equal(left.TransverseDisplacement.Meters, right.TransverseDisplacement.Meters);
        Assert.Equal(hinge.TransverseDisplacement.Meters, left.TransverseDisplacement.Meters);
        DisplacementClose(thetaB * l + (mb * l * l / 2 + v * l * l * l / 6) / EI, hinge.TransverseDisplacement);
        RotationClose(thetaB + (mb * l + v * l * l / 2) / EI, hinge.RotationLeftRadians!.Value);
        // The right part is a simply supported beam of 2 m between the hinge and the end roller under 10 kN at midspan:
        // its rotation at the hinge follows from the deflection of the hinge and the chord.
        double w = hinge.TransverseDisplacement.Meters;
        RotationClose(-w / 2 - 10e3 * 2 * 2 / (16 * EI), hinge.RotationRadians);
    }

    [Fact]
    public void AReleaseOverASupportSplitsTheBeamIntoTwoSingleSpans()
    {
        BeamSolution solution = Run(2, [Pin(0), Roller(1), Roller(2)], [Point(.5, -1000), Point(1.5, -400)], 1);

        ForceClose(500, At(solution, 0).ReactionY);
        ForceClose(700, At(solution, 1).ReactionY);
        ForceClose(200, At(solution, 2).ReactionY);
        Assert.Equal(0, solution.EvaluateAt(M(1), EvaluationSide.Left).BendingMoment.NewtonMeters);
        Assert.Equal(0, solution.EvaluateAt(M(1), EvaluationSide.Right).BendingMoment.NewtonMeters);
        Close(250, solution.EvaluateAt(M(.5), EvaluationSide.Left).BendingMoment.NewtonMeters, 1e-9);
        BeamNodeResult support = At(solution, 1);
        // Left span carries 1000 N at its middle: θ(L) = P l²/(16 EI); right span 400 N at its middle.
        RotationClose(1000.0 / (16 * EI), support.RotationLeftRadians!.Value);
        RotationClose(-400.0 / (16 * EI), support.RotationRadians);
        Close(0, support.TransverseDisplacement.Meters, 1e-18);
    }

    [Fact]
    public void ACantileverPartBeyondAReleaseCarriesItsTipLoad()
    {
        BeamSolution solution = Run(2, [Clamp(0), Roller(2)], [Point(1.5, -1000)], 1);

        ForceClose(500, At(solution, 2).ReactionY);
        ForceClose(500, At(solution, 0).ReactionY);
        MomentClose(500, At(solution, 0).ReactionMoment);
        BeamNodeResult hinge = At(solution, 1);
        DisplacementClose(-500.0 / (3 * EI), hinge.TransverseDisplacement);
        RotationClose(-500.0 / (2 * EI), hinge.RotationLeftRadians!.Value);
        Assert.NotEqual(hinge.RotationLeftRadians!.Value, hinge.RotationRadians);
    }

    [Fact]
    public void ADropInSpanBetweenTwoClampsIsStaticallyDeterminate()
    {
        BeamSolution solution = Run(2, [Clamp(0), Clamp(2)], [Point(1, -1000)], .5, 1.5);

        ForceClose(500, At(solution, 0).ReactionY);
        ForceClose(500, At(solution, 2).ReactionY);
        MomentClose(250, At(solution, 0).ReactionMoment);
        MomentClose(-250, At(solution, 2).ReactionMoment);
        Close(250, solution.EvaluateAt(M(1), EvaluationSide.Left).BendingMoment.NewtonMeters, 1e-9);
    }

    [Fact]
    public void AReleaseOnATranslationalSpringSplitsOnlyTheRotation()
    {
        const double k = 5e5;
        BeamModel beam = new(M(4), [new BeamSegment(default, M(4), Steel(), Rectangle(), SectionAxisDesignation.Y)],
            [Pin(0), Roller(4), Support.CreateSpring(M(2), TranslationalStiffness.FromNewtonsPerMeter(k), null)],
            [Point(1, -1e3), Point(3, -1e3)], [new MomentRelease(M(2))]);
        BeamSolution solution = Solve(beam);

        // Symmetric: the spring takes the shear that both halves deliver; the release carries no moment.
        BeamNodeResult hinge = At(solution, 2);
        Assert.Equal(0, solution.EvaluateAt(M(2), EvaluationSide.Left).BendingMoment.NewtonMeters);
        ForceClose(-k * hinge.TransverseDisplacement.Meters, hinge.ReactionY);
        double total = At(solution, 0).ReactionY!.Value.Newtons + At(solution, 4).ReactionY!.Value.Newtons + hinge.ReactionY!.Value.Newtons;
        Close(2e3, total, 1e-7);
        Assert.Equal(-hinge.RotationRadians, hinge.RotationLeftRadians!.Value, 1e-18);
    }

    [Fact]
    public void ALoadOnTheReleasePassesAsAShearJumpAndNeverAsAMoment()
    {
        BeamSolution solution = Run(6, [Pin(0), Roller(3), Roller(6)], [Point(4.5, -3e3)], 4.5);
        BeamSectionResult left = solution.EvaluateAt(M(4.5), EvaluationSide.Left), right = solution.EvaluateAt(M(4.5), EvaluationSide.Right);

        Close(-3e3, right.ShearForce.Newtons - left.ShearForce.Newtons, 1e-7);
        Assert.Equal(0, left.BendingMoment.NewtonMeters);
        Assert.Equal(0, right.BendingMoment.NewtonMeters);
        // The part right of the release carries nothing but the roller at its end: it takes no shear, so the whole
        // load goes through the left part and the end roller stays unloaded.
        Close(0, right.ShearForce.Newtons, 1e-7);
        Close(3e3, left.ShearForce.Newtons, 1e-7);
        ForceClose(0, At(solution, 6).ReactionY);
    }

    [Fact]
    public void ATrapezoidLoadAcrossAReleaseKeepsGlobalEquilibrium()
    {
        DistributedLoad load = Linear(1, 5, -2e3, -500);
        BeamSolution solution = Run(6, [Pin(0), Roller(3), Roller(6)], [load], 2);

        double reactions = new[] { 0d, 3, 6 }.Sum(x => At(solution, x).ReactionY!.Value.Newtons);
        Close(-ResultantOf(load), reactions, 1e-6);
        double moment = new[] { 3d, 6 }.Sum(x => x * At(solution, x).ReactionY!.Value.Newtons);
        Close(-MomentAboutOriginOf(load), moment, 1e-6);
        Assert.Equal(0, solution.EvaluateAt(M(2), EvaluationSide.Left).BendingMoment.NewtonMeters);
    }

    [Fact]
    public void AReleaseAtAZeroOfTheMomentChangesNothingAndTheRotationDoesNotJump()
    {
        BeamSolution plain = Run(4, [Clamp(0), Roller(4)], [Uniform(0, 4, -1e3)]);
        BeamSolution released = Run(4, [Clamp(0), Roller(4)], [Uniform(0, 4, -1e3)], 1);

        foreach (double x in new[] { 0, .5, 1, 1.5, 2.5, 4 })
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                if (x == 0 && side == EvaluationSide.Left || x == 4 && side == EvaluationSide.Right) continue;
                BeamSectionResult a = plain.EvaluateAt(M(x), side), b = released.EvaluateAt(M(x), side);
                Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-6);
                Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-6);
                Close(a.TransverseDisplacement.Meters, b.TransverseDisplacement.Meters, 1e-12);
                Close(a.RotationRadians, b.RotationRadians, 1e-12);
            }
        BeamNodeResult hinge = At(released, 1);
        Close(hinge.RotationRadians, hinge.RotationLeftRadians!.Value, 1e-12);
    }

    // Public rotation limits

    [Fact]
    public void OnlyReleaseNodesCarryALeftRotationAndEvaluateAtReturnsBothLimits()
    {
        BeamSolution solution = Run(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3), Uniform(0, 3, -1e3)], 6);
        foreach (BeamNodeResult node in solution.Nodes)
            Assert.Equal(node.Position.Meters == 6, node.RotationLeftRadians.HasValue);
        BeamNodeResult hinge = At(solution, 6);
        Assert.Equal(hinge.RotationRadians, solution.EvaluateAt(M(6), EvaluationSide.Right).RotationRadians);
        Assert.Equal(hinge.RotationLeftRadians, solution.EvaluateAt(M(6), EvaluationSide.Left).RotationRadians);
        Assert.NotEqual(hinge.RotationRadians, hinge.RotationLeftRadians);
        // Everywhere else both sides share the node's rotation exactly.
        foreach (BeamNodeResult node in solution.Nodes.Where(n => n.Position.Meters is > 0 and < 8 && n.Position.Meters != 6))
        {
            Assert.Equal(node.RotationRadians, solution.EvaluateAt(node.Position, EvaluationSide.Left).RotationRadians);
            Assert.Equal(node.RotationRadians, solution.EvaluateAt(node.Position, EvaluationSide.Right).RotationRadians);
        }
    }

    [Fact]
    public void WithoutReleasesNoNodeHasALeftRotationAndTheResultsAreBitIdenticalToTheOldConstructor()
    {
        BeamModel old = Beam([Pin(0), Roller(L)], [Point(1, -1000)]);
        BeamModel viaSegments = new(old.Length, old.Segments, old.Supports, old.Loads, []);
        BeamSolution a = Solve(old), b = Solve(viaSegments);
        Assert.All(a.Nodes, node => Assert.Null(node.RotationLeftRadians));
        Assert.Equal(a.Nodes.Select(n => n.RotationRadians), b.Nodes.Select(n => n.RotationRadians));
        Assert.Equal(a.Nodes.Select(n => n.TransverseDisplacement.Meters), b.Nodes.Select(n => n.TransverseDisplacement.Meters));
        Assert.Equal(a.Extrema.MaximumBendingMoment.Value, b.Extrema.MaximumBendingMoment.Value);
    }

    // Independent differential reference: the frozen solver-rework prototype with hinges

    public static TheoryData<string> Models => new()
    {
        "gerber", "over-support", "clamp-roller", "drop-in", "spring-release", "udl-over-release", "trapezoid", "force-on-release",
        "two-releases-three-supports", "release-on-pinned", "near-support-1e-3", "near-support-6e-9", "load-next-to-release"
    };

    private static (BeamModel Beam, double[] Hinges) ModelWithReleases(string name)
    {
        Support[] rollers3 = [Pin(0), Roller(1), Roller(2)];
        return name switch
        {
            "gerber" => (Released(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3)], 6), [6]),
            "over-support" => (Released(2, rollers3, [Point(.5, -1000), Point(1.5, -400)], 1), [1]),
            "clamp-roller" => (Released(2, [Clamp(0), Roller(2)], [Point(1.5, -1000)], 1), [1]),
            "drop-in" => (Released(2, [Clamp(0), Clamp(2)], [Point(1, -1000)], .5, 1.5), [.5, 1.5]),
            "udl-over-release" => (Released(4, [Pin(0), Roller(2), Roller(4)], [Uniform(0, 4, -700)], 3), [3]),
            "trapezoid" => (Released(6, [Pin(0), Roller(3), Roller(6)], [Linear(1, 5, -2e3, -500)], 2), [2]),
            "force-on-release" => (Released(6, [Pin(0), Roller(3), Roller(6)], [Point(4.5, -3e3)], 4.5), [4.5]),
            "two-releases-three-supports" => (Released(9, [Clamp(0), Roller(3), Roller(6), Roller(9)],
                [Point(1.5, -4e3), Uniform(3, 9, -1e3)], 4.5, 7.5), [4.5, 7.5]),
            "release-on-pinned" => (Released(4, [Pin(0), Pin(2), Roller(4)], [Point(1, -1e3), Point(3, -2e3)], 2), [2]),
            "near-support-1e-3" => (Released(2, rollers3, [Point(1.5, -1000)], 1 + 1e-3), [1 + 1e-3]),
            "near-support-6e-9" => (Released(2, rollers3, [Point(1.5, -1000)], 1 + 6e-9), [1 + 6e-9]),
            "load-next-to-release" => (Released(2, [Clamp(0), Roller(2)], [Point(1 + 6e-9, -1000)], 1), [1]),
            "spring-release" => throw new NotSupportedException("The prototype takes springs through its own load record."),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void TheProductAgreesWithTheFrozenPrototypeOnNodesFieldsAndExtrema(string name)
    {
        if (name == "spring-release") return;
        (BeamModel beam, double[] hinges) = ModelWithReleases(name);
        BeamModel withoutReleases = new(beam.Length, beam.Segments, beam.Supports, beam.Loads);
        ReferenceBeamSolution reference = ReferenceBeamSolver.Prototype(withoutReleases, new SolverLoads([], [], [], [], [], hinges));
        BeamSolution product = Solve(beam);

        Assert.Equal(reference.Nodes.Select(n => n.Position), product.Nodes.Select(n => n.Position));
        for (int i = 0; i < product.Nodes.Count; i++)
        {
            BeamNodeResult p = product.Nodes[i], r = reference.Nodes[i];
            DisplacementClose(r.TransverseDisplacement.Meters, p.TransverseDisplacement);
            RotationClose(r.RotationRadians, p.RotationRadians);
            Assert.Equal(r.ReactionY.HasValue, p.ReactionY.HasValue);
            if (r.ReactionY.HasValue) ForceClose(r.ReactionY.Value.Newtons, p.ReactionY);
            Assert.Equal(r.ReactionMoment.HasValue, p.ReactionMoment.HasValue);
            if (r.ReactionMoment.HasValue) MomentClose(r.ReactionMoment.Value.NewtonMeters, p.ReactionMoment);
            bool release = hinges.Contains(p.Position.Meters);
            Assert.Equal(release, p.RotationLeftRadians.HasValue);
            if (release)
                RotationClose(reference.EvaluateAt(p.Position, EvaluationSide.Left).RotationRadians, p.RotationLeftRadians!.Value);
        }

        double[] samples = [.. product.Nodes.Select(n => n.Position.Meters)
            .SelectMany((x, i) => i == 0 ? [x] : new[] { x, (x + product.Nodes[i - 1].Position.Meters) / 2 })];
        foreach (double x in samples)
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult p = product.EvaluateAt(M(x), side), r = reference.EvaluateAt(M(x), side);
                Close(r.BendingMoment.NewtonMeters, p.BendingMoment.NewtonMeters, 1e-6);
                Close(r.ShearForce.Newtons, p.ShearForce.Newtons, 1e-6);
                Close(r.TransverseDisplacement.Meters, p.TransverseDisplacement.Meters, 1e-12);
                Close(r.RotationRadians, p.RotationRadians, 1e-12);
            }
        Close(reference.Extrema.MaximumBendingMoment.Value.NewtonMeters, product.Extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-6);
        Close(reference.Extrema.MinimumBendingMoment.Value.NewtonMeters, product.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-6);
        Close(reference.Extrema.MaximumShearForce.Value.Newtons, product.Extrema.MaximumShearForce.Value.Newtons, 1e-6);
        Close(reference.Extrema.MinimumShearForce.Value.Newtons, product.Extrema.MinimumShearForce.Value.Newtons, 1e-6);
        Close(reference.Extrema.MaximumTransverseDisplacement.Value.Meters, product.Extrema.MaximumTransverseDisplacement.Value.Meters, 1e-12);
        Close(reference.Extrema.MinimumTransverseDisplacement.Value.Meters, product.Extrema.MinimumTransverseDisplacement.Value.Meters, 1e-12);
    }

    // Invariants

    [Fact]
    public void MirroringTheBeamSwapsTheRotationLimitsAndKeepsMomentAndDeflection()
    {
        BeamSolution solution = Run(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3), Uniform(0, 3, -1e3)], 6);
        BeamSolution mirrored = Run(8, [Roller(0), Roller(4), Pin(8)], [Point(1, -10e3), Uniform(5, 8, -1e3)], 2);

        BeamNodeResult a = At(solution, 6), b = At(mirrored, 2);
        DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
        RotationClose(-a.RotationRadians, b.RotationLeftRadians!.Value);
        RotationClose(-a.RotationLeftRadians!.Value, b.RotationRadians);
        foreach (double x in new[] { 0.5, 3, 4, 5, 7.5 })
        {
            BeamSectionResult s = solution.EvaluateAt(M(x), EvaluationSide.Right), m = mirrored.EvaluateAt(M(8 - x), EvaluationSide.Left);
            Close(s.BendingMoment.NewtonMeters, m.BendingMoment.NewtonMeters, 1e-6);
            Close(s.ShearForce.Newtons, -m.ShearForce.Newtons, 1e-6);
            Close(s.TransverseDisplacement.Meters, m.TransverseDisplacement.Meters, 1e-12);
            Close(s.RotationRadians, -m.RotationRadians, 1e-12);
        }
    }

    [Fact]
    public void ResultsAreLinearInTheLoads()
    {
        Support[] supports = [Clamp(0), Roller(3), Roller(6), Roller(9)];
        BeamLoad[] first = [Point(1.5, -4e3)], second = [Uniform(3, 9, -1e3)];
        BeamSolution a = Run(9, supports, first, 4.5, 7.5), b = Run(9, supports, second, 4.5, 7.5);
        BeamSolution both = Run(9, supports, [.. first, .. second], 4.5, 7.5);
        BeamSolution doubled = Run(9, supports, [Point(1.5, -8e3)], 4.5, 7.5);

        foreach (double x in new[] { 0.5, 3, 4.5, 6, 7.5, 8 })
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult sa = a.EvaluateAt(M(x), side), sb = b.EvaluateAt(M(x), side), sab = both.EvaluateAt(M(x), side);
                Close(sa.BendingMoment.NewtonMeters + sb.BendingMoment.NewtonMeters, sab.BendingMoment.NewtonMeters, 1e-6);
                Close(sa.ShearForce.Newtons + sb.ShearForce.Newtons, sab.ShearForce.Newtons, 1e-6);
                Close(sa.TransverseDisplacement.Meters + sb.TransverseDisplacement.Meters, sab.TransverseDisplacement.Meters, 1e-12);
                Close(sa.RotationRadians + sb.RotationRadians, sab.RotationRadians, 1e-12);
                Close(2 * sa.RotationRadians, doubled.EvaluateAt(M(x), side).RotationRadians, 1e-12);
            }
    }

    [Fact]
    public void EachReleasedBodyIsInEquilibriumWithItsLoadsAndReactions()
    {
        // Gerber beam: the right body [6, 8] carries 10 kN at 7 and is held by the hinge shear and the end roller.
        BeamSolution solution = Run(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3)], 6);
        double endReaction = At(solution, 8).ReactionY!.Value.Newtons;
        // Moment about the hinge: the end roller times 2 m balances the load times 1 m; the hinge shear carries the rest.
        Close(10e3 * 1, endReaction * 2, 1e-6);
        Close(10e3 - endReaction, solution.EvaluateAt(M(6), EvaluationSide.Right).ShearForce.Newtons, 1e-6);
    }

    [Fact]
    public void ARepeatedSolveIsBitIdentical()
    {
        BeamModel beam = Released(8, [Pin(0), Roller(4), Roller(8)], [Point(7, -10e3), Uniform(0, 3, -1e3)], 6);
        BeamSolution a = Solve(beam), b = Solve(beam);
        Assert.Equal(a.Nodes.Select(n => (n.RotationRadians, n.RotationLeftRadians, n.TransverseDisplacement.Meters)),
            b.Nodes.Select(n => (n.RotationRadians, n.RotationLeftRadians, n.TransverseDisplacement.Meters)));
    }

    // Failures

    [Fact]
    public void ACantileverWithAReleaseIsAnUnstableMechanismWithItsOwnMessage()
    {
        BeamSolverException exception = Reject(Released(2, [Clamp(0)], [Point(1.9, -1)], 1));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
        Assert.Contains("moment releases", exception.Message);
        Assert.IsType<MechanicalMechanismException>(exception.InnerException);
        Assert.Empty(exception.ValidationErrors);
    }

    [Fact]
    public void WithoutReleasesTheMechanismMessageIsUnchanged()
    {
        BeamSolverException exception = Reject(Beam([Pin(0)], [Point(1, -1)]));
        Assert.Equal("Supports leave an axial translation or a transverse rigid-body motion unconstrained.", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheMechanismsOfTheRankMatrixAreUnstable(int index)
    {
        BeamModel beam = index switch
        {
            0 => Released(2, [Clamp(2)], [Point(.2, -1)], .5),
            1 => Released(2, [Pin(0), Roller(2)], [Point(.5, -1)], 1),
            2 => Released(4, [Pin(0), Roller(2), Roller(4)], [Point(1, -1)], 1, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        Assert.Equal(SolverErrorCode.UnstableModel, Reject(beam).Code);
    }

    [Fact]
    public void ASupportedReleaseBetweenTwoRollersStaysAMechanismWithoutAxialRestraint()
    {
        BeamSolverException exception = Reject(Released(4, [Roller(0), Roller(2), Roller(4)], [Point(1, -1)], 2));
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
    }

    [Fact]
    public void AMechanismWinsOverTheRangeRulesOfTheBendingStiffness()
    {
        var material = new Material("Soft", Pressure.FromPascals(double.Epsilon), Pressure.FromPascals(1), MaterialStrengthType.Re);
        var section = new CustomSection(Area.FromSquareMeters(1), SecondMomentOfArea.FromMetersToTheFourth(double.Epsilon),
            SectionModulus.FromCubicMeters(1));
        BeamModel beam = new(M(2), [new BeamSegment(default, M(2), material, section, SectionAxisDesignation.Y)],
            [Pin(0), Roller(2)], [Point(1, -1)], [new MomentRelease(M(1))]);
        BeamSolverException exception = Reject(beam);
        Assert.Equal(SolverErrorCode.UnstableModel, exception.Code);
        Assert.IsType<MechanicalMechanismException>(exception.InnerException);
    }

    public static TheoryData<int, ValidationErrorCode> ModelErrors => new()
    {
        { 0, ValidationErrorCode.MomentReleaseNotInsideBeam },
        { 1, ValidationErrorCode.MomentReleaseNotInsideBeam },
        { 2, ValidationErrorCode.DuplicateMomentRelease },
        { 3, ValidationErrorCode.MomentReleaseAtFixedSupport },
        { 4, ValidationErrorCode.MomentReleaseAtPointMoment },
        { 5, ValidationErrorCode.MomentReleaseAtRotationalSpring },
    };

    [Theory]
    [MemberData(nameof(ModelErrors))]
    public void InvalidReleaseCombinationsAreAnInvalidModelWithTheValidatorsErrors(int index, ValidationErrorCode code)
    {
        BeamModel beam = index switch
        {
            0 => Released(2, [Pin(0), Roller(2)], [], 0),
            1 => Released(2, [Pin(0), Roller(2)], [], 2),
            2 => Released(2, [Pin(0), Roller(2), Roller(1)], [], 1, 1),
            3 => Released(2, [Pin(0), Clamp(1)], [], 1),
            4 => Released(2, [Pin(0), Roller(2)], [Couple(1, 0)], 1),
            5 => new BeamModel(M(2), [new BeamSegment(default, M(2), Steel(), Rectangle(), SectionAxisDesignation.Y)],
                [Pin(0), Roller(2), Support.CreateSpring(M(1), null, RotationalStiffness.FromNewtonMetersPerRadian(5e3))],
                [], [new MomentRelease(M(1))]),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        BeamSolverException exception = Reject(beam);
        Assert.Equal(SolverErrorCode.InvalidModel, exception.Code);
        Assert.Equal(code, Assert.Single(exception.ValidationErrors).Code);
        Assert.Equal(BeamModelValidator.Validate(beam), exception.ValidationErrors);
    }

    [Fact]
    public void AnalysisReturnsTheReleaseRejectionsAsFailures()
    {
        BeamAnalysisOutcome unstable = BeamAnalysis.Analyze(Released(2, [Clamp(0)], [Point(1.9, -1)], 1));
        BeamAnalysisOutcome invalid = BeamAnalysis.Analyze(Released(2, [Pin(0), Roller(2)], [], 0));
        Assert.Equal(BeamAnalysisFailureCode.UnstableModel, Assert.IsType<BeamAnalysisFailure>(unstable.Failure).Code);
        Assert.Equal(BeamAnalysisFailureCode.InvalidModel, Assert.IsType<BeamAnalysisFailure>(invalid.Failure).Code);
    }

    [Fact]
    public void ANearlyCoincidentReleasePairIsNeverMergedOrSnapped()
    {
        double h = Math.BitIncrement(1);
        BeamSolution solution = Run(2, [Clamp(0), Clamp(2)], [Point(1.5, -1000)], 1, h);
        Assert.Equal(2, solution.Nodes.Count(n => n.RotationLeftRadians.HasValue));
        Assert.Contains(solution.Nodes, n => n.Position.Meters == 1);
        Assert.Contains(solution.Nodes, n => n.Position.Meters == h);
    }
}
