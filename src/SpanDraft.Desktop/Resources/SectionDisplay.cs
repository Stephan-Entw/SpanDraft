using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Resources;

/// <summary>Names actual loaded geometry instead of assuming the initial square-tube template.</summary>
public static class SectionDisplay
{
    public static string Name(Section section, UnitProfile? profile = null)
    {
        var unit = (profile ?? UnitProfile.Default)[QuantityKind.SectionDimension];
        return section switch
        {
            RectangleSection s => Caption(Strings.RectangleSectionName, unit, s.Width.Meters, s.Height.Meters),
            RectangularHollowSection s => Caption(Strings.RectangularHollowSectionName, unit, s.Width.Meters,
                s.Height.Meters, s.WallThickness.Meters),
            CircleSection s => Caption(Strings.CircleSectionName, unit, s.Diameter.Meters),
            CircularHollowSection s => Caption(Strings.CircularHollowSectionName, unit, s.OuterDiameter.Meters, s.WallThickness.Meters),
            CustomSection => Strings.CustomSectionName,
            _ => throw new ArgumentException("Unknown section type.", nameof(section))
        };
    }

    private static string Caption(string format, UnitDefinition unit, params double[] values) =>
        string.Format(CultureInfo.CurrentUICulture, format,
            values.Select(v => (object)InputQuantityFormatter.Display(v, unit)).Append(unit.Symbol).ToArray());
}
