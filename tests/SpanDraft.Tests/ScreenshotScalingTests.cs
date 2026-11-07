using SpanDraft.Screenshots;
using Xunit;

namespace SpanDraft.Tests;

public sealed class ScreenshotScalingTests
{
    private static DisplayDevice Primary => new("primary", "display", "PCI-primary", 5, 2940, 1846, 0, 0, 192);
    private static DisplayDevice Secondary => new("secondary", "display", "PCI-secondary", 1, 1920, 1080, 2940, 0, 168);

    [Fact]
    public async Task SingleMonitorChangesBetweenProfilesAndRestoresOriginalScaling()
    {
        var desktop = new Desktop(Primary);
        var session = desktop.Session();
        Assert.Equal(240u, (await session.SelectAsync(250, TestContext.Current.CancellationToken)).CaptureDisplay.Dpi);
        Assert.Equal(192u, (await session.SelectAsync(200, TestContext.Current.CancellationToken)).CaptureDisplay.Dpi);
        await session.RestoreAsync();
        Assert.True(session.Restored);
        Assert.Equal(new[] { 250, 200 }, desktop.Writes.Select(w => w.Percent));
        Assert.Equal(new[] { 250, 200 }, session.Transitions.Select(t => t.ScalePercent));
        Assert.Equal(new[] { Primary }, desktop.Displays);
        await session.RestoreAsync();
        Assert.Equal(2, desktop.Writes.Count);
    }

    [Fact]
    public async Task TwoAlreadyConfiguredMonitorsNeedNeitherScalingApiNorPause()
    {
        var original = new[] { Primary, Secondary with { Dpi = 240 } };
        var session = new DisplayScalingSession(original, () => original,
            _ => throw new Exception("Must not read DPI offsets."), (_, _, _) => throw new Exception("Must not change scaling."));
        Assert.Equal("secondary", (await session.SelectAsync(250, TestContext.Current.CancellationToken)).Target);
        Assert.Equal("primary", (await session.SelectAsync(200, TestContext.Current.CancellationToken)).Target);
        await session.RestoreAsync();
        Assert.True(session.Restored);
        Assert.Empty(session.Transitions);
    }

    [Fact]
    public async Task ScalingPrefersSecondaryMonitorAndRestoresItAfterStoreOnlyRun()
    {
        var desktop = new Desktop(Primary, Secondary);
        var session = desktop.Session();
        Assert.Equal("secondary", (await session.SelectAsync(250, TestContext.Current.CancellationToken)).Target);
        Assert.Equal(Primary, desktop.Displays[0]);
        await session.RestoreAsync();
        Assert.Equal(new[] { ("secondary", 250), ("secondary", 175) }, desktop.Writes);
        Assert.Equal(new[] { Primary, Secondary }, desktop.Displays);
    }

    [Fact]
    public async Task UnsupportedSecondaryFallsBackToPrimary()
    {
        var desktop = new Desktop(Primary, Secondary);
        desktop.Scales["secondary"] = desktop.Scales["secondary"] with { Maximum = 0 };
        var session = desktop.Session();
        Assert.Equal("primary", (await session.SelectAsync(250, TestContext.Current.CancellationToken)).Target);
        await session.RestoreAsync();
        Assert.All(desktop.Writes, w => Assert.Equal("primary", w.Name));
    }

