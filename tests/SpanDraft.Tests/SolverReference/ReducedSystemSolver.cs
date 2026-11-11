using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Solver;

namespace SpanDraft.Tests.SolverReference;

internal static class ReducedSystemSolver
{
    internal const double MinimumSingularValueRatio = 1e-12;
    internal const double MaximumBackwardError = 1e-10;

    internal static Vector<double> Solve(BeamSystem system, DofMap dofs)
    {
        var displacements = Vector<double>.Build.Dense(dofs.Count);
        int count = dofs.FreeDofs.Count;
        if (count == 0)
            return displacements;
        var stiffness = Matrix<double>.Build.Dense(count, count, (i, j) => system.Stiffness[dofs.FreeDofs[i], dofs.FreeDofs[j]]);
        var free = Vector<double>.Build.Dense(count, i => system.Loads[dofs.FreeDofs[i]]);
        Vector<double> solved = SolveSymmetric(stiffness, free);
        for (int i = 0; i < count; i++)
            displacements[dofs.FreeDofs[i]] = solved[i];
        return displacements;
    }

    /// <summary>Solves an unsymmetric, possibly indefinite system after equilibrating rows and columns.</summary>
    internal static Vector<double> SolveGeneral(Matrix<double> matrix, Vector<double> free)
    {
        int count = free.Count;
        double[] rows = Enumerable.Repeat(1.0, count).ToArray(), columns = Enumerable.Repeat(1.0, count).ToArray();
        for (int pass = 0; pass < 8; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                double largest = 0;
                for (int j = 0; j < count; j++) largest = Math.Max(largest, Math.Abs(rows[i] * matrix[i, j] * columns[j]));
                if (largest > 0) rows[i] /= Math.Sqrt(largest);
            }
            for (int j = 0; j < count; j++)
            {
                double largest = 0;
                for (int i = 0; i < count; i++) largest = Math.Max(largest, Math.Abs(rows[i] * matrix[i, j] * columns[j]));
                if (largest > 0) columns[j] /= Math.Sqrt(largest);
            }
        }
        var scaled = Matrix<double>.Build.Dense(count, count, (i, j) => rows[i] * matrix[i, j] * columns[j]);
        var loads = Vector<double>.Build.Dense(count, i => rows[i] * free[i]);
        NumericalGuard.AllFinite(scaled.Enumerate(), "Scaled cut system");
        NumericalGuard.AllFinite(loads, "Scaled cut loads");
        Vector<double> singularValues = scaled.Svd(computeVectors: false).S;
        double top = NumericalGuard.Positive(singularValues.Maximum(), "Largest singular value");
        if (singularValues.Minimum() / top <= MinimumSingularValueRatio)
            throw new BeamSolverException(SolverErrorCode.IllConditionedSystem,
                "Hinges or springs leave the beam almost free to move, or springs too close and too different in stiffness prevent a reliable solution.");
        Vector<double> solution = scaled.LU().Solve(loads);
        NumericalGuard.AllFinite(solution, "Cut solution");
        CheckBackwardError(scaled, solution, loads);
        return Vector<double>.Build.Dense(count, i => columns[i] * solution[i]);
    }

    internal static Vector<double> SolveSymmetric(Matrix<double> stiffness, Vector<double> free)
    {
        int count = free.Count;
        var scaling = Vector<double>.Build.Dense(count);
        for (int i = 0; i < count; i++)
        {
            double diagonal = NumericalGuard.Positive(stiffness[i, i], "Reduced stiffness diagonal");
            scaling[i] = NumericalGuard.Positive(1 / Math.Sqrt(diagonal), "Diagonal scaling factor");
        }
        // qf = D*y; A = D*Kff*D and b = D*Ff. K and F remain untouched.
        var matrix = Matrix<double>.Build.Dense(count, count);
        for (int i = 0; i < count; i++)
            for (int j = i; j < count; j++)
            {
                double value = scaling[i] * stiffness[i, j] * scaling[j];
                // Mirror the computed value to preserve symmetry through scaling roundoff.
                matrix[i, j] = matrix[j, i] = value;
            }
        var loads = Vector<double>.Build.Dense(count, i => scaling[i] * free[i]);
        NumericalGuard.AllFinite(matrix.Enumerate(), "Scaled stiffness");
        NumericalGuard.AllFinite(loads, "Scaled loads");

        Vector<double> solution;
        try
        {
            Vector<double> singularValues = matrix.Svd(computeVectors: false).S;
            NumericalGuard.AllFinite(singularValues, "Scaled singular values");
            double largest = NumericalGuard.Positive(singularValues.Maximum(), "Largest singular value");
            if (singularValues.Minimum() / largest <= MinimumSingularValueRatio)
                throw new BeamSolverException(SolverErrorCode.IllConditionedSystem,
                    "The scaled reduced stiffness is singular or too poorly conditioned for a reliable solution.");

            // Check/factor even with zero loads: an unloaded model still must be solvable.
            solution = matrix.Cholesky().Solve(loads);
        }
        catch (Exception exception) when (exception is ArgumentException or NonConvergenceException or NumericalBreakdownException)
        {
            throw new BeamSolverException(SolverErrorCode.NumericalFailure,
                "Math.NET could not factor or solve the reduced stiffness system.", innerException: exception);
        }
        NumericalGuard.AllFinite(solution, "Scaled displacements");
        CheckBackwardError(matrix, solution, loads);
        var displacements = Vector<double>.Build.Dense(count);
        for (int i = 0; i < count; i++)
            displacements[i] = NumericalGuard.Finite(scaling[i] * solution[i], "Nodal displacement");
        return displacements;
    }

    internal static void CheckBackwardError(Matrix<double> matrix, Vector<double> solution, Vector<double> loads)
    {
        Vector<double> residual = matrix * solution - loads;
        NumericalGuard.AllFinite(residual, "Scaled residual");
        double denominator = NumericalGuard.Finite(matrix.InfinityNorm() * solution.InfinityNorm() + loads.InfinityNorm(),
            "Backward error denominator");
        double residualNorm = residual.InfinityNorm();
        if ((denominator == 0 && residualNorm != 0) ||
            (denominator > 0 && residualNorm / denominator > MaximumBackwardError))
            throw new BeamSolverException(SolverErrorCode.NumericalFailure,
                "The reduced solution failed the relative backward error check.");
    }
}
