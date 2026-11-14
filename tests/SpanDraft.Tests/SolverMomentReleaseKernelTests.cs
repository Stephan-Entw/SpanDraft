using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverOutputOracle;

namespace SpanDraft.Tests;

/// <summary>
/// The state kernel with internal moment releases against the exact rational compatibility oracle, which adds the
/// rotation jump of every release as an unknown and the vanishing left moment as its condition, and against
/// closed-form statics.
/// </summary>
public sealed class SolverMomentReleaseKernelTests
{
    private const double EI = 210e9 * 8e-6;

    private static InternalMomentRelease[] Releases(params double[] positions) =>
        [.. positions.Select(x => new InternalMomentRelease(x))];

    public static TheoryData<string> Configurations => new()
    {
        "gerber", "release-over-roller", "release-over-pinned", "clamp-release-roller", "drop-in-span", "two-releases-three-supports",
        "release-on-spring", "release-with-section-change", "release-in-section", "trapezoid-over-release",
        "load-starting-at-release", "force-on-release", "force-on-supported-release", "release-at-load-and-boundary",
        "mirrored-gerber", "release-with-reactions-both-sides", "clamp-clamp-releases"
    };

    internal static MechanicalInput Case(string name) => name switch
    {
        // Gerber beam: A pinned at 0, B roller at 4, hinge at 6, C roller at 8; 10 kN down at 7.
        "gerber" => Model(8, EI, [new(0, U | W), new(4, W), new(8, W)], [new(7, -10e3)], releases: Releases(6)),
        "release-over-roller" => Model(4, EI, [new(0, U | W), new(2, W), new(4, W)], [new(1, -1e3), new(3, -2e3)],
            releases: Releases(2)),
        "release-over-pinned" => Model(4, EI, [new(0, U | W), new(2, U | W), new(4, W)], [new(1, -1e3), new(3, -2e3)],
            lines: [new(0, 4, -500)], releases: Releases(2)),
        "clamp-release-roller" => Model(2, EI, [new(0, U | W | Theta), new(2, W)], [new(1.5, -1e3)], releases: Releases(1)),
        "drop-in-span" => Model(2, EI, [new(0, U | W | Theta), new(2, W | U | Theta)], [new(1, -1e3)], releases: Releases(.5, 1.5)),
        "two-releases-three-supports" => Model(9, EI, [new(0, U | W | Theta), new(3, W), new(6, W), new(9, W)],
            [new(1.5, -4e3)], lines: [new(3, 9, -1e3)], releases: Releases(4.5, 7.5)),
        "release-on-spring" => Model(4, EI, [new(0, U | W), new(4, W)], [new(1, -1e3), new(3, -1e3)],
            elastic: [new(2, 5e5, 0, false)], releases: Releases(2)),
        "release-with-section-change" => new(4, [new StiffnessSection(0, 2, EI), new StiffnessSection(2, 4, 3 * EI)],
            [new(0, U | W), new(1, W), new(4, W)], [new(3, -1e3)], [], [new(0, 4, -300)], null, Releases(2)),
        "release-in-section" => new(4, [new StiffnessSection(0, 2, 2 * EI), new StiffnessSection(2, 4, EI)],
            [new(0, U | W), new(1, W), new(4, W)], [new(3, -1e3)], [], [new(0, 4, -300)], null, Releases(3)),
        "trapezoid-over-release" => Model(6, EI, [new(0, U | W), new(3, W), new(6, W)], lines: [new(1, 5, -2e3, -500)],
            releases: Releases(2)),
        "load-starting-at-release" => Model(6, EI, [new(0, U | W), new(3, W), new(6, W)],
            lines: [new(4.5, 6, -2e3), new(1.5, 4.5, -700, 300)], releases: Releases(4.5)),
        "force-on-release" => Model(6, EI, [new(0, U | W), new(3, W), new(6, W)], [new(4.5, -3e3)], releases: Releases(4.5)),
        "force-on-supported-release" => Model(6, EI, [new(0, U | W), new(3, W), new(6, W)], [new(3, -3e3), new(5, -1e3)],
            releases: Releases(3)),
        "release-at-load-and-boundary" => new(6, [new StiffnessSection(0, 4.5, EI), new StiffnessSection(4.5, 6, 5 * EI)],
            [new(0, U | W), new(3, W), new(6, W)], [new(4.5, -3e3)], [], [new(4.5, 6, -900)], null, Releases(4.5)),
        "mirrored-gerber" => Model(8, EI, [new(0, W), new(4, U | W), new(8, W)], [new(1, -10e3)], releases: Releases(2)),
        "release-with-reactions-both-sides" => Model(6, EI, [new(0, U | W), new(2.5, W), new(3.5, W), new(6, W)],
            [new(1, -2e3), new(5, -1e3)], releases: Releases(3)),
        "clamp-clamp-releases" => Model(6, EI, [new(0, U | W | Theta), new(6, U | W | Theta)], [new(3, -6e3)],
            lines: [new(0, 6, -1e3)], releases: Releases(1.5, 4.5)),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void EveryOutputIsEnclosedByItsVerifiedBoundAroundTheExactCompatibilitySolution(string name) =>
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(Case(name)));

