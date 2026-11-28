using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

/// <remarks>
/// <see cref="Unavailable"/> means the requesting workspace cannot process the open (any more): it is closing, closed or
/// the application shuts down. Unlike <see cref="Cancelled"/> (a decision of the user) it leaves the request unhandled.
/// </remarks>
internal enum ProjectOpenStatus { Opened, AlreadyOpen, Cancelled, Missing, Failed, RecoveryPending, Unavailable }
internal sealed record ProjectOpenResult(ProjectOpenStatus Status, WindowId? Owner = null, Exception? Error = null,
    string? CanonicalPath = null)
{
    internal static ProjectOpenResult Cancelled { get; } = new(ProjectOpenStatus.Cancelled);
    internal static ProjectOpenResult Unavailable { get; } = new(ProjectOpenStatus.Unavailable);
}

internal interface IProjectOpenRequest
{
    /// <summary>The request still has an owner and the application can process it.</summary>
    bool CanContinue { get; }
    Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave);
    Task<ProjectOpenStatus> ActivateProjectAsync(ProjectState state, string path, Action<WindowId, Action> commit);
    bool ActivateExisting(WindowId owner);
    Task<bool> ActivateRecoveryAsync(Guid candidate);
}

internal sealed class ProjectOpenCoordinator(IProjectFileStore files, IProjectIdentityResolver identities, ProjectPathRegistry registry)
{
    internal async Task<ProjectOpenResult> OpenAsync(string path, IProjectOpenRequest request)
    {
        if (!request.CanContinue) return ProjectOpenResult.Unavailable;
        var identity = identities.Resolve(Path.GetFullPath(path));
        await registry.SettleRecoveryAsync();
        if (!request.CanContinue) return ProjectOpenResult.Unavailable;
        bool afterSave = false;
        // The result of a successful flight this open joined. Its owner is not activated directly: the next turn of the loop
        // decides on the current ownership (the owner may be closing, may have given the path up or handed it to a
        // recovery successor meanwhile). Only the loop arbitrates, so every reservation it takes is run by RunFlightAsync.
        ProjectOpenResult? joined = null;
        while (true)
        {
            if (!request.CanContinue) return ProjectOpenResult.Unavailable;
            var decision = registry.ArbitrateOpen(identity);
            switch (decision.State)
            {
                case ProjectPathState.Free:
                    return await RunFlightAsync(decision.Reservation!, request, afterSave);
                case ProjectPathState.RecoveryPending:
                    return await request.ActivateRecoveryAsync(decision.RecoveryCandidate!.Value)
                        ? new(ProjectOpenStatus.RecoveryPending) : ProjectOpenResult.Unavailable;
                case ProjectPathState.Opening:
                    var result = await decision.Flight!;
                    // Whatever the flight ended with, it is no result for a workspace that went away while it waited.
                    if (!request.CanContinue) return ProjectOpenResult.Unavailable;
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
                    if (!request.ActivateExisting(decision.Owner!.Value)) return ProjectOpenResult.Unavailable;
                    // A joined flight whose owner still holds the path keeps its single-flight result.
                    return joined is { Owner: { } flightOwner } && flightOwner == decision.Owner
                        ? joined : new(ProjectOpenStatus.AlreadyOpen, Owner: decision.Owner, CanonicalPath: identity.CanonicalPath);
                case ProjectPathState.SaveTargetReserved:
                    await decision.SaveCompletion!;
                    afterSave = true;
                    break;
            }
        }
    }

    private async Task<ProjectOpenResult> RunFlightAsync(OpenReservation token, IProjectOpenRequest request, bool afterSave)
    {
        var returned = ProjectOpenResult.Unavailable;
        ProjectOpenResult? opened = null;
        try { return returned = await request.RunOpenAsync(RunTransactionAsync, afterSave); }
        // A failed flight is shared only after its result handler has taken responsibility for the outcome.
        finally { registry.TryReleaseOpen(token, returned); }

        async Task<ProjectOpenResult> RunTransactionAsync()
        {
            try
            {
                string path = token.Identity.CanonicalPath;
                var bytes = await files.ReadAsync(path);
                if (!request.CanContinue) return ProjectOpenResult.Unavailable;
                if (bytes is null) return new(ProjectOpenStatus.Missing);
                var state = ProjectFileCodec.Deserialize(bytes);
                var status = await request.ActivateProjectAsync(state, path, Commit);
                return opened ?? new(status);
            }
            catch (OperationCanceledException) { return request.CanContinue ? ProjectOpenResult.Cancelled : ProjectOpenResult.Unavailable; }
            catch (Exception error) { return opened ?? new(ProjectOpenStatus.Failed, Error: error); }
        }

        void Commit(WindowId owner, Action publish)
        {
            var flight = registry.CommitOpenOwnership(token, owner)
                ?? throw new InvalidOperationException("The open reservation lost ownership during activation.");
            try
            {
                publish();
                opened = new(ProjectOpenStatus.Opened, Owner: owner, CanonicalPath: token.Identity.CanonicalPath);
                flight.SetResult(opened);
            }
            catch (Exception error)
            {
                flight.TrySetException(error);
                throw;
            }
        }
    }
}
