using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DiagnosticsViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 12, 3, 4, TimeSpan.Zero);
    private static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
    private static readonly DiagnosticsEnvironment Environment = new("1.2.3", "TestOS 1", "Arm64", ".NET 10.0.0");

    private sealed class Clock : TimeProvider
    {
        public long Timestamp;
        public DateTimeOffset Utc = Start;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed class Frames : IInteractionFrameProbe
    {
        public readonly List<TaskCompletionSource<long?>> Requests = [];
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<long?>();
            lock (Requests) Requests.Add(completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    // Runs refreshes only when the test says so, like a UI thread that is busy.
    private sealed class Ui
    {
        public readonly ConcurrentQueue<Action> Queue = new();
        public int Posted;
        public void Post(Action action) { Interlocked.Increment(ref Posted); Queue.Enqueue(action); }
        public void Drain() { while (Queue.TryDequeue(out var action)) action(); }
    }

    // The culture's own time format (ICU uses a narrow no-break space before AM/PM) at UTC+2.
    private static string LocalTime(DateTimeOffset utc) =>
        utc.ToOffset(TimeSpan.FromHours(2)).DateTime.ToString("T", System.Globalization.CultureInfo.CurrentCulture);

    private static TimeSpan Ms(double value) => TimeSpan.FromMilliseconds(value);

    /// <summary>Records one finished entry with the given cumulative boundaries (ms) and renders its frame.</summary>
    private static void Record(InteractionPerformance recorder, Clock clock, Frames frames, InteractionAction action,
        double? analysisStart = null, double? analysisEnd = null, double committed = 15, double frame = 26)
    {
        clock.Timestamp = 0;
        using (recorder.Begin(action))
        {
            if (analysisStart is null) recorder.PresentationStarting();
            else
            {
                clock.Timestamp = Ms(analysisStart.Value).Ticks; recorder.AnalysisStarting();
                clock.Timestamp = Ms(analysisEnd!.Value).Ticks; recorder.AnalysisFinished();
            }
            clock.Timestamp = Ms(committed).Ticks; recorder.Committed();
        }
        clock.Timestamp = Ms(frame).Ticks;
        frames.Requests[^1].SetResult(clock.Timestamp);
    }

    [Fact]
    public void EnvironmentDescribesTheRunningApplicationWithoutProjectData()
    {
        var current = DiagnosticsEnvironment.Current();
        Assert.Equal(ApplicationMetadata.Version, current.Version);
        Assert.Equal(RuntimeInformation.OSDescription, current.OperatingSystem);
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), current.Architecture);
        Assert.Equal(RuntimeInformation.FrameworkDescription, current.Runtime);
        Assert.Equal(DiagnosticsEnvironment.Current(), new DiagnosticsViewModel(null).Environment);
        Assert.Equal(new[] { "Action", "Analysis", "Preparation", "Presentation", "Time", "Total", "UI" },
            typeof(DiagnosticsRow).GetProperties().Select(p => p.Name).Order());
        Assert.All(typeof(DiagnosticsRow).GetProperties(), p => Assert.Equal(typeof(string), p.PropertyType));
    }

    [Fact]
    public void EmptyHistoryAndMissingRecorderShowNoRowsAndStayNormalByDefault()
    {
        var ui = new Ui();
        foreach (var recorder in new InteractionPerformance?[] { new(new Frames()), null })
        {
            using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
            Assert.Empty(model.Rows); Assert.True(model.IsEmpty); Assert.False(model.HasEntries);
            Assert.False(model.IsAdvanced); Assert.Null(model.VisibleRows);
            model.IsAdvanced = true;
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DiagnosticsRow>>(model.VisibleRows));
        }
        Assert.Equal(0, ui.Posted);
    }

    [Fact]
    public void ExistingEntriesAreShownNewestFirstWithAllPhasesAndLocalTime()
    {
        using var culture = new UiCultureScope("en-US");
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        clock.Utc = Start;
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.PresentationStarting(); recorder.Committed(); }
        clock.Utc = Start.AddSeconds(90);
        Record(recorder, clock, frames, InteractionAction.DistributedLoad, 3, 8, 15, 26);
        // The first frame stays pending; only the completed entry is shown.
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, new Ui().Post);
        var row = Assert.Single(model.Rows);
        Assert.Equal(new DiagnosticsRow(LocalTime(Start.AddSeconds(90)), "Distributed load", "3.0", "5.0", "7.0", "11.0", "26.0"), row);
        frames.Requests[0].SetResult(Ms(40).Ticks);
        using var later = new DiagnosticsViewModel(recorder, Environment, Plus2, new Ui().Post);
        // Newest first means last recorded first: the first interaction finished its frame last.
        Assert.Equal(new[] { "Point force", "Distributed load" }, later.Rows.Select(r => r.Action));
        Assert.Equal(new[] { LocalTime(Start), LocalTime(Start.AddSeconds(90)) }, later.Rows.Select(r => r.Time));
    }

    [Theory]
    [InlineData("en-US", "0.0", "26.0")]
    [InlineData("de-DE", "0,0", "26,0")]
    public void NonMechanicalActionsShowAnalysisAsZeroAndPhasesAddUp(string language, string zero, string total)
    {
        using var culture = new UiCultureScope(language);
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        Record(recorder, clock, frames, InteractionAction.AnnotationOffset, committed: 15, frame: 26);
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, new Ui().Post);
        var row = Assert.Single(model.Rows);
        Assert.Equal(zero, row.Analysis);
        Assert.Equal(total, row.Total);
        var entry = Assert.Single(recorder.GetEntries());
        Assert.Equal(TimeSpan.Zero, entry.Analysis);
    }

    [Theory]
    [InlineData("en-US", "Point force", "Undo")]
    [InlineData("de-DE", "Punktkraft", "Rückgängig")]
    public void EveryActionHasALocalizedNeutralNameInsteadOfTheInternalOne(string language, string pointForce, string undo)
    {
        using var culture = new UiCultureScope(language);
        var names = Enum.GetValues<InteractionAction>().Select(DiagnosticsViewModel.ActionName).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(Enum.GetValues<InteractionAction>(), action =>
        {
            string name = DiagnosticsViewModel.ActionName(action);
            Assert.False(string.IsNullOrWhiteSpace(name));
            if (language == "de-DE") Assert.NotEqual(action.ToString(), name);
        });
        Assert.Equal(pointForce, DiagnosticsViewModel.ActionName(InteractionAction.PointForce));
        Assert.Equal(undo, DiagnosticsViewModel.ActionName(InteractionAction.Undo));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticsViewModel.ActionName((InteractionAction)999));
    }

    [Fact]
    public void ExtendedModeOnlyHandsTheRowsToTheViewWhileItIsOn()
    {
        using var culture = new UiCultureScope("en-US");
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        Record(recorder, clock, frames, InteractionAction.Support);
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, new Ui().Post);
        var changed = new List<string?>(); model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Null(model.VisibleRows);
        model.IsAdvanced = true;
        Assert.Same(model.Rows, model.VisibleRows);
        model.IsAdvanced = false;
        Assert.Null(model.VisibleRows);
        Assert.Contains(nameof(DiagnosticsViewModel.VisibleRows), changed);
    }

    [Fact]
    public void NewCompletedEntryRefreshesThroughTheUiOnlyAndIntermediateStatesDoNot()
    {
        using var culture = new UiCultureScope("en-US");
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var ui = new Ui();
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
        var changed = new List<string?>(); model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        using (recorder.Begin(InteractionAction.Support))
        { clock.Timestamp = 1; recorder.AnalysisStarting(); recorder.AnalysisFinished(); recorder.Committed(); }
        Assert.Equal(0, ui.Posted); // Still waiting for the frame.
        recorder.CancelPending();
        using (recorder.Begin(InteractionAction.Support)) { recorder.PresentationStarting(); } // Not committed.
        Assert.Equal(0, ui.Posted); Assert.Empty(model.Rows);
        Record(recorder, clock, frames, InteractionAction.Redo);
        Assert.Equal(1, ui.Posted);
        Assert.Empty(model.Rows); // Nothing changes on the notifying thread.
        ui.Drain();
        Assert.Equal("Redo", Assert.Single(model.Rows).Action);
        Assert.True(model.HasEntries); Assert.False(model.IsEmpty);
        Assert.Contains(nameof(DiagnosticsViewModel.Rows), changed);
    }

    [Fact]
    public void EntriesRecordedWhileARefreshIsQueuedAreCoalescedWithoutLosingTheLastOne()
    {
        using var culture = new UiCultureScope("en-US");
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var ui = new Ui();
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
        for (int i = 0; i < 3; i++) Record(recorder, clock, frames, InteractionAction.Undo);
        Assert.Equal(1, ui.Posted);
        ui.Drain();
        Assert.Equal(3, model.Rows.Count);
        Record(recorder, clock, frames, InteractionAction.Redo);
        Assert.Equal(2, ui.Posted);
        ui.Drain();
        Assert.Equal(4, model.Rows.Count); Assert.Equal("Redo", model.Rows[0].Action);
    }

    [Fact]
    public void EntryRecordedWhileTheViewIsBeingUpdatedQueuesAnotherRefresh()
    {
        using var culture = new UiCultureScope("en-US");
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var ui = new Ui();
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
        bool recordedDuringUpdate = false;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DiagnosticsViewModel.Rows) || recordedDuringUpdate) return;
            recordedDuringUpdate = true;
            Record(recorder, clock, frames, InteractionAction.Redo);
        };
        Record(recorder, clock, frames, InteractionAction.Undo);
        ui.Drain(); // Drains the follow-up refresh as well.
        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(2, ui.Posted);
    }

    [Fact]
    public async Task EntriesFromBackgroundThreadsReachTheUiAndNoneIsLostWhileTheViewIsOpening()
    {
        for (int round = 0; round < 100; round++)
        {
            var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
            for (int i = 0; i < 8; i++)
            {
                using var scope = recorder.Begin(InteractionAction.AnnotationOffset);
                recorder.PresentationStarting(); recorder.Committed();
            }
            var ui = new Ui();
            var completion = Task.Run(() => Parallel.ForEach(frames.Requests, request => request.SetResult(1)));
            using var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
            await completion;
            ui.Drain();
            Assert.Equal(8, model.Rows.Count);
            Assert.True(ui.Posted <= 8);
        }
    }

    [Fact]
    public void DisposedViewIgnoresQueuedAndLaterEntriesAndNoListenerRemains()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var ui = new Ui();
        var model = new DiagnosticsViewModel(recorder, Environment, Plus2, ui.Post);
        Record(recorder, clock, frames, InteractionAction.Undo);
        Assert.Equal(1, ui.Posted);
        model.Dispose(); model.Dispose();
        ui.Drain();
        Assert.Empty(model.Rows); // The refresh queued before closing is a no-op.
        Record(recorder, clock, frames, InteractionAction.Redo);
        Assert.Equal(1, ui.Posted);
        Assert.Equal(2, recorder.GetEntries().Count);
    }

    [Fact]
    public void FailingUiQueueDoesNotBlockLaterRefreshes()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var ui = new Ui(); bool fail = true;
        using var model = new DiagnosticsViewModel(recorder, Environment, Plus2,
            action => { if (fail) throw new InvalidOperationException("Dispatcher shut down."); ui.Post(action); });
        Record(recorder, clock, frames, InteractionAction.Undo);
        fail = false;
        Record(recorder, clock, frames, InteractionAction.Redo);
        ui.Drain();
        Assert.Equal(2, model.Rows.Count);
    }
}
