using System.Globalization;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopUiNumberTests
{
    [Theory]
    [InlineData("de-DE", 13300, "13,3·10³")]
    [InlineData("en-US", 13300, "13.3·10³")]
    [InlineData("de-DE", -50000, "-50·10³")]
    [InlineData("en-US", -13300, "-13.3·10³")]
    [InlineData("de-DE", 0.00123456, "1,23·10⁻³")]
    [InlineData("en-US", -0.00123456, "-1.23·10⁻³")]
    [InlineData("de-DE", 0.01, "0,01")]
    [InlineData("en-US", 0.009, "9·10⁻³")]
    [InlineData("en-US", 9999, "9999")]
    [InlineData("de-DE", 10000, "10·10³")]
    [InlineData("en-US", 999499, "999·10³")]
    [InlineData("de-DE", 999500, "1·10⁶")]
    [InlineData("en-US", -999500, "-1·10⁶")]
    [InlineData("de-DE", 0.0009999999, "1·10⁻³")]
    [InlineData("en-US", double.MaxValue, "180·10³⁰⁶")]
    [InlineData("de-DE", -double.MaxValue, "-180·10³⁰⁶")]
    [InlineData("de-DE", double.Epsilon, "4,94·10⁻³²⁴")]
    [InlineData("en-US", -double.Epsilon, "-4.94·10⁻³²⁴")]
    [InlineData("en-US", 1e-300, "1·10⁻³⁰⁰")]
    [InlineData("de-DE", 0, "0")]
    [InlineData("en-US", -0.0, "0")]
    [InlineData("de-DE", double.PositiveInfinity, "∞")]
    public void DisplayRulesUseUICultureAndSuperscriptEngineeringGroups(string culture, double value, string expected)
    {
        using var scope = new UiCultureScope(culture);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "de-DE" ? "en-US" : "de-DE");
            Assert.Equal(expected, UiNumbers.Compact(value));
            Assert.Equal(expected, UiNumbers.Indicator(value));
            Assert.DoesNotContain("E", expected);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void RoundedDisplaysLeaveLengthAndDraftBuffersLossless(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var length = Length.FromMillimeters(1234567.89012345);
        var input = new LengthInputViewModel(() => length, _ => LengthCommitResult.Success);
        Assert.Equal(culture == "de-DE" ? "1,23·10⁶ mm" : "1.23·10⁶ mm", input.DisplayText);
        input.Begin();
        Assert.Equal(UiNumbers.Format(length.Millimeters), input.Text);
        Assert.True(UiNumbers.TryParseLength(input.Text, out var parsedLength));
        Assert.Equal(length, parsedLength);

        var document = new EditorDocument(Length.FromMillimeters(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            loads: [new EditorPointForce(Guid.NewGuid(), Length.FromMillimeters(500), Force.FromNewtons(13333.123456789), "F1")]);
        var editor = new EditorViewModel(document, () => { });
        Assert.True(editor.EditLoad(document.Loads[0].Id));
        Assert.Equal(UiNumbers.Format(document.Loads[0].Value), editor.LoadDraft!.ValueText);
        Assert.True(UiNumbers.TryParseSignedValue(editor.LoadDraft.ValueText, out var parsedForce));
        Assert.Equal(document.Loads[0].Value, parsedForce);
        Assert.Equal(culture == "de-DE" ? "13,3·10³ N" : "13.3·10³ N", editor.Overview.Loads[0].Value);
        Assert.Same(document, editor.Document);
    }
}
