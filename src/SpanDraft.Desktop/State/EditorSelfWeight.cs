namespace SpanDraft.Desktop.State;

/// <summary>Persistent identity and activation; magnitude and range come from the current beam.</summary>
public sealed record EditorSelfWeight(Guid Id, bool Enabled)
{
    public const string Symbol = "g";
    public Guid Id { get; init; } = Id != Guid.Empty ? Id : throw new ArgumentException("An entity ID is required.", nameof(Id));
}
