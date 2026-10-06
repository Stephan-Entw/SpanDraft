namespace SpanDraft.Desktop.Layout;

/// <summary>Immutable piecewise-linear reversible mapping; never clamps its caller's coordinates.</summary>
public sealed class StationTransform
{
    public StationTransform(IEnumerable<LayoutStation> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);
        var copy = stations.ToArray();
        if (copy.Length < 2) throw new ArgumentException("At least two stations are required.", nameof(stations));
        for (int i = 0; i < copy.Length; i++)
        {
            LayoutNumbers.Finite(copy[i].PhysicalX, nameof(stations));
            LayoutNumbers.Finite(copy[i].ScreenX, nameof(stations));
            LayoutNumbers.NonNegative(copy[i].LeftExtent, nameof(stations));
            LayoutNumbers.NonNegative(copy[i].RightExtent, nameof(stations));
            if (i > 0 && (copy[i].PhysicalX <= copy[i - 1].PhysicalX || copy[i].ScreenX <= copy[i - 1].ScreenX))
                throw new ArgumentException("Both coordinate sequences must be strictly increasing.", nameof(stations));
            if (i > 0)
            {
                LayoutNumbers.Positive(copy[i].PhysicalX - copy[i - 1].PhysicalX, nameof(stations));
                LayoutNumbers.Positive(copy[i].ScreenX - copy[i - 1].ScreenX, nameof(stations));
            }
        }
        Stations = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<LayoutStation> Stations { get; }
    public double PhysicalToScreen(double x) => Map(x, false);
    public double ScreenToPhysical(double screenX) => Map(screenX, true);

    private double Map(double value, bool inverse)
    {
        LayoutNumbers.Finite(value, nameof(value));
        double From(int i) => inverse ? Stations[i].ScreenX : Stations[i].PhysicalX;
        double To(int i) => inverse ? Stations[i].PhysicalX : Stations[i].ScreenX;
        int low = 0, high = Stations.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (value == From(middle)) return To(middle);
            if (value < From(middle)) high = middle - 1; else low = middle + 1;
        }
        int segment = Math.Clamp(high, 0, Stations.Count - 2);
        double t = (value - From(segment)) / (From(segment + 1) - From(segment));
        return LayoutNumbers.Finite(To(segment) + t * (To(segment + 1) - To(segment)), nameof(value));
    }
}
