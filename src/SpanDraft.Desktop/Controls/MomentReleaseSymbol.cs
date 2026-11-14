using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>A moment release as drawn: an open ring on the beam axis at screen x.</summary>
public sealed record MomentReleaseVisual(Guid? Id, MomentReleasePreview Preview, string Name, double X, bool IsPreview);

/// <summary>Shared fixed-DIP geometry, layout and hit testing of the moment release ring.</summary>
public static class MomentReleaseSymbol
{
    public static IReadOnlyList<MomentReleaseVisual> Layout(IEnumerable<EditorMomentRelease> releases, BeamLayoutFrame frame,
        MomentReleasePreview? preview, Guid? hiddenId, string? previewName)
    {
        var visuals = new List<MomentReleaseVisual>();
        foreach (var release in releases)
        {
            bool draft = release.Id == hiddenId && preview is not null;
            visuals.Add(Create(release.Id, draft ? preview! : new(release.Position), draft ? previewName ?? release.Name : release.Name,
                frame, draft));
        }
        if (preview is not null && !releases.Any(r => r.Id == hiddenId))
            visuals.Add(Create(null, preview, previewName ?? "", frame, true));
        return visuals;
    }

    private static MomentReleaseVisual Create(Guid? id, MomentReleasePreview preview, string name, BeamLayoutFrame frame, bool isPreview) =>
        new(id, preview, name.Trim(), frame.Layout.Transform.PhysicalToScreen(preview.Position.Meters), isPreview);

    /// <summary>True when the point lies within the ring plus its hit padding; the ring is small, so a circle suffices.</summary>
    public static bool Contains(MomentReleaseVisual visual, double beamY, double x, double y, double padding = SchematicMetrics.MomentReleaseHitPadding)
    {
        double dx = x - visual.X, dy = y - beamY, radius = SchematicMetrics.MomentReleaseRadius + padding;
        return dx * dx + dy * dy <= radius * radius;
    }
}
