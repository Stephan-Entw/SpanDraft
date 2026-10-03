using MathNet.Numerics.LinearAlgebra;

namespace SpanDraft.Solver;

internal static class EulerBernoulliElement
{
    /// <summary>
    /// SI inputs E [Pa], A [m²], I [m⁴], L [m]. DOFs [u1, w1, θ1, u2, w2, θ2].
    /// w is upwards, θ is counterclockwise (θ = dw/dx). No axis transformation is needed.
    /// </summary>
    internal static Matrix<double> Stiffness(double youngsModulus, double area, double inertia, double length)
    {
        NumericalGuard.Positive(length, "Element length");
        double ea = NumericalGuard.Positive(youngsModulus * area, "Axial rigidity EA");
        double ei = NumericalGuard.Positive(youngsModulus * inertia, "Bending rigidity EI");
        double a = NumericalGuard.Positive(ea / length, "EA/L");
        double e = NumericalGuard.Positive(2 * (ei / length), "2EI/L");
        double d = NumericalGuard.Positive(2 * e, "4EI/L");
        double c = NumericalGuard.Positive(3 * (e / length), "6EI/L²");
        double b = NumericalGuard.Positive(2 * (c / length), "12EI/L³");
        return Matrix<double>.Build.DenseOfArray(new double[,]
        {
            { a, 0, 0, -a, 0, 0 },
            { 0, b, c, 0, -b, c },
            { 0, c, d, 0, -c, e },
            { -a, 0, 0, a, 0, 0 },
            { 0, -b, -c, 0, b, -c },
            { 0, c, e, 0, -c, d }
        });
    }

    internal static Vector<double> UniformLoad(double intensity, double length)
    {
        NumericalGuard.Positive(length, "Element length");
        // Integrating the cubic Hermite functions N1..N4 over [0,L] gives
        // [L/2, L²/12, L/2, -L²/12]; axial functions do not receive transverse loads.
        double force = NumericalGuard.Finite(intensity * (length / 2), "Equivalent nodal force");
        double moment = NumericalGuard.Finite(force * (length / 6), "Equivalent nodal moment");
        return Vector<double>.Build.Dense([0, force, moment, 0, force, -moment]);
    }
}
