using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;
using static SpanDraft.Solver.Kernel.Directed;

namespace SpanDraft.Solver.Kernel;

internal sealed record PieceQuality(double Start, double End, ImmutableArray<double> Bounds,
    ImmutableArray<double> Denominators, ImmutableArray<double> UpperValues);
internal readonly record struct OutputGateCheck(double BoundSI, double DenominatorSI, double Ratio, bool Passed);
internal readonly record struct GateLocation(string Kind, double Position, FieldSide? Side, BendingComponent Component);
internal sealed record BendingQuality(bool IsUnloaded, bool IsVerified, bool GatePassed,
    ImmutableArray<double> ScalesLow, ImmutableArray<double> ScalesHigh,
    ImmutableArray<double> ScalesLowSI, ImmutableArray<double> ScalesHighSI,
    ImmutableArray<PieceQuality> Pieces, ImmutableArray<double> MaxRatios, GateLocation? Worst,
    double? Mu, ImmutableArray<double> TestedMus, int PrimaryIterations, int StateAdjoints,
    int FunctionalAdjoints, int AdjointIterations, int LuSolveCalls, int LuTransposeSolveCalls,
    int StationChecks, int ReactionChecks, int PieceChecks);

internal static class BendingOutputQuality
{
    internal static readonly double SiMin = ScaleByPowerOfTwo(1, -969);
    internal static readonly double SiMax = ScaleByPowerOfTwo(1, 1000);

    internal static (ImmutableArray<double> Low, ImmutableArray<double> High) Scales(BendingFields fields,
        ExcitationBounds excitation, IEnumerable<NormalizedBendingField> stations)
    {
        double[] low = new double[4], high = new double[4];
        foreach (NormalizedBendingField station in stations)
            for (int q = 0; q < 4; q++)
            {
                KernelFailureException.RequireFinite(station.Values[q]);
                KernelFailureException.RequireFinite(station.Bounds[q]);
                low[q] = Math.Max(low[q], Subtract(Abs(station.Values[q]), station.Bounds[q], false));
                high[q] = Math.Max(high[q], Add(Abs(station.Values[q]), station.Bounds[q], true));
            }
        double ln = ScaleByPowerOfTwo(fields.Stations[^1].Position, -fields.EL);
        double ld = Outward(ln, false), lu = Outward(ln, true);
        double eiHigh = Add(fields.EI.Value, fields.EI.Bound(), true);
        double eiLow = Subtract(fields.EI.Value, fields.EI.Bound(), false);
        double sm = Math.Max(low[2], Multiply(excitation.Lower, ld, false));
        double sv = Math.Max(Divide(sm, lu, false), excitation.Lower);
        double st = Math.Max(low[1], Divide(Multiply(sm, ld, false), eiHigh, false));
        double sw = Math.Max(Math.Max(low[0], Divide(Multiply(Multiply(sm, ld, false), ld, false), eiHigh, false)),
            Multiply(st, ld, false));
        double hm = Math.Max(high[2], Multiply(excitation.Upper, ln, true));
        double hv = Math.Max(Divide(hm, ln, true), excitation.Upper);
        double ht = Math.Max(high[1], Divide(Multiply(hm, ln, true), eiLow, true));
        double hw = Math.Max(Math.Max(high[0], Divide(Multiply(Multiply(hm, ln, true), ln, true), eiLow, true)),
            Multiply(ht, ln, true));
        return ([Math.Max(sw, 0), Math.Max(st, 0), Math.Max(sm, 0), Math.Max(sv, 0)], [hw, ht, hm, hv]);
    }

    internal static OutputGateCheck Check(double bound, double denominator, int exponent)
    {
        KernelFailureException.RequireFinite(bound);
        KernelFailureException.RequireFinite(denominator);
        KernelFailureException.Require(bound >= 0 && denominator > 0);
        double d = DirectedScale(denominator, exponent, false);
        double b = Add(DirectedScale(Add(bound, Lambda, true), exponent, true), Lambda, true);
        if (!IsFinite(b) || !IsFinite(d) || d <= 0)
            throw new NumericalRangeException("The SI gate budget is outside its range.");
        double ratio = Divide(b, d, true);
        KernelFailureException.RequireFinite(ratio);
        return new(b, d, ratio, b <= ThresholdDown(d));
    }

