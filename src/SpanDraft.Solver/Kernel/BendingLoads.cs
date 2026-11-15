using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal static class BendingLoadTerms
{
    internal static Bounded PointForce(Bounded force, Bounded distance, int p)
    {
        if (p == 1)
            return force;
        Bounded term = force * distance;
        if (p == 2)
            return term;
        term *= distance;
        return p == 3 ? term / new Bounded(2) : (term * distance) / new Bounded(6);
    }

    internal static Bounded PointMoment(Bounded moment, Bounded distance, int p)
    {
        Bounded term = -moment;
        if (p == 2)
            return term;
        term *= distance;
        return p == 3 ? term : (term * distance) / new Bounded(2);
    }

    internal static Bounded Difference(Bounded q, Bounded near, Bounded far, Bounded width, int p, bool inside)
    {
        if (inside)
        {
            Bounded term = q * near;
            for (int i = 1; i < p; i++)
                term *= near;
            return term / new Bounded(p switch { 1 => 1, 2 => 2, 3 => 6, _ => 24 });
        }
        Bounded t = q * width;
        return p switch
        {
            1 => t,
            2 => (t * (near + far)) / new Bounded(2),
            3 => (t * ((near * near + near * far) + far * far)) / new Bounded(6),
            _ => ((t * (near + far)) * (near * near + far * far)) / new Bounded(24)
        };
    }

    /// <summary>
    /// The p-th integral of a linear ramp with rise <paramref name="rise"/> over a load of width
    /// <paramref name="width"/>: zero at the load start, the rise at its end. Outside the load every
    /// factor is a positive distance, so cancellation is avoided for short loads far from the origin.
    /// </summary>
    internal static Bounded Ramp(Bounded rise, Bounded near, Bounded far, Bounded width, int p, bool inside)
    {
        if (inside)
        {
            // The quotient comes first, so rise * (n / d) cannot overflow for a short load.
            Bounded term = rise * (near / width);
            for (int i = 0; i < p; i++)
                term *= near;
            return term / new Bounded(p switch { 1 => 2, 2 => 6, 3 => 24, _ => 120 });
        }
        Bounded t = rise * width;
        return p switch
        {
            1 => t / new Bounded(2),
            2 => (t * (width + far * new Bounded(3))) / new Bounded(6),
            3 => (t * ((width * width + (width * far) * new Bounded(4)) + (far * far) * new Bounded(6)))
                / new Bounded(24),
            _ => (t * (((width * width * width + ((width * width) * far) * new Bounded(5)) +
                ((width * far) * far) * new Bounded(10)) + ((far * far) * far) * new Bounded(10)))
                / new Bounded(120)
        };
    }
}

internal readonly record struct ScaledPointLoad(double Position, double Value);
/// <summary>
/// A line load clipped to one mechanical section, in normalized units. The end intensities carry the data
/// error of the clip. <paramref name="HasRamp"/> is false for constant loads, which have no ramp term.
/// </summary>
internal readonly record struct ScaledLineLoad(double Start, double End, double StartValue, double StartError,
    double EndValue, double EndError, bool HasRamp)
{
    internal ScaledLineLoad(double start, double end, double value) : this(start, end, value, 0, value, 0, false)
    {
    }

    internal Bounded StartIntensity => new(StartValue, StartError);
    internal Bounded EndIntensity => new(EndValue, EndError);
    internal Bounded Rise => EndIntensity - StartIntensity;

    /// <summary>The intensity at x within the clipped load, with its data and evaluation error.</summary>
    internal Bounded IntensityAt(double x, int lengthExponent)
    {
        if (!HasRamp || x == Start)
            return StartIntensity;
        if (x == End)
            return EndIntensity;
        return Interpolate(StartIntensity, EndIntensity, Start, End, x, lengthExponent);
    }

    /// <summary>
    /// The linear interpolation first + (last - first) · share between the load ends, with the share taken from
    /// global distances so that it stays exact for short loads far from the origin.
    /// </summary>
    internal static Bounded Interpolate(Bounded first, Bounded last, double start, double end, double x,
        int lengthExponent)
    {
        Bounded share = RoundCounted.BoundedDistance(x, start, lengthExponent) /
            RoundCounted.BoundedDistance(end, start, lengthExponent);
        return first + (last - first) * share;
    }
}
internal readonly record struct StationBendingLoads(Bounded Force, Bounded Moment);

