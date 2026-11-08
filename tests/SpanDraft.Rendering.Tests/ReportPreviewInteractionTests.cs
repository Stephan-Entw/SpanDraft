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
public sealed class ReportPreviewInteractionTests : ProjectReportTestSupport
{

    [Fact]
    public void PreviewZoomUsesFixedStepsAndFitFollowsOnlyViewportChanges()
    {
        var (main, _) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.UpdateViewport(400, 600); double fit = preview.Zoom;
        Assert.True(preview.IsPageFit); Assert.True(fit > .25 && fit < .5);
        preview.ZoomInCommand.Execute(null); Assert.Equal(.5, preview.Zoom); Assert.False(preview.IsPageFit);
        preview.UpdateViewport(1200, 800); Assert.Equal(.5, preview.Zoom);
        preview.ShowPage(1); Assert.Equal(.5, preview.Zoom);
        preview.ZoomOutCommand.Execute(null); Assert.Equal(.25, preview.Zoom); Assert.False(preview.ZoomOutCommand.CanExecute(null));
        foreach (double step in new[] { .5, .75, 1, 1.25, 1.5, 2, 3, 4 })
        { preview.ZoomInCommand.Execute(null); Assert.Equal(step, preview.Zoom); Assert.Equal($"{step * 100:0} %", preview.ZoomLabel); }
        Assert.False(preview.ZoomInCommand.CanExecute(null));
        foreach (double step in new[] { 3, 2, 1.5, 1.25, 1, .75, .5, .25 })
        { preview.ZoomOutCommand.Execute(null); Assert.Equal(step, preview.Zoom); }
        preview.PageFitCommand.Execute(null); Assert.True(preview.IsPageFit); Assert.NotEqual(fit, preview.Zoom);
        preview.UpdateViewport(80, 100); Assert.True(preview.Zoom < .25); Assert.False(preview.ZoomOutCommand.CanExecute(null));
        preview.ZoomInCommand.Execute(null); Assert.Equal(.25, preview.Zoom);
        preview.UpdateViewport(5000, 7000); preview.PageFitCommand.Execute(null);
        Assert.True(preview.Zoom > 4); Assert.False(preview.ZoomInCommand.CanExecute(null));
        preview.ZoomOutCommand.Execute(null); Assert.Equal(4, preview.Zoom);
        main.CloseReport();
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        Assert.Equal(0, main.ReportPreview!.PageIndex); Assert.True(main.ReportPreview.IsPageFit);
        main.CloseReport();
    }

