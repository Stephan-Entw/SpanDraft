namespace SpanDraft.Desktop.Presentation;

/// <summary>Global preferences and local convenience values, never part of a project revision.</summary>
public sealed record UserSettings
{
    public UnitProfile Profile { get; init; } = UnitProfile.Default;
    public UnitProfileKind LastStandardProfile { get; init; } = UnitProfileKind.MechanicalEngineering;
    public UnitProfile? CustomProfile { get; init; }
    private int _significantDigits = ResultPresentationOptions.DefaultSignificantDigits;
    private EngineeringNotation _notation = EngineeringNotation.Automatic;
    public int SignificantDigits
    {
        get => _significantDigits;
        init { ResultPresentationOptions.ValidateSignificantDigits(value); _significantDigits = value; }
    }
    public EngineeringNotation Notation
    {
        get => _notation;
        init { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); _notation = value; }
    }
    public UiLanguagePreference UiLanguage { get; init; } = UiLanguagePreference.System;
    public RegionalFormatPreference RegionalFormat { get; init; } = RegionalFormatPreference.System;
    private string? _lastAuthor;
    public string? LastAuthor
    {
        get => _lastAuthor;
        init => _lastAuthor = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    public bool HasCustomProfile => CustomProfile is not null;
    public ResultPresentationOptions Presentation => new(Profile, SignificantDigits, Notation);
    public static UserSettings Default { get; } = new();

    public static UnitProfile Standard(UnitProfileKind kind) => kind switch
    {
        UnitProfileKind.MechanicalEngineering => UnitProfile.MechanicalEngineering,
        UnitProfileKind.StructuralEngineering => UnitProfile.StructuralEngineering,
        UnitProfileKind.UnitedStates => UnitProfile.UnitedStates,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool SameUnits(UnitProfile left, UnitProfile right) =>
        Enum.GetValues<QuantityKind>().All(q => left[q].Id == right[q].Id);

    public static UnitProfile Recognize(UnitProfile profile) =>
        new[] { UnitProfile.MechanicalEngineering, UnitProfile.StructuralEngineering, UnitProfile.UnitedStates }
            .FirstOrDefault(p => SameUnits(p, profile)) ?? profile;

    public UserSettings Select(UnitProfileKind kind)
    {
        var profile = kind == UnitProfileKind.Custom
            ? CustomProfile ?? throw new InvalidOperationException("No custom configuration exists.") : Standard(kind);
        return this with { Profile = profile, LastStandardProfile = kind == UnitProfileKind.Custom ? LastStandardProfile : kind };
    }

    public UserSettings WithUnit(QuantityKind quantity, UnitDefinition unit)
    {
        var profile = Recognize(Profile.WithUnit(quantity, unit));
        return this with
        {
            Profile = profile,
            CustomProfile = profile.Kind == UnitProfileKind.Custom ? profile : CustomProfile,
            LastStandardProfile = profile.Kind == UnitProfileKind.Custom ? LastStandardProfile : profile.Kind
        };
    }

    public bool ContentEquals(UserSettings other) => SignificantDigits == other.SignificantDigits && Notation == other.Notation && LastStandardProfile == other.LastStandardProfile
        && LastAuthor == other.LastAuthor && UiLanguage == other.UiLanguage && RegionalFormat == other.RegionalFormat
        && SameUnits(Profile, other.Profile) && HasCustomProfile == other.HasCustomProfile
        && (CustomProfile is null || SameUnits(CustomProfile, other.CustomProfile!));
}
