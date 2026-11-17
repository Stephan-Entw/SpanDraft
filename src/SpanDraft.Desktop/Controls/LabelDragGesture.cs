using Avalonia;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

/// <summary>One screen-space annotation gesture. Contains no document or committed state.</summary>
public sealed class LabelDragGesture(EntityAnnotation label, Point press, AnnotationOffset? originalOffset = null)
{
    public IReadOnlyList<Guid> EntityIds { get; } = label.EntityIds.Count > 0 ? label.EntityIds.ToArray()
        : throw new ArgumentException("A committed entity is required.", nameof(label));
    public bool IsSupport { get; } = label.IsSupport;
    public AnnotationOffset? OriginalOffset { get; } = originalOffset;
    public Rect AutoBounds { get; } = label.AutoBounds;
    public Rect StartBounds { get; } = label.Bounds;
    public bool IsDragging { get; private set; }
    private AnnotationOffset? _pendingOffset;

    public AnnotationOffset? Update(Point pointer)
    {
        var delta = new Vector(pointer.X - press.X, pointer.Y - press.Y);
        double dx = StartBounds.X - AutoBounds.X + delta.X;
        double dy = StartBounds.Y - AutoBounds.Y + delta.Y;
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || !double.IsFinite(dx) || !double.IsFinite(dy)) return null;
        if (delta.Length >= SupportDragGesture.Threshold) IsDragging = true;
        return IsDragging ? new AnnotationOffset(dx, dy) : null;
    }

    public void Apply(EditorViewModel editor, Point pointer)
    {
        if (Update(pointer) is { } offset)
        {
            _pendingOffset = offset;
            editor.PreviewAnnotationOffsets(EntityIds, offset);
        }
    }

    public bool OpenOnClick(EditorViewModel editor)
    {
        if (IsDragging || EntityIds.Count != 1) return false;
        var id = EntityIds[0];
        return editor.Document.SelfWeight?.Id == id ? editor.EditSelfWeight(id)
            : editor.Document.MomentReleases.Any(r => r.Id == id) ? editor.EditMomentRelease(id)
            : IsSupport ? editor.EditSupport(id) : editor.Document.DistributedLoads.Any(l => l.Id == id)
            ? editor.EditDistributedLoad(id) : editor.EditLoad(id);
    }

    public void Complete(EditorViewModel editor)
    {
        editor.ClearAnnotationPreview();
        if (_pendingOffset is { } offset) editor.SetAnnotationOffsets(EntityIds, offset);
        _pendingOffset = null;
    }

    public void Cancel(EditorViewModel editor)
    {
        _pendingOffset = null;
        editor.ClearAnnotationPreview();
    }
}
