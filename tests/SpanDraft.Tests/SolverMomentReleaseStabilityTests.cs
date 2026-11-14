using SpanDraft.Solver.Kernel;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

/// <summary>Station rules and the exact body-rank mechanism check of internal moment releases.</summary>
public sealed class SolverMomentReleaseStabilityTests
{
    private const double Length = 4;
    private static readonly RestrainedDirections Clamp = U | W | Theta;

    private static MechanicalInput Input(RigidRestraint[] restraints, double[] releases, ElasticRestraint[]? elastic = null,
        AppliedPointMoment[]? moments = null, StiffnessSection[]? sections = null, double length = Length) =>
        sections is null
            ? new(length, 1, restraints, [], moments ?? [], [], elastic, releases.Select(x => new InternalMomentRelease(x)))
            : new(length, sections, restraints, [], moments ?? [], [], elastic, releases.Select(x => new InternalMomentRelease(x)));

    private static MechanicalStability Check(MechanicalInput input) => MechanicalStability.Check(StationModel.Create(input));

    public static TheoryData<string, int, int, bool> Mechanisms => new()
    {
        // name, rank, modes, stable (axial restraint is present in every case)
        { "pinned-roller one release is a mechanism", 3, 4, false },
        { "pinned-roller-roller one release is determinate", 4, 4, true },
        { "pinned-roller-roller two releases are a mechanism", 5, 6, false },
        { "cantilever with a release", 3, 4, false },
        { "release on a roller", 4, 4, true },
        { "release on a pinned support", 4, 4, true },
        { "translational spring on the release", 4, 4, true },
        { "free part left of the release", 3, 4, false },
        { "two clamps with two releases", 6, 6, true },
        { "release beside the support by one bit", 4, 4, true },
        { "release on a section boundary", 4, 4, true },
        { "all in one: clamp, roller, release and a spring", 6, 6, true },
    };

    private static MechanicalInput Case(string name) => name switch
    {
        "pinned-roller one release is a mechanism" => Input([new(0, U | W), new(4, W)], [2]),
        "pinned-roller-roller one release is determinate" => Input([new(0, U | W), new(2, W), new(4, W)], [1]),
        "pinned-roller-roller two releases are a mechanism" => Input([new(0, U | W), new(2, W), new(4, W)], [1, 3]),
        "cantilever with a release" => Input([new(0, Clamp)], [1]),
        "release on a roller" => Input([new(0, U | W), new(2, W), new(4, W)], [2]),
        "release on a pinned support" => Input([new(0, U | W), new(2, U | W), new(4, W)], [2]),
        "translational spring on the release" => Input([new(0, U | W), new(4, W)], [2], [new(2, 5, 0, false)]),
        "free part left of the release" => Input([new(4, Clamp)], [2]),
        "two clamps with two releases" => Input([new(0, Clamp), new(4, Clamp)], [1, 3]),
        "release beside the support by one bit" => Input([new(0, U | W), new(2, W), new(4, W)], [Math.BitIncrement(2)]),
        "release on a section boundary" => Input([new(0, U | W), new(2, W), new(4, W)], [1],
            sections: [new(0, 1, 1), new(1, 4, 2)]),
        "all in one: clamp, roller, release and a spring" => Input([new(0, Clamp), new(2, W)], [1, 3],
            [new(4, 7, 0, false)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Mechanisms))]
    public void TheBodyRankDecidesExactlyWhetherAMechanismRemains(string name, int rank, int modes, bool stable)
    {
        MechanicalInput input = Case(name);
        MechanicalStability stability = Check(input);
        Assert.Equal(rank, stability.BendingRank);
        Assert.Equal(modes, stability.BendingModes);
        Assert.True(stability.AxiallyRestrained);
        Assert.Equal(stable, stability.IsStable);
    }

    [Fact]
    public void AReleaseNeverChangesTheAxialMode()
    {
        MechanicalStability withoutAxial = Check(Input([new(0, W), new(2, W), new(4, W)], [1]));
        Assert.False(withoutAxial.AxiallyRestrained);
        Assert.False(withoutAxial.IsStable);
        Assert.True(Check(Input([new(0, U | W), new(4, W)], [])).IsStable);
    }

    [Fact]
    public void NoReleaseKeepsTheTwoRigidBodyModesOfASingleBody()
    {
        Assert.Equal(new MechanicalStability(2, true, 2), Check(Input([new(0, U | W), new(4, W)], [])));
        Assert.Equal(new MechanicalStability(2, true), Check(Input([new(0, Clamp)], [])));
    }

    [Fact]
    public void ARankDeficientConfigurationWithExactNeighbourPositionsStaysExact()
    {
        double h = Math.BitIncrement(2);
        // Two roller bodies meeting at h with an almost parallel pair of rows: no tolerance may merge them.
        MechanicalInput input = Input([new(0, U | W), new(2, W), new(h, W), new(4, W)], [h]);
        Assert.Equal(new MechanicalStability(4, true, 4), Check(input));
        // The release one bit before the roller at the end is determinate; only the numerical gate may object.
        MechanicalInput near = Input([new(0, U | W), new(2, W), new(4, W)], [Math.BitDecrement(4)]);
        Assert.Equal(new MechanicalStability(4, true, 4), Check(near));
        // Without the middle roller the same release is a mechanism, however close it is to the end.
        MechanicalInput open = Input([new(0, U | W), new(4, W)], [Math.BitDecrement(4)]);
        Assert.Equal(new MechanicalStability(3, true, 4), Check(open));
    }

