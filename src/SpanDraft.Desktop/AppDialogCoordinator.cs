using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

/// <summary>The application-wide dialogs (§77).</summary>
internal enum AppDialogKind { Settings, About, WhatsNew }

/// <summary>
/// The application-wide dialogs (§77): at most one instance of each kind. Asking for one that is open activates it instead
/// (it keeps its owner); each is modal to its owner only, so other workspaces stay usable. Workspace-owned dialogs, pickers,
/// the report preview and Diagnostics are not managed here.
/// </summary>
/// <param name="refuses">True while no new application dialog may open (the application quits or shuts down).</param>
internal sealed class AppDialogCoordinator(Func<bool> refuses)
{
    private sealed record Entry(Window Dialog, Window Owner, Task Shown);

    private readonly Dictionary<AppDialogKind, Entry> _open = [];

    /// <summary>Raised once a dialog is no longer registered as open, so work it held back (queued activations) can resume.</summary>
    public event Action? Closed;

    // A dialog counts as open until its ShowDialog completed; the entry itself is removed by its own continuation.
    public Window? Find(AppDialogKind kind) => _open.TryGetValue(kind, out var entry) && !entry.Shown.IsCompleted ? entry.Dialog : null;

    /// <summary>Activates the open dialog of this kind; false if there is none.</summary>
    public bool Activate(AppDialogKind kind)
    {
        if (Find(kind) is not { } dialog) return false;
        dialog.Activate();
        return true;
    }

    /// <summary>
    /// Shows a dialog of this kind modally to <paramref name="owner"/> and completes when it is closed. An open one is
    /// activated instead, and nothing opens while the application quits.
    /// </summary>
    public async Task ShowAsync(AppDialogKind kind, Window owner, Func<Window> create)
    {
        if (Activate(kind) || refuses()) return;
        var dialog = create();
        var entry = new Entry(dialog, owner, dialog.ShowDialog(owner));
        _open[kind] = entry;
        try { await entry.Shown; }
        finally
        {
            if (_open.TryGetValue(kind, out var current) && ReferenceEquals(current, entry)) _open.Remove(kind);
            Closed?.Invoke();
        }
    }

    /// <summary>An application dialog is open over this window; an ordinary close of the window waits for it.</summary>
    public bool IsOwnedBy(Window owner) => _open.Values.Any(entry => ReferenceEquals(entry.Owner, owner) && !entry.Shown.IsCompleted);

    /// <summary>
    /// An application dialog that cannot be closed safely keeps the quit from starting: Settings while it saves or holds
    /// unconfirmed changes (they are never discarded silently). About and What's new can always be closed.
    /// </summary>
    public bool BlocksQuit([NotNullWhen(true)] out Window? blocker)
    {
        blocker = Find(AppDialogKind.Settings) is { DataContext: SettingsViewModel { IsSaving: true } or SettingsViewModel { HasChanges: true } } settings
            ? settings : null;
        return blocker is not null;
    }

    /// <summary>Closes every open application dialog for the quit and completes once all of them are closed.</summary>
    public async Task CloseForQuitAsync()
    {
        var open = _open.Values.ToList();
        foreach (var entry in open) entry.Dialog.Close();
        await Task.WhenAll(open.Select(static entry => entry.Shown));
    }
}
