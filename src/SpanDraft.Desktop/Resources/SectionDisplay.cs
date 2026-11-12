using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Resources;

/// <summary>Names actual loaded geometry instead of assuming the initial square-tube template.</summary>
public static class SectionDisplay
{
    public static string Name(ISectionDefinition section, ResultPresentationOptions? options = null)
    {
        options ??= ResultPresentationOptions.Default;
        var unit = options.Profile[QuantityKind.SectionDimension];
        string Caption(string shape, double radius, bool diameter, params double[] values)
        {
            var name = shape + " " + (diameter ? "Ø" : "") + Dimensions(values, unit, options);
            return radius > 0 ? name + RadiusSuffix(radius, unit, options) : name;
        }
        return SectionDefinitionCompatibility.Normalize(section) switch
        {
            RectangleSectionGeometry s => Caption(Strings.ShapeRectangle, 0, false, s.Width.Meters, s.Height.Meters),
            RectangularHollowSectionGeometry s => Caption(Strings.ShapeRectangularHollow, s.OuterRadius.Meters,
                false, s.Width.Meters, s.Height.Meters, s.WallThickness.Meters),
            CircleSectionGeometry s => Caption(Strings.ShapeCircle, 0, true, s.Diameter.Meters),
            CircularHollowSectionGeometry s => Caption(Strings.ShapeCircularHollow, 0, true,
                s.OuterDiameter.Meters, s.WallThickness.Meters),
            ISectionGeometry s => Caption(Strings.SectionDefinitionShapeI, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            USectionGeometry s => Caption(Strings.ShapeU, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            TSectionGeometry s => Caption(Strings.ShapeT, s.Radius.Meters,
                false, s.Height.Meters, s.Width.Meters, s.WebThickness.Meters, s.FlangeThickness.Meters),
            AngleSectionGeometry s => Caption(Strings.ShapeAngle, s.InnerRadius.Meters,
                false, s.Width.Meters, s.Height.Meters, s.Thickness.Meters),
            ManualSectionDefinition => Strings.SectionDefinitionCustomName,
            _ => throw new ArgumentException("Unknown section type.", nameof(section))
        };
    }

    /// <summary>
    /// Area, second moment and section modulus are given values when the section stores them
    /// (manual and legacy custom sections) and results when they are derived from geometry.
    /// </summary>
    public static string Property(ISectionDefinition section, double siValue, QuantityKind kind, ResultPresentationOptions options) =>
        Property(SectionDefinitionCompatibility.Normalize(section) is ManualSectionDefinition, siValue, kind, options);

    internal static string Property(bool isGiven, double siValue, QuantityKind kind, ResultPresentationOptions options) => isGiven
        ? QuantityFormatter.FormatInput(siValue, options.Profile[kind], options)
        : QuantityFormatter.FormatResult(siValue, kind, options);

    /// <summary>Dimensions are given values: " × "-joined numbers, each marked when rounded, followed by the unit.</summary>
    internal static string Dimensions(IEnumerable<double> meters, UnitDefinition unit, ResultPresentationOptions options) =>
        string.Join(" × ", meters.Select(v => QuantityFormatter.FormatInputNumber(v, unit, options))) + " " + unit.Symbol;

    internal static string RadiusSuffix(double meters, UnitDefinition unit, ResultPresentationOptions options) =>
        string.Format(CultureInfo.CurrentUICulture, Strings.SectionDefinitionRadiusSuffix,
            QuantityFormatter.FormatInputNumber(meters, unit, options), unit.Symbol);
}
