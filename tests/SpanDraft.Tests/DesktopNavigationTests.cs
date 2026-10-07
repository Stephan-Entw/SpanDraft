using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SpanDraft.Analysis;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopNavigationTests
{
    [Fact]
    public void SectionTemplateAllowsClearedSelectionDuringSetupDetachment()
    {
        using var environment = new DesktopControlEnvironment();
        var view = new ProjectSetupView();
        var picker = view.FindControl<ComboBox>("SectionPicker")!;
        var placeholder = Assert.IsType<TextBlock>(picker.ItemTemplate!.Build(null));
        Assert.True(string.IsNullOrEmpty(placeholder.Text));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void CreateReplacesSetupWithCompleteEditorThroughWindowContentBinding(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            int analyses = 0;
            var model = new MainWindowViewModel(beam => { analyses++; return BeamAnalysis.Analyze(beam); });
            // Build the real window/XAML, but never show it or run startup recovery.
            var window = new MainWindow { DataContext = model };
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            var host = Assert.Single(((Grid)window.Content!).Children.OfType<ContentControl>());
            Assert.Same(model.Setup, host.Content);

            model.Setup.ApplyCommand.Execute(null);
            Layout(window);

            var editor = Assert.IsType<EditorViewModel>(model.CurrentViewModel);
            Assert.Same(editor, model.Editor);
            Assert.Same(editor.Session, model.Session);
            Assert.Same(editor, host.Content);
            Assert.Empty(window.GetVisualDescendants().OfType<ProjectSetupView>());
            var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
            Assert.Same(editor, view.DataContext);
            Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
            Assert.Single(view.GetVisualDescendants().OfType<BeamEditorSurface>());
            var canvas = Assert.Single(view.GetVisualDescendants().OfType<BeamCanvas>());
            Assert.NotNull(canvas.Scene);
            Assert.True(canvas.Bounds.Width > 0 && canvas.Bounds.Height > 0);
            Assert.Equal(6, view.GetVisualDescendants().OfType<ToggleButton>().Count(b => b.Command is not null));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == editor.Presentation.StatusText);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Strings.Results));
            Assert.True(editor.Session.IsDirty);
            Assert.Empty(editor.Session.UndoHistory);
            Assert.Equal(Strings.Untitled + "* — " + Strings.ApplicationTitle, window.Title);
            Assert.Equal(1, analyses);

            // Editing the project uses the same setup; cancelling must restore the editor too.
            model.EditProject();
            Layout(window);
            Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
            model.Setup.CancelCommand.Execute(null);
            Layout(window);
            Assert.Same(editor, host.Content);
            Assert.Single(window.GetVisualDescendants().OfType<BeamCanvas>());
            Assert.Equal(1, analyses);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    private static void Layout(Window window)
    {
        window.ApplyTemplate();
        window.Measure(new Size(1250, 800));
        window.Arrange(new Rect(0, 0, 1250, 800));
        // The inert window is never shown; perform its content layout directly.
        var content = (Control)window.Content!;
        content.InvalidateMeasure();
        content.Measure(new Size(1250, 800));
        content.InvalidateArrange();
        content.Arrange(new Rect(0, 0, 1250, 800));
        if (window.GetVisualDescendants().OfType<EditorView>().SingleOrDefault() is { } editor)
        {
            editor.Measure(new Size(1250, 768));
            editor.Arrange(new Rect(0, 0, 1250, 768));
        }
    }

}
