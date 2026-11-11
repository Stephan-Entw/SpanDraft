using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;

namespace SpanDraft.Tests;

public sealed class SolverBendingVerificationTests
{
    [Fact]
    public void StagesCAndDProveAdjointResidualInverseRelationAndActualError()
    {
        double[] matrix = [1d / 3, 2, -1, 3, 5, 2, -2, 1, 4];
        long[] counts = [1, 0, 0, 0, 0, 0, 0, 0, 0];
        EquilibratedSystem scaled = EquilibratedSystem.Create(3, matrix, counts, [1, -3, 7], [0, Math.ScaleB(1, -50), 0]);
        var lu = new DenseLu(3, scaled.S);
        var (y, iterations) = BendingStateSolver.Primary(scaled, lu);
        Assert.InRange(iterations, 1, 5);
        var (_, _, d) = BendingStateSolver.ResidualAndD(scaled, y);
        double[][] z = new double[3][], sb = new double[3][];
        double[] raw = new double[3];
        for (int i = 0; i < 3; i++)
        {
            BendingVerification.AdjointState adjoint = BendingVerification.Adjoint(scaled, lu, d, i);
            Assert.InRange(adjoint.Iterations, 1, 5);
            Assert.All(adjoint.Z, value => Assert.True(value == 0 || double.IsNormal(value)));
            z[i] = adjoint.Z;
            sb[i] = adjoint.ResidualBound;
            raw[i] = BendingVerification.UpperDot(z[i], d);
        }
        BendingVerification.Contraction proof = BendingVerification.VerifyContraction(scaled, z, sb, raw, [1, 1, 1]);
        BendingVerification.Result complete = BendingVerification.Verify(scaled, lu, d, [1, 1, 1]);
        Assert.Equal(raw, complete.RawBound);
        var exactS = Matrix(matrix);
        exactS[0, 0] = Integer(1) / Integer(3);
        SolverExactNumber[] exactB = [Integer(1), Integer(-3) + PowerOfTwo(-51), Integer(7)];
        for (int i = 0; i < 3; i++)
        {
            exactB[i] = Reduce(exactB[i] * PowerOfTwo(scaled.RS[i]));
            for (int j = 0; j < 3; j++)
                exactS[i, j] = Reduce(exactS[i, j] * PowerOfTwo(scaled.RS[i] + scaled.CS[j]));
        }
        SolverExactNumber[] exactY = Solve(exactS, exactB);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(proof.V[i] > 0 && double.IsNormal(proof.V[i]));
            Assert.True(proof.V[i] >= raw[i]);
            SolverExactNumber mv = Integer(0);
            for (int j = 0; j < 3; j++)
            {
                SolverExactNumber storedAdjointResidual = Integer(i == j ? 1 : 0) -
                    Dot(Enumerable.Range(0, 3).Select(k => (From(z[i][k]), From(scaled.S[k * 3 + j]))));
                Bounded(storedAdjointResidual, sb[i][j]);
                SolverExactNumber m = Reduce(From(sb[i][j]) + Dot(Enumerable.Range(0, 3)
                    .Select(k => (From(z[i][k]).Abs(), From(scaled.DS[k * 3 + j])))));
                SolverExactNumber inverseDefect = Integer(i == j ? 1 : 0) -
                    Dot(Enumerable.Range(0, 3).Select(k => (From(z[i][k]), exactS[k, j])));
                Assert.True(inverseDefect.Abs().CompareTo(m) <= 0);
                mv = Reduce(mv + m * From(proof.V[j]));
            }
            Bounded(mv, proof.MV[i]);
            Assert.True(From(proof.MV[i]).CompareTo(From(proof.Mu) * From(proof.V[i])) <= 0);
            Bounded(From(y[i]) - exactY[i], proof.Bound[i]);
            Assert.True(proof.Bound[i] >= raw[i]);
        }
        Assert.True(proof.Mu < 1);
        Assert.Equal(4, proof.Mus.Length);
    }

    [Fact]
    public void FixedMatrixRhsAndBoundsProduceNotVerifiedWithoutDisturbingLu()
    {
        var scaled = new EquilibratedSystem(1, [0], [0], [1], [2], [1], [0]);
        var lu = new DenseLu(1, scaled.S);
        var (y, _) = BendingStateSolver.Primary(scaled, lu);
        Assert.Equal(new[] { 1d }, y);
        var (_, _, d) = BendingStateSolver.ResidualAndD(scaled, y);
        KernelFailureException error = Assert.Throws<KernelFailureException>(() =>
            BendingVerification.Verify(scaled, lu, d, [1]));
        Assert.Equal(KernelFailureCause.NotVerified, error.Cause);
        Assert.Equal(4, error.TestedMus.Length);
        Assert.All(error.TestedMus, mu => Assert.True(mu >= 2));
    }

    [Fact]
    public void InvalidWeightStopsSequenceEvenWhenItsSuccessorWouldBeValid()
    {
        var scaled = new EquilibratedSystem(2, [0, 0], [0, 0], [1, 0, 0, 1], [0, 0, 0, 0], [0, 0], [0, 0]);
        double[][] z = [[0, 0], [0, 0]], sb = [[0, 0.125], [0, 0]];
        KernelFailureException error = Assert.Throws<KernelFailureException>(() =>
            BendingVerification.VerifyContraction(scaled, z, sb, [0, 1], [0, 0]));
        Assert.Equal(KernelFailureCause.NotVerified, error.Cause);
        Assert.Empty(error.TestedMus);
        // If the invalid initial vector were skipped, Mv would repair its zero component.
        BendingVerification.Contraction repaired = BendingVerification.VerifyContraction(scaled, z, sb, [0.125, 1], [0, 0]);
        Assert.True(repaired.Mu < 1);
    }

    [Fact]
    public void InvalidMvStopsWithoutRecordingTheCandidate()
    {
        var scaled = Scalar();
        KernelFailureException first = Assert.Throws<KernelFailureException>(() =>
            BendingVerification.VerifyContraction(scaled, [[0]], [[8]], [Math.ScaleB(1, 1022)], [0]));
        Assert.Empty(first.TestedMus);
        KernelFailureException later = Assert.Throws<KernelFailureException>(() =>
            BendingVerification.VerifyContraction(scaled, [[0]], [[1.5]], [Math.ScaleB(1, 1022)], [0]));
        Assert.Equal(KernelFailureCause.NotVerified, later.Cause);
        Assert.Equal(2, later.TestedMus.Length);
        Assert.All(later.TestedMus, mu => Assert.True(double.IsFinite(mu) && mu >= 1.5));
    }

    [Fact]
    public void LastAdmissibleCandidateIsSelectedEvenWhenItsMuIsLarger()
    {
        var scaled = new EquilibratedSystem(3, [0, 0, 0], [0, 0, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1],
            new double[9], new double[3], new double[3]);
        double[][] z = [new double[3], new double[3], new double[3]];
        double[][] sb = [[0, 1d / 512, 1.125], [1d / 64, 0, 0], [0.25, 0, 0]];
        double[] raw = [1d / 8192, 1d / 128, 1];
        double[] weights = new[] { 0.625, 1d / 1024, 1d / 256 }.Select(v => Math.ScaleB(v, 200)).ToArray();
        BendingVerification.Contraction proof = BendingVerification.VerifyContraction(scaled, z, sb, raw, weights);
        Assert.Equal(4, proof.Mus.Length);
        Assert.True(proof.Mus[0] > 1 && proof.Mus[1] > 1);
        Assert.True(proof.Mus[2] < proof.Mus[3] && proof.Mus[3] < 1);
        Assert.Equal(proof.Mus[3], proof.Mu);
        Assert.NotEqual(proof.Mus.Min(), proof.Mu);
    }

    [Fact]
    public void RawBoundCanUnderestimateActualErrorButVerifiedBoundEnclosesIt()
    {
        // Exact scalar system y*=1, computed y=0, stored approximate adjoint z=1/2.
        // Its exact residual is 1/2, so M=1/2 and RawBound=1/2 < |y-y*|=1.
        double raw = BendingVerification.UpperDot([0.5], [1]);
        BendingVerification.Contraction proof = BendingVerification.VerifyContraction(Scalar(), [[0.5]], [[0.5]], [raw], [1]);
        Assert.True(raw < 1);
        Assert.True(proof.Bound[0] >= 1);
        Assert.True(proof.Bound[0] > raw);
    }

    [Fact]
    public void MuRoundsUpAndFinalBoundUsesDownwardDenominatorAndUpwardQuotient()
    {
        double raw = 0.125;
        BendingVerification.Contraction proof = BendingVerification.VerifyContraction(Scalar(), [[0]], [[0.25]], [raw], [1]);
        Assert.True(From(proof.Mu).CompareTo(From(proof.MV[0]) / From(proof.V[0])) > 0);
        Assert.Equal(Math.BitIncrement(proof.MV[0] / proof.V[0]), proof.Mu);
        double denominator = Math.BitDecrement(1 - proof.Mu);
        double quotient = Math.BitIncrement(proof.MV[0] / denominator);
        double expected = Math.BitIncrement(raw + quotient);
        Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(proof.Bound[0]));
        double missingDown = Math.BitIncrement(raw + Math.BitIncrement(proof.MV[0] / (1 - proof.Mu)));
        Assert.NotEqual(missingDown, proof.Bound[0]);
        SolverExactNumber required = From(raw) + From(proof.MV[0]) / (Integer(1) - From(proof.Mu));
        Assert.True(From(proof.Bound[0]).CompareTo(required) >= 0);
    }

    [Fact]
    public void OverflowOfFinalVerifiedBoundIsNumericalRange()
    {
        Assert.Throws<NumericalRangeException>(() => BendingVerification.VerifyContraction(Scalar(),
            [[0]], [[0.999]], [Math.ScaleB(1, 1020)], [0]));
    }

    [Fact]
    public void ExactAdjointStillReceivesOneMandatoryRefinement()
    {
        var scaled = Scalar();
        BendingVerification.AdjointState adjoint = BendingVerification.Adjoint(scaled, new DenseLu(1, [1]), [1], 0);
        Assert.Equal(1, adjoint.Iterations);
        Assert.Equal(new[] { 1d }, adjoint.Z);
    }

    [Fact]
    public void AdjointStorageFlushesSubnormalsButPreservesLambda()
    {
        foreach (int exponent in new[] { 1022, 1023 })
        {
            double a = Math.ScaleB(1, exponent);
            var scaled = new EquilibratedSystem(1, [0], [0], [a], [0], [1], [0]);
            BendingVerification.AdjointState adjoint = BendingVerification.Adjoint(scaled, new DenseLu(1, [a]), [1], 0);
            Assert.Equal(exponent == 1022 ? Binary64.Lambda : 0, adjoint.Z[0]);
            Assert.Equal(1, adjoint.Iterations);
            Assert.False(double.IsSubnormal(adjoint.Z[0]));
        }
    }

    private static EquilibratedSystem Scalar() => new(1, [0], [0], [1], [0], [1], [0]);
}
