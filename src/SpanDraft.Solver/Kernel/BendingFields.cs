using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal enum FieldSide { Left, Right, Interior }
internal readonly record struct VerifiedOutput(double Value, double VerifiedOutputBound);
internal sealed record NormalizedBendingField(ImmutableArray<double> Values, ImmutableArray<double> Bounds);
internal sealed record BendingField(double Position, FieldSide Side, ImmutableArray<VerifiedOutput> Bending)
{
    internal VerifiedOutput U => default;
    internal VerifiedOutput N => default;
}

// Only the immutable data needed for direct evaluation survives the numerical solve.
internal sealed record BendingFields(ImmutableArray<MechanicalStation> Stations, ImmutableArray<double> EventPositions,
    PreparedBendingLoads Loads,
    ImmutableArray<double> State, ImmutableArray<double> StateBound, int EL,
    ImmutableArray<int> Exponents, RoundCounted EI, bool IsUnloaded)
{
    internal static BendingFields From(VerifiedBendingState state) => new(state.System.Stations,
        state.System.EventStations.Select(s => s.Position).ToImmutableArray(),
        state.System.Loads, state.FullState, state.FullStateBound, state.System.EL,
        state.System.StateExponents, state.System.NormalizedEI, state.IsUnloaded);

    internal bool IsEventPosition(double x) => EventPositions.BinarySearch(x) >= 0;

    internal int SegmentOf(double x)
    {
        int low = 0, high = Stations.Length - 1;
        while (high - low > 1)
        {
            int middle = low + (high - low) / 2;
            if (Stations[middle].Position <= x) low = middle;
            else high = middle;
        }
        return low;
    }

    internal NormalizedBendingField EvaluateNormalized(double x, FieldSide side)
    {
        Validate(x, side);
        if (IsUnloaded)
            return new([0, 0, 0, 0], [0, 0, 0, 0]);
        if (x == 0) side = FieldSide.Right;
        if (x == Stations[^1].Position) side = FieldSide.Left;
        int j = SegmentOf(x);
        int station = x == Stations[^1].Position ? Stations.Length - 1 : x == Stations[j].Position ? j : -1;
        if (x == Stations[j].Position)
        {
            if (side == FieldSide.Right)
                return new(State.Slice(4 * j, 4), StateBound.Slice(4 * j, 4));
            j--;
            side = FieldSide.Left;
        }
        Bounded s = RoundCounted.BoundedDistance(x, Stations[j].Position, EL);
        var e = new Bounded(EI.Value, EI.Bound());
        Bounded one = new(1);
        Bounded a2 = ((s * s) / new Bounded(2)) / e;
        Bounded[][] g = [[one, s, a2, (((s * s) * s) / new Bounded(6)) / e],
            [one, s / e, a2], [one, s], [one]];
        var values = ImmutableArray.CreateBuilder<double>(4);
        var bounds = ImmutableArray.CreateBuilder<double>(4);
        for (int q = 0; q < 4; q++)
        {
            Bounded field = default;
            for (int k = q; k < 4; k++)
                field += g[q][k - q] * new Bounded(State[4 * j + k], StateBound[4 * j + k]);
            Bounded load = Loads.Segments[j].Integral(x, 4 - q, side);
            field += q < 2 ? load / e : load;
            KernelFailureException.RequireFinite(field.Value);
            KernelFailureException.RequireFinite(field.Error);
            // Both sides share the verified station kinematics, including exact restrained zeros.
            bool stationKinematics = q < 2 && station >= 0;
            values.Add(stationKinematics ? State[4 * station + q] : field.Value);
            // Retain the propagated bound for open-piece certification as well as the shared state's bound.
            bounds.Add(stationKinematics ? Math.Max(field.Error, StateBound[4 * station + q]) : field.Error);
        }
        return new(values.MoveToImmutable(), bounds.MoveToImmutable());
    }

    internal BendingField EvaluateAt(double x, FieldSide side)
    {
        NormalizedBendingField field = EvaluateNormalized(x, side);
        FieldSide physical = x == 0 ? FieldSide.Right : x == Stations[^1].Position ? FieldSide.Left : side;
        return new(x, physical, Enumerable.Range(0, 4).Select(q =>
            ToSi(field.Values[q], field.Bounds[q], Exponents[q], IsUnloaded)).ToImmutableArray());
    }

    internal static VerifiedOutput ToSi(double value, double bound, int exponent, bool unloaded = false)
    {
        if (unloaded) return default;
        KernelFailureException.RequireFinite(value);
        KernelFailureException.RequireFinite(bound);
        KernelFailureException.Require(bound >= 0);
        if (!IsZero(value) && MagnitudeBits(value) < MagnitudeBits(Lambda))
        {
            value = 0;
            bound = Directed.Add(bound, Lambda, upper: true);
        }
        value = ScaleByPowerOfTwo(value, exponent);
        bound = Directed.DirectedScale(bound, exponent, upper: true);
        if (MagnitudeBits(value) <= MagnitudeBits(Lambda))
            bound = Directed.Add(bound, Lambda, upper: true);
        if (!IsFinite(value) || !IsFinite(bound))
            throw new NumericalRangeException("The SI output or its bound is outside its range.");
        return new(value, bound);
    }

    private void Validate(double x, FieldSide side)
    {
        if (!IsFinite(x) || x < 0 || x > Stations[^1].Position)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (side == FieldSide.Interior && IsEventPosition(x))
            throw new ArgumentException("Interior requires a position inside an open event piece.", nameof(side));
    }
}
