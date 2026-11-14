using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using System.Globalization;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

/// <summary>The sole committed UI document. Input buffers are held separately.</summary>
public sealed record EditorDocument
{
    public EditorDocument(Length length, Material material, Section section,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorDistributedLoad>? distributedLoads = null, string? sectionDisplayName = null,
        EditorSelfWeight? selfWeight = null)
        : this(length, material, section, SectionAxisDesignation.Y, supports, loads, namingState, distributedLoads, sectionDisplayName, selfWeight)
    {
    }

    public EditorDocument(Length length, Material material, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorDistributedLoad>? distributedLoads = null, string? sectionDisplayName = null,
        EditorSelfWeight? selfWeight = null)
        : this(length, [], [new EditorSegment(material, section, bendingAxis, sectionDisplayName)], supports, loads,
            namingState, distributedLoads, selfWeight)
    {
    }

    /// <summary>
    /// A beam of several segments. <paramref name="segmentBoundaries"/> are the positions between consecutive segments:
    /// strictly ascending and strictly inside the beam, so the segments tile it by construction.
    /// </summary>
    public EditorDocument(Length length, IEnumerable<Length> segmentBoundaries, IEnumerable<EditorSegment> segments,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorDistributedLoad>? distributedLoads = null, EditorSelfWeight? selfWeight = null)
    {
        ArgumentNullException.ThrowIfNull(segmentBoundaries);
        ArgumentNullException.ThrowIfNull(segments);
        var boundaryArray = segmentBoundaries.ToArray();
        var segmentArray = segments.ToArray();
        if (segmentArray.Length != boundaryArray.Length + 1 || segmentArray.Any(segment => segment is null))
            throw new ArgumentException("A document has one more segment than segment boundaries.", nameof(segments));
        double previous = 0;
        foreach (var boundary in boundaryArray)
        {
            if (!(boundary.Meters > previous) || !(boundary.Meters < length.Meters))
                throw new ArgumentException("Segment boundaries must ascend strictly inside the beam.", nameof(segmentBoundaries));
            previous = boundary.Meters;
        }
        _boundaries = boundaryArray;
        _segments = segmentArray;
        Length = length;
        Supports = Array.AsReadOnly((supports ?? []).ToArray());
        Loads = Array.AsReadOnly((loads ?? []).ToArray());
        DistributedLoads = Array.AsReadOnly((distributedLoads ?? []).ToArray());
        NamingState = namingState ?? new();
        SelfWeight = selfWeight;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in NamedEntities)
            if (!names.Add(EntityNaming.Normalize(entity.Name)))
                throw new ArgumentException("Entity names must be unique across the project.", nameof(supports));
    }

    private readonly Length[] _boundaries;
    private readonly EditorSegment[] _segments;
    private readonly Length _length;

    /// <summary>The beam length; the last segment extends or shortens with it, but never past the last boundary.</summary>
    public Length Length
    {
        get => _length;
        init
        {
            if (_boundaries is { Length: > 0 } boundaries && !(value.Meters > boundaries[^1].Meters))
                throw new ArgumentException("The beam must extend beyond its last segment boundary.", nameof(Length));
            _length = value;
        }
    }

    /// <summary>The positions between consecutive segments, strictly ascending inside the beam; empty for one segment.</summary>
    public IReadOnlyList<Length> SegmentBoundaries => Array.AsReadOnly(_boundaries);
    public IReadOnlyList<EditorSegment> Segments => Array.AsReadOnly(_segments);
    public bool HasSeveralSegments => _segments.Length > 1;

    public (Length Start, Length End) SegmentRange(int index)
    {
        if ((uint)index >= (uint)_segments.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return (index == 0 ? default : _boundaries[index - 1], index == _segments.Length - 1 ? Length : _boundaries[index]);
    }

    /// <summary>The index of the segment that contains x; a boundary belongs to the segment that starts there.</summary>
    public int SegmentIndexAt(Length position)
    {
        int index = 0;
        while (index < _boundaries.Length && _boundaries[index].Meters <= position.Meters) index++;
        return index;
    }

    // The single-segment view of the document: material, section, axis and display name of the one segment.
    // Reading them on a document with several segments is a programming error, never a silent first-segment fallback.
    private EditorSegment Only => _segments.Length == 1 ? _segments[0]
        : throw new InvalidOperationException("The document has several segments; read them from Segments.");

    public Material Material { get => Only.Material; init => _segments = [new EditorSegment(value, Only.Section, Only.BendingAxis, Only.SectionDisplayName)]; }
    public ISectionDefinition Section => Only.Section;
    public SectionAxisDesignation BendingAxis => Only.BendingAxis;

    /// <summary>Null selects a generated name; explicit names are desktop metadata.</summary>
    public string? SectionDisplayName
    {
        get => Only.SectionDisplayName;
        init => _segments = [new EditorSegment(Only.Material, Only.Section, Only.BendingAxis, value)];
    }
    public IReadOnlyList<EditorSupport> Supports { get; }
    public IReadOnlyList<EditorPointLoad> Loads { get; }
    public IReadOnlyList<EditorDistributedLoad> DistributedLoads { get; }
    public NamingState NamingState { get; }
    public EditorSelfWeight? SelfWeight { get; init; }
    public IEnumerable<Guid> EntityIds => NamedEntities.Select(e => e.Id)
        .Concat(SelfWeight is { } weight ? [weight.Id] : []);
    /// <summary>
    /// Self-weight needs the density of every segment's material, and no entity may use the reserved name. For
    /// several segments the message names the first segment without a density.
    /// </summary>
    public string? SelfWeightActivationError
    {
        get
        {
            int missing = Array.FindIndex(_segments, segment => segment.Material.Density is null);
            if (missing >= 0)
                return _segments.Length == 1 ? Strings.SelfWeightMissingDensity
                    : string.Format(CultureInfo.CurrentCulture, Strings.SelfWeightMissingDensitySegment, missing + 1);
            return NamedEntities.Any(e => EntityNaming.IsReserved(e.Name)) ? Strings.SelfWeightNameConflict : null;
        }
    }

    /// <summary>The constant self-weight load of each segment over its own range, in segment order; empty when off.</summary>
    public IReadOnlyList<DistributedLoad> SelfWeightLoads
    {
        get
        {
            if (SelfWeight?.Enabled != true) return [];
            if (SelfWeightActivationError is { } error) throw new ArgumentException(error);
            return Array.AsReadOnly(_segments.Select((segment, index) =>
            {
                var (start, end) = SegmentRange(index);
                return DistributedLoad.FromSelfWeight(start, end, segment.Material.Density!.Value, segment.Section.Area);
            }).ToArray());
        }
    }

    /// <summary>The self-weight load of a document with exactly one segment; use <see cref="SelfWeightLoads"/> otherwise.</summary>
    public DistributedLoad? SelfWeightLoad => SelfWeightLoads is [var load] ? load
        : SelfWeightLoads.Count == 0 ? null : throw new InvalidOperationException("The document has several segments.");

    public IEnumerable<(Guid Id, string Name)> NamedEntities =>
        Supports.Select(s => (s.Id, s.Name)).Concat(Loads.Select(l => (l.Id, l.Name)))
            .Concat(DistributedLoads.Select(l => (l.Id, l.Name)));

    public EditorDocument WithSupports(IEnumerable<EditorSupport> supports, NamingState? namingState = null) =>
        new(Length, _boundaries, _segments, supports, Loads, NextNamingState(namingState), DistributedLoads, SelfWeight);

    public EditorDocument WithLoads(IEnumerable<EditorPointLoad> loads, NamingState? namingState = null) =>
        new(Length, _boundaries, _segments, Supports, loads, NextNamingState(namingState), DistributedLoads, SelfWeight);

    public EditorDocument WithDistributedLoads(IEnumerable<EditorDistributedLoad> loads, NamingState? namingState = null) =>
        new(Length, _boundaries, _segments, Supports, Loads, NextNamingState(namingState), loads, SelfWeight);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis) =>
        WithSection(section, bendingAxis, SectionDisplayName);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis, string? sectionDisplayName) =>
        new(Length, _boundaries, [new EditorSegment(Only.Material, section, bendingAxis, sectionDisplayName)],
            Supports, Loads, NamingState, DistributedLoads, SelfWeight);

    public EditorDocument WithBendingAxis(SectionAxisDesignation bendingAxis) => WithSection(Section, bendingAxis);

    /// <summary>Replaces the segment boundaries and the segments together; everything else is kept.</summary>
    public EditorDocument WithSegments(IEnumerable<Length> segmentBoundaries, IEnumerable<EditorSegment> segments) =>
        new(Length, segmentBoundaries, segments, Supports, Loads, NamingState, DistributedLoads, SelfWeight);

    /// <summary>Replaces one segment; boundaries and all other segments are kept.</summary>
    public EditorDocument WithSegment(int index, EditorSegment segment)
    {
        if ((uint)index >= (uint)_segments.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var segments = _segments.ToArray();
        segments[index] = segment;
        return WithSegments(_boundaries, segments);
    }

    /// <summary>
    /// Splits the segment that contains <paramref name="position"/> there; both halves keep every property of the
    /// original, including its display name. A position on an existing boundary or an end is rejected.
    /// </summary>
    public EditorDocument WithBoundaryAt(Length position)
    {
        if (!(position.Meters > 0) || !(position.Meters < Length.Meters) || _boundaries.Any(b => b == position))
            throw new ArgumentException("A new boundary must lie strictly inside the beam and off the existing ones.", nameof(position));
        int index = SegmentIndexAt(position);
        return WithSegments(_boundaries.Take(index).Append(position).Concat(_boundaries.Skip(index)),
            _segments.Take(index + 1).Concat(_segments.Skip(index)));
    }

    /// <summary>Removes one boundary; the merged segment keeps the left or the right neighbour's properties.</summary>
    public EditorDocument WithoutBoundary(int boundaryIndex, bool keepLeft = true)
    {
        if ((uint)boundaryIndex >= (uint)_boundaries.Length) throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
        return WithSegments(_boundaries.Where((_, i) => i != boundaryIndex),
            _segments.Where((_, i) => i != (keepLeft ? boundaryIndex + 1 : boundaryIndex)));
    }

    /// <summary>Moves one boundary without passing its neighbours.</summary>
    public EditorDocument WithBoundary(int boundaryIndex, Length position)
    {
        if ((uint)boundaryIndex >= (uint)_boundaries.Length) throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
        var boundaries = _boundaries.ToArray();
        boundaries[boundaryIndex] = position;
        return WithSegments(boundaries, _segments);
    }

    private NamingState NextNamingState(NamingState? requested)
    {
        var next = requested ?? NamingState;
        if (next.NextSupportOrdinal < NamingState.NextSupportOrdinal || next.NextForceNumber < NamingState.NextForceNumber
            || next.NextMomentNumber < NamingState.NextMomentNumber
            || next.NextDistributedLoadNumber < NamingState.NextDistributedLoadNumber)
            throw new ArgumentException("Project naming counters cannot move backwards.", nameof(requested));
        return next;
    }

    public BeamModel ToBeamModel() => new(Length, _segments.Select((segment, index) =>
        {
            var (start, end) = SegmentRange(index);
            return new BeamSegment(start, end, segment.Material, segment.Section, segment.BendingAxis);
        }),
        Supports.Select(s => s.Type == SupportType.Spring
            ? (s.Spring ?? throw new InvalidOperationException("A spring support needs its springs.")).ToCore(s.Position)
            : new Support(s.Position, s.Type)),
        Loads.Select(l => l.ToCore()).Concat(DistributedLoads.Select(l => l.ToCore()))
            .Concat(SelfWeightLoads));
}
