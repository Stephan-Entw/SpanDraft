using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Translational spring supports through the public solver against independently derived closed forms.
/// A spring of stiffness k holds the beam with the force R = -k w (upwards positive). With the compliance
/// f = L³ / (48 EI) of a simply supported beam at midspan, a midspan spring under a midspan load P
/// (downward, so the point force is -P) deflects by w = -P f / (1 + k f).
/// </summary>
public sealed class SolverSpringAnalyticalTests
{
    internal static Support Spring(double x, double k) =>
        Support.CreateSpring(M(x), TranslationalStiffness.FromNewtonsPerMeter(k), null);

    private static double Relative(double expected, double actual) =>
        Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);

    [Theory]
    [InlineData(1e3)]
    [InlineData(1e5)]
    [InlineData(1e6)]
    [InlineData(1e8)]
    [InlineData(1e11)]
    public void MidspanSpringOnASimplySupportedBeamMatchesTheFlexibilityFormula(double k)
    {
        const double p = 1500;
        double f = Math.Pow(L, 3) / (48 * EI);
        double w = -p * f / (1 + k * f);
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            Spring(L / 2, k)], [Point(L / 2, -p)]));
        BeamNodeResult mid = At(solution, L / 2);

        Assert.True(Relative(w, mid.TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-k * w, mid.ReactionY!.Value.Newtons) < 1e-10);
        Assert.Equal(0, mid.ReactionX!.Value.Newtons);
        Assert.Null(mid.ReactionMoment);
        // The remaining load is shared by the two rigid supports.
        double ends = (p + k * w) / 2;
        Assert.True(Relative(ends, At(solution, 0).ReactionY!.Value.Newtons) < 1e-9);
        Assert.True(Relative(ends, At(solution, L).ReactionY!.Value.Newtons) < 1e-9);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void BeamOnTwoEndSpringsAddsTheRigidBodyMotionToTheSimplySupportedDeflection(double a)
    {
        // R_A = P (L - a) / L, R_B = P a / L; the end settlements are R / k and the load point deflects
        // by the linear interpolation plus the simply supported deflection P a² (L - a)² / (3 EI L).
        const double p = 2000, k = 4e5;
        double rA = p * (L - a) / L, rB = p * a / L;
        BeamSolution solution = Solve(Beam([Spring(0, k), Spring(L, k)], [Point(a, -p)]));

        Assert.True(Relative(rA, At(solution, 0).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(rB, At(solution, L).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(-rA / k, At(solution, 0).TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-rB / k, At(solution, L).TransverseDisplacement.Meters) < 1e-10);
        double bending = p * a * a * (L - a) * (L - a) / (3 * EI * L);
        double expected = -(rA / k * (L - a) / L + rB / k * a / L) - bending;
        Assert.True(Relative(expected, At(solution, a).TransverseDisplacement.Meters) < 1e-10);
        Assert.Equal(0, At(solution, 0).ReactionX!.Value.Newtons);
        Assert.Equal(0, At(solution, L).ReactionX!.Value.Newtons);
    }

    [Theory]
    [InlineData(1e-3)]
    [InlineData(1)]
    [InlineData(1e3)]
    [InlineData(1e6)]
    [InlineData(1e9)]
    [InlineData(1e12)]
    public void MiddleSpringApproachesTheRigidMiddleSupportWithTheClosedFormDifference(double kLOverEi)
    {
        // Two spans with the load P at L/4. R_B(k) = P d0 / (dBB + 1/k) with the simple-beam flexibilities
        // d0 (deflection at B due to the unit load at L/4) and dBB = L³ / (48 EI); the relative distance
        // to the rigid R_B is 1 / (1 + k dBB).
        const double p = 1000;
        double k = kLOverEi * EI / Math.Pow(L, 3);
        double dBB = Math.Pow(L, 3) / (48 * EI);
        double x = L / 4;
        double d0 = 11 * Math.Pow(L, 3) / (768 * EI);
        double expected = p * d0 / (dBB + 1 / k);
        double rigid = p * d0 / dBB;

        BeamSolution spring = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            Spring(L / 2, k)], [Point(x, -p)]));
        BeamSolution reference = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            SupportAt(L / 2, SupportType.Roller)], [Point(x, -p)]));

        double actual = At(spring, L / 2).ReactionY!.Value.Newtons;
        Assert.True(Relative(expected, actual) < 1e-9, $"expected {expected:R}, actual {actual:R}");
        Assert.True(Relative(rigid, At(reference, L / 2).ReactionY!.Value.Newtons) < 1e-10);
        double distance = (rigid - actual) / rigid;
        double analytic = 1 / (1 + kLOverEi / 48);
        // The distance is resolved in the delivered digits only while it exceeds the rounding of the reactions.
        if (analytic > 1e-6)
            Assert.True(Relative(analytic, distance) < 1e-6, $"distance {distance:R} vs {analytic:R}");
        Assert.True(distance >= -1e-12);
    }
}
