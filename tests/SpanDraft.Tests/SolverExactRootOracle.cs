using SpanDraft.Solver.Kernel;
using System.Numerics;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

/// <summary>
/// Exact rational test oracle for the candidate positions of the extremum search: the polynomial of a field
/// piece is rebuilt by exact interpolation of <see cref="SolverOutputOracle"/> and its real roots in (0, 1) are
/// isolated with a Sturm sequence over the squarefree part. It shares no code with the product root finder.
/// </summary>
internal static class SolverExactRootOracle
{
    /// <summary>The exact polynomial in u = (x - a) / (b - a) of one field component on the piece [a, b].</summary>
    internal static Polynomial FieldPolynomial(SolverOutputOracle oracle, double a, double b, int component)
    {
        const int points = 6;
        var us = new Rational[points];
        var values = new Rational[points];
        for (int k = 0; k < points; k++)
        {
            double x = k == 0 ? a : k == points - 1 ? b : a + (b - a) * k / (points - 1);
            FieldSide side = k == 0 ? FieldSide.Right : k == points - 1 ? FieldSide.Left : FieldSide.Interior;
            us[k] = (Rational.From(From(x)) - Rational.From(From(a))) / (Rational.From(From(b)) - Rational.From(From(a)));
            values[k] = Rational.From(oracle.Field(x, side)[component]);
        }
        return Polynomial.Interpolate(us, values);
    }

    /// <summary>Distinct real roots strictly inside (0, 1), each in an interval no wider than 2^-<paramref name="bits"/>.</summary>
    internal static IReadOnlyList<(Rational Low, Rational High)> InteriorRoots(Polynomial polynomial, int bits = 48)
    {
        Polynomial p = polynomial.Trim();
        var roots = new List<(Rational, Rational)>();
        if (p.Degree <= 0)
            return roots;
        p = p.Squarefree();
        while (p.Degree > 0 && p.Evaluate(Rational.Zero).IsZero)
            p = p.DivideExactly(new Polynomial([Rational.Zero, Rational.One]));
        while (p.Degree > 0 && p.Evaluate(Rational.One).IsZero)
            p = p.DivideExactly(new Polynomial([-Rational.One, Rational.One]));
        if (p.Degree <= 0)
            return roots;
        var chain = SturmChain(p);
        Isolate(chain, Rational.Zero, Rational.One, bits, roots);
        return roots;
    }

    private static void Isolate(List<Polynomial> chain, Rational low, Rational high, int bits,
        List<(Rational, Rational)> roots)
    {
        int count = Variations(chain, low) - Variations(chain, high);
        if (count == 0)
            return;
        if (count == 1 && (high - low).CompareTo(Rational.PowerOfTwo(-bits)) <= 0)
        {
            roots.Add((low, high));
            return;
        }
        Rational middle = (low + high) / new Rational(2);
        if (chain[0].Evaluate(middle).IsZero)
        {
            // The root sits exactly on the bisection point; bracket it by a tiny interval around it.
            Rational epsilon = Rational.PowerOfTwo(-bits - 2);
            Isolate(chain, low, middle - epsilon, bits, roots);
            roots.Add((middle - epsilon, middle + epsilon));
            Isolate(chain, middle + epsilon, high, bits, roots);
            return;
        }
        Isolate(chain, low, middle, bits, roots);
        Isolate(chain, middle, high, bits, roots);
    }

    private static List<Polynomial> SturmChain(Polynomial p)
    {
        var chain = new List<Polynomial> { p, p.Derivative() };
        while (chain[^1].Degree > 0)
        {
            Polynomial remainder = -Polynomial.Remainder(chain[^2], chain[^1]);
            if (remainder.Trim().Degree < 0)
                break;
            chain.Add(remainder.Trim());
        }
        return chain;
    }

    private static int Variations(List<Polynomial> chain, Rational x)
    {
        int variations = 0, previous = 0;
        foreach (Polynomial polynomial in chain)
        {
            int sign = polynomial.Evaluate(x).Sign;
            if (sign == 0)
                continue;
            if (previous != 0 && sign != previous)
                variations++;
            previous = sign;
        }
        return variations;
    }

    internal readonly struct Rational : IComparable<Rational>
    {
        internal static readonly Rational Zero = new(0), One = new(1);

        internal Rational(BigInteger numerator, BigInteger? denominator = null)
        {
            BigInteger d = denominator ?? BigInteger.One;
            if (d.Sign < 0)
            {
                numerator = -numerator;
                d = -d;
            }
            BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, d);
            if (!gcd.IsOne && !gcd.IsZero)
            {
                numerator /= gcd;
                d /= gcd;
            }
            Numerator = numerator;
            Denominator = d;
        }

