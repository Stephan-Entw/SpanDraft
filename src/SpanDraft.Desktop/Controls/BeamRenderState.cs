using Avalonia;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public sealed record SupportVisual(Guid? Id, SupportPreview Preview, string Name, double X, bool IsPreview, bool IsMirrored);
/// <summary>AutoBounds is the offset-independent reference; Bounds is the final visible rectangle.</summary>
public sealed record EntityAnnotation(Guid? Id, bool IsSupport, string Text, Rect AutoBounds, Rect Bounds,
    bool IsPreview, bool IsInvalid, bool IsManual = false);

/// <summary>One measured scene shared by drawing and hit testing. No station layout is generated here.</summary>
public sealed record BeamRenderState(BeamLayoutFrame Frame, IReadOnlyList<SupportVisual> Supports,
    IReadOnlyList<PointLoadVisual> Loads, IReadOnlyList<PointLoadGlyph> Glyphs,
    IReadOnlyList<EntityAnnotation> Annotations, double MinimumPaneHeight)
{
    public static BeamRenderState Create(EditorDocument document, BeamLayoutFrame frame, Func<string, Size> measure,
        SupportPreview? supportPreview = null, Guid? hiddenSupportId = null, string? supportName = null,
        PointLoadPreview? loadPreview = null, Guid? hiddenLoadId = null, string? loadName = null,
        EditorPresentationState? presentation = null)
    {
        var supports = new List<SupportVisual>();
        foreach (var support in document.Supports)
        {
            bool draft = support.Id == hiddenSupportId && supportPreview is not null;
            AddSupport(support.Id, draft ? supportPreview! : new(support.Position, support.Type),
                draft ? supportName ?? support.Name : support.Name, draft);
        }
        if (supportPreview is not null && !document.Supports.Any(s => s.Id == hiddenSupportId))
            AddSupport(null, supportPreview, supportName ?? "", true);
        void AddSupport(Guid? id, SupportPreview preview, string name, bool draft) => supports.Add(
            new(id, preview, name.Trim(), frame.Layout.Transform.PhysicalToScreen(preview.Position.Meters), draft,
                preview.Type == SupportType.Fixed && FixedSupportGeometry.AtPosition(preview.Position.Meters, document.Length.Meters).IsMirrored));
        var loads = PointLoadSymbol.Layout(document.Loads, frame, loadPreview, hiddenLoadId, loadName);
        var annotations = new List<EntityAnnotation>();
        double above = Place(loads.Select((v, i) => (v.Id, v.Name, Text: PointLoadSymbol.Label(v.Preview, v.Name),
            v.X, v.IsPreview, v.Preview.IsInvalid, Order: i)), false);
        double below = Place(supports.Select((v, i) => (v.Id, v.Name, Text: v.Name,
            v.X, v.IsPreview, v.Preview.IsInvalid, Order: i)), true);
        // Establish every reference anchor before considering any offsets. A manual
        // label can never move its own anchor through the obstacle pass below.
        for (int i = 0; i < annotations.Count; i++)
            if (annotations[i].Id is { } id && presentation?.AnnotationOffsets.TryGetValue(id, out var offset) == true)
                annotations[i] = annotations[i] with
                {
                    Bounds = annotations[i].AutoBounds.Translate(new Vector(offset.Dx, offset.Dy)), IsManual = true
                };
        var manual = annotations.Where(a => a.IsManual).Select(a => a.Bounds).ToArray();
        if (manual.Length > 0)
        {
            AvoidManual(false);
            AvoidManual(true);
        }
        double extent = annotations.Select(a => Math.Max(Math.Abs(a.Bounds.Top - frame.Viewport.BeamY),
            Math.Abs(a.Bounds.Bottom - frame.Viewport.BeamY))).DefaultIfEmpty(0).Max() + SchematicMetrics.EntityLabelPadding;
        // Offsets remain unrestricted. Saturate only the requested pane height in
        // numerically extreme cases, never a label's position or stored offset.
        double visibleHeight = 2 * Math.Min(double.MaxValue / 2, extent);
        return new(frame, supports.AsReadOnly(), loads, PointLoadSymbol.Group(loads), annotations.AsReadOnly(),
            Math.Max(visibleHeight, Math.Max(SchematicMetrics.MinimumBeamPaneHeight, 2 * Math.Max(above, below))));

        void AvoidManual(bool support)
        {
            double line = annotations.Where(a => a.IsSupport == support).Select(a => a.AutoBounds.Height)
                .DefaultIfEmpty(SchematicMetrics.AxisLabelLineHeight).Max() + SchematicMetrics.EntityLabelPadding;
            var placed = new List<Rect>();
            for (int i = 0; i < annotations.Count; i++)
            {
                var label = annotations[i];
                if (label.IsSupport != support || label.IsManual) continue;
                int lane = 0;
                Rect candidate;
                do
                {
                    double y = support ? frame.Viewport.BeamY + SchematicMetrics.SupportGroundY + 8 + lane * line
                        : frame.Viewport.BeamY - SchematicMetrics.ForceTopOffset - 8 - label.AutoBounds.Height - lane * line;
                    candidate = new(label.AutoBounds.X, y, label.AutoBounds.Width, label.AutoBounds.Height);
                    lane++;
                }
                while (manual.Concat(placed).Any(bounds => candidate.Inflate(SchematicMetrics.EntityLabelPadding).Intersects(bounds)));
                annotations[i] = label with { Bounds = candidate };
                placed.Add(candidate);
            }
        }

        double Place(IEnumerable<(Guid? Id, string Name, string Text, double X, bool IsPreview, bool IsInvalid, int Order)> source, bool support)
        {
            var measured = source.Select(v => (Value: v, Size: measure(v.Text))).ToArray();
            double line = Math.Max(SchematicMetrics.AxisLabelLineHeight, measured.Select(v => v.Size.Height).DefaultIfEmpty(16).Max())
                + SchematicMetrics.EntityLabelPadding;
            var ends = new List<double>();
            foreach (var item in measured.OrderBy(v => Left(v.Value.X, v.Size.Width)).ThenBy(v => v.Value.Order).ThenBy(v => v.Value.Id))
            {
                double left = Left(item.Value.X, item.Size.Width);
                int lane = ends.FindIndex(end => left - end >= SchematicMetrics.EntityLabelPadding);
                if (lane < 0) { lane = ends.Count; ends.Add(left + item.Size.Width); }
                else ends[lane] = left + item.Size.Width;
                double y = support ? frame.Viewport.BeamY + SchematicMetrics.SupportGroundY + 8 + lane * line
                    : frame.Viewport.BeamY - SchematicMetrics.ForceTopOffset - 8 - item.Size.Height - lane * line;
                var bounds = new Rect(left, y, item.Size.Width, item.Size.Height);
                annotations.Add(new(item.Value.Id, support, item.Value.Text, bounds, bounds, item.Value.IsPreview, item.Value.IsInvalid));
            }
            // Always reserve a preview row. Hover/leave cannot change the beam height.
            return (support ? SchematicMetrics.SupportGroundY : SchematicMetrics.ForceTopOffset) + 16 +
                (ends.Count + (support ? 0 : 1)) * line;
        }
        double Left(double x, double width) => Math.Clamp(x - width / 2, 8, Math.Max(8, frame.Viewport.Width - width - 8));
    }

    public EntityAnnotation? HitTestLabel(double x, double y) =>
        Annotations.LastOrDefault(a => a.Bounds.Contains(new Point(x, y)));
}
