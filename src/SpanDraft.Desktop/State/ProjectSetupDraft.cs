using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Only confirmed Create values; no session, input buffers or navigation state.</summary>
public sealed record ProjectSetupDraft(Length? Length = null, Material? Material = null,
    ISectionDefinition? Section = null, SectionAxisDesignation BendingAxis = SectionAxisDesignation.Y,
    string? SectionDisplayName = null, string? CalculationTitle = null, string? ProjectTitle = null,
    string? Reference = null, string? Author = null)
{
    public bool ContentEquals(ProjectSetupDraft other) => Length == other.Length
        && CalculationTitle == other.CalculationTitle && ProjectTitle == other.ProjectTitle
        && Reference == other.Reference && Author == other.Author
        && BendingAxis == other.BendingAxis && SectionDisplayName == other.SectionDisplayName
        && (ReferenceEquals(Material, other.Material) || Material is { } a && other.Material is { } b
            && ProjectState.MaterialContentEquals(a, b))
        && (ReferenceEquals(Section, other.Section) || Section is { } x && other.Section is { } y
            && ProjectState.SectionContentEquals(x, y));
}
