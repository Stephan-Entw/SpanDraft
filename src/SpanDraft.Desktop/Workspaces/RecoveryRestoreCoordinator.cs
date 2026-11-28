using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>
/// The restore transaction of one recovery candidate: claim it, find or create the target workspace, restore it there and
/// only then commit the claim. Any failure before the commit aborts the claim, so the candidate stays offered, and closes
/// a workspace that was created for it. It holds no persistence or priority logic (the catalog) and creates no windows
/// itself (the workspace manager).
/// </summary>
internal sealed class RecoveryRestoreCoordinator(RecoveryCatalog catalog, WorkspaceWindowManager manager)
{
    /// <summary>
    /// Restores a candidate; false if it is not (any more) available. A failed restore throws, after the claim was aborted
    /// and a workspace created for it was closed.
    /// </summary>
    internal async Task<bool> RestoreAsync(Guid candidateId, WorkspaceWindowHandle source)
    {
        if (!manager.CanAcquireWorkspace) return false;
        var claim = await catalog.TryBeginClaimAsync(candidateId);
        if (claim is null) return false;
        WorkspaceWindowHandle? created = null;
        bool restored = false;
        try
        {
            // The last undecided candidate may take over the Recovery Center's own start workspace.
            var acquired = await manager.AcquireRestoreTargetAsync(source, lastUndecided: !catalog.HasUndecided);
            if (acquired is not { } destination) return false;
            var (target, isNew) = destination;
            if (isNew) created = target;
            if (!manager.CanUseTarget(target)) return false;
            var model = target.Model!;
            model.LeaveRecoveryCenter();
            var candidate = claim.Candidate;
            if (candidate.Kind == RecoverySlotKind.Project)
                model.RestoreProject(candidate.Project!, claim.Slot, claim.HoldsPath ? candidate.CandidateId : null);
            else
                model.RestoreSetup(candidate.Setup!, claim.Slot);
            restored = true;
            await claim.CommitAsync();
            if (manager.CanUseTarget(target)) manager.Activate(target.WindowId);
            return true;
        }
        finally
        {
            await claim.AbortAsync();
            if (!restored && created is not null) manager.CleanupUnusedWorkspace(created);
        }
    }
}
