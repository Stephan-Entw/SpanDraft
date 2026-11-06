using Avalonia;

namespace SpanDraft.Desktop.Controls;

internal sealed record ResultDiagramRegion(int Sign, IReadOnlyList<ResultDiagramPoint> Points)
{
    internal ResultDiagramPoint Extremum => Points.Aggregate((best, point) => Math.Abs(point.SiValue) > Math.Abs(best.SiValue) ? point : best);
}

internal static class ResultDiagramRegions
{
    internal const double ZeroTolerance = 1e-9;
    internal const double HatchSpacing = 12;

    internal static IReadOnlyList<ResultDiagramRegion> Create(IReadOnlyList<ResultDiagramPoint> section, double zeroY)
    {
        if (section.Count == 0) return [];
        var signs = section.Select(p => Math.Abs(p.SiValue) < ZeroTolerance ? 0 : Math.Sign(p.SiValue)).ToArray();
        for (int i = signs.Length - 2; i >= 0; i--) if (signs[i] == 0) signs[i] = signs[i + 1];
        for (int i = 1; i < signs.Length; i++) if (signs[i] == 0) signs[i] = signs[i - 1];
        if (signs[0] == 0) return [];
        var regions = new List<ResultDiagramRegion>();
        var points = new List<ResultDiagramPoint>();
        for (int i = 0; i < section.Count; i++)
        {
            var point = Zeroed(section[i]);
            if (i > 0 && signs[i] != signs[i - 1])
            {
                var previous = Zeroed(section[i - 1]);
                double scale = Math.Max(Math.Abs(previous.SiValue), Math.Abs(point.SiValue));
                double t = Math.Abs(previous.SiValue / scale) / (Math.Abs(previous.SiValue / scale) + Math.Abs(point.SiValue / scale));
                var crossing = previous with
                {
                    Position = SpanDraft.Core.Units.Length.FromMeters(previous.Position.Meters + t * (point.Position.Meters - previous.Position.Meters)),
                    SiValue = 0, Value = 0, Screen = new(previous.Screen.X + t * (point.Screen.X - previous.Screen.X), zeroY)
                };
                points.Add(crossing);
                regions.Add(new(signs[i - 1], points.ToArray()));
                points = [crossing];
            }
            points.Add(point);
        }
        regions.Add(new(signs[^1], points.ToArray()));
        return regions.Where(r => r.Points.Any(p => Math.Abs(p.SiValue) >= ZeroTolerance)).ToArray();

        ResultDiagramPoint Zeroed(ResultDiagramPoint point) => Math.Abs(point.SiValue) < ZeroTolerance
            ? point with { SiValue = 0, Value = 0, Screen = new(point.Screen.X, zeroY) } : point;
    }

    internal static IEnumerable<(Point Start, Point End)> Hatch(ResultDiagramRegion region, double zeroY)
    {
        var points = region.Points;
        int segment = 1;
        for (double x = Math.Ceiling(points[0].Screen.X / HatchSpacing) * HatchSpacing; x <= points[^1].Screen.X; x += HatchSpacing)
        {
            while (segment < points.Count - 1 && points[segment].Screen.X < x) segment++;
            var a = points[segment - 1].Screen; var b = points[segment].Screen;
            if (b.X <= a.X) continue;
            double y = a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y);
            if (y != zeroY) yield return (new(x, zeroY), new(x, y));
        }
    }
}
