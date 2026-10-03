namespace SpanDraft.Core.Units;

/// <summary>A finite, signed quantity in newtons per metre. The default value represents zero.</summary>
public readonly record struct ForcePerLength
{
    private ForcePerLength(double value) => NewtonsPerMeter = DomainGuard.Finite(value, nameof(value));

    public double NewtonsPerMeter { get; }

    public static ForcePerLength FromNewtonsPerMeter(double value) => new(value);
}
