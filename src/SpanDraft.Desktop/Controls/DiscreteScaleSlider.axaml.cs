using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace SpanDraft.Desktop.Controls;

public partial class DiscreteScaleSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, int>(nameof(Minimum));
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, int>(nameof(Maximum), 4);
    public static readonly StyledProperty<int> StepProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, int>(nameof(Step), 1, validate: step => step > 0);
    public static readonly StyledProperty<string?> StartLabelProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, string?>(nameof(StartLabel));
    public static readonly StyledProperty<string?> EndLabelProperty =
        AvaloniaProperty.Register<DiscreteScaleSlider, string?>(nameof(EndLabel));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string? StartLabel { get => GetValue(StartLabelProperty); set => SetValue(StartLabelProperty, value); }
    public string? EndLabel { get => GetValue(EndLabelProperty); set => SetValue(EndLabelProperty, value); }

    private readonly StationPanel _stations;

    public DiscreteScaleSlider()
    {
        InitializeComponent();
        _stations = new StationPanel(ScaleSlider) { UseLayoutRounding = false };
        Stations.Child = _stations;
        UpdateStations();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_stations is null)
            return;

        if (change.Property == MinimumProperty || change.Property == MaximumProperty || change.Property == StepProperty)
            UpdateStations();
        else if (change.Property == ValueProperty)
            UpdateActiveStation();
    }

    private void UpdateStations()
    {
        _stations.Children.Clear();
        for (long value = Minimum; value < Maximum; value += Step)
            AddStation((int)value);
        AddStation(Maximum);
        UpdateActiveStation();

        void AddStation(int value) => _stations.Children.Add(new TextBlock
        {
            Text = value.ToString(CultureInfo.CurrentCulture),
            Classes = { "caption", "station" }
        });
    }

    private void UpdateActiveStation()
    {
        for (int i = 0; i < _stations.Children.Count; i++)
        {
            long value = i == _stations.Children.Count - 1 ? Maximum : Minimum + (long)i * Step;
            _stations.Children[i].Classes.Set("active", value == Value);
        }
    }

    private sealed class StationPanel(Slider slider) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var label in Children)
                label.Measure(Size.Infinity);
            return new Size(0, Children.Max(label => label.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var (start, length) = TickSpan();
            for (int i = 0; i < Children.Count; i++)
            {
                var label = Children[i];
                double value = i == Children.Count - 1 ? slider.Maximum : slider.Minimum + i * slider.TickFrequency;
                double fraction = slider.Maximum > slider.Minimum ? (value - slider.Minimum) / (slider.Maximum - slider.Minimum) : 0.5;
                var size = label.DesiredSize;
                label.Arrange(new Rect(start + length * fraction - size.Width / 2, 0, size.Width, finalSize.Height));
            }
            return finalSize;
        }

        private (double Start, double Length) TickSpan()
        {
            // The stations and slider share the same horizontal origin.
            var tickBar = slider.GetVisualDescendants().OfType<TickBar>()
                .FirstOrDefault(bar => bar.Placement == TickBarPlacement.Bottom && bar.IsVisible);
            if (tickBar?.TranslatePoint(default, slider) is { } tickOrigin && tickBar.Bounds.Width > tickBar.ReservedSpace.Width)
                return (tickOrigin.X + tickBar.ReservedSpace.Width / 2, tickBar.Bounds.Width - tickBar.ReservedSpace.Width);

            var track = slider.GetVisualDescendants().OfType<Track>().FirstOrDefault();
            if (track?.Thumb is { } thumb && track.TranslatePoint(default, slider) is { } trackOrigin)
                return (trackOrigin.X + thumb.Bounds.Width / 2, track.Bounds.Width - thumb.Bounds.Width);

            return (0, slider.Bounds.Width);
        }
    }
}
