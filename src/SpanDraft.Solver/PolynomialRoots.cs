namespace SpanDraft.Solver;

/// <summary>Deterministic real roots of degree ≤ 3 on [0,1], not a sampling search.</summary>
internal static class PolynomialRoots
{
    // double.Epsilon is the smallest subnormal, not floating-point relative precision.
    internal const double MachinePrecision = 2.2204460492503131e-16;
    private const double BoundaryTolerance = 32 * MachinePrecision;
    private const double RoundoffFactor = 64 * MachinePrecision;

    internal static IReadOnlyList<double> InUnitInterval(params double[] coefficients)
    {
        if (coefficients.Length is < 1 or > 4)
            throw new ArgumentException("Expected a polynomial of degree at most three.", nameof(coefficients));
        NumericalGuard.AllFinite(coefficients, "Root polynomial");
        double scale = coefficients.Max(Math.Abs);
        if (scale == 0) return Array.Empty<double>(); // A constant field needs endpoints only.
        double[] c = coefficients.Select(value => value / scale).ToArray();
        int degree = c.Length - 1;
        while (degree > 0 && c[degree] == 0) degree--;
        var roots = new List<double>();
        if (degree == 1)
            Add(roots, -c[0] / c[1]);
        else if (degree == 2)
            Quadratic(c[0], c[1], c[2], roots);
        else if (degree == 3)
        {
            // Derivative roots partition a cubic into monotone intervals. Checking
            // those roots also finds even-multiplicity roots without a sign change.
            var boundaries = new List<double> { 0 };
            foreach (double t in InUnitInterval(c[1], 2 * c[2], 3 * c[3]))
                if (t > 0 && t < 1) boundaries.Add(t);
            boundaries.Add(1);
            // If both stationary values are zero to coefficient roundoff,
            // the three roots form an unresolved multiple-root cluster.
            // Represent it once at the inflection instead of reporting two
            // almost identical stationary candidates as separate roots.
            if (boundaries.Count == 4 && NearZero(c, boundaries[1]) && NearZero(c, boundaries[2]))
            {
                Add(roots, -c[2] / (3 * c[3]));
                return roots.AsReadOnly();
            }
            foreach (double t in boundaries)
                if (NearZero(c, t)) Add(roots, t);
            for (int i = 1; i < boundaries.Count; i++)
            {
                double lo = boundaries[i - 1], hi = boundaries[i];
                double flo = Evaluate(c, lo), fhi = Evaluate(c, hi);
                // An accepted boundary root already represents this monotone
                // interval's endpoint. Do not generate a second root from its
                // rounding residual, especially around multiple roots.
                if (NearZero(c, lo) || NearZero(c, hi) || Math.Sign(flo) == Math.Sign(fhi)) continue;
                // Interval width, rather than a small function value, controls
                // convergence even at flat/triple roots.
                for (int iteration = 0; iteration < 128 && hi - lo > BoundaryTolerance; iteration++)
                {
                    double mid = lo + (hi - lo) / 2;
                    double fm = Evaluate(c, mid);
                    if (fm == 0) { lo = hi = mid; break; }
                    if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; }
                    else hi = mid;
                }
                Add(roots, lo + (hi - lo) / 2);
            }
        }
        roots.Sort();
        var distinct = new List<double>();
        foreach (double root in roots)
            if (distinct.Count == 0 || root - distinct[^1] > BoundaryTolerance)
                distinct.Add(root);
        return distinct.AsReadOnly();
    }

    private static void Quadratic(double c, double b, double a, List<double> roots)
    {
        double bb = b * b, ac4 = 4 * a * c;
        double discriminant = bb - ac4;
        double uncertainty = RoundoffFactor * (Math.Abs(bb) + Math.Abs(ac4));
        if (discriminant < -uncertainty) return;
        if (Math.Abs(discriminant) <= uncertainty)
        {
            Add(roots, -b / (2 * a));
            return;
        }
        // This form avoids subtracting nearly equal numbers for one root.
        double numerator = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
        Add(roots, numerator / a);
        Add(roots, c / numerator);
    }

    private static double Evaluate(double[] c, double t)
    {
        double value = 0;
        for (int i = c.Length - 1; i >= 0; i--) value = value * t + c[i];
        return value;
    }

    private static bool NearZero(double[] c, double t)
    {
        double bound = 0;
        for (int i = c.Length - 1; i >= 0; i--) bound = bound * Math.Abs(t) + Math.Abs(c[i]);
        return Math.Abs(Evaluate(c, t)) <= RoundoffFactor * bound;
    }

    private static void Add(List<double> roots, double root)
    {
        if (!double.IsFinite(root) || root < -BoundaryTolerance || root > 1 + BoundaryTolerance) return;
        if (root <= BoundaryTolerance) root = 0;
        else if (root >= 1 - BoundaryTolerance) root = 1;
        roots.Add(root);
    }
}
