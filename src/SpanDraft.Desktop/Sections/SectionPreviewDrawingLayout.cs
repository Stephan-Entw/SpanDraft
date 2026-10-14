using Avalonia;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Sections;

public sealed record PreviewDrawingLine(Point Start, Point End);
public sealed record PreviewDrawingArrow(Point Tip, Vector TailDirection)
{
    public const double Length = 13.2, HalfWidth = 2;
    public IReadOnlyList<Point> Points => [Tip, Tip + TailDirection * Length + new Vector(-TailDirection.Y, TailDirection.X) * HalfWidth,
        Tip + TailDirection * Length - new Vector(-TailDirection.Y, TailDirection.X) * HalfWidth];
    public Rect Bounds => new(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X) - Points.Min(p => p.X), Points.Max(p => p.Y) - Points.Min(p => p.Y));
}
public sealed record PreviewDrawingDimension(string Key, string Text, Rect TextBounds,
    IReadOnlyList<PreviewDrawingLine> Lines, IReadOnlyList<PreviewDrawingArrow> Arrows);
public sealed record PreviewDrawingAxis(SectionAxisDesignation Axis, Point Start, Point End, Rect LabelBounds, bool IsReference = false)
{
    public Point LabelPosition => LabelBounds.TopLeft;
}

/// <summary>Screen-space annotation layout. Physical contours, thicknesses and engineering values are never changed.</summary>
public sealed class SectionPreviewDrawingLayout
{
    public const double AxisStartOverhang = 6, AxisEndOverhang = 20;
    public const double WebCaptionGap = 28;
    public const double WebHeightFraction = .70, TWebHeightFraction = .25;
    public const double RadiusLeaderAngleDegrees = 35, RadiusLeaderLength = 32;
    private SectionPreviewDrawingLayout(double scale, Rect profileBounds, double height)
    { Scale = scale; ProfileBounds = profileBounds; _height = height; }
    private readonly double _height;
    public double Scale { get; }
    public Rect ProfileBounds { get; }
    public IReadOnlyList<PreviewDrawingDimension> Dimensions { get; private set; } = [];
    public IReadOnlyList<PreviewDrawingAxis> Axes { get; private set; } = [];
    public Point Project(Point point) => new(ProfileBounds.Left + point.X * Scale, ProfileBounds.Top + (_height - point.Y) * Scale);

