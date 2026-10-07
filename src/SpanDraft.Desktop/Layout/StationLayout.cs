namespace SpanDraft.Desktop.Layout;

public readonly record struct StationRequirement(double PhysicalX, double LeftExtent, double RightExtent);

/// <summary>A minimum visible distance between two physical station anchors.</summary>
public readonly record struct SpanRequirement(double StartPhysicalX, double EndPhysicalX, double MinimumScreenWidth);

public readonly record struct LayoutStation(double PhysicalX, double ScreenX, double LeftExtent, double RightExtent);

public sealed class StationLayoutResult
{
    internal StationLayoutResult(IEnumerable<LayoutStation> stations, bool distorted, bool overconstrained)
    {
        Transform = new(stations);
        IsDistorted = distorted;
        IsOverconstrained = overconstrained;
    }

    public IReadOnlyList<LayoutStation> Stations => Transform.Stations;
    public StationTransform Transform { get; }
    public bool IsDistorted { get; }
    public bool IsOverconstrained { get; }
}

/// <summary>Pure, exact-station layout. Physical coordinates use metres; screen values use DIPs.</summary>
public static class StationLayout
{
    public const double DistortionRelativeTolerance = 1e-10;

    public static StationLayoutResult Compute(double lengthMeters, double screenLeft, double screenRight,
        IEnumerable<StationRequirement> requirements, double clearance = SchematicMetrics.Clearance,
        IEnumerable<SpanRequirement>? spanRequirements = null)
    {
        LayoutNumbers.Positive(lengthMeters, nameof(lengthMeters));
        LayoutNumbers.Finite(screenLeft, nameof(screenLeft));
        LayoutNumbers.Finite(screenRight, nameof(screenRight));
        double width = LayoutNumbers.Positive(screenRight - screenLeft, nameof(screenRight));
        LayoutNumbers.Positive(clearance, nameof(clearance));
        ArgumentNullException.ThrowIfNull(requirements);
        var source = requirements.ToArray();
        var spans = (spanRequirements ?? []).ToArray();
        foreach (var span in spans)
        {
            LayoutNumbers.NonNegative(span.StartPhysicalX, nameof(spanRequirements));
            LayoutNumbers.Finite(span.EndPhysicalX, nameof(spanRequirements));
            if (span.StartPhysicalX >= span.EndPhysicalX || span.EndPhysicalX > lengthMeters)
                throw new ArgumentOutOfRangeException(nameof(spanRequirements));
            LayoutNumbers.NonNegative(span.MinimumScreenWidth, nameof(spanRequirements));
        }
        foreach (var r in source)
        {
            LayoutNumbers.NonNegative(r.PhysicalX, nameof(requirements));
            if (r.PhysicalX > lengthMeters) throw new ArgumentOutOfRangeException(nameof(requirements));
            LayoutNumbers.NonNegative(r.LeftExtent, nameof(requirements));
            LayoutNumbers.NonNegative(r.RightExtent, nameof(requirements));
        }
        var stations = source.Concat(spans.SelectMany(s => new[]
            { new StationRequirement(s.StartPhysicalX, 0, 0), new StationRequirement(s.EndPhysicalX, 0, 0) }))
            .Concat([new(0, 0, 0), new(lengthMeters, 0, 0)])
            .GroupBy(r => r.PhysicalX).OrderBy(g => g.Key)
            .Select(g => new StationRequirement(g.Key, g.Max(r => r.LeftExtent), g.Max(r => r.RightExtent)))
            .ToArray();
        int count = stations.Length - 1;
        var physicalWeights = new double[count];
        var minimumWeights = new double[count];
        // Scale before summing: even finite extents may have an overflowing unscaled sum.
        double scale = Math.Max(clearance, stations.Max(r => Math.Max(r.LeftExtent, r.RightExtent)));
        for (int i = 0; i < count; i++)
        {
            physicalWeights[i] = (stations[i + 1].PhysicalX - stations[i].PhysicalX) / lengthMeters;
            minimumWeights[i] = stations[i].RightExtent / scale + clearance / scale + stations[i + 1].LeftExtent / scale;
        }
        double minimumSum = minimumWeights.Sum();
        double available = width / scale;
        bool overconstrained = minimumSum > available;
        double[] gaps;
        if (overconstrained || minimumSum == available)
            gaps = minimumWeights.Select(m => width * (m / minimumSum)).ToArray();
        else
            gaps = Distribute(width, physicalWeights, minimumWeights.Select(m => scale * m).ToArray());

        if (spans.Length > 0)
        {
            var offsets = new double[stations.Length];
            for (int i = 0; i < count; i++) offsets[i + 1] = offsets[i] + gaps[i];
            var indices = stations.Select((s, i) => (s.PhysicalX, i)).ToDictionary(s => s.PhysicalX, s => s.i);
            var intervals = spans.Select(s => (Start: indices[s.StartPhysicalX], End: indices[s.EndPhysicalX],
                Minimum: s.MinimumScreenWidth)).ToArray();
            if (intervals.Any(s => offsets[s.End] - offsets[s.Start] < s.Minimum))
                (gaps, overconstrained) = DistributeSpans(width, physicalWeights, stations, clearance, intervals);
        }

        var result = new LayoutStation[stations.Length];
        double offset = 0;
        bool distorted = false;
        for (int i = 0; i < stations.Length; i++)
        {
            double screen = i == count ? screenRight : screenLeft + offset;
            LayoutNumbers.Finite(screen, nameof(screenRight));
            if (i > 0)
            {
                double actualGap = screen - result[i - 1].ScreenX;
                if (actualGap <= 0) throw new ArgumentException("Screen bounds cannot represent strictly ordered stations.", nameof(screenRight));
                if (Math.Abs(actualGap - width * physicalWeights[i - 1]) > width * DistortionRelativeTolerance)
                    distorted = true;
            }
            result[i] = new(stations[i].PhysicalX, screen, stations[i].LeftExtent, stations[i].RightExtent);
            if (i < count) offset += gaps[i];
        }
        return new(result, distorted, overconstrained);
    }

