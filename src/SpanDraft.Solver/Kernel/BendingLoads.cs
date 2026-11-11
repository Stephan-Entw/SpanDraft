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
}

internal readonly record struct ScaledPointLoad(double Position, double Value);
internal readonly record struct ScaledLineLoad(double Start, double End, double Value);
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
            total += BendingLoadTerms.Difference(new(line.Value), near, far, width, p, inside);
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
        foreach (ConstantLineLoad load in input.LineLoads)
        {
            while (firstSegment < count - 2 && stations[firstSegment + 1].Position <= load.Start)
                firstSegment++;
            double value = Binary64.ScaleByPowerOfTwo(load.NewtonsPerMeter, preparation.LineLoadExponent);
            for (int j = firstSegment; j < count - 1 && stations[j].Position < load.End; j++)
            {
                double start = Math.Max(load.Start, stations[j].Position);
                double end = Math.Min(load.End, stations[j + 1].Position);
                if (end > start)
                    lines[j].Add(new(start, end, value));
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
}
