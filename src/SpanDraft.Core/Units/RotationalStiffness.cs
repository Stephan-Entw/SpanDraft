namespace SpanDraft.Core.Units;

/// <summary>A finite, strictly positive rotational spring stiffness in N·m/rad. The zero default is invalid.</summary>
public readonly record struct RotationalStiffness
{
    private RotationalStiffness(double value) => NewtonMetersPerRadian = DomainGuard.Positive(value, nameof(value));

    public double NewtonMetersPerRadian { get; }

    public static RotationalStiffness FromNewtonMetersPerRadian(double value) => new(value);
}
