using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

internal static class SolverBendingTestSupport
{
    internal static MechanicalInput Model(double length = 2, double ei = 1, RigidRestraint[]? restraints = null,
        TransversePointForce[]? forces = null, AppliedPointMoment[]? moments = null, ConstantLineLoad[]? lines = null) =>
        new(length, ei, restraints ?? [new(0, RestrainedDirections.U | RestrainedDirections.W | RestrainedDirections.Theta)],
            forces ?? [], moments ?? [], lines ?? []);

    internal static BendingPreparation Prepare(MechanicalInput input) =>
        BendingPreparation.Create(input, StationModel.Create(input));

    internal static void Encloses(Bounded value, SolverExactNumber exact)
    {
        Assert.True(double.IsFinite(value.Error));
        Assert.True(double.IsFinite(value.Magnitude));
        AssertStored(value.Error);
        AssertStored(value.Magnitude);
        Assert.True((From(value.Value) - exact).Abs().CompareTo(From(value.Error)) <= 0,
            $"Value {value.Value:R}, bound {value.Error:R} do not enclose exact {exact}");
        Assert.True(From(value.Value).Abs().CompareTo(From(value.Magnitude)) <= 0);
    }

    internal static SolverExactNumber Pow(SolverExactNumber value, int power)
    {
        SolverExactNumber result = Integer(1);
        for (int i = 0; i < power; i++)
            result *= value;
        return result;
    }

    internal static int Factorial(int n) => n switch { 0 or 1 => 1, 2 => 2, 3 => 6, 4 => 24, _ => throw new ArgumentOutOfRangeException(nameof(n)) };
}

public sealed class SolverBendingNumericsTests
{
    public static TheoryData<double, double, int> Distances => new()
    {
        { 1, 0, 0 }, { Math.BitIncrement(1), 1, 1 }, { 1, Math.ScaleB(1, -54), 0 },
        { Math.BitIncrement(2), 1, 2 }, { Math.ScaleB(1, 900), 1, 900 },
        { Math.ScaleB(1, -900), Math.ScaleB(1, -950), -900 },
        { Math.BitIncrement(Binary64.Lambda), Binary64.Lambda, -10 },
        { double.Epsilon, 0, -968 }, { double.Epsilon * 2, double.Epsilon, 0 },
        { 1, 1, -968 }, { 0, -0d, 5 }
    };

    [Theory]
    [MemberData(nameof(Distances))]
    public void A1GlobalDistanceHasOneRoundingAndBoundEnclosesExactDifference(double x, double origin, int exponent)
    {
        SolverExactNumber exact = (From(x) - From(origin)) * PowerOfTwo(-exponent);
        Bounded distance = RoundCounted.BoundedDistance(x, origin, exponent);
        SolverBendingTestSupport.Encloses(distance, exact);
        RoundCounted coefficient = RoundCounted.Distance(x, origin, exponent);
        Assert.Equal(1, coefficient.Count);
        double raw = x - origin;
        Assert.Equal(x != origin && (Math.Abs(raw) <= Binary64.Lambda ||
            Math.Abs(Math.ScaleB(raw, -exponent)) <= Binary64.Lambda), coefficient.Underflow);
        if (!coefficient.Underflow && coefficient.Value != 0)
            Assert.True((From(coefficient.Value) - exact).Abs().CompareTo(From(coefficient.Bound())) <= 0);
        if (x == origin)
            Assert.Equal(default, distance);
    }

    [Fact]
    public void A1DistanceSubtractsGlobalsBeforeScalingAndUsesTheContractErrorExpression()
    {
        double origin = Math.ScaleB(1, -54);
        Bounded distance = RoundCounted.BoundedDistance(1, origin, 0);
        Assert.Equal(1d, distance.Value); // Ties to even, but the exact difference is smaller.
        Assert.Equal(0x3ca0000000000002UL, BitConverter.DoubleToUInt64Bits(distance.Error));
    }

    public static TheoryData<double, double, double, double> Coefficients => new()
    {
        { 0, 2, 1, 1 }, { 1, Math.BitIncrement(1), 210e9, 8e-6 },
        { Math.ScaleB(1, -54), 1, 1.1, 1.3 },
        { 0, Math.ScaleB(1, -960), Math.ScaleB(1, -500), Math.ScaleB(1, -400) },
        { 0, Math.ScaleB(1, 900), Math.ScaleB(1, 500), Math.ScaleB(1, 400) },
        { 0.25, Math.BitDecrement(2), Math.BitIncrement(1), Math.BitDecrement(2) }
    };

