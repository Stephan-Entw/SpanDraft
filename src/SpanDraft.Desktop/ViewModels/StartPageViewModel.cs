using System.Globalization;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class StartPageViewModel(AsyncActionCommand newCommand, AsyncActionCommand openCommand,
    AsyncActionCommand userManualCommand, Func<string, Task<bool>> openRecent, Func<bool> canOpen) : ObservableObject
{
    public AsyncActionCommand NewCommand { get; } = newCommand;
    public AsyncActionCommand OpenCommand { get; } = openCommand;
    public AsyncActionCommand UserManualCommand { get; } = userManualCommand;
    public IReadOnlyList<RecentProjectItemViewModel> RecentProjects { get; private set; } = [];
    public bool IsEmpty => RecentProjects.Count == 0;

    internal void SetRecentProjects(IReadOnlyList<RecentProjectEntry> entries)
    {
        RecentProjects = entries.Select(e => new RecentProjectItemViewModel(e, openRecent, canOpen)).ToArray();
        Notify(nameof(RecentProjects)); Notify(nameof(IsEmpty));
    }

    internal void Refresh()
    {
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
