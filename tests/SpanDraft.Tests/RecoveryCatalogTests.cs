using System.Text;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class RecoveryCatalogTests
{
    private sealed class Fixture
    {
        public TestRecoveryStorage Storage { get; } = new();
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ProjectPathRegistry Registry { get; } = new();
        public RecoveryCatalog Catalog { get; }
        public IProjectIdentityResolver Resolver { get; }
        public Fixture(IProjectIdentityResolver? resolver = null) { Resolver = resolver ?? Identities; Catalog = new(Storage, Files, Resolver, Registry); }

        public RecoverySlot Project(string? original, int minute = 0, ProjectState? state = null, RecoveryId? id = null, bool legacy = false)
        {
            var slot = legacy ? RecoverySlot.LegacyProject : RecoverySlot.Project(id ?? RecoveryId.New());
            Storage.Put(slot, ProjectRecovery.Encode(new(state ?? State(2), original is null ? null : TestPath(original), Now.AddMinutes(minute))));
            return slot;
        }
        public RecoverySlot Setup(int minute = 0, RecoveryId? id = null, bool legacy = false)
        {
            var slot = legacy ? RecoverySlot.LegacySetup : RecoverySlot.Setup(id ?? RecoveryId.New());
            Storage.Put(slot, ProjectSetupRecovery.Encode(new(new(Length: M(1.5)), Now.AddMinutes(minute))));
            return slot;
        }
        public RecoverySlot Damaged(RecoverySlotKind kind = RecoverySlotKind.Project)
        {
            var slot = new RecoverySlot(kind, RecoveryId.New());
            Storage.Put(slot, Encoding.UTF8.GetBytes("{"));
            return slot;
        }
        public async Task<RecoveryCatalog> Load() { Assert.True(await Catalog.InitializeAsync()); return Catalog; }
        public RecoveryCandidate Of(RecoverySlot slot) => Catalog.Candidates.Single(c => c.Slot == slot);
        public ProjectIdentity Identity(string name) => Identities.Resolve(TestPath(name));
    }

    // ---- loading ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyAreaLoadsWithoutCandidates()
    {
        var f = new Fixture(); await f.Load();
        Assert.Equal(RecoveryCatalogState.Loaded, f.Catalog.State); Assert.False(f.Catalog.HasUndecided); Assert.Empty(f.Catalog.Candidates);
    }

    [Fact]
    public async Task ADamagedSlotIsIsolatedAndTheRestIsStillOffered()
    {
        var f = new Fixture(); var good = f.Project("a.spandraft"); var draft = f.Setup(); var bad = f.Damaged(); var badDraft = f.Damaged(RecoverySlotKind.Setup);
        await f.Load();
        Assert.Equal(4, f.Catalog.Undecided.Count);
        Assert.False(f.Of(good).IsDamaged); Assert.False(f.Of(draft).IsDamaged);
        Assert.True(f.Of(bad).IsDamaged); Assert.True(f.Of(badDraft).IsDamaged);
        Assert.NotNull(f.Of(bad).ValidationError); Assert.Equal(RecoveryCandidateState.Orphaned, f.Of(bad).State);
    }

    [Fact]
    public async Task AReadFailureOfOneSlotMakesOnlyThatCandidateDamaged()
    {
        var f = new Fixture(); var good = f.Project("a.spandraft"); var unreadable = f.Project("b.spandraft");
        f.Storage.FailRead = slot => slot == unreadable;
        await f.Load();
        Assert.False(f.Of(good).IsDamaged); Assert.True(f.Of(unreadable).IsDamaged);
    }

    [Fact]
    public async Task AnEnumerationFailureLeavesTheCatalogUnavailableUntilARetrySucceeds()
    {
        var f = new Fixture(); f.Project("a.spandraft"); f.Storage.FailEnumerate = new IOException("unreadable area");
        Assert.False(await f.Catalog.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RecoveryCatalogState.Unavailable, f.Catalog.State); Assert.Equal("unreadable area", f.Catalog.Error!.Message); Assert.False(f.Catalog.HasUndecided);
        f.Storage.FailEnumerate = null;
        Assert.True(await f.Catalog.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RecoveryCatalogState.Loaded, f.Catalog.State); Assert.Null(f.Catalog.Error); Assert.Single(f.Catalog.Undecided);
    }

    [Fact]
    public async Task LoadingTwiceDoesNotReadAgainOrDuplicate()
    {
        var f = new Fixture(); f.Project("a.spandraft"); await f.Load(); await f.Load();
        Assert.Single(f.Catalog.Candidates);
    }

    // ---- Create draft and project of the same id --------------------------------------------------------------

    [Fact]
    public async Task AValidProjectSupersedesItsDraftOfTheSameId()
    {
        var f = new Fixture(); var id = RecoveryId.New(); var project = f.Project(null, id: id); var draft = f.Setup(id: id);
        await f.Load();
        Assert.Equal(project, Assert.Single(f.Catalog.Candidates).Slot); Assert.Single(f.Catalog.Undecided);
    }

    [Fact]
    public async Task ADamagedProjectKeepsAValidDraftOfTheSameIdAsACandidate()
    {
        var f = new Fixture(); var id = RecoveryId.New(); f.Storage.Put(RecoverySlot.Project(id), Encoding.UTF8.GetBytes("{")); var draft = f.Setup(id: id);
        await f.Load();
        Assert.Equal(2, f.Catalog.Undecided.Count); Assert.False(f.Of(draft).IsDamaged); Assert.True(f.Of(RecoverySlot.Project(id)).IsDamaged);
    }

    [Fact]
    public async Task ADraftAndAProjectOfDifferentIdsAreBothOffered()
    {
        var f = new Fixture(); f.Project(null); f.Setup(); await f.Load();
        Assert.Equal(2, f.Catalog.Undecided.Count);
    }

    // ---- grouping, priority and registry ----------------------------------------------------------------------

    [Fact]
    public async Task TheNewestVariantOfAPathIsPublishedAsRecoveryPending()
    {
        var f = new Fixture(); var older = f.Project("a.spandraft", minute: 1); var newer = f.Project("a.spandraft", minute: 5); var other = f.Project("b.spandraft");
        await f.Load();
        Assert.True(f.Of(newer).IsPreferred); Assert.False(f.Of(older).IsPreferred); Assert.True(f.Of(other).IsPreferred);
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(f.Identity("a.spandraft")));
        Assert.Equal(f.Of(newer).CandidateId, f.Registry.ArbitrateOpen(f.Identity("a.spandraft")).RecoveryCandidate);
        Assert.Equal(f.Of(other).CandidateId, f.Registry.ArbitrateOpen(f.Identity("b.spandraft")).RecoveryCandidate);
    }

    [Fact]
    public async Task AnExactTieIsBrokenByIdOrderAndIdentifiedSlotsComeBeforeLegacy()
    {
        var f = new Fixture(); var low = new RecoveryId(Guid.Parse("00000000-0000-0000-0000-000000000001")); var high = new RecoveryId(Guid.Parse("ffffffff-0000-0000-0000-000000000000"));
        var a = f.Project("a.spandraft", id: high); var b = f.Project("a.spandraft", id: low); var legacy = f.Project("a.spandraft", legacy: true);
        await f.Load();
        Assert.True(f.Of(b).IsPreferred); Assert.False(f.Of(a).IsPreferred); Assert.False(f.Of(legacy).IsPreferred);
        var g = new Fixture(); var identified = g.Project("a.spandraft"); var old = g.Project("a.spandraft", legacy: true);
        await g.Load();
        Assert.True(g.Of(identified).IsPreferred); Assert.False(g.Of(old).IsPreferred);
        // A newer legacy slot still beats an older identified one.
        var h = new Fixture(); var oldIdentified = h.Project("a.spandraft", minute: -3); var newLegacy = h.Project("a.spandraft", legacy: true);
        await h.Load(); Assert.True(h.Of(newLegacy).IsPreferred);
    }

    [Fact]
    public async Task ProjectsWithoutAnOriginalAndDraftsNeverHoldAPath()
    {
        var f = new Fixture(); f.Project(null); f.Setup(); f.Damaged(); await f.Load();
        Assert.All(f.Catalog.Candidates, c => Assert.False(c.IsPreferred));
    }

    [Fact]
    public async Task DiscardingTheNewestVariantPassesThePathToTheNextOlderOneAndThenFreesIt()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft");
        var oldest = f.Project("a.spandraft", minute: 1); var middle = f.Project("a.spandraft", minute: 2); var newest = f.Project("a.spandraft", minute: 3);
        await f.Load();
        Assert.True(await f.Catalog.DiscardAsync(f.Of(newest).CandidateId));
        Assert.Equal(f.Of(middle).CandidateId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate); Assert.True(f.Of(middle).IsPreferred);
        Assert.Contains(newest.FileName, f.Storage.Discarded); Assert.False(f.Storage.Data.ContainsKey(newest));
        Assert.True(await f.Catalog.DiscardAsync(f.Of(middle).CandidateId));
        Assert.Equal(f.Of(oldest).CandidateId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
        Assert.True(await f.Catalog.DiscardAsync(f.Of(oldest).CandidateId));
        Assert.Equal(ProjectPathState.Free, f.Registry.StateOf(identity)); Assert.False(f.Catalog.HasUndecided);
    }

    [Fact]
    public async Task DiscardingAnOlderVariantLeavesThePreferredOneInPlace()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var older = f.Project("a.spandraft", minute: 1); var newer = f.Project("a.spandraft", minute: 2);
        await f.Load(); var candidate = f.Of(newer).CandidateId;
        Assert.True(await f.Catalog.DiscardAsync(f.Of(older).CandidateId));
        Assert.Equal(candidate, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
    }

    // ---- redundancy -------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARecoveryIdenticalToItsOriginalIsRemovedAndNeverOffered()
    {
        var f = new Fixture(); var state = State(3); string path = TestPath("same.spandraft");
        var json = System.Text.Json.Nodes.JsonNode.Parse(ProjectFileCodec.Serialize(state))!; json["futureField"] = true;   // formatting and extra data do not matter
        f.Files.Data[path] = Encoding.UTF8.GetBytes(json.ToJsonString());
        var slot = f.Project("same.spandraft", state: state); var companionId = slot.Id!.Value; var draft = f.Setup(id: companionId);
        await f.Load();
        Assert.Empty(f.Catalog.Candidates); Assert.False(f.Storage.Data.ContainsKey(slot)); Assert.False(f.Storage.Data.ContainsKey(draft));
        Assert.Equal(ProjectPathState.Free, f.Registry.StateOf(f.Identity("same.spandraft")));
    }

    [Theory]
    [InlineData("different")] [InlineData("missing")] [InlineData("malformed")] [InlineData("unreadable")]
    public async Task ARecoveryStaysWhenItsOriginalDiffersIsMissingMalformedOrUnreadable(string kind)
    {
        var f = new Fixture(); var state = State(3); string path = TestPath("orig.spandraft");
        switch (kind)
        {
            case "different": f.Files.Data[path] = ProjectFileCodec.Serialize(State(2)); break;
            case "malformed": f.Files.Data[path] = Encoding.UTF8.GetBytes("{}"); break;
            case "unreadable": f.Files.Data[path] = ProjectFileCodec.Serialize(state); f.Files.BeforeRead = p => p == path ? throw new IOException("locked") : Task.CompletedTask; break;
        }
        var slot = f.Project("orig.spandraft", state: state);
        await f.Load();
        Assert.True(f.Storage.Data.ContainsKey(slot)); Assert.Equal(RecoveryCandidateState.Orphaned, f.Of(slot).State);
    }

    [Fact]
    public async Task ARecoveryDiffersFromItsOriginalByProjectInformationAlone()
    {
        var f = new Fixture(); var saved = State() with { Info = new("Beam", null, null, "Author") };
        f.Files.Data[TestPath("o.spandraft")] = ProjectFileCodec.Serialize(saved);
        var same = f.Project("o.spandraft", state: saved);
        await f.Load(); Assert.Empty(f.Catalog.Candidates);
        var g = new Fixture(); g.Files.Data[TestPath("o.spandraft")] = ProjectFileCodec.Serialize(saved);
        var changed = g.Project("o.spandraft", state: saved with { Info = new("Beam", null, "Unsaved drawing", "Author") });
        await g.Load(); Assert.Single(g.Catalog.Undecided);
    }

    [Fact]
    public async Task ARedundantSlotThatCannotBeDeletedStaysHiddenAndIsCheckedAgainNextTime()
    {
        var f = new Fixture(); var state = State(3); f.Files.Data[TestPath("r.spandraft")] = ProjectFileCodec.Serialize(state);
        var slot = f.Project("r.spandraft", state: state); f.Storage.FailDelete = _ => true;
        await f.Load();
        Assert.Empty(f.Catalog.Candidates); Assert.True(f.Storage.Data.ContainsKey(slot));
    }

    // ---- claim ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AClaimedCandidateLeavesTheUndecidedListOnCommitAndReturnsOnAbort()
    {
        var f = new Fixture(); var slot = f.Project(null); await f.Load(); var id = f.Of(slot).CandidateId; int changes = 0; f.Catalog.Changed += () => changes++;
        var claim = (await f.Catalog.TryBeginClaimAsync(id))!;
        Assert.Equal(RecoveryCandidateState.Claiming, f.Catalog.Find(id)!.State); Assert.False(f.Catalog.HasUndecided);
        await claim.AbortAsync();
        Assert.Equal(RecoveryCandidateState.Orphaned, f.Catalog.Find(id)!.State); Assert.True(f.Catalog.HasUndecided);
        claim = (await f.Catalog.TryBeginClaimAsync(id))!; await claim.CommitAsync(); await claim.CommitAsync(); await claim.AbortAsync();   // finishing twice is harmless
        Assert.Equal(RecoveryCandidateState.Claimed, f.Catalog.Find(id)!.State); Assert.False(f.Catalog.HasUndecided);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task ACandidateCanOnlyBeClaimedOnceAndNotWhileBeingDiscarded()
    {
        var f = new Fixture(); var slot = f.Project(null); await f.Load(); var id = f.Of(slot).CandidateId;
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Catalog.TryBeginClaimAsync(id)));
        Assert.Single(claims, c => c is not null);
        Assert.False(await f.Catalog.DiscardAsync(id));                       // claiming candidates are not discardable
        await claims.Single(c => c is not null)!.CommitAsync();
        Assert.False(await f.Catalog.DiscardAsync(id)); Assert.Null(await f.Catalog.TryBeginClaimAsync(id));
        Assert.True(f.Storage.Data.ContainsKey(slot));
    }

    [Fact]
    public async Task ClaimAndDiscardOfTheSameCandidateInParallelHaveExactlyOneWinner()
    {
        var f = new Fixture(); var slot = f.Project(null); await f.Load(); var id = f.Of(slot).CandidateId;
        var claim = f.Catalog.TryBeginClaimAsync(id); var discard = f.Catalog.DiscardAsync(id);
        var claimed = await claim; bool discarded = await discard;
        Assert.True(claimed is not null ^ discarded);
    }

    [Fact]
    public async Task ADamagedCandidateCannotBeClaimedButCanBeDiscarded()
    {
        var f = new Fixture(); var bad = f.Damaged(); await f.Load(); var id = f.Of(bad).CandidateId;
        Assert.Null(await f.Catalog.TryBeginClaimAsync(id));
        Assert.True(await f.Catalog.DiscardAsync(id)); Assert.Contains(bad.FileName, f.Storage.Discarded); Assert.Empty(f.Catalog.Candidates);
    }

    [Fact]
    public async Task ALegacySlotIsMovedToAnIdentifiedSlotOnlyWhenClaimed()
    {
        var f = new Fixture(); var legacy = f.Project("a.spandraft", legacy: true); var legacyDraft = f.Setup(legacy: true); await f.Load();
        Assert.True(f.Storage.Data.ContainsKey(RecoverySlot.LegacyProject)); Assert.True(f.Storage.Data.ContainsKey(RecoverySlot.LegacySetup));
        Assert.True(f.Of(legacy).IsLegacy);
        var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(legacy).CandidateId))!;
        Assert.False(claim.Slot.IsLegacy); Assert.Equal(RecoverySlotKind.Project, claim.Slot.Kind);
        Assert.True(f.Storage.Data.ContainsKey(claim.Slot)); Assert.False(f.Storage.Data.ContainsKey(RecoverySlot.LegacyProject));
        Assert.True(f.Storage.Data.ContainsKey(RecoverySlot.LegacySetup));     // untouched: it was not claimed
        await claim.CommitAsync();
        var draftClaim = (await f.Catalog.TryBeginClaimAsync(f.Of(legacyDraft).CandidateId))!;
        Assert.Equal(RecoverySlotKind.Setup, draftClaim.Slot.Kind); Assert.False(draftClaim.Slot.IsLegacy);
    }

    [Fact]
    public async Task AFailedLegacyMoveLeavesTheLegacySlotAndTheCandidateUntouched()
    {
        var f = new Fixture(); var legacy = f.Project("a.spandraft", legacy: true); await f.Load(); var id = f.Of(legacy).CandidateId;
        f.Storage.FailMove = (_, _) => true;
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.TryBeginClaimAsync(id));
        Assert.True(f.Storage.Data.ContainsKey(RecoverySlot.LegacyProject)); Assert.Equal(RecoveryCandidateState.Orphaned, f.Catalog.Find(id)!.State);
        Assert.True(f.Catalog.Find(id)!.IsLegacy);
        f.Storage.FailMove = null;
        Assert.NotNull(await f.Catalog.TryBeginClaimAsync(id));
    }

    [Fact]
    public async Task ClaimingTheProjectRemovesItsSupersededDraftBeforeTheWorkspaceOwnsTheSlot()
    {
        var f = new Fixture(); var id = RecoveryId.New(); var project = f.Project(null, id: id); var draft = f.Setup(id: id); await f.Load();
        var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(project).CandidateId))!;
        // Afterwards the project slot is the workspace's to delete at any time, so no draft may be left that it keeps superseded.
        Assert.False(f.Storage.Data.ContainsKey(draft)); Assert.True(f.Storage.Data.ContainsKey(project));
        await claim.CommitAsync();
        Assert.False(f.Storage.Data.ContainsKey(draft)); Assert.True(f.Storage.Data.ContainsKey(project));
    }

    [Fact]
    public async Task AProjectWhoseSupersededDraftCannotBeDeletedIsNotRestoredAndStaysUndecided()
    {
        var f = new Fixture(); var id = RecoveryId.New(); var project = f.Project(null, id: id); var draft = f.Setup(id: id); await f.Load();
        f.Storage.FailDelete = slot => slot == draft;
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.TryBeginClaimAsync(f.Of(project).CandidateId));
        Assert.Equal(RecoveryCandidateState.Orphaned, f.Of(project).State); Assert.True(f.Storage.Data.ContainsKey(draft));
        f.Storage.FailDelete = null;
        Assert.NotNull(await f.Catalog.TryBeginClaimAsync(f.Of(project).CandidateId)); Assert.False(f.Storage.Data.ContainsKey(draft));
    }

    [Fact]
    public async Task ARedundantProjectKeepsItsSlotWhileItsSupersededDraftCannotBeDeleted()
    {
        var f = new Fixture(); var state = State(3); string path = TestPath("same.spandraft"); var id = RecoveryId.New();
        f.Files.Data[path] = ProjectFileCodec.Serialize(state);
        var project = f.Project("same.spandraft", state: state, id: id); var draft = f.Setup(id: id);
        f.Storage.FailDelete = slot => slot == draft;
        await f.Load();
        // Nothing is offered, but the project slot stays so that the draft cannot become a valid old draft by itself.
        Assert.Empty(f.Catalog.Candidates); Assert.True(f.Storage.Data.ContainsKey(project)); Assert.True(f.Storage.Data.ContainsKey(draft));
        var restart = new Fixture { }; restart.Storage.Put(project, f.Storage.Data[project]); restart.Storage.Put(draft, f.Storage.Data[draft]);
        restart.Files.Data[path] = f.Files.Data[path];
        await restart.Load();
        Assert.Empty(restart.Catalog.Candidates); Assert.False(restart.Storage.Data.ContainsKey(project)); Assert.False(restart.Storage.Data.ContainsKey(draft));
    }

    [Fact]
    public async Task ADraftSharingItsIdWithADamagedProjectIsRestoredUnderAFreshIdSoDiscardingTheDamagedOneCannotTouchIt()
    {
        var f = new Fixture(); var id = RecoveryId.New();
        var damaged = new RecoverySlot(RecoverySlotKind.Project, id); f.Storage.Put(damaged, Encoding.UTF8.GetBytes("{"));
        var draft = f.Setup(id: id); await f.Load();
        var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(draft).CandidateId))!;
        Assert.NotEqual(id, claim.Slot.Id); Assert.Equal(RecoverySlotKind.Setup, claim.Slot.Kind);
        var main = new MainWindowViewModel(WindowId.Create(), f.Registry, f.Resolver, files: f.Files, dialogs: f.Dialogs, recovery: new(f.Storage, utcNow: () => Now));
        main.RestoreSetup(claim.Candidate.Setup!, claim.Slot); await claim.CommitAsync();
        CompleteSetup(main); Assert.True(await main.Setup.ConfirmAsync());
        // The confirmed draft became a project under the new id; the damaged candidate is a different slot.
        var created = RecoverySlot.Project(claim.Slot.Id!.Value);
        Assert.True(f.Storage.Data.ContainsKey(created)); Assert.Equal("{", Encoding.UTF8.GetString(f.Storage.Data[damaged]));
        Assert.True(await f.Catalog.DiscardAsync(f.Of(damaged).CandidateId));
        Assert.True(f.Storage.Data.ContainsKey(created)); Assert.False(f.Storage.Data.ContainsKey(damaged));
    }

    [Fact]
    public async Task ARestoreThatFailsWhileAdoptingTheSlotLeavesNoRegistryOwnershipBehind()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var slot = f.Project("a.spandraft"); await f.Load();
        var context = new WorkspaceRecoveryContext(f.Storage); context.SetupDraftChanged(new());   // the workspace already has a recovery
        var main = new MainWindowViewModel(WindowId.Create(), f.Registry, f.Resolver, files: f.Files, dialogs: f.Dialogs, recovery: context);
        var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(slot).CandidateId))!;
        Assert.Throws<InvalidOperationException>(() => main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId));
        Assert.Null(main.Session); Assert.Null(f.Registry.IdentityOwnedBy(main.WindowId)); Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, context.State);
        await claim.AbortAsync();
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity)); Assert.Equal(RecoveryCandidateState.Orphaned, f.Of(slot).State);
    }

    [Fact]
    public async Task AnOriginalThatDoesNotAnswerInTimeKeepsItsRecoveryAndDoesNotHoldBackTheStart()
    {
        var f = new Fixture(); var state = State(3);
        var slow = f.Project("slow.spandraft", state: state); var fast = f.Project("fast.spandraft", state: state);
        f.Files.Data[TestPath("fast.spandraft")] = ProjectFileCodec.Serialize(state);
        f.Files.BeforeRead = path => path == TestPath("slow.spandraft") ? new TaskCompletionSource().Task : Task.CompletedTask;
        f.Catalog.RedundancyTimeout = TimeSpan.FromMilliseconds(100);
        await WaitFor(f.Load());
        Assert.False(f.Of(slow).IsDamaged); Assert.True(f.Storage.Data.ContainsKey(slow));
        Assert.DoesNotContain(f.Catalog.Candidates, c => c.Slot == fast);
    }

    // ---- error classification -----------------------------------------------------------------------------------

    [Fact]
    public async Task ASecurityErrorOnOneSlotDamagesOnlyThatCandidateAndOnTheAreaItFailsClosed()
    {
        var f = new Fixture(); var good = f.Project("a.spandraft"); var locked = f.Project("b.spandraft");
        f.Storage.FailRead = slot => slot == locked; f.Storage.FailureFactory = _ => new System.Security.SecurityException("denied");
        await f.Load();
        Assert.False(f.Of(good).IsDamaged); Assert.True(f.Of(locked).IsDamaged);

        var g = new Fixture(); g.Project("a.spandraft"); g.Storage.FailEnumerate = new System.Security.SecurityException("denied");
        Assert.False(await g.Catalog.InitializeAsync(TestContext.Current.CancellationToken)); Assert.Equal(RecoveryCatalogState.Unavailable, g.Catalog.State);
    }

    [Fact]
    public async Task AnOriginalPathTheResolverRejectsKeepsTheValidCandidateUnresolved()
    {
        var resolver = new TestIdentityResolver(p => throw new NotSupportedException("bad path format"));
        var f = new Fixture(resolver); var slot = f.Project("a.spandraft"); await f.Load();
        Assert.False(f.Of(slot).IsDamaged); Assert.Null(f.Of(slot).Identity); Assert.True(f.Catalog.HasUndecided);
    }

    // ---- discard ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task DiscardMovesTheSlotToTheDiscardedAreaAndChangedIsRaised()
    {
        var f = new Fixture(); var slot = f.Project("a.spandraft"); var legacy = f.Setup(legacy: true); await f.Load(); int changes = 0; f.Catalog.Changed += () => changes++;
        Assert.True(await f.Catalog.DiscardAsync(f.Of(slot).CandidateId)); Assert.True(await f.Catalog.DiscardAsync(f.Of(legacy).CandidateId));
        Assert.Equal(2, f.Storage.Discarded.Count); Assert.Equal(2, changes); Assert.Empty(f.Storage.Slots());
        Assert.Contains(f.Storage.Discarded, n => n.StartsWith("project-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedDiscardLeavesTheCandidateOfferedAndThePathHeld()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var slot = f.Project("a.spandraft"); await f.Load(); var id = f.Of(slot).CandidateId;
        f.Storage.FailDiscard = _ => true;
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.DiscardAsync(id));
        Assert.Equal(RecoveryCandidateState.Orphaned, f.Catalog.Find(id)!.State); Assert.True(f.Storage.Data.ContainsKey(slot));
        Assert.Equal(id, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
        f.Storage.FailDiscard = null; Assert.True(await f.Catalog.DiscardAsync(id));
    }

    [Fact]
    public async Task DiscardingAPairMovesTheDraftBeforeTheProjectAndAFailingProjectMoveKeepsTheProjectActive()
    {
        var f = new Fixture(); var id = RecoveryId.New(); var project = f.Project(null, id: id); var draft = f.Setup(id: id); await f.Load(); var candidate = f.Of(project).CandidateId;
        var order = new List<string>(); f.Storage.FailDiscard = slot => { order.Add(slot.FileName); return slot.Kind == RecoverySlotKind.Project; };
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.DiscardAsync(candidate));
        Assert.Equal(new[] { draft.FileName, project.FileName }, order);
        Assert.Equal(project, Assert.Single(f.Catalog.Undecided).Slot); Assert.False(f.Storage.Data.ContainsKey(draft)); Assert.True(f.Storage.Data.ContainsKey(project));
        f.Storage.FailDiscard = null; Assert.True(await f.Catalog.DiscardAsync(candidate)); Assert.Empty(f.Storage.Slots());
    }

    [Fact]
    public async Task AFailingDraftMoveChangesNothingAtAll()
    {
        var f = new Fixture(); var id = RecoveryId.New(); var project = f.Project(null, id: id); var draft = f.Setup(id: id); await f.Load(); var candidate = f.Of(project).CandidateId;
        f.Storage.FailDiscard = slot => slot.Kind == RecoverySlotKind.Setup;
        await Assert.ThrowsAsync<IOException>(() => f.Catalog.DiscardAsync(candidate));
        Assert.True(f.Storage.Data.ContainsKey(draft)); Assert.True(f.Storage.Data.ContainsKey(project)); Assert.Single(f.Catalog.Undecided);
    }

    // ---- restore through the workspace and the registry -------------------------------------------------------

    private static MainWindowViewModel Workspace(Fixture f, TestRecoveryStorage? storage = null, Action<Guid?>? showRecoveryCenter = null) =>
        new(WindowId.Create(), f.Registry, f.Resolver, showRecoveryCenter: showRecoveryCenter, files: f.Files, dialogs: f.Dialogs, recovery: new(storage ?? f.Storage));

    [Fact]
    public async Task RestoringThePreferredVariantOwnsThePathAndTheOlderVariantTakesOverWhenItIsReleased()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var older = f.Project("a.spandraft", minute: 1); var newer = f.Project("a.spandraft", minute: 2); await f.Load();
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(newer).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId); await claim.CommitAsync();
        Assert.Equal(ProjectPathState.Open, f.Registry.StateOf(identity)); Assert.Equal(main.WindowId, f.Registry.ArbitrateOpen(identity).Owner);
        Assert.True(f.Of(older).IsPreferred); Assert.Null(main.Session!.RecoveryOriginPath); Assert.Equal(identity.CanonicalPath, main.Session.FilePath);
        // Closing the restored project hands the path straight to the older variant, with no Free state in between.
        Assert.True(f.Registry.ReleaseOwner(main.WindowId));
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity));
        Assert.Equal(f.Of(older).CandidateId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
    }

    [Fact]
    public async Task AnOlderVariantRestoresDetachedWithoutTouchingThePath()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var older = f.Project("a.spandraft", minute: 1); var newer = f.Project("a.spandraft", minute: 2); await f.Load();
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(older).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, null); await claim.CommitAsync();
        Assert.Null(main.Session!.FilePath); Assert.Equal(TestPath("a.spandraft"), main.Session.RecoveryOriginPath);
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity));
        Assert.Equal(f.Of(newer).CandidateId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
    }

    [Fact]
    public async Task AnAbortedRestoreKeepsThePathPendingAndTheCandidateUndecided()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var slot = f.Project("a.spandraft"); await f.Load(); var id = f.Of(slot).CandidateId;
        var claim = (await f.Catalog.TryBeginClaimAsync(id))!;
        Assert.Equal(id, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);       // still held while the restore runs
        await claim.AbortAsync();
        Assert.Equal(id, f.Registry.ArbitrateOpen(identity).RecoveryCandidate); Assert.True(f.Catalog.Find(id)!.State == RecoveryCandidateState.Orphaned);
    }

    [Fact]
    public async Task EveryPlaceWhereAnOpenEntryDisappearsHandsThePathToTheNextVariant()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var second = f.Project("a.spandraft", minute: 1); var first = f.Project("a.spandraft", minute: 2); await f.Load();
        var secondId = f.Of(second).CandidateId;
        // A restore of the preferred variant, then Save As elsewhere gives the original path up.
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(first).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId); await claim.CommitAsync();
        f.Dialogs.SavePath = TestPath("elsewhere.spandraft");
        Assert.True(await main.SaveAsync(saveAs: true));
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity)); Assert.Equal(secondId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
    }

    [Fact]
    public async Task ADiscardAfterTheOwnerLeftFreesThePathThatTheHandOverHadMadePending()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var second = f.Project("a.spandraft", minute: 1); var first = f.Project("a.spandraft", minute: 2); await f.Load();
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(first).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId); await claim.CommitAsync();
        Assert.True(f.Registry.ReleaseOwner(main.WindowId));
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity));
        Assert.True(await f.Catalog.DiscardAsync(f.Of(second).CandidateId));
        Assert.Equal(ProjectPathState.Free, f.Registry.StateOf(identity));
        Assert.Null(f.Registry.RecoveryCandidateOf(identity));
    }

    [Fact]
    public async Task ADiscardAfterSaveAsMovedAwayFreesThePathThatTheHandOverHadMadePending()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var second = f.Project("a.spandraft", minute: 1); var first = f.Project("a.spandraft", minute: 2); await f.Load();
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(first).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId); await claim.CommitAsync();
        f.Dialogs.SavePath = TestPath("elsewhere.spandraft");
        Assert.True(await main.SaveAsync(saveAs: true));
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity));
        Assert.True(await f.Catalog.DiscardAsync(f.Of(second).CandidateId));
        Assert.Equal(ProjectPathState.Free, f.Registry.StateOf(identity));
    }

    [Fact]
    public async Task ADiscardWhileAWindowStillOwnsThePathLetsItFallFreeOnRelease()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var second = f.Project("a.spandraft", minute: 1); var first = f.Project("a.spandraft", minute: 2); await f.Load();
        var main = Workspace(f); var claim = (await f.Catalog.TryBeginClaimAsync(f.Of(first).CandidateId))!;
        main.RestoreProject(claim.Candidate.Project!, claim.Slot, claim.Candidate.CandidateId); await claim.CommitAsync();
        Assert.True(await f.Catalog.DiscardAsync(f.Of(second).CandidateId));
        Assert.Equal(ProjectPathState.Open, f.Registry.StateOf(identity));
        Assert.True(f.Registry.ReleaseOwner(main.WindowId));
        Assert.Equal(ProjectPathState.Free, f.Registry.StateOf(identity));
    }

    [Fact]
    public async Task AnOriginalThatBecomesResolvableAfterLoadStillClaimsItsPathBeforeOpenAndSaveAs()
    {
        bool online = false;
        var resolver = new TestIdentityResolver(p => online ? new(p, caseSensitive: true) : throw new IOException("volume not available"));
        var f = new Fixture(resolver); var slot = f.Project("a.spandraft"); await f.Load();
        Assert.Null(f.Of(slot).Identity); Assert.True(f.Catalog.HasUndecided);        // kept and undecided, but without a path claim
        online = true;
        var identity = resolver.Resolve(TestPath("a.spandraft"));
        f.Files.Data[TestPath("a.spandraft")] = ProjectFileCodec.Serialize(State(7));

        // Open on the original path leads to the recovery, not to the file on disk.
        Guid? shown = null; var opener = Workspace(f, showRecoveryCenter: id => shown = id);
        Assert.False(await opener.OpenAsync(TestPath("a.spandraft")));
        Assert.Equal(f.Of(slot).CandidateId, shown); Assert.Null(opener.Session);
        Assert.Equal(f.Of(slot).Identity, identity); Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(identity));
    }

    [Fact]
    public async Task SaveAsOntoAnOriginalThatBecomesResolvableAfterLoadIsRejectedWhileItsRecoveryIsPending()
    {
        bool online = false;
        var resolver = new TestIdentityResolver(p => online ? new(p, caseSensitive: true) : throw new IOException("volume not available"));
        var f = new Fixture(resolver); var slot = f.Project("a.spandraft"); await f.Load();
        online = true;
        var main = Workspace(f);
        main.NewCommand.Execute(null); CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        f.Dialogs.SavePath = TestPath("a.spandraft");
        Assert.False(await main.SaveAsync(saveAs: true));
        Assert.Equal(ProjectPathState.RecoveryPending, f.Registry.StateOf(resolver.Resolve(TestPath("a.spandraft"))));
        Assert.False(f.Files.Data.ContainsKey(TestPath("a.spandraft")));
    }

    [Fact]
    public async Task AnOpenAttemptThatIsCancelledOrFailsNeverLosesAPendingVariant()
    {
        var f = new Fixture(); var identity = f.Identity("a.spandraft"); var slot = f.Project("a.spandraft", minute: 1); await f.Load();
        // Another window briefly holds the path (Opening) while the catalog also wants it pending; its release passes the path on.
        Assert.True(f.Registry.UpdateRecovery(identity, f.Of(slot).CandidateId, null));
        var token = f.Registry.ArbitrateOpen(identity).Reservation!;
        Assert.True(f.Registry.TryReleaseOpen(token, ProjectOpenResult.Cancelled));
        Assert.Equal(f.Of(slot).CandidateId, f.Registry.ArbitrateOpen(identity).RecoveryCandidate);
    }
}
