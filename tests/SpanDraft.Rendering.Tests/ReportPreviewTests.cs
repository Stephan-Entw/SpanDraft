using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Skia.Helpers;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Reporting;
using Xunit;

// Control rendering must stay on the owning Avalonia dispatcher thread.
#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ReportPreviewTests : ProjectReportTestSupport
{

    [Fact]
    public void ShellReportWorkspacePreservesSnapshotDirtyStateAndDraftsWithoutStayingBusy()
    {
        var state = ReportTestProjects.Create(); int analyses = 0;
        var (main, files) = Open(state, analyzed: () => analyses++);
        main.Editor!.SetAnnotationOffset(state.Document.Loads[0].Id, new(20, 10));
        main.Editor.DimensionLength.Begin(); main.Editor.DimensionLength.Text = "9000";
        var revision = main.Session!.CurrentRevision; var editor = main.Editor;
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = Assert.IsType<ReportPreviewViewModel>(main.CurrentViewModel);
        Assert.Equal(MainWorkspaceKind.ReportPreview, main.Workspace);
        Assert.False(main.IsWorkspaceBusy); Assert.False(editor.IsBusy); Assert.True(editor.PreserveDrafts);
        editor.DimensionLength.LoseFocus();
        Assert.Same(revision, main.Session.CurrentRevision); Assert.True(main.Session.IsDirty); Assert.Empty(files.Writes);
        Assert.Equal(state.Document.Length, main.Editor.Document.Length); Assert.Equal(1, analyses);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        Assert.False(main.CanNewProject); Assert.False(main.CanOpenProject); Assert.False(main.CanSaveProject);
        Assert.False(main.CanCloseProject); Assert.False(main.CanReport); Assert.False(main.CanUndo); Assert.False(main.CanRedo);
        Assert.False(main.NewAsync().GetAwaiter().GetResult()); Assert.False(main.OpenAsync("missing.spandraft").GetAwaiter().GetResult());
        Assert.False(main.SaveAsync().GetAwaiter().GetResult()); Assert.False(main.CloseProjectAsync().GetAwaiter().GetResult());
        Assert.False(main.ReportAsync().GetAwaiter().GetResult()); Assert.False(main.Undo()); Assert.False(main.Redo());
        Assert.Same(preview, main.ReportPreview);
        Assert.True(main.CloseReport()); Assert.Same(editor, main.CurrentViewModel); Assert.False(editor.IsReportOpen);
        Assert.Null(preview.Picture); Assert.Same(revision, main.Session.CurrentRevision);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        Assert.True(main.ReportAsync().GetAwaiter().GetResult()); Assert.NotSame(preview, main.ReportPreview);
        Assert.Equal(0, main.ReportPreview!.PageIndex); Assert.True(main.ReportPreview.IsPageFit);
        main.CloseReport();
        var unavailable = state with { Document = state.Document.WithSupports([]) };
        var (invalid, _) = Open(unavailable); Assert.False(invalid.CanReport); Assert.False(invalid.ReportAsync().GetAwaiter().GetResult());
        Assert.Throws<InvalidOperationException>(() => ProjectReport.Capture(invalid.Editor!, Now));
    }

    [Fact]
    public void PreviewNavigationAndPdfExportDoNotTouchProjectAndProtectProjectTargets()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.ShowPage(1);
        Assert.Equal("2 / 2", preview.PageLabel); Assert.False(preview.NextCommand.CanExecute(null));
        string destination = Path.GetFullPath("export.pdf");
        preview.SavePdfAsync(destination).GetAwaiter().GetResult(); Assert.True(files.Data.ContainsKey(destination));
        Assert.Equal(new[] { destination }, files.Writes);
        Assert.Throws<IOException>(() => preview.SavePdfAsync(main.Session!.FilePath!).GetAwaiter().GetResult());
        Assert.Throws<IOException>(() => preview.SavePdfAsync(Path.GetFullPath("export.txt")).GetAwaiter().GetResult());
        Assert.Equal(new[] { destination }, files.Writes);
        byte[] exported = files.Data[destination].ToArray(); files.FailWrites = true;
        Assert.Throws<IOException>(() => preview.SavePdfAsync(destination).GetAwaiter().GetResult());
        Assert.Equal(exported, files.Data[destination]);
        Assert.False(main.Session!.IsDirty); main.CloseReport();
    }

    [Fact]
    public void SinglePagePreviewDisablesBothNavigationCommands()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        Assert.Single(preview.Report.Pages);
        Assert.False(preview.PreviousCommand.CanExecute(null)); Assert.False(preview.NextCommand.CanExecute(null));
        preview.PreviousCommand.Execute(null); preview.NextCommand.Execute(null); Assert.Equal(0, preview.PageIndex);
        main.CloseReport();
    }

    [Fact]
    public void OutputGuardsWorkspaceAndRecoversAfterErrorsAndCancellation()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!; var original = preview.Picture;
            var pending = new TaskCompletionSource();
            var output = preview.OutputAsync(() => pending.Task);
            Assert.True(main.IsWorkspaceBusy); Assert.True(preview.IsBusy); Assert.True(main.Session!.IsBusy);
            Assert.False(main.CloseReport()); Assert.False(main.RequestCloseAsync().GetAwaiter().GetResult());
            Assert.False(preview.CloseCommand.CanExecute(null)); Assert.False(preview.ZoomInCommand.CanExecute(null));
            Assert.False(preview.NextCommand.CanExecute(null));
            Assert.False(view.FindControl<Button>("SavePdfButton")!.IsEffectivelyEnabled);
            Assert.False(window.FindControl<MenuItem>("QuitMenuItem")!.IsEffectivelyEnabled);
            preview.ShowPage(1); Assert.Same(original, preview.Picture);
            bool duplicate = false; preview.OutputAsync(() => { duplicate = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert.False(duplicate);
            pending.SetException(new IOException("Output failed")); Dispatcher.UIThread.RunJobs(); output.GetAwaiter().GetResult();
            Assert.False(main.IsWorkspaceBusy); Assert.False(main.Session.IsBusy);
            Assert.True(preview.HasError); Assert.Contains("Output failed", preview.Error);
            Assert.True(view.FindControl<TextBlock>("ErrorText")!.IsVisible);
            preview.OutputAsync(() => Task.CompletedTask).GetAwaiter().GetResult();
            Assert.False(preview.HasError); Assert.False(view.FindControl<TextBlock>("ErrorText")!.IsVisible);
            Assert.True(preview.CloseCommand.CanExecute(null)); Assert.Same(original, preview.Picture);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(LeaveDecision.Cancel)] [InlineData(LeaveDecision.Save)] [InlineData(LeaveDecision.Discard)]
    public void ApplicationCloseKeepsOrDisposesPreviewOnlyAfterTheLeaveDecision(LeaveDecision decision)
    {
        var state = ReportTestProjects.Create(); var files = new Files();
        var dialogs = new ProjectTestSupport.Dialogs { Leave = decision };
        string path = Path.GetFullPath("close-report.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(state);
        var main = new MainWindowViewModel(files: files, dialogs: dialogs);
        Assert.True(main.OpenAsync(path).GetAwaiter().GetResult());
        main.Editor!.SetAnnotationOffset(state.Document.Loads[0].Id, new(20, 10));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!; var picture = preview.Picture;
        Assert.Equal(decision != LeaveDecision.Cancel, main.RequestCloseAsync().GetAwaiter().GetResult());
        Assert.Equal(1, dialogs.LeaveQuestions);
        if (decision == LeaveDecision.Cancel)
        {
            Assert.Same(preview, main.CurrentViewModel); Assert.Same(picture, preview.Picture);
            Assert.True(main.Editor.IsReportOpen); Assert.True(main.Session!.IsDirty);
            Assert.Empty(files.Writes); main.CloseReport();
        }
        else
        {
            Assert.Null(main.ReportPreview); Assert.Null(preview.Picture);
            Assert.Equal(decision == LeaveDecision.Save, files.Writes.Count == 1);
            Assert.False(main.Editor.IsReportOpen);
        }
    }
}
