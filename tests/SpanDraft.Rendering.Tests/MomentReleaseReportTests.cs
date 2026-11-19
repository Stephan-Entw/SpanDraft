using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Reporting;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class MomentReleaseReportTests : ProjectReportTestSupport
{
    // Gerber beam: pinned end, roller, hinge, end roller; the load makes the rotation jump at the hinge.
    private static EditorDocument Document(bool releases) => new EditorDocument(M(8), ProjectTemplates.Material, ProjectTemplates.Section,
        new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(4), SupportType.Roller, "B"),
            new(Guid.NewGuid(), M(8), SupportType.Roller, "C") },
        [new EditorPointForce(Guid.NewGuid(), M(7), Force.FromNewtons(-10000), "F1")])
        .WithMomentReleases(releases ? [new(Guid.NewGuid(), M(6), "G1")] : []);

    private static ReportDocument Capture(EditorDocument document, out EditorViewModel editor)
    {
        editor = EditorViewModel.ForSession(ProjectSession.Create(new ProjectState(document, new([]))), () => { });
        return ProjectReport.Capture(editor, Now);
    }

    [Theory]
    [InlineData("de-DE", "Gelenke", "Gelenkgrößen", "Gelenk", "Typ")]
    [InlineData("en-US", "Hinges", "Hinge results", "Hinge", "Type")]
    public void HingeDefinitionsAndResultsStayInOrderOnTheFirstPage(
        string culture, string definitionsTitle, string resultsTitle, string hingeType, string typeHeader)
    {
        using var scope = new UiCultureScope(culture);
        using var report = Capture(Document(true), out var editor);
        var overview = editor.Overview;
        Assert.Equal([Strings.SetupSegments, Strings.Supports, definitionsTitle, Strings.Forces, Strings.Reactions, resultsTitle],
            report.Data.Tables.Select(t => t.Title));
        var definitions = report.Data.Tables[2];
        Assert.Equal(["Name", typeHeader, "x [mm]"], definitions.Headers.Select(h => h.PlainText));
        Assert.Equal(["G1", hingeType, overview.MomentReleases[0].Position], Assert.Single(definitions.Rows));

        var results = report.Data.Tables[5];
        Assert.Equal([hingeType, overview.RotationLeftHeader, overview.RotationRightHeader, overview.ReleaseShearHeader],
            results.Headers.Select(h => h.PlainText));
        Assert.Contains("[rad]", results.Headers[1].PlainText);
        Assert.Contains("[rad]", results.Headers[2].PlainText);
        var row = Assert.Single(results.Rows);
        var result = Assert.Single(overview.MomentReleaseResults);
        Assert.Equal(["G1", result.RotationLeft, result.RotationRight, result.Shear], row);
        Assert.All(row.Skip(1), cell => Assert.Contains(cell, c => char.IsDigit(c)));
        Assert.NotEqual(row[1], row[2]);
        Assert.Contains(report.Data.Labels.Assumptions, a => a.Contains(Strings.ReportMomentReleaseTheory));
        // The Gerber beam is statically determinate: the hinge shear is the one transmitted force, equal on both sides.
        Assert.DoesNotContain("/", row[3]);

        var page = report.Pages[0];
        Assert.Equal(Enumerable.Range(0, report.Data.Tables.Count), page.Tables.Select(t => t.Table));
        Assert.All(page.Tables, fragment => Assert.Equal(ReportDocument.RightWidth, fragment.Bounds.Width, 3));
        Assert.True(page.Tables[^1].Bounds.Bottom < page.SummaryTop);
        Assert.All(report.Pages.Skip(1), continuation => Assert.Empty(continuation.Tables));
        AssertCompleteTablesAndFinalSummary(report);
        using var bitmap = report.RenderPage(0, 96);
        Assert.Contains(report.TextRuns, run => run.Text == definitionsTitle);
        Assert.Contains(report.TextRuns, run => run.Text == resultsTitle);
        AssertTableHeadersAndBounds(report, 0);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ACompactHingeReportFitsCompletelyOnOnePage(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var document = Document(true);
        document = document.WithSupports([document.Supports[0] with { Type = SupportType.Fixed }, document.Supports[^1]])
            .WithLoads([]);
        using var captured = Capture(document, out _);
        // Supply compact graphics to isolate the table and block flow when the page has enough vertical room.
        using var report = new ReportDocument(captured.Data, new(80, 60, (_, _, _, _) => { }));

        var page = Assert.Single(report.Pages);
        Assert.Contains(page.Tables, f => report.Data.Tables[f.Table].Title == Strings.MomentReleases);
        Assert.Contains(page.Tables, f => report.Data.Tables[f.Table].Title == Strings.MomentReleaseResults);
        AssertCompleteTablesAndFinalSummary(report);
    }

    [Fact]
    public void WithoutAHingeThereIsNoTableAndTheNotesStayUnchanged()
    {
        using var scope = new UiCultureScope("en-US");
        using var report = Capture(Document(false), out var editor);
        Assert.DoesNotContain(report.Data.Tables, t => t.Title == Strings.MomentReleases || t.Title == Strings.MomentReleaseResults);
        Assert.DoesNotContain(report.Data.Labels.Assumptions, a => a.Contains(Strings.ReportMomentReleaseTheory));
        Assert.False(editor.Overview.HasMomentReleases);
        Assert.False(editor.Overview.HasMomentReleaseResults);
    }

    [Theory]
    [InlineData("de-DE", false, 1, EngineeringNotation.Automatic)]
    [InlineData("de-DE", false, 5, EngineeringNotation.Automatic)]
    [InlineData("en-US", true, 1, EngineeringNotation.Automatic)]
    [InlineData("en-US", true, 5, EngineeringNotation.Automatic)]
    [InlineData("en-US", true, 5, EngineeringNotation.Off)]
    public void ReportTablesKeepTechnicalPartsTogetherAtSupportedPrecisions(
        string culture, bool us, int digits, EngineeringNotation notation)
    {
        using var scope = new UiCultureScope(culture);
        const string longName = "ExtraordinarilyLongNameForTheDistributedLoad";
        var document = Document(true);
        document = document.WithSupports([.. document.Supports,
            new(Guid.NewGuid(), M(2), SupportType.Spring, "D", new(TranslationalStiffness.FromNewtonsPerMeter(5e5), null)),
            new(Guid.NewGuid(), M(2), SupportType.Spring, "E", new(null, RotationalStiffness.FromNewtonMetersPerRadian(2e4)))])
            .WithDistributedLoads([
                new(Guid.NewGuid(), M(.81234), M(2.4345), ForcePerLength.FromNewtonsPerMeter(-1234.56),
                    ForcePerLength.FromNewtonsPerMeter(-6789.01), longName),
                new(Guid.NewGuid(), M(4), M(5.1234), ForcePerLength.FromNewtonsPerMeter(-4321.23), "q2")]);
        var profile = us ? UnitProfile.UnitedStates : UnitProfile.MechanicalEngineering;
        var editor = EditorViewModel.ForSession(ProjectSession.Create(new ProjectState(document, new([]))), () => { },
            resultPresentation: new ResultPresentationOptions(profile, digits, notation));
        using var report = ProjectReport.Capture(editor, Now);
        AssertCompleteTablesAndFinalSummary(report);

        var layouts = report.Pages.SelectMany(p => p.Tables).GroupBy(f => report.Data.Tables[f.Table].Title)
            .ToDictionary(group => group.Key, group => group.First().Layout);
        var definitions = layouts[Strings.MomentReleases];
        Assert.Contains(definitions.Columns[2].Header, line => line.PlainText.Contains($"[{profile[QuantityKind.BeamLength].Symbol}]", StringComparison.Ordinal));
        Assert.Contains(definitions.Columns[0].Header, line => line.PlainText == Strings.OverviewName);
        var results = layouts[Strings.MomentReleaseResults];
        Assert.Contains(results.Columns[1].Header, line => line.PlainText.Contains("[rad]", StringComparison.Ordinal));
        Assert.All(results.Rows[0].Skip(1), cell => Assert.Single(cell));
        Assert.Contains(layouts[Strings.Reactions].Rows, row => row[0].SequenceEqual(["D + E"]));
        var loads = layouts[Strings.DistributedLoads];
        Assert.True(loads.Rows[0][0].Length > 1);
        Assert.Equal(longName, string.Concat(loads.Rows[0][0]));
        var values = loads.Rows[0][1];
        Assert.InRange(values.Length, 1, 2);
        if (values.Length == 2) Assert.EndsWith("…", values[0]);
        Assert.Equal(editor.Overview.DistributedLoads[0].Value.Replace(" ", ""), string.Concat(values).Replace(" ", ""));
        Assert.EndsWith(profile[QuantityKind.DistributedLoad].Symbol, values[^1]);
        Assert.Single(loads.Rows[1][1]);

        for (int page = 0; page < report.Pages.Count; page++)
        {
            using var image = report.RenderPage(page, 96);
            AssertTableHeadersAndBounds(report, page);
            foreach (var fragment in report.Pages[page].Tables)
            {
                var table = report.Data.Tables[fragment.Table];
                var columns = fragment.Layout.Columns;
                var gaps = columns.Zip(columns.Skip(1), (previous, next) => next.Left - previous.Right).ToArray();
                Assert.All(gaps, gap =>
                {
                    Assert.True(gap >= 4 - .01f);
                    Assert.Equal(gaps[0], gap, .01f);
                });
                for (int i = 0; i < fragment.Layout.Columns.Count; i++)
                {
                    var column = fragment.Layout.Columns[i];
                    float left = fragment.Bounds.Left + column.Left;
                    float right = fragment.Bounds.Left + column.Right;
                    Assert.InRange(column.ValueSize, 7, 8.5f);
                    foreach (var row in fragment.Rows)
                    for (int lineIndex = row.FirstLine; lineIndex < Math.Min(row.FirstLine + row.LineCount, row.Cells[i].Length); lineIndex++)
                    {
                        string line = row.Cells[i][lineIndex];
                        if (line.Length == 0) continue;
                        bool strong = i == 0 && (!table.StrongFirstLine || lineIndex == 0);
                        using var expected = new ReportTextLayout(line, column.ValueSize, strong);
                        Assert.Contains(report.TextRuns, run => run.Text == line
                            && run.Bounds.Left >= left - .01f && run.Bounds.Right <= right + .01f
                            && run.Bounds.Top >= fragment.Bounds.Top + fragment.HeadingHeight && run.Bounds.Bottom <= fragment.Bounds.Bottom + .01f
                            && Math.Abs(run.Bounds.Width - expected.Width) <= .01f);
                    }
                }
            }
        }
    }
}
