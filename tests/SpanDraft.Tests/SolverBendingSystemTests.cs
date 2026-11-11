using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverBendingSystemTests
{
    public static TheoryData<string> SystemCases => new()
    {
        "unloaded", "end-force", "point-moment", "udl", "pinned-roller", "indeterminate",
        "coincidence", "support-load", "force-and-moment", "event-load", "multi-section-udl",
        "short-udl", "ulp-stations", "signed-zero", "right-fixed", "interior-fixed"
    };

    private static MechanicalInput Case(string name) => name switch
    {
        "unloaded" => Model(),
        "end-force" => Model(forces: [new(2, -7)]),
        "point-moment" => Model(moments: [new(0.75, 11)]),
        "udl" => Model(lines: [new(0, 2, -3)]),
        "pinned-roller" => Model(restraints: [new(0, U | W), new(2, W)], forces: [new(1, -3)]),
        "indeterminate" => Model(restraints: [new(0, U | W), new(1, W), new(2, W)],
            forces: [new(0.5, -3), new(1.5, 5)]),
        "coincidence" => Model(restraints: [new(1, U | W | Theta), new(1, U | W), new(1, W)],
            lines: [new(0, 2, 3)]),
        "support-load" => Model(restraints: [new(0, U | W), new(1, W), new(2, W)], forces: [new(1, -3)]),
        "force-and-moment" => Model(restraints: [new(0, U | W | Theta), new(1, W)],
            forces: [new(1, 3), new(1, -7)], moments: [new(1, 11), new(1, -2)]),
        "event-load" => Model(forces: [new(0.75, -3)], moments: [new(1.25, 7)]),
        "multi-section-udl" => Model(restraints: [new(0, U | W), new(0.5, W), new(1.5, W), new(2, W)],
            lines: [new(0.25, 1.75, -3), new(0, 1.25, 5), new(1.25, 2, 2)]),
        "short-udl" => Model(lines: [new(1, Math.BitIncrement(1), Math.ScaleB(1, 52))]),
        "ulp-stations" => Model(restraints: [new(1, U | W), new(Math.BitIncrement(1), W)],
            lines: [new(0.25, 1.75, 3)]),
        "signed-zero" => Model(forces: [new(-0d, -0d), new(1, 0)], moments: [new(2, -0d)],
            lines: [new(-0d, 2, -0d)]),
        "right-fixed" => Model(restraints: [new(2, U | W | Theta)], forces: [new(0, 3)], moments: [new(0, 7)]),
        "interior-fixed" => Model(restraints: [new(1, U | W | Theta)], forces: [new(0, 3), new(2, -7)],
            moments: [new(0, 11), new(2, -2)], lines: [new(0.25, 1.75, 5)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(SystemCases))]
    public void ReducedSystemMatchesExactMechanicsAndHasLocalBand(string name)
    {
        MechanicalInput input = Case(name);
        BendingSystem system = SolverKernel.BuildSystem(input);
        CheckStructure(system);
        CheckExactMechanics(input, system);
        if (system.IsUnloaded)
        {
            Assert.Equal(0, system.EF);
            Assert.Equal(default, system.Excitation);
            Assert.All(system.B, v => Assert.Equal(0d, v));
            Assert.All(system.BB, v => Assert.Equal(0d, v));
        }
    }

    [Fact]
    public void SmallCantileverHasFixedMatrixRoundingCountsAndBoundaryRhs()
    {
        double[] expectedA = [-0.5, -1d / 6, 1, 0, -1, -0.5, 0, 1, -1, -1, 0, 0, 0, -1, 0, 0];
        long[] expectedK = [5, 8, 0, 0, 3, 5, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0];
        MechanicalInput[] inputs = [Model(length: 1), Model(length: 1, forces: [new(1, 1)]),
            Model(length: 1, moments: [new(1, 1)]), Model(length: 1, lines: [new(0, 1, 1)])];
        double[][] expectedB = [[0, 0, 0, 0], [0, 0, 0, 1], [0, 0, -1, 0], [1d / 24, 1d / 6, 0.5, 1]];
        // Frozen FPC Difference-only operations, with EI data rounding; no artificial zero-ramp term.
        ulong[] udlBounds = [0x3c8d555555555580, 0x3ca800000000001d, 0x3cb4000000000010, 0x3cb800000000000d];
        for (int i = 0; i < inputs.Length; i++)
        {
            BendingSystem system = SolverKernel.BuildSystem(inputs[i]);
            AssertBits(expectedA, system.A);
            Assert.Equal(expectedK, system.K);
            Assert.Equal(expectedB[i], system.B);
            Assert.Equal(i == 3 ? udlBounds : new ulong[4], system.BB.Select(BitConverter.DoubleToUInt64Bits));
        }
    }

    [Fact]
    public void AllLoadKindsLeaveMechanicsMatrixAndRoundingCountsBitIdentical()
    {
        foreach (RigidRestraint[] restraints in new[]
        {
            new RigidRestraint[] { new(0, U | W | Theta) },
            [new(0, U | W), new(1, W), new(2, W)],
            [new(1, U | W | Theta), new(1, U | W), new(1, W)]
        })
        {
            MechanicalInput[] inputs =
            [
                Model(restraints: restraints),
                Model(restraints: restraints, forces: [new(0.75, -3)]),
                Model(restraints: restraints, moments: [new(1.25, 11)]),
                Model(restraints: restraints, lines: [new(0.25, 1.75, -7)]),
                Model(restraints: restraints, forces: [new(1, 13)], moments: [new(1, -17)]),
                Model(restraints: restraints, forces: [new(Math.BitIncrement(1), -0d)],
                    moments: [new(0.3, 0)], lines: [new(0.1, 1.9, -0d)]),
                Model(restraints: restraints, forces: [new(0, 23), new(2, -29), new(0.625, 31)],
                    moments: [new(0, -37), new(1, 41)], lines: [new(0, 2, -43)])
            ];
            BendingSystem baseline = SolverKernel.BuildSystem(inputs[0]);
            foreach (MechanicalInput input in inputs.Skip(1))
            {
                BendingSystem loaded = SolverKernel.BuildSystem(input);
                Assert.Equal(baseline.Stations, loaded.Stations);
                Assert.Equal(baseline.FreeComponents, loaded.FreeComponents);
                Assert.Equal(baseline.ReducedColumns, loaded.ReducedColumns);
                AssertBits(baseline.A, loaded.A);
                Assert.Equal(baseline.K, loaded.K);
                Assert.Equal(baseline.EL, loaded.EL);
                Assert.Equal(baseline.EEI, loaded.EEI);
                CheckStructure(loaded);
            }
        }
    }

    [Fact]
    public void CanonicalPermutationsPreserveFullSystemIncludingAccumulatedBounds()
    {
        RigidRestraint[] restraints = [new(0, U | W | Theta), new(1, U | W), new(1, W), new(2, W)];
        TransversePointForce[] forces = [new(1, 1), new(1, -1), new(1, Math.ScaleB(1, -52)), new(0.75, -3), new(0.25, 7)];
        AppliedPointMoment[] moments = [new(1, -7), new(1, 3), new(0.5, 11), new(1.5, -2)];
        ConstantLineLoad[] lines = [new(0, 2, 1), new(0.25, 1.75, -5), new(0, 1, 7), new(0, 2, -1)];
        BendingSystem a = SolverKernel.BuildSystem(Model(restraints: restraints, forces: forces, moments: moments, lines: lines));
        BendingSystem b = SolverKernel.BuildSystem(Model(restraints: restraints.Reverse().ToArray(),
            forces: forces.Reverse().ToArray(), moments: moments.Reverse().ToArray(), lines: lines.Reverse().ToArray()));
        AssertBits(a.A, b.A);
        Assert.Equal(a.K, b.K);
        AssertBits(a.B, b.B);
        AssertBits(a.BB, b.BB);
        Assert.Equal(a.Excitation, b.Excitation);
    }

    [Fact]
    public void CoincidentRigidSupportsEliminateEachDirectionExactlyOnce()
    {
        BendingSystem single = SolverKernel.BuildSystem(Model(restraints: [new(1, U | W | Theta)]));
        BendingSystem combined = SolverKernel.BuildSystem(Case("coincidence"));
        Assert.Equal(single.Stations, combined.Stations);
        Assert.Equal(3, combined.Stations.Length);
        Assert.Equal(single.FreeComponents, combined.FreeComponents);
        Assert.Equal(single.ReducedColumns, combined.ReducedColumns);
        Assert.Equal(8, combined.Dimension);
        AssertBits(single.A, combined.A);
        Assert.Equal(single.K, combined.K);
        Assert.Equal(-1, combined.ReducedColumns[4]);
        Assert.Equal(-1, combined.ReducedColumns[5]);
        Assert.True(combined.ReducedColumns[6] >= 0);
        Assert.True(combined.ReducedColumns[7] >= 0);
    }

    [Fact]
    public void SupportedPointForceDoesNotEnterEquationsButUnsupportedCoupleDoes()
    {
        RigidRestraint[] supports = [new(0, U | W), new(1, W), new(2, W)];
        BendingSystem force = SolverKernel.BuildSystem(Model(restraints: supports, forces: [new(1, 3)]));
        Assert.All(force.B, value => Assert.Equal(0d, value));
        Assert.All(force.BB, value => Assert.Equal(0d, value));
        BendingSystem couple = SolverKernel.BuildSystem(Model(restraints: supports, moments: [new(1, 3)]));
        Assert.Single(couple.B, value => value != 0);
        Assert.Contains(couple.B, value => value < 0);
    }

    [Fact]
    public void ConstantLoadsAreClippedAtGlobalMechanicalBoundariesInCanonicalOrder()
    {
        MechanicalInput input = Case("multi-section-udl");
        StationModel model = StationModel.Create(input);
        BendingPreparation preparation = BendingPreparation.Create(input, model);
        PreparedBendingLoads loads = PreparedBendingLoads.Create(input, model, preparation);
        for (int j = 0; j < loads.Segments.Length; j++)
        {
            double start = model.MechanicalStations[j].Position, end = model.MechanicalStations[j + 1].Position;
            Assert.Equal(input.LineLoads.Where(q => q.Start < end && q.End > start).Select(q =>
                (Math.Max(start, q.Start), Math.Min(end, q.End))),
                loads.Segments[j].Lines.Select(q => (q.Start, q.End)));
            for (int p = 1; p <= 4; p++)
                Encloses(loads.Segments[j].Integral(end, p), SolverBendingNumericsTests.ExactIntegral(
                    input, preparation, start, end, end, p));
        }
    }

    [Fact]
    public void EveryRigidSupportCombinationPreservesEquationCountEliminationAndBand()
    {
        double[] positions = [0, 1, Math.BitIncrement(1), 2];
        RestrainedDirections[] types = [None, W, U | W, U | W | Theta];
        for (int combination = 0; combination < 256; combination++)
        {
            var supports = new List<RigidRestraint>();
            for (int i = 0; i < positions.Length; i++)
            {
                RestrainedDirections type = types[(combination >> (2 * i)) & 3];
                if (type != None)
                    supports.Add(new(positions[i], type));
            }
            MechanicalInput input = Model(ei: 1.25, restraints: supports.ToArray(), forces: [new(0.75, -3)],
                moments: [new(1, 7)], lines: [new(0.25, 1.75, -5)]);
            bool stable = supports.Any(s => s.Directions.HasFlag(Theta)) ||
                supports.Count >= 2 && supports.Any(s => s.Directions.HasFlag(U));
            if (!stable)
            {
                Assert.Throws<MechanicalMechanismException>(() => SolverKernel.BuildSystem(input));
                continue;
            }
            BendingSystem system = SolverKernel.BuildSystem(input);
            CheckStructure(system);
            CheckExactMechanics(input, system);
        }
    }

    private static void CheckStructure(BendingSystem system)
    {
        var expected = new List<FreeBendingComponent>();
        for (int i = 0; i < system.Stations.Length; i++)
        {
            RestrainedDirections directions = system.Stations[i].Directions;
            if (!directions.HasFlag(W)) expected.Add(new(i, BendingComponent.W));
            if (!directions.HasFlag(Theta)) expected.Add(new(i, BendingComponent.Theta));
            if (i < system.Stations.Length - 1)
            {
                expected.Add(new(i, BendingComponent.M));
                expected.Add(new(i, BendingComponent.V));
            }
        }
        Assert.Equal(expected, system.FreeComponents);
        Assert.Equal(expected.Count, system.Dimension);
        Assert.Equal(expected.Count * expected.Count, system.A.Length);
        Assert.Equal(system.A.Length, system.K.Length);
        Assert.Equal(expected.Count, system.B.Length);
        Assert.Equal(expected.Count, system.BB.Length);
        for (int full = 0; full < system.ReducedColumns.Length; full++)
            Assert.Equal(expected.IndexOf(new(full / 4, (BendingComponent)(full % 4))), system.ReducedColumns[full]);
        for (int row = 0; row < system.Dimension; row++)
            for (int col = 0; col < system.Dimension; col++)
                if (system.A[row * system.Dimension + col] != 0)
                    Assert.True(Math.Abs(row - col) <= 2, $"Entry ({row}, {col}) is outside band (2,2).");
    }

    private static void CheckExactMechanics(MechanicalInput input, BendingSystem system)
    {
        BendingPreparation units = Prepare(input);
        int row = 0;
        for (int i = 0; i < system.Stations.Length; i++)
            for (int q = 0; q < 4; q++)
            {
                RestrainedDirections directions = system.Stations[i].Directions;
                if (i == 0 && q < 2 || q == 2 && directions.HasFlag(Theta) || q == 3 && directions.HasFlag(W))
                    continue;
                double x = system.Stations[i].Position;
                SolverExactNumber a = i == 0 ? Integer(0) :
                    (From(x) - From(system.Stations[i - 1].Position)) * PowerOfTwo(-system.EL);
                SolverExactNumber e = From(input.EI) * PowerOfTwo(-system.EEI);
                for (int col = 0; col < system.Dimension; col++)
                {
                    FreeBendingComponent component = system.FreeComponents[col];
                    SolverExactNumber coefficient = Integer(component.StationIndex == i && (int)component.Component == q ? 1 : 0);
                    long count = 0;
                    if (i > 0 && component.StationIndex == i - 1 && (int)component.Component >= q)
                    {
                        int order = (int)component.Component - q;
                        SolverExactNumber transfer = Pow(a, order) / Integer(Factorial(order));
                        if (q < 2 && component.Component >= BendingComponent.M)
                            transfer /= e;
                        coefficient -= transfer;
                        count = (q, (int)component.Component) switch
                        {
                            (0, 1) or (2, 3) => 1,
                            (1, 2) => 3,
                            (0, 2) or (1, 3) => 5,
                            (0, 3) => 8,
                            _ => 0
                        };
                    }
                    int index = row * system.Dimension + col;
                    Assert.Equal(count, system.K[index]);
                    // Independent gamma expression bounds the stored matrix entry.
                    SolverExactNumber gamma = Integer(count) / (PowerOfTwo(53) - Integer(count));
                    SolverExactNumber allowed = gamma / (Integer(1) - gamma) * From(system.A[index]).Abs();
                    Assert.True((From(system.A[index]) - coefficient).Abs().CompareTo(allowed) <= 0,
                        $"Matrix entry ({row},{col}) disagrees with exact mechanics.");
                }
                SolverExactNumber rhs = i == 0 ? Integer(0) : SolverBendingNumericsTests.ExactIntegral(input, units,
                    system.Stations[i - 1].Position, x, x, 4 - q);
                if (q < 2) rhs /= e;
                if (q == 2)
                    foreach (AppliedPointMoment moment in input.PointMoments.Where(m => m.Position == x))
                        rhs -= From(moment.NewtonMeters) * PowerOfTwo(system.MomentExponent);
                if (q == 3)
                    foreach (TransversePointForce force in input.PointForces.Where(f => f.Position == x))
                        rhs += From(force.Newtons) * PowerOfTwo(system.ForceExponent);
                Assert.True((From(system.B[row]) - rhs).Abs().CompareTo(From(system.BB[row])) <= 0,
                    $"RHS row {row} disagrees with exact mechanics.");
                row++;
            }
        Assert.Equal(system.Dimension, row);
    }

    private static void AssertBits(IEnumerable<double> expected, IEnumerable<double> actual) =>
        Assert.Equal(expected.Select(BitConverter.DoubleToUInt64Bits), actual.Select(BitConverter.DoubleToUInt64Bits));
}
