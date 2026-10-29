using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private IReadOnlyList<ResultProbeRow>? _shownProbeRows;

    private Length? SnapResultProbe(Point point) => Frame is { } frame && _editor is { } editor
        ? ResultProbeSnap.Placement(frame.Layout, frame.Viewport.BeamY, point.X, point.Y,
            editor.ResultPresentation.Profile[QuantityKind.BeamLength]) : null;

    private bool IsProbeStation(Length? position) => position is { } p
        && Frame?.Layout.Stations.Any(s => s.PhysicalX == p.Meters) == true;

    private void SynchronizeResultProbe()
    {
        bool visible = _editor is { IsResultProbeTool: true, IsBusy: false, ResultProbePosition: not null }
            && Frame is not null;
        ProbeMarker.IsVisible = ProbePoint.IsVisible = ProbeBox.IsVisible = visible;
        if (!visible) return;
        var editor = _editor!;
        if (!ReferenceEquals(_shownProbeRows, editor.ResultProbeRows))
        {
            _shownProbeRows = editor.ResultProbeRows;
            ProbeValues.Children.Clear();
            ProbeValues.RowDefinitions.Clear();
            foreach (var row in editor.ResultProbeRows)
            {
                int index = ProbeValues.RowDefinitions.Count;
                ProbeValues.RowDefinitions.Add(new(GridLength.Auto));
                var label = new TextBlock { Text = row.Label, Classes = { "propertyLabel" } };
                var value = new TextBlock { Text = row.Value, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetRow(label, index);
                Grid.SetRow(value, index);
                Grid.SetColumn(value, 1);
                ProbeValues.Children.Add(label);
                ProbeValues.Children.Add(value);
            }
        }
        ProbeBox.Measure(Size.Infinity);
        PositionResultProbe(new Size(Math.Max(ProbeBox.DesiredSize.Width, ProbeBox.Bounds.Width),
            Math.Max(ProbeBox.DesiredSize.Height, ProbeBox.Bounds.Height)));
    }

    private void PositionResultProbe(Size size)
    {
        if (!ProbeBox.IsVisible || _editor?.ResultProbePosition is not { } position || Frame is not { } frame) return;
        double x = frame.Layout.Transform.PhysicalToScreen(position.Meters);
        Canvas.SetLeft(ProbeMarker, x);
        Canvas.SetTop(ProbeMarker, frame.Viewport.BeamY - ProbeMarker.Height / 2);
        Canvas.SetLeft(ProbePoint, x - ProbePoint.Width / 2);
        Canvas.SetTop(ProbePoint, frame.Viewport.BeamY - ProbePoint.Height / 2);
        double left = x + 12;
        if (left + size.Width > BeamPane.Bounds.Width - 4) left = x - 12 - size.Width;
        Canvas.SetLeft(ProbeBox, Math.Clamp(left, 4, Math.Max(4, BeamPane.Bounds.Width - size.Width - 4)));
        Canvas.SetTop(ProbeBox, Math.Clamp(frame.Viewport.BeamY + 12, 4,
            Math.Max(4, BeamPane.Bounds.Height - size.Height - 4)));
    }
}
