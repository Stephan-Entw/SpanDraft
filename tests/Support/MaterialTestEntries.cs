using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;

namespace SpanDraft.Tests;

internal static class MaterialTestEntries
{
    internal static Material Entry(string name = "Shop stainless", MaterialCategory category = MaterialCategory.StainlessSteel) =>
        new(name, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300), MaterialStrengthType.Re,
            MassDensity.FromKilogramsPerCubicMeter(7800), PoissonRatio.FromValue(.3), category, "1.4567", ["UNS Example", "AISI Shop"]);
}
