using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverOutputOracle;

namespace SpanDraft.Tests;

/// <summary>
/// Spring groups at the edge of the numerical contract against the exact rational compatibility oracle, which
/// solves the global force method with the spring law as one more exact condition. The configurations follow the
/// extreme cases of the rework study (stiff and soft springs next to and between close rigid supports, springs
/// only, tight spring clusters, a load on a spring) over the distances L/10 ... L/10^11 and the stiffnesses
/// k L³/EI = 10^-3 ... 10^15. Every model must be delivered with all outputs inside their verified bounds.
/// </summary>
public sealed class SolverSpringAcceptanceTests
{
    private const double L = 4, EI = 210e9 * 8e-6;

    public static TheoryData<string> Configurations => new()
    {
        "stiff-between-rigid", "stiff-next-to-rigid", "soft-between-rigid", "pair", "side-support", "springs-only",
        "three", "eight", "force-on-spring"
    };

    private static MechanicalInput Build(string name, double g, double k)
    {
        const double m = L / 2;
        TransversePointForce[] forces = [new(.3 * L, -1e4), new(.8 * L, -1e4)];
        TransverseLineLoad[] line = [new(0, L, -2e3)];
        MechanicalInput Case(RigidRestraint[] rigid, ElasticRestraint[] elastic, TransversePointForce[]? f = null,
            AppliedPointMoment[]? moments = null) => Model(L, EI, rigid, f ?? forces, moments, line, elastic);
        return name switch
        {
            "stiff-between-rigid" => Case([new(0, U | W), new(m, W), new(m + 2 * g, W), new(L, W)],
                [new(m + g, k, 0, false)], moments: [new(m - 0.1, 5e4)]),
            "stiff-next-to-rigid" => Case([new(0, U | W), new(m, W), new(L, W)], [new(m + g, k, 0, false)],
                moments: [new(m + 2 * g, 5e4)]),
            "soft-between-rigid" => Case([new(0, U | W), new(m, W), new(m + 2 * g, W), new(L, W)],
                [new(m + g, k * 1e-12, 0, false)]),
            "pair" => Case([new(0, U | W), new(L, W)], [new(m, k, 0, false), new(m + g, k, 0, false)]),
            "side-support" => Case([new(0, U | W), new(m, W), new(L, W)], [new(m + g, k, 0, false)]),
            "springs-only" => Case([], [new(0, k, 0, false), new(g, k, 0, false), new(L, k, 0, false)]),
            "three" => Case([new(0, U | W), new(L, W)], [new(m, k, 0, false), new(m + g, k, 0, false),
                new(m + 2 * g, k, 0, false)]),
            "eight" => Case([new(0, U | W), new(L, W)],
                [.. Enumerable.Range(0, 8).Select(i => new ElasticRestraint(m + i * g, k, 0, false))]),
            "force-on-spring" => Case([new(0, U | W), new(L, W)], [new(m, k, 0, false), new(m + g, k, 0, false)],
                [new(m, -2e4), new(.3 * L, -1e4)], [new(m + g, 900)]),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ExtremeSpringGroupsAreDeliveredWithEveryOutputInsideItsVerifiedBound(string name)
    {
        int checkedModels = 0;
        foreach (double ratio in new[] { 1e-1, 1e-3, 1e-5, 1e-7, 1e-9, 1e-11 })
            foreach (int exponent in new[] { -3, 0, 3, 6, 9, 12, 15 })
            {
                double g = ratio * L, k = Math.Pow(10, exponent) * EI / (L * L * L);
                // The eight-spring cluster does not fit into the beam at the coarsest distance.
                if (name == "eight" && L / 2 + 7 * g > L)
                    continue;
                MechanicalInput input = Build(name, g, k);
                try
                {
                    SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(input));
                }
                catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
                {
                    throw new InvalidOperationException($"{name}, distance L*{ratio:e0}, k L^3/EI = 1e{exponent}", exception);
                }
                checkedModels++;
            }
        Assert.Equal(name == "eight" ? 35 : 42, checkedModels);
    }

    [Fact]
    public void StiffSpringsSixNanometresApartMatchTheFrozenExactRationalValues()
    {
        // Pinned and roller support, two springs at 1 m and 1 m + 6 nm with k = 4.48e16 N/m, the load -1000 N at 0.6 m.
        // The expected values are from the exact rational force-method reference of the spring study.
        const double k = 4.480000000000001e16, ei = 210e9 * (0.04 * 0.08 * 0.08 * 0.08 / 12);
        var input = Model(2, ei, [new(0, U | W), new(2, W)], [new(.6, -1000)],
            elastic: [new(1, k, 0, false), new(1 + 6e-9, k, 0, false)]);
        BendingResult result = SolverKernel.Solve(input);
        double Reaction(double x) => Assert.Single(result.Reactions, r => r.Position == x && r.Direction == W).Output.Value;

        Relative(12395.99564491011, Reaction(1));
        Relative(-11603.995645505118, Reaction(1 + 6e-9));
        Relative(303.999965485517, Reaction(0));
        Relative(-3.642856543128868e-05, result.EvaluateAt(.6, FieldSide.Right).Bending[0].Value);
        Relative(-96.00003451448303, result.EvaluateAt(1, FieldSide.Right).Bending[2].Value);
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(input));

        static void Relative(double expected, double actual) =>
            Assert.True(Math.Abs(actual - expected) <= 1e-8 * Math.Abs(expected), $"expected {expected:R}, actual {actual:R}");
    }
}
