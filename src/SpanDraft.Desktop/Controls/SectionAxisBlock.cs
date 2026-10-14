using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>One selection target containing either read-only properties or editable manual inputs.</summary>
public sealed class SectionAxisBlock : Border
{
    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<SectionAxisBlock, bool>(nameof(IsSelected));
    public static readonly StyledProperty<bool> IsSelectableProperty = AvaloniaProperty.Register<SectionAxisBlock, bool>(nameof(IsSelectable));
    public static readonly RoutedEvent<RoutedEventArgs> SelectedEvent = RoutedEvent.Register<SectionAxisBlock, RoutedEventArgs>(nameof(Selected), RoutingStrategies.Bubble);
    private Point? _pressed;
    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsSelectable { get => GetValue(IsSelectableProperty); set => SetValue(IsSelectableProperty, value); }
    public event EventHandler<RoutedEventArgs> Selected { add => AddHandler(SelectedEvent, value); remove => RemoveHandler(SelectedEvent, value); }
    public SectionAxisBlock()
    {
        Padding = new Thickness(11, 16); Margin = new Thickness(0, 0, 0, 8); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e.GetPosition(this) : null;
        }, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_pressed is { } start && e.InitialPressMouseButton == MouseButton.Left
                && new Rect(Bounds.Size).Contains(e.GetPosition(this)) && ((Vector)(e.GetPosition(this) - start)).Length < 6) Select();
            _pressed = null;
        }, handledEventsToo: true);
    }
    public void Select()
    {
        if (IsSelectable && IsEffectivelyEnabled) RaiseEvent(new RoutedEventArgs(SelectedEvent));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && ReferenceEquals(e.Source, this) && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Space && IsSelectable)
        { e.Handled = true; Select(); }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty || change.Property == IsSelectableProperty)
        {
            Focusable = IsSelectable;
            Cursor = IsSelectable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            BorderBrush = IsSelected ? SubtleAccent() : Brushes.Transparent;
            Background = IsSelected ? FindBrush("AccentSubtle", Brushes.Transparent) : Brushes.Transparent;
        }
    }
    private IBrush SubtleAccent() => FindBrush("Accent", Brushes.Gray) is ISolidColorBrush brush
        ? new SolidColorBrush(brush.Color, .3) : Brushes.Gray;
    private IBrush FindBrush(string key, IBrush fallback) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;
    protected override AutomationPeer OnCreateAutomationPeer() => new AxisBlockPeer(this);
    private sealed class AxisBlockPeer(SectionAxisBlock owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => owner.IsSelectable ? AutomationControlType.Button : AutomationControlType.Group;
        protected override object? GetProviderCore(Type providerType) => providerType == typeof(IInvokeProvider) && owner.IsSelectable ? this : base.GetProviderCore(providerType);
        public void Invoke() => owner.Select();
    }
}
