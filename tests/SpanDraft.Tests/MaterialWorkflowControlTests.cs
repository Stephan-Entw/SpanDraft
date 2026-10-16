using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpanDraft.Desktop.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class MaterialWorkflowControlTests
{
    [Theory]
    [InlineData("de-DE", 1100, 650, 1)] [InlineData("en-US", 1100, 650, 1)]
    [InlineData("de-DE", 1250, 800, 1)] [InlineData("en-US", 1250, 800, 1)]
    [InlineData("de-DE", 1100, 650, 1.25)] [InlineData("en-US", 1100, 650, 1.25)]
    [InlineData("de-DE", 1250, 800, 1.25)] [InlineData("en-US", 1250, 800, 1.25)]
    [InlineData("de-DE", 1100, 650, 1.5)] [InlineData("en-US", 1100, 650, 1.5)]
    [InlineData("de-DE", 1250, 800, 1.5)] [InlineData("en-US", 1250, 800, 1.5)]
    public async Task SelectionAndEditorKeepActionsVisibleAndUseNativeRowsWithLongNamesAndErrors(string culture, double width, double height, double scaling)
    {
        using var environment = new DesktopControlEnvironment(scaling); using var scope = new UiCultureScope(culture);
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        var longName = new string('W', 200);
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new([new(new Material(longName, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), MaterialCategory.Other, "A long number", ["Other standard"])]));
        await main.InitializeLibrariesAsync(); main.Setup.SelectedMaterial = new("Project only", Pressure.FromPascals(210e9), Pressure.FromMegapascals(355));
        var setup = new ProjectSetupView { DataContext = main.Setup }; var window = new Window { Content = setup, Width = width, Height = height };
        window.Show(); main.Setup.ShowMaterials(); Arrange(window, width, height);
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var list = view.FindControl<ListBox>("MaterialEntries")!;
        Assert.Equal(SelectionMode.Single, list.SelectionMode); Assert.Same(selection.CurrentEntry, selection.SelectedEntry);
        Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>(), row => Assert.Empty(row.GetVisualDescendants().OfType<Button>()));
        Assert.Same(selection.UseSelectedCommand, view.FindControl<Button>("UseMaterialButton")!.Command);
        Assert.Same(selection.EditSelectedCommand, view.FindControl<Button>("EditSelectedMaterialButton")!.Command);
        Assert.Same(selection.DeleteSelectedCommand, view.FindControl<Button>("DeleteSelectedMaterialButton")!.Command);
        selection.SelectedEntry = selection.UserEntries.Single(); Arrange(window, width, height); VerifyLayout();
        Capture("selection");
        selection.EditSelectedCommand.Execute(null); Arrange(window, width, height);
        var editorView = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).Editor);
        Assert.Empty(editorView.GetVisualDescendants().OfType<CheckBox>());
        Assert.False(view.FindControl<Button>("AddMaterialLibraryButton")!.IsVisible);
        Assert.Same(editor.ReplaceLibraryCommand, view.FindControl<Button>("ConfirmMaterialButton")!.Command);
        var rows = editorView.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("materialInputRow")).ToArray();
        Assert.Equal(4, rows.Length);
        foreach (var row in rows)
        {
            var label = row.Children.OfType<TextBlock>().Single(t => t.Name == "MaterialInputLabel");
            var input = row.Children.OfType<TextBox>().Single(); var unit = row.Children.OfType<TextBlock>().Single(t => t.Name == "MaterialInputUnit");
            Assert.Equal(128, input.Bounds.Width); Assert.True(label.Bounds.Right < input.Bounds.Left); Assert.True(input.Bounds.Right < unit.Bounds.Left);
            Assert.InRange(Math.Abs(label.Bounds.Center.Y - input.Bounds.Center.Y), 0, 1);
            Assert.InRange(Math.Abs(unit.Bounds.Center.Y - input.Bounds.Center.Y), 0, 1);
        }
        rows[0].Children.OfType<TextBox>().Single().Focus(); editor.Inputs[0].Text = "0";
        editorView.FindControl<TextBox>("MaterialNumberInput")!.Focus(); Arrange(window, width, height);
        Assert.False(view.FindControl<Button>("ConfirmMaterialButton")!.IsEffectivelyEnabled); VerifyLayout(); Capture("edit-invalid");
        editor.CancelCommand.Execute(null); Assert.Same(selection, main.Setup.CurrentStep);
        selection.NewCommand.Execute(null); Arrange(window, width, height);
        VerifyLayout(); Capture("new-empty");
        editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).Editor); editor.Name = longName; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
        editorView = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        Assert.True(view.FindControl<Button>("AddMaterialLibraryButton")!.IsVisible);
        Assert.Same(editor.AddLibraryCommand, view.FindControl<Button>("AddMaterialLibraryButton")!.Command);
        Assert.Same(editor.ApplyCommand, view.FindControl<Button>("ConfirmMaterialButton")!.Command);
        Assert.True(editor.HasLibraryError); Assert.True(editor.CanApply); Arrange(window, width, height); VerifyLayout(); Capture("new-conflict");
        window.Close();

        void Capture(string state)
        {
            if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(width * scaling), (int)(height * scaling)), new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(setup);
            bitmap.Save(Path.Combine(directory, $"materials-{culture}-{width}x{height}-{scaling:0.##}-{state}.png"), PngBitmapEncoderOptions.Default);
        }

        void VerifyLayout()
        {
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text?.Length > 0 && t.Bounds.Width > 0))
                Assert.True(text.Bounds.Height + 1 >= text.TextLayout.Height, "Clipped text: " + text.Text);
            foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            foreach (var action in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("primary")))
            {
                var point = action.TranslatePoint(default, setup)!.Value;
                Assert.InRange(point.X, 0, width - action.Bounds.Width); Assert.InRange(point.Y, 0, height - action.Bounds.Height);
            }
            Assert.All(window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBox or ComboBox or Button { Command: not null }),
                c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))));
        }
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task NewMaterialStartsNeutralAndShowsFieldErrorsOnlyAfterLeavingTheField(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel(); main.Setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        selection.NewCommand.Execute(null);
        var setup = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = setup, Width = 1100, Height = 650 }; window.Show(); Arrange(window, 1100, 650);
        var editor = selection.Editor!;
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        var name = view.FindControl<TextBox>("MaterialNameInput")!;
        var numbers = view.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "MaterialValueInput").ToArray();
        Assert.True(name.IsFocused); Assert.Null(editor.Draft); Assert.False(editor.CanConfirm); Assert.False(editor.CanAddLibrary);
        Assert.All(view.GetVisualDescendants().OfType<TextBox>(), input => Assert.DoesNotContain("error", input.Classes));
        Assert.Empty(VisibleErrors());

        numbers[0].Focus();
        Assert.Contains("error", name.Classes); Assert.Single(VisibleErrors());
        numbers[0].Text = "-";
        Assert.DoesNotContain("error", numbers[0].Classes); Assert.Single(VisibleErrors());
        numbers[1].Focus();
        Assert.Contains("error", numbers[0].Classes); Assert.DoesNotContain("error", numbers[1].Classes);
        Assert.Equal(2, VisibleErrors().Length); Assert.False(editor.CanConfirm);

        name.Text = "Draft"; numbers[0].Text = "210";
        Assert.DoesNotContain("error", name.Classes); Assert.DoesNotContain("error", numbers[0].Classes);
        Assert.Empty(VisibleErrors()); Assert.False(editor.CanConfirm);
        numbers[1].Text = "355";
        Assert.True(editor.CanConfirm); Assert.NotNull(editor.Draft); Assert.Empty(VisibleErrors());

        numbers[0].Text = "0";
        Assert.Contains("error", numbers[0].Classes); Assert.False(editor.CanConfirm);
        editor.CancelCommand.Execute(null); selection.NewCommand.Execute(null); Arrange(window, 1100, 650);
        view = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        Assert.All(view.GetVisualDescendants().OfType<TextBox>(), input => Assert.DoesNotContain("error", input.Classes));
        Assert.Empty(VisibleErrors()); Assert.False(selection.Editor!.CanConfirm);
        window.Close();

        TextBlock[] VisibleErrors() => view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("validationMessage") && !string.IsNullOrEmpty(t.Text)).ToArray();
    }

    [Fact]
    public async Task SearchEnterOnlyFocusesSelectionWhileArrowsClickEnterAndDoubleClickUseNativeSelection()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new([new(new Material("One user", Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), MaterialCategory.Other)]));
        await main.InitializeLibrariesAsync(); main.Setup.SelectedMaterial = new("Project only", Pressure.FromPascals(210e9), Pressure.FromMegapascals(355));
        var setup = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = setup, Width = 1100, Height = 650 }; window.Show(); main.Setup.ShowMaterials(); Arrange(window, 1100, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep); var list = view.FindControl<ListBox>("MaterialEntries")!;
        Assert.True(selection.SelectedEntry!.IsProject);
        var selected = selection.SelectedEntry = selection.FilteredEntries[0]; var row = (ListBoxItem)list.ContainerFromItem(selected)!;
        row.Focus(NavigationMethod.Tab); Press(row, Key.Down);
        Assert.NotSame(selected, selection.SelectedEntry); Assert.Null(main.Session);
        var search = view.FindControl<TextBox>("MaterialSearchInput")!; search.Focus(); search.Text = "One user";
        selection.SelectedEntry = selection.CurrentEntry; Assert.True(selection.SelectedEntry!.IsProject); Press(search, Key.Enter); Arrange(window, 1100, 650);
        Assert.Equal("One user", selection.SelectedEntry!.Name); Assert.Equal(ProjectSetupPage.MaterialSelection, main.Setup.Page);
        row = (ListBoxItem)list.ContainerFromItem(selection.SelectedEntry)!; Assert.True(row.IsFocused);
        Press(row, Key.Enter); Assert.True(main.Setup.IsOverview); Assert.Equal("One user", main.Setup.SelectedMaterial!.Name); Assert.Null(main.Session);
        main.Setup.ShowMaterials(); Arrange(window, 1100, 650);
        selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep); view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        list = view.FindControl<ListBox>("MaterialEntries")!; var target = selection.BuiltInEntries[2];
        row = (ListBoxItem)list.ContainerFromItem(target)!;
        var pointer = new Pointer(1, PointerType.Mouse, true); var point = row.TranslatePoint(new Point(10, 10), window)!.Value;
        var pressed = new PointerPressedEventArgs(row, pointer, window, point, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None);
        row.RaiseEvent(pressed); pointer.Capture(null);
        Assert.Same(target, selection.SelectedEntry); Assert.Equal("One user", main.Setup.SelectedMaterial.Name);
        var text = row.GetVisualDescendants().OfType<TextBlock>().First();
        text.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pressed) { Source = text });
        Assert.True(main.Setup.IsOverview); Assert.Same(target.Material, main.Setup.SelectedMaterial); Assert.Null(main.Session);
        window.Close();
    }

    [Fact]
    public async Task ExplicitLibraryButtonStaysInEditorAndProjectApplyChangesOnlySetup()
    {
        using var environment = new DesktopControlEnvironment(); var files = new Files();
        var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials")); await main.InitializeLibrariesAsync();
        var original = main.Setup.SelectedMaterial; var setup = new ProjectSetupView { DataContext = main.Setup }; var window = new Window { Content = setup };
        window.Show(); main.Setup.ShowMaterials(); Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).NewCommand.Execute(null); Arrange(window, 1100, 650);
        var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).Editor); editor.Name = "Added from UI"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
        Arrange(window, 1100, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        Activate(view.FindControl<Button>("AddMaterialLibraryButton")!);
        Assert.True(editor.IsInLibrary); Assert.Same(editor, Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep).Editor); Assert.Same(original, main.Setup.SelectedMaterial);
        Activate(view.FindControl<Button>("ConfirmMaterialButton")!);
        Assert.True(main.Setup.IsOverview); Assert.Equal("Added from UI", main.Setup.SelectedMaterial!.Name); Assert.Single(main.UserMaterials.All); Assert.Null(main.Session);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public async Task ScrollbarUsesIdenticalHeaderAndRowGeometryAndDiscardRestoresFocusAndScroll(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new(Enumerable.Range(0, 80).Select(i =>
            new MaterialLibraryEntry(new("Shop " + i, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), MaterialCategory.Other))));
        await main.InitializeLibrariesAsync(); main.Setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        selection.SelectedFilter = selection.UserFilter; selection.SearchText = "shop";
        var setup = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = setup, Width = width, Height = height }; window.Show(); Arrange(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var list = view.FindControl<ListBox>("MaterialEntries")!;
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        selection.SelectedEntry = selection.FilteredEntries[10];
        scroll.Offset = new(0, 320); Arrange(window, width, height);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height); Assert.True(scroll.Offset.Y > 0);
        var heading = list.GetVisualDescendants().OfType<MaterialTableColumns>().Single(g => g.Name == "MaterialTableHeader");
        var headings = heading.Children.OfType<TextBlock>().ToArray();
        Assert.Equal(4, headings.Length); Assert.Equal(Strings.MaterialDesignation, headings[0].Text); Assert.DoesNotContain("*", headings[0].Text!);
        foreach (var text in headings) Assert.True(text.Bounds.Width + 1 >= text.TextLayout.Width, "Clipped column heading: " + text.Text);
        foreach (var row in list.GetVisualDescendants().OfType<ListBoxItem>())
        {
            Assert.InRange(row.Bounds.Height, 38, 40);
            var grid = Assert.Single(row.GetVisualDescendants().OfType<MaterialTableColumns>());
            Assert.True(grid.ColumnDefinitions[0].ActualWidth >= 200);
            var cells = grid.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(120, cells[1].Bounds.Width); Assert.Equal(80, cells[2].Bounds.Width); Assert.Equal(85, cells[3].Bounds.Width);
            for (int column = 0; column < 4; column++)
            {
                Assert.InRange(Math.Abs(headings[column].TranslatePoint(default, list)!.Value.X - cells[column].TranslatePoint(default, list)!.Value.X), 0, 1);
                Assert.InRange(Math.Abs(headings[column].Bounds.Width - cells[column].Bounds.Width), 0, 1);
            }
        }
        var before = scroll.Offset; var selected = selection.SelectedEntry;
        selection.EditSelectedCommand.Execute(null); Arrange(window, width, height);
        Assert.Same(view, Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>()));
        Assert.False(view.FindControl<ListBox>("MaterialFilters")!.IsEffectivelyEnabled);
        var name = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.Name == "MaterialNameInput");
        Assert.True(name.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, name, NavigationDirection.Next));
        Assert.True(Assert.Single(view.GetVisualDescendants().OfType<ComboBox>()).IsFocused);
        Press(name, Key.Escape); Arrange(window, width, height);
        Assert.Null(selection.Editor); Assert.Same(selected, selection.SelectedEntry);
        Assert.Equal("shop", selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter);
        Assert.Same(list, view.FindControl<ListBox>("MaterialEntries")); Assert.Equal(before, scroll.Offset);
        Assert.True(((ListBoxItem)list.ContainerFromItem(selected!)!).IsFocused);
        selection.EditSelectedCommand.Execute(null); Arrange(window, width, height);
        var editor = selection.Editor!; editor.Name = "Renamed outside search";
        Assert.True(await editor.ReplaceLibraryAsync()); Arrange(window, width, height);
        Assert.Equal("", selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter);
        Assert.Equal(editor.Name, selection.SelectedEntry!.Name);
        var renamedRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selection.SelectedEntry));
        Assert.True(renamedRow.IsFocused);
        var top = renamedRow.TranslatePoint(default, scroll)!.Value.Y;
        Assert.InRange(top, 0, scroll.Viewport.Height - renamedRow.Bounds.Height);
        window.Close();
    }

    private static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static void Activate(Button button) => typeof(Button)
        .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Arrange(Window window, double width, double height)
    {
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!; content.InvalidateMeasure(); content.InvalidateArrange();
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
