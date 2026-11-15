using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Desktop identity and metadata for a transverse line load with linearly varying intensity.</summary>
public sealed record EditorDistributedLoad(Guid Id, Length StartPosition, Length EndPosition,
    ForcePerLength StartIntensity, ForcePerLength EndIntensity, string Name)
{
    private string _name = EntityNaming.Normalize(Name);

    /// <summary>Creates a constant load.</summary>
    public EditorDistributedLoad(Guid id, Length startPosition, Length endPosition, ForcePerLength intensity,
        string name) : this(id, startPosition, endPosition, intensity, intensity, name)
    {
    }

    public string Name { get => _name; init => _name = EntityNaming.Normalize(value); }

    /// <summary>What <see cref="StartPosition"/> is measured from; null means a fixed distance from the beam start.</summary>
    public PositionRef? StartReference { get; init; }
    /// <summary>What <see cref="EndPosition"/> is measured from; null means a fixed distance from the beam start.</summary>
    public PositionRef? EndReference { get; init; }
    public PositionRef EffectiveStartReference => StartReference ?? new PositionRef.FromBeamStart(StartPosition);
    public PositionRef EffectiveEndReference => EndReference ?? new PositionRef.FromBeamStart(EndPosition);
    public bool IsConstant => StartIntensity == EndIntensity;
    public DistributedLoad ToCore() => new(StartPosition, EndPosition, StartIntensity, EndIntensity);
}

public enum DistributedLoadInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
public enum DistributedLoadEndpoint { Start, End }
/// <summary>The transient range and end intensities of a load being placed or edited, in SI.</summary>
public sealed record DistributedLoadPreview(Length StartPosition, Length EndPosition, double StartIntensity,
    double EndIntensity, bool IsInvalid = false)
{
    /// <summary>A constant load.</summary>
    public DistributedLoadPreview(Length startPosition, Length endPosition, double intensity, bool isInvalid = false)
        : this(startPosition, endPosition, intensity, intensity, isInvalid)
    {
    }

    public bool IsConstant => StartIntensity == EndIntensity;
}
