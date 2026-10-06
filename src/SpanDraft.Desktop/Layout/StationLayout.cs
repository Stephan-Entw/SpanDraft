namespace SpanDraft.Desktop.Layout;

public readonly record struct StationRequirement(double PhysicalX, double LeftExtent, double RightExtent);

/// <summary>Extension point only; interval constraints are not processed in Stage 1.</summary>
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
        IEnumerable<StationRequirement> requirements, double clearance = SchematicMetrics.Clearance)
    {
        LayoutNumbers.Positive(lengthMeters, nameof(lengthMeters));
        LayoutNumbers.Finite(screenLeft, nameof(screenLeft));
        LayoutNumbers.Finite(screenRight, nameof(screenRight));
        double width = LayoutNumbers.Positive(screenRight - screenLeft, nameof(screenRight));
        LayoutNumbers.Positive(clearance, nameof(clearance));
        ArgumentNullException.ThrowIfNull(requirements);
        var source = requirements.ToArray();
        foreach (var r in source)
        {
            LayoutNumbers.NonNegative(r.PhysicalX, nameof(requirements));
            if (r.PhysicalX > lengthMeters) throw new ArgumentOutOfRangeException(nameof(requirements));
            LayoutNumbers.NonNegative(r.LeftExtent, nameof(requirements));
            LayoutNumbers.NonNegative(r.RightExtent, nameof(requirements));
        }
        var stations = source.Concat([new(0, 0, 0), new(lengthMeters, 0, 0)])
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
