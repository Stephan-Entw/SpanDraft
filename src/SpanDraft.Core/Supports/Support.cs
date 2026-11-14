using SpanDraft.Core.Units;

namespace SpanDraft.Core.Supports;

/// <summary>
/// A support at x measured from the left beam end towards the right. Rigid supports are created with
/// the constructor, spring supports with <see cref="CreateSpring"/>. A spring support is axially rigid and
/// carries a linear-elastic transverse spring, a rotational spring or both; with a rotational spring
/// the translation may instead be rigid (partial restraint).
/// </summary>
public sealed class Support
{
    public Support(Length position, SupportType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown support type.");
        if (type == SupportType.Spring)
            throw new ArgumentException("A spring support needs its stiffness; use Support.CreateSpring.", nameof(type));

        Position = position;
        Type = type;
    }

    private Support(Length position, TranslationalStiffness? translational, RotationalStiffness? rotational,
        bool rigidTranslation)
    {
        Position = position;
        Type = SupportType.Spring;
        TranslationalSpring = translational;
        RotationalSpring = rotational;
        RigidTranslation = rigidTranslation;
    }

    /// <summary>
    /// Creates a spring support. At least one spring is required. <paramref name="rigidTranslation"/> holds the
    /// transverse translation rigidly (partial restraint) and requires a rotational spring and no translational one.
    /// </summary>
    public static Support CreateSpring(Length position, TranslationalStiffness? translational,
        RotationalStiffness? rotational, bool rigidTranslation = false)
    {
        // A default value type is zero and therefore not a stiffness.
        if (translational is { } t)
            DomainGuard.Positive(t.NewtonsPerMeter, nameof(translational));
        if (rotational is { } r)
            DomainGuard.Positive(r.NewtonMetersPerRadian, nameof(rotational));
        if (translational is null && rotational is null)
            throw new ArgumentException("A spring support needs a translational or a rotational spring.", nameof(translational));
        if (rigidTranslation && translational is not null)
            throw new ArgumentException("A rigid translation excludes a translational spring.", nameof(rigidTranslation));
        if (rigidTranslation && rotational is null)
            throw new ArgumentException("A rigid translation without a rotational spring is a roller.", nameof(rigidTranslation));
        return new(position, translational, rotational, rigidTranslation);
    }

    public Length Position { get; }
    public SupportType Type { get; }

    /// <summary>The transverse spring stiffness; null when there is no translational spring.</summary>
    public TranslationalStiffness? TranslationalSpring { get; }

    /// <summary>The rotational spring stiffness; null when there is no rotational spring.</summary>
    public RotationalStiffness? RotationalSpring { get; }

    /// <summary>True when the transverse translation is rigid and only the rotation is elastic.</summary>
    public bool RigidTranslation { get; }
}
