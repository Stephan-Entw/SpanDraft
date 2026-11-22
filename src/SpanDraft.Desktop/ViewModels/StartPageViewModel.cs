using System.Globalization;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class StartPageViewModel(AsyncActionCommand newCommand, AsyncActionCommand openCommand,
    AsyncActionCommand userManualCommand, Func<string, Task<bool>> openRecent, Func<bool> canOpen,
    AsyncActionCommand showRecoveryCommand) : ObservableObject
{
    public AsyncActionCommand NewCommand { get; } = newCommand;
    public AsyncActionCommand OpenCommand { get; } = openCommand;
    public AsyncActionCommand UserManualCommand { get; } = userManualCommand;
    public AsyncActionCommand ShowRecoveryCommand { get; } = showRecoveryCommand;
    public int RecoveryCount { get; private set; }
    /// <summary>Permanent notice while undecided recovery exists; the Recovery Center is one click away.</summary>
    public bool HasRecovery => RecoveryCount > 0;
    public string RecoveryHint => RecoveryCount == 1 ? Strings.RecoveryHintOne
        : string.Format(CultureInfo.CurrentUICulture, Strings.RecoveryHintMany, RecoveryCount);
    public IReadOnlyList<RecentProjectItemViewModel> RecentProjects { get; private set; } = [];
    public bool IsEmpty => RecentProjects.Count == 0;

    internal void SetRecentProjects(IReadOnlyList<RecentProjectEntry> entries)
    {
        RecentProjects = entries.Select(e => new RecentProjectItemViewModel(e, openRecent, canOpen)).ToArray();
        Notify(nameof(RecentProjects)); Notify(nameof(IsEmpty));
    }

    internal void SetRecoveryCount(int count)
    {
        if (RecoveryCount == count) return;
        RecoveryCount = count;
        Notify(nameof(RecoveryCount)); Notify(nameof(HasRecovery)); Notify(nameof(RecoveryHint));
    }

    internal void Refresh()
    {
        Notify(nameof(RecoveryHint));
        foreach (var item in RecentProjects) item.Refresh();
    }
}

public sealed class RecentProjectItemViewModel(RecentProjectEntry entry,
    Func<string, Task<bool>> open, Func<bool> canOpen) : ObservableObject
{
    public string Name => Path.GetFileNameWithoutExtension(entry.Path);
    public string FilePath => entry.Path;
    public string AutomationName => Name + ", " + FilePath;
    public string LastUsedLabel => FormatLastUsed(entry.LastUsedUtc, DateTimeOffset.Now);
    public AsyncActionCommand OpenCommand { get; } = new(() => open(entry.Path), canOpen);

    public static string FormatLastUsed(DateTimeOffset lastUsedUtc, DateTimeOffset now)
    {
        var local = lastUsedUtc.ToLocalTime();
        var today = now.ToLocalTime().Date;
        return local.Date == today ? Strings.Today + ", " + local.ToString("t", CultureInfo.CurrentCulture)
            : local.Date == today.AddDays(-1) ? Strings.Yesterday : local.ToString("d", CultureInfo.CurrentCulture);
    }

    internal void Refresh() { OpenCommand.Refresh(); Notify(nameof(LastUsedLabel)); }
}
