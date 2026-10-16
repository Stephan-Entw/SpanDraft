using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopShellCommandTests
{
    [Fact]
    public async Task FileCommandsFollowWorkspaceAndSessionForCommandsAndDirectCalls()
    {
        var files = new Files(); var dialogs = new Dialogs { OpenPath = TestPath("initial.spandraft") };
        files.Data[dialogs.OpenPath] = SpanDraft.Desktop.Persistence.ProjectFileCodec.Serialize(State());
        var main = new MainWindowViewModel(files: files, dialogs: dialogs);
        Check(false, true, false, false);
        Assert.False(await main.NewAsync()); Assert.False(await main.SaveAsync());
        Assert.True(await main.OpenAsync()); Check(true, true, true, false);
        main.EditProject(); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.NewAsync()); Assert.False(await main.SaveAsync());
        main.Setup.ShowMaterials(); Assert.Equal(MainWorkspaceKind.MaterialSelection, main.Workspace); Check(false, false, false, false);
        var selection = (MaterialSelectionViewModel)main.Setup.CurrentStep!;
        int workspaceNotifications = 0; main.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(main.Workspace)) workspaceNotifications++; };
        selection.NewCommand.Execute(null); Assert.Equal(MainWorkspaceKind.MaterialEditor, main.Workspace);
        Assert.True(workspaceNotifications > 0); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.SaveAsync());
        main.Setup.Escape(); main.Setup.ShowOverview(); main.Setup.ShowSections();
        Assert.Equal(MainWorkspaceKind.SectionDefinition, main.Workspace); Check(false, false, false, false);
        main.Setup.Definition!.SelectForm(SectionEditorType.Circle); Check(false, false, false, true);
        Assert.True(main.Undo()); Assert.True(main.CanRedo);
        main.Setup.Escape(); main.Setup.CancelCommand.Execute(null); Check(true, true, true, false);
        Assert.True(await main.NewAsync()); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.SaveAsync(saveAs: true));
        void Check(bool create, bool open, bool save, bool undo)
        {
            Assert.Equal(create, main.NewCommand.CanExecute(null)); Assert.Equal(open, main.OpenCommand.CanExecute(null));
            Assert.Equal(save, main.SaveCommand.CanExecute(null)); Assert.Equal(save, main.SaveAsCommand.CanExecute(null));
            Assert.Equal(undo, main.UndoCommand.CanExecute(null));
        }
    }
    [Fact]
    public async Task BusyDisablesFileAndHistoryCommandsAndRestoresThemAfterwards()
    {
        var files = new Files(); var dialogs = new Dialogs();
        var main = new MainWindowViewModel(files: files, dialogs: dialogs); Assert.True(await main.Setup.ConfirmAsync());
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeWrite = (_, _, _) => pending.Task;
        var save = main.SaveAsync(); Assert.True(main.IsWorkspaceBusy);
        Assert.False(main.NewCommand.CanExecute(null)); Assert.False(main.OpenCommand.CanExecute(null));
        Assert.False(main.SaveCommand.CanExecute(null)); Assert.False(main.UndoCommand.CanExecute(null));
        pending.SetResult(); Assert.True(await save); Assert.True(main.SaveCommand.CanExecute(null));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KeyboardHistoryRoutesToTheVisibleWorkspaceAndTextBoxesKeepNativeUndo(bool section)
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); main.Setup.ApplyCommand.Execute(null);
        var session = main.Session!;
        if (section)
        {
            main.EditProject(); main.Setup.ShowSections(); main.Setup.Definition!.SelectForm(SectionEditorType.Circle);
        }
        else
        {
            var input = main.Editor!.DimensionLength; input.Begin(); input.Text = "2000"; Assert.True(input.Confirm());
        }
        var shell = new MainWindow();
        typeof(MainWindow).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, main);
        typeof(MainWindow).GetField("_initializing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, false);
        var text = new TextBox { Text = "", Width = 200, Height = 32, IsUndoEnabled = true };
        var host = new Window { Content = text, Width = 500, Height = 200 }; host.Show();
        host.Measure(new Size(500, 200)); host.Arrange(new Rect(0, 0, 500, 200));
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.True(text.Focus());
        text.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "unconfirmed" });
        Assert.Equal("unconfirmed", text.Text); Assert.True(text.CanUndo);
        var revision = session.CurrentRevision;
        var key = Send(Key.Z, primary); Assert.False(key.Handled); Assert.True(main.CanUndo);
        text.RaiseEvent(key); Assert.Equal("", text.Text); Assert.Same(revision, session.CurrentRevision);
        Assert.True(main.CanUndo);
        var redo = Send(Key.Z, primary | KeyModifiers.Shift); Assert.False(redo.Handled);
        text.RaiseEvent(redo); Assert.Equal("unconfirmed", text.Text);
        if (OperatingSystem.IsWindows()) Assert.False(Send(Key.Y, KeyModifiers.Control).Handled);
        text.IsReadOnly = true;
        Assert.True(Send(Key.Z, primary).Handled); Assert.True(main.CanRedo);
        Assert.True(Send(Key.Z, primary | KeyModifiers.Shift).Handled); Assert.True(main.CanUndo);
        if (OperatingSystem.IsWindows()) { Send(Key.Z, primary); Assert.True(Send(Key.Y, KeyModifiers.Control).Handled); }
        host.Close(); shell.Close();
        KeyEventArgs Send(Key key, KeyModifiers modifiers)
        {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            typeof(MainWindow).GetMethod("ProjectKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [shell, e]);
            return e;
        }
    }
    [Fact]
    public void MainMenuProtectsSectionNumericAndNameDraftsOnFocusLoss()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); main.Setup.ShowSections(); var vm = main.Setup.Definition!;
        var view = new SpanDraft.Desktop.Views.SectionDefinitionView { DataContext = vm };
        var host = new Window { Content = view, Width = 1100, Height = 650 }; host.Show();
        host.Measure(new Size(1100, 650)); host.Arrange(new Rect(0, 0, 1100, 650));
        var field = vm.Parameters.First(); var original = field.ConfirmedValue; field.PendingText = "150";
        main.SetMainMenuOpen(true);
        var input = new TextBox { DataContext = field };
        typeof(SpanDraft.Desktop.Views.SectionDefinitionView).GetMethod("InputLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [input, new RoutedEventArgs()]);
        Assert.Equal(original, field.ConfirmedValue); Assert.True(field.IsPending);
        vm.BeginNameEdit(); vm.PendingName = "Pending name";
        typeof(SpanDraft.Desktop.Views.SectionDefinitionView).GetMethod("NameLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [view, new RoutedEventArgs()]);
        Dispatcher.UIThread.RunJobs(); Assert.True(vm.IsEditingName); Assert.NotEqual("Pending name", vm.DisplayName);
        main.SetMainMenuOpen(false); host.Close();
    }
    [Theory]
    [InlineData("de-DE", "Benutzerhandbuch")] [InlineData("en-US", "User Manual (German)")]
    public async Task ManualLauncherUsesLocalFileAndReportsFailuresWithoutLaunchingWhenMissing(string culture, string title)
    {
        using var scope = new UiCultureScope(culture); Assert.Equal(title, Strings.UserManual);
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-manual-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Help"));
        try
        {
            int calls = 0; Uri? opened = null;
            var launcher = new UserManualLauncher(uri => { calls++; opened = uri; return Task.FromResult(true); }, directory);
            Assert.False(await launcher.OpenAsync()); Assert.Equal(0, calls);
            string path = Path.Combine(directory, "Help", "index.html"); await File.WriteAllTextAsync(path, "<html></html>", TestContext.Current.CancellationToken);
            Assert.True(await launcher.OpenAsync()); Assert.True(opened!.IsFile); Assert.Equal(path, opened.LocalPath);
            Assert.False(await new UserManualLauncher(_ => Task.FromResult(false), directory).OpenAsync());
            Assert.False(await new UserManualLauncher(_ => throw new IOException("failure"), directory).OpenAsync());
        }
        finally { Directory.Delete(directory, true); }
    }
}
