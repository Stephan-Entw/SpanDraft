namespace SpanDraft.Solver;

/// <summary>A finite, signed displacement in metres. The default value is zero.</summary>
public readonly record struct Displacement
{
    private Displacement(double meters)
    {
        if (!double.IsFinite(meters))
            throw new ArgumentOutOfRangeException(nameof(meters), meters, "Displacement must be finite.");
        Meters = meters;
    }

    public double Meters { get; }

    public static Displacement FromMeters(double meters) => new(meters);
}
