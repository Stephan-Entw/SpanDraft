using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;

namespace SpanDraft.Solver;

internal readonly record struct PointAction(double X, double Value);
internal readonly record struct UniformAction(double Start, double End, double Intensity);

/// <summary>Linearly varying line load from StartIntensity at Start to EndIntensity at End (N/m).</summary>
internal readonly record struct LinearAction(double Start, double End, double StartIntensity, double EndIntensity);

/// <summary>
/// Elastic support in SI units. Translational is N/m and Rotational N*m/rad; PositiveInfinity is rigid,
/// a rotational value of 0 leaves rotation free. Core has no elastic support type yet.
/// </summary>
internal readonly record struct ElasticSupport(double X, double Translational, double Rotational = 0, bool AxialRestraint = false)
{
    internal bool RigidTranslation => double.IsPositiveInfinity(Translational);
    internal bool RigidRotation => double.IsPositiveInfinity(Rotational);
}

/// <summary>
/// Solver-side load components in SI units. Bending and axial action are separate because the
/// first-order beam does not couple them. A future inclined force is one transverse and one
/// axial point action at the same position. The Core load model has no axial type yet.
/// </summary>
internal sealed record SolverLoads(
    IReadOnlyList<PointAction> Forces,
    IReadOnlyList<PointAction> Moments,
    IReadOnlyList<UniformAction> Uniform,
    IReadOnlyList<PointAction> AxialForces,
    IReadOnlyList<UniformAction> AxialUniform,
    IReadOnlyList<double>? InternalHinges = null,
    IReadOnlyList<ElasticSupport>? InternalSprings = null,
    IReadOnlyList<PointAction>? InternalRigidity = null,
    IReadOnlyList<LinearAction>? InternalLinear = null,
    IReadOnlyList<UniformAction>? InternalCurvature = null)
{
    /// <summary>EI (N*m²) from X onwards; before the first entry the beam's own EI applies. Core has one section.</summary>
    internal IReadOnlyList<PointAction> Rigidity => InternalRigidity ?? [];

    internal IReadOnlyList<LinearAction> Linear => InternalLinear ?? [];

    /// <summary>Imposed curvature in 1/m, e.g. α·ΔT/h from a temperature difference over the depth.</summary>
    internal IReadOnlyList<UniformAction> Curvature => InternalCurvature ?? [];

    /// <summary>Positions with a moment release. Core has no hinge type yet.</summary>
    internal IReadOnlyList<double> Hinges => InternalHinges ?? [];

    internal IReadOnlyList<ElasticSupport> Springs => InternalSprings ?? [];

    internal static SolverLoads From(BeamModel beam, SolverLoads? additional = null)
    {
        var forces = new List<PointAction>();
        var moments = new List<PointAction>();
        var uniform = new List<UniformAction>();
        foreach (BeamLoad load in beam.Loads)
        {
            switch (load)
            {
                case PointForce force:
                    forces.Add(new(force.Position.Meters, force.Force.Newtons));
                    break;
                case PointMoment moment:
                    moments.Add(new(moment.Position.Meters, moment.Moment.NewtonMeters));
                    break;
                case UniformDistributedLoad distributed:
                    uniform.Add(new(distributed.StartPosition.Meters, distributed.EndPosition.Meters,
                        distributed.Intensity.NewtonsPerMeter));
                    break;
                default: throw new NotSupportedException($"Unsupported beam load: {load.GetType().Name}.");
            }
        }
        if (additional is null)
            return new(forces, moments, uniform, [], []);
        forces.AddRange(additional.Forces);
        moments.AddRange(additional.Moments);
        uniform.AddRange(additional.Uniform);
        return new(forces, moments, uniform, additional.AxialForces, additional.AxialUniform, additional.Hinges,
            additional.Springs, additional.Rigidity, additional.Linear, additional.Curvature);
    }

    /// <summary>Every position where a result can change character.</summary>
    internal IEnumerable<double> Positions =>
        Forces.Select(p => p.X)
            .Concat(Hinges)
            .Concat(Springs.Select(s => s.X))
            .Concat(Moments.Select(p => p.X))
            .Concat(AxialForces.Select(p => p.X))
            .Concat(Uniform.SelectMany(u => new[] { u.Start, u.End }))
            .Concat(AxialUniform.SelectMany(u => new[] { u.Start, u.End }))
            .Concat(Rigidity.Select(r => r.X))
            .Concat(Linear.SelectMany(u => new[] { u.Start, u.End }))
            .Concat(Curvature.SelectMany(u => new[] { u.Start, u.End }));
}
