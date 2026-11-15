using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal sealed class StationModel
{
    private StationModel(ImmutableArray<MechanicalStation> mechanicalStations, ImmutableArray<EventStation> eventStations,
        ImmutableArray<int> intervalSections)
    {
        MechanicalStations = mechanicalStations;
        EventStations = eventStations;
        IntervalSections = intervalSections;
    }

    internal ImmutableArray<MechanicalStation> MechanicalStations { get; }

    /// <summary>
    /// For every interval between consecutive mechanical stations the index of the stiffness section that
    /// contains it. Every section boundary is a mechanical station, so no interval straddles a boundary.
    /// </summary>
    internal ImmutableArray<int> IntervalSections { get; }
    internal ImmutableArray<EventStation> EventStations { get; }

    internal static StationModel Create(MechanicalInput input)
    {
        var restraints = new SortedDictionary<double, RestrainedDirections>
        {
            [0d] = RestrainedDirections.None,
            [input.Length] = RestrainedDirections.None
        };
        // A section boundary is a mechanical station without restraints. It never counts as occupied: a support
        // or a spring may sit on it.
        RequireTiling(input);
        foreach (StiffnessSection section in input.Sections.Skip(1))
            restraints[StationPosition(section.Start)] = RestrainedDirections.None;
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

        // A release is a mechanical station of its own unless a support, a spring or a section boundary is there.
        // The rotation is free to jump at a release, so no rotational restraint and no applied moment may act on it:
        // it would be undefined which side carries them.
        var releases = new HashSet<double>();
        foreach (InternalMomentRelease release in input.MomentReleases)
        {
            double position = StationPosition(release.Position);
            if (!(position > 0 && position < input.Length))
                throw new InvalidMechanicalModelException("A moment release must lie strictly inside the beam.");
            if (!releases.Add(position))
                throw new InvalidMechanicalModelException("Two moment releases cannot share a position.");
            if (moments.Contains(position))
                throw new InvalidMechanicalModelException("A moment release cannot share its position with an applied moment.");
            restraints.TryAdd(position, RestrainedDirections.None);
            if (restraints[position].HasFlag(RestrainedDirections.Theta) || springs.GetValueOrDefault(position).Rotational != 0)
                throw new InvalidMechanicalModelException(
                    "A moment release cannot share its position with a rotational restraint.");
        }
        var lineStarts = input.LineLoads.ToLookup(q => StationPosition(q.Start));
        var lineEnds = input.LineLoads.ToLookup(q => StationPosition(q.End));

        var eventPositions = new SortedSet<double>(restraints.Keys);
        eventPositions.UnionWith(forces.Select(group => group.Key));
        eventPositions.UnionWith(moments.Select(group => group.Key));
        eventPositions.UnionWith(lineStarts.Select(group => group.Key));
        eventPositions.UnionWith(lineEnds.Select(group => group.Key));

        ImmutableArray<MechanicalStation> mechanical = restraints
            .Select(pair => new MechanicalStation(pair.Key, pair.Value, springs.GetValueOrDefault(pair.Key),
                releases.Contains(pair.Key)))
            .ToImmutableArray();
        return new StationModel(mechanical,
            eventPositions.Select(position => new EventStation(position,
                restraints.TryGetValue(position, out RestrainedDirections directions)
                    ? new MechanicalStation(position, directions, springs.GetValueOrDefault(position),
                        releases.Contains(position))
                    : null,
                forces[position].ToImmutableArray(), moments[position].ToImmutableArray(),
                lineStarts[position].ToImmutableArray(), lineEnds[position].ToImmutableArray())).ToImmutableArray(),
            AssignSections(input, mechanical));
    }

    private static void RequireTiling(MechanicalInput input)
    {
        if (input.Sections.IsEmpty)
            throw new InvalidMechanicalModelException("The beam needs at least one stiffness section.");
        if (input.Sections.Length == 1)
        {
            // One section covers the beam by definition; its range is judged by the range rules of the kernel,
            // so NaN and degenerate lengths are compared by identity only.
            StiffnessSection only = input.Sections[0];
            if (!StationPosition(only.Start).Equals(0d) || !only.End.Equals(input.Length))
                throw new InvalidMechanicalModelException("The stiffness section must cover the whole beam.");
            return;
        }
        double expected = 0;
        foreach (StiffnessSection section in input.Sections)
        {
            // Exact comparison: sections tile the beam or the model is invalid.
            if (StationPosition(section.Start) != expected || !(section.End > section.Start))
                throw new InvalidMechanicalModelException(
                    "Stiffness sections must follow each other in ascending order without gap or overlap.");
            expected = section.End;
        }
        if (expected != input.Length)
            throw new InvalidMechanicalModelException("The stiffness sections must end exactly at the beam end.");
    }

    // Exact classification by station identity: a section boundary is itself a station, so the left station of an
    // interval decides its section and the right one is checked to stay inside it. No midpoint is ever tested.
    private static ImmutableArray<int> AssignSections(MechanicalInput input, ImmutableArray<MechanicalStation> stations)
    {
        var result = ImmutableArray.CreateBuilder<int>(stations.Length - 1);
        if (input.Sections.Length == 1)
        {
            result.AddRange(Enumerable.Repeat(0, stations.Length - 1));
            return result.MoveToImmutable();
        }
        int section = 0;
        for (int i = 0; i < stations.Length - 1; i++)
        {
            while (input.Sections[section].End <= stations[i].Position)
                section++;
            if (input.Sections[section].Start > stations[i].Position || input.Sections[section].End < stations[i + 1].Position)
                throw new InvalidMechanicalModelException("A mechanical interval straddles a stiffness section boundary.");
            result.Add(section);
        }
        return result.MoveToImmutable();
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
/// restraint of a spring support; springs act through <see cref="Springs"/>. A <see cref="Release"/> is an internal
/// moment release: the bending moment is zero on both sides and the rotation may jump.
/// </summary>
internal readonly record struct MechanicalStation(double Position, RestrainedDirections Directions,
    SpringStiffnesses Springs = default, bool Release = false)
{
    internal bool RigidW => Directions.HasFlag(RestrainedDirections.W);
    internal bool RigidTheta => Directions.HasFlag(RestrainedDirections.Theta);
    internal bool ElasticW => Springs.Translational != 0;
    internal bool ElasticTheta => Springs.Rotational != 0;

    /// <summary>True when a transverse restraint, rigid or elastic, acts at the station.</summary>
    internal bool ConstrainsW => RigidW || ElasticW;

    /// <summary>True when a rotational restraint, rigid or elastic, acts at the station.</summary>
    internal bool ConstrainsTheta => RigidTheta || ElasticTheta;

    /// <summary>True when a restraint, rigid or elastic, acts in the direction (the axial one is always rigid).</summary>
    internal bool Constrains(RestrainedDirections direction) => direction switch
    {
        RestrainedDirections.W => ConstrainsW,
        RestrainedDirections.Theta => ConstrainsTheta,
        _ => Directions.HasFlag(direction)
    };
}

internal sealed class InvalidMechanicalModelException(string message) : ArgumentException(message);

internal sealed record EventStation(double Position, MechanicalStation? MechanicalStation,
    ImmutableArray<TransversePointForce> PointForces, ImmutableArray<AppliedPointMoment> PointMoments,
    ImmutableArray<TransverseLineLoad> LineLoadStarts, ImmutableArray<TransverseLineLoad> LineLoadEnds);