    [Fact]
    public void AGerberBeamFollowsClosedFormStatics()
    {
        BendingResult result = SolverKernel.Solve(Case("gerber"));
        double Reaction(double x) => Assert.Single(result.Reactions, r => r.Position == x && r.Direction == W).Output.Value;

        Assert.Equal(5e3, Reaction(8), 1e-5);
        Assert.Equal(7.5e3, Reaction(4), 1e-5);
        Assert.Equal(-2.5e3, Reaction(0), 1e-5);
        Assert.Equal(-10e3, result.EvaluateAt(4, FieldSide.Right).Bending[2].Value, 1e-5);
        Assert.Equal(5e3, result.EvaluateAt(6, FieldSide.Left).Bending[3].Value, 1e-5);
        Assert.Equal(5e3, result.EvaluateAt(6.5, FieldSide.Interior).Bending[3].Value, 1e-5);
        Assert.Equal(-5e3, result.EvaluateAt(7.5, FieldSide.Interior).Bending[3].Value, 1e-5);
        Assert.Equal(5e3, result.EvaluateAt(6, FieldSide.Right).Bending[3].Value, 1e-5);
    }

    [Fact]
    public void TheMomentIsExactlyZeroOnBothSidesAndWIsSharedWhileTheRotationJumps()
    {
        foreach (string name in new[] { "gerber", "release-over-roller", "trapezoid-over-release", "force-on-release",
                     "release-with-section-change", "clamp-release-roller" })
        {
            MechanicalInput input = Case(name);
            BendingResult result = SolverKernel.Solve(input);
            foreach (InternalMomentRelease release in input.MomentReleases)
            {
                BendingStation station = Assert.Single(result.Stations, s => s.Position == release.Position);
                Assert.True(station.Jumps.HasFlag(BendingJump.Release), name);
                Assert.Equal(0d, station.Left!.Bending[2].Value);
                Assert.Equal(0d, station.Right!.Bending[2].Value);
                Assert.Equal(BitConverter.DoubleToInt64Bits(station.Left.Bending[0].Value),
                    BitConverter.DoubleToInt64Bits(station.Right.Bending[0].Value));
                Assert.NotEqual(station.Left.Bending[1].Value, station.Right.Bending[1].Value);
                // The bound of a prescribed zero stays a bound, never zero by construction.
                Assert.True(station.Left.Bending[2].VerifiedOutputBound >= 0);
                foreach (FieldSide side in new[] { FieldSide.Left, FieldSide.Right })
                    Assert.Equal(0d, result.EvaluateAt(release.Position, side).Bending[2].Value);
            }
        }
    }

