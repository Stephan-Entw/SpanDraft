using System.Collections.ObjectModel;

namespace SpanDraft.Desktop.State;

/// <summary>Finite DIPs relative to the current automatic entity anchor, never absolute canvas coordinates.</summary>
public readonly record struct AnnotationOffset
{
    public AnnotationOffset(double dx, double dy)
    {
        if (!double.IsFinite(dx)) throw new ArgumentOutOfRangeException(nameof(dx));
        if (!double.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dy));
        Dx = dx;
        Dy = dy;
    }
    public double Dx { get; }
    public double Dy { get; }
}

/// <summary>Immutable presentation data belonging to an open project session. Missing offset means Auto.</summary>
public sealed class EditorPresentationState
{
    public EditorPresentationState(IEnumerable<KeyValuePair<Guid, AnnotationOffset>>? offsets = null) =>
        AnnotationOffsets = new ReadOnlyDictionary<Guid, AnnotationOffset>((offsets ?? []).ToDictionary());

    public IReadOnlyDictionary<Guid, AnnotationOffset> AnnotationOffsets { get; }

    public EditorPresentationState WithOffset(Guid id, AnnotationOffset? offset)
    {
        if (offset is { } value && AnnotationOffsets.TryGetValue(id, out var current) && current == value
            || offset is null && !AnnotationOffsets.ContainsKey(id)) return this;
        var copy = AnnotationOffsets.ToDictionary();
        if (offset is { } next) copy[id] = next; else copy.Remove(id);
        return new(copy);
    }

    public EditorPresentationState RetainEntities(EditorDocument document)
    {
        var ids = document.NamedEntities.Select(e => e.Id).ToHashSet();
        return AnnotationOffsets.Keys.All(ids.Contains) ? this
            : new(AnnotationOffsets.Where(e => ids.Contains(e.Key)));
    }

    public bool ContentEquals(EditorPresentationState other) => AnnotationOffsets.Count == other.AnnotationOffsets.Count
        && AnnotationOffsets.All(e => other.AnnotationOffsets.TryGetValue(e.Key, out var value) && value == e.Value);
}
