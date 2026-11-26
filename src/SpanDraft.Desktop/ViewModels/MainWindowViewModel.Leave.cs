using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Workspaces;
using RecoveryLeave = SpanDraft.Desktop.Persistence.WorkspaceRecoveryContext.RecoveryLeave;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>The outcome of the Save/Discard/Cancel decision when the workspace leaves its project.</summary>
internal enum LeaveChoice { Cancel, Clean, Saved, Discard }

/// <summary>
/// Leaving the project or draft of this workspace: the one decision (Save/Discard/Cancel), the recovery preparation and
/// the commit, shared by every path that gives up the current work. A <see cref="WorkspaceCloseTransaction"/> holds them
/// together for a close; in-workspace transitions (Open, Create, setup cancel) use them inside their own operation.
/// </summary>
public sealed partial class MainWindowViewModel
{
    // A close transaction reserved this workspace: it is busy until the transaction commits or rolls back.
    private bool _leaveReserved;
    private bool _pendingFaultReport;

    internal ProjectPathRegistry ProjectPaths => _projectPaths;

    /// <summary>The Create draft that owns the workspace's draft recovery, if one is shown.</summary>
    private ProjectSetupDraft? CurrentDraft => Mode == MainViewMode.ProjectSetup && Setup.Mode == ProjectSetupMode.Create ? Setup.Draft : null;

    /// <summary>Asks about unsaved project changes. Save failures are thrown; a cancelled save is <see cref="LeaveChoice.Cancel"/>.</summary>
    private async Task<LeaveChoice> DecideLeaveAsync(bool recordRecent = true)
    {
        if (Session?.IsDirty != true) return LeaveChoice.Clean;
        var decision = _dialogs is null ? LeaveDecision.Cancel : await _dialogs.ConfirmLeaveAsync(ProjectFileName);
        return decision switch
        {
            LeaveDecision.Discard => LeaveChoice.Discard,
            LeaveDecision.Save => await SaveCurrentAsync(recordRecent: recordRecent) ? LeaveChoice.Saved : LeaveChoice.Cancel,
            _ => LeaveChoice.Cancel
        };
    }

