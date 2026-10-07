using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Resources;

/// <summary>Names actual loaded geometry instead of assuming the initial square-tube template.</summary>
public static class SectionDisplay
{
    public static string Name(Section section) => section switch
    {
        RectangleSection s => Caption(Strings.RectangleSectionName, s.Width.Millimeters, s.Height.Millimeters),
        RectangularHollowSection s => Caption(Strings.RectangularHollowSectionName, s.Width.Millimeters,
            s.Height.Millimeters, s.WallThickness.Millimeters),
        CircleSection s => Caption(Strings.CircleSectionName, s.Diameter.Millimeters),
        CircularHollowSection s => Caption(Strings.CircularHollowSectionName, s.OuterDiameter.Millimeters, s.WallThickness.Millimeters),
        CustomSection => Strings.CustomSectionName,
        _ => throw new ArgumentException("Unknown section type.", nameof(section))
    };

    private static string Caption(string format, params double[] values) =>
        string.Format(CultureInfo.CurrentUICulture, format, values.Select(v => (object)UiNumbers.Format(v)).ToArray());
}
