using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Engineering;
using SpanDraft.Reporting;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class MaterialStrengthTests
{
    private static Material Typed(MaterialStrengthType type) =>
        new("Independent material", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235), type);

    private static ProjectState WithMaterial(Material material)
    {
        var state = ReportTestProjects.Create();
        return state with { Document = state.Document with { Material = material } };
    }

    private static byte[] LegacyProject(int version = 3)
    {
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(WithMaterial(Typed(MaterialStrengthType.Re))))!;
        root["formatVersion"] = version;
        root["document"]!["material"]!.AsObject().Remove("strengthType");
        if (version == 2) root.AsObject().Remove("info");
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    [Fact]
    public void DomainExposesExactlyTwoTypesAndRequiresAnExplicitValidType()
    {
        Assert.Equal(new[] { MaterialStrengthType.Re, MaterialStrengthType.Rp02 }, Enum.GetValues<MaterialStrengthType>());
        Assert.All(typeof(Material).GetConstructors(), c =>
            Assert.Contains(c.GetParameters(), p => p.ParameterType == typeof(MaterialStrengthType) && !p.IsOptional));
        Assert.Throws<ArgumentOutOfRangeException>(() => Typed((MaterialStrengthType)99));
    }

    [Theory]
    [InlineData(MaterialStrengthType.Re, "re")]
    [InlineData(MaterialStrengthType.Rp02, "rp02")]
    public void BothTypesRoundtripWithoutChangingMechanics(MaterialStrengthType type, string wire)
    {
        var material = Typed(type);
        var state = WithMaterial(material);
        var bytes = ProjectFileCodec.Serialize(state);
        Assert.Equal(wire, JsonNode.Parse(bytes)!["document"]!["material"]!["strengthType"]!.GetValue<string>());
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        Assert.Equal(type, loaded.Document.Material.StrengthType);
        var baseline = SolverTestSupport.Solve(WithMaterial(Typed(MaterialStrengthType.Re)).Document.ToBeamModel());
        var actual = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        SolverTestSupport.SameResults(baseline, actual);
        Assert.Equal(BeamEngineeringAnalysis.Analyze(baseline).SafetyFactor, BeamEngineeringAnalysis.Analyze(actual).SafetyFactor);

        foreach (var category in Enum.GetValues<MaterialCategory>())
        {
            var library = new UserMaterialLibrary([LibraryTestSupport.WithMetadata(material, category)]);
            var saved = MaterialLibraryCodec.Serialize(library);
            var entry = Assert.Single(MaterialLibraryCodec.Deserialize(saved).All);
            Assert.Equal(category, entry.Category);
            Assert.Equal(type, entry.StrengthType);
            Assert.Equal(material.YieldStrength, entry.YieldStrength);
            Assert.Equal(saved, MaterialLibraryCodec.Serialize(new([entry])));
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void HistoricalDataDoesNotInferTypeAndRegularWriterRejectsIt(int version)
    {
        var state = ProjectFileCodec.Deserialize(LegacyProject(version));
        Assert.Null(state.Document.Material.StrengthType);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Serialize(state));
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.ToDto(state));
        Assert.Contains(Strings.MaterialStrengthUnspecified, MaterialStrengthDisplay.Summary(state.Document.Material));
        var knownName = JsonNode.Parse(LegacyProject(version))!;
        knownName["document"]!["material"]!["name"] = "S235JR";
        Assert.Null(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(knownName.ToJsonString())).Document.Material.StrengthType);
    }

    [Theory]
    [InlineData("fy")] [InlineData("Re")] [InlineData("ReH")] [InlineData("rp01")] [InlineData("")]
    public void UnknownWireTypesAreRejectedByProjectLibraryAndCatalog(string type)
    {
        var project = JsonNode.Parse(LegacyProject())!;
        project["document"]!["material"]!["strengthType"] = type;
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(project.ToJsonString())));
        var library = LibraryTestSupport.MaterialJson();
        library["entries"]![0]!["material"]!["strengthType"] = type;
        Assert.Throws<LibraryFormatException>(() => MaterialLibraryCodec.Deserialize(LibraryTestSupport.Bytes(library)));
        library["format"] = MaterialCatalogCodec.Format;
        Assert.Throws<LibraryFormatException>(() => MaterialCatalogCodec.Deserialize(LibraryTestSupport.Bytes(library)));
    }

    [Fact]
    public async Task MixedLibraryPreservesOnlyUntouchedLegacyEntriesAndPublishesAtomically()
    {
        var files = new Files();
        files.Data["materials"] = Encoding.UTF8.GetBytes(LibraryTestSupport.MaterialFixture);
        var store = new MaterialLibraryStore(files, "materials");
        var library = await store.LoadAsync();
        var legacy = Assert.Single(library.All);
        Assert.Null(legacy.StrengthType);
        Assert.Throws<ArgumentException>(() => library.Add(LibraryTestSupport.WithMetadata(legacy, MaterialCategory.Other)));
        Assert.Throws<ArgumentException>(() => library.Replace(legacy.Name, legacy));
        library.Add(LibraryTestSupport.WithMetadata(Typed(MaterialStrengthType.Rp02), MaterialCategory.StructuralSteel));
        await store.SaveAsync(library, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync();
        Assert.Null(loaded.All[0].StrengthType);
        Assert.Equal(MaterialStrengthType.Rp02, loaded.All[1].StrengthType);
        var preserved = files.Data["materials"].ToArray();
        var changed = new UserMaterialLibrary([LibraryTestSupport.WithMetadata(legacy, MaterialCategory.Aluminium)]);
        await Assert.ThrowsAsync<LibraryFormatException>(() => store.SaveAsync(changed, TestContext.Current.CancellationToken));
        Assert.Equal(preserved, files.Data["materials"]);
        await Assert.ThrowsAsync<LibraryFormatException>(() => new MaterialLibraryStore(files, "new-library").SaveAsync(new([legacy]), TestContext.Current.CancellationToken));
        Assert.False(files.Data.ContainsKey("new-library"));
        var m = legacy;
        loaded.Replace(m.Name, new Material(m.Name, m.YoungsModulus, m.YieldStrength, MaterialStrengthType.Re, m.Density, m.PoissonRatio, legacy.Category, legacy.MaterialNumber, legacy.OtherStandards));
        files.FailWritePath = "materials";
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(loaded, TestContext.Current.CancellationToken));
        Assert.Equal(preserved, files.Data["materials"]);
        files.FailWritePath = null;
        await store.SaveAsync(loaded, TestContext.Current.CancellationToken);
        Assert.All((await store.LoadAsync()).All, e => Assert.NotNull(e.StrengthType));
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void EditorRequiresChoiceKeepsValueAndCategoryIndependentAndUpdatesAccessibleLabels(string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel();
        main.NewCommand.Execute(null);
        var editor = new MaterialEditorViewModel(main.Setup, null, SetupEditorMode.ProjectDraft);
        editor.Name = "Custom"; editor.Inputs[0].Text = "210"; editor.Inputs[1].Text = "235";
        Assert.Null(editor.StrengthType); Assert.Null(editor.Draft); Assert.False(editor.CanConfirm);
        Assert.Equal(2, editor.StrengthTypes.Count);
        var view = new MaterialEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show();
        try
        {
            var choice = view.FindControl<ComboBox>("MaterialStrengthTypeInput")!;
            Assert.Equal(Strings.MaterialStrengthType, AutomationProperties.GetName(choice));
            Assert.Null(choice.SelectedItem);
            editor.CompleteStrengthTypeInput(); Assert.True(editor.ShowStrengthTypeError);
            foreach (var type in editor.StrengthTypes)
            {
                choice.SelectedItem = type;
                foreach (var category in editor.Categories)
                {
                    editor.Category = category;
                    Assert.Equal(type.Value, editor.Draft!.StrengthType);
                    Assert.Equal(235e6, editor.Draft.YieldStrength.Pascals);
                }
                window.Measure(new Size(1100, 650)); window.Arrange(new Rect(0, 0, 1100, 650)); Dispatcher.UIThread.RunJobs();
                Assert.True(editor.CanConfirm); Assert.False(editor.ShowStrengthTypeError);
                var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.DataContext == editor.Inputs[1]);
                Assert.Equal(MaterialStrengthDisplay.Label(type.Value) + " [MPa]", AutomationProperties.GetName(input));
                var row = new MaterialSelectionItem(editor.Draft!, MaterialSelectionKind.Project);
                Assert.Equal(MaterialStrengthDisplay.Symbol(type.Value) + " 235", row.StrengthSummary);
                Assert.Contains(row.StrengthSummary, row.AccessibleName);
                main.Setup.SelectedMaterial = editor.Draft;
                Assert.Contains(row.StrengthSummary + " MPa", main.Setup.MaterialSummary);
            }
            editor.Inputs[1].Text = "0"; Assert.False(editor.CanConfirm);
            editor.Inputs[1].Text = "235";
            editor.StrengthType = new((MaterialStrengthType)999, "invalid"); Assert.False(editor.CanConfirm);
        }
        finally { window.Close(); }
    }

    // Recovery completes on a worker; keep Avalonia access on its owning test thread.
