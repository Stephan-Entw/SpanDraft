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
        if (!IsDragging || x < 0 || x > width || y < 0 || y > height) return null;
        // Keep the original grab offset; never jump the symbol to the pointer on press.
        return SupportSnap.AtX(viewport, viewport.BeamToScreen(originalPosition.Meters) + x - pressX);
    }
}
