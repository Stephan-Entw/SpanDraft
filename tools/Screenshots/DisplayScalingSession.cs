using System.Diagnostics;

namespace SpanDraft.Screenshots;

internal sealed record ScaleTransition(string Target, int ScalePercent, DateTimeOffset AppliedUtc,
    DisplayDevice[] DisplaysBefore, DisplayDevice[] DisplaysAfter);

internal sealed class DisplayScalingSession(
    DisplayDevice[] original,
    Func<DisplayDevice[]> readDisplays,
    Func<string, DisplayScale> readScale,
    Action<string, DisplayScale, int> setScale)
{
    private DisplayDevice[] expected = [.. original];
    private readonly Dictionary<string, DisplayScale> changed = new(StringComparer.OrdinalIgnoreCase);
    internal List<ScaleTransition> Transitions { get; } = [];
    internal bool Restored { get; private set; }

    internal async Task<DisplaySnapshot> SelectAsync(int percent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssertExpected();
        uint dpi = CaptureScaling.ExpectedDpi(percent);
        if (expected.Any(d => d.Active && d.Width > 0 && d.Height > 0 && d.Dpi == dpi))
            return DisplaySnapshot.RequireTarget(expected, percent);

        if (expected.Count(d => d.Primary) != 1) throw new InvalidOperationException("Cannot identify exactly one primary display.");
        // Prefer a secondary display when a change is needed, leaving the user's primary display alone.
        var errors = new List<string>();
        foreach (var display in expected.Where(d => d.Active && d.Width > 0 && d.Height > 0)
                     .OrderBy(d => d.Primary).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            DisplayScale scale;
            int relative;
            try
            {
                scale = readScale(display.Name);
                relative = scale.Relative(percent);
                if (CaptureScaling.ExpectedDpi(scale.Percent) != display.Dpi)
                    throw new InvalidOperationException("Custom scaling differs from the Windows scale steps; automatic restoration cannot be guaranteed.");
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                errors.Add(display.Name + ": " + error.Message);
                continue;
            }
            AssertExpected();
            // Record ownership before calling Windows: even a failed setter may have changed state.
            changed.TryAdd(display.Name, scale);
            Console.WriteLine($"Setting {display.Name} to {percent}% for capture.");
            var before = expected;
            setScale(display.Name, scale, relative);
            expected = await WaitForAsync(before.Select(d => d.Name == display.Name ? d with { Dpi = dpi } : d).ToArray(), cancellationToken);
            Transitions.Add(new(display.Name, percent, DateTimeOffset.UtcNow, before, expected));
            return DisplaySnapshot.RequireTarget(expected, percent, display.Name);
        }
        throw new InvalidOperationException($"No active display can be automatically scaled to {percent}%. " + string.Join("; ", errors));
    }

    internal void AssertExpected()
    {
        if (!expected.SequenceEqual(readDisplays()))
            throw new InvalidOperationException("Display configuration changed outside the runner's automatic scaling transition.");
    }

    internal async Task RestoreAsync()
    {
        if (Restored) return;
        var errors = new List<Exception>();
        foreach (var entry in changed.Reverse())
        {
            try
            {
                // Never write to a reconnected/replaced display or a changed topology.
                DisplaySnapshot.AssertScaleOnlyTransition(original, readDisplays());
                var now = readScale(entry.Key);
                if (now.Current != entry.Value.Current)
                {
                    Console.WriteLine($"Restoring {entry.Key} to {entry.Value.Percent}%.");
                    setScale(entry.Key, entry.Value, entry.Value.Current);
                }
            }
            catch (Exception error) { errors.Add(error); }
        }
        try { expected = await WaitForAsync(original, CancellationToken.None); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Original display scaling could not be restored: "
            + string.Join("; ", errors.Select(e => e.Message)), errors);
        Restored = true;
    }

    private async Task<DisplayDevice[]> WaitForAsync(DisplayDevice[] target, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = readDisplays();
            DisplaySnapshot.AssertScaleOnlyTransition(target, actual);
            if (target.SequenceEqual(actual)) return actual;
            if (timer.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException("Windows did not apply the requested native display DPI within 15 seconds.");
            await Task.Delay(100, cancellationToken);
        }
    }
}
