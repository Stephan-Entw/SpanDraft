using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Rotational springs and partial restraints through the public solver against closed forms. A rotational spring
/// of stiffness kθ acts on the beam with the support moment C = -kθ θ (counterclockwise positive). A cantilever whose
/// root is held in translation and elastic in rotation (partial restraint) under the tip load -P deflects by
/// w = -(P L³ / (3 EI) + P L² / kθ).
/// </summary>
public sealed class SolverRotationalSpringTests
{
    internal static Support Rotational(double x, double kTheta) =>
        Support.CreateSpring(M(x), null, RotationalStiffness.FromNewtonMetersPerRadian(kTheta));

    internal static Support Partial(double x, double kTheta) =>
        Support.CreateSpring(M(x), null, RotationalStiffness.FromNewtonMetersPerRadian(kTheta), rigidTranslation: true);

    internal static Support Combined(double x, double k, double kTheta) =>
        Support.CreateSpring(M(x), TranslationalStiffness.FromNewtonsPerMeter(k),
            RotationalStiffness.FromNewtonMetersPerRadian(kTheta));

    private static double Relative(double expected, double actual) =>
        Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);

    [Theory]
    [InlineData(1e2)]
    [InlineData(1e4)]
    [InlineData(1e6)]
    [InlineData(1e9)]
    [InlineData(1e12)]
    public void PartialRestraintCantileverAddsTheRootRotationToTheTipDeflection(double kTheta)
    {
        const double p = 800;
        BeamSolution solution = Solve(Beam([Partial(0, kTheta)], [Point(L, -p)]));
        BeamNodeResult root = At(solution, 0), tip = At(solution, L);

        Assert.True(Relative(-(p * Math.Pow(L, 3) / (3 * EI) + p * L * L / kTheta), tip.TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-p * L / kTheta, root.RotationRadians) < 1e-10);
        Assert.True(Relative(p, root.ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(p * L, root.ReactionMoment!.Value.NewtonMeters) < 1e-10);
        Assert.Equal(0, root.TransverseDisplacement.Meters);
        Assert.Equal(0, root.ReactionX!.Value.Newtons);
    }

    [Theory]
    [InlineData(1e3)]
    [InlineData(1e6)]
    [InlineData(1e10)]
    public void TwoEndRotationalSpringsGiveTheClosedFormEndMomentUnderUniformLoad(double kTheta)
    {
        // M = q L² / 12 / (1 + 2 EI / (kθ L)): the fixed-end value reduced by the end flexibility.
        const double q = 3000;
        double moment = q * L * L / 12 / (1 + 2 * EI / (kTheta * L));
        BeamSolution solution = Solve(Beam([Partial(0, kTheta), Partial(L, kTheta)], [Uniform(0, L, -q)]));

        Assert.True(Relative(moment, At(solution, 0).ReactionMoment!.Value.NewtonMeters) < 1e-10);
        Assert.True(Relative(-moment, At(solution, L).ReactionMoment!.Value.NewtonMeters) < 1e-10);
        Assert.True(Relative(q * L / 2, At(solution, 0).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(-moment / kTheta, At(solution, 0).RotationRadians) < 1e-10);
        // The support moment is the bending moment just inside the beam, with the sign of its position.
        Close(-moment, solution.EvaluateAt(M(0), EvaluationSide.Right).BendingMoment.NewtonMeters, 1e-6 + 1e-9 * moment);
    }

    [Theory]
    [InlineData(2e4, 5e3)]
    [InlineData(1e8, 1e9)]
    [InlineData(1e3, 1e11)]
    public void CombinedSpringOnACantileverAddsTranslationAndRotationOfTheRoot(double k, double kTheta)
    {
        const double p = 500;
        BeamSolution solution = Solve(Beam([Combined(0, k, kTheta)], [Point(L, -p)]));
        double expected = -(p / k + p * L * L / kTheta + p * Math.Pow(L, 3) / (3 * EI));
        Assert.True(Relative(expected, At(solution, L).TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-p / k, At(solution, 0).TransverseDisplacement.Meters) < 1e-10);
        Assert.True(Relative(-p * L / kTheta, At(solution, 0).RotationRadians) < 1e-10);
        Assert.True(Relative(p, At(solution, 0).ReactionY!.Value.Newtons) < 1e-10);
        Assert.True(Relative(p * L, At(solution, 0).ReactionMoment!.Value.NewtonMeters) < 1e-10);
    }

    [Theory]
    [InlineData(1e2)]
    [InlineData(1e4)]
    [InlineData(1e7)]
    public void RotationalSpringInsideASimplySupportedBeamReducesTheMidspanRotation(double kTheta)
    {
        // Load P at L/4: the plain rotation at midspan is P L² / (128 EI); the couple C = -kθ θ adds C L / (12 EI),
        // so θ = θP / (1 + kθ L / (12 EI)).
        const double p = 1200;
        double thetaP = p * L * L / (128 * EI);
        double theta = thetaP / (1 + kTheta * L / (12 * EI));
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            Rotational(L / 2, kTheta)], [Point(L / 4, -p)]));
        BeamNodeResult mid = At(solution, L / 2);

        Assert.True(Relative(theta, mid.RotationRadians) < 1e-9);
        Assert.True(Relative(-kTheta * theta, mid.ReactionMoment!.Value.NewtonMeters) < 1e-9);
        Assert.Null(mid.ReactionY);
        Assert.Equal(0, mid.ReactionX!.Value.Newtons);
    }

    [Fact]
    public void ARotationalSpringDoesNotReactToASymmetricLoadAtTheMidspan()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            Rotational(L / 2, 1e5)], [Uniform(0, L, -1000)]));
        Close(0, At(solution, L / 2).ReactionMoment!.Value.NewtonMeters, 1e-6);
        Close(0, At(solution, L / 2).RotationRadians, 1e-14);
    }

    [Theory]
    [InlineData(1e3, 4e3)]
    [InlineData(1e7, 2e6)]
    public void BeamHeldOnlyBySpringsSatisfiesSpringLawsAndEquilibrium(double k, double kTheta)
    {
        Support[] supports = [Combined(0.3, k, kTheta), Combined(1.7, 2 * k, kTheta / 3)];
        BeamSolution solution = Solve(Beam(supports, [Uniform(0, L, -400), Point(1, -900), Couple(0.5, 70)]));
        BeamNodeResult a = At(solution, 0.3), b = At(solution, 1.7);

        Close(-k * a.TransverseDisplacement.Meters, a.ReactionY!.Value.Newtons, 1e-6);
        Close(-2 * k * b.TransverseDisplacement.Meters, b.ReactionY!.Value.Newtons, 1e-6);
        Close(-kTheta * a.RotationRadians, a.ReactionMoment!.Value.NewtonMeters, 1e-6);
        Close(-kTheta / 3 * b.RotationRadians, b.ReactionMoment!.Value.NewtonMeters, 1e-6);
        Close(400 * L + 900, a.ReactionY!.Value.Newtons + b.ReactionY!.Value.Newtons, 1e-6);
        // Moments about x = 0: loads 400 L · L/2 + 900 · 1, the couple +70 and the reactions.
        double about = -(400 * L * L / 2) - 900 * 1 + 70 + a.ReactionMoment!.Value.NewtonMeters + b.ReactionMoment!.Value.NewtonMeters
            + a.ReactionY!.Value.Newtons * 0.3 + b.ReactionY!.Value.Newtons * 1.7;
        Close(0, about, 1e-5);
    }

    [Theory]
    [InlineData(1e-3)]
    [InlineData(1)]
    [InlineData(1e6)]
    [InlineData(1e12)]
    [InlineData(1e15)]
    public void PartialRestraintApproachesTheFixedSupportWithTheClosedFormDifference(double kLOverEi)
    {
        const double p = 700;
        double kTheta = kLOverEi * EI / L;
        BeamSolution partial = Solve(Beam([Partial(0, kTheta)], [Point(L, -p)]));
        BeamSolution fixedSupport = Solve(Cantilever(Point(L, -p)));

        double tip = At(partial, L).TransverseDisplacement.Meters, reference = At(fixedSupport, L).TransverseDisplacement.Meters;
        Assert.True(Relative(-(p * Math.Pow(L, 3) / (3 * EI) + p * L * L / kTheta), tip) < 1e-9);
        // Distance to the clamp: w_clamped / w = 1 / (1 + 3 EI / (kθ L)).
        Assert.True(Relative(p * L, At(partial, 0).ReactionMoment!.Value.NewtonMeters) < 1e-9);
        Assert.True(Math.Abs(reference) <= Math.Abs(tip) * (1 + 1e-12));
    }

    [Fact]
    public void ARotationalSpringAloneIsAnUnstableMechanismButAPartialRestraintAloneIsStable()
    {
        Assert.Equal(SolverErrorCode.UnstableModel,
            Assert.Throws<BeamSolverException>(() => Solve(Beam([Rotational(1, 1e5)], [Point(1, -1)]))).Code);
        // The partial restraint alone holds translation and, elastically, rotation: stable.
        BeamSolution solution = Solve(Beam([Partial(1, 1e5)], [Point(1.5, -1000)]));
        Assert.Equal(0, At(solution, 1).TransverseDisplacement.Meters);
    }
}
