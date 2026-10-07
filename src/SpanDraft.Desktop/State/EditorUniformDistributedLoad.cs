using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Desktop identity and metadata for a constant transverse line load.</summary>
public sealed record EditorUniformDistributedLoad(Guid Id, Length StartPosition, Length EndPosition,
    ForcePerLength Intensity, string Name)
{
    private string _name = EntityNaming.Normalize(Name);
    public string Name { get => _name; init => _name = EntityNaming.Normalize(value); }
    public UniformDistributedLoad ToCore() => new(StartPosition, EndPosition, Intensity);
}

public enum DistributedLoadInteraction { Neutral, Placement, NewDraft, EditDraft, Drag }
public enum DistributedLoadEndpoint { Start, End }
public sealed record DistributedLoadPreview(Length StartPosition, Length EndPosition, double Intensity,
    bool IsInvalid = false);
