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
    // What this catalog has published to the registry: identity -> candidate.
    private readonly Dictionary<ProjectIdentity, Guid> _published = [];

    internal RecoveryCatalog(IRecoveryStorage storage, IProjectFileStore files, IProjectIdentityResolver identities,
        ProjectPathRegistry registry)
    {
        _storage = storage;
        _files = files;
        _identities = identities;
        _registry = registry;
        registry.RecoverySuccessor = PreferredSuccessor;
    }

    public event Action? Changed;

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
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFormatException or System.Text.Json.JsonException)
        {
            return candidate with { ValidationError = e.Message };
        }
    }

    private ProjectIdentity? Resolve(string? path)
    {
        if (path is null) return null;
        try { return _identities.Resolve(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private async Task<List<RecoveryCandidate>> ResolveCompanionsAndRedundancyAsync(List<RecoveryCandidate> loaded)
    {
        // A valid project slot of the same id supersedes its Create draft: the project was created from it.
        var projectIds = loaded.Where(static c => c is { Kind: RecoverySlotKind.Project, IsDamaged: false, Slot.Id: not null })
            .Select(static c => c.Slot.Id!.Value).ToHashSet();
        for (int i = 0; i < loaded.Count; i++)
            if (loaded[i] is { Kind: RecoverySlotKind.Setup, Slot.Id: { } id } setup && projectIds.Contains(id))
                loaded[i] = setup with { State = RecoveryCandidateState.Superseded };
        // Identical to the original file (read, parsed and equal in full content): nothing to recover any more.
        for (int i = 0; i < loaded.Count; i++)
        {
            var candidate = loaded[i];
            if (candidate is not { Kind: RecoverySlotKind.Project, IsDamaged: false, Project: { OriginalFilePath: { } original } snapshot }) continue;
            if (!await IsRedundantAsync(snapshot, original)) continue;
            loaded[i] = candidate with { State = RecoveryCandidateState.Redundant };
            try { await _storage.DeleteAsync(candidate.Slot); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* stays hidden; the next start checks again */ }
            if (candidate.Slot.Id is { } id) await RemoveCompanionAsync(loaded, id, markRedundant: true);
        }
        return loaded;
    }

    private async Task<bool> IsRedundantAsync(RecoverySnapshot snapshot, string original)
    {
        try
        {
            var bytes = await _files.ReadAsync(original);
            return bytes is not null && snapshot.State.ContentEquals(ProjectFileCodec.Deserialize(bytes));
        }
        // A missing, unreadable or invalid original never makes the recovery redundant.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFormatException or System.Text.Json.JsonException)
        { return false; }
    }

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
        _candidates = all;
        _successors = preferredOrphaned;
        Publish(preferredHolders);
    }

    private void Publish(Dictionary<ProjectIdentity, Guid> wanted)
    {
        foreach (var (identity, current) in _published.ToList())
        {
            bool wantsOne = wanted.TryGetValue(identity, out var next);
            if (wantsOne && next == current) continue;
            // The path may meanwhile be owned by a restored window or already be gone; the update then simply does not apply.
            bool applied = _registry.UpdateRecovery(identity, current, wantsOne ? next : null);
            _published.Remove(identity);
            if (applied && wantsOne) _published[identity] = next;
        }
        foreach (var (identity, candidate) in wanted)
            if (!_published.ContainsKey(identity) && _registry.UpdateRecovery(identity, null, candidate))
                _published[identity] = candidate;
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
            if (candidate.IsLegacy)
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
            if (committed && candidate.Slot is { Kind: RecoverySlotKind.Project, Id: { } id })
                await RemoveCompanionAsync(all, id, markRedundant: false);
            Apply(all);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    /// <summary>Deletes the superseded draft of a project; best effort, a leftover stays hidden and is retried at the next start.</summary>
    private async Task RemoveCompanionAsync(List<RecoveryCandidate> all, RecoveryId id, bool markRedundant)
    {
        int index = all.FindIndex(c => c.State == RecoveryCandidateState.Superseded && c.Slot == RecoverySlot.Setup(id));
        if (index < 0) return;
        try
        {
            await _storage.DeleteAsync(all[index].Slot);
            if (markRedundant) all[index] = all[index] with { State = RecoveryCandidateState.Redundant };
            else all.RemoveAt(index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
