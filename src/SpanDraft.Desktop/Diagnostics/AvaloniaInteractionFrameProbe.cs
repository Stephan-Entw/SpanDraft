using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace SpanDraft.Desktop.Diagnostics;

/// <summary>Observes a composition batch without invalidating or changing any visuals.</summary>
internal sealed class AvaloniaInteractionFrameProbe : IInteractionFrameProbe, IDisposable
{
    private readonly TopLevel _window;
    private readonly Visual _visual;
    private CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public AvaloniaInteractionFrameProbe(TopLevel window, Visual visual)
    {
        _window = window;
        _visual = visual;
        _window.Closed += Closed;
        if (_window is Window desktopWindow) desktopWindow.Closing += Closing;
        _window.PropertyChanged += WindowChanged;
        _visual.PropertyChanged += VisualChanged;
        _visual.DetachedFromVisualTree += Detached;
    }

    public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        if (_disposed || !IsAvailable(_window, _visual) || cancellationToken.IsCancellationRequested)
            return Task.FromResult<long?>(null);
        var compositor = ElementComposition.GetElementVisual(_visual)?.Compositor;
        if (compositor is null) return Task.FromResult<long?>(null);
        var observation = new Observation(_window, _visual, compositor, clock, cancellationToken, _lifetime.Token);
        try { _window.RequestAnimationFrame(observation.AnimationFrame); }
        catch (Exception) { observation.Abort(); }
        return observation.Completion;
    }

    private static bool IsAvailable(TopLevel window, Visual visual) => window.IsVisible && visual.IsEffectivelyVisible
        && window is not Window { WindowState: WindowState.Minimized }
        && TopLevel.GetTopLevel(visual) == window;

    private void Closed(object? sender, EventArgs e) => Dispose();
    private void Closing(object? sender, WindowClosingEventArgs e) => CancelObservations();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => CancelObservations();
    private void VisualChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && !_visual.IsEffectivelyVisible) CancelObservations();
    }
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
            && !IsAvailable(_window, _visual)) CancelObservations();
    }

    private void CancelObservations()
    {
        if (_disposed) return;
        var previous = _lifetime;
        _lifetime = new();
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closed -= Closed;
        if (_window is Window desktopWindow) desktopWindow.Closing -= Closing;
        _window.PropertyChanged -= WindowChanged;
        _visual.PropertyChanged -= VisualChanged;
        _visual.DetachedFromVisualTree -= Detached;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class Observation
    {
        private readonly object _gate = new();
        // The recorder consumes this without a UI context; finish before a later view change can cancel it.
        private readonly TaskCompletionSource<long?> _completion = new();
        private TopLevel? _window;
        private Visual? _visual;
        private Compositor? _compositor;
        private TimeProvider? _clock;
        private CancellationTokenRegistration _cancellation, _lifetime;

        public Observation(TopLevel window, Visual visual, Compositor compositor, TimeProvider clock,
            CancellationToken cancellation, CancellationToken lifetime)
        {
            (_window, _visual, _compositor, _clock) = (window, visual, compositor, clock);
            _cancellation = cancellation.Register(static state => ((Observation)state!).Abort(), this);
            _lifetime = lifetime.Register(static state => ((Observation)state!).Abort(), this);
            if (_completion.Task.IsCompleted) Unregister();
        }

        public Task<long?> Completion => _completion.Task;
        public void Abort() => Finish(rendered: false);

        public void AnimationFrame(TimeSpan unused)
        {
            try
            {
                lock (_gate)
                {
                    if (_window is null) return;
                    if (!IsAvailable(_window, _visual!)
                        || ElementComposition.GetElementVisual(_visual!)?.Compositor != _compositor)
                    { Finish(rendered: false); return; }
                    var batch = _compositor!.RequestCompositionBatchCommitAsync();
                    _ = batch.Rendered.ContinueWith(static (task, state) =>
                        ((Observation)state!).Finish(task.IsCompletedSuccessfully), this,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            catch (Exception) { Abort(); }
        }

        private void Finish(bool rendered)
        {
            lock (_gate)
            {
                if (_window is null) return;
                long? timestamp = null;
                try { if (rendered) timestamp = _clock!.GetTimestamp(); }
                finally
                {
                    _window = null; _visual = null; _compositor = null; _clock = null;
                    _completion.TrySetResult(timestamp);
                    Unregister();
                }
            }
        }

        private void Unregister()
        {
            // Do not wait on a cancellation callback that may be completing on another thread.
            _cancellation.Unregister();
            _lifetime.Unregister();
        }
    }
}
