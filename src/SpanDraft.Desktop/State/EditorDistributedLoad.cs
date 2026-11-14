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
    public bool IsConstant => StartIntensity == EndIntensity;
    public DistributedLoad ToCore() => new(StartPosition, EndPosition, StartIntensity, EndIntensity);
}

public enum DistributedLoadInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
public enum DistributedLoadEndpoint { Start, End }
public sealed record DistributedLoadPreview(Length StartPosition, Length EndPosition, double Intensity,
    bool IsInvalid = false);
