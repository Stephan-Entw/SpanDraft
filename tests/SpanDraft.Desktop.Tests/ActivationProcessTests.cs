using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SpanDraft.Desktop.Activation;
using Xunit;

namespace SpanDraft.Desktop.Tests;

/// <summary>
/// The Single-Instance contract across real OS process boundaries. Each participant is a separate
/// SpanDraft.Activation.ProcessHost process driving the production InstanceCoordinator and broker
/// for a temporary DataScope; the tests never assume which process wins an election.
/// </summary>
public sealed class ActivationProcessTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const int RetryAttempts = 3;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "spandraft-process-" + Guid.NewGuid().ToString("N"));
    private readonly List<Participant> _participants = [];

    private DataScope Scope => new(Path.Combine(_root, "scope"));

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var participant in _participants)
        {
            try { participant.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private Participant Start(string argument)
    {
        var participant = new Participant(Scope, argument);
        _participants.Add(participant);
        participant.Start();
        return participant;
    }

    private string Project(string name) => Path.Combine(_root, name + ".spandraft");

    private static async Task<Participant> SinglePrimaryAsync(params Participant[] candidates)
    {
        var elected = await Task.WhenAny(candidates.Select(static p => p.PrimaryObserved))
            .WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var primary = await elected;
        Assert.Same(primary, Assert.Single(candidates, static p => p.IsPrimary));
        return primary;
    }

    private static async Task StartTogetherAsync(params Participant[] participants)
    {
        foreach (var participant in participants) await participant.WaitForLineAsync("waiting");
        // Shared start gate: all elections begin back to back.
        foreach (var participant in participants) participant.Send("go");
    }

    private void AssertElectionLockHeldElsewhere()
    {
        using var unexpectedPrimary = PrimaryInstance.TryAcquire(Scope);
        Assert.Null(unexpectedPrimary);
    }

    private void AssertOnlyInstanceFilesInScope() => Assert.Subset(new HashSet<string> { "instance.lock", "instance.json" },
        Directory.EnumerateFileSystemEntries(Scope.Directory).Select(Path.GetFileName).ToHashSet()!);

    [Fact]
    public async Task SimultaneousColdStartElectsOnePrimaryAndTheOtherRequestReachesItAfterRetry()
    {
        var a = Start(Project("x"));
        var b = Start(Project("y"));
        await StartTogetherAsync(a, b);

        var primary = await SinglePrimaryAsync(a, b);
        var secondary = ReferenceEquals(primary, a) ? b : a;
        Assert.Equal($"primary {primary.Request}", primary.Lines.First(static l => l.StartsWith("primary")));
        AssertElectionLockHeldElsewhere();

        // The Primary is still Starting: the Secondary keeps its request and retries without any error.
        await primary.WaitForRetriesAsync(secondary);
        Assert.False(secondary.HasExited);
        Assert.False(secondary.IsPrimary);
        Assert.DoesNotContain(secondary.Lines, static l => l.StartsWith("outcome"));
        Assert.DoesNotContain(primary.Lines, static l => l.StartsWith("handled"));

        primary.Send("ready");
        await primary.WaitForLineAsync($"handled {secondary.Request}");
        Assert.Equal(0, await secondary.WaitForExitAsync());
        Assert.Equal(["waiting", "outcome Forwarded"], secondary.Lines);
        Assert.Empty(secondary.Errors);
        // The startup's own request is processed first, then the forwarded one.
        Assert.Equal([$"handled {primary.Request}", $"handled {secondary.Request}"],
            primary.Lines.Where(static l => l.StartsWith("handled")));
        AssertOnlyInstanceFilesInScope();

        primary.Send("exit");
        Assert.Equal(0, await primary.WaitForExitAsync());
        Assert.Empty(primary.Errors);
    }

    [Fact]
    public async Task WaitingSecondariesReElectExactlyOnePrimaryThatKeepsItsOwnRequest()
    {
        var first = Start(Project("first"));
        await StartTogetherAsync(first);
        Assert.Same(first, await SinglePrimaryAsync(first));

        var s1 = Start(Project("z"));
        var s2 = Start("spandraft://open/demo");
        await StartTogetherAsync(s1, s2);
        await first.WaitForRetriesAsync(s1);
        await first.WaitForRetriesAsync(s2);
        Assert.False(s1.IsPrimary || s2.IsPrimary || s1.HasExited || s2.HasExited);
        AssertElectionLockHeldElsewhere();

        // The Primary ends without ever becoming Ready (like a quit from the unreadable-recovery question) and frees the lock.
        first.Send("exit");
        Assert.Equal(0, await first.WaitForExitAsync());
        Assert.DoesNotContain(first.Lines, static l => l.StartsWith("handled"));

        var successor = await SinglePrimaryAsync(s1, s2);
        var waiting = ReferenceEquals(successor, s1) ? s2 : s1;
        Assert.Equal($"primary {successor.Request}", successor.Lines.First(static l => l.StartsWith("primary")));
        AssertElectionLockHeldElsewhere();
        await successor.WaitForRetriesAsync(waiting);
        Assert.False(waiting.IsPrimary);
        Assert.False(waiting.HasExited);

        successor.Send("ready");
        await successor.WaitForLineAsync($"handled {waiting.Request}");
        Assert.Equal(0, await waiting.WaitForExitAsync());
        Assert.Equal(["waiting", "outcome Forwarded"], waiting.Lines);
        Assert.Empty(waiting.Errors);
        Assert.Equal([$"handled {successor.Request}", $"handled {waiting.Request}"],
            successor.Lines.Where(static l => l.StartsWith("handled")));
        Assert.Equal(2, _participants.Count(static p => p.IsPrimary));
        AssertOnlyInstanceFilesInScope();

        successor.Send("exit");
        Assert.Equal(0, await successor.WaitForExitAsync());
    }

    private sealed class Participant : IDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly ConcurrentQueue<string> _errors = new();
        private readonly Channel<bool> _outputChanged = Channel.CreateBounded<bool>(1);
        private readonly TaskCompletionSource<Participant> _primary = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;

        public Participant(DataScope scope, string argument)
        {
            var request = LaunchRequest.FromArguments([argument]);
            Request = $"{request.Type} {request.Value}";
            string helper = Path.Combine(AppContext.BaseDirectory, "SpanDraft.Activation.ProcessHost.dll");
            var start = new ProcessStartInfo(DotnetHost())
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            start.ArgumentList.Add(helper);
            start.ArgumentList.Add(scope.Directory);
            start.ArgumentList.Add(argument);
            _process = new Process { StartInfo = start };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not { } line) { _outputChanged.Writer.TryComplete(); return; }
                _lines.Enqueue(line);
                if (line.StartsWith("primary ", StringComparison.Ordinal)) _primary.TrySetResult(this);
                _outputChanged.Writer.TryWrite(true);
            };
            _process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) _errors.Enqueue(line); };
        }

        public void Start()
        {
            _started = _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string Request { get; }
        public IReadOnlyList<string> Lines => [.. _lines];
        public IReadOnlyList<string> Errors => [.. _errors];
        public bool IsPrimary => _lines.Any(static l => l.StartsWith("primary "));
        public Task<Participant> PrimaryObserved => _primary.Task;
        public bool HasExited => _process.HasExited;

        public void Send(string command)
        {
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }

        public Task WaitForRetriesAsync(Participant secondary) => WaitForLineAsync($"retry {secondary.Request}", RetryAttempts);

        public async Task WaitForLineAsync(string expected, int occurrences = 1)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                while (_lines.Count(line => line == expected) < occurrences)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!await _outputChanged.Reader.WaitToReadAsync(timeout.Token))
                    {
                        // The last line may have arrived immediately before EOF.
                        Assert.True(_lines.Count(line => line == expected) >= occurrences, MissingOutput(expected, occurrences));
                        return;
                    }
                    _outputChanged.Reader.TryRead(out _);
                }
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            { Assert.Fail(MissingOutput(expected, occurrences)); }
        }

        private string MissingOutput(string expected, int occurrences) =>
            $"Expected {occurrences} occurrence(s) of '{expected}'. Output: {string.Join(" | ", Lines)} Errors: {string.Join(" | ", Errors)}";

        public async Task<int> WaitForExitAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Timeout);
            await _process.WaitForExitAsync(timeout.Token);
            return _process.ExitCode;
        }

        public void Dispose()
        {
            try
            {
                if (!_started) return;
                try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (_process.HasExited) { }
                Assert.True(_process.WaitForExit((int)Timeout.TotalMilliseconds), $"Participant {Request} survived cleanup.");
            }
            finally { _process.Dispose(); }
        }

        // The muxer of the runtime executing the tests, independent of PATH and any installed SpanDraft.
        private static string DotnetHost()
        {
            string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
            string muxer = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            return File.Exists(muxer) ? muxer : "dotnet";
        }
    }
}
