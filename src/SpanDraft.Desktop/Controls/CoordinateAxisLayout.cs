using Avalonia;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Controls;

public sealed record AxisStationLabel(double PhysicalX, string Text, Rect Bounds, int Lane, AxisEndpointRole Role);
public sealed record CoordinateAxisLayout(StationLayoutResult StationLayout, IReadOnlyList<AxisStationLabel> Labels,
    AxisLabelPackingResult Packing, double LineHeight, double PaneHeight)
{
    public string UnitSymbol { get; init; } = "mm";
    public const double AxisY = 8;
    public const double ErrorReserve = 40;
    public static CoordinateAxisLayout Create(StationLayoutResult layout, double paneWidth,
        Func<string, Size> measure, bool editing = false, UnitDefinition? unit = null)
    {
        unit ??= UnitCatalog.Millimeter;
        var text = layout.Stations.Select(s => InputQuantityFormatter.Display(s.PhysicalX, unit)).ToArray();
        var sizes = text.Select(measure).ToArray();
        double lineHeight = Math.Max(SchematicMetrics.AxisLabelLineHeight, sizes.Max(s => s.Height));
        if (editing) lineHeight = Math.Max(lineHeight, SchematicMetrics.LengthInputHeight);
        var requirements = layout.Stations.Select((s, i) => new AxisLabelRequirement(s.ScreenX,
            editing && i == text.Length - 1 ? SchematicMetrics.LengthInputWidth + 8 + measure(unit.Symbol).Width : sizes[i].Width,
            i, i == 0 ? AxisEndpointRole.Start : i == text.Length - 1 ? AxisEndpointRole.End : AxisEndpointRole.Interior));
        var packing = AxisLabelPacker.Pack(requirements, 0, paneWidth, lineHeight: lineHeight);
        var labels = packing.Labels.Select(p =>
        {
            int i = checked((int)p.Requirement.StableOrderKey);
            return new AxisStationLabel(layout.Stations[i].PhysicalX, text[i],
                new(p.Left, SchematicMetrics.AxisBaseHeight + p.Lane * lineHeight,
                    p.Right - p.Left, lineHeight), p.Lane, p.Requirement.EndpointRole);
        }).ToArray();
        double height = packing.PaneHeight + (layout.IsDistorted ? lineHeight + 4 : 0) + (editing ? ErrorReserve : 0);
        return new(layout, Array.AsReadOnly(labels), packing, lineHeight, height) { UnitSymbol = unit.Symbol };
    }
}
