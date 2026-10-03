using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>The sole committed UI document. Input buffers are held separately.</summary>
public sealed record EditorDocument(Length Length, Material Material, Section Section)
{
    public BeamModel ToBeamModel() => new(Length, Material, Section, [], []);
}
