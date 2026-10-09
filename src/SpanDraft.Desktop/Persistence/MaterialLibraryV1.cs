namespace SpanDraft.Desktop.Persistence;

// Library wire contracts are independent of all project DTOs and versions.
internal sealed record MaterialLibraryV1
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required MaterialLibraryEntryV1[] Entries { get; init; }
}

internal sealed record MaterialCatalogV1
{
    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required MaterialLibraryEntryV1[] Entries { get; init; }
}

internal sealed record MaterialLibraryEntryV1
{
    public required LibraryMaterialV1 Material { get; init; }
    public required string Category { get; init; }
    public string? MaterialNumber { get; init; }
    public string[]? OtherStandards { get; init; }
}

internal sealed record LibraryMaterialV1
{
    public required string Name { get; init; }
    public required double YoungsModulus { get; init; }
    public required double YieldStrength { get; init; }
    public double? Density { get; init; }
    public double? PoissonRatio { get; init; }
}
