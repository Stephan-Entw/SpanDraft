using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SegmentReportTests : ProjectReportTestSupport
{
    private static Material Steel(string name, double re, double? density = 7850) => new(name, Pressure.FromPascals(210e9),
        Pressure.FromPascals(re), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null);

    private static EditorDocument Document(bool selfWeight = false) => new(M(3), [M(1), M(2)],
        [new(Steel("Steel S235", 235e6), new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y),
            new(Steel("Aluminium", 120e6), new RectangleSectionGeometry(M(0.1), M(0.1)), SectionAxisDesignation.Y),
            new(Steel("Steel S355", 355e6), new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Z)],
        [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A"), new(Guid.NewGuid(), M(3), SupportType.Roller, "B")],
        [new EditorPointForce(Guid.NewGuid(), M(1), Force.FromNewtons(-2000), "F1")],
        selfWeight: selfWeight ? new(Guid.NewGuid(), true) : null);

    private static ReportDocument Capture(EditorDocument document, out EditorViewModel editor)
    {
        editor = EditorViewModel.ForSession(ProjectSession.Create(new ProjectState(document, new([]))), () => { });
        return ProjectReport.Capture(editor, Now);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ReportDescribesEverySegmentInTheOverviewAndInItsOwnDetailBlock(string culture)
    {
        using var scope = new UiCultureScope(culture);
        using var report = Capture(Document(selfWeight: true), out var editor);
        var engineering = editor.Presentation.Result!.Engineering;

        // The overview is the one of the project overview: heading and complete description, no positions.
        var table = Assert.Single(report.Data.Tables, t => t.Title == Strings.SetupSegments);
        Assert.Equal(0, report.Data.Tables.ToList().IndexOf(table));
        Assert.True(table.StrongFirstLine);
        Assert.Empty(table.Headers);
        Assert.Equal(editor.Overview.Segments.Select(s => s.Title + "\n" + s.Description), table.Rows.Select(r => r[0]));
        Assert.DoesNotContain(report.Data.Tables.SelectMany(t => t.Headers), h => h.PlainText == Strings.SegmentNumber);

        // Every segment, the governing one included, has a detail block with its own section and material.
        Assert.Equal(editor.Overview.Segments.Select(s => (s.Title, s.Section, s.Material)),
            report.Data.Segments.Select(s => (s.Title, s.Section, s.Material)));
        Assert.All(report.Data.Segments, s =>
        {
            Assert.NotEmpty(s.SectionProperties);
            Assert.Equal(2, s.MaterialProperties.Count);
        });
        Assert.Equal(Strings.Section, report.Data.Labels.Section);
        Assert.Equal(Strings.ReportMaterial, report.Data.Labels.Material);

        int governing = engineering.GoverningSegmentIndex;
        Assert.Equal((governing + 1).ToString(), report.Data.Indicators.Single(i => i.Label.PlainText == Strings.ReportGoverningSegment).Value);
        Assert.Equal(editor.Presentation.Stress, report.Data.Indicators.Single(i => i.Label.PlainText.StartsWith(Strings.ReportStress)).Value);
        Assert.NotNull(report.Data.Indicators.Single(i => i.Label.PlainText.StartsWith(Strings.ReportStress)).Position);
        Assert.Contains(report.Data.Labels.Assumptions, a => a.Contains(Strings.ReportSegmentTheory));
        Assert.DoesNotContain(report.Data.Labels.Assumptions, a => a.StartsWith(Strings.ReportTheory));
        // Self-weight appears with one row per segment.
        var loads = Assert.Single(report.Data.Tables, t => t.Title == Strings.DistributedLoads);
        Assert.Equal(3, loads.Rows.Count(r => r[0] == EditorSelfWeight.Symbol));
    }

    [Fact]
    public void ASingleSegmentReportHasTheSameStructureWithoutAGoverningIndicator()
    {
        using var scope = new UiCultureScope("en-US");
        var single = new EditorDocument(M(2), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(2), SupportType.Roller, "B")],
            [new EditorPointForce(Guid.NewGuid(), M(1), Force.FromNewtons(-1000), "F1")]);
        using var report = Capture(single, out _);

        // No special case: the overview and the detail block exist for one segment as for several.
        var table = Assert.Single(report.Data.Tables, t => t.Title == Strings.SetupSegments);
        Assert.Single(table.Rows);
        var segment = Assert.Single(report.Data.Segments);
        Assert.Equal(SegmentLabels.Title(1), segment.Title);
        Assert.Equal(4, report.Data.Indicators.Count);
        Assert.Contains(report.Data.Labels.Assumptions, a => a.StartsWith(Strings.ReportTheory));
        Assert.Null(report.Data.Indicators.Single(i => i.Label.PlainText.StartsWith(Strings.ReportStress)).Position);
        // Details and notes follow the key figures in the normal page flow; nothing forces a second page.
        AssertBlockFlow(report);
        Assert.Single(report.SketchAreas());
    }

    [Fact]
    public void EveryPageKeepsItsTablesInsideTheBodyAndTheBlocksFollowTheTables()
    {
        using var report = Capture(Document(selfWeight: true), out _);

        AssertCompleteTablesAndFinalSummary(report);
        Assert.Equal(3, report.SketchAreas().Count);
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var bitmap = report.RenderPage(i, 96);
            AssertTextContained(report);
            AssertInformationBlocksSeparated(report, i);
        }
    }
}
