using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface
{
    private LabelDragGesture? _labelGesture;
    private double? _labelPaneHeight;
    private VerticalAlignment _labelPaneAlignment;
    private static readonly Cursor LabelDragCursor = new(StandardCursorType.SizeAll);

    private void FreezeLabelPane()
    {
        _labelPaneHeight = BeamPane.Height;
        _labelPaneAlignment = BeamPane.VerticalAlignment;
        BeamPane.VerticalAlignment = VerticalAlignment.Top;
        BeamPane.Height = Frame!.Viewport.Height;
    }

    private void RestoreLabelPane()
    {
        if (_labelPaneHeight is not { } height) return;
        _labelPaneHeight = null;
        BeamPane.Height = height;
        BeamPane.VerticalAlignment = _labelPaneAlignment;
    }

    private void UpdateLabelGesture(Point point)
    {
        if (_editor is { } editor) _labelGesture?.Apply(editor, point);
    }

    private void FinishLabelGesture(Point point)
    {
        UpdateLabelGesture(point);
        var gesture = _labelGesture!;
        // Clear before releasing capture: the resulting CaptureLost is not Cancel.
        _labelGesture = null;
        RestoreLabelPane();
        ReleaseGesture();
        if (_editor is { } editor) gesture.OpenOnClick(editor);
        SynchronizeVisuals();
    }

    private bool CancelLabelGesture()
    {
        if (_labelGesture is not { } gesture) return false;
        _labelGesture = null;
        // Do not call the generic editor cancel path: an existing draft owns its
        // buffers. Clear first to prevent synchronous notifications re-entering.
        if (_editor is { } editor) gesture.Cancel(editor);
        RestoreLabelPane();
        ReleaseGesture();
        SynchronizeVisuals();
        return true;
    }

    private void LabelGestureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CancelLabelGesture()) e.Handled = true;
    }
}
