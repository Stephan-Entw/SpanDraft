using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Persistence;

public sealed class LocalSettingsStore(IProjectFileStore files, string path, Action? ensureDirectory = null)
{
    public string FilePath { get; } = path;

    public static LocalSettingsStore Local(IProjectFileStore files)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "settings.json"), () => Directory.CreateDirectory(directory));
    }

    public async Task<UserSettings> LoadAsync()
    {
        try
        {
            var bytes = await files.ReadAsync(FilePath);
            return bytes is null ? UserSettings.Default : SettingsCodec.Deserialize(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return UserSettings.Default; }
    }

    public Task SaveAsync(UserSettings settings)
    {
        byte[] bytes = SettingsCodec.Serialize(settings);
        ensureDirectory?.Invoke();
        return files.WriteAtomicAsync(FilePath, bytes);
    }
}

public static class SettingsCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private sealed record SettingsData
    {
        public required int Version { get; init; }
        public required UnitProfileKind SelectedProfile { get; init; }
        public required UnitProfileKind LastStandardProfile { get; init; }
        public required bool HasCustomProfile { get; init; }
        public Dictionary<QuantityKind, string>? CustomUnits { get; init; }
        public int? SignificantDigits { get; init; }
        public EngineeringNotation? Notation { get; init; }
        public UiLanguagePreference? UiLanguage { get; init; }
        public RegionalFormatPreference? RegionalFormat { get; init; }
        public string? LastAuthor { get; init; }
    }

    public static byte[] Serialize(UserSettings settings) => JsonSerializer.SerializeToUtf8Bytes(new SettingsData
    {
        Version = 4, LastAuthor = settings.LastAuthor, SelectedProfile = settings.Profile.Kind, LastStandardProfile = settings.LastStandardProfile,
        HasCustomProfile = settings.HasCustomProfile, SignificantDigits = settings.SignificantDigits, Notation = settings.Notation,
        UiLanguage = settings.UiLanguage, RegionalFormat = settings.RegionalFormat,
        CustomUnits = settings.CustomProfile?.Units.ToDictionary(p => p.Key, p => p.Value.Id)
    }, Options);

    public static UserSettings Deserialize(byte[] bytes)
    {
        var dto = JsonSerializer.Deserialize<SettingsData>(bytes, Options) ?? throw new JsonException("Missing settings.");
        if (dto.Version is not (1 or 2 or 3 or 4) || !Enum.IsDefined(dto.SelectedProfile)
            || dto.LastStandardProfile == UnitProfileKind.Custom || !Enum.IsDefined(dto.LastStandardProfile)
            || dto.HasCustomProfile != (dto.CustomUnits is not null)
            || dto.SelectedProfile == UnitProfileKind.Custom && !dto.HasCustomProfile
            || dto.SelectedProfile != UnitProfileKind.Custom && dto.SelectedProfile != dto.LastStandardProfile)
            throw new JsonException("Invalid settings version or profile selection.");
        if (dto.Version >= 2 && (dto.UiLanguage is not { } language || !Enum.IsDefined(language)
            || dto.RegionalFormat is not { } region || !Enum.IsDefined(region)))
            throw new JsonException("Invalid language or regional format.");
        // V1–V3 Standard/Detailed both migrate to the new defaults; units remain unchanged.
        int digits = dto.Version < 4 ? ResultPresentationOptions.DefaultSignificantDigits
            : dto.SignificantDigits ?? throw new JsonException("Missing significant digits.");
        var notation = dto.Version < 4 ? EngineeringNotation.Automatic
            : dto.Notation ?? throw new JsonException("Missing notation.");
        if (digits is < ResultPresentationOptions.MinimumSignificantDigits or > ResultPresentationOptions.MaximumSignificantDigits
            || !Enum.IsDefined(notation)) throw new JsonException("Invalid numeric preferences.");
        UnitProfile? custom = null;
        if (dto.CustomUnits is not null)
        {
            var units = dto.CustomUnits.ToDictionary(p => p.Key,
                p => UnitCatalog.All.FirstOrDefault(u => u.Id == p.Value) ?? throw new JsonException("Unknown unit."));
            if (dto.Version < 4)
            {
                // Old custom profiles predate the semantic material quantities.
                foreach (var kind in new[] { QuantityKind.YoungsModulus, QuantityKind.MaterialStrength, QuantityKind.Density, QuantityKind.PoissonRatio })
                    units.TryAdd(kind, UserSettings.Standard(dto.LastStandardProfile)[kind]);
            }
            custom = new UnitProfile(units);
            if (UserSettings.Recognize(custom).Kind != UnitProfileKind.Custom)
                throw new JsonException("A custom profile must be an individual combination.");
        }
        string? lastAuthor = null;
        if (dto.Version >= 3)
        {
            using var json = JsonDocument.Parse(bytes);
            lastAuthor = json.RootElement.TryGetProperty("lastAuthor", out _) ? dto.LastAuthor
                : json.RootElement.TryGetProperty("defaultAuthor", out var legacy) ? legacy.GetString() : null;
        }
        return new UserSettings { LastAuthor = lastAuthor, CustomProfile = custom, SignificantDigits = digits, Notation = notation,
            UiLanguage = dto.Version == 1 ? UiLanguagePreference.System : dto.UiLanguage!.Value,
            RegionalFormat = dto.Version == 1 ? RegionalFormatPreference.System : dto.RegionalFormat!.Value,
            LastStandardProfile = dto.LastStandardProfile }.Select(dto.SelectedProfile);
    }
}
