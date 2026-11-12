using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using Xunit;

// Control rendering must stay on the owning Avalonia dispatcher thread.
#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ReportCaptureTests : ProjectReportTestSupport
{

    [Theory]
    [InlineData("de-DE", "en-US")] [InlineData("en-US", "de-DE")]
    public void MaterialNotesUseReportLanguageFreezeAndWrapBelowProperties(string ui, string numbers)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(numbers);
        var state = ReportTestProjects.Create();
        var note = new SpanDraft.Core.Materials.MaterialNote("Reference value for a test specimen.\nSecond line.",
            [new("de", "Referenzwert für einen Probekörper.\nZweite Zeile.")]);
        state = state with { Document = state.Document with { Material = MaterialNoteTestSupport.WithNote(state.Document.Material, note) } };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var expected = Strings.MaterialNote + ": " + MaterialNoteDisplay.Resolve(note, CultureInfo.CurrentUICulture);
        Assert.Equal(expected, report.Data.MaterialNote);
        using var without = new ReportDocument(report.Data with { MaterialNote = null }, new(130, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0);
        var properties = report.TextRuns.Where(r => report.Data.MaterialProperties.Any(p => r.Text.Contains(p.PlainText))).ToArray();
        Assert.Contains(report.TextRuns, r => r.Text.StartsWith(Strings.MaterialNote + ":", StringComparison.Ordinal));
        Assert.Contains(report.TextRuns, r => r.Text == (ui == "de-DE" ? "Zweite Zeile." : "Second line."));
        var noteStart = report.TextRuns.Single(r => r.Text.StartsWith(Strings.MaterialNote + ":", StringComparison.Ordinal));
        Assert.True(noteStart.Bounds.Top > properties.Max(p => p.Bounds.Bottom));
        AssertTextContained(report);
        Assert.True(report.Pages[0].Tables.First().Bounds.Top > without.Pages[0].Tables.First().Bounds.Top);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui == "de-DE" ? "en-US" : "de-DE");
        Assert.Equal(expected, report.Data.MaterialNote);
        var clean = state with { Document = state.Document with { Material = MaterialNoteTestSupport.WithNote(state.Document.Material, null) } };
        var (cleanMain, _) = Open(clean); using var cleanReport = ProjectReport.Capture(cleanMain.Editor!, Now);
        Assert.Null(cleanReport.Data.MaterialNote);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void MaterialMetadataUpdatesUseTheCurrentSnapshotWithoutReanalysis(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var initial = ReportTestProjects.Create();
        var originalMaterial = LibraryTestSupport.WithMetadata(initial.Document.Material, initial.Document.Material.Category, "1.0038", []);
        initial = initial with { Document = initial.Document with { Material = originalMaterial } };
        int analyses = 0;
        var (main, files) = Open(initial, analyzed: () => analyses++);
        var editor = main.Editor!;
        var analysis = editor.Presentation;
        using var original = ProjectReport.Capture(editor, Now);
        Assert.Equal("S235JR (1.0038)", original.Data.Material);
        var updated = new SpanDraft.Core.Materials.Material("Project steel", originalMaterial.YoungsModulus,
            originalMaterial.YieldStrength, originalMaterial.StrengthType!.Value, originalMaterial.Density,
            originalMaterial.PoissonRatio, originalMaterial.Category, "9.8765", ["Standard B", "Standard A"]);
        editor.ApplySetup(editor.Document.Section, editor.Document.BendingAxis, updated);
        AssertCurrent("Project steel (9.8765) · Standard B · Standard A");
        Assert.True(editor.Undo()); AssertCurrent("S235JR (1.0038)");
        Assert.True(editor.Redo()); AssertCurrent("Project steel (9.8765) · Standard B · Standard A");
        Assert.Equal("S235JR (1.0038)", original.Data.Material);
        Assert.Equal(1, analyses); Assert.Empty(files.Writes);

        void AssertCurrent(string expected)
        {
            using var report = ProjectReport.Capture(editor, Now);
            Assert.Equal(expected, report.Data.Material);
            Assert.Equal(expected, editor.Overview.Material);
            Assert.Same(analysis, editor.Presentation);
            Assert.Equal(original.Data.Indicators.Select(i => (i.Label.PlainText, i.Value, i.Position)),
                report.Data.Indicators.Select(i => (i.Label.PlainText, i.Value, i.Position)));
            using var bitmap = report.RenderPage(0, 96);
            Assert.Equal(expected, string.Join(" · ", MaterialDesignationRuns(report).Select(r => r.Text)));
            AssertTextContained(report);
        }
    }

    [Theory]
    [InlineData("de-DE", false)] [InlineData("en-US", false)]
    [InlineData("de-DE", true)] [InlineData("en-US", true)]
    public void ReportAndUiSharePrecisionNotationAndSemanticMaterialUnits(string culture, bool engineering)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create();
        var original = state.Document.Loads[0];
        state = state with { Document = state.Document.WithLoads([
            new EditorPointForce(original.Id, Length.FromMeters(.8123456789), Force.FromNewtons(-12345.6789), original.Name)]) };
        var options = new ResultPresentationOptions(UnitProfile.UnitedStates, 5,
            engineering ? EngineeringNotation.Automatic : EngineeringNotation.Off);
        var (main, _) = Open(state, options);
        var editor = main.Editor!;
        var bytes = ProjectFileCodec.Serialize(editor.Session.CurrentRevision.State);
        using var report = ProjectReport.Capture(editor, Now);
        var material = state.Document.Material;
        // Material values and load positions are given: the same precision and notation apply, marked when rounded.
        Assert.Contains(report.Data.MaterialProperties, p => p.PlainText.EndsWith(
            QuantityFormatter.FormatInput(material.YoungsModulus.Pascals, options.Profile[QuantityKind.YoungsModulus], options), StringComparison.Ordinal));
        Assert.Contains(report.Data.MaterialProperties, p => p.PlainText.EndsWith(
            QuantityFormatter.FormatInput(material.YieldStrength.Pascals, options.Profile[QuantityKind.MaterialStrength], options), StringComparison.Ordinal));
        Assert.DoesNotContain(report.Data.MaterialProperties, p => p.PlainText.Contains("= ≈", StringComparison.Ordinal));
        var forces = report.Data.Tables.Single(t => t.Title == Strings.Forces);
        Assert.Equal(editor.Overview.Forces[0].Value, forces.Rows[0][1]);
        Assert.Equal(editor.Overview.Forces[0].Position, forces.Rows[0][2]);
        Assert.Equal(QuantityFormatter.FormatInputNumber(.8123456789,
            options.Profile[QuantityKind.BeamLength], options), forces.Rows[0][2]);
        Assert.Equal(editor.Overview.Reactions.Select(r => r.Ry), report.Data.Tables.Single(t => t.Title == Strings.Reactions).Rows.Select(r => r[2]));
        Assert.Equal(editor.Presentation.Displacement, report.Data.Indicators[0].Value);
        Assert.Equal(editor.Presentation.Moment, report.Data.Indicators[1].Value);
        for (int i = 0; i < report.Pages.Count; i++) { using var bitmap = report.RenderPage(i); AssertTextContained(report); }
        using var pdf = new MemoryStream(); report.WritePdf(pdf); Assert.True(pdf.Length > 0);
        Assert.Equal(bytes, ProjectFileCodec.Serialize(editor.Session.CurrentRevision.State));
    }

    [Fact]
    public void ReportKeepsAllWholeUnitDigitsOfAGivenLoadPosition()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        var state = ReportTestProjects.Create();
        var original = state.Document.Loads[0];
        state = state with { Document = state.Document.WithLoads([
            new EditorPointForce(original.Id, Length.FromMillimeters(1234), Force.FromNewtons(-1000), original.Name)]) };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var force = report.Data.Tables.Single(t => t.Title == Strings.Forces).Rows[0];
        Assert.Equal("1\u202f234", force[2]);
        Assert.Equal("-1\u202f000 N", force[1]);
        for (int i = 0; i < report.Pages.Count; i++) { using var bitmap = report.RenderPage(i); AssertTextContained(report); }
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void PdfWrappingPreservesWholeGroupedNumbersInsideNarrowTableCells(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var (main, _) = Open(ReportTestProjects.Create());
        using var captured = ProjectReport.Capture(main.Editor!, Now);
        string number = QuantityFormatter.FormatResultNumber(123456789012345d, QuantityKind.PoissonRatio,
            significantDigits: 5, notation: EngineeringNotation.Off);
        var table = new ReportTable("Grouped numbers", ["Name", "Value"], [.8f, .2f],
            [new[] { "Group", number }]);
        using var report = new ReportDocument(captured.Data with { Tables = [table] }, new(80, (_, _, _) => { }));
        using var bitmap = report.RenderPage(0);
        var run = Assert.Single(report.TextRuns, r => r.Text == number);
        var fragment = Assert.Single(report.Pages[0].Tables);
        Assert.True(run.Bounds.Left >= fragment.Bounds.Left + fragment.Bounds.Width * .8f - .1f);
        Assert.True(run.Bounds.Right <= fragment.Bounds.Right + .1f);
        using var pdf = new MemoryStream(); report.WritePdf(pdf); Assert.True(pdf.Length > 0);
    }

    [Theory]
    [InlineData("de-DE", "de-DE", 0, false)]
    [InlineData("en-US", "en-US", 1, false)]
    [InlineData("en-US", "de-DE", 2, true)]
    [InlineData("de-DE", "en-US", 3, true)]
    public void CapturesExistingResultsUnitsCultureAndConfirmedStateWithoutAnalysisOrMutation(string ui, string culture, int units, bool detailed)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var profile = units switch
        {
            0 => UnitProfile.MechanicalEngineering, 1 => UnitProfile.StructuralEngineering,
            2 => UnitProfile.UnitedStates, _ => UnitProfile.MechanicalEngineering.WithUnit(QuantityKind.BeamLength, UnitCatalog.Meter)
        };
        int analyses = 0;
        var (main, files) = Open(ReportTestProjects.Create(), new(profile, detailed ? 5 : 3), () => analyses++);
        var session = main.Session!; var revision = session.CurrentRevision; var original = files.Data[session.FilePath!].ToArray();
        var editor = main.Editor!;
        editor.PreviewAnnotationOffset(editor.Document.Loads[0].Id, new(99, 88));
        using var report = ProjectReport.Capture(editor, Now);
        Assert.Equal(1, analyses);
        Assert.Equal(editor.Presentation.Displacement, report.Data.Indicators[0].Value);
        Assert.Equal(editor.Presentation.Moment, report.Data.Indicators[1].Value);
        Assert.Equal(editor.Presentation.Stress, report.Data.Indicators[2].Value);
        Assert.Equal(editor.Presentation.SafetyFactor, report.Data.Indicators[3].Value);
        Assert.Equal(Now, report.Data.CreatedAt);
        Assert.Equal(Now.ToLocalTime().ToString("d", CultureInfo.CurrentCulture), report.Data.Date);
        Assert.Contains(profile[QuantityKind.BeamLength].Symbol, report.Data.Tables.Single(t => t.Title == Strings.Supports).Headers[2].PlainText);
        Assert.Contains(profile[QuantityKind.TransverseForce].Symbol, report.Data.Tables.Single(t => t.Title == Strings.Reactions).Headers[2].PlainText);
        using var before = new MemoryStream(); report.WritePng(before, 0);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture == "de-DE" ? "en-US" : "de-DE");
        Assert.True(main.SetResultPresentation(UnitProfile.UnitedStates, 5));
        using var after = new MemoryStream(); report.WritePng(after, 0);
        Assert.Equal(before.ToArray(), after.ToArray());
        using var backgroundPdf = new MemoryStream();
        Task.Run(() => report.WritePdf(backgroundPdf), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Assert.True(backgroundPdf.Length > 0);
        Assert.Same(revision, session.CurrentRevision); Assert.False(session.IsDirty); Assert.False(session.CanUndo);
        Assert.False(session.CanRedo); Assert.Empty(files.Writes); Assert.Equal(original, files.Data[session.FilePath!]); Assert.Equal(1, analyses);
    }

    [Fact]
    public void StandardProjectRetainsBalancedReactionsAndEngineeringReferenceValues()
    {
        var (main, _) = Open(ReportTestProjects.Create());
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var result = main.Editor!.Presentation.Result!;
        SolverTestSupport.ForceClose(1000, result.Solution.Nodes[0].ReactionY);
        SolverTestSupport.ForceClose(800, result.Solution.Nodes[^1].ReactionY);
        SolverTestSupport.Close(710, result.Engineering.BendingMomentMagnitude.NewtonMeters, 1e-7);
        Assert.Equal(new[] { "0,785 mm", "710 N·m", "22,8 MPa", "10,3" }, report.Data.Indicators.Select(i => i.Value));
    }

    [Theory]
    [InlineData("de-DE", 0)] [InlineData("en-US", 0)]
    [InlineData("de-DE", 1)] [InlineData("en-US", 1)]
    [InlineData("de-DE", 2)] [InlineData("en-US", 2)]
    [InlineData("de-DE", 3)] [InlineData("en-US", 3)]
    [InlineData("de-DE", 4)] [InlineData("en-US", 4)]
    public void CaptureOmitsEmptyLoadGroupsAndPreservesGroupOrder(string culture, int group)
    {
        using var scope = new UiCultureScope(culture);
        var state = ReportTestProjects.Create(true);
        var document = state.Document;
        document = document.WithLoads(group == 4 ? document.Loads : document.Loads.Where(l =>
            group == 1 && l.Kind == PointLoadKind.Force || group == 2 && l.Kind == PointLoadKind.Moment).Take(1));
        document = document.WithDistributedLoads(group is 3 or 4 ? document.DistributedLoads.Take(1) : []);
        state = state with { Document = document, Presentation = state.Presentation.RetainEntities(document) };
        var (main, _) = Open(state);
        using var report = ProjectReport.Capture(main.Editor!, Now);
        var expected = new List<string> { Strings.Supports };
        if (group is 1 or 4) expected.Add(Strings.Forces);
        if (group is 2 or 4) expected.Add(Strings.Moments);
        if (group is 3 or 4) expected.Add(Strings.DistributedLoads);
        expected.Add(Strings.Reactions);
        Assert.Equal(expected, report.Data.Tables.Select(t => t.Title));
        Assert.All(report.Data.Tables, t => Assert.NotEmpty(t.Rows));
        Assert.Equal(culture == "de-DE" ? "Kr\u00e4fte" : "Forces", Strings.Forces);
        Assert.Equal(culture == "de-DE" ? "Momente" : "Moments", Strings.Moments);
        Assert.Equal(culture == "de-DE" ? "Streckenlasten" : "Distributed loads", Strings.DistributedLoads);
    }

    [Theory]
    [InlineData(null, "T-Profil 100 × 80 × 8 × 10 mm")]
    [InlineData("Mein T-Profil", "Mein T-Profil")]
    public void CapturedSectionNameMatchesOverviewWithoutAxisSuffix(string? displayName, string expected)
    {
        var state = ReportTestProjects.Create(); var mm = Length.FromMillimeters;
        state = state with { Document = state.Document.WithSection(
            new TSectionGeometry(mm(100), mm(80), mm(8), mm(10), mm(0)), SectionAxisDesignation.Y, displayName) };
        var (main, _) = Open(state);
        var editor = main.Editor!;
        Assert.Equal(displayName, editor.Document.SectionDisplayName);

        using var report = ProjectReport.Capture(editor, Now);

        Assert.Equal(expected, report.Data.Section);
        Assert.Equal(editor.Overview.Section, report.Data.Section);
        Assert.Equal(displayName, editor.Document.SectionDisplayName);
    }

    [Fact]
    public void AllSectionFormsAndAxesUseAuthoritativeProperties()
    {
        var fixture = ReportTestProjects.Create(); var mm = Length.FromMillimeters;
        ISectionDefinition[] sections = [new RectangleSectionGeometry(mm(60), mm(100)),
            new RectangularHollowSectionGeometry(mm(60), mm(100), mm(4), mm(6)), new CircleSectionGeometry(mm(50)),
            new CircularHollowSectionGeometry(mm(60), mm(4)), new ISectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)),
            new USectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)), new TSectionGeometry(mm(100), mm(60), mm(4), mm(6), mm(3)),
            new AngleSectionGeometry(mm(60), mm(100), mm(6), mm(3)),
            new ManualSectionDefinition(Area.FromSquareMeters(.001), new(SectionAxisDesignation.U, SecondMomentOfArea.FromMetersToTheFourth(1e-6), SectionModulus.FromCubicMeters(2e-5)))];
        foreach (var section in sections)
        foreach (var axis in section.Axes)
        {
            var state = fixture with { Document = fixture.Document.WithSection(section, axis.AxisDesignation) };
            var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
            Assert.Equal(main.Editor!.Overview.Section, report.Data.Section);
            Assert.DoesNotMatch(@" · [yzuv]-[yzuv]$", report.Data.Section);
            Assert.Contains(report.Data.SectionProperties, p => p.PlainText.StartsWith("I" + axis.AxisDesignation.ToString().ToLowerInvariant() + " = "));
            bool sameDisplay = SectionDisplay.Property(section, axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus,
                main.Editor!.ResultPresentation) ==
                SectionDisplay.Property(section, axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus,
                main.Editor.ResultPresentation);
            Assert.Equal(sameDisplay ? 2 : 3, report.Data.SectionProperties.Count);
            using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
        }
    }

    [Fact]
    public void SupportsSharingAStationReportOneCommonReactionRow()
    {
        // Project files and the editor keep support positions unique; only a programmatic document reaches
        // this state. The report must still show the station's common reaction once, never once per support.
        var state = ReportTestProjects.Create();
        var supports = state.Document.Supports;
        state = state with { Document = state.Document.WithSupports([
            supports[0], new(Guid.NewGuid(), supports[0].Position, SupportType.Fixed, "Clamp"), supports[1]]) };
        var editor = EditorViewModel.ForSession(ProjectSession.Create(state), () => { });
        using var report = ProjectReport.Capture(editor, Now);
        var solution = editor.Presentation.Result!.Solution;
        var node = solution.Nodes.Single(n => n.Position == supports[0].Position);
        Assert.Equal(3, report.Data.Tables.Single(t => t.Title == Strings.Supports).Rows.Count);
        var reactions = report.Data.Tables.Single(t => t.Title == Strings.Reactions).Rows;
        Assert.Equal(new[] { supports[0].Name + " + Clamp", supports[1].Name }, reactions.Select(r => r[0]));
        string Number(double value, QuantityKind kind) => QuantityFormatter.FormatResultNumber(value, kind, editor.ResultPresentation);
        Assert.Equal(new[] { Number(node.ReactionX!.Value.Newtons, QuantityKind.AxialForce),
            Number(node.ReactionY!.Value.Newtons, QuantityKind.TransverseForce),
            Number(node.ReactionMoment!.Value.NewtonMeters, QuantityKind.Moment) }, reactions[0].Skip(1));
        // Summing the reported rows reproduces the applied load; nothing is counted twice.
        double applied = state.Document.Loads.Sum(l => l.Value) + state.Document.DistributedLoads.Sum(q =>
            q.Intensity.NewtonsPerMeter * (q.EndPosition.Meters - q.StartPosition.Meters));
        Assert.Equal(-applied, solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0), 6);
        Assert.Equal(editor.Overview.Reactions.Select(r => r.Ry), reactions.Select(r => r[2]));
        using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
        using var pdf = new MemoryStream(); report.WritePdf(pdf); Assert.True(pdf.Length > 0);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public void LegacyAndUnloadedProjectsNeedNoMetadataOrInventedResults(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var state = ReportTestProjects.Create(); state = state with { Info = null, Document = state.Document.WithLoads([]).WithDistributedLoads([]) };
        state = state with { Presentation = state.Presentation.RetainEntities(state.Document) };
        var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.Empty(report.Data.Metadata); Assert.Empty(report.Data.Author);
        Assert.Contains("∞", report.Data.Indicators[3].Value);
        Assert.Equal(new[] { Strings.Supports, Strings.Reactions }, report.Data.Tables.Select(t => t.Title));
        using var bitmap = report.RenderPage(0, 96); AssertTextContained(report);
        Assert.DoesNotContain(report.TextRuns, r => r.Text == report.Data.Labels.Author);
        string[] labels = [report.Data.Labels.Project, report.Data.Labels.Reference];
        var columns = labels.Select(label => Assert.Single(report.TextRuns, r => r.Text == label).Bounds).ToArray();
        Assert.All(columns, bounds => Assert.Equal(columns[0].Top, bounds.Top));
        Assert.All(columns, bounds => Assert.True(bounds.Bottom < report.Pages[0].BodyTop - 12));
        Assert.All(columns, column => Assert.Single(report.TextRuns, r => r.Text == "-"
            && r.Bounds.Left == column.Left && r.Bounds.Top > column.Bottom
            && r.Bounds.Bottom < report.Pages[0].BodyTop - 12));
    }

    [Theory]
    [InlineData(80)] [InlineData(150)]
    public void RealProjectWithManyLoadsCanProduceMoreThanTwoPages(int count)
    {
        var state = ReportTestProjects.Create(true);
        var loads = Enumerable.Range(0, count).Select(i => new EditorPointForce(Guid.NewGuid(),
            Length.FromMeters(.05 + i * 6.9 / (count - 1)), Force.FromNewtons(-100), "F" + (i + 1)));
        state = state with { Document = state.Document.WithLoads(loads), Presentation = new() };
        var (main, _) = Open(state); using var report = ProjectReport.Capture(main.Editor!, Now);
        Assert.True(report.Pages.Count > 2);
        int forceTable = report.Data.Tables.ToList().FindIndex(t => t.Title == Strings.Forces);
        Assert.Equal(count, report.Data.Tables[forceTable].Rows.Count);
        Assert.Equal(count, report.Pages.SelectMany(p => p.Tables).Where(t => t.Table == forceTable).Sum(t => t.Rows.Count));
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
        using (var pdf = File.Create(Path.Combine(directory, $"viele-lasten-{count}.pdf"))) report.WritePdf(pdf);
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var png = File.Create(Path.Combine(directory, $"viele-lasten-{count}-{i + 1}.png")); report.WritePng(png, i);
            AssertTextContained(report);
        }
    }
}
