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

    internal static SolverModel Create(BeamModel beam, SolverLoads? additional = null)
    {
        var positions = new SortedSet<double> { 0, beam.Length.Meters };
        if (additional is not null)
            positions.UnionWith(additional.Positions);
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

    /// <summary>
    /// Counts the motion a chain of rigid parts keeps, in beam order. f is the freedom of the current part
    /// given everything to its left. A part that keeps two freedoms at a hinge has a free tip.
    /// </summary>
    internal static bool ChainIsStable(BeamModel beam, IReadOnlyList<double> hinges)
    {
        var items = beam.Supports.GroupBy(s => s.Position.Meters)
            .Select(g => (X: g.Key, Order: 0, Restraint: g.Any(s => s.Type == SupportType.Fixed) ? 2 : 1))
            .Concat(hinges.Distinct().Select(h => (X: h, Order: 1, Restraint: 0)))
            .OrderBy(i => i.X).ThenBy(i => i.Order);
        var supportPositions = beam.Supports.Select(s => s.Position.Meters).ToHashSet();
        int freedom = 2;
        foreach (var item in items)
        {
            if (item.Order == 0)
            {
                freedom = Math.Max(freedom - item.Restraint, 0);
                continue;
            }
            if (freedom == 2) return false;
            if (supportPositions.Contains(item.X))
            {
                if (freedom != 0) return false;
                freedom = 1;
            }
            else freedom = freedom == 0 ? 1 : 2;
        }
        return freedom == 0;
    }

    /// <summary>
    /// Exact rigid-body check for parts between hinges. Each part moves as w = a + b*x; a translational
    /// restraint (rigid or elastic) gives a + b*x = 0, a rotational restraint b = 0 and a hinge continuity of w.
    /// Positions are exact binary fractions, so the rank needs no tolerance.
    /// </summary>
    internal static bool IsKinematicallyStable(IReadOnlyList<(double X, bool Rotation)> restraints,
        IReadOnlyList<double> hinges, double length)
    {
        double[] cuts = [0, .. hinges.Distinct().Order(), length];
        int parts = cuts.Length - 1;
        var rows = new List<Rational[]>();
        Rational[] Row() => Enumerable.Repeat(Rational.Zero, 2 * parts).ToArray();
        foreach ((double x, bool rotation) in restraints)
            for (int i = 0; i < parts; i++)
            {
                if (x < cuts[i] || x > cuts[i + 1]) continue;
                Rational[] row = Row();
                if (rotation) row[2 * i + 1] = Rational.One;
                else
                {
                    row[2 * i] = Rational.One;
                    row[2 * i + 1] = Rational.From(x);
                }
                rows.Add(row);
            }
        for (int i = 0; i < parts - 1; i++)
        {
            Rational[] row = Row();
            Rational h = Rational.From(cuts[i + 1]);
            row[2 * i] = Rational.One;
            row[2 * i + 1] = h;
            row[2 * i + 2] = -Rational.One;
            row[2 * i + 3] = -h;
            rows.Add(row);
        }
        int rank = 0;
        for (int column = 0; column < 2 * parts && rank < rows.Count; column++)
        {
            int pivot = rows.FindIndex(rank, r => !r[column].IsZero);
            if (pivot < 0) continue;
            (rows[rank], rows[pivot]) = (rows[pivot], rows[rank]);
            for (int r = 0; r < rows.Count; r++)
            {
                if (r == rank || rows[r][column].IsZero) continue;
                Rational factor = rows[r][column] / rows[rank][column];
                for (int c = column; c < 2 * parts; c++) rows[r][c] -= factor * rows[rank][c];
            }
            rank++;
        }
        return rank == 2 * parts;
    }

    internal readonly record struct Rational(System.Numerics.BigInteger N, System.Numerics.BigInteger D)
    {
        internal static Rational Zero => new(0, 1);
        internal static Rational One => new(1, 1);
        internal bool IsZero => N.IsZero;

        internal static Rational From(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent++; else mantissa |= 1L << 52;
            exponent -= 1075;
            System.Numerics.BigInteger n = negative ? -mantissa : mantissa;
            return exponent >= 0 ? Make(n << exponent, 1) : Make(n, System.Numerics.BigInteger.One << -exponent);
        }

        private static Rational Make(System.Numerics.BigInteger n, System.Numerics.BigInteger d)
        {
            if (d.Sign < 0) { n = -n; d = -d; }
            System.Numerics.BigInteger g = System.Numerics.BigInteger.GreatestCommonDivisor(n, d);
            return g.IsZero || g.IsOne ? new(n, d) : new(n / g, d / g);
        }

        public static Rational operator -(Rational a) => new(-a.N, a.D);
        public static Rational operator -(Rational a, Rational b) => Make(a.N * b.D - b.N * a.D, a.D * b.D);
        public static Rational operator *(Rational a, Rational b) => Make(a.N * b.N, a.D * b.D);
        public static Rational operator /(Rational a, Rational b) => Make(a.N * b.D, a.D * b.N);
    }

    internal void EnsureStable(SolverModel model, IReadOnlyList<double>? hinges = null)
    {
        bool axialBlocked = model.Nodes.Any(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Axial)));
        bool rotationBlocked = model.Nodes.Any(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Rotation)));
        int transverseSupports = model.Nodes.Count(n => IsConstrained(Index(n.Index, DegreeOfFreedom.Transverse)));
        // A connected positive-stiffness beam has rigid modes u = c, w = a + b*x, θ = b.
        // With the current support types, a θ restraint always includes a w restraint.
        // With hinges, ChainIsStable decides; a hinge never affects the axial equation.
        if (!axialBlocked || (hinges is not { Count: > 0 } && !rotationBlocked && transverseSupports < 2))
            throw new BeamSolverException(SolverErrorCode.UnstableModel,
                "Supports leave an axial translation or a transverse rigid-body motion unconstrained.");
    }
}
