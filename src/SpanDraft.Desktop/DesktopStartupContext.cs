using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public sealed record DesktopStartupContext(IProjectFileStore Files, LocalSettingsStore SettingsStore, UserSettings Settings)
{
    public static async Task<DesktopStartupContext> LoadAsync(IProjectFileStore files, LocalSettingsStore settingsStore) =>
        new(files, settingsStore, await settingsStore.LoadAsync());
}
