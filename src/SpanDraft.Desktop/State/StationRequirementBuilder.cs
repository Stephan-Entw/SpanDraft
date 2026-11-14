using SpanDraft.Desktop.Controls;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.State;

/// <summary>The Entity-aware boundary; only committed glyph extents enter the pure layout.</summary>
public static class StationRequirementBuilder
{
    public static IReadOnlyList<StationRequirement> FromDocument(EditorDocument document, double supportSymbolScale = 1, double loadSymbolScale = 1)
    {
        LayoutNumbers.Positive(supportSymbolScale, nameof(supportSymbolScale));
        LayoutNumbers.Positive(loadSymbolScale, nameof(loadSymbolScale));
        const double stroke = SchematicMetrics.SymbolStrokeWidth / 2;
        var requirements = new List<StationRequirement> { new(0, 0, 0), new(document.Length.Meters, 0, 0) };
        // A segment boundary is a station without extent, so that the layout keeps it as a distinct position.
        foreach (var boundary in document.SegmentBoundaries)
            requirements.Add(new(boundary.Meters, 0, 0));
        foreach (var support in document.Supports)
        {
            if (support.Type == SupportType.Fixed)
            {
                var geometry = FixedSupportGeometry.AtPosition(support.Position.Meters, document.Length.Meters);
                requirements.Add(new(support.Position.Meters, geometry.LeftExtent * supportSymbolScale, geometry.RightExtent * supportSymbolScale));
                continue;
            }
            var (left, right) = Extents(support);
            requirements.Add(new(support.Position.Meters, left * supportSymbolScale, right * supportSymbolScale));
        }
        foreach (var release in document.MomentReleases)
        {
            // The ring sits on the beam axis; it is a station of its own so that the results can show the rotation jump there.
            double extent = (SchematicMetrics.MomentReleaseRadius + stroke) * supportSymbolScale;
            requirements.Add(new(release.Position.Meters, extent, extent));
        }
        foreach (var load in document.Loads)
        {
            double extent = load.Kind switch
            {
                PointLoadKind.Force => load.Value == 0 ? 0 : SchematicMetrics.ForceArrowHalfWidth,
                PointLoadKind.Moment => SchematicMetrics.PointLoadHalfSize,
                _ => throw new ArgumentOutOfRangeException(nameof(document))
            };
            requirements.Add(new(load.Position.Meters, (extent + stroke) * loadSymbolScale, (extent + stroke) * loadSymbolScale));
        }
        foreach (var load in document.DistributedLoads)
        {
            // Each end reserves the arrow head width only where it carries an intensity; a triangle's zero end has no head.
            double Extent(double intensity) => ((intensity == 0 ? 0 : SchematicMetrics.DistributedArrowHeadHalfWidth) + stroke) * loadSymbolScale;
            double start = Extent(load.StartIntensity.NewtonsPerMeter), end = Extent(load.EndIntensity.NewtonsPerMeter);
            requirements.Add(new(load.StartPosition.Meters, start, start));
            requirements.Add(new(load.EndPosition.Meters, end, end));
        }
        foreach (var weight in document.SelfWeightLoads)
        {
            double extent = (SchematicMetrics.DistributedArrowHeadHalfWidth + stroke) * loadSymbolScale;
            requirements.Add(new(weight.StartPosition.Meters, extent, extent));
            requirements.Add(new(weight.EndPosition.Meters, extent, extent));
        }
        return Array.AsReadOnly(requirements.GroupBy(r => r.PhysicalX).OrderBy(g => g.Key)
            .Select(g => new StationRequirement(g.Key, g.Max(r => r.LeftExtent), g.Max(r => r.RightExtent))).ToArray());
    }

    public static IReadOnlyList<SpanRequirement> SpansFromDocument(EditorDocument document) =>
        Array.AsReadOnly(document.DistributedLoads.Select(l => new SpanRequirement(l.StartPosition.Meters,
            l.EndPosition.Meters, SchematicMetrics.DistributedMinimumWidth))
            .Concat(document.SelfWeightLoads.Select(weight => new SpanRequirement(weight.StartPosition.Meters,
                weight.EndPosition.Meters, SchematicMetrics.DistributedMinimumWidth))).ToArray());

    private static (double Left, double Right) Extents(EditorSupport support) => support.Type switch
    {
        SupportType.Pinned => (new HingedSupportGeometry(false).LeftExtent, new HingedSupportGeometry(false).RightExtent),
        SupportType.Roller => (new HingedSupportGeometry(true).LeftExtent, new HingedSupportGeometry(true).RightExtent),
        SupportType.Spring when support.Spring is { } spring =>
            (SupportSymbol.SpringGeometry(spring).LeftExtent, SupportSymbol.SpringGeometry(spring).RightExtent),
        _ => throw new ArgumentOutOfRangeException(nameof(support))
    };
}
