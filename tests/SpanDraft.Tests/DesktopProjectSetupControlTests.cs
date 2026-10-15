using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        Assert.Single(window.GetVisualDescendants().OfType<BeamSetupCard>());
        var setupView = Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
        Assert.Equal(Strings.BendingAxis + " y-y · " + Strings.SectionDefinitionEquivalent, main.Setup.AxisSummary);
        Assert.Equal(SectionAxisDesignation.Y, main.Setup.BendingAxis);
        main.Setup.BendingAxis = SectionAxisDesignation.Z; Capture("create-z");
        Assert.Equal(Strings.BendingAxis + " z-z · " + Strings.SectionDefinitionEquivalent, main.Setup.AxisSummary);
        Assert.Equal(SectionAxisDesignation.Z, main.Setup.BendingAxis);
        main.Setup.ShowMaterials(); Capture("material-selection-builtins");
        ScrollBottom(); Capture("material-selection-user");
        var materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        Assert.Null(materials.CurrentEntry);
        Assert.Equal("X5CrNi18-10", materials.UserEntries.Single().Name);
        Assert.Equal("1.4301", materials.UserEntries.Single().MaterialNumber); Assert.Equal("AISI 304 · UNS S30400", materials.UserEntries.Single().OtherStandards);
        materials.SelectedEntry = materials.BuiltInEntries[0]; materials.EditSelectedCommand.Execute(null); Capture("builtin-customize");
        var material = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep);
        Assert.True(material.CanConfirm);
        material.Name = "Custom sample"; Capture("material-save");
        main.Setup.Escape(); materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        materials.SelectedEntry = materials.UserEntries.Single(); materials.EditSelectedCommand.Execute(null); Capture("user-material-edit");
        Assert.True(Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep).IsLibraryEdit);
        main.Setup.Escape(); main.Setup.ShowOverview(); main.Setup.ApplyCommand.Execute(null); main.EditProject();
        Capture("edit");
        main.Setup.ShowMaterials(); Capture("current-project-material");
        materials = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep); materials.EditSelectedCommand.Execute(null);
        Capture("current-material-edit"); main.Setup.Escape();
        main.Setup.ShowSections(); Capture("section-definition");
        var editor=main.Setup.Definition!; editor.SelectForm(SectionEditorType.Angle);
        editor.LoadPreset(main.UserSections.All.Single()); editor.SelectedAxis=SectionAxisDesignation.V;
        Capture("angle-preset"); Assert.True(await editor.ConfirmAsync()); Capture("overview-v");
        main.Setup.ShowSections(); editor=main.Setup.Definition!; editor.SelectForm(SectionEditorType.TSection);
        Fill(editor); Capture("t-section-w-plus-minus"); editor.SelectForm(SectionEditorType.Manual);
        foreach (var config in new[] { ManualAxisConfiguration.YZ, ManualAxisConfiguration.UV })
        {
            editor.Configuration=editor.Configurations.Single(c=>c.Value==config); Fill(editor);
            Capture("manual-"+config); ScrollBottom(); Capture("manual-"+config+"-bottom");
        }
        editor.BeginNameEdit(); editor.PendingName="Manual preset"; editor.CommitName(); Capture("manual-preset-save");
        files.FailWritePath="sections"; Assert.False(await editor.SaveLibraryAsync()); Capture("section-save-error");
        ScrollBottom(); Capture("section-save-error-bottom");
        editor.Parameters[0].PendingText="0"; editor.Parameters[0].Commit(); Assert.False(editor.HasValidSection); Capture("manual-invalid");
        editor.CancelCommand.Execute(null);
        main.Setup.ShowMaterials(); Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null);
        Capture("material-new-invalid");
        files.Data["materials"] = [123]; files.Data["sections"] = [123];
        main = new MainWindowViewModel(files: files, dialogs: dialogs, materialStore: new(files, "materials"), sectionStore: new(files, "sections"),
            catalogLoader: () => throw new LibraryFormatException("Injected"));
        await main.InitializeLibrariesAsync(); window.DataContext = main; Capture("create-unavailable");
        main.Setup.ShowMaterials(); Capture("materials-unavailable");
        Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null); Capture("material-oneoff-unavailable");
        main.Setup.ShowSections(); Capture("sections-unavailable");
        var unavailable=main.Setup.Definition!; unavailable.SelectForm(SectionEditorType.Rectangle); Fill(unavailable); Capture("section-oneoff-unavailable");

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
        Assert.IsType<Button>(Assert.Single(view.GetVisualDescendants().OfType<Control>(), c => c.IsFocused));
        Assert.True(DesktopControlEnvironment.MoveFocus(window, view.GetVisualDescendants().OfType<Control>().Single(c => c.IsFocused), NavigationDirection.Previous));
        Assert.True(input.IsFocused);
        Press(Key.Enter); Assert.Equal(ProjectSetupPage.MaterialEditor, main.Setup.Page); Assert.Null(main.Editor);
        var editor = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep);
        editor.Name = "Draft"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "235";
        input.Focus(); Press(Key.Enter); Assert.Equal(ProjectSetupPage.MaterialEditor, main.Setup.Page);
        view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "MaterialValueInput").Focus();
        Press(Key.Enter); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page); Assert.Null(main.Editor);
        main.Setup.ShowSections();
        Arrange(window, 1100, 650); Press(Key.Escape); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page);
        Press(Key.Escape); Assert.Equal(ProjectSetupPage.Overview, main.Setup.Page); Assert.Null(main.Editor);
        await main.Setup.ConfirmAsync(); Assert.NotNull(main.Editor);
        void Press(Key key) => (view.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsFocused) ?? view)
            .RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }

    [Fact]
    public async Task CardsAndLengthUseNativeKeyboardActionsWithoutCreatingAProjectOnEnter()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); var view = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = view }; window.Show(); Arrange(window, 1100, 650);
        var card = view.FindControl<BeamSetupCard>("BeamCard")!;
        var length = card.FindControl<TextBox>("SetupLengthInput")!;
        var section = card.FindControl<Button>("SectionCardButton")!;
        var material = card.FindControl<Button>("MaterialCardButton")!;
        length.Focus(); length.Text = "1500"; Press(length, Key.Enter);
        Assert.Null(main.Session); Assert.Equal(1500, main.Setup.SelectedLength.Millimeters);
        length.Text = "1750"; Press(length, Key.Enter);
        Assert.Null(main.Session); Assert.Equal(1750, main.Setup.SelectedLength.Millimeters);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, length, NavigationDirection.Next)); Assert.True(section.IsFocused);
        Press(section, Key.Enter); Arrange(window, 1100, 650);
        Assert.Equal(ProjectSetupPage.SectionDefinition, main.Setup.Page);
        Assert.Empty(window.OwnedWindows); main.Setup.Escape(); Arrange(window, 1100, 650);
        Assert.True(section.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, section, NavigationDirection.Next)); Assert.True(material.IsFocused);
        material.Focus(); Press(material, Key.Space);
        material.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        Assert.Equal(ProjectSetupPage.MaterialSelection, main.Setup.Page);
        main.Setup.Escape(); Arrange(window, 1100, 650);
        Assert.True(material.IsFocused);
        // The inert window has no native mouse hit testing; invoke the button's
        // mouse activation path, which raises Click and executes its command.
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(section, null);
        Assert.True(main.Setup.IsSectionDefinition);
        main.Setup.Definition!.BeginNameEdit(); main.Setup.Definition.PendingName = new string('W', 250);
        main.Setup.Definition.CommitName(); Assert.True(await main.Setup.Definition.ConfirmAsync()); Arrange(window, 1100, 650);
        var description = card.FindControl<TextBlock>("SectionDescription")!;
        Assert.Equal(description.Text, ToolTip.GetTip(description)); Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, description.TextTrimming);
        Assert.Equal(card.FindControl<Border>("LengthSurface")!.Bounds.Height, section.Bounds.Height);
        Assert.Equal(section.Bounds.Height, material.Bounds.Height);
        Assert.InRange(card.FindControl<Border>("BeamCardSurface")!.Bounds.Height, 110, 120);
        Assert.InRange(section.Bounds.Height, 85, 95);
        var normal = section.Background;
        ((IPseudoClasses)section.Classes).Set(":pointerover", true);
        Assert.NotEqual(normal, section.Background);
        ((IPseudoClasses)section.Classes).Set(":pointerover", false);
        section.Focus(NavigationMethod.Tab);
        Assert.True(Assert.Single(section.GetVisualDescendants().OfType<Border>(), b => b.Name == "FocusRing").IsVisible);
        window.Close();
        static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public async Task SingleCompactBeamCardKeepsHorizontalLengthAndLongNamesInsideItsThreeAreas(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel(); var setup = main.Setup;
        setup.SelectedSection = new CircleSectionGeometry(M(.1)); setup.BendingAxis = SectionAxisDesignation.Z;
        setup.ShowSections(); var definition = setup.Definition!;
        definition.BeginNameEdit(); definition.PendingName = new string('W', 250); definition.CommitName();
        Assert.True(await definition.ConfirmAsync());
        setup.SelectedMaterial = new(new string('M', 250), ProjectTemplates.Material.YoungsModulus, ProjectTemplates.Material.YieldStrength);
        var view = new ProjectSetupView { DataContext = setup };
        var window = new Window { Content = view, Width = width, Height = height }; window.Show(); Arrange(window, width, height);
        var card = view.FindControl<BeamSetupCard>("BeamCard")!;
        var surface = Assert.IsType<Border>(card.Content);
        Assert.Same(surface, card.FindControl<Border>("BeamCardSurface"));
        Assert.Equal(new Thickness(1), surface.BorderThickness);
        Assert.InRange(surface.Bounds.Height, 110, 120);
        Assert.True(double.IsNaN(surface.Height));
        var areas = Assert.IsType<Grid>(surface.Child);
        Assert.Equal(new[] { 2d, 4.3, 3.7 }, areas.ColumnDefinitions.Select(c => c.Width.Value));
        Assert.Equal(3, areas.Children.Count);
        var lengthArea = Assert.IsType<Border>(areas.Children[0]);
        Assert.False(lengthArea.Focusable); Assert.Equal(default, lengthArea.BorderThickness);
        var section = Assert.IsType<Button>(areas.Children[1]); var material = Assert.IsType<Button>(areas.Children[2]);
        Assert.True(section.Focusable); Assert.True(material.Focusable);
        Assert.Same(setup.ChangeSectionCommand, section.Command); Assert.Same(setup.ChangeMaterialCommand, material.Command);
        Assert.Equal(lengthArea.Bounds.Height, section.Bounds.Height); Assert.Equal(section.Bounds.Height, material.Bounds.Height);
        Assert.InRange(section.Bounds.Height, 85, 95);
        foreach (var button in new[] { section, material })
        {
            Assert.True(double.IsNaN(button.Height)); Assert.Equal(new Thickness(16, 12), button.Padding);
            Assert.All(button.GetVisualDescendants().OfType<TextBlock>(), text => Assert.True(text.Bounds.Height >= text.TextLayout.Height));
        }
        Assert.True(lengthArea.Bounds.Right < section.Bounds.Left); Assert.True(section.Bounds.Right < material.Bounds.Left);
        var length = card.FindControl<TextBox>("SetupLengthInput")!;
        var label = card.FindControl<TextBlock>("SetupLengthLabel")!; var unit = card.FindControl<TextBlock>("SetupLengthUnit")!;
        Assert.True(label.Bounds.Right < length.Bounds.Left); Assert.True(length.Bounds.Right < unit.Bounds.Left);
        Assert.InRange(Math.Abs(label.Bounds.Center.Y - length.Bounds.Center.Y), 0, 1);
        Assert.InRange(Math.Abs(unit.Bounds.Center.Y - length.Bounds.Center.Y), 0, 1);
        var lengthCenter = length.TranslatePoint(new Point(0, length.Bounds.Height / 2), lengthArea)!.Value.Y;
        Assert.InRange(Math.Abs(lengthCenter - lengthArea.Bounds.Height / 2), 0, 1);
        Assert.Equal("1000", length.Text); Assert.Equal("mm", unit.Text); Assert.True(length.Bounds.Width >= 60);
        foreach (var name in new[] { "SectionDescription", "MaterialDescription", "SectionAxisDescription" })
        {
            var text = card.FindControl<TextBlock>(name)!;
            Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming); Assert.Equal(text.Text, ToolTip.GetTip(text));
            var button = text.FindAncestorOfType<Button>()!;
            var position = text.TranslatePoint(default, button)!.Value;
            Assert.InRange(position.X + text.Bounds.Width, 0, button.Bounds.Width - button.Padding.Right);
            Assert.True(text.Bounds.Height >= text.TextLayout.Height);
        }
        Assert.Equal(Strings.BendingAxis + " z-z · " + Strings.SectionDefinitionEquivalent,
            card.FindControl<TextBlock>("SectionAxisDescription")!.Text);
        Assert.Equal(SectionAxisDesignation.Z, setup.BendingAxis);
        length.Focus(); length.Text = "0";
        Assert.True(DesktopControlEnvironment.MoveFocus(window, length, NavigationDirection.Next));
        Arrange(window, width, height);
        Assert.True(section.IsFocused); Assert.Equal("0", length.Text); Assert.Equal(1000, setup.SelectedLength.Millimeters);
        var error = card.FindControl<TextBlock>("SetupLengthError")!;
        Assert.True(error.IsEffectivelyVisible); Assert.True(error.Bounds.Height >= error.TextLayout.Height);
        Assert.True(error.TranslatePoint(default, surface)!.Value.Y + error.Bounds.Height <= surface.Bounds.Height);
        Assert.False(setup.CanApply); Assert.False(setup.ApplyCommand.CanExecute(null));
        Assert.False(view.FindControl<Button>("ApplySetupButton")!.IsEffectivelyEnabled); Assert.Null(main.Session);
        Assert.True(surface.Bounds.Height <= 130);
        window.Close();
    }

    private static void Fill(SectionDefinitionViewModel model)
    {
        foreach (var input in model.Parameters)
        {
            input.PendingText=input.Key switch { "h"=>"200", "b"=>"100", "tw" or "t"=>"5", "tf"=>"10", "r"=>"0", "A"=>"1000", _ when input.Key.StartsWith('I')=>"1000000", _=>"20000" };
            input.Commit();
        }
        if (model.RequiresAxisSelection) model.SelectedAxis=model.AvailableAxes[0].Value;
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