    [Theory]
    [MemberData(nameof(Coefficients))]
    public void A2CoefficientsIncludeExactEiProductAndDistanceRounding(double start, double end, double young, double inertia)
    {
        MechanicalInput input = SolverBendingTestSupport.Model(length: end, ei: young * inertia);
        BendingPreparation preparation = SolverBendingTestSupport.Prepare(input);
        TransferCoefficients co = TransferCoefficients.Create(start, end, preparation);
        SolverExactNumber a = (From(end) - From(start)) * PowerOfTwo(-preparation.EL);
        SolverExactNumber e = From(young) * From(inertia) * PowerOfTwo(-preparation.EEI);
        RoundCounted[] actual = [co.A, co.AE, co.A2, co.A3];
        SolverExactNumber[] exact = [a, a / e, a * a / Integer(2) / e, a * a * a / Integer(6) / e];
        Assert.Equal(new long[] { 1, 3, 5, 8 }, actual.Select(t => t.Count));
        Assert.Equal(1, preparation.NormalizedEI.Count);
        for (int i = 0; i < actual.Length; i++)
        {
            actual[i].RequireNormal();
            Assert.True((From(actual[i].Value) - exact[i]).Abs().CompareTo(From(actual[i].Bound())) <= 0);
        }
        double roundedA = Math.ScaleB(end - start, -preparation.EL);
        double roundedE = Math.ScaleB(input.EI, -preparation.EEI);
        double aa = roundedA * roundedA;
        Assert.Equal(new[] { roundedA, roundedA / roundedE, (aa / 2) / roundedE, ((aa * roundedA) / 6) / roundedE },
            actual.Select(t => t.Value));
    }

    [Fact]
    public void RoundCountedKeepsIntegerPowerOfTwoRulesAndUnderflowHistory()
    {
        var a = new RoundCounted(0.75, 2);
        var b = new RoundCounted(1.25, 3);
        Assert.Equal(6, (a * b).Count);
        Assert.Equal(6, (a / b).Count);
        Assert.Equal(2, (a * 2).Count);
        Assert.Equal(2, (a / 2).Count);
        Assert.Equal(3, (a * 6).Count);
        Assert.Equal(3, (a / 6).Count);
        Assert.Equal(new RoundCounted(-0.75, 2), -a);
        RoundCounted underflow = new RoundCounted(Binary64.Lambda) / 2;
        Assert.True(underflow.Underflow);
        Assert.True((underflow * 8).Underflow);
        Assert.Throws<NumericalRangeException>(() => (underflow * 8).RequireNormal());
        Assert.Throws<NumericalRangeException>(() => (new RoundCounted(Binary64.Lambda) * new RoundCounted(Binary64.Lambda)).RequireNormal());
        Assert.Throws<NumericalRangeException>(() => new RoundCounted(Binary64.Lambda).RequireNormal());
        Assert.Throws<NumericalRangeException>(() => RoundCounted.ComputedBound(1L << 52, 1));
        Assert.Equal(0, new RoundCounted(1).Bound());
    }

    [Fact]
    public void ComputedBoundEnclosesTheExactRelativeBoundOnTheStoredMagnitude()
    {
        foreach (long count in new[] { 0L, 1, 3, 5, 8, 1L << 40 })
            foreach (double value in new[] { Math.ScaleB(1, -800), -1d, Math.ScaleB(1, 800) })
            {
                double bound = RoundCounted.ComputedBound(count, value);
                SolverExactNumber exact = Integer(count) / (PowerOfTwo(53) - Integer(2 * count)) * From(value).Abs();
                Assert.True(From(bound).CompareTo(exact) >= 0);
                AssertStored(bound);
            }
    }

    [Fact]
    public void A3PointLoadTermsEncloseRationalPolynomialsWithBothSigns()
    {
        double[] distances = [0, Math.ScaleB(1, -330), Math.BitIncrement(1), 0.125, Math.BitDecrement(2)];
        double[] intensities = [1.25, -1.25, Math.BitDecrement(1), Math.ScaleB(1, -900), -0d];
        foreach (double d in distances)
            foreach (double v in intensities)
                for (int p = 1; p <= 4; p++)
                {
                    Bounded distance = RoundCounted.BoundedDistance(d, 0, 0);
                    SolverExactNumber exact = From(v) * SolverBendingTestSupport.Pow(From(d), p - 1) /
                        Integer(SolverBendingTestSupport.Factorial(p - 1));
                    SolverBendingTestSupport.Encloses(BendingLoadTerms.PointForce(new(v), distance, p), exact);
                    if (p >= 2)
                    {
                        exact = Integer(-1) * From(v) * SolverBendingTestSupport.Pow(From(d), p - 2) /
                            Integer(SolverBendingTestSupport.Factorial(p - 2));
                        SolverBendingTestSupport.Encloses(BendingLoadTerms.PointMoment(new(v), distance, p), exact);
                    }
                }
    }

