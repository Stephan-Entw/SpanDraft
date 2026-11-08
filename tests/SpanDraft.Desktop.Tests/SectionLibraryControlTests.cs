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
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SectionLibraryControlTests : SectionDefinitionRedesignTestSupport
{
    [Fact]
    public void InputSizesAndSpacingStayFixedWhenWindowHeightChanges()
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection); Fill(vm);
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            Arrange(650);
            var workspace = view.FindControl<Grid>("InputWorkspace")!;
            var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
            var inputs = pinned.GetVisualDescendants().OfType<TextBox>().ToArray();
            var sizes = inputs.Select(b => b.Bounds.Size).ToArray();
            var rows = pinned.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("definitionInputRow")).ToArray();
            var rowMargins = rows.Select(r => r.Margin).ToArray();
            var margin = workspace.Margin; double spacing = pinned.Spacing;
            foreach (double height in new[] { 900, 600, 800, 650 })
            {
                Arrange(height);
                Assert.Equal(margin, workspace.Margin); Assert.Equal(spacing, pinned.Spacing);
                Assert.Equal(sizes, inputs.Select(b => b.Bounds.Size));
                Assert.Equal(rowMargins, rows.Select(r => r.Margin));
                Assert.Same(view.FindControl<ScrollViewer>("InputScroll"), pinned.FindAncestorOfType<ScrollViewer>());
            }

            void Arrange(double height)
            {
                window.Height = height; view.Width = 1100; view.Height = height;
                for (int i = 0; i < 3; i++)
                { Dispatcher.UIThread.RunJobs(); view.Measure(new Size(1100, height)); view.Arrange(new Rect(0, 0, 1100, height)); }
            }
        }
        finally { window.Close(); }
    }
    [Theory]
    [InlineData("de-DE", 1)] [InlineData("en-US", 1)]
    [InlineData("de-DE", 1.25)] [InlineData("en-US", 1.25)]
    [InlineData("de-DE", 1.5)] [InlineData("en-US", 1.5)]
    public async Task LibraryActionKeepsItsBoundsAndCenteredContentAcrossDraftAndSaveStates(string culture, double scaling)
    {
        using var environment = new DesktopControlEnvironment(scaling); using var scope = new UiCultureScope(culture);
        var library = new Library(); var vm = new SectionDefinitionViewModel(library: library);
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            var button = view.FindControl<Button>("SaveLibraryButton")!;
            Assert.Same(vm.SaveLibraryCommand, button.Command);
            Layout(); Assert.False(button.IsEffectivelyVisible);
            vm.SelectForm(SectionEditorType.Circle); Check(false);
            var bounds = button.Bounds;
            var heading = view.FindControl<StackPanel>("LibraryHeading")!;
            var headingPosition = heading.TranslatePoint(default, view);
            var input = vm.Parameters.Single();
            Fill(vm); Check(true); Stable();
            string valid = input.PendingText;
            input.PendingText = "120"; Check(false); Stable();
            Assert.True(input.Commit()); Check(true); Stable();
            input.PendingText = "invalid"; Check(false); Stable();
            Assert.False(input.Commit()); Check(false); Stable();
            input.PendingText = valid; Assert.True(input.Commit()); Check(true); Stable();
            vm.BeginNameEdit(); vm.PendingName = "Taken"; Assert.True(vm.CommitName());
            library.Source.Add(new("Taken", new CircleSectionGeometry(M(.04))));
            vm.RefreshLibraryState(); Check(false); Stable();
            var message = view.FindControl<TextBlock>("LibrarySaveMessage")!;
            Assert.Equal(culture == "de-DE" ? "Name bereits vergeben" : "Name already taken", message.Text);
            Assert.DoesNotContain("validationMessage", message.Classes);
            vm.BeginNameEdit(); vm.PendingName = "Free"; Assert.True(vm.CommitName()); Check(true);
            library.SaveError = Strings.LibrarySaveFailed;
            button.Focus(); Assert.False(await vm.SaveLibraryAsync()); Check(true); Stable();
            Assert.False(message.Focusable); Assert.Null(ToolTip.GetTip(button));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<SectionInputHint>(), h => h.IsEffectivelyVisible);
            Assert.Equal(culture == "de-DE" ? "Bibliothek konnte nicht gespeichert werden." : "The library could not be saved.", message.Text);
            Assert.DoesNotContain("librarySaveError", button.Classes);
            library.SaveError = null; Assert.True(await vm.SaveLibraryAsync()); Check(false); Stable();
            Assert.True(vm.IsInLibrary); Assert.Null(message.Text);
            Assert.Equal(culture == "de-DE" ? "In Bibliothek" : "In library", AutomationProperties.GetName(button));
            Assert.Equal("✓", ((Grid)button.Content!).Children.OfType<TextBlock>().Single(t => t.IsEffectivelyVisible && Grid.GetColumn(t) == 0).Text);

            var hidden = new SectionDefinitionViewModel(showLibraryOption: false);
            hidden.SelectForm(SectionEditorType.ISection); Fill(hidden); view.DataContext = hidden;
            Layout(); Assert.False(button.IsEffectivelyVisible);

            void Check(bool enabled)
            {
                Layout(); Assert.True(button.IsEffectivelyVisible); Assert.Equal(enabled, button.IsEffectivelyEnabled);
                var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
                Assert.InRange(Math.Abs(button.Bounds.Width - pinned.Bounds.Width), 0, 1);
                var content = view.FindControl<Grid>("LibrarySaveButtonContent")!;
                Assert.InRange(Math.Abs(content.TranslatePoint(new Point(content.Bounds.Width / 2, 0), button)!.Value.X - button.Bounds.Width / 2), 0, 1);
                Assert.Equal(view.FindControl<TextBlock>("LibrarySaveButtonLabel")!.Text, AutomationProperties.GetName(button));
                var message = view.FindControl<TextBlock>("LibrarySaveMessage")!;
                Assert.Equal(vm.HasLibraryMessage, message.IsEffectivelyVisible);
                if (!vm.HasLibraryMessage)
                    Assert.Equal(button.Bounds.Height, view.FindControl<StackPanel>("LibrarySaveActions")!.Bounds.Height);
            }
            void Stable()
            {
                Assert.Equal(bounds, button.Bounds);
                if (!vm.HasLibraryMessage) Assert.Equal(headingPosition, heading.TranslatePoint(default, view));
            }
            void Layout()
            {
                for (int i = 0; i < 3; i++)
                { Dispatcher.UIThread.RunJobs(); view.Measure(new Size(1100, 650)); view.Arrange(new Rect(0, 0, 1100, 650)); }
            }
        }
        finally { window.Close(); }
    }
    [Theory]
    [InlineData("de-DE", 1500, 980)] [InlineData("en-US", 1500, 980)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    public async Task NameEditorAndDirectSaveHaveStableLayoutAndNoLibraryDialog(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var library = new Library(); library.Source.Add(new("Existing", new ISectionGeometry(M(.12), M(.08), M(.008), M(.008), M(.005))));
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.ISection); Fill(vm); vm.SelectedAxis = SectionAxisDesignation.Y;
        var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view, Width = width, Height = height };
        var header = view.FindControl<Grid>("CommandHeader")!; var columns = view.FindControl<Grid>("DefinitionColumns")!;
        Rect? initialHeader = null;
        window.Show(); Layout(); initialHeader = header.Bounds;
        var beforeHeader = header.Bounds; var beforeColumns = columns.Bounds; var beforeBody = columns.TranslatePoint(default, view);
        var scroll = view.FindControl<Border>("InputColumn")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        var name = view.FindControl<TextBox>("NameInput")!; var automatic = view.FindControl<Button>("ResetNameButton")!;
        vm.BeginNameEdit(); Layout(); Assert.True(name.IsFocused); Assert.True(automatic.IsEffectivelyVisible);
        vm.PendingName = " "; Press(name, Key.Enter); Layout(); Assert.True(vm.HasNameError); Assert.True(vm.IsEditingName);
        Assert.IsType<PathIcon>(name.InnerRightContent);
        Assert.Equal(vm.NameError, ToolTip.GetTip(name));
        Assert.Equal(vm.NameError, Avalonia.Automation.AutomationProperties.GetHelpText(name));
        Assert.True(name.IsFocused); Assert.True(vm.IsEditingName);
        automatic.Focus(); Layout(); Assert.True(vm.IsEditingName); automatic.Command!.Execute(null); Layout();
        Assert.False(vm.IsEditingName); Assert.False(vm.HasNameError);
        vm.BeginNameEdit(); Layout(); vm.PendingName = "Existing";
        view.FindControl<Button>("CancelButton")!.Focus(); Layout(); Assert.False(vm.IsEditingName); Assert.Equal("Existing", vm.DisplayName);
        Assert.Null(vm.LibraryError); vm.BeginNameEdit(); Layout(); vm.PendingName = "Abandoned"; Press(name, Key.Escape); Layout(); Assert.Equal("Existing", vm.DisplayName);
        vm.BeginNameEdit(); Layout(); vm.PendingName = new string('W', 120); Press(name, Key.Enter); Layout();
        Assert.False(vm.IsEditingName); Assert.Equal(120, vm.DisplayName.Length);
        vm.BeginNameEdit(); Layout(); vm.PendingName = ""; view.FindControl<Button>("CancelButton")!.Focus(); Layout();
        Assert.True(vm.IsEditingName); Assert.True(vm.HasNameError); vm.CancelNameEdit(); Layout();
        var save = view.FindControl<Button>("SaveLibraryButton")!;
        Assert.Null(save.Flyout); Assert.Null(FlyoutBase.GetAttachedFlyout(save));
        vm.BeginNameEdit(); Layout(); vm.PendingName = "EXISTING"; Press(name, Key.Enter); Layout();
        Assert.False(await vm.SaveLibraryAsync()); Layout();
        var libraryMessage = view.FindControl<TextBlock>("LibrarySaveMessage")!;
        Assert.True(vm.HasLibraryNameConflict); Assert.False(save.IsEffectivelyEnabled);
        Assert.Equal(Strings.LibraryNameTaken, libraryMessage.Text); Assert.Null(vm.LibraryError);
        Assert.Single(library.Entries); Assert.False(vm.IsEditingName); Assert.Equal("EXISTING", vm.DisplayName);
        Assert.False(save.Command!.CanExecute(null)); Assert.Single(library.Entries);
        vm.BeginNameEdit(); Layout(); vm.PendingName = new string('W', 120); Press(name, Key.Enter); Layout();
        library.SaveError = Strings.LibrarySaveFailed;
        save.Focus(); save.Command.Execute(null); Layout();
        Assert.False(libraryMessage.Focusable); Assert.True(save.IsEffectivelyEnabled);
        Assert.Equal(Strings.LibrarySaveFailedShort, libraryMessage.Text);
        Assert.DoesNotContain("librarySaveError", save.Classes);
        library.SaveError = null;
        save.Command!.Execute(null); Layout();
        Assert.Equal(vm.DisplayName, library.Entries[1].Name); Assert.Null(libraryMessage.Text);
        Assert.True(vm.IsInLibrary); Assert.False(save.IsEffectivelyEnabled);
        Assert.Null(save.Flyout); Assert.Empty(window.OwnedWindows);
        Assert.Equal(beforeHeader, header.Bounds); Assert.Equal(beforeColumns, columns.Bounds);
        Assert.Equal(beforeBody, columns.TranslatePoint(default, view));
        Assert.Same(view.FindControl<ScrollViewer>("InputScroll"), scroll);
        window.Close();

        void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            { Dispatcher.UIThread.RunJobs(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); }
            if (initialHeader is { } initial) Assert.Equal(initial, header.Bounds);
        }
        static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }
    [Theory]
    [InlineData("de-DE", SectionEditorType.Circle)] [InlineData("en-US", SectionEditorType.Circle)]
    [InlineData("de-DE", SectionEditorType.Manual)] [InlineData("en-US", SectionEditorType.Manual)]
    public void SectionWorkspaceKeepsCompletionInHeaderAndLibrarySaveAbovePresets(string culture, SectionEditorType type)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        vm.SelectForm(type); Fill(vm);
        var owner = new Window { Content = view }; owner.Show();
        view.Measure(new Size(1100, 650)); view.Arrange(new Rect(0, 0, 1100, 650));
        Assert.Empty(view.GetVisualDescendants().OfType<Menu>());
        var save = view.FindControl<Button>("SaveLibraryButton")!;
        var cancel = view.FindControl<Button>("CancelButton")!;
        var confirm = view.FindControl<Button>("ConfirmButton")!;
        var actions = view.FindControl<StackPanel>("HeaderActions")!;
        Assert.Equal(new[] { cancel, confirm }, actions.Children.OfType<Button>().Where(b => b.IsEffectivelyVisible));
        Assert.Same(vm.SaveLibraryCommand, save.Command);
        Assert.Same(vm.CancelCommand, cancel.Command);
        Assert.Same(vm.ConfirmCommand, confirm.Command);
        string saveText = culture == "de-DE" ? "In Bibliothek speichern" : "Save to library";
        Assert.Equal(saveText, view.FindControl<TextBlock>("LibrarySaveButtonLabel")!.Text);
        Assert.Equal(saveText, AutomationProperties.GetName(save));
        Assert.Equal(culture == "de-DE" ? "Abbrechen" : "Cancel", cancel.Content);
        Assert.Equal(culture == "de-DE" ? "Übernehmen" : "Apply", confirm.Content);
        Assert.Equal(cancel.Content, AutomationProperties.GetName(cancel));
        Assert.Equal(confirm.Content, AutomationProperties.GetName(confirm));
        Assert.Contains("secondary", save.Classes); Assert.Contains("primary", confirm.Classes);
        Assert.DoesNotContain("primary", cancel.Classes);
        Assert.True(cancel.Bounds.Right <= confirm.Bounds.Left);
        Assert.True(confirm.Bounds.Right <= actions.Bounds.Width);
        var saveActions = view.FindControl<StackPanel>("LibrarySaveActions")!;
        var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
        Assert.Same(pinned, saveActions.Parent);
        Assert.Equal(new Control[] { save, view.FindControl<TextBlock>("LibrarySaveMessage")! }, saveActions.Children);
        Assert.InRange(Math.Abs(save.Bounds.Width - pinned.Bounds.Width), 0, 1);
        Assert.Same(saveActions, pinned.Children.Last());
        Assert.Same(view.FindControl<ScrollViewer>("InputScroll"), save.FindAncestorOfType<ScrollViewer>());
        Assert.True(saveActions.Bounds.Left >= 0 && saveActions.Bounds.Right <= pinned.Bounds.Width + 1);
        var heading = view.FindControl<StackPanel>("LibraryHeading")!;
        Assert.IsType<Border>(heading.Children[0]);
        Assert.Equal(Strings.SectionDefinitionFromLibrary, Assert.IsType<TextBlock>(heading.Children[1]).Text);
        Assert.True(save.TranslatePoint(default, view)!.Value.Y + save.Bounds.Height <= heading.TranslatePoint(default, view)!.Value.Y);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "Alle Maße beziehen sich auf den dargestellten Querschnitt." or "All dimensions refer to the section shown.");
        owner.Close();
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void PresetButtonsKeepLoadingAndDeletionSeparateAndShowDeleteErrorsWithoutAValidDraft(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var library = new Library { DeleteError = "Delete failed" };
        var entry = new SectionLibraryEntry("Saved circle", new CircleSectionGeometry(M(.04))); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle);
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1250, Height = 800 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = view.FindControl<ItemsControl>("LibraryEntryList")!.GetVisualDescendants().OfType<Button>().ToArray();
            var load = buttons.Single(b => b.Classes.Contains("presetLoad"));
            var delete = buttons.Single(b => b.Classes.Contains("presetDelete"));
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, library.DeleteCalls); Assert.Null(vm.Section); Assert.False(vm.HasCustomName);
            Assert.Equal(library.DeleteError, vm.LibraryError);
            Assert.Equal(library.DeleteError, view.FindControl<TextBlock>("LibrarySaveMessage")!.Text);
            Assert.True(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<Button>("SaveLibraryButton")!.IsEffectivelyEnabled);

            load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, library.DeleteCalls); Assert.Equal(entry.Name, vm.DisplayName); Assert.True(vm.CanConfirm);
            var section = vm.Section;
            library.DeleteError = null;
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, library.DeleteCalls); Assert.Empty(vm.LibraryEntries); Assert.Null(vm.LibraryError);
            Assert.Same(section, vm.Section); Assert.Equal(entry.Name, vm.DisplayName); Assert.True(vm.CanConfirm);
        }
        finally { window.Close(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClickingDeletePreservesFocusedPendingInputsAndNames(bool editingName)
    {
        using var environment = new DesktopControlEnvironment();
        var library = new Library { CancelDelete = true };
        var entry = new SectionLibraryEntry("Saved circle", new CircleSectionGeometry(M(.04))); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle); vm.LoadPreset(entry);
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1250, Height = 800 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            if (editingName)
            {
                vm.BeginNameEdit(); Dispatcher.UIThread.RunJobs();
                Assert.True(view.FindControl<TextBox>("NameInput")!.IsFocused); vm.PendingName = "Uncommitted name";
            }
            else
            {
                var input = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext is SectionDefinitionInput);
                input.Focus(); vm.Parameters.Single().PendingText = "99";
            }
            var section = vm.Section; var preview = vm.Preview;
            var delete = view.FindControl<ItemsControl>("LibraryEntryList")!.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("presetDelete"));
            var pointer = new Pointer(72, PointerType.Mouse, true);
            var position = delete.TranslatePoint(new Point(16, 16), window)!.Value;
            delete.RaiseEvent(new PointerPressedEventArgs(delete, pointer, window, position, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            delete.Focus(NavigationMethod.Pointer); Dispatcher.UIThread.RunJobs();
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, library.DeleteCalls); Assert.Single(vm.LibraryEntries); Assert.Null(vm.LibraryError);
            Assert.Same(section, vm.Section); Assert.Same(preview, vm.Preview); Assert.Equal(entry.Name, vm.DisplayName);
            if (editingName) { Assert.True(vm.IsEditingName); Assert.Equal("Uncommitted name", vm.PendingName); }
            else { Assert.True(vm.Parameters.Single().IsPending); Assert.Equal("99", vm.Parameters.Single().PendingText); }
        }
        finally { window.Close(); }
    }
    [Theory]
    [InlineData("de-DE", 1500, 980)] [InlineData("en-US", 1500, 980)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public void EntireInputPanelScrollsTogetherOnlyWhenContentOverflows(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        foreach (int count in new[] { 1, 5, 8 })
        {
            var library = new Library(); var vm = new SectionDefinitionViewModel(new(units), library);
            var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view, Width = width, Height = height };
            window.Show();
            foreach (var type in vm.Forms.Select(f => f.Value).Append(SectionEditorType.Manual))
            {
                vm.SelectForm(type); Fill(vm);
                for (int i = 0; i < count; i++) library.Source.Add(new(type + " saved " + i + " mounting bracket", vm.Section!));
                vm.SelectForm(type); Layout();
                Assert.Equal(count, vm.LibraryEntries.Count);
                var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
                var heading = view.FindControl<StackPanel>("LibraryHeading")!;
                var scroll = view.FindControl<ScrollViewer>("InputScroll")!;
                var list = view.FindControl<ItemsControl>("LibraryEntryList")!;
                scroll.Offset = default; Layout();
                Assert.True(view.FindControl<Grid>("LibraryQuickSelection")!.IsEffectivelyVisible);
                Assert.True(heading.IsEffectivelyVisible); Assert.True(scroll.IsEffectivelyVisible);
                Assert.All(heading.GetVisualDescendants().OfType<Control>(), c => Assert.True(c.IsEffectivelyVisible));
                Assert.Same(scroll, pinned.FindAncestorOfType<ScrollViewer>());
                Assert.Same(scroll, heading.FindAncestorOfType<ScrollViewer>());
                Assert.Same(scroll, list.FindAncestorOfType<ScrollViewer>());
                Assert.Null(view.FindControl<ScrollViewer>("LibraryScroll"));
                Assert.Same(scroll, Assert.Single(view.FindControl<Border>("InputColumn")!.GetVisualDescendants()
                    .OfType<ScrollViewer>(), s => s.FindAncestorOfType<TextBox>() is null));
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                var save = view.FindControl<Button>("SaveLibraryButton")!;
                Assert.True(save.IsEffectivelyVisible);
                var savePosition = save.TranslatePoint(default, view)!.Value;
                var inputColumn = view.FindControl<Border>("InputColumn")!;
                var columnPosition = inputColumn.TranslatePoint(default, view)!.Value;
                Assert.True(savePosition.X >= columnPosition.X && savePosition.X + save.Bounds.Width <= columnPosition.X + inputColumn.Bounds.Width);
                Assert.True(savePosition.Y >= 0);
                Assert.True(savePosition.Y + save.Bounds.Height <= heading.TranslatePoint(default, view)!.Value.Y);
                foreach (var box in pinned.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
                {
                    var position = box.TranslatePoint(default, view)!.Value;
                    Assert.True(position.Y >= 0);
                    Assert.True(position.Y + box.Bounds.Height <= heading.TranslatePoint(default, view)!.Value.Y, type + ": inputs overlap library heading");
                }
                var profile = view.FindControl<Border>("ProfileCard")!;
                var profileLeft = profile.TranslatePoint(default, view)!.Value.X;
                var profileRight = profileLeft + profile.Bounds.Width;
                var cards = list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("presetCard")).ToArray();
                Assert.Equal(count, cards.Length);
                foreach (var entry in cards)
                {
                    var left = entry.TranslatePoint(default, view)!.Value.X;
                    Assert.InRange(Math.Abs(left - profileLeft), 0, .5);
                    Assert.InRange(Math.Abs(left + entry.Bounds.Width - profileRight), 0, .5);
                    var buttons = entry.GetVisualDescendants().OfType<Button>().ToArray();
                    Assert.Equal(2, buttons.Length);
                    var load = buttons.Single(b => b.Classes.Contains("presetLoad"));
                    var delete = buttons.Single(b => b.Classes.Contains("presetDelete"));
                    Assert.DoesNotContain(delete.GetVisualAncestors(), a => a is Button);
                    Assert.True(load.TranslatePoint(default, entry)!.Value.X + load.Bounds.Width <= delete.TranslatePoint(default, entry)!.Value.X);
                    Assert.Equal(Strings.SectionDefinitionDeleteLibrary, Avalonia.Automation.AutomationProperties.GetName(delete));
                    Assert.Equal(Strings.SectionDefinitionDeleteLibrary, ToolTip.GetTip(delete));
                    Assert.Same(Application.Current!.FindResource("FluentIcon.Delete"), Assert.IsType<PathIcon>(delete.Content).Data);
                    foreach (var text in load.GetVisualDescendants().OfType<TextBlock>())
                    {
                        Assert.True(text.Bounds.Width + 1 >= text.TextLayout.Width);
                        Assert.True(text.Bounds.Height + 1 >= text.TextLayout.Height);
                    }
                }
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical
                    && b.FindAncestorOfType<ScrollViewer>() == scroll);
                if (bar.IsEffectivelyVisible)
                {
                    var barLeft = bar.TranslatePoint(default, view)!.Value.X;
                    Assert.True(barLeft >= profileRight + 4, type + ": scrollbar touches cards");
                    var column = view.FindControl<Border>("InputColumn")!;
                    var columnRight = column.TranslatePoint(default, view)!.Value.X + column.Bounds.Width;
                    Assert.InRange(columnRight - (barLeft + bar.Bounds.Width), 0, 2);
                }
                Assert.True(scroll.Viewport.Height > 0, type + ": library has no scroll area");
                scroll.Offset = default; Layout();
                var first = cards[0];
                var before = first.TranslatePoint(default, view)!.Value;
                var inputBefore = pinned.TranslatePoint(default, view)!.Value;
                var headingBefore = heading.TranslatePoint(default, view)!.Value;
                var saveBefore = save.TranslatePoint(default, view)!.Value;
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)); Layout();
                double delta = first.TranslatePoint(default, view)!.Value.Y - before.Y;
                Assert.InRange(Math.Abs(pinned.TranslatePoint(default, view)!.Value.Y - inputBefore.Y - delta), 0, .5);
                Assert.InRange(Math.Abs(heading.TranslatePoint(default, view)!.Value.Y - headingBefore.Y - delta), 0, .5);
                Assert.InRange(Math.Abs(save.TranslatePoint(default, view)!.Value.Y - saveBefore.Y - delta), 0, .5);
                if (scroll.Extent.Height > scroll.Viewport.Height + 1)
                    Assert.True(delta < 0);
                else
                {
                    Assert.Equal(0, scroll.Offset.Y);
                    Assert.False(bar.IsEffectivelyVisible);
                }
                var last = cards[^1];
                double lastTop = last.TranslatePoint(default, scroll)!.Value.Y;
                Assert.True(lastTop < scroll.Viewport.Height);
                Assert.True(lastTop + last.Bounds.Height <= scroll.Viewport.Height + 1);
                if (units == UnitProfile.Default && type == SectionEditorType.ISection && width == 1250 && height == 800
                    && Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                    bitmap.Render(view); bitmap.Save(Path.Combine(directory, $"section-library-{culture}-{count}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            window.Close();
            void Layout()
            {
                for (int pass = 0; pass < 3; pass++)
                { Dispatcher.UIThread.RunJobs(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); }
            }
        }
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void WholePanelScrollingKeepsLibraryReachableThroughResizeAndFeedback(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var library = new Library();
            var vm = new SectionDefinitionViewModel(new(units), library); vm.SelectForm(SectionEditorType.ISection); Fill(vm);
            library.Source.Add(new("Saved profile", vm.Section!)); vm.RefreshLibraryState();
            var view = new SectionDefinitionView { DataContext = vm, UseLayoutRounding = false };
            var window = new Window { Content = view, Width = 1000, Height = 600, UseLayoutRounding = false }; window.Show();
            var region = view.FindControl<Grid>("LibraryQuickSelection")!;
            var heading = view.FindControl<StackPanel>("LibraryHeading")!;
            var scroll = view.FindControl<ScrollViewer>("InputScroll")!;
            var workspace = view.FindControl<Grid>("InputWorkspace")!;
            var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
            var save = view.FindControl<Button>("SaveLibraryButton")!;
            Arrange(600);
            var inputSizes = pinned.GetVisualDescendants().OfType<TextBox>().Select(b => b.Bounds.Size).ToArray();
            var margin = workspace.Margin; double spacing = pinned.Spacing;
            double buttonHeight = save.Bounds.Height;
            foreach (var height in new[] { 600, 650, 980, 600, 800 })
            {
                Arrange(height);
                Check();
                vm.BeginNameEdit(); vm.PendingName = "Saved profile"; vm.CommitName(); Arrange(height);
                Assert.True(vm.IsInLibrary); Check();
                vm.Parameters[0].PendingText = "120"; vm.Parameters[0].Commit(); Arrange(height);
                Assert.True(vm.HasLibraryNameConflict); Check();
                vm.LoadPreset(library.Entries[0]); Arrange(height); Check();

                void Check()
                {
                    Assert.True(region.IsEffectivelyVisible); Assert.True(heading.IsEffectivelyVisible); Assert.True(scroll.IsEffectivelyVisible);
                    Assert.True(view.FindControl<StackPanel>("LibraryContent")!.IsEffectivelyVisible);
                    Assert.Same(scroll, pinned.FindAncestorOfType<ScrollViewer>());
                    Assert.Equal(inputSizes, pinned.GetVisualDescendants().OfType<TextBox>().Select(b => b.Bounds.Size));
                    Assert.Equal(margin, workspace.Margin); Assert.Equal(spacing, pinned.Spacing);
                    Assert.Equal(buttonHeight, save.Bounds.Height);
                    var preset = view.FindControl<ItemsControl>("LibraryEntryList")!.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("presetLoad"));
                    Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                    Assert.True(scroll.Viewport.Height > 0);
                    scroll.Offset = new Vector(0, scroll.Extent.Height); Arrange(height);
                    var top = preset.TranslatePoint(default, scroll)!.Value.Y;
                    Assert.True(top < scroll.Viewport.Height);
                    Assert.InRange(top + preset.Bounds.Height, 0, scroll.Viewport.Height + 1);
                    scroll.Offset = default; Arrange(height);
                }
            }
            window.Close();
            void Arrange(double height)
            {
                window.Width = 1000; window.Height = height;
                // The platform proxy does not send native resize callbacks. Explicit view dimensions
                // keep queued parent layout passes from restoring the previous window client size.
                view.Width = 1000; view.Height = height;
                for (int pass = 0; pass < 3; pass++)
                {
                    Dispatcher.UIThread.RunJobs();
                    view.Measure(new Size(1000, height)); view.Arrange(new Rect(0, 0, 1000, height));
                }
            }
        }
    }
    [Theory]
    [InlineData("de-DE", 1500, 980)] [InlineData("en-US", 1500, 980)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    public async Task ReferenceStatesRenderWithReachableCommandsReadablePanelsAndWorkingQuickAccess(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var library = new Library();
        library.Source.Add(new("Träger Montagegestell", new ISectionGeometry(M(.12), M(.08), M(.008), M(.008), M(.005))));
        library.Source.Add(new("I/H-Profil 100 × 100 × 10 × 10", new ISectionGeometry(M(.1), M(.1), M(.01), M(.01), M(.005))));
        library.Source.Add(new("Träger Grundrahmen", new ISectionGeometry(M(.16), M(.08), M(.008), M(.012), M(.006))));
        for (int i = 0; i < 3; i++) library.Source.Add(new("Rundstab Ø" + (40 + 20 * i), new CircleSectionGeometry(M(.04 + .02 * i))));
        var vm = new SectionDefinitionViewModel(library: library); var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height }; window.Show();
        Capture("gallery");
        Assert.Equal(4, view.FindControl<StackPanel>("FormChooser")!.GetVisualDescendants().OfType<UniformGrid>().Single().Columns);
        foreach (var type in new[] { SectionEditorType.ISection, SectionEditorType.Circle, SectionEditorType.Angle, SectionEditorType.Manual })
        {
            vm.SelectForm(type); Fill(vm);
            if (vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
            Capture(type.ToString());
            if (!vm.IsManual)
            {
                var canvas = view.FindControl<SectionPreviewControl>("SectionPreview")!;
                Assert.True(canvas.Bounds.Height > height / 2);
                if (width == 1500) Assert.True(canvas.Bounds.Width > 520); Assert.True(canvas.Bounds.Width >= 180);
            }
            if (vm.LibraryEntries.Count > 0)
            {
                var button = view.FindControl<ItemsControl>("LibraryEntryList")!.GetVisualDescendants().OfType<Button>().First();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(vm.HasValidSection); Assert.All(vm.Parameters, p => Assert.False(p.IsPending));
            }
        }
        vm.SelectForm(SectionEditorType.ISection); Fill(vm); vm.SelectedAxis = SectionAxisDesignation.Y;
        vm.BeginNameEdit(); vm.PendingName = "Draft duplicate"; Assert.True(vm.CommitName());
        vm.BeginNameEdit(); Capture("name-edit");
        vm.PendingName = " "; Assert.False(vm.CommitName()); Capture("name-error");
        var nameInput = view.FindControl<TextBox>("NameInput")!;
        nameInput.Focus(); ToolTip.SetIsOpen(nameInput, true); Layout(); CaptureTip(nameInput, "name-error-tooltip");
        ToolTip.SetIsOpen(nameInput, false);
        vm.CancelNameEdit(); Capture("name-normal");
        library.Source.Add(new(vm.DisplayName, new ISectionGeometry(M(.12), M(.08), M(.008), M(.008), M(.005))));
        vm.RefreshLibraryState();
        Assert.False(await vm.SaveLibraryAsync()); Layout(); Capture("library-conflict");
        Assert.Equal(Strings.LibraryNameTaken, view.FindControl<TextBlock>("LibrarySaveMessage")!.Text);
        vm.BeginNameEdit(); vm.PendingName = "Save fails"; vm.CommitName();
        library.SaveError = Strings.LibrarySaveFailed;
        Assert.False(await vm.SaveLibraryAsync()); Layout(); Capture("library-save-error");
        Assert.Equal(Strings.LibrarySaveFailedShort, view.FindControl<TextBlock>("LibrarySaveMessage")!.Text);
        library.SaveError = null;
        vm.ResetName();
        Layout();
        var radiusHint = view.GetVisualDescendants().OfType<SectionInputHint>().Single(h => h.IsEffectivelyVisible && h.DataContext is SectionDefinitionInput { Key: "r" });
        Assert.Equal(new Size(18, 18), radiusHint.Bounds.Size);
        Assert.Equal(new CornerRadius(9), radiusHint.CornerRadius);
        var infoIcon = Assert.IsType<PathIcon>(radiusHint.Content);
        Assert.Same(Application.Current!.FindResource("FluentIcon.Info"), infoIcon.Data);
        Assert.Equal(new Thickness(0), radiusHint.BorderThickness);
        Assert.Same(Application.Current!.FindResource("FluentIcon.Edit"), view.FindControl<PathIcon>("TitleEditIcon")!.Data);
        Assert.DoesNotContain(view.FindControl<StackPanel>("HeaderActions")!.Children, c => c == view.FindControl<Button>("ChangeFormButton"));
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, new Size(724, 700), UnitCatalog.Millimeter, _ => new Size(80, 14));
        Assert.Equal(310, Math.Max(layout.ProfileBounds.Width, layout.ProfileBounds.Height), 6);
        radiusHint.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(ToolTip.GetIsOpen(radiusHint)); Capture("info-focus"); CaptureTip(radiusHint, "info-tooltip");
        ToolTip.SetIsOpen(radiusHint, false);
        var input = vm.Parameters.Single(p => p.Key == "tw"); input.PendingText = "invalid"; input.Commit(); Layout();
        var errorBox = view.GetVisualDescendants().OfType<TextBox>().Single(t => ReferenceEquals(t.DataContext, input));
        Assert.Same(Application.Current!.FindResource("FluentIcon.ErrorCircle"), Assert.IsType<PathIcon>(errorBox.InnerRightContent).Data);
        errorBox.Focus(); ToolTip.SetIsOpen(errorBox, true); Dispatcher.UIThread.RunJobs(); Capture("error-focus"); CaptureTip(errorBox, "error-tooltip");
        ToolTip.SetIsOpen(errorBox, false);
        input.PendingText = "10"; input.Commit(); vm.BeginNameEdit(); vm.PendingName = new string('W', 80); vm.CommitName(); Capture("long-name");
        vm.ResetNameCommand.Execute(null); Capture("reset-name"); window.Close();

        void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            { Dispatcher.UIThread.RunJobs(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); }
        }
        void CaptureTip(Control hint, string name)
        {
            var tip = Assert.IsType<ToolTip>(hint.GetValue(Avalonia.Controls.Diagnostics.ToolTipDiagnostics.ToolTipProperty));
            var text = tip.GetVisualDescendants().OfType<TextBlock>().First();
            tip.Measure(new Size(320, 300)); tip.Arrange(new Rect(default, tip.DesiredSize));
            Assert.True(tip.Bounds.Width > 0); Assert.True(tip.Bounds.Height >= text.TextLayout.Height);
            if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
            // The inert render timer cannot advance Fluent's tooltip fade-in. Capture the
            // actual template border, whose resolved styles/content are already laid out.
            var frame = Assert.Single(tip.GetVisualChildren().OfType<Control>());
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(frame.Bounds.Width), (int)Math.Ceiling(frame.Bounds.Height)), new Vector(96, 96));
            bitmap.Render(frame); bitmap.Save(Path.Combine(directory, $"section-{culture}-{width}x{height}-{name}.png"), PngBitmapEncoderOptions.Default);
        }
        void Capture(string name)
        {
            Layout();
            foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.FindAncestorOfType<TextBox>() is null))
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, name + ": horizontal overflow");
            foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && !string.IsNullOrEmpty(t.Text)))
            {
                Assert.True(text.Bounds.Height + 1 >= text.TextLayout.Height, name + ": vertical clipping: " + text.Text);
                if (text.TextTrimming == TextTrimming.None) Assert.True(text.Bounds.Width + 1 >= text.TextLayout.Width, name + ": horizontal clipping: " + text.Text);
            }
            var actions = view.FindControl<StackPanel>("HeaderActions")!;
            Assert.Equal(0, Grid.GetRow(actions));
            foreach (var button in actions.Children.OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var p = button.TranslatePoint(default, view)!.Value;
                Assert.InRange(p.X, 0, width - button.Bounds.Width); Assert.InRange(p.Y, 0, height - button.Bounds.Height);
            }
            if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory); view.InvalidateVisual();
            foreach (var visual in view.GetVisualDescendants()) visual.InvalidateVisual();
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)); bitmap.Render(view);
            bitmap.Save(Path.Combine(directory, $"section-{culture}-{width}x{height}-{name}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
