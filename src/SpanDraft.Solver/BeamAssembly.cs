using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;

namespace SpanDraft.Solver;

internal sealed record BeamSystem(Matrix<double> Stiffness, Vector<double> Loads);

internal static class BeamAssembly
{
    internal static BeamSystem Assemble(BeamModel beam, SolverModel model, DofMap dofs)
    {
        var stiffness = Matrix<double>.Build.Dense(dofs.Count, dofs.Count);
        var loads = Vector<double>.Build.Dense(dofs.Count);
        foreach (BeamElement element in model.Elements)
        {
            Matrix<double> local = EulerBernoulliElement.Stiffness(beam.Material.YoungsModulus.Pascals,
                beam.Section.Area.SquareMeters, beam.Section.SecondMomentOfArea.MetersToTheFourth,
                element.LengthMeters);
            int[] indices = element.GlobalDofs;
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    stiffness[indices[i], indices[j]] += local[i, j];
        }

        foreach (BeamLoad load in beam.Loads)
        {
            switch (load)
            {
                case PointForce force:
                    loads[DofMap.Index(model.NodeIndex(force.Position), DegreeOfFreedom.Transverse)] += force.Force.Newtons;
                    break;
                case PointMoment moment:
                    loads[DofMap.Index(model.NodeIndex(moment.Position), DegreeOfFreedom.Rotation)] += moment.Moment.NewtonMeters;
                    break;
                case UniformDistributedLoad distributed:
                    foreach (BeamElement element in model.Elements)
                    {
                        // UDL endpoints are mesh nodes: each element is wholly inside or outside.
                        if (!element.IsCoveredBy(distributed))
                            continue;
                        Vector<double> local = EulerBernoulliElement.UniformLoad(
                            distributed.Intensity.NewtonsPerMeter, element.LengthMeters);
                        int[] indices = element.GlobalDofs;
                        for (int i = 0; i < 6; i++)
                            loads[indices[i]] += local[i];
                    }
                    break;
                default: throw new NotSupportedException($"Unsupported beam load: {load.GetType().Name}.");
            }
        }

        NumericalGuard.AllFinite(stiffness.Enumerate(), "Global stiffness");
        NumericalGuard.AllFinite(loads, "Global load vector");
        return new BeamSystem(stiffness, loads);
    }
}