#pragma warning disable xUnit1031
    [Fact]
    public void LegacyProjectRequiresConfirmationBeforeSaveAndReportButRetainsRecoveryAndHistory()
    {
        using var environment = new DesktopControlEnvironment();
        var app = new App(create: false);
        var path = TestPath("legacy-strength.spandraft");
        var original = LegacyProject(); app.Files.Data[path] = original;
        Assert.True(app.Main.OpenAsync(path).GetAwaiter().GetResult());
        Assert.False(app.Main.Session!.IsDirty);
        Assert.False(app.Main.SaveAsync().GetAwaiter().GetResult()); Assert.False(app.Main.SaveAsync(saveAs: true).GetAwaiter().GetResult());
        Assert.Equal(0, app.Dialogs.SaveQuestions); Assert.Empty(app.Files.Writes);
        Assert.False(app.Main.ReportAsync().GetAwaiter().GetResult());
        Assert.Throws<InvalidOperationException>(() => ProjectReport.Capture(app.Main.Editor!));
        Assert.All(app.Dialogs.Errors, message => Assert.Contains(Strings.MaterialStrengthResolve, message));
        app.ChangeLength(5);
        app.Delay.ReleaseAll(); app.Recovery.DrainAsync().GetAwaiter().GetResult();
        var recovered = (app.Recovery.ReadAsync().GetAwaiter().GetResult())!;
        Assert.Null(recovered.State.Document.Material.StrengthType);
        Assert.Equal(5, recovered.State.Document.Length.Meters);
        var calls = app.Analyses;
        app.Main.EditProject(); app.Main.Setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(app.Main.Setup.CurrentStep);
        Assert.Contains(Strings.MaterialStrengthUnspecified, selection.CurrentEntry!.StrengthSummary);
        selection.UseSelected();
        var editor = selection.Editor!;
        Assert.Null(editor.StrengthType); Assert.False(editor.CanConfirm);
        editor.StrengthType = editor.StrengthTypes[1];
        Assert.True(editor.ConfirmAsync().GetAwaiter().GetResult()); selection.UseSelected(); Assert.True(app.Main.Setup.ConfirmAsync().GetAwaiter().GetResult());
        Assert.Equal(calls, app.Analyses);
        Assert.True(app.Main.Session.IsDirty);
        Assert.True(app.Main.Undo()); Assert.Null(app.Main.Editor!.Document.Material.StrengthType);
        Assert.False(app.Main.SaveAsync().GetAwaiter().GetResult());
        Assert.True(app.Main.Redo()); Assert.Equal(MaterialStrengthType.Rp02, app.Main.Editor.Document.Material.StrengthType);
        Assert.Equal(calls, app.Analyses);
        Assert.True(app.Main.SaveAsync().GetAwaiter().GetResult());
        Assert.Equal(MaterialStrengthType.Rp02, ProjectFileCodec.Deserialize(app.Files.Data[path]).Document.Material.StrengthType);
        Assert.NotEqual(original, app.Files.Data[path]);
        using var report = ProjectReport.Capture(app.Main.Editor);
        Assert.Contains("Rp0,2", report.Data.Indicators[3].Label.PlainText);
    }

