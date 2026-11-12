namespace SpanDraft.Desktop.Presentation;

public enum EngineeringNotation { Automatic, Off }

/// <summary>
/// Global display preferences for given and calculated values alike, independent of stored SI values and
/// project revisions.
/// </summary>
public sealed record ResultPresentationOptions
{
    // One setting for every displayed value; more than five digits adds no engineering information.
    public const int MinimumSignificantDigits = 1;
    public const int MaximumSignificantDigits = 5;
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
