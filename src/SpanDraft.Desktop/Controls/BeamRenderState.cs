using Avalonia;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public sealed record SupportVisual(Guid? Id, SupportPreview Preview, string Name, double X, bool IsPreview);
public sealed record EntityAnnotation(Guid? Id, bool IsSupport, string Text, Rect Bounds, bool IsPreview, bool IsInvalid);

/// <summary>One measured scene shared by drawing and hit testing. No station layout is generated here.</summary>
public sealed record BeamRenderState(BeamLayoutFrame Frame, IReadOnlyList<SupportVisual> Supports,
    IReadOnlyList<PointLoadVisual> Loads, IReadOnlyList<PointLoadGlyph> Glyphs,
    IReadOnlyList<EntityAnnotation> Annotations, double MinimumPaneHeight)
{
    public static BeamRenderState Create(EditorDocument document, BeamLayoutFrame frame, Func<string, Size> measure,
        SupportPreview? supportPreview = null, Guid? hiddenSupportId = null, string? supportName = null,
        PointLoadPreview? loadPreview = null, Guid? hiddenLoadId = null, string? loadName = null)
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
            new(id, preview, name.Trim(), frame.Layout.Transform.PhysicalToScreen(preview.Position.Meters), draft));
        var loads = PointLoadSymbol.Layout(document.Loads, frame, loadPreview, hiddenLoadId, loadName);
        var annotations = new List<EntityAnnotation>();
        double above = Place(loads.Select((v, i) => (v.Id, v.Name, Text: PointLoadSymbol.Label(v.Preview, v.Name),
            v.X, v.IsPreview, v.Preview.IsInvalid, Order: i)), false);
        double below = Place(supports.Select((v, i) => (v.Id, v.Name, Text: v.Name,
            v.X, v.IsPreview, v.Preview.IsInvalid, Order: i)), true);
        return new(frame, supports.AsReadOnly(), loads, PointLoadSymbol.Group(loads), annotations.AsReadOnly(),
            Math.Max(SchematicMetrics.MinimumBeamPaneHeight, 2 * Math.Max(above, below)));

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
                    : frame.Viewport.BeamY - SchematicMetrics.ForceHeight - 8 - item.Size.Height - lane * line;
                annotations.Add(new(item.Value.Id, support, item.Value.Text, new(left, y, item.Size.Width, item.Size.Height),
                    item.Value.IsPreview, item.Value.IsInvalid));
            }
            // Always reserve a preview row. Hover/leave cannot change the beam height.
            return (support ? SchematicMetrics.SupportGroundY : SchematicMetrics.ForceHeight) + 16 +
                (ends.Count + (support ? 0 : 1)) * line;
        }
        double Left(double x, double width) => Math.Clamp(x - width / 2, 8, Math.Max(8, frame.Viewport.Width - width - 8));
    }

    public EntityAnnotation? HitTestLabel(double x, double y) =>
        Annotations.FirstOrDefault(a => a.Bounds.Contains(new Point(x, y)));
}
