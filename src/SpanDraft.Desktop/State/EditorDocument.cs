using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>The sole committed UI document. Input buffers are held separately.</summary>
public sealed record EditorDocument
{
    public EditorDocument(Length length, Material material, Section section,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorUniformDistributedLoad>? distributedLoads = null)
    {
        Length = length;
        Material = material;
        Section = section;
        Supports = Array.AsReadOnly((supports ?? []).ToArray());
        Loads = Array.AsReadOnly((loads ?? []).ToArray());
        DistributedLoads = Array.AsReadOnly((distributedLoads ?? []).ToArray());
        NamingState = namingState ?? new();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in NamedEntities)
            if (!names.Add(EntityNaming.Normalize(entity.Name)))
                throw new ArgumentException("Entity names must be unique across the project.", nameof(supports));
    }

    public Length Length { get; init; }
    public Material Material { get; init; }
    public Section Section { get; init; }
    public IReadOnlyList<EditorSupport> Supports { get; }
    public IReadOnlyList<EditorPointLoad> Loads { get; }
    public IReadOnlyList<EditorUniformDistributedLoad> DistributedLoads { get; }
    public NamingState NamingState { get; }
    public IEnumerable<(Guid Id, string Name)> NamedEntities =>
        Supports.Select(s => (s.Id, s.Name)).Concat(Loads.Select(l => (l.Id, l.Name)))
            .Concat(DistributedLoads.Select(l => (l.Id, l.Name)));

    public EditorDocument WithSupports(IEnumerable<EditorSupport> supports, NamingState? namingState = null) =>
        new(Length, Material, Section, supports, Loads, NextNamingState(namingState), DistributedLoads);

    public EditorDocument WithLoads(IEnumerable<EditorPointLoad> loads, NamingState? namingState = null) =>
        new(Length, Material, Section, Supports, loads, NextNamingState(namingState), DistributedLoads);

    public EditorDocument WithDistributedLoads(IEnumerable<EditorUniformDistributedLoad> loads, NamingState? namingState = null) =>
        new(Length, Material, Section, Supports, Loads, NextNamingState(namingState), loads);

    private NamingState NextNamingState(NamingState? requested)
    {
        var next = requested ?? NamingState;
        if (next.NextSupportOrdinal < NamingState.NextSupportOrdinal || next.NextForceNumber < NamingState.NextForceNumber
            || next.NextMomentNumber < NamingState.NextMomentNumber
            || next.NextDistributedLoadNumber < NamingState.NextDistributedLoadNumber)
            throw new ArgumentException("Project naming counters cannot move backwards.", nameof(requested));
        return next;
    }

    public BeamModel ToBeamModel() => new(Length, Material, Section,
        Supports.Select(s => new Support(s.Position, s.Type)),
        Loads.Select(l => l.ToCore()).Concat(DistributedLoads.Select(l => l.ToCore())));
}
