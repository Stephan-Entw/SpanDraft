namespace SpanDraft.Core.Units;

/// <summary>A finite, signed quantity in newtons. The default value represents zero.</summary>
public readonly record struct Force
{
    private Force(double value) => Newtons = DomainGuard.Finite(value, nameof(value));

    public double Newtons { get; }

    public static Force FromNewtons(double value) => new(value);
}
