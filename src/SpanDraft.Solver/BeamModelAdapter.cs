using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Solver.Kernel;

namespace SpanDraft.Solver;

internal static class BeamModelAdapter
{
    internal static MechanicalInput Adapt(BeamModel beam)
    {
        var pointForces = new List<TransversePointForce>();
        var pointMoments = new List<AppliedPointMoment>();
        var lineLoads = new List<ConstantLineLoad>();

        foreach (BeamLoad load in beam.Loads)
        {
            switch (load)
            {
                case PointForce force:
                    pointForces.Add(new(force.Position.Meters, force.Force.Newtons));
                    break;
                case PointMoment moment:
                    pointMoments.Add(new(moment.Position.Meters, moment.Moment.NewtonMeters));
                    break;
                case UniformDistributedLoad uniform:
                    lineLoads.Add(new(uniform.StartPosition.Meters, uniform.EndPosition.Meters,
                        uniform.Intensity.NewtonsPerMeter));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported beam load {load.GetType().Name}.");
            }
        }

        return new MechanicalInput(beam.Length.Meters, BendingStiffness(beam),
            beam.Supports.Select(AdaptSupport), pointForces, pointMoments, lineLoads);
    }

    /// <summary>The restraints alone, for the mechanism check of a beam whose EI is not representable.</summary>
    internal static IEnumerable<RigidRestraint> AdaptRestraints(BeamModel beam) => beam.Supports.Select(AdaptSupport);

    private static RigidRestraint AdaptSupport(Support support) =>
        new(support.Position.Meters, support.Type switch
        {
            SupportType.Fixed => RestrainedDirections.U | RestrainedDirections.W | RestrainedDirections.Theta,
            SupportType.Pinned => RestrainedDirections.U | RestrainedDirections.W,
            SupportType.Roller => RestrainedDirections.W,
            _ => throw new ArgumentOutOfRangeException(nameof(support), support.Type, "Unknown support type.")
        });

    private static double BendingStiffness(BeamModel beam) =>
        Binary64.StiffnessProduct(beam.Material.YoungsModulus.Pascals,
            beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth);
}
