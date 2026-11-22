namespace SpanDraft.Desktop.Activation;

public enum InstanceOutcomeKind { Primary, Forwarded, Rejected, CommunicationFailure }

/// <summary>Result of the election: this process is the Primary, or its request has been handed over or finally failed.</summary>
public sealed record InstanceOutcome(InstanceOutcomeKind Kind, PrimaryInstance? Primary = null);

public sealed record InstanceCoordinatorOptions
{
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>How long a lock holder may stay unreachable before it counts as hung; Retry answers never count.</summary>
    public TimeSpan BootstrapTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>Primary/Secondary election and the Secondary-side request ownership (Accepted/Retry/Rejected).</summary>
public static class InstanceCoordinator
{
    public static async Task<InstanceOutcome> ResolveAsync(DataScope scope, LaunchRequest request,
        InstanceCoordinatorOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var backoff = options.InitialBackoff;
        DateTimeOffset? unreachableSince = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Every attempt re-evaluates the election: a freed lock turns this request into the initial one.
            if (PrimaryInstance.TryAcquire(scope) is { } primary) return new(InstanceOutcomeKind.Primary, primary);
            var metadata = InstanceMetadata.TryRead(scope);
            var status = metadata is null ? null
                : await BrokerClient.SendAsync(metadata, request, options.ConnectTimeout, cancellationToken);
            switch (status)
            {
                case BrokerStatus.Accepted: return new(InstanceOutcomeKind.Forwarded);
                case BrokerStatus.Rejected: return new(InstanceOutcomeKind.Rejected);
                case BrokerStatus.Retry: unreachableSince = null; break;
                default:
                    // Missing metadata or endpoint right after the election is a bootstrap race, not an error yet.
                    unreachableSince ??= options.Time.GetUtcNow();
                    if (options.Time.GetUtcNow() - unreachableSince > options.BootstrapTimeout)
                        return new(InstanceOutcomeKind.CommunicationFailure);
                    break;
            }
            await Task.Delay(backoff, options.Time, cancellationToken);
            backoff = TimeSpan.FromTicks(Math.Min(options.MaxBackoff.Ticks, backoff.Ticks * 3 / 2));
        }
    }
}
