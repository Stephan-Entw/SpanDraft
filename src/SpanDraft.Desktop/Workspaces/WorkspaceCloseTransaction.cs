using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;
using RecoveryLeave = SpanDraft.Desktop.Persistence.WorkspaceRecoveryContext.RecoveryLeave;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>
/// The close of one workspace (§16), shared by the window close, Close Project and the later application quit:
/// <c>Preflight → Reservation/Freeze → decision → Prepare → Commit/Rollback</c>.
/// </summary>
/// <remarks>
/// <see cref="TryBegin"/> runs synchronously, before the first await: the workspace leaves request routing, its view model is
/// reserved (busy) and its path ownership is marked as possibly released, so opens of its paths wait instead of being told
/// they are open. The reservation lasts through the decision, the preparation and up to the commit or the rollback.
/// Nothing visible changes and nothing is released before <see cref="Commit"/>: the session, editor, EditSetup inputs, report,
/// project identity and start slot stay as they are. The commit is synchronous and cannot fail; everything that can still
/// abort a close (the caller's last-window arbitration) happens before it and ends in <see cref="AbortAsync"/>.
/// A save that succeeded during the decision is never undone. This type holds no window or quit logic.
/// A workspace whose window is closed by the platform (system shutdown) before the commit leaves its transaction orphaned
/// (<see cref="FinishClosed"/>): late continuations finish what is already under way and decide nothing new.
/// </remarks>
internal sealed class WorkspaceCloseTransaction
{
    private readonly MainWindowViewModel _model;
    private readonly WorkspaceWindowHandle? _handle;
    private readonly OwnerReleaseToken? _release;
    private readonly bool _recordRecent;
    private readonly bool _fromQuitFreeze;
    private RecoveryLeave? _recovery;
    private bool _finished, _orphaned;

    private WorkspaceCloseTransaction(MainWindowViewModel model, WorkspaceWindowHandle? handle, OwnerReleaseToken? release,
        bool recordRecent, bool fromQuitFreeze)
    {
        _model = model;
        _handle = handle;
        _release = release;
        _recordRecent = recordRecent;
        _fromQuitFreeze = fromQuitFreeze;
    }

    public MainWindowViewModel Model => _model;

    /// <summary>
    /// The workspace was cleaned up before this transaction committed (its window was closed by a system shutdown): nothing
    /// is decided, committed or shown for it any more.
    /// </summary>
    public bool IsOrphaned => _orphaned;

    /// <summary>
    /// Preflight and reservation, synchronously. Null when the workspace cannot close now: it is busy (a native picker, a
    /// running operation, a settings freeze), already closing, or not routable. <paramref name="handle"/> is null only for a
    /// workspace without a window (view model level). <paramref name="fromQuitFreeze"/>: the application quit hands its
    /// freeze of this workspace over to the close reservation, without a gap; only a frozen workspace can be begun so.
    /// </summary>
    public static WorkspaceCloseTransaction? TryBegin(MainWindowViewModel model, WorkspaceWindowHandle? handle, bool recordRecent = true,
        bool fromQuitFreeze = false)
    {
        if (handle is { CanRoute: false }) return null;
        // Out of request routing first, so nothing observes the reserved workspace as routable.
        if (handle is not null) handle.IsCloseRequested = true;
        if (!model.TryReserveLeave(fromQuitFreeze))
        {
            if (handle is not null) handle.IsCloseRequested = false;
            return null;
        }
        var release = model.ProjectPaths.BeginOwnerRelease(model.WindowId);
        var transaction = new WorkspaceCloseTransaction(model, handle, release, recordRecent, fromQuitFreeze);
        // Known to the workspace from the start: its final cleanup finishes or orphans it, whatever state it is in.
        if (handle is not null) handle.CloseTransaction = transaction;
        return transaction;
    }

