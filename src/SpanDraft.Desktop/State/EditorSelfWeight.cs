namespace SpanDraft.Desktop.State;

/// <summary>Persistent identity and activation; magnitude and range come from the current beam.</summary>
public sealed record EditorSelfWeight(Guid Id, bool Enabled)
{
    public const string Symbol = "g";
    private Guid _id = Check(Id);

    /// <summary>The entity identity; an empty one is rejected by the constructor and by <c>with</c> alike.</summary>
    public Guid Id { get => _id; init => _id = Check(value); }

    private static Guid Check(Guid id) => id != Guid.Empty ? id : throw new ArgumentException("An entity ID is required.", nameof(Id));
}
