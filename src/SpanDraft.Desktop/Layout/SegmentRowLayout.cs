namespace SpanDraft.Desktop.Layout;

/// <summary>A segment of the row with the label that fits it, or none.</summary>
public readonly record struct SegmentRowSpan(double StartX, double EndX, string? Text)
{
    public double Width => EndX - StartX;
}

/// <summary>A segment's extent in metres and its labels from the most to the least detailed.</summary>
public readonly record struct SegmentRowInput(double StartMeters, double EndMeters, IReadOnlyList<string> Candidates);

/// <summary>
/// The segment row under the beam. Its boundaries are those of the station layout, so they stand exactly where the beam's
/// segments and the ticks of the x axis are, in a schematic drawing as well; it never asks the layout for room.
/// Each segment gets the most detailed label that fits its width without wrapping, and none when not even the last fits.
/// The label sits in the line, which is interrupted around it.
/// </summary>
public static class SegmentRowLayout
{
    /// <summary>The clear space kept at either end of a label; the line is interrupted over the label and this space.</summary>
    public const double LabelPadding = 10;

    /// <summary>The part of <see cref="LabelPadding"/> that stays empty on the line beside the label; the rest is line.</summary>
    public const double LabelGap = 5;

    public static IReadOnlyList<SegmentRowSpan> Create(IReadOnlyList<SegmentRowInput> segments, StationTransform transform,
        Func<string, double> measureWidth)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(measureWidth);
        var spans = new SegmentRowSpan[segments.Count];
        for (int i = 0; i < spans.Length; i++)
        {
            double start = transform.PhysicalToScreen(segments[i].StartMeters), end = transform.PhysicalToScreen(segments[i].EndMeters);
            spans[i] = new(start, end, Choose(end - start, segments[i].Candidates, measureWidth));
        }
        return Array.AsReadOnly(spans);
    }

    /// <summary>The first candidate that fits the width with its padding; null when none does.</summary>
    public static string? Choose(double width, IReadOnlyList<string> candidates, Func<string, double> measureWidth)
    {
        double available = width - 2 * LabelPadding;
        if (!(available > 0)) return null;
        foreach (string candidate in candidates)
            if (measureWidth(candidate) <= available) return candidate;
        return null;
    }
}
