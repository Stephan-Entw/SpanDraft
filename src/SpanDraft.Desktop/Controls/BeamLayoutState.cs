using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public enum BeamPointerInteraction { SupportPlacement, LoadPlacement, SupportDrag, LoadDrag, LabelDrag, DistributedLoadPlacement, DistributedLoadDrag }
public sealed record BeamLayoutFrame(StationLayoutResult Layout, BeamViewport Viewport);

/// <summary>Surface-owned transient layout cache, never a second committed document.</summary>
public sealed class BeamLayoutState
{
    private EditorDocument? _document;
    private double _width;
    private StationOuterMargins? _outerMargins;
    private double _supportSymbolScale = 1;
    private double _loadSymbolScale = 1;
    private BeamLayoutFrame? _committed;
    public BeamLayoutFrame? Snapshot { get; private set; }
    public BeamPointerInteraction? Interaction { get; private set; }
    public BeamLayoutFrame? Current => Snapshot ?? _committed;

    public BeamLayoutFrame? Update(EditorDocument document, double width, double height,
        double belowBeamSpace = SchematicMetrics.BelowBeamSpace, StationOuterMargins? outerMargins = null,
        double supportSymbolScale = 1, double loadSymbolScale = 1)
    {
        if (Snapshot is not null) return Snapshot;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return null;
        if (!ReferenceEquals(document, _document) || width != _width || outerMargins != _outerMargins
            || supportSymbolScale != _supportSymbolScale || loadSymbolScale != _loadSymbolScale || _committed is null)
        {
            var requirements = StationRequirementBuilder.FromDocument(document, supportSymbolScale, loadSymbolScale);
            var margins = StationOuterMargins.Calculate(requirements[0], requirements[^1]);
            if (outerMargins is { } requested)
            {
                var minimum = StationOuterMargins.Calculate(requirements[0], requirements[^1], baseSideMargin: 0);
                margins = new(Math.Max(minimum.Left, LayoutNumbers.NonNegative(requested.Left, nameof(outerMargins))),
                    Math.Max(minimum.Right, LayoutNumbers.NonNegative(requested.Right, nameof(outerMargins))));
            }
            // Initial/narrow arrangement still has positive end anchors. StationLayout
            // handles density through its existing proportional best-effort fallback.
            double factor = Math.Min(1, Math.Max(0, width - Math.Min(1, width / 2)) / (margins.Left + margins.Right));
            double left = margins.Left * factor, right = width - margins.Right * factor;
            var layout = StationLayout.Compute(document.Length.Meters, left, right, requirements,
                spanRequirements: StationRequirementBuilder.SpansFromDocument(document));
            _committed = new(layout, BeamViewport.Fit(width, height, belowBeamSpace));
            _document = document;
            _width = width;
            _outerMargins = outerMargins;
            _supportSymbolScale = supportSymbolScale;
            _loadSymbolScale = loadSymbolScale;
        }
        else if (_committed.Viewport.Height != height || _committed.Viewport.BelowBeamSpace != belowBeamSpace)
            _committed = _committed with { Viewport = BeamViewport.Fit(width, height, belowBeamSpace) };
        return _committed;
    }

    public BeamLayoutFrame? BeginInteraction(BeamPointerInteraction interaction)
    {
        Snapshot ??= _committed;
        Interaction = Snapshot is null ? null : interaction;
        return Snapshot;
    }

    public void EndInteraction() { Snapshot = null; Interaction = null; }
}