    [Fact]
    public void OnlyReleaseStationsDropTheSharedRotation()
    {
        BendingResult result = SolverKernel.Solve(Case("gerber"));
        foreach (BendingStation station in result.Stations.Where(s => s.Left is not null && s.Right is not null))
        {
            bool sameTheta = BitConverter.DoubleToInt64Bits(station.Left!.Bending[1].Value) ==
                BitConverter.DoubleToInt64Bits(station.Right!.Bending[1].Value);
            Assert.Equal(!station.Jumps.HasFlag(BendingJump.Release), sameTheta);
        }
    }

    [Fact]
    public void AReleaseNeitherChangesShearNorDeflectionAndCarriesAPointForceAsAJump()
    {
        BendingResult plain = SolverKernel.Solve(Case("force-on-release"));
        BendingStation station = Assert.Single(plain.Stations, s => s.Position == 4.5);
        Assert.Equal(-3e3, station.Right!.Bending[3].Value - station.Left!.Bending[3].Value, 1e-6);
        BendingResult free = SolverKernel.Solve(Case("release-over-roller"));
        BendingStation roller = Assert.Single(free.Stations, s => s.Position == 2);
        double reaction = Assert.Single(free.Reactions, r => r.Position == 2 && r.Direction == W).Output.Value;
        Assert.Equal(reaction, roller.Right!.Bending[3].Value - roller.Left!.Bending[3].Value, 1e-6);
    }

    [Fact]
    public void AReleaseAtAZeroOfTheMomentChangesNothingAndTheRotationDoesNotJump()
    {
        // A beam clamped at 0 and propped at 4 under a uniform load q has M = 0 exactly at x = L/4 = 1. Releasing it
        // there keeps the statics intact: every field stays within the sum of the verified bounds.
        RigidRestraint[] supports = [new(0, U | W | Theta), new(4, W)];
        BendingResult plain = SolverKernel.Solve(Model(4, EI, supports, lines: [new(0, 4, -1e3)]));
        BendingResult released = SolverKernel.Solve(Model(4, EI, supports, lines: [new(0, 4, -1e3)], releases: Releases(1)));
        foreach (double x in new[] { 0, .25, 1, 1.5, 2.5, 4 })
            foreach (FieldSide side in new[] { FieldSide.Left, FieldSide.Right })
            {
                if (x == 0 && side == FieldSide.Left || x == 4 && side == FieldSide.Right) continue;
                BendingField a = plain.EvaluateAt(x, side), b = released.EvaluateAt(x, side);
                for (int q = 0; q < 4; q++)
                    Assert.True(Math.Abs(a.Bending[q].Value - b.Bending[q].Value) <=
                        a.Bending[q].VerifiedOutputBound + b.Bending[q].VerifiedOutputBound,
                        $"x {x} {side} component {q}: {a.Bending[q]} against {b.Bending[q]}");
            }
        BendingStation station = Assert.Single(released.Stations, s => s.Position == 1);
        Assert.True(Math.Abs(station.Left!.Bending[1].Value - station.Right!.Bending[1].Value) <=
            station.Left.Bending[1].VerifiedOutputBound + station.Right.Bending[1].VerifiedOutputBound);
        Assert.Equal(plain.Reactions.Length, released.Reactions.Length);
        foreach (BendingReaction reaction in plain.Reactions)
        {
            BendingReaction other = Assert.Single(released.Reactions, r => r.Position == reaction.Position && r.Direction == reaction.Direction);
            Assert.True(Math.Abs(reaction.Output.Value - other.Output.Value) <=
                reaction.Output.VerifiedOutputBound + other.Output.VerifiedOutputBound);
        }
    }

    public static TheoryData<string, double> Neighbours => new()
    {
        { "release-beside-roller", 1e-3 }, { "release-beside-roller", 1e-6 }, { "release-beside-roller", 6e-9 },
        { "release-beside-roller", 1e-11 }, { "release-bit-from-roller", 0 },
        { "force-beside-release", 1e-3 }, { "force-beside-release", 6e-9 }, { "force-beside-release", 1e-12 },
        { "release-beside-boundary", 1e-6 }, { "release-beside-boundary", 6e-9 }, { "two-releases-close", 1e-6 },
        { "two-releases-close", 6e-9 }, { "release-beside-spring", 1e-6 }, { "release-beside-spring", 6e-9 },
        { "line-load-end-beside-release", 6e-9 }
    };

