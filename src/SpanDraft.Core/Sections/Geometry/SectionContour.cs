namespace SpanDraft.Core.Sections.Geometry;

/// <summary>An ordered, simple closed boundary. Neighbouring endpoints must be exactly equal.</summary>
public sealed class SectionContour
{
    public SectionContour(IEnumerable<SectionSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var copy = segments.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A contour must contain at least one segment.", nameof(segments));
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is null)
                throw new ArgumentException("A contour cannot contain null segments.", nameof(segments));
        }
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i].End != copy[(i + 1) % copy.Length].Start)
                throw new ArgumentException("Contour endpoints must form a connected closed chain.", nameof(segments));
        }
        Segments = Array.AsReadOnly(copy);
        GeometryTopology.ValidateContour(this);
    }

    public IReadOnlyList<SectionSegment> Segments { get; }

    public SectionContour Reversed() => new(Segments.Reverse().Select(segment => segment.Reversed()));
}