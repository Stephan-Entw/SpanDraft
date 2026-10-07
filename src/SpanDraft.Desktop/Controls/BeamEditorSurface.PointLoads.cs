using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private PointLoadDraftViewModel? _shownLoadDraft;
    private int _loadPopupFocusVersion;
    private PointLoadDragGesture? _loadGesture;

    private IReadOnlyList<PointLoadVisual> LoadVisuals => _scene?.Loads ?? [];
    private PointLoadVisual? ActiveLoadVisual => LoadVisuals.FirstOrDefault(v => v.IsPreview);

    private static bool OverToolbar(object? source) => source is Control control
        && (control is ToggleButton toggle && toggle.Classes.Contains("toolbar")
            || control.GetVisualAncestors().OfType<ToggleButton>().Any(t => t.Classes.Contains("toolbar")));

    private bool OverLoadFlyout(object? source) => source is Control control && (control == LoadEditor
        || control.GetVisualAncestors().Contains(LoadEditor) || control.GetLogicalAncestors().Contains(LoadEditor));

    private void SynchronizeLoadVisuals()
    {
        if (ActiveLoadVisual is { } visual)
        {
            Canvas.SetLeft(LoadAnchor, visual.X);
            Canvas.SetTop(LoadAnchor, visual.Y + PointLoadSymbol.HalfSize + 8);
        }
        var draft = _editor?.LoadDraft;
        bool show = _editor?.IsLoadFlyoutVisible == true && !_editor.IsBusy;
        if (_shownLoadDraft == draft && LoadPopup.IsOpen == show) return;
        _shownLoadDraft = draft;
        int version = ++_loadPopupFocusVersion;
        LoadPopup.IsOpen = show;
        if (show)
            Dispatcher.UIThread.Post(() =>
            {
                if (_loadPopupFocusVersion == version && _shownLoadDraft == draft && LoadPopup.IsOpen
                    && _editor?.IsLoadFlyoutVisible == true) LoadEditor.FocusPosition();
            }, DispatcherPriority.Input);
        else if (draft is null && TopLevel.GetTopLevel(this) is not null)
            Dispatcher.UIThread.Post(() =>
            {
                if (_loadPopupFocusVersion == version && _shownLoadDraft is null && _editor?.SupportDraft is null && _editor?.DistributedLoadDraft is null
                    && TopLevel.GetTopLevel(this) is not null) Focus();
            }, DispatcherPriority.Input);
    }

    private bool HandleLoadPress(Point point, PointerPressedEventArgs e)
    {
        if (_editor is null || Frame is null) return false;
        if (_editor.IsLoadFlyoutVisible)
        {
            if (ActiveLoadVisual is { } active && PointLoadSymbol.Contains(active, point.X, point.Y))
            {
                if (_editor.LoadDraft?.OriginalId is { } id) CaptureLoad(id, active, point, e);
            }
            else _editor.CancelLoadInteraction();
            e.Handled = true;
            return true;
        }
        if (_editor.LoadState == LoadInteraction.Placement)
        {
            Focus();
            _editor.HoverLoadPlacement(SnapPlacement(point));
            if (_editor.LoadPreview is { IsInvalid: false } preview)
            {
                _placementClick = preview.Position;
                _pointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return true;
        }
        if (_editor.LoadState != LoadInteraction.Neutral) return true;
        if (_editor.Interaction == SupportInteraction.Neutral
            && PointLoadSymbol.HitTest(LoadVisuals, point.X, point.Y) is { } hit)
        {
            Focus();
            CaptureLoad(hit, LoadVisuals.First(v => v.Id == hit), point, e);
            e.Handled = true;
            return true;
        }
        if (_editor.Interaction == SupportInteraction.Neutral && _scene is { } scene
            && PointLoadSymbol.HitTestGlyph(scene.Glyphs, point.X, point.Y) is { } shared)
        {
            ShowSharedSelection(shared, point);
            e.Handled = true;
            return true;
        }
        return false;
    }

    private void ShowSharedSelection(PointLoadGlyph glyph, Point point)
    {
        ShowEntitySelection(glyph.Entities.Where(e => e.Id is not null).Select(e => e.Id!.Value).Distinct().ToArray(), point);
    }

    private void CaptureLoad(Guid id, PointLoadVisual visual, Point point, PointerPressedEventArgs e)
    {
        _layout.BeginInteraction(BeamPointerInteraction.LoadDrag);
        _loadGesture = new(id, visual.Preview.Position, point.X, Frame!.Layout.Transform);
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
    }

    private void UpdateLoadGesture(Point point)
    {
        if (_loadGesture is null || _editor is null) return;
        var position = _loadGesture.Update(point.X, point.Y, BeamPane.Bounds.Height);
        if (!_loadGesture.IsDragging) return;
        if (_editor.LoadState != LoadInteraction.Drag)
        {
            Focus();
            if (!_editor.BeginLoadDrag(_loadGesture.LoadId)) { ReleaseGesture(); return; }
        }
        _editor.UpdateLoadDrag(position);
    }

    private void LoadPopupClosed(object? sender, EventArgs e)
    {
        if (!LoadPopup.IsOpen && _editor?.PreserveDrafts != true && _editor?.IsLoadFlyoutVisible == true) _editor.CancelLoadInteraction();
    }
}
