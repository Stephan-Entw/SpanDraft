using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Releases;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Core.Beams;

/// <summary>
/// A single straight beam of one or more segments; each segment has constant material, section and bending
/// axis. Local x runs rightwards from zero at the left end; transverse positive is upwards in the selected
/// planar bending plane; positive rotation and moment are counterclockwise. Bending axes are resolved at
/// construction. Further consistency, including the exact tiling of the beam by its segments, is checked by
/// BeamModelValidator.
/// </summary>
public sealed class BeamModel
{
    public BeamModel(Length length, Material material, Section section,
        IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
        : this(length, material, section, SectionAxisDesignation.Y, supports, loads)
    {
    }

    /// <summary>Creates a beam of one segment using exactly the explicitly selected section axis.</summary>
    public BeamModel(Length length, Material material, ISectionDefinition section,
        SectionAxisDesignation bendingAxis, IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
        : this(length, [new BeamSegment(default, PositiveLength(length), material, section, bendingAxis)],
            supports, loads)
    {
    }

    /// <summary>
    /// Creates a beam from consecutive segments. The segments are taken as given; the validator reports gaps,
    /// overlaps and ends that differ from the beam ends.
    /// </summary>
    public BeamModel(Length length, IEnumerable<BeamSegment> segments,
        IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
        : this(length, segments, supports, loads, [])
    {
    }

    /// <summary>
    /// Creates a beam from consecutive segments with internal moment releases. The releases are taken as given;
    /// the validator reports positions outside the beam interior and conflicts with supports and loads.
    /// </summary>
    public BeamModel(Length length, IEnumerable<BeamSegment> segments,
        IEnumerable<Support> supports, IEnumerable<BeamLoad> loads, IEnumerable<MomentRelease> momentReleases)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(momentReleases);

        BeamSegment[] segmentArray = segments.ToArray();
        Support[] supportArray = supports.ToArray();
        BeamLoad[] loadArray = loads.ToArray();
        MomentRelease[] releaseArray = momentReleases.ToArray();
        if (segmentArray.Length == 0)
            throw new ArgumentException("A beam requires at least one segment.", nameof(segments));
        if (segmentArray.Any(segment => segment is null))
            throw new ArgumentException("Segments must not contain null entries.", nameof(segments));
        if (supportArray.Any(support => support is null))
            throw new ArgumentException("Supports must not contain null entries.", nameof(supports));
        if (loadArray.Any(load => load is null))
            throw new ArgumentException("Loads must not contain null entries.", nameof(loads));
        if (releaseArray.Any(release => release is null))
            throw new ArgumentException("Moment releases must not contain null entries.", nameof(momentReleases));

        Length = length;
        Segments = Array.AsReadOnly(segmentArray);
        Supports = Array.AsReadOnly(supportArray);
        Loads = Array.AsReadOnly(loadArray);
        MomentReleases = Array.AsReadOnly(releaseArray);
    }

    public Length Length { get; }
    public IReadOnlyList<BeamSegment> Segments { get; }
    public IReadOnlyList<Support> Supports { get; }
    public IReadOnlyList<BeamLoad> Loads { get; }

    /// <summary>Internal moment releases (hinges); empty for a beam without any.</summary>
    public IReadOnlyList<MomentRelease> MomentReleases { get; }

    /// <summary>The material of a beam with exactly one segment; use <see cref="Segments"/> otherwise.</summary>
    public Material Material => OnlySegment.Material;

    /// <summary>The section of a beam with exactly one segment; use <see cref="Segments"/> otherwise.</summary>
    public ISectionDefinition Section => OnlySegment.Section;

    /// <summary>The bending axis of a beam with exactly one segment; use <see cref="Segments"/> otherwise.</summary>
    public SectionAxisDesignation BendingAxis => OnlySegment.BendingAxis;

    /// <summary>The axis properties of a beam with exactly one segment; use <see cref="Segments"/> otherwise.</summary>
    public SectionAxisProperties BendingAxisProperties => OnlySegment.BendingAxisProperties;

    private static Length PositiveLength(Length length)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        return length;
    }

    private BeamSegment OnlySegment => Segments.Count == 1 ? Segments[0] :
        throw new InvalidOperationException("The beam has several segments; read them from Segments.");
}
