using System.Reflection;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using SpanDraft.Desktop.Diagnostics;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class InteractionFrameProbeTests
{
    private sealed class Clock : TimeProvider
    {
        public long Timestamp;
        public override long GetTimestamp() => Timestamp;
    }

    // The inert platform has no render timer; drive Avalonia's real server pass explicitly.
    private static void Render(Compositor compositor)
    {
        var server = typeof(Compositor).GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compositor)!;
        server.GetType().GetMethod("Render")!.Invoke(server, [false]);
    }

    private static void CompleteStartupFrame(Border visual)
    {
        Render(ElementComposition.GetElementVisual(visual)!.Compositor);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task AnimationTickAndProcessedBatchAreNotTheRenderedEndpoint()
    {
        using var environment = new DesktopControlEnvironment();
        var visual = new Border(); var window = new Window { Content = visual };
        window.Show(); DesktopControlEnvironment.ArrangeSection(window);
        CompleteStartupFrame(visual);
        using var probe = new AvaloniaInteractionFrameProbe(window, visual);
        var clock = new Clock { Timestamp = 10 };
        var recorder = new InteractionPerformance(probe, clock);
        var finished = new TaskCompletionSource<InteractionPerformanceEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.MeasurementFinished += finished.SetResult;
        using (recorder.Begin(InteractionAction.AnnotationOffset))
        { recorder.PresentationStarting(); recorder.Committed(); }
        var observation = probe.ObserveNextFrameAsync(clock, CancellationToken.None);
        var secondObservation = probe.ObserveNextFrameAsync(clock, CancellationToken.None);
        Assert.False(observation.IsCompleted);
        var batch = ElementComposition.GetElementVisual(visual)!.Compositor.RequestCompositionBatchCommitAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(observation.IsCompleted);
        var processedCheck = batch.Processed.ContinueWith(_ =>
        {
            Assert.False(observation.IsCompleted);
            Assert.False(secondObservation.IsCompleted);
            clock.Timestamp = 25;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Render(ElementComposition.GetElementVisual(visual)!.Compositor);
        Assert.True(processedCheck.IsCompletedSuccessfully);
        await processedCheck;
        Assert.True(observation.IsCompletedSuccessfully);
        Assert.True(secondObservation.IsCompletedSuccessfully);
        clock.Timestamp = 999;
        Assert.Equal(25, await observation);
        Assert.Equal(25, await secondObservation);
        // Close while still on the window's owning thread: the recorder finishes on its own thread,
        // and awaiting it may resume this test on another one.
        window.Close();
        var entry = await ProjectTestSupport.WaitFor(finished.Task);
        Assert.Same(Assert.Single(recorder.GetEntries()), entry);
    }

    [Fact]
    public async Task VetoedWindowCloseKeepsTheObservationAndTheFrameStillCompletes()
    {
        using var environment = new DesktopControlEnvironment();
        var visual = new Border(); var window = new Window { Content = visual };
        window.Show(); DesktopControlEnvironment.ArrangeSection(window);
        CompleteStartupFrame(visual);
        using var probe = new AvaloniaInteractionFrameProbe(window, visual);
        var clock = new Clock { Timestamp = 3 };
        var observation = probe.ObserveNextFrameAsync(clock, CancellationToken.None);
        bool veto = true;
        window.Closing += (_, e) => e.Cancel = veto;
        window.Close();
        Assert.True(window.IsVisible);
        Assert.False(observation.IsCompleted);
        Dispatcher.UIThread.RunJobs();
        Render(ElementComposition.GetElementVisual(visual)!.Compositor);
        Assert.Equal(3, await observation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        veto = false;
        window.Close();
    }

    [Theory]
    [InlineData("cancel")] [InlineData("detach")] [InlineData("hide")]
    [InlineData("close")] [InlineData("dispose")] [InlineData("minimize")] [InlineData("visualHide")]
    public async Task LifetimeChangeAbandonsObservationAndLateFrameCannotCompleteIt(string change)
    {
        using var environment = new DesktopControlEnvironment();
        var visual = new Border(); var window = new Window { Content = visual };
        window.Show(); DesktopControlEnvironment.ArrangeSection(window);
        CompleteStartupFrame(visual);
        using var probe = new AvaloniaInteractionFrameProbe(window, visual);
        using var cancellation = new CancellationTokenSource();
        var observation = probe.ObserveNextFrameAsync(new Clock(), cancellation.Token);
        var compositor = ElementComposition.GetElementVisual(visual)!.Compositor;
        Dispatcher.UIThread.RunJobs();
        Assert.False(observation.IsCompleted);
        switch (change)
        {
            case "cancel": cancellation.Cancel(); break;
            case "detach": window.Content = null; break;
            case "hide": window.Hide(); break;
            case "visualHide": visual.IsVisible = false; break;
            case "minimize": window.WindowState = WindowState.Minimized; break;
            case "close": window.Close(); break;
            default: probe.Dispose(); break;
        }
        Assert.True(observation.IsCompletedSuccessfully);
        Assert.Null(await observation);
        Render(compositor);
        Assert.Null(await observation);
        if (window.IsVisible) window.Close();
    }

    [Fact]
    public async Task UnattachedHiddenAndDisposedTargetsReturnWithoutWaiting()
    {
        using var environment = new DesktopControlEnvironment();
        var visual = new Border(); var window = new Window();
        using var probe = new AvaloniaInteractionFrameProbe(window, visual);
        var observation = probe.ObserveNextFrameAsync(new Clock(), CancellationToken.None);
        Assert.True(observation.IsCompletedSuccessfully);
        Assert.Null(await observation);
        window.Content = visual; window.Show(); DesktopControlEnvironment.ArrangeSection(window);
        window.Hide();
        observation = probe.ObserveNextFrameAsync(new Clock(), CancellationToken.None);
        Assert.True(observation.IsCompletedSuccessfully);
        Assert.Null(await observation);
        probe.Dispose();
        observation = probe.ObserveNextFrameAsync(new Clock(), CancellationToken.None);
        Assert.True(observation.IsCompletedSuccessfully);
        Assert.Null(await observation);
        window.Close();
    }
}
