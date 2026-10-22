namespace SpanDraft.Desktop.State;

/// <summary>Validated project information, independent of file names and user preferences.</summary>
public sealed record ProjectInfo
{
    public ProjectInfo(string calculationTitle, string? projectTitle, string? reference, string author)
    {
        CalculationTitle = Required(calculationTitle, nameof(calculationTitle));
        ProjectTitle = Optional(projectTitle);
        Reference = Optional(reference);
        Author = Required(author, nameof(author));
    }

    public string CalculationTitle { get; }
    public string? ProjectTitle { get; }
    public string? Reference { get; }
    public string Author { get; }

    internal static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Required(string value, string name) => Optional(value)
        ?? throw new ArgumentException("A non-empty value is required.", name);
}