    [Theory]
    [InlineData("de-DE", "75", .75)] [InlineData("en-US", "75 %", .75)]
    [InlineData("de-DE", "125", 1.25)] [InlineData("en-US", " 125% ", 1.25)]
    [InlineData("de-DE", "87,5 %", .875)] [InlineData("en-US", "87.5", .875)]
    [InlineData("de-DE", "25", .25)] [InlineData("en-US", "400 %", 4)]
    public void InlineZoomCommitsManualPercentageWithoutMovingTheToolbar(string language, string text, double zoom)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, 1100, 650);
        try
        {
            var preview = main.ReportPreview!; var picture = preview.Picture;
            var value = view.FindControl<Grid>("ZoomValue")!;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            var center = view.FindControl<StackPanel>("PreviewControls")!;
            var bounds = new Rect(center.TranslatePoint(default, view)!.Value, center.Bounds.Size);
            Click(button); Dispatcher.UIThread.RunJobs();
            Assert.True(input.IsVisible); Assert.True(input.IsFocused); Assert.False(button.IsVisible);
            Assert.False(view.FindControl<TextBlock>("ZoomLabel")!.IsEffectivelyVisible);
            Assert.Equal(preview.ZoomLabel, input.SelectedText);
            Assert.Equal(value.Bounds.Width, input.Bounds.Width);
            var border = Assert.Single(input.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_BorderElement");
            Assert.Same(Application.Current!.FindResource("Accent"), border.BorderBrush);
            input.Text = text; Press(input, Key.Enter); Dispatcher.UIThread.RunJobs();
            Assert.Equal(zoom, preview.Zoom); Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Assert.Same(picture, preview.Picture); Assert.False(input.IsVisible); Assert.True(button.IsVisible);
            Assert.True(view.FindControl<TextBlock>("ZoomLabel")!.IsEffectivelyVisible);
            Assert.Equal(preview.ZoomLabel, view.FindControl<TextBlock>("ZoomLabel")!.Text);
            Assert.Equal(bounds, new Rect(center.TranslatePoint(default, view)!.Value, center.Bounds.Size));
            var label = view.FindControl<TextBlock>("ZoomLabel")!;
            var minus = view.FindControl<Button>("ZoomOutButton")!;
            var plus = view.FindControl<Button>("ZoomInButton")!;
            double midpoint = (minus.TranslatePoint(new(minus.Bounds.Width / 2, 0), value)!.Value.X
                + plus.TranslatePoint(new(plus.Bounds.Width / 2, 0), value)!.Value.X) / 2;
            Assert.Equal(midpoint, value.Bounds.Width / 2, .5);
            Assert.True(label.TranslatePoint(new(label.Bounds.Width, 0), value)!.Value.X <= value.Bounds.Width);
            preview.UpdateViewport(1000, 700); Assert.Equal(zoom, preview.Zoom);
            preview.ShowPage(1); Assert.Equal(zoom, preview.Zoom); Assert.False(main.Session!.IsDirty);
            preview.ZoomOutCommand.Execute(null);
            double lower = new[] { .25, .5, .75, 1, 1.25, 1.5, 2, 3, 4 }.LastOrDefault(step => step < zoom, zoom);
            Assert.Equal(lower, preview.Zoom);
            Wheel(window, view, new Point(400, 200), KeyModifiers.Control, 1);
            Assert.True(preview.Zoom > lower);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("")] [InlineData("abc")] [InlineData("NaN")] [InlineData("Infinity")]
    [InlineData("0")] [InlineData("-75")] [InlineData("24")] [InlineData("401")]
    [InlineData("75%%")] [InlineData("1,000")] [InlineData("1e2")]
    public void InvalidInlineZoomStaysEditableAndEscapeDiscardsWithoutClosingTheReport(string text)
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!; double zoom = preview.Zoom;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            Click(button); input.Text = text; Press(input, Key.Enter);
            Assert.Equal(zoom, preview.Zoom); Assert.True(preview.IsPageFit);
            Assert.True(input.IsFocused); Assert.True(input.IsVisible); Assert.Contains("error", input.Classes);
            Assert.Equal(Strings.ReportZoomRange, Avalonia.Automation.AutomationProperties.GetHelpText(input));
            Press(input, Key.Escape); Dispatcher.UIThread.RunJobs();
            Assert.Same(preview, main.ReportPreview); Assert.True(view.IsFocused);
            Assert.Equal(zoom, preview.Zoom); Assert.True(button.IsVisible); Assert.False(input.IsVisible);
            Assert.DoesNotContain("error", input.Classes);
            Click(button); input.Text = "125"; view.FindControl<Button>("PageFitButton")!.Focus();
            Assert.Equal(zoom, preview.Zoom); Assert.False(input.IsVisible);
            Click(button); input.Text = "125"; Press(input, Key.Escape);
            Assert.Equal(zoom, preview.Zoom); Assert.Same(preview, main.ReportPreview);
            Press(view, Key.Escape); Assert.Null(main.ReportPreview);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ZoomCommandsAndBusyStateDiscardPendingInputAndDoNotAllowStaleCommits()
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            var button = view.FindControl<Button>("ZoomValueButton")!;
            var input = view.FindControl<TextBox>("ZoomInput")!;
            Click(button); input.Text = "125"; preview.ZoomInCommand.Execute(null);
            Assert.False(input.IsVisible); double zoom = preview.Zoom;
            Assert.Equal(preview.ZoomLabel, view.FindControl<TextBlock>("ZoomLabel")!.Text);
            Click(button); input.Text = "125";
            var pending = new TaskCompletionSource(); var output = preview.OutputAsync(() => pending.Task);
            Assert.False(input.IsVisible); Assert.False(button.IsEffectivelyEnabled);
            Assert.False(preview.TrySetZoom("125")); Assert.Equal(zoom, preview.Zoom);
            pending.SetResult(); output.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsEffectivelyEnabled);
            main.CloseReport(); Assert.False(preview.TrySetZoom("125"));
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FitTracksMainWindowResizingWhileManualZoomStaysFixed(bool widthFit)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, 1100, 650);
        try
        {
            var preview = main.ReportPreview!;
            var fitCommand = widthFit ? preview.PageWidthCommand : preview.PageFitCommand;
            fitCommand.Execute(null); Dispatcher.UIThread.RunJobs(); double initial = preview.Zoom; CheckZoomLabel();
            Resize(1250, 800); Assert.True(preview.Zoom > initial);
            Assert.Equal(widthFit, preview.IsPageWidthFit); Assert.Equal(!widthFit, preview.IsPageFit);
            preview.ShowPage(1); Assert.Equal(widthFit, preview.IsPageWidthFit); CheckZoomLabel();
            preview.ZoomInCommand.Execute(null); Dispatcher.UIThread.RunJobs(); double manual = preview.Zoom; CheckZoomLabel();
            Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Resize(1100, 650); Assert.Equal(manual, preview.Zoom);
            fitCommand.Execute(null); Dispatcher.UIThread.RunJobs(); CheckZoomLabel();
            Assert.Equal(initial, preview.Zoom, .002);
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.True(preview.PageWidth + 32 <= scroll.Viewport.Width + 1);
            if (widthFit)
            {
                Assert.Equal(scroll.Viewport.Width - 32, preview.PageWidth, 1.0);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            else Assert.True(preview.PageHeight + 32 <= scroll.Viewport.Height + 1);
            Assert.All(new[] { "PageFitButton", "PageWidthButton" }, name =>
                Assert.DoesNotContain("selected", view.FindControl<Button>(name)!.Classes));
        }
        finally { window.Close(); }

        void CheckZoomLabel()
        {
            string text = view.FindControl<TextBlock>("ZoomLabel")!.Text!;
            Assert.Equal(main.ReportPreview!.ZoomLabel, text); Assert.EndsWith(" %", text);
            double percent = double.Parse(text[..^2], CultureInfo.CurrentUICulture);
            Assert.InRange(Math.Abs(percent - main.ReportPreview.Zoom * 100), 0, .5);
        }

        void Resize(int width, int height)
        {
            window.Width = width; window.Height = height;
            window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            content.InvalidateMeasure(); content.Measure(new(width, height));
            content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs(); CheckZoomLabel();
        }
    }

    [Fact]
    public void WidthFitUsesOnlyTheAvailableWidthAndRetainsItsModeAcrossPages()
    {
        var (main, _) = Open(ReportTestProjects.Create(true));
        Assert.True(main.ReportAsync().GetAwaiter().GetResult());
        var preview = main.ReportPreview!;
        preview.UpdateViewport(800, 600); preview.PageWidthCommand.Execute(null);
        double zoom = preview.Zoom;
        Assert.Equal(768, preview.PageWidth, .001); Assert.True(preview.PageHeight > 600);
        preview.UpdateViewport(800, 1000); Assert.Equal(zoom, preview.Zoom);
        preview.ShowPage(1); Assert.True(preview.IsPageWidthFit); Assert.Equal(zoom, preview.Zoom);
        preview.UpdateViewport(600, 1000); Assert.Equal(568, preview.PageWidth, .001);
        preview.ZoomOutCommand.Execute(null); Assert.False(preview.IsPageWidthFit);
        main.CloseReport();
    }

    [Fact]
    public void WheelScrollsVerticallyAndShiftScrollsHorizontallyWithoutChangingZoomOrPage()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 2) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            scroll.Offset = new(100, 150); Dispatcher.UIThread.RunJobs();
            Wheel(window, view, new(200, 200), KeyModifiers.None, -1);
            Assert.Equal(100, scroll.Offset.X); Assert.True(scroll.Offset.Y > 150);
            double y = scroll.Offset.Y;
            Wheel(window, view, new(200, 200), KeyModifiers.Shift, -1);
            Assert.True(scroll.Offset.X > 100); Assert.Equal(y, scroll.Offset.Y);
            Assert.Equal(2, preview.Zoom); Assert.Equal(0, preview.PageIndex);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void ControlWheelKeepsThePaperPointUnderTheMouseAcrossRapidZooms(double scaling)
    {
        using var scaled = new DesktopControlEnvironment(scaling);
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 2) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var picture = page.Page;
            scroll.Offset = new(100, 200); Dispatcher.UIThread.RunJobs();
            var pointer = new Point(300, 250);
            var local = scroll.TranslatePoint(pointer, page)!.Value;
            var relative = new Point(local.X / page.Bounds.Width, local.Y / page.Bounds.Height);
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            Wheel(window, view, pointer, KeyModifiers.Control | KeyModifiers.Shift, 1, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, preview.Zoom); Assert.Same(picture, page.Page); Assert.Equal(0, preview.PageIndex);
            AssertAnchor();
            Wheel(window, view, pointer, KeyModifiers.Control, -1);
            Assert.Equal(3, preview.Zoom); AssertAnchor();

            void AssertAnchor()
            {
                var actual = page.TranslatePoint(new(relative.X * page.Bounds.Width, relative.Y * page.Bounds.Height), scroll)!.Value;
                Assert.Equal(pointer.X, actual.X, 1 / scaling + .01);
                Assert.Equal(pointer.Y, actual.Y, 1 / scaling + .01);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ControlWheelAccumulatesFractionsHonorsLimitsAndDropsPendingAnchorsOnPageChanges()
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            while (preview.Zoom < 1) preview.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            var pointer = new Point(500, 250);
            Wheel(window, view, pointer, KeyModifiers.Control, .4); Assert.Equal(1, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, .4); Assert.Equal(1, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, .2); Assert.Equal(1.25, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, 20); Assert.Equal(4, preview.Zoom);
            var offset = scroll.Offset;
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(offset, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, .4);
            Wheel(window, view, pointer, KeyModifiers.Control, -1); Assert.Equal(3, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, -20); Assert.Equal(.25, preview.Zoom);
            offset = scroll.Offset;
            Wheel(window, view, pointer, KeyModifiers.Control, -1); Assert.Equal(offset, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, -.4);
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(.5, preview.Zoom);
            Wheel(window, view, pointer, KeyModifiers.Control, 4, false);
            preview.ShowPage(1); Dispatcher.UIThread.RunJobs(); Assert.Equal(default, scroll.Offset);
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            var pending = new TaskCompletionSource(); var output = preview.OutputAsync(() => pending.Task);
            double zoom = preview.Zoom;
            Wheel(window, view, pointer, KeyModifiers.Control, 1); Assert.Equal(zoom, preview.Zoom);
            Assert.Equal(default, scroll.Offset);
            pending.SetResult(); Dispatcher.UIThread.RunJobs(); output.GetAwaiter().GetResult();
            Wheel(window, view, pointer, KeyModifiers.Control, 1, false);
            main.CloseReport(); Dispatcher.UIThread.RunJobs(); Assert.Null(main.ReportPreview);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ControlWheelFromFitAccountsForAppearingScrollbarsAndCenteredPaper(bool widthFit)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            var preview = main.ReportPreview!;
            if (widthFit) preview.PageWidthCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            if (widthFit) { scroll.Offset = new(0, 100); Dispatcher.UIThread.RunJobs(); }
            var pointer = new Point(scroll.Viewport.Width / 2, 250);
            var local = scroll.TranslatePoint(pointer, page)!.Value;
            var relative = new Point(local.X / page.Bounds.Width, local.Y / page.Bounds.Height);
            var picture = preview.Picture;
            Wheel(window, view, pointer, KeyModifiers.Control, 1);
            var actual = page.TranslatePoint(new(relative.X * page.Bounds.Width, relative.Y * page.Bounds.Height), scroll)!.Value;
            Assert.False(preview.IsPageFit); Assert.False(preview.IsPageWidthFit);
            Assert.Same(picture, preview.Picture); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.Equal(pointer.Y, actual.Y, 1.0);
            if (widthFit)
            {
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                Assert.Equal(pointer.X, actual.X, 1.0);
            }
            else
            {
                Assert.Equal(0, scroll.Offset.X);
                Assert.Equal(scroll.Viewport.Width / 2, actual.X, 1.0);
            }
        }
        finally { window.Close(); }
    }
}
