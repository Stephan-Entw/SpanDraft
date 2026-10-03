namespace SpanDraft.Core.Units;

/// <summary>A finite, non-negative quantity in metres to the fourth power. The default value represents zero.</summary>
public readonly record struct SecondMomentOfArea
{
    private SecondMomentOfArea(double value) => MetersToTheFourth = DomainGuard.NonNegative(value, nameof(value));

    public double MetersToTheFourth { get; }

    public static SecondMomentOfArea FromMetersToTheFourth(double value) => new(value);

    public double MillimetersToTheFourth => MetersToTheFourth * 1000000000000;

    public static SecondMomentOfArea FromMillimetersToTheFourth(double value) =>
        new(DomainGuard.NonNegative(value, nameof(value)) / 1000000000000);
}
