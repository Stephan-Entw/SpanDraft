using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal sealed record VerifiedBendingState(BendingSystem System, bool IsUnloaded, bool IsVerified,
    ImmutableArray<int> RS, ImmutableArray<int> CS, ImmutableArray<double> S, ImmutableArray<double> DS,
    ImmutableArray<double> SB, ImmutableArray<double> DSB, ImmutableArray<double> Y,
    ImmutableArray<double> FullState, int PrimaryIterations, ImmutableArray<double> Residual,
    ImmutableArray<double> ResidualRoundingBound, ImmutableArray<double> D,
    ImmutableArray<double> RawBound, ImmutableArray<double> VerifiedStateBound,
    ImmutableArray<double> FullStateBound, ImmutableArray<double> StateScaleError,
    ImmutableArray<double> TestedMus, double? SelectedMu,
    int AdjointSolves, int AdjointIterations, int MaxAdjointIterations);
