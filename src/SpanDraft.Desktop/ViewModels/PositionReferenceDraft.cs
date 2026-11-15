using System.Globalization;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>One choice of what a position is measured from: the beam start or a segment.</summary>
public sealed record PositionReferenceOption(Guid? Segment, string Name);

/// <summary>
/// What the flyout of a position decides besides its value: what it is measured from, and whether it is bound to the
/// end of a segment. Moving the position follows the rules of <see cref="PositionReferences"/>; the user changes
/// the reference or the binding only through this draft, and the document is not touched until the flyout is confirmed.
/// </summary>
public sealed class PositionReferenceDraft : ObservableObject
{
    private readonly Func<EditorDocument> _read;
    private PositionRef _current;
    private Length _position;

    /// <param name="reference">The reference the position has now; null for a position that is set freshly.</param>
    /// <param name="position">The position that reference belongs to.</param>
    public PositionReferenceDraft(Func<EditorDocument> read, PositionRef? reference, Length position)
    {
        _read = read;
        _position = position;
        _current = reference ?? PositionReferences.ForNewPosition(read(), position);
        _options = BuildOptions();
    }

    private IReadOnlyList<PositionReferenceOption> _options;

    /// <summary>The reference that would be stored for the current position.</summary>
    public PositionRef Current => _current;

    /// <summary>The choice of reference only matters, and is only shown, with several segments.</summary>
    public bool ShowReference => _read().HasSeveralSegments;

    /// <summary>The beam start and the segments that contain the position; the list only changes when its content does.</summary>
    public IReadOnlyList<PositionReferenceOption> Options => _options;

    private IReadOnlyList<PositionReferenceOption> BuildOptions()
    {
        var document = _read();
        var options = new List<PositionReferenceOption> { new(null, Strings.PositionFromBeamStart) };
        var containing = PositionReferences.SegmentsContaining(document, _position).ToList();
        if (_current.SegmentId is { } current && document.IndexOfSegment(current) is var own and >= 0 && !containing.Contains(own))
            containing.Add(own);
        foreach (int index in containing.Order())
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
            _current = value.Segment is { } id ? PositionReferences.InSegment(document, document.IndexOfSegment(id), _position)
                : new PositionRef.FromBeamStart(_position);
            NotifyAll();
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

    /// <summary>Called when the user has moved the position, by the pointer or by the text.</summary>
    internal void PositionChanged(Length position)
    {
        if (position == _position) return;
        _current = PositionReferences.AfterMove(_read(), _current, _position, position);
        _position = position;
        NotifyAll();
    }

    private string SegmentName(int index) => string.Format(CultureInfo.CurrentCulture, Strings.SegmentSelected, index + 1);

    // The options are replaced only when they differ: replacing an unchanged list makes a combo box drop and re-offer its
    // selection, which must never be taken for a choice of the user.
    private void NotifyAll()
    {
        var options = BuildOptions();
        bool optionsChanged = !options.SequenceEqual(_options);
        if (optionsChanged) _options = options;
        foreach (string name in new[] { nameof(Current), nameof(ShowReference), nameof(SelectedOption),
            nameof(CanHoldEnd), nameof(HoldEnd), nameof(HoldEndLabel) }) Notify(name);
        if (optionsChanged) Notify(nameof(Options));
    }
}
