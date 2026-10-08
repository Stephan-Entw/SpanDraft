namespace SpanDraft.Desktop.Presentation;

/// <summary>Application-level result display selection, independent of project revisions and persistence.</summary>
public sealed record ResultPresentationOptions
{
    public ResultPresentationOptions(UnitProfile? profile = null, PresentationMode mode = PresentationMode.Standard)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Profile = profile ?? UnitProfile.Default;
        Mode = mode;
    }

    public UnitProfile Profile { get; }
    public PresentationMode Mode { get; }
    public static ResultPresentationOptions Default { get; } = new();
}
