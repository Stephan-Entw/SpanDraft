using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class TechnicalLineBreaksTests
{
    private const char Nbsp = TechnicalLineBreaks.NoBreakSpace;
    private const char Wj = TechnicalLineBreaks.WordJoiner;

    [Theory]
    // A stiffness may only wrap after its "=".
    [InlineData("kθ = 23 500 kN·m/rad", "kθ = ")]
    [InlineData("k = ≈ 14,3 kip/in", "k = ")]
    // A linear load may only wrap after its ellipsis.
    [InlineData("≈ -1,23 · 10⁶ … ≈ -2,35 · 10⁶ N/m", "≈ -1,23 · 10⁶ … ")]
    [InlineData("-2 000 … -6 000 N/m", "-2 000 … ")]
    // Everything else stays on one line.
    [InlineData("-4,5 kN", null)]
    [InlineData("3 000…9 000", null)]
    [InlineData("-701 · 10⁻⁶", null)]
    [InlineData("12 / -3", null)]
    public void OnlyTheSpaceAfterAnEqualsSignOrAnEllipsisStaysBreakable(string text, string? breakAfter)
    {
        string result = TechnicalLineBreaks.Protect(text);
        Assert.Equal(text, result.Replace(Nbsp, ' ').Replace(Wj.ToString(), ""));
        var breakable = Enumerable.Range(0, result.Length).Where(i => result[i] == ' ').ToArray();
        if (breakAfter is null) Assert.Empty(breakable);
        else Assert.Equal(new[] { Protected(breakAfter).Length - 1 }, breakable);
        // Units never split at "/" or "·".
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] == '/') Assert.Equal(Wj, result[i + 1]);
            if (result[i] == '·') { Assert.Equal(Wj, result[i - 1]); Assert.Equal(Wj, result[i + 1]); }
        }
        Assert.Equal(result, TechnicalLineBreaks.Converter.Convert(text, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string Protected(string prefix) => TechnicalLineBreaks.Protect(prefix);
}
