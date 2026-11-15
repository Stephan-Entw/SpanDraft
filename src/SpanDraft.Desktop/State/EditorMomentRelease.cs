using SpanDraft.Core.Releases;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Desktop identity and metadata of an internal moment release (hinge); the ID never enters Core.</summary>
public sealed record EditorMomentRelease(Guid Id, Length Position, string Name)
{
    private string _name = EntityNaming.Normalize(Name);
    public string Name { get => _name; init => _name = EntityNaming.Normalize(value); }

    /// <summary>What <see cref="Position"/> is measured from; null means a fixed distance from the beam start.</summary>
    public PositionRef? Reference { get; init; }
    public PositionRef EffectiveReference => Reference ?? new PositionRef.FromBeamStart(Position);

    public MomentRelease ToCore() => new(Position);
}

public enum MomentReleaseInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }

/// <summary>The transient position of a moment release being placed, edited or dragged.</summary>
public sealed record MomentReleasePreview(Length Position, bool IsInvalid = false);
