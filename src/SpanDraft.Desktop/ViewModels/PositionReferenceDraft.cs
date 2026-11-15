using System.Globalization;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>One choice of what a position is measured from: the beam start or a segment.</summary>
public sealed record PositionReferenceOption(Guid? Segment, string Name);

/// <summary>
/// What the flyout of a position decides besides its value: what the position is measured from, how far from it, and
/// whether it is bound to the end of a segment. The distance the flyout shows is the distance from this reference;
/// the global x is only shown next to it. Changing the reference keeps the point where it is and re-expresses its
/// distance; typing a distance never changes the reference. Moving the position with the pointer follows the rules of
/// <see cref="PositionReferences"/>. The document is not touched until the flyout is confirmed.
/// </summary>
public sealed class PositionReferenceDraft : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private readonly UnitDefinition _unit;
    private PositionRef _current;
    private Length _position;
    private IReadOnlyList<PositionReferenceOption> _options;

    /// <param name="reference">The reference the position has now; null for a position that is set freshly.</param>
    /// <param name="position">The position that reference belongs to.</param>
    public PositionReferenceDraft(Func<EditorDocument> read, PositionRef? reference, Length position, UnitDefinition? unit = null)
    {
        _read = read;
        _unit = unit ?? UnitCatalog.Millimeter;
        _position = position;
        _current = reference ?? PositionReferences.ForNewPosition(read(), position);
        _options = BuildOptions();
    }

    /// <summary>Raised when the reference or the point changed by a choice or a pointer move, so the shown distance must follow.</summary>
    public event Action? DistanceChanged;

    /// <summary>The reference that would be stored for the current position.</summary>
    public PositionRef Current => _current;

    /// <summary>The global position of the point.</summary>
    public Length Position => _position;

    /// <summary>The distance of the point from its reference: from the beam start, the segment start, or the length of the segment when bound to its end.</summary>
    public Length Distance => _current switch
    {
        PositionRef.FromBeamStart beam => beam.Distance,
        PositionRef.FromSegmentStart fromStart => fromStart.Offset,
        PositionRef.AtSegmentEnd end => SegmentOf(end.Segment)?.Length ?? default,
        _ => default
    };

    /// <summary>The largest distance from the current reference that still lies in the beam or its segment.</summary>
    public Length MaxDistance => _current.SegmentId is { } id && SegmentOf(id) is { } segment ? segment.Length : _read().Length;

    public bool IsSegmentBased => _current.SegmentId is not null;

    /// <summary>The choice of reference only matters, and is only shown, with several segments.</summary>
    public bool ShowReference => _read().HasSeveralSegments;

    /// <summary>The global position next to the distance, which is only a different number once the reference is a segment.</summary>
    public bool ShowPositionInfo => ShowReference && _current is not PositionRef.FromBeamStart;

    public string PositionInfo => string.Format(CultureInfo.CurrentCulture, Strings.GlobalPositionInfo,
        InputQuantityFormatter.FormatExact(_position.Meters, _unit), _unit.Symbol);

    /// <summary>
    /// The beam start and the segments that contain the point, so every choice describes the same point and none moves it;
    /// on an inner boundary both neighbours are offered. The list only changes when its content does.
    /// </summary>
    public IReadOnlyList<PositionReferenceOption> Options => _options;

    private IReadOnlyList<PositionReferenceOption> BuildOptions()
    {
        var document = _read();
        var options = new List<PositionReferenceOption> { new(null, Strings.PositionFromBeamStart) };
        foreach (int index in PositionReferences.SegmentsContaining(document, _position))
            options.Add(new(document.Segments[index].Id, SegmentName(index)));
        return options;
    }

    public PositionReferenceOption? SelectedOption
    {
        get => Options.FirstOrDefault(option => option.Segment == _current.SegmentId);
        set
        {
            if (value is null || value.Segment == _current.SegmentId) return;
            var document = _read();
            if (value.Segment is { } id)
            {
                int index = document.IndexOfSegment(id);
                if (index < 0 || !PositionReferences.SegmentsContaining(document, _position).Contains(index)) return;
                _current = PositionReferences.InSegment(document, index, _position);
            }
            else _current = new PositionRef.FromBeamStart(_position);
            _position = document.ResolvePosition(_current);
            NotifyAll();
            DistanceChanged?.Invoke();
        }
    }

    /// <summary>An end binding is offered where the position is exactly the end of a segment, or is bound already.</summary>
    public bool CanHoldEnd => _current is PositionRef.AtSegmentEnd || PositionReferences.EndingSegmentIndex(_read(), _position) is not null;

    public bool HoldEnd
    {
        get => _current is PositionRef.AtSegmentEnd;
        set
        {
            var document = _read();
            if (value && _current is not PositionRef.AtSegmentEnd && PositionReferences.EndingSegmentIndex(document, _position) is { } ending)
                _current = PositionReferences.AtEndOf(document, ending);
            else if (!value && _current is PositionRef.AtSegmentEnd end)
                _current = PositionReferences.InSegment(document, document.IndexOfSegment(end.Segment), _position);
            else return;
            NotifyAll();
            DistanceChanged?.Invoke();
        }
    }

    /// <summary>Names the segment the binding is to, so a position on a boundary is not left ambiguous.</summary>
    public string HoldEndLabel
    {
        get
        {
            var document = _read();
            int? index = _current is PositionRef.AtSegmentEnd end ? document.IndexOfSegment(end.Segment)
                : PositionReferences.EndingSegmentIndex(document, _position);
            return document.HasSeveralSegments && index is { } number and >= 0
                ? string.Format(CultureInfo.CurrentCulture, Strings.HoldAtEndOfSegment, number + 1) : Strings.HoldAtSegmentEnd;
        }
    }

    /// <summary>Reads a typed distance from the current reference; false when it is not a length or lies beyond the beam or segment.</summary>
    public bool TryParseDistance(string text, out Length distance) =>
        InputQuantityFormatter.TryParsePosition(text, _unit, MaxDistance, out distance);

    /// <summary>
    /// Applies a typed distance from the current reference; the reference stays. A bound end that is typed over becomes a
    /// distance from the segment start. False, and nothing changes, when the distance lies beyond the beam or segment.
    /// </summary>
    public bool TrySetDistance(Length distance)
    {
        if (distance.Meters > MaxDistance.Meters) return false;
        var document = _read();
        var next = _current;
        switch (_current)
        {
            case PositionRef.FromBeamStart:
                next = new PositionRef.FromBeamStart(distance);
                break;
            case PositionRef.FromSegmentStart fromStart:
                next = fromStart with { Offset = distance };
                break;
            case PositionRef.AtSegmentEnd end:
                if (distance == Distance) return true;
                next = new PositionRef.FromSegmentStart(end.Segment, distance);
                break;
        }
        var position = document.ResolvePosition(next);
        // Every validation of the flyout passes through here; nothing is announced while nothing changed.
        if (next == _current && position == _position) return true;
        _current = next;
        _position = position;
        NotifyAll();
        return true;
    }

    /// <summary>Called when the user has moved the point with the pointer.</summary>
    internal void PositionChanged(Length position)
    {
        if (position == _position) return;
        _current = PositionReferences.AfterMove(_read(), _current, _position, position);
        _position = position;
        NotifyAll();
        DistanceChanged?.Invoke();
    }

    private EditorSegment? SegmentOf(Guid id)
    {
        var document = _read();
        int index = document.IndexOfSegment(id);
        return index >= 0 ? document.Segments[index] : null;
    }

    private string SegmentName(int index) => string.Format(CultureInfo.CurrentCulture, Strings.SegmentSelected, index + 1);

    // The options are replaced only when they differ: replacing an unchanged list makes a combo box drop and re-offer its
    // selection, which must never be taken for a choice of the user.
    private void NotifyAll()
    {
        var options = BuildOptions();
        bool optionsChanged = !options.SequenceEqual(_options);
        if (optionsChanged) _options = options;
        foreach (string name in new[] { nameof(Current), nameof(Position), nameof(Distance), nameof(MaxDistance), nameof(IsSegmentBased),
            nameof(ShowReference), nameof(ShowPositionInfo), nameof(PositionInfo), nameof(SelectedOption),
            nameof(CanHoldEnd), nameof(HoldEnd), nameof(HoldEndLabel) }) Notify(name);
        if (optionsChanged) Notify(nameof(Options));
    }
}
