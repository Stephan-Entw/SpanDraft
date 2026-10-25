using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class MaterialDisplayTests
{
    [Theory]
    [InlineData("Name", null, null, "Name")]
    [InlineData("Name", null, new string[0], "Name")]
    [InlineData("Name", "1.2345", null, "Name (1.2345)")]
    [InlineData("Name", null, new[] { "Standard A" }, "Name · Standard A")]
    [InlineData("Name", null, new[] { "Standard B", "Standard A" }, "Name · Standard B · Standard A")]
    [InlineData("Name", "1.2345", new[] { "Standard B", "Standard A" }, "Name (1.2345) · Standard B · Standard A")]
    [InlineData("X5CrNi18-10", "1.4301", new[] { "AISI 304" }, "X5CrNi18-10 (1.4301) · AISI 304")]
    [InlineData("AlMgSi0,5 T66", "3.3206", new[] { "EN AW-6060 T66" }, "AlMgSi0,5 T66 (3.3206) · EN AW-6060 T66")]
    [InlineData("S235JR", "1.0038", new string[0], "S235JR (1.0038)")]
    [InlineData("Name", " \t ", new[] { "", " \t " }, "Name")]
    [InlineData(" Name ", " 1.2345 ", new[] { " Standard B ", "", " Standard A ", "Standard B" }, " Name  (1.2345) · Standard B · Standard A · Standard B")]
    public void FormatsSnapshotWithoutChangingNamesOrStandardOrder(string name, string? number, string[]? standards, string expected)
    {
        var material = new Material(name, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300),
            MaterialStrengthType.Re, materialNumber: number, otherStandards: standards);
        Assert.Equal(expected, MaterialDisplay.Format(material));
        Assert.Equal(name, material.Name);
    }

    [Fact]
    public void RequiresASnapshot() => Assert.Throws<ArgumentNullException>(() => MaterialDisplay.Format(null!));
}
