using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopDocumentTests
{
    [Fact]
    public void DocumentWindowRendersSelectableMarkdownAndClosesOnEscape()
    {
        using var environment = new DesktopControlEnvironment();
        var paragraph = string.Concat(Enumerable.Repeat("A long sentence wraps inside the document viewport. ", 20)).TrimEnd();
        var sample = "# Document heading\n\n## Section heading\n\nIntroduction.\n\nAnother paragraph.\n\n" +
            "### Subsection heading\n\n- First item\n- Second item\n\n1. Numbered item\n\n" +
            string.Join("\n\n", Enumerable.Repeat(paragraph, 10));
        var window = new DocumentWindow("Window title", sample) { WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Window title", window.Title);
            var markdown = Assert.IsType<MarkdownScrollViewer>(window.Content);
            Assert.True(markdown.IsFocused);
            Assert.True(markdown.SelectionEnabled);
            Assert.True(markdown.Padding.Left > 0 && markdown.Padding.Top > 0);

            foreach (double width in new[] { window.Width, window.MinWidth })
            {
                window.Width = width;
                DesktopControlEnvironment.ArrangeSection(window, width, window.Height);
                var viewer = Assert.Single(markdown.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
                Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 0.5);
                var bar = Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(), b => b.IsVisible);
                Assert.Equal(Orientation.Vertical, bar.Orientation);

                var blocks = markdown.GetVisualDescendants().OfType<CTextBlock>().ToArray();
                var heading = Assert.Single(blocks, block => block.Classes.Contains("Heading1") && block.Text == "Document heading");
                var section = Assert.Single(blocks, block => block.Classes.Contains("Heading2") && block.Text == "Section heading");
                var subsection = Assert.Single(blocks, block => block.Classes.Contains("Heading3") && block.Text == "Subsection heading");
                var introduction = Assert.Single(blocks, block => block.Text == "Introduction.");
                var paragraphBeforeList = Assert.Single(blocks, block => block.Text == "Another paragraph.");
                var firstItem = Assert.Single(blocks, block => block.Text == "First item");
                var secondItem = Assert.Single(blocks, block => block.Text == "Second item");
                var body = blocks.First(block => block.Text == paragraph);
                Assert.True(heading.FontSize > section.FontSize);
                Assert.True(section.FontSize > subsection.FontSize && subsection.FontSize > body.FontSize);
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
                Assert.Equal(FontWeight.SemiBold, section.FontWeight);
                Assert.Equal(FontWeight.SemiBold, subsection.FontWeight);
                Assert.Equal(AppTypography.Family, body.FontFamily);
                Assert.Equal(TextWrapping.Wrap, body.TextWrapping);
                Assert.True(body.Bounds.Height > body.FontSize * 2);

                var list = firstItem.FindAncestorOfType<Grid>();
                Assert.NotNull(list);
                Assert.Equal(new[] { "•", "•", "1." }, blocks.Where(block => block.Classes.Contains("ListMarker"))
                    .Select(block => Assert.IsType<CRun>(Assert.Single(block.Content)).Text));
                double paragraphGap = paragraphBeforeList.Bounds.Top - introduction.Bounds.Bottom;
                double precedingGap = subsection.Bounds.Top - paragraphBeforeList.Bounds.Bottom;
                double followingGap = list.Bounds.Top - subsection.Bounds.Bottom;
                Assert.True(precedingGap > paragraphGap && precedingGap > followingGap);
                Assert.True(list.Bounds.Left > paragraphBeforeList.Bounds.Left);
                var first = firstItem.TranslatePoint(default, list)!.Value;
                var second = secondItem.TranslatePoint(default, list)!.Value;
                double itemGap = second.Y - first.Y - firstItem.Bounds.Height;
                Assert.True(itemGap > 0 && itemGap < paragraphGap);
                Assert.Equal(first.X, second.X, 3);
                var marker = list.Children.OfType<CTextBlock>().First(block => block.Classes.Contains("ListMarker"));
                Assert.Equal(first.Y, marker.Bounds.Top, 3);
                Assert.True(first.X > marker.Bounds.Right);
            }

            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            markdown.RaiseEvent(escape);
            Assert.True(escape.Handled);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [Fact]
    public void IndentedMarkdownExamplesUseDocumentColorsAndWrapAtMinimumWidth()
    {
        using var environment = new DesktopControlEnvironment();
        var example = string.Concat(Enumerable.Repeat("An example notice retains its text while wrapping. ", 6)).TrimEnd();
        var window = new DocumentWindow("Examples", $"# Heading\n\n    {example}\n\nFollowing paragraph.")
        {
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        try
        {
            window.Show();
            DesktopControlEnvironment.ArrangeSection(window, window.MinWidth, window.Height);
            var markdown = Assert.IsType<MarkdownScrollViewer>(window.Content);
            var border = Assert.Single(markdown.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("CodeBlock"));
            var text = Assert.Single(border.GetVisualDescendants().OfType<TextBlock>(), b => b.Classes.Contains("CodeBlock"));
            var app = Application.Current!;
            Assert.Equal(example, text.Text!.TrimEnd());
            Assert.Equal(app.FindResource("SurfaceSubtle"), border.Background);
            Assert.Equal(app.FindResource("TextPrimary"), text.Foreground);
            Assert.Equal(AppTypography.Family, text.FontFamily);
            var scroll = Assert.Single(border.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.True(text.Bounds.Height > text.FontSize * 2, $"Text bounds {text.Bounds}, scroll viewport {scroll.Viewport}");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData("en-US", "What's new", "Release notes", "Privacy")]
    [InlineData("de-DE", "Was ist neu?", "Versionshinweise", "Datenschutz")]
    public void HelpAndAboutOpenTheExpectedLocalizedDocuments(string culture, string whatsNew, string notesHeading, string privacy)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var files = new Files();
        var main = new MainWindow(TestHosts.Initialized(new DesktopApplicationHost(new(files, new(files, "settings"), UserSettings.Default))));
        main.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var menu = main.FindControl<Menu>("MainMenu")!;
            var help = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, Strings.Help));
            var notes = main.FindControl<MenuItem>("WhatsNewMenuItem")!;
            Assert.Equal(new object?[] { Strings.UserManual, whatsNew, Strings.Diagnostics, null, Strings.About },
                help.Items.Select(item => (item as MenuItem)?.Header));
            Assert.Equal(whatsNew, notes.Header);
            notes.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var releaseNotes = ApplicationDocuments.ReadReleaseNotes();
            using var reader = new StringReader(releaseNotes);
            Assert.Equal($"# {notesHeading}", reader.ReadLine());
            CheckDocument(main, whatsNew, releaseNotes);

            var aboutMenuItem = Assert.Single(help.Items.OfType<MenuItem>(), item => Equals(item.Header, Strings.About));
            aboutMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var about = Assert.IsType<AboutWindow>(Assert.Single(main.OwnedWindows));
            var links = about.GetLogicalDescendants().OfType<Button>().ToArray();
            Assert.Equal(new[] { "GPL v3", privacy, "GitHub" }, links.Select(link => Assert.IsType<TextBlock>(link.Content).Text));
            links[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CheckDocument(about, ApplicationMetadata.License, ApplicationDocuments.ReadLicense());
            links[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CheckDocument(about, privacy, ApplicationDocuments.ReadPrivacy());
            about.Close();
        }
        finally
        {
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CheckDocument(Window owner, string title, string source)
    {
        var window = Assert.IsType<DocumentWindow>(Assert.Single(owner.OwnedWindows));
        Assert.Equal(title, window.Title);
        Assert.Equal(source, Assert.IsType<MarkdownScrollViewer>(window.Content).Markdown);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(owner.OwnedWindows);
    }
}