    /// <summary>
    /// The decision (Save/Discard/Cancel only for unsaved project changes) and the recovery preparation. False when the user
    /// cancelled or something failed (already shown and restored); the caller then calls <see cref="AbortAsync"/>.
    /// </summary>
    public async Task<bool> PrepareAsync()
    {
        var choice = await _model.DecideCloseAsync(_recordRecent);
        // Orphaned meanwhile: no new recovery transition is started for a workspace that is gone.
        if (choice == LeaveChoice.Cancel || _orphaned) return false;
        var (ok, leave) = await _model.PrepareCloseAsync(choice);
        if (_orphaned)
        {
            // The prepared transition was the user's decision (Discard, or cleanup after Save): it stays final, nothing is
            // republished and the context writes nothing more for the workspace that is gone.
            leave?.Commit(retire: true);
            return false;
        }
        if (!ok) return false;
        _recovery = leave;
        return true;
    }

    /// <summary>
    /// Commits the prepared close; synchronous, nothing can fail. For a window close the workspace leaves the start slot and
    /// stays reserved until <see cref="FinishClosed"/>; otherwise it becomes the start page and is released at once. The
    /// returned task only reports what was deferred meanwhile; await it after the commit is complete.
    /// </summary>
    public Task Commit(bool closeWindow)
    {
        if (_finished) throw new InvalidOperationException("The close transaction is already finished.");
        _finished = true;
        _model.CommitClose(_recovery, closeWindow);
        if (closeWindow)
        {
            if (_handle is not null) _handle.IsClosing = true;
            return Task.CompletedTask;
        }
        var report = _model.ReleaseLeaveReservation();
        if (_handle is not null) _handle.IsCloseRequested = false;
        Detach();
        if (_release is not null) _model.ProjectPaths.EndOwnerRelease(_release);
        return report;
    }

    /// <summary>
    /// The final cleanup of the workspace released its ownership: waiting opens decide anew. Before a commit this happens
    /// only when the platform closed the window (system shutdown): the transaction is orphaned then.
    /// </summary>
    public void FinishClosed()
    {
        if (!_finished) _finished = _orphaned = true;
        if (_release is not null) _model.ProjectPaths.EndOwnerRelease(_release);
    }

    private void Detach()
    {
        if (_handle is not null && ReferenceEquals(_handle.CloseTransaction, this)) _handle.CloseTransaction = null;
    }

    /// <summary>
    /// Gives the close up, in this order: restore the recovery protection (a fault if impossible), release the reservation,
    /// make the workspace routable again, run <paramref name="afterRoutable"/> (the owner's quit cancel), then end the owner
    /// release so that waiting opens find their owner activatable. Idempotent after a commit, an earlier abort or orphaning.
    /// </summary>
    public async Task AbortAsync(Action? afterRoutable = null)
    {
        if (_finished) return;
        _finished = true;
        if (_recovery is not null) await _recovery.RollbackAsync();
        // A quit's close gives the workspace back to the quit freeze, which the quit then ends for all workspaces at once.
        var report = _model.ReleaseLeaveReservation(backToQuitFreeze: _fromQuitFreeze);
        if (_handle is not null) _handle.IsCloseRequested = false;
        Detach();
        afterRoutable?.Invoke();
        if (_release is not null) _model.ProjectPaths.EndOwnerRelease(_release);
        await report;
    }

    /// <summary>A whole close without window and lifecycle: used by Close Project without an owner and at view model level.</summary>
    public static async Task<bool> RunAsync(MainWindowViewModel model, bool closeWindow)
    {
        if (TryBegin(model, handle: null, recordRecent: closeWindow) is not { } transaction) return false;
        try
        {
            if (!await transaction.PrepareAsync())
            {
                await transaction.AbortAsync();
                return false;
            }
            await transaction.Commit(closeWindow);
            // Without a window there is no Closed event: the final cleanup of the workspace happens here, once.
            if (closeWindow)
            {
                model.ReleaseReport();
                model.Dispose();
                transaction.FinishClosed();
            }
            return true;
        }
        catch
        {
            await transaction.AbortAsync();
            throw;
        }
    }
}
