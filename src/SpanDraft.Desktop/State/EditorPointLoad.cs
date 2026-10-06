using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Desktop identity and order only; Core loads never carry these IDs.</summary>
public abstract record EditorPointLoad(Guid Id, Length Position)
{
    public abstract PointLoadKind Kind { get; }
    public abstract double Value { get; }
    public abstract BeamLoad ToCore();

    public static EditorPointLoad Create(Guid id, Length position, PointLoadKind kind, double value) => kind switch
    {
        PointLoadKind.Force => new EditorPointForce(id, position, Force.FromNewtons(value)),
        PointLoadKind.Moment => new EditorPointMoment(id, position, Moment.FromNewtonMeters(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public sealed record EditorPointForce(Guid Id, Length Position, Force Force) : EditorPointLoad(Id, Position)
{
    public override PointLoadKind Kind => PointLoadKind.Force;
    public override double Value => Force.Newtons;
    public override BeamLoad ToCore() => new PointForce(Position, Force);
}

public sealed record EditorPointMoment(Guid Id, Length Position, Moment Moment) : EditorPointLoad(Id, Position)
{
    public override PointLoadKind Kind => PointLoadKind.Moment;
    public override double Value => Moment.NewtonMeters;
    public override BeamLoad ToCore() => new PointMoment(Position, Moment);
}

public enum PointLoadKind { Force, Moment }
public enum LoadInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
public sealed record PointLoadPreview(Length Position, PointLoadKind Kind, double Value, bool IsInvalid = false);
