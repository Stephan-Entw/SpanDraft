using Avalonia;
using System.Globalization;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Solver;

namespace SpanDraft.Desktop.Controls;

public enum ResultDiagramKind { TransverseDisplacement, ShearForce, BendingMoment }
public sealed record ResultDiagramPoint(Length Position, EvaluationSide Side, double Value, Point Screen, double SiValue);
public sealed record ResultDiagramJump(ResultDiagramPoint Left, ResultDiagramPoint Right);
public sealed record ResultDiagramTick(int Index, double ScreenY);
public enum ResultDiagramExtremumKind { Minimum, Maximum, MinimumAndMaximum }
public sealed record ResultDiagramMarker(ResultDiagramExtremumKind Kind, Length Position,
    EvaluationSide? Side, double Value, Point Screen, double SiValue);

/// <summary>Signed display-unit scale; normalization avoids overflowing a mixed-sign range.</summary>
public sealed class ResultDiagramScale
{
    private static readonly double[] PreferredMantissas = [1, 2, 2.5, 5];
    private readonly double _magnitude;
    private readonly double _normalizedStep;
    private readonly double _top;
    private readonly double _height;

    public ResultDiagramScale(double minimum, double maximum, double top, double height)
    {
        if (!double.IsFinite(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (!double.IsFinite(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (!double.IsFinite(top)) throw new ArgumentOutOfRangeException(nameof(top));
        if (!double.IsFinite(height) || height <= 0 || !double.IsFinite(top + height))
            throw new ArgumentOutOfRangeException(nameof(height));
        Minimum = minimum;
        Maximum = maximum;
        _magnitude = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        _top = top;
        _height = height;
        if (IsZero) return;

        // Decompose before rescaling: 10^-324 is not representable as a double,
        // and subtracting large mixed-sign extrema could overflow.
        string scientific = _magnitude.ToString("E16", CultureInfo.InvariantCulture);
        int separator = scientific.IndexOf('E');
        double magnitudeMantissa = double.Parse(scientific.AsSpan(0, separator), CultureInfo.InvariantCulture);
        int magnitudeExponent = int.Parse(scientific.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        double lower = Math.Min(0, minimum / _magnitude), upper = Math.Max(0, maximum / _magnitude);
        int targetTicks = (int)Math.Clamp(Math.Floor(height / 32) + 1, 2, 6);
        double desiredMantissa = magnitudeMantissa * ((upper - lower) / (targetTicks - 1));
        int exponent = magnitudeExponent;
        while (desiredMantissa < 1) { desiredMantissa *= 10; exponent--; }
        while (desiredMantissa >= 10) { desiredMantissa /= 10; exponent++; }
        int preferred = Array.FindIndex(PreferredMantissas, m => m >= desiredMantissa);
        if (preferred < 0) { preferred = 0; exponent++; }
        int minimumTicks = minimum < 0 && maximum > 0 ? 3 : 2;

        // A bounded candidate search, not an open-ended floating-point tick loop.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            double mantissa = PreferredMantissas[preferred];
            double step = mantissa / magnitudeMantissa * Math.Pow(10, exponent - magnitudeExponent);
            int lowerIndex = (int)Math.Floor(lower / step), upperIndex = (int)Math.Ceiling(upper / step);
            // Preserve an arbitrarily small opposite-sign bound even if its
            // ratio to the larger extremum underflows during normalization.
            if (minimum < 0 && lowerIndex == 0) lowerIndex = -1;
            if (maximum > 0 && upperIndex == 0) upperIndex = 1;
            // Multiplication and division may round in opposite directions at
            // an exact step boundary. Check containment after rounding outward.
            if (lowerIndex * step > lower) lowerIndex--;
            if (upperIndex * step < upper) upperIndex++;
            int count = upperIndex - lowerIndex + 1;
            if (count <= 6 && (height / (count - 1) >= 28 || count <= minimumTicks))
            {
                StepMantissa = mantissa;
                StepExponent = exponent;
                LowerTickIndex = lowerIndex;
                UpperTickIndex = upperIndex;
                _normalizedStep = step;
                return;
            }
            if (++preferred == PreferredMantissas.Length) { preferred = 0; exponent++; }
        }
        throw new InvalidOperationException("Cannot construct a bounded diagram scale.");
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double StepMantissa { get; }
    public int StepExponent { get; }
    public int LowerTickIndex { get; }
    public int UpperTickIndex { get; }
    public bool IsZero => _magnitude == 0;
    public double ZeroY => TickY(0);
    public double ToScreen(double value)
    {
        if (IsZero || value == 0) return ZeroY;
        double lower = LowerTickIndex * _normalizedStep, upper = UpperTickIndex * _normalizedStep;
        return _top + (upper - value / _magnitude) / (upper - lower) * _height;
    }

    private double TickY(int index) => IsZero ? _top + _height / 2 :
        _top + (UpperTickIndex - index) / (double)(UpperTickIndex - LowerTickIndex) * _height;

    public IReadOnlyList<ResultDiagramTick> Ticks()
    {
        return Enumerable.Range(LowerTickIndex, UpperTickIndex - LowerTickIndex + 1).Reverse()
            .Select(index => new ResultDiagramTick(index, TickY(index))).ToArray();
    }
}

/// <summary>Pure plotting projection of an existing solution and the editor's immutable mapping.</summary>
public sealed record ResultDiagramProjection(StationLayoutResult StationLayout, ResultDiagramScale Scale,
    IReadOnlyList<IReadOnlyList<ResultDiagramPoint>> Sections, IReadOnlyList<ResultDiagramJump> Jumps,
    IReadOnlyList<ResultDiagramTick> Ticks, IReadOnlyList<ResultDiagramMarker> Markers, UnitDefinition Unit)
{
    public const double MaximumSampleSpacing = 8;
    public const double MaximumChordError = 0.5;
    private const int MaximumDepth = 16;

    public static QuantityKind QuantityOf(ResultDiagramKind kind) => kind switch
    {
        ResultDiagramKind.TransverseDisplacement => QuantityKind.TransverseDisplacement,
        ResultDiagramKind.ShearForce => QuantityKind.TransverseForce,
        ResultDiagramKind.BendingMoment => QuantityKind.Moment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static ResultDiagramProjection Create(BeamSolution solution, ResultDiagramKind kind,
        StationLayoutResult layout, double plotTop, double plotHeight, UnitProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(layout);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(plotTop) || !double.IsFinite(plotHeight) || plotHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(plotHeight));

        var extrema = solution.Extrema;
        var unit = (profile ?? UnitProfile.Default)[QuantityOf(kind)];
        var (minimumSi, maximumSi, minimumPosition, maximumPosition, minimumSide, maximumSide) = kind switch
        {
            ResultDiagramKind.TransverseDisplacement =>
                (extrema.MinimumTransverseDisplacement.Value.Meters,
                 extrema.MaximumTransverseDisplacement.Value.Meters,
                 extrema.MinimumTransverseDisplacement.Position, extrema.MaximumTransverseDisplacement.Position,
                 extrema.MinimumTransverseDisplacement.Side, extrema.MaximumTransverseDisplacement.Side),
            ResultDiagramKind.ShearForce =>
                (extrema.MinimumShearForce.Value.Newtons, extrema.MaximumShearForce.Value.Newtons,
                 extrema.MinimumShearForce.Position, extrema.MaximumShearForce.Position,
                 extrema.MinimumShearForce.Side, extrema.MaximumShearForce.Side),
            _ => (extrema.MinimumBendingMoment.Value.NewtonMeters, extrema.MaximumBendingMoment.Value.NewtonMeters,
                  extrema.MinimumBendingMoment.Position, extrema.MaximumBendingMoment.Position,
                  extrema.MinimumBendingMoment.Side, extrema.MaximumBendingMoment.Side)
        };
        var scale = new ResultDiagramScale(unit.FromSi(minimumSi), unit.FromSi(maximumSi), plotTop, plotHeight);
        // Keep the solver's selected representative and side, including zeros and ties.
        // The analytic value places the dot on the corresponding one-sided limit.
        bool isConstant = minimumSi == maximumSi;
        ResultDiagramMarker[] markers = isConstant
            ? [Marker(ResultDiagramExtremumKind.MinimumAndMaximum, minimumPosition, minimumSide, minimumSi)]
            : [Marker(ResultDiagramExtremumKind.Minimum, minimumPosition, minimumSide, minimumSi),
               Marker(ResultDiagramExtremumKind.Maximum, maximumPosition, maximumSide, maximumSi)];
        var sections = new List<IReadOnlyList<ResultDiagramPoint>>();
        var jumps = new List<ResultDiagramJump>();
        // Exact positions only: close but distinct physical stations must remain distinct.
        var breaks = layout.Stations.Select(s => s.PhysicalX)
            .Concat([minimumPosition.Meters, maximumPosition.Meters]).Distinct().Order().ToArray();
        for (int i = 0; i < solution.Nodes.Count - 1; i++)
        {
            double start = solution.Nodes[i].Position.Meters, end = solution.Nodes[i + 1].Position.Meters;
            var positions = new[] { start }.Concat(breaks.Where(x => x > start && x < end)).Append(end).ToArray();
            for (int j = 0; j < positions.Length - 1; j++)
            {
                var left = Evaluate(positions[j], EvaluationSide.Right);
                var right = Evaluate(positions[j + 1], EvaluationSide.Left);
                var points = new List<ResultDiagramPoint> { left };
                Subdivide(left, right, points, 0);
                sections.Add(points.AsReadOnly());
            }
            if (i + 1 < solution.Nodes.Count - 1 && CanJump(i + 1))
            {
                var left = Evaluate(end, EvaluationSide.Left);
                var right = Evaluate(end, EvaluationSide.Right);
                if (left.SiValue != right.SiValue) jumps.Add(new(left, right));
            }
        }
        return new(layout, scale, sections.AsReadOnly(), jumps.AsReadOnly(), scale.Ticks(), Array.AsReadOnly(markers), unit);

        ResultDiagramMarker Marker(ResultDiagramExtremumKind markerKind, Length position, EvaluationSide? side, double siValue)
        {
            double value = unit.FromSi(siValue);
            return new(markerKind, position, side, value,
                new(layout.Transform.PhysicalToScreen(position.Meters), scale.ToScreen(value)), siValue);
        }

        ResultDiagramPoint Evaluate(double x, EvaluationSide side)
        {
            var position = Length.FromMeters(x);
            var result = solution.EvaluateAt(position, side);
            double siValue = kind switch
            {
                ResultDiagramKind.TransverseDisplacement => result.TransverseDisplacement.Meters,
                ResultDiagramKind.ShearForce => result.ShearForce.Newtons,
                _ => result.BendingMoment.NewtonMeters
            };
            double value = unit.FromSi(siValue);
            return new(position, side, value, new(layout.Transform.PhysicalToScreen(x), scale.ToScreen(value)), siValue);
        }

        bool CanJump(int nodeIndex)
        {
            var node = solution.Nodes[nodeIndex];
            // Only classify discontinuities here; their values always come from EvaluateAt.
            // UDL boundaries alone cannot create jump lines from numerical residuals.
            return kind switch
            {
                ResultDiagramKind.ShearForce => node.ReactionY is { Newtons: not 0 }
                    || solution.Beam.Loads.OfType<PointForce>().Any(l => l.Position == node.Position),
                ResultDiagramKind.BendingMoment => node.ReactionMoment is { NewtonMeters: not 0 }
                    || solution.Beam.Loads.OfType<PointMoment>().Any(l => l.Position == node.Position),
                _ => false
            };
        }

        void Subdivide(ResultDiagramPoint left, ResultDiagramPoint right, List<ResultDiagramPoint> points, int depth)
        {
            double span = right.Position.Meters - left.Position.Meters;
            double midpoint = left.Position.Meters + span / 2;
            if (depth == MaximumDepth || midpoint <= left.Position.Meters || midpoint >= right.Position.Meters)
            {
                points.Add(right);
                return;
            }
            var middle = Evaluate(midpoint, EvaluationSide.Right);
            bool refine = right.Screen.X - left.Screen.X > MaximumSampleSpacing;
            foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
            {
                double x = left.Position.Meters + span * fraction;
                if (x <= left.Position.Meters || x >= right.Position.Meters) continue;
                var probe = fraction == 0.5 ? middle : Evaluate(x, EvaluationSide.Right);
                double t = (probe.Screen.X - left.Screen.X) / (right.Screen.X - left.Screen.X);
                double chord = left.Screen.Y + (right.Screen.Y - left.Screen.Y) * t;
                refine |= Math.Abs(probe.Screen.Y - chord) > MaximumChordError;
            }
            if (refine)
            {
                Subdivide(left, middle, points, depth + 1);
                Subdivide(middle, right, points, depth + 1);
            }
            else points.Add(right);
        }
    }
}
