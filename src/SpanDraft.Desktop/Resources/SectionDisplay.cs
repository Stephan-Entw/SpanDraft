using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Resources;

/// <summary>Names actual loaded geometry instead of assuming the initial square-tube template.</summary>
public static class SectionDisplay
{
    public static string Name(ISectionDefinition section, UnitProfile? profile = null, ResultPresentationOptions? options = null)
    {
        var unit = (profile ?? UnitProfile.Default)[QuantityKind.SectionDimension];
        return SectionDefinitionCompatibility.Normalize(section) switch
        {
            RectangleSectionGeometry s => Caption(Strings.ShapeRectangle, unit, options, 0, false, s.Width.Meters, s.Height.Meters),
            RectangularHollowSectionGeometry s => Caption(Strings.ShapeRectangularHollow, unit, options, s.OuterRadius.Meters,
                false, s.Width.Meters, s.Height.Meters, s.WallThickness.Meters),
            CircleSectionGeometry s => Caption(Strings.ShapeCircle, unit, options, 0, true, s.Diameter.Meters),
            CircularHollowSectionGeometry s => Caption(Strings.ShapeCircularHollow, unit, options, 0, true,
                s.OuterDiameter.Meters, s.WallThickness.Meters),
            ISectionGeometry s => Caption(Strings.SectionDefinitionShapeI, unit, options, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            USectionGeometry s => Caption(Strings.ShapeU, unit, options, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            TSectionGeometry s => Caption(Strings.ShapeT, unit, options, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            AngleSectionGeometry s => Caption(Strings.ShapeAngle, unit, options, s.InnerRadius.Meters,
                false, s.Width.Meters, s.Height.Meters, s.Thickness.Meters),
            ManualSectionDefinition => Strings.SectionDefinitionCustomName,
            _ => throw new ArgumentException("Unknown section type.", nameof(section))
        };
    }

    private static string Caption(string shape, UnitDefinition unit, ResultPresentationOptions? options, double radius, bool diameter, params double[] values)
    {
        var name = shape + " " + (diameter ? "Ø" : "")
            + string.Join(" × ", values.Select(v => InputQuantityFormatter.Display(v, unit, options))) + " " + unit.Symbol;
        return radius > 0 ? name + string.Format(CultureInfo.CurrentUICulture, Strings.SectionDefinitionRadiusSuffix,
            InputQuantityFormatter.Display(radius, unit, options), unit.Symbol) : name;
    }
}
