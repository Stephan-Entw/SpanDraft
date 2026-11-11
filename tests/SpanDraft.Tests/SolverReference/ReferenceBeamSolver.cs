using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;

namespace SpanDraft.Tests.SolverReference;

/// <summary>
/// Frozen, independent comparison solvers outside the product. Everything in this folder is test-only:
/// it is not developed further, receives no features and must never be referenced from src.
/// <see cref="FiniteElement"/> is the former stiffness-method solver; it is not exact and loses accuracy
/// with closely spaced stations. <see cref="Prototype"/> is the feasibility prototype of the solver rework,
/// including feature primitives that the product cannot express.
/// </summary>
internal static class ReferenceBeamSolver
{
    internal static ReferenceBeamSolution FiniteElement(BeamModel beam)
    {
        SolverModel model = SolverModel.Create(beam);
        var dofs = new DofMap(beam, model);
        BeamSystem system = BeamAssembly.Assemble(beam, model, dofs);
        Vector<double> q = ReducedSystemSolver.Solve(system, dofs);
        Vector<double> reactions = system.Stiffness * q - system.Loads;
        var nodes = model.Nodes.Select(node =>
        {
            int u = DofMap.Index(node.Index, DegreeOfFreedom.Axial);
            int w = DofMap.Index(node.Index, DegreeOfFreedom.Transverse);
            int theta = DofMap.Index(node.Index, DegreeOfFreedom.Rotation);
            return new BeamNodeResult(node.Index, node.Position, Displacement.FromMeters(q[u]), Displacement.FromMeters(q[w]), q[theta],
                dofs.IsConstrained(u) ? Force.FromNewtons(reactions[u]) : null,
                dofs.IsConstrained(w) ? Force.FromNewtons(reactions[w]) : null,
                dofs.IsConstrained(theta) ? Moment.FromNewtonMeters(reactions[theta]) : null);
        });
        return new ReferenceBeamSolution(beam, nodes, model);
    }

    /// <summary>Additional components exercise load kinds that Core cannot express.</summary>
    internal static ReferenceBeamSolution Prototype(BeamModel beam, SolverLoads? additional = null)
    {
        ArgumentNullException.ThrowIfNull(beam);
        IReadOnlyList<ValidationError> errors = BeamModelValidator.Validate(beam);
        // Core cannot hold elastic supports; they count as supports here.
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
        return new ReferenceBeamSolution(beam, nodes, model, result.Segments, result.RotationsLeft);
    }
}
