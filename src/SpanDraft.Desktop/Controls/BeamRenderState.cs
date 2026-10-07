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
    public IReadOnlyList<DistributedLoadVisual> DistributedLoads { get; init; } = [];
    public double RequiredBelowBeamSpace { get; init; } = SchematicMetrics.BelowBeamSpace;

    public static BeamRenderState Create(EditorDocument document, BeamLayoutFrame frame, Func<string, Size> measure,
        SupportPreview? supportPreview = null, Guid? hiddenSupportId = null, string? supportName = null,
        PointLoadPreview? loadPreview = null, Guid? hiddenLoadId = null, string? loadName = null,
        EditorPresentationState? presentation = null, DistributedLoadPreview? distributedPreview = null,
        Guid? hiddenDistributedId = null, string? distributedName = null)
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
        var distributed = DistributedLoadSymbol.Layout(document.DistributedLoads, frame, distributedPreview, hiddenDistributedId, distributedName);
        var annotations = new List<EntityAnnotation>();
        double above = Place(loads.Select((v, i) => (v.Id, v.Name, Text: PointLoadSymbol.Label(v.Preview, v.Name),
            v.X, v.IsPreview, v.Preview.IsInvalid, Order: i)).Concat(distributed.Select((v, i) =>
            (v.Id, v.Name, Text: DistributedLoadSymbol.Label(v.Preview, v.Name), X: v.CenterX,
                v.IsPreview, v.Preview.IsInvalid, Order: loads.Count + i))), false);
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
        above = Math.Max(above, Math.Max(0, annotations.Select(a => frame.Viewport.BeamY - a.Bounds.Top)
            .DefaultIfEmpty(0).Max()) + SchematicMetrics.EntityLabelPadding);
        below = Math.Max(below, Math.Max(0, annotations.Select(a => a.Bounds.Bottom - frame.Viewport.BeamY)
            .DefaultIfEmpty(0).Max()));
        // Reserve one fixed support row below the beam. Only actual overflow
        // (e.g. manual offsets or overlapping support labels) needs extra space.
        below = Math.Max(below, SchematicMetrics.BelowBeamSpace);
        // Offsets remain unrestricted. Saturate only the requested pane height.
        double visibleHeight = Math.Min(double.MaxValue, above + below);
        return new(frame, supports.AsReadOnly(), loads, PointLoadSymbol.Group(loads), annotations.AsReadOnly(),
            Math.Max(visibleHeight, SchematicMetrics.MinimumBeamPaneHeight))
            { DistributedLoads = distributed, RequiredBelowBeamSpace = below };

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
                    double y = support ? frame.Viewport.BeamY + SchematicMetrics.SupportLabelTopOffset + lane * line
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
            double rowHeight = Math.Max(SchematicMetrics.AxisLabelLineHeight,
                measured.Select(v => v.Size.Height).DefaultIfEmpty(SchematicMetrics.AxisLabelLineHeight).Max());
            double line = rowHeight + SchematicMetrics.EntityLabelPadding;
            var ends = new List<double>();
            foreach (var item in measured.OrderBy(v => Left(v.Value.X, v.Size.Width)).ThenBy(v => v.Value.Order).ThenBy(v => v.Value.Id))
            {
                double left = Left(item.Value.X, item.Size.Width);
                int lane = ends.FindIndex(end => left - end >= SchematicMetrics.EntityLabelPadding);
                if (lane < 0) { lane = ends.Count; ends.Add(left + item.Size.Width); }
                else ends[lane] = left + item.Size.Width;
                double y = support ? frame.Viewport.BeamY + SchematicMetrics.SupportLabelTopOffset + lane * line
                    : frame.Viewport.BeamY - SchematicMetrics.ForceTopOffset - 8 - item.Size.Height - lane * line;
                var bounds = new Rect(left, y, item.Size.Width, item.Size.Height);
                annotations.Add(new(item.Value.Id, support, item.Value.Text, bounds, bounds, item.Value.IsPreview, item.Value.IsInvalid));
            }
            // Only put spacing between support rows; the axis provides the lower
            // clearance. Keep one support row available even before placement.
            if (support)
                return SchematicMetrics.SupportLabelTopOffset + rowHeight + Math.Max(0, ends.Count - 1) * line;
            // Always reserve a load preview row. Hover/leave cannot change the beam height.
            return SchematicMetrics.ForceTopOffset + 16 + (ends.Count + 1) * line;
        }
        double Left(double x, double width) => Math.Clamp(x - width / 2, 8, Math.Max(8, frame.Viewport.Width - width - 8));
    }

    public EntityAnnotation? HitTestLabel(double x, double y) =>
        Annotations.LastOrDefault(a => a.Bounds.Contains(new Point(x, y)));

    public IReadOnlyList<EntityAnnotation> HitTestLabels(double x, double y) =>
        Annotations.Where(a => a.Id is not null && a.Bounds.Contains(new Point(x, y))).ToArray();

    /// <summary>Local scene hits only. Labels take precedence; all different symbol entities are equal candidates.</summary>
    public IReadOnlyList<Guid> HitTestEntities(double x, double y)
    {
        var labels = HitTestLabels(x, y);
        if (labels.Count > 0) return labels.Select(a => a.Id!.Value).Distinct().ToArray();
        return Supports.Where(s => s.Id is not null && SupportSymbol.Contains(s.Preview, Frame.Layout.Transform, Frame.Viewport.BeamY, x, y))
            .Select(s => s.Id!.Value)
            .Concat(Loads.Where(l => l.Id is not null && PointLoadSymbol.Contains(l, x, y)).Select(l => l.Id!.Value))
            .Concat(DistributedLoads.Where(l => l.Id is not null && DistributedLoadSymbol.Contains(l, x, y)).Select(l => l.Id!.Value))
            .Distinct().ToArray();
    }
}
