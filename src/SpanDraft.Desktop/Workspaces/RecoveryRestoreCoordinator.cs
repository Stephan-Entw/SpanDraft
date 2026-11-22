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
        var claim = await catalog.TryBeginClaimAsync(candidateId);
        if (claim is null) return false;
        WorkspaceWindowHandle? created = null;
        try
        {
            // The last undecided candidate may take over the Recovery Center's own start workspace.
            var (target, isNew) = await manager.AcquireRestoreTargetAsync(source, lastUndecided: !catalog.HasUndecided);
            if (isNew) created = target;
            var model = target.Model!;
            model.LeaveRecoveryCenter();
            var candidate = claim.Candidate;
            if (candidate.Kind == RecoverySlotKind.Project)
                model.RestoreProject(candidate.Project!, claim.Slot, claim.HoldsPath ? candidate.CandidateId : null);
            else
                model.RestoreSetup(candidate.Setup!, claim.Slot);
            await claim.CommitAsync();
            manager.Activate(target.WindowId);
            return true;
        }
        catch
        {
            await claim.AbortAsync();
            if (created is not null && created.Model?.HasProject != true) manager.CloseInternal(created);
            throw;
        }
    }
}
