using System.Globalization;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopInputUnitTests
{
    public static IEnumerable<object[]> UnitCases()
    {
        foreach (string culture in new[] { "de-DE", "en-US" })
        foreach (var quantity in new[] { QuantityKind.BeamLength, QuantityKind.TransverseForce, QuantityKind.Moment, QuantityKind.DistributedLoad })
        foreach (var unit in UnitCatalog.All.Where(u => u.Dimension == UnitCatalog.DimensionOf(quantity)))
            yield return [culture, quantity, unit.Id];
    }

    [Theory]
    [MemberData(nameof(UnitCases))]
    public void EditableFormattingParsingAndInvalidValuesUseTheSelectedUnits(string culture, QuantityKind quantity, string unitId)
    {
        using var scope = new ResultCultureScope(culture, culture == "de-DE" ? "en-US" : "de-DE");
        var unit = UnitCatalog.All.Single(u => u.Id == unitId);
        Assert.Equal(UnitCatalog.DimensionOf(quantity), unit.Dimension);
        foreach (double original in new[] { 0d, .027387593197926163, -1234.5678901234567, 1e-200, 1e200 })
        {
            string text = InputQuantityFormatter.Format(original, unit);
            Assert.Equal(unit.FromSi(original).ToString("R", CultureInfo.CurrentCulture), text);
            Assert.True(InputQuantityFormatter.TryParse(text, unit, out double parsed), $"{unit.Id}: {original:R} -> {text}");
            Assert.True(Math.Abs(parsed - original) <= Math.Abs(original) * 5e-16);
        }
        string changed = culture == "de-DE" ? "-1,25" : "-1.25";
        Assert.True(InputQuantityFormatter.TryParse(changed, unit, out double value));
        Assert.Equal(unit.ToSi(-1.25), value);
        foreach (string invalid in new[] { "", "-", "no", "NaN", "Infinity", "∞", "1e999", "1e-999", "1,234,567", "1.234.567" })
            Assert.False(InputQuantityFormatter.TryParse(invalid, unit, out _), invalid);
        Assert.False(InputQuantityFormatter.TryParse(culture == "de-DE" ? "1.25" : "1,25", unit, out _));
    }

    public static IEnumerable<object[]> DraftCases()
    {
        foreach (string culture in new[] { "de-DE", "en-US" })
        foreach (int index in Enumerable.Range(0, 5)) yield return [culture, index];
    }

    private static EditorDocument Document() => new(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section);
    private static UnitProfile Profile(int index) => index == 4
        ? UnitProfile.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch) : DesktopResultPresentationTests.Profile(index);

    [Theory]
    [MemberData(nameof(DraftCases))]
    public void AllDraftFieldsDisplayConvertAndPreserveUnchangedSiValues(string culture, int index)
    {
        using var scope = new ResultCultureScope(culture, culture == "de-DE" ? "en-US" : "de-DE");
        var profile = Profile(index);
        var document = Document();
        var original = Length.FromMeters(.027387593197926163);
        double raw = -1234.5678901234567;
        var positionUnit = profile[QuantityKind.BeamLength];
        var support = new SupportDraftViewModel(() => document, null, SupportType.Pinned, original, profile);
        Assert.Equal(positionUnit.Symbol, support.PositionUnit);
        Assert.Equal(InputQuantityFormatter.Format(original.Meters, positionUnit), support.PositionText);
        Assert.True(support.TryGetValue(out var position)); Assert.Equal(original, position);
        var changedPosition = Length.FromMeters(.5);
        support.PositionText = InputQuantityFormatter.Format(changedPosition.Meters, positionUnit);
        Assert.True(support.TryGetValue(out position)); Assert.Equal(.5, position.Meters, 14);
        support.PositionText = "-1"; Assert.False(support.IsValid);

        foreach (var kind in Enum.GetValues<PointLoadKind>())
        {
            var unit = profile[kind == PointLoadKind.Force ? QuantityKind.TransverseForce : QuantityKind.Moment];
            var point = new PointLoadDraftViewModel(() => document, null, kind, original, raw, profile);
            Assert.Equal(unit.Symbol, point.Unit); Assert.Equal(positionUnit.Symbol, point.PositionUnit);
            Assert.Equal(InputQuantityFormatter.Format(raw, unit), point.ValueText);
            Assert.True(point.TryGetValues(out position, out double value));
            Assert.Equal(original, position); Assert.Equal(raw, value);
            point.PositionText = InputQuantityFormatter.Format(.5, positionUnit);
            point.ValueText = InputQuantityFormatter.Format(-500, unit);
            Assert.True(point.TryGetValues(out position, out value));
            Assert.Equal(.5, position.Meters, 14); Assert.Equal(-500, value, 10);
            point.ValueText = "NaN"; Assert.False(point.IsValid);
            point.ValueText = InputQuantityFormatter.Format(raw, unit);
            point.PositionText = InputQuantityFormatter.Format(original.Meters, positionUnit);
            Assert.True(point.TryGetValues(out position, out value));
            Assert.Equal(original, position); Assert.Equal(raw, value);
        }

        var end = Length.FromMeters(.9876543210987654);
        var intensityUnit = profile[QuantityKind.DistributedLoad];
        var distributed = new DistributedLoadDraftViewModel(() => document, null, original, end, raw, profile);
        Assert.Equal(positionUnit.Symbol, distributed.PositionUnit); Assert.Equal(intensityUnit.Symbol, distributed.IntensityUnit);
        Assert.True(distributed.TryGetValues(out var start, out var stop, out double intensity));
        Assert.Equal(original, start); Assert.Equal(end, stop); Assert.Equal(raw, intensity);
        distributed.StartText = InputQuantityFormatter.Format(.2, positionUnit);
        distributed.EndText = InputQuantityFormatter.Format(.8, positionUnit);
        distributed.IntensityText = InputQuantityFormatter.Format(-500, intensityUnit);
        Assert.True(distributed.TryGetValues(out start, out stop, out intensity));
        Assert.Equal(.2, start.Meters, 14); Assert.Equal(.8, stop.Meters, 14); Assert.Equal(-500, intensity, 10);
        distributed.EndText = distributed.StartText; Assert.False(distributed.IsValid);
        distributed.EndText = "bad"; Assert.False(distributed.IsValid);
    }

    [Theory]
    [MemberData(nameof(DraftCases))]
    public void LengthAndFocusPreserveTheExactOriginalAndConvertOnlyChangedText(string culture, int index)
    {
        using var scope = new ResultCultureScope(culture, culture == "de-DE" ? "en-US" : "de-DE");
        var profile = Profile(index);
        var unit = profile[QuantityKind.BeamLength];
        Length current = Length.FromMeters(1.0000000000000002), original = current;
        bool preserve = false;
        var input = new LengthInputViewModel(() => current, value => { current = value; return LengthCommitResult.Success; },
            () => preserve, () => unit);
        input.Begin(); Assert.Equal(InputQuantityFormatter.Format(original.Meters, unit), input.Text);
        Assert.Equal(unit.Symbol, input.Unit); Assert.True(input.Confirm()); Assert.Equal(original, current);
        input.Begin(); input.Text = InputQuantityFormatter.Format(2.5, unit);
        preserve = true; input.LoseFocus(); Assert.True(input.IsEditing); Assert.Equal(original, current);
        preserve = false; input.LoseFocus(); Assert.False(input.IsEditing); Assert.Equal(2.5, current.Meters, 14);
        input.Begin(); input.Text = "-1"; Assert.False(input.Confirm()); Assert.True(input.HasError);
        input.LoseFocus(); Assert.False(input.IsEditing); Assert.False(input.HasError);
        Assert.Equal(2.5, current.Meters, 14);
    }

    [Theory]
    [InlineData("mm")]
    [InlineData("m")]
    [InlineData("ft")]
    [InlineData("in")]
    public void PositionEndpointsAreInclusiveAndRetainTheExactBeamLength(string id)
    {
        var unit = UnitCatalog.All.Single(u => u.Id == id);
        var length = Length.FromMeters(1.0000000000000002);
        Assert.True(InputQuantityFormatter.TryParsePosition(InputQuantityFormatter.Format(length.Meters, unit), unit, length, out var endpoint));
        Assert.Equal(length, endpoint);
        Assert.True(InputQuantityFormatter.TryParsePosition("0", unit, length, out var zero));
        Assert.Equal(0, zero.Meters);
        Assert.False(InputQuantityFormatter.TryParsePosition(InputQuantityFormatter.Format(2, unit), unit, length, out _));
        Assert.False(InputQuantityFormatter.TryParseLength("0", unit, out _));
        Assert.False(InputQuantityFormatter.TryParseLength("-1", unit, out _));
    }

    [Fact]
    public void ExtendedDisplayRangeAndConversionUnderflowDoNotLoseSiValues()
    {
        foreach (var unit in new[] { UnitCatalog.Millimeter, UnitCatalog.KipPerFoot, UnitCatalog.Megapascal })
        foreach (double value in new[] { double.MaxValue, double.Epsilon, -double.MaxValue, -double.Epsilon })
        {
            string text = InputQuantityFormatter.Format(value, unit);
            Assert.True(InputQuantityFormatter.TryParse(text, unit, out double parsed), $"{unit.Id}: {value:R} -> {text}");
            Assert.Equal(value, parsed);
        }
        Assert.False(InputQuantityFormatter.TryParse("1E-324", UnitCatalog.Millimeter, out _));
        Assert.False(InputQuantityFormatter.TryParse("1E308", UnitCatalog.Kip, out _));
        Assert.True(InputQuantityFormatter.TryParseLength("1E309", UnitCatalog.Millimeter, out var length));
        Assert.Equal(1e306, length.Meters);
        Assert.False(InputQuantityFormatter.TryParseLength("1E312", UnitCatalog.Millimeter, out _));
    }
}