    public static SectionPreviewDrawingLayout Create(SectionPreviewGeometry geometry, Size size, UnitDefinition unit, Func<string, Size> measure)
    {
        string Label(PreviewDimension dimension) => (dimension.Key is "d" or "D" ? "Ø" : dimension.Key)
            + (dimension.IsConfirmed ? " = " + InputQuantityFormatter.WithUnit(dimension.Value, unit) : "");
        var labels = geometry.Dimensions.Values.ToDictionary(d => d.Key, Label);
        var textSizes = labels.ToDictionary(p => p.Key, p => measure(p.Value));
        // Preserve the physical drawing envelope when changing the radius caption.
        double detailWidth = geometry.Dimensions.Values.Where(IsDetail).Select(d => d.Key == "r" && d.IsConfirmed
            ? measure("R" + InputQuantityFormatter.WithUnit(d.Value, unit)).Width : textSizes[d.Key].Width).DefaultIfEmpty(48).Max();
        double left = geometry.Dimensions.ContainsKey("h") ? Math.Max(58, textSizes["h"].Width + 34) : 58;
        double right = Math.Max(100, detailWidth + 52);
        const double top = 52, bottom = 90;
        double width = Math.Max(1, size.Width - left - right), height = Math.Max(1, size.Height - top - bottom);
        // Match the reference's generous drawing space without enlarging the physical profile indefinitely.
        double scale = Math.Min(Math.Min(width / geometry.Width, height / geometry.Height),
            310 / Math.Max(geometry.Width, geometry.Height));
        var bounds = new Rect(left + (width - geometry.Width * scale) / 2, top + (height - geometry.Height * scale) / 2,
            geometry.Width * scale, geometry.Height * scale);
        var layout = new SectionPreviewDrawingLayout(scale, bounds, geometry.Height);
        var dimensions = new List<PreviewDrawingDimension>();
        Point Project(Point point) => layout.Project(point);
        Rect Caption(string key, Point point, bool centered = false)
        {
            var textSize = textSizes[key];
            return FitText(new Rect(new Point(point.X - (centered ? textSize.Width / 2 : 0), point.Y), textSize), size);
        }
        foreach (var dimension in geometry.Dimensions.Values.Where(d => !IsDetail(d)))
        {
            Point start = Project(dimension.Start), end = Project(dimension.End);
            bool vertical = dimension.Key == "h";
            if (geometry.Type == SectionEditorType.TSection && vertical)
                start = new(Project(geometry.Dimensions["tw"].Start).X, start.Y);
            if (geometry.Type == SectionEditorType.TSection && dimension.Key == "b")
            {
                // The width belongs to the upper flange. Extend from its lower
                // outer corners to the common width dimension below the profile.
                double flangeBottom = geometry.Height - geometry.Dimensions["tf"].Value;
                start = Project(new(0, flangeBottom));
                end = Project(new(geometry.Width, flangeBottom));
            }
            double measureY = bounds.Bottom + 28;
            Point a = vertical ? new(bounds.Left - 22, start.Y) : new(start.X, measureY);
            Point b = vertical ? new(bounds.Left - 22, end.Y) : new(end.X, measureY);
            var normal = vertical ? new Vector(-5, 0) : new Vector(0, 5);
            var lines = new List<PreviewDrawingLine> { new(start + normal, a + normal), new(end + normal, b + normal) };
            var arrows = new List<PreviewDrawingArrow>();
            AddDimension(a, b, lines, arrows);
            var caption = Caption(dimension.Key, vertical
                ? new(a.X - textSizes[dimension.Key].Width - 8, (a.Y + b.Y - textSizes[dimension.Key].Height) / 2)
                : new((a.X + b.X) / 2, a.Y + 16), !vertical);
            dimensions.Add(new(dimension.Key, labels[dimension.Key], caption, lines, arrows));
        }
        {
            // Incomplete drawings use the envelope's center as a drawing reference,
            // never as a calculated centroid or principal-axis result.
            bool reference = geometry.Centroid is null;
            Point center = geometry.Centroid is { } centroid ? Project(centroid) : bounds.Center;
            bool principal = !reference && geometry.Type == SectionEditorType.Angle;
            double angle = principal ? geometry.PrincipalAngle!.Value : 0;
            var axes = new List<PreviewDrawingAxis>();
            PreviewDrawingAxis Axis(SectionAxisDesignation axis, Vector direction)
            {
                double DistanceToEdge(Vector ray)
                {
                    double horizontal = Math.Abs(ray.X) < 1e-12 ? double.PositiveInfinity : ((ray.X > 0 ? bounds.Right : bounds.Left) - center.X) / ray.X;
                    double vertical = Math.Abs(ray.Y) < 1e-12 ? double.PositiveInfinity : ((ray.Y > 0 ? bounds.Bottom : bounds.Top) - center.Y) / ray.Y;
                    return Math.Min(horizontal, vertical);
                }
                Point start = center - direction * (DistanceToEdge(-direction) + AxisStartOverhang);
                Point end = center + direction * (DistanceToEdge(direction) + AxisEndOverhang);
                Size labelSize = measure(axis.ToString().ToLowerInvariant());
                Point labelCenter = end + direction * 10;
                return new(axis, start, end, FitText(new Rect(labelCenter - new Vector(labelSize.Width / 2, labelSize.Height / 2), labelSize), size), reference);
            }
            axes.Add(Axis(principal ? SectionAxisDesignation.U : SectionAxisDesignation.Y, new(Math.Cos(angle), -Math.Sin(angle))));
            axes.Add(Axis(principal ? SectionAxisDesignation.V : SectionAxisDesignation.Z, new(-Math.Sin(angle), -Math.Cos(angle))));
            layout.Axes = axes;
        }
        Vector RadiusDirection(PreviewDimension dimension)
        {
            Vector direction = Project(dimension.Start) - Project(dimension.End);
            if (geometry.Type == SectionEditorType.RectangularHollow) direction = -direction;
            if (direction.Length == 0) direction = geometry.Type == SectionEditorType.TSection ? new Vector(1, 1) : new Vector(1, -1);
            return direction / direction.Length;
        }
        bool IsFixedWebMeasure(string key) => key == "tw"
            && geometry.Type is SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection;
        bool CaptionCoversMaterial(Rect text)
        {
            if (!bounds.Intersects(text)) return false;
            if (geometry.Type is not (SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection or SectionEditorType.Angle)) return true;
            bool angle = geometry.Type == SectionEditorType.Angle;
            bool upperFlange = !angle, lowerFlange = geometry.Type != SectionEditorType.TSection;
            double flange = geometry.Dimensions[angle ? "t" : "tf"].Value * scale;
            double webRight = angle ? bounds.Left + flange : Project(geometry.Dimensions["tw"].End).X;
            double upper = bounds.Top + (upperFlange ? flange : 0), lower = bounds.Bottom - (lowerFlange ? flange : 0);
            if (text.Top < upper || text.Bottom > lower || text.Left <= webRight) return true;
            double radius = geometry.Dimensions["r"].Value * scale;
            if (radius <= 0 || text.Left >= webRight + radius) return false;
            // Concave fillets add material outside their quarter circles.
            double x = text.Left - webRight - radius;
            double topY = text.Top - upper - radius, bottomY = text.Bottom - lower + radius;
            return (upperFlange && topY < 0 && x * x + topY * topY > radius * radius)
                || (lowerFlange && bottomY > 0 && x * x + bottomY * bottomY > radius * radius);
        }
        IEnumerable<PreviewDrawingDimension> Candidates(PreviewDimension dimension)
        {
            Point start = Project(dimension.Start), end = Project(dimension.End);
            if (dimension.Key == "r")
            {
                Vector opening = RadiusDirection(dimension);
                // Prefer a short technical leader attached to the actual arc point.
                // Alternatives remain straight and geometry-relative when labels collide.
                foreach (double angle in new[] { RadiusLeaderAngleDegrees, 45d, 20d, 55d, 10d, 65d, 5d, 75d, 2d })
                foreach (double length in new[] { RadiusLeaderLength, 34d, 36d, 40d, 48d, 64d, 80d, 96d, 112d, 144d, 176d, 208d, 240d, 280d, 320d })
                {
                    double radians = angle * Math.PI / 180;
                    var direction = new Vector(Math.Cos(radians), Math.Sign(opening.Y) * Math.Sin(radians));
                    var leaderEnd = end + direction * length;
                    var caption = new Rect(new Point(leaderEnd.X + 4, leaderEnd.Y - textSizes["r"].Height / 2), textSizes["r"]);
                    if (!new Rect(size).Contains(caption) || direction.X * opening.X + direction.Y * opening.Y <= 0) continue;
                    yield return new("r", labels["r"], caption, [new(end, leaderEnd)], [new(end, direction)]);
                }
                yield break;
            }
            bool vertical = dimension.Start.X == dimension.End.X;
            // A web continues through the profile: moving its measure a fixed distance
            // can still put the filled arrows on material. Include positions beyond
            // the actual profile envelope, independently of its aspect ratio.
            var horizontalOffsets = IsFixedWebMeasure(dimension.Key)
                ? new[] { bounds.Bottom - bounds.Height * (geometry.Type == SectionEditorType.TSection ? TWebHeightFraction : WebHeightFraction) - start.Y }
                : new[] { -24d, 24d, -40d, 40d, -56d, 56d, -72d, 72d, -88d, 88d, -104d, 104d }
                    .Concat(new[] { 12d, 20d, 36d, 52d, 68d }.SelectMany(gap => new[] { bounds.Bottom + gap - start.Y, bounds.Top - gap - start.Y }));
            var verticalOffsets = IsFixedWebMeasure("tw")
                ? new[] { 32d, 36d, 38d, 46d, 60d, 6d, 10d, 14d, 18d, 22d, 26d,
                    size.Width - bounds.Right - PreviewDrawingArrow.HalfWidth - 4 }
                : new[] { 32d, 36d, 38d, 46d, 60d };
            foreach (double offset in vertical ? verticalOffsets : horizontalOffsets)
            {
                Point a = vertical ? new(bounds.Right + offset, start.Y) : start + new Vector(0, offset);
                Point b = vertical ? new(bounds.Right + offset, end.Y) : end + new Vector(0, offset);
                var lines = new List<PreviewDrawingLine>(); var arrows = new List<PreviewDrawingArrow>();
                if (vertical)
                {
                    lines.Add(new(start + new Vector(4, 0), a + new Vector(5, 0)));
                    lines.Add(new(end + new Vector(4, 0), b + new Vector(5, 0)));
                }
                else
                {
                    var separation = new Vector(0, Math.Sign(offset) * 4);
                    lines.Add(new(start + separation, a + separation)); lines.Add(new(end + separation, b + separation));
                }
                AddDimension(a, b, lines, arrows);
                // Anchor the web caption to its right edge, independently of flange width.
                foreach (double gap in IsFixedWebMeasure(dimension.Key)
                    ? Enumerable.Range((int)WebCaptionGap, Math.Max(1,
                        (int)Math.Floor(size.Width - b.X - textSizes[dimension.Key].Width - 4) - (int)WebCaptionGap + 1))
                        .Select(gap => (double)gap) : new[] { 18d })
                foreach (bool captionOnLeft in vertical && IsFixedWebMeasure("tw") ? new[] { false, true } : new[] { false })
                foreach (double captionOffset in vertical && IsFixedWebMeasure("tw")
                    ? new[] { 0d, -textSizes[dimension.Key].Height - 8, textSizes[dimension.Key].Height + 8 } : new[] { 0d })
                {
                    var caption = Caption(dimension.Key, new(vertical
                        ? captionOnLeft ? a.X - gap - textSizes[dimension.Key].Width : a.X + gap
                        : (IsFixedWebMeasure(dimension.Key) ? b.X : bounds.Right) + gap,
                        (a.Y + b.Y - textSizes[dimension.Key].Height) / 2 + captionOffset));
                    if (captionOnLeft ? caption.Right > a.X - 12
                        : caption.Left < (vertical ? a.X + 12 : b.X + PreviewDrawingArrow.Length + 4)) continue;
                    var candidateLines = lines.ToArray();
                    if (!vertical) candidateLines[^1] = candidateLines[^1] with { End = new(caption.Left - 4, a.Y) };
                    yield return new(dimension.Key, labels[dimension.Key], caption, candidateLines, arrows);
                }
            }
        }
        bool ConflictingLines(PreviewDrawingDimension first, PreviewDrawingDimension second)
        {
            // A fixed web measure can intersect the straight radius leader; keep
            // its endpoint extensions protected and check all captions and heads.
            var firstLines = IsFixedWebMeasure(first.Key) && second.Key == "r" ? first.Lines.Take(2) : first.Lines;
            var secondLines = IsFixedWebMeasure(second.Key) && first.Key == "r" ? second.Lines.Take(2) : second.Lines;
            return firstLines.Any(line => secondLines.Any(other => LinesCross(line, other)));
        }
        bool allowLeaderCrossings = false;
        bool Fits(PreviewDrawingDimension candidate)
        {
            var text = candidate.TextBounds.Inflate(2);
            // Web and radius captions can use the empty space beside the web.
            if (IsFixedWebMeasure(candidate.Key) || candidate.Key == "r"
                ? CaptionCoversMaterial(text) : bounds.Intersects(text)) return false;
            var viewport = new Rect(size);
            if (candidate.Arrows.Any(arrow => !viewport.Contains(arrow.Bounds))
                || candidate.Lines.Any(line => !viewport.Contains(line.Start) || !viewport.Contains(line.End))) return false;
            var measureLine = candidate.Key == "r" ? candidate.Lines[0] : candidate.Lines[2];
            if (candidate.Key != "r" && !IsFixedWebMeasure(candidate.Key) && (IntersectsText(measureLine, bounds.Inflate(1))
                || candidate.Arrows.Any(arrow => arrow.Bounds.Intersects(bounds.Inflate(1))))) return false;
            // Even subpixel separation from the centroid axis must not move the
            // web away from its prescribed relative height.
            if (!IsFixedWebMeasure(candidate.Key) && layout.Axes.Any(axis => LiesOnAxis(measureLine, axis))) return false;
            return !dimensions.Any(other => text.Intersects(other.TextBounds.Inflate(2))
                || candidate.Lines.Any(line => IntersectsText(line, other.TextBounds))
                || other.Lines.Any(line => IntersectsText(line, candidate.TextBounds))
                || (!allowLeaderCrossings && (candidate.Key == "r" || other.Key == "r") && ConflictingLines(candidate, other))
                || candidate.Arrows.Any(arrow => arrow.Bounds.Intersects(other.TextBounds))
                || other.Arrows.Any(arrow => arrow.Bounds.Intersects(candidate.TextBounds)));
        }
        // Resolve the fixed web and its straight radius line before the flange's
        // flexible outer annotation, so impossible radius positions are tried once.
        var details = geometry.Dimensions.Values.Where(IsDetail).OrderBy(d => IsFixedWebMeasure("tw")
            ? d.Key switch { "tw" => 0, "r" => 1, _ => 2 } : d.Key == "r" ? 1 : 0).ToArray();
        var candidates = details.Select(d => Candidates(d)
            .DistinctBy(candidate => (candidate.TextBounds, candidate.Lines[0], candidate.Lines[^1])).ToArray()).ToArray();
        var sourceAxes = layout.Axes;
        var axisSizes = sourceAxes.ToDictionary(a => a.Axis, a => measure(a.Axis.ToString().ToLowerInvariant()));
        bool PlaceAxisLabels()
        {
            var placed = new List<PreviewDrawingAxis>();
            bool Next(int index)
            {
                if (index == sourceAxes.Count) { layout.Axes = placed.ToArray(); return true; }
                var axis = sourceAxes[index];
                Vector direction = axis.End - axis.Start; direction /= direction.Length;
                var normal = new Vector(-direction.Y, direction.X);
                Size labelSize = axisSizes[axis.Axis];
                foreach (double distance in new[] { 10d, 26d, 42d })
                foreach (double offset in new[] { 0d, 20d, -20d, 40d, -40d, 60d, -60d })
                {
                    Point center = axis.End + direction * distance + normal * offset;
                    var label = FitText(new Rect(center - new Vector(labelSize.Width / 2, labelSize.Height / 2), labelSize), size);
                    if (bounds.Intersects(label) || placed.Any(a => a.LabelBounds.Inflate(4).Intersects(label))
                        || dimensions.Any(d => d.TextBounds.Inflate(4).Intersects(label)
                            || d.Lines.Any(line => IntersectsText(line, label))
                            || d.Arrows.Any(arrow => arrow.Bounds.Intersects(label)))) continue;
                    placed.Add(axis with { LabelBounds = label });
                    if (Next(index + 1)) return true;
                    placed.RemoveAt(placed.Count - 1);
                }
                return false;
            }
            return Next(0);
        }
        bool Place(int index)
        {
            if (index == details.Length) return PlaceAxisLabels();
            foreach (var candidate in candidates[index].Where(Fits))
            {
                dimensions.Add(candidate);
                if (Place(index + 1)) return true;
                dimensions.RemoveAt(dimensions.Count - 1);
            }
            return false;
        }
        bool placed = Place(0);
        if (!placed)
        {
            // In extreme aspect ratios a leader may have to cross another thin line.
            // Keep text and arrow heads clear instead of falling back to overlapping labels.
            allowLeaderCrossings = true;
            placed = Place(0);
        }
        if (!placed)
        {
            // Keep the direct topology even in viewports too small to fit every annotation.
            foreach (var group in candidates) if (group.Length > 0) dimensions.Add(group[0]);
            PlaceAxisLabels();
        }
        layout.Dimensions = dimensions;
        return layout;
    }
    private static bool IsDetail(PreviewDimension dimension) => dimension.Key is "t" or "tw" or "tf" or "r";
    private static Rect FitText(Rect rect, Size viewport) => new(new Point(
        Math.Clamp(rect.Left, 4, Math.Max(4, viewport.Width - rect.Width - 4)),
        Math.Clamp(rect.Top, 4, Math.Max(4, viewport.Height - rect.Height - 4))), rect.Size);

