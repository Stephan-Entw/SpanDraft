using Avalonia.Input;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class SectionDefinitionRedesignTests : SectionDefinitionRedesignTestSupport
{
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
    public async Task SaveUsesCurrentAutomaticNameAndDuplicateNeverOverwrites()
    {
        var library = new Library(); var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        string name = vm.DisplayName; var section = vm.Section;
        Assert.True(await vm.SaveLibraryAsync());
        var original = Assert.Single(library.Entries);
        Assert.Equal(name, original.Name); Assert.Same(section, original.Section);
        Assert.Equal(name, vm.DisplayName); Assert.False(vm.HasCustomName);
        Assert.False(vm.CanSaveLibrary); Assert.True(vm.IsInLibrary); Assert.Null(vm.LibraryError);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Null(vm.LibraryError); Assert.Equal(1, library.SaveCalls);
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
        Assert.True(vm.HasLibraryNameConflict); Assert.False(vm.CanSaveLibrary);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Null(vm.LibraryError); Assert.Equal(0, library.SaveCalls);
        Assert.Same(original, Assert.Single(library.Entries)); Assert.False(vm.IsEditingName);
        vm.BeginNameEdit(); vm.PendingName = ""; Assert.False(vm.CommitName()); Assert.False(vm.CanSaveLibrary);
        vm.CancelNameEdit(); vm.BeginNameEdit(); vm.PendingName = "My current name"; Assert.True(vm.CommitName());
        Assert.Null(vm.LibraryError); Assert.True(await vm.SaveLibraryAsync());
        Assert.Equal(vm.DisplayName, library.Entries[1].Name);
        int closes = 0; vm.CloseRequested += _ => closes++;
        Assert.True(await vm.ConfirmAsync()); Assert.Equal(1, closes); Assert.Equal(2, library.Entries.Count);
    }
    [Theory]
    [InlineData(SectionEditorType.Rectangle)] [InlineData(SectionEditorType.RectangularHollow)]
    [InlineData(SectionEditorType.Circle)] [InlineData(SectionEditorType.CircularHollow)]
    [InlineData(SectionEditorType.ISection)] [InlineData(SectionEditorType.USection)]
    [InlineData(SectionEditorType.TSection)] [InlineData(SectionEditorType.Angle)] [InlineData(SectionEditorType.Manual)]
    public async Task SavedStatusRecognizesLoadedValuesAndTracksEditsUndoAndDeletion(SectionEditorType type)
    {
        var library = new Library(); var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(type); Fill(vm); vm.BeginNameEdit(); vm.PendingName = "Saved"; vm.CommitName();
        Assert.True(await vm.SaveLibraryAsync()); var entry = Assert.Single(library.Entries);
        var loaded = new SectionDefinitionViewModel(library: library); loaded.SelectForm(type); loaded.LoadPreset(entry);
        Assert.True(loaded.IsInLibrary); Assert.False(loaded.CanSaveLibrary);
        loaded.BeginNameEdit(); loaded.PendingName = " SAVED ";
        Assert.True(loaded.IsInLibrary); Assert.False(loaded.HasLibraryNameConflict); loaded.CommitName();
        loaded.SelectedAxis = loaded.AvailableAxes[0].Value; Assert.True(loaded.IsInLibrary);
        var input = loaded.Parameters[0]; input.PendingText = type == SectionEditorType.Manual ? "1100" : "120"; input.Commit();
        Assert.False(loaded.IsInLibrary); Assert.True(loaded.HasLibraryNameConflict); Assert.False(loaded.CanSaveLibrary);
        Assert.Null(loaded.LibraryError); Assert.Equal(Strings.LibraryNameTaken, loaded.LibraryMessage);
        Assert.False(await loaded.SaveLibraryAsync()); Assert.Equal(1, library.SaveCalls); Assert.Same(entry, Assert.Single(library.Entries));
        Assert.True(loaded.Undo()); Assert.True(loaded.IsInLibrary); Assert.Null(loaded.LibraryMessage);
        Assert.True(loaded.Redo()); Assert.True(loaded.HasLibraryNameConflict);
        await loaded.DeletePresetAsync(entry);
        Assert.False(loaded.HasLibraryNameConflict); Assert.True(loaded.CanSaveLibrary); Assert.Null(loaded.LibraryMessage);
    }
    [Fact]
    public async Task PendingSavePreventsDuplicateWritesAndKeepsTheActionLabelStable()
    {
        var library = new Library { SaveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        string label = vm.LibraryActionLabel;
        var saving = vm.SaveLibraryAsync();
        Assert.True(vm.IsBusy); Assert.False(vm.CanSaveLibrary); Assert.Equal(label, vm.LibraryActionLabel);
        Assert.False(await vm.SaveLibraryAsync()); Assert.Equal(1, library.SaveCalls);
        library.SaveCompletion.SetResult(null); Assert.True(await saving);
        Assert.False(vm.IsBusy); Assert.True(vm.IsInLibrary); Assert.False(vm.CanSaveLibrary);
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
    [Fact]
    public void QuickAccessIncludesEveryMatchingPresetAndPreservesLibraryOrder()
    {
        var library = new Library();
        for (int i = 0; i < 7; i++)
        {
            library.Source.Add(new("Circle " + i, new CircleSectionGeometry(M(.04 + i * .01))));
            library.Source.Add(new("Rectangle " + i, new RectangleSectionGeometry(M(.1), M(.2))));
        }
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => "Circle " + i), vm.LibraryEntries.Select(e => e.Name));
        Assert.False(vm.IsLibraryEmpty);
        vm.SelectForm(SectionEditorType.ISection); Assert.Empty(vm.LibraryEntries); Assert.True(vm.IsLibraryEmpty);
        library.Error = "Cannot read library"; vm.SelectForm(SectionEditorType.ISection);
        Assert.False(vm.IsLibraryEmpty); Assert.Null(vm.LibraryError);
    }
    [Theory]
    [InlineData(false, null)] [InlineData(true, null)] [InlineData(false, "Delete failed")]
    public async Task DeleteOnlyChangesTheLibraryAndAllowsTheNameToBeReused(bool cancel, string? error)
    {
        var library = new Library { CancelDelete = cancel, DeleteError = error };
        var entry = new SectionLibraryEntry("Preset", new RectangleSectionGeometry(M(.12), M(.06)));
        library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library);
        vm.SelectForm(SectionEditorType.Circle); Fill(vm);
        vm.BeginNameEdit(); vm.PendingName = "Circle draft"; Assert.True(vm.CommitName());
        vm.SelectForm(SectionEditorType.Rectangle); vm.LoadPreset(entry); vm.SelectedAxis = SectionAxisDesignation.Z;
        var section = vm.Section; var preview = vm.Preview;
        var inputs = vm.Parameters.Select(p => (p.Key, p.PendingText, p.ConfirmedValue, p.HasError)).ToArray();
        bool undo = vm.CanUndo, redo = vm.CanRedo;

        await vm.DeletePresetAsync(entry);

        Assert.Equal(cancel || error is not null, library.Entries.Contains(entry));
        Assert.Equal(library.Entries, vm.LibraryEntries.Select(p => p.Entry));
        Assert.Equal(error, vm.LibraryError); Assert.False(vm.IsBusy);
        Assert.Same(section, vm.Section); Assert.Same(preview, vm.Preview);
        Assert.Equal(inputs, vm.Parameters.Select(p => (p.Key, p.PendingText, p.ConfirmedValue, p.HasError)));
        Assert.Equal(entry.Name, vm.DisplayName); Assert.Equal(SectionAxisDesignation.Z, vm.SelectedAxis);
        Assert.Equal(undo, vm.CanUndo); Assert.Equal(redo, vm.CanRedo); Assert.True(await vm.ConfirmAsync());
        if (!cancel && error is null)
        {
            Assert.True(await vm.SaveLibraryAsync()); Assert.Null(vm.LibraryError);
            Assert.Equal(entry.Name, Assert.Single(library.Entries).Name);
        }
        vm.SelectForm(SectionEditorType.Circle); Assert.Equal("Circle draft", vm.DisplayName); Assert.True(vm.HasValidSection);
    }
    [Fact]
    public async Task DeleteIgnoresStaleEntriesAndBlocksConcurrentDraftAndLibraryActions()
    {
        var library = new Library { DeleteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var entry = new SectionLibraryEntry("Preset", new CircleSectionGeometry(M(.04)));
        library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle); vm.LoadPreset(entry);
        await vm.DeletePresetAsync(new(entry.Name, entry.Section)); Assert.Equal(0, library.DeleteCalls);
        var deleting = vm.DeletePresetAsync(entry);
        Assert.True(vm.IsBusy); Assert.False(vm.CanUndo); Assert.False(vm.CanRedo); Assert.False(vm.CanConfirm);
        await vm.DeletePresetAsync(entry); Assert.False(await vm.SaveLibraryAsync());
        vm.SelectForm(SectionEditorType.Rectangle); Assert.Equal(SectionEditorType.Circle, vm.Type);
        Assert.Equal(1, library.DeleteCalls); Assert.Single(library.Entries);
        library.DeleteCompletion.SetResult(null); await deleting;
        Assert.False(vm.IsBusy); Assert.Empty(vm.LibraryEntries); Assert.True(vm.CanConfirm);
        await vm.DeletePresetAsync(entry); Assert.Equal(1, library.DeleteCalls);
    }
    [Theory]
    [InlineData(null)] [InlineData("Library unavailable")]
    public async Task UnavailableLibraryCannotDeleteAndUsesTheExistingError(string? error)
    {
        var library = new Library { CanSave = false, Error = error };
        var entry = new SectionLibraryEntry("Preset", new CircleSectionGeometry(M(.04))); library.Source.Add(entry);
        var vm = new SectionDefinitionViewModel(library: library); vm.SelectForm(SectionEditorType.Circle);
        await vm.DeletePresetAsync(entry);
        Assert.Equal(error ?? Strings.SectionLibraryUnavailable, vm.LibraryError);
        Assert.Equal(0, library.DeleteCalls); Assert.Single(vm.LibraryEntries); Assert.False(vm.IsBusy);
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
        Assert.Contains("203", InputQuantityFormatter.FormatExact(loaded.Height.Meters, UnitCatalog.Millimeter));
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
        Assert.Equal(!vm.RequiresAxisSelection, vm.CanConfirm); Assert.False(vm.CanSaveLibrary); Assert.True(vm.IsInLibrary);
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
        Assert.False(vm.CanSaveLibrary); Assert.True(vm.IsInLibrary); Assert.False(vm.HasLibraryError);
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
        vm.SelectForm(type); Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        var input = vm.Parameters[0]; input.PendingText = "100";
        Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        if (vm.Parameters.Count > 1)
        { input.Commit(); Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary); }
        Fill(vm); Assert.True(vm.ShowLibraryAction); Assert.True(vm.CanSaveLibrary); Assert.Null(vm.SelectedAxis);
        Assert.Equal(!vm.RequiresAxisSelection, vm.CanConfirm);
        var valid = input.PendingText;
        input.PendingText = "invalid"; Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
        input.Commit(); Assert.True(vm.ShowLibraryAction); Assert.False(vm.CanSaveLibrary);
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
        vm.ResetNameCommand.Execute(null); Assert.False(vm.CanSaveLibrary); Assert.True(vm.CanConfirm); Assert.Null(vm.LibraryError);
        Assert.True(vm.IsInLibrary); Assert.False(await vm.SaveLibraryAsync()); Assert.Null(vm.LibraryError);
    }
}
