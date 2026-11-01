namespace SpanDraft.Desktop.Persistence;

// Historical envelope; an existing info object requires a non-empty author.
public sealed record ProjectFileV3
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public ProjectInfoV3? Info { get; init; }
    public required DocumentV2 Document { get; init; }
    public required PresentationV2 Presentation { get; init; }
}

public sealed record ProjectInfoV3
{
    public required string CalculationTitle { get; init; }
    public string? ProjectTitle { get; init; }
    public string? Reference { get; init; }
    public required string Author { get; init; }
}
