namespace SpanDraft.Desktop.Layout;

/// <summary>Local DIP geometry shared by pinned and roller drawing, layout and hit testing.</summary>
public readonly record struct HingedSupportGeometry(bool IsRoller)
{
    public double TriangleHalfWidth => SchematicMetrics.PinnedTriangleHalfWidth * (IsRoller ? SchematicMetrics.RollerTriangleScale : 1);
    public double TriangleHeight => SchematicMetrics.PinnedTriangleHeight * (IsRoller ? SchematicMetrics.RollerTriangleScale : 1);

    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> TriangleLines
    {
        get
        {
            yield return (0, 0, -TriangleHalfWidth, TriangleHeight);
            yield return (TriangleHalfWidth, TriangleHeight, 0, 0);
            if (IsRoller)
                yield return (-SchematicMetrics.RollerBaseHalfWidth, TriangleHeight, SchematicMetrics.RollerBaseHalfWidth, TriangleHeight);
        }
    }

    public (double StartX, double StartY, double EndX, double EndY) Ground =>
        (-SchematicMetrics.SupportGroundHalfWidth, SchematicMetrics.SupportGroundY,
            SchematicMetrics.SupportGroundHalfWidth, SchematicMetrics.SupportGroundY);

    public (double X, double Y, double Radius) Joint => (0, 0, SchematicMetrics.SupportJointRadius);

    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Hatches
    {
        get
        {
            double spacing = (2 * SchematicMetrics.SupportGroundHalfWidth - SchematicMetrics.SupportHatchSize)
                / (SchematicMetrics.HingedHatchCount - 1);
            for (int i = 0; i < SchematicMetrics.HingedHatchCount; i++)
            {
                double x = -SchematicMetrics.SupportGroundHalfWidth + SchematicMetrics.SupportHatchSize + i * spacing;
                yield return (x, SchematicMetrics.SupportGroundY, x - SchematicMetrics.SupportHatchSize,
                    SchematicMetrics.SupportGroundY + SchematicMetrics.SupportHatchSize);
            }
        }
    }

    // The ground line is the widest contour, the joint the highest, and the hatches the lowest.
    public double LeftExtent => SchematicMetrics.SupportGroundHalfWidth + SchematicMetrics.SupportGroundStrokeWidth / 2;
    public double RightExtent => LeftExtent;
    public double Top => -Joint.Radius - SchematicMetrics.SymbolStrokeWidth / 2;
    public double Bottom => SchematicMetrics.SupportGroundY + SchematicMetrics.SupportHatchSize + SchematicMetrics.SymbolStrokeWidth / 2;

    public bool Contains(double dx, double dy, double padding) =>
        dx >= -LeftExtent - padding && dx <= RightExtent + padding && dy >= Top - padding && dy <= Bottom + padding;
}
