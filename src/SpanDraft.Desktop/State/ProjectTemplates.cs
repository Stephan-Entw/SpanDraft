using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Geometric and provisional material templates, not normative catalogue entries.</summary>
public static class ProjectTemplates
{
    public static RectangularHollowSection Section { get; } = new(
        Length.FromMillimeters(100), Length.FromMillimeters(100), Length.FromMillimeters(5));

    public static Material Material { get; } = new("S235JR",
        Pressure.FromPascals(210e9), Pressure.FromMegapascals(235));
}
