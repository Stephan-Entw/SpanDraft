using System.Numerics;

namespace SpanDraft.Solver.Kernel;

/// <summary>
/// The exact rank of the rigid-body modes of the restraints. Without releases the beam is one body with the
/// two bending modes w = a + b·x; every moment release cuts it into one more body with two modes of its own.
/// </summary>
internal readonly record struct MechanicalStability(int BendingRank, bool AxiallyRestrained, int BendingModes = 2)
{
    internal bool IsStable => BendingRank == BendingModes && AxiallyRestrained;

    internal static MechanicalStability Check(StationModel stations)
    {
        // The bodies are the stretches between the cuts. A release is a point of two bodies; the axial force
        // passes through it, so the release changes nothing for the axial mode.
        double[] cuts = [0, .. stations.MechanicalStations.Where(station => station.Release)
            .Select(station => station.Position), stations.MechanicalStations[^1].Position];
        int bodies = cuts.Length - 1;
        int columns = 2 * bodies;
        var rows = new List<BigInteger[]>();
        bool axial = false;
        foreach (MechanicalStation station in stations.MechanicalStations)
        {
            axial |= station.Directions.HasFlag(RestrainedDirections.U);
            for (int body = 0; body < bodies; body++)
            {
                if (station.Position < cuts[body] || station.Position > cuts[body + 1])
                    continue;
                if (station.ConstrainsW)
                    rows.Add(Row(columns, (2 * body, Binary64.ExactUnits(1)), (2 * body + 1, Binary64.ExactUnits(station.Position))));
                if (station.ConstrainsTheta)
                    rows.Add(Row(columns, (2 * body + 1, BigInteger.One)));
            }
            if (station.Release)
            {
                // w is continuous across the release: a + b·h of the left body equals that of the right one.
                int body = Array.IndexOf(cuts, station.Position, 1) - 1;
                rows.Add(Row(columns, (2 * body, Binary64.ExactUnits(1)), (2 * body + 1, Binary64.ExactUnits(station.Position)),
                    (2 * body + 2, -Binary64.ExactUnits(1)), (2 * body + 3, -Binary64.ExactUnits(station.Position))));
            }
        }

        return new(ExactRank(rows, columns), axial, columns);
    }

    private static BigInteger[] Row(int columns, params (int Column, BigInteger Value)[] entries)
    {
        var row = new BigInteger[columns];
        foreach ((int column, BigInteger value) in entries)
            row[column] = value;
        return row;
    }

    // Fraction-free Bareiss on integer rows; coordinates are never rounded or classified by distance.
    private static int ExactRank(List<BigInteger[]> rows, int columns)
    {
        int rank = 0;
        BigInteger previous = BigInteger.One;
        for (int column = 0; column < columns && rank < rows.Count; column++)
        {
            int pivot = rows.FindIndex(rank, row => !row[column].IsZero);
            if (pivot < 0)
                continue;
            (rows[rank], rows[pivot]) = (rows[pivot], rows[rank]);
            BigInteger value = rows[rank][column];
            for (int i = rank + 1; i < rows.Count; i++)
            {
                BigInteger multiplier = rows[i][column];
                for (int j = 0; j < columns; j++)
                    rows[i][j] = (value * rows[i][j] - multiplier * rows[rank][j]) / previous;
            }
            previous = value;
            rank++;
        }
        return rank;
    }
}

internal sealed class MechanicalMechanismException(MechanicalStability stability)
    : ArithmeticException("The mechanical restraints leave a rigid-body mode.")
{
    internal MechanicalStability Stability { get; } = stability;
}
