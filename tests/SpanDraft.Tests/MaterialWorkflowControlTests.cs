using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
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
    private static void Arrange(Window window, double width = 1100, double height = 650)
    { window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); Dispatcher.UIThread.RunJobs(); }
    private static void Activate(Button button)
    {
        if (button.Command is { } command) { Assert.True(command.CanExecute(null)); command.Execute(null); }
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static void VerifyLayout(Window window)
    {
        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text?.Length > 0 && t.Bounds.Width > 0))
            Assert.True(text.Bounds.Height + 1 >= text.TextLayout.Height, "Clipped text: " + text.Text);
        foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, "Horizontal overflow");
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is TextBox or ComboBox or Button { Command: not null }))
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
    }
    private static void VerifyCompactEditor(MaterialEditorWindow modal)
    {
        VerifyLayout(modal);
        Assert.Equal(408, modal.Width);
        Assert.Equal(SizeToContent.Height, modal.SizeToContent);
        var form = modal.GetVisualDescendants().OfType<MaterialEditorView>().Single();
        double X(Control control) => control.TranslatePoint(default, modal)!.Value.X;
        var name = form.FindControl<TextBox>("MaterialNameInput")!;
        foreach (var control in new Control[] { name, form.FindControl<ComboBox>("MaterialCategoryInput")!, form.FindControl<TextBox>("MaterialNumberInput")!, form.FindControl<TextBox>("MaterialStandardsInput")! })
        {
            Assert.Equal(208, control.Bounds.Width, 6);
            Assert.Equal(name.Bounds.Height, control.Bounds.Height, 6);
            Assert.Equal(X(name), X(control), 6);
            Assert.Equal(24, modal.Bounds.Width - X(control) - control.Bounds.Width, 6);
        }
        var strength = form.FindControl<Grid>("MaterialStrengthRow")!;
        var type = form.FindControl<ComboBox>("MaterialStrengthTypeInput")!;
        Assert.Equal(name.Bounds.Height, type.Bounds.Height, 6);
        Assert.Equal(76, type.Bounds.Width, 6); Assert.Equal(X(name), X(type), 6);
        var value = form.FindControl<TextBox>("MaterialStrengthValueInput")!;
        Assert.Equal(8, X(value) - X(type) - type.Bounds.Width, 6);
        var unit = ((Grid)value.Parent!).Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 4);
        Assert.Equal(8, X(unit) - X(value) - value.Bounds.Width, 6);
        foreach (var inputName in new[] { "MaterialYoungsModulusInput", "MaterialStrengthValueInput", "MaterialDensityInput", "MaterialPoissonInput" })
        {
            var input = form.FindControl<TextBox>(inputName)!;
            Assert.Equal(name.Bounds.Height, input.Bounds.Height, 6);
            Assert.Equal(68, input.Bounds.Width, 6); Assert.Equal(X(value), X(input), 6);
            var row = (Grid)input.Parent!;
            var inputUnit = row.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 4);
            Assert.Equal(X(unit), X(inputUnit), 6);
            Assert.Equal(48, inputUnit.Bounds.Width, 6);
            Assert.Equal(X(name) + name.Bounds.Width, X(inputUnit) + inputUnit.Bounds.Width, 6);
            Assert.Equal(208, row.Bounds.Width, 6);
            var label = ((Grid)row.Parent!).Children.OfType<TextBlock>().Single(t => Grid.GetRow(t) == 0 && Grid.GetColumn(t) == 0);
            Assert.Equal(24, X(label), 6);
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, "Clipped label: " + label.Text);
        }
        var rows = ((StackPanel)strength.Parent!).Children.OfType<Grid>().ToArray();
        for (int i = 1; i < rows.Length; i++)
            Assert.Equal(i == 4 ? 16 : 8, rows[i].Bounds.Top - rows[i - 1].Bounds.Bottom, 6);
        var buttons = modal.FindControl<StackPanel>("MaterialEditorActions")!.Children.OfType<Button>().ToArray();
        Assert.Equal(buttons[0].Bounds.Size, buttons[1].Bounds.Size);
        Assert.All(buttons, button => { Assert.True(button.Bounds.Width >= 88); Assert.True(button.Bounds.Height >= 32); });
        Assert.Equal(24, modal.Bounds.Width - X(buttons[1]) - buttons[1].Bounds.Width, 6);
        Assert.Equal(new Thickness(24, 16), ((Border)buttons[0].Parent!.Parent!).Padding);
    }
    private static (Window Window, MainWindowViewModel Main, MaterialSelectionViewModel Selection, MaterialSelectionView View) Open(Files files, Dialogs dialogs, double width = 1100, double height = 650)
    {
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, materialStore: new(files, "materials")); main.NewCommand.Execute(null);
        main.InitializeLibrariesAsync().GetAwaiter().GetResult();
        main.Setup.SelectedMaterial = new("Project", Pressure.FromPascals(70e9), Pressure.FromMegapascals(150), MaterialStrengthType.Rp02);
        var window = new Window { Content = new ProjectSetupView { DataContext = main.Setup }, Width = width, Height = height };
        dialogs.MaterialEditorHook = model => new MaterialEditorWindow(model).ShowDialog(window);
        window.Show(); main.Setup.ShowMaterials(); Arrange(window, width, height);
        return (window, main, (MaterialSelectionViewModel)main.Setup.CurrentStep!, window.GetVisualDescendants().OfType<MaterialSelectionView>().Single());
    }
    private static MaterialEditorWindow Modal(Window owner) => Assert.Single(owner.OwnedWindows.OfType<MaterialEditorWindow>());

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task OriginIconsShareOneLeadingSlotAndProjectSuffixStaysVisibleWithLongNames(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); files.Data["materials"] = MaterialLibraryCodec.Serialize(new([MaterialWorkflowRegressionTests.Entry("Own material")]));
        var (owner, main, selection, view) = Open(files, new Dialogs());
        try
        {
            main.Setup.ShowOverview(); main.Setup.SelectedMaterial = main.BuiltInMaterials!.All[0]; main.Setup.ShowMaterials(); Arrange(owner);
            selection = (MaterialSelectionViewModel)main.Setup.CurrentStep!;
            view = owner.GetVisualDescendants().OfType<MaterialSelectionView>().Single();
            selection.EditSelectedCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            selection.Editor!.Name = new string('W', 160); Assert.True(await selection.Editor.ConfirmAsync()); Arrange(owner);
            var list = view.FindControl<ListBox>("MaterialEntries")!;
            Assert.Same(selection.CurrentEntry, list.Items[0]);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "ProjectMaterialEntry");
            var header = Assert.Single(list.GetVisualDescendants().OfType<MaterialTableColumns>(), c => c.Name == "MaterialTableHeader");
            double nameStart = header.Children[0].TranslatePoint(default, list)!.Value.X;
            foreach (var item in new[] { selection.CurrentEntry!, selection.BuiltInEntries[0], selection.UserEntries[0] })
            {
                list.ScrollIntoView(item); Arrange(owner);
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(item));
                Assert.Empty(row.GetVisualDescendants().OfType<Button>());
                var columns = Assert.Single(row.GetVisualDescendants().OfType<MaterialTableColumns>()); Assert.Equal(4, columns.ColumnDefinitions.Count);
                var slot = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MaterialOriginSlot");
                var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MaterialRowName");
                var suffix = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MaterialProjectSuffix");
                var projectIcon = row.GetVisualDescendants().OfType<PathIcon>().Single(i => i.Name == "MaterialProjectIcon");
                var builtInIcon = row.GetVisualDescendants().OfType<PathIcon>().Single(i => i.Name == "MaterialBuiltInIcon");
                Assert.Equal(16, slot.Bounds.Width); Assert.Equal(16, slot.Bounds.Height);
                Assert.True(slot.IsHitTestVisible); Assert.False(slot.Focusable);
                Assert.InRange(Math.Abs(nameStart - name.TranslatePoint(default, list)!.Value.X), 0, 1);
                Assert.Equal(item.IsProject, projectIcon.IsVisible); Assert.Equal(item.IsBuiltIn, builtInIcon.IsVisible);
                Assert.Same(Application.Current!.FindResource("FluentIcon.CheckmarkCircle"), projectIcon.Data);
                Assert.Same(Application.Current!.FindResource("FluentIcon.LockClosed"), builtInIcon.Data);
                foreach (var icon in new[] { projectIcon, builtInIcon }) { Assert.False(icon.Focusable); Assert.False(icon.IsHitTestVisible); }
                Assert.Equal(item.OriginLabel, ToolTip.GetTip(slot)); Assert.Equal(item.OriginLabel, AutomationProperties.GetName(slot));
                Assert.Equal(item.AccessibleName, AutomationProperties.GetName(row));
                Assert.Equal(item.DisplayName, ToolTip.GetTip((Control)columns.Children[0]));
                Assert.Equal(item.Material.Name, name.Text);
                Assert.StartsWith(item.DisplayName + " · ", item.AccessibleName);
                Assert.Equal(item.IsProject, suffix.IsVisible);
                if (item.IsProject)
                {
                    Assert.Equal(Strings.MaterialCurrentEntry, item.OriginLabel); Assert.Contains(Strings.MaterialCurrentEntry, item.AccessibleName);
                    Assert.Equal(Strings.MaterialProjectSuffix, suffix.Text);
                    Assert.Same(Application.Current!.FindResource("TextSecondary"), suffix.Foreground);
                    Assert.True(name.Bounds.Right <= suffix.Bounds.Left);
                    Assert.True(suffix.Bounds.Right <= ((Control)suffix.Parent!).Bounds.Width);
                    Assert.True(suffix.TextLayout.Width <= suffix.Bounds.Width + 1);
                }
                else if (item.IsBuiltIn) Assert.Contains(Strings.BuiltInMaterialReadOnly, item.AccessibleName);
                else { Assert.Null(item.OriginLabel); Assert.Null(item.ProjectSuffix); }
            }
            VerifyLayout(owner); Assert.Empty(files.Writes);
        }
        finally { foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }

    [Theory]
    [InlineData("de-DE", 1100, 650, 1)] [InlineData("en-US", 1100, 650, 1)]
    [InlineData("de-DE", 1250, 800, 1)] [InlineData("en-US", 1250, 800, 1)]
    [InlineData("de-DE", 1100, 650, 1.25)] [InlineData("en-US", 1100, 650, 1.25)]
    [InlineData("de-DE", 1250, 800, 1.25)] [InlineData("en-US", 1250, 800, 1.25)]
    [InlineData("de-DE", 1100, 650, 1.5)] [InlineData("en-US", 1100, 650, 1.5)]
    [InlineData("de-DE", 1250, 800, 1.5)] [InlineData("en-US", 1250, 800, 1.5)]
    public void BrowserAndOwnedEditorKeepCompactLayoutAndSourceActions(string culture, double width, double height, double scaling)
    {
        using var environment = new DesktopControlEnvironment(scaling); using var scope = new UiCultureScope(culture);
        var files = new Files(); var dialogs = new Dialogs();
        files.Data["materials"] = MaterialLibraryCodec.Serialize(new(Enumerable.Range(0, 40).Select(i => MaterialWorkflowRegressionTests.Entry(new string('W', 120) + i))));
        var (owner, main, selection, view) = Open(files, dialogs, width, height);
        try
        {
            VerifyLayout(owner);
            var search = view.FindControl<TextBox>("MaterialSearchInput")!;
            var list = view.FindControl<ListBox>("MaterialEntries")!; var use = view.FindControl<Button>("UseMaterialButton")!;
            Assert.Equal(SelectionMode.Single, list.SelectionMode);
            Assert.Same(selection.FilteredEntries, list.ItemsSource);
            Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]); Assert.Same(selection.CurrentEntry, selection.SelectedEntry);
            var project = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selection.CurrentEntry!));
            var projectColumns = Assert.Single(project.GetVisualDescendants().OfType<MaterialTableColumns>());
            var heading = Assert.Single(view.GetVisualDescendants().OfType<MaterialTableColumns>(), c => c.Name == "MaterialTableHeader");
            Assert.Null(view.FindControl<MaterialTableColumns>("ProjectMaterialTableHeader"));
            Assert.True(search.TranslatePoint(default, view)!.Value.Y + search.Bounds.Height <= heading.TranslatePoint(default, view)!.Value.Y);
            Assert.True(heading.TranslatePoint(default, view)!.Value.Y + heading.Bounds.Height <= project.TranslatePoint(default, view)!.Value.Y);
            Assert.Equal(4, projectColumns.ColumnDefinitions.Count);
            Assert.Equal("\u2014", ((TextBlock)projectColumns.Children[1]).Text);
            Assert.Equal("70", ((TextBlock)projectColumns.Children[2]).Text);
            for (int i = 0; i < 4; i++)
            {
                var cell = projectColumns.Children[i]; var header = heading.Children[i];
                if (i == 0)
                {
                    var name = project.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MaterialRowName");
                    Assert.InRange(Math.Abs(name.TranslatePoint(default, view)!.Value.X - header.TranslatePoint(default, view)!.Value.X), 0, 1);
                    Assert.InRange(Math.Abs(projectColumns.ColumnDefinitions[0].ActualWidth - heading.ColumnDefinitions[0].ActualWidth), 0, 1);
                }
                else
                {
                    Assert.InRange(Math.Abs(cell.TranslatePoint(default, view)!.Value.X - header.TranslatePoint(default, view)!.Value.X), 0, 1);
                    Assert.InRange(Math.Abs(cell.Bounds.Width - header.Bounds.Width), 0, 1);
                }
            }
            Assert.Equal(Strings.MaterialStrengthColumn, ((TextBlock)heading.Children[3]).Text);
            selection.SearchText = "no library match"; selection.SelectedFilter = selection.BuiltInFilter; Arrange(owner, width, height);
            Assert.Empty(selection.FilteredEntries); Assert.True(selection.IsEmpty); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
            selection.SearchText = ""; selection.SelectedFilter = selection.Filters[0]; selection.SelectedEntry = selection.CurrentEntry; Arrange(owner, width, height);
            Assert.True(view.FindControl<Button>("SaveProjectMaterialButton")!.IsVisible); Assert.False(view.FindControl<Button>("DeleteSelectedMaterialButton")!.IsVisible);
            var actions = view.FindControl<StackPanel>("MaterialDetailActions")!; Assert.Equal(Orientation.Horizontal, actions.Orientation);
            Assert.True(actions.Children.Where(c => c.IsVisible).All(c => c.Bounds.Right <= actions.Bounds.Width + 1));
            selection.SelectedEntry = selection.BuiltInEntries[0]; Arrange(owner, width, height);
            Assert.Equal(Strings.CopyMaterial, view.FindControl<Button>("EditSelectedMaterialButton")!.Content);
            Assert.False(view.FindControl<Button>("SaveProjectMaterialButton")!.IsVisible); Assert.False(selection.ShowDelete);
            selection.SelectedEntry = selection.UserEntries[0]; selection.EditSelectedCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var modal = Modal(owner); Arrange(modal, modal.Width, modal.Bounds.Height);
            Assert.Same(owner, modal.Owner); Assert.False(selection.CanBrowse); Assert.False(modal.ShowInTaskbar);
            Assert.Empty(view.GetVisualDescendants().OfType<MaterialEditorView>());
            var editor = selection.Editor!; Assert.Same(editor, modal.DataContext);
            VerifyCompactEditor(modal);
            var buttons = modal.FindControl<StackPanel>("MaterialEditorActions")!.Children.OfType<Button>().ToArray();
            Assert.Equal(new[] { Strings.Cancel, Strings.OK }, buttons.Select(b => (string)b.Content!));
            Assert.Same(editor.ConfirmCommand, buttons[1].Command); Assert.True(buttons[0].Bounds.Right < buttons[1].Bounds.Left);
            foreach (var button in buttons)
            {
                var position = button.TranslatePoint(default, modal)!.Value;
                Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= modal.Bounds.Width);
                Assert.True(position.Y >= 0 && position.Y + button.Bounds.Height <= modal.Bounds.Height, $"Button {position} + {button.Bounds.Size}, window {modal.Bounds}, client {modal.ClientSize}");
            }
            var form = modal.GetVisualDescendants().OfType<MaterialEditorView>().Single();
            var strength = form.FindControl<Grid>("MaterialStrengthRow")!;
            var combo = form.FindControl<ComboBox>("MaterialStrengthTypeInput")!;
            var value = form.FindControl<TextBox>("MaterialStrengthValueInput")!;
            Assert.True(combo.Bounds.Right < value.Bounds.Left);
            Assert.InRange(Math.Abs(combo.Bounds.Center.Y - value.Bounds.Center.Y), 0, 1);
            editor.Inputs[0].Text = "0"; editor.Inputs[0].CompleteInput(); Arrange(modal, modal.Width, modal.Bounds.Height);
            Assert.False(buttons[1].IsEffectivelyEnabled); VerifyCompactEditor(modal);
            Press(modal, Key.Escape); Dispatcher.UIThread.RunJobs(); Assert.Empty(owner.OwnedWindows); Assert.False(selection.IsEditing); Assert.True(owner.IsEnabled);
            Assert.Same(selection.UserEntries[0], selection.SelectedEntry); Assert.Empty(files.Writes);
            Assert.Same(selection.UseSelectedCommand, use.Command);
        }
        finally { foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task NewModalValidatesFieldsAndOkOnlyCreatesLibraryEntry(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); var (owner, main, selection, view) = Open(files, new Dialogs());
        try
        {
            var original = main.Setup.SelectedMaterial; selection.NewCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var modal = Modal(owner); var editor = selection.Editor!; var form = modal.GetVisualDescendants().OfType<MaterialEditorView>().Single();
            var name = form.FindControl<TextBox>("MaterialNameInput")!; Assert.True(name.IsFocused);
            Assert.False(editor.CanConfirm); Assert.False(editor.ShowNameError);
            Press(name, Key.Enter); Assert.True(form.FindControl<ComboBox>("MaterialCategoryInput")!.IsFocused); Assert.True(editor.ShowNameError);
            foreach (var input in editor.Inputs) { input.Text = "invalid"; input.CompleteInput(); }
            form.FindControl<ComboBox>("MaterialStrengthTypeInput")!.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Arrange(modal, modal.Width, modal.Bounds.Height); VerifyCompactEditor(modal);
            editor.Inputs[2].Text = ""; editor.Inputs[3].Text = "";
            editor.Name = "New saved"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
            Assert.False(editor.CanConfirm); editor.StrengthType = editor.StrengthTypes[1]; Assert.True(editor.CanConfirm);
            Assert.True(await editor.ConfirmAsync()); Dispatcher.UIThread.RunJobs(); Assert.Empty(owner.OwnedWindows);
            Assert.Same(original, main.Setup.SelectedMaterial); Assert.Equal("New saved", selection.SelectedEntry!.Name);
            Assert.Equal(MaterialStrengthType.Rp02, Assert.Single(main.UserMaterials.All).StrengthType);
            Assert.Equal(ProjectSetupPage.MaterialSelection, main.Setup.Page);
            Activate(view.FindControl<Button>("UseMaterialButton")!); Assert.True(main.Setup.IsOverview); Assert.Equal("New saved", main.Setup.SelectedMaterial!.Name);
        }
        finally { foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }

    [Fact]
    public void WindowCloseDiscardsAndNativeListAndSearchKeyboardRemainIndependent()
    {
        using var environment = new DesktopControlEnvironment(); var files = new Files();
        var (owner, main, selection, view) = Open(files, new Dialogs());
        try
        {
            var original = selection.CurrentEntry; selection.EditSelectedCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var modal = Modal(owner); selection.Editor!.Name = "Discarded"; modal.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Same(original, selection.CurrentEntry); Assert.False(selection.IsEditing); Assert.Empty(files.Writes);
            var search = view.FindControl<TextBox>("MaterialSearchInput")!; search.Text = "S355JR"; Press(search, Key.Enter); Arrange(owner);
            Assert.Equal("S355JR", selection.SelectedEntry!.Name); Assert.Equal(ProjectSetupPage.MaterialSelection, main.Setup.Page);
            var list = view.FindControl<ListBox>("MaterialEntries")!; var row = (ListBoxItem)list.ContainerFromItem(selection.SelectedEntry)!;
            Press(row, Key.Enter); Assert.True(main.Setup.IsOverview); Assert.Equal("S355JR", main.Setup.SelectedMaterial!.Name);
        }
        finally { owner.Close(); }
    }

    [Fact]
    public async Task RealDialogAdapterShowsConflictChoicesAndRequiresSecondReplacementConfirmation()
    {
        using var environment = new DesktopControlEnvironment(); var owner = new Window(); owner.Show();
        try
        {
            const string displayName = "Mine (1.4567) · UNS Example · AISI Shop";
            var dialogs = new ProjectDialogs(owner); var decision = dialogs.ResolveMaterialNameConflictAsync(displayName); Dispatcher.UIThread.RunJobs();
            var conflict = Assert.Single(owner.OwnedWindows); var buttons = conflict.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(conflict.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == string.Format(Strings.MaterialNameConflictQuestion, displayName));
            Assert.Equal(new[] { Strings.ReplaceMaterial, Strings.RenameMaterial, Strings.Cancel }, buttons.Select(b => b.Content));
            Activate(buttons[0]); Assert.Equal(MaterialNameConflictDecision.Replace, await decision);
            var confirmation = dialogs.ConfirmMaterialReplaceAsync(displayName); Dispatcher.UIThread.RunJobs();
            var confirmWindow = Assert.Single(owner.OwnedWindows); Assert.Equal(2, confirmWindow.GetVisualDescendants().OfType<Button>().Count());
            Assert.Contains(confirmWindow.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == string.Format(Strings.ReplaceMaterialQuestion, displayName));
            Press(confirmWindow, Key.Escape); Assert.False(await confirmation);
        }
        finally { foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }

    [Fact]
    public async Task SavingDisablesDuplicateOkEscapeAndWindowCloseUntilWriteCompletes()
    {
        using var environment = new DesktopControlEnvironment(); var files = new Files();
        var (owner, main, selection, view) = Open(files, new Dialogs());
        var release = new TaskCompletionSource();
        try
        {
            selection.NewCommand.Execute(null); Dispatcher.UIThread.RunJobs(); var modal = Modal(owner); var editor = selection.Editor!;
            editor.Name = "Pending"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355"; editor.StrengthType = editor.StrengthTypes[0];
            files.BeforeWrite = (_, _, _) => release.Task;
            var saving = editor.ConfirmAsync(); Assert.True(editor.IsBusy); Assert.False(editor.CanConfirm); Assert.False(await editor.ConfirmAsync());
            Press(modal, Key.Escape); modal.Close(); Assert.Same(modal, Modal(owner)); Assert.True(selection.IsEditing);
            release.SetResult();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!saving.IsCompleted)
            { Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30)); Dispatcher.UIThread.RunJobs(); Thread.Yield(); }
            Assert.True(await saving); Dispatcher.UIThread.RunJobs(); Assert.Empty(owner.OwnedWindows); Assert.Single(files.Writes); Assert.Null(main.Session);
        }
        finally { release.TrySetResult(); foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }

    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public async Task ScrollAndFocusSurviveModalCancelAndRenamedSelectionBecomesVisible(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); files.Data["materials"] = MaterialLibraryCodec.Serialize(new(
            Enumerable.Range(0, 80).Select(i => MaterialWorkflowRegressionTests.Entry("Shop " + i))));
        var (owner, main, selection, view) = Open(files, new Dialogs(), width, height);
        try
        {
            selection.SelectedFilter = selection.UserFilter; selection.SearchText = "Shop";
            var list = view.FindControl<ListBox>("MaterialEntries")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            selection.SelectedEntry = selection.FilteredEntries[10]; scroll.Offset = new(0, 320); Arrange(owner, width, height);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height); Assert.True(scroll.Offset.Y > 0);
            var heading = list.GetVisualDescendants().OfType<MaterialTableColumns>().Single(c => c.Name == "MaterialTableHeader");
            foreach (var row in list.GetVisualDescendants().OfType<ListBoxItem>())
            {
                var columns = Assert.Single(row.GetVisualDescendants().OfType<MaterialTableColumns>());
                var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MaterialRowName");
                for (int i = 0; i < 4; i++)
                {
                    var cell = i == 0 ? name : columns.Children[i];
                    Assert.InRange(Math.Abs(heading.Children[i].TranslatePoint(default, list)!.Value.X
                        - cell.TranslatePoint(default, list)!.Value.X), 0, 1);
                    Assert.InRange(Math.Abs(heading.Children[i].Bounds.Width - cell.Bounds.Width), 0, 1);
                }
            }
            var before = scroll.Offset; var selected = selection.SelectedEntry;
            selection.EditSelectedCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Press(Modal(owner), Key.Escape); Arrange(owner, width, height);
            Assert.Same(selected, selection.SelectedEntry); Assert.Equal(before, scroll.Offset);
            Assert.Equal("Shop", selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter);
            Assert.True(((ListBoxItem)list.ContainerFromItem(selected!)!).IsFocused);
            selection.EditSelectedCommand.Execute(null); Dispatcher.UIThread.RunJobs(); var editor = selection.Editor!;
            editor.Name = "Renamed outside search"; editor.OtherStandards = "Other alias";
            Assert.True(await editor.ConfirmAsync()); Arrange(owner, width, height);
            Assert.Empty(selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter);
            var renamed = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selection.SelectedEntry)); Assert.True(renamed.IsFocused);
            var top = renamed.TranslatePoint(default, scroll)!.Value.Y;
            Assert.True(top >= 0 && top + renamed.Bounds.Height <= scroll.Viewport.Height);
        }
        finally { foreach (var modal in owner.OwnedWindows.ToArray()) modal.Close(); owner.Close(); }
    }
}
