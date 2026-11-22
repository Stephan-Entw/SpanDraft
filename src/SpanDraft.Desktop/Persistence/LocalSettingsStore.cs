using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Persistence;

/// <summary>Why the settings of a process are what they are; the file is only called damaged when it was read and rejected.</summary>
public enum SettingsLoadState
{
    /// <summary>The file does not exist; defaults are in use and nothing needs protection.</summary>
    Missing,
    /// <summary>The file was read and is valid.</summary>
    Valid,
    /// <summary>The file was read, but its content or format is invalid; defaults are in use and the file is kept.</summary>
    RecoveredFromInvalidSettings,
    /// <summary>The file could not be read (transient or technical failure); its content is unknown, not damaged.</summary>
    Unreadable
}

/// <summary>The outcome of one settings load. <see cref="InvalidContent"/> keeps the rejected bytes for a diagnostic backup.</summary>
public sealed record SettingsLoadResult(SettingsLoadState State, UserSettings Settings, byte[]? InvalidContent = null)
{
    public static SettingsLoadResult Loaded(UserSettings settings) => new(SettingsLoadState.Valid, settings);
}

public sealed class LocalSettingsStore(IProjectFileStore files, string path, Action? ensureDirectory = null)
{
    private readonly object _queueLock = new();
    private Task _queue = Task.CompletedTask;
    public string FilePath { get; } = path;

    public static LocalSettingsStore Local(IProjectFileStore files)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpanDraft");
        return new(files, Path.Combine(directory, "settings.json"), () => Directory.CreateDirectory(directory));
    }

    public async Task<UserSettings> LoadAsync() => (await LoadDetailedAsync()).Settings;

    /// <summary>Never throws for file or content problems: SpanDraft starts with defaults and says why.</summary>
    public async Task<SettingsLoadResult> LoadDetailedAsync()
    {
        byte[]? bytes;
        try { bytes = await files.ReadAsync(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(SettingsLoadState.Unreadable, UserSettings.Default); }
        if (bytes is null) return new(SettingsLoadState.Missing, UserSettings.Default);
        try { return SettingsLoadResult.Loaded(SettingsCodec.Deserialize(bytes)); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { return new(SettingsLoadState.RecoveredFromInvalidSettings, UserSettings.Default, bytes); }
    }

    /// <summary>
    /// Keeps the rejected original under a diagnostic name next to the settings file, never replacing an existing backup.
    /// Best effort: returns the path, or null when no backup could be made.
    /// </summary>
    public async Task<string?> BackupInvalidAsync(byte[] original, CancellationToken cancellationToken = default)
    {
        try
        {
            ensureDirectory?.Invoke();
            string directory = Path.GetDirectoryName(FilePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(FilePath), extension = Path.GetExtension(FilePath);
            for (int i = 0; i < 100; i++)
            {
                string candidate = Path.Combine(directory, i == 0 ? $"{name}.invalid{extension}" : $"{name}.invalid.{i}{extension}");
                if (await files.TryWriteNewAtomicAsync(candidate, original, cancellationToken)) return candidate;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException) { }
        return null;
    }

    public Task SaveAsync(UserSettings settings)
    {
        byte[] bytes = SettingsCodec.Serialize(settings);
        ensureDirectory?.Invoke();
        // Writes are published in the order of the calls, so an older snapshot can never replace a newer one.
        lock (_queueLock) return _queue = WriteAfterAsync(_queue, bytes);
    }

    private async Task WriteAfterAsync(Task previous, byte[] bytes)
    {
        // The failure of an earlier save belongs to its own caller.
        try { await previous; }
        catch (Exception) { }
        await files.WriteAtomicAsync(FilePath, bytes);
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
        // One parse serves both the typed data and the check which author field the file really contains.
        using var json = JsonDocument.Parse(bytes);
        var dto = json.RootElement.Deserialize<SettingsData>(Options) ?? throw new JsonException("Missing settings.");
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
        // Earlier versions allowed up to 15 digits; larger stored values now load as the maximum.
        digits = Math.Min(digits, ResultPresentationOptions.MaximumSignificantDigits);
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
            // Every version predating the spring supports lacks their stiffness quantities; a missing entry takes the
            // unit of the last standard profile instead of invalidating the custom profile.
            foreach (var kind in new[] { QuantityKind.TranslationalStiffness, QuantityKind.RotationalStiffness })
                units.TryAdd(kind, UserSettings.Standard(dto.LastStandardProfile)[kind]);
            custom = new UnitProfile(units);
            if (UserSettings.Recognize(custom).Kind != UnitProfileKind.Custom)
                throw new JsonException("A custom profile must be an individual combination.");
        }
        string? lastAuthor = null;
        if (dto.Version >= 3)
        {
            lastAuthor = json.RootElement.TryGetProperty("lastAuthor", out _) ? dto.LastAuthor
                : json.RootElement.TryGetProperty("defaultAuthor", out var legacy) ? legacy.GetString() : null;
        }
        return new UserSettings { LastAuthor = lastAuthor, CustomProfile = custom, SignificantDigits = digits, Notation = notation,
            UiLanguage = dto.Version == 1 ? UiLanguagePreference.System : dto.UiLanguage!.Value,
            RegionalFormat = dto.Version == 1 ? RegionalFormatPreference.System : dto.RegionalFormat!.Value,
            LastStandardProfile = dto.LastStandardProfile }.Select(dto.SelectedProfile);
    }
}
