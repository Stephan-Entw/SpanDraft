namespace SpanDraft.Desktop.Presentation;

/// <summary>Global preferences, never part of a project revision.</summary>
public sealed record UserSettings
{
    public UnitProfile Profile { get; init; } = UnitProfile.Default;
    public UnitProfileKind LastStandardProfile { get; init; } = UnitProfileKind.MechanicalEngineering;
    public UnitProfile? CustomProfile { get; init; }
    public PresentationMode Mode { get; init; } = PresentationMode.Standard;
    public bool HasCustomProfile => CustomProfile is not null;
    public ResultPresentationOptions Presentation => new(Profile, Mode);
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

    public bool ContentEquals(UserSettings other) => Mode == other.Mode && LastStandardProfile == other.LastStandardProfile
        && SameUnits(Profile, other.Profile) && HasCustomProfile == other.HasCustomProfile
        && (CustomProfile is null || SameUnits(CustomProfile, other.CustomProfile!));
}
