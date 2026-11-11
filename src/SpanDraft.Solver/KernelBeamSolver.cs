using SpanDraft.Core.Beams;
using SpanDraft.Core.Validation;
using SpanDraft.Solver.Kernel;

namespace SpanDraft.Solver;

internal static class KernelBeamSolver
{
    internal static BeamSolution Solve(BeamModel beam)
    {
        ArgumentNullException.ThrowIfNull(beam);
        IReadOnlyList<ValidationError> errors = BeamModelValidator.Validate(beam);
        if (errors.Count != 0)
            throw new BeamSolverException(SolverErrorCode.InvalidModel,
                "Beam model validation failed. Inspect ValidationErrors for all model errors.", errors);

        return new BeamSolution(beam, SolverKernel.Solve(BeamModelAdapter.Adapt(beam)));
    }
}
