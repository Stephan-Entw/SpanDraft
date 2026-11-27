using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

/// <remarks>
/// <see cref="Unavailable"/> means the requesting workspace cannot process the open (any more): it is closing, closed or
/// the application shuts down. Unlike <see cref="Cancelled"/> (a decision of the user) it leaves the request unhandled.
/// </remarks>
internal enum ProjectOpenStatus { Opened, AlreadyOpen, Cancelled, Missing, Failed, RecoveryPending, Unavailable }
internal sealed record ProjectOpenResult(ProjectOpenStatus Status, WindowId? Owner = null, Exception? Error = null)
{
    internal static ProjectOpenResult Cancelled { get; } = new(ProjectOpenStatus.Cancelled);
    internal static ProjectOpenResult Unavailable { get; } = new(ProjectOpenStatus.Unavailable);
}

internal interface IProjectOpenWorkspace
{
    WindowId WindowId { get; }
    /// <summary>
    /// The workspace can still process this open: not disposed, no leave or close running, routable, the application not
    /// shutting down. Checked after every wait, so a waiting open never changes a workspace that went away meanwhile.
    /// </summary>
    bool CanContinue { get; }
    Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave);
    Task<bool> ActivateProjectAsync(ProjectState state, string path);
    void ActivateExisting(WindowId owner);
    void ActivateRecovery(Guid candidate);
}

internal sealed class ProjectOpenCoordinator(IProjectFileStore files, IProjectIdentityResolver identities, ProjectPathRegistry registry)
{
    internal async Task<ProjectOpenResult> OpenAsync(string path, IProjectOpenWorkspace workspace)
    {
        await registry.SettleRecoveryAsync();
        if (!workspace.CanContinue) return ProjectOpenResult.Unavailable;
        var identity = identities.Resolve(Path.GetFullPath(path));
        bool afterSave = false;
        // The result of a successful flight this open joined. Its owner is not activated directly: the next turn of the loop
        // decides on the current ownership (the owner may be closing, may have given the path up or handed it to a
        // recovery successor meanwhile). Only the loop arbitrates, so every reservation it takes is run by RunFlightAsync.
        ProjectOpenResult? joined = null;
        while (true)
        {
            if (!workspace.CanContinue) return ProjectOpenResult.Unavailable;
            var decision = registry.ArbitrateOpen(identity);
            switch (decision.State)
            {
                case ProjectPathState.Free:
                    return await RunFlightAsync(decision.Reservation!, workspace, afterSave);
                case ProjectPathState.RecoveryPending:
                    workspace.ActivateRecovery(decision.RecoveryCandidate!.Value);
                    return new(ProjectOpenStatus.RecoveryPending);
                case ProjectPathState.Opening:
                    var result = await decision.Flight!;
                    // Whatever the flight ended with, it is no result for a workspace that went away while it waited.
                    if (!workspace.CanContinue) return ProjectOpenResult.Unavailable;
                    // Unavailable only says that the opening workspace could not take the open: decide again for this one.
                    if (result.Status == ProjectOpenStatus.Unavailable) break;
                    if (result.Owner is null) return result;
                    joined = result;
                    break;
                case ProjectPathState.Open when decision.OwnerRelease is { } release:
                    // Its owner is closing: no AlreadyOpen for a workspace that may be gone; decide again afterwards.
                    await release;
                    break;
                case ProjectPathState.Open:
                    workspace.ActivateExisting(decision.Owner!.Value);
                    // A joined flight whose owner still holds the path keeps its single-flight result.
                    return joined is { Owner: { } flightOwner } && flightOwner == decision.Owner
                        ? joined : new(ProjectOpenStatus.AlreadyOpen, Owner: decision.Owner);
                case ProjectPathState.SaveTargetReserved:
                    await decision.SaveCompletion!;
                    afterSave = true;
                    break;
            }
        }
    }

    private async Task<ProjectOpenResult> RunFlightAsync(OpenReservation token, IProjectOpenWorkspace workspace, bool afterSave)
    {
        var outcome = ProjectOpenResult.Cancelled;
        var returned = ProjectOpenResult.Cancelled;
        bool started = false;
        try { return returned = await workspace.RunOpenAsync(RunTransactionAsync, afterSave); }
        // Every participant gets the real result, also when the workspace refused before the transaction began.
        finally { registry.TryReleaseOpen(token, started ? outcome : returned); }

        async Task<ProjectOpenResult> RunTransactionAsync()
        {
            started = true;
            try
            {
                string path = token.Identity.CanonicalPath;
                var bytes = await files.ReadAsync(path);
                if (!workspace.CanContinue) return outcome = ProjectOpenResult.Unavailable;
                if (bytes is null) return outcome = new(ProjectOpenStatus.Missing);
                var state = ProjectFileCodec.Deserialize(bytes);
                if (!await workspace.ActivateProjectAsync(state, path)) return outcome;
                if (!registry.TryCommitOpen(token, workspace.WindowId))
                    throw new InvalidOperationException("The open reservation lost ownership during activation.");
                return outcome = new(ProjectOpenStatus.Opened, Owner: workspace.WindowId);
            }
            catch (Exception error) { return outcome = new(ProjectOpenStatus.Failed, Error: error); }
            finally { registry.TryReleaseOpen(token, outcome); }
        }
    }
}
