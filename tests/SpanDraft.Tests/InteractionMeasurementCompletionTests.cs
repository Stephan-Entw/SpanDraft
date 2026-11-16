using SpanDraft.Desktop.Diagnostics;
using Xunit;

namespace SpanDraft.Tests;

public sealed class InteractionMeasurementCompletionTests
{
    private sealed class Frames : IInteractionFrameProbe
    {
        public readonly TaskCompletionSource<long?> Frame = new();
        public int Calls;
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
        { Calls++; return Frame.Task.WaitAsync(cancellationToken); }
    }

    [Theory]
    [InlineData("rendered")] [InlineData("missing")] [InlineData("faulted")] [InlineData("cancelled")]
    public async Task EveryCommittedObservationHasExactlyOneTerminalNotification(string outcome)
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var terminal = new TaskCompletionSource<InteractionPerformanceEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int notifications = 0, recorded = 0;
        recorder.EntryRecorded += () => recorded++;
        recorder.MeasurementFinished += entry => { notifications++; terminal.TrySetResult(entry); };
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        Assert.False(terminal.Task.IsCompleted);
        switch (outcome)
        {
            case "rendered": frames.Frame.SetResult(TimeProvider.System.GetTimestamp()); break;
            case "missing": frames.Frame.SetResult(null); break;
            case "faulted": frames.Frame.SetException(new InvalidOperationException("Unavailable frame.")); break;
            default: recorder.CancelPending(); break;
        }
        var result = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(1, notifications);
        Assert.Equal(outcome == "rendered" ? 1 : 0, recorded);
        if (outcome == "rendered") Assert.Same(Assert.Single(recorder.GetEntries()), result);
        else Assert.Null(result);
        if (outcome == "cancelled") frames.Frame.SetResult(TimeProvider.System.GetTimestamp());
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void RejectedOrUncommittedScopesDoNotAnnounceACompletedMeasurement()
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        int finished = 0;
        recorder.MeasurementFinished += _ => finished++;
        using (recorder.Begin(InteractionAction.PointForce)) { }
        Assert.Equal(0, frames.Calls); Assert.Equal(0, finished);
    }

    [Fact]
    public async Task AnExistingDiagnosticListenerCannotSuppressTheTerminalNotification()
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var terminal = new TaskCompletionSource<InteractionPerformanceEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.EntryRecorded += () => throw new InvalidOperationException("Listener failed.");
        recorder.MeasurementFinished += entry => terminal.SetResult(entry);
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        frames.Frame.SetResult(TimeProvider.System.GetTimestamp());
        Assert.NotNull(await terminal.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }
}
