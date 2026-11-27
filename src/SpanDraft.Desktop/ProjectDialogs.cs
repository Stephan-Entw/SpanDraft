using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

/// <summary>Only this adapter knows native pickers and owned modal windows.</summary>
/// <param name="shutdownAuthorized">True once a system shutdown closes every window (owned editors must not keep theirs open).</param>
internal sealed class ProjectDialogs(Window owner, Func<bool>? shutdownAuthorized) : IProjectDialogs
{
    public ProjectDialogs(Window owner) : this(owner, null) { }

    private static FilePickerFileType ProjectType => new(Strings.ProjectFileFilter) { Patterns = ["*.spandraft"] };

    public async Task<string?> PickOpenPathAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        { Title = Strings.Open, AllowMultiple = false, FileTypeFilter = [ProjectType] });
        if (files.Count == 0) return null;
        using var file = files[0];
        return file.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly);
    }

    public async Task<string?> PickSavePathAsync(string? currentFilePath, string? calculationTitle)
    {
        using var folder = currentFilePath is null ? null
            : await owner.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(currentFilePath)!);
        using var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = Strings.SaveAs, FileTypeChoices = [ProjectType], DefaultExtension = "spandraft",
            SuggestedFileName = ProjectSaveFileName.Suggest(currentFilePath, calculationTitle, Strings.Untitled),
            SuggestedStartLocation = folder, ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly);
    }

    public Task<LeaveDecision> ConfirmLeaveAsync(string projectFileName) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.LeaveQuestion, projectFileName), DialogChoices.Leave());

    public Task<bool> ConfirmRecoveryRetryAsync(string reason) => ShowAsync(
        Strings.RecoveryUnavailable + "\n\n" + reason, DialogChoices.RecoveryUnavailable());

    public async Task ShowErrorAsync(string message) => await ShowAsync(message, DialogChoices.Acknowledge());

    public Task<bool> ConfirmRemoveMissingRecentAsync(string path) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.MissingRecentQuestion, path), DialogChoices.RemoveMissingRecent());

    public Task<bool> ConfirmDiscardPendingRequestsAsync(string? singleFileName, int count) => ShowAsync(
        singleFileName is not null
            ? string.Format(CultureInfo.CurrentUICulture, Strings.PendingRequestOneQuestion, singleFileName)
            : string.Format(CultureInfo.CurrentUICulture, Strings.PendingRequestManyQuestion, count),
        DialogChoices.DiscardPendingRequests());

    public Task<bool> ConfirmMaterialDeleteAsync(string displayName) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.DeleteMaterialQuestion, displayName), DialogChoices.Delete());
    public Task<bool> ConfirmSectionDeleteAsync(string name) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.DeleteSectionQuestion, name), DialogChoices.Delete());

    public Task ShowMaterialEditorAsync(MaterialEditorViewModel editor) =>
        new MaterialEditorWindow(editor, shutdownAuthorized) { Icon = owner.Icon }.ShowDialog(owner);

    public Task<MaterialNameConflictDecision> ResolveMaterialNameConflictAsync(string displayName) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.MaterialNameConflictQuestion, displayName), DialogChoices.MaterialNameConflict());

    public Task<bool> ConfirmMaterialReplaceAsync(string displayName) => ShowAsync(
        string.Format(CultureInfo.CurrentUICulture, Strings.ReplaceMaterialQuestion, displayName), DialogChoices.ReplaceMaterial());

    private Task<T> ShowAsync<T>(string message, IReadOnlyList<DialogChoice<T>> choices)
    {
        var (plan, dismissResult) = DialogConvention.Arrange(choices);
        var dialog = new Window
        {
            Title = Strings.ApplicationTitle, Width = 520, SizeToContent = SizeToContent.Height,
            CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon
        };
        dialog.Classes.Add("dialog");
        bool answered = false;
        var buttons = new StackPanel();
        buttons.Classes.Add("dialogButtons");
        foreach (var entry in plan)
        {
            var button = new Button { Content = entry.Label, IsDefault = entry.IsDefault, IsCancel = entry.IsCancel };
            button.Classes.Add(entry.StyleClass);
            AutomationProperties.SetName(button, entry.Label);
            button.Click += (_, _) => { answered = true; dialog.Close(entry.Result); };
            buttons.Children.Add(button);
        }
        var footer = new Border { Child = buttons };
        footer.Classes.Add("dialogFooter");
        Grid.SetRow(footer, 1);
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 400, Margin = new Thickness(24) },
                footer
            }
        };
        // Escape presses the Cancel button (IsCancel). The title bar button answers like Cancel as well, not with whatever
        // default(T) happens to be.
        dialog.Closing += (_, e) =>
        {
            if (answered || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true; answered = true;
            dialog.Close(dismissResult);
        };
        return dialog.ShowDialog<T>(owner);
    }
}
