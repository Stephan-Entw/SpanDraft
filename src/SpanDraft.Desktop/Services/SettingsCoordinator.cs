using System.Text.Json;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Services;

public enum SettingsCheck { Ok, Busy, DraftBlocked }

/// <summary>
/// Whatever holds runtime state derived from the settings (today one workspace, later every workspace).
/// All members run on the UI thread; <see cref="ApplySettings"/> is synchronous, performs no I/O and must not fail.
/// </summary>
public interface ISettingsParticipant
{
    /// <summary>Starts the short settings commit: no new operation may start until released. False when it is busy.</summary>
    bool TryFreezeForSettings();
    /// <summary>Checks the candidate against the participant's current (possibly changed) state.</summary>
    SettingsCheck ValidateSettings(UserSettings candidate);
    void ApplySettings(UserSettings committed);
    void ReleaseSettingsFreeze();
}

public enum SettingsApplyOutcome { Applied, Busy, DraftBlocked, PersistFailed }

public sealed record SettingsApplyResult(SettingsApplyOutcome Outcome, Exception? Failure = null)
{
    public bool Succeeded => Outcome == SettingsApplyOutcome.Applied;
}

/// <summary>
/// The settings transaction across participants. An open settings dialog locks nothing; only this short commit is coordinated.
/// </summary>
public sealed class SettingsCoordinator(SettingsService settings)
{
    private readonly List<ISettingsParticipant> _participants = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingRegistrations;
    private TaskCompletionSource? _applyCompletion;

    public SettingsService Settings => settings;
    internal Task? ActiveApply { get { lock (_participants) return _applyCompletion?.Task; } }
    internal event Action? ApplyCompleted;

    internal RegistrationReservation? TryReserveRegistration()
    {
        lock (_participants)
        {
            if (_applyCompletion is not null || settings.IsApplicationShuttingDown) return null;
            _pendingRegistrations++;
            return new(this);
        }
    }

    public IDisposable Register(ISettingsParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        lock (_participants)
        {
            if (_applyCompletion is not null || settings.IsApplicationShuttingDown)
                throw new InvalidOperationException("Settings participant registration is unavailable.");
            return AddParticipant(participant);
        }
    }

    private IDisposable AddParticipant(ISettingsParticipant participant)
    {
        _participants.Add(participant);
        return new Registration(this, participant);
    }

    public async Task<SettingsApplyResult> ApplyAsync(SettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await _gate.WaitAsync();
        var frozen = new List<ISettingsParticipant>();
        TaskCompletionSource? completion = null;
        try
        {
            ISettingsParticipant[] participants;
            lock (_participants)
            {
                if (_pendingRegistrations != 0 || settings.IsApplicationShuttingDown) return new(SettingsApplyOutcome.Busy);
                _applyCompletion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                participants = [.. _participants];
            }
            foreach (var participant in participants)
            {
                if (!participant.TryFreezeForSettings()) return new(SettingsApplyOutcome.Busy);
                frozen.Add(participant);
            }
            // Validated against the state of every participant right before persisting, not against what the dialog saw.
            var candidate = patch.ApplyTo(settings.CurrentSettings);
            foreach (var participant in participants)
            {
                switch (participant.ValidateSettings(candidate))
                {
                    case SettingsCheck.Busy: return new(SettingsApplyOutcome.Busy);
                    case SettingsCheck.DraftBlocked: return new(SettingsApplyOutcome.DraftBlocked);
                }
            }
            SettingsCommit commit;
            try { commit = await settings.PersistAsync(patch); }
            catch (OperationCanceledException) when (settings.IsApplicationShuttingDown) { return new(SettingsApplyOutcome.Busy); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException
                or NotSupportedException or System.Security.SecurityException)
            { return new(SettingsApplyOutcome.PersistFailed, e); }
            // No await from here on: the service state, every participant and the notification change as one step.
            if (settings.IsApplicationShuttingDown) return new(SettingsApplyOutcome.Applied);
            var committed = settings.CurrentSettings;
            foreach (var participant in participants) participant.ApplySettings(committed);
            commit.Notify();
            return new(SettingsApplyOutcome.Applied);
        }
        finally
        {
            try { foreach (var participant in frozen) participant.ReleaseSettingsFreeze(); }
            finally
            {
                if (completion is not null)
                    lock (_participants) _applyCompletion = null;
                _gate.Release();
                if (completion is not null)
                {
                    completion.SetResult();
                    if (!settings.IsApplicationShuttingDown) ApplyCompleted?.Invoke();
                }
            }
        }
    }

    internal sealed class RegistrationReservation(SettingsCoordinator owner) : IDisposable
    {
        private bool _pending = true;

        internal IDisposable Register(ISettingsParticipant participant)
        {
            lock (owner._participants)
            {
                if (!_pending || owner.Settings.IsApplicationShuttingDown)
                    throw new InvalidOperationException("The settings registration reservation is unavailable.");
                var registration = owner.AddParticipant(participant);
                Release();
                return registration;
            }
        }

        public void Dispose() { lock (owner._participants) Release(); }

        private void Release()
        {
            if (!_pending) return;
            _pending = false;
            owner._pendingRegistrations--;
        }
    }

    private sealed class Registration(SettingsCoordinator owner, ISettingsParticipant participant) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (owner._participants) owner._participants.Remove(participant);
        }
    }
}
