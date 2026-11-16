using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed record DiagnosticsEnvironment(string Version, string OperatingSystem, string Architecture, string Runtime)
{
    public static DiagnosticsEnvironment Current() => new(ApplicationMetadata.Version,
        RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription);
}

/// <summary>One finished interaction as shown to the user; contains no project content.</summary>
public sealed record DiagnosticsRow(string Time, string Action, string Preparation, string Analysis,
    string Presentation, string UI, string Total);

/// <summary>Local, in-memory view of the interaction history; it is only notified about newly recorded entries.</summary>
public sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly InteractionPerformance? _performance;
    private readonly TimeZoneInfo _zone;
    private readonly Action<Action> _post;
    private int _pending, _disposed;
    private bool _advanced;
    // The formatted rows are only built once something shows them; refreshes in the normal view just keep the snapshot.
    private IReadOnlyList<InteractionPerformanceEntry> _entries = [];
    private IReadOnlyList<DiagnosticsRow>? _rows;

    /// <param name="post">Runs a refresh on the UI thread; recorded entries can be reported from any thread.</param>
    public DiagnosticsViewModel(InteractionPerformance? performance, DiagnosticsEnvironment? environment = null,
        TimeZoneInfo? zone = null, Action<Action>? post = null)
    {
        _performance = performance;
        Environment = environment ?? DiagnosticsEnvironment.Current();
        _zone = zone ?? TimeZoneInfo.Local;
        // Background priority keeps the refresh behind input and the frame of the interaction being measured.
        _post = post ?? (static action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));
        if (_performance is null) return;
        // Subscribe before reading: an entry recorded in between is part of the snapshot and only triggers a redundant refresh.
        _performance.EntryRecorded += EntryRecorded;
        _entries = _performance.GetEntries();
    }

    public DiagnosticsEnvironment Environment { get; }
    public bool HasEntries => _entries.Count > 0;
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DiagnosticsRow> Rows => _rows ??= Build(_entries);

    /// <summary>The recorded entries are only formatted and handed to the view while the extended mode is on.</summary>
    public IReadOnlyList<DiagnosticsRow>? VisibleRows => _advanced ? Rows : null;

    public bool IsAdvanced
    {
        get => _advanced;
        set { if (Set(ref _advanced, value)) Notify(nameof(VisibleRows)); }
    }

    public static string ActionName(InteractionAction action) => action switch
    {
        InteractionAction.PointForce => Strings.DiagnosticsActionPointForce,
        InteractionAction.PointMoment => Strings.DiagnosticsActionPointMoment,
        InteractionAction.DistributedLoad => Strings.DiagnosticsActionDistributedLoad,
        InteractionAction.Support => Strings.DiagnosticsActionSupport,
        InteractionAction.SpringSupport => Strings.DiagnosticsActionSpringSupport,
        InteractionAction.MomentRelease => Strings.DiagnosticsActionMomentRelease,
        InteractionAction.SelfWeight => Strings.DiagnosticsActionSelfWeight,
        InteractionAction.BeamGeometry => Strings.DiagnosticsActionBeamGeometry,
        InteractionAction.ProjectSetup => Strings.DiagnosticsActionProjectSetup,
        InteractionAction.AnnotationOffset => Strings.DiagnosticsActionAnnotationOffset,
        InteractionAction.SelfWeightVisibility => Strings.DiagnosticsActionSelfWeightVisibility,
        InteractionAction.Undo => Strings.DiagnosticsActionUndo,
        InteractionAction.Redo => Strings.DiagnosticsActionRedo,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    // Any thread. Entries arriving while a refresh is already queued are covered by that refresh.
    private void EntryRecorded()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _pending, 1) != 0) return;
        try { _post(Refresh); }
        catch (Exception) { Interlocked.Exchange(ref _pending, 0); }
    }

    private void Refresh()
    {
        // Release the queue slot first (full fence): an entry recorded after this point queues the next refresh,
        // and one recorded before it is already in the snapshot read below.
        Interlocked.Exchange(ref _pending, 0);
        if (Volatile.Read(ref _disposed) != 0) return;
        _entries = _performance!.GetEntries(); _rows = null;
        Notify(nameof(Rows)); Notify(nameof(VisibleRows)); Notify(nameof(HasEntries)); Notify(nameof(IsEmpty));
    }

    private IReadOnlyList<DiagnosticsRow> Build(IReadOnlyList<InteractionPerformanceEntry> entries)
    {
        var rows = new DiagnosticsRow[entries.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var entry = entries[entries.Count - 1 - i];
            rows[i] = new(TimeZoneInfo.ConvertTime(entry.StartedAtUtc, _zone).ToString("T", CultureInfo.CurrentCulture),
                ActionName(entry.Action), Milliseconds(entry.Preparation), Milliseconds(entry.Analysis),
                Milliseconds(entry.Presentation), Milliseconds(entry.UI), Milliseconds(entry.Total));
        }
        return rows;
    }

    private static string Milliseconds(TimeSpan duration) =>
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticsMilliseconds,
            duration.TotalMilliseconds.ToString("0.0", CultureInfo.CurrentCulture));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_performance is not null) _performance.EntryRecorded -= EntryRecorded;
    }
}
