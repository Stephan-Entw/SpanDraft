namespace SpanDraft.Core.Sections.Geometry;

/// <summary>Exact contour support distances in the integration frame, including interior arc extrema.</summary>
internal static class GeometryProjection
{
    internal static (double Positive, double Negative) Extrema(SectionContour[] contours,
        GeometryFrame frame, Extended cy, Extended cz, double dy, double dz)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var contour in contours)
        foreach (var segment in contour.Segments)
        {
            Include(frame.Point(segment.Start));
            Include(frame.Point(segment.End));
            if (segment is SectionArc arc)
            {
                var parameter = GeometryMath.ArcParameter(arc, dy, dz);
                if (GeometryMath.OnArc(arc, parameter))
                    Include(frame.ArcPoint(arc, parameter));
                parameter = GeometryMath.ArcParameter(arc, -dy, -dz);
                if (GeometryMath.OnArc(arc, parameter))
                    Include(frame.ArcPoint(arc, parameter));
            }
        }
        return (DomainGuard.Positive(max, nameof(contours)), DomainGuard.Positive(-min, nameof(contours)));

        void Include(Point2 p)
        {
            var distance = (((Extended)p.Y - cy) * dy + ((Extended)p.Z - cz) * dz).Value;
            min = Math.Min(min, distance);
            max = Math.Max(max, distance);
        }
    }

}
