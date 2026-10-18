using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class MaterialWorkflowRegressionTests
{
    private sealed class Fixture(bool brokenCatalog = false, bool store = true)
    {
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        private MainWindowViewModel? _main;
        public MainWindowViewModel Main
        {
            get
            {
                if (_main is not null) return _main;
                _main = new(files: Files, dialogs: Dialogs,
                    materialStore: store ? new(Files, "materials") : null,
                    catalogLoader: brokenCatalog ? () => throw new LibraryFormatException("Injected") : null);
                _main.NewCommand.Execute(null);
                return _main;
            }
        }
        public async Task Load(params MaterialLibraryEntry[] entries)
        {
            Files.Data["materials"] = MaterialLibraryCodec.Serialize(new(entries));
            await Main.InitializeLibrariesAsync();
        }
        public MaterialSelectionViewModel Select()
        { Main.Setup.ShowMaterials(); return Assert.IsType<MaterialSelectionViewModel>(Main.Setup.CurrentStep); }
        public MaterialEditorViewModel New()
        {
            Select().NewCommand.Execute(null);
            var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(Main.Setup.CurrentStep).Editor);
            editor.Name = "Shop material"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355";
            return editor;
        }
    }
    private static MaterialLibraryEntry Entry(string name = "Shop stainless", MaterialCategory category = MaterialCategory.StainlessSteel) =>
        new(new(name, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300)), category, "1.4567", ["UNS Example", "AISI Shop"]);

    [Theory]
    [InlineData("shop STAINLESS")] [InlineData("1.4567")] [InlineData("uns example")] [InlineData("  AISI SHOP  ")]
    public async Task SearchMatchesNameNumberAndStandardsIgnoringCase(string query)
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select();
        selection.SearchText = query;
        var row = Assert.Single(selection.FilteredEntries); Assert.True(row.IsUser); Assert.Equal("Shop stainless", row.Name);
        Assert.Equal("1.4567", row.MaterialNumber); Assert.Equal("UNS Example · AISI Shop", row.OtherStandards);
    }

    [Fact]
    public async Task CategoriesSearchAndMineFilterCombineWithoutMergingSources()
    {
        var f = new Fixture(); await f.Load(Entry(), Entry("Shop structural", MaterialCategory.StructuralSteel));
        var selection = f.Select(); Assert.Equal(7, selection.Filters.Count);
        Assert.Equal(selection.BuiltInEntries.Count + 2, selection.FilteredEntries.Count);
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StainlessSteel);
        selection.SearchText = "shop"; Assert.Equal("Shop stainless", Assert.Single(selection.FilteredEntries).Name);
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StructuralSteel);
        Assert.Equal("Shop structural", Assert.Single(selection.FilteredEntries).Name);
        selection.SelectedFilter = selection.Filters.Single(c => c.Kind == MaterialFilterKind.User);
        Assert.Equal(2, selection.FilteredEntries.Count); Assert.All(selection.FilteredEntries, e => Assert.True(e.IsUser));
        selection.SearchText = "nothing"; Assert.Empty(selection.FilteredEntries); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        selection.SelectedFilter = new(MaterialFilterKind.All, "Invalid filter"); Assert.Equal(MaterialFilterKind.User, selection.SelectedFilter.Kind);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task SnapshotSelectionUsesAUniqueFullMatchOrAPinnedMetadataFreeProjectRow(int match)
    {
        var f = new Fixture(); var builtIn = f.Main.BuiltInMaterials!.Find("S235JR")!;
        await f.Load(match == 2 ? [builtIn] : []);
        var material = match switch
        {
            0 => new Material("Independent", builtIn.Material.YoungsModulus, builtIn.Material.YieldStrength),
            3 => new Material(builtIn.Material.Name, builtIn.Material.YoungsModulus, builtIn.Material.YieldStrength),
            _ => new Material(builtIn.Material.Name, builtIn.Material.YoungsModulus, builtIn.Material.YieldStrength, builtIn.Material.Density, builtIn.Material.PoissonRatio)
        };
        f.Main.Setup.SelectedMaterial = material; var selection = f.Select();
        Assert.Same(material, f.Main.Setup.SelectedMaterial);
        if (match == 1)
        {
            Assert.Null(selection.CurrentEntry); Assert.True(selection.SelectedEntry!.IsBuiltIn);
            Assert.Same(builtIn, selection.SelectedEntry.Entry);
        }
        else
        {
            var current = selection.CurrentEntry!; Assert.Same(current, selection.SelectedEntry); Assert.Same(material, current.Material);
            Assert.Null(current.Entry); Assert.Null(current.MaterialNumber); Assert.False(current.HasCategory); Assert.False(current.HasOtherStandards);
            Assert.False(selection.ShowDelete); Assert.False(selection.CanDeleteSelected);
            selection.SearchText = "no library match"; selection.SelectedFilter = selection.Filters.Single(c => c.Kind == MaterialFilterKind.User);
            Assert.Empty(selection.FilteredEntries); Assert.Same(current, Assert.Single(selection.VisibleEntries)); Assert.Same(current, selection.SelectedEntry);
            selection.UseSelectedCommand.Execute(null); Assert.Same(material, f.Main.Setup.SelectedMaterial); Assert.True(f.Main.Setup.IsOverview);
        }
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public async Task BeamCaptionDependsOnlyOnSnapshotWithAnyCatalogAndUserLibrary(bool brokenCatalog, int user)
    {
        var f = new Fixture(brokenCatalog); var snapshot = new Material("S235JR", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235));
        await f.Load(user == 0 ? [] : [new(snapshot, MaterialCategory.Other, user == 1 ? "1.0038" : "Different", ["Alias"])]);
        f.Main.Setup.SelectedMaterial = snapshot;
        Assert.Equal("S235JR", f.Main.Setup.MaterialName); Assert.Contains("E ", f.Main.Setup.MaterialSummary); Assert.Contains("fy ", f.Main.Setup.MaterialSummary);
        var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.FirstOrDefault() ?? selection.CurrentEntry;
        if (selection.CanUseSelected) selection.UseSelected();
        Assert.Equal("S235JR", f.Main.Setup.MaterialName);
    }

    [Fact]
    public async Task ReopeningSelectionUsesTheMaterialAlreadyAppliedToTheSetupDraft()
    {
        var f = new Fixture(); await f.Load(); CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync());
        var committed = f.Main.Editor!.Document.Material; f.Main.EditProject(); var editor = f.New();
        Assert.True(await editor.ApplyAsync()); var changed = f.Main.Setup.SelectedMaterial;
        var selection = f.Select(); Assert.Same(changed, selection.CurrentEntry!.Material); Assert.Same(selection.CurrentEntry, selection.SelectedEntry);
        f.Main.Setup.ShowOverview(); f.Main.Setup.CancelCommand.Execute(null); Assert.Same(committed, f.Main.Editor.Document.Material);
    }

    [Fact]
    public async Task ApplyAndExplicitAddAreIndependentAndLibrarySaveSurvivesEditorCancel()
    {
        var f = new Fixture(); await f.Load(); var selected = f.Main.Setup.SelectedMaterial; var editor = f.New();
        editor.MaterialNumber = "1.2345"; editor.OtherStandards = " A ; B ; ";
        Assert.True(await editor.AddLibraryAsync()); Assert.Same(editor, Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep).Editor); Assert.Same(selected, f.Main.Setup.SelectedMaterial);
        Assert.True(editor.IsInLibrary); Assert.False(editor.AddLibraryCommand.CanExecute(null));
        var entry = Assert.Single(f.Main.UserMaterials.All); Assert.Equal(new[] { "A", "B" }, entry.OtherStandards);
        editor.CancelCommand.Execute(null); Assert.Same(selected, f.Main.Setup.SelectedMaterial); Assert.Single(f.Main.UserMaterials.All);
        editor = f.New(); editor.Name = "Second"; Assert.True(await editor.AddLibraryAsync());
        Assert.True(await editor.ApplyAsync()); Assert.Equal("Second", f.Main.Setup.SelectedMaterial!.Name);
        Assert.Equal(2, f.Main.UserMaterials.All.Count); Assert.Equal(2, f.Files.Writes.Count);
    }

    [Theory]
    [InlineData("name")] [InlineData("E")] [InlineData("fy")] [InlineData("rho")] [InlineData("nu")]
    [InlineData("category")] [InlineData("number")] [InlineData("standards")]
    public async Task SavedStateComparesAllMaterialValuesAndLibraryMetadata(string changed)
    {
        using var scope = new UiCultureScope("en-US");
        var f = new Fixture(); await f.Load(); var editor = f.New(); Assert.True(await editor.AddLibraryAsync());
        var saved = f.Main.UserMaterials.All.Single(); Assert.True(editor.IsInLibrary);
        switch (changed)
        {
            case "name": editor.Name = "Renamed"; break;
            case "E": editor.Inputs[0].Text = "220"; break;
            case "fy": editor.Inputs[1].Text = "400"; break;
            case "rho": editor.Inputs[2].Text = "7800"; break;
            case "nu": editor.Inputs[3].Text = "0.25"; break;
            case "category": editor.Category = editor.Categories[0]; break;
            case "number": editor.MaterialNumber = "1.2345"; break;
            default: editor.OtherStandards = "Another standard"; break;
        }
        Assert.False(editor.IsInLibrary); Assert.Equal(Strings.SectionDefinitionAddLibrary, editor.LibraryActionLabel);
        Assert.Same(saved, f.Main.UserMaterials.All.Single()); Assert.Single(f.Files.Writes); Assert.True(editor.CanApply);
        Assert.Equal(changed == "name", editor.CanAddLibrary); // Adding must never replace the saved name.
        if (changed != "name") Assert.Equal(Strings.LibraryNameConflict, editor.LibraryError);
    }

    [Fact]
    public async Task NormalizedUnchangedValuesRestoreSavedStateWithoutWritingAgain()
    {
        var f = new Fixture(); await f.Load(); var editor = f.New(); editor.OtherStandards = "A; B";
        Assert.True(await editor.AddLibraryAsync()); editor.MaterialNumber = "1.2345"; Assert.False(editor.IsInLibrary);
        editor.MaterialNumber = " "; editor.Name = " Shop material "; editor.OtherStandards = " A ; ; B ; ";
        Assert.True(editor.IsInLibrary); Assert.Single(f.Files.Writes);
    }

    [Fact]
    public async Task SavingAProjectRowThenReturningSelectsItsNewUniqueLibraryMatch()
    {
        var f = new Fixture(); await f.Load();
        f.Main.Setup.SelectedMaterial = Entry().Material;
        var selection = f.Select(); Assert.True(selection.SelectedEntry!.IsProject);
        selection.EditSelectedCommand.Execute(null); var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep).Editor);
        Assert.Empty(editor.MaterialNumber); Assert.Empty(editor.OtherStandards);
        Assert.True(await editor.AddLibraryAsync()); editor.CancelCommand.Execute(null);
        Assert.Same(selection, f.Main.Setup.CurrentStep); Assert.Null(selection.CurrentEntry);
        Assert.True(selection.SelectedEntry!.IsUser); Assert.Same(f.Main.UserMaterials.All.Single(), selection.SelectedEntry.Entry);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrDamagedLibraryDoesNotPreventProjectApply(bool damaged)
    {
        var f = new Fixture(store: damaged);
        if (damaged) { f.Files.Data["materials"] = [123]; await f.Main.InitializeLibrariesAsync(); }
        var editor = f.New(); Assert.False(editor.CanAddLibrary); Assert.False(await editor.AddLibraryAsync());
        Assert.True(await editor.ApplyAsync()); Assert.Equal("Shop material", f.Main.Setup.SelectedMaterial!.Name); Assert.Empty(f.Files.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReplaceKeepsProjectIndependentAndRestoresSelectionFilters(bool cancel)
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select();
        selection.SelectedFilter = selection.Filters.Single(c => c.Kind == MaterialFilterKind.User); selection.SearchText = "shop";
        selection.SelectedEntry = Assert.Single(selection.FilteredEntries); selection.EditSelectedCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep).Editor); var project = f.Main.Setup.SelectedMaterial;
        editor.Name = "Shop renamed"; editor.Inputs[1].Text = "400";
        if (cancel) editor.CancelCommand.Execute(null); else Assert.True(await editor.ReplaceLibraryAsync());
        Assert.Same(selection, f.Main.Setup.CurrentStep); Assert.Same(project, f.Main.Setup.SelectedMaterial);
        Assert.Equal("shop", selection.SearchText); Assert.Equal(MaterialFilterKind.User, selection.SelectedFilter.Kind);
        Assert.Equal(cancel ? "Shop stainless" : "Shop renamed", selection.SelectedEntry!.Name);
        Assert.Equal(cancel ? 300e6 : 400e6, selection.SelectedEntry.Material.YieldStrength.Pascals);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task ReplacementCorrectsOnlyFiltersThatHideTheRenamedOrRecategorizedEntry(bool rename, bool recategorize)
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select();
        selection.SearchText = "shop stainless";
        var filter = selection.Filters.Single(c => c.Category == MaterialCategory.StainlessSteel);
        selection.SelectedFilter = filter; selection.SelectedEntry = Assert.Single(selection.FilteredEntries);
        selection.EditSelectedCommand.Execute(null); var editor = Assert.IsType<MaterialEditorViewModel>(selection.Editor);
        var snapshot = f.Main.Setup.SelectedMaterial;
        editor.Name = rename ? "Renamed" : "Shop stainless";
        if (recategorize) editor.Category = editor.Categories.Single(c => c.Value == MaterialCategory.Other);
        Assert.True(await editor.ReplaceLibraryAsync());
        Assert.Same(selection, f.Main.Setup.CurrentStep); Assert.False(selection.IsEditing);
        Assert.Equal(rename ? "" : "shop stainless", selection.SearchText);
        Assert.Equal(recategorize ? MaterialFilterKind.All : MaterialFilterKind.Category, selection.SelectedFilter.Kind);
        Assert.Same(Assert.Single(selection.UserEntries), selection.SelectedEntry);
        Assert.Contains(selection.SelectedEntry, selection.FilteredEntries); Assert.Same(snapshot, f.Main.Setup.SelectedMaterial);
    }

    [Fact]
    public async Task EmbeddedDraftFreezesBrowsingAndDiscardPreservesTheExactBrowseState()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select();
        selection.SearchText = "shop"; selection.SelectedFilter = selection.UserFilter;
        var entry = selection.SelectedEntry = Assert.Single(selection.FilteredEntries);
        var rows = selection.FilteredEntries;
        selection.EditSelectedCommand.Execute(null);
        Assert.Same(selection, f.Main.Setup.CurrentStep); Assert.Equal(ProjectSetupPage.MaterialSelection, f.Main.Setup.Page);
        Assert.False(selection.CanBrowse); Assert.False(selection.NewCommand.CanExecute(null));
        Assert.False(selection.UseSelectedCommand.CanExecute(null)); Assert.False(selection.BackCommand.CanExecute(null));
        Assert.False(selection.ShowUserCommand.CanExecute(null));
        selection.SearchText = "ignored"; selection.SelectedFilter = selection.Filters[0];
        Assert.Equal("shop", selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter);
        selection.Editor!.Name = "Discarded"; f.Main.Setup.Escape();
        Assert.False(selection.IsEditing); Assert.Same(entry, selection.SelectedEntry); Assert.Same(rows, selection.FilteredEntries);
        Assert.Equal("Shop stainless", Assert.Single(f.Main.UserMaterials.All).Material.Name); Assert.Empty(f.Files.Writes);
        f.Main.Setup.Escape(); Assert.True(f.Main.Setup.IsOverview);
    }

    [Fact]
    public async Task DeleteCancellationAndFailedWritesRetainSelectionAndBytes()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single();
        var selected = selection.SelectedEntry; var bytes = f.Files.Data["materials"].ToArray();
        f.Dialogs.ConfirmLibraryDelete = false; await selection.DeleteSelectedAsync();
        Assert.Same(selected, selection.SelectedEntry); Assert.Equal(bytes, f.Files.Data["materials"]);
        f.Dialogs.ConfirmLibraryDelete = true; f.Files.FailWritePath = "materials";
        Assert.False(await selection.DeleteSelectedAsync()); Assert.Same(selected, selection.SelectedEntry); Assert.Equal(bytes, f.Files.Data["materials"]);
        f.Files.FailWritePath = null; Assert.True(await selection.DeleteSelectedAsync()); Assert.Empty(f.Main.UserMaterials.All);
        Assert.True(selection.SelectedEntry is null || selection.VisibleEntries.Contains(selection.SelectedEntry));
    }

    [Fact]
    public async Task DuplicateLibraryRenameBlocksReplaceWithoutChangingEitherEntry()
    {
        var f = new Fixture(); await f.Load(Entry("First"), Entry("Second")); var selection = f.Select();
        selection.SelectedEntry = selection.UserEntries[0]; selection.EditSelectedCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep).Editor);
        var library = f.Main.UserMaterials; var bytes = f.Files.Data["materials"].ToArray();
        editor.Name = "SECOND"; Assert.NotNull(editor.Draft); Assert.False(editor.CanReplaceLibrary);
        Assert.Equal(Strings.LibraryNameConflict, editor.LibraryError); Assert.False(await editor.ReplaceLibraryAsync());
        Assert.Same(library, f.Main.UserMaterials); Assert.Equal(bytes, f.Files.Data["materials"]); Assert.Empty(f.Files.Writes);
        Assert.False(editor.CanApply); editor.CancelCommand.Execute(null); Assert.Same(selection, f.Main.Setup.CurrentStep);
    }

    [Fact]
    public async Task SelectionAndEditorPersistOnlyCoreMaterialSnapshotFields()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single(); selection.UseSelected();
        CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync()); var before = f.Main.Session!.CurrentRevision.State;
        var bytes = ProjectFileCodec.Serialize(before); var material = JsonNode.Parse(bytes)!["document"]!["material"]!.AsObject();
        Assert.Equal(new[] { "name", "youngsModulus", "yieldStrength" }, material.Select(p => p.Key));
        foreach (var key in new[] { "materialNumber", "category", "otherStandards", "libraryId", "provenance" }) Assert.DoesNotContain("\"" + key + "\"", Encoding.UTF8.GetString(bytes));
        Assert.True(before.ContentEquals(ProjectFileCodec.Deserialize(bytes)));
        var recovery = new ProjectRecovery(f.Files, TestPath("material-workflow-recovery.json")); await recovery.FlushAsync(before, null);
        Assert.True(before.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task NewDraftTitleDoesNotExposePreservedSessionAndEditKeepsFileTitle(string culture)
    {
        using var scope = new UiCultureScope(culture); var f = new Fixture(); await f.Load();
        string newTitle = Strings.NewProject + " — " + Strings.ApplicationTitle; Assert.Equal(newTitle, f.Main.WindowTitle);
        CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync()); f.Dialogs.SavePath = TestPath("Maschine.spandraft"); Assert.True(await f.Main.SaveAsync());
        var session = f.Main.Session!; var revision = session.CurrentRevision; var title = f.Main.WindowTitle;
        Assert.True(await f.Main.NewAsync()); Assert.Equal(newTitle, f.Main.WindowTitle); Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision); Assert.False(session.IsDirty);
        f.Main.Setup.CancelCommand.Execute(null); Assert.Equal(title, f.Main.WindowTitle);
        f.Main.EditProject(); Assert.Equal(title, f.Main.WindowTitle); f.Main.Setup.CancelCommand.Execute(null);
        Assert.True(await f.Main.NewAsync()); CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.NotSame(session, f.Main.Session); Assert.Null(f.Main.Session!.FilePath); Assert.True(f.Main.Session.IsDirty);
        Assert.Equal(Strings.Untitled + "* — " + Strings.ApplicationTitle, f.Main.WindowTitle);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task InitialExplicitSectionNameBelongsOnlyToItsFormAndResultUsesActiveAxis(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var initial = new ISectionGeometry(M(.2), M(.1), M(.005), M(.01), M(0));
        var vm = new SectionDefinitionViewModel(initialState: new(initial, SectionAxisDesignation.Z, "HEB 200"));
        var inputs = vm.Parameters.ToArray(); Assert.Equal("HEB 200", vm.DisplayName);
        vm.SelectForm(SectionEditorType.Circle); Assert.False(vm.HasCustomName); Assert.NotEqual("HEB 200", vm.DisplayName);
        vm.SelectForm(SectionEditorType.ISection); Assert.Equal("HEB 200", vm.DisplayName); Assert.Equal(inputs, vm.Parameters); Assert.Equal(SectionAxisDesignation.Z, vm.ResultAxis);
        SpanDraft.Desktop.Sections.SectionDefinitionResult? result = null; vm.CloseRequested += r => result = r;
        Assert.True(await vm.ConfirmAsync()); Assert.Equal("HEB 200", result!.DisplayName); Assert.False(result.IsAutomaticName);
        Assert.Equal(SectionAxisDesignation.Z, result.BendingAxis); Assert.IsType<ISectionGeometry>(result.Section);
    }
}
