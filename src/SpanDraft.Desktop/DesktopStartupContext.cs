using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public sealed record DesktopStartupContext(IProjectFileStore Files, LocalSettingsStore SettingsStore, UserSettings Settings)
{
    public IProjectIdentityResolver? ProjectIdentities { get; init; }
    public DesktopStateStores? StateStores { get; init; }
    /// <summary>How the settings were obtained; absent when the settings were supplied directly, which counts as a valid load.</summary>
    public SettingsLoadResult? SettingsLoad { get; init; }
    public Func<SpanDraft.Desktop.Libraries.BuiltInMaterialCatalog>? BuiltInLoader { get; init; }
    public Func<string?> UserNameProvider { get; init; } =
        static () => OperatingSystem.IsWindows() ? Environment.UserName : null;

    public static async Task<DesktopStartupContext> LoadAsync(IProjectFileStore files, LocalSettingsStore settingsStore)
    {
        var load = await settingsStore.LoadDetailedAsync();
        return new(files, settingsStore, load.Settings) { SettingsLoad = load, StateStores = DesktopStateStores.Local(files) };
    }
}

/// <summary>The remaining local stores can be supplied together by an embedding host.</summary>
public sealed record DesktopStateStores(IRecoveryStorage Recovery,
    MaterialLibraryStore Materials, SectionLibraryStore Sections, RecentProjectsStore Recents)
{
    public static DesktopStateStores Local(IProjectFileStore files) => new(FileRecoveryStorage.Local(),
        MaterialLibraryStore.Local(files), SectionLibraryStore.Local(files), RecentProjectsStore.Local(files));

    /// <summary>
    /// The default of every composition that is not the production root (designer, tests, harnesses): the file-based
    /// stores keep going through <paramref name="files"/>, recovery stays in memory and never touches the user profile.
    /// </summary>
    public static DesktopStateStores Isolated(IProjectFileStore files) => Local(files) with { Recovery = new InMemoryRecoveryStorage() };

    public static DesktopStateStores InDirectory(IProjectFileStore files, string directory)
    {
        directory = Path.GetFullPath(directory);
        Action ensureDirectory = () => Directory.CreateDirectory(directory);
        return new(new FileRecoveryStorage(directory),
            new(files, Path.Combine(directory, "material-library.json"), ensureDirectory),
            new(files, Path.Combine(directory, "section-library.json"), ensureDirectory),
            new(files, Path.Combine(directory, "recent-projects.json"), ensureDirectory: ensureDirectory));
    }
}
