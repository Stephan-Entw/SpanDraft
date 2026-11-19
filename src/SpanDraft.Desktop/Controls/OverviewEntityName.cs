using Avalonia;

namespace SpanDraft.Desktop.Controls;

public sealed class OverviewEntityName : TechnicalTextBlock
{
    public static readonly DirectProperty<OverviewEntityName, string?> NameToolTipProperty =
        AvaloniaProperty.RegisterDirect<OverviewEntityName, string?>(nameof(NameToolTip), name => name.NameToolTip);

    private string? _nameToolTip;
    public string? NameToolTip => _nameToolTip;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        SetAndRaise(NameToolTipProperty, ref _nameToolTip, TextLayout.TextLines.Any(line => line.HasCollapsed) ? Text : null);
        return size;
    }
}
