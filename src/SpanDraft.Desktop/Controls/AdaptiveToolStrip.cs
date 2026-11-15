using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace SpanDraft.Desktop.Controls;

/// <summary>
/// A horizontal tool row that drops its captions in favour of group labels (the <c>:compact</c> pseudo-class)
/// as soon as the captioned row no longer fits the width it is given.
/// </summary>
public sealed class AdaptiveToolStrip : StackPanel
{
    // Width of the captioned row, measured the last time it was shown; it decides when the captions may return.
    private double captionedWidth = double.NaN;

    public bool IsCompact { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        double room = availableSize.Width;
        if (IsCompact && captionedWidth <= room) SetCompact(false);
        if (!IsCompact)
        {
            var captioned = base.MeasureOverride(availableSize);
            captionedWidth = captioned.Width;
            if (captioned.Width <= room) return captioned;
            SetCompact(true);
        }
        return base.MeasureOverride(availableSize);
    }

    private void SetCompact(bool compact)
    {
        IsCompact = compact;
        PseudoClasses.Set(":compact", compact);
        // The caption styles change below this strip while it is being measured: the tools measured so far are stale.
        foreach (var descendant in this.GetVisualDescendants().OfType<Layoutable>()) descendant.InvalidateMeasure();
    }
}