    /// <summary>
    /// The recovery part of an in-workspace transition, committed at once (nothing can abort it afterwards). False after a
    /// shown failure: the workspace keeps its work and its protection (or is faulted if even that could not be restored).
    /// </summary>
    private async Task<bool> LeaveRecoveryAsync(RecoveryLeaveScope? scope, LeaveChoice choice)
    {
        if (scope is not { } leaving || _recovery is null) return true;
        try
        {
            var leave = await _recovery.PrepareLeaveAsync(leaving, choice == LeaveChoice.Discard, Session, CurrentDraft);
            leave.Commit(retire: false);
            return true;
        }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryError, e);
            return false;
        }
    }

    // ---- close transaction primitives ---------------------------------------------------------------------------

    /// <summary>
    /// Synchronously reserves the workspace for a close: commands, inputs, settings freezes and opens are refused from here
    /// until the transaction commits or releases it. False when it is busy, already reserved or gone.
    /// </summary>
    internal bool TryReserveLeave()
    {
        if (_disposed || _leaveReserved || IsWorkspaceBusy) return false;
        _leaveReserved = true;
        Session?.SetBusy(true);
        NotifySession();
        return true;
    }

    /// <summary>The decision of a close; a failed save is shown and counts as Cancel. Nothing visible changes.</summary>
    internal async Task<LeaveChoice> DecideCloseAsync(bool recordRecent)
    {
        try { return await DecideLeaveAsync(recordRecent); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.ProjectFileError, e);
            return LeaveChoice.Cancel;
        }
    }

    /// <summary>Prepares the recovery of the whole workspace for its close; false after a shown failure.</summary>
    internal async Task<(bool Ok, RecoveryLeave? Leave)> PrepareCloseAsync(LeaveChoice choice)
    {
        if (_recovery is null) return (true, null);
        try { return (true, await _recovery.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, choice == LeaveChoice.Discard, Session, CurrentDraft)); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryError, e);
            return (false, null);
        }
    }

    /// <summary>
    /// Commits a close synchronously. For a window close the workspace stays reserved and keeps session, report and
    /// ownership: the final cleanup releases them exactly once. Otherwise it becomes the start page here.
    /// </summary>
    internal void CommitClose(RecoveryLeave? leave, bool closeWindow)
    {
        leave?.Commit(retire: closeWindow);
        Editor?.CancelEditorInteraction();
        if (closeWindow) return;
        ReleaseReport();
        DetachSession();
        _projectPaths.ReleaseOwner(WindowId);
        _editor = null;
        Navigate(MainViewMode.Start);
    }

    /// <summary>
    /// Ends the reservation (after a commit to the start page or a rollback). The returned task reports what was deferred
    /// meanwhile (recovery errors, an entered fault); callers await it only after the workspace is fully released.
    /// </summary>
    internal Task ReleaseLeaveReservation()
    {
        if (!_leaveReserved) return Task.CompletedTask;
        _leaveReserved = false;
        Session?.SetBusy(IsBusy);
        NotifySession();
        return ReportPendingRecoveryAsync();
    }

    /// <summary>
    /// Reserves an internal close (a dedicated workspace whose setup was cancelled or whose Recovery Center was left) at the
    /// moment it is decided, also from within the operation that decided it: like a close reservation it stops requests,
    /// waiting opens and local commands. Released with <see cref="ReleaseLeaveReservation"/>. False if already reserved.
    /// </summary>
    internal bool ReserveInternalClose()
    {
        if (_disposed || _leaveReserved) return false;
        _leaveReserved = true;
        NotifySession();
        return true;
    }

    /// <summary>No operation runs in the workspace apart from a close reservation of its own.</summary>
    internal bool IsOperationIdle => !IsOperationBusy && _recoveryCenter?.IsBusy != true
        && !(Mode == MainViewMode.ProjectSetup && (Setup.Definition?.IsBusy == true
            || Setup.CurrentStep is MaterialSelectionViewModel { IsActionBusy: true } or MaterialSelectionViewModel { IsEditing: true }));

    /// <summary>
    /// Runs the close transaction of this workspace without a window: Save/Discard/Cancel, recovery preparation and commit.
    /// True once committed; the window of a real workspace closes through its owner instead.
    /// </summary>
    public Task<bool> RequestCloseAsync() => WorkspaceCloseTransaction.RunAsync(this, closeWindow: true);

    /// <summary>A projectless workspace without any recovery slot, fault or running work: it can close without a transaction.</summary>
    internal bool IsSafelyEmpty => !_disposed && Session is null && !IsWorkspaceBusy && Mode != MainViewMode.ProjectSetup
        && (_recovery?.IsEmpty ?? true);

    // ---- RecoveryFaulted --------------------------------------------------------------------------------------

    /// <summary>The recovery protection was lifted by an aborted close and could not be restored (§60).</summary>
    public bool IsRecoveryFaulted => _recovery is { Fault: not RecoveryFault.None };

    /// <summary>The workspace content takes input: nothing runs and no faulted recovery forbids changes.</summary>
    public bool IsWorkspaceEditable => !IsWorkspaceBusy && !IsRecoveryFaulted;

    public bool CanRestoreRecoveryProtection => IsRecoveryFaulted && !IsWorkspaceBusy;
    public AsyncActionCommand RestoreRecoveryProtectionCommand { get; }

    /// <summary>"Save recovery data again": the remediation of a faulted recovery; false while the storage still fails.</summary>
    internal Task<bool> RestoreRecoveryProtectionAsync() => !CanRestoreRecoveryProtection ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        try { await _recovery!.RestoreProtectionAsync(Session, CurrentDraft); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryError, e);
            return false;
        }
        return true;
    });

    private void RecoveryFaultChanged()
    {
        if (_disposed) return;
        ApplyFreeze();
        if (IsRecoveryFaulted) _pendingFaultReport = true;
        NotifySession();
    }

    /// <summary>Project mutations stop while settings are committed or while the project recovery is faulted.</summary>
    private void ApplyFreeze() => Session?.SetFrozen(_settingsSaving || IsRecoveryFaulted);

    private async Task ReportPendingRecoveryAsync()
    {
        if (_pendingRecoveryError is { } recoveryError)
        {
            _pendingRecoveryError = null;
            await ReportRecoveryErrorAsync(recoveryError);
        }
        if (_pendingFaultReport && !IsBusy)
        {
            _pendingFaultReport = false;
            if (_recovery?.FaultError is { } fault)
                await RunOperationAsync(async () => { await ShowErrorAsync(Strings.RecoveryFaultedError, fault); return true; });
        }
    }
}
