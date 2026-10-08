using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Controls;

public partial class CoordinateAxisPane : UserControl
{
    private EditorViewModel? _editor;
    private double _paneWidth;
    public StationLayoutResult? StationLayout { get; private set; }
    public CoordinateAxisLayout? AxisLayout { get; private set; }
    public event Action? LengthEditStarting;

    public CoordinateAxisPane()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveEditor();
        AttachedToVisualTree += (_, _) => ObserveEditor();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_editor is not null) _editor.DimensionLength.PropertyChanged -= LengthChanged;
            _editor = null;
        };
    }

    private void ObserveEditor()
    {
        if (_editor == DataContext) return;
        if (_editor is not null) _editor.DimensionLength.PropertyChanged -= LengthChanged;
        _editor = DataContext as EditorViewModel;
        if (_editor is not null) _editor.DimensionLength.PropertyChanged += LengthChanged;
        Repack();
    }

    public void SetStationLayout(StationLayoutResult layout, double width)
    {
        if (ReferenceEquals(StationLayout, layout) && _paneWidth == width) return;
        StationLayout = layout;
        _paneWidth = width;
        Repack();
    }

    private void LengthChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Buffer changes are intentionally absent: editing reserves fixed bounds once.
        if (e.PropertyName is nameof(LengthInputViewModel.IsEditing) or nameof(LengthInputViewModel.Unit)) Repack();
    }

    private void Repack()
    {
        if (StationLayout is null || _paneWidth <= 0) return;
        Size Measure(string text) => SchematicText.Measure(text, Typeface.Default, FontSize);
        bool editing = _editor?.DimensionLength.IsEditing == true;
        AxisLayout = CoordinateAxisLayout.Create(StationLayout, _paneWidth, Measure, editing,
            _editor?.ResultPresentation.Profile[QuantityKind.BeamLength]);
        AxisCanvas.AxisLayout = AxisLayout;
        AxisCanvas.LabelFontSize = FontSize;
        // Reserve for all supported input units, so changing label text never moves the beam.
        Height = UnitCatalog.All.Where(u => u.Dimension == UnitDimension.Length)
            .Max(u => CoordinateAxisLayout.Create(StationLayout, _paneWidth, Measure, editing, u).PaneHeight);
        var end = AxisLayout.Labels.Single(l => l.Role == AxisEndpointRole.End);
        LengthButton.Content = end.Text;
        LengthButton.Height = end.Bounds.Height;
        Canvas.SetLeft(EndValue, end.Bounds.X);
        Canvas.SetTop(EndValue, end.Bounds.Y);
        EndValue.Width = end.Bounds.Width;
        EndValue.Height = end.Bounds.Height;
        Canvas.SetLeft(LengthError, Math.Max(0, Math.Min(end.Bounds.Right - 360, _paneWidth - 360)));
        Canvas.SetTop(LengthError, AxisLayout.Packing.PaneHeight);
        DistortionHint.IsVisible = StationLayout.IsDistorted;
        Canvas.SetLeft(DistortionHint, StationLayout.Stations[0].ScreenX);
        Canvas.SetTop(DistortionHint, AxisLayout.Packing.PaneHeight + (_editor?.DimensionLength.IsEditing == true ? CoordinateAxisLayout.ErrorReserve : 0));
    }

    private void BeginLengthEdit(object? sender, RoutedEventArgs e)
    {
        LengthEditStarting?.Invoke();
        _editor?.DimensionLength.Begin();
        Dispatcher.UIThread.Post(AxisLengthInput.FocusInput, DispatcherPriority.Input);
    }
}

public sealed class CoordinateAxisCanvas : Control
{
    public static readonly StyledProperty<CoordinateAxisLayout?> AxisLayoutProperty = AvaloniaProperty.Register<CoordinateAxisCanvas, CoordinateAxisLayout?>(nameof(AxisLayout));
    public static readonly StyledProperty<IBrush?> AxisBrushProperty = AvaloniaProperty.Register<CoordinateAxisCanvas, IBrush?>(nameof(AxisBrush));
    public static readonly StyledProperty<double> LabelFontSizeProperty = AvaloniaProperty.Register<CoordinateAxisCanvas, double>(nameof(LabelFontSize), 13);
    public CoordinateAxisLayout? AxisLayout { get => GetValue(AxisLayoutProperty); set => SetValue(AxisLayoutProperty, value); }
    public IBrush? AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }
    static CoordinateAxisCanvas() => AffectsRender<CoordinateAxisCanvas>(AxisLayoutProperty, AxisBrushProperty, LabelFontSizeProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (AxisLayout is not { } axis) return;
        var pen = new Pen(AxisBrush, 1);
        context.DrawLine(pen, new(axis.StationLayout.Stations[0].ScreenX, CoordinateAxisLayout.AxisY),
            new(axis.StationLayout.Stations[^1].ScreenX, CoordinateAxisLayout.AxisY));
        foreach (var station in axis.StationLayout.Stations)
            context.DrawLine(pen, new(station.ScreenX, CoordinateAxisLayout.AxisY - 4), new(station.ScreenX, CoordinateAxisLayout.AxisY + 4));
        foreach (var label in axis.Labels.Where(l => l.Role != AxisEndpointRole.End))
            context.DrawText(SchematicText.Format(label.Text, Typeface.Default, LabelFontSize, AxisBrush), label.Bounds.Position);
        context.DrawText(SchematicText.Format("x [" + axis.UnitSymbol + "]", Typeface.Default, LabelFontSize, AxisBrush),
            new(axis.StationLayout.Stations[^1].ScreenX + 8, 0));
    }
}
