using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

/// <summary>Only this adapter knows native pickers and owned modal windows.</summary>
internal sealed class ProjectDialogs(Window owner) : IProjectDialogs
{
    private static FilePickerFileType ProjectType => new(Strings.ProjectFileFilter) { Patterns = ["*.spandraft"] };

    public async Task<string?> PickOpenPathAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        { Title = Strings.Open, AllowMultiple = false, FileTypeFilter = [ProjectType] });
        if (files.Count == 0) return null;
        using var file = files[0];
        return file.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly);
    }

    public async Task<string?> PickSavePathAsync(string? currentFilePath)
    {
        using var folder = currentFilePath is null ? null
            : await owner.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(currentFilePath)!);
        using var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = Strings.SaveAs, FileTypeChoices = [ProjectType], DefaultExtension = "spandraft",
            SuggestedFileName = currentFilePath is null ? Strings.Untitled + ".spandraft" : Path.GetFileName(currentFilePath),
            SuggestedStartLocation = folder, ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly);
    }

    public Task<LeaveDecision> ConfirmLeaveAsync(string projectName) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.LeaveQuestion, projectName),
        [(Strings.Save, LeaveDecision.Save), (Strings.DontSave, LeaveDecision.Discard), (Strings.Cancel, LeaveDecision.Cancel)]);

    public Task<RecoveryDecision> ConfirmRecoveryAsync(bool damaged, DateTimeOffset? writtenAtUtc) => damaged
        ? ShowAsync(Strings.RecoveryDiscardQuestion,
            [(Strings.Discard, RecoveryDecision.Discard), (Strings.Cancel, RecoveryDecision.Cancel)])
        : ShowAsync(string.Format(CultureInfo.CurrentUICulture, Strings.RecoveryQuestion,
            writtenAtUtc!.Value.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture)),
            [(Strings.Restore, RecoveryDecision.Restore), (Strings.Discard, RecoveryDecision.Discard)]);

    public async Task ShowErrorAsync(string message) => await ShowAsync(message, [(Strings.OK, true)]);

    public Task<bool> ConfirmMaterialDeleteAsync(string name) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.DeleteMaterialQuestion, name),
        [(Strings.Cancel, false), (Strings.Delete, true)]);
    public Task<bool> ConfirmSectionDeleteAsync(string name) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.DeleteSectionQuestion, name),
        [(Strings.Cancel, false), (Strings.Delete, true)]);

    private Task<T> ShowAsync<T>(string message, (string Label, T Result)[] choices)
    {
        var dialog = new Window
        {
            Title = Strings.ApplicationTitle, Width = 520, SizeToContent = SizeToContent.Height,
            CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, result) in choices)
        {
            var button = new Button { Content = label, IsDefault = buttons.Children.Count == 0 };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => dialog.Close(result);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 24,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 400 },
                buttons
            }
        };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(default(T)); } };
        return dialog.ShowDialog<T>(owner);
    }
}
