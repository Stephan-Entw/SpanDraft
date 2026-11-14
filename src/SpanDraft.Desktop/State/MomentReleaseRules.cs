using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

public enum MomentReleaseConflict { None, NotInsideBeam, Duplicate, FixedSupport, RotationalSpring, PointMoment }

/// <summary>
/// The position rules between an internal moment release and the entities it cannot share a position with. They
/// repeat the exact comparisons of the Core validator so that the editor never commits a document the solver would
/// reject, and the loaded file never contains one.
/// </summary>
public static class MomentReleaseRules
{
    /// <summary>The conflict of a release at <paramref name="position"/>; <paramref name="exclude"/> is the release being edited.</summary>
    public static MomentReleaseConflict Conflict(EditorDocument document, Length position, Guid? exclude = null)
    {
        double x = position.Meters;
        if (!(x > 0 && x < document.Length.Meters)) return MomentReleaseConflict.NotInsideBeam;
        if (document.MomentReleases.Any(r => r.Id != exclude && r.Position.Meters == x)) return MomentReleaseConflict.Duplicate;
        if (document.Supports.Any(s => s.Position.Meters == x && s.Type == SupportType.Fixed)) return MomentReleaseConflict.FixedSupport;
        if (document.Supports.Any(s => s.Position.Meters == x && s.Spring?.Rotational is not null)) return MomentReleaseConflict.RotationalSpring;
        if (document.Loads.Any(l => l.Kind == PointLoadKind.Moment && l.Position.Meters == x)) return MomentReleaseConflict.PointMoment;
        return MomentReleaseConflict.None;
    }

    public static string? Message(MomentReleaseConflict conflict) => conflict switch
    {
        MomentReleaseConflict.None => null,
        MomentReleaseConflict.NotInsideBeam => Strings.MomentReleaseInsideBeam,
        MomentReleaseConflict.Duplicate => Strings.MomentReleaseAlreadyExists,
        MomentReleaseConflict.FixedSupport => Strings.MomentReleaseAtFixedSupport,
        MomentReleaseConflict.RotationalSpring => Strings.MomentReleaseAtRotationalSpring,
        MomentReleaseConflict.PointMoment => Strings.MomentReleaseAtMoment,
        _ => throw new ArgumentOutOfRangeException(nameof(conflict))
    };

    /// <summary>True when a support of this type and springs cannot stand at the position of a release.</summary>
    public static bool SupportConflicts(EditorDocument document, Length position, SupportType type, SpringData? spring) =>
        (type == SupportType.Fixed || spring?.Rotational is not null)
        && document.MomentReleases.Any(r => r.Position.Meters == position.Meters);

    /// <summary>True when a point moment cannot act at the position because a release is there.</summary>
    public static bool MomentConflicts(EditorDocument document, Length position) =>
        document.MomentReleases.Any(r => r.Position.Meters == position.Meters);
}
