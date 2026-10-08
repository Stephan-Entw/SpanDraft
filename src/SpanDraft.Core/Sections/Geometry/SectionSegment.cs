namespace SpanDraft.Core.Sections.Geometry;

/// <summary>A directed boundary element. Only lines and circular arcs are supported.</summary>
public abstract class SectionSegment
{
    private protected SectionSegment(SectionPoint start, SectionPoint end)
    {
        Start = start;
        End = end;
    }

    public SectionPoint Start { get; }
    public SectionPoint End { get; }

    public abstract SectionSegment Reversed();
}

/// <summary>A non-zero directed straight segment.</summary>
public sealed class SectionLine : SectionSegment
{
    public SectionLine(SectionPoint start, SectionPoint end) : base(start, end)
    {
        if (start == end)
            throw new ArgumentException("A line must have distinct endpoints.", nameof(end));
    }

    public override SectionLine Reversed() => new(End, Start);
}

/// <summary>Direction in the y/z plane, viewed with y to the right and z upwards.</summary>
public enum ArcDirection
{
    Counterclockwise,
    Clockwise
}