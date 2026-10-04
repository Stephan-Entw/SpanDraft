using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Immutable document identity; the ID never enters Core.</summary>
public sealed record EditorSupport(Guid Id, Length Position, SupportType Type);

public sealed record SupportPreview(Length Position, SupportType Type, bool IsInvalid = false);

public enum SupportInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
