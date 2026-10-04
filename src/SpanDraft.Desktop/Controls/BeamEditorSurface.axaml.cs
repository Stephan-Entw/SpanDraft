using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface : UserControl
{
    private EditorViewModel? _editor;
    private SupportDraftViewModel? _shownDraft;
    private SupportDragGesture? _gesture;
    private Length? _placementClick;
    private IPointer? _pointer;
    private Point? _lastPointer;
    private static readonly Cursor PlacementCursor = new(StandardCursorType.Cross);
    private static readonly Cursor EditCursor = new(StandardCursorType.Hand);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeWestEast);

    public BeamEditorSurface()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveEditor();
        AttachedToVisualTree += (_, _) => ObserveEditor();
        DetachedFromVisualTree += (_, _) =>
        {
            ReleaseGesture();
            if (_editor is not null)
            {
                _editor.CancelSupportInteraction();
                _editor.PropertyChanged -= EditorChanged;
            }
            _editor = null;
            SupportPopup.IsOpen = false;
            _shownDraft = null;
        };
    }

    private BeamViewport Viewport => BeamViewport.Fit(Bounds.Width, Bounds.Height, _editor?.Document.Length.Meters ?? 1);

    private void ObserveEditor()
    {
        if (_editor == DataContext) return;
        if (_editor is not null) _editor.PropertyChanged -= EditorChanged;
        _editor = DataContext as EditorViewModel;
        if (_editor is not null) _editor.PropertyChanged += EditorChanged;
        SynchronizeVisuals();
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Document) or nameof(EditorViewModel.SupportDraft)
            or nameof(EditorViewModel.Preview) or nameof(EditorViewModel.HoveredSupportId)) SynchronizeVisuals();
    }

    private void SynchronizeVisuals()
    {
        var geometry = Viewport;
        Canvas.SetLeft(DimensionOverlay, geometry.Midpoint - DimensionOverlay.Width / 2);
        Canvas.SetTop(DimensionOverlay, geometry.DimensionY - DimensionButton.Height / 2);
        double x = _editor?.Preview is { } preview ? geometry.BeamToScreen(preview.Position.Meters) : geometry.Midpoint;
        Canvas.SetLeft(CoordinateOverlay, Math.Clamp(x + 12, 8, Math.Max(8, Bounds.Width - 170)));
        Canvas.SetTop(CoordinateOverlay, geometry.BeamY - 28);
        Canvas.SetLeft(FeedbackOverlay, Math.Clamp(x - 180, 8, Math.Max(8, Bounds.Width - 368)));
        Canvas.SetTop(FeedbackOverlay, geometry.BeamY + 48);
        Canvas.SetLeft(SupportAnchor, x);
        Canvas.SetTop(SupportAnchor, geometry.BeamY + 40);
        Cursor = _editor?.Interaction switch
        {
            SupportInteraction.Placement => PlacementCursor,
            SupportInteraction.Drag => DragCursor,
            _ => _editor?.HoveredSupportId is not null ? EditCursor : Cursor.Default
        };
        var draft = _editor?.SupportDraft;
        if (_shownDraft == draft) return;
        _shownDraft = draft;
        SupportPopup.IsOpen = draft is not null;
        if (draft is not null)
            Dispatcher.UIThread.Post(() => { if (_shownDraft == draft) SupportEditor.FocusPosition(); }, DispatcherPriority.Input);
        else if (TopLevel.GetTopLevel(this) is not null)
            Dispatcher.UIThread.Post(() => { if (_shownDraft is null && TopLevel.GetTopLevel(this) is not null) Focus(); }, DispatcherPriority.Input);
    }

    private void SurfaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        SynchronizeVisuals();
        if (_editor?.Interaction == SupportInteraction.Placement && _lastPointer is { } point)
            _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
    }

    private void BeginDimensionEdit(object? sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        ReleaseGesture();
        _editor.CancelSupportInteraction();
        _editor.DimensionLength.Begin();
        Dispatcher.UIThread.Post(DimensionInput.FocusInput, DispatcherPriority.Input);
    }

    private bool OverOverlay(object? source) => source is Control control
        && (control == DimensionOverlay || control.GetVisualAncestors().Any(a => a == DimensionOverlay));

    private void SurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor is null) return;
        var point = e.GetPosition(this);
        _lastPointer = point;
        if (_gesture is not null) { UpdateGesture(point); return; }
        if (_placementClick is not null) return;
        if (OverOverlay(e.Source)) { _editor.HoverPlacement(null); _editor.HoverSupport(null); return; }
        if (_editor.Interaction == SupportInteraction.Placement)
            _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
        else if (_editor.Interaction == SupportInteraction.Neutral)
            _editor.HoverSupport(SupportSymbol.HitTest(_editor.Document.Supports, Viewport, point.X, point.Y));
    }

    private void SurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || OverOverlay(e.Source) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        _lastPointer = point;
        if (_editor.Interaction == SupportInteraction.Placement)
        {
            Focus();
            _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
            if (_editor.Preview is { IsInvalid: false } preview)
            {
                _placementClick = preview.Position;
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
        }
        else if (_editor.Interaction == SupportInteraction.Neutral
            && SupportSymbol.HitTest(_editor.Document.Supports, Viewport, point.X, point.Y) is { } id)
        {
            Focus();
            var support = _editor.Document.Supports.First(s => s.Id == id);
            _gesture = new(id, support.Position, point.X);
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void UpdateGesture(Point point)
    {
        if (_gesture is null || _editor is null) return;
        var position = _gesture.Update(Viewport, point.X, point.Y, Bounds.Width, Bounds.Height);
        if (!_gesture.IsDragging) return;
        if (_editor.Interaction == SupportInteraction.Neutral) _editor.BeginSupportDrag(_gesture.SupportId);
        _editor.UpdateSupportDrag(position);
    }

    private void SurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_placementClick is { } position && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            var point = e.GetPosition(this);
            ReleaseGesture();
            if (_editor.Interaction == SupportInteraction.Placement)
            {
                _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y) is not null ? position : null);
                // Open after the initiating press has finished routing: otherwise
                // light-dismiss can interpret that same press as an outside click.
                _editor.PlaceSupport();
            }
            e.Handled = true;
            return;
        }
        if (_gesture is null || _editor is null || e.InitialPressMouseButton != MouseButton.Left) return;
        UpdateGesture(e.GetPosition(this));
        var gesture = _gesture;
        ReleaseGesture();
        if (gesture.IsDragging) _editor.EndSupportDrag();
        else _editor.EditSupport(gesture.SupportId);
        e.Handled = true;
    }

    private void SurfacePointerExited(object? sender, PointerEventArgs e)
    {
        if (_gesture is not null) return;
        _lastPointer = null;
        _editor?.HoverPlacement(null);
        _editor?.HoverSupport(null);
    }

    private void SurfaceCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_gesture is null && _placementClick is null) return;
        ReleaseGesture();
        _editor?.CancelSupportInteraction();
    }

    private void ReleaseGesture()
    {
        _gesture = null;
        _placementClick = null;
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
    }

    private void SurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ReleaseGesture();
        _editor?.CancelSupportInteraction();
        if (_editor?.DimensionLength.HasError == true) _editor.DimensionLength.Cancel();
        e.Handled = true;
    }

    private void SupportPopupClosed(object? sender, EventArgs e)
    {
        if (!SupportPopup.IsOpen && _editor?.SupportDraft is not null) _editor.CancelSupportInteraction();
    }
}
