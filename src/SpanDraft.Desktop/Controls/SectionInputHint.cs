using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>A field tooltip available through pointer hover and keyboard focus.</summary>
public sealed class SectionInputHint : Button
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<SectionInputHint, string?>(nameof(Message));
    private readonly TextBlock _tooltipText = new() { MaxWidth = 280, TextWrapping = TextWrapping.Wrap };
    protected override Type StyleKeyOverride => typeof(Button);
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public SectionInputHint()
    {
        ToolTip.SetTip(this, _tooltipText);
        ToolTip.SetShowDelay(this, 200);
        ToolTip.SetShouldUseOverlayLayer(this, true);
        GotFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(Message)) ToolTip.SetIsOpen(this, true);
        };
        LostFocus += (_, _) => ToolTip.SetIsOpen(this, false);
        DetachedFromVisualTree += (_, _) => CloseHint();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MessageProperty)
        {
            _tooltipText.Text = Message;
            AutomationProperties.SetName(this, Message);
            if (string.IsNullOrWhiteSpace(Message)) CloseHint();
        }
        if (change.Property == IsVisibleProperty && !IsVisible) CloseHint();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (!string.IsNullOrWhiteSpace(Message)) ToolTip.SetIsOpen(this, true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ToolTip.GetIsOpen(this))
        {
            CloseHint();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void CloseHint()
    {
        ToolTip.SetIsOpen(this, false);
    }
}
