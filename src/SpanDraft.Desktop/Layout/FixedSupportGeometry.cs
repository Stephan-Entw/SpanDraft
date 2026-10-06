namespace SpanDraft.Desktop.Layout;

/// <summary>Local DIP geometry, mirrored only at the exact physical beam end.</summary>
public readonly record struct FixedSupportGeometry(bool IsMirrored)
{
    public static FixedSupportGeometry AtPosition(double positionMeters, double lengthMeters) => new(positionMeters == lengthMeters);

    public (double StartX, double StartY, double EndX, double EndY) Wall =>
        (0, -SchematicMetrics.SupportWallHalfHeight, 0, SchematicMetrics.SupportWallHalfHeight);

    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Hatches
    {
        get
        {
            double spacing = (2 * SchematicMetrics.SupportWallHalfHeight - SchematicMetrics.SupportHatchSize)
                / (SchematicMetrics.FixedHatchCount - 1);
            double endX = IsMirrored ? SchematicMetrics.SupportHatchSize : -SchematicMetrics.SupportHatchSize;
            for (int i = 0; i < SchematicMetrics.FixedHatchCount; i++)
            {
                double y = -SchematicMetrics.SupportWallHalfHeight + i * spacing;
                yield return (0, y, endX, y + SchematicMetrics.SupportHatchSize);
            }
        }
    }

    private static double HatchExtent => Math.Max(SchematicMetrics.FixedWallStrokeWidth / 2,
        SchematicMetrics.SupportHatchSize + SchematicMetrics.SymbolStrokeWidth / 2);
    public double LeftExtent => IsMirrored ? SchematicMetrics.FixedWallStrokeWidth / 2 : HatchExtent;
    public double RightExtent => IsMirrored ? HatchExtent : SchematicMetrics.FixedWallStrokeWidth / 2;

    // Preserve the generous interaction zone while reflecting it with the glyph.
    public bool Contains(double dx, double dy, double padding)
    {
        double localX = IsMirrored ? -dx : dx;
        return localX >= -SchematicMetrics.FixedHitHalfWidth - padding && localX <= padding
            && Math.Abs(dy) <= SchematicMetrics.SupportWallHalfHeight + padding;
    }
}
