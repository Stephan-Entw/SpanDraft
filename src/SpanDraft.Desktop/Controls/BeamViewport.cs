namespace SpanDraft.Desktop.Controls;

/// <summary>Shared drawing/overlay geometry. Beam coordinates use metres; screen coordinates use DIPs.</summary>
public readonly record struct BeamViewport(double Left, double Right, double BeamY, double DimensionY, double LengthMeters)
{
    public static BeamViewport Fit(double width, double height, double lengthMeters)
    {
        double margin = Math.Min(72, Math.Max(0, (width - 1) / 4));
        double beamY = height * 0.5;
        return new(margin, Math.Max(margin + 1, width - margin), beamY, beamY - 40, lengthMeters);
    }

    public double BeamToScreen(double meters) => Left + (meters / LengthMeters) * (Right - Left);
    public double ScreenToBeam(double pixels) => ((pixels - Left) / (Right - Left)) * LengthMeters;
    public double Midpoint => BeamToScreen(LengthMeters / 2);
}
