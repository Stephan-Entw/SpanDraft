using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

/// <summary>Nodal results for the original beam. No interpolation between nodes is provided.</summary>
public sealed class BeamSolution
{
    internal BeamSolution(BeamModel beam, IEnumerable<BeamNodeResult> nodes)
    {
        Beam = beam;
        Nodes = Array.AsReadOnly(nodes.ToArray());
    }

    public BeamModel Beam { get; }
    public IReadOnlyList<BeamNodeResult> Nodes { get; }
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
