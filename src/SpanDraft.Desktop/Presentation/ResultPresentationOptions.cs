namespace SpanDraft.Desktop.Presentation;

public enum EngineeringNotation { Automatic, Off }

/// <summary>Global display preferences, independent of stored SI values and project revisions.</summary>
public sealed record ResultPresentationOptions
{
    // 1–15 is useful technical precision within double's reliable decimal precision.
    public const int MinimumSignificantDigits = 1;
    public const int MaximumSignificantDigits = 15;
    public const int DefaultSignificantDigits = 3;

    public ResultPresentationOptions(UnitProfile? profile = null, int significantDigits = DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        ValidateSignificantDigits(significantDigits);
        if (!Enum.IsDefined(notation)) throw new ArgumentOutOfRangeException(nameof(notation));
        Profile = profile ?? UnitProfile.Default;
        SignificantDigits = significantDigits;
        Notation = notation;
    }

    internal static void ValidateSignificantDigits(int digits)
    {
        if (digits is < MinimumSignificantDigits or > MaximumSignificantDigits)
            throw new ArgumentOutOfRangeException(nameof(digits));
    }

    public UnitProfile Profile { get; }
    public int SignificantDigits { get; }
    public EngineeringNotation Notation { get; }
    public static ResultPresentationOptions Default { get; } = new();
}
