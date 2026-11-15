using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>The springs of a <see cref="SupportType.Spring"/> support; see <see cref="Support.CreateSpring"/>.</summary>
public sealed record SpringData(TranslationalStiffness? Translational, RotationalStiffness? Rotational,
    bool RigidTranslation = false)
{
    public SpringData(TranslationalStiffness translational) : this(translational, null) { }

    /// <summary>Checks the spring combination by creating the Core support once.</summary>
    internal Support ToCore(Length position) => Support.CreateSpring(position, Translational, Rotational, RigidTranslation);
}

/// <summary>Immutable document identity; the ID never enters Core.</summary>
public sealed record EditorSupport(Guid Id, Length Position, SupportType Type, string Name, SpringData? Spring = null)
{
    private string _name = EntityNaming.Normalize(Name);
    public string Name { get => _name; init => _name = EntityNaming.Normalize(value); }

    /// <summary>
    /// What <see cref="Position"/> is measured from; null means a fixed distance from the beam start. For a
    /// segment reference <see cref="Position"/> is the global x derived by the document, never a second truth.
    /// </summary>
    public PositionRef? Reference { get; init; }
    public PositionRef EffectiveReference => Reference ?? new PositionRef.FromBeamStart(Position);
}

public sealed record SupportPreview(Length Position, SupportType Type, bool IsInvalid = false, SpringData? Spring = null);

public enum SupportInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