    [Fact]
    public void AMechanismIsReportedBeforeAnyNumericalRange()
    {
        MechanicalInput input = new(Math.ScaleB(1, -900), double.Epsilon, [new(0, U | W)], [new(0, double.Epsilon)], [], [],
            null, [new InternalMomentRelease(Math.ScaleB(1, -901))]);
        MechanicalMechanismException exception = Assert.Throws<MechanicalMechanismException>(() => SolverKernel.BuildSystem(input));
        Assert.False(exception.Stability.IsStable);
        Assert.Equal(4, exception.Stability.BendingModes);
    }

    [Fact]
    public void TheStaticMechanismCheckAcceptsReleasesAndUsesTheGivenLength()
    {
        SolverKernel.RequireStable([new(0, U | W), new(30, W), new(60, W)], null, [new InternalMomentRelease(10)], 60);
        Assert.Throws<MechanicalMechanismException>(() => SolverKernel.RequireStable([new(0, U | W), new(30, W), new(60, W)],
            null, [new InternalMomentRelease(10), new InternalMomentRelease(50)], 60));
        Assert.Throws<InvalidMechanicalModelException>(() => SolverKernel.RequireStable([new(0, U | W), new(30, W)], null,
            [new InternalMomentRelease(10)], 5));
    }

    [Fact]
    public void AReleaseIsAMechanicalStationOfItsOwnAndMergesWithSupportsSpringsAndBoundaries()
    {
        StationModel own = StationModel.Create(Input([new(0, U | W), new(4, W)], [1.5]));
        Assert.Equal([0d, 1.5, 4], own.MechanicalStations.Select(s => s.Position));
        Assert.Equal([false, true, false], own.MechanicalStations.Select(s => s.Release));
        Assert.Equal([0d, 1.5, 4], own.EventStations.Select(s => s.Position));
        Assert.All(own.EventStations, e => Assert.NotNull(e.MechanicalStation));
        Assert.True(own.EventStations[1].MechanicalStation!.Value.Release);

        StationModel onSupport = StationModel.Create(Input([new(0, U | W), new(2, W), new(4, W)], [2]));
        Assert.Equal(3, onSupport.MechanicalStations.Length);
        Assert.Equal(new MechanicalStation(2, W, default, true), onSupport.MechanicalStations[1]);

        StationModel onSpring = StationModel.Create(Input([new(0, U | W), new(4, W)], [2], [new(2, 5, 0, false)]));
        Assert.Equal(new MechanicalStation(2, U, new SpringStiffnesses(5, 0), true), onSpring.MechanicalStations[1]);

        StationModel onBoundary = StationModel.Create(Input([new(0, U | W), new(4, W)], [1],
            sections: [new(0, 1, 1), new(1, 4, 2)]));
        Assert.Equal([0d, 1, 4], onBoundary.MechanicalStations.Select(s => s.Position));
        Assert.Equal([0, 1], onBoundary.IntervalSections);
        Assert.True(onBoundary.MechanicalStations[1].Release);
    }

    [Fact]
    public void AReleaseInsideASectionKeepsTheSectionAssignmentOfBothIntervals()
    {
        StationModel model = StationModel.Create(Input([new(0, U | W), new(4, W)], [2],
            sections: [new(0, 1, 1), new(1, 4, 2)]));
        Assert.Equal([0d, 1, 2, 4], model.MechanicalStations.Select(s => s.Position));
        Assert.Equal([0, 1, 1], model.IntervalSections);
    }

