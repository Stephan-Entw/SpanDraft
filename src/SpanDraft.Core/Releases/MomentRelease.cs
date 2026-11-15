using SpanDraft.Core.Units;

namespace SpanDraft.Core.Releases;

/// <summary>
/// An internal moment release (hinge) at x from the left beam end: the bending moment is zero on both sides and the
/// rotation may jump, while deflection, shear force and axial force are transmitted. Its position must lie strictly
/// inside the beam; further consistency is checked by BeamModelValidator.
/// </summary>
public sealed class MomentRelease
{
    public MomentRelease(Length position) => Position = position;

    public Length Position { get; }
}
