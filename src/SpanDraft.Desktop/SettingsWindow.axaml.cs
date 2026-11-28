using Avalonia.Controls;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this(new(UserSettings.Default, _ => Task.FromResult<string?>(null))) { }

    /// <param name="shutdownAuthorized">True once a system shutdown closes every window: nothing may keep this one open.</param>
    public SettingsWindow(SettingsViewModel model, Func<bool>? shutdownAuthorized = null)
    {
        InitializeComponent();
        DataContext = model;
        Closing += (_, e) =>
        {
            // A platform shutdown only asks: the owner workspace decides; nothing here may block it or discard anything.
            if (Workspaces.PlatformCloseRequest.IsPlatform(e) || shutdownAuthorized?.Invoke() == true) return;
            if (model.IsSaving) e.Cancel = true;
        };
        // Unconfirmed changes end with the dialog itself, not with a close that may still be refused.
        Closed += (_, _) => model.DiscardPending();
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is not { } screen) return;
            double availableWidth = screen.WorkingArea.Width / screen.Scaling;
            double availableHeight = screen.WorkingArea.Height / screen.Scaling;
            MinWidth = Math.Min(MinWidth, availableWidth);
            MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
        };
    }

    private void CancelSettings(object? sender, RoutedEventArgs e) => Close();

    private async void ConfirmSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel model || model.IsSaving) return;
        if ((!model.HasChanges || await model.ApplyAsync()) && !model.IsApplicationShuttingDown) Close();
    }
}
