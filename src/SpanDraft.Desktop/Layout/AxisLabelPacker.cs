namespace SpanDraft.Desktop.Layout;

public enum AxisEndpointRole { Interior, Start, End }
public readonly record struct AxisLabelRequirement(double StationAnchorX, double MeasuredWidth, long StableOrderKey,
    AxisEndpointRole EndpointRole = AxisEndpointRole.Interior);
public readonly record struct PackedAxisLabel(AxisLabelRequirement Requirement, double Left, double Right, int Lane);

public sealed class AxisLabelPackingResult
{
    internal AxisLabelPackingResult(IEnumerable<PackedAxisLabel> labels, int lanes, double height)
    {
        Labels = Array.AsReadOnly(labels.ToArray());
        LaneCount = lanes;
        PaneHeight = height;
    }
    public IReadOnlyList<PackedAxisLabel> Labels { get; }
    public int LaneCount { get; }
    public double PaneHeight { get; }
}

/// <summary>Minimum interval partitioning of already measured labels, independent of text and fonts.</summary>
public static class AxisLabelPacker
{
    public static AxisLabelPackingResult Pack(IEnumerable<AxisLabelRequirement> requirements, double paneLeft, double paneRight,
        double horizontalPadding = SchematicMetrics.AxisLabelPadding, double baseHeight = SchematicMetrics.AxisBaseHeight,
        double lineHeight = SchematicMetrics.AxisLabelLineHeight, double verticalPadding = SchematicMetrics.AxisVerticalPadding)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        LayoutNumbers.Finite(paneLeft, nameof(paneLeft));
        LayoutNumbers.Finite(paneRight, nameof(paneRight));
        double width = LayoutNumbers.Positive(paneRight - paneLeft, nameof(paneRight));
        LayoutNumbers.NonNegative(horizontalPadding, nameof(horizontalPadding));
        LayoutNumbers.NonNegative(baseHeight, nameof(baseHeight));
        LayoutNumbers.Positive(lineHeight, nameof(lineHeight));
        LayoutNumbers.NonNegative(verticalPadding, nameof(verticalPadding));
        var source = requirements.ToArray();
        var keys = new HashSet<long>();
        var anchors = new HashSet<double>();
        var bounds = new List<PackedAxisLabel>();
        foreach (var r in source)
        {
            LayoutNumbers.Finite(r.StationAnchorX, nameof(requirements));
            LayoutNumbers.NonNegative(r.MeasuredWidth, nameof(requirements));
            if (!Enum.IsDefined(r.EndpointRole) || !keys.Add(r.StableOrderKey) || !anchors.Add(r.StationAnchorX))
                throw new ArgumentException("Exactly one label and stable key per station is required.", nameof(requirements));
            double left = r.StationAnchorX - r.MeasuredWidth / 2;
            if (r.EndpointRole != AxisEndpointRole.Interior)
                left = r.MeasuredWidth <= width ? Math.Clamp(left, paneLeft, paneRight - r.MeasuredWidth)
                    : r.EndpointRole == AxisEndpointRole.Start ? paneLeft : paneRight - r.MeasuredWidth;
            bounds.Add(new(r, LayoutNumbers.Finite(left, nameof(requirements)),
                LayoutNumbers.Finite(left + r.MeasuredWidth, nameof(requirements)), 0));
        }
        var laneEnds = new List<double>();
        var labels = new List<PackedAxisLabel>();
        foreach (var label in bounds.OrderBy(b => b.Left).ThenBy(b => b.Requirement.StableOrderKey))
        {
            int lane = laneEnds.FindIndex(end => label.Left - end >= horizontalPadding);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(label.Right); }
            else laneEnds[lane] = label.Right;
            labels.Add(label with { Lane = lane });
        }
        double height = LayoutNumbers.Finite(baseHeight + laneEnds.Count * lineHeight + verticalPadding, nameof(lineHeight));
        return new(labels, laneEnds.Count, height);
    }
}
