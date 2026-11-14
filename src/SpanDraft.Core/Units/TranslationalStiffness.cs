namespace SpanDraft.Core.Units;

/// <summary>A finite, strictly positive translational spring stiffness in N/m. The zero default is invalid.</summary>
public readonly record struct TranslationalStiffness
{
    private TranslationalStiffness(double value) => NewtonsPerMeter = DomainGuard.Positive(value, nameof(value));

    public double NewtonsPerMeter { get; }

    public static TranslationalStiffness FromNewtonsPerMeter(double value) => new(value);
}
