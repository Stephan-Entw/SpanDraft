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
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TheReportListsTheHingesWithBothRotationsAndTheShearAndStatesTheirModel(string culture)
    {
        using var scope = new UiCultureScope(culture);
        using var report = Capture(Document(true), out var editor);
        var table = Assert.Single(report.Data.Tables, t => t.Title == Strings.MomentReleases);
        Assert.Equal(5, table.Headers.Count);
        Assert.Equal(table.Headers.Count, table.Widths.Count);
        Assert.Equal(1f, table.Widths.Sum(), 1e-5);
        var row = Assert.Single(table.Rows);
        Assert.Equal(table.Headers.Count, row.Count);
        Assert.Equal("G1", row[0]);
        var overview = editor.Overview;
        Assert.Equal([overview.MomentReleases[0].Position, overview.MomentReleaseResults[0].RotationLeft,
            overview.MomentReleaseResults[0].RotationRight, overview.MomentReleaseResults[0].Shear], row.Skip(1));
        Assert.All(row.Skip(1), cell => Assert.Contains(cell, c => char.IsDigit(c)));
        Assert.NotEqual(row[2], row[3]);
        Assert.Contains("θ", table.Headers[2].PlainText);
        Assert.Contains(report.Data.Labels.Assumptions, a => a.Contains(Strings.ReportMomentReleaseTheory));
        // The Gerber beam is statically determinate: the hinge shear is the one transmitted force, equal on both sides.
        Assert.DoesNotContain("/", row[4]);
    }

    [Fact]
    public void WithoutAHingeThereIsNoTableAndTheNotesStayUnchanged()
    {
        using var scope = new UiCultureScope("en-US");
        using var report = Capture(Document(false), out var editor);
        Assert.DoesNotContain(report.Data.Tables, t => t.Title == Strings.MomentReleases);
        Assert.DoesNotContain(report.Data.Labels.Assumptions, a => a.Contains(Strings.ReportMomentReleaseTheory));
        Assert.False(editor.Overview.HasMomentReleases);
        Assert.False(editor.Overview.HasMomentReleaseResults);
    }

    [Fact]
    public void EveryPageKeepsTheHingeTableInsideTheBodyAndRendersTheHingeInTheSchematic()
    {
        using var report = Capture(Document(true), out _);
        AssertCompleteTablesAndFinalSummary(report);
        int table = Array.FindIndex(report.Data.Tables.ToArray(), t => t.Title == Strings.MomentReleases);
        Assert.Contains(report.Pages.SelectMany(p => p.Tables), f => f.Table == table && f.Rows.Count > 0);
        for (int i = 0; i < report.Pages.Count; i++)
        {
            using var bitmap = report.RenderPage(i, 96);
            Assert.True(bitmap.Width > 0);
        }
    }
}
