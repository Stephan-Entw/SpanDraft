using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal sealed class StationModel
{
    private StationModel(ImmutableArray<MechanicalStation> mechanicalStations, ImmutableArray<EventStation> eventStations)
    {
        MechanicalStations = mechanicalStations;
        EventStations = eventStations;
    }

    internal ImmutableArray<MechanicalStation> MechanicalStations { get; }
    internal ImmutableArray<EventStation> EventStations { get; }

    internal static StationModel Create(MechanicalInput input)
    {
        var restraints = new SortedDictionary<double, RestrainedDirections>
        {
            [0d] = RestrainedDirections.None,
            [input.Length] = RestrainedDirections.None
        };
        foreach (RigidRestraint restraint in input.Restraints)
        {
            double position = StationPosition(restraint.Position);
            restraints[position] = restraints.GetValueOrDefault(position) | restraint.Directions;
        }

        var forces = input.PointForces.ToLookup(f => StationPosition(f.Position));
        var moments = input.PointMoments.ToLookup(m => StationPosition(m.Position));
        var lineStarts = input.LineLoads.ToLookup(q => StationPosition(q.Start));
        var lineEnds = input.LineLoads.ToLookup(q => StationPosition(q.End));

        var eventPositions = new SortedSet<double>(restraints.Keys);
        eventPositions.UnionWith(forces.Select(group => group.Key));
        eventPositions.UnionWith(moments.Select(group => group.Key));
        eventPositions.UnionWith(lineStarts.Select(group => group.Key));
        eventPositions.UnionWith(lineEnds.Select(group => group.Key));

        return new StationModel(
            restraints.Select(pair => new MechanicalStation(pair.Key, pair.Value)).ToImmutableArray(),
            eventPositions.Select(position => new EventStation(position,
                restraints.TryGetValue(position, out RestrainedDirections directions)
                    ? new MechanicalStation(position, directions)
                    : null,
                forces[position].ToImmutableArray(), moments[position].ToImmutableArray(),
                lineStarts[position].ToImmutableArray(), lineEnds[position].ToImmutableArray())).ToImmutableArray());
    }

    private static double StationPosition(double position) => position == 0d ? 0d : position;
}

internal readonly record struct MechanicalStation(double Position, RestrainedDirections Directions);

internal sealed record EventStation(double Position, MechanicalStation? MechanicalStation,
    ImmutableArray<TransversePointForce> PointForces, ImmutableArray<AppliedPointMoment> PointMoments,
    ImmutableArray<ConstantLineLoad> LineLoadStarts, ImmutableArray<ConstantLineLoad> LineLoadEnds);
