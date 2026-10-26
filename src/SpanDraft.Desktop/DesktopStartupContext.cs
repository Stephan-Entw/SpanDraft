using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public sealed record DesktopStartupContext(IProjectFileStore Files, LocalSettingsStore SettingsStore, UserSettings Settings)
{
    public DesktopStateStores? StateStores { get; init; }
    public Func<string?> UserNameProvider { get; init; } =
        static () => OperatingSystem.IsWindows() ? Environment.UserName : null;

    public static async Task<DesktopStartupContext> LoadAsync(IProjectFileStore files, LocalSettingsStore settingsStore) =>
        new(files, settingsStore, await settingsStore.LoadAsync());
}

/// <summary>The remaining local stores can be supplied together by an embedding host.</summary>
public sealed record DesktopStateStores(ProjectRecovery Recovery, ProjectSetupRecovery SetupRecovery,
    MaterialLibraryStore Materials, SectionLibraryStore Sections, RecentProjectsStore Recents)
{
    public static DesktopStateStores Local(IProjectFileStore files) => new(ProjectRecovery.Local(files),
        ProjectSetupRecovery.Local(files), MaterialLibraryStore.Local(files), SectionLibraryStore.Local(files),
        RecentProjectsStore.Local(files));

    public static DesktopStateStores InDirectory(IProjectFileStore files, string directory)
    {
        directory = Path.GetFullPath(directory);
        Action ensureDirectory = () => Directory.CreateDirectory(directory);
        return new(new(files, Path.Combine(directory, "recovery.json"), ensureDirectory: ensureDirectory),
            new(files, Path.Combine(directory, "setup-recovery.json"), ensureDirectory: ensureDirectory),
            new(files, Path.Combine(directory, "material-library.json"), ensureDirectory),
            new(files, Path.Combine(directory, "section-library.json"), ensureDirectory),
            new(files, Path.Combine(directory, "recent-projects.json"), ensureDirectory: ensureDirectory));
    }
}