    private static double[] Distribute(double width, double[] weights, double[] minimum)
    {
        var active = new bool[weights.Length];
        var gaps = new double[weights.Length];
        while (true)
        {
            double fixedWidth = 0, freeWeight = 0;
            for (int i = 0; i < weights.Length; i++)
                if (active[i]) fixedWidth += minimum[i]; else freeWeight += weights[i];
            double remaining = Math.Max(0, width - fixedWidth);
            if (freeWeight <= 0) throw new ArgumentException("Physical gaps cannot be represented proportionally.", nameof(weights));
            bool changed = false;
            for (int i = 0; i < weights.Length; i++)
            {
                gaps[i] = active[i] ? minimum[i] : remaining * (weights[i] / freeWeight);
                if (!active[i] && gaps[i] < minimum[i]) { active[i] = true; changed = true; }
            }
            if (!changed) return gaps;
            if (active.All(a => a)) return minimum;
        }
    }

    // All constraints point forward through the station sequence. A longest-path
    // pass is enough; there is no general constraint solver or Entity dependency.
    private static (double[] Gaps, bool Overconstrained) DistributeSpans(double width, double[] weights,
        StationRequirement[] stations, double clearance, (int Start, int End, double Minimum)[] spans)
    {
        double scale = Math.Max(clearance, Math.Max(stations.Max(s => Math.Max(s.LeftExtent, s.RightExtent)), spans.Max(s => s.Minimum)));
        double available = width / scale;
        var minima = Enumerable.Range(0, weights.Length).Select(i =>
            stations[i].RightExtent / scale + clearance / scale + stations[i + 1].LeftExtent / scale).ToArray();
        var ending = Enumerable.Range(0, stations.Length).Select(i => spans.Where(s => s.End == i)
            .Select(s => (s.Start, Minimum: s.Minimum / scale)).ToArray()).ToArray();
        double[] Place(double factor)
        {
            var positions = new double[stations.Length];
            for (int i = 1; i < positions.Length; i++)
            {
                double position = positions[i - 1] + Math.Max(minima[i - 1], factor * weights[i - 1]);
                foreach (var span in ending[i]) position = Math.Max(position, positions[span.Start] + span.Minimum);
                positions[i] = position;
            }
            return positions;
        }
        var minimum = Place(0);
        bool overconstrained = minimum[^1] > available;
        double[] selected;
        if (overconstrained || minimum[^1] == available) selected = minimum;
        else
        {
            double low = 0, high = available;
            for (int iteration = 0; iteration < 96; iteration++)
            {
                double middle = low + (high - low) / 2;
                if (middle == low || middle == high) break;
                if (Place(middle)[^1] <= available) low = middle; else high = middle;
            }
            selected = Place(low);
        }
        var gaps = new double[weights.Length];
        for (int i = 0; i < gaps.Length; i++)
            gaps[i] = overconstrained ? width * ((selected[i + 1] - selected[i]) / selected[^1])
                : scale * (selected[i + 1] - selected[i]);
        return (gaps, overconstrained);
    }
}

public readonly record struct StationOuterMargins(double Left, double Right)
{
    public static StationOuterMargins Calculate(StationRequirement start, StationRequirement end,
        double baseSideMargin = SchematicMetrics.BaseSideMargin, double outerPadding = SchematicMetrics.OuterPadding)
    {
        LayoutNumbers.NonNegative(baseSideMargin, nameof(baseSideMargin));
        LayoutNumbers.NonNegative(outerPadding, nameof(outerPadding));
        LayoutNumbers.NonNegative(start.LeftExtent, nameof(start));
        LayoutNumbers.NonNegative(end.RightExtent, nameof(end));
        return new(LayoutNumbers.Finite(Math.Max(baseSideMargin, start.LeftExtent + outerPadding), nameof(start)),
            LayoutNumbers.Finite(Math.Max(baseSideMargin, end.RightExtent + outerPadding), nameof(end)));
    }
}
