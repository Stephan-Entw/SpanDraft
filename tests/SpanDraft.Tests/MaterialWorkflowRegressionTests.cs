using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class MaterialWorkflowRegressionTests
{
    private sealed class Fixture
    {
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public MainWindowViewModel Main { get; }
        public int Analyses { get; private set; }
        public Fixture(bool brokenCatalog = false, bool store = true)
        {
            Main = new(b => { Analyses++; return BeamAnalysis.Analyze(b); }, files: Files, dialogs: Dialogs,
                materialStore: store ? new(Files, "materials") : null,
                catalogLoader: brokenCatalog ? () => throw new LibraryFormatException("Injected") : null);
            Main.NewCommand.Execute(null);
        }
        public async Task Load(params Material[] entries)
        {
            Files.Data["materials"] = MaterialLibraryCodec.Serialize(new(entries));
            await Main.InitializeLibrariesAsync();
        }
        public MaterialSelectionViewModel Select()
        { Main.Setup.ShowMaterials(); return Assert.IsType<MaterialSelectionViewModel>(Main.Setup.CurrentStep); }
        public MaterialEditorViewModel New()
        {
            Select().NewCommand.Execute(null);
            var editor = Assert.IsType<MaterialSelectionViewModel>(Main.Setup.CurrentStep).Editor!;
            Fill(editor); return editor;
        }
    }
    internal static Material Entry(string name = "Shop stainless", MaterialCategory category = MaterialCategory.StainlessSteel) =>
        new(name, Pressure.FromPascals(200e9), Pressure.FromMegapascals(300), MaterialStrengthType.Re,
            MassDensity.FromKilogramsPerCubicMeter(7800), PoissonRatio.FromValue(.3), category, "1.4567", ["UNS Example", "AISI Shop"]);
    private static void Fill(MaterialEditorViewModel editor)
    { editor.Name = "Shop material"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "355"; editor.StrengthType = editor.StrengthTypes[0]; }

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
    public async Task EveryFilterIsExclusiveAndCategoriesIncludeBothLibrarySources()
    {
        var f = new Fixture(); await f.Load(Entry(), Entry("Shop structural", MaterialCategory.StructuralSteel));
        f.Main.Setup.SelectedMaterial = Entry("Project"); var selection = f.Select();
        Assert.Equal(9, selection.Filters.Count);
        Assert.Equal(selection.BuiltInEntries.Count + 3, selection.FilteredEntries.Count);
        Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]);
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StainlessSteel);
        Assert.Contains(selection.FilteredEntries, e => e.IsBuiltIn); Assert.Contains(selection.FilteredEntries, e => e.IsUser);
        selection.SearchText = "shop";
        Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]);
        Assert.Equal("Shop stainless", Assert.Single(selection.FilteredEntries, e => e.IsUser).Name);
        selection.SelectedFilter = selection.BuiltInFilter;
        Assert.Empty(selection.FilteredEntries); selection.SearchText = "";
        Assert.All(selection.FilteredEntries, e => Assert.True(e.IsBuiltIn)); Assert.True(selection.IsBuiltInFilter);
        selection.SelectedFilter = selection.UserFilter;
        Assert.Equal(2, selection.FilteredEntries.Count); Assert.All(selection.FilteredEntries, e => Assert.True(e.IsUser));
        Assert.False(selection.IsBuiltInFilter); Assert.True(selection.IsUserFilter);
        selection.SearchText = "nothing"; Assert.Empty(selection.FilteredEntries);
        Assert.True(selection.IsEmpty); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        selection.SelectedFilter = new(MaterialFilterKind.All, "Invalid filter"); Assert.Same(selection.UserFilter, selection.SelectedFilter);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProjectRowRemainsIndependentEvenWithIdenticalBuiltInAndUserEntries(bool userMatch)
    {
        var f = new Fixture(); var material = f.Main.BuiltInMaterials!.Find("S235JR")!;
        await f.Load(userMatch ? [material] : []); f.Main.Setup.SelectedMaterial = material;
        var selection = f.Select(); var current = selection.CurrentEntry!;
        Assert.Same(current, selection.SelectedEntry); Assert.True(current.IsProject);
        Assert.False(current.IsBuiltIn);
        Assert.Same(current, selection.FilteredEntries[0]);
        Assert.Equal(userMatch ? 3 : 2, selection.FilteredEntries.Count(e => e.Name == material.Name));
        Assert.Contains(selection.FilteredEntries, e => e.IsBuiltIn && ReferenceEquals(e.Material, material));
        if (userMatch) Assert.Contains(selection.FilteredEntries, e => e.IsUser);
        Assert.Equal(material.MaterialNumber, current.MaterialNumber); Assert.True(current.HasCategory);
        Assert.False(selection.ShowSave); Assert.False(selection.ShowDelete);
        selection.SearchText = "no match"; selection.SelectedFilter = selection.UserFilter;
        Assert.Empty(selection.FilteredEntries); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        selection.SearchText = ""; selection.SelectedFilter = selection.ProjectFilter;
        Assert.Same(current, Assert.Single(selection.FilteredEntries)); selection.SelectedEntry = current;
        selection.UseSelected(); Assert.Same(material, f.Main.Setup.SelectedMaterial); Assert.True(f.Main.Setup.IsOverview);
    }

    [Theory]
    [InlineData(" project only ")] [InlineData("1.4567")] [InlineData("uns EXAMPLE")]
    public async Task ProjectSnapshotParticipatesInSearchAndCategoryFiltersUsingItsOwnData(string query)
    {
        var f = new Fixture(); await f.Load(Entry("Library", MaterialCategory.StructuralSteel));
        f.Main.Setup.SelectedMaterial = Entry("Project only", MaterialCategory.Other); var selection = f.Select();
        selection.SearchText = query; Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]);
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.Other);
        Assert.Same(selection.CurrentEntry, Assert.Single(selection.FilteredEntries));
        selection.SelectedEntry = selection.CurrentEntry;
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StructuralSteel);
        Assert.DoesNotContain(selection.CurrentEntry, selection.FilteredEntries); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        selection.SelectedFilter = selection.ProjectFilter; Assert.Same(selection.CurrentEntry, Assert.Single(selection.FilteredEntries));
        selection.SearchText = "not a material"; Assert.Empty(selection.FilteredEntries); Assert.True(selection.IsEmpty);
        selection.SearchText = "  "; Assert.Same(selection.CurrentEntry, Assert.Single(selection.FilteredEntries));
        selection.SearchText = Strings.MaterialProjectSuffix; Assert.Empty(selection.FilteredEntries);
    }

    [Fact]
    public async Task SourceFiltersStayExclusiveEvenWhenAllSourcesHaveIdenticalMaterialData()
    {
        var f = new Fixture(); var material = f.Main.BuiltInMaterials!.All[0]; await f.Load(material);
        f.Main.Setup.SelectedMaterial = material; var selection = f.Select();
        selection.SearchText = material.Name;
        selection.ShowBuiltInCommand.Execute(null);
        Assert.All(selection.FilteredEntries, e => Assert.True(e.IsBuiltIn)); Assert.False(selection.IsProjectFilter); Assert.False(selection.IsUserFilter);
        Assert.Null(selection.SelectedEntry); Assert.False(selection.HasSelection);
        selection.ShowUserCommand.Execute(null);
        Assert.True(Assert.Single(selection.FilteredEntries).IsUser); Assert.False(selection.IsBuiltInFilter); Assert.False(selection.IsProjectFilter);
        selection.ShowProjectCommand.Execute(null);
        Assert.Same(selection.CurrentEntry, Assert.Single(selection.FilteredEntries)); Assert.True(selection.IsProjectFilter);
        Assert.False(selection.IsUserFilter); Assert.False(selection.IsBuiltInFilter);
        selection.SelectedFilter = selection.Filters[0];
        Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]); Assert.Equal(3, selection.FilteredEntries.Count);
        selection.SelectedEntry = selection.CurrentEntry; Assert.False(selection.ShowSave);
    }

    [Fact]
    public async Task ProjectSourceWithoutSnapshotIsEmptyAndDoesNotSelectATemplate()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); Assert.Null(selection.CurrentEntry);
        selection.ShowProjectCommand.Execute(null);
        Assert.True(selection.IsEmpty); Assert.Empty(selection.FilteredEntries); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        selection.SelectedFilter = selection.Filters[0]; Assert.Equal(selection.BuiltInEntries.Count + 1, selection.FilteredEntries.Count);
        Assert.DoesNotContain(selection.FilteredEntries, e => e.IsProject);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ProjectEditingCorrectsOnlyFiltersThatWouldHideTheConfirmedSnapshot(bool rename, bool recategorize)
    {
        var f = new Fixture(); await f.Load(); f.Main.Setup.SelectedMaterial = Entry(); var original = f.Main.Setup.SelectedMaterial;
        var selection = f.Select(); selection.SearchText = "Shop stainless";
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StainlessSteel);
        var filter = selection.SelectedFilter; selection.EditSelectedCommand.Execute(null); var editor = selection.Editor!;
        if (rename) editor.Name = "Renamed project";
        if (recategorize) editor.Category = editor.Categories.Single(c => c.Value == MaterialCategory.Other);
        Assert.True(await editor.ConfirmAsync());
        Assert.Same(selection.CurrentEntry, selection.SelectedEntry); Assert.Same(selection.CurrentEntry, selection.FilteredEntries[0]);
        Assert.Equal(rename ? "" : "Shop stainless", selection.SearchText);
        Assert.Same(recategorize ? selection.Filters[0] : filter, selection.SelectedFilter);
        Assert.Same(original, f.Main.Setup.SelectedMaterial); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task DeletingOwnMaterialsNeverSelectsAProjectHiddenByTheSourceFilter()
    {
        var f = new Fixture(); await f.Load(Entry("First"), Entry("Second")); f.Main.Setup.SelectedMaterial = Entry("Project");
        f.Dialogs.ConfirmLibraryDelete = true; var selection = f.Select(); selection.SelectedFilter = selection.UserFilter;
        selection.SelectedEntry = selection.FilteredEntries[0]; Assert.True(await selection.DeleteSelectedAsync());
        Assert.Equal("First (1.4567) · UNS Example · AISI Shop", f.Dialogs.MaterialDeleteDisplayName);
        Assert.Null(f.Main.UserMaterials.Find("First")); Assert.NotNull(f.Main.UserMaterials.Find("Second"));
        Assert.Equal("Second", selection.SelectedEntry!.Name); Assert.Contains(selection.SelectedEntry, selection.FilteredEntries);
        Assert.True(await selection.DeleteSelectedAsync()); Assert.True(selection.IsEmpty); Assert.Null(selection.SelectedEntry); Assert.False(selection.CanUseSelected);
        Assert.NotNull(selection.CurrentEntry); selection.SelectedFilter = selection.ProjectFilter;
        Assert.Same(selection.CurrentEntry, Assert.Single(selection.FilteredEntries));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProjectEditorOnlyChangesLocalSnapshotUntilBrowserApplyAndCanBeDiscarded(bool apply)
    {
        var f = new Fixture(); await f.Load(); CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync());
        var session = f.Main.Session!; var revision = session.CurrentRevision; int analyses = f.Analyses;
        f.Main.EditProject(); var original = f.Main.Setup.SelectedMaterial;
        var selection = f.Select(); selection.EditSelectedCommand.Execute(null); var editor = selection.Editor!;
        Assert.Equal(original!.Category, editor.Category!.Value); Assert.Equal(original.MaterialNumber, editor.MaterialNumber);
        editor.MaterialNumber = "New number"; editor.OtherStandards = " New A ; ; New B ; "; editor.Name = "Custom project";
        Assert.True(await editor.ConfirmAsync()); Assert.False(selection.IsEditing);
        Assert.Equal("Custom project", selection.CurrentEntry!.Name); Assert.Same(selection.CurrentEntry, selection.SelectedEntry);
        Assert.Same(original, f.Main.Setup.SelectedMaterial); Assert.Same(revision, session.CurrentRevision); Assert.Empty(f.Files.Writes);
        if (apply)
        {
            selection.UseSelected(); Assert.Equal("Custom project", f.Main.Setup.SelectedMaterial!.Name);
            Assert.True(await f.Main.Setup.ConfirmAsync()); Assert.NotSame(revision, session.CurrentRevision);
            Assert.Equal("New number", f.Main.Editor!.Document.Material.MaterialNumber); Assert.Equal(analyses, f.Analyses);
            Assert.True(f.Main.Undo()); Assert.Equal(original.MaterialNumber, f.Main.Editor.Document.Material.MaterialNumber);
            Assert.True(f.Main.Redo()); Assert.Equal("New number", f.Main.Editor.Document.Material.MaterialNumber);
        }
        else
        {
            selection.BackCommand.Execute(null); Assert.Same(original, f.Main.Setup.SelectedMaterial);
            Assert.Same(original, f.Select().CurrentEntry!.Material);
            f.Main.Setup.ShowOverview(); f.Main.Setup.CancelCommand.Execute(null); Assert.Same(revision, session.CurrentRevision);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NewAndBuiltInCopySaveToLibraryStaySelectedAndNeverApplyProject(bool copy)
    {
        var f = new Fixture(); await f.Load(); var selection = f.Select(); Material? source = null;
        if (copy) { selection.SelectedEntry = selection.BuiltInEntries[0]; source = selection.SelectedEntry.Material; selection.EditSelectedCommand.Execute(null); }
        else selection.NewCommand.Execute(null);
        var editor = selection.Editor!;
        if (copy) { Assert.Equal(source!.Name, editor.Name); Assert.Equal(source.MaterialNumber, editor.MaterialNumber); }
        else { Assert.Empty(editor.Name); Assert.All(editor.Inputs, i => Assert.Empty(i.Text)); Assert.Null(editor.StrengthType); Assert.Equal(MaterialCategory.Other, editor.Category!.Value); Fill(editor); }
        Assert.True(await editor.ConfirmAsync()); Assert.Null(f.Main.Setup.SelectedMaterial); Assert.Null(f.Main.Session);
        var saved = Assert.Single(f.Main.UserMaterials.All); Assert.Same(saved, selection.SelectedEntry!.Material);
        Assert.Same(selection.UserFilter, selection.SelectedFilter);
        if (copy) { Assert.Equal(source!.YoungsModulus, saved.YoungsModulus); Assert.Same(source, selection.BuiltInEntries[0].Material); }
        selection.BackCommand.Execute(null); Assert.Single(f.Main.UserMaterials.All);
        selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single(); selection.UseSelected(); Assert.Same(saved, f.Main.Setup.SelectedMaterial);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UserEditPersistsDirectlyAndOnlyCorrectsFiltersThatHideTheResult(bool hide)
    {
        var f = new Fixture(); await f.Load(Entry()); f.Main.Setup.SelectedMaterial = Entry("Project"); var project = f.Main.Setup.SelectedMaterial;
        var selection = f.Select(); selection.SearchText = "shop stainless";
        selection.SelectedFilter = selection.Filters.Single(c => c.Category == MaterialCategory.StainlessSteel);
        var filter = selection.SelectedFilter; selection.SelectedEntry = selection.UserEntries.Single(); selection.EditSelectedCommand.Execute(null);
        var editor = selection.Editor!; editor.Inputs[1].Text = "400";
        if (hide) { editor.Name = "Renamed"; editor.Category = editor.Categories.Single(c => c.Value == MaterialCategory.Other); }
        Assert.True(await editor.ConfirmAsync()); Assert.Same(selection.UserEntries.Single(), selection.SelectedEntry);
        Assert.Equal(hide ? "" : "shop stainless", selection.SearchText); Assert.Equal(hide ? MaterialFilterKind.All : filter.Kind, selection.SelectedFilter.Kind);
        Assert.Same(project, f.Main.Setup.SelectedMaterial); selection.BackCommand.Execute(null);
        Assert.Equal(400e6, Assert.Single(MaterialLibraryCodec.Deserialize(f.Files.Data["materials"]).All).YieldStrength.Pascals);
    }

    [Fact]
    public async Task ModalCancelPreservesBrowseStateAndLibraryBytes()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); selection.SelectedFilter = selection.UserFilter; selection.SearchText = "shop";
        var selected = selection.SelectedEntry = selection.UserEntries.Single(); var rows = selection.FilteredEntries;
        selection.EditSelectedCommand.Execute(null); Assert.False(selection.CanBrowse);
        selection.SearchText = "ignored"; selection.SelectedFilter = selection.Filters[0];
        selection.Editor!.Name = "Discarded"; f.Main.Setup.Escape();
        Assert.False(selection.IsEditing); Assert.Same(selected, selection.SelectedEntry); Assert.Same(rows, selection.FilteredEntries);
        Assert.Equal("shop", selection.SearchText); Assert.Same(selection.UserFilter, selection.SelectedFilter); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task UserRenameConflictIsVisibleAtNameAndNeverReplacesAnotherEntry()
    {
        var f = new Fixture(); await f.Load(Entry("First"), Entry("Second")); var selection = f.Select();
        selection.SelectedEntry = selection.UserEntries[0]; selection.EditSelectedCommand.Execute(null); var editor = selection.Editor!;
        editor.Name = " SECOND "; editor.CompleteNameInput(); Assert.True(editor.ShowNameError); Assert.False(editor.CanConfirm);
        Assert.Equal(Strings.LibraryNameConflict, editor.NameError); Assert.False(await editor.ConfirmAsync()); Assert.Empty(f.Files.Writes);
        editor.CancelCommand.Execute(null); Assert.Equal("First", selection.SelectedEntry!.Name);
    }

    [Fact]
    public async Task FailedWritesKeepEditorInputsSelectionAndPublishedLibraryAndCanBeRetried()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single();
        var selected = selection.SelectedEntry; var before = f.Files.Data["materials"].ToArray();
        selection.EditSelectedCommand.Execute(null); var editor = selection.Editor!; editor.Name = "Renamed"; f.Files.FailWritePath = "materials";
        Assert.False(await editor.ConfirmAsync()); Assert.Same(editor, selection.Editor); Assert.Equal("Renamed", editor.Name); Assert.True(editor.HasFormError);
        Assert.Same(selected, selection.SelectedEntry); Assert.Equal(before, f.Files.Data["materials"]);
        f.Files.FailWritePath = null; Assert.True(await editor.ConfirmAsync()); Assert.Equal("Renamed", selection.SelectedEntry!.Name);
    }

    [Theory]
    [InlineData("name")] [InlineData("category")] [InlineData("number")] [InlineData("standards")] [InlineData("E")]
    [InlineData("strength")] [InlineData("strengthType")] [InlineData("rho")] [InlineData("nu")]
    public async Task DuplicateDetectionIncludesEveryMaterialField(string field)
    {
        var f = new Fixture(); var m = Entry(); await f.Load(m);
        f.Main.Setup.SelectedMaterial = new Material(field == "name" ? "Other" : m.Name,
            field == "E" ? Pressure.FromPascals(201e9) : m.YoungsModulus,
            field == "strength" ? Pressure.FromMegapascals(301) : m.YieldStrength,
            field == "strengthType" ? MaterialStrengthType.Rp02 : MaterialStrengthType.Re,
            field == "rho" ? null : m.Density, field == "nu" ? null : m.PoissonRatio,
            field == "category" ? MaterialCategory.Other : m.Category, field == "number" ? null : m.MaterialNumber,
            field == "standards" ? m.OtherStandards.Reverse() : m.OtherStandards);
        Assert.True(f.Select().ShowSave);
    }

    [Fact]
    public async Task DuplicateNormalizationTrimsButKeepsCaseAndStandardOrderSignificant()
    {
        var f = new Fixture(); var m = Entry(); await f.Load(m);
        f.Main.Setup.SelectedMaterial = new Material(" " + m.Name + " ", m.YoungsModulus, m.YieldStrength, MaterialStrengthType.Re,
            m.Density, m.PoissonRatio, m.Category, " " + m.MaterialNumber + " ", [" UNS Example ", "", " AISI Shop "]);
        Assert.False(f.Select().ShowSave);
        f.Main.Setup.ShowOverview(); f.Main.Setup.SelectedMaterial = Entry(m.Name.ToUpperInvariant()); Assert.True(f.Select().ShowSave);
    }

    [Fact]
    public async Task SavingProjectAddsCompleteSnapshotWithoutChangingProjectRowOrMergingIt()
    {
        var f = new Fixture(); await f.Load(); f.Main.Setup.SelectedMaterial = Entry(); var selection = f.Select(); var row = selection.CurrentEntry;
        Assert.True(await selection.SaveSelectedAsync()); Assert.Same(row, selection.CurrentEntry); Assert.Same(row, selection.SelectedEntry);
        Assert.False(selection.ShowSave); Assert.True(MaterialSnapshotComparison.Equals(row!.Material, Assert.Single(f.Main.UserMaterials.All)));
        Assert.Equal(0, f.Dialogs.MaterialConflictQuestions);
    }

    [Fact]
    public async Task SavingHistoricalProjectNameTrimsOnlyTheLibrarySnapshot()
    {
        var f = new Fixture(); await f.Load(); f.Main.Setup.SelectedMaterial = Entry("  Historical  ");
        var selection = f.Select(); Assert.True(await selection.SaveSelectedAsync());
        Assert.Equal("Historical", Assert.Single(f.Main.UserMaterials.All).Name);
        Assert.Equal("  Historical  ", selection.CurrentEntry!.Name);
        Assert.False(selection.ShowSave);
    }

    [Theory]
    [InlineData("name")] [InlineData("category")] [InlineData("number")] [InlineData("standards")]
    public async Task EachMetadataChangeParticipatesInHistoryAndRecoveryWithoutAnalysis(string field)
    {
        var f = new Fixture(); await f.Load(); f.Main.Setup.SelectedMaterial = Entry(); CompleteSetup(f.Main);
        Assert.True(await f.Main.Setup.ConfirmAsync()); var original = f.Main.Session!.CurrentRevision.State;
        int analyses = f.Analyses; f.Main.EditProject(); var selection = f.Select(); selection.EditSelectedCommand.Execute(null);
        var editor = selection.Editor!;
        if (field == "name") editor.Name = "Renamed project material";
        else if (field == "category") editor.Category = editor.Categories.Single(c => c.Value == MaterialCategory.Other);
        else if (field == "number") editor.MaterialNumber = "New number";
        else editor.OtherStandards = "Different standard";
        Assert.True(await editor.ConfirmAsync()); selection.UseSelected(); Assert.True(await f.Main.Setup.ConfirmAsync());
        var changed = f.Main.Session.CurrentRevision.State;
        Assert.False(original.ContentEquals(changed)); Assert.Equal(analyses, f.Analyses);
        Assert.Equal(MaterialDisplay.Format(changed.Document.Material), f.Main.Editor!.Overview.Material);
        Assert.Equal(f.Main.Editor.Overview.Material, f.Main.Setup.MaterialName);
        Assert.Same(changed.Document.Material, changed.Document.ToBeamModel().Material);
        Assert.True(changed.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(changed))));
        Assert.True(f.Main.Undo()); Assert.True(original.ContentEquals(f.Main.Session.CurrentRevision.State));
        Assert.Equal(MaterialDisplay.Format(original.Document.Material), f.Main.Editor.Overview.Material);
        Assert.True(f.Main.Redo()); Assert.True(changed.ContentEquals(f.Main.Session.CurrentRevision.State));
        Assert.Equal(MaterialDisplay.Format(changed.Document.Material), f.Main.Editor.Overview.Material);
        Assert.Equal(analyses, f.Analyses);
        var recovery = new ProjectRecovery(f.Files, "metadata-recovery"); await recovery.FlushAsync(changed, null);
        Assert.True(changed.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HistoricalMetadataDefaultsWithoutCatalogLookupAndRetainsUnresolvedStrength(bool explicitNull)
    {
        var state = State(); var json = JsonNode.Parse(ProjectFileCodec.Serialize(state))!;
        var material = json["document"]!["material"]!.AsObject(); material["name"] = "S235JR";
        material.Remove("strengthType");
        foreach (var field in new[] { "category", "materialNumber", "otherStandards" })
            if (explicitNull) material[field] = null; else material.Remove(field);
        var loaded = ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(MaterialCategory.Other, loaded.Document.Material.Category);
        Assert.Null(loaded.Document.Material.MaterialNumber); Assert.Empty(loaded.Document.Material.OtherStandards);
        Assert.Null(loaded.Document.Material.StrengthType);
        var f = new Fixture(); await f.Load(); var recovery = new ProjectRecovery(f.Files, "legacy-metadata");
        await recovery.FlushAsync(loaded, null); Assert.True(loaded.ContentEquals((await recovery.ReadAsync())!.State));
        var draft = new ProjectSetupDraft(Material: loaded.Document.Material);
        f.Files.Data["setup-legacy"] = ProjectSetupRecovery.Encode(new(draft, Now));
        Assert.True(draft.ContentEquals((await new ProjectSetupRecovery(f.Files, "setup-legacy").ReadAsync())!.Draft));
    }

    [Theory]
    [InlineData("category")] [InlineData("otherStandards")]
    public async Task InvalidMetadataIsRejectedByProjectAndSetupRecovery(string field)
    {
        var state = State(); var project = JsonNode.Parse(ProjectFileCodec.Serialize(state))!;
        var material = project["document"]!["material"]!;
        if (field == "category") material[field] = "future-category";
        else material[field] = new JsonArray((JsonNode?)null);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var setup = JsonNode.Parse(ProjectSetupRecovery.Encode(new(new(Material: state.Document.Material), Now)))!;
        setup["draft"]!["material"] = material.DeepClone();
        var files = new Files(); files.Data["invalid-setup"] = Encoding.UTF8.GetBytes(setup.ToJsonString());
        await Assert.ThrowsAsync<ProjectFormatException>(() => new ProjectSetupRecovery(files, "invalid-setup").ReadAsync());
    }

    [Theory]
    [InlineData(MaterialNameConflictDecision.Cancel, false)]
    [InlineData(MaterialNameConflictDecision.Replace, false)]
    [InlineData(MaterialNameConflictDecision.Replace, true)]
    [InlineData(MaterialNameConflictDecision.Rename, false)]
    public async Task ProjectNameConflictRequiresExplicitChoiceAndReplacementConfirmation(MaterialNameConflictDecision choice, bool confirm)
    {
        var existing = LibraryTestSupport.WithMetadata(Entry(category: MaterialCategory.Other), MaterialCategory.Other, "9.8765", ["Existing standard"]);
        var f = new Fixture(); await f.Load(existing); var before = f.Files.Data["materials"].ToArray();
        f.Main.Setup.SelectedMaterial = Entry(); var project = f.Main.Setup.SelectedMaterial; var selection = f.Select();
        f.Dialogs.MaterialConflict = choice; f.Dialogs.ConfirmMaterialReplace = confirm;
        bool saved = await selection.SaveSelectedAsync(); Assert.Equal(choice == MaterialNameConflictDecision.Replace && confirm, saved);
        Assert.Equal(1, f.Dialogs.MaterialConflictQuestions); Assert.Equal(choice == MaterialNameConflictDecision.Replace ? 1 : 0, f.Dialogs.MaterialReplaceQuestions);
        Assert.Equal("Shop stainless (9.8765) · Existing standard", f.Dialogs.MaterialConflictDisplayName);
        if (choice == MaterialNameConflictDecision.Replace)
            Assert.Equal(f.Dialogs.MaterialConflictDisplayName, f.Dialogs.MaterialReplaceDisplayName);
        if (choice == MaterialNameConflictDecision.Rename)
        {
            var editor = selection.Editor!; Assert.Equal(project!.Name, editor.Name); Assert.True(editor.ShowNameError);
            Assert.False(editor.CanConfirm); Assert.Equal(Strings.LibraryNameConflict, editor.NameError);
            editor.Name = "Unique"; Assert.True(await editor.ConfirmAsync()); Assert.Equal(2, f.Main.UserMaterials.All.Count);
            Assert.Equal(MaterialCategory.Other, f.Main.UserMaterials.All[0].Category); Assert.Equal(project.MaterialNumber, f.Main.UserMaterials.All[1].MaterialNumber);
        }
        else if (saved)
        {
            var replaced = f.Main.UserMaterials.Find(project!.Name)!;
            Assert.Single(f.Main.UserMaterials.All);
            Assert.Equal(MaterialCategory.StainlessSteel, replaced.Category);
            Assert.Equal(project.MaterialNumber, replaced.MaterialNumber);
            Assert.Equal(project.OtherStandards, replaced.OtherStandards);
        }
        else { Assert.Equal(before, f.Files.Data["materials"]); Assert.Empty(f.Files.Writes); }
        Assert.Same(project, f.Main.Setup.SelectedMaterial); Assert.Same(selection.CurrentEntry, selection.SelectedEntry);
    }

    [Fact]
    public async Task RenameCancelAndFailedReplacementKeepTheOriginalBytes()
    {
        var f = new Fixture(); await f.Load(Entry(category: MaterialCategory.Other)); f.Main.Setup.SelectedMaterial = Entry(); var selection = f.Select();
        var before = f.Files.Data["materials"].ToArray(); f.Dialogs.MaterialConflict = MaterialNameConflictDecision.Rename;
        await selection.SaveSelectedAsync(); selection.Editor!.CancelCommand.Execute(null); Assert.Equal(before, f.Files.Data["materials"]);
        f.Dialogs.MaterialConflict = MaterialNameConflictDecision.Replace; f.Dialogs.ConfirmMaterialReplace = true; f.Files.FailWritePath = "materials";
        Assert.False(await selection.SaveSelectedAsync()); Assert.Equal(before, f.Files.Data["materials"]); Assert.True(selection.HasError); Assert.True(selection.ShowSave);
    }

    [Fact]
    public async Task LegacyStrengthResolutionPrecedesLibrarySaveAndRetainsAllConflictChoices()
    {
        var f = new Fixture(); await f.Load(Entry(category: MaterialCategory.Other));
        var state = State() with { Document = State().Document with { Material = Entry() } };
        var json = JsonNode.Parse(ProjectFileCodec.Serialize(state))!;
        json["document"]!["material"]!.AsObject().Remove("strengthType");
        f.Main.Setup.SelectedMaterial = ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())).Document.Material;
        var selection = f.Select(); Assert.False(await selection.SaveSelectedAsync());
        var editor = selection.Editor!; Assert.True(editor.IsProjectDraft); Assert.Null(editor.StrengthType);
        editor.StrengthType = editor.StrengthTypes[0]; Assert.True(await editor.ConfirmAsync());
        Assert.Null(f.Main.Setup.SelectedMaterial.StrengthType); Assert.Empty(f.Files.Writes);
        Assert.Equal(MaterialStrengthType.Re, selection.CurrentEntry!.Material.StrengthType);
        f.Dialogs.MaterialConflict = MaterialNameConflictDecision.Cancel;
        Assert.False(await selection.SaveSelectedAsync()); Assert.Equal(1, f.Dialogs.MaterialConflictQuestions); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task DeleteCancellationAndFailedWritesRetainSelectionAndBytes()
    {
        var f = new Fixture(); await f.Load(Entry()); f.Main.Setup.SelectedMaterial = Entry(); var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single();
        var selected = selection.SelectedEntry; var bytes = f.Files.Data["materials"].ToArray();
        f.Dialogs.ConfirmLibraryDelete = false; await selection.DeleteSelectedAsync(); Assert.Same(selected, selection.SelectedEntry); Assert.Equal(bytes, f.Files.Data["materials"]);
        f.Dialogs.ConfirmLibraryDelete = true; f.Files.FailWritePath = "materials"; Assert.False(await selection.DeleteSelectedAsync()); Assert.Same(selected, selection.SelectedEntry);
        f.Files.FailWritePath = null; Assert.True(await selection.DeleteSelectedAsync()); Assert.Empty(f.Main.UserMaterials.All);
        Assert.Same(selection.CurrentEntry, selection.SelectedEntry); Assert.True(selection.ShowSave);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrDamagedLibraryStillAllowsProjectEditingAndBuiltInUse(bool damaged)
    {
        var f = new Fixture(store: damaged);
        if (damaged) { f.Files.Data["materials"] = [123]; await f.Main.InitializeLibrariesAsync(); }
        f.Main.Setup.SelectedMaterial = Entry(); var selection = f.Select(); Assert.False(selection.NewCommand.CanExecute(null));
        Assert.False(selection.CanSaveSelected); selection.EditSelectedCommand.Execute(null); selection.Editor!.Name = "Project only";
        Assert.True(await selection.Editor.ConfirmAsync()); selection.UseSelected(); Assert.Equal("Project only", f.Main.Setup.SelectedMaterial!.Name);
        selection = f.Select(); selection.SelectedEntry = selection.BuiltInEntries[0]; Assert.False(selection.CanEditSelected); Assert.True(selection.CanUseSelected); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task CompleteProjectAndBothRecoverySnapshotsPreserveMetadataWithoutLibraryReferences()
    {
        var f = new Fixture(); await f.Load(Entry()); var selection = f.Select(); selection.SelectedEntry = selection.UserEntries.Single(); selection.UseSelected();
        CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync()); var state = f.Main.Session!.CurrentRevision.State;
        var bytes = ProjectFileCodec.Serialize(state); var wire = JsonNode.Parse(bytes)!["document"]!["material"]!;
        Assert.Equal("stainlessSteel", wire["category"]!.GetValue<string>()); Assert.Equal("1.4567", wire["materialNumber"]!.GetValue<string>());
        Assert.Equal(2, wire["otherStandards"]!.AsArray().Count); Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(bytes)));
        var recovery = new ProjectRecovery(f.Files, "project-recovery"); await recovery.FlushAsync(state, null);
        Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
        var draft = new ProjectSetupDraft(Material: state.Document.Material); f.Files.Data["setup-recovery"] = ProjectSetupRecovery.Encode(new(draft, Now));
        Assert.True(draft.ContentEquals((await new ProjectSetupRecovery(f.Files, "setup-recovery").ReadAsync())!.Draft));
        await f.Main.SaveMaterialAsync(Entry("Renamed", MaterialCategory.Other), Entry().Name);
        Assert.Equal(bytes, ProjectFileCodec.Serialize(state));
        foreach (var key in new[] { "libraryId", "source", "provenance" }) Assert.DoesNotContain("\"" + key + "\"", Encoding.UTF8.GetString(bytes));
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
