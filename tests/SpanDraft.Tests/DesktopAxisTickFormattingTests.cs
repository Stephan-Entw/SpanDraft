using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopAxisTickFormattingTests
{
    [Theory]
    [InlineData("de-DE", -3, 0, 2, -1, null, "-0,6|-0,4|-0,2|0")]
    [InlineData("en-US", -3, 0, 2, -1, null, "-0.6|-0.4|-0.2|0")]
    [InlineData("de-DE", 0, 1, 1, -2, null, "0|0,01")]
    [InlineData("en-US", 5, 5, 2.5, -3, null, "0.0125")]
    [InlineData("de-DE", 0, 1, 5, 3, null, "0|5000")]
    [InlineData("de-DE", 0, 3, 5, 3, 3, "0|5|10|15")]
    [InlineData("en-US", -3, 0, 5, 3, 3, "-15|-10|-5|0")]
    [InlineData("de-DE", -3, 1, 5, 3, 3, "-15|-10|-5|0|5")]
    [InlineData("de-DE", -1, 1, 2.5, -3, -3, "-2,5|0|2,5")]
    [InlineData("en-US", -1, 1, 2.5, -3, -3, "-2.5|0|2.5")]
    [InlineData("en-US", 0, 5, 2.5, -3, -3, "0|2.5|5|7.5|10|12.5")]
    [InlineData("en-US", 49, 50, 2.5, -4, -3, "12.25|12.5")]
    [InlineData("en-US", 0, 0, 0, 0, null, "0")]
    [InlineData("de-DE", 0, 3, 2, -324, -324, "0|2|4|6")]
    [InlineData("en-US", -1, 1, 1, 309, 309, "-1|0|1")]
    [InlineData("en-US", -2, 2, 1, 308, 306, "-200|-100|0|100|200")]
    [InlineData("de-DE", -1, 4, 2.5, 308, 309, "-0,25|0|0,25|0,5|0,75|1")]
    public void TicksUseOneNotationAndLocalizedExactDecimals(string culture, int lower, int upper,
        double mantissa, int exponent, int? engineeringExponent, string labels)
    {
        using var scope = new UiCultureScope(culture);
        var formatter = AxisTickFormatter.Create(lower, upper, mantissa, exponent);
        Assert.Equal(engineeringExponent, formatter.EngineeringExponent);
        Assert.Equal(labels.Split('|'), Enumerable.Range(lower, upper - lower + 1).Select(index => formatter.Format(index)));
    }
}
