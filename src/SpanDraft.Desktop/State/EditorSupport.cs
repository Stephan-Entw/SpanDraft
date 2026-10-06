using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Immutable document identity; the ID never enters Core.</summary>
public sealed record EditorSupport(Guid Id, Length Position, SupportType Type, string Name)
{
    private string _name = EntityNaming.Normalize(Name);
    public string Name { get => _name; init => _name = EntityNaming.Normalize(value); }
}

public sealed record SupportPreview(Length Position, SupportType Type, bool IsInvalid = false);

public enum SupportInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
