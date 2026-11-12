using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// One committed document through the application chain: solver, analysis, engineering, presentation,
/// overview, diagram projection and result probe. Expected values are statics of the document itself.
/// </summary>
public sealed class SolverProductChainTests
{
    private const double X1 = 0.10054, X2 = 0.100540006; // The 6 nm regression pair.

    public static TheoryData<string> Models => new(
        "cantilever", "simple", "udl", "moment", "indeterminate", "multiple-inner-supports", "load-on-support", "coincident-fixed",
        "coincident-pinned", "six-nanometer-loads", "six-nanometer-support", "unloaded", "bending-axis", "self-weight");

    [Theory]
    [MemberData(nameof(Models))]
    public void DocumentResultsAreConsistentFromSolverToPresentation(string name)
    {
        EditorDocument document = Document(name);
        var editor = new EditorViewModel(document, () => { });
        BeamModel beam = document.ToBeamModel();
        var result = editor.Presentation.Result!;
        BeamSolution solution = result.Solution;

        // The chain delivers exactly the public solver's solution of the committed model.
        BeamSolution direct = Solve(beam);
        Assert.Equal(Events(beam), solution.Nodes.Select(n => n.Position.Meters));
        foreach (var (a, b) in direct.Nodes.Zip(solution.Nodes))
        {
            Assert.Equal(a.TransverseDisplacement, b.TransverseDisplacement);
            Assert.Equal(a.RotationRadians, b.RotationRadians);
            Assert.Equal((a.ReactionX, a.ReactionY, a.ReactionMoment), (b.ReactionX, b.ReactionY, b.ReactionMoment));
            Assert.Equal(0, b.AxialDisplacement.Meters);
        }

        // Reactions exist exactly for the union of restraints at a station, once per direction.
        foreach (BeamNodeResult node in solution.Nodes)
        {
            SupportType[] types = beam.Supports.Where(s => s.Position == node.Position).Select(s => s.Type).ToArray();
            Assert.Equal(types.Any(t => t is SupportType.Fixed or SupportType.Pinned), node.ReactionX.HasValue);
            Assert.Equal(types.Length > 0, node.ReactionY.HasValue);
            Assert.Equal(types.Contains(SupportType.Fixed), node.ReactionMoment.HasValue);
            if (node.ReactionX is { } rx) Assert.Equal(0, rx.Newtons);
        }

        // Global equilibrium, then the shear and moment jump at every station.
        double forceScale = 1 + beam.Loads.Sum(l => Math.Abs(Resultant(l)));
        double momentScale = forceScale * beam.Length.Meters + beam.Loads.OfType<PointMoment>().Sum(m => Math.Abs(m.Moment.NewtonMeters));
        double vertical = solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0) + beam.Loads.Sum(Resultant);
        double moment = solution.Nodes.Sum(n => (n.ReactionY?.Newtons ?? 0) * n.Position.Meters + (n.ReactionMoment?.NewtonMeters ?? 0))
            + beam.Loads.Sum(MomentAboutOrigin);
        Assert.InRange(Math.Abs(vertical), 0, 1e-9 * forceScale);
        Assert.InRange(Math.Abs(moment), 0, 1e-9 * momentScale);
        foreach (BeamNodeResult node in solution.Nodes)
        {
            double x = node.Position.Meters;
            double force = (node.ReactionY?.Newtons ?? 0) + beam.Loads.OfType<PointForce>().Where(l => l.Position == node.Position).Sum(l => l.Force.Newtons);
            double couple = (node.ReactionMoment?.NewtonMeters ?? 0) + beam.Loads.OfType<PointMoment>().Where(l => l.Position == node.Position).Sum(l => l.Moment.NewtonMeters);
            BeamSectionResult left = solution.EvaluateAt(node.Position, EvaluationSide.Left), right = solution.EvaluateAt(node.Position, EvaluationSide.Right);
            if (x == 0 || node.Position == beam.Length)
            {
                // Both sides select the inward limit; outside the beam V and M vanish.
                Assert.Equal((left.ShearForce, left.BendingMoment), (right.ShearForce, right.BendingMoment));
                double sign = x == 0 ? 1 : -1;
                Assert.InRange(Math.Abs(sign * right.ShearForce.Newtons - force), 0, 1e-9 * forceScale);
                Assert.InRange(Math.Abs(sign * right.BendingMoment.NewtonMeters + couple), 0, 1e-9 * momentScale);
            }
            else
            {
                Assert.InRange(Math.Abs(right.ShearForce.Newtons - left.ShearForce.Newtons - force), 0, 1e-9 * forceScale);
                Assert.InRange(Math.Abs(right.BendingMoment.NewtonMeters - left.BendingMoment.NewtonMeters + couple), 0, 1e-9 * momentScale);
            }
            foreach (BeamSectionResult section in new[] { left, right })
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(node.TransverseDisplacement.Meters),
                    BitConverter.DoubleToInt64Bits(section.TransverseDisplacement.Meters));
                Assert.Equal(BitConverter.DoubleToInt64Bits(node.RotationRadians),
                    BitConverter.DoubleToInt64Bits(section.RotationRadians));
                if (node.ReactionY.HasValue) Assert.Equal(0L, BitConverter.DoubleToInt64Bits(section.TransverseDisplacement.Meters));
                Assert.Equal(0, section.AxialDisplacement.Meters);
                Assert.Equal(0, section.AxialForce.Newtons);
            }
        }

        // Extrema are attained by the fields, and Engineering takes its maxima from them.
        BeamExtrema extrema = solution.Extrema;
        Attained(extrema.MinimumShearForce, true, s => s.ShearForce.Newtons, v => v.Newtons);
        Attained(extrema.MaximumShearForce, false, s => s.ShearForce.Newtons, v => v.Newtons);
        Attained(extrema.MinimumBendingMoment, true, s => s.BendingMoment.NewtonMeters, v => v.NewtonMeters);
        Attained(extrema.MaximumBendingMoment, false, s => s.BendingMoment.NewtonMeters, v => v.NewtonMeters);
        Attained(extrema.MinimumTransverseDisplacement, true, s => s.TransverseDisplacement.Meters, v => v.Meters);
        Attained(extrema.MaximumTransverseDisplacement, false, s => s.TransverseDisplacement.Meters, v => v.Meters);
        var engineering = result.Engineering;
        Assert.Equal(Math.Max(Math.Abs(extrema.MinimumBendingMoment.Value.NewtonMeters), Math.Abs(extrema.MaximumBendingMoment.Value.NewtonMeters)),
            engineering.BendingMomentMagnitude.NewtonMeters);
        Assert.Equal(Math.Max(Math.Abs(extrema.MinimumTransverseDisplacement.Value.Meters), Math.Abs(extrema.MaximumTransverseDisplacement.Value.Meters)),
            engineering.TransverseDisplacementMagnitude.Meters);
        SectionAxisProperties axis = beam.BendingAxisProperties;
        Assert.Equal(engineering.BendingMomentMagnitude.NewtonMeters /
            Math.Min(axis.PositiveSectionModulus.CubicMeters, axis.NegativeSectionModulus.CubicMeters), engineering.MaximumBendingStress.Pascals);
        var options = editor.ResultPresentation;
        Assert.Equal(QuantityFormatter.Format(engineering.BendingMomentMagnitude.NewtonMeters, QuantityKind.Moment, options), editor.Presentation.Moment);
        Assert.Equal(QuantityFormatter.Format(engineering.TransverseDisplacementMagnitude.Meters, QuantityKind.TransverseDisplacement, options),
            editor.Presentation.Displacement);

        // Overview: one reaction row per support station with the station's common reaction.
        var stations = document.Supports.GroupBy(s => s.Position).ToArray();
        Assert.Equal(stations.Length, editor.Overview.Reactions.Count);
        foreach (var (station, row) in stations.Zip(editor.Overview.Reactions))
        {
            BeamNodeResult node = solution.Nodes.Single(n => n.Position == station.Key);
            Assert.Equal(string.Join(" + ", station.Select(s => s.Name)), row.Name);
            // Only a row with exactly one support identifies an editable entity.
            Assert.Equal(station.Count() == 1 ? station.Single().Id : (Guid?)null, row.Id);
            Assert.Equal(station.Count() == 1, row.IsEditable);
            Assert.Equal(QuantityFormatter.FormatNumber(node.ReactionY!.Value.Newtons, QuantityKind.TransverseForce, options), row.Ry);
            Assert.Equal(node.ReactionX is { } rx ? QuantityFormatter.FormatNumber(rx.Newtons, QuantityKind.AxialForce, options) : "", row.Rx);
            Assert.Equal(node.ReactionMoment is { } rm ? QuantityFormatter.FormatNumber(rm.NewtonMeters, QuantityKind.Moment, options) : "", row.Moment);
        }

        // Diagrams: markers are the solver's extrema; every jump line connects both analytical limits.
        StationLayoutResult layout = StationLayout.Compute(beam.Length.Meters, 72, 872, []);
        foreach (ResultDiagramKind kind in Enum.GetValues<ResultDiagramKind>())
        {
            ResultDiagramProjection plot = ResultDiagramProjection.Create(solution, kind, layout, 36, 132);
            Func<BeamSectionResult, double> value = kind switch
            {
                ResultDiagramKind.TransverseDisplacement => s => s.TransverseDisplacement.Meters,
                ResultDiagramKind.ShearForce => s => s.ShearForce.Newtons,
                _ => s => s.BendingMoment.NewtonMeters
            };
            (double minimum, double maximum) = kind switch
            {
                ResultDiagramKind.TransverseDisplacement => (extrema.MinimumTransverseDisplacement.Value.Meters, extrema.MaximumTransverseDisplacement.Value.Meters),
                ResultDiagramKind.ShearForce => (extrema.MinimumShearForce.Value.Newtons, extrema.MaximumShearForce.Value.Newtons),
                _ => (extrema.MinimumBendingMoment.Value.NewtonMeters, extrema.MaximumBendingMoment.Value.NewtonMeters)
            };
            Assert.Equal(minimum == maximum ? [minimum] : new[] { minimum, maximum }, plot.Markers.Select(m => m.SiValue));
            foreach (ResultDiagramJump jump in plot.Jumps)
            {
                Assert.Equal(jump.Left.Position, jump.Right.Position);
                Assert.Equal(value(solution.EvaluateAt(jump.Left.Position, EvaluationSide.Left)), jump.Left.SiValue);
                Assert.Equal(value(solution.EvaluateAt(jump.Right.Position, EvaluationSide.Right)), jump.Right.SiValue);
            }
            Assert.All(plot.Sections.SelectMany(s => s), point =>
                Assert.Equal(value(solution.EvaluateAt(point.Position, point.Side)), point.SiValue));
            if (kind == ResultDiagramKind.TransverseDisplacement) Assert.Empty(plot.Jumps);
        }

        // Probe: both one-sided limits at every interior station, formatted like every other result.
        editor.ToggleResultProbeTool();
        foreach (BeamNodeResult node in solution.Nodes)
        {
            editor.HoverResultProbe(node.Position, atStation: true);
            bool interior = node.Position.Meters > 0 && node.Position != beam.Length;
            BeamSectionResult right = solution.EvaluateAt(node.Position, interior ? EvaluationSide.Right : node.Position.Meters > 0 ? EvaluationSide.Left : EvaluationSide.Right);
            BeamSectionResult left = interior ? solution.EvaluateAt(node.Position, EvaluationSide.Left) : right;
            Probe("V", QuantityKind.TransverseForce, left.ShearForce.Newtons, right.ShearForce.Newtons);
            Probe("M", QuantityKind.Moment, left.BendingMoment.NewtonMeters, right.BendingMoment.NewtonMeters);
            // Continuous kinematics are reported once, from the station's own value.
            Probe("w", QuantityKind.TransverseDisplacement, node.TransverseDisplacement.Meters, node.TransverseDisplacement.Meters);
            Probe("θ", QuantityKind.Rotation, node.RotationRadians, node.RotationRadians);
            Assert.DoesNotContain(editor.ResultProbeRows, r => r.Label is "w−" or "w+" or "θ−" or "θ+");
        }

        void Attained<T>(BeamExtremum<T> extremum, bool minimum, Func<BeamSectionResult, double> field, Func<T, double> scalar)
        {
            double value = scalar(extremum.Value);
            foreach (EvaluationSide side in extremum.Side is { } s ? [s] : Enum.GetValues<EvaluationSide>())
                Assert.Equal(value, field(solution.EvaluateAt(extremum.Position, side)));
            // No station limit lies beyond the reported extremum (up to the solver's relative tie rule).
            foreach (BeamNodeResult node in solution.Nodes)
                foreach (EvaluationSide side in Enum.GetValues<EvaluationSide>())
                {
                    double candidate = field(solution.EvaluateAt(node.Position, side));
                    double slack = 1e-13 * Math.Max(Math.Abs(candidate), Math.Abs(value));
                    Assert.True(minimum ? candidate >= value - slack : candidate <= value + slack);
                }
        }

        void Probe(string label, QuantityKind kind, double left, double right)
        {
            string a = QuantityFormatter.Format(left, kind, options), b = QuantityFormatter.Format(right, kind, options);
            if (a == b) Assert.Equal(b, editor.ResultProbeRows.Single(r => r.Label == label).Value);
            else
            {
                Assert.Equal(a, editor.ResultProbeRows.Single(r => r.Label == label + "−").Value);
                Assert.Equal(b, editor.ResultProbeRows.Single(r => r.Label == label + "+").Value);
            }
        }
    }

    private static double[] Events(BeamModel beam) => new[] { 0, beam.Length.Meters }
        .Concat(beam.Supports.Select(s => s.Position.Meters))
        .Concat(beam.Loads.SelectMany(l => l switch
        {
            PointForce f => [f.Position.Meters],
            PointMoment m => [m.Position.Meters],
            UniformDistributedLoad q => new[] { q.StartPosition.Meters, q.EndPosition.Meters },
            _ => throw new NotSupportedException()
        })).Distinct().Order().ToArray();

    private static double Resultant(BeamLoad load) => load switch
    {
        PointForce f => f.Force.Newtons,
        UniformDistributedLoad q => q.Intensity.NewtonsPerMeter * (q.EndPosition.Meters - q.StartPosition.Meters),
        _ => 0
    };

    private static double MomentAboutOrigin(BeamLoad load) => load switch
    {
        PointForce f => f.Force.Newtons * f.Position.Meters,
        PointMoment m => m.Moment.NewtonMeters,
        UniformDistributedLoad q => Resultant(q) * (q.StartPosition.Meters + q.EndPosition.Meters) / 2,
        _ => 0
    };

    private static EditorDocument Document(string name) => name switch
    {
        "cantilever" => From(Cantilever(Point(L, -1000))),
        "simple" => From(Beam(loads: [Point(0.75, -1000)])),
        "udl" => From(Beam(loads: [Uniform(0.25, 1.75, -1000)])),
        "moment" => From(Beam(loads: [Couple(0.75, 400)])),
        "indeterminate" => From(Beam([SupportAt(0, SupportType.Fixed), SupportAt(1, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Point(0.75, -500), Uniform(0, L, -300)])),
        "multiple-inner-supports" => From(Beam([SupportAt(0, SupportType.Pinned), SupportAt(.5, SupportType.Roller),
            SupportAt(1.25, SupportType.Roller), SupportAt(L, SupportType.Roller)],
            [Point(.5, -1000), Couple(.5, 100), Point(1, -300), Uniform(.25, 1.5, -200)])),
        "load-on-support" => From(Beam([SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Point(1, -1000), Couple(1, 100), Point(1.5, -200)])),
        "coincident-fixed" => From(Beam([SupportAt(0, SupportType.Fixed), SupportAt(0, SupportType.Pinned),
            SupportAt(0, SupportType.Roller), SupportAt(L, SupportType.Roller)], [Point(1, -1000)])),
        "coincident-pinned" => From(Beam([SupportAt(0.5, SupportType.Roller), SupportAt(0.5, SupportType.Pinned),
            SupportAt(L, SupportType.Roller)], [Point(1.25, -1000), Uniform(0, 0.5, -200)])),
        "six-nanometer-loads" => From(Cantilever(Point(X1, -5000), Point(X2, -3000))),
        "six-nanometer-support" => From(Beam([SupportAt(X1, SupportType.Pinned), SupportAt(1, SupportType.Roller)],
            [Point(X2, -5000), Point(.5, -3000)])),
        "unloaded" => From(Beam()),
        "bending-axis" => From(SectionAxisTestSupport.AxisBeam(
            SectionAxisTestSupport.Manual(SectionAxisDesignation.Y, SectionAxisDesignation.Z),
            SectionAxisDesignation.Z, [Point(1, -1000), Uniform(0.25, 1.75, -300)])),
        "self-weight" => SelfWeightTestProjects.Document(enabled: true),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static EditorDocument From(BeamModel beam) => new(beam.Length, beam.Material, beam.Section, beam.BendingAxis,
        beam.Supports.Select((s, i) => new EditorSupport(Guid.NewGuid(), s.Position, s.Type, "S" + (i + 1))),
        beam.Loads.Select((l, i) => l switch
        {
            PointForce f => (EditorPointLoad)new EditorPointForce(Guid.NewGuid(), f.Position, f.Force, "F" + (i + 1)),
            PointMoment m => new EditorPointMoment(Guid.NewGuid(), m.Position, m.Moment, "M" + (i + 1)),
            _ => null!
        }).Where(l => l is not null),
        distributedLoads: beam.Loads.OfType<UniformDistributedLoad>().Select((q, i) =>
            new EditorUniformDistributedLoad(Guid.NewGuid(), q.StartPosition, q.EndPosition, q.Intensity, "q" + (i + 1))));
}
