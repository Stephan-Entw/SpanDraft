using System.Numerics;

namespace SpanDraft.Solver.Kernel;

internal readonly record struct MechanicalStability(int BendingRank, bool AxiallyRestrained)
{
    internal bool IsStable => BendingRank == 2 && AxiallyRestrained;

    internal static MechanicalStability Check(StationModel stations)
    {
        var rows = new List<BigInteger[]>();
        bool axial = false;
        foreach (MechanicalStation station in stations.MechanicalStations)
        {
            axial |= station.Directions.HasFlag(RestrainedDirections.U);
            if (station.Directions.HasFlag(RestrainedDirections.W))
                rows.Add([Binary64.ExactUnits(1), Binary64.ExactUnits(station.Position)]);
            if (station.Directions.HasFlag(RestrainedDirections.Theta))
                rows.Add([BigInteger.Zero, BigInteger.One]);
        }

        return new(ExactRank(rows, 2), axial);
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
