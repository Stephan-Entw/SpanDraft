using System.Globalization;
using System.Text.Json;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Services;

/// <summary>The culture and UI culture applied to the process at startup; they never change afterwards.</summary>
public sealed record ProcessCulture(CultureInfo Culture, CultureInfo UiCulture);

/// <summary>
/// The state of one explicit settings write: the service already holds it, but subscribers hear about it only after
/// <see cref="Notify"/>, so the coordinator can update every participant first.
/// </summary>
public readonly struct SettingsCommit
{
    private readonly SettingsService? _owner;
    internal SettingsCommit(SettingsService owner) => _owner = owner;
    public void Notify() => _owner?.RaiseChanged();
}

/// <summary>
/// The one authoritative runtime state of the user settings. It knows no windows; the store is only its persistence.
/// </summary>
public sealed class SettingsService
{
    private readonly LocalSettingsStore? _store;
    private readonly Func<bool>? _isApplicationShuttingDown;
    private readonly object _sync = new();
    // Serializes every write, so an older snapshot can never replace a newer decision.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserSettings _current;
    private UserSettings? _lastPersisted;
    private SettingsLoadState _state;
    private byte[]? _invalidContent;
    private ProcessCulture? _processCulture;

    /// <param name="store">Null for a composition without persistence; settings then live in memory only.</param>
    public SettingsService(LocalSettingsStore? store, SettingsLoadResult load) : this(store, load, null) { }

    internal SettingsService(LocalSettingsStore? store, SettingsLoadResult load, Func<bool>? isApplicationShuttingDown)
    {
        ArgumentNullException.ThrowIfNull(load);
        _store = store;
        _isApplicationShuttingDown = isApplicationShuttingDown;
        _current = load.Settings;
        _state = load.State;
        _invalidContent = load.InvalidContent;
        // Only a state known to have been read from a valid file counts as persisted.
        _lastPersisted = load.State == SettingsLoadState.Valid ? load.Settings : null;
    }

    public UserSettings CurrentSettings { get { lock (_sync) return _current; } }
    internal bool IsApplicationShuttingDown => _isApplicationShuttingDown?.Invoke() == true;
    /// <summary>The last state known to be validly persisted; null while the file is missing, invalid or unreadable.</summary>
    public UserSettings? LastPersistedSettings { get { lock (_sync) return _lastPersisted; } }
    public SettingsLoadState LoadState { get { lock (_sync) return _state; } }
    /// <summary>Null until the bootstrap established it; constant for the rest of the process.</summary>
    public ProcessCulture? ProcessCulture { get { lock (_sync) return _processCulture; } }

    /// <summary>Raised after settings changed in memory (an author update, or a committed write after the participants applied it).</summary>
    public event Action? Changed;

    /// <summary>Resolves the cultures from the current settings, applies them to the process and freezes them. Startup only.</summary>
    public ProcessCulture EstablishProcessCulture(CultureInfo systemCulture, CultureInfo systemUiCulture)
    {
        lock (_sync)
        {
            if (_processCulture is not null) throw new InvalidOperationException("The process culture is already established.");
            var resolved = CultureConfigurator.Resolve(_current, systemCulture, systemUiCulture);
            CultureConfigurator.Apply(resolved);
            return _processCulture = new(resolved.Culture, resolved.UiCulture);
        }
    }

    /// <summary>
    /// The explicit user write: the patch is merged into the then-current settings, an invalid original file is backed up
    /// first (best effort), the result is written atomically and only then published. A failure leaves everything unchanged.
    /// </summary>
    public async Task<SettingsCommit> PersistAsync(SettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await _gate.WaitAsync();
        try
        {
            if (IsApplicationShuttingDown) throw new OperationCanceledException();
            UserSettings candidate; byte[]? invalid;
            lock (_sync) { candidate = patch.ApplyTo(_current); invalid = _state == SettingsLoadState.RecoveredFromInvalidSettings ? _invalidContent : null; }
            if (_store is not null)
            {
                if (invalid is not null) await _store.BackupInvalidAsync(invalid, canStartWrite: () => !IsApplicationShuttingDown);
                if (IsApplicationShuttingDown) throw new OperationCanceledException();
                await _store.SaveAsync(candidate);
            }
            lock (_sync)
            {
                // A concurrent author update stays: only the patch fields are replaced in the latest current state.
                _current = patch.ApplyTo(_current);
                if (_store is not null) { _lastPersisted = candidate; _state = SettingsLoadState.Valid; _invalidContent = null; }
            }
            return new SettingsCommit(this);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// A project was created: the author is current immediately. Persisting it is best effort and never replaces a
    /// damaged or unreadable file, because only an explicit user decision may do that.
    /// </summary>
    public async Task UpdateLastAuthorAsync(string? author)
    {
        if (IsApplicationShuttingDown) return;
        lock (_sync) _current = _current with { LastAuthor = author };
        RaiseChanged();
        if (_store is null) return;
        await _gate.WaitAsync();
        try
        {
            if (IsApplicationShuttingDown) return;
            UserSettings snapshot; SettingsLoadState state;
            lock (_sync) { snapshot = _current; state = _state; }
            if (state is not (SettingsLoadState.Valid or SettingsLoadState.Missing)) return;
            try
            {
                await _store.SaveAsync(snapshot);
                lock (_sync) { _lastPersisted = snapshot; _state = SettingsLoadState.Valid; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException
                or NotSupportedException or System.Security.SecurityException)
            { /* Remembering the author is best effort. */ }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Changes the presentation in memory only; nothing is persisted and the persisted state stays as it was.</summary>
    public void ApplyRuntimePresentation(UnitProfile profile, int significantDigits, EngineeringNotation notation)
    {
        if (IsApplicationShuttingDown) return;
        lock (_sync)
        {
            var updated = _current with
            {
                Profile = profile, SignificantDigits = significantDigits, Notation = notation,
                CustomProfile = profile.Kind == UnitProfileKind.Custom ? profile : _current.CustomProfile,
                LastStandardProfile = profile.Kind == UnitProfileKind.Custom ? _current.LastStandardProfile : profile.Kind
            };
            // An identical presentation is no change at all.
            if (updated.ContentEquals(_current)) return;
            _current = updated;
        }
        RaiseChanged();
    }

    internal void RaiseChanged()
    {
        if (!IsApplicationShuttingDown) Changed?.Invoke();
    }
}
