using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverTestSupport;
using static System.BitConverter;

namespace SpanDraft.Tests;

public sealed class SolverMechanicalInputTests
{
    [Fact]
    public void AdaptsSelectedBendingAxisAndBuildsStationsForAllLoadTypes()
    {
        var section = new ManualSectionDefinition(Area.FromSquareMeters(1),
            new ManualSectionAxis(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(0.125),
                SectionModulus.FromCubicMeters(1)),
            new ManualSectionAxis(SectionAxisDesignation.Z, SecondMomentOfArea.FromMetersToTheFourth(0.5),
                SectionModulus.FromCubicMeters(1)));
        var material = new Material("Test material", Pressure.FromPascals(8), Pressure.FromPascals(1),
            MaterialStrengthType.Re);
        var beam = new BeamModel(M(L), material, section, SectionAxisDesignation.Z,
            [SupportAt(1, SupportType.Fixed)], [Point(1, -7), Couple(1.25, 11), Uniform(0.25, 1.75, -13)]);

        MechanicalInput input = BeamModelAdapter.Adapt(beam);
        Assert.Equal(L, input.Length);
        Assert.Equal(4d, input.EI);
        Assert.Equal(new RigidRestraint(1, U | W | Theta), Assert.Single(input.Restraints));
        var force = new TransversePointForce(1, -7);
        var moment = new AppliedPointMoment(1.25, 11);
        var lineLoad = new ConstantLineLoad(0.25, 1.75, -13);
        Assert.Equal(force, Assert.Single(input.PointForces));
        Assert.Equal(moment, Assert.Single(input.PointMoments));
        Assert.Equal(lineLoad, Assert.Single(input.LineLoads));

        StationModel stations = StationModel.Create(input);
        var supportStation = new MechanicalStation(1, U | W | Theta);
        Assert.Equal(new[] { new MechanicalStation(0, None), supportStation, new MechanicalStation(L, None) },
            stations.MechanicalStations.AsEnumerable());
        Assert.Equal(new[] { 0d, 0.25, 1, 1.25, 1.75, L }, stations.EventStations.Select(s => s.Position));
        Assert.Equal(stations.MechanicalStations[0], stations.EventStations[0].MechanicalStation);
        Assert.Equal(lineLoad, Assert.Single(stations.EventStations[1].LineLoadStarts));
        Assert.Equal(supportStation, stations.EventStations[2].MechanicalStation);
        Assert.Equal(force, Assert.Single(stations.EventStations[2].PointForces));
        Assert.Null(stations.EventStations[3].MechanicalStation);
        Assert.Equal(moment, Assert.Single(stations.EventStations[3].PointMoments));
        Assert.Equal(lineLoad, Assert.Single(stations.EventStations[4].LineLoadEnds));
        Assert.Equal(stations.MechanicalStations[^1], stations.EventStations[^1].MechanicalStation);
    }

    [Theory]
    [InlineData(SupportType.Fixed, (int)(U | W | Theta))]
    [InlineData(SupportType.Pinned, (int)(U | W))]
    [InlineData(SupportType.Roller, (int)W)]
    public void MapsEachSupportToItsRestrainedDirections(SupportType type, int directions)
    {
        MechanicalInput input = BeamModelAdapter.Adapt(Beam([SupportAt(0, type)]));
        Assert.Equal(new RigidRestraint(0, (RestrainedDirections)directions), Assert.Single(input.Restraints));
    }

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    public void CoincidentRigidRestraintsAreCombinedOnlyInStationModel(SupportType strongest)
    {
        Support[] supports = strongest == SupportType.Fixed
            ? [SupportAt(1, SupportType.Roller), SupportAt(1, SupportType.Fixed), SupportAt(1, SupportType.Pinned)]
            : [SupportAt(1, SupportType.Roller), SupportAt(1, SupportType.Pinned)];
        MechanicalInput input = BeamModelAdapter.Adapt(Beam(supports));
        RestrainedDirections[] expected = strongest == SupportType.Fixed ? [W, U | W, U | W | Theta] : [W, U | W];
        Assert.Equal(expected.Order(), input.Restraints.Select(r => r.Directions));

        StationModel stations = StationModel.Create(input);
        StationModel single = StationModel.Create(BeamModelAdapter.Adapt(Beam([SupportAt(1, strongest)])));
        Assert.Equal(single.MechanicalStations.AsEnumerable(), stations.MechanicalStations.AsEnumerable());
        MechanicalStation support = Assert.Single(stations.MechanicalStations, s => s.Position == 1);
        Assert.Equal(strongest == SupportType.Fixed ? U | W | Theta : U | W, support.Directions);
        Assert.Equal(support, Assert.Single(stations.EventStations, s => s.Position == 1).MechanicalStation);
    }

