using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
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
    /// A beam of several segments given by their boundaries (the legacy shape). <paramref name="segmentBoundaries"/> are
    /// the positions between consecutive segments: strictly ascending and strictly inside the beam, so the segments
    /// tile it by construction. The segments' lengths follow from the boundaries.
    /// </summary>
    public EditorDocument(Length length, IEnumerable<Length> segmentBoundaries, IEnumerable<EditorSegment> segments,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorDistributedLoad>? distributedLoads = null, EditorSelfWeight? selfWeight = null,
        IEnumerable<EditorMomentRelease>? momentReleases = null)
        : this(SegmentsFromBoundaries(length, segmentBoundaries, segments), supports, loads, namingState, distributedLoads,
            selfWeight, momentReleases)
    {
    }

    /// <summary>
    /// A beam as the ordered chain of its segments. Every segment has a positive length; the beam length is their
    /// sum and the boundaries are the running sums. Segments without an identity get one.
    /// </summary>
    public EditorDocument(IEnumerable<EditorSegment> segments,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorDistributedLoad>? distributedLoads = null, EditorSelfWeight? selfWeight = null,
        IEnumerable<EditorMomentRelease>? momentReleases = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments = NormalizeSegments(segments);
        (_boundaries, _length) = Accumulate(_segments);
        _supports = Array.AsReadOnly((supports ?? []).ToArray());
        _loads = Array.AsReadOnly((loads ?? []).ToArray());
        _distributedLoads = Array.AsReadOnly((distributedLoads ?? []).ToArray());
        _momentReleases = Array.AsReadOnly((momentReleases ?? []).ToArray());
        SettlePositions();
        NamingState = namingState ?? new();
        SelfWeight = selfWeight;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in NamedEntities)
            if (!names.Add(EntityNaming.Normalize(entity.Name)))
                throw new ArgumentException("Entity names must be unique across the project.", nameof(supports));
    }

    private static EditorSegment[] SegmentsFromBoundaries(Length length, IEnumerable<Length> segmentBoundaries,
        IEnumerable<EditorSegment> segments)
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
        double start = 0;
        var seen = new HashSet<Guid>();
        for (int i = 0; i < segmentArray.Length; i++)
        {
            double end = i < boundaryArray.Length ? boundaryArray[i].Meters : length.Meters;
            // The legacy shape may use one segment object for several segments; later copies become new segments.
            var id = segmentArray[i].Id != Guid.Empty && seen.Add(segmentArray[i].Id) ? segmentArray[i].Id : Guid.Empty;
            segmentArray[i] = segmentArray[i] with { Id = id, Length = Length.FromMeters(end - start) };
            start = end;
        }
        return segmentArray;
    }

    private static EditorSegment[] NormalizeSegments(IEnumerable<EditorSegment> segments)
    {
        var array = segments.ToArray();
        if (array.Length == 0 || array.Any(segment => segment is null))
            throw new ArgumentException("A beam has at least one segment.", nameof(segments));
        for (int i = 0; i < array.Length; i++)
        {
            // A lone segment may still have no extent (such a beam is rejected by the length checks of the editor,
            // as before); the segments of a chain must all have one.
            if (array.Length > 1 && !(array[i].Length.Meters > 0))
                throw new ArgumentException("Segment lengths must be positive.", nameof(segments));
        }
        var used = array.Where(segment => segment.Id != Guid.Empty).Select(segment => segment.Id).ToList();
        if (used.Distinct().Count() != used.Count)
            throw new ArgumentException("Segment identities must be unique.", nameof(segments));
        // Segments built without an identity get the next free deterministic one, so equal documents stay equal.
        int next = 1;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i].Id != Guid.Empty) continue;
            Guid id;
            do id = new Guid(next++, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); while (used.Contains(id));
            used.Add(id);
            array[i] = array[i] with { Id = id };
        }
        return array;
    }

    // The running sums of the lengths: the boundaries between consecutive segments and the beam length.
    private static (Length[] Boundaries, Length Length) Accumulate(EditorSegment[] segments)
    {
        var boundaries = new Length[segments.Length - 1];
        double end = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            end += segments[i].Length.Meters;
            if (i < boundaries.Length) boundaries[i] = Length.FromMeters(end);
        }
        return (boundaries, Length.FromMeters(end));
    }

    private readonly Length[] _boundaries;
    private readonly EditorSegment[] _segments;
    private readonly Length _length;

    /// <summary>
    /// The beam length, the sum of the segment lengths. Setting it resizes the last segment, but never to
    /// nothing; the length of a beam of several segments is otherwise changed through its segments.
    /// </summary>
    public Length Length
    {
        get => _length;
        init
        {
            double before = _segments.Take(_segments.Length - 1).Sum(segment => segment.Length.Meters);
            if (!(value.Meters > before))
                throw new ArgumentException("The beam must extend beyond its last segment boundary.", nameof(Length));
            var segments = _segments.ToArray();
            segments[^1] = segments[^1] with { Length = Length.FromMeters(value.Meters - before) };
            _segments = segments;
            _length = value;
            SettlePositions();
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

    /// <summary>The order of the segment with this identity, or -1.</summary>
    public int IndexOfSegment(Guid id) => Array.FindIndex(_segments, segment => segment.Id == id);

    /// <summary>
    /// The global x of a position: the distance itself from the beam start, the segment start plus the distance, or
    /// the segment end. Throws <see cref="ArgumentException"/> when the segment is missing or the distance exceeds it.
    /// </summary>
    public Length ResolvePosition(PositionRef reference) =>
        TryResolve(reference, out var position, out var conflict) ? position
            : throw new ArgumentException(conflict == GeometryConflictKind.SegmentMissing
                ? "The position refers to a segment that is not in the beam."
                : "The distance from the segment start exceeds the segment.", nameof(reference));

    /// <summary>Resolves a position against this chain of segments; false names the conflict.</summary>
    public bool TryResolve(PositionRef reference, out Length position, out GeometryConflictKind conflict)
    {
        ArgumentNullException.ThrowIfNull(reference);
        conflict = default;
        position = default;
        switch (reference)
        {
            case PositionRef.FromBeamStart beam:
                position = beam.Distance;
                return true;
            case PositionRef.FromSegmentStart from:
            {
                int index = IndexOfSegment(from.Segment);
                if (index < 0) { conflict = GeometryConflictKind.SegmentMissing; return false; }
                var (start, _) = SegmentRange(index);
                if (from.Offset.Meters > _segments[index].Length.Meters) { conflict = GeometryConflictKind.BeyondSegment; return false; }
                position = Length.FromMeters(start.Meters + from.Offset.Meters);
                return true;
            }
            case PositionRef.AtSegmentEnd end:
            {
                int index = IndexOfSegment(end.Segment);
                if (index < 0) { conflict = GeometryConflictKind.SegmentMissing; return false; }
                position = SegmentRange(index).End;
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(reference));
        }
    }

    // Brings every cached global position in line with its reference. A fixed distance from the beam start is its
    // own truth and is never touched; a segment reference that cannot be resolved is a programming error here, the
    // checked operations below report it as a conflict before a document is built.
    private void SettlePositions()
    {
        Length Settle(Length current, PositionRef? reference) => reference switch
        {
            null => current,
            PositionRef.FromBeamStart beam => beam.Distance,
            _ => ResolvePosition(reference)
        };
        _supports = Array.AsReadOnly(_supports.Select(s => s.Reference is null ? s
            : s with { Position = Settle(s.Position, s.Reference), Reference = Canonical(s.Reference) }).ToArray());
        _loads = Array.AsReadOnly(_loads.Select(l => l.Reference is null ? l
            : l with { Position = Settle(l.Position, l.Reference), Reference = Canonical(l.Reference) }).ToArray());
        _momentReleases = Array.AsReadOnly(_momentReleases.Select(r => r.Reference is null ? r
            : r with { Position = Settle(r.Position, r.Reference), Reference = Canonical(r.Reference) }).ToArray());
        _distributedLoads = Array.AsReadOnly(_distributedLoads.Select(l => l.StartReference is null && l.EndReference is null ? l
            : l with
            {
                StartPosition = Settle(l.StartPosition, l.StartReference), StartReference = Canonical(l.StartReference),
                EndPosition = Settle(l.EndPosition, l.EndReference), EndReference = Canonical(l.EndReference)
            }).ToArray());
    }

    // A fixed distance from the beam start is stored as no reference, so equal positions are equal records.
    private static PositionRef? Canonical(PositionRef? reference) => reference is PositionRef.FromBeamStart ? null : reference;

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

    public Material Material { get => Only.Material; init => _segments = [Only.WithMaterial(value)]; }
    public ISectionDefinition Section => Only.Section;
    public SectionAxisDesignation BendingAxis => Only.BendingAxis;

    /// <summary>Null selects a generated name; explicit names are desktop metadata.</summary>
    public string? SectionDisplayName
    {
        get => Only.SectionDisplayName;
        init => _segments = [Only.WithSection(Only.Section, Only.BendingAxis, value)];
    }
    private IReadOnlyList<EditorSupport> _supports = [];
    private IReadOnlyList<EditorPointLoad> _loads = [];
    private IReadOnlyList<EditorDistributedLoad> _distributedLoads = [];
    private IReadOnlyList<EditorMomentRelease> _momentReleases = [];

    /// <summary>The supports; every position is the resolved global x, see <see cref="EditorSupport.Reference"/>.</summary>
    public IReadOnlyList<EditorSupport> Supports => _supports;
    public IReadOnlyList<EditorPointLoad> Loads => _loads;
    public IReadOnlyList<EditorDistributedLoad> DistributedLoads => _distributedLoads;

    /// <summary>The internal moment releases (hinges), in creation order; empty for a beam without any.</summary>
    public IReadOnlyList<EditorMomentRelease> MomentReleases => _momentReleases;
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
            .Concat(DistributedLoads.Select(l => (l.Id, l.Name))).Concat(MomentReleases.Select(r => (r.Id, r.Name)));

    public EditorDocument WithSupports(IEnumerable<EditorSupport> supports, NamingState? namingState = null) =>
        new(_segments, supports, Loads, NextNamingState(namingState), DistributedLoads, SelfWeight, MomentReleases);

    public EditorDocument WithLoads(IEnumerable<EditorPointLoad> loads, NamingState? namingState = null) =>
        new(_segments, Supports, loads, NextNamingState(namingState), DistributedLoads, SelfWeight, MomentReleases);

    public EditorDocument WithDistributedLoads(IEnumerable<EditorDistributedLoad> loads, NamingState? namingState = null) =>
        new(_segments, Supports, Loads, NextNamingState(namingState), loads, SelfWeight, MomentReleases);

    public EditorDocument WithMomentReleases(IEnumerable<EditorMomentRelease> releases, NamingState? namingState = null) =>
        new(_segments, Supports, Loads, NextNamingState(namingState), DistributedLoads, SelfWeight, releases);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis) =>
        WithSection(section, bendingAxis, SectionDisplayName);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis, string? sectionDisplayName) =>
        new([Only.WithSection(section, bendingAxis, sectionDisplayName)],
            Supports, Loads, NamingState, DistributedLoads, SelfWeight, MomentReleases);

    public EditorDocument WithBendingAxis(SectionAxisDesignation bendingAxis) => WithSection(Section, bendingAxis);

    /// <summary>Replaces the segment boundaries and the segments together; everything else is kept.</summary>
    public EditorDocument WithSegments(IEnumerable<Length> segmentBoundaries, IEnumerable<EditorSegment> segments) =>
        new(Length, segmentBoundaries, segments, Supports, Loads, NamingState, DistributedLoads, SelfWeight, MomentReleases);

    /// <summary>Replaces one segment; boundaries and all other segments are kept.</summary>
    public EditorDocument WithSegment(int index, EditorSegment segment)
    {
        if ((uint)index >= (uint)_segments.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var segments = _segments.ToArray();
        segments[index] = segment.Id == Guid.Empty ? segment with { Id = _segments[index].Id } : segment;
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
        // The left half keeps the identity of the split segment, the right half is a new segment.
        return WithSegments(_boundaries.Take(index).Append(position).Concat(_boundaries.Skip(index)),
            _segments.Take(index + 1).Append(_segments[index] with { Id = Guid.NewGuid() }).Concat(_segments.Skip(index + 1)));
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

    // ---- Checked changes of the segment chain ----
    // Each returns the changed document or a rejection. The beam really gains or loses geometry: positions that
    // refer to a later segment move with it, a fixed distance from the beam start stays where it is. Nothing is ever
    // corrected, limited, re-attached or deleted to make a change valid.

    /// <summary>Replaces the whole chain, with the identities, order and lengths of <paramref name="segments"/>.</summary>
    public GeometryChange WithSegmentChain(IEnumerable<EditorSegment> segments)
    {
        var chain = NormalizeSegments(segments);
        var probe = new EditorDocument(chain);
        var conflicts = new List<GeometryConflict>();
        void Check(Guid id, string name, PositionRef? reference, DistributedLoadEndpoint? role = null)
        {
            if (reference is null or PositionRef.FromBeamStart) return;
            if (!probe.TryResolve(reference, out _, out var kind)) conflicts.Add(new(id, name, kind, role));
        }
        foreach (var s in Supports) Check(s.Id, s.Name, s.Reference);
        foreach (var l in Loads) Check(l.Id, l.Name, l.Reference);
        foreach (var l in DistributedLoads)
        {
            Check(l.Id, l.Name, l.StartReference, DistributedLoadEndpoint.Start);
            Check(l.Id, l.Name, l.EndReference, DistributedLoadEndpoint.End);
        }
        foreach (var r in MomentReleases) Check(r.Id, r.Name, r.Reference);
        if (conflicts.Count > 0) return GeometryChange.Blocked(conflicts);

        var changed = new EditorDocument(chain, Supports, Loads, NamingState, DistributedLoads, SelfWeight, MomentReleases);
        var broken = changed.PositionRuleConflicts();
        return broken.Count > 0 ? GeometryChange.Blocked(broken) : GeometryChange.Accepted(changed);
    }

    /// <summary>Inserts a new segment so that it gets this place in the chain; 0 is in front of the first segment.</summary>
    public GeometryChange InsertSegment(int index, EditorSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if ((uint)index > (uint)_segments.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!(segment.Length.Meters > 0)) throw new ArgumentException("A new segment needs a length.", nameof(segment));
        var chain = _segments.ToList();
        chain.Insert(index, segment.Id == Guid.Empty ? segment with { Id = Guid.NewGuid() } : segment);
        return WithSegmentChain(chain);
    }

    /// <summary>Appends a new segment behind the last one.</summary>
    public GeometryChange AddSegment(EditorSegment segment) => InsertSegment(_segments.Length, segment);

    /// <summary>Inserts the copy of a segment, with a new identity, directly after the original.</summary>
    public GeometryChange DuplicateSegment(Guid id)
    {
        int index = IndexOfSegment(id);
        return index < 0 ? GeometryChange.Rejected(GeometryRejection.UnknownSegment)
            : InsertSegment(index + 1, _segments[index] with { Id = Guid.NewGuid() });
    }

    /// <summary>Removes a segment; the last remaining segment cannot be removed.</summary>
    public GeometryChange RemoveSegment(Guid id)
    {
        int index = IndexOfSegment(id);
        if (index < 0) return GeometryChange.Rejected(GeometryRejection.UnknownSegment);
        if (_segments.Length == 1) return GeometryChange.Rejected(GeometryRejection.LastSegment);
        return WithSegmentChain(_segments.Where((_, i) => i != index));
    }

    /// <summary>Gives a segment another length; everything behind it moves by the difference.</summary>
    public GeometryChange ResizeSegment(Guid id, Length length)
    {
        int index = IndexOfSegment(id);
        if (index < 0) return GeometryChange.Rejected(GeometryRejection.UnknownSegment);
        if (!(length.Meters > 0)) throw new ArgumentException("A segment needs a length.", nameof(length));
        var chain = _segments.ToArray();
        chain[index] = chain[index] with { Length = length };
        return WithSegmentChain(chain);
    }

    /// <summary>
    /// The elements whose resolved positions break a position rule: outside the beam, a line load that no longer
    /// has a positive extent, and the exact coincidence rules of the Core validator. The self-weight is not a
    /// position and is left out; a beam without supports is not an error here.
    /// </summary>
    private IReadOnlyList<GeometryConflict> PositionRuleConflicts()
    {
        var conflicts = new List<GeometryConflict>();
        foreach (var l in DistributedLoads)
            if (!(l.StartPosition.Meters < l.EndPosition.Meters))
                conflicts.Add(new(l.Id, l.Name, GeometryConflictKind.RuleViolation, DistributedLoadEndpoint.End));
        if (conflicts.Count > 0) return conflicts;

        var pointLoads = Loads.Count;
        foreach (var error in BeamModelValidator.Validate(ToBeamModel(includeSelfWeight: false)))
        {
            if (error.Code == ValidationErrorCode.MissingSupports) continue;
            var match = System.Text.RegularExpressions.Regex.Match(error.Path ?? "", @"^(Supports|Loads|MomentReleases)\[(\d+)\]");
            if (!match.Success) continue;
            int i = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            (Guid Id, string Name)? entity = match.Groups[1].Value switch
            {
                "Supports" => (Supports[i].Id, Supports[i].Name),
                "MomentReleases" => (MomentReleases[i].Id, MomentReleases[i].Name),
                _ when i < pointLoads => (Loads[i].Id, Loads[i].Name),
                _ when i - pointLoads < DistributedLoads.Count => (DistributedLoads[i - pointLoads].Id, DistributedLoads[i - pointLoads].Name),
                _ => null
            };
            if (entity is { } e && !conflicts.Any(c => c.EntityId == e.Id))
                conflicts.Add(new(e.Id, e.Name, GeometryConflictKind.RuleViolation));
        }
        return conflicts;
    }

    private NamingState NextNamingState(NamingState? requested)
    {
        var next = requested ?? NamingState;
        if (!next.IsNotBehind(NamingState))
            throw new ArgumentException("Project naming counters cannot move backwards.", nameof(requested));
        return next;
    }

    public BeamModel ToBeamModel() => ToBeamModel(includeSelfWeight: true);

    private BeamModel ToBeamModel(bool includeSelfWeight) => new(Length, _segments.Select((segment, index) =>
        {
            var (start, end) = SegmentRange(index);
            return new BeamSegment(start, end, segment.Material, segment.Section, segment.BendingAxis);
        }),
        Supports.Select(s => s.Type == SupportType.Spring
            ? (s.Spring ?? throw new InvalidOperationException("A spring support needs its springs.")).ToCore(s.Position)
            : new Support(s.Position, s.Type)),
        Loads.Select(l => l.ToCore()).Concat(DistributedLoads.Select(l => l.ToCore()))
            .Concat(includeSelfWeight ? SelfWeightLoads : []), MomentReleases.Select(r => r.ToCore()));
}
