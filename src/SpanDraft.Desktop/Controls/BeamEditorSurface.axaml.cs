using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
    private int _popupFocusVersion;
    private TopLevel? _topLevel;
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
            ObserveTopLevel(null);
            ReleaseGesture();
            if (_editor is not null)
            {
                _editor.CancelEditorInteraction();
                _editor.PropertyChanged -= EditorChanged;
            }
            _editor = null;
            SupportPopup.IsOpen = false;
            _shownDraft = null;
            LoadPopup.IsOpen = false;
            _shownLoadDraft = null;
        };
    }

    private BeamViewport Viewport => BeamViewport.Fit(Bounds.Width, Bounds.Height, _editor?.Document.Length.Meters ?? 1);

    private void ObserveEditor()
    {
        ObserveTopLevel(TopLevel.GetTopLevel(this));
        if (_editor == DataContext) return;
        if (_editor is not null) _editor.PropertyChanged -= EditorChanged;
        _editor = DataContext as EditorViewModel;
        if (_editor is not null) _editor.PropertyChanged += EditorChanged;
        SynchronizeVisuals();
    }

    private void ObserveTopLevel(TopLevel? topLevel)
    {
        if (_topLevel == topLevel) return;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerPressedEvent, SupportOutsidePointerPressed);
            if (_topLevel is Window window) window.Deactivated -= SupportWindowDeactivated;
        }
        _topLevel = topLevel;
        if (_topLevel is not null)
        {
            _topLevel.AddHandler(PointerPressedEvent, SupportOutsidePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            if (_topLevel is Window window) window.Deactivated += SupportWindowDeactivated;
        }
    }

    private void SupportWindowDeactivated(object? sender, EventArgs e)
    {
        if (_editor?.SupportDraft is not null || _editor?.LoadDraft is not null)
        {
            ReleaseGesture();
            _editor.CancelEditorInteraction();
        }
    }

    private void SupportOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || (!_editor.IsSupportFlyoutVisible && !_editor.IsLoadFlyoutVisible)) return;
        if (OverSupportFlyout(e.Source) || OverLoadFlyout(e.Source)) return;
        if (LoadPopup.IsOpen && new Rect(LoadEditor.Bounds.Size).Contains(e.GetPosition(LoadEditor))) return;
        // Overlay-hosted popup content and its type dropdown belong to this session.
        if (SupportPopup.IsOpen && new Rect(SupportEditor.Bounds.Size).Contains(e.GetPosition(SupportEditor))) return;
        var point = e.GetPosition(this);
        if (new Rect(Bounds.Size).Contains(point) && _editor.Preview is { } preview
            && SupportSymbol.Contains(preview, Viewport, point.X, point.Y)) return;
        if (new Rect(Bounds.Size).Contains(point) && ActiveLoadVisual is { } load
            && PointLoadSymbol.Contains(load, point.X, point.Y)) return;
        ReleaseGesture();
        _editor.CancelEditorInteraction();
        e.Handled = !OverToolbar(e.Source);
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Document) or nameof(EditorViewModel.SupportDraft)
            or nameof(EditorViewModel.IsSupportFlyoutVisible) or nameof(EditorViewModel.Preview)
            or nameof(EditorViewModel.HoveredSupportId) or nameof(EditorViewModel.HasSupportFeedback)
            or nameof(EditorViewModel.ConstraintConflict) or nameof(EditorViewModel.LoadPreview)
            or nameof(EditorViewModel.LoadDraft) or nameof(EditorViewModel.IsLoadFlyoutVisible)
            or nameof(EditorViewModel.HoveredLoadId) or nameof(EditorViewModel.HasLoadFeedback)) SynchronizeVisuals();
    }

    private void SynchronizeVisuals()
    {
        // Reserve space before hover begins. Growing/shrinking with a hover preview
        // would move the beam away from the pointer and cause alternating snap/leave.
        MinHeight = PointLoadSymbol.MinimumHeight(_editor?.Document.Loads ?? [],
            reserveAdditionalLane: _editor is not null && _editor.LoadState != LoadInteraction.Neutral);
        var geometry = Viewport;
        var length = BeamLengthGeometry.Create(geometry, _editor?.ConstraintConflict);
        Canvas.SetLeft(DimensionOverlay, length.DimensionMidpoint - DimensionOverlay.Width / 2);
        Canvas.SetTop(DimensionOverlay, geometry.DimensionY - DimensionButton.Height / 2);
        double x = (_editor?.Preview?.Position ?? _editor?.LoadPreview?.Position) is { } position
            ? geometry.BeamToScreen(position.Meters) : geometry.Midpoint;
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
        if (_editor?.LoadState == LoadInteraction.Placement) Cursor = PlacementCursor;
        else if (_editor?.LoadState == LoadInteraction.Drag) Cursor = DragCursor;
        else if (_editor?.HoveredLoadId is not null) Cursor = EditCursor;
        SynchronizeLoadVisuals();
        var draft = _editor?.SupportDraft;
        bool show = _editor?.IsSupportFlyoutVisible == true;
        FeedbackOverlay.Text = _editor?.LoadFeedback ?? _editor?.SupportFeedback;
        FeedbackOverlay.IsVisible = !show && _editor?.IsLoadFlyoutVisible != true
            && (_editor?.HasSupportFeedback == true || _editor?.HasLoadFeedback == true);
        if (_shownDraft == draft && SupportPopup.IsOpen == show) return;
        _shownDraft = draft;
        int version = ++_popupFocusVersion;
        SupportPopup.IsOpen = show;
        if (show)
            Dispatcher.UIThread.Post(() =>
            {
                if (_popupFocusVersion == version && _shownDraft == draft && SupportPopup.IsOpen
                    && _editor?.IsSupportFlyoutVisible == true) SupportEditor.FocusPosition();
            }, DispatcherPriority.Input);
        else if (draft is null && TopLevel.GetTopLevel(this) is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (_popupFocusVersion == version && _shownDraft is null && _editor?.LoadDraft is null
                    && TopLevel.GetTopLevel(this) is not null) Focus();
            }, DispatcherPriority.Input);
    }

    private void SurfaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        SynchronizeVisuals();
        if (_editor?.Interaction == SupportInteraction.Placement && _lastPointer is { } point)
            _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
        if (_editor?.LoadState == LoadInteraction.Placement && _lastPointer is { } loadPoint)
            _editor.HoverLoadPlacement(SupportSnap.Placement(Viewport, loadPoint.X, loadPoint.Y));
    }

    private void BeginDimensionEdit(object? sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        ReleaseGesture();
        _editor.CancelEditorInteraction();
        _editor.DimensionLength.Begin();
        Dispatcher.UIThread.Post(DimensionInput.FocusInput, DispatcherPriority.Input);
    }

    private bool OverOverlay(object? source) => source is Control control
        && (control == DimensionOverlay || control.GetVisualAncestors().Any(a => a == DimensionOverlay));

    private bool OverSupportFlyout(object? source) => source is Control control && (control == SupportEditor
        || control.GetVisualAncestors().Contains(SupportEditor) || control.GetLogicalAncestors().Contains(SupportEditor));

    private void SurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor is null) return;
        var point = e.GetPosition(this);
        _lastPointer = point;
        if (_gesture is not null || _loadGesture is not null) { UpdateGesture(point); return; }
        if (_placementClick is not null) return;
        if (OverOverlay(e.Source)) { _editor.HoverPlacement(null); _editor.HoverSupport(null); _editor.HoverLoadPlacement(null); _editor.HoverLoad(null); return; }
        if (_editor.LoadState == LoadInteraction.Placement)
        {
            _editor.HoverLoadPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
            return;
        }
        if (_editor.Interaction == SupportInteraction.Placement)
            _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y));
        else if (_editor.Interaction == SupportInteraction.Neutral && _editor.LoadState == LoadInteraction.Neutral)
        {
            var loadId = PointLoadSymbol.HitTest(LoadVisuals, point.X, point.Y);
            _editor.HoverLoad(loadId);
            _editor.HoverSupport(loadId is null ? SupportSymbol.HitTest(_editor.Document.Supports, Viewport, point.X, point.Y) : null);
        }
    }

    private void SurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || OverOverlay(e.Source) || OverSupportFlyout(e.Source) || OverLoadFlyout(e.Source)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        _lastPointer = point;
        if (HandleLoadPress(point, e)) return;
        if (_editor.IsSupportFlyoutVisible)
        {
            if (_editor.Preview is { } preview && SupportSymbol.Contains(preview, Viewport, point.X, point.Y))
            {
                if (_editor.SupportDraft?.OriginalId is { } id)
                {
                    _gesture = new(id, preview.Position, point.X);
                    _pointer = e.Pointer;
                    e.Pointer.Capture(this);
                }
            }
            else _editor.CancelEditorInteraction();
            // The active symbol belongs to the flyout; other canvas clicks only dismiss.
            e.Handled = true;
        }
        else if (_editor.Interaction == SupportInteraction.Placement)
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
        if (_loadGesture is not null) { UpdateLoadGesture(point); return; }
        if (_gesture is null || _editor is null) return;
        var position = _gesture.Update(Viewport, point.X, point.Y, Bounds.Width, Bounds.Height);
        if (!_gesture.IsDragging) return;
        if (_editor.Interaction != SupportInteraction.Drag)
        {
            Focus();
            if (!_editor.BeginSupportDrag(_gesture.SupportId)) { ReleaseGesture(); return; }
        }
        _editor.UpdateSupportDrag(position);
    }

    private void SurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_placementClick is { } position && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            var point = e.GetPosition(this);
            ReleaseGesture();
            if (_editor.LoadState == LoadInteraction.Placement)
            {
                _editor.HoverLoadPlacement(SupportSnap.Placement(Viewport, point.X, point.Y) is not null ? position : null);
                _editor.PlaceLoad();
            }
            else if (_editor.Interaction == SupportInteraction.Placement)
            {
                _editor.HoverPlacement(SupportSnap.Placement(Viewport, point.X, point.Y) is not null ? position : null);
                // Open after the initiating press has finished routing: otherwise
                // light-dismiss can interpret that same press as an outside click.
                _editor.PlaceSupport();
            }
            e.Handled = true;
            return;
        }
        if (_loadGesture is not null && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            UpdateLoadGesture(e.GetPosition(this));
            var loadGesture = _loadGesture;
            ReleaseGesture();
            if (loadGesture is not null)
            {
                if (loadGesture.IsDragging) _editor.EndLoadDrag();
                else _editor.EditLoad(loadGesture.LoadId);
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
        if (_gesture is not null || _loadGesture is not null) return;
        _lastPointer = null;
        _editor?.HoverPlacement(null);
        _editor?.HoverSupport(null);
        _editor?.HoverLoadPlacement(null);
        _editor?.HoverLoad(null);
    }

    private void SurfaceCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_gesture is null && _loadGesture is null && _placementClick is null) return;
        ReleaseGesture();
        _editor?.CancelEditorInteraction();
    }

    private void ReleaseGesture()
    {
        _gesture = null;
        _loadGesture = null;
        _placementClick = null;
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
    }

    private void SurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ReleaseGesture();
        _editor?.CancelEditorInteraction();
        if (_editor is { } editor && (editor.DimensionLength.IsEditing || editor.DimensionLength.HasError
            || editor.ConstraintConflict is not null)) editor.DimensionLength.Cancel();
        e.Handled = true;
    }

    private void SupportPopupClosed(object? sender, EventArgs e)
    {
        if (!SupportPopup.IsOpen && _editor?.IsSupportFlyoutVisible == true) _editor.CancelEditorInteraction();
    }
}