    private static MechanicalInput Near(string name, double gap) => name switch
    {
        "release-beside-roller" => Model(2, EI, [new(0, U | W), new(1, W), new(2, W)], [new(1.5, -1000)], releases: Releases(1 + gap)),
        "release-bit-from-roller" => Model(2, EI, [new(0, U | W), new(1, W), new(2, W)], [new(1.5, -1000)],
            releases: Releases(Math.BitIncrement(1))),
        "force-beside-release" => Model(2, EI, [new(0, U | W | Theta), new(2, W)], [new(1 + gap, -1000)], releases: Releases(1)),
        "release-beside-boundary" => new(2, [new StiffnessSection(0, 1, EI), new StiffnessSection(1, 2, 2 * EI)],
            [new(0, U | W), new(1.5, W), new(2, W)], [new(1.75, -1000)], [], [], null, Releases(1 + gap)),
        "two-releases-close" => Model(3, EI, [new(0, U | W | Theta), new(3, U | W | Theta)], [new(1.5, -1000)],
            releases: Releases(1, 1 + gap)),
        "release-beside-spring" => Model(2, EI, [new(0, U | W), new(2, W)], [new(.5, -1000)],
            elastic: [new(1, 4.48e8, 0, false)], releases: Releases(1 + gap)),
        "line-load-end-beside-release" => Model(2, EI, [new(0, U | W), new(1.5, W), new(2, W)],
            lines: [new(.25, 1 + gap, -1000)], releases: Releases(1)),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Neighbours))]
    public void CloseNeighboursOfAReleaseStayExactWithinTheirVerifiedBounds(string name, double gap) =>
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(Near(name, gap)));

    [Fact]
    public void AMomentLeftOfAReleaseOutsideItsBoundAroundZeroIsAnInternalInconsistency()
    {
        BendingResult result = SolverKernel.Solve(Case("gerber"));
        BendingFields fields = result.Fields;
        // The release is the station at 6; its left piece starts at the roller at 4. Shifting the moment state there
        // by far more than its bound makes the computed left moment inconsistent with the exact zero.
        int roller = Array.FindIndex(fields.Stations.ToArray(), s => s.Position == 4);
        int index = 4 * roller + 2;
        BendingFields tampered = fields with { State = fields.State.SetItem(index, fields.State[index] + 1) };
        KernelFailureException failure = Assert.Throws<KernelFailureException>(() =>
            tampered.EvaluateNormalized(6, FieldSide.Left));
        Assert.Equal(KernelFailureCause.InternalInconsistency, failure.Cause);
        // Everywhere else the tampered state is still evaluated: only the exact zero is guarded.
        tampered.EvaluateNormalized(5, FieldSide.Interior);
        tampered.EvaluateNormalized(6, FieldSide.Right);
    }

    [Fact]
    public void TheMomentLeftOfAReleaseIsTheExactZeroEvenWhenTheComputedValueIsNotZero()
    {
        BendingResult result = SolverKernel.Solve(Case("gerber"));
        BendingFields fields = result.Fields;
        int roller = Array.FindIndex(fields.Stations.ToArray(), s => s.Position == 4);
        int index = 4 * roller + 2;
        NormalizedBendingField computed = fields.EvaluateNormalized(6, FieldSide.Left);
        // Shift the moment state of the left piece by half the bound: the computed left moment is then clearly not zero,
        // but still inside its bound around the exact zero, which is what the output must be.
        BendingFields shifted = fields with { State = fields.State.SetItem(index, fields.State[index] + computed.Bounds[2] / 2) };
        NormalizedBendingField output = shifted.EvaluateNormalized(6, FieldSide.Left);
        Assert.Equal(0d, output.Values[2]);
        Assert.True(output.Bounds[2] > 0);
        // The shift reaches the moment only through the transfer; without the exact zero the value would show it.
        Assert.NotEqual(0d, shifted.EvaluateNormalized(5, FieldSide.Interior).Values[2]);
    }
}
