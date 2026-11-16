namespace SpanDraft.Desktop.Diagnostics;

public enum InteractionAction
{
    PointForce, PointMoment, DistributedLoad, Support, SpringSupport, MomentRelease,
    SelfWeight, BeamGeometry, ProjectSetup, AnnotationOffset, SelfWeightVisibility, Undo, Redo
}

public sealed record InteractionPerformanceEntry(DateTimeOffset StartedAtUtc, InteractionAction Action,
    TimeSpan Preparation, TimeSpan Analysis, TimeSpan Presentation, TimeSpan UI, TimeSpan Total);

/// <summary>Returns a timestamp from the supplied monotonic clock, or null if no frame can be observed.</summary>
public interface IInteractionFrameProbe
{
    Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken);
}

/// <summary>One UI-thread interaction context; completed entries are safe to read from any thread.</summary>
public sealed class InteractionPerformance(IInteractionFrameProbe frames, TimeProvider? clock = null)
{
    public const int Capacity = 50;
    // A frame that has not rendered by now no longer belongs to the interaction (occluded or stalled window).
    internal static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly InteractionPerformanceEntry?[] _entries = new InteractionPerformanceEntry[Capacity];
    private readonly HashSet<Measurement> _pending = [];
    private int _next, _count;
    private Measurement? _current;

    public IReadOnlyList<InteractionPerformanceEntry> GetEntries()
    {
        lock (_gate)
        {
            var result = new InteractionPerformanceEntry[_count];
            for (int i = 0; i < result.Length; i++)
                result[i] = _entries[(_next - _count + i + Capacity) % Capacity]!;
            return result;
        }
    }

    /// <summary>Begins a measurement on an optional recorder; without one the returned scope does nothing.</summary>
    public static Scope Measure(InteractionPerformance? owner, InteractionAction action) =>
        owner is null ? default : owner.Begin(action);

    public Scope Begin(InteractionAction action)
    {
        // Nested product calls belong to the outer confirmation, including its final notifications.
        if (_current is not null) return default;
        var measurement = new Measurement(action, _clock.GetTimestamp(), _clock.GetUtcNow());
        _current = measurement;
        return new(this, measurement);
    }

    internal void AnalysisStarting()
    {
        if (_current is { } measurement) measurement.AnalysisStart = _clock.GetTimestamp();
    }

    internal void AnalysisFinished()
    {
        if (_current is { } measurement) measurement.AnalysisEnd = _clock.GetTimestamp();
    }

    internal void PresentationStarting()
    {
        if (_current is not { } measurement) return;
        measurement.AnalysisStart = measurement.AnalysisEnd = _clock.GetTimestamp();
    }

    internal void Committed()
    {
        if (_current is { } measurement) measurement.Committed = true;
    }

    public void CancelPending()
    {
        Measurement[] pending;
        lock (_gate) { pending = _pending.ToArray(); _pending.Clear(); }
        foreach (var measurement in pending)
        {
            try { measurement.Cancellation!.Cancel(); }
            catch (ObjectDisposedException) { /* A concurrent frame completion already released it. */ }
        }
    }

    private void End(Measurement measurement)
    {
        if (!ReferenceEquals(_current, measurement)) return;
        _current = null;
        if (!measurement.Committed || measurement.AnalysisEnd is null) return;
        measurement.UIStart = _clock.GetTimestamp();
        measurement.Cancellation = new(FrameTimeout, _clock);
        lock (_gate) _pending.Add(measurement);
        _ = FinishAsync(measurement);
    }

    private async Task FinishAsync(Measurement measurement)
    {
        try
        {
            var end = await frames.ObserveNextFrameAsync(_clock, measurement.Cancellation!.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_pending.Remove(measurement) || end is null) return;
                // Convert cumulative boundaries once so the phases add up exactly to Total.
                TimeSpan Elapsed(long timestamp) => _clock.GetElapsedTime(measurement.Start, timestamp);
                var preparation = Elapsed(measurement.AnalysisStart!.Value);
                var analyzed = Elapsed(measurement.AnalysisEnd!.Value);
                var presented = Elapsed(measurement.UIStart);
                var total = Elapsed(end.Value);
                _entries[_next] = new(measurement.Utc, measurement.Action, preparation, analyzed - preparation,
                    presented - analyzed, total - presented, total);
                _next = (_next + 1) % Capacity;
                _count = Math.Min(_count + 1, Capacity);
            }
        }
        catch (Exception) { /* Diagnostics must never fail a product command or the dispatcher. */ }
        finally
        {
            lock (_gate) _pending.Remove(measurement);
            measurement.Cancellation!.Dispose();
        }
    }

    internal sealed class Measurement(InteractionAction action, long start, DateTimeOffset utc)
    {
        public readonly InteractionAction Action = action;
        public readonly long Start = start;
        public readonly DateTimeOffset Utc = utc;
        public long? AnalysisStart, AnalysisEnd;
        public long UIStart;
        public bool Committed;
        public CancellationTokenSource? Cancellation;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly InteractionPerformance? _owner;
        private readonly Measurement? _measurement;
        internal Scope(InteractionPerformance owner, Measurement measurement) => (_owner, _measurement) = (owner, measurement);
        public void Dispose() { if (_measurement is not null) _owner!.End(_measurement); }
    }
}
