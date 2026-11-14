using SpanDraft.Solver;
using static SpanDraft.Tests.SolverExactNumber;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.MultiSectionTestSupport;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Kernel results with several stiffness sections against the exact rational oracle: the global force method
/// with the compatibility integrals taken section by section, independent of the kernel's station equations.
/// </summary>
public sealed class SolverMultiSectionExactTests
{
    private const double Ei = 3.5;

    private static StiffnessSection[] Two(double boundary, double ratio, double length = 2) =>
        [new(0, boundary, Ei), new(boundary, length, Ei / ratio)];

    public static TheoryData<string> Names => new()
    {
        "cantilever-step", "two-span-boundary-on-support", "three-sections-mixed", "boundary-on-spring",
        "boundary-on-partial-restraint", "boundary-on-support-and-load", "tiny-section", "contrast-1e3", "contrast-1e6"
    };

    internal static MechanicalInput Case(string name) => name switch
    {
        "cantilever-step" => Multi(2, Two(0.8, 3), forces: [new(2, -1500)]),
        "two-span-boundary-on-support" => Multi(2, Two(1.25, 5), [new(0, U | W), new(1.25, W), new(2, W)],
            lines: [new(0, 2, -800)]),
        "three-sections-mixed" => Multi(2, [new(0, 0.6, Ei), new(0.6, 1.4, Ei / 4), new(1.4, 2, Ei * 4)],
            [new(0, U | W | Theta), new(1.5, W)], forces: [new(0.6, -3)], moments: [new(1.4, 2)],
            lines: [new(0.3, 1.7, -2, 5)]),
        "boundary-on-spring" => Multi(2, Two(0.8, 10), [new(0, U | W), new(2, W)], forces: [new(1.3, -7)],
            lines: [new(0, 0.8, -3)], elastic: [new(0.8, 40, 0, false)]),
        "boundary-on-partial-restraint" => Multi(2, Two(1, 8), [new(0, U | W)], forces: [new(2, -9)],
            moments: [new(1, 3)], elastic: [new(1, 0, 25, true)]),
        "boundary-on-support-and-load" => Multi(2, Two(1, 0.125), [new(0, U | W | Theta), new(1, W)],
            forces: [new(1, -4), new(1.5, 2)], lines: [new(1, 2, 3, -1)]),
        // A section far shorter than its neighbours keeps its own station distances.
        "tiny-section" => Multi(2, [new(0, 1, Ei), new(1, 1.0009765625, Ei * 64), new(1.0009765625, 2, Ei)],
            [new(0, U | W), new(2, W)], forces: [new(1.5, -5)]),
        "contrast-1e3" => Multi(2, Two(0.8, 1e3), forces: [new(2, -1500)], lines: [new(0.2, 1.8, -400)]),
        "contrast-1e6" => Multi(2, Two(1.25, 1e6), [new(0, U | W), new(1.25, W), new(2, W)],
            forces: [new(0.5, -300)], lines: [new(0, 2, -800)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void FieldsReactionsAndQualityEncloseTheExactSolution(string name) =>
        SolverBendingOutputTests.CheckExactOutputs(SolverOutputOracle.FromCompatibility(Case(name)));

    [Fact]
    public void ABoundaryIsAnEventStationWithoutJumpsAndTheSectionRotationFieldIsContinuousThere()
    {
        BendingResult result = SolverKernel.Solve(Case("cantilever-step"));
        BendingStation station = Assert.Single(result.Stations, s => s.Position == 0.8);
        Assert.Equal(BendingJump.None, station.Jumps);
        // The kinematics are the shared station state; their bounds may differ with the section of the side.
        Assert.Equal(station.Left!.Bending[0].Value, station.Right!.Bending[0].Value);
        Assert.Equal(station.Left.Bending[1].Value, station.Right.Bending[1].Value);
        Assert.Equal(station.Left.Bending[2].Value, station.Right.Bending[2].Value, 1e-9);
        Assert.Equal(station.Left.Bending[3].Value, station.Right.Bending[3].Value, 1e-9);
        Assert.DoesNotContain(result.Reactions, r => r.Position == 0.8);
    }

    [Fact]
    public void EachBoundaryAddsExactlyFourUnknownsAndKeepsTheOtherRowsOfTheSystem()
    {
        MechanicalInput single = Multi(2, [new(0, 2, Ei)], forces: [new(2, -1)]);
        MechanicalInput one = Multi(2, [new(0, 0.8, Ei), new(0.8, 2, Ei)], forces: [new(2, -1)]);
        MechanicalInput two = Multi(2, [new(0, 0.8, Ei), new(0.8, 1.3, Ei), new(1.3, 2, Ei)], forces: [new(2, -1)]);

        int baseline = SolverKernel.BuildSystem(single).Dimension;
        Assert.Equal(baseline + 4, SolverKernel.BuildSystem(one).Dimension);
        Assert.Equal(baseline + 8, SolverKernel.BuildSystem(two).Dimension);
    }

    [Fact]
    public void AnUnloadedMultiSectionBeamIsTheExactNullPath()
    {
        BendingResult result = SolverKernel.Solve(Multi(2, Two(0.8, 3), [new(0, U | W), new(2, W)]));
        Assert.True(result.Quality.IsUnloaded);
        Assert.All(result.Stations, s => Assert.Equal(0, s.Right?.Bending[0].Value ?? 0));
        Assert.Equal(3, result.Stations.Length);
    }

    [Fact]
    public void ABoundaryNeverMakesARigidBodyModeStable()
    {
        MechanicalInput input = Multi(2, Two(1, 2), [new(0, U | W)]);
        Assert.Throws<MechanicalMechanismException>(() => SolverKernel.BuildSystem(input));
    }

    [Theory]
    [InlineData(1e3)]
    [InlineData(1e6)]
    public void StiffnessContrastDoesNotChangeForcesOfADeterminateCantileverAndScalesDeflections(double ratio)
    {
        // R and M(0) do not depend on EI; w(L) = P (a³ / (3 EI1) + ...) grows with the soft part.
        MechanicalInput input = Multi(2, Two(0.8, ratio), forces: [new(2, -1500)]);
        BendingResult result = SolverKernel.Solve(input);
        double l2 = 1.2;
        double expected = -1500 * ((8 - l2 * l2 * l2) / (3 * Ei) + l2 * l2 * l2 * ratio / (3 * Ei));
        Assert.True(Relative(expected, result.EvaluateAt(2, FieldSide.Left).Bending[0].Value) < 1e-9);
        Assert.True(Relative(1500, result.Reactions.Single(r => r.Position == 0 && r.Direction == W).Output.Value) < 1e-9);
    }

    [Fact]
    public void TheNormalizationFollowsTheStiffestSectionAndEveryIntervalKeepsItsOwnNormalizedStiffness()
    {
        StiffnessSection[] sections = [new(0, 0.8, 3), new(0.8, 1.3, 3e5), new(1.3, 2, 12)];
        BendingSystem system = SolverKernel.BuildSystem(Multi(2, sections, forces: [new(2, -1)]));

        Assert.Equal(Binary64.PowerOfTwoExponent(3e5), system.EEI);
        Assert.Equal(sections.Select(s => Math.ScaleB(s.EI, -system.EEI)), system.IntervalEI.Select(e => e.Value));
        Assert.All(system.IntervalEI, e => Assert.InRange(e.Value, double.Epsilon, 2));
        Assert.InRange(system.IntervalEI.Max(e => e.Value), 1, 2);
        Assert.Equal(system.IntervalEI.Min(e => e.Value), system.LeastEI.Value);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0.2)]
    public void InteriorDisplacementExtremaAreStationaryPointsOfTheExactRotationInEverySection(double ratio)
    {
        // The second span has another EI than the first; a piece built with the wrong section's EI would miss the root.
        var input = Multi(2, Two(1.25, ratio), [new(0, U | W), new(1.25, W), new(2, W)], lines: [new(0, 2, -800)]);
        BendingResult result = SolverKernel.Solve(input);
        var oracle = SolverOutputOracle.FromCompatibility(input);
        double thetaScale = new[] { 0d, 0.5, 1.25, 1.6, 2 }.Max(x => Math.Abs(result.EvaluateAt(x, x == 2 ? FieldSide.Left : FieldSide.Right).Bending[1].Value));
        BendingExtremum[] interior = new[] { result.Extrema.W.Minimum, result.Extrema.W.Maximum }
            .Where(e => e.Side == FieldSide.Interior).ToArray();

        Assert.NotEmpty(interior);
        Assert.Contains(interior, e => e.Position > 1.25);
        foreach (BendingExtremum extremum in interior)
            Assert.True(oracle.Field(extremum.Position, FieldSide.Interior)[1].Abs().CompareTo(From(1e-7 * thetaScale)) <= 0,
                $"w' is not zero at x = {extremum.Position:R}");
    }
}
