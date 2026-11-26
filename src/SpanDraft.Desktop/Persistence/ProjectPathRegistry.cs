using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public readonly record struct WindowId(Guid Value)
{
    public static WindowId Create() => new(Guid.NewGuid());
}

internal sealed class OpenReservation(ProjectIdentity identity)
{
    public Guid OperationId { get; } = Guid.NewGuid();
    public ProjectIdentity Identity { get; } = identity;
}

internal sealed class SaveTargetReservation(WindowId windowId, ProjectIdentity identity, ProjectIdentity? previousIdentity)
{
    public Guid OperationId { get; } = Guid.NewGuid();
    public WindowId WindowId { get; } = windowId;
    public ProjectIdentity Identity { get; } = identity;
    internal ProjectIdentity? PreviousIdentity { get; } = previousIdentity;
}

internal enum ProjectPathState { Free, RecoveryPending, Opening, Open, SaveTargetReserved }
internal enum SaveTargetDecision { Reserved, SaveCurrent, Rejected }

internal sealed record OpenDecision(ProjectPathState State, OpenReservation? Reservation = null,
    Task<ProjectOpenResult>? Flight = null, WindowId? Owner = null, Guid? RecoveryCandidate = null, Task? SaveCompletion = null,
    Task? OwnerRelease = null);

/// <summary>
/// Held by a workspace close transaction while its window may give up whatever it owns. It belongs to the transaction, not
/// to an identity: every path the window owns meanwhile (also one taken over by a Save As) is protected by it, and only
/// <see cref="ProjectPathRegistry.EndOwnerRelease"/> ends it.
/// </summary>
internal sealed class OwnerReleaseToken(WindowId window)
{
    public WindowId Window { get; } = window;
    internal TaskCompletionSource Signal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Ended { get; set; }
}

/// <summary>The process-wide arbiter for project paths. Its lock protects only in-memory transitions.</summary>
internal sealed class ProjectPathRegistry
{
    private abstract record Entry(ProjectPathState State);
    private sealed record Recovery(Guid Candidate) : Entry(ProjectPathState.RecoveryPending);
    private sealed record Opening(OpenReservation Token) : Entry(ProjectPathState.Opening)
    {
        public TaskCompletionSource<ProjectOpenResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record Open(WindowId Window) : Entry(ProjectPathState.Open);
    private sealed record Saving(SaveTargetReservation Token) : Entry(ProjectPathState.SaveTargetReserved)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private Func<ProjectIdentity, Guid?>? _recoverySuccessor;
    private readonly Dictionary<ProjectIdentity, Entry> _paths = [];
    private readonly Dictionary<WindowId, ProjectIdentity> _owned = [];
    private readonly Dictionary<WindowId, Saving> _saves = [];
    private readonly Dictionary<WindowId, OwnerReleaseToken> _releases = [];

    /// <summary>
    /// Names the recovery candidate that takes over a path as soon as its current entry (Opening, Open, SaveTargetReserved)
    /// disappears, so that the path goes straight to RecoveryPending without a Free window in between. It is called under
    /// the registry lock and must therefore only read an immutable snapshot and never call back into the registry.
    /// </summary>
    internal Func<ProjectIdentity, Guid?>? RecoverySuccessor
    {
        set { lock (_gate) _recoverySuccessor = value; }
    }

    private Func<Task>? _recoverySettler;

    /// <summary>
    /// Lets the recovery catalog finish what it could not decide earlier (an original path that could not be resolved at
    /// load) before a caller arbitrates a path. Async because the catalog's own serialization is async.
    /// </summary>
    internal Func<Task>? RecoverySettler
    {
        set { lock (_gate) _recoverySettler = value; }
    }

