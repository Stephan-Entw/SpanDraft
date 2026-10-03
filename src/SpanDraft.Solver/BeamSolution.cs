using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

/// <summary>Immutable nodal and analytical, piecewise continuous results for the original beam.</summary>
public sealed class BeamSolution
{
    private readonly ElementField[] fields;

    internal BeamSolution(BeamModel beam, IEnumerable<BeamNodeResult> nodes, SolverModel model)
    {
        Beam = beam;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        fields = model.Elements.Select(element => new ElementField(beam, element,
            Nodes[element.Left.Index], Nodes[element.Right.Index])).ToArray();
        Extrema = BeamExtrema.Find(fields);
    }

    public BeamModel Beam { get; }
    public IReadOnlyList<BeamNodeResult> Nodes { get; }
    public BeamExtrema Extrema { get; }

    /// <summary>
    /// Evaluates the physical fields at x. At interior nodes Left/Right selects the
    /// adjacent element without averaging. At either beam end both sides select the
    /// existing inward limit. Positions and node comparisons use exact SI values.
    /// </summary>
    public BeamSectionResult EvaluateAt(Length position, EvaluationSide side)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown evaluation side.");
        if (position.Meters > Beam.Length.Meters)
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position is outside the beam.");

        int low = 0, high = Nodes.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            double x = Nodes[middle].Position.Meters;
            if (position.Meters < x) high = middle - 1;
            else if (position.Meters > x) low = middle + 1;
            else
            {
                int index = side == EvaluationSide.Left ? middle - 1 : middle;
                return fields[Math.Clamp(index, 0, fields.Length - 1)].EvaluateAt(position);
            }
        }
        return fields[high].EvaluateAt(position);
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
        Force? reactionX, Force? reactionY, Moment? reactionMoment)
    {
        NodeIndex = nodeIndex;
        Position = position;
        AxialDisplacement = axialDisplacement;
        TransverseDisplacement = transverseDisplacement;
        RotationRadians = rotationRadians;
        ReactionX = reactionX;
        ReactionY = reactionY;
        ReactionMoment = reactionMoment;
    }

    public int NodeIndex { get; }
    public Length Position { get; }
    public Displacement AxialDisplacement { get; }
    public Displacement TransverseDisplacement { get; }
    public double RotationRadians { get; }
    public Force? ReactionX { get; }
    public Force? ReactionY { get; }
    public Moment? ReactionMoment { get; }
}