    private static bool IntersectsText(PreviewDrawingLine line, Rect rect)
    {
        double low = 0, high = 1;
        Vector delta = line.End - line.Start;
        bool Clip(double direction, double distance)
        {
            if (Math.Abs(direction) < 1e-12) return distance >= 0;
            double fraction = distance / direction;
            if (direction < 0) low = Math.Max(low, fraction); else high = Math.Min(high, fraction);
            return low <= high;
        }
        return Clip(-delta.X, line.Start.X - rect.Left) && Clip(delta.X, rect.Right - line.Start.X)
            && Clip(-delta.Y, line.Start.Y - rect.Top) && Clip(delta.Y, rect.Bottom - line.Start.Y) && high > low;
    }

    private static bool LinesCross(PreviewDrawingLine first, PreviewDrawingLine second)
    {
        Vector a = first.End - first.Start, b = second.End - second.Start, gap = second.Start - first.Start;
        double Cross(Vector p, Vector q) => p.X * q.Y - p.Y * q.X;
        double determinant = Cross(a, b);
        if (Math.Abs(determinant) < 1e-10) return false;
        double t = Cross(gap, b) / determinant, u = Cross(gap, a) / determinant;
        return t > 1e-8 && t < 1 - 1e-8 && u > 1e-8 && u < 1 - 1e-8;
    }

