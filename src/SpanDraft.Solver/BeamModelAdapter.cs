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
        var lineLoads = new List<TransverseLineLoad>();

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
                case DistributedLoad distributed:
                    lineLoads.Add(new(distributed.StartPosition.Meters, distributed.EndPosition.Meters,
                        distributed.StartIntensity.NewtonsPerMeter, distributed.EndIntensity.NewtonsPerMeter));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported beam load {load.GetType().Name}.");
            }
        }

        (IEnumerable<RigidRestraint> rigid, IEnumerable<ElasticRestraint> elastic) = AdaptRestraints(beam);
        return new MechanicalInput(beam.Length.Meters, BendingStiffness(beam), rigid, pointForces, pointMoments,
            lineLoads, elastic);
    }

    /// <summary>The restraints alone, for the mechanism check of a beam whose EI is not representable.</summary>
    internal static (IEnumerable<RigidRestraint> Rigid, IEnumerable<ElasticRestraint> Elastic) AdaptRestraints(
        BeamModel beam) =>
        (beam.Supports.Where(s => s.Type != SupportType.Spring).Select(AdaptRigid),
            beam.Supports.Where(s => s.Type == SupportType.Spring).Select(AdaptSpring));

    private static RigidRestraint AdaptRigid(Support support) =>
        new(support.Position.Meters, support.Type switch
        {
            SupportType.Fixed => RestrainedDirections.U | RestrainedDirections.W | RestrainedDirections.Theta,
            SupportType.Pinned => RestrainedDirections.U | RestrainedDirections.W,
            SupportType.Roller => RestrainedDirections.W,
            _ => throw new ArgumentOutOfRangeException(nameof(support), support.Type, "Unknown support type.")
        });

    private static ElasticRestraint AdaptSpring(Support support) =>
        new(support.Position.Meters, support.TranslationalSpring?.NewtonsPerMeter ?? 0,
            support.RotationalSpring?.NewtonMetersPerRadian ?? 0, support.RigidTranslation);

    private static double BendingStiffness(BeamModel beam) =>
        Binary64.StiffnessProduct(beam.Material.YoungsModulus.Pascals,
            beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth);
}