        internal BigInteger Numerator { get; }
        internal BigInteger Denominator { get; }
        internal bool IsZero => Numerator.IsZero;
        internal int Sign => Numerator.Sign;

        internal static Rational From(SolverExactNumber value) => new(value.Numerator, value.Denominator);
        internal static Rational PowerOfTwo(int exponent) => exponent >= 0
            ? new(BigInteger.One << exponent) : new(BigInteger.One, BigInteger.One << -exponent);
        internal double ToDouble() => (double)Numerator / (double)Denominator;

        public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public static Rational operator +(Rational a, Rational b) =>
            new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);
        public static Rational operator -(Rational a, Rational b) => a + -b;
        public static Rational operator *(Rational a, Rational b) =>
            new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public static Rational operator /(Rational a, Rational b) =>
            new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    internal sealed class Polynomial(Rational[] coefficients)
    {
        // Ascending powers.
        internal Rational[] Coefficients { get; } = coefficients;
        internal int Degree => Trim().Coefficients.Length - 1;

        internal static Polynomial Interpolate(Rational[] xs, Rational[] ys)
        {
            int n = xs.Length;
            var matrix = new Rational[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                Rational power = Rational.One;
                for (int j = 0; j < n; j++)
                {
                    matrix[i, j] = power;
                    power *= xs[i];
                }
                matrix[i, n] = ys[i];
            }
            for (int column = 0; column < n; column++)
            {
                int pivot = Enumerable.Range(column, n - column).First(r => !matrix[r, column].IsZero);
                for (int j = 0; j <= n; j++)
                    (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);
                for (int r = 0; r < n; r++)
                {
                    if (r == column || matrix[r, column].IsZero) continue;
                    Rational factor = matrix[r, column] / matrix[column, column];
                    for (int j = column; j <= n; j++)
                        matrix[r, j] -= factor * matrix[column, j];
                }
            }
            return new Polynomial(Enumerable.Range(0, n).Select(i => matrix[i, n] / matrix[i, i]).ToArray()).Trim();
        }

        internal Polynomial Trim()
        {
            int length = Coefficients.Length;
            while (length > 0 && Coefficients[length - 1].IsZero) length--;
            return length == Coefficients.Length ? this : new Polynomial(Coefficients[..length]);
        }

        internal Rational Evaluate(Rational x)
        {
            Rational value = Rational.Zero;
            for (int i = Coefficients.Length - 1; i >= 0; i--)
                value = value * x + Coefficients[i];
            return value;
        }

        internal Polynomial Derivative() => new Polynomial(Coefficients.Skip(1)
            .Select((c, i) => c * new Rational(i + 1)).ToArray()).Trim();

        public static Polynomial operator -(Polynomial p) => new(p.Coefficients.Select(c => -c).ToArray());

        internal static Polynomial Remainder(Polynomial a, Polynomial b)
        {
            Rational[] remainder = a.Trim().Coefficients.ToArray();
            Polynomial divisor = b.Trim();
            int dd = divisor.Coefficients.Length - 1;
            for (int degree = remainder.Length - 1; degree >= dd; degree--)
            {
                if (remainder[degree].IsZero) continue;
                Rational factor = remainder[degree] / divisor.Coefficients[dd];
                for (int i = 0; i <= dd; i++)
                    remainder[degree - dd + i] -= factor * divisor.Coefficients[i];
            }
            return new Polynomial(remainder[..Math.Min(remainder.Length, dd)]).Trim();
        }

        internal Polynomial DivideExactly(Polynomial divisor)
        {
            Rational[] rest = Trim().Coefficients.ToArray();
            Polynomial d = divisor.Trim();
            int dd = d.Coefficients.Length - 1;
            var quotient = new Rational[Math.Max(rest.Length - dd, 1)];
            for (int degree = rest.Length - 1; degree >= dd; degree--)
            {
                Rational factor = rest[degree] / d.Coefficients[dd];
                quotient[degree - dd] = factor;
                for (int i = 0; i <= dd; i++)
                    rest[degree - dd + i] -= factor * d.Coefficients[i];
            }
            return new Polynomial(quotient).Trim();
        }

        internal Polynomial Squarefree()
        {
            Polynomial a = Trim(), b = a.Derivative();
            while (b.Degree >= 0)
            {
                Polynomial next = Remainder(a, b);
                (a, b) = (b, next);
            }
            // a is a constant multiple of gcd(p, p').
            return a.Degree <= 0 ? Trim() : DivideExactly(a);
        }
    }
}
