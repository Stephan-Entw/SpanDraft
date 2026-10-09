using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;

namespace SpanDraft.Desktop.State;

/// <summary>
/// The single explicit definition of current solver/engineering input equivalence.
/// When Domain semantics grow, update this comparer and its behavioral tests together.
/// Section definitions are compared through their selected mechanical properties.
/// Material name, density and Poisson ratio are not used by the current analysis.
/// Unknown support/load variants are never considered equivalent, even by reference identity.
/// </summary>
public static class BeamModelMechanicalComparer
{
    public static bool AreEquivalent(BeamModel left, BeamModel right) =>
        left.Length == right.Length
        && left.Material.YoungsModulus == right.Material.YoungsModulus
        && left.Material.YieldStrength == right.Material.YieldStrength
        && left.Section.Area == right.Section.Area
        && left.BendingAxis == right.BendingAxis
        && left.BendingAxisProperties.SecondMomentOfArea == right.BendingAxisProperties.SecondMomentOfArea
        && left.BendingAxisProperties.PositiveSectionModulus == right.BendingAxisProperties.PositiveSectionModulus
        && left.BendingAxisProperties.NegativeSectionModulus == right.BendingAxisProperties.NegativeSectionModulus
        && left.Supports.Count == right.Supports.Count
        && left.Supports.Zip(right.Supports).All(pair => IsKnownSupportType(pair.First.Type)
            && pair.First.Type == pair.Second.Type && pair.First.Position == pair.Second.Position)
        && left.Loads.Count == right.Loads.Count
        && left.Loads.Zip(right.Loads).All(pair => LoadsEquivalent(pair.First, pair.Second));

    public static bool IsKnownSupportType(SupportType type) => type is SupportType.Fixed or SupportType.Pinned or SupportType.Roller;

    public static bool LoadsEquivalent(BeamLoad left, BeamLoad right) => (left, right) switch
    {
        (PointForce a, PointForce b) => a.Position == b.Position && a.Force == b.Force,
        (PointMoment a, PointMoment b) => a.Position == b.Position && a.Moment == b.Moment,
        (UniformDistributedLoad a, UniformDistributedLoad b) => a.StartPosition == b.StartPosition
            && a.EndPosition == b.EndPosition && a.Intensity == b.Intensity,
        _ => false
    };
}
