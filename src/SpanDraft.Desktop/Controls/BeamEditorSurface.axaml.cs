using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private readonly BeamLayoutState _layout = new();
    private BeamRenderState? _scene;
    private bool _synchronizing;
    private BeamLayoutFrame? Frame => _layout.Current;
    private SupportDraftViewModel? _shownDraft;
    private int _popupFocusVersion;
    private TopLevel? _topLevel;
    private SupportDragGesture? _gesture;
    private Length? _placementClick;
    private IPointer? _pointer;
    private static readonly Cursor PlacementCursor = new(StandardCursorType.Cross);
    private static readonly Cursor EditCursor = new(StandardCursorType.Hand);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeWestEast);

    public BeamEditorSurface()
    {
        InitializeComponent();
        BeamPane.MinHeight = SpanDraft.Desktop.Layout.SchematicMetrics.MinimumBeamPaneHeight;
        CoordinateAxis.LengthEditStarting += () => { ReleaseGesture(); _editor?.CancelEditorInteraction(); };
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
            _layout.EndInteraction();
            SelectionPopup.IsOpen = false;
            _editor = null;
            SupportPopup.IsOpen = false;
            _shownDraft = null;
            LoadPopup.IsOpen = false;
            _shownLoadDraft = null;
        };
    }

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
        var point = e.GetPosition(BeamPane);
        if (new Rect(BeamPane.Bounds.Size).Contains(point) && _editor.Preview is { } preview
            && Frame is { } supportFrame && SupportSymbol.Contains(preview, supportFrame.Layout.Transform, supportFrame.Viewport.BeamY, point.X, point.Y)) return;
        if (new Rect(BeamPane.Bounds.Size).Contains(point) && ActiveLoadVisual is { } load
            && PointLoadSymbol.Contains(load, point.X, point.Y)) return;
        if (_scene?.HitTestLabel(point.X, point.Y) is { } label && label.Id is { } labelId
            && (labelId == _editor.SupportDraft?.OriginalId || labelId == _editor.LoadDraft?.OriginalId)) return;
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
        if (_synchronizing || _editor is null) return;
        _synchronizing = true;
        try
        {
            bool placement = _editor.Interaction == SupportInteraction.Placement || _editor.LoadState == LoadInteraction.Placement;
            if (!placement && _gesture is null && _loadGesture is null && _placementClick is null) _layout.EndInteraction();
            var frame = _layout.Update(_editor.Document, BeamPane.Bounds.Width, BeamPane.Bounds.Height);
            if (frame is null) return;
            if (placement && _layout.Snapshot is null)
                frame = _layout.BeginInteraction(_editor.Interaction == SupportInteraction.Placement
                    ? BeamPointerInteraction.SupportPlacement : BeamPointerInteraction.LoadPlacement)!;
            System.Func<string, Size> measure = text => SchematicText.Measure(text, Typeface.Default, TechnicalCanvas.LabelFontSize);
            // Reserve from committed labels, including a spare preview row. Neither
            // hover nor transient text changes the pane's requested height.
            var committed = BeamRenderState.Create(_editor.Document, frame, measure);
            BeamPane.MinHeight = committed.MinimumPaneHeight;
            _scene = BeamRenderState.Create(_editor.Document, frame, measure,
                _editor.Preview, _editor.HiddenSupportId, _editor.SupportPreviewName,
                _editor.LoadPreview, _editor.HiddenLoadId, _editor.LoadPreviewName);
            TechnicalCanvas.Scene = _scene;
            CoordinateAxis.SetStationLayout(frame.Layout, frame.Viewport.Width);
            double x = (_editor.Preview?.Position ?? _editor.LoadPreview?.Position) is { } position
                ? frame.Layout.Transform.PhysicalToScreen(position.Meters)
                : (frame.Layout.Stations[0].ScreenX + frame.Layout.Stations[^1].ScreenX) / 2;
            Canvas.SetLeft(CoordinateOverlay, Math.Clamp(x + 12, 8, Math.Max(8, BeamPane.Bounds.Width - 170)));
            Canvas.SetTop(CoordinateOverlay, frame.Viewport.BeamY - 28);
            Canvas.SetLeft(FeedbackOverlay, Math.Clamp(x - 180, 8, Math.Max(8, BeamPane.Bounds.Width - 368)));
            Canvas.SetTop(FeedbackOverlay, frame.Viewport.BeamY + 64);
            Canvas.SetLeft(SupportAnchor, x);
            Canvas.SetTop(SupportAnchor, frame.Viewport.BeamY + 56);
            Cursor = placement ? PlacementCursor : _editor.Interaction == SupportInteraction.Drag || _editor.LoadState == LoadInteraction.Drag
                ? DragCursor : _editor.HoveredSupportId is not null || _editor.HoveredLoadId is not null ? EditCursor : Cursor.Default;
            SynchronizeLoadVisuals();
            var draft = _editor.SupportDraft;
            bool show = _editor.IsSupportFlyoutVisible;
            FeedbackOverlay.Text = _editor.LoadFeedback ?? _editor.SupportFeedback;
            FeedbackOverlay.IsVisible = !show && !_editor.IsLoadFlyoutVisible && (_editor.HasSupportFeedback || _editor.HasLoadFeedback);
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
        finally { _synchronizing = false; }
    }

    private void SurfaceSizeChanged(object? sender, SizeChangedEventArgs e) => SynchronizeVisuals();
    private bool OverOverlay(object? source) => source is Control control
        && (control == CoordinateAxis || control.GetVisualAncestors().Contains(CoordinateAxis));
    private Length? SnapPlacement(Point point) => Frame is { } frame
        ? SupportSnap.Placement(frame.Layout.Transform, frame.Viewport.BeamY, point.X, point.Y) : null;

    private bool OverSupportFlyout(object? source) => source is Control control && (control == SupportEditor
        || control.GetVisualAncestors().Contains(SupportEditor) || control.GetLogicalAncestors().Contains(SupportEditor));

    private void SurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor is null) return;
        var point = e.GetPosition(BeamPane);
        if (_gesture is not null || _loadGesture is not null) { UpdateGesture(point); return; }
        if (_placementClick is not null) return;
        if (OverOverlay(e.Source)) { _editor.HoverPlacement(null); _editor.HoverSupport(null); _editor.HoverLoadPlacement(null); _editor.HoverLoad(null); return; }
        if (_editor.LoadState == LoadInteraction.Placement)
        {
            _editor.HoverLoadPlacement(SnapPlacement(point));
            return;
        }
        if (_editor.Interaction == SupportInteraction.Placement)
            _editor.HoverPlacement(SnapPlacement(point));
        else if (_editor.Interaction == SupportInteraction.Neutral && _editor.LoadState == LoadInteraction.Neutral)
        {
            var loadId = PointLoadSymbol.HitTest(LoadVisuals, point.X, point.Y);
            _editor.HoverLoad(loadId);
            _editor.HoverSupport(loadId is null ? HitSupport(point) : null);
        }
    }

    private void SurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || OverOverlay(e.Source) || OverSupportFlyout(e.Source) || OverLoadFlyout(e.Source)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(BeamPane);
        if (_scene?.HitTestLabel(point.X, point.Y) is { Id: { } labelId } label)
        {
            if (_editor.SupportDraft is null && _editor.LoadDraft is null)
            {
                if (label.IsSupport) _editor.EditSupport(labelId); else _editor.EditLoad(labelId);
            }
            e.Handled = true;
            return;
        }
        if (HandleLoadPress(point, e)) return;
        if (_editor.IsSupportFlyoutVisible)
        {
            if (_editor.Preview is { } preview && Frame is { } supportFrame && SupportSymbol.Contains(preview, supportFrame.Layout.Transform, supportFrame.Viewport.BeamY, point.X, point.Y))
            {
                if (_editor.SupportDraft?.OriginalId is { } id)
                {
                    _layout.BeginInteraction(BeamPointerInteraction.SupportDrag);
                    _gesture = new(id, preview.Position, point.X, Frame!.Layout.Transform);
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
            _editor.HoverPlacement(SnapPlacement(point));
            if (_editor.Preview is { IsInvalid: false } preview)
            {
                _placementClick = preview.Position;
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
        }
        else if (_editor.Interaction == SupportInteraction.Neutral
            && HitSupport(point) is { } id)
        {
            Focus();
            var support = _editor.Document.Supports.First(s => s.Id == id);
            _layout.BeginInteraction(BeamPointerInteraction.SupportDrag);
            _gesture = new(id, support.Position, point.X, Frame!.Layout.Transform);
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void UpdateGesture(Point point)
    {
        if (_loadGesture is not null) { UpdateLoadGesture(point); return; }
        if (_gesture is null || _editor is null) return;
        var position = _gesture.Update(point.X, point.Y, BeamPane.Bounds.Height);
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
            var point = e.GetPosition(BeamPane);
            if (_editor.LoadState == LoadInteraction.Placement)
            {
                _editor.HoverLoadPlacement(SnapPlacement(point) is not null ? position : null);
                _editor.PlaceLoad();
            }
            else if (_editor.Interaction == SupportInteraction.Placement)
            {
                _editor.HoverPlacement(SnapPlacement(point) is not null ? position : null);
                // Open after the initiating press has finished routing: otherwise
                // light-dismiss can interpret that same press as an outside click.
                _editor.PlaceSupport();
            }
            ReleaseGesture();
            SynchronizeVisuals();
            e.Handled = true;
            return;
        }
        if (_loadGesture is not null && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            UpdateLoadGesture(e.GetPosition(BeamPane));
            var loadGesture = _loadGesture;
            if (loadGesture is not null)
            {
                if (loadGesture.IsDragging) _editor.EndLoadDrag();
                else _editor.EditLoad(loadGesture.LoadId);
            }
            ReleaseGesture();
            SynchronizeVisuals();
            e.Handled = true;
            return;
        }
        if (_gesture is null || _editor is null || e.InitialPressMouseButton != MouseButton.Left) return;
        UpdateGesture(e.GetPosition(BeamPane));
        var gesture = _gesture;
        if (gesture.IsDragging) _editor.EndSupportDrag();
        else _editor.EditSupport(gesture.SupportId);
        ReleaseGesture();
        SynchronizeVisuals();
        e.Handled = true;
    }

    private void SurfacePointerExited(object? sender, PointerEventArgs e)
    {
        if (_gesture is not null || _loadGesture is not null) return;
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
        _layout.EndInteraction();
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
    }

    private Guid? HitSupport(Point point) => Frame is { } frame
        ? SupportSymbol.HitTest(_editor!.Document.Supports, frame.Layout.Transform, frame.Viewport.BeamY, point.X, point.Y) : null;

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
