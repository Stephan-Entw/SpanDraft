using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
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
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public async Task SelectionAndEditorKeepActionsVisibleAndUseNativeRowsWithLongNamesAndErrors(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        var longName = new string('W', 200);
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new([new(new Material(longName, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), MaterialCategory.Other, "A long number", ["Other standard"])]));
        await main.InitializeLibrariesAsync(); main.Setup.SelectedMaterial = new("Project only", Pressure.FromPascals(210e9), Pressure.FromMegapascals(355));
        var setup = new ProjectSetupView { DataContext = main.Setup }; var window = new Window { Content = setup, Width = width, Height = height };
        window.Show(); main.Setup.ShowMaterials(); Arrange(window, width, height);
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var list = view.FindControl<ListBox>("MaterialEntries")!;
        Assert.Equal(SelectionMode.Single, list.SelectionMode); Assert.Same(selection.CurrentEntry, list.SelectedItem);
        Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>(), row => Assert.Empty(row.GetVisualDescendants().OfType<Button>()));
        Assert.Same(selection.UseSelectedCommand, view.FindControl<Button>("UseMaterialButton")!.Command);
        Assert.Same(selection.EditSelectedCommand, view.FindControl<Button>("EditSelectedMaterialButton")!.Command);
        Assert.Same(selection.DeleteSelectedCommand, view.FindControl<Button>("DeleteSelectedMaterialButton")!.Command);
        selection.SelectedEntry = selection.UserEntries.Single(); Arrange(window, width, height); VerifyLayout();
        selection.EditSelectedCommand.Execute(null); Arrange(window, width, height);
        var editorView = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        var editor = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep);
        Assert.Empty(editorView.GetVisualDescendants().OfType<CheckBox>());
        Assert.False(editorView.FindControl<Button>("AddMaterialLibraryButton")!.IsVisible);
        Assert.Same(editor.ReplaceLibraryCommand, editorView.FindControl<Button>("ConfirmMaterialButton")!.Command);
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
        editor.Inputs[0].Text = "0"; Arrange(window, width, height);
        Assert.False(editorView.FindControl<Button>("ConfirmMaterialButton")!.IsEffectivelyEnabled); VerifyLayout();
        editor.CancelCommand.Execute(null); Assert.Same(selection, main.Setup.CurrentStep);
        selection.NewCommand.Execute(null); Arrange(window, width, height);
        editor = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep); editor.Name = longName; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
        editor.EndNameEdit(); editorView = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        Assert.True(editorView.FindControl<Button>("AddMaterialLibraryButton")!.IsVisible);
        Assert.Same(editor.AddLibraryCommand, editorView.FindControl<Button>("AddMaterialLibraryButton")!.Command);
        Assert.Same(editor.ApplyCommand, editorView.FindControl<Button>("ConfirmMaterialButton")!.Command);
        Assert.True(editor.HasLibraryError); Assert.True(editor.CanApply); Arrange(window, width, height); VerifyLayout();
        window.Close();

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

    [Fact]
    public async Task SearchEnterOnlyFocusesSelectionWhileArrowsClickEnterAndDoubleClickUseNativeSelection()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new([new(new Material("One user", Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), MaterialCategory.Other)]));
        await main.InitializeLibrariesAsync(); var setup = new ProjectSetupView { DataContext = main.Setup };
        var window = new Window { Content = setup, Width = 1100, Height = 650 }; window.Show(); main.Setup.ShowMaterials(); Arrange(window, 1100, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var selection = Assert.IsType<MaterialSelectionViewModel>(main.Setup.CurrentStep); var list = view.FindControl<ListBox>("MaterialEntries")!;
        var selected = selection.SelectedEntry; var row = (ListBoxItem)list.ContainerFromItem(selected!)!;
        row.Focus(NavigationMethod.Tab); Press(row, Key.Down);
        Assert.NotSame(selected, selection.SelectedEntry); Assert.Null(main.Session);
        var search = view.FindControl<TextBox>("MaterialSearchInput")!; search.Focus(); search.Text = "One user";
        Assert.Null(selection.SelectedEntry); Press(search, Key.Enter); Arrange(window, 1100, 650);
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
        var editor = Assert.IsType<MaterialEditorViewModel>(main.Setup.CurrentStep); editor.Name = "Added from UI"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
        editor.EndNameEdit(); Arrange(window, 1100, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialEditorView>());
        Activate(view.FindControl<Button>("AddMaterialLibraryButton")!);
        Assert.True(editor.IsInLibrary); Assert.Same(editor, main.Setup.CurrentStep); Assert.Same(original, main.Setup.SelectedMaterial);
        Activate(view.FindControl<Button>("ConfirmMaterialButton")!);
        Assert.True(main.Setup.IsOverview); Assert.Equal("Added from UI", main.Setup.SelectedMaterial!.Name); Assert.Single(main.UserMaterials.All); Assert.Null(main.Session);
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