    /// <summary>Call immediately before resolving and arbitrating a path; completes at once when nothing is pending.</summary>
    internal Task SettleRecoveryAsync()
    {
        Func<Task>? settler;
        lock (_gate) settler = _recoverySettler;
        return settler?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>Removes the entry of a path that is no longer held, or hands it to its successor recovery candidate.</summary>
    private void Vacate(ProjectIdentity identity)
    {
        if (_recoverySuccessor?.Invoke(identity) is { } candidate) _paths[identity] = new Recovery(candidate);
        else _paths.Remove(identity);
    }

    internal ProjectPathState StateOf(ProjectIdentity identity)
    {
        lock (_gate) return _paths.GetValueOrDefault(identity)?.State ?? ProjectPathState.Free;
    }

    /// <summary>The recovery candidate currently holding the path (RecoveryPending), or null.</summary>
    internal Guid? RecoveryCandidateOf(ProjectIdentity identity)
    {
        lock (_gate) return _paths.GetValueOrDefault(identity) is Recovery recovery ? recovery.Candidate : null;
    }

    internal ProjectIdentity? IdentityOwnedBy(WindowId window)
    {
        lock (_gate) return _owned.GetValueOrDefault(window);
    }

    internal OpenDecision ArbitrateOpen(ProjectIdentity identity)
    {
        lock (_gate)
        {
            switch (_paths.GetValueOrDefault(identity))
            {
                case Recovery recovery: return new(ProjectPathState.RecoveryPending, RecoveryCandidate: recovery.Candidate);
                case Opening opening: return new(ProjectPathState.Opening, Flight: opening.Completion.Task);
                // The owner may be about to give the path up: wait for its decision instead of reporting it as open.
                case Open open when _releases.TryGetValue(open.Window, out var release):
                    return new(ProjectPathState.Open, Owner: open.Window, OwnerRelease: release.Signal.Task);
                case Open open: return new(ProjectPathState.Open, Owner: open.Window);
                case Saving saving: return new(ProjectPathState.SaveTargetReserved, SaveCompletion: saving.Completion.Task);
                default:
                    var token = new OpenReservation(identity);
                    _paths.Add(identity, new Opening(token));
                    return new(ProjectPathState.Free, Reservation: token);
            }
        }
    }

    internal bool TryCommitOpen(OpenReservation token, WindowId window)
    {
        lock (_gate)
        {
            if (_paths.GetValueOrDefault(token.Identity) is not Opening opening || !ReferenceEquals(opening.Token, token)
                || _saves.ContainsKey(window)) return false;
            SetOwner(token.Identity, window);
            opening.Completion.SetResult(new(ProjectOpenStatus.Opened, Owner: window));
            return true;
        }
    }

    internal bool TryReleaseOpen(OpenReservation token, ProjectOpenResult outcome)
    {
        lock (_gate)
        {
            if (_paths.GetValueOrDefault(token.Identity) is not Opening opening || !ReferenceEquals(opening.Token, token)) return false;
            Vacate(token.Identity);
            opening.Completion.SetResult(outcome);
            return true;
        }
    }

    internal SaveTargetDecision ArbitrateSave(ProjectIdentity identity, WindowId window, out SaveTargetReservation? reservation)
    {
        lock (_gate)
        {
            reservation = null;
            if (_saves.ContainsKey(window)) return SaveTargetDecision.Rejected;
            var entry = _paths.GetValueOrDefault(identity);
            if (entry is Open open && open.Window == window) return SaveTargetDecision.SaveCurrent;
            if (entry is not null) return SaveTargetDecision.Rejected;

            reservation = new(window, identity, _owned.GetValueOrDefault(window));
            var saving = new Saving(reservation);
            _paths.Add(identity, saving);
            _saves.Add(window, saving);
            return SaveTargetDecision.Reserved;
        }
    }

    internal bool TryCommitSave(SaveTargetReservation token, ProjectSession.PreparedSave save)
    {
        lock (_gate)
        {
            if (!OwnsSave(token, out var saving)) return false;
            // MarkSaved has no observers here. The source stays pinned until the target is owned.
            save.MarkSaved();
            _paths[token.Identity] = new Open(token.WindowId);
            _owned[token.WindowId] = token.Identity;
            if (token.PreviousIdentity is { } previous) Vacate(previous);
            _saves.Remove(token.WindowId);
            saving.Completion.SetResult();
            Pulse(token.WindowId);
            return true;
        }
    }

    internal bool TryReleaseSave(SaveTargetReservation token)
    {
        lock (_gate)
        {
            if (!OwnsSave(token, out var saving)) return false;
            Vacate(token.Identity);
            _saves.Remove(token.WindowId);
            saving.Completion.SetResult();
            return true;
        }
    }

    private bool OwnsSave(SaveTargetReservation token, out Saving saving)
    {
        saving = _saves.GetValueOrDefault(token.WindowId)!;
        return saving is not null && ReferenceEquals(saving.Token, token);
    }

    internal bool ReleaseOwner(WindowId window)
    {
        lock (_gate)
        {
            if (_saves.ContainsKey(window) || !_owned.Remove(window, out var identity)) return false;
            Vacate(identity);
            Pulse(window);
            return true;
        }
    }

    internal bool UpdateRecovery(ProjectIdentity identity, Guid? expected, Guid? preferred)
    {
        lock (_gate)
        {
            var entry = _paths.GetValueOrDefault(identity);
            if (expected is { } old ? entry is not Recovery recovery || recovery.Candidate != old : entry is not null)
                return false;
            if (preferred is { } candidate) _paths[identity] = new Recovery(candidate);
            else _paths.Remove(identity);
            return true;
        }
    }

    /// <summary>
    /// Makes the path's recovery entry match <paramref name="preferred"/> (null: none): it is created, replaced or removed
    /// only where the path is free or already RecoveryPending. A path held by a window or an operation keeps its entry; the
    /// successor provider hands it over when that entry goes away. Idempotent, so the catalog can reconcile at any time.
    /// </summary>
    internal void SetRecovery(ProjectIdentity identity, Guid? preferred)
    {
        lock (_gate)
        {
            var entry = _paths.GetValueOrDefault(identity);
            if (entry is not null and not Recovery) return;
            if (preferred is { } candidate) _paths[identity] = new Recovery(candidate);
            else _paths.Remove(identity);
        }
    }

    internal bool TryCommitRecovery(ProjectIdentity identity, Guid candidate, WindowId window)
    {
        lock (_gate)
        {
            if (_paths.GetValueOrDefault(identity) is not Recovery recovery || recovery.Candidate != candidate
                || _saves.ContainsKey(window)) return false;
            SetOwner(identity, window);
            return true;
        }
    }

    private void SetOwner(ProjectIdentity identity, WindowId window)
    {
        _paths[identity] = new Open(window);
        if (_owned.TryGetValue(window, out var previous) && !previous.Equals(identity)) Vacate(previous);
        _owned[window] = identity;
        Pulse(window);
    }

    // ---- owner release (workspace close) ------------------------------------------------------------------------

    /// <summary>
    /// Marks that <paramref name="window"/> is deciding whether to give up what it owns (its close transaction is running).
    /// Opens of a path it owns meanwhile wait for the outcome and are then arbitrated anew, instead of being told the path
    /// is open in a workspace that may never take a request again. Null if a release of this window is already running.
    /// </summary>
    internal OwnerReleaseToken? BeginOwnerRelease(WindowId window)
    {
        lock (_gate)
        {
            if (_releases.ContainsKey(window)) return null;
            var token = new OwnerReleaseToken(window);
            _releases.Add(window, token);
            return token;
        }
    }

    /// <summary>Ends the release of its transaction (committed or rolled back); waiting opens arbitrate anew. Idempotent.</summary>
    internal void EndOwnerRelease(OwnerReleaseToken token)
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (token.Ended || !_releases.TryGetValue(token.Window, out var current) || !ReferenceEquals(current, token)) return;
            token.Ended = true;
            _releases.Remove(token.Window);
            signal = token.Signal;
        }
        signal.TrySetResult();
    }

    /// <summary>
    /// The window's ownership changed during its release (a Save As or the final release): waiters arbitrate anew, so a path
    /// it no longer holds is served at once, while a path it now holds stays protected by the same, still running release.
    /// </summary>
    private void Pulse(WindowId window)
    {
        if (!_releases.TryGetValue(window, out var token)) return;
        var signal = token.Signal;
        token.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Continuations run asynchronously, so nothing re-enters the registry under its own lock.
        signal.TrySetResult();
    }
}