    internal static double QSum(BendingFields fields, double x0, double x1)
    {
        double sum = 0;
        foreach (ScaledLineLoad line in fields.Loads.Segments[fields.SegmentOf(x0)].Lines)
            if (line.Start <= x0 && line.End >= x1)
                sum = Add(sum, Add(Abs(line.Value), 0, true), true);
        KernelFailureException.RequireFinite(sum);
        return sum;
    }

    internal static PieceQuality Piece(BendingFields fields, double x0, double x1,
        NormalizedBendingField start, NormalizedBendingField end, ImmutableArray<double> scales)
    {
        double h = DirectedScale(Outward(x1 - x0, true), -fields.EL, true);
        double qs = QSum(fields, x0, x1);
        double[] b = Enumerable.Range(0, 4).Select(q => Math.Max(start.Bounds[q], end.Bounds[q])).ToArray();
        double[] d = scales.ToArray();
        double lv = Subtract(Subtract(Math.Max(Abs(start.Values[3]), Abs(end.Values[3])), Multiply(h, qs, true), false),
            Multiply(2, b[3], true), false);
        d[3] = Math.Max(d[3], lv);
        double lm = double.NegativeInfinity;
        foreach (NormalizedBendingField edge in new[] { start, end })
        {
            double lower = Subtract(Subtract(Abs(edge.Values[2]),
                Multiply(Add(Abs(edge.Values[3]), b[3], true), h, true), false),
                Multiply(Multiply(qs, Multiply(h, h, true), true), 0.5, true), false);
            lm = Math.Max(lm, lower);
        }
        d[2] = Math.Max(d[2], Subtract(lm, Multiply(2, b[2], true), false));
        double uv = Sum(Abs(start.Values[3]), b[3], Multiply(h, qs, true));
        double um = Sum(Abs(start.Values[2]), b[2], Multiply(uv, h, true));
        double el = Subtract(fields.EI.Value, fields.EI.Bound(), false);
        double ut = Sum(Abs(start.Values[1]), b[1], Divide(Multiply(um, h, true), el, true));
        double uw = Sum(Abs(start.Values[0]), b[0], Multiply(ut, h, true));
        double[] upper = [uw, ut, um, uv];
        for (int q = 0; q < 4; q++)
            upper[q] = Add(upper[q], b[q], true);
        return new(x0, x1, b.ToImmutableArray(), d.ToImmutableArray(), upper.ToImmutableArray());
    }

    private static double Sum(double a, double b, double c) => Add(Add(Add(0, a, true), b, true), c, true);