internal sealed record SegmentBendingLoads(ImmutableArray<ScaledPointLoad> Forces,
    ImmutableArray<ScaledPointLoad> Moments, ImmutableArray<ScaledLineLoad> Lines, int LengthExponent)
{
    internal Bounded Integral(double x, int p, FieldSide side = FieldSide.Left)
    {
        Bounded total = default;
        foreach (ScaledPointLoad force in Forces)
            if (x > force.Position || p == 1 && x == force.Position && side == FieldSide.Right)
                total += BendingLoadTerms.PointForce(new(force.Value), Distance(x, force.Position), p);
        if (p >= 2)
            foreach (ScaledPointLoad moment in Moments)
                if (x > moment.Position || p == 2 && x == moment.Position && side == FieldSide.Right)
                    total += BendingLoadTerms.PointMoment(new(moment.Value), Distance(x, moment.Position), p);
        foreach (ScaledLineLoad line in Lines)
        {
            if (!(x > line.Start))
                continue;
            bool inside = x <= line.End;
            Bounded near = Distance(x, line.Start);
            Bounded far = inside ? default : Distance(x, line.End);
            Bounded width = Distance(line.End, line.Start);
            total += BendingLoadTerms.Difference(line.StartIntensity, near, far, width, p, inside);
            if (line.HasRamp)
                total += BendingLoadTerms.Ramp(line.Rise, near, far, width, p, inside);
        }
        return total;
    }

    private Bounded Distance(double x, double origin) =>
        RoundCounted.BoundedDistance(x, origin, LengthExponent);
}

internal sealed record PreparedBendingLoads(ImmutableArray<SegmentBendingLoads> Segments,
    ImmutableArray<StationBendingLoads> Stations)
{
    internal static PreparedBendingLoads Create(MechanicalInput input, StationModel model, BendingPreparation preparation)
    {
        ImmutableArray<MechanicalStation> stations = model.MechanicalStations;
        int count = stations.Length;
        var forces = Enumerable.Range(0, count - 1).Select(_ => new List<ScaledPointLoad>()).ToArray();
        var moments = Enumerable.Range(0, count - 1).Select(_ => new List<ScaledPointLoad>()).ToArray();
        var lines = Enumerable.Range(0, count - 1).Select(_ => new List<ScaledLineLoad>()).ToArray();
        var stationForces = new Bounded[count];
        var stationMoments = new Bounded[count];

        AssignPoints(input.PointForces.Select(f => new ScaledPointLoad(f.Position,
            Binary64.ScaleByPowerOfTwo(f.Newtons, preparation.ForceExponent))), forces, stationForces);
        AssignPoints(input.PointMoments.Select(m => new ScaledPointLoad(m.Position,
            Binary64.ScaleByPowerOfTwo(m.NewtonMeters, preparation.MomentExponent))), moments, stationMoments);

        int firstSegment = 0;
        foreach (TransverseLineLoad load in input.LineLoads)
        {
            while (firstSegment < count - 2 && stations[firstSegment + 1].Position <= load.Start)
                firstSegment++;
            double first = Binary64.ScaleByPowerOfTwo(load.StartNewtonsPerMeter, preparation.LineLoadExponent);
            double last = Binary64.ScaleByPowerOfTwo(load.EndNewtonsPerMeter, preparation.LineLoadExponent);
            for (int j = firstSegment; j < count - 1 && stations[j].Position < load.End; j++)
            {
                double start = Math.Max(load.Start, stations[j].Position);
                double end = Math.Min(load.End, stations[j + 1].Position);
                if (end <= start)
                    continue;
                lines[j].Add(load.IsConstant
                    ? new(start, end, first)
                    : Clip(load, start, end, first, last, preparation.EL));
            }
        }

        return new(Enumerable.Range(0, count - 1).Select(j => new SegmentBendingLoads(
                forces[j].ToImmutableArray(), moments[j].ToImmutableArray(), lines[j].ToImmutableArray(),
                preparation.EL)).ToImmutableArray(),
            Enumerable.Range(0, count).Select(i => new StationBendingLoads(stationForces[i], stationMoments[i]))
                .ToImmutableArray());

        void AssignPoints(IEnumerable<ScaledPointLoad> points, List<ScaledPointLoad>[] segments, Bounded[] jumps)
        {
            int station = 0;
            foreach (ScaledPointLoad point in points)
            {
                while (station < count - 1 && stations[station + 1].Position <= point.Position)
                    station++;
                if (stations[station].Position == point.Position)
                    jumps[station] += new Bounded(point.Value);
                else
                    segments[station].Add(point);
            }
        }
    }

    /// <summary>
    /// Clips a linear load to one mechanical section. The end intensities are interpolated from global
    /// distances; the interpolation error enters their bounds. A clip end that coincides with a load end
    /// takes that end intensity exactly.
    /// </summary>
    private static ScaledLineLoad Clip(TransverseLineLoad load, double start, double end, double first,
        double last, int lengthExponent)
    {
        // A clipped width that cannot be normalized would make the ramp divisor unusable.
        if (RoundCounted.BoundedDistance(end, start, lengthExponent).Value < Binary64.ScaleByPowerOfTwo(1, -969))
            throw new NumericalRangeException("The normalized clipped distributed load length is too small.");
        Bounded a = ClippedIntensity(start);
        Bounded b = ClippedIntensity(end);
        return new(start, end, a.Value, a.Error, b.Value, b.Error, true);

        Bounded ClippedIntensity(double position)
        {
            if (position == load.Start)
                return new(first);
            if (position == load.End)
                return new(last);
            return ScaledLineLoad.Interpolate(new(first), new(last), load.Start, load.End, position, lengthExponent);
        }
    }
}
