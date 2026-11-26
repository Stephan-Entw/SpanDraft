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
/// </remarks>
internal sealed class WorkspaceCloseTransaction
{
    private readonly MainWindowViewModel _model;
    private readonly WorkspaceWindowHandle? _handle;
    private readonly OwnerReleaseToken? _release;
    private readonly bool _recordRecent;
    private RecoveryLeave? _recovery;
    private bool _finished;

    private WorkspaceCloseTransaction(MainWindowViewModel model, WorkspaceWindowHandle? handle, OwnerReleaseToken? release, bool recordRecent)
    {
        _model = model;
        _handle = handle;
        _release = release;
        _recordRecent = recordRecent;
    }

    public MainWindowViewModel Model => _model;

    /// <summary>
    /// Preflight and reservation, synchronously. Null when the workspace cannot close now: it is busy (a native picker, a
    /// running operation, a settings freeze), already closing, or not routable. <paramref name="handle"/> is null only for a
    /// workspace without a window (view model level).
    /// </summary>
    public static WorkspaceCloseTransaction? TryBegin(MainWindowViewModel model, WorkspaceWindowHandle? handle, bool recordRecent = true)
    {
        if (handle is { CanRoute: false }) return null;
        // Out of request routing first, so nothing observes the reserved workspace as routable.
        if (handle is not null) handle.IsCloseRequested = true;
        if (!model.TryReserveLeave())
        {
            if (handle is not null) handle.IsCloseRequested = false;
            return null;
        }
        var release = model.ProjectPaths.BeginOwnerRelease(model.WindowId);
        return new(model, handle, release, recordRecent);
    }

    /// <summary>
    /// The decision (Save/Discard/Cancel only for unsaved project changes) and the recovery preparation. False when the user
    /// cancelled or something failed (already shown and restored); the caller then calls <see cref="AbortAsync"/>.
    /// </summary>
    public async Task<bool> PrepareAsync()
    {
        var choice = await _model.DecideCloseAsync(_recordRecent);
        if (choice == LeaveChoice.Cancel) return false;
        var (ok, leave) = await _model.PrepareCloseAsync(choice);
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
        if (_release is not null) _model.ProjectPaths.EndOwnerRelease(_release);
        return report;
    }

    /// <summary>The final cleanup released the ownership of the closed workspace: waiting opens decide anew.</summary>
    public void FinishClosed()
    {
        if (_release is not null) _model.ProjectPaths.EndOwnerRelease(_release);
    }

    /// <summary>
    /// Gives the close up, in this order: restore the recovery protection (a fault if impossible), release the reservation,
    /// make the workspace routable again, run <paramref name="afterRoutable"/> (the owner's quit cancel), then end the owner
    /// release so that waiting opens find their owner activatable. Idempotent after a commit or an earlier abort.
    /// </summary>
    public async Task AbortAsync(Action? afterRoutable = null)
    {
        if (_finished) return;
        _finished = true;
        if (_recovery is not null) await _recovery.RollbackAsync();
        var report = _model.ReleaseLeaveReservation();
        if (_handle is not null) _handle.IsCloseRequested = false;
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
