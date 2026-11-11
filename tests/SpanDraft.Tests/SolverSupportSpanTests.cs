using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using SpanDraft.Tests.SolverReference;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverSupportSpanTests
{
    private const double X1 = 0.10054, X2 = 0.100540006; // 100.54 mm and 100.540006 mm.

    private static BeamSectionResult Right(BeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Right);
    private static BeamSectionResult Left(BeamSolution s, double x) => s.EvaluateAt(M(x), EvaluationSide.Left);

    [Fact]
    public void TwoForcesSixNanometersApartKeepTheirExactPositions()
    {
        BeamModel beam = Cantilever(Point(X1, -5000), Point(X2, -3000));
        BeamSolution solution = Solve(beam);
        Assert.Equal(new[] { 0, X1, X2, L }, solution.Nodes.Select(n => n.Position.Meters));
        ForceClose(8000, At(solution, 0).ReactionY);
        MomentClose(5000 * X1 + 3000 * X2, At(solution, 0).ReactionMoment);
        Close(8000, Left(solution, X1).ShearForce.Newtons, 1e-9);
        Close(3000, Right(solution, X1).ShearForce.Newtons, 1e-9);
        Close(3000, Left(solution, X2).ShearForce.Newtons, 1e-9);
        Close(0, Right(solution, X2).ShearForce.Newtons, 1e-9);
        Close(8000, solution.Extrema.MaximumShearForce.Value.Newtons, 1e-9);
    }

    [Fact]
    public void ForceSixNanometersBesideASupportHasCorrectShear()
    {
        BeamModel beside = Beam([SupportAt(X1, SupportType.Pinned), SupportAt(1, SupportType.Roller)],
            [Point(X2, -5000), Point(.5, -3000)]);
        BeamSolution solution = Solve(beside);
        double span = 1 - X1;
        double rightReaction = (5000 * (X2 - X1) + 3000 * (.5 - X1)) / span;
        double leftReaction = 8000 - rightReaction;
        ForceClose(leftReaction, At(solution, X1).ReactionY);
        Close(leftReaction, Right(solution, X1).ShearForce.Newtons, 1e-9);
        Close(leftReaction - 5000, Right(solution, X2).ShearForce.Newtons, 1e-9);
        Close(leftReaction, solution.Extrema.MaximumShearForce.Value.Newtons, 1e-9);
    }

    [Fact]
    public void ExtremeSupportDistancesAreSolvedWithEquilibrium()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Pinned), SupportAt(1e-9, SupportType.Roller),
            SupportAt(1, SupportType.Roller), SupportAt(1 + 1e-9, SupportType.Roller), SupportAt(2, SupportType.Roller)],
            [Point(.5, -1000), Uniform(0, 2, -100), Couple(1.5, 50)]);
        BeamSolution solution = Solve(beam);
        double sum = solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0);
        Close(1200, sum, 1e-4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ProductAgreesWithFrozenFiniteElementReference(int seed)
    {
        var random = new Random(seed);
        for (int model = 0; model < 200; model++)
        {
            BeamModel beam = RandomBeam(random);
            AssertSame(ReferenceBeamSolver.FiniteElement(beam), Solve(beam));
        }
    }

    private static BeamModel RandomBeam(Random random)
    {
        double Grid() => random.Next(0, 201) / 100.0;
        Support[] supports = random.Next(6) switch
        {
            0 => [SupportAt(0, SupportType.Fixed)],
            1 => [SupportAt(L, SupportType.Fixed)],
            2 => [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)],
            3 => [SupportAt(.5, SupportType.Pinned), SupportAt(1.5, SupportType.Roller)],
            4 => [SupportAt(0, SupportType.Fixed), SupportAt(1, SupportType.Roller), SupportAt(L, SupportType.Fixed)],
            _ => [SupportAt(0, SupportType.Roller), SupportAt(.7, SupportType.Pinned), SupportAt(1.3, SupportType.Roller), SupportAt(L, SupportType.Roller)]
        };
        var loads = new List<BeamLoad>();
        int count = random.Next(1, 7);
        for (int i = 0; i < count; i++)
        {
            double magnitude = (random.NextDouble() - .5) * 20000;
            switch (random.Next(3))
            {
                case 0: loads.Add(Point(Grid(), magnitude)); break;
                case 1: loads.Add(Couple(Grid(), magnitude / 4)); break;
                default:
                    double a = Grid(), b = Grid();
                    if (a != b) loads.Add(Uniform(Math.Min(a, b), Math.Max(a, b), magnitude));
                    break;
            }
        }
        return Beam(supports, loads);
    }

    private static void AssertSame(ReferenceBeamSolution expected, BeamSolution actual)
    {
        Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
        BeamSectionResult[] samples = expected.Nodes.SelectMany((n, i) => new[]
        {
            expected.EvaluateAt(n.Position, EvaluationSide.Left), expected.EvaluateAt(n.Position, EvaluationSide.Right)
        }).ToArray();
        double w = Math.Max(samples.Max(s => Math.Abs(s.TransverseDisplacement.Meters)), 1e-12);
        double t = Math.Max(samples.Max(s => Math.Abs(s.RotationRadians)), 1e-12);
        double v = Math.Max(samples.Max(s => Math.Abs(s.ShearForce.Newtons)), 1e-3);
        double m = Math.Max(samples.Max(s => Math.Abs(s.BendingMoment.NewtonMeters)), 1e-3);
        // A beam with only moments has V = 0 exactly; rounding is then measured against M/L, not against zero.
        (v, m) = (Math.Max(v, m / L), Math.Max(m, v * L));
        // The assembled system loses accuracy with short elements; 1e-6 is the acceptance tolerance.
        const double relative = 1e-6;
        foreach (BeamNodeResult node in expected.Nodes)
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = expected.EvaluateAt(node.Position, side), b = actual.EvaluateAt(node.Position, side);
                Assert.InRange(Math.Abs(a.TransverseDisplacement.Meters - b.TransverseDisplacement.Meters) / w, 0, relative);
                Assert.InRange(Math.Abs(a.RotationRadians - b.RotationRadians) / t, 0, relative);
                Assert.InRange(Math.Abs(a.ShearForce.Newtons - b.ShearForce.Newtons) / v, 0, relative);
                Assert.InRange(Math.Abs(a.BendingMoment.NewtonMeters - b.BendingMoment.NewtonMeters) / m, 0, relative);
            }
        foreach (var pair in expected.Nodes.Zip(actual.Nodes))
        {
            Assert.Equal(pair.First.ReactionX.HasValue, pair.Second.ReactionX.HasValue);
            Assert.Equal(pair.First.ReactionY.HasValue, pair.Second.ReactionY.HasValue);
            Assert.Equal(pair.First.ReactionMoment.HasValue, pair.Second.ReactionMoment.HasValue);
            if (pair.First.ReactionY is { } ry)
                Assert.InRange(Math.Abs(ry.Newtons - pair.Second.ReactionY!.Value.Newtons) / v, 0, relative);
            if (pair.First.ReactionMoment is { } rm)
                Assert.InRange(Math.Abs(rm.NewtonMeters - pair.Second.ReactionMoment!.Value.NewtonMeters) / m, 0, relative);
        }
        Assert.InRange(Math.Abs(expected.Extrema.MaximumBendingMoment.Value.NewtonMeters - actual.Extrema.MaximumBendingMoment.Value.NewtonMeters) / m, 0, relative);
        Assert.InRange(Math.Abs(expected.Extrema.MinimumBendingMoment.Value.NewtonMeters - actual.Extrema.MinimumBendingMoment.Value.NewtonMeters) / m, 0, relative);
        Assert.InRange(Math.Abs(expected.Extrema.MaximumShearForce.Value.Newtons - actual.Extrema.MaximumShearForce.Value.Newtons) / v, 0, relative);
        Assert.InRange(Math.Abs(expected.Extrema.MinimumShearForce.Value.Newtons - actual.Extrema.MinimumShearForce.Value.Newtons) / v, 0, relative);
        Assert.InRange(Math.Abs(expected.Extrema.MaximumTransverseDisplacement.Value.Meters - actual.Extrema.MaximumTransverseDisplacement.Value.Meters) / w, 0, relative);
        Assert.InRange(Math.Abs(expected.Extrema.MinimumTransverseDisplacement.Value.Meters - actual.Extrema.MinimumTransverseDisplacement.Value.Meters) / w, 0, relative);
    }
}