    [Fact]
    public void A3IntensityIsMultipliedBeforeTheDistancePowers()
    {
        double v = Math.ScaleB(1, 500), d = Math.ScaleB(1, -400);
        Bounded force = BendingLoadTerms.PointForce(new(v), new(d), 4);
        Assert.Equal(((v * d) * d) * d / 6, force.Value);
        Assert.NotEqual(0, force.Value);
        Bounded moment = BendingLoadTerms.PointMoment(new(v), new(d), 4);
        Assert.Equal(((-v) * d) * d / 2, moment.Value);
    }

    [Fact]
    public void A4SegmentIntegralsEncloseIndependentMacaulayDifferences()
    {
        foreach (double width in new[] { 0.75, Math.ScaleB(1, -40), Math.ScaleB(1, -969) })
        {
            double start = width < Math.ScaleB(1, -52) ? 0 : 0.5;
            var input = SolverBendingTestSupport.Model(forces: [new(0.25, 3), new(0.75, -5)],
                moments: [new(0.5, 7), new(1.5, -2)], lines: [new(start, start + width, -1 / width)]);
            // The normalized length threshold depends on eL; use L=1 for the exact LEN_MIN case.
            if (width == Math.ScaleB(1, -969))
                input = SolverBendingTestSupport.Model(length: 1, lines: [new(0, width, 1 / width)]);
            StationModel stations = StationModel.Create(input);
            BendingPreparation preparation = BendingPreparation.Create(input, stations);
            SegmentBendingLoads segment = Assert.Single(PreparedBendingLoads.Create(input, stations, preparation).Segments);
            foreach (double x in new[] { 0, 0.25, 0.5, 0.75, 1d }.Append(input.Length))
                for (int p = 1; p <= 4; p++)
                {
                    SolverExactNumber exact = ExactIntegral(input, preparation, 0, input.Length, x, p);
                    SolverBendingTestSupport.Encloses(segment.Integral(x, p), exact);
                }
        }
    }

    internal static SolverExactNumber ExactIntegral(MechanicalInput input, BendingPreparation preparation,
        double segmentStart, double segmentEnd, double x, int p)
    {
        SolverExactNumber result = Integer(0);
        SolverExactNumber scale = PowerOfTwo(-preparation.EL);
        foreach (TransversePointForce force in input.PointForces)
            if (segmentStart < force.Position && force.Position < segmentEnd && x > force.Position)
                result += From(force.Newtons) * PowerOfTwo(preparation.ForceExponent) *
                    SolverBendingTestSupport.Pow((From(x) - From(force.Position)) * scale, p - 1) /
                    Integer(SolverBendingTestSupport.Factorial(p - 1));
        if (p >= 2)
            foreach (AppliedPointMoment moment in input.PointMoments)
                if (segmentStart < moment.Position && moment.Position < segmentEnd && x > moment.Position)
                    result -= From(moment.NewtonMeters) * PowerOfTwo(preparation.MomentExponent) *
                        SolverBendingTestSupport.Pow((From(x) - From(moment.Position)) * scale, p - 2) /
                        Integer(SolverBendingTestSupport.Factorial(p - 2));
        foreach (ConstantLineLoad line in input.LineLoads)
        {
            double a = Math.Max(segmentStart, line.Start), b = Math.Min(segmentEnd, line.End);
            if (b <= a || x <= a)
                continue;
            SolverExactNumber near = (From(x) - From(a)) * scale;
            SolverExactNumber far = x > b ? (From(x) - From(b)) * scale : Integer(0);
            result += From(line.NewtonsPerMeter) * PowerOfTwo(preparation.LineLoadExponent) *
                (SolverBendingTestSupport.Pow(near, p) - SolverBendingTestSupport.Pow(far, p)) /
                Integer(SolverBendingTestSupport.Factorial(p));
        }
        return result;
    }
}
