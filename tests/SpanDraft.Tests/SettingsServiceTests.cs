using System.Text;
using System.Globalization;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class SettingsServiceTests
{
    private static readonly string Path_ = TestPath("settings.json");
    private static readonly byte[] Damaged = Encoding.UTF8.GetBytes("{ not json");

    private static (SettingsService Service, Files Files, LocalSettingsStore Store) Create(Action<Files>? arrange = null)
    {
        var files = new Files(); arrange?.Invoke(files);
        var store = new LocalSettingsStore(files, Path_);
        var load = store.LoadDetailedAsync().GetAwaiter().GetResult();
        return (new SettingsService(store, load), files, store);
    }

    private static SettingsPatch Patch(UserSettings from, int digits) => SettingsPatch.From(from) with { SignificantDigits = digits };

    [Fact]
    public void MissingFileGivesDefaultsWhichAreNotClaimedToBePersisted()
    {
        var (service, _, _) = Create();
        Assert.Equal(SettingsLoadState.Missing, service.LoadState);
        Assert.True(UserSettings.Default.ContentEquals(service.CurrentSettings));
        Assert.Null(service.LastPersistedSettings);
    }

    [Fact]
    public void ValidFileGivesCurrentAndLastPersistedAsLoaded()
    {
        var saved = UserSettings.Default with { SignificantDigits = 5, LastAuthor = "Ada" };
        var (service, _, _) = Create(f => f.Data[Path_] = SettingsCodec.Serialize(saved));
        Assert.Equal(SettingsLoadState.Valid, service.LoadState);
        Assert.True(saved.ContentEquals(service.CurrentSettings));
        Assert.True(saved.ContentEquals(service.LastPersistedSettings!));
    }

    [Fact]
    public void InvalidContentIsRecoveredWithDefaultsAndKeepsTheOriginalUntouched()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        Assert.Equal(SettingsLoadState.RecoveredFromInvalidSettings, service.LoadState);
        Assert.True(UserSettings.Default.ContentEquals(service.CurrentSettings));
        Assert.Null(service.LastPersistedSettings);
        Assert.Equal(Damaged, files.Data[Path_]);
        Assert.Empty(files.Writes);
    }

    [Fact]
    public async Task ATransientReadErrorIsNotInvalidContent()
    {
        var files = new Files { };
        files.Data[Path_] = SettingsCodec.Serialize(UserSettings.Default);
        files.BeforeRead = _ => throw new IOException("locked");
        var store = new LocalSettingsStore(files, Path_);
        var load = await store.LoadDetailedAsync();
        Assert.Equal(SettingsLoadState.Unreadable, load.State);
        var service = new SettingsService(store, load);
        Assert.Null(service.LastPersistedSettings);
        Assert.NotEqual(SettingsLoadState.RecoveredFromInvalidSettings, service.LoadState);
    }

    [Fact]
    public async Task APatchPersistsAndTheServiceBecomesValidWithTheWrittenStateAsLastPersisted()
    {
        var (service, files, _) = Create();
        var patch = Patch(service.CurrentSettings, 4);
        var commit = await service.PersistAsync(patch);
        Assert.Equal(4, service.CurrentSettings.SignificantDigits);
        Assert.Equal(SettingsLoadState.Valid, service.LoadState);
        Assert.Equal(4, service.LastPersistedSettings!.SignificantDigits);
        Assert.Equal(4, SettingsCodec.Deserialize(files.Data[Path_]).SignificantDigits);
        commit.Notify();
    }

    [Fact]
    public async Task ChangedFiresOnlyWhenTheCommitIsNotified()
    {
        var (service, _, _) = Create();
        int changed = 0; service.Changed += () => changed++;
        var commit = await service.PersistAsync(Patch(service.CurrentSettings, 4));
        Assert.Equal(0, changed);
        commit.Notify();
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task CustomAndLastStandardProfileTravelWithThePatch()
    {
        var (service, _, _) = Create();
        var custom = UserSettings.Default.Profile.Kind == UnitProfileKind.Custom ? UserSettings.Default.Profile
            : UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch).Profile;
        var patch = SettingsPatch.From(service.CurrentSettings) with { Profile = custom, CustomProfile = custom, LastStandardProfile = UnitProfileKind.UnitedStates };
        await service.PersistAsync(patch);
        Assert.Same(custom, service.CurrentSettings.CustomProfile);
        Assert.Equal(UnitProfileKind.UnitedStates, service.CurrentSettings.LastStandardProfile);
    }

    [Fact]
    public async Task AStalePatchNeverResetsANewerAuthor()
    {
        var (service, files, _) = Create();
        var stale = SettingsPatch.From(service.CurrentSettings);   // taken before the author exists
        await service.UpdateLastAuthorAsync("Ada");
        await service.PersistAsync(stale with { SignificantDigits = 5 });
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
        Assert.Equal("Ada", SettingsCodec.Deserialize(files.Data[Path_]).LastAuthor);
        Assert.Equal(5, service.CurrentSettings.SignificantDigits);
    }

    [Fact]
    public async Task TheAuthorIsCurrentImmediatelyEvenWhileItsWriteIsStillRunning()
    {
        var (service, files, _) = Create();
        var release = new TaskCompletionSource();
        files.BeforeWrite = (_, _, _) => release.Task;
        var update = service.UpdateLastAuthorAsync("Ada");
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
        Assert.False(update.IsCompleted);
        release.SetResult(); await update;
        Assert.Equal("Ada", service.LastPersistedSettings!.LastAuthor);
    }

    [Fact]
    public async Task AFailedAuthorWriteKeepsTheAuthorInMemoryAndDoesNotThrow()
    {
        var (service, files, _) = Create();
        files.FailWritePath = Path_;
        await service.UpdateLastAuthorAsync("Ada");
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
        Assert.Null(service.LastPersistedSettings);
    }

    [Fact]
    public async Task TheAutomaticAuthorWriteNeverOverwritesAnInvalidFile()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        await service.UpdateLastAuthorAsync("Ada");
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
        Assert.Equal(Damaged, files.Data[Path_]);
        Assert.Empty(files.Writes);
        Assert.Equal(SettingsLoadState.RecoveredFromInvalidSettings, service.LoadState);
    }

    [Fact]
    public async Task TheAutomaticAuthorWriteNeverOverwritesAnUnreadableFile()
    {
        var files = new Files();
        files.Data[Path_] = SettingsCodec.Serialize(UserSettings.Default with { SignificantDigits = 1 });
        files.BeforeRead = _ => throw new IOException("locked");
        var store = new LocalSettingsStore(files, Path_);
        var service = new SettingsService(store, await store.LoadDetailedAsync());
        files.BeforeRead = null;
        await service.UpdateLastAuthorAsync("Ada");
        Assert.Empty(files.Writes);
        Assert.Equal(1, SettingsCodec.Deserialize(files.Data[Path_]).SignificantDigits);
    }

    [Fact]
    public async Task TheFirstExplicitSaveBacksUpTheInvalidFileAndReplacesIt()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        string backup = Path.Combine(Path.GetDirectoryName(Path_)!, "settings.invalid.json");
        await service.PersistAsync(Patch(service.CurrentSettings, 4));
        Assert.Equal(Damaged, files.Data[backup]);
        Assert.Equal(4, SettingsCodec.Deserialize(files.Data[Path_]).SignificantDigits);
        Assert.Equal(SettingsLoadState.Valid, service.LoadState);
        Assert.NotNull(service.LastPersistedSettings);
    }

    [Fact]
    public async Task AnExistingBackupIsNeverOverwrittenAndTheNextFreeNameIsUsed()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        string directory = Path.GetDirectoryName(Path_)!;
        string first = Path.Combine(directory, "settings.invalid.json"), second = Path.Combine(directory, "settings.invalid.1.json");
        var previous = Encoding.UTF8.GetBytes("older backup");
        files.Data[first] = previous;
        await service.PersistAsync(Patch(service.CurrentSettings, 4));
        Assert.Equal(previous, files.Data[first]);
        Assert.Equal(Damaged, files.Data[second]);
    }

    [Fact]
    public async Task AFailingBackupDoesNotBlockTheExplicitSave()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        files.FailWritePath = Path.Combine(Path.GetDirectoryName(Path_)!, "settings.invalid.json");
        await service.PersistAsync(Patch(service.CurrentSettings, 4));
        Assert.Equal(4, SettingsCodec.Deserialize(files.Data[Path_]).SignificantDigits);
    }

    [Fact]
    public async Task APersistFailureLeavesTheServiceStateAndTheFileUntouched()
    {
        var original = UserSettings.Default with { SignificantDigits = 5 };
        var (service, files, _) = Create(f => f.Data[Path_] = SettingsCodec.Serialize(original));
        files.FailWritePath = Path_;
        await Assert.ThrowsAsync<IOException>(() => service.PersistAsync(Patch(service.CurrentSettings, 2)));
        Assert.Equal(5, service.CurrentSettings.SignificantDigits);
        Assert.Equal(5, service.LastPersistedSettings!.SignificantDigits);
    }

    [Fact]
    public async Task TheOriginalInvalidFileStaysUntilTheExplicitSaveCommits()
    {
        var (service, files, _) = Create(f => f.Data[Path_] = Damaged);
        files.FailWritePath = Path_;
        await Assert.ThrowsAsync<IOException>(() => service.PersistAsync(Patch(service.CurrentSettings, 2)));
        Assert.Equal(Damaged, files.Data[Path_]);
        Assert.Equal(SettingsLoadState.RecoveredFromInvalidSettings, service.LoadState);
    }

    [Fact]
    public void RuntimePresentationChangesNeitherPersistedStateNorTheFile()
    {
        var (service, files, _) = Create();
        int changed = 0; service.Changed += () => changed++;
        service.ApplyRuntimePresentation(UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch).Profile, 4, EngineeringNotation.Off);
        Assert.Equal(1, changed);
        Assert.Empty(files.Writes);
        Assert.Null(service.LastPersistedSettings);
        service.ApplyRuntimePresentation(service.CurrentSettings.Profile, service.CurrentSettings.SignificantDigits, service.CurrentSettings.Notation);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task TheProcessCultureIsEstablishedOnceAndStaysConstantWhenTheLanguageSettingChanges()
    {
        using var scope = new ProcessCultureScope();
        var (service, _, _) = Create(f => f.Data[Path_] = SettingsCodec.Serialize(UserSettings.Default with { UiLanguage = UiLanguagePreference.English }));
        Assert.Null(service.ProcessCulture);
        var system = CultureInfo.GetCultureInfo("de-DE");
        var established = service.EstablishProcessCulture(system, system);
        Assert.Same(established, service.ProcessCulture);
        Assert.Equal("en", established.UiCulture.TwoLetterISOLanguageName);

        await service.PersistAsync(SettingsPatch.From(service.CurrentSettings) with { UiLanguage = UiLanguagePreference.German });
        Assert.Same(established, service.ProcessCulture);
        Assert.Equal("en", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Assert.Throws<InvalidOperationException>(() => service.EstablishProcessCulture(system, system));
    }

    [Fact]
    public async Task ASettingsServiceWithoutAStoreWorksInMemoryOnly()
    {
        var service = new SettingsService(null, SettingsLoadResult.Loaded(UserSettings.Default));
        await service.PersistAsync(Patch(service.CurrentSettings, 4));
        await service.UpdateLastAuthorAsync("Ada");
        Assert.Equal(4, service.CurrentSettings.SignificantDigits);
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
    }

    private sealed class ProcessCultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture, _ui = CultureInfo.CurrentUICulture,
            _defaultCulture = CultureInfo.DefaultThreadCurrentCulture!, _defaultUi = CultureInfo.DefaultThreadCurrentUICulture!;
        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture; CultureInfo.CurrentUICulture = _ui;
            CultureInfo.DefaultThreadCurrentCulture = _defaultCulture; CultureInfo.DefaultThreadCurrentUICulture = _defaultUi;
        }
    }
}
