using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private int _selfWeightPopupFocusVersion;
    private bool OverSelfWeightFlyout(object? source) => source is Control control && (control == SelfWeightEditor
        || control.GetVisualAncestors().Contains(SelfWeightEditor) || control.GetLogicalAncestors().Contains(SelfWeightEditor));

    private void SynchronizeSelfWeightVisuals()
    {
        if (Frame is { } frame && _editor is { } editor)
        {
            Canvas.SetLeft(SelfWeightAnchor, frame.Layout.Transform.PhysicalToScreen(editor.Document.Length.Meters / 2));
            Canvas.SetTop(SelfWeightAnchor, frame.Viewport.BeamY + 28);
        }
        bool show = _editor?.IsSelfWeightFlyoutVisible == true && !_editor.IsBusy;
        if (SelfWeightPopup.IsOpen == show) return;
        int version = ++_selfWeightPopupFocusVersion;
        SelfWeightPopup.IsOpen = show;
        Dispatcher.UIThread.Post(() =>
        {
            if (_selfWeightPopupFocusVersion != version || TopLevel.GetTopLevel(this) is null) return;
            if (show && _editor?.IsSelfWeightFlyoutVisible == true) SelfWeightEditor.FocusAction();
            else if (_editor?.IsEditorNeutral == true) Focus();
        }, DispatcherPriority.Input);
    }

    private bool HandleSelfWeightPress(Point point, PointerPressedEventArgs e)
    {
        if (_editor?.IsSelfWeightFlyoutVisible != true) return false;
        var visual = _scene?.DistributedLoads.FirstOrDefault(l => l.Id == _editor.Document.SelfWeight?.Id);
        if (visual is null || !DistributedLoadSymbol.Contains(visual, point.X, point.Y)) _editor.CancelSelfWeightInteraction();
        e.Handled = true;
        return true;
    }

    private void SelfWeightPopupClosed(object? sender, EventArgs e)
    {
        if (!SelfWeightPopup.IsOpen && _editor?.PreserveDrafts != true && _editor?.IsSelfWeightFlyoutVisible == true)
            _editor.CancelSelfWeightInteraction();
    }
}
