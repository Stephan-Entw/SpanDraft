using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public enum RecoveryCatalogState { NotLoaded, Loaded, Unavailable }

/// <summary>
/// The one process-wide list of recovery candidates. It reads and validates every slot once at startup, groups the
/// variants of one original project, publishes the preferred one as RecoveryPending in the path registry and mediates
/// restore (claim) and discard. It knows no windows and no UI.
/// </summary>
/// <remarks>
/// Every mutation runs under one gate (lock order: catalog gate, then registry lock). The registry never calls into the
/// catalog under its own lock; it only reads the immutable <see cref="PreferredSuccessor"/> snapshot, so a path whose
/// current owner goes away passes straight to its next undecided variant.
/// </remarks>
public sealed class RecoveryCatalog
{
    private readonly IRecoveryStorage _storage;
    private readonly IProjectFileStore _files;
    private readonly IProjectIdentityResolver _identities;
    private readonly ProjectPathRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Both replaced as a whole under the gate, read without it.
    private volatile IReadOnlyList<RecoveryCandidate> _candidates = [];
    private volatile IReadOnlyDictionary<ProjectIdentity, Guid> _successors = new Dictionary<ProjectIdentity, Guid>();

    internal RecoveryCatalog(IRecoveryStorage storage, IProjectFileStore files, IProjectIdentityResolver identities,
        ProjectPathRegistry registry)
    {
        _storage = storage;
        _files = files;
        _identities = identities;
        _registry = registry;
        registry.RecoverySuccessor = PreferredSuccessor;
        registry.RecoverySettler = ResolveLateAsync;
    }

    public event Action? Changed;

    /// <summary>How long the startup redundancy check waits for one original file; slower originals keep their recovery.</summary>
    internal TimeSpan RedundancyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public RecoveryCatalogState State { get; private set; }
    /// <summary>Why the recovery situation is unknown (State is Unavailable).</summary>
    public Exception? Error { get; private set; }

    /// <summary>The visible candidates: undecided, being claimed, and restored ones, newest first. Superseded ones are not listed.</summary>
    public IReadOnlyList<RecoveryCandidate> Candidates => _candidates.Where(static c => c.State is RecoveryCandidateState.Orphaned
        or RecoveryCandidateState.Claiming or RecoveryCandidateState.Claimed).ToArray();

    /// <summary>What the user still has to decide: valid and damaged candidates that are not restored or being restored.</summary>
    public IReadOnlyList<RecoveryCandidate> Undecided => _candidates.Where(static c => c.State == RecoveryCandidateState.Orphaned).ToArray();

    public bool HasUndecided => _candidates.Any(static c => c.State == RecoveryCandidateState.Orphaned);

    public RecoveryCandidate? Find(Guid candidateId) => _candidates.FirstOrDefault(c => c.CandidateId == candidateId);

    /// <summary>The registry's view: the preferred undecided candidate of a path, read from an immutable snapshot.</summary>
    internal Guid? PreferredSuccessor(ProjectIdentity identity) => _successors.TryGetValue(identity, out var id) ? id : null;

