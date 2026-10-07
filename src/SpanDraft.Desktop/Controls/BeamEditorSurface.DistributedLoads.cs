using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private DistributedLoadDraftViewModel? _shownDistributedDraft;
    private int _distributedPopupFocusVersion;
    private DistributedLoadDragGesture? _distributedGesture;
    private DistributedLoadVisual? ActiveDistributedVisual => _scene?.DistributedLoads.FirstOrDefault(v => v.IsPreview);

    private bool OverDistributedFlyout(object? source) => source is Control control && (control == DistributedLoadEditor
        || control.GetVisualAncestors().Contains(DistributedLoadEditor) || control.GetLogicalAncestors().Contains(DistributedLoadEditor));

    private void SynchronizeDistributedVisuals()
    {
        if (ActiveDistributedVisual is { } visual)
        {
            Canvas.SetLeft(DistributedLoadAnchor, visual.CenterX);
            Canvas.SetTop(DistributedLoadAnchor, visual.BeamY + 28);
        }
        var draft = _editor?.DistributedLoadDraft;
        bool show = _editor?.IsDistributedLoadFlyoutVisible == true;
        if (_shownDistributedDraft == draft && DistributedLoadPopup.IsOpen == show) return;
        _shownDistributedDraft = draft;
        int version = ++_distributedPopupFocusVersion;
        DistributedLoadPopup.IsOpen = show;
        if (show)
            Dispatcher.UIThread.Post(() =>
            {
                if (_distributedPopupFocusVersion == version && _shownDistributedDraft == draft && DistributedLoadPopup.IsOpen
                    && _editor?.IsDistributedLoadFlyoutVisible == true) DistributedLoadEditor.FocusStart();
            }, DispatcherPriority.Input);
        else if (draft is null && TopLevel.GetTopLevel(this) is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (_distributedPopupFocusVersion == version && _shownDistributedDraft is null && _editor?.SupportDraft is null
                    && _editor?.LoadDraft is null && TopLevel.GetTopLevel(this) is not null) Focus();
            }, DispatcherPriority.Input);
    }

    private bool HandleDistributedPress(Point point, PointerPressedEventArgs e)
    {
        if (_editor is null || Frame is null) return false;
        if (_editor.IsDistributedLoadFlyoutVisible)
        {
            if (ActiveDistributedVisual is { } active && DistributedLoadSymbol.Contains(active, point.X, point.Y))
            {
                if (DistributedLoadSymbol.HitEndpoint(active, point.X, point.Y) is { } endpoint)
                    CaptureDistributedLoad(active, endpoint, point, e);
            }
            else _editor.CancelDistributedLoadInteraction();
            e.Handled = true;
            return true;
        }
        if (_editor.DistributedLoadState == DistributedLoadInteraction.Placement)
        {
            Focus();
            _editor.HoverDistributedLoadPlacement(SnapPlacement(point));
            if (_editor.DistributedPointerPosition is { } position
                && (_editor.DistributedFirstEndpoint is null || _editor.DistributedLoadPreview is { IsInvalid: false }))
            {
                _placementClick = position;
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return true;
        }
        return _editor.DistributedLoadState != DistributedLoadInteraction.Neutral;
    }

    private void CaptureDistributedLoad(DistributedLoadVisual visual, DistributedLoadEndpoint endpoint,
        Point point, PointerPressedEventArgs e)
    {
        _layout.BeginInteraction(BeamPointerInteraction.DistributedLoadDrag);
        _distributedGesture = new(visual.Id, endpoint,
            endpoint == DistributedLoadEndpoint.Start ? visual.Preview.StartPosition : visual.Preview.EndPosition,
            point.X, Frame!.Layout.Transform);
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
    }

    private void UpdateDistributedGesture(Point point)
    {
        if (_distributedGesture is null || _editor is null) return;
        var position = _distributedGesture.Update(point.X, point.Y, BeamPane.Bounds.Height);
        if (!_distributedGesture.IsDragging) return;
        if (_editor.DistributedLoadState != DistributedLoadInteraction.Drag)
        {
            Focus();
            if (!_editor.BeginDistributedLoadDrag(_distributedGesture.LoadId, _distributedGesture.Endpoint)) { ReleaseGesture(); return; }
        }
        _editor.UpdateDistributedLoadDrag(position);
    }

    private bool HandleNeutralEntityPress(Point point, PointerPressedEventArgs e)
    {
        if (_editor?.IsEditorNeutral != true || _scene is null) return false;
        var hits = _scene.HitTestEntities(point.X, point.Y);
        if (hits.Count > 1)
        {
            ShowEntitySelection(hits, point);
            e.Handled = true;
            return true;
        }
        if (hits.Count != 1) return false;
        Guid id = hits[0];
        if (_scene.DistributedLoads.FirstOrDefault(l => l.Id == id) is not { } load) return false;
        Focus();
        if (DistributedLoadSymbol.HitEndpoint(load, point.X, point.Y) is { } endpoint)
            CaptureDistributedLoad(load, endpoint, point, e);
        else _editor.EditDistributedLoad(id);
        e.Handled = true;
        return true;
    }

    private void ShowEntitySelection(IReadOnlyList<Guid> ids, Point point)
    {
        SelectionEntries.Children.Clear();
        if (_editor is null || _scene is null) return;
        foreach (Guid id in ids)
        {
            string text = _scene.Annotations.First(a => a.Id == id).Text;
            var button = new Button { Content = text };
            button.Classes.Add("ghost");
            button.Click += (_, _) =>
            {
                SelectionPopup.IsOpen = false;
                if (_editor is not { } editor) return;
                if (editor.Document.Supports.Any(s => s.Id == id)) editor.EditSupport(id);
                else if (editor.Document.DistributedLoads.Any(l => l.Id == id)) editor.EditDistributedLoad(id);
                else editor.EditLoad(id);
            };
            SelectionEntries.Children.Add(button);
        }
        Canvas.SetLeft(SelectionAnchor, point.X);
        Canvas.SetTop(SelectionAnchor, point.Y);
        SelectionPopup.IsOpen = SelectionEntries.Children.Count > 1;
    }

    private void DistributedLoadPopupClosed(object? sender, EventArgs e)
    {
        if (!DistributedLoadPopup.IsOpen && _editor?.IsDistributedLoadFlyoutVisible == true) _editor.CancelDistributedLoadInteraction();
    }
}
