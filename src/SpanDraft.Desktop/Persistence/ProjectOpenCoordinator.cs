using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

internal enum ProjectOpenStatus { Opened, AlreadyOpen, Cancelled, Missing, Failed, RecoveryPending }
internal sealed record ProjectOpenResult(ProjectOpenStatus Status, WindowId? Owner = null, Exception? Error = null)
{
    internal static ProjectOpenResult Cancelled { get; } = new(ProjectOpenStatus.Cancelled);
}

internal interface IProjectOpenWorkspace
{
    WindowId WindowId { get; }
    Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave);
    Task<bool> ActivateProjectAsync(ProjectState state, string path);
    void ActivateExisting(WindowId owner);
    void ActivateRecovery(Guid candidate);
}

internal sealed class ProjectOpenCoordinator(IProjectFileStore files, IProjectIdentityResolver identities, ProjectPathRegistry registry)
{
    internal async Task<ProjectOpenResult> OpenAsync(string path, IProjectOpenWorkspace workspace)
    {
        var identity = identities.Resolve(Path.GetFullPath(path));
        bool afterSave = false;
        while (true)
        {
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
                    if (result.Owner is { } owner) workspace.ActivateExisting(owner);
                    return result;
                case ProjectPathState.Open:
                    workspace.ActivateExisting(decision.Owner!.Value);
                    return new(ProjectOpenStatus.AlreadyOpen, Owner: decision.Owner);
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
        try { return await workspace.RunOpenAsync(RunTransactionAsync, afterSave); }
        finally { registry.TryReleaseOpen(token, outcome); }

        async Task<ProjectOpenResult> RunTransactionAsync()
        {
            try
            {
                string path = token.Identity.CanonicalPath;
                var bytes = await files.ReadAsync(path);
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
