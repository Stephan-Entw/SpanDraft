namespace SpanDraft.Core.Units;

/// <summary>A finite, signed product moment of area, integral(y*z dA), in metres to the fourth power.</summary>
public readonly record struct ProductMomentOfArea
{
    private ProductMomentOfArea(double value) => MetersToTheFourth = DomainGuard.Finite(value, nameof(value));

    public double MetersToTheFourth { get; }
    public double MillimetersToTheFourth => MetersToTheFourth * 1000000000000;

    public static ProductMomentOfArea FromMetersToTheFourth(double value) => new(value);
    public static ProductMomentOfArea FromMillimetersToTheFourth(double value) =>
        new(DomainGuard.Finite(value, nameof(value)) / 1000000000000);
}