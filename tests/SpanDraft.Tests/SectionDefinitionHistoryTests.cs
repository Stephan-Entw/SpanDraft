using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class SectionDefinitionHistoryTests
{
    private static SectionDefinitionViewModel Rectangle() => new(initialState:
        new(new RectangleSectionGeometry(M(.1), M(.1)), SectionAxisDesignation.Y));
    private static SectionDefinitionInput Input(SectionDefinitionViewModel vm, string key) => vm.Parameters.Single(i => i.Key == key);
    private static void Commit(SectionDefinitionViewModel vm, string key, string text)
    { var input = Input(vm, key); input.PendingText = text; Assert.True(input.Commit()); }
    private static void Name(SectionDefinitionViewModel vm, string text)
    { vm.BeginNameEdit(); vm.PendingName = text; Assert.True(vm.CommitName()); }
    private static void Fill(SectionDefinitionViewModel vm)
    {
        foreach (var input in vm.Parameters.ToArray()) Commit(vm, input.Key, input.Key switch
        { "b" => "100", "h" => "150", "t" or "tw" => "5", "tf" => "10", "r" => "0", "A" => "1000", _ => input.Key.StartsWith('I') ? "1000000" : "20000" });
    }
    [Fact]
    public void DimensionsRestoreConfirmedSiValuesWithoutRawTextAndRecalculate()
    {
        var vm = Rectangle(); Assert.False(vm.CanUndo);
        Commit(vm, "h", "150"); Assert.True(vm.CanUndo);
        vm.HighlightedParameter = "h";
        Assert.True(vm.Undo()); Assert.Equal(.1, Input(vm, "h").ConfirmedValue);
        Assert.Equal("100", Input(vm, "h").PendingText); Assert.False(Input(vm, "h").IsPending);
        Assert.Null(vm.HighlightedParameter); Assert.Equal(.01, vm.Section!.Area.SquareMeters, 10);
        Assert.True(vm.Redo()); Assert.Equal(.15, Input(vm, "h").ConfirmedValue);
        Assert.Equal(.015, vm.Section!.Area.SquareMeters, 10);
    }
    [Fact]
    public void GeometryErrorIsAConfirmedHistoryStepAndCanBeCorrectedAfterRedo()
    {
        var vm = new SectionDefinitionViewModel(initialState: new(
            new RectangularHollowSectionGeometry(M(.1), M(.15), M(.005), M(0)), SectionAxisDesignation.Z, "Tube"));
        var thickness = Input(vm, "t"); thickness.PendingText = "80";
        Assert.False(thickness.Commit());
        AssertGeometryError(); Assert.True(vm.CanUndo);

        Assert.True(vm.Undo()); AssertValid(.005, SectionAxisDesignation.Z); Assert.False(vm.CanUndo);
        Assert.True(vm.Redo()); AssertGeometryError(); Assert.False(vm.CanRedo);

        Commit(vm, "t", "6"); AssertValid(.006, null);
        Assert.True(vm.Undo()); AssertGeometryError();
        Assert.True(vm.Redo()); AssertValid(.006, null);
        Assert.True(vm.Undo()); AssertGeometryError();
        Assert.True(vm.Undo()); AssertValid(.005, SectionAxisDesignation.Z); Assert.False(vm.CanUndo);

        void AssertGeometryError()
        {
            var input = Input(vm, "t");
            Assert.Equal(.08, input.ConfirmedValue); Assert.Equal("80", input.PendingText);
            Assert.True(input.HasError); Assert.False(input.HasParseError); Assert.False(input.IsPending);
            Assert.Null(vm.Section); Assert.False(vm.HasValidSection); Assert.False(vm.CanConfirm);
            Assert.Equal("Tube", vm.DisplayName); Assert.Null(vm.SelectedAxis);
        }
        void AssertValid(double value, SectionAxisDesignation? axis)
        {
            var section = Assert.IsType<RectangularHollowSectionGeometry>(vm.Section);
            Assert.Equal(.1, section.Width.Meters); Assert.Equal(.15, section.Height.Meters);
            Assert.Equal(value, section.WallThickness.Meters); Assert.Equal(0, section.OuterRadius.Meters);
            Assert.Equal("Tube", vm.DisplayName); Assert.Equal(axis, vm.SelectedAxis);
            Assert.All(vm.Parameters, input => { Assert.False(input.IsPending); Assert.False(input.HasError); Assert.False(input.HasParseError); });
        }
    }
    [Fact]
    public void NewConfirmedChangeFromGeometryErrorReplacesOnlyTheRedoBranch()
    {
        var vm = new SectionDefinitionViewModel(initialState: new(
            new RectangularHollowSectionGeometry(M(.1), M(.15), M(.005), M(0)), SectionAxisDesignation.Z));
        Input(vm, "t").PendingText = "80"; Assert.False(Input(vm, "t").Commit());
        Commit(vm, "t", "6"); Assert.True(vm.Undo());
        Assert.Null(vm.Section); Assert.Equal(.08, Input(vm, "t").ConfirmedValue); Assert.True(vm.CanRedo);

        Commit(vm, "t", "7"); Assert.False(vm.CanRedo); Assert.False(vm.Redo());
        Assert.True(vm.Undo()); Assert.Null(vm.Section); Assert.Equal(.08, Input(vm, "t").ConfirmedValue);
        Assert.False(Input(vm, "t").HasParseError); Assert.True(Input(vm, "t").HasError);
        Assert.True(vm.Undo()); Assert.Equal(.005, Input(vm, "t").ConfirmedValue); Assert.True(vm.HasValidSection);
        Assert.False(vm.CanUndo); Assert.True(vm.Redo()); Assert.Null(vm.Section);
        Assert.True(vm.Redo()); Assert.Equal(.007, Input(vm, "t").ConfirmedValue); Assert.True(vm.HasValidSection);
        Assert.False(vm.CanRedo);
    }
    [Fact]
    public void GalleryNavigationIsNotAHistoryStepAndEmptySelectionCanBeRestored()
    {
        var empty = new SectionDefinitionViewModel(); empty.SelectForm(SectionEditorType.Rectangle);
        Assert.True(empty.Undo()); Assert.Null(empty.Type); Assert.True(empty.IsChoosingForm);
        Assert.True(empty.Redo()); Assert.Equal(SectionEditorType.Rectangle, empty.Type);
        var vm = Rectangle(); vm.ChangeFormCommand.Execute(null); Assert.True(vm.IsChoosingForm); Assert.False(vm.CanUndo);
        vm.SelectForm(SectionEditorType.ISection); Assert.True(vm.Undo());
        Assert.Equal(SectionEditorType.Rectangle, vm.Type); Assert.False(vm.IsChoosingForm);
        Assert.True(vm.Redo()); Assert.Equal(SectionEditorType.ISection, vm.Type);
    }
    [Fact]
    public void EveryFormRetainsItsOwnNameAndDraftAcrossMultipleUndoRedoSteps()
    {
        var vm = Rectangle(); Name(vm, "A"); vm.SelectForm(SectionEditorType.ISection); Fill(vm); Name(vm, "B");
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Equal("A", vm.DisplayName);
        Assert.True(vm.Undo()); Assert.Equal("B", vm.DisplayName);
        Assert.True(vm.Undo()); Assert.False(vm.HasCustomName);
        Assert.True(vm.Redo()); Assert.Equal("B", vm.DisplayName);
        Assert.True(vm.Redo()); Assert.Equal("A", vm.DisplayName);
        vm.SelectForm(SectionEditorType.ISection); Assert.Equal("B", vm.DisplayName); Assert.Equal(.15, Input(vm, "h").ConfirmedValue);
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Equal("A", vm.DisplayName); Assert.Equal(.1, Input(vm, "h").ConfirmedValue);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BendingAxisChangesAreReversible(bool principal)
    {
        var section = principal ? (ISectionDefinition)new AngleSectionGeometry(M(.1), M(.15), M(.005), M(0))
            : new RectangleSectionGeometry(M(.1), M(.15));
        var first = principal ? SectionAxisDesignation.U : SectionAxisDesignation.Y;
        var second = principal ? SectionAxisDesignation.V : SectionAxisDesignation.Z;
        var vm = new SectionDefinitionViewModel(initialState: new(section, first));
        vm.SelectedAxis = second; Assert.True(vm.Undo()); Assert.Equal(first, vm.SelectedAxis);
        Assert.True(vm.Redo()); Assert.Equal(second, vm.SelectedAxis);
    }
    [Fact]
    public void ManualConfigurationsRestoreAllAxisInputsAndTheirRememberedSelection()
    {
        var vm = new SectionDefinitionViewModel(); vm.SelectForm(SectionEditorType.Manual);
        vm.Configuration = vm.Configurations.Single(c => c.Value == ManualAxisConfiguration.YZ); Fill(vm);
        Commit(vm, "IZ", "2000000"); vm.SelectedAxis = SectionAxisDesignation.Z;
        vm.Configuration = vm.Configurations.Single(c => c.Value == ManualAxisConfiguration.UV); Fill(vm);
        Commit(vm, "IV", "3000000"); vm.SelectedAxis = SectionAxisDesignation.V;
        vm.Configuration = vm.Configurations.Single(c => c.Value == ManualAxisConfiguration.YZ);
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis);
        Assert.True(vm.Undo()); Assert.Equal(ManualAxisConfiguration.UV, vm.Configuration.Value);
        Assert.Equal(SectionAxisDesignation.V, vm.SelectedAxis); Assert.Equal(3e-6, Input(vm, "IV").ConfirmedValue);
        Assert.True(vm.Redo()); Assert.Equal(ManualAxisConfiguration.YZ, vm.Configuration.Value);
        Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis); Assert.Equal(2e-6, Input(vm, "IZ").ConfirmedValue);
    }
    [Fact]
    public void NamesAndResetEachProduceOneStepButUnchangedConfirmationDoesNot()
    {
        var vm = Rectangle(); string automatic = vm.DisplayName;
        vm.BeginNameEdit(); Assert.True(vm.CommitName()); vm.ResetName(); Assert.False(vm.CanUndo);
        Name(vm, "Custom"); Assert.True(vm.Undo()); Assert.Equal(automatic, vm.DisplayName); Assert.False(vm.CanUndo);
        Assert.True(vm.Redo()); vm.ResetName(); Assert.Equal(automatic, vm.DisplayName);
        Assert.True(vm.Undo()); Assert.Equal("Custom", vm.DisplayName);
        Assert.True(vm.Redo()); Assert.Equal(automatic, vm.DisplayName);
    }
    [Fact]
    public void TypingErrorsUnchangedCommitsAndPresentationNeverCreateHistoryOrEraseRedo()
    {
        var vm = Rectangle(); Commit(vm, "h", "150"); Assert.True(vm.Undo());
        Input(vm, "h").PendingText = "bad"; Assert.False(vm.CanRedo); Assert.False(vm.CanUndo);
        Assert.False(Input(vm, "h").Commit()); Assert.True(vm.CanRedo);
        Assert.True(Input(vm, "h").HasParseError); Assert.False(Input(vm, "h").LastCommitChangedValue);
        Assert.Equal(.1, Input(vm, "h").ConfirmedValue); Assert.False(vm.CanUndo);
        Commit(vm, "h", "100"); Assert.False(vm.CanUndo); Assert.True(vm.CanRedo);
        Assert.False(Input(vm, "h").HasParseError); Assert.True(vm.HasValidSection);
        typeof(SectionDefinitionViewModel).GetMethod("ApplyResultPresentation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(vm, [ResultPresentationOptions.Default]);
        vm.HighlightedParameter = "b"; Assert.True(vm.CanRedo); Assert.False(vm.CanUndo);
        vm.BeginNameEdit(); Assert.False(vm.CanRedo); vm.CancelNameEdit(); Assert.True(vm.CanRedo);
        Commit(vm, "h", "175"); Assert.False(vm.CanRedo);
    }
    [Fact]
    public void PendingInputInInactiveFormAlsoBlocksHistory()
    {
        var vm = Rectangle(); Input(vm, "h").PendingText = "12";
        vm.SelectForm(SectionEditorType.Circle); Assert.False(vm.CanUndo); Assert.False(vm.Undo());
        vm.SelectForm(SectionEditorType.Rectangle); Assert.False(vm.CanUndo);
        Commit(vm, "h", "120"); Assert.True(vm.CanUndo);
    }
    [Fact]
    public void HistoryIsBoundedToTwoHundredSteps()
    {
        var vm = Rectangle(); for (int i = 1; i <= 210; i++) Name(vm, "Name " + i);
        int undo = 0; while (vm.Undo()) undo++;
        Assert.Equal(ProjectSession.HistoryLimit, undo); Assert.Equal("Name 10", vm.DisplayName);
        int redo = 0; while (vm.Redo()) redo++; Assert.Equal(ProjectSession.HistoryLimit, redo);
    }
    [Fact]
    public async Task PresetIsOneStepAndUndoNeverRemovesPersistedLibraryEntries()
    {
        var library = new Library(); var entry = new SectionLibraryEntry("Preset", new RectangleSectionGeometry(M(.2), M(.3)));
        library.Items.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library, initialState: new(new RectangleSectionGeometry(M(.1), M(.1)), SectionAxisDesignation.Y));
        vm.LoadPreset(entry); Assert.Equal("Preset", vm.DisplayName); Assert.True(vm.Undo());
        Assert.Equal(.1, Input(vm, "h").ConfirmedValue); Assert.False(vm.CanUndo);
        Assert.True(vm.Redo()); vm.LoadPreset(entry); Assert.True(vm.Undo()); Assert.False(vm.CanUndo);
        Commit(vm, "h", "150"); Assert.True(await vm.SaveLibraryAsync()); Assert.Equal(2, library.Items.Count);
        Assert.True(vm.Undo()); Assert.Equal(2, library.Items.Count);
        library.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Commit(vm, "h", "175"); var saving = vm.SaveLibraryAsync();
        Assert.True(vm.IsBusy); Assert.False(vm.CanUndo); Assert.False(vm.CanRedo);
        library.Completion.SetResult(null); Assert.True(await saving); Assert.True(vm.CanUndo);
    }
    [Fact]
    public async Task ManySectionChangesBecomeOneProjectRevisionOnlyAfterSetupApply()
    {
        var main = new MainWindowViewModel(); main.NewCommand.Execute(null); Assert.True(await main.Setup.ConfirmAsync());
        var session = main.Session!; var original = session.CurrentRevision;
        main.EditProject(); main.Setup.ShowSections(); var vm = main.Setup.Definition!;
        Commit(vm, "h", "150"); Commit(vm, "h", "175"); Name(vm, "Changed"); vm.SelectedAxis = SectionAxisDesignation.Y;
        Assert.Same(original, session.CurrentRevision); Assert.True(main.UndoCommand.CanExecute(null));
        main.UndoCommand.Execute(null); Assert.Equal("Changed", vm.DisplayName); Assert.Null(vm.SelectedAxis);
        main.RedoCommand.Execute(null); Assert.Equal(SectionAxisDesignation.Y, vm.SelectedAxis);
        Assert.True(await vm.ConfirmAsync()); Assert.Empty(session.UndoHistory);
        Assert.True(await main.Setup.ConfirmAsync()); Assert.Single(session.UndoHistory);
        Assert.True(main.Undo()); Assert.Same(original, session.CurrentRevision);
    }
    private sealed class Library : ISectionDefinitionLibrary
    {
        public List<SectionLibraryEntry> Items { get; } = [];
        public IReadOnlyList<SectionLibraryEntry> Entries => Items;
        public bool CanSave => true; public string? Error => null;
        public TaskCompletionSource<string?>? Completion { get; set; }
        public bool ContainsName(string name) => Items.Any(i => i.Name == name);
        public async Task<string?> SaveAsync(SectionLibraryEntry entry)
        { if (Completion is not null) await Completion.Task; Items.Add(entry); return null; }
    }
}
