using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

// Global nodal DOFs are [u, w, θ]: index = 3 * node index + enum value.
internal enum DegreeOfFreedom { Axial = 0, Transverse = 1, Rotation = 2 }

internal sealed record BeamNode(int Index, Length Position);

internal sealed record BeamElement(BeamNode Left, BeamNode Right)
{
    internal double LengthMeters => Right.Position.Meters - Left.Position.Meters;

    // Every UDL boundary is a node, so coverage is always all-or-nothing.
    internal bool IsCoveredBy(UniformDistributedLoad load) =>
        Left.Position.Meters >= load.StartPosition.Meters &&
        Right.Position.Meters <= load.EndPosition.Meters;

    // Local DOF order is [u1, w1, θ1, u2, w2, θ2]. Both axes point rightwards.
    internal int[] GlobalDofs =>
    [
        DofMap.Index(Left.Index, DegreeOfFreedom.Axial),
        DofMap.Index(Left.Index, DegreeOfFreedom.Transverse),
        DofMap.Index(Left.Index, DegreeOfFreedom.Rotation),
        DofMap.Index(Right.Index, DegreeOfFreedom.Axial),
        DofMap.Index(Right.Index, DegreeOfFreedom.Transverse),
        DofMap.Index(Right.Index, DegreeOfFreedom.Rotation)
    ];
}

internal sealed class SolverModel
{
    private readonly Dictionary<double, int> nodeIndices;

    private SolverModel(BeamNode[] nodes, BeamElement[] elements)
    {
        Nodes = Array.AsReadOnly(nodes);
        Elements = Array.AsReadOnly(elements);
        nodeIndices = nodes.ToDictionary(node => node.Position.Meters, node => node.Index);
    }

    internal IReadOnlyList<BeamNode> Nodes { get; }
    internal IReadOnlyList<BeamElement> Elements { get; }
    internal int NodeIndex(Length position) => nodeIndices[position.Meters];

    internal static SolverModel Create(BeamModel beam)
    {
        var positions = new SortedSet<double> { 0, beam.Length.Meters };
        foreach (Support support in beam.Supports)
            positions.Add(support.Position.Meters);
        foreach (BeamLoad load in beam.Loads)
        {
            switch (load)
            {
                case PointForce force: positions.Add(force.Position.Meters); break;
                case PointMoment moment: positions.Add(moment.Position.Meters); break;
                case UniformDistributedLoad distributed:
                    positions.Add(distributed.StartPosition.Meters);
                    positions.Add(distributed.EndPosition.Meters);
                    break;
                default: throw new NotSupportedException($"Unsupported beam load: {load.GetType().Name}.");
            }
        }

        BeamNode[] nodes = positions.Select((x, i) => new BeamNode(i, Length.FromMeters(x))).ToArray();
        BeamElement[] elements = Enumerable.Range(0, nodes.Length - 1)
            .Select(i => new BeamElement(nodes[i], nodes[i + 1])).ToArray();
        foreach (BeamElement element in elements)
            NumericalGuard.Positive(element.LengthMeters, "Element length");
        return new SolverModel(nodes, elements);
    }
}

internal sealed class DofMap
{
    private readonly bool[] constrained;

    internal DofMap(BeamModel beam, SolverModel model)
    {
        constrained = new bool[checked(3 * model.Nodes.Count)];
        foreach (Support support in beam.Supports)
        {
            int node = model.NodeIndex(support.Position);
            constrained[Index(node, DegreeOfFreedom.Transverse)] = true;
            if (support.Type is SupportType.Fixed or SupportType.Pinned)
                constrained[Index(node, DegreeOfFreedom.Axial)] = true;
            if (support.Type == SupportType.Fixed)
                constrained[Index(node, DegreeOfFreedom.Rotation)] = true;
        }
        FreeDofs = Array.AsReadOnly(Enumerable.Range(0, Count).Where(i => !constrained[i]).ToArray());
    }

    internal int Count => constrained.Length;
    internal IReadOnlyList<int> FreeDofs { get; }
    internal bool IsConstrained(int index) => constrained[index];
    internal static int Index(int node, DegreeOfFreedom dof) => checked(3 * node + (int)dof);

    internal void EnsureStable(SolverModel model)
    {
        bool axialBlocked = model.Nodes.Any(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Axial)));
        bool rotationBlocked = model.Nodes.Any(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Rotation)));
        int transverseSupports = model.Nodes.Count(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Transverse)));
        // A connected positive-stiffness beam has rigid modes u = c, w = a + b*x, θ = b.
        // With the current support types, a θ restraint always includes a w restraint.
        if (!axialBlocked || (!rotationBlocked && transverseSupports < 2))
            throw new BeamSolverException(SolverErrorCode.UnstableModel,
                "Supports leave an axial translation or a transverse rigid-body motion unconstrained.");
    }
}
