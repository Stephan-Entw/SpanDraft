using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private MomentReleaseDraftViewModel? _shownReleaseDraft;
    private int _releasePopupFocusVersion;
    private SupportDragGesture? _releaseGesture;

    private MomentReleaseVisual? ActiveReleaseVisual => _scene?.MomentReleases.FirstOrDefault(v => v.IsPreview);

    /// <summary>The ring never snaps to a beam end: it must lie strictly inside the beam.</summary>
    private Length? SnapRelease(Point point) => Frame is { } frame
        ? SupportSnap.ReleasePlacement(frame.Layout.Transform, frame.Viewport.BeamY, point.X, point.Y) : null;

    private bool OverReleaseFlyout(object? source) => source is Control control && (control == MomentReleaseEditor
        || control.GetVisualAncestors().Contains(MomentReleaseEditor) || control.GetLogicalAncestors().Contains(MomentReleaseEditor));

    private bool OverActiveRelease(Point point) => ActiveReleaseVisual is { } active && Frame is { } frame
        && MomentReleaseSymbol.Contains(active, frame.Viewport.BeamY, point.X, point.Y);

    private void SynchronizeReleaseVisuals()
    {
        if (ActiveReleaseVisual is { } visual && Frame is { } frame)
        {
            Canvas.SetLeft(MomentReleaseAnchor, visual.X);
            Canvas.SetTop(MomentReleaseAnchor, frame.Viewport.BeamY + 56);
        }
        var draft = _editor?.MomentReleaseDraft;
        bool show = _editor?.IsMomentReleaseFlyoutVisible == true && !_editor.IsBusy;
        if (_shownReleaseDraft == draft && MomentReleasePopup.IsOpen == show) return;
        _shownReleaseDraft = draft;
        int version = ++_releasePopupFocusVersion;
        MomentReleasePopup.IsOpen = show;
        if (show)
            Dispatcher.UIThread.Post(() =>
            {
                if (_releasePopupFocusVersion == version && _shownReleaseDraft == draft && MomentReleasePopup.IsOpen
                    && _editor?.IsMomentReleaseFlyoutVisible == true) MomentReleaseEditor.FocusPosition();
            }, DispatcherPriority.Input);
        else if (draft is null && TopLevel.GetTopLevel(this) is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (_releasePopupFocusVersion == version && _shownReleaseDraft is null && _editor?.SupportDraft is null
                    && _editor?.LoadDraft is null && _editor?.DistributedLoadDraft is null
                    && _editor?.IsSelfWeightFlyoutVisible != true && TopLevel.GetTopLevel(this) is not null) Focus();
            }, DispatcherPriority.Input);
    }

    private bool HandleReleasePress(Point point, PointerPressedEventArgs e)
    {
        if (_editor is null || Frame is null) return false;
        if (_editor.IsMomentReleaseFlyoutVisible)
        {
            if (OverActiveRelease(point))
            {
                if (_editor.MomentReleaseDraft?.OriginalId is { } id) CaptureRelease(id, ActiveReleaseVisual!, point, e);
            }
            else _editor.CancelMomentReleaseInteraction();
            e.Handled = true;
            return true;
        }
        if (_editor.MomentReleaseState == MomentReleaseInteraction.Placement)
        {
            Focus();
            _editor.HoverMomentReleasePlacement(SnapRelease(point));
            if (_editor.MomentReleasePreview is { IsInvalid: false } preview)
            {
                _placementClick = preview.Position;
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return true;
        }
        return _editor.MomentReleaseState != MomentReleaseInteraction.Neutral;
    }

    /// <summary>A press on the ring itself in the neutral state grabs the hinge, even over a support symbol.</summary>
    private bool HandleNeutralReleasePress(Point point, PointerPressedEventArgs e)
    {
        if (_editor is null || _scene is null || _editor.MomentReleaseState != MomentReleaseInteraction.Neutral) return false;
        var hits = _scene.HitTestEntities(point.X, point.Y);
        if (hits.Count != 1 || _scene.MomentReleases.FirstOrDefault(r => r.Id == hits[0]) is not { } release) return false;
        Focus();
        CaptureRelease(hits[0], release, point, e);
        e.Handled = true;
        return true;
    }

    private void CaptureRelease(Guid id, MomentReleaseVisual visual, Point point, PointerPressedEventArgs e)
    {
        _layout.BeginInteraction(BeamPointerInteraction.MomentReleaseDrag);
        _releaseGesture = new(id, visual.Preview.Position, point.X, Frame!.Layout.Transform, endpointRadius: 0);
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
    }

    private void UpdateReleaseGesture(Point point)
    {
        if (_releaseGesture is null || _editor is null) return;
        var position = _releaseGesture.Update(point.X, point.Y, BeamPane.Bounds.Height);
        if (!_releaseGesture.IsDragging) return;
        if (_editor.MomentReleaseState != MomentReleaseInteraction.Drag)
        {
            Focus();
            if (!_editor.BeginMomentReleaseDrag(_releaseGesture.SupportId)) { ReleaseGesture(); return; }
        }
        _editor.UpdateMomentReleaseDrag(position);
    }

    private void FinishReleaseGesture(Point point)
    {
        UpdateReleaseGesture(point);
        var gesture = _releaseGesture;
        if (gesture is not null && _editor is not null)
        {
            if (gesture.IsDragging) _editor.EndMomentReleaseDrag();
            else _editor.EditMomentRelease(gesture.SupportId);
        }
        ReleaseGesture();
        SynchronizeVisuals();
    }

    private void MomentReleasePopupClosed(object? sender, EventArgs e)
    {
        if (!MomentReleasePopup.IsOpen && _editor?.PreserveDrafts != true && _editor?.IsMomentReleaseFlyoutVisible == true)
            _editor.CancelMomentReleaseInteraction();
    }
}
