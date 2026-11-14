using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal enum BendingComponent { W, Theta, M, V }
internal readonly record struct FreeBendingComponent(int StationIndex, BendingComponent Component);

internal sealed record BendingSystem(int Dimension, ImmutableArray<double> A, ImmutableArray<long> K,
    ImmutableArray<double> B, ImmutableArray<double> BB, ImmutableArray<MechanicalStation> Stations,
    ImmutableArray<FreeBendingComponent> FreeComponents, ImmutableArray<int> ReducedColumns,
    int EL, int EEI, int EF, ImmutableArray<int> StateExponents, int ForceExponent, int MomentExponent,
    int LineLoadExponent, bool IsUnloaded, ExcitationBounds Excitation, ImmutableArray<RoundCounted> IntervalEI,
    ImmutableArray<EventStation> EventStations, PreparedBendingLoads Loads)
{
    /// <summary>The smallest normalized stiffness of any interval: the conservative scale for rotations.</summary>
    internal RoundCounted LeastEI => IntervalEI.MinBy(e => e.Value);
}

internal static class BendingSystemBuilder
{
    internal static BendingSystem Build(StationModel model, BendingPreparation preparation, PreparedBendingLoads loads)
    {
        ImmutableArray<MechanicalStation> stations = model.MechanicalStations;
        var free = ImmutableArray.CreateBuilder<FreeBendingComponent>();
        int[] columns = Enumerable.Repeat(-1, checked(stations.Length * 4)).ToArray();
        for (int i = 0; i < stations.Length; i++)
            for (int q = 0; q < 4; q++)
            {
                bool prescribed = q == 0 && stations[i].RigidW ||
                    q == 1 && stations[i].RigidTheta ||
                    i == stations.Length - 1 && q >= 2;
                if (prescribed)
                    continue;
                columns[4 * i + q] = free.Count;
                free.Add(new(i, (BendingComponent)q));
            }

        int n = free.Count;
        var a = new double[checked(n * n)];
        var k = new long[a.Length];
        var b = new double[n];
        var bb = new double[n];
        int row = 0;
        for (int i = 0; i < stations.Length; i++)
        {
            TransferCoefficients co = i == 0 ? default : TransferCoefficients.Create(
                stations[i - 1].Position, stations[i].Position, preparation, i - 1);
            var transferred = new Bounded[4];
            if (i > 0)
                for (int q = 0; q < 4; q++)
                {
                    transferred[q] = loads.Segments[i - 1].Integral(stations[i].Position, 4 - q);
                    if (q < 2)
                        transferred[q] /= new Bounded(preparation.IntervalEI[i - 1].Value, preparation.IntervalEI[i - 1].Bound());
                }
            bool rigidW = stations[i].RigidW;
            bool rigidTheta = stations[i].RigidTheta;
            if (i > 0)
            {
                Jump(0, default);
                Jump(1, default);
            }
            if (!rigidTheta)
                Jump(2, -loads.Stations[i].Moment);
            if (!rigidW)
                Jump(3, loads.Stations[i].Force);

            void Jump(int q, Bounded stationLoad)
            {
                if (row >= n)
                    throw new KernelFailureException(KernelFailureCause.InternalInconsistency, "Too many bending equations.");
                Put(4 * i + q, new(1));
                // The transverse spring law enters the shear jump: V+ - V- + k w+ = P.
                if (q == 3 && stations[i].ElasticW)
                    Put(4 * i, preparation.ScaledTranslationalStiffness(stations[i].Springs.Translational));
                // The rotational spring law enters the moment jump: M+ - M- - k_theta theta+ = -C.
                if (q == 2 && stations[i].ElasticTheta)
                    Put(4 * i + 1, -preparation.ScaledRotationalStiffness(stations[i].Springs.Rotational));
                if (i > 0)
                {
                    int previous = 4 * (i - 1);
                    Put(previous + q, new(-1));
                    switch (q)
                    {
                        case 0:
                            Put(previous + 1, -co.A);
                            Put(previous + 2, -co.A2);
                            Put(previous + 3, -co.A3);
                            break;
                        case 1:
                            Put(previous + 2, -co.AE);
                            Put(previous + 3, -co.A2);
                            break;
                        case 2:
                            Put(previous + 3, -co.A);
                            break;
                    }
                }
                // As in jump(), magnitude is reset to the stored constant before the RHS addition.
                Bounded propagatedLoad = transferred[q];
                Bounded rhs = new Bounded(propagatedLoad.Value, propagatedLoad.Error) +
                    new Bounded(stationLoad.Value, stationLoad.Error);
                if (!Binary64.IsFinite(rhs.Error))
                    throw new NumericalRangeException("The bending RHS bound is not finite.");
                b[row] = rhs.Value;
                bb[row] = rhs.Error;
                row++;
            }

            void Put(int fullColumn, RoundCounted value)
            {
                int column = columns[fullColumn];
                if (column < 0)
                    return;
                int index = row * n + column;
                a[index] = value.Value;
                k[index] = value.Count;
            }
        }
        if (row != n)
            throw new KernelFailureException(KernelFailureCause.InternalInconsistency, "Bending equations and unknowns differ.");

        return new(n, a.ToImmutableArray(), k.ToImmutableArray(), b.ToImmutableArray(), bb.ToImmutableArray(),
            stations, free.ToImmutable(), columns.ToImmutableArray(), preparation.EL, preparation.EEI, preparation.EF,
            preparation.StateExponents, preparation.ForceExponent, preparation.MomentExponent,
            preparation.LineLoadExponent, preparation.IsUnloaded, preparation.Excitation, preparation.IntervalEI,
            model.EventStations, loads);
    }
}
