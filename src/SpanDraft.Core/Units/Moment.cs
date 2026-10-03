namespace SpanDraft.Core.Units;

/// <summary>A finite, signed quantity in newton metres. The default value represents zero.</summary>
public readonly record struct Moment
{
    private Moment(double value) => NewtonMeters = DomainGuard.Finite(value, nameof(value));

    public double NewtonMeters { get; }

    public static Moment FromNewtonMeters(double value) => new(value);

    public double NewtonMillimeters => NewtonMeters * 1000;

    public static Moment FromNewtonMillimeters(double value) =>
        new(DomainGuard.Finite(value, nameof(value)) / 1000);
}