    private static bool LiesOnAxis(PreviewDrawingLine line, PreviewDrawingAxis axis)
    {
        Vector ray = axis.End - axis.Start, direction = line.End - line.Start;
        if (ray.Length == 0 || direction.Length == 0) return false;
        double length = ray.Length; ray /= length; direction /= direction.Length;
        double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
        if (Math.Abs(Cross(ray, direction)) > .01) return false;
        Vector start = line.Start - axis.Start, end = line.End - axis.Start;
        double projectionStart = start.X * ray.X + start.Y * ray.Y, projectionEnd = end.X * ray.X + end.Y * ray.Y;
        return Math.Max(projectionStart, projectionEnd) >= 0 && Math.Min(projectionStart, projectionEnd) <= length
            && Math.Min(Math.Abs(Cross(start, ray)), Math.Abs(Cross(end, ray))) < 6;
    }

    private static void AddDimension(Point start, Point end, List<PreviewDrawingLine> lines, List<PreviewDrawingArrow> arrows)
    {
        Vector direction = end - start;
        double length = direction.Length;
        if (length <= 0) return;
        direction /= length;
        bool outside = length < 2 * PreviewDrawingArrow.Length + 8;
        double extension = PreviewDrawingArrow.Length + 3;
        lines.Add(new(outside ? start - direction * extension : start, outside ? end + direction * extension : end));
        arrows.Add(new(start, outside ? -direction : direction));
        arrows.Add(new(end, outside ? direction : -direction));
    }
}
