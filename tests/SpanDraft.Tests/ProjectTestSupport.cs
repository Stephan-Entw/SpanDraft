using System.Collections.Concurrent;
using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Tests;

internal static class ProjectTestSupport
{
    public static readonly Guid SupportId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid LoadId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    public static Length M(double value) => Length.FromMeters(value);
    public static ProjectState State(int section = 0)
    {
        Section profile = section switch
        {
            0 => new RectangleSection(M(.1), M(.2)),
            1 => new RectangularHollowSection(M(.11), M(.22), M(.003)),
            2 => new CircleSection(M(.12345678901234567)),
            3 => new CircularHollowSection(M(.123), M(.004)),
            _ => new CustomSection(Area.FromSquareMeters(.000123456789),
                SecondMomentOfArea.FromMetersToTheFourth(1.23456789e-9), SectionModulus.FromCubicMeters(1.23456789e-6))
        };
        var document = new EditorDocument(M(1.0000000000000002),
            new Material("Test steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6)), profile,
            [new(SupportId, M(0), SupportType.Fixed, "A"),
                new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), M(.5), SupportType.Pinned, "B"),
                new(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), M(1), SupportType.Roller, "C")],
            [new EditorPointMoment(LoadId, M(0), Moment.FromNewtonMeters(0), "M5"),
                new EditorPointForce(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), M(.25), Force.FromNewtons(-1234.5678901234567), "F7")],
            new(10, 11, 12, 13),
            [new(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), M(0), M(.75), ForcePerLength.FromNewtonsPerMeter(0), "q8")]);
        return new(document, new([new(SupportId, new(12.345678901234567, -75)), new(LoadId, new(0, 0))]));
    }

    public sealed class Files : IProjectFileStore
    {
        public ConcurrentDictionary<string, byte[]> Data { get; } = new();
        public ConcurrentQueue<string> Writes { get; } = new();
        public Func<string, byte[], CancellationToken, Task>? BeforeWrite { get; set; }
        public Func<string, Task>? BeforeRead { get; set; }
        public string? FailWritePath { get; set; }
        public bool FailDelete { get; set; }
        public bool HonorWriteCancellation { get; set; } = true;
        public async Task<byte[]?> ReadAsync(string path)
        {
            if (BeforeRead is not null) await BeforeRead(path);
            return Data.TryGetValue(path, out var bytes) ? bytes.ToArray() : null;
        }
        public async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (BeforeWrite is not null) await BeforeWrite(path, bytes, cancellationToken);
            if (HonorWriteCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (path == FailWritePath) throw new IOException("Injected write failure.");
            Data[path] = bytes.ToArray();
            Writes.Enqueue(path);
        }
        public Task DeleteAsync(string path)
        {
            if (FailDelete) throw new IOException("Injected delete failure.");
            Data.TryRemove(path, out _);
            return Task.CompletedTask;
        }
    }

    public sealed class Delay(bool honorCancellation = true)
    {
        public List<TaskCompletionSource> Waiters { get; } = [];
        public Task Wait(TimeSpan duration, CancellationToken token)
        {
            if (duration != TimeSpan.FromSeconds(1)) throw new InvalidOperationException("Unexpected debounce duration.");
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Waiters.Add(waiter);
            if (honorCancellation) token.Register(() => waiter.TrySetCanceled(token));
            return waiter.Task;
        }
        public void ReleaseAll() { foreach (var waiter in Waiters) waiter.TrySetResult(); }
    }

    public sealed class Dialogs : IProjectDialogs
    {
        public string? OpenPath { get; set; }
        public string? SavePath { get; set; } = "/test/Beam01.spandraft";
        public LeaveDecision Leave { get; set; } = LeaveDecision.Cancel;
        public RecoveryDecision Recovery { get; set; } = RecoveryDecision.Restore;
        public List<string> Errors { get; } = [];
        public int LeaveQuestions { get; private set; }
        public int RecoveryQuestions { get; private set; }
        public int SaveQuestions { get; private set; }
        public Func<Task<LeaveDecision>>? LeaveHook { get; set; }
        public Func<Task<string?>>? SaveHook { get; set; }
        public Task<string?> PickOpenPathAsync() => Task.FromResult(OpenPath);
        public Task<string?> PickSavePathAsync(string? currentFilePath)
        { SaveQuestions++; return SaveHook?.Invoke() ?? Task.FromResult(SavePath); }
        public Task<LeaveDecision> ConfirmLeaveAsync(string projectName)
        { LeaveQuestions++; return LeaveHook?.Invoke() ?? Task.FromResult(Leave); }
        public Task<RecoveryDecision> ConfirmRecoveryAsync(bool damaged, DateTimeOffset? writtenAtUtc)
        { RecoveryQuestions++; return Task.FromResult(damaged ? RecoveryDecision.Discard : Recovery); }
        public Task ShowErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
    }

    public sealed class App
    {
        public const string Slot = "/private/recovery.json";
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new();
        public ProjectRecovery Recovery { get; }
        public MainWindowViewModel Main { get; }
        public int Analyses { get; private set; }
        public App(bool create = true)
        {
            Recovery = new(Files, Slot, Delay.Wait, () => Now);
            Main = new(b => { Analyses++; return BeamAnalysis.Analyze(b); }, Files, Dialogs, Recovery);
            if (create) Main.Setup.ApplyCommand.Execute(null);
        }
        public void ChangeLength(double meters = 1.5)
        {
            var input = Main.Editor!.DimensionLength;
            input.Begin();
            input.Text = SpanDraft.Desktop.State.UiNumbers.Format(meters * 1000);
            if (!input.Confirm()) throw new InvalidOperationException(input.ErrorText);
        }
    }
}
