using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Services;

/// <summary>
/// The application-global runtime state, created exactly once by the <see cref="DesktopApplicationHost"/>. Stores stay behind
/// these services as persistence boundaries; windows and view models only see the services.
/// </summary>
public sealed record SharedServices(SettingsService Settings, SettingsCoordinator SettingsCoordinator,
    MaterialLibraryService Materials, SectionLibraryService Sections, RecentProjectsService Recents)
{
    public static SharedServices Create(LocalSettingsStore? settingsStore, SettingsLoadResult settings, DesktopStateStores? stores,
        Func<BuiltInMaterialCatalog>? builtInLoader = null) => Create(settingsStore, settings, stores, builtInLoader, null);

    internal static SharedServices Create(LocalSettingsStore? settingsStore, SettingsLoadResult settings, DesktopStateStores? stores,
        Func<BuiltInMaterialCatalog>? builtInLoader, Func<bool>? isApplicationShuttingDown)
    {
        var service = new SettingsService(settingsStore, settings, isApplicationShuttingDown);
        return new(service, new(service), new(stores?.Materials, builtInLoader), new(stores?.Sections), new(stores?.Recents));
    }

    /// <summary>No persistence at all: nothing touches the disk, and the libraries are readable but cannot be changed.</summary>
    public static SharedServices Transient(UserSettings? settings = null, Func<BuiltInMaterialCatalog>? builtInLoader = null)
    {
        var service = new SettingsService(null, SettingsLoadResult.Loaded(settings ?? UserSettings.Default));
        return new(service, new(service), MaterialLibraryService.Transient(builtInLoader), SectionLibraryService.Transient(), new(null));
    }

    /// <summary>Loads the libraries, the built-in catalog and the recent projects; every service loads at most once.</summary>
    public Task InitializeAsync() => Task.WhenAll(Materials.LoadAsync(), Sections.LoadAsync(), Recents.LoadAsync());
}
