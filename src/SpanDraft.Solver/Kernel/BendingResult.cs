using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

[Flags]
internal enum BendingJump { None = 0, PointForce = 1, PointMoment = 2, ReactionY = 4, ReactionMoment = 8, Release = 16 }
internal sealed record BendingStation(double Position, BendingField? Left, BendingField? Right, BendingJump Jumps);
internal sealed record BendingPiece(double Start, double End, ImmutableArray<double> ThetaCoefficients,
    ImmutableArray<double> ShearCoefficients, double Intensity, PieceQuality Quality, double IntensityChange = 0)
{
    internal (double Position, FieldSide Side) CandidatePosition(double t)
    {
        double x = Start + t * (End - Start);
        KernelFailureException.RequireFinite(x);
        KernelFailureException.Require(x >= Start && x <= End);
        return (x, x == Start ? FieldSide.Right : x == End ? FieldSide.Left : FieldSide.Interior);
    }

    internal IReadOnlyList<double> Candidates(BendingComponent component) => component switch
    {
        BendingComponent.W => PolynomialRoots.InUnitInterval(ThetaCoefficients.ToArray()),
        BendingComponent.M => PolynomialRoots.InUnitInterval(ShearCoefficients.ToArray()),
        BendingComponent.V => PolynomialRoots.InUnitInterval(Intensity, IntensityChange),
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    internal static BendingPiece Create(BendingFields fields, PieceQuality quality)
    {
        double a = quality.Start, b = quality.End;
        if (fields.IsUnloaded) return new(a, b, [0], [0], 0, quality);
        NormalizedBendingField start = fields.EvaluateNormalized(a, FieldSide.Right);
        double h = RoundCounted.BoundedDistance(b, a, fields.EL).Value;
        Bounded intensity = default;
        double change = 0;
        foreach (ScaledLineLoad line in fields.Loads.Segments[fields.SegmentOf(a)].Lines)
        {
            if (!(line.Start <= a && line.End >= b))
                continue;
            if (!line.HasRamp)
            {
                intensity += new Bounded(line.StartValue);
                continue;
            }
            // q(t) = q0 + dq t on the piece; dq is the slope times the piece length, not a difference of values.
            intensity += new Bounded(line.IntensityAt(a, fields.EL).Value);
            double width = RoundCounted.BoundedDistance(line.End, line.Start, fields.EL).Value;
            change += line.Rise.Value * (h / width);
        }
        double ei = fields.IntervalEI[fields.SegmentOf(a)].Value;
        double m = start.Values[2], v = start.Values[3], q = intensity.Value;
        ImmutableArray<double> theta = [start.Values[1], (m * h) / ei, ((v * h) * h / 2) / ei,
            (((q * h) * h) * h / 6) / ei];
        ImmutableArray<double> shear = [v, q * h];
        if (change != 0)
        {
            theta = theta.Add((((change * h) * h) * h / 24) / ei);
            shear = shear.Add(change * h / 2);
        }
        foreach (double value in theta.Concat(shear))
            KernelFailureException.RequireFinite(value);
        KernelFailureException.RequireFinite(change);
        return new(a, b, theta, shear, q, quality, change);
    }
}

internal readonly record struct BendingExtremum(double Position, FieldSide Side, VerifiedOutput Output);
internal sealed record PieceExtremumCandidates(BendingPiece Piece, ImmutableArray<BendingExtremum> W,
    ImmutableArray<BendingExtremum> V, ImmutableArray<BendingExtremum> M);
internal sealed record BendingExtremumRange(BendingExtremum Minimum, BendingExtremum Maximum);
internal sealed record BendingExtrema(BendingExtremumRange W, BendingExtremumRange V, BendingExtremumRange M)
{
    internal static BendingExtrema Find(BendingFields fields, ImmutableArray<BendingPiece> pieces) =>
        Find(pieces.Select(piece => Candidates(fields, piece)));

    /// <summary>
    /// Evaluates the extremum candidates of one piece: both endpoints and the interior roots of each component, in the
    /// order they are offered to a range. The evaluation is the costly part and is shared by every search over the piece.
    /// </summary>
    internal static PieceExtremumCandidates Candidates(BendingFields fields, BendingPiece piece)
    {
        var w = ImmutableArray.CreateBuilder<BendingExtremum>();
        var v = ImmutableArray.CreateBuilder<BendingExtremum>();
        var m = ImmutableArray.CreateBuilder<BendingExtremum>();
        AddEndpoint(piece.Start, FieldSide.Right);
        AddEndpoint(piece.End, FieldSide.Left);
        if (!fields.IsUnloaded)
        {
            AddRoots(w, BendingComponent.W);
            AddRoots(v, BendingComponent.V);
            AddRoots(m, BendingComponent.M);
        }
        return new(piece, w.ToImmutable(), v.ToImmutable(), m.ToImmutable());

        void AddRoots(ImmutableArray<BendingExtremum>.Builder candidates, BendingComponent component)
        {
            foreach (double t in piece.Candidates(component))
            {
                if (!(t > 0 && t < 1)) continue;
                var (x, side) = piece.CandidatePosition(t);
                BendingField field = fields.EvaluateAt(x, side);
                candidates.Add(new(x, side, field.Bending[(int)component]));
            }
        }

        void AddEndpoint(double x, FieldSide side)
        {
            BendingField field = fields.EvaluateAt(x, side);
            w.Add(new(x, side, field.Bending[0]));
            v.Add(new(x, side, field.Bending[3]));
            m.Add(new(x, side, field.Bending[2]));
        }
    }

    /// <summary>
    /// The extrema over the given pieces, offered to each range in the given order. Ties are decided with a tolerance,
    /// which is not transitive, so the order of the candidates is part of the result.
    /// </summary>
    internal static BendingExtrema Find(IEnumerable<PieceExtremumCandidates> pieces)
    {
        var w = new Range();
        var v = new Range();
        var m = new Range();
        foreach (PieceExtremumCandidates piece in pieces)
        {
            foreach (BendingExtremum candidate in piece.W) w.Add(candidate);
            foreach (BendingExtremum candidate in piece.V) v.Add(candidate);
            foreach (BendingExtremum candidate in piece.M) m.Add(candidate);
        }
        return new(w.Result, v.Result, m.Result);
    }

    internal static bool Better(BendingExtremum candidate, BendingExtremum current, bool minimize)
    {
        double a = candidate.Output.Value, b = current.Output.Value;
        double scale = Math.Max(Math.Abs(a), Math.Abs(b));
        bool tied = scale == 0 || Math.Abs(a / scale - b / scale) <= 64 * PolynomialRoots.MachinePrecision;
        return tied ? candidate.Position < current.Position || candidate.Position == current.Position && candidate.Side < current.Side :
            minimize ? a < b : a > b;
    }

    private sealed class Range
    {
        private BendingExtremum? minimum, maximum;
        internal BendingExtremumRange Result => new(minimum!.Value, maximum!.Value);
        internal void Add(BendingExtremum value)
        {
            if (minimum is null || Better(value, minimum.Value, true)) minimum = value;
            if (maximum is null || Better(value, maximum.Value, false)) maximum = value;
        }
    }
}

/// <summary>
/// The result of one solve. <paramref name="SectionExtrema"/> holds the extrema of every stiffness section, found on
/// the pieces inside that section only, so an extremum on a boundary is taken from the inner side.
/// </summary>
internal sealed record BendingResult(BendingFields Fields, ImmutableArray<BendingStation> Stations,
    ImmutableArray<BendingReaction> Reactions, ImmutableArray<BendingPiece> Pieces, BendingExtrema Extrema,
    ImmutableArray<BendingExtrema> SectionExtrema, BendingQuality Quality)
{
    internal BendingField EvaluateAt(double x, FieldSide side) => Fields.EvaluateAt(x, side);
}
