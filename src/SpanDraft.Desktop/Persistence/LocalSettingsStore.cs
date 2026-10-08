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

    private sealed record SettingsV1
    {
        public required int Version { get; init; }
        public required UnitProfileKind SelectedProfile { get; init; }
        public required UnitProfileKind LastStandardProfile { get; init; }
        public required bool HasCustomProfile { get; init; }
        public Dictionary<QuantityKind, string>? CustomUnits { get; init; }
        public required PresentationMode PresentationMode { get; init; }
    }

    public static byte[] Serialize(UserSettings settings) => JsonSerializer.SerializeToUtf8Bytes(new SettingsV1
    {
        Version = 1, SelectedProfile = settings.Profile.Kind, LastStandardProfile = settings.LastStandardProfile,
        HasCustomProfile = settings.HasCustomProfile, PresentationMode = settings.Mode,
        CustomUnits = settings.CustomProfile?.Units.ToDictionary(p => p.Key, p => p.Value.Id)
    }, Options);

    public static UserSettings Deserialize(byte[] bytes)
    {
        var dto = JsonSerializer.Deserialize<SettingsV1>(bytes, Options) ?? throw new JsonException("Missing settings.");
        if (dto.Version != 1 || !Enum.IsDefined(dto.SelectedProfile) || !Enum.IsDefined(dto.PresentationMode)
            || dto.LastStandardProfile == UnitProfileKind.Custom || !Enum.IsDefined(dto.LastStandardProfile)
            || dto.HasCustomProfile != (dto.CustomUnits is not null)
            || dto.SelectedProfile == UnitProfileKind.Custom && !dto.HasCustomProfile
            || dto.SelectedProfile != UnitProfileKind.Custom && dto.SelectedProfile != dto.LastStandardProfile)
            throw new JsonException("Invalid settings version or profile selection.");
        UnitProfile? custom = null;
        if (dto.CustomUnits is not null)
        {
            var units = dto.CustomUnits.ToDictionary(p => p.Key,
                p => UnitCatalog.All.FirstOrDefault(u => u.Id == p.Value) ?? throw new JsonException("Unknown unit."));
            custom = new UnitProfile(units);
            if (UserSettings.Recognize(custom).Kind != UnitProfileKind.Custom)
                throw new JsonException("A custom profile must be an individual combination.");
        }
        return new UserSettings { CustomProfile = custom, Mode = dto.PresentationMode,
            LastStandardProfile = dto.LastStandardProfile }.Select(dto.SelectedProfile);
    }
}
