using Avalonia;
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

[Collection("Schematic text")]
public sealed class SectionDefinitionRedesignTests
{
    private sealed class Library : ISectionDefinitionLibrary
    {
        public UserSectionLibrary Source { get; } = new();
        public IReadOnlyList<SectionLibraryEntry> Entries => Source.All;
        public bool CanSave { get; set; } = true;
        public string? Error { get; set; }
        public string? SaveError { get; set; }
        public bool ContainsName(string name) => Source.Find(name) is not null;
        public Task<string?> SaveAsync(SectionLibraryEntry entry)
        {
            if (SaveError is null) Source.Add(entry);
            return Task.FromResult(SaveError);
        }
    }
    private static void Fill(SectionDefinitionViewModel vm)
    {
        foreach (var input in vm.Parameters)
        {
            input.PendingText = input.Key switch
            { "h" or "b" or "d" or "D" => "100", "tw" or "tf" or "t" => "10", "r" => "5", "A" => "1000", _ => input.Key.StartsWith('I') ? "1000000" : "20000" };
            input.Commit();
        }
    }
    [Fact]
    public void UnchangedAutomaticNameEditKeepsNameResponsiveToDimensions()
    {
        using var culture = new UiCultureScope("en-US");
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        string name = vm.DisplayName;
        vm.BeginNameEdit(); Assert.True(vm.CommitName());
        Assert.False(vm.IsEditingName); Assert.False(vm.HasCustomName); Assert.Equal(name, vm.DisplayName);
        vm.BeginNameEdit(); vm.PendingName = "  " + name + "  "; Assert.True(vm.CommitName());
        Assert.False(vm.HasCustomName); Assert.Equal(name, vm.DisplayName);
        var diameter = vm.Parameters.Single(); diameter.PendingText = "120"; Assert.True(diameter.Commit());
        Assert.False(vm.HasCustomName); Assert.NotEqual(name, vm.DisplayName); Assert.Contains("120", vm.DisplayName);
    }
    [Fact]
    public void UnchangedCustomNameEditPreservesNameAcrossDimensionChanges()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        vm.BeginNameEdit(); vm.PendingName = "My profile"; Assert.True(vm.CommitName());
        Assert.True(vm.HasCustomName);
        vm.BeginNameEdit(); Assert.True(vm.CommitName());
        Assert.False(vm.IsEditingName); Assert.True(vm.HasCustomName); Assert.Equal("My profile", vm.DisplayName);
        var diameter = vm.Parameters.Single(); diameter.PendingText = "120"; Assert.True(diameter.Commit());
        Assert.True(vm.HasCustomName); Assert.Equal("My profile", vm.DisplayName);
    }
    [Fact]
    public void UnchangedLibraryNameEditPreservesLoadedCustomName()
    {
        var library = new Library(); var entry = new SectionLibraryEntry("Library profile", new CircleSectionGeometry(M(.04)));
        library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle); vm.LoadPreset(entry);
        Assert.True(vm.HasCustomName); Assert.Equal(entry.Name, vm.DisplayName);
        vm.BeginNameEdit(); Assert.True(vm.CommitName());
        Assert.False(vm.IsEditingName); Assert.True(vm.HasCustomName); Assert.Equal(entry.Name, vm.DisplayName);
        var diameter = vm.Parameters.Single(); diameter.PendingText = "120"; Assert.True(diameter.Commit());
        Assert.True(vm.HasCustomName); Assert.Equal(entry.Name, vm.DisplayName);
        Assert.Equal(M(.04), Assert.IsType<CircleSectionGeometry>(entry.Section).Diameter);
    }
    [Fact]
    public void CompactSectionInputsChangeOnlyWithArrangedHeight()
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.ISection); Fill(vm);
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.Measure(new Size(1100, 650)); view.Arrange(new Rect(0, 0, 1100, 650));
            var workspace = view.FindControl<Grid>("InputWorkspace")!;
            Assert.Contains("compactSectionInputs", workspace.Classes);

            view.InvalidateMeasure(); view.Measure(new Size(1100, 900));
            Assert.Contains("compactSectionInputs", workspace.Classes);
            view.Arrange(new Rect(0, 0, 1100, 900));
            Assert.DoesNotContain("compactSectionInputs", workspace.Classes);

            view.InvalidateMeasure(); view.Measure(new Size(1100, 650));
            Assert.DoesNotContain("compactSectionInputs", workspace.Classes);
            view.Arrange(new Rect(0, 0, 1100, 650));
            Assert.Contains("compactSectionInputs", workspace.Classes);
        }
        finally { window.Close(); }
    }
    [Fact]
    public void HeaderLibraryActionTracksDraftValidity()
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel();
        var view = new SectionDefinitionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            var header = view.FindControl<Grid>("LibrarySaveAction")!;
            var button = view.FindControl<Button>("SaveLibraryButton")!;
            Assert.Same(vm.SaveLibraryCommand, button.Command);
            AssertVisibility(false);
            vm.SelectForm(SectionEditorType.ISection); AssertVisibility(false);
            var input = vm.Parameters.Single(p => p.Key == "h");
            input.PendingText = "100"; Assert.True(input.Commit()); AssertVisibility(false);
            Fill(vm); Assert.Null(vm.SelectedAxis); Assert.False(vm.CanConfirm); AssertVisibility(true);

            string valid = input.PendingText;
            input.PendingText = "120"; AssertVisibility(false);
            Assert.True(input.Commit()); AssertVisibility(true);
            input.PendingText = "invalid"; AssertVisibility(false);
            Assert.False(input.Commit()); AssertVisibility(false);
            input.PendingText = valid; Assert.True(input.Commit()); AssertVisibility(true);

            var hidden = new SectionDefinitionViewModel(showLibraryOption: false);
            hidden.SelectForm(SectionEditorType.ISection); Fill(hidden); view.DataContext = hidden;
            AssertVisibility(false);

            void AssertVisibility(bool expected)
            {
                Dispatcher.UIThread.RunJobs();
                view.Measure(new Size(1100, 650)); view.Arrange(new Rect(0, 0, 1100, 650));
                Assert.Equal(expected, header.IsVisible);
                Assert.Equal(expected, button.IsEffectivelyVisible);
            }
        }
        finally { window.Close(); }
    }
    [Fact]
    public async Task SaveUsesCurrentAutomaticNameAndDuplicateNeverOverwrites()
    {
        var library = new Library(); var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        string name = vm.DisplayName; var section = vm.Section;
        Assert.True(await vm.SaveLibraryAsync());
        var original = Assert.Single(library.Entries);
        Assert.Equal(name, original.Name); Assert.Same(section, original.Section);
        Assert.Equal(name, vm.DisplayName); Assert.False(vm.HasCustomName);
        Assert.True(vm.CanSaveLibrary); Assert.Null(vm.LibraryError);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Equal(Strings.LibraryNameConflict, vm.LibraryError);
        Assert.Same(original, Assert.Single(library.Entries)); Assert.Equal(name, vm.DisplayName); Assert.True(vm.CanConfirm);
        var diameter = vm.Parameters.Single(); diameter.PendingText = "120"; diameter.Commit();
        Assert.Null(vm.LibraryError); Assert.NotEqual(name, vm.DisplayName);
        Assert.True(await vm.SaveLibraryAsync()); Assert.Equal(vm.DisplayName, library.Entries[1].Name);
        Assert.Same(original, library.Entries[0]);
    }
    [Fact]
    public async Task RenamingDraftDoesNotCheckLibraryAndApplyDoesNotSave()
    {
        var library = new Library(); var original = new SectionLibraryEntry("Existing", new CircleSectionGeometry(M(.04)));
        library.Source.Add(original);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        vm.BeginNameEdit(); vm.PendingName = " existing "; Assert.True(vm.CommitName());
        Assert.Equal("existing", vm.DisplayName); Assert.Null(vm.LibraryError); Assert.True(vm.CanConfirm);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Equal(Strings.LibraryNameConflict, vm.LibraryError);
        Assert.Same(original, Assert.Single(library.Entries)); Assert.False(vm.IsEditingName);
        vm.BeginNameEdit(); vm.PendingName = ""; Assert.False(vm.CommitName()); Assert.False(vm.CanSaveLibrary);
        vm.CancelNameEdit(); vm.BeginNameEdit(); vm.PendingName = "My current name"; Assert.True(vm.CommitName());
        Assert.Null(vm.LibraryError); Assert.True(await vm.SaveLibraryAsync());
        Assert.Equal(vm.DisplayName, library.Entries[1].Name);
        int closes = 0; vm.CloseRequested += _ => closes++;
        Assert.True(await vm.ConfirmAsync()); Assert.Equal(1, closes); Assert.Equal(2, library.Entries.Count);
    }
    [Theory]
    [InlineData(ManualAxisConfiguration.YZ, SectionAxisDesignation.Z, ManualAxisConfiguration.YZ, SectionAxisDesignation.Z)]
    [InlineData(ManualAxisConfiguration.YZ, SectionAxisDesignation.Z, ManualAxisConfiguration.Z, SectionAxisDesignation.Z)]
    [InlineData(ManualAxisConfiguration.YZ, SectionAxisDesignation.Z, ManualAxisConfiguration.UV, null)]
    public void ManualPresetRetainsOnlyAvailableCurrentBendingAxis(ManualAxisConfiguration current,
        SectionAxisDesignation selected, ManualAxisConfiguration preset, SectionAxisDesignation? expected)
    {
        var source = new SectionDefinitionViewModel(); source.SelectForm(SectionEditorType.Manual);
        source.Configuration = source.Configurations.Single(c => c.Value == preset); Fill(source);
        var last = source.Parameters.Last(); last.PendingText = "50000"; last.Commit();
        var library = new Library(); var entry = new SectionLibraryEntry("Manual", source.Section!); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration = vm.Configurations.Single(c => c.Value == current); Fill(vm);
        last = vm.Parameters.Last(); last.PendingText = "60000"; last.Commit(); vm.SelectedAxis = selected;
        Assert.Equal(selected, vm.SelectedAxis); vm.LoadPreset(entry);
        Assert.Equal(expected, vm.SelectedAxis); Assert.Same(source.Section, entry.Section);
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
        var offset = scroll.Offset;
        var name = view.FindControl<TextBox>("NameInput")!; var automatic = view.FindControl<Button>("ResetNameButton")!;
        var error = view.FindControl<SectionInputHint>("NameErrorHint")!;
        vm.BeginNameEdit(); Layout(); Assert.True(name.IsFocused); Assert.True(automatic.IsEffectivelyVisible);
        vm.PendingName = " "; Press(name, Key.Enter); Layout(); Assert.True(vm.HasNameError); Assert.True(vm.IsEditingName);
        error.Focus(); Layout(); Assert.True(ToolTip.GetIsOpen(error)); Assert.True(vm.IsEditingName);
        Press(error, Key.Escape); Layout(); Assert.True(vm.IsEditingName); Assert.False(ToolTip.GetIsOpen(error));
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
        var libraryError = view.FindControl<SectionInputHint>("LibraryErrorHint")!;
        Assert.Contains("librarySaveError", save.Classes); Assert.True(libraryError.IsEffectivelyVisible);
        Assert.True(libraryError.IsFocused); Assert.True(ToolTip.GetIsOpen(libraryError));
        Assert.Single(library.Entries); Assert.False(vm.IsEditingName); Assert.Equal("EXISTING", vm.DisplayName);
        libraryError.Focus(); Layout(); Assert.True(ToolTip.GetIsOpen(libraryError));
        Press(libraryError, Key.Escape); Layout(); Assert.False(ToolTip.GetIsOpen(libraryError)); Assert.True(window.IsVisible);
        save.Focus(); save.Command!.Execute(null); Layout();
        Assert.True(libraryError.IsFocused); Assert.True(ToolTip.GetIsOpen(libraryError)); Assert.Single(library.Entries);
        Assert.DoesNotContain(view.FindControl<ItemsControl>("LibraryEntryList")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.LibraryError);
        vm.BeginNameEdit(); Layout(); vm.PendingName = new string('W', 120); Press(name, Key.Enter); Layout();
        save.Command!.Execute(null); Layout();
        Assert.Equal(vm.DisplayName, library.Entries[1].Name); Assert.False(libraryError.IsEffectivelyVisible);
        Assert.Null(save.Flyout); Assert.Empty(window.OwnedWindows);
        Assert.Equal(beforeHeader, header.Bounds); Assert.Equal(beforeColumns, columns.Bounds);
        Assert.Equal(beforeBody, columns.TranslatePoint(default, view)); Assert.Equal(offset, scroll.Offset);
        window.Close();

        void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            { Dispatcher.UIThread.RunJobs(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); }
            if (initialHeader is { } initial) Assert.Equal(initial, header.Bounds);
        }
        static void Press(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }
    [Fact]
    public void SectionWorkspaceHasNoLocalMenuAndKeepsItsHeaderActions()
    {
        using var environment = new DesktopControlEnvironment();
        var vm = new SectionDefinitionViewModel(); var view = new SectionDefinitionView { DataContext = vm };
        var owner = new Window { Content = view }; owner.Show();
        view.Measure(new Size(1100, 650)); view.Arrange(new Rect(0, 0, 1100, 650));
        Assert.Empty(view.GetVisualDescendants().OfType<Menu>());
        Assert.Same(vm.SaveLibraryCommand, view.FindControl<Button>("SaveLibraryButton")!.Command);
        Assert.Same(vm.CancelCommand, view.FindControl<Button>("CancelButton")!.Command);
        Assert.Same(vm.ConfirmCommand, view.FindControl<Button>("ConfirmButton")!.Command);
        owner.Close();
    }
    [Fact]
    public void QuickAccessFiltersByShapeCapsFiveAndPreservesLibraryOrder()
    {
        var library = new Library();
        for (int i = 0; i < 7; i++)
        {
            library.Source.Add(new("Circle " + i, new CircleSectionGeometry(M(.04 + i * .01))));
            library.Source.Add(new("Rectangle " + i, new RectangleSectionGeometry(M(.1), M(.2))));
        }
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => "Circle " + i), vm.LibraryEntries.Select(e => e.Name));
        Assert.False(vm.IsLibraryEmpty);
        vm.SelectForm(SectionEditorType.ISection); Assert.Empty(vm.LibraryEntries); Assert.True(vm.IsLibraryEmpty);
        library.Error = "Cannot read library"; vm.SelectForm(SectionEditorType.ISection);
        Assert.False(vm.IsLibraryEmpty); Assert.Null(vm.LibraryError);
    }
    [Theory]
    [InlineData("de-DE", 1500, 980)] [InlineData("en-US", 1500, 980)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1000, 600)] [InlineData("en-US", 1000, 600)]
    public void OnlyLibraryContentScrollsAndAllInputsStayVisible(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var library = new Library(); var vm = new SectionDefinitionViewModel(new(units), library);
            var view = new SectionDefinitionView { DataContext = vm }; var window = new Window { Content = view, Width = width, Height = height };
            window.Show();
            foreach (var type in vm.Forms.Select(f => f.Value).Append(SectionEditorType.Manual))
            {
                vm.SelectForm(type); Fill(vm);
                for (int i = 0; i < 5; i++) library.Source.Add(new(type + " saved " + i, vm.Section!));
                vm.SelectForm(type); Layout();
                var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
                var heading = view.FindControl<StackPanel>("LibraryHeading")!;
                var scroll = view.FindControl<ScrollViewer>("LibraryScroll")!;
                var list = view.FindControl<ItemsControl>("LibraryEntryList")!;
                var workspace = view.FindControl<Grid>("InputWorkspace")!;
                double availableScrollHeight = workspace.Bounds.Height - pinned.DesiredSize.Height - heading.DesiredSize.Height;
                bool libraryVisible = availableScrollHeight >= 96;
                Assert.Equal(libraryVisible, view.FindControl<Grid>("LibraryQuickSelection")!.IsEffectivelyVisible);
                Assert.Equal(libraryVisible, heading.IsEffectivelyVisible);
                Assert.Equal(libraryVisible, scroll.IsEffectivelyVisible);
                Assert.All(heading.GetVisualDescendants().OfType<Control>(), c => Assert.Equal(libraryVisible, c.IsEffectivelyVisible));
                Assert.DoesNotContain(pinned.GetVisualAncestors(), a => a is ScrollViewer);
                Assert.Same(scroll, list.FindAncestorOfType<ScrollViewer>());
                foreach (var box in pinned.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
                {
                    var position = box.TranslatePoint(default, view)!.Value;
                    Assert.InRange(position.Y, 0, height - box.Bounds.Height);
                    if (libraryVisible)
                        Assert.True(position.Y + box.Bounds.Height <= heading.TranslatePoint(default, view)!.Value.Y, type + ": inputs overlap library heading");
                }
                if (culture == "de-DE" && width == 1000 && height == 600 && type == SectionEditorType.ISection)
                    Assert.False(libraryVisible);
                if (!libraryVisible)
                {
                    Assert.False(view.FindControl<StackPanel>("LibraryContent")!.IsEffectivelyVisible);
                    continue;
                }
                var profile = view.FindControl<Border>("ProfileCard")!;
                var profileLeft = profile.TranslatePoint(default, view)!.Value.X;
                var profileRight = profileLeft + profile.Bounds.Width;
                foreach (var entry in list.GetVisualDescendants().OfType<Button>())
                {
                    var left = entry.TranslatePoint(default, view)!.Value.X;
                    Assert.InRange(Math.Abs(left - profileLeft), 0, .5);
                    Assert.InRange(Math.Abs(left + entry.Bounds.Width - profileRight), 0, .5);
                }
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
                if (bar.IsEffectivelyVisible)
                {
                    var barLeft = bar.TranslatePoint(default, view)!.Value.X;
                    Assert.True(barLeft >= profileRight + 4, type + ": scrollbar touches cards");
                    var column = view.FindControl<Border>("InputColumn")!;
                    var columnRight = column.TranslatePoint(default, view)!.Value.X + column.Bounds.Width;
                    Assert.InRange(columnRight - (barLeft + bar.Bounds.Width), 0, 2);
                }
                Assert.True(scroll.Viewport.Height >= 96, type + ": insufficient visible library height: " + scroll.Viewport);
                var fixedInputs = pinned.TranslatePoint(default, view); var fixedHeading = heading.TranslatePoint(default, view);
                scroll.Offset = default; Layout();
                var first = list.GetVisualDescendants().OfType<Button>().First();
                var before = first.TranslatePoint(default, view)!.Value;
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)); Layout();
                Assert.Equal(fixedInputs, pinned.TranslatePoint(default, view)); Assert.Equal(fixedHeading, heading.TranslatePoint(default, view));
                if (scroll.Extent.Height > scroll.Viewport.Height + 1)
                    Assert.True(first.TranslatePoint(default, view)!.Value.Y < before.Y);
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
    public void LibraryQuickSelectionReturnsAfterResizeAndUsesActualScrollHeight(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        foreach (var units in new[] { UnitProfile.Default, UnitProfile.UnitedStates })
        {
            var vm = new SectionDefinitionViewModel(new(units), new Library()); vm.SelectForm(SectionEditorType.ISection); Fill(vm);
            var view = new SectionDefinitionView { DataContext = vm, UseLayoutRounding = false };
            var window = new Window { Content = view, Width = 1000, Height = 600, UseLayoutRounding = false }; window.Show();
            var region = view.FindControl<Grid>("LibraryQuickSelection")!;
            var workspace = view.FindControl<Grid>("InputWorkspace")!;
            var pinned = view.FindControl<StackPanel>("PinnedInputs")!;
            var heading = view.FindControl<StackPanel>("LibraryHeading")!;
            var scroll = view.FindControl<ScrollViewer>("LibraryScroll")!;
            Arrange(600); Assert.False(region.IsEffectivelyVisible);
            double thresholdHeight = 600 + 96 - (workspace.Bounds.Height - pinned.DesiredSize.Height - heading.DesiredSize.Height);
            foreach (var height in new[] { thresholdHeight - 1, thresholdHeight, thresholdHeight + 1, 980, 600, thresholdHeight })
            {
                Arrange(height);
                bool visible = height >= thresholdHeight;
                Assert.True(visible == region.IsEffectivelyVisible,
                    $"{culture}/{height}: expected visibility {visible} at threshold {thresholdHeight}; actual {region.IsEffectivelyVisible}");
                Assert.Equal(visible, heading.IsEffectivelyVisible); Assert.Equal(visible, scroll.IsEffectivelyVisible);
                Assert.Equal(visible, view.FindControl<StackPanel>("LibraryContent")!.IsEffectivelyVisible);
                if (visible) Assert.True(scroll.Viewport.Height >= 96 - 1e-7,
                    $"{culture}/{height}: visible library viewport is only {scroll.Viewport.Height} DIP");
                foreach (var box in pinned.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
                    Assert.InRange(box.TranslatePoint(default, view)!.Value.Y, 0, height - box.Bounds.Height);
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
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)]
    public void EachParametricPresetRestoresOriginalParametersAndProperties(SectionEditorType type)
    {
        var source = new SectionDefinitionViewModel(); source.SelectForm(type); Fill(source);
        var library = new Library(); var entry = new SectionLibraryEntry("Preset", source.Section!); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(type);
        Assert.Single(vm.LibraryEntries); vm.LoadPreset(entry);
        Assert.Equal(source.Parameters.Select(p => (p.Key, p.ConfirmedValue)), vm.Parameters.Select(p => (p.Key, p.ConfirmedValue)));
        Assert.Equal(source.Section!.Area, vm.Section!.Area); Assert.Equal(source.Section.Axes, vm.Section.Axes);
        Assert.NotSame(source.Section, vm.Section); Assert.Null(vm.SelectedAxis);
        vm.Parameters[0].PendingText = "invalid"; vm.Parameters[0].Commit(); Assert.Null(vm.Section);
        vm.LoadPreset(entry); Assert.True(vm.HasValidSection); Assert.All(vm.Parameters, p => Assert.False(p.HasError));
    }
    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void PresetLoadKeepsExactSiValuesLibraryNameAndCompatibleAxis(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var library = new Library();
        var original = new ISectionGeometry(M(.20312345678901234), M(.10712345678901234), M(.00512345678901234), M(.01012345678901234), M(.00312345678901234));
        var entry = new SectionLibraryEntry("Original", original); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(new(UnitProfile.UnitedStates), library); vm.SelectForm(SectionEditorType.ISection);
        vm.BeginNameEdit(); vm.PendingName = "Pending draft name"; vm.LoadPreset(entry);
        Assert.False(vm.IsEditingName); Assert.Equal(entry.Name, vm.DisplayName);
        var loaded = Assert.IsType<ISectionGeometry>(vm.Section);
        Assert.Equal(original.Height, loaded.Height); Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.WebThickness, loaded.WebThickness); Assert.Equal(original.FlangeThickness, loaded.FlangeThickness); Assert.Equal(original.Radius, loaded.Radius);
        Assert.All(vm.Parameters, p => { Assert.False(p.IsPending); Assert.False(p.HasError); p.Commit(); });
        Assert.Null(vm.SelectedAxis); vm.SelectedAxis = SectionAxisDesignation.Z;
        vm.LoadPreset(entry); Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis);
        vm.BeginNameEdit(); vm.PendingName = "My draft"; Assert.True(vm.CommitName());
        var height = vm.Parameters.Single(p => p.Key == "h"); height.PendingText = "10"; height.Commit();
        vm.SelectedAxis = SectionAxisDesignation.Y; vm.LoadPreset(entry);
        Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis); Assert.Equal(entry.Name, vm.DisplayName);
        Assert.True(vm.HasCustomName);
        height.PendingText = "11"; height.Commit(); Assert.Equal(entry.Name, vm.DisplayName);
        Assert.Same(original, entry.Section); Assert.Equal("Original", entry.Name); Assert.Single(library.Entries);
        vm.ResetNameCommand.Execute(null); Assert.False(vm.HasCustomName); Assert.NotEqual(entry.Name, vm.DisplayName); Assert.Contains("11", vm.DisplayName);
        Assert.Contains("203", InputQuantityFormatter.Format(loaded.Height.Meters, UnitCatalog.Millimeter));
    }
    [Theory]
    [InlineData(ManualAxisConfiguration.Y)] [InlineData(ManualAxisConfiguration.Z)]
    [InlineData(ManualAxisConfiguration.U)] [InlineData(ManualAxisConfiguration.V)]
    [InlineData(ManualAxisConfiguration.YZ)] [InlineData(ManualAxisConfiguration.UV)]
    public void ManualPresetsRestoreAxisConfigurationAndValuesWithoutSelectingBendingAxis(ManualAxisConfiguration configuration)
    {
        var source = new SectionDefinitionViewModel(); source.SelectForm(SectionEditorType.Manual);
        source.Configuration = source.Configurations.Single(c => c.Value == configuration); Fill(source);
        if (source.Parameters.Count > 3)
        { var input = source.Parameters.Last(); input.PendingText = "30000"; input.Commit(); }
        var library = new Library(); var entry = new SectionLibraryEntry("Manual", source.Section!); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Manual); vm.LoadPreset(entry);
        Assert.Equal(entry.Name, vm.DisplayName); Assert.True(vm.HasCustomName);
        Assert.Equal(configuration, vm.Configuration.Value); Assert.Equal(source.Section!.Area, vm.Section!.Area);
        Assert.Equal(source.Section.Axes, vm.Section.Axes); Assert.Null(vm.SelectedAxis);
        Assert.Equal(!vm.RequiresAxisSelection, vm.CanConfirm); Assert.True(vm.CanSaveLibrary);
    }
    [Fact]
    public async Task SaveNeedsNoBendingAxisStaysOpenAndDiscardDoesNotUndoExplicitSave()
    {
        var library = new Library(); var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(SectionEditorType.ISection); Fill(vm);
        Assert.True(vm.RequiresAxisSelection); Assert.Null(vm.SelectedAxis);
        Assert.False(vm.CanConfirm); Assert.True(vm.ShowLibraryAction); Assert.True(vm.CanSaveLibrary);
        int closes = 0; vm.CloseRequested += result => { Assert.Null(result); closes++; };
        Assert.True(await vm.SaveLibraryAsync()); Assert.Equal(0, closes); Assert.Single(library.Entries);
        Assert.True(vm.CanSaveLibrary); Assert.False(vm.HasLibraryError);
        vm.CancelCommand.Execute(null); Assert.Equal(1, closes); Assert.Single(library.Entries);
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)] [InlineData(SectionEditorType.Manual)]
    public void LibraryActionRequiresCompleteValidCommittedInputs(SectionEditorType type)
    {
        var vm = new SectionDefinitionViewModel(); Assert.False(vm.ShowLibraryAction);
        vm.SelectForm(type); Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        var input = vm.Parameters[0]; input.PendingText = "100";
        Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        if (vm.Parameters.Count > 1)
        { input.Commit(); Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary); }
        Fill(vm); Assert.True(vm.ShowLibraryAction); Assert.True(vm.CanSaveLibrary); Assert.Null(vm.SelectedAxis);
        Assert.Equal(!vm.RequiresAxisSelection, vm.CanConfirm);
        var valid = input.PendingText;
        input.PendingText = "invalid"; Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        input.Commit(); Assert.False(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        input.PendingText = valid; input.Commit(); Assert.True(vm.ShowLibraryAction); Assert.True(vm.CanSaveLibrary);
        vm.BeginNameEdit(); vm.PendingName = " "; Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        Assert.False(vm.CommitName()); Assert.False(vm.CanSaveLibrary); vm.CancelNameEdit(); Assert.True(vm.CanSaveLibrary);
        var hidden = new SectionDefinitionViewModel(showLibraryOption: false); hidden.SelectForm(type); Fill(hidden);
        Assert.False(hidden.ShowLibraryAction); Assert.False(hidden.CanSaveLibrary);
    }
    [Fact]
    public async Task PendingInputAndInvalidNameBlockSaveButLibraryFailuresNeverBlockApply()
    {
        var library = new Library { SaveError = "Write failed" }; var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        vm.Parameters[0].PendingText = "101"; Assert.False(vm.SaveLibraryCommand.CanExecute(null)); Assert.False(vm.CanConfirm);
        vm.Parameters[0].Commit(); vm.BeginNameEdit(); vm.PendingName = ""; Assert.False(vm.CanSaveLibrary); vm.CancelNameEdit();
        Assert.False(await vm.SaveLibraryAsync()); Assert.Equal("Write failed", vm.LibraryError); Assert.True(vm.CanConfirm);
        Assert.True(await vm.ConfirmAsync()); Assert.Empty(library.Entries);
        library.SaveError = null; Assert.True(await vm.SaveLibraryAsync());
        vm.BeginNameEdit(); vm.PendingName = "Second"; vm.CommitName(); Assert.True(vm.CanSaveLibrary);
        vm.ResetNameCommand.Execute(null); Assert.True(vm.CanSaveLibrary); Assert.True(vm.CanConfirm); Assert.Null(vm.LibraryError);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Equal(Strings.LibraryNameConflict, vm.LibraryError);
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
        var nameError = view.FindControl<SectionInputHint>("NameErrorHint")!;
        nameError.Focus(); Layout(); CaptureTip(nameError, "name-error-tooltip");
        vm.CancelNameEdit(); Capture("name-normal");
        library.Source.Add(new(vm.DisplayName, vm.Section!));
        Assert.False(await vm.SaveLibraryAsync()); Layout(); Capture("library-conflict");
        var libraryError = view.FindControl<SectionInputHint>("LibraryErrorHint")!;
        libraryError.Focus(); Layout(); CaptureTip(libraryError, "library-error-tooltip");
        vm.ResetName();
        Layout();
        var radiusHint = view.GetVisualDescendants().OfType<SectionInputHint>().Single(h => h.IsEffectivelyVisible && h.DataContext is SectionDefinitionInput { Key: "r" });
        Assert.Equal(new Size(18, 18), radiusHint.Bounds.Size);
        Assert.Equal(new CornerRadius(9), radiusHint.CornerRadius);
        var infoIcon = Assert.IsType<SectionFluentIcon>(radiusHint.Content);
        Assert.EndsWith("ic_fluent_info_16_regular.svg", infoIcon.Path);
        Assert.Equal(new Thickness(0), radiusHint.BorderThickness);
        Assert.EndsWith("ic_fluent_edit_16_regular.svg", view.FindControl<SectionFluentIcon>("TitleEditIcon")!.Path);
        Assert.DoesNotContain(view.FindControl<StackPanel>("HeaderActions")!.Children, c => c == view.FindControl<Button>("ChangeFormButton"));
        var layout = SectionPreviewDrawingLayout.Create(vm.Preview!, new Size(724, 700), UnitCatalog.Millimeter, _ => new Size(80, 14));
        Assert.Equal(310, Math.Max(layout.ProfileBounds.Width, layout.ProfileBounds.Height), 6);
        radiusHint.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(ToolTip.GetIsOpen(radiusHint)); Capture("info-focus"); CaptureTip(radiusHint, "info-tooltip");
        ToolTip.SetIsOpen(radiusHint, false);
        var input = vm.Parameters.Single(p => p.Key == "tw"); input.PendingText = "invalid"; input.Commit(); Layout();
        var errorHint = view.GetVisualDescendants().OfType<SectionInputHint>().Single(h => h.IsEffectivelyVisible && h.Classes.Contains("errorHint"));
        Assert.EndsWith("ic_fluent_error_circle_16_regular.svg", Assert.IsType<SectionFluentIcon>(errorHint.Content).Path);
        errorHint.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(ToolTip.GetIsOpen(errorHint)); Capture("error-focus"); CaptureTip(errorHint, "error-tooltip");
        ToolTip.SetIsOpen(errorHint, false);
        input.PendingText = "10"; input.Commit(); vm.BeginNameEdit(); vm.PendingName = new string('W', 80); vm.CommitName(); Capture("long-name");
        vm.ResetNameCommand.Execute(null); Capture("reset-name"); window.Close();

        void Layout()
        {
            for (int pass = 0; pass < 3; pass++)
            { Dispatcher.UIThread.RunJobs(); window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); }
        }
        void CaptureTip(SectionInputHint hint, string name)
        {
            var text = Assert.IsType<TextBlock>(ToolTip.GetTip(hint));
            var tip = Assert.Single(text.GetVisualAncestors().OfType<ToolTip>());
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
