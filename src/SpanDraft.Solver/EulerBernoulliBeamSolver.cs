using SpanDraft.Core.Beams;
using SpanDraft.Core.Validation;
using SpanDraft.Solver.Kernel;

namespace SpanDraft.Solver;

/// <summary>Linear elastic Euler-Bernoulli analysis of one straight beam along global x.</summary>
public sealed class EulerBernoulliBeamSolver
{
    /// <summary>
    /// The only product entry: Core model, adapter, kernel, <see cref="BeamSolution"/>. Every expected
    /// rejection is a <see cref="BeamSolverException"/> whose inner exception keeps the kernel cause.
    /// </summary>
    public BeamSolution Solve(BeamModel beam)
    {
        ArgumentNullException.ThrowIfNull(beam);
        IReadOnlyList<ValidationError> errors = BeamModelValidator.Validate(beam);
        if (errors.Count != 0)
            throw new BeamSolverException(SolverErrorCode.InvalidModel,
                "Beam model validation failed. Inspect ValidationErrors for all model errors.", errors);

        try
        {
            return new BeamSolution(beam, SolverKernel.Solve(Adapt(beam)));
        }
        catch (MechanicalMechanismException mechanism)
        {
            throw new BeamSolverException(SolverErrorCode.UnstableModel,
                "Supports leave an axial translation or a transverse rigid-body motion unconstrained.",
                innerException: mechanism);
        }
        catch (NumericalRangeException range)
        {
            throw BeamSolverException.Rejected(range.Cause, range);
        }
        catch (KernelFailureException failure)
        {
            throw BeamSolverException.Rejected(failure.Cause, failure);
        }
    }

    private static MechanicalInput Adapt(BeamModel beam)
    {
        try
        {
            return BeamModelAdapter.Adapt(beam);
        }
        catch (NumericalRangeException)
        {
            // The exact mechanism check precedes every range rule, including the adapter's EI product.
            SolverKernel.RequireStable(BeamModelAdapter.AdaptRestraints(beam));
            throw;
        }
    }
}
