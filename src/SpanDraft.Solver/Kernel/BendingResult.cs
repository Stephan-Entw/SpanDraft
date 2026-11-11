using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

[Flags]
internal enum BendingJump { None = 0, PointForce = 1, PointMoment = 2, ReactionY = 4, ReactionMoment = 8 }
internal sealed record BendingStation(double Position, BendingField? Left, BendingField? Right, BendingJump Jumps);
internal sealed record BendingPiece(double Start, double End, ImmutableArray<double> ThetaCoefficients,
    ImmutableArray<double> ShearCoefficients, double Intensity, PieceQuality Quality)
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
        BendingComponent.V => PolynomialRoots.InUnitInterval(Intensity),
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    internal static BendingPiece Create(BendingFields fields, PieceQuality quality)
    {
        double a = quality.Start, b = quality.End;
        if (fields.IsUnloaded) return new(a, b, [0], [0], 0, quality);
        NormalizedBendingField start = fields.EvaluateNormalized(a, FieldSide.Right);
        double h = RoundCounted.BoundedDistance(b, a, fields.EL).Value;
        Bounded intensity = default;
        foreach (ScaledLineLoad line in fields.Loads.Segments[fields.SegmentOf(a)].Lines)
            if (line.Start <= a && line.End >= b)
                intensity += new Bounded(line.Value);
        double ei = fields.EI.Value;
        double m = start.Values[2], v = start.Values[3], q = intensity.Value;
        ImmutableArray<double> theta = [start.Values[1], (m * h) / ei, ((v * h) * h / 2) / ei,
            (((q * h) * h) * h / 6) / ei];
        ImmutableArray<double> shear = [v, q * h];
        foreach (double value in theta.Concat(shear))
            KernelFailureException.RequireFinite(value);
        return new(a, b, theta, shear, q, quality);
    }
}

internal readonly record struct BendingExtremum(double Position, FieldSide Side, VerifiedOutput Output);
internal sealed record BendingExtremumRange(BendingExtremum Minimum, BendingExtremum Maximum);
internal sealed record BendingExtrema(BendingExtremumRange W, BendingExtremumRange V, BendingExtremumRange M)
{
    internal static BendingExtrema Find(BendingFields fields, ImmutableArray<BendingPiece> pieces)
    {
        var w = new Range();
        var v = new Range();
        var m = new Range();
        foreach (BendingPiece piece in pieces)
        {
            AddEndpoint(piece.Start, FieldSide.Right);
            AddEndpoint(piece.End, FieldSide.Left);
            if (fields.IsUnloaded) continue;
            AddRoots(w, BendingComponent.W);
            AddRoots(v, BendingComponent.V);
            AddRoots(m, BendingComponent.M);

            void AddRoots(Range range, BendingComponent component)
            {
                foreach (double t in piece.Candidates(component))
                {
                    if (!(t > 0 && t < 1)) continue;
                    var (x, side) = piece.CandidatePosition(t);
                    BendingField field = fields.EvaluateAt(x, side);
                    range.Add(new(x, side, field.Bending[(int)component]));
                }
            }
        }
        return new(w.Result, v.Result, m.Result);

        void AddEndpoint(double x, FieldSide side)
        {
            BendingField field = fields.EvaluateAt(x, side);
            w.Add(new(x, side, field.Bending[0]));
            v.Add(new(x, side, field.Bending[3]));
            m.Add(new(x, side, field.Bending[2]));
        }
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

internal sealed record BendingResult(BendingFields Fields, ImmutableArray<BendingStation> Stations,
    ImmutableArray<BendingReaction> Reactions, ImmutableArray<BendingPiece> Pieces, BendingExtrema Extrema, BendingQuality Quality)
{
    internal BendingField EvaluateAt(double x, FieldSide side) => Fields.EvaluateAt(x, side);
}