#pragma warning restore xUnit1031

    [Theory]
    [InlineData(MaterialStrengthType.Re)] [InlineData(MaterialStrengthType.Rp02)]
    public async Task ProjectAndSetupRecoveryPreserveBothTypesAndSetupAlsoPreservesLegacy(MaterialStrengthType type)
    {
        var files = new Files();
        foreach (var state in new[] { WithMaterial(Typed(type)), ProjectFileCodec.Deserialize(LegacyProject()) })
        {
            var recovery = new ProjectRecovery(files, "project-recovery");
            await recovery.FlushAsync(state, null);
            Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
            var draft = new ProjectSetupDraft(Material: state.Document.Material);
            files.Data["setup-recovery"] = ProjectSetupRecovery.Encode(new(draft, Now));
            var setup = await new ProjectSetupRecovery(files, "setup-recovery").ReadAsync();
            Assert.True(draft.ContentEquals(setup!.Draft));
        }
    }

    [Theory]
    [InlineData(MaterialStrengthType.Re, "Re", "de-DE")]
    [InlineData(MaterialStrengthType.Rp02, "Rp0,2", "de-DE")]
    [InlineData(MaterialStrengthType.Re, "Re", "en-US")]
    [InlineData(MaterialStrengthType.Rp02, "Rp0,2", "en-US")]
    public async Task ReportUsesStoredTypeInMaterialFormulaAndRenderedPdf(MaterialStrengthType type, string symbol, string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var files = new Files(); var path = TestPath("typed-report.spandraft");
        files.Data[path] = ProjectFileCodec.Serialize(WithMaterial(Typed(type)));
        var main = new MainWindowViewModel(files: files);
        Assert.True(await main.OpenAsync(path));
        using var report = ProjectReport.Capture(main.Editor!);
        Assert.Contains(report.Data.MaterialProperties, p => p.PlainText.StartsWith(MaterialStrengthDisplay.Label(type) + " = ", StringComparison.Ordinal));
        Assert.Equal(Strings.ReportSafety + " S = " + symbol + " / σb,max", report.Data.Indicators[3].Label.PlainText);
        Assert.Equal(new[] { Strings.ReportTheory }, report.Data.Labels.Assumptions);
        using var pdf = new MemoryStream(); report.WritePdf(pdf);
        Assert.Contains(report.TextRuns, r => r.Text.Contains(symbol, StringComparison.Ordinal));
        Assert.DoesNotContain(report.TextRuns, r => r.Text.Contains("fy", StringComparison.Ordinal));
        Assert.All(report.TextRuns, run =>
        {
            Assert.True(run.Bounds.Left >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Right <= ReportDocument.PageWidth - ReportDocument.Margin + .1f, run.Text);
            Assert.True(run.Bounds.Top >= ReportDocument.Margin - .1f, run.Text);
            Assert.True(run.Bounds.Bottom <= ReportDocument.PageHeight - ReportDocument.Margin + 1, run.Text);
        });
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.ToArray(), 0, 5));
    }
}
