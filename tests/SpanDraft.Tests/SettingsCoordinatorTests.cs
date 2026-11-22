using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class SettingsCoordinatorTests
{
    private static readonly string SettingsPath = TestPath("settings.json");

    private sealed class Participant(List<string> log, string name = "p") : ISettingsParticipant
    {
        public bool CanFreeze { get; set; } = true;
        public SettingsCheck Check { get; set; } = SettingsCheck.Ok;
        public bool Frozen { get; private set; }
        public int Applied { get; private set; }
        public UserSettings? LastApplied { get; private set; }
        public Action? OnApply { get; set; }
        public bool TryFreezeForSettings() { if (!CanFreeze || Frozen) return false; Frozen = true; log.Add(name + ":freeze"); return true; }
        public SettingsCheck ValidateSettings(UserSettings candidate) { log.Add(name + ":validate"); return Check; }
        public void ApplySettings(UserSettings committed) { Applied++; LastApplied = committed; log.Add(name + ":apply"); OnApply?.Invoke(); }
        public void ReleaseSettingsFreeze() { Frozen = false; log.Add(name + ":release"); }
    }

    private static (SettingsCoordinator Coordinator, SettingsService Service, Files Files) Create()
    {
        var files = new Files();
        var store = new LocalSettingsStore(files, SettingsPath);
        var service = new SettingsService(store, store.LoadDetailedAsync().GetAwaiter().GetResult());
        return (new SettingsCoordinator(service), service, files);
    }

    private static SettingsPatch Patch(SettingsService s, int digits) => SettingsPatch.From(s.CurrentSettings) with { SignificantDigits = digits };

    [Fact]
    public async Task EveryParticipantIsFrozenValidatedAppliedAndReleasedInOrder()
    {
        var (coordinator, service, _) = Create(); var log = new List<string>();
        var a = new Participant(log, "a"); var b = new Participant(log, "b");
        coordinator.Register(a); coordinator.Register(b);
        var result = await coordinator.ApplyAsync(Patch(service, 4));
        Assert.Equal(SettingsApplyOutcome.Applied, result.Outcome);
        Assert.Equal(["a:freeze", "b:freeze", "a:validate", "b:validate", "a:apply", "b:apply", "a:release", "b:release"], log);
        Assert.Equal(4, a.LastApplied!.SignificantDigits);
        Assert.False(a.Frozen); Assert.False(b.Frozen);
    }

    [Fact]
    public async Task ABusyParticipantMakesTheApplyBusyAndReleasesEveryFreezeWithoutPersisting()
    {
        var (coordinator, service, files) = Create(); var log = new List<string>();
        var a = new Participant(log, "a"); var b = new Participant(log, "b") { CanFreeze = false };
        coordinator.Register(a); coordinator.Register(b);
        var result = await coordinator.ApplyAsync(Patch(service, 4));
        Assert.Equal(SettingsApplyOutcome.Busy, result.Outcome);
        Assert.False(a.Frozen); Assert.Equal(0, a.Applied); Assert.Empty(files.Writes);
        Assert.Equal(UserSettings.Default.SignificantDigits, service.CurrentSettings.SignificantDigits);
    }

    [Fact]
    public async Task AParticipantWhoseStateChangedBlocksTheCommitAtTheLastCheck()
    {
        var (coordinator, service, files) = Create(); var log = new List<string>();
        var blocked = new Participant(log, "a") { Check = SettingsCheck.DraftBlocked }; var other = new Participant(log, "b");
        coordinator.Register(blocked); coordinator.Register(other);
        var result = await coordinator.ApplyAsync(Patch(service, 4));
        Assert.Equal(SettingsApplyOutcome.DraftBlocked, result.Outcome);
        Assert.Empty(files.Writes); Assert.Equal(0, other.Applied);
        Assert.False(blocked.Frozen); Assert.False(other.Frozen);
        Assert.Contains("a:validate", log);   // the participants are asked directly before persisting
    }

    [Fact]
    public async Task ABusyValidationBlocksTheCommit()
    {
        var (coordinator, service, files) = Create();
        coordinator.Register(new Participant([], "a") { Check = SettingsCheck.Busy });
        Assert.Equal(SettingsApplyOutcome.Busy, (await coordinator.ApplyAsync(Patch(service, 4))).Outcome);
        Assert.Empty(files.Writes);
    }

    [Fact]
    public async Task APersistFailureNeitherAppliesNorNotifiesAndReleasesTheFreeze()
    {
        var (coordinator, service, files) = Create(); var log = new List<string>();
        var participant = new Participant(log); coordinator.Register(participant);
        int changed = 0; service.Changed += () => changed++;
        files.FailWritePath = SettingsPath;
        var result = await coordinator.ApplyAsync(Patch(service, 4));
        Assert.Equal(SettingsApplyOutcome.PersistFailed, result.Outcome);
        Assert.IsType<IOException>(result.Failure);
        Assert.Equal(0, participant.Applied); Assert.Equal(0, changed); Assert.False(participant.Frozen);
        Assert.Equal(UserSettings.Default.SignificantDigits, service.CurrentSettings.SignificantDigits);
        Assert.Null(service.LastPersistedSettings);
    }

    [Fact]
    public async Task TheParticipantsHaveAppliedWhenTheServiceAnnouncesTheChange()
    {
        var (coordinator, service, _) = Create();
        var participant = new Participant([]); coordinator.Register(participant);
        var seenAtChange = new List<(int Applied, int Digits)>();
        service.Changed += () => seenAtChange.Add((participant.Applied, service.CurrentSettings.SignificantDigits));
        await coordinator.ApplyAsync(Patch(service, 4));
        Assert.Equal([(1, 4)], seenAtChange);
    }

    [Fact]
    public async Task ADisposedRegistrationReceivesNothingMore()
    {
        var (coordinator, service, _) = Create();
        var participant = new Participant([]); var registration = coordinator.Register(participant);
        registration.Dispose();
        Assert.Equal(SettingsApplyOutcome.Applied, (await coordinator.ApplyAsync(Patch(service, 4))).Outcome);
        Assert.Equal(0, participant.Applied);
    }

    [Fact]
    public async Task ConcurrentAppliesAreSerializedAndTheLastOneWins()
    {
        var (coordinator, service, files) = Create();
        var release = new TaskCompletionSource();
        files.BeforeWrite = (_, _, _) => release.Task;
        var first = coordinator.ApplyAsync(Patch(service, 4));
        var second = coordinator.ApplyAsync(Patch(service, 5));
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.True((await first).Succeeded); Assert.True((await second).Succeeded);
        Assert.Equal(5, service.CurrentSettings.SignificantDigits);
        Assert.Equal(5, SettingsCodec.Deserialize(files.Data[SettingsPath]).SignificantDigits);
    }

    [Fact]
    public async Task ThePatchIsMergedIntoTheLatestSettingsSoANewerAuthorSurvives()
    {
        var (coordinator, service, files) = Create();
        coordinator.Register(new Participant([]));
        var patch = Patch(service, 4);
        await service.UpdateLastAuthorAsync("Ada");
        await coordinator.ApplyAsync(patch);
        Assert.Equal("Ada", service.CurrentSettings.LastAuthor);
        Assert.Equal("Ada", SettingsCodec.Deserialize(files.Data[SettingsPath]).LastAuthor);
    }
}
