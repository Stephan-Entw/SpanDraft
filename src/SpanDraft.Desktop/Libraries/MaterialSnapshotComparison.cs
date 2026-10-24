using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Libraries;

internal static class MaterialSnapshotComparison
{
    internal static bool Equals(Material a, Material b) => a.Name.Trim() == b.Name.Trim()
        && a.YoungsModulus == b.YoungsModulus && a.YieldStrength == b.YieldStrength
        && a.StrengthType == b.StrengthType && a.Density == b.Density && a.PoissonRatio == b.PoissonRatio
        && a.Category == b.Category && a.MaterialNumber == b.MaterialNumber && a.OtherStandards.SequenceEqual(b.OtherStandards);
}
