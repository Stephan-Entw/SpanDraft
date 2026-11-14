using System.Collections.Immutable;
using static System.BitConverter;

namespace SpanDraft.Solver.Kernel;

/// <summary>Immutable snapshot of mechanical primitives in SI units.</summary>
internal sealed class MechanicalInput
{
    internal MechanicalInput(double length, double ei, IEnumerable<RigidRestraint> restraints,
        IEnumerable<TransversePointForce> pointForces, IEnumerable<AppliedPointMoment> pointMoments,
        IEnumerable<TransverseLineLoad> lineLoads, IEnumerable<ElasticRestraint>? elasticRestraints = null)
    {
        Length = length;
        EI = ei;
        Restraints = restraints.OrderBy(r => (r.Position, r.Directions, DoubleToInt64Bits(r.Position)))
            .ToImmutableArray();

        ElasticRestraints = (elasticRestraints ?? []).OrderBy(r => (r.Position, DoubleToInt64Bits(r.Position),
                DoubleToInt64Bits(r.TranslationalStiffness), DoubleToInt64Bits(r.RotationalStiffness), r.RigidTranslation))
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
    internal ImmutableArray<ElasticRestraint> ElasticRestraints { get; }
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

/// <summary>
/// A linear-elastic support in SI units: a transverse spring (N/m), a rotational spring (N·m/rad) or both; zero
/// means no spring in that direction. A support with a rotational spring may hold the translation rigidly
/// (partial restraint). It is always axially rigid.
/// </summary>
internal readonly record struct ElasticRestraint(double Position, double TranslationalStiffness,
    double RotationalStiffness, bool RigidTranslation);
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
