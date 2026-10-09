namespace SpanDraft.Core.Materials;

/// <summary>A finite Poisson ratio strictly between -1 and 0.5. Zero is valid.</summary>
public readonly record struct PoissonRatio
{
    private PoissonRatio(double value)
    {
        DomainGuard.Finite(value, nameof(value));
        if (value <= -1 || value >= .5)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Poisson ratio must be strictly between -1 and 0.5.");
        Value = value;
    }

    public double Value { get; }

    public static PoissonRatio FromValue(double value) => new(value);
}