    // ---- load -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads all slots. A defect of one slot only makes that candidate damaged; if the recovery area as a whole cannot be
    /// listed the recovery situation is unknown and the catalog stays <see cref="RecoveryCatalogState.Unavailable"/>
    /// (fail-closed). Calling it again retries; once loaded it does nothing.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (State == RecoveryCatalogState.Loaded) return true;
            IReadOnlyList<RecoverySlot> slots;
            try { slots = await _storage.EnumerateAsync(cancellationToken); }
            catch (Exception e) when (RecoveryErrors.IsStorage(e))
            {
                Error = e;
                State = RecoveryCatalogState.Unavailable;
                return false;
            }
            var loaded = new List<RecoveryCandidate>();
            foreach (var slot in slots) loaded.Add(await ReadCandidateAsync(slot));
            loaded = await ResolveCompanionsAndRedundancyAsync(loaded);
            Error = null;
            State = RecoveryCatalogState.Loaded;
            Apply(loaded);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
        return true;
    }

    private async Task<RecoveryCandidate> ReadCandidateAsync(RecoverySlot slot)
    {
        var candidate = new RecoveryCandidate { CandidateId = Guid.NewGuid(), Slot = slot };
        try
        {
            var bytes = await _storage.ReadAsync(slot) ?? throw new FileNotFoundException("The recovery slot disappeared.", slot.FileName);
            if (slot.Kind == RecoverySlotKind.Project)
            {
                var snapshot = ProjectRecovery.Decode(bytes);
                return candidate with { Project = snapshot, Identity = Resolve(snapshot.OriginalFilePath) };
            }
            return candidate with { Setup = ProjectSetupRecovery.Decode(bytes) };
        }
        catch (Exception e) when (RecoveryErrors.IsCandidateDefect(e))
        {
            return candidate with { ValidationError = e.Message };
        }
    }

    private ProjectIdentity? Resolve(string? path)
    {
        if (path is null) return null;
        try { return _identities.Resolve(path); }
        catch (Exception e) when (RecoveryErrors.IsCandidateDefect(e)) { return null; }
    }

    private async Task<List<RecoveryCandidate>> ResolveCompanionsAndRedundancyAsync(List<RecoveryCandidate> loaded)
    {
        // A valid project slot of the same id supersedes its Create draft: the project was created from it.
        var projectIds = loaded.Where(static c => c is { Kind: RecoverySlotKind.Project, IsDamaged: false, Slot.Id: not null })
            .Select(static c => c.Slot.Id!.Value).ToHashSet();
        for (int i = 0; i < loaded.Count; i++)
            if (loaded[i] is { Kind: RecoverySlotKind.Setup, Slot.Id: { } id } setup && projectIds.Contains(id))
                loaded[i] = setup with { State = RecoveryCandidateState.Superseded };
        // Identical to the original file (read, parsed and equal in full content): nothing to recover any more. The originals
        // are checked concurrently and each read is bounded, so an unreachable volume delays the start by at most the timeout.
        var checks = new Dictionary<int, Task<bool>>();
        for (int i = 0; i < loaded.Count; i++)
            if (loaded[i] is { Kind: RecoverySlotKind.Project, IsDamaged: false, Project: { OriginalFilePath: { } original } snapshot })
                checks[i] = IsRedundantAsync(snapshot, original);
        await Task.WhenAll(checks.Values);
        foreach (var (i, check) in checks)
        {
            if (!await check) continue;
            var candidate = loaded[i];
            loaded[i] = candidate with { State = RecoveryCandidateState.Redundant };
            // The project slot keeps its draft superseded, so the draft goes first; if that fails the project slot stays
            // (hidden) as well and the next start checks again.
            if (candidate.Slot.Id is { } id && !await TryRemoveCompanionAsync(loaded, id, markRedundant: true)) continue;
            try { await _storage.DeleteAsync(candidate.Slot); }
            catch (Exception e) when (RecoveryErrors.IsStorage(e)) { /* stays hidden; the next start checks again */ }
        }
        return loaded;
    }

    private async Task<bool> IsRedundantAsync(RecoverySnapshot snapshot, string original)
    {
        try
        {
            var bytes = await _files.ReadAsync(original).WaitAsync(RedundancyTimeout);
            return bytes is not null && snapshot.State.ContentEquals(ProjectFileCodec.Deserialize(bytes));
        }
        // A missing, unreadable, unreachable (timed out) or invalid original never makes the recovery redundant.
        catch (Exception e) when (RecoveryErrors.IsCandidateDefect(e) || e is TimeoutException)
        { return false; }
    }

    /// <summary>
    /// A valid candidate whose original path could not be resolved at load stays undecided without a path claim. Every file
    /// operation settles the registry first, which retries the resolution here; once it succeeds the candidate takes part
    /// in grouping and holds its path like any other, before the caller arbitrates.
    /// </summary>
    private async Task ResolveLateAsync()
    {
        if (!_candidates.Any(IsUnresolved)) return;
        bool changed = false;
        await _gate.WaitAsync();
        try
        {
            var all = _candidates.ToList();
            for (int i = 0; i < all.Count; i++)
                if (IsUnresolved(all[i]) && Resolve(all[i].Project!.OriginalFilePath) is { } identity)
                {
                    all[i] = all[i] with { Identity = identity };
                    changed = true;
                }
            if (changed) Apply(all);
        }
        finally { _gate.Release(); }
        if (changed) Changed?.Invoke();
    }

    private static bool IsUnresolved(RecoveryCandidate c) => c is { Kind: RecoverySlotKind.Project, IsDamaged: false, State: RecoveryCandidateState.Orphaned,
        Identity: null, Project.OriginalFilePath: not null };

    // ---- grouping and registry publication --------------------------------------------------------------------

    /// <summary>Replaces the candidate list, marks the preferred ones and brings the registry up to date.</summary>
    private void Apply(List<RecoveryCandidate> all)
    {
        // The preferred variant of a path: newest first, identified slots before a legacy one on an exact tie, then id order.
        var preferredHolders = new Dictionary<ProjectIdentity, Guid>();
        var preferredOrphaned = new Dictionary<ProjectIdentity, Guid>();
        foreach (var group in all.Where(static c => c is { Kind: RecoverySlotKind.Project, IsDamaged: false, Identity: not null }
                     && c.State is RecoveryCandidateState.Orphaned or RecoveryCandidateState.Claiming).GroupBy(static c => c.Identity!))
        {
            var ordered = group.OrderByDescending(static c => c.WrittenAtUtc).ThenBy(static c => c.IsLegacy)
                .ThenBy(static c => c.Slot.Id).ToList();
            preferredHolders[group.Key] = ordered[0].CandidateId;
            if (ordered.FirstOrDefault(static c => c.State == RecoveryCandidateState.Orphaned) is { } orphaned)
                preferredOrphaned[group.Key] = orphaned.CandidateId;
        }
        for (int i = 0; i < all.Count; i++)
        {
            bool preferred = all[i].Identity is { } identity && preferredHolders.TryGetValue(identity, out var id) && id == all[i].CandidateId;
            if (all[i].IsPreferred != preferred) all[i] = all[i] with { IsPreferred = preferred };
        }
        var previous = _candidates;
        _candidates = all;
        _successors = preferredOrphaned;
        Publish(previous, all, preferredHolders);
    }

    /// <summary>
    /// Brings the registry's recovery entries in line with the new list. It reconciles against the registry (never against
    /// what an earlier call published), so an entry that came into being through a path hand-over is corrected too.
    /// </summary>
    private void Publish(IReadOnlyList<RecoveryCandidate> previous, List<RecoveryCandidate> next, Dictionary<ProjectIdentity, Guid> wanted)
    {
        var identities = previous.Concat(next).Select(static c => c.Identity).OfType<ProjectIdentity>().ToHashSet();
        foreach (var identity in identities) _registry.SetRecovery(identity, wanted.TryGetValue(identity, out var id) ? id : null);
    }

    // ---- claim ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the exclusive restore right of an undecided, valid candidate. A legacy slot is moved to an identified one
    /// first; if that fails the legacy slot is untouched, the candidate stays undecided and the error is thrown.
    /// Returns null when the candidate is not (any more) available for a restore.
    /// </summary>
    public async Task<RecoveryClaim?> TryBeginClaimAsync(Guid candidateId)
    {
        await _gate.WaitAsync();
        try
        {
            var all = _candidates.ToList();
            int index = all.FindIndex(c => c.CandidateId == candidateId);
            if (index < 0 || all[index] is not { State: RecoveryCandidateState.Orphaned, IsDamaged: false } candidate) return null;
            // The workspace's slot must be one nothing else in the catalog refers to. A project slot's draft companion is deleted
            // first (afterwards only the project slot keeps it superseded, and the workspace may delete that at any time); if
            // that fails the restore is refused and the candidate stays undecided.
            if (candidate.Slot is { Kind: RecoverySlotKind.Project, Id: { } projectId })
            {
                await RemoveCompanionAsync(all, projectId, markRedundant: false);
                _candidates = all.ToList();
                index = all.FindIndex(c => c.CandidateId == candidateId);
            }
            // A legacy slot has no id; a draft whose id is also the id of a (damaged) project slot would share its identity with
            // that candidate, which could later be discarded under the restored workspace. Both get a fresh id by an atomic move.
            bool sharesId = candidate.Slot is { Kind: RecoverySlotKind.Setup, Id: { } draftId }
                && all.Any(c => c.Slot.Kind == RecoverySlotKind.Project && c.Slot.Id == draftId);
            if (candidate.IsLegacy || sharesId)
            {
                var target = candidate.Slot.WithId(RecoveryId.New());
                await _storage.MoveAsync(candidate.Slot, target);
                candidate = candidate with { Slot = target };
            }
            all[index] = candidate with { State = RecoveryCandidateState.Claiming };
            Apply(all);
            candidate = all[index];
            bool holdsPath = candidate.Identity is { } identity && _registry.RecoveryCandidateOf(identity) == candidate.CandidateId;
            return new RecoveryClaim(this, candidate, holdsPath);
        }
        finally { _gate.Release(); }
    }

    internal async Task FinishClaimAsync(Guid candidateId, bool committed)
    {
        await _gate.WaitAsync();
        try
        {
            var all = _candidates.ToList();
            int index = all.FindIndex(c => c.CandidateId == candidateId && c.State == RecoveryCandidateState.Claiming);
            if (index < 0) return;
            var candidate = all[index];
            all[index] = candidate with { State = committed ? RecoveryCandidateState.Claimed : RecoveryCandidateState.Orphaned };
            Apply(all);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    /// <summary>
    /// Deletes the superseded draft of a project slot. False when it exists but cannot be deleted (it then stays in the
    /// list, hidden); true when there was none or it is gone.
    /// </summary>
    private async Task<bool> TryRemoveCompanionAsync(List<RecoveryCandidate> all, RecoveryId id, bool markRedundant)
    {
        try { await RemoveCompanionAsync(all, id, markRedundant); return true; }
        catch (Exception e) when (RecoveryErrors.IsStorage(e)) { return false; }
    }

    /// <summary>Deletes the superseded draft of a project slot, if there is one; a failed delete is thrown and leaves it listed.</summary>
    private async Task RemoveCompanionAsync(List<RecoveryCandidate> all, RecoveryId id, bool markRedundant)
    {
        int index = all.FindIndex(c => c.State == RecoveryCandidateState.Superseded && c.Slot == RecoverySlot.Setup(id));
        if (index < 0) return;
        await _storage.DeleteAsync(all[index].Slot);
        if (markRedundant) all[index] = all[index] with { State = RecoveryCandidateState.Redundant };
        else all.RemoveAt(index);
    }

    // ---- discard ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves an undecided candidate to the Discarded area (never deleting it) and lets the next older variant of its
    /// original take the path. A Create draft companion goes first; if either move fails the error is thrown and the
    /// candidate that is still relevant stays offered. Candidates that are being restored or already restored are not discarded.
    /// </summary>
    public async Task<bool> DiscardAsync(Guid candidateId)
    {
        await _gate.WaitAsync();
        try
        {
            var all = _candidates.ToList();
            int index = all.FindIndex(c => c.CandidateId == candidateId);
            if (index < 0 || all[index].State != RecoveryCandidateState.Orphaned) return false;
            var candidate = all[index];
            // The superseded draft is moved first: if the project's own move fails afterwards, the project stays active.
            if (candidate.Slot is { Kind: RecoverySlotKind.Project, Id: { } id })
            {
                int companion = all.FindIndex(c => c.State == RecoveryCandidateState.Superseded && c.Slot == RecoverySlot.Setup(id));
                if (companion >= 0)
                {
                    await _storage.DiscardAsync(all[companion].Slot);
                    all.RemoveAt(companion);
                    index = all.FindIndex(c => c.CandidateId == candidateId);
                    _candidates = all.ToList();
                }
            }
            await _storage.DiscardAsync(candidate.Slot);
            all.RemoveAt(index);
            Apply(all);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
        return true;
    }
}