    internal static BendingQuality Evaluate(VerifiedBendingState state, BendingFields fields,
        ImmutableArray<EventStation> events, ImmutableArray<NormalizedReaction> reactions, BendingSolveContext? context)
    {
        ImmutableArray<double> zero = [0, 0, 0, 0];
        if (state.IsUnloaded)
            return new(true, true, true, zero, zero, zero, zero,
                events.Zip(events.Skip(1), (a, b) => new PieceQuality(a.Position, b.Position, zero, zero, zero)).ToImmutableArray(),
                zero, null, null, [], 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var sides = events.SelectMany(e => PhysicalSides(e.Position, fields.Stations[^1].Position)
            .Select(side => (e.Position, Side: side, Field: fields.EvaluateNormalized(e.Position, side)))).ToArray();
        var (low, high) = Scales(fields, state.System.Excitation, sides.Select(s => s.Field));
        var lowSI = ImmutableArray.CreateBuilder<double>(4);
        var highSI = ImmutableArray.CreateBuilder<double>(4);
        for (int q = 0; q < 4; q++)
        {
            var (lower, upper) = ScaleRange(low[q], high[q], fields.Exponents[q]);
            lowSI.Add(lower);
            highSI.Add(upper);
        }
        bool passed = true;
        var ratios = new double[4];
        GateLocation? worst = null;
        double max = 0;
        int stationChecks = 0, reactionChecks = 0, pieceChecks = 0;
        void Test(double bound, double denominator, int q, string kind, double position, FieldSide? side)
        {
            OutputGateCheck check = Check(bound, denominator, fields.Exponents[q]);
            passed &= check.Passed;
            switch (kind)
            {
                case "station": stationChecks++; break;
                case "reaction": reactionChecks++; break;
                case "piece": pieceChecks++; break;
            }
            ratios[q] = Math.Max(ratios[q], check.Ratio);
            if (check.Ratio > max)
            {
                max = check.Ratio;
                worst = new(kind, position, side, (BendingComponent)q);
            }
        }
        foreach (var s in sides)
            for (int q = 0; q < 4; q++)
            {
                double d = q < 2 ? low[q] : Math.Max(low[q], Subtract(Abs(s.Field.Values[q]), s.Field.Bounds[q], false));
                Test(s.Field.Bounds[q], d, q, "station", s.Position, s.Side);
            }
        foreach (NormalizedReaction reaction in reactions)
        {
            if (reaction.Direction == RestrainedDirections.U) continue;
            int q = reaction.Component;
            CheckMagnitude(Abs(reaction.Value), fields.Exponents[q]);
            Test(reaction.Bound, Math.Max(low[q], Subtract(Abs(reaction.Value), reaction.Bound, false)),
                q, "reaction", reaction.Position, null);
        }
        var pieces = ImmutableArray.CreateBuilder<PieceQuality>(events.Length - 1);
        for (int i = 1; i < events.Length; i++)
        {
            double x0 = events[i - 1].Position, x1 = events[i].Position;
            PieceQuality piece = Piece(fields, x0, x1, fields.EvaluateNormalized(x0, FieldSide.Right),
                fields.EvaluateNormalized(x1, FieldSide.Left), low);
            for (int q = 0; q < 4; q++)
            {
                Test(piece.Bounds[q], piece.Denominators[q], q, "piece", x0, null);
                CheckMagnitude(piece.UpperValues[q], fields.Exponents[q]);
            }
            pieces.Add(piece);
        }
        if (!passed)
            throw new KernelFailureException(KernelFailureCause.Gate, $"The directed SI output gate failed at {worst}.");
        return new(false, true, true, low, high, lowSI.MoveToImmutable(), highSI.MoveToImmutable(), pieces.MoveToImmutable(),
            ratios.ToImmutableArray(), worst, state.SelectedMu, state.TestedMus, state.PrimaryIterations, state.AdjointSolves,
            context!.FunctionalSolves, state.AdjointIterations + context.FunctionalIterations,
            context.Lu.SolveCalls, context.Lu.TransposeSolveCalls, stationChecks, reactionChecks, pieceChecks);
    }

    internal static void CheckMagnitude(double magnitude, int exponent)
    {
        KernelFailureException.RequireFinite(magnitude);
        if (DirectedScale(magnitude, exponent, true) > SiMax)
            throw new NumericalRangeException("The SI value majorant exceeds SI_MAX.");
    }

    internal static (double Low, double High) ScaleRange(double low, double high, int exponent)
    {
        KernelFailureException.RequireFinite(low);
        KernelFailureException.RequireFinite(high);
        double lower = DirectedScale(low, exponent, false), upper = DirectedScale(high, exponent, true);
        if (!(lower >= SiMin && upper <= SiMax))
            throw new NumericalRangeException("The SI reference scale is outside its range.");
        return (lower, upper);
    }

    internal static IEnumerable<FieldSide> PhysicalSides(double x, double length)
    {
        if (x != 0) yield return FieldSide.Left;
        if (x != length) yield return FieldSide.Right;
    }
}
