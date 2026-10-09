using System.Globalization;
using System.Text;
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
public sealed class DesktopProjectSetupWorkflowTests
{
    private sealed class Fixture
    {
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public MainWindowViewModel Main { get; }
        public int Analyses { get; private set; }
        public Fixture(bool brokenCatalog = false)
        {
            Main = new(b => { Analyses++; return BeamAnalysis.Analyze(b); }, Files, Dialogs,
                materialStore: new(Files, "materials"), sectionStore: new(Files, "sections"),
                catalogLoader: brokenCatalog ? () => throw new LibraryFormatException("Injected") : null);
        }
        public async Task Load() => await Main.InitializeLibrariesAsync();
        public void Create() => Main.Setup.ApplyCommand.Execute(null);
    }
    private static MaterialLibraryEntry Entry(string name = "Mine") => new(
        new Material(name, Pressure.FromPascals(210e9), Pressure.FromMegapascals(355),
            MassDensity.FromKilogramsPerCubicMeter(7800), PoissonRatio.FromValue(.3)),
        MaterialCategory.AlloySteel, "1.2345", ["Alt A", "Alt B"]);
    private static ISectionDefinition Rectangle() => new RectangleSectionGeometry(M(.1), M(.2));
    private static MaterialSelectionViewModel Materials(Fixture f)
    { f.Main.Setup.ShowMaterials(); return Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep); }
    private static SectionSelectionViewModel Sections(Fixture f)
    { f.Main.Setup.ShowSections(); return Assert.IsType<SectionSelectionViewModel>(f.Main.Setup.CurrentStep); }
    private static MaterialEditorViewModel NewMaterial(Fixture f)
    {
        Materials(f).NewCommand.Execute(null);
        var e = Assert.IsType<MaterialEditorViewModel>(f.Main.Setup.CurrentStep);
        e.Name = "One-off";
        e.Inputs[0].Text = "210"; e.Inputs[1].Text = "355";
        return e;
    }
    private static SectionEditorViewModel NewSection(Fixture f)
    {
        Sections(f).NewCommand.Execute(null);
        var e = Assert.IsType<SectionEditorViewModel>(f.Main.Setup.CurrentStep);
        foreach (var input in e.Parameters) input.Text = input.Key == "b" ? "100" : "200";
        return e;
    }

    [Fact]
    public async Task NewProjectUsesCompleteBuiltInAndModernGeometrySnapshot()
    {
        var f = new Fixture(); await f.Load(); Assert.Null(f.Main.Editor);
        var s = Assert.IsType<RectangularHollowSectionGeometry>(f.Main.Setup.SelectedSection);
        Assert.Equal(100, s.Width.Millimeters); Assert.Equal(100, s.Height.Millimeters);
        Assert.Equal(5, s.WallThickness.Millimeters); Assert.Equal(0, s.OuterRadius.Millimeters);
        Assert.Same(f.Main.BuiltInMaterials!.Find("S235JR")!.Material, f.Main.Setup.SelectedMaterial);
        Assert.False(Materials(f).HasCurrent); Assert.False(Sections(f).HasCurrent);
        f.Main.Setup.ShowOverview(); f.Create();
        var d = f.Main.Editor!.Document;
        Assert.Same(s, d.Section); Assert.Equal(SectionAxisDesignation.Y, d.BendingAxis);
        Assert.Equal(210e9, d.Material.YoungsModulus.Pascals); Assert.Equal(235e6, d.Material.YieldStrength.Pascals);
        Assert.Equal(7850, d.Material.Density!.Value.KilogramsPerCubicMeter); Assert.Equal(.3, d.Material.PoissonRatio!.Value.Value);
        Assert.True(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(f.Main.Session!.CurrentRevision.State))
            .ContentEquals(f.Main.Session.CurrentRevision.State));
        Assert.Equal(1, f.Analyses);
    }

    [Fact]
    public async Task ExistingSnapshotNeverMatchesSourcesAndCurrentEditHasNoLibraryMetadata()
    {
        var f = new Fixture();
        var builtIn = f.Main.BuiltInMaterials!.Find("S235JR")!;
        f.Files.Data["materials"] = MaterialLibraryCodec.Serialize(new([builtIn]));
        f.Files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("Same geometry", Rectangle())]));
        await f.Load();
        var state = State() with { Document = State().Document.WithSection(Rectangle(), SectionAxisDesignation.Z) with { Material = builtIn.Material } };
        var path = TestPath("matching.spandraft"); f.Files.Data[path] = ProjectFileCodec.Serialize(state); f.Dialogs.OpenPath = path;
        Assert.True(await f.Main.OpenAsync()); var snapshot = f.Main.Editor!.Document;
        f.Main.EditProject(); var selection = Materials(f);
        Assert.Same(snapshot.Material, selection.CurrentMaterial);
        Assert.Equal(4, selection.BuiltInGroups.Count); Assert.Equal(8, selection.BuiltInGroups.Sum(g => g.Entries.Count));
        Assert.Single(selection.UserEntries);
        selection.EditCurrentCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal(MaterialCategory.Other, editor.Category!.Value);
        Assert.Empty(editor.MaterialNumber); Assert.Empty(editor.OtherStandards);
        Assert.Equal(snapshot.Material.Density, editor.Draft!.Material.Density);
        f.Main.Setup.Escape(); Assert.Equal(ProjectSetupPage.MaterialSelection, f.Main.Setup.Page);
        f.Main.Setup.Escape(); Assert.Equal(ProjectSetupPage.Overview, f.Main.Setup.Page);
        f.Main.Setup.Escape(); Assert.Equal(ProjectSetupPage.Overview, f.Main.Setup.Page);
        f.Main.Setup.CancelCommand.Execute(null); Assert.Same(snapshot, f.Main.Editor.Document);
    }

    [Fact]
    public async Task BuiltInUseCopiesAllValuesAndCustomizePreservesCatalog()
    {
        var f = new Fixture(); await f.Load(); var selection = Materials(f);
        var row = selection.BuiltInGroups.SelectMany(g => g.Entries).Single(r => r.Entry.Material.Name == "X5CrNi18-10");
        Assert.Equal("X5CrNi18-10 · 1.4301 · AISI 304", row.Caption);
        Assert.True(row.IsBuiltIn); Assert.False(row.DeleteCommand.CanExecute(null));
        var original = row.Entry.Material;
        row.EditCommand.Execute(null); var editor = Assert.IsType<MaterialEditorViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal("1.4301", editor.MaterialNumber); Assert.Equal("AISI 304", editor.OtherStandards);
        Assert.NotSame(original, editor.Draft!.Material); editor.Inputs[0].Text = "190";
        Assert.True(await editor.ConfirmAsync()); Assert.Equal(190e9, f.Main.Setup.SelectedMaterial!.YoungsModulus.Pascals);
        Assert.Equal(200e9, original.YoungsModulus.Pascals); Assert.Empty(f.Main.UserMaterials.All);
        row = Materials(f).BuiltInGroups.SelectMany(g => g.Entries).Single(r => r.Entry.Material.Name == original.Name);
        row.UseCommand.Execute(null); Assert.Same(original, f.Main.Setup.SelectedMaterial);
        Assert.Equal(original.Density, f.Main.Setup.SelectedMaterial!.Density);
        Assert.Equal(original.PoissonRatio, f.Main.Setup.SelectedMaterial.PoissonRatio);
    }

    [Theory]
    [InlineData("de-DE", "210,125", "0,3")]
    [InlineData("en-US", "210.125", "0.3")]
    public async Task CustomMaterialUsesCultureOptionalValuesAndSemicolonMetadata(string culture, string eText, string nuText)
    {
        using var scope = new UiCultureScope(culture);
        var f = new Fixture(); await f.Load(); var editor = NewMaterial(f);
        editor.Inputs[0].Text = eText; editor.Inputs[3].Text = nuText;
        editor.OtherStandards = " AISI 304 ; ; UNS S30400; ";
        Assert.Equal(new[] { "AISI 304", "UNS S30400" }, editor.Draft!.OtherStandards);
        Assert.Null(editor.Draft.Material.Density); Assert.Equal(.3, editor.Draft.Material.PoissonRatio!.Value.Value);
        Assert.True(await editor.ConfirmAsync()); Assert.Equal(210.125e9, f.Main.Setup.SelectedMaterial!.YoungsModulus.Pascals);
        Assert.Empty(f.Main.UserMaterials.All); Assert.Empty(f.Files.Writes);
        Assert.False(f.Main.Setup.HasDensity); Assert.True(f.Main.Setup.HasPoissonRatio);
    }

    [Theory]
    [InlineData("-1", false)] [InlineData("0.5", false)] [InlineData("NaN", false)]
    [InlineData("0", true)] [InlineData("-0.5", true)] [InlineData("", true)]
    public async Task PoissonValidationUsesDomainIncludingZeroAndUnknown(string text, bool valid)
    {
        using var scope = new UiCultureScope("en-US");
        var f = new Fixture(); await f.Load(); var editor = NewMaterial(f); editor.Inputs[3].Text = text;
        Assert.Equal(valid, editor.CanConfirm);
        if (!valid) { Assert.False(await editor.ConfirmAsync()); Assert.True(editor.HasError); }
    }

    [Fact]
    public async Task MaterialSaveIsPersistentBeforeUseAndSurvivesSetupCancel()
    {
        var f = new Fixture(); await f.Load(); f.Create(); var document = f.Main.Editor!.Document;
        f.Main.EditProject(); var editor = NewMaterial(f); editor.SaveToLibrary = true;
        Assert.True(await editor.ConfirmAsync());
        Assert.Equal("One-off", Assert.Single(MaterialLibraryCodec.Deserialize(f.Files.Data["materials"]).All).Material.Name);
        Assert.Equal("One-off", f.Main.Setup.SelectedMaterial!.Name);
        f.Main.Setup.CancelCommand.Execute(null); Assert.Same(document, f.Main.Editor.Document);
        Assert.Single(f.Main.UserMaterials.All);
    }

    [Fact]
    public async Task MaterialLibraryEditAndDeleteRequireExplicitProjectUse()
    {
        var f = new Fixture(); f.Files.Data["materials"] = MaterialLibraryCodec.Serialize(new([Entry()])); await f.Load(); f.Create();
        var snapshot = f.Main.Editor!.Document; f.Main.EditProject();
        Materials(f).UserEntries.Single().EditCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(f.Main.Setup.CurrentStep);
        Assert.True(editor.IsLibraryEdit); editor.Name = "Renamed"; editor.Inputs[1].Text = "400";
        Assert.True(await editor.ConfirmAsync()); Assert.Equal(ProjectSetupPage.MaterialSelection, f.Main.Setup.Page);
        Assert.Same(snapshot.Material, f.Main.Setup.SelectedMaterial); Assert.Same(snapshot, f.Main.Editor.Document);
        var selection = Assert.IsType<MaterialSelectionViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal("Renamed", selection.UserEntries.Single().Entry.Material.Name);
        selection.UserEntries.Single().UseCommand.Execute(null);
        Assert.Equal(400e6, f.Main.Setup.SelectedMaterial!.YieldStrength.Pascals);
        selection = Materials(f); f.Dialogs.ConfirmLibraryDelete = false; await selection.DeleteAsync("Renamed");
        Assert.Single(f.Main.UserMaterials.All);
        f.Dialogs.ConfirmLibraryDelete = true; await selection.DeleteAsync("Renamed");
        Assert.Empty(f.Main.UserMaterials.All); Assert.Same(snapshot, f.Main.Editor.Document);
        Assert.Equal("Renamed", f.Main.Setup.SelectedMaterial!.Name);
    }

    [Theory]
    [InlineData(true, "add")] [InlineData(true, "replace")] [InlineData(true, "remove")]
    [InlineData(false, "add")] [InlineData(false, "replace")] [InlineData(false, "remove")]
    public async Task AllLibraryWriteFailuresPreserveActiveStateAndPersistedBytes(bool material, string operation)
    {
        var f = new Fixture();
        f.Files.Data["materials"] = MaterialLibraryCodec.Serialize(new([Entry()]));
        f.Files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("Mine", Rectangle())]));
        await f.Load();
        var oldMaterials = f.Main.UserMaterials; var oldSections = f.Main.UserSections;
        string path = material ? "materials" : "sections"; var bytes = f.Files.Data[path].ToArray();
        f.Files.FailWritePath = path;
        string? error = material ? operation switch
        {
            "add" => await f.Main.SaveMaterialAsync(Entry("Added")),
            "replace" => await f.Main.SaveMaterialAsync(Entry("Renamed"), "Mine"),
            _ => await f.Main.DeleteMaterialAsync("Mine")
        } : operation switch
        {
            "add" => await f.Main.SaveSectionAsync(new("Added", Rectangle())),
            "replace" => await f.Main.SaveSectionAsync(new("Renamed", Rectangle()), "Mine"),
            _ => await f.Main.DeleteSectionAsync("Mine")
        };
        Assert.NotNull(error); Assert.Same(oldMaterials, f.Main.UserMaterials); Assert.Same(oldSections, f.Main.UserSections);
        Assert.Equal(bytes, f.Files.Data[path]); Assert.Empty(f.Files.Writes); Assert.False(f.Main.IsBusy);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SaveFailureRetainsEditorAndAllowsOneOffUse(bool material)
    {
        var f = new Fixture(); await f.Load();
        f.Files.FailWritePath = material ? "materials" : "sections";
        if (material)
        {
            var editor = NewMaterial(f); editor.SaveToLibrary = true; var draft = editor.Draft;
            Assert.False(await editor.ConfirmAsync()); Assert.Same(editor, f.Main.Setup.CurrentStep);
            Assert.Same(draft, editor.Draft); Assert.True(editor.HasError); Assert.Empty(f.Main.UserMaterials.All);
            editor.SaveToLibrary = false; Assert.True(await editor.ConfirmAsync());
        }
        else
        {
            var editor = NewSection(f); editor.SaveToLibrary = true; editor.PresetName = "Draft"; var draft = editor.Draft;
            Assert.False(await editor.ConfirmAsync()); Assert.Same(editor, f.Main.Setup.CurrentStep);
            Assert.Same(draft, editor.Draft); Assert.True(editor.HasError); Assert.Empty(f.Main.UserSections.All);
            editor.SaveToLibrary = false; Assert.True(await editor.ConfirmAsync());
        }
        Assert.Equal(ProjectSetupPage.Overview, f.Main.Setup.Page); Assert.Empty(f.Files.Writes);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task PendingWriteKeepsOldStateAndBlocksNavigationAndDuplicateActions(bool material)
    {
        var f = new Fixture(); await f.Load(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        f.Files.BeforeWrite = (_, _, _) => { entered.TrySetResult(); return release.Task; };
        var old = material ? (object)f.Main.UserMaterials : f.Main.UserSections;
        Task<bool> pending;
        if (material) { var e = NewMaterial(f); e.SaveToLibrary = true; pending = e.ConfirmAsync(); }
        else { var e = NewSection(f); e.SaveToLibrary = true; e.PresetName = "Draft"; pending = e.ConfirmAsync(); }
        try
        {
            await WaitFor(entered.Task);
            Assert.True(f.Main.IsBusy); Assert.Same(old, material ? (object)f.Main.UserMaterials : f.Main.UserSections);
            var page = f.Main.Setup.Page; f.Main.Setup.Escape(); Assert.Equal(page, f.Main.Setup.Page);
            Assert.False(await f.Main.NewAsync()); Assert.False(await f.Main.Setup.ConfirmAsync());
            Assert.NotNull(material ? await f.Main.SaveSectionAsync(new("Concurrent", Rectangle())) : await f.Main.SaveMaterialAsync(Entry("Concurrent")));
        }
        finally { release.TrySetResult(); }
        Assert.True(await WaitFor(pending)); Assert.False(f.Main.IsBusy); Assert.Equal(ProjectSetupPage.Overview, f.Main.Setup.Page);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task EveryParametricShapeCanBeConstructedFromDraftAndDomainRejectsInvalidCombination(int kind)
    {
        var f = new Fixture(); await f.Load(); var editor = NewSection(f);
        editor.Type = editor.Types.Single(t => (int)t.Value == kind);
        foreach (var input in editor.Parameters)
            input.Text = input.Key switch { "b" => "100", "h" => "200", "d" or "D" => "100", "t" or "tw" => "5", "tf" => "10", "r" => "0", _ => throw new InvalidOperationException() };
        Assert.True(editor.IsValid, editor.Error); Assert.IsAssignableFrom<IParametricSectionDefinition>(editor.Draft);
        Assert.Equal(2, editor.Axes.Count); Assert.NotEmpty(editor.Area);
        if (editor.Parameters.Any(p => p.Key == "t"))
        {
            var input = editor.Parameters.Single(p => p.Key == "t"); input.Text = "500";
            Assert.False(editor.IsValid); Assert.Empty(editor.Axes); Assert.False(await editor.ConfirmAsync());
            input.Text = "5"; Assert.True(editor.IsValid);
        }
        Assert.True(await editor.ConfirmAsync()); Assert.Equal(ProjectSetupPage.Overview, f.Main.Setup.Page);
        Assert.Empty(f.Main.UserSections.All); Assert.Empty(f.Files.Writes);
    }

    [Theory]
    [InlineData(ManualAxisConfiguration.Y)] [InlineData(ManualAxisConfiguration.Z)]
    [InlineData(ManualAxisConfiguration.U)] [InlineData(ManualAxisConfiguration.V)]
    [InlineData(ManualAxisConfiguration.YZ)] [InlineData(ManualAxisConfiguration.UV)]
    public async Task ManualConfigurationsHaveCanonicalAxesAndExactlyOneW(ManualAxisConfiguration config)
    {
        var f = new Fixture(); await f.Load(); var editor = NewSection(f);
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Manual);
        editor.Configuration = editor.Configurations.Single(c => c.Value == config);
        foreach (var input in editor.Parameters) input.Text = input.Key == "A" ? "1000" : input.Key.StartsWith('I') ? "1000000" : "20000";
        var section = Assert.IsType<ManualSectionDefinition>(editor.Draft);
        Assert.Equal(config is ManualAxisConfiguration.YZ or ManualAxisConfiguration.UV ? 2 : 1, section.Axes.Count);
        Assert.All(section.Axes, a => Assert.Equal(a.PositiveSectionModulus, a.NegativeSectionModulus));
        Assert.Equal(section.Axes.OrderBy(a => a.AxisDesignation), section.Axes);
        Assert.Equal(6, editor.Configurations.Count);
        var previous = editor.Configuration;
        editor.Configuration = new((ManualAxisConfiguration)99, "invalid");
        Assert.Same(previous, editor.Configuration); Assert.True(await editor.ConfirmAsync());
        Assert.Equal(section.Axes[0].AxisDesignation, f.Main.Setup.BendingAxis);
    }

    [Fact]
    public async Task SectionSwitchPreservesAvailableAxisAndFallsBackBeforeReadingProperties()
    {
        var f = new Fixture(); await f.Load(); var setup = f.Main.Setup;
        setup.BendingAxis = SectionAxisDesignation.Z; setup.SelectedSection = Rectangle();
        Assert.Equal(SectionAxisDesignation.Z, setup.BendingAxis);
        setup.SelectedSection = new AngleSectionGeometry(M(.1), M(.2), M(.005), M(0));
        Assert.Equal(SectionAxisDesignation.U, setup.BendingAxis);
        setup.BendingAxis = SectionAxisDesignation.V; var old = setup.Inertia;
        setup.SelectedSection = Rectangle(); Assert.Equal(SectionAxisDesignation.Y, setup.BendingAxis);
        Assert.NotEqual(old, setup.Inertia); Assert.NotEmpty(setup.Modulus);
        setup.SelectedSection = new TSectionGeometry(M(.2), M(.1), M(.005), M(.01), M(0));
        Assert.True(setup.IsAsymmetric); Assert.NotEqual(setup.PositiveModulus, setup.NegativeModulus);
        Assert.Contains("W+ =", setup.Modulus); Assert.Contains("W− =", setup.Modulus);
    }

    [Fact]
    public async Task SectionPresetSaveEditDeleteDoesNotChangeSnapshotWithoutUse()
    {
        var f = new Fixture(); await f.Load(); f.Create(); var snapshot = f.Main.Editor!.Document;
        f.Main.EditProject(); var editor = NewSection(f); editor.SaveToLibrary = true; editor.PresetName = "My preset";
        Assert.True(await editor.ConfirmAsync()); Assert.Single(SectionLibraryCodec.Deserialize(f.Files.Data["sections"]).All);
        Assert.Same(snapshot, f.Main.Editor.Document);
        Sections(f).UserEntries.Single().EditCommand.Execute(null);
        editor = Assert.IsType<SectionEditorViewModel>(f.Main.Setup.CurrentStep); Assert.True(editor.IsLibraryEdit);
        var used = f.Main.Setup.SelectedSection; editor.PresetName = "Renamed";
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Circle); editor.Parameters.Single().Text = "50";
        Assert.True(await editor.ConfirmAsync()); Assert.Same(used, f.Main.Setup.SelectedSection);
        var selection = Assert.IsType<SectionSelectionViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal("Renamed", selection.UserEntries.Single().Entry.Name);
        selection.UserEntries.Single().UseCommand.Execute(null); Assert.IsType<CircleSectionGeometry>(f.Main.Setup.SelectedSection);
        selection = Sections(f); f.Dialogs.ConfirmLibraryDelete = false; await selection.DeleteAsync("Renamed"); Assert.Single(f.Main.UserSections.All);
        f.Dialogs.ConfirmLibraryDelete = true; await selection.DeleteAsync("Renamed"); Assert.Empty(f.Main.UserSections.All);
        Assert.Same(snapshot, f.Main.Editor.Document);
        f.Main.Setup.ShowOverview(); f.Main.Setup.ApplyCommand.Execute(null);
        Assert.DoesNotContain("My preset", Encoding.UTF8.GetString(ProjectFileCodec.Serialize(f.Main.Session!.CurrentRevision.State)));
        Assert.DoesNotContain("Renamed", Encoding.UTF8.GetString(ProjectFileCodec.Serialize(f.Main.Session.CurrentRevision.State)));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DamagedLibrariesAreIndependentAndNeverOverwritten(bool material)
    {
        var f = new Fixture(); var corrupt = Encoding.UTF8.GetBytes("{");
        string path = material ? "materials" : "sections"; f.Files.Data[path] = corrupt;
        f.Files.Data[material ? "sections" : "materials"] = material
            ? SectionLibraryCodec.Serialize(new([new("Usable", Rectangle())]))
            : MaterialLibraryCodec.Serialize(new([Entry()]));
        await f.Load();
        Assert.Equal(!material, f.Main.CanSaveMaterials); Assert.Equal(material, f.Main.CanSaveSections);
        if (material)
        {
            Assert.True(Materials(f).HasLibraryError); Assert.Equal(8, Materials(f).BuiltInGroups.Sum(g => g.Entries.Count));
            Assert.Single(f.Main.UserSections.All); var e = NewMaterial(f); Assert.False(e.CanSaveToLibrary);
            e.SaveToLibrary = true; Assert.False(await e.ConfirmAsync()); e.SaveToLibrary = false; Assert.True(await e.ConfirmAsync());
        }
        else
        {
            Assert.True(Sections(f).HasLibraryError); Assert.Single(f.Main.UserMaterials.All);
            var e = NewSection(f); Assert.False(e.CanSaveToLibrary); Assert.True(await e.ConfirmAsync());
        }
        Assert.Equal(corrupt, f.Files.Data[path]); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task BrokenCatalogHasNoFallbackButAllowsOneOffAndOpeningProjects()
    {
        var f = new Fixture(brokenCatalog: true); await f.Load();
        Assert.Null(f.Main.Setup.SelectedMaterial); Assert.True(f.Main.Setup.HasStartupError);
        Assert.False(f.Main.Setup.CanApply); f.Create(); Assert.Null(f.Main.Editor);
        Assert.True(await NewMaterial(f).ConfirmAsync()); f.Create(); Assert.NotNull(f.Main.Editor);
        var path = TestPath("independent.spandraft"); f.Files.Data[path] = ProjectFileCodec.Serialize(State()); f.Dialogs.OpenPath = path;
        f.Dialogs.Leave = LeaveDecision.Discard;
        Assert.True(await f.Main.OpenAsync()); Assert.Equal("Test steel", f.Main.Editor!.Document.Material.Name);
    }

    [Fact]
    public async Task CancelPreservesDocumentDirtyHistoryAndAnalysisWhileApplyCommitsTogether()
    {
        var f = new Fixture(); await f.Load(); f.Create(); var session = f.Main.Session!;
        session.Commit(session.CurrentRevision.State with { Document = f.Main.Editor!.Document with { Length = M(2) } });
        session.Undo();
        var document = f.Main.Editor!.Document; var revision = session.CurrentRevision;
        var undo = session.UndoHistory.ToArray(); var redo = session.RedoHistory.ToArray();
        var dirty = session.IsDirty; var presentation = f.Main.Editor.Presentation; int calls = f.Analyses;
        f.Main.EditProject(); var setup = f.Main.Setup;
        setup.SelectedMaterial = Entry().Material; setup.SelectedSection = new AngleSectionGeometry(M(.1), M(.2), M(.005), M(0));
        setup.BendingAxis = SectionAxisDesignation.V; setup.CancelCommand.Execute(null);
        Assert.Same(document, f.Main.Editor.Document); Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(undo, session.UndoHistory); Assert.Equal(redo, session.RedoHistory);
        Assert.Equal(dirty, session.IsDirty); Assert.Same(presentation, f.Main.Editor.Presentation); Assert.Equal(calls, f.Analyses);
        f.Main.EditProject(); setup = f.Main.Setup; setup.SelectedMaterial = Entry().Material;
        setup.SelectedSection = new AngleSectionGeometry(M(.1), M(.2), M(.005), M(0)); setup.BendingAxis = SectionAxisDesignation.V;
        Assert.Equal(calls, f.Analyses); setup.ApplyCommand.Execute(null);
        Assert.Same(setup.SelectedMaterial, f.Main.Editor.Document.Material); Assert.Same(setup.SelectedSection, f.Main.Editor.Document.Section);
        Assert.Equal(SectionAxisDesignation.V, f.Main.Editor.Document.BendingAxis); Assert.Equal(calls + 1, f.Analyses);
        Assert.Equal(undo.Length + 1, session.UndoHistory.Count); Assert.Empty(session.RedoHistory);
        Assert.True(f.Main.Undo()); Assert.True(revision.State.ContentEquals(session.CurrentRevision.State));
    }

    [Fact]
    public async Task SavedProjectLoadsAndAnalyzesWhenAllLibrariesAndCatalogAreUnavailable()
    {
        var f = new Fixture(); await f.Load(); f.Create();
        var state = State() with { Document = State().Document.WithSection(Rectangle(), SectionAxisDesignation.Z) with { Length = M(1), Material = Entry().Material } };
        f.Main.Session!.Commit(state); Assert.True(await f.Main.SaveAsync()); var path = f.Main.Session.FilePath!;
        Assert.True(await f.Main.RequestCloseAsync());
        var reopened = new Fixture(brokenCatalog: true);
        reopened.Files.Data["materials"] = Encoding.UTF8.GetBytes("{"); reopened.Files.Data["sections"] = Encoding.UTF8.GetBytes("{");
        reopened.Files.Data[path] = f.Files.Data[path]; reopened.Dialogs.OpenPath = path; await reopened.Load();
        Assert.True(await reopened.Main.OpenAsync()); Assert.True(state.ContentEquals(reopened.Main.Session!.CurrentRevision.State));
        Assert.True(reopened.Main.Editor!.Presentation.IsSuccess);
        string json = Encoding.UTF8.GetString(reopened.Files.Data[path]);
        foreach (var key in new[] { "materialNumber", "otherStandards", "category", "presetName", "libraryId", "provenance" })
            Assert.DoesNotContain("\"" + key + "\"", json);
    }

    [Fact]
    public async Task OpenSectionEditorRejectsAffectedUnitChangesAndRetainsTextOnPresentationChange()
    {
        var f = new Fixture(); await f.Load(); var editor = NewSection(f); editor.Parameters[0].Text = "100,";
        Assert.False(f.Main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Standard));
        Assert.Same(editor, f.Main.Setup.CurrentStep); Assert.Equal("100,", editor.Parameters[0].Text);
        Assert.True(f.Main.SetResultPresentation(UnitProfile.Default, PresentationMode.Detailed));
        Assert.Equal("100,", editor.Parameters[0].Text);
        f.Main.Setup.Escape(); Assert.True(f.Main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Standard));
    }

    [Theory]
    [InlineData("de-DE", 0)] [InlineData("de-DE", 1)] [InlineData("de-DE", 2)]
    [InlineData("en-US", 0)] [InlineData("en-US", 1)] [InlineData("en-US", 2)]
    public async Task UnchangedInputBuffersPreserveExactSnapshotSiAcrossCulturesAndUnitProfiles(string culture, int profile)
    {
        using var scope = new UiCultureScope(culture);
        var f = new Fixture(); await f.Load();
        Assert.True(f.Main.SetResultPresentation(profile switch { 0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering, _ => UnitProfile.UnitedStates }, PresentationMode.Standard));
        var material = new Material(" Precise ", Pressure.FromPascals(210123456789.12345), Pressure.FromPascals(355123456.78912345),
            MassDensity.FromKilogramsPerCubicMeter(7800.123456789), PoissonRatio.FromValue(.31234567890123456));
        var manual = new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567),
            new(SectionAxisDesignation.U, SecondMomentOfArea.FromMetersToTheFourth(1.2345678901234567e-6), SectionModulus.FromCubicMeters(2.3456789012345678e-5)),
            new(SectionAxisDesignation.V, SecondMomentOfArea.FromMetersToTheFourth(3.4567890123456789e-6), SectionModulus.FromCubicMeters(4.567890123456789e-5)));
        f.Main.Setup.SelectedMaterial = material; f.Main.Setup.SelectedSection = manual; f.Create(); f.Main.EditProject();
        Materials(f).EditCurrentCommand.Execute(null);
        var materialEditor = Assert.IsType<MaterialEditorViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal(material.Name, materialEditor.Name);
        Assert.Equal(material.YoungsModulus, materialEditor.Draft!.Material.YoungsModulus);
        Assert.Equal(material.YieldStrength, materialEditor.Draft.Material.YieldStrength);
        Assert.Equal(material.Density, materialEditor.Draft.Material.Density); Assert.Equal(material.PoissonRatio, materialEditor.Draft.Material.PoissonRatio);
        Assert.True(await materialEditor.ConfirmAsync());
        Sections(f).EditCurrentCommand.Execute(null);
        var sectionEditor = Assert.IsType<SectionEditorViewModel>(f.Main.Setup.CurrentStep);
        Assert.Equal(manual.Area, sectionEditor.Draft!.Area); Assert.Equal(manual.Axes, sectionEditor.Draft.Axes);
        Assert.True(await sectionEditor.ConfirmAsync());
        f.Main.Setup.SelectedSection = new RectangleSection(M(.12345678901234567), M(.23456789012345678));
        f.Main.Setup.ApplyCommand.Execute(null); f.Main.EditProject(); Sections(f).EditCurrentCommand.Execute(null);
        var geometry = Assert.IsType<RectangleSectionGeometry>(Assert.IsType<SectionEditorViewModel>(f.Main.Setup.CurrentStep).Draft);
        Assert.Equal(.12345678901234567, geometry.Width.Meters); Assert.Equal(.23456789012345678, geometry.Height.Meters);
    }

    [Theory]
    [InlineData(0, "0")] [InlineData(1, "-1")] [InlineData(2, "0")] [InlineData(0, "NaN")]
    public async Task InvalidRequiredMaterialValuesCannotBeUsed(int input, string text)
    {
        var f = new Fixture(); await f.Load(); var editor = NewMaterial(f); editor.Inputs[input].Text = text;
        Assert.False(editor.CanConfirm); Assert.True(editor.HasError); Assert.False(await editor.ConfirmAsync());
        Assert.Equal(text, editor.Inputs[input].Text); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task DuplicateNamesBlockLibrarySaveWhileOneOffDraftRemainsUsable()
    {
        var f = new Fixture(); f.Files.Data["materials"] = MaterialLibraryCodec.Serialize(new([Entry("One-off")]));
        f.Files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("Mine", Rectangle())])); await f.Load();
        var m = NewMaterial(f); m.Name = "one-OFF"; m.SaveToLibrary = true;
        Assert.False(m.CanConfirm); m.SaveToLibrary = false; Assert.True(await m.ConfirmAsync());
        var s = NewSection(f); s.PresetName = "mINE"; s.SaveToLibrary = true;
        Assert.False(s.CanConfirm); s.SaveToLibrary = false; Assert.True(await s.ConfirmAsync()); Assert.Empty(f.Files.Writes);
    }

    [Fact]
    public async Task CatalogAndUserLibraryFailuresDoNotPreventRecovery()
    {
        var files = new Files(); var dialogs = new Dialogs(); var slot = TestPath("phase7-recovery.json");
        var state = State(2) with { Document = State(2).Document with { Length = M(1) } };
        files.Data[slot] = ProjectRecovery.Encode(new(state, null, Now));
        files.Data["materials"] = Encoding.UTF8.GetBytes("{"); files.Data["sections"] = Encoding.UTF8.GetBytes("{");
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, recovery: new(files, slot),
            materialStore: new(files, "materials"), sectionStore: new(files, "sections"),
            catalogLoader: () => throw new LibraryFormatException("Injected"));
        await main.InitializeLibrariesAsync(); Assert.True(await main.InitializeRecoveryAsync());
        Assert.True(state.ContentEquals(main.Session!.CurrentRevision.State)); Assert.True(main.Editor!.Presentation.IsSuccess);
        Assert.NotNull(main.BuiltInError); Assert.NotNull(main.MaterialLibraryError); Assert.NotNull(main.SectionLibraryError);
        Assert.Equal("{", Encoding.UTF8.GetString(files.Data["materials"])); Assert.Equal("{", Encoding.UTF8.GetString(files.Data["sections"]));
    }

    [Fact]
    public async Task ManualInputUnitsAreProtectedAndTypeSwitchUsesCurrentDimensionUnits()
    {
        var f = new Fixture(); await f.Load(); var editor = NewSection(f);
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Manual);
        var profile = UnitProfile.Default.WithUnit(QuantityKind.SectionDimension, UnitCatalog.Inch);
        Assert.True(f.Main.SetResultPresentation(profile, PresentationMode.Standard));
        Assert.False(f.Main.SetResultPresentation(profile.WithUnit(QuantityKind.Area, UnitCatalog.SquareInch), PresentationMode.Standard));
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Rectangle);
        Assert.All(editor.Parameters, p => Assert.Equal(UnitCatalog.Inch, p.Unit));
        Assert.False(f.Main.SetResultPresentation(UnitProfile.Default, PresentationMode.Standard));
    }

    [Fact]
    public async Task ManualConfigurationChangesPreserveIncompleteAndHiddenAxisBuffers()
    {
        var f = new Fixture(); await f.Load(); var editor = NewSection(f);
        editor.Type = editor.Types.Single(t => t.Value == SectionEditorType.Manual);
        var area = editor.Parameters.Single(p => p.Key == "A"); area.Text = "unfinished";
        var inertia = editor.Parameters.Single(p => p.Key == "IY"); inertia.Text = "1e";
        editor.Configuration = editor.Configurations.Single(c => c.Value == ManualAxisConfiguration.YZ);
        Assert.Same(area, editor.Parameters.Single(p => p.Key == "A")); Assert.Same(inertia, editor.Parameters.Single(p => p.Key == "IY"));
        editor.Configuration = editor.Configurations.Single(c => c.Value == ManualAxisConfiguration.UV);
        editor.Configuration = editor.Configurations.Single(c => c.Value == ManualAxisConfiguration.Y);
        Assert.Equal("unfinished", area.Text); Assert.Equal("1e", editor.Parameters.Single(p => p.Key == "IY").Text);
        Assert.False(editor.IsValid); Assert.Empty(editor.Axes); Assert.False(await editor.ConfirmAsync());
    }
}
