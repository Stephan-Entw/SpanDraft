using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

internal static class AppTypography
{
    internal static readonly FontFamily Family = new("avares://SpanDraft.Desktop/Assets/Fonts#IBM Plex Sans");
    internal static readonly Typeface Typeface = new(Family);
    internal static readonly FontFeatureCollection TabularFigures = FontFeatureCollection.Parse("tnum");
    internal static double Size(string key) => Application.Current?.TryFindResource(key, out var value) == true && value is double size
        ? size : throw new InvalidOperationException("Missing typography token: " + key);
}
