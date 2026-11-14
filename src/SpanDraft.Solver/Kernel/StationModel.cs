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
        var occupied = new HashSet<double>();
        foreach (RigidRestraint restraint in input.Restraints)
        {
            double position = StationPosition(restraint.Position);
            occupied.Add(position);
            restraints[position] = restraints.GetValueOrDefault(position) | restraint.Directions;
        }

        // A spring shares its position with no other support: the rule for such a combination is undefined.
        var springs = new Dictionary<double, SpringStiffnesses>();
        foreach (ElasticRestraint elastic in input.ElasticRestraints)
        {
            RequireValid(elastic);
            double position = StationPosition(elastic.Position);
            if (!occupied.Add(position))
                throw new InvalidMechanicalModelException("A spring support cannot share its position with another support.");
            springs.Add(position, new(elastic.TranslationalStiffness, elastic.RotationalStiffness));
            // Springs hold the beam axially; a rigid translation is a rigid W restraint with an elastic rotation.
            restraints[position] = restraints.GetValueOrDefault(position) | RestrainedDirections.U |
                (elastic.RigidTranslation ? RestrainedDirections.W : RestrainedDirections.None);
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
            restraints.Select(pair => new MechanicalStation(pair.Key, pair.Value, springs.GetValueOrDefault(pair.Key)))
                .ToImmutableArray(),
            eventPositions.Select(position => new EventStation(position,
                restraints.TryGetValue(position, out RestrainedDirections directions)
                    ? new MechanicalStation(position, directions, springs.GetValueOrDefault(position))
                    : null,
                forces[position].ToImmutableArray(), moments[position].ToImmutableArray(),
                lineStarts[position].ToImmutableArray(), lineEnds[position].ToImmutableArray())).ToImmutableArray());
    }

    private static double StationPosition(double position) => position == 0d ? 0d : position;

    private static void RequireValid(ElasticRestraint elastic)
    {
        double w = elastic.TranslationalStiffness, theta = elastic.RotationalStiffness;
        if (!double.IsFinite(w) || !double.IsFinite(theta) || w < 0 || theta < 0)
            throw new InvalidMechanicalModelException("Spring stiffness must be finite and non-negative.");
        if (w == 0 && theta == 0 && !elastic.RigidTranslation)
            throw new InvalidMechanicalModelException("A spring support needs a translational or a rotational spring.");
        if (elastic.RigidTranslation && (w != 0 || theta == 0))
            throw new InvalidMechanicalModelException("A rigid translation needs a rotational spring and no translational one.");
    }
}

/// <summary>Spring stiffnesses of one station in SI units (N/m, N·m/rad); zero means no spring.</summary>
internal readonly record struct SpringStiffnesses(double Translational, double Rotational);

/// <summary>
/// The mechanical restraints at one position. <see cref="Directions"/> holds the rigid ones, including the axial
/// restraint of a spring support; springs act through <see cref="Springs"/>.
/// </summary>
internal readonly record struct MechanicalStation(double Position, RestrainedDirections Directions,
    SpringStiffnesses Springs = default)
{
    internal bool RigidW => Directions.HasFlag(RestrainedDirections.W);
    internal bool RigidTheta => Directions.HasFlag(RestrainedDirections.Theta);
    internal bool ElasticW => Springs.Translational != 0;
    internal bool ElasticTheta => Springs.Rotational != 0;

    /// <summary>True when a transverse restraint, rigid or elastic, acts at the station.</summary>
    internal bool ConstrainsW => RigidW || ElasticW;

    /// <summary>True when a rotational restraint, rigid or elastic, acts at the station.</summary>
    internal bool ConstrainsTheta => RigidTheta || ElasticTheta;
}

internal sealed class InvalidMechanicalModelException(string message) : ArgumentException(message);

internal sealed record EventStation(double Position, MechanicalStation? MechanicalStation,
    ImmutableArray<TransversePointForce> PointForces, ImmutableArray<AppliedPointMoment> PointMoments,
    ImmutableArray<TransverseLineLoad> LineLoadStarts, ImmutableArray<TransverseLineLoad> LineLoadEnds);
