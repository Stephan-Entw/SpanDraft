using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Linearly varying distributed loads through the public solver against closed-form beam theory.
/// With ξ = x / L: a downward triangle q(ξ) = -q0 ξ on a simply supported beam has A = q0 L / 6,
/// B = q0 L / 3, V = A - q0 L ξ² / 2, M = A x - q0 L² ξ³ / 6 and
/// w = -q0 L⁴ (7ξ - 10ξ³ + 3ξ⁵) / (360 EI).
/// </summary>
public sealed class SolverLinearLoadAnalyticalTests
{
    private static BeamSectionResult Section(BeamSolution solution, double x,
        EvaluationSide side = EvaluationSide.Right) => solution.EvaluateAt(M(x), side);

    [Theory]
    [InlineData(3000)]
    [InlineData(-3000)]
    public void TriangleLoadOnASimplySupportedBeamMatchesTheClosedFormFields(double q0)
    {
        // q(x) = -q0 x / L: the load grows from zero at the pinned support to q0 at the roller.
        BeamSolution solution = Solve(Beam(loads: [Linear(0, L, 0, -q0)]));
        ForceClose(q0 * L / 6, At(solution, 0).ReactionY);
        ForceClose(q0 * L / 3, At(solution, L).ReactionY);
        foreach (double xi in new[] { 0, 0.125, 0.25, 0.4, 0.5, 0.6, 0.75, 0.9, 1 })
        {
            double x = xi * L;
            BeamSectionResult result = Section(solution, x);
            Close(q0 * L / 6 - q0 * L * xi * xi / 2, result.ShearForce.Newtons, 1e-7);
            Close(q0 * L / 6 * x - q0 * L * L * xi * xi * xi / 6, result.BendingMoment.NewtonMeters, 1e-7);
            DisplacementClose(-q0 * Math.Pow(L, 4) * (7 * xi - 10 * xi * xi * xi + 3 * Math.Pow(xi, 5)) / (360 * EI),
                result.TransverseDisplacement);
            RotationClose(-q0 * Math.Pow(L, 3) * (7 - 30 * xi * xi + 15 * Math.Pow(xi, 4)) / (360 * EI),
                result.RotationRadians);
        }
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(-4000)]
    public void SignChangingLoadHasZeroResultantAndTheClosedFormShear(double q0)
    {
        // q(ξ) = q0 (2ξ - 1): zero total force, so A = -B = q0 L / 6 and V = q0 L (ξ² - ξ + 1/6).
        BeamSolution solution = Solve(Beam(loads: [Linear(0, L, -q0, q0)]));
        ForceClose(q0 * L / 6, At(solution, 0).ReactionY);
        ForceClose(-q0 * L / 6, At(solution, L).ReactionY);
        foreach (double xi in new[] { 0, 0.1, 0.2113248654051871, 0.5, 0.7886751345948129, 0.9, 1 })
        {
            double x = xi * L;
            BeamSectionResult result = Section(solution, x);
            Close(q0 * L * (xi * xi - xi + 1d / 6), result.ShearForce.Newtons, 1e-7);
            Close(q0 * L * L * (xi * xi * xi / 3 - xi * xi / 2 + xi / 6), result.BendingMoment.NewtonMeters, 1e-7);
        }
    }

    [Theory]
    [InlineData(500)]
    [InlineData(-500)]
    public void AntisymmetricTriangleOnACantileverHasNoResultantButAFixedEndMoment(double q)
    {
        // The load -q at a and +q at b has zero resultant. Its moment about the support is q l² / 6.
        const double a = 0.5, b = 1.75, width = b - a;
        BeamSolution solution = Solve(Cantilever(Linear(a, b, -q, q)));
        Close(0, At(solution, 0).ReactionY!.Value.Newtons, 1e-6);
        MomentClose(-q * width * width / 6, At(solution, 0).ReactionMoment);
        // Beyond the load the cantilever is unloaded and the shear vanishes while the moment stays constant.
        BeamSectionResult tip = Section(solution, L, EvaluationSide.Left);
        Close(0, tip.ShearForce.Newtons, 1e-6);
        Close(0, tip.BendingMoment.NewtonMeters, 1e-6);
        BeamSectionResult before = Section(solution, 0.25), after = Section(solution, 1.9);
        Close(0, before.ShearForce.Newtons, 1e-6);
        Close(q * width * width / 6, before.BendingMoment.NewtonMeters, 1e-6);
        Close(0, after.BendingMoment.NewtonMeters, 1e-6);
    }

    [Fact]
    public void ShortSteepTriangleFarFromTheOriginKeepsItsResultantAndFirstMoment()
    {
        // 2^-30 m wide at x = 1.5 with q up to 2^30 * 4000: R = 2000 N and the centroid at a + 2 l / 3.
        double width = Math.ScaleB(1, -30), peak = 4000 * Math.ScaleB(1, 30);
        double a = 1.5, b = a + width;
        BeamSolution solution = Solve(Beam(loads: [Linear(a, b, 0, -peak)]));
        double resultant = peak * (b - a) / 2;
        double centroid = a + 2 * (b - a) / 3;
        ForceClose(resultant * (L - centroid) / L, At(solution, 0).ReactionY);
        ForceClose(resultant * centroid / L, At(solution, L).ReactionY);
        Close(resultant * (L - centroid) / L, Section(solution, 0.5).ShearForce.Newtons, 1e-6);
    }

