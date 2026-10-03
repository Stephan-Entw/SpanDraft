using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;

namespace SpanDraft.Solver;

/// <summary>Linear elastic Euler-Bernoulli analysis of one straight beam along global x.</summary>
public sealed class EulerBernoulliBeamSolver
{
    public BeamSolution Solve(BeamModel beam)
    {
        ArgumentNullException.ThrowIfNull(beam);
        IReadOnlyList<ValidationError> errors = BeamModelValidator.Validate(beam);
        if (errors.Count != 0)
            throw new BeamSolverException(SolverErrorCode.InvalidModel,
                "Beam model validation failed. Inspect ValidationErrors for all model errors.", errors);

        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        dofs.EnsureStable(model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        Vector<double> displacements = ReducedSystemSolver.Solve(system, dofs);
        // Reactions always come from the original, unreduced system, including support loads.
        Vector<double> reactions = system.Stiffness * displacements - system.Loads;
        NumericalGuard.AllFinite(reactions, "Reactions");

        var results = new List<BeamNodeResult>(model.Nodes.Count);
        foreach (BeamNode node in model.Nodes)
        {
            int u = DofMap.Index(node.Index, DegreeOfFreedom.Axial);
            int w = DofMap.Index(node.Index, DegreeOfFreedom.Transverse);
            int theta = DofMap.Index(node.Index, DegreeOfFreedom.Rotation);
            results.Add(new BeamNodeResult(node.Index, node.Position,
                Displacement.FromMeters(displacements[u]), Displacement.FromMeters(displacements[w]), displacements[theta],
                dofs.IsConstrained(u) ? Force.FromNewtons(reactions[u]) : null,
                dofs.IsConstrained(w) ? Force.FromNewtons(reactions[w]) : null,
                dofs.IsConstrained(theta) ? Moment.FromNewtonMeters(reactions[theta]) : null));
        }
        return new BeamSolution(beam, results, model);
    }
}
