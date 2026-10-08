using System.Globalization;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopPresentationUnitTests
{
    public static IEnumerable<object[]> Units()
    {
        const double inch = 0.0254;
        const double foot = 0.3048;
        const double kip = 4448.2216152605;
        (UnitDefinition Unit, double Scale)[] units =
        [
            (UnitCatalog.Meter, 1), (UnitCatalog.Millimeter, 0.001),
            (UnitCatalog.Foot, foot), (UnitCatalog.Inch, inch),
            (UnitCatalog.SquareMeter, 1), (UnitCatalog.SquareMillimeter, 1e-6),
            (UnitCatalog.SquareCentimeter, 1e-4), (UnitCatalog.SquareInch, inch * inch),
            (UnitCatalog.MeterToFourth, 1), (UnitCatalog.MillimeterToFourth, 1e-12),
            (UnitCatalog.CentimeterToFourth, 1e-8), (UnitCatalog.InchToFourth, Math.Pow(inch, 4)),
            (UnitCatalog.CubicMeter, 1), (UnitCatalog.CubicMillimeter, 1e-9),
            (UnitCatalog.CubicCentimeter, 1e-6), (UnitCatalog.CubicInch, Math.Pow(inch, 3)),
            (UnitCatalog.Radian, 1), (UnitCatalog.Newton, 1), (UnitCatalog.Kilonewton, 1000),
            (UnitCatalog.Kip, kip), (UnitCatalog.NewtonMeter, 1), (UnitCatalog.KilonewtonMeter, 1000),
            (UnitCatalog.KipFoot, kip * foot), (UnitCatalog.NewtonPerMeter, 1),
            (UnitCatalog.KilonewtonPerMeter, 1000), (UnitCatalog.KipPerFoot, kip / foot),
            (UnitCatalog.Pascal, 1), (UnitCatalog.Megapascal, 1e6),
            (UnitCatalog.Ksi, kip / (inch * inch)), (UnitCatalog.Dimensionless, 1)
        ];
        foreach (var (unit, scale) in units) yield return [unit, scale];
    }

    [Theory]
    [MemberData(nameof(Units))]
    public void EveryUnitConvertsBothDirectionsAndPreservesSigns(UnitDefinition unit, double expectedScale)
    {
        Close(expectedScale, unit.SiUnitsPerUnit);
        foreach (double value in new[] { -1234.5678, -1, 0, 1, 1234.5678 })
        {
            Close(value * expectedScale, unit.ToSi(value));
            Close(value, unit.FromSi(value * expectedScale));
            Close(value, unit.FromSi(unit.ToSi(value)));
        }
    }

    [Fact]
    public void CatalogIsCompleteUniqueAndReadOnly()
    {
        Assert.Equal(Units().Count(), UnitCatalog.All.Count);
        Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(unit => unit.Id).Distinct().Count());
        Assert.Equal(UnitCatalog.All.Count, UnitCatalog.All.Select(unit => unit.Symbol).Distinct().Count());
        Assert.Throws<NotSupportedException>(() => ((IList<UnitDefinition>)UnitCatalog.All).Clear());
        Assert.All(Enum.GetValues<QuantityKind>(), kind => Assert.Equal(UnitCatalog.DimensionOf(kind), UnitProfile.Default[kind].Dimension));
    }

    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering, "mm|mm|mm|mm|mm²|mm⁴|mm³|rad|N|N|N·m|N/m|MPa|")]
    [InlineData(UnitProfileKind.StructuralEngineering, "m|mm|mm|mm|cm²|cm⁴|cm³|rad|kN|kN|kN·m|kN/m|MPa|")]
    [InlineData(UnitProfileKind.UnitedStates, "ft|in|in|in|in²|in⁴|in³|rad|kip|kip|kip·ft|kip/ft|ksi|")]
    public void BuiltInProfileMatchesSpecification(UnitProfileKind kind, string expected)
    {
        var profile = kind == UnitProfileKind.MechanicalEngineering ? UnitProfile.MechanicalEngineering
            : kind == UnitProfileKind.StructuralEngineering ? UnitProfile.StructuralEngineering : UnitProfile.UnitedStates;
        Assert.Equal(kind, profile.Kind);
        Assert.Equal(expected, string.Join('|', Enum.GetValues<QuantityKind>().Select(quantity => profile[quantity].Symbol)));
    }

    [Fact]
    public void CustomProfileCopiesInputAndChangesOneQuantityAtATime()
    {
        Assert.Same(UnitProfile.MechanicalEngineering, UnitProfile.Default);
        var source = new Dictionary<QuantityKind, UnitDefinition>(UnitProfile.Default.Units);
        var custom = new UnitProfile(source);
        source[QuantityKind.BeamLength] = UnitCatalog.Foot;
        Assert.Same(UnitCatalog.Millimeter, custom[QuantityKind.BeamLength]);
        var mixed = custom.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot)
            .WithUnit(QuantityKind.TransverseDisplacement, UnitCatalog.Inch)
            .WithUnit(QuantityKind.TransverseForce, UnitCatalog.Kip);
        Assert.Equal(UnitProfileKind.Custom, mixed.Kind);
        Assert.Same(UnitCatalog.Foot, mixed[QuantityKind.BeamLength]);
        Assert.Same(UnitCatalog.Inch, mixed[QuantityKind.TransverseDisplacement]);
        Assert.Same(UnitCatalog.Millimeter, mixed[QuantityKind.AxialDisplacement]);
        Assert.Same(UnitCatalog.Kip, mixed[QuantityKind.TransverseForce]);
        Assert.Same(UnitCatalog.Newton, mixed[QuantityKind.AxialForce]);
        Assert.Same(UnitCatalog.Millimeter, UnitProfile.Default[QuantityKind.BeamLength]);
        Assert.Equal(UnitProfileKind.Custom, UnitProfile.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Millimeter).Kind);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<QuantityKind, UnitDefinition>)mixed.Units).Clear());
    }

    [Fact]
    public void InvalidProfilesAndConversionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => UnitProfile.Default.WithUnit(QuantityKind.Area, UnitCatalog.Millimeter));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitProfile.Default.WithUnit((QuantityKind)99, UnitCatalog.Meter));
        var units = new Dictionary<QuantityKind, UnitDefinition>(UnitProfile.Default.Units);
        units.Remove(QuantityKind.Area);
        Assert.Throws<ArgumentException>(() => new UnitProfile(units));
        units[(QuantityKind)99] = UnitCatalog.SquareMeter;
        Assert.Throws<ArgumentException>(() => new UnitProfile(units));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitCatalog.Newton.ToSi(double.NaN));
        Assert.Throws<OverflowException>(() => UnitCatalog.Millimeter.FromSi(double.MaxValue));
        Assert.Throws<OverflowException>(() => UnitCatalog.Millimeter.ToSi(double.Epsilon));
    }

    [Theory]
    [InlineData("de-DE", "1,02 kip", "0,0003 in", "301·10⁻⁶ in")]
    [InlineData("en-US", "1.02 kip", "0.0003 in", "301·10⁻⁶ in")]
    public void UsPrecisionIsChosenInDisplayUnitBeforeAnyRounding(string culture, string force, string displacement, string detailed)
    {
        var references = new ModelReferenceValues(0.05, 5000, 1000, 235e6);
        var c = CultureInfo.GetCultureInfo(culture);
        // 4514.96 N rounds to 4510 N in SI, whose converted value would round to 1.01 kip.
        Assert.Equal(force, QuantityFormatter.Format(4514.96, QuantityKind.TransverseForce,
            UnitProfile.UnitedStates, references: references, culture: c));
        Assert.Equal(displacement, QuantityFormatter.Format(0.00765 / 1000, QuantityKind.TransverseDisplacement,
            UnitProfile.UnitedStates, references: references, culture: c));
        Assert.Equal(detailed, QuantityFormatter.Format(0.00765 / 1000, QuantityKind.TransverseDisplacement,
            UnitProfile.UnitedStates, PresentationMode.Detailed, culture: c));
        Assert.Equal(culture == "de-DE" ? "0,0005 kip" : "0.0005 kip", QuantityFormatter.Format(2.7, QuantityKind.TransverseForce,
            UnitProfile.UnitedStates, references: references, culture: c));
    }

    [Fact]
    public void DetailedUsConversionDoesNotRoundSiFirst()
    {
        // SI three-digit rounding would produce 4510 N and subsequently 1.01 kip.
        Assert.Equal("1.02 kip", QuantityFormatter.Format(4514.96, QuantityKind.AxialForce,
            UnitProfile.UnitedStates, PresentationMode.Detailed, culture: CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MixedLengthUnitsUsePhysicalLengthRatherThanBeamDisplayUnit()
    {
        var profile = UnitProfile.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot);
        var references = new ModelReferenceValues(10, 1000, 1000, 235e6);
        Assert.Equal("0.1 mm", QuantityFormatter.Format(0.05 / 1000, QuantityKind.TransverseDisplacement,
            profile, references: references, culture: CultureInfo.InvariantCulture));
        Assert.Equal("0.05 mm", QuantityFormatter.Format(0.05 / 1000, QuantityKind.AxialDisplacement,
            profile, references: references, culture: CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NotationKeepsExplicitlyChosenUnitAndExactRoundedDigits()
    {
        var references = new ModelReferenceValues(1, 5000, 1000, 235e6);
        Assert.Equal("4520 N", QuantityFormatter.Format(4516.02, QuantityKind.TransverseForce,
            references: references, culture: CultureInfo.InvariantCulture));
        Assert.Equal("4.52 kN", QuantityFormatter.Format(4516.02, QuantityKind.TransverseForce,
            UnitProfile.StructuralEngineering, references: references, culture: CultureInfo.InvariantCulture));
        Assert.Equal("1·10⁶ N", QuantityFormatter.Format(1e6, QuantityKind.AxialForce, culture: CultureInfo.InvariantCulture));
        Assert.Equal("1000 kN", QuantityFormatter.Format(1e6, QuantityKind.AxialForce,
            UnitProfile.StructuralEngineering, culture: CultureInfo.InvariantCulture));
    }

    private static void Close(double expected, double actual) =>
        Assert.True(Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-14,
            $"Expected {expected:R}, actual {actual:R}");
}
