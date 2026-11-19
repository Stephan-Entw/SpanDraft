using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SpringSupportReportTests
{
    private static EditorDocument Document(bool springs) => new(M(2), ProjectTemplates.Material, ProjectTemplates.Section,
        springs
            ? [new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"),
                new(Guid.NewGuid(), M(1), SupportType.Spring, "B", new(TranslationalStiffness.FromNewtonsPerMeter(2.5e6), null)),
                new(Guid.NewGuid(), M(2), SupportType.Spring, "C", new(null, RotationalStiffness.FromNewtonMetersPerRadian(3e4), true))]
            : [new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(2), SupportType.Roller, "B")],
        [new EditorPointForce(Guid.NewGuid(), M(.5), Force.FromNewtons(-1000), "F1")]);

    [Theory]
    [InlineData("de-DE", "Federlager sind linear-elastisch mit konstanter Steifigkeit und axial starr.")]
    [InlineData("en-US", "Spring supports are linear-elastic with constant stiffness and axially rigid.")]
    public void ReportShowsSpringStiffnessesReactionsAndTheSpringNote(string culture, string springNote)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var editor = EditorViewModel.ForSession(ProjectSession.Create(new ProjectState(Document(true), new([]))), () => { });
        using var report = ProjectReport.Capture(editor, Now);
        var supports = Assert.Single(report.Data.Tables, t => t.Title == Strings.Supports);
        Assert.Equal(editor.Overview.Supports.Select(s => s.Description), supports.Rows.Select(r => r[1]));
        Assert.Contains(supports.Rows, r => r[1].Contains("N/mm") && r[1].Contains("\nk = "));
        Assert.Contains(supports.Rows, r => r[1].Contains("N·m/rad") && r[1].Contains("\nkθ = "));
        var reactions = Assert.Single(report.Data.Tables, t => t.Title == Strings.Reactions);
        Assert.Equal(3, reactions.Rows.Count);
        // Spring and partial restraint reactions are real values, not placeholders.
        Assert.All(reactions.Rows.Skip(1), r => Assert.Contains(r.Skip(1), v => v.Any(char.IsDigit)));
        Assert.Contains(report.Data.Labels.Assumptions, n => n.Contains(springNote));
    }

    [Fact]
    public void ReportWithoutSpringsKeepsItsNotesUnchanged()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var editor = EditorViewModel.ForSession(ProjectSession.Create(new ProjectState(Document(false), new([]))), () => { });
        using var report = ProjectReport.Capture(editor, Now);
        Assert.DoesNotContain(report.Data.Labels.Assumptions, n => n.Contains(Strings.ReportSpringTheory));
    }
}
