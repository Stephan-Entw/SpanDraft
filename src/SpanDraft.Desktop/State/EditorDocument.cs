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
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null)
    {
        Length = length;
        Material = material;
        Section = section;
        Supports = Array.AsReadOnly((supports ?? []).ToArray());
        Loads = Array.AsReadOnly((loads ?? []).ToArray());
    }

    public Length Length { get; init; }
    public Material Material { get; init; }
    public Section Section { get; init; }
    public IReadOnlyList<EditorSupport> Supports { get; }
    public IReadOnlyList<EditorPointLoad> Loads { get; }

    public EditorDocument WithSupports(IEnumerable<EditorSupport> supports) =>
        new(Length, Material, Section, supports, Loads);

    public EditorDocument WithLoads(IEnumerable<EditorPointLoad> loads) =>
        new(Length, Material, Section, Supports, loads);

    public BeamModel ToBeamModel() => new(Length, Material, Section,
        Supports.Select(s => new Support(s.Position, s.Type)), Loads.Select(l => l.ToCore()));
}
