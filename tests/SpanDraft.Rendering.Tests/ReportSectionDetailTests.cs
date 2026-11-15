using System.Globalization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>The report's page flow and its detail block per segment, for one segment as for many.</summary>
[Collection("Avalonia")]
public sealed class ReportSectionDetailTests : ProjectReportTestSupport
{
    private static readonly Func<double, Length> Mm = Length.FromMillimeters;

    private static Material Steel(string name, double re) => new(name, Pressure.FromPascals(210e9), Pressure.FromPascals(re),
        MaterialStrengthType.Re);

    /// <summary>Segments cycling through symmetric and unsymmetric sections and two materials; optionally with long names.</summary>
    private static EditorDocument Document(int count, bool longNames = false)
    {
        EditorSegment Segment(int i)
        {
            ISectionDefinition section = (i % 3) switch
            {
                0 => new ISectionGeometry(Mm(100), Mm(100), Mm(12), Mm(10), Mm(5)),
                1 => new TSectionGeometry(Mm(100), Mm(80), Mm(8), Mm(10), Mm(5)),
                _ => new RectangularHollowSectionGeometry(Mm(60), Mm(100), Mm(4), Mm(6))
            };
            string? display = longNames ? string.Join(" ", Enumerable.Repeat("Very long custom library name " + i, 12)) : null;
            return new(Steel(longNames ? string.Join(" ", Enumerable.Repeat("Alloy designation", 14)) + i : i % 2 == 0 ? "S235JR (1.0038)" : "S355J2 (1.0577)",
                i % 2 == 0 ? 235e6 : 355e6), section, SectionAxisDesignation.Y, display);
        }
        return new EditorDocument(M(count), Enumerable.Range(1, count - 1).Select(i => M(i)),
            Enumerable.Range(0, count).Select(Segment).ToArray(),
            [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A"), new(Guid.NewGuid(), M(count), SupportType.Roller, "B")],
            [new EditorPointForce(Guid.NewGuid(), M(.5), Force.FromNewtons(-2000), "F1")]);
    }

    private static ReportDocument Capture(EditorDocument document, out EditorViewModel editor, out ProjectState state)
    {
        state = new ProjectState(document, new([]));
        editor = EditorViewModel.ForSession(ProjectSession.Create(state), () => { });
        return ProjectReport.Capture(editor, Now);
    }

    private static void Export(ReportDocument report, string name)
    {
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
        using (var pdf = File.Create(Path.Combine(directory, name + ".pdf"))) report.WritePdf(pdf);
        for (int i = 0; i < report.Pages.Count; i++)
        { using var png = File.Create(Path.Combine(directory, $"{name}-{i + 1}.png")); report.WritePng(png, i); }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(6)] [InlineData(12)]
    public void EverySegmentHasItsOwnDetailBlockInOrderOnItsOwnPageWithoutOverlap(int count)
    {
        using var report = Capture(Document(count), out var editor, out _);

        AssertCompleteTablesAndFinalSummary(report);
        AssertBlockFlow(report);
        Assert.Equal(count, report.Data.Segments.Count);
        Assert.Equal(count, report.SketchAreas().Count);
        // The overview and the blocks name the segments alike and describe each with its own section and material.
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(editor.Overview.Segments[i].Title, report.Data.Segments[i].Title);
            Assert.Equal(editor.Overview.Segments[i].Section, report.Data.Segments[i].Section);
            Assert.Equal(editor.Overview.Segments[i].Material, report.Data.Segments[i].Material);
        }
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96);
            AssertTextContained(report);
            AssertInformationBlocksSeparated(report, page);
        }
        // A block is never split: it lies completely on one page, below the key figures and before the notes.
        var order = report.Pages.SelectMany((p, i) => p.Blocks.Select(b => (Page: i, b.Kind))).ToArray();
        Assert.Equal(ReportBlockKind.Summary, order[0].Kind);
        Assert.Equal(ReportBlockKind.Notes, order[^1].Kind);
        if (count >= 6) Assert.True(report.Pages.Count > 1);
        Export(report, "abschnitte-" + count);
    }

    [Fact]
    public void AnUnsymmetricSectionShowsPositiveAndNegativeModuliAndASymmetricOneASingleModulus()
    {
        using var report = Capture(Document(3), out _, out _);
        // Segment 1 is the I section, segment 2 the T section, segment 3 the hollow section.
        Assert.Equal(2, report.Data.Segments[0].SectionProperties.Count);
        Assert.Equal(3, report.Data.Segments[1].SectionProperties.Count);
        Assert.Contains(report.Data.Segments[1].SectionProperties, t => t.Runs.Any(r => r.Position == TechnicalPosition.Superscript && r.Text == "+"));
        Assert.Contains(report.Data.Segments[1].SectionProperties, t => t.Runs.Any(r => r.Position == TechnicalPosition.Superscript && r.Text == "−"));
        Assert.DoesNotContain(report.Data.Segments[0].SectionProperties, t => t.Runs.Any(r => r.Position == TechnicalPosition.Superscript));
        Assert.All(report.Data.Segments, s => Assert.Equal(2, s.MaterialProperties.Count));
        for (int page = 0; page < report.Pages.Count; page++)
        { using var bitmap = report.RenderPage(page, 96); AssertInformationBlocksSeparated(report, page); }
    }

    [Fact]
    public void LongSectionAndMaterialNamesWrapCompletelyInOverviewAndBlocks()
    {
        using var report = Capture(Document(3, longNames: true), out _, out _);

        AssertBlockFlow(report);
        var overview = report.Data.Tables[0];
        Assert.True(overview.StrongFirstLine);
        for (int i = 0; i < 3; i++)
        {
            Assert.EndsWith(report.Data.Segments[i].Material, overview.Rows[i][0]);
            Assert.Contains(report.Data.Segments[i].Section, overview.Rows[i][0]);
        }
        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var bitmap = report.RenderPage(page, 96);
            AssertTextContained(report);
            AssertInformationBlocksSeparated(report, page);
            // The overview is complete: every row's text is found again, wrapped but neither shortened nor with an ellipsis.
            if (!report.Pages[page].HasGraphics) continue;
            Assert.DoesNotContain(report.TextRuns, r => r.Text.Contains('…'));
        }
        Export(report, "abschnitte-lange-namen");
    }

    [Fact]
    public void OverviewTableHasNoHeaderAndNoPositionColumnAndItsHeadingsAreStrong()
    {
        using var report = Capture(Document(2), out var editor, out _);
        var table = report.Data.Tables[0];
        Assert.Equal(SpanDraft.Desktop.Resources.Strings.SetupSegments, table.Title);
        Assert.Empty(table.Headers);
        Assert.Equal(1, table.Widths.Count);
        Assert.Equal(editor.Overview.Segments.Select(s => s.Title + "\n" + s.Description), table.Rows.Select(r => r[0]));
        using var bitmap = report.RenderPage(0, 96);
        var heading = Assert.Single(report.TextRuns, r => r.Text == editor.Overview.Segments[0].Title && r.Bounds.Left > ReportDocument.Margin + ReportDocument.LeftWidth);
        Assert.True(heading.Bounds.Top > report.Pages[0].BodyTop - 20);
    }

    [Fact]
    public void TheDiagramsStartAtTheirCompactHeightAndShareOnlyTheRoomTheRightColumnNeedsBeyondIt()
    {
        const float minimum = 60;
        var source = SyntheticReportData.Create();
        ReportTable Table(int rows) => new("Forces", ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { "F" + i }).ToArray());
        // A short right column: the left column keeps its compact height, every diagram has exactly the minimum.
        using var compact = new ReportDocument(source with { Tables = [Table(2)] }, new(130, minimum, (_, _, _, _) => { }));
        var areas = compact.TechnicalAreas(0);
        Assert.All(areas.Skip(1), a => Assert.Equal(minimum, a.Bounds.Height, 2));
        Assert.Equal(areas[0].Bounds.Top + 130 + 4 + 3 * minimum, compact.Pages[0].UpperBottom, 2);

        // A tall right column: the extra room is shared equally and the left column ends where the right one does.
        using var tall = new ReportDocument(source with { Tables = [Table(25)] }, new(130, minimum, (_, _, _, _) => { }));
        var tallAreas = tall.TechnicalAreas(0);
        var heights = tallAreas.Skip(1).Select(a => a.Bounds.Height).ToArray();
        Assert.All(heights, h => { Assert.Equal(heights[0], h, 2); Assert.True(h > minimum); });
        Assert.Equal(tall.Pages[0].Tables.Max(f => f.Bounds.Bottom), tallAreas[^1].Bounds.Bottom, 2);
        Assert.Equal(tall.Pages[0].UpperBottom, tallAreas[^1].Bounds.Bottom, 2);
    }

    [Fact]
    public void KeyFiguresFollowTheUpperAreaOnPageOneWhenTheyFitAndMoveWithTheFollowingBlocksWhenTheyDoNot()
    {
        var source = SyntheticReportData.Create();
        ReportTable Table(int rows) => new("Forces", ["Name"], [1],
            Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)new[] { "F" + i }).ToArray());
        using var fits = new ReportDocument(source with { Tables = [Table(2)] }, new(130, 60, (_, _, _, _) => { }));
        Assert.True(fits.Pages[0].SummaryTop >= fits.Pages[0].UpperBottom + 16 - .01f);
        AssertBlockFlow(fits);

        // The right column fills the page: the key figures start the next page, followed by the details and the notes.
        using var full = new ReportDocument(source with { Tables = [Table(40)] }, new(130, 60, (_, _, _, _) => { }));
        Assert.Null(full.Pages[0].SummaryTop);
        AssertBlockFlow(full);
        int summaryPage = full.Pages.Select((p, i) => (p, i)).Single(p => p.p.SummaryTop.HasValue).i;
        Assert.True(summaryPage >= 1);
        Assert.Equal(ReportBlockKind.Summary, full.Pages[summaryPage].Blocks[0].Kind);
    }

    [Fact]
    public void ManyBlocksStackPageByPageAndAnImpossibleBlockFailsInsteadOfOverlapping()
    {
        var source = SyntheticReportData.Create();
        var segments = Enumerable.Range(0, 40).Select(i => source.Segments[0] with { Title = "Abschnitt " + (i + 1) }).ToArray();
        using var report = new ReportDocument(source with { Segments = segments }, new(130, 60, (_, _, _, _) => { }));
        AssertBlockFlow(report);
        Assert.True(report.Pages.Count > 3);
        // Equal blocks stack with the same count on every full continuation page.
        var counts = report.Pages.Skip(1).Take(report.Pages.Count - 2).Select(p => p.Blocks.Count).ToArray();
        Assert.All(counts.Skip(1), c => Assert.InRange(c, counts[0] - 1, counts[0] + 1));
        var huge = source.Segments[0] with { Section = string.Join(" ", Enumerable.Repeat("unbreakable words in a very long name", 400)) };
        Assert.Throws<InvalidOperationException>(() => new ReportDocument(source with { Segments = [huge] }, new(130, 60, (_, _, _, _) => { })));
    }

    [Fact]
    public void TheBeamDrawingShowsTheSegmentRowOnlyForSeveralSegmentsAndKeepsItsStationsExact()
    {
        var one = Document(1); var three = Document(3);
        using var oneReport = Capture(one, out var oneEditor, out var oneState);
        var oneVisuals = new ReportVisuals(oneState, oneEditor.Presentation, oneReport.Data);
        Assert.False(oneVisuals.SegmentRow.IsVisible);

        using var report = Capture(three, out var editor, out var state);
        var visuals = new ReportVisuals(state, editor.Presentation, report.Data);
        var row = visuals.SegmentRow;
        Assert.True(row.IsVisible);
        Assert.Equal(3, row.Spans.Count);
        // The row follows the stations of the beam drawing, which are also the ticks of the axis.
        var stations = visuals.Beam.Scene!.Frame.Layout.Stations;
        Assert.Equal(stations[0].ScreenX, row.Spans[0].StartX, 3);
        Assert.Equal(stations[^1].ScreenX, row.Spans[^1].EndX, 3);
        for (int i = 0; i < 2; i++)
            Assert.Equal(row.Spans[i].EndX, row.Spans[i + 1].StartX, 3);
        // The same labelling as the editor: heading first, shortened step by step, never wrapped.
        Assert.All(row.Spans.Where(s => s.Text?.Contains(SegmentLabels.Separator) == true), s => Assert.StartsWith("Abschnitt ", s.Text));
        // The row has its own band: the beam drawing is taller than that of one segment by the row's height.
        Assert.True(visuals.Graphics.BeamHeight > oneVisuals.Graphics.BeamHeight);
    }
}
