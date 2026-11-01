namespace SpanDraft.Desktop.Persistence;

// Current envelope; document and presentation retain their V2 contracts.
public sealed record ProjectFileV4
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public ProjectInfoV4? Info { get; init; }
    public required DocumentV2 Document { get; init; }
    public required PresentationV2 Presentation { get; init; }
}

public sealed record ProjectInfoV4
{
    public required string CalculationTitle { get; init; }
    public string? ProjectTitle { get; init; }
    public string? Reference { get; init; }
    public string? Author { get; init; }
}
