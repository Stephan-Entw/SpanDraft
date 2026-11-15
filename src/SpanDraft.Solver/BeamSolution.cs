using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Solver.Kernel;

namespace SpanDraft.Solver;

/// <summary>Immutable nodal and analytical, piecewise continuous results for the original beam.</summary>
public sealed class BeamSolution
{
    private readonly BendingResult result;

    internal BeamSolution(BeamModel beam, BendingResult result)
    {
        Beam = beam;
        this.result = result;
        var reactions = new Dictionary<(double Position, RestrainedDirections Direction), double>();
        foreach (BendingReaction reaction in result.Reactions)
            if (!reactions.TryAdd((reaction.Position, reaction.Direction), reaction.Output.Value))
                throw new InvalidOperationException("Duplicate kernel reaction at the same position and direction.");

        Nodes = Array.AsReadOnly(result.Stations.Select((station, index) =>
        {
            BendingField field = (station.Position == beam.Length.Meters ? station.Left : station.Right)
                ?? throw new InvalidOperationException("Kernel station is missing its required inward or right field.");
            // The rotation left of a moment release differs from the node's own one; it exists nowhere else.
            double? rotationLeft = station.Jumps.HasFlag(BendingJump.Release) ? station.Left?.Bending[1].Value : null;
            return new BeamNodeResult(index, Length.FromMeters(station.Position), default,
                Displacement.FromMeters(field.Bending[0].Value), field.Bending[1].Value,
                reactions.TryGetValue((station.Position, RestrainedDirections.U), out double rx) ? Force.FromNewtons(rx) : null,
                reactions.TryGetValue((station.Position, RestrainedDirections.W), out double ry) ? Force.FromNewtons(ry) : null,
                reactions.TryGetValue((station.Position, RestrainedDirections.Theta), out double rm) ? Moment.FromNewtonMeters(rm) : null,
                rotationLeft);
        }).ToArray());
        Extrema = new BeamExtrema(result.Extrema);
        // Consumers pair SegmentExtrema[i] with Beam.Segments[i] by index, so the kernel must emit exactly one per segment.
        if (result.SectionExtrema.Length != beam.Segments.Count)
            throw new InvalidOperationException("The kernel must report one extrema set per beam segment.");
        SegmentExtrema = Array.AsReadOnly(result.SectionExtrema.Select(extrema => new BeamExtrema(extrema)).ToArray());
    }

    public BeamModel Beam { get; }
    public IReadOnlyList<BeamNodeResult> Nodes { get; }
    public BeamExtrema Extrema { get; }

    /// <summary>
    /// The extrema within each beam segment, in segment order; exactly one entry per <see cref="BeamModel.Segments"/> item. They are found on the polynomial pieces of the
    /// segment alone: at a segment boundary the inner side is used, so a boundary extremum belongs to both
    /// neighbours with the value of its own side.
    /// </summary>
    public IReadOnlyList<BeamExtrema> SegmentExtrema { get; }

    /// <summary>
    /// Evaluates the physical fields at x. At exact nodes w and rotation share the nodal state, except at a
    /// moment release, where the rotation jumps and Left/Right select the one-sided rotation;
    /// Left/Right selects the force and moment limits without averaging. At either beam end both sides select the
    /// existing inward limit. Positions and node comparisons use exact SI values.
    /// </summary>
    public BeamSectionResult EvaluateAt(Length position, EvaluationSide side)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown evaluation side.");
        if (position.Meters > Beam.Length.Meters)
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position is outside the beam.");

        BendingField field = result.EvaluateAt(position.Meters,
            side == EvaluationSide.Left ? FieldSide.Left : FieldSide.Right);
        return new BeamSectionResult(position, 0, field.Bending[0].Value, field.Bending[1].Value,
            0, field.Bending[3].Value, field.Bending[2].Value);
    }
}

/// <summary>
/// x and u are positive rightwards, w upwards, rotation and moments counterclockwise.
/// A null reaction denotes a free DOF; a constrained DOF can have a zero reaction.
/// </summary>
public sealed class BeamNodeResult
{
    internal BeamNodeResult(int nodeIndex, Length position, Displacement axialDisplacement,
        Displacement transverseDisplacement, double rotationRadians,
        Force? reactionX, Force? reactionY, Moment? reactionMoment, double? rotationLeftRadians = null)
    {
        NodeIndex = nodeIndex;
        Position = position;
        AxialDisplacement = axialDisplacement;
        TransverseDisplacement = transverseDisplacement;
        RotationRadians = rotationRadians;
        ReactionX = reactionX;
        ReactionY = reactionY;
        ReactionMoment = reactionMoment;
        RotationLeftRadians = rotationLeftRadians;
    }

    public int NodeIndex { get; }
    public Length Position { get; }
    public Displacement AxialDisplacement { get; }
    public Displacement TransverseDisplacement { get; }
    /// <summary>
    /// The rotation at the node; at a moment release it is the right-hand limit. Everywhere else both sides agree.
    /// </summary>
    public double RotationRadians { get; }

    /// <summary>
    /// The left-hand rotation limit at a moment release, where the rotation jumps; null at every other node.
    /// </summary>
    public double? RotationLeftRadians { get; }
    public Force? ReactionX { get; }
    public Force? ReactionY { get; }
    public Moment? ReactionMoment { get; }
}
