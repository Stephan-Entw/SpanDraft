using Xunit;

namespace SpanDraft.Tests;

internal static class NumericAssert
{
    // Relative tolerance preserves meaningful accuracy across SI and engineering units.
    internal static void Close(double expected, double actual) =>
        Assert.InRange(actual, expected - Math.Max(Math.Abs(expected) * 1e-10, 1e-30),
            expected + Math.Max(Math.Abs(expected) * 1e-10, 1e-30));
}
