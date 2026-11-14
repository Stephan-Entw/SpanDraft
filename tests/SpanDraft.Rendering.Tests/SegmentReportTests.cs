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
    public void ReportListsEverySegmentAndDescribesTheGoverningOne(string culture)
    {
        using var scope = new UiCultureScope(culture);
        using var report = Capture(Document(selfWeight: true), out var editor);
        var engineering = editor.Presentation.Result!.Engineering;

        var table = Assert.Single(report.Data.Tables, t => t.Title == Strings.SetupSegments);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["1", "2", "3"], table.Rows.Select(r => r[0]));
        Assert.Equal(table.Headers.Count, table.Widths.Count);
        Assert.Equal(1f, table.Widths.Sum(), 1e-5);
        Assert.All(table.Rows, r => Assert.Equal(table.Headers.Count, r.Count));
        Assert.Equal(editor.Overview.Segments.Select(s => s.Range), table.Rows.Select(r => r[1]));
        Assert.Equal(editor.Overview.Segments.Select(s => s.Material), table.Rows.Select(r => r[3]));

        int governing = engineering.GoverningSegmentIndex;
        Assert.Equal(editor.Overview.Segments[governing].Section, report.Data.Section);
        Assert.Equal(editor.Overview.Segments[governing].Material, report.Data.Material);
        Assert.Equal(string.Format(Strings.ReportSegmentSection, governing + 1), report.Data.Labels.Section);
        Assert.Equal(string.Format(Strings.ReportSegmentMaterial, governing + 1), report.Data.Labels.Material);
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
    public void ASingleSegmentReportHasNoSegmentTableNoGoverningIndicatorAndTheOriginalLabels()
    {
        using var scope = new UiCultureScope("en-US");
        var single = new EditorDocument(M(2), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(2), SupportType.Roller, "B")],
            [new EditorPointForce(Guid.NewGuid(), M(1), Force.FromNewtons(-1000), "F1")]);
        using var report = Capture(single, out _);

        Assert.DoesNotContain(report.Data.Tables, t => t.Title == Strings.SetupSegments);
        Assert.Equal(4, report.Data.Indicators.Count);
        Assert.Equal(Strings.Section, report.Data.Labels.Section);
        Assert.Equal(Strings.ReportMaterial, report.Data.Labels.Material);
        Assert.Contains(report.Data.Labels.Assumptions, a => a.StartsWith(Strings.ReportTheory));
        Assert.Null(report.Data.Indicators.Single(i => i.Label.PlainText.StartsWith(Strings.ReportStress)).Position);
    }

    [Fact]
    public void EveryPageKeepsItsTablesInsideTheBodyAndTheSegmentTableFollowsItsHeading()
    {
        using var report = Capture(Document(selfWeight: true), out _);

        AssertCompleteTablesAndFinalSummary(report);
        var segments = Array.FindIndex(report.Data.Tables.ToArray(), t => t.Title == Strings.SetupSegments);
        Assert.Contains(report.Pages.SelectMany(p => p.Tables), f => f.Table == segments && f.Rows.Count > 0);
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var bitmap = report.RenderPage(i, 96);
            Assert.True(bitmap.Width > 0);
        }
    }
}
