namespace SpanDraft.Desktop.Layout;

/// <summary>Technical DIP geometry shared by glyphs and neutral layout requirements.</summary>
public static class SchematicMetrics
{
    public const double BeamStrokeWidth = 3;
    public const double SymbolStrokeWidth = 1.5;
    public const double ForceArrowHalfWidth = 4;
    public const double ArrowHeadLength = 8;
    public const double Clearance = 2 * ForceArrowHalfWidth;
    public const double SupportGroundHalfWidth = 20;
    public const double SupportGroundY = 20;
    public const double SupportGroundStrokeWidth = 2;
    public const double SupportHatchSize = 9;
    public const int HingedHatchCount = 4;
    public const double SupportJointRadius = 3;
    public const double PinnedTriangleHalfWidth = 14;
    public const double PinnedTriangleHeight = 20;
    public const double RollerTriangleScale = 0.8;
    public const double RollerBaseHalfWidth = 18;
    public const double SupportLabelTopOffset = 40;
    public const double SupportWallHalfHeight = 24;
    public const double FixedHitHalfWidth = 18;
    public const int FixedHatchCount = 5;
    public const double FixedWallStrokeWidth = 2;
    public const double PointLoadHalfSize = MomentRadius + MomentArrowHalfWidth;
    public const double MomentRadius = 15;
    public const double MomentArrowHalfWidth = ForceArrowHalfWidth;
    public const double ForceHeight = 36;
    public const double ForceBeamGap = 1;
    // Include both stroke radii so the painted force symbol clears the beam.
    public const double ForceBeamOffset = (BeamStrokeWidth + SymbolStrokeWidth) / 2 + ForceBeamGap;
    public const double ForceTopOffset = ForceBeamOffset + ForceHeight;
    public const double DistributedArrowHeight = 28;
    public const double DistributedArrowHeadLength = 7;
    public const double DistributedArrowHeadHalfWidth = 3.5;
    public const double DistributedMinimumWidth = 24;
    public const double DistributedTargetSpacing = 32;
    public const double DistributedFillOpacity = 0.06;
    public const double DistributedOverlapFillOpacity = 0.26;
    public const double DistributedMaximumFillOpacity = 0.75;
    public const double DistributedTopOffset = ForceBeamOffset + DistributedArrowHeight;
    public const double EntityLabelPadding = 8;
    public const double MinimumBeamPaneHeight = 180;
    // One support label row. The axis pane supplies the clearance below it.
    public const double BelowBeamSpace = SupportLabelTopOffset + AxisLabelLineHeight;
    public const double LengthInputWidth = 88;
    public const double LengthInputHeight = 30;
    public const double BaseSideMargin = 72;
    public const double OuterPadding = 8;
    public const double AxisLabelPadding = 8;
    public const double AxisBaseHeight = 24;
    public const double AxisLabelLineHeight = 16;
    public const double AxisVerticalPadding = 8;

    /// <summary>Total neutral fill opacity for the number of UDLs covering a screen interval.</summary>
    public static double DistributedFillOpacityForCount(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;
        if (count == 1) return DistributedFillOpacity;
        if (count == 2) return DistributedOverlapFillOpacity;
        double remaining = (1 - DistributedOverlapFillOpacity) / (1 - DistributedFillOpacity);
        return Math.Min(DistributedMaximumFillOpacity,
            1 - (1 - DistributedFillOpacity) * Math.Pow(remaining, count - 1));
    }
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