    [Fact]
    public void LinearLoadEqualsConstantPlusTriangleBySuperposition()
    {
        BeamSolution linear = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(1.25, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Linear(0.25, 1.75, -300, 900)]));
        BeamSolution parts = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(1.25, SupportType.Roller),
            SupportAt(L, SupportType.Roller)], [Uniform(0.25, 1.75, -300), Linear(0.25, 1.75, 0, 1200)]));
        foreach (double x in new[] { 0.1, 0.25, 0.7, 1.25, 1.5, 1.75, 1.9 })
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = linear.EvaluateAt(M(x), side), b = parts.EvaluateAt(M(x), side);
                DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
                RotationClose(a.RotationRadians, b.RotationRadians);
                Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
                Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-7);
            }
    }

    [Fact]
    public void SplittingALinearLoadAtAnyPositionDoesNotChangeTheResult()
    {
        BeamModel Model(params BeamLoad[] loads) => Beam([SupportAt(0, SupportType.Fixed),
            SupportAt(1.25, SupportType.Roller)], loads);
        BeamSolution whole = Solve(Model(Linear(0.25, 1.75, -300, 900)));
        // The half-way intensity is exactly 300; the third-way split exercises the clip interpolation.
        BeamSolution halves = Solve(Model(Linear(0.25, 1, -300, 300), Linear(1, 1.75, 300, 900)));
        BeamSolution thirds = Solve(Model(Linear(0.25, 0.75, -300, 100), Linear(0.75, 1.5, 100, 700),
            Linear(1.5, 1.75, 700, 900)));
        foreach (BeamSolution split in new[] { halves, thirds })
            foreach (double x in new[] { 0.1, 0.25, 0.8, 1.25, 1.6, 1.9 })
                foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
                {
                    BeamSectionResult a = whole.EvaluateAt(M(x), side), b = split.EvaluateAt(M(x), side);
                    DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
                    Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
                    Close(a.ShearForce.Newtons, b.ShearForce.Newtons, 1e-7);
                }
    }

    [Theory]
    [InlineData(200, -700)]
    [InlineData(-1000, 0)]
    [InlineData(-300, -300.5)]
    public void MirroringTheBeamSwapsTheEndIntensitiesAndFlipsTheShear(double first, double second)
    {
        BeamSolution original = Solve(Beam([SupportAt(0, SupportType.Fixed), SupportAt(1.25, SupportType.Roller)],
            [Linear(0.25, 1.75, first, second)]));
        BeamSolution mirrored = Solve(Beam([SupportAt(L, SupportType.Fixed), SupportAt(L - 1.25, SupportType.Roller)],
            [Linear(L - 1.75, L - 0.25, second, first)]));
        foreach (double x in new[] { 0.1, 0.25, 0.8, 1.25, 1.6, 1.9 })
        {
            BeamSectionResult a = original.EvaluateAt(M(x), EvaluationSide.Right);
            BeamSectionResult b = mirrored.EvaluateAt(M(L - x), EvaluationSide.Left);
            DisplacementClose(a.TransverseDisplacement.Meters, b.TransverseDisplacement);
            RotationClose(a.RotationRadians, -b.RotationRadians);
            Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-6);
            Close(a.ShearForce.Newtons, -b.ShearForce.Newtons, 1e-6);
        }
    }

    public static TheoryData<string> PrototypeModels => new()
    {
        "triangle", "trapezoid-overhang", "antisymmetric-cantilever", "overlapping-and-point-loads"
    };

    private static BeamModel PrototypeModel(string name) => name switch
    {
        "triangle" => Beam(loads: [Linear(0, L, 0, -2500)]),
        "trapezoid-overhang" => Beam([SupportAt(0.25, SupportType.Pinned), SupportAt(1.5, SupportType.Roller)],
            [Linear(0, L, -1200, 800)]),
        "antisymmetric-cantilever" => Cantilever(Linear(0.5, 1.75, -700, 700)),
        _ => Beam([SupportAt(0, SupportType.Fixed), SupportAt(0.75, SupportType.Roller), SupportAt(L, SupportType.Roller)],
            [Linear(0.25, 1.75, -200, 600), Uniform(0, 1, 150), Linear(1, L, 400, -100), Point(1.25, -300), Couple(0.5, 50)])
    };

    [Theory]
    [MemberData(nameof(PrototypeModels))]
    public void ProductSolverAgreesWithTheFrozenPrototypeForLinearLoads(string name)
    {
        BeamModel beam = PrototypeModel(name);
        BeamSolution product = Solve(beam);
        var prototype = SolverReference.ReferenceBeamSolver.Prototype(beam);
        foreach (BeamNodeResult node in product.Nodes)
        {
            BeamNodeResult other = Assert.Single(prototype.Nodes, n => n.Position == node.Position);
            DisplacementClose(other.TransverseDisplacement.Meters, node.TransverseDisplacement);
            RotationClose(other.RotationRadians, node.RotationRadians);
            Assert.Equal(other.ReactionY.HasValue, node.ReactionY.HasValue);
            if (other.ReactionY is { } reaction) Close(reaction.Newtons, node.ReactionY!.Value.Newtons, 1e-6);
        }
        foreach (double x in new[] { 0.05, 0.3, 0.6, 0.9, 1.2, 1.55, 1.9 })
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult a = product.EvaluateAt(M(x), side), b = prototype.EvaluateAt(M(x), side);
                DisplacementClose(b.TransverseDisplacement.Meters, a.TransverseDisplacement);
                Close(b.BendingMoment.NewtonMeters, a.BendingMoment.NewtonMeters, 1e-6);
                Close(b.ShearForce.Newtons, a.ShearForce.Newtons, 1e-6);
            }
    }
}
