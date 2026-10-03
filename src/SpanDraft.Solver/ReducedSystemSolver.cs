using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;

namespace SpanDraft.Solver;

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

        var scaling = Vector<double>.Build.Dense(count);
        for (int i = 0; i < count; i++)
        {
            double diagonal = NumericalGuard.Positive(system.Stiffness[dofs.FreeDofs[i], dofs.FreeDofs[i]],
                "Reduced stiffness diagonal");
            scaling[i] = NumericalGuard.Positive(1 / Math.Sqrt(diagonal), "Diagonal scaling factor");
        }
        // qf = D*y; A = D*Kff*D and b = D*Ff. K and F remain untouched.
        var matrix = Matrix<double>.Build.Dense(count, count);
        for (int i = 0; i < count; i++)
            for (int j = i; j < count; j++)
            {
                double value = scaling[i] * system.Stiffness[dofs.FreeDofs[i], dofs.FreeDofs[j]] * scaling[j];
                // Mirror the computed value to preserve symmetry through scaling roundoff.
                matrix[i, j] = matrix[j, i] = value;
            }
        var loads = Vector<double>.Build.Dense(count, i => scaling[i] * system.Loads[dofs.FreeDofs[i]]);
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
        for (int i = 0; i < count; i++)
            displacements[dofs.FreeDofs[i]] = NumericalGuard.Finite(scaling[i] * solution[i], "Nodal displacement");
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
