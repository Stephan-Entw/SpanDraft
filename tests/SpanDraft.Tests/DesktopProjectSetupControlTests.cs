using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopProjectSetupControlTests
{
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public async Task AllSetupStatesRenderWithReachableActionsAndNoHorizontalOverflow(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var files = new Files(); var dialogs = new Dialogs();
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, materialStore: new(files, "materials"), sectionStore: new(files, "sections"));
        var builtin = main.BuiltInMaterials!.Find("X5CrNi18-10")!;
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new([new(builtin.Material, builtin.Category, builtin.MaterialNumber, ["AISI 304", "UNS S30400"])]));
        files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("My custom angle", new AngleSectionGeometry(M(.1), M(.2), M(.005), M(0)))]));
        await main.InitializeLibrariesAsync();
        // Use the production shell and its menu in an inert window, without starting local file/recovery I/O.
        var shell = new MainWindow(); var content = shell.Content; shell.Content = null;
        var window = new Window { DataContext = main, Content = content, Width = width, Height = height };
        foreach (var template in shell.DataTemplates) window.DataTemplates.Add(template);
        window.Show();
        Arrange(window, width, height);
        Capture("create");
        Assert.Equal(2, window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("selectionSurface")));
        var setupView = Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
        Assert.Equal("y-y", main.Setup.SelectedAxisChoice.Label);
        main.Setup.BendingAxis = SectionAxisDesignation.Z; Capture("create-z");
        Assert.Equal("z-z", setupView.FindControl<ComboBox>("AxisPicker")!.SelectedItem is AxisChoice choice ? choice.Label : "");
        main.Setup.ShowMaterials(); Capture("material-selection-builtins");
        ScrollBottom(); Capture("material-selection-user");
        var materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        Assert.False(materials.HasCurrent);
        Assert.Equal("X5CrNi18-10 · 1.4301 · AISI 304 · UNS S30400", materials.UserEntries.Single().Caption);
        materials.BuiltInGroups[0].Entries[0].EditCommand.Execute(null); Capture("builtin-customize");
        var material = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep);
        Assert.True(material.CanConfirm);
        material.SaveToLibrary = true; material.Name = "Custom sample"; Capture("material-save");
        main.Setup.Escape(); materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        materials.UserEntries.Single().EditCommand.Execute(null); Capture("user-material-edit");
        Assert.True(Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep).IsLibraryEdit);
        main.Setup.Escape(); main.Setup.ShowOverview(); main.Setup.ApplyCommand.Execute(null); main.EditProject();
        Capture("edit");
        main.Setup.ShowMaterials(); Capture("current-project-material");
        materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep); materials.EditCurrentCommand.Execute(null);
        Capture("current-material-edit"); main.Setup.Escape();
        main.Setup.ShowSections(); Capture("section-selection");
        var sections = Assert.IsType<SectionSelectionViewModel>(main.Setup.CurrentStep);
        sections.UserEntries.Single().EditCommand.Execute(null); Capture("angle-preset-edit");
        var editor = Assert.IsType<SectionEditorViewModel>(main.Setup.CurrentStep);
        Assert.Equal(new[] { "u-u", "v-v" }, editor.Axes.Select(a => a.Label));
        Assert.Contains(editor.Axes, a => a.IsAsymmetric);
        main.Setup.Escape(); sections = Assert.IsType<SectionSelectionViewModel>(main.Setup.CurrentStep);
        sections.UserEntries.Single().UseCommand.Execute(null); main.Setup.BendingAxis = SectionAxisDesignation.V; Capture("overview-v");
        main.Setup.ShowSections(); sections = Assert.IsType<SectionSelectionViewModel>(main.Setup.CurrentStep);
        sections.EditCurrentCommand.Execute(null); Capture("rhs-editor");
        editor = Assert.IsType<SectionEditorViewModel>(main.Setup.CurrentStep);
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.TSection);
        foreach (var p in editor.Parameters) p.Text = p.Key switch { "h" => "200", "b" => "100", "tw" => "5", "tf" => "10", _ => "0" };
        Capture("t-section-w-plus-minus");
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Manual);
        foreach (var config in new[] { ManualAxisConfiguration.YZ, ManualAxisConfiguration.UV })
        {
            editor.Configuration = editor.Configurations.Single(c => c.Value == config);
            foreach (var p in editor.Parameters) p.Text = p.Key == "A" ? "1000" : p.Key.StartsWith('I') ? "1000000" : "20000";
            Capture("manual-" + config); ScrollBottom(); Capture("manual-" + config + "-bottom");
        }
        editor.SaveToLibrary = true; editor.PresetName = "Manual preset"; Capture("manual-preset-save");
        files.FailWritePath = "sections"; Assert.False(await editor.ConfirmAsync()); Capture("section-save-error");
        ScrollBottom(); Capture("section-save-error-bottom");
        editor.Parameters[0].Text = "0"; Assert.False(editor.IsValid); Assert.Empty(editor.Axes); Capture("manual-invalid");
        main.Setup.ShowMaterials(); Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null);
        Capture("material-new-invalid");
        files.Data["materials"] = [123]; files.Data["sections"] = [123];
        main = new MainWindowViewModel(files: files, dialogs: dialogs, materialStore: new(files, "materials"), sectionStore: new(files, "sections"),
            catalogLoader: () => throw new LibraryFormatException("Injected"));
        await main.InitializeLibrariesAsync(); window.DataContext = main; Capture("create-unavailable");
        main.Setup.ShowMaterials(); Capture("materials-unavailable");
        Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null); Capture("material-oneoff-unavailable");
        main.Setup.ShowSections(); Capture("sections-unavailable");
        Assert.IsType<SectionSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null); Capture("section-oneoff-unavailable");

        void ScrollBottom()
        {
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
                scroll.Offset = new(0, scroll.Extent.Height);
        }
        void Capture(string name)
        {
            if (!name.EndsWith("-bottom", StringComparison.Ordinal) && name != "material-selection-user")
                foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.FindAncestorOfType<TextBox>() is null)) scroll.Offset = default;
            Arrange(window, width, height);
            var root = (Control)window.Content!;
            Assert.Equal(width, root.Bounds.Width); Assert.Equal(height, root.Bounds.Height);
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, name + ": horizontal overflow");
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Text?.Length > 0))
                Assert.True(text.Bounds.Height + 1 >= text.TextLayout.Height, name + ": clipped text: " + text.Text);
            var interactive = window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible &&
                (c is TextBox || c is ComboBox || c is CheckBox || c is Button { Command: not null } && c.FindAncestorOfType<ProjectSetupView>() is not null));
            Assert.All(interactive, c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)), name + ": missing automation name"));
            var setup = window.GetVisualDescendants().OfType<ProjectSetupView>().Single();
            var action = setup.GetVisualDescendants().OfType<Button>().Last(b => b.IsEffectivelyVisible && b.Classes.Contains("primary"));
            var point = action.TranslatePoint(default, setup)!.Value;
            Assert.InRange(point.Y, 0, setup.Bounds.Height - action.Bounds.Height + 1);
            if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            root.InvalidateVisual();
            foreach (var visual in root.GetVisualDescendants()) visual.InvalidateVisual();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
            bitmap.Render((Control)window.Content!);
            bitmap.Save(Path.Combine(directory, $"setup-{culture}-{width}x{height}-{name}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ActualDeleteConfirmationOnlyAcceptsTheExplicitDeleteButton(bool material)
    {
        using var environment = new DesktopControlEnvironment();
        var owner = new Window { Width = 1100, Height = 650 }; owner.Show();
        var type = typeof(MainWindow).Assembly.GetType("SpanDraft.Desktop.ProjectDialogs")!;
        var dialogs = (IProjectDialogs)Activator.CreateInstance(type, [owner])!;
        foreach (var action in new[] { "escape", "cancel", "delete" })
        {
            var pending = material ? dialogs.ConfirmMaterialDeleteAsync("Mine") : dialogs.ConfirmSectionDeleteAsync("Mine");
            var dialog = Assert.Single(owner.OwnedWindows); Arrange(dialog, 520, 300);
            var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string).ToArray();
            Assert.True(buttons[0].IsDefault); Assert.False(buttons[1].IsDefault);
            Assert.Equal(Strings.Cancel, buttons[0].Content); Assert.Equal(Strings.Delete, buttons[1].Content);
            if (action == "escape") dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else buttons[action == "cancel" ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(action == "delete", await WaitFor(pending));
        }
        owner.Close();
    }

    [Fact]
    public async Task EnterOnlyConfirmsValidDraftAndEscapeReturnsOneSetupLevel()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); var view = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        main.Setup.ShowMaterials(); Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null);
        Arrange(window, 1100, 650);
        var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.Name == "MaterialNameInput");
        Assert.True(input.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, input, NavigationDirection.Next));
        Assert.IsType<ComboBox>(Assert.Single(view.GetVisualDescendants().OfType<Control>(), c => c.IsFocused));
        Assert.True(DesktopControlEnvironment.MoveFocus(window, view.GetVisualDescendants().OfType<Control>().Single(c => c.IsFocused), NavigationDirection.Previous));
        Assert.True(input.IsFocused);
        Press(Key.Enter); Assert.Equal(ProjectSetupPage.MaterialEditor, main.Setup.Page); Assert.Null(main.Editor);
        var editor = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep);
        editor.Name = "Draft"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "235";
        Press(Key.Enter); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page); Assert.Null(main.Editor);
        main.Setup.ShowSections(); Assert.IsType<SectionSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null);
        Arrange(window, 1100, 650); Press(Key.Escape); Assert.Equal(ProjectSetupPage.SectionSelection, main.Setup.Page);
        Press(Key.Escape); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page);
        Press(Key.Escape); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page); Assert.Null(main.Editor);
        await main.Setup.ConfirmAsync(); Assert.NotNull(main.Editor);
        void Press(Key key) => (view.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsFocused) ?? view)
            .RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }

    private static void Arrange(Window window, double width, double height)
    {
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            foreach (var child in content.GetVisualDescendants().OfType<Control>()) { child.InvalidateMeasure(); child.InvalidateArrange(); }
            content.InvalidateMeasure(); content.InvalidateArrange();
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