    [Fact]
    public void AdjacentStoredCoordinatesRemainSeparateEventStations()
    {
        double next = Math.BitIncrement(1d);
        StationModel stations = StationModel.Create(BeamModelAdapter.Adapt(Cantilever(Point(next, 5), Point(1, 3))));

        Assert.Equal(new[] { 0d, 1, next, L }, stations.EventStations.Select(s => s.Position));
        Assert.Equal(new TransversePointForce(1, 3), Assert.Single(stations.EventStations[1].PointForces));
        Assert.Equal(new TransversePointForce(next, 5), Assert.Single(stations.EventStations[2].PointForces));
    }

    [Theory]
    [InlineData(-7d)]
    [InlineData(0d)]
    public void AdditionalLoadsDoNotAddMechanicalStations(double force)
    {
        StationModel unloaded = StationModel.Create(BeamModelAdapter.Adapt(Cantilever()));
        StationModel loaded = StationModel.Create(BeamModelAdapter.Adapt(Cantilever(Point(1, force))));

        Assert.Equal(unloaded.MechanicalStations.AsEnumerable(), loaded.MechanicalStations.AsEnumerable());
        EventStation loadStation = Assert.Single(loaded.EventStations, s => s.Position == 1);
        Assert.Null(loadStation.MechanicalStation);
        Assert.Equal(new TransversePointForce(1, force), Assert.Single(loadStation.PointForces));
    }

    [Fact]
    public void PermutationsPreserveCanonicalPrimitivesAndEventAttachments()
    {
        Support[] supports = [SupportAt(L, SupportType.Roller), SupportAt(-0d, SupportType.Fixed),
            SupportAt(0d, SupportType.Pinned), SupportAt(0.75, SupportType.Roller)];
        BeamLoad[] loads = [Point(-0d, 0d), Point(0d, -0d), Point(0d, 0d), Point(0d, 0d), Point(0d, -7),
            Couple(1, 6), Couple(1, -2), Uniform(-0d, 1, 8), Uniform(0d, 1, -9), Uniform(0d, 1.5, 2)];
        MechanicalInput first = BeamModelAdapter.Adapt(Beam(supports, loads));
        MechanicalInput second = BeamModelAdapter.Adapt(Beam(supports.Reverse(), loads.Reverse()));

        Assert.Equal(first.Restraints.AsEnumerable(), second.Restraints.AsEnumerable());
        Assert.Equal(first.PointForces.Select(ForceBits), second.PointForces.Select(ForceBits));
        Assert.Contains((long.MinValue, 0L), first.PointForces.Select(ForceBits));
        Assert.Contains((0L, long.MinValue), first.PointForces.Select(ForceBits));
        Assert.Equal(2, first.PointForces.Count(f => ForceBits(f) == (0L, 0L)));
        Assert.Equal(first.PointMoments.AsEnumerable(), second.PointMoments.AsEnumerable());
        Assert.Equal(first.LineLoads.AsEnumerable(), second.LineLoads.AsEnumerable());

        StationModel firstStations = StationModel.Create(first);
        StationModel secondStations = StationModel.Create(second);
        Assert.Equal(firstStations.MechanicalStations.AsEnumerable(), secondStations.MechanicalStations.AsEnumerable());
        Assert.Equal(firstStations.EventStations.Length, secondStations.EventStations.Length);
        foreach ((EventStation left, EventStation right) in firstStations.EventStations.Zip(secondStations.EventStations))
        {
            Assert.Equal(left.Position, right.Position);
            Assert.Equal(left.MechanicalStation, right.MechanicalStation);
            Assert.Equal(left.PointForces.Select(ForceBits), right.PointForces.Select(ForceBits));
            Assert.Equal(left.PointMoments.AsEnumerable(), right.PointMoments.AsEnumerable());
            Assert.Equal(left.LineLoadStarts.AsEnumerable(), right.LineLoadStarts.AsEnumerable());
            Assert.Equal(left.LineLoadEnds.AsEnumerable(), right.LineLoadEnds.AsEnumerable());
        }

        AssertPositiveOrigin(firstStations);
        AssertPositiveOrigin(secondStations);
        Assert.Equal(5, firstStations.EventStations[0].PointForces.Length);
        Assert.Equal(3, firstStations.EventStations[0].LineLoadStarts.Length);
    }

    [Fact]
    public void MechanicalInputSnapshotsSourceCollections()
    {
        var pointForces = new List<TransversePointForce> { new(1, -7) };
        var input = new MechanicalInput(L, 1, [], pointForces, [], []);
        pointForces.Clear();

        Assert.Equal(new TransversePointForce(1, -7), Assert.Single(input.PointForces));
    }

    private static (long Position, long Value) ForceBits(TransversePointForce force) =>
        (DoubleToInt64Bits(force.Position), DoubleToInt64Bits(force.Newtons));

    private static void AssertPositiveOrigin(StationModel stations)
    {
        Assert.Equal(0L, DoubleToInt64Bits(Assert.Single(stations.MechanicalStations, s => s.Position == 0).Position));
        Assert.Equal(0L, DoubleToInt64Bits(Assert.Single(stations.EventStations, s => s.Position == 0).Position));
    }
}
