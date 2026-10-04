using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Controls;

/// <summary>One horizontal pointer gesture. Screen positions are transient only.</summary>
public sealed class SupportDragGesture(Guid supportId, Length originalPosition, double pressX)
{
    public const double Threshold = 4;
    public Guid SupportId { get; } = supportId;
    public bool IsDragging { get; private set; }

    public Length? Update(BeamViewport viewport, double x, double y, double width, double height)
    {
        if (Math.Abs(x - pressX) >= Threshold) IsDragging = true;
        if (!IsDragging || !double.IsFinite(x) || !double.IsFinite(y) || y < 0 || y > height) return null;
        // Keep the original grab offset; never jump the symbol to the pointer on press.
        double effectiveX = viewport.BeamToScreen(originalPosition.Meters) + x - pressX;
        return SupportSnap.AtX(viewport, Math.Clamp(effectiveX, viewport.Left, viewport.Right));
    }
}
