using System.Collections.Immutable;
using static System.BitConverter;

namespace SpanDraft.Solver.Kernel;

/// <summary>Immutable snapshot of mechanical primitives in SI units.</summary>
internal sealed class MechanicalInput
{
    internal MechanicalInput(double length, double ei, IEnumerable<RigidRestraint> restraints,
        IEnumerable<TransversePointForce> pointForces, IEnumerable<AppliedPointMoment> pointMoments,
        IEnumerable<TransverseLineLoad> lineLoads)
    {
        Length = length;
        EI = ei;
        Restraints = restraints.OrderBy(r => (r.Position, r.Directions, DoubleToInt64Bits(r.Position)))
            .ToImmutableArray();

        // Bit keys make the storage order deterministic, including signed zeros.
        PointForces = pointForces.OrderBy(f => (f.Position, DoubleToInt64Bits(f.Position), DoubleToInt64Bits(f.Newtons)))
            .ToImmutableArray();
        PointMoments = pointMoments.OrderBy(m => (m.Position, DoubleToInt64Bits(m.Position), DoubleToInt64Bits(m.NewtonMeters)))
            .ToImmutableArray();
        LineLoads = lineLoads.OrderBy(q => (q.Start, q.End, DoubleToInt64Bits(q.Start), DoubleToInt64Bits(q.End),
                DoubleToInt64Bits(q.StartNewtonsPerMeter), DoubleToInt64Bits(q.EndNewtonsPerMeter)))
            .ToImmutableArray();
    }

    internal double Length { get; }
    internal double EI { get; }
    internal ImmutableArray<RigidRestraint> Restraints { get; }
    internal ImmutableArray<TransversePointForce> PointForces { get; }
    internal ImmutableArray<AppliedPointMoment> PointMoments { get; }
    internal ImmutableArray<TransverseLineLoad> LineLoads { get; }
}

[Flags]
internal enum RestrainedDirections
{
    None = 0,
    U = 1,
    W = 2,
    Theta = 4
}

internal readonly record struct RigidRestraint(double Position, RestrainedDirections Directions);
internal readonly record struct TransversePointForce(double Position, double Newtons);
internal readonly record struct AppliedPointMoment(double Position, double NewtonMeters);
/// <summary>
/// A transverse line load whose intensity varies linearly from the start to the end value (N/m).
/// Equal end values are a constant load and keep the constant-load evaluation path.
/// </summary>
internal readonly record struct TransverseLineLoad(double Start, double End, double StartNewtonsPerMeter,
    double EndNewtonsPerMeter)
{
    internal TransverseLineLoad(double start, double end, double newtonsPerMeter)
        : this(start, end, newtonsPerMeter, newtonsPerMeter)
    {
    }

    /// <summary>True when both end intensities are equal in value.</summary>
    internal bool IsConstant => StartNewtonsPerMeter == EndNewtonsPerMeter;
}