    public static TheoryData<string> Invalid => new()
    {
        "at the beam start", "at the beam end", "beyond the beam end", "negative zero", "not a number",
        "twice at one position", "on an applied moment", "on a zero valued applied moment", "on a clamp",
        "on a rotational restraint", "on a rotational spring", "on a spring with a rigid translation",
        "on a spring with both stiffnesses"
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void ReleaseCoincidencesAndPositionsOutsideTheInteriorAreRejectedByTheKernel(string name)
    {
        RigidRestraint[] pins = [new(0, U | W), new(4, W)];
        MechanicalInput input = name switch
        {
            "at the beam start" => Input(pins, [0]),
            "at the beam end" => Input(pins, [4]),
            "beyond the beam end" => Input(pins, [Math.BitIncrement(4)]),
            "negative zero" => Input(pins, [-0d]),
            "not a number" => Input(pins, [double.NaN]),
            "twice at one position" => Input(pins, [1, 1]),
            "on an applied moment" => Input(pins, [1], moments: [new(1, 2)]),
            "on a zero valued applied moment" => Input(pins, [1], moments: [new(1, 0)]),
            "on a clamp" => Input([new(0, U | W), new(1, Clamp), new(4, W)], [1]),
            "on a rotational restraint" => Input([new(0, U | W), new(1, Theta), new(4, W)], [1]),
            "on a rotational spring" => Input(pins, [1], [new(1, 0, 5, false)]),
            "on a spring with a rigid translation" => Input(pins, [1], [new(1, 0, 5, true)]),
            "on a spring with both stiffnesses" => Input(pins, [1], [new(1, 3, 5, false)]),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        Assert.Throws<InvalidMechanicalModelException>(() => StationModel.Create(input));
        Assert.Throws<InvalidMechanicalModelException>(() => SolverKernel.BuildSystem(input));
    }

    [Fact]
    public void ReleasesAtDistinctNeighbourPositionsAreBothKept()
    {
        StationModel model = StationModel.Create(Input([new(0, U | W), new(2, W), new(4, W)], [1, Math.BitIncrement(1)]));
        Assert.Equal(2, model.MechanicalStations.Count(s => s.Release));
        Assert.Equal(5, model.MechanicalStations.Length);
    }

    [Fact]
    public void ReleasesAreStoredInAscendingOrderWhateverTheirInputOrder()
    {
        MechanicalInput input = Input([new(0, U | W), new(4, W)], [3, 1, 2]);
        Assert.Equal([1d, 2, 3], input.MomentReleases.Select(r => r.Position));
    }

    // Differential test: the kernel's exact rank against the independent rational Gauss elimination of the frozen reference.
    [Fact]
    public void TheBodyRankAgreesWithTheIndependentRationalEliminationOnRandomConfigurations()
    {
        var random = new Random(20261114);
        int mechanisms = 0, stables = 0;
        for (int trial = 0; trial < 4000; trial++)
        {
            bool grid = trial % 4 != 3;
            double Position() => grid ? random.Next(0, 17) / 4d : random.NextDouble() * 4;
            var taken = new HashSet<double>();
            var rigid = new List<RigidRestraint>();
            var elastic = new List<ElasticRestraint>();
            var reference = new List<(double X, bool Rotation)>();
            var clampOrTheta = new HashSet<double>();
            for (int i = random.Next(1, 5); i > 0; i--)
            {
                double x = Position();
                switch (random.Next(0, 6))
                {
                    case 0:
                        rigid.Add(new(x, Clamp));
                        reference.Add((x, false));
                        reference.Add((x, true));
                        clampOrTheta.Add(x);
                        break;
                    case 1:
                        rigid.Add(new(x, U | W));
                        reference.Add((x, false));
                        break;
                    case 2:
                        rigid.Add(new(x, W));
                        reference.Add((x, false));
                        break;
                    default:
                        if (!taken.Add(x)) continue;
                        // A spring shares its position with no other support; a rigid one placed later at x is skipped below.
                        int kind = random.Next(0, 4);
                        double kw = kind is 0 or 2 ? 3.5 : 0, kt = kind is 1 or 2 ? 2.5 : 0;
                        bool rigidW = kind == 3;
                        elastic.Add(new(x, rigidW ? 0 : kw, rigidW ? 2.5 : kt, rigidW));
                        if (kw != 0 || rigidW) reference.Add((x, false));
                        if (kt != 0 || rigidW) { reference.Add((x, true)); clampOrTheta.Add(x); }
                        break;
                }
            }
            // Springs must not coincide with rigid supports and no support may be missing for the axial mode.
            if (elastic.Any(e => rigid.Any(r => r.Position == e.Position))) continue;
            if (elastic.GroupBy(e => e.Position).Any(g => g.Count() > 1)) continue;
            var releases = new List<double>();
            for (int i = random.Next(0, 4); i > 0; i--)
            {
                double h = random.Next(0, 3) == 0 && rigid.Count > 0 ? rigid[random.Next(rigid.Count)].Position : Position();
                if (h <= 0 || h >= Length || clampOrTheta.Contains(h) || releases.Contains(h)) continue;
                releases.Add(h);
            }

            // A spring is axially rigid; a rigid restraint at the beam start must not coincide with one.
            IEnumerable<RigidRestraint> axial = elastic.Any(e => e.Position == 0) ? [] : [new(0, U)];
            MechanicalInput input = new(Length, 1, rigid.Concat(axial), [], [], [], elastic,
                releases.Select(h => new InternalMomentRelease(h)));
            MechanicalStability stability = Check(input);
            bool expected = DofMap.IsKinematicallyStable(reference, releases, Length);
            Assert.True(expected == (stability.BendingRank == stability.BendingModes),
                $"Seed trial {trial}: rigid [{string.Join(", ", rigid)}], elastic [{string.Join(", ", elastic)}], releases [{string.Join(", ", releases)}], " +
                $"kernel {stability}, reference stable {expected}");
            Assert.Equal(2 * (releases.Count + 1), stability.BendingModes);
            if (expected) stables++; else mechanisms++;
        }
        // The sample must contain both outcomes, otherwise the comparison proves little.
        Assert.True(stables > 200 && mechanisms > 200, $"{stables} stable and {mechanisms} mechanism cases");
    }
}
