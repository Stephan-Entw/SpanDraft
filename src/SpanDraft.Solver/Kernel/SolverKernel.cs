using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal static class SolverKernel
{
    internal static BendingResult Solve(MechanicalInput input)
    {
        BendingSystem system = BuildSystem(input);
        VerifiedBendingState state = BendingStateSolver.Solve(system, out BendingSolveContext? context);
        BendingFields fields = BendingFields.From(state);
        ImmutableArray<NormalizedReaction> normalized = BendingReactions.Calculate(state, context);
        BendingQuality quality = BendingOutputQuality.Evaluate(state, fields, system.EventStations, normalized, context);
        ImmutableArray<BendingPiece> pieces = quality.Pieces.Select(p => BendingPiece.Create(fields, p)).ToImmutableArray();
        BendingExtrema extrema = BendingExtrema.Find(fields, pieces);
        ImmutableArray<BendingStation> stations = system.EventStations.Select(e =>
        {
            BendingJump jumps = BendingJump.None;
            if (!e.PointForces.IsEmpty) jumps |= BendingJump.PointForce;
            if (!e.PointMoments.IsEmpty) jumps |= BendingJump.PointMoment;
            if (e.MechanicalStation?.Directions.HasFlag(RestrainedDirections.W) == true) jumps |= BendingJump.ReactionY;
            if (e.MechanicalStation?.Directions.HasFlag(RestrainedDirections.Theta) == true) jumps |= BendingJump.ReactionMoment;
            return new BendingStation(e.Position, e.Position == 0 ? null : fields.EvaluateAt(e.Position, FieldSide.Left),
                e.Position == input.Length ? null : fields.EvaluateAt(e.Position, FieldSide.Right), jumps);
        }).ToImmutableArray();
        ImmutableArray<BendingReaction> reactions = normalized.Select(r => new BendingReaction(r.Position, r.Direction,
            r.Direction == RestrainedDirections.U ? default : BendingFields.ToSi(r.Value, r.Bound,
                system.StateExponents[r.Component], system.IsUnloaded))).ToImmutableArray();
        return new(fields, stations, reactions, pieces, extrema, quality);
    }

    internal static VerifiedBendingState SolveSystem(BendingSystem system) => BendingStateSolver.Solve(system);

    /// <summary>Exact mechanism check of the restraints alone; it reads neither length nor stiffness nor loads.</summary>
    internal static void RequireStable(IEnumerable<RigidRestraint> restraints) =>
        RequireStable(StationModel.Create(new MechanicalInput(1, 1, restraints, [], [], [])));

    private static void RequireStable(StationModel stations)
    {
        MechanicalStability stability = MechanicalStability.Check(stations);
        if (!stability.IsStable)
            throw new MechanicalMechanismException(stability);
    }

    internal static BendingSystem BuildSystem(MechanicalInput input)
    {
        StationModel stations = StationModel.Create(input);
        RequireStable(stations);

        BendingPreparation preparation = BendingPreparation.Create(input, stations);
        PreparedBendingLoads loads = PreparedBendingLoads.Create(input, stations, preparation);
        return BendingSystemBuilder.Build(stations, preparation, loads);
    }
}
