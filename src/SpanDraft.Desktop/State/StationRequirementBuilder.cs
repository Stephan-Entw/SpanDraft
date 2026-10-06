using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.State;

/// <summary>The Entity-aware boundary; only committed glyph extents enter the pure layout.</summary>
public static class StationRequirementBuilder
{
    public static IReadOnlyList<StationRequirement> FromDocument(EditorDocument document)
    {
        const double stroke = SchematicMetrics.SymbolStrokeWidth / 2;
        var requirements = new List<StationRequirement> { new(0, 0, 0), new(document.Length.Meters, 0, 0) };
        foreach (var support in document.Supports)
        {
            if (support.Type == SupportType.Fixed)
            {
                var geometry = FixedSupportGeometry.AtPosition(support.Position.Meters, document.Length.Meters);
                requirements.Add(new(support.Position.Meters, geometry.LeftExtent, geometry.RightExtent));
                continue;
            }
            var extents = support.Type switch
            {
                SupportType.Pinned => (SchematicMetrics.SupportHalfWidth, SchematicMetrics.PinnedHatchRightExtent),
                SupportType.Roller => (SchematicMetrics.SupportHalfWidth, SchematicMetrics.SupportHalfWidth),
                _ => throw new ArgumentOutOfRangeException(nameof(document))
            };
            requirements.Add(new(support.Position.Meters, extents.Item1 + stroke, extents.Item2 + stroke));
        }
        foreach (var load in document.Loads)
        {
            double extent = load.Kind switch
            {
                PointLoadKind.Force => load.Value == 0 ? 0 : SchematicMetrics.ForceArrowHalfWidth,
                PointLoadKind.Moment => SchematicMetrics.PointLoadHalfSize,
                _ => throw new ArgumentOutOfRangeException(nameof(document))
            };
            requirements.Add(new(load.Position.Meters, extent + stroke, extent + stroke));
        }
        return Array.AsReadOnly(requirements.GroupBy(r => r.PhysicalX).OrderBy(g => g.Key)
            .Select(g => new StationRequirement(g.Key, g.Max(r => r.LeftExtent), g.Max(r => r.RightExtent))).ToArray());
    }
}
