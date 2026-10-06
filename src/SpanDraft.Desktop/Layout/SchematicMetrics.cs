namespace SpanDraft.Desktop.Layout;

/// <summary>Technical DIP geometry shared by glyphs and neutral layout requirements.</summary>
public static class SchematicMetrics
{
    public const double SymbolStrokeWidth = 1.5;
    public const double ForceArrowHalfWidth = 4;
    public const double ArrowHeadLength = 8;
    public const double Clearance = 2 * ForceArrowHalfWidth;
    public const double SupportHalfWidth = 18;
    public const double SupportTriangleHeight = 20;
    public const double SupportGroundY = 32;
    public const double SupportWallHalfHeight = 24;
    public const double FixedHatchWidth = 12;
    public const double PinnedHatchRightExtent = 20;
    public const double PointLoadHalfSize = 18;
    public const double MomentRadius = 15;
    public const double MomentArrowHalfWidth = 3;
    public const double ForceHeight = 36;
    public const double EntityLabelPadding = 8;
    public const double MinimumBeamPaneHeight = 220;
    public const double LengthInputWidth = 88;
    public const double LengthInputHeight = 30;
    public const double BaseSideMargin = 72;
    public const double OuterPadding = 8;
    public const double AxisLabelPadding = 8;
    public const double AxisBaseHeight = 24;
    public const double AxisLabelLineHeight = 16;
    public const double AxisVerticalPadding = 8;
}

internal static class LayoutNumbers
{
    internal static double Finite(double value, string parameter)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter);
        return value;
    }

    internal static double Positive(double value, string parameter)
    {
        if (Finite(value, parameter) <= 0) throw new ArgumentOutOfRangeException(parameter);
        return value;
    }

    internal static double NonNegative(double value, string parameter)
    {
        if (Finite(value, parameter) < 0) throw new ArgumentOutOfRangeException(parameter);
        return value;
    }
}
