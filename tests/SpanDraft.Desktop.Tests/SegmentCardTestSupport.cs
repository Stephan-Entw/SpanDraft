using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SpanDraft.Tests;

/// <summary>
/// The segment cards live in an item template, so their named parts are not in the name scope of the view; they
/// are found in the visual tree below the card instead.
/// </summary>
internal static class SegmentCardTestSupport
{
    public static IReadOnlyList<Border> Cards(this Control root) => root.GetVisualDescendants().OfType<Border>()
        .Where(border => border.Name == "BeamCardSurface").ToArray();

    public static Border FirstCard(this Control root) => root.Cards().First();

    public static T Part<T>(this Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);
}
