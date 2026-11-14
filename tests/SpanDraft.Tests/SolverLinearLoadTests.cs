using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

/// <summary>
/// Kernel-level linearly varying line loads against the independent exact rational mechanics of
/// <see cref="SolverOutputOracle"/>: every field, reaction and piece bound is enclosed and gated.
/// </summary>
public sealed class SolverLinearLoadTests
{
    private static readonly double Short = Math.ScaleB(1, -30);

    public static TheoryData<string> Cases => new()
    {
        "triangle", "trapezoid-overhang", "sign-change", "antisymmetric-cantilever", "ends-at-support",
        "overlapping", "short-far-from-origin", "right-fixed-ramp", "zero-end-intensities", "ramp-with-point-loads",
        "zero-crossing-at-support", "zero-crossing-ulp-below-support", "zero-crossing-ulp-above-support",
        "extremely-short-far-from-origin", "load-from-fixed-end-to-free-end", "one-ulp-rise", "steep-short-triangle", "non-dyadic-clips", "cancelling-clip"
    };

    internal static MechanicalInput Input(string name) => name switch
    {
        "triangle" => Model(length: 3, ei: 2e3, restraints: [new(0, U | W), new(3, W)], lines: [new(0, 3, 0, -6)]),
        "trapezoid-overhang" => Model(length: 3, ei: 5, restraints: [new(0.5, U | W), new(2.5, W)],
            lines: [new(0, 3, -2, 5)]),
        "sign-change" => Model(length: 2, ei: 7, restraints: [new(0, U | W), new(2, W)], lines: [new(0, 2, -3, 3)]),
        "antisymmetric-cantilever" => Model(length: 2, ei: 3, lines: [new(0.5, 1.75, -4, 4)]),
        "ends-at-support" => Model(length: 2, ei: 11, restraints: [new(0, U | W), new(1, W), new(2, W)],
            lines: [new(0.25, 1, 3, -5), new(1, 2, -1, 4)]),
        "overlapping" => Model(length: 4, ei: 13, restraints: [new(0, U | W), new(1.5, W), new(4, W)],
            lines: [new(0, 4, 1, 1), new(0.5, 3.5, -8, 2), new(1, 2.5, 5, -7)]),
        "short-far-from-origin" => Model(length: 2, ei: 1, restraints: [new(0, U | W), new(2, W)],
            lines: [new(1.5, 1.5 + Short, 0, Math.ScaleB(1, 30))]),
        "right-fixed-ramp" => Model(length: 2, ei: 1,
            restraints: [new(2, U | W | Theta)], lines: [new(0, 2, 10, 0)]),
        "zero-end-intensities" => Model(length: 2, ei: 1, restraints: [new(0, U | W), new(2, W)],
            lines: [new(0, 1, 0, 0), new(0.5, 2, 0, 6)]),
        "ramp-with-point-loads" => Model(length: 3, ei: 9, restraints: [new(0, U | W), new(2, W)],
            forces: [new(1, -3), new(2.5, 2)], moments: [new(1.5, 4)], lines: [new(0.5, 2.75, -6, 9)]),
        // The load -3 ... 3 on [0, 2] crosses zero at x = 1, exactly at, one ulp below and one ulp above a support.
        "zero-crossing-at-support" => Model(length: 2, ei: 7, restraints: [new(0, U | W), new(1, W), new(2, W)],
            lines: [new(0, 2, -3, 3)]),
        "zero-crossing-ulp-below-support" => Model(length: 2, ei: 7,
            restraints: [new(0, U | W), new(Math.BitDecrement(1), W), new(2, W)], lines: [new(0, 2, -3, 3)]),
        "zero-crossing-ulp-above-support" => Model(length: 2, ei: 7,
            restraints: [new(0, U | W), new(Math.BitIncrement(1), W), new(2, W)], lines: [new(0, 2, -3, 3)]),
        "extremely-short-far-from-origin" => Model(length: 2, ei: 1, restraints: [new(0, U | W), new(2, W)],
            lines: [new(1.5, 1.5 + Math.ScaleB(1, -42), 1e5, -Math.ScaleB(1, 45))]),
        "load-from-fixed-end-to-free-end" => Model(length: 2, ei: 5, lines: [new(0, 2, 8, -8)]),
        "one-ulp-rise" => Model(length: 2, ei: 3, restraints: [new(0, U | W), new(2, W)],
            lines: [new(0.25, 1.75, 3, Math.BitIncrement(3))]),
        "steep-short-triangle" => Model(length: 3, ei: 2, restraints: [new(0, U | W), new(3, W)],
            lines: [new(1, 1 + Math.ScaleB(1, -20), 0, 1e9)]),
        // Interpolation at the clip ends of decimal data rounds, so the data error of the clipped end is nonzero.
        "non-dyadic-clips" => Model(length: 3, ei: 7, restraints: [new(0, U | W), new(1.1, W), new(1.7, W), new(3, W)],
            lines: [new(0.3, 2.9, -1.7, 4.3), new(0.2, 2.7, 3.1, -0.7)]),
        // Both clip ends of the narrow section lie next to the zero crossing, where the interpolation cancels
        // almost completely and the absolute data error dominates the value.
        "cancelling-clip" => Model(length: 3, ei: 7,
            restraints: [new(0, U | W), new(1.6, W), new(1.6 + Math.ScaleB(1, -24), W), new(3, W)],
            lines: [new(0.3, 2.9, 3.3e9, -3.3e9 + 0.1)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void LinearLoadsEncloseTheExactMechanicsThroughoutEveryPiece(string name) =>
        SolverBendingOutputTests.CheckExactOutputs(SolverOutputOracle.FromCompatibility(Input(name)));

    [Fact]
    public void SegmentIntegralsEncloseTheExactClippedIntegralsAtEveryOrder()
    {
        // Supports clip the loads: [0.25, 1.75] is cut at 1, and the second load is cut at 0.75 and 1.5.
        MechanicalInput input = Model(length: 2, restraints: [new(0, U | W), new(1, W), new(1.5, W)],
            forces: [new(0.5, -3)], moments: [new(0.25, 2)],
            lines: [new(0.25, 1.75, -3, 5), new(0.125, 1.875, 7, -1)]);
        StationModel stations = StationModel.Create(input);
        BendingPreparation preparation = BendingPreparation.Create(input, stations);
        PreparedBendingLoads loads = PreparedBendingLoads.Create(input, stations, preparation);
        for (int j = 0; j < loads.Segments.Length; j++)
        {
            double start = stations.MechanicalStations[j].Position, end = stations.MechanicalStations[j + 1].Position;
            foreach (double x in new[] { start, 0.25, 0.375, 0.75, 1, 1.25, 1.5, end }.Where(x => x >= start && x <= end))
                for (int p = 1; p <= 4; p++)
                    Encloses(loads.Segments[j].Integral(x, p),
                        SolverBendingNumericsTests.ExactIntegral(input, preparation, start, end, x, p));
        }
    }

    [Fact]
    public void ARampWithEqualEndIntensitiesAddsNoValueToTheConstantIntegral()
    {
        double[] widths = [1, 0.75, Math.ScaleB(1, -20)];
        foreach (double width in widths)
            foreach (double q in new[] { 3.5, -0.125 })
                for (int p = 1; p <= 4; p++)
                    foreach (double x in new[] { width / 2, width, width * 2 })
                    {
                        var constant = new SegmentBendingLoads([], [], [new(0, width, q)], 0);
                        var ramp = new SegmentBendingLoads([], [], [new(0, width, q, 0, q, 0, true)], 0);
                        Bounded expected = constant.Integral(x, p), actual = ramp.Integral(x, p);
                        Assert.Equal(expected.Value, actual.Value);
                        Assert.True(actual.Error >= expected.Error);
                    }
    }

    [Fact]
    public void TheInnerAndOuterRampBranchesAgreeAtTheLoadEnd()
    {
        // At x = End the inner branch (x <= End) and the outer branch with far = 0 describe the same value.
        foreach (double width in new[] { 1, 0.375, Math.ScaleB(1, -25) })
            for (int p = 1; p <= 4; p++)
            {
                Bounded near = new(width), rise = new(-6.5), far = default, extent = new(width);
                Bounded inner = BendingLoadTerms.Ramp(rise, near, far, extent, p, inside: true);
                Bounded outer = BendingLoadTerms.Ramp(rise, near, far, extent, p, inside: false);
                SolverExactNumber exact = From(-6.5) * SolverBendingTestSupport.Pow(From(width), p) /
                    Integer(SolverBendingTestSupport.Factorial(p + 1));
                Encloses(inner, exact);
                Encloses(outer, exact);
            }
    }

    [Fact]
    public void RampTermsEncloseTheRationalRampIntegralsInsideAndBeyondTheLoad()
    {
        foreach (double rise in new[] { 4.25, -4.25, 1e-3, -Math.ScaleB(1, 40) })
            foreach (double beyond in new[] { 0, 0.5, 3, Math.ScaleB(1, -30), 1e6 })
                for (int p = 1; p <= 4; p++)
                {
                    const double width = 0.625;
                    foreach (bool inside in new[] { true, false })
                    {
                        // Inside is only meaningful for near <= width, so inside uses a share of the width.
                        double near = inside ? width * 0.75 : width + beyond, far = inside ? 0 : beyond;
                        if (!inside && beyond == 0) continue;
                        SolverExactNumber exact = SolverOutputOracle.LineIntegral(Integer(0), From(rise), From(width),
                            From(near), From(far), p);
                        Encloses(BendingLoadTerms.Ramp(new(rise), RoundCounted.BoundedDistance(near, 0, 0),
                            RoundCounted.BoundedDistance(far, 0, 0), new(width), p, inside), exact);
                    }
                }
    }

    [Fact]
    public void ClippedIntensitiesInterpolateTheOriginalLoadWithTheirDataError()
    {
        MechanicalInput input = Model(length: 3, restraints: [new(0, U | W), new(1.1, W), new(3, W)],
            lines: [new(0.3, 2.9, -1.7, 4.3)]);
        StationModel stations = StationModel.Create(input);
        BendingPreparation preparation = BendingPreparation.Create(input, stations);
        PreparedBendingLoads loads = PreparedBendingLoads.Create(input, stations, preparation);
        SolverExactNumber scale = PowerOfTwo(preparation.LineLoadExponent);
        foreach (ScaledLineLoad clipped in loads.Segments.SelectMany(segment => segment.Lines))
        {
            Assert.True(clipped.HasRamp);
            foreach ((double x, Bounded value) in new[] { (clipped.Start, clipped.StartIntensity), (clipped.End, clipped.EndIntensity) })
            {
                SolverExactNumber exact = (From(-1.7) + (From(4.3) - From(-1.7)) * (From(x) - From(0.3)) /
                    (From(2.9) - From(0.3))) * scale;
                Encloses(value, exact);
            }
        }
        // The original load ends are carried exactly.
        Assert.Equal(Binary64.ScaleByPowerOfTwo(-1.7, preparation.LineLoadExponent), loads.Segments[0].Lines[0].StartValue);
        Assert.Equal(0, loads.Segments[0].Lines[0].StartError);
        Assert.Equal(Binary64.ScaleByPowerOfTwo(4.3, preparation.LineLoadExponent), loads.Segments[1].Lines[0].EndValue);
    }

    [Fact]
    public void PowerOfTwoUnitChangePreservesLinearFieldsBoundsAndGateBits()
    {
        MechanicalInput input = Input("ramp-with-point-loads");
        var changed = new MechanicalInput(Math.ScaleB(input.Length, 20), Math.ScaleB(input.EI, -10),
            input.Restraints.Select(r => r with { Position = Math.ScaleB(r.Position, 20) }),
            input.PointForces.Select(p => new TransversePointForce(Math.ScaleB(p.Position, 20), Math.ScaleB(p.Newtons, 5))),
            input.PointMoments.Select(p => new AppliedPointMoment(Math.ScaleB(p.Position, 20), Math.ScaleB(p.NewtonMeters, 25))),
            input.LineLoads.Select(q => new TransverseLineLoad(Math.ScaleB(q.Start, 20), Math.ScaleB(q.End, 20),
                Math.ScaleB(q.StartNewtonsPerMeter, -15), Math.ScaleB(q.EndNewtonsPerMeter, -15))));
        BendingResult a = SolverKernel.Solve(input), b = SolverKernel.Solve(changed);
        Assert.Equal(a.Quality.MaxRatios, b.Quality.MaxRatios);
        Assert.Equal(a.Quality.Mu, b.Quality.Mu);
        Assert.Equal(a.Fields.State, b.Fields.State);
        Assert.Equal(a.Fields.StateBound, b.Fields.StateBound);
        foreach (double x in new[] { 0, 0.5, 1, 1.5, 2, 2.5, 2.75, 3 })
            foreach (FieldSide side in new[] { FieldSide.Left, FieldSide.Right })
            {
                NormalizedBendingField original = a.Fields.EvaluateNormalized(x, side);
                NormalizedBendingField scaled = b.Fields.EvaluateNormalized(Math.ScaleB(x, 20), side);
                Assert.Equal(original.Values, scaled.Values);
                Assert.Equal(original.Bounds, scaled.Bounds);
            }
        SolverBendingOutputTests.CheckExactOutputs(SolverOutputOracle.FromCompatibility(changed));
    }

    [Fact]
    public void BothEndIntensitiesMustBeZeroForTheLoadToBeUnloaded()
    {
        Assert.True(BendingExcitation.IsUnloaded(Model(lines: [new(0, 1, 0, -0d)])));
        Assert.False(BendingExcitation.IsUnloaded(Model(lines: [new(0, 1, 0, Math.ScaleB(1, -1000))])));
        // Equal and opposite ends cancel in the resultant but are a loaded system with exact nonzero fields.
        MechanicalInput compensating = Model(length: 2, restraints: [new(0, U | W), new(2, W)], lines: [new(0, 2, -5, 5)]);
        BendingResult result = SolverKernel.Solve(compensating);
        Assert.False(result.Quality.IsUnloaded);
        Assert.NotEqual(0, result.EvaluateAt(0, FieldSide.Right).Bending[3].Value);
        SolverBendingOutputTests.CheckExactOutputs(SolverOutputOracle.FromCompatibility(compensating));
    }

    [Fact]
    public void AClippedLoadWidthBelowTheNormalizedLimitIsARangeFailure()
    {
        // The support at 2^-989 clips the load [2^-990, 1] to a width of 2^-990, below 2^-969 of the beam length.
        double support = Math.ScaleB(1, -989), start = Math.ScaleB(1, -990);
        MechanicalInput narrow = Model(length: 1, restraints: [new(0, U | W), new(support, W), new(1, W)],
            lines: [new(start, 1, 0, 1)]);
        var error = Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(narrow));
        Assert.Contains("clipped distributed load", error.Message);

        // The same load without the nearby support is not clipped to a narrow piece.
        MechanicalInput wide = Model(length: 1, restraints: [new(0, U | W), new(1, W)], lines: [new(start, 1, 0, 1)]);
        Assert.True(SolverKernel.Solve(wide).Quality.GatePassed);
    }

    [Theory]
    [InlineData(double.MaxValue, -double.MaxValue)]
    [InlineData(1e300, 1e300)]
    [InlineData(1e300, -1e300)]
    public void OverflowingIntensitiesAreRejectedWithoutPartialResults(double first, double second)
    {
        MechanicalInput input = Model(length: 2, restraints: [new(0, U | W), new(2, W)], lines: [new(0, 2, first, second)]);
        Assert.ThrowsAny<Exception>(() => SolverKernel.Solve(input));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryExtremumCandidateMatchesTheExactRootIsolationOfItsPiece(string name) =>
        CheckCandidatesAgainstExactRoots(name, Input(name));

    /// <summary>
    /// Compares the candidate positions of every piece with the exact roots of the derivative polynomials
    /// isolated by a Sturm sequence on exactly interpolated oracle fields: no missed and no spurious roots.
    /// </summary>
    internal static void CheckCandidatesAgainstExactRoots(string name, MechanicalInput input)
    {
        SolverOutputOracle oracle = SolverOutputOracle.FromCompatibility(input);
        BendingResult result = SolverKernel.Solve(input);
        foreach (BendingPiece piece in result.Pieces)
            foreach ((BendingComponent component, int field, bool derivative) in new[]
                { (BendingComponent.W, 1, false), (BendingComponent.M, 3, false), (BendingComponent.V, 3, true) })
            {
                var polynomial = SolverExactRootOracle.FieldPolynomial(oracle, piece.Start, piece.End, field);
                if (derivative)
                    polynomial = polynomial.Derivative();
                // A root closer to an end than the local boundary tolerance (32 ulps of 1) is mapped to that end,
                // whose value is an endpoint candidate of the piece anyway; no separate interior root exists.
                const double boundary = 1e-13;
                var exact = SolverExactRootOracle.InteriorRoots(polynomial)
                    .Where(r => r.Low.ToDouble() > boundary && r.High.ToDouble() < 1 - boundary).ToList();
                double[] candidates = piece.Candidates(component).Where(t => t > boundary && t < 1 - boundary).ToArray();
                string context = $"{name} piece [{piece.Start:R}, {piece.End:R}] {component}";
                Assert.True(exact.Count == candidates.Length, $"{context}: {exact.Count} exact roots, {candidates.Length} candidates");
                // The clip data error of a cancelling load shifts a root by about error / (slope * piece length);
                // the extremum value stays verified at the candidate, so only that case needs a wider position bound.
                double tolerance = name == "cancelling-clip" ? 1e-7 : 1e-9;
                foreach (var ((low, high), t) in exact.Zip(candidates))
                    Assert.True(t >= low.ToDouble() - tolerance && t <= high.ToDouble() + tolerance,
                        $"{context}: candidate {t:R} outside the exact root interval [{low.ToDouble():R}, {high.ToDouble():R}]");
            }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PieceMajorantEnclosesTheExactLargerEndIntensitySumTightly(string name)
    {
        MechanicalInput input = Input(name);
        BendingResult result = SolverKernel.Solve(input);
        SolverExactNumber scale = PowerOfTwo(Prepare(input).LineLoadExponent);
        foreach (BendingPiece piece in result.Pieces)
        {
            SolverExactNumber exact = Integer(0);
            foreach (TransverseLineLoad load in input.LineLoads.Where(l => l.Start <= piece.Start && l.End >= piece.End))
            {
                SolverExactNumber width = From(load.End) - From(load.Start), first = From(load.StartNewtonsPerMeter);
                SolverExactNumber rise = From(load.EndNewtonsPerMeter) - first;
                SolverExactNumber atStart = (first + rise * (From(piece.Start) - From(load.Start)) / width).Abs();
                SolverExactNumber atEnd = (first + rise * (From(piece.End) - From(load.Start)) / width).Abs();
                exact += SolverOutputOracle.Max(atStart, atEnd) * scale;
            }
            double majorant = BendingOutputQuality.QSum(result.Fields, piece.Start, piece.End);
            Assert.True(From(majorant).CompareTo(exact) >= 0,
                $"{name} [{piece.Start:R}, {piece.End:R}]: majorant {majorant:R} below the exact sum");
            // Tightness holds except where the clip data error itself dominates the intensity.
            Assert.True(name == "cancelling-clip" ||
                From(majorant).CompareTo(exact * (Integer(1) + PowerOfTwo(-40)) + PowerOfTwo(-1000)) <= 0,
                $"{name} [{piece.Start:R}, {piece.End:R}]: majorant {majorant:R} is loose");
        }
    }
}