    [Theory]
    [InlineData(240, 192u)] // Custom percentage cannot be set through Windows' discrete steps.
    [InlineData(250, 230u)] // An existing custom scale cannot be restored using a discrete offset.
    public async Task UnsupportedCustomScalingFailsWithoutAnyWrite(int percent, uint originalDpi)
    {
        var desktop = new Desktop(Primary);
        desktop.Displays[0] = Primary with { Dpi = originalDpi };
        var session = desktop.Session();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SelectAsync(percent, TestContext.Current.CancellationToken));
        await session.RestoreAsync();
        Assert.True(session.Restored);
        Assert.Empty(desktop.Writes);
    }

    [Fact]
    public async Task AlreadyConfiguredCustomScaleDoesNotNeedThePrivateApi()
    {
        var desktop = new Desktop(Primary);
        desktop.Displays[0] = Primary with { Dpi = 230 };
        var session = desktop.Session();
        Assert.Equal(230u, (await session.SelectAsync(240, TestContext.Current.CancellationToken)).CaptureDisplay.Dpi);
        await session.RestoreAsync();
        Assert.Empty(desktop.Writes);
    }

    [Fact]
    public async Task FailedSetterStillRestoresAChangeMadeBeforeTheError()
    {
        var desktop = new Desktop(Primary);
        var session = desktop.Session(afterWrite: percent => { if (percent == 250) throw new InvalidOperationException("setter failed"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SelectAsync(250, TestContext.Current.CancellationToken));
        await session.RestoreAsync();
        Assert.True(session.Restored);
        Assert.Equal(new[] { ("primary", 250), ("primary", 200) }, desktop.Writes);
    }

    [Fact]
    public async Task CancellationDuringScalingDoesNotCancelRestoration()
    {
        using var cancellation = new CancellationTokenSource();
        var desktop = new Desktop(Primary);
        var session = desktop.Session(afterWrite: percent => { if (percent == 250) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SelectAsync(250, cancellation.Token));
        await session.RestoreAsync();
        Assert.True(session.Restored);
        Assert.Equal(new[] { Primary }, desktop.Displays);
    }

    [Fact]
    public async Task ExternalChangesAreRejectedBeforeAnotherScalingWrite()
    {
        var desktop = new Desktop(Primary);
        var session = desktop.Session();
        await session.SelectAsync(250, TestContext.Current.CancellationToken);
        desktop.Displays[0] = desktop.Displays[0] with { Dpi = 168 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SelectAsync(200, TestContext.Current.CancellationToken));
        Assert.Single(desktop.Writes);
        await session.RestoreAsync();
        Assert.Equal(new[] { Primary }, desktop.Displays);
    }

    [Fact]
    public async Task ChangedTopologyPreventsRestoringToAReplacedDisplayAndReportsFailure()
    {
        var desktop = new Desktop(Primary);
        var session = desktop.Session();
        await session.SelectAsync(250, TestContext.Current.CancellationToken);
        desktop.Displays[0] = desktop.Displays[0] with { Identity = "replacement" };
        await Assert.ThrowsAsync<AggregateException>(() => session.RestoreAsync());
        Assert.False(session.Restored);
        Assert.Single(desktop.Writes);
    }

    [Theory]
    [InlineData(1, 1, 4)] [InlineData(-4, -5, 7)] [InlineData(-4, 8, 7)]
    [InlineData(-4, 0, 8)] [InlineData(int.MinValue, 0, 0)]
    public void UnknownWindowsScaleRangesAreRejected(int minimum, int current, int maximum) =>
        Assert.Throws<InvalidOperationException>(() => new DisplayScale(1, 0, minimum, current, maximum).Relative(250));

    private sealed class Desktop(params DisplayDevice[] displays)
    {
        internal DisplayDevice[] Displays = displays;
        internal Dictionary<string, DisplayScale> Scales = displays.ToDictionary(d => d.Name,
            d => new DisplayScale(1, (uint)(d.Primary ? 0 : 1), -4, d.Primary ? 0 : -1, 7));
        internal List<(string Name, int Percent)> Writes = [];

        internal DisplayScalingSession Session(Action<int>? afterWrite = null) => new([.. Displays], () => [.. Displays],
            name => Scales[name], (name, _, relative) =>
            {
                var scale = Scales[name] with { Current = relative };
                Scales[name] = scale;
                Displays = Displays.Select(d => d.Name == name ? d with { Dpi = CaptureScaling.ExpectedDpi(scale.Percent) } : d).ToArray();
                Writes.Add((name, scale.Percent));
                afterWrite?.Invoke(scale.Percent);
            });
    }
}
