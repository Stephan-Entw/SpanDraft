namespace SpanDraft.Solver.Kernel;

internal static class SolverKernel
{
    internal static VerifiedBendingState SolveSystem(BendingSystem system) => BendingStateSolver.Solve(system);

    internal static BendingSystem BuildSystem(MechanicalInput input)
    {
        StationModel stations = StationModel.Create(input);
        MechanicalStability stability = MechanicalStability.Check(stations);
        if (!stability.IsStable)
            throw new MechanicalMechanismException(stability);

        BendingPreparation preparation = BendingPreparation.Create(input, stations);
        PreparedBendingLoads loads = PreparedBendingLoads.Create(input, stations, preparation);
        return BendingSystemBuilder.Build(stations, preparation, loads);
    }
}
