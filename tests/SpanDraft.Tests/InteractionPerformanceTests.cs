using SpanDraft.Analysis;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class InteractionPerformanceTests
{
    private sealed class Clock(long frequency = TimeSpan.TicksPerSecond) : TimeProvider
    {
        public long Timestamp;
        public DateTimeOffset Utc = Now;
        public override long TimestampFrequency => frequency;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow() => Utc;
        public readonly List<ManualTimer> Timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
    }

    // The frame timeout is driven by hand instead of waiting for wall-clock time.
    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        public TimeSpan DueTime = due;
        public bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) { DueTime = dueTime; return true; }
        public void Fire() => callback(state);
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Frames : IInteractionFrameProbe
    {
        public readonly List<TaskCompletionSource<long?>> Requests = [];
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<long?>();
            Requests.Add(completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ImmediateFrames(bool available = true, bool throws = false) : IInteractionFrameProbe
    {
        public int Calls;
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
        {
            Calls++;
            if (throws) throw new InvalidOperationException("Frame observation failed.");
            return Task.FromResult(available ? (long?)clock.GetTimestamp() : null);
        }
    }

    [Fact]
    public void PhasesUseTheSameMonotonicTimelineAndOnlyFinishAtTheFrame()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        using (recorder.Begin(InteractionAction.PointForce))
        {
            clock.Timestamp = 3; recorder.AnalysisStarting();
            clock.Timestamp = 8; recorder.AnalysisFinished();
            clock.Timestamp = 15; recorder.Committed();
        }
        Assert.Empty(recorder.GetEntries());
        clock.Utc = Now.AddDays(-1); // Wall-clock corrections must not affect durations.
        clock.Timestamp = 26; Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        var entry = Assert.Single(recorder.GetEntries());
        Assert.Equal(Now, entry.StartedAtUtc);
        Assert.Equal(InteractionAction.PointForce, entry.Action);
        Assert.Equal(new[] { 3L, 5, 7, 11, 26 },
            new[] { entry.Preparation.Ticks, entry.Analysis.Ticks, entry.Presentation.Ticks, entry.UI.Ticks, entry.Total.Ticks });
    }

    [Fact]
    public void RoundingCumulativeBoundariesKeepsThePhaseSumEqualToTotal()
    {
        var clock = new Clock(3); var recorder = new InteractionPerformance(new ImmediateFrames(), clock);
        using (recorder.Begin(InteractionAction.BeamGeometry))
        {
            clock.Timestamp = 1; recorder.AnalysisStarting();
            clock.Timestamp = 2; recorder.AnalysisFinished();
            clock.Timestamp = 4; recorder.Committed();
        }
        var entry = Assert.Single(recorder.GetEntries());
        Assert.Equal(TimeSpan.FromTicks(13_333_333), entry.Total);
        Assert.Equal(entry.Total, entry.Preparation + entry.Analysis + entry.Presentation + entry.UI);
    }

    [Fact]
    public void RingRetainsTheLatestFiftyInOrderAcrossRepeatedWrapsAndSnapshotsAreIndependent()
    {
        var clock = new Clock(); var recorder = new InteractionPerformance(new ImmediateFrames(), clock);
        IReadOnlyList<InteractionPerformanceEntry>? snapshot = null;
        for (int i = 0; i < 105; i++)
        {
            clock.Utc = Now.AddSeconds(i);
            using (recorder.Begin(InteractionAction.AnnotationOffset))
            { recorder.PresentationStarting(); recorder.Committed(); }
            Assert.Equal(Math.Min(i + 1, 50), recorder.GetEntries().Count);
            if (i == 49) snapshot = recorder.GetEntries();
            if (i == 50) Assert.Equal(Now.AddSeconds(1), recorder.GetEntries()[0].StartedAtUtc);
        }
        Assert.Equal(Enumerable.Range(55, 50).Select(i => Now.AddSeconds(i)), recorder.GetEntries().Select(e => e.StartedAtUtc));
        Assert.Equal(Now, snapshot![0].StartedAtUtc);
    }

    [Fact]
    public async Task AsynchronousFrameCompletionsAndSnapshotsAreThreadSafe()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        for (int i = 0; i < 70; i++)
        {
            clock.Utc = Now.AddSeconds(i);
            using var measurement = recorder.Begin(InteractionAction.AnnotationOffset);
            recorder.PresentationStarting(); recorder.Committed();
        }
        await Task.WhenAll(frames.Requests.Select((request, i) => Task.Run(() =>
        {
            request.SetResult(i + 1);
            Assert.InRange(recorder.GetEntries().Count, 0, 50);
        })));
        var entries = recorder.GetEntries();
        Assert.Equal(50, entries.Count);
        Assert.Equal(50, entries.Select(entry => entry.StartedAtUtc).Distinct().Count());
        Assert.All(entries, entry => Assert.True(entry.Total > TimeSpan.Zero));
    }

    [Fact]
    public void FrameThatNeverRendersIsDiscardedAtTheTimeoutAndCannotCompleteLater()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        var timer = Assert.Single(clock.Timers);
        Assert.Equal(InteractionPerformance.FrameTimeout, timer.DueTime);
        Assert.False(timer.Disposed);
        timer.Fire();
        Assert.True(timer.Disposed);
        clock.Timestamp = 1_000_000_000; // A frame arriving long afterwards must not become a huge UI time.
        Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        Assert.Empty(recorder.GetEntries());
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        frames.Requests[1].SetResult(clock.Timestamp);
        Assert.Single(recorder.GetEntries());
    }

    [Fact]
    public void FrameBeforeTheTimeoutIsRecordedAndReleasesItsTimer()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        var timer = Assert.Single(clock.Timers);
        clock.Timestamp = 5; Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        Assert.Single(recorder.GetEntries());
        Assert.True(timer.Disposed);
    }

    [Fact]
    public void EntriesExposeOnlyTechnicalMetadata()
    {
        var fields = typeof(InteractionPerformanceEntry).GetProperties();
        Assert.Equal(new[] { "Action", "Analysis", "Preparation", "Presentation", "StartedAtUtc", "Total", "UI" },
            fields.Select(p => p.Name).Order());
        Assert.All(fields, p => Assert.Contains(p.PropertyType,
            new[] { typeof(InteractionAction), typeof(DateTimeOffset), typeof(TimeSpan) }));
        Assert.True(typeof(InteractionAction).IsEnum);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void UnavailableOrFaultingProbeDoesNotChangeSuccessfulCommands(bool available, bool throws)
    {
        var recorder = new InteractionPerformance(new ImmediateFrames(available, throws));
        var editor = new EditorViewModel(State().Document, () => { }, performance: recorder);
        Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        Assert.Single(editor.Session.UndoHistory);
        Assert.Empty(recorder.GetEntries());
    }

    [Fact]
    public void PendingFrameDoesNotBlockCommandsAndCancellationRejectsLateCompletion()
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var editor = new EditorViewModel(State().Document, () => { }, performance: recorder);
        Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        Assert.Single(editor.Session.UndoHistory);
        Assert.Empty(recorder.GetEntries());
        recorder.CancelPending();
        frames.Requests[0].SetResult(100);
        Assert.Empty(recorder.GetEntries());
        Assert.True(editor.Undo());
        frames.Requests[1].SetResult(TimeProvider.System.GetTimestamp());
        Assert.Equal(InteractionAction.Undo, Assert.Single(recorder.GetEntries()).Action);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrFaultedAsynchronousFrameIsDiscarded(bool faulted)
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var editor = new EditorViewModel(State().Document, () => { }, performance: recorder);
        Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        if (faulted) frames.Requests[0].SetException(new InvalidOperationException("Frame failed."));
        else frames.Requests[0].SetResult(null);
        Assert.Empty(recorder.GetEntries());
        Assert.True(editor.Undo());
        frames.Requests[1].SetResult(TimeProvider.System.GetTimestamp());
        Assert.Equal(InteractionAction.Undo, Assert.Single(recorder.GetEntries()).Action);
    }

    [Fact]
    public void PreviewInvalidBusyCancelledAndUnchangedConfirmationsNeverObserveFrames()
    {
        var frames = new ImmediateFrames(); var recorder = new InteractionPerformance(frames);
        var editor = new EditorViewModel(State().Document, () => { }, performance: recorder);
        Assert.False(editor.ConfirmLoad()); Assert.False(editor.Undo()); Assert.False(editor.Redo());
        editor.PreviewAnnotationOffset(SupportId, new(1, 2)); editor.ClearAnnotationPreview();
        editor.EditSupport(SupportId); editor.SupportDraft!.PositionText = "invalid";
        Assert.False(editor.ConfirmSupport()); editor.CancelEditorInteraction();
        editor.EditSupport(SupportId); Assert.True(editor.ConfirmSupport()); // Content-identical commit.
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "invalid";
        Assert.False(editor.DimensionLength.Confirm()); editor.DimensionLength.Cancel();
        Assert.False(editor.ToggleSelfWeight()); // This material has no density.
        editor.Session.SetBusy(true);
        Assert.False(editor.SetAnnotationOffset(SupportId, new(1, 2))); Assert.False(editor.Undo());
        editor.Session.SetBusy(false);
        Assert.Equal(0, frames.Calls); Assert.Empty(recorder.GetEntries()); Assert.Empty(editor.Session.UndoHistory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoAndRedoAreEachMeasuredOnceIncludingMetadataHistory(bool mechanical)
    {
        var clock = new Clock(); var recorder = new InteractionPerformance(new ImmediateFrames(), clock);
        int analyses = 0;
        var editor = new EditorViewModel(State().Document, () => { }, beam =>
        { analyses++; clock.Timestamp += 5; return BeamAnalysis.Analyze(beam); }, performance: recorder);
        if (mechanical)
        { editor.EditLoad(LoadId); editor.LoadDraft!.ValueText = "10"; Assert.True(editor.ConfirmLoad()); }
        else Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        Assert.True(editor.Undo()); Assert.False(editor.Undo());
        Assert.True(editor.Redo()); Assert.False(editor.Redo());
        var entries = recorder.GetEntries();
        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { InteractionAction.Undo, InteractionAction.Redo }, entries.Skip(1).Select(e => e.Action));
        Assert.All(entries, entry => Assert.Equal(TimeSpan.FromTicks(mechanical ? 5 : 0), entry.Analysis));
        Assert.Equal(mechanical ? 4 : 1, analyses);
    }

    [Fact]
    public void MechanicalConfirmationIncludesSessionAndFinalLengthNotifications()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        bool initialized = false;
        var editor = new EditorViewModel(State().Document, () => { }, beam =>
        { if (initialized) clock.Timestamp += 7; return BeamAnalysis.Analyze(beam); }, performance: recorder);
        initialized = true;
        editor.Session.Changed += () => clock.Timestamp += 3;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "2000";
        editor.DimensionLength.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(LengthInputViewModel.IsEditing)) clock.Timestamp += 5; };
        Assert.True(editor.DimensionLength.Confirm());
        Assert.False(editor.DimensionLength.IsEditing);
        Assert.Empty(recorder.GetEntries());
        clock.Timestamp += 11; Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        var entry = Assert.Single(recorder.GetEntries());
        Assert.Equal(InteractionAction.BeamGeometry, entry.Action);
        Assert.Equal(TimeSpan.FromTicks(7), entry.Analysis);
        Assert.Equal(TimeSpan.FromTicks(8), entry.Presentation);
        Assert.Equal(TimeSpan.FromTicks(11), entry.UI);
    }

    [Fact]
    public async Task InitialSetupIsExcludedButExistingSetupIncludesOuterNotificationsAndMetadata()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var main = new MainWindowViewModel(files: new Files(), performance: recorder);
        Assert.True(await main.NewAsync()); CompleteSetup(main);
        Assert.True(await main.Setup.ConfirmAsync());
        Assert.Empty(frames.Requests); Assert.Empty(recorder.GetEntries());
        main.EditProject();
        main.Setup.ProjectTitleInput.Text = "Private project content";
        main.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(MainWindowViewModel.Mode)) clock.Timestamp += 9; };
        Assert.True(await main.Setup.ConfirmAsync());
        Assert.Equal(MainViewMode.Editor, main.Mode);
        clock.Timestamp += 2; Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        var entry = Assert.Single(recorder.GetEntries());
        Assert.Equal(InteractionAction.ProjectSetup, entry.Action);
        Assert.Equal(TimeSpan.Zero, entry.Analysis);
        Assert.Equal(TimeSpan.FromTicks(9), entry.Presentation);
        Assert.Equal(TimeSpan.FromTicks(2), entry.UI);
        Assert.DoesNotContain("Private", entry.ToString());
    }

    [Fact]
    public async Task LeavingTheEditorCancelsItsPendingMeasurement()
    {
        var frames = new Frames(); var recorder = new InteractionPerformance(frames);
        var main = new MainWindowViewModel(files: new Files(), performance: recorder);
        Assert.True(await main.NewAsync()); CompleteSetup(main);
        Assert.True(await main.Setup.ConfirmAsync());
        var editor = main.Editor!;
        editor.ToggleSupportTool(SupportType.Fixed); editor.HoverPlacement(M(0));
        Assert.True(editor.PlaceSupport()); Assert.True(editor.ConfirmSupport());
        main.EditProject();
        Assert.Single(frames.Requests).SetResult(TimeProvider.System.GetTimestamp());
        Assert.Empty(recorder.GetEntries());
    }

    [Fact]
    public void ProductExceptionsKeepTheirSemanticsAndDoNotLeakTheContext()
    {
        var frames = new ImmediateFrames(); var recorder = new InteractionPerformance(frames);
        bool fail = false;
        var editor = new EditorViewModel(State().Document, () => { }, beam =>
        { if (fail) throw new InvalidOperationException("Analysis failed."); return BeamAnalysis.Analyze(beam); }, performance: recorder);
        fail = true;
        editor.EditLoad(LoadId); editor.LoadDraft!.ValueText = "10";
        Assert.Throws<InvalidOperationException>(() => editor.ConfirmLoad());
        Assert.Empty(recorder.GetEntries()); Assert.Equal(0, frames.Calls);
        fail = false;
        Assert.True(editor.SetAnnotationOffset(SupportId, new(1, 2)));
        Assert.Equal(InteractionAction.AnnotationOffset, Assert.Single(recorder.GetEntries()).Action);
    }

    [Theory]
    [InlineData(InteractionAction.PointForce)] [InlineData(InteractionAction.PointMoment)]
    [InlineData(InteractionAction.DistributedLoad)] [InlineData(InteractionAction.Support)]
    [InlineData(InteractionAction.SpringSupport)] [InlineData(InteractionAction.MomentRelease)]
    public void EntityCreateEditAndDeleteEachProduceOneClassifiedEntry(InteractionAction action)
    {
        var recorder = new InteractionPerformance(new ImmediateFrames());
        var editor = new EditorViewModel(SelfWeightTestProjects.Document(), () => { }, performance: recorder);
        switch (action)
        {
            case InteractionAction.PointForce:
            case InteractionAction.PointMoment:
                editor.ToggleLoadTool(action == InteractionAction.PointForce ? PointLoadKind.Force : PointLoadKind.Moment);
                editor.HoverLoadPlacement(M(1)); Assert.True(editor.PlaceLoad()); Assert.True(editor.ConfirmLoad());
                editor.EditLoad(editor.Document.Loads.Single().Id); editor.LoadDraft!.ValueText = "20";
                Assert.True(editor.ConfirmLoad()); editor.EditLoad(editor.Document.Loads.Single().Id); editor.DeleteLoad();
                break;
            case InteractionAction.DistributedLoad:
                editor.ToggleDistributedLoadTool();
                editor.HoverDistributedLoadPlacement(M(.5)); Assert.True(editor.PlaceDistributedLoadEndpoint());
                editor.HoverDistributedLoadPlacement(M(2)); Assert.True(editor.PlaceDistributedLoadEndpoint());
                Assert.True(editor.ConfirmDistributedLoad());
                editor.EditDistributedLoad(editor.Document.DistributedLoads.Single().Id);
                editor.DistributedLoadDraft!.NameText = "Technical classification must not store this";
                Assert.True(editor.ConfirmDistributedLoad());
                editor.EditDistributedLoad(editor.Document.DistributedLoads.Single().Id); editor.DeleteDistributedLoad();
                break;
            case InteractionAction.MomentRelease:
                editor.ToggleMomentReleaseTool(); editor.HoverMomentReleasePlacement(M(1));
                Assert.True(editor.PlaceMomentRelease()); Assert.True(editor.ConfirmMomentRelease());
                editor.EditMomentRelease(editor.Document.MomentReleases.Single().Id); editor.MomentReleaseDraft!.NameText = "Changed";
                Assert.True(editor.ConfirmMomentRelease());
                editor.EditMomentRelease(editor.Document.MomentReleases.Single().Id); editor.DeleteMomentRelease();
                break;
            default:
                editor.ToggleSupportTool(action == InteractionAction.SpringSupport ? SupportType.Spring : SupportType.Roller);
                editor.HoverPlacement(M(1)); Assert.True(editor.PlaceSupport());
                if (action == InteractionAction.SpringSupport) editor.SupportDraft!.TranslationalText = "100";
                Assert.True(editor.ConfirmSupport());
                var id = editor.Document.Supports.Last().Id;
                editor.EditSupport(id); editor.SupportDraft!.NameText = "Changed"; Assert.True(editor.ConfirmSupport());
                editor.EditSupport(id); editor.DeleteSupport();
                break;
        }
        Assert.Equal(3, recorder.GetEntries().Count);
        Assert.All(recorder.GetEntries(), entry => Assert.Equal(action, entry.Action));
    }

    [Fact]
    public void SelfWeightVisibilityDisableAndDirectSetupUseTheSharedPath()
    {
        var recorder = new InteractionPerformance(new ImmediateFrames());
        var editor = new EditorViewModel(SelfWeightTestProjects.Document(), () => { }, performance: recorder);
        Assert.True(editor.ToggleSelfWeight()); editor.ToggleSelfWeightVisibility(); editor.DisableSelfWeight();
        Assert.True(editor.ApplySetup(M(4), editor.Document.Section, SectionAxisDesignation.Y, editor.Document.Material, null));
        Assert.Equal(new[] { InteractionAction.SelfWeight, InteractionAction.SelfWeightVisibility,
            InteractionAction.SelfWeight, InteractionAction.ProjectSetup }, recorder.GetEntries().Select(e => e.Action));
        Assert.Equal(TimeSpan.Zero, recorder.GetEntries()[1].Analysis);
    }

    [Fact]
    public void EntryRecordedIsRaisedOnlyOnceTheCompletedEntryIsInTheHistory()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        var counts = new List<int>();
        recorder.EntryRecorded += () => counts.Add(recorder.GetEntries().Count);
        using (recorder.Begin(InteractionAction.PointForce))
        {
            recorder.AnalysisStarting(); recorder.AnalysisFinished(); recorder.Committed();
            Assert.Empty(counts);
        }
        Assert.Empty(counts); // The frame is still pending: no intermediate notification.
        clock.Timestamp = 4; Assert.Single(frames.Requests).SetResult(clock.Timestamp);
        Assert.Equal(new[] { 1 }, counts);
    }

    [Fact]
    public void EntryRecordedIsNotRaisedForDiscardedMeasurements()
    {
        var clock = new Clock(); var frames = new Frames(); var recorder = new InteractionPerformance(frames, clock);
        int raised = 0; recorder.EntryRecorded += () => raised++;
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.PresentationStarting(); } // Never committed.
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.Committed(); } // No analysis boundary.
        Assert.Empty(frames.Requests);
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.PresentationStarting(); recorder.Committed(); }
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.PresentationStarting(); recorder.Committed(); }
        using (recorder.Begin(InteractionAction.PointForce)) { recorder.PresentationStarting(); recorder.Committed(); }
        Assert.Equal(3, frames.Requests.Count);
        clock.Timers[0].Fire(); // Timeout.
        recorder.CancelPending(); // Cancellation of the remaining ones.
        foreach (var request in frames.Requests) request.TrySetResult(clock.Timestamp);
        var unavailable = new InteractionPerformance(new ImmediateFrames(available: false));
        unavailable.EntryRecorded += () => raised++;
        using (unavailable.Begin(InteractionAction.Undo)) { unavailable.PresentationStarting(); unavailable.Committed(); }
        Assert.Equal(0, raised);
        Assert.Empty(recorder.GetEntries());
    }

    [Fact]
    public void RemovedEntryRecordedListenersAreNotCalledAndFailingOnesDoNotLoseTheEntry()
    {
        var recorder = new InteractionPerformance(new ImmediateFrames());
        int raised = 0;
        Action counter = () => raised++;
        recorder.EntryRecorded += counter;
        using (recorder.Begin(InteractionAction.Redo)) { recorder.PresentationStarting(); recorder.Committed(); }
        recorder.EntryRecorded -= counter;
        using (recorder.Begin(InteractionAction.Redo)) { recorder.PresentationStarting(); recorder.Committed(); }
        Assert.Equal(1, raised);
        recorder.EntryRecorded += () => throw new InvalidOperationException("Listener failure.");
        using (recorder.Begin(InteractionAction.Redo)) { recorder.PresentationStarting(); recorder.Committed(); }
        Assert.Equal(3, recorder.GetEntries().Count);
    }
}
