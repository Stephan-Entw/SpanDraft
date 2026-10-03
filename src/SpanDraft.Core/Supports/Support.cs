using SpanDraft.Core.Units;

namespace SpanDraft.Core.Supports;

/// <summary>A support at x measured from the left beam end towards the right.</summary>
public sealed class Support
{
    public Support(Length position, SupportType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown support type.");

        Position = position;
        Type = type;
    }

    public Length Position { get; }
    public SupportType Type { get; }
}
