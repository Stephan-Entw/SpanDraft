using SpanDraft.Desktop;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;

namespace SpanDraft.Tests;

/// <summary>Builds the same shared services the host owns, over the stores a test supplies.</summary>
public static class TestServices
{
    /// <param name="load">Initializes the services like the bootstrap does; pass false to test the load itself.</param>
    public static SharedServices Create(LocalSettingsStore? settingsStore = null, UserSettings? settings = null,
        MaterialLibraryStore? materialStore = null, SectionLibraryStore? sectionStore = null, RecentProjectsStore? recentStore = null,
        Func<BuiltInMaterialCatalog>? catalogLoader = null, SettingsLoadResult? settingsLoad = null, bool load = true)
    {
        var settingsService = new SettingsService(settingsStore, settingsLoad ?? SettingsLoadResult.Loaded(settings ?? UserSettings.Default));
        var services = new SharedServices(settingsService, new(settingsService),
            materialStore is null ? MaterialLibraryService.Transient(catalogLoader) : new(materialStore, catalogLoader),
            sectionStore is null ? SectionLibraryService.Transient() : new(sectionStore),
            new(recentStore));
        if (load) services.InitializeAsync().GetAwaiter().GetResult();
        return services;
    }

    /// <summary>Services over a state directory (all stores), already initialized.</summary>
    public static SharedServices Create(DesktopStateStores stores, LocalSettingsStore? settingsStore = null, UserSettings? settings = null,
        bool load = true) => Create(settingsStore, settings, stores.Materials, stores.Sections, stores.Recents, load: load);
}

/// <summary>Headless hosts do not run the bootstrap; the test starts the shared services explicitly (never the process culture).</summary>
public static class TestHosts
{
    public static DesktopApplicationHost Initialized(DesktopApplicationHost host)
    {
        host.InitializeServicesAsync().GetAwaiter().GetResult();
        return host;
    }
}
