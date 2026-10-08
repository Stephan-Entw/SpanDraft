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
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface : UserControl
{
    public static readonly DirectProperty<BeamEditorSurface, StationLayoutResult?> StationLayoutProperty =
        AvaloniaProperty.RegisterDirect<BeamEditorSurface, StationLayoutResult?>(nameof(StationLayout), surface => surface.StationLayout);
    private StationLayoutResult? _stationLayout;
    public StationLayoutResult? StationLayout => _stationLayout;
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
        AddHandler(KeyDownEvent, LabelGestureKeyDown, RoutingStrategies.Tunnel);
        SupportEditor.AddHandler(KeyDownEvent, LabelGestureKeyDown, RoutingStrategies.Tunnel);
        LoadEditor.AddHandler(KeyDownEvent, LabelGestureKeyDown, RoutingStrategies.Tunnel);
        DistributedLoadEditor.AddHandler(KeyDownEvent, LabelGestureKeyDown, RoutingStrategies.Tunnel);
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
                _editor.InteractionsCancelling -= CancelSurfaceInteractions;
            }
            _layout.EndInteraction();
            SetAndRaise(StationLayoutProperty, ref _stationLayout, null);
            SelectionPopup.IsOpen = false;
            _editor = null;
            SupportPopup.IsOpen = false;
            _shownDraft = null;
            LoadPopup.IsOpen = false;
            _shownLoadDraft = null;
            DistributedLoadPopup.IsOpen = false;
            _shownDistributedDraft = null;
        };
    }

    private void ObserveEditor()
    {
        ObserveTopLevel(TopLevel.GetTopLevel(this));
        if (_editor == DataContext) return;
        CancelLabelGesture();
        if (_editor is not null)
        {
            _editor.PropertyChanged -= EditorChanged;
            _editor.InteractionsCancelling -= CancelSurfaceInteractions;
        }
        _editor = DataContext as EditorViewModel;
        if (_editor is not null)
        {
            _editor.PropertyChanged += EditorChanged;
            _editor.InteractionsCancelling += CancelSurfaceInteractions;
        }
        SynchronizeVisuals();
    }

    private void ObserveTopLevel(TopLevel? topLevel)
    {
        if (_topLevel == topLevel) return;
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerPressedEvent, SupportOutsidePointerPressed);
            _topLevel.RemoveHandler(KeyDownEvent, LabelGestureKeyDown);
            if (_topLevel is Window window) window.Deactivated -= SupportWindowDeactivated;
        }
        _topLevel = topLevel;
        if (_topLevel is not null)
        {
            _topLevel.AddHandler(PointerPressedEvent, SupportOutsidePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel.AddHandler(KeyDownEvent, LabelGestureKeyDown, RoutingStrategies.Tunnel);
            if (_topLevel is Window window) window.Deactivated += SupportWindowDeactivated;
        }
    }

    private void SupportWindowDeactivated(object? sender, EventArgs e)
    {
        if (_editor?.PreserveDrafts == true) return;
        if (CancelLabelGesture()) return;
        if (_editor?.SupportDraft is not null || _editor?.LoadDraft is not null || _editor?.DistributedLoadDraft is not null)
        {
            ReleaseGesture();
            _editor.CancelEditorInteraction();
        }
    }

    private void SupportOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || _editor.PreserveDrafts || OverMenu(e.Source) || (!_editor.IsSupportFlyoutVisible && !_editor.IsLoadFlyoutVisible && !_editor.IsDistributedLoadFlyoutVisible)) return;
        // Overview name buttons open the existing editors on Click (also via keyboard).
        // Do not consume their press or discard the same component's active buffer here.
        if (e.Source is Control overviewSource && (overviewSource is Button { } overviewButton
                && overviewButton.Classes.Contains("overviewEntityAction")
            || overviewSource.GetVisualAncestors().OfType<Button>().Any(b => b.Classes.Contains("overviewEntityAction")))) return;
        if (OverSupportFlyout(e.Source) || OverLoadFlyout(e.Source) || OverDistributedFlyout(e.Source)) return;
        if (DistributedLoadPopup.IsOpen && new Rect(DistributedLoadEditor.Bounds.Size).Contains(e.GetPosition(DistributedLoadEditor))) return;
        if (LoadPopup.IsOpen && new Rect(LoadEditor.Bounds.Size).Contains(e.GetPosition(LoadEditor))) return;
        // Overlay-hosted popup content and its type dropdown belong to this session.
        if (SupportPopup.IsOpen && new Rect(SupportEditor.Bounds.Size).Contains(e.GetPosition(SupportEditor))) return;
        var point = e.GetPosition(BeamPane);
        if (new Rect(BeamPane.Bounds.Size).Contains(point) && _editor.Preview is { } preview
            && Frame is { } supportFrame && SupportSymbol.Contains(preview, supportFrame.Layout.Transform, supportFrame.Viewport.BeamY, point.X, point.Y)) return;
        if (new Rect(BeamPane.Bounds.Size).Contains(point) && ActiveLoadVisual is { } load
            && PointLoadSymbol.Contains(load, point.X, point.Y)) return;
        if (new Rect(BeamPane.Bounds.Size).Contains(point) && ActiveDistributedVisual is { } distributed
            && DistributedLoadSymbol.Contains(distributed, point.X, point.Y)) return;
        if (_scene?.HitTestLabel(point.X, point.Y) is { } label && label.Id is { } labelId
            && (labelId == _editor.SupportDraft?.OriginalId || labelId == _editor.LoadDraft?.OriginalId
                || labelId == _editor.DistributedLoadDraft?.OriginalId)) return;
        ReleaseGesture();
        _editor.CancelEditorInteraction();
        e.Handled = !OverToolbar(e.Source);
    }

    private static bool OverMenu(object? source) => source is Control c
        && (c is MenuItem or Menu || c.GetVisualAncestors().Any(a => a is MenuItem or Menu)
            || c.GetLogicalAncestors().Any(a => a is MenuItem or Menu));

    private void CancelSurfaceInteractions()
    {
        ReleaseGesture();
        SelectionPopup.IsOpen = false;
        _layout.EndInteraction();
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsBusy) && _editor!.IsBusy)
        {
            ReleaseGesture();
            _editor.CancelPointerDrag();
        }
        if (_labelGesture is { } label && (e.PropertyName == nameof(EditorViewModel.Document)
            && !_editor!.Document.NamedEntities.Any(entity => entity.Id == label.EntityId)
            || e.PropertyName == nameof(EditorViewModel.IsSupportFlyoutVisible) && _editor!.Interaction == SupportInteraction.Placement
            || e.PropertyName == nameof(EditorViewModel.IsLoadFlyoutVisible) && _editor!.LoadState == LoadInteraction.Placement
            || e.PropertyName == nameof(EditorViewModel.IsDistributedLoadFlyoutVisible) && _editor!.DistributedLoadState == DistributedLoadInteraction.Placement))
            CancelLabelGesture();
        if (e.PropertyName is nameof(EditorViewModel.ResultPresentation) or nameof(EditorViewModel.IsBusy) or nameof(EditorViewModel.Document) or nameof(EditorViewModel.EditorPresentation) or nameof(EditorViewModel.RenderPresentation) or nameof(EditorViewModel.SupportDraft)
            or nameof(EditorViewModel.IsSupportFlyoutVisible) or nameof(EditorViewModel.Preview)
            or nameof(EditorViewModel.HoveredSupportId) or nameof(EditorViewModel.HasSupportFeedback)
            or nameof(EditorViewModel.ConstraintConflict) or nameof(EditorViewModel.LoadPreview)
            or nameof(EditorViewModel.LoadDraft) or nameof(EditorViewModel.IsLoadFlyoutVisible)
            or nameof(EditorViewModel.HoveredLoadId) or nameof(EditorViewModel.HasLoadFeedback)
            or nameof(EditorViewModel.DistributedLoadPreview) or nameof(EditorViewModel.DistributedLoadDraft)
            or nameof(EditorViewModel.IsDistributedLoadFlyoutVisible) or nameof(EditorViewModel.HoveredDistributedLoadId)
            or nameof(EditorViewModel.HasDistributedLoadFeedback)) SynchronizeVisuals();
    }

    private void SynchronizeVisuals()
    {
        if (_synchronizing) return;
        if (_editor is null)
        {
            SetAndRaise(StationLayoutProperty, ref _stationLayout, null);
            return;
        }
        _synchronizing = true;
        try
        {
            bool placement = _editor.Interaction == SupportInteraction.Placement || _editor.LoadState == LoadInteraction.Placement
                || _editor.DistributedLoadState == DistributedLoadInteraction.Placement;
            if (!placement && _gesture is null && _loadGesture is null && _distributedGesture is null
                && _placementClick is null && _labelGesture is null) _layout.EndInteraction();
            var frame = _layout.Update(_editor.Document, BeamPane.Bounds.Width, BeamPane.Bounds.Height,
                Frame?.Viewport.BelowBeamSpace ?? SpanDraft.Desktop.Layout.SchematicMetrics.BelowBeamSpace);
            if (frame is null)
            {
                SetAndRaise(StationLayoutProperty, ref _stationLayout, null);
                return;
            }
            if (placement && _layout.Snapshot is null)
                frame = _layout.BeginInteraction(_editor.Interaction == SupportInteraction.Placement
                    ? BeamPointerInteraction.SupportPlacement : _editor.LoadState == LoadInteraction.Placement
                        ? BeamPointerInteraction.LoadPlacement : BeamPointerInteraction.DistributedLoadPlacement)!;
            System.Func<string, Size> measure = text => SchematicText.Measure(text, Typeface.Default, TechnicalCanvas.LabelFontSize);
            // Reserve from committed labels, including a spare preview row. Neither
            // hover nor transient text changes the pane's requested height.
            var committed = BeamRenderState.Create(_editor.Document, frame, measure, presentation: _editor.EditorPresentation,
                profile: _editor.ResultPresentation.Profile, reserveUnitWidths: true);
            if (_labelGesture is null)
            {
                BeamPane.MinHeight = committed.MinimumPaneHeight;
                // Keep the ordinary lower reserve fixed when the upper area grows.
                // Accommodate genuine annotation overflow only after a gesture ends.
                frame = _layout.Update(_editor.Document, BeamPane.Bounds.Width, BeamPane.Bounds.Height,
                    committed.RequiredBelowBeamSpace)!;
            }
            _scene = BeamRenderState.Create(_editor.Document, frame, measure,
                _editor.Preview, _editor.HiddenSupportId, _editor.SupportPreviewName,
                _editor.LoadPreview, _editor.HiddenLoadId, _editor.LoadPreviewName, _editor.RenderPresentation,
                _editor.DistributedLoadPreview, _editor.HiddenDistributedLoadId, _editor.DistributedLoadPreviewName,
                _editor.ResultPresentation.Profile, reserveUnitWidths: true);
            TechnicalCanvas.Scene = _scene;
            CoordinateAxis.SetStationLayout(frame.Layout, frame.Viewport.Width);
            SetAndRaise(StationLayoutProperty, ref _stationLayout, frame.Layout);
            double x = (_editor.Preview?.Position ?? _editor.LoadPreview?.Position
                ?? _editor.DistributedPointerPosition ?? _editor.DistributedLoadPreview?.EndPosition) is { } position
                ? frame.Layout.Transform.PhysicalToScreen(position.Meters)
                : (frame.Layout.Stations[0].ScreenX + frame.Layout.Stations[^1].ScreenX) / 2;
            Canvas.SetLeft(CoordinateOverlay, Math.Clamp(x + 12, 8, Math.Max(8, BeamPane.Bounds.Width - 170)));
            Canvas.SetTop(CoordinateOverlay, frame.Viewport.BeamY - 28);
            Canvas.SetLeft(FeedbackOverlay, Math.Clamp(x - 180, 8, Math.Max(8, BeamPane.Bounds.Width - 368)));
            Canvas.SetTop(FeedbackOverlay, frame.Viewport.BeamY + 64);
            Canvas.SetLeft(SupportAnchor, x);
            Canvas.SetTop(SupportAnchor, frame.Viewport.BeamY + 56);
            Cursor = _labelGesture?.IsDragging == true ? LabelDragCursor : placement ? PlacementCursor : _editor.Interaction == SupportInteraction.Drag || _editor.LoadState == LoadInteraction.Drag
                || _editor.DistributedLoadState == DistributedLoadInteraction.Drag ? DragCursor
                : _editor.HoveredSupportId is not null || _editor.HoveredLoadId is not null || _editor.HoveredDistributedLoadId is not null ? EditCursor : Cursor.Default;
            SynchronizeLoadVisuals();
            SynchronizeDistributedVisuals();
            var draft = _editor.SupportDraft;
            bool show = _editor.IsSupportFlyoutVisible && !_editor.IsBusy;
            FeedbackOverlay.Text = _editor.DistributedLoadFeedback ?? _editor.LoadFeedback ?? _editor.SupportFeedback;
            FeedbackOverlay.IsVisible = !show && !_editor.IsLoadFlyoutVisible && !_editor.IsDistributedLoadFlyoutVisible
                && (_editor.HasSupportFeedback || _editor.HasLoadFeedback || _editor.HasDistributedLoadFeedback);
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
                    if (_popupFocusVersion == version && _shownDraft is null && _editor?.LoadDraft is null && _editor?.DistributedLoadDraft is null
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
        if (_editor is null || _editor.IsBusy) return;
        var point = e.GetPosition(BeamPane);
        if (_labelGesture is not null) { UpdateLabelGesture(point); e.Handled = true; return; }
        if (_gesture is not null || _loadGesture is not null || _distributedGesture is not null) { UpdateGesture(point); return; }
        if (_placementClick is not null) return;
        if (OverOverlay(e.Source)) { _editor.HoverPlacement(null); _editor.HoverSupport(null); _editor.HoverLoadPlacement(null); _editor.HoverLoad(null);
            _editor.HoverDistributedLoadPlacement(null); _editor.HoverDistributedLoad(null); return; }
        if (_editor.DistributedLoadState == DistributedLoadInteraction.Placement)
        {
            _editor.HoverDistributedLoadPlacement(SnapPlacement(point));
            return;
        }
        if (_editor.LoadState == LoadInteraction.Placement)
        {
            _editor.HoverLoadPlacement(SnapPlacement(point));
            return;
        }
        if (_editor.Interaction == SupportInteraction.Placement)
            _editor.HoverPlacement(SnapPlacement(point));
        else if (_editor.IsEditorNeutral)
        {
            var hits = _scene?.HitTestEntities(point.X, point.Y);
            Guid? id = hits?.Count == 1 ? hits[0] : null;
            _editor.HoverLoad(id is { } loadId && _editor.Document.Loads.Any(l => l.Id == loadId) ? id : null);
            _editor.HoverSupport(id is { } supportId && _editor.Document.Supports.Any(s => s.Id == supportId) ? id : null);
            _editor.HoverDistributedLoad(id is { } distributedId && _editor.Document.DistributedLoads.Any(l => l.Id == distributedId) ? id : null);
        }
    }

    private void SurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null || _editor.IsBusy || OverOverlay(e.Source) || OverSupportFlyout(e.Source) || OverLoadFlyout(e.Source) || OverDistributedFlyout(e.Source)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(BeamPane);
        if (_editor.IsEditorNeutral && _scene?.HitTestLabels(point.X, point.Y).Count > 1)
        {
            ShowEntitySelection(_scene.HitTestEntities(point.X, point.Y), point);
            e.Handled = true;
            return;
        }
        if (_scene?.HitTestLabel(point.X, point.Y) is { } label)
        {
            if (label.Id is { } labelId && Frame is not null
                && (_editor.IsEditorNeutral || labelId == _editor.SupportDraft?.OriginalId || labelId == _editor.LoadDraft?.OriginalId
                    || labelId == _editor.DistributedLoadDraft?.OriginalId))
            {
                Focus();
                _layout.BeginInteraction(BeamPointerInteraction.LabelDrag);
                _labelGesture = new(label, point, _editor.EditorPresentation.AnnotationOffsets.TryGetValue(labelId, out var offset) ? offset : null);
                FreezeLabelPane();
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return;
        }
        if (HandleDistributedPress(point, e)) return;
        if (HandleNeutralEntityPress(point, e)) return;
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
        if (_distributedGesture is not null) { UpdateDistributedGesture(point); return; }
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
        if (_editor?.IsBusy == true) return;
        if (_labelGesture is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            FinishLabelGesture(e.GetPosition(BeamPane));
            e.Handled = true;
            return;
        }
        if (_placementClick is { } position && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            var point = e.GetPosition(BeamPane);
            if (_editor.DistributedLoadState == DistributedLoadInteraction.Placement)
            {
                _editor.HoverDistributedLoadPlacement(SnapPlacement(point) is not null ? position : null);
                _editor.PlaceDistributedLoadEndpoint();
            }
            else if (_editor.LoadState == LoadInteraction.Placement)
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
            ReleaseGesture(keepDistributedPlacement: _editor.DistributedLoadState == DistributedLoadInteraction.Placement);
            SynchronizeVisuals();
            e.Handled = true;
            return;
        }
        if (_distributedGesture is not null && _editor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            UpdateDistributedGesture(e.GetPosition(BeamPane));
            var distributedGesture = _distributedGesture;
            if (distributedGesture is not null)
            {
                if (distributedGesture.IsDragging) _editor.EndDistributedLoadDrag();
                else if (distributedGesture.LoadId is { } id) _editor.EditDistributedLoad(id);
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
        if (_gesture is not null || _loadGesture is not null || _distributedGesture is not null || _labelGesture is not null) return;
        _editor?.HoverPlacement(null);
        _editor?.HoverSupport(null);
        _editor?.HoverLoadPlacement(null);
        _editor?.HoverLoad(null);
        _editor?.HoverDistributedLoadPlacement(null);
        _editor?.HoverDistributedLoad(null);
    }

    private void SurfaceCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (CancelLabelGesture()) return;
        if (_gesture is null && _loadGesture is null && _distributedGesture is null && _placementClick is null) return;
        ReleaseGesture();
        if (_editor?.PreserveDrafts == true) _editor.CancelPointerDrag();
        else _editor?.CancelEditorInteraction();
    }

    private void ReleaseGesture(bool keepDistributedPlacement = false)
    {
        CancelLabelGesture();
        _gesture = null;
        _loadGesture = null;
        _distributedGesture = null;
        _placementClick = null;
        if (!keepDistributedPlacement) _layout.EndInteraction();
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
    }

    private Guid? HitSupport(Point point) => Frame is { } frame
        ? SupportSymbol.HitTest(_editor!.Document.Supports, frame.Layout.Transform, frame.Viewport.BeamY, point.X, point.Y) : null;

    private void SurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (CancelLabelGesture()) { e.Handled = true; return; }
        ReleaseGesture();
        _editor?.CancelEditorInteraction();
        if (_editor is { } editor && (editor.DimensionLength.IsEditing || editor.DimensionLength.HasError
            || editor.ConstraintConflict is not null)) editor.DimensionLength.Cancel();
        e.Handled = true;
    }

    private void SupportPopupClosed(object? sender, EventArgs e)
    {
        if (!SupportPopup.IsOpen && _editor?.PreserveDrafts != true && _editor?.IsSupportFlyoutVisible == true) _editor.CancelEditorInteraction();
    }
}
