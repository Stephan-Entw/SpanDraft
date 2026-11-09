using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;

namespace SpanDraft.Solver;

/// <summary>Linear elastic Euler-Bernoulli analysis of one straight beam along global x.</summary>
public sealed class EulerBernoulliBeamSolver
{
    public BeamSolution Solve(BeamModel beam) => Solve(beam, null);

    /// <summary>Additional components exercise load kinds that Core cannot express yet.</summary>
    internal BeamSolution Solve(BeamModel beam, SolverLoads? additional)
    {
        ArgumentNullException.ThrowIfNull(beam);
        IReadOnlyList<ValidationError> errors = BeamModelValidator.Validate(beam);
        // Core cannot hold elastic supports yet; they count as supports here.
        if (additional?.Springs is { Count: > 0 })
            errors = errors.Where(e => e.Code != ValidationErrorCode.MissingSupports).ToArray();
        if (errors.Count != 0)
            throw new BeamSolverException(SolverErrorCode.InvalidModel,
                "Beam model validation failed. Inspect ValidationErrors for all model errors.", errors);

        SolverModel model = SolverModel.Create(beam, additional);
        var dofs = new DofMap(beam, model);
        SolverLoads loads = SolverLoads.From(beam, additional);
        SupportSpanSolver.CheckModel(beam, loads);
        if (loads.Hinges.Count == 0 && loads.Springs.Count == 0)
            dofs.EnsureStable(model);
        else
        {
            bool axial = beam.Supports.Any(s => s.Type is SupportType.Fixed or SupportType.Pinned) ||
                loads.Springs.Any(s => s.AxialRestraint);
            var restraints = beam.Supports.Select(s => (s.Position.Meters, false))
                .Concat(beam.Supports.Where(s => s.Type == SupportType.Fixed).Select(s => (s.Position.Meters, true)))
                .Concat(loads.Springs.Select(s => (s.X, false)))
                .Concat(loads.Springs.Where(s => s.Rotational > 0).Select(s => (s.X, true)))
                .ToArray();
            if (!axial)
                throw new BeamSolverException(SolverErrorCode.UnstableModel,
                    "Supports leave an axial translation unconstrained.");
            if (!DofMap.IsKinematicallyStable(restraints, loads.Hinges, beam.Length.Meters))
                throw new BeamSolverException(SolverErrorCode.UnstableModel,
                    "Supports, springs and hinges leave a part of the beam free to move.");
        }
        StationResult result = SupportSpanSolver.Solve(beam, model, loads);
        NumericalGuard.AllFinite(result.Displacements, "Displacements");
        NumericalGuard.AllFinite(result.Rotations, "Rotations");
        NumericalGuard.AllFinite(result.ReactionsY.Select(r => r ?? 0), "Reactions");
        NumericalGuard.AllFinite(result.ReactionMoments.Select(r => r ?? 0), "Reactions");

        var nodes = new List<BeamNodeResult>(model.Nodes.Count);
        foreach (BeamNode node in model.Nodes)
        {
            int i = node.Index;
            nodes.Add(new BeamNodeResult(i, node.Position,
                Displacement.FromMeters(result.AxialDisplacements[i]), Displacement.FromMeters(result.Displacements[i]),
                result.Rotations[i],
                result.ReactionsX[i] is { } rx ? Force.FromNewtons(rx) : null,
                result.ReactionsY[i] is { } ry ? Force.FromNewtons(ry) : null,
                result.ReactionMoments[i] is { } rm ? Moment.FromNewtonMeters(rm) : null));
        }
        return new BeamSolution(beam, nodes, model, result.Segments, result.RotationsLeft);
    }
}
