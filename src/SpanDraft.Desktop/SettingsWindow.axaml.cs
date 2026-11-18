using Avalonia.Controls;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this(new(UserSettings.Default, _ => Task.FromResult<string?>(null))) { }

    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Closing += (_, e) =>
        {
            if (model.IsSaving) e.Cancel = true;
            else model.DiscardPending();
        };
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
        if (!model.HasChanges || await model.ApplyAsync()) Close();
    }
}
