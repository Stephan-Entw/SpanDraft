using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The segment list of the Edit setup: splitting, selecting, editing, removing and applying segments.</summary>
public sealed class DesktopSegmentSetupTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static Material Steel(string name = "Steel", double? density = 7850) => new(name, Pressure.FromPascals(210e9),
        Pressure.FromPascals(235e6), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null);
    private static ISectionDefinition Rectangle(double w = 0.1, double h = 0.2) => new RectangleSectionGeometry(M(w), M(h));

    private static EditorDocument Document(bool selfWeight = false, int segments = 1)
    {
        var supports = new EditorSupport[] { new(Guid.NewGuid(), M(0), SupportType.Fixed, "A") };
        var weight = selfWeight ? new EditorSelfWeight(Guid.NewGuid(), true) : null;
        return segments == 1
            ? new EditorDocument(M(3), Steel(), Rectangle(), SectionAxisDesignation.Y, supports, selfWeight: weight)
            : new EditorDocument(M(3), [M(1), M(2)], [new(Steel("S1"), Rectangle(), SectionAxisDesignation.Y, "First"),
                new(Steel("S2"), Rectangle(0.1, 0.3), SectionAxisDesignation.Y), new(Steel("S3"), Rectangle(0.05, 0.1), SectionAxisDesignation.Y)],
                supports, selfWeight: weight);
    }

    private static ProjectSetupViewModel Setup(EditorDocument document, ProjectSetupMode mode = ProjectSetupMode.Edit)
    {
        var first = document.Segments[0];
        return new(mode, first.Section, first.BendingAxis, first.Material, _ => Task.FromResult(true), () => { },
            originalDocument: mode == ProjectSetupMode.Edit ? document : null);
    }

    private static bool Split(ProjectSetupViewModel setup, string text)
    {
        setup.SplitInput.Begin();
        setup.SplitInput.Text = text;
        return setup.SplitInput.Confirm();
    }

    [Fact]
    public void AnEditSetupListsOneRowPerSegmentAndACreateSetupHidesTheList()
    {
        var one = Setup(Document());
        var three = Setup(Document(segments: 3));

        Assert.True(one.ShowSegments);
        Assert.False(one.HasSeveralSegments);
        Assert.Single(one.SegmentRows);
        Assert.Equal(3, three.SegmentRows.Count);
        Assert.Equal([1, 2, 3], three.SegmentRows.Select(r => r.Number));
        Assert.Equal("First", three.SegmentRows[0].Section);
        Assert.Contains("S2", three.SegmentRows[1].Material);
        Assert.Equal(0, three.SelectedSegmentIndex);
        Assert.False(Setup(Document(), ProjectSetupMode.Create).ShowSegments);
    }

    [Fact]
    public void SplittingCopiesTheSelectedSegmentIntoBothHalvesAndSelectsTheNewOne()
    {
        var setup = Setup(Document(segments: 3));

        Assert.True(Split(setup, "500"));
        Assert.Equal([M(0.5), M(1), M(2)], setup.SegmentBoundaries);
        Assert.Equal(4, setup.SegmentRows.Count);
        Assert.Equal(1, setup.SelectedSegmentIndex);
        Assert.Equal("First", setup.SegmentRows[0].Section);
        Assert.Equal("First", setup.SegmentRows[1].Section);
        Assert.Equal(setup.SegmentRows[0].Material, setup.SegmentRows[1].Material);
        Assert.Equal("First", setup.SectionDisplayName);
        Assert.True(setup.CanEditBoundary);
        Assert.Equal("", setup.SplitInput.Text);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3000")]
    [InlineData("3500")]
    [InlineData("1000")]
    [InlineData("2000")]
    public void ASplitOutsideTheBeamOrOnABoundaryIsRejectedWithoutChange(string text)
    {
        var setup = Setup(Document(segments: 3));
        Assert.False(Split(setup, text));
        Assert.True(setup.SplitInput.HasError);
        // A zero or negative text is no length at all and reports the generic length error.
        Assert.Equal(text == "0" ? Strings.InvalidLength : Strings.SegmentSplitInvalid, setup.SplitInput.ErrorText);
        Assert.Equal([M(1), M(2)], setup.SegmentBoundaries);
        Assert.Equal(3, setup.SegmentRows.Count);
    }

    [Fact]
    public void EachSegmentKeepsItsOwnMaterialSectionAxisAndNameWhileOthersAreEdited()
    {
        var setup = Setup(Document(segments: 3));
        setup.SelectedSegmentIndex = 1;
        Assert.Equal("S2", setup.SelectedMaterial!.Name);
        setup.SelectedMaterial = Steel("Changed");
        setup.SelectedSection = Rectangle(0.2, 0.2);
        setup.SelectedSegmentIndex = 2;
        Assert.Equal("S3", setup.SelectedMaterial!.Name);
        setup.SelectedSegmentIndex = 0;
        Assert.Equal("S1", setup.SelectedMaterial!.Name);
        Assert.Equal("First", setup.SectionDisplayName);

        var segments = setup.BuildSegments();
        Assert.Equal(["S1", "Changed", "S3"], segments.Select(s => s.Material.Name));
        Assert.Equal(0.2 * 0.2, segments[1].Section.Area.SquareMeters, 12);
        Assert.Equal([ "First", null, null ], segments.Select(s => s.SectionDisplayName));
        Assert.Equal(["S1", "Changed", "S3"], setup.SegmentRows.Select(r => r.Material.Split(' ')[0]));
    }

    [Fact]
    public void TheSegmentStartMovesBetweenItsNeighboursOnly()
    {
        var setup = Setup(Document(segments: 3));
        setup.SelectedSegmentIndex = 1;
        Assert.Equal(M(1), setup.BoundaryInput.TryGetLength(out var start) ? start : default);

        setup.BoundaryInput.Begin();
        setup.BoundaryInput.Text = "1500";
        Assert.True(setup.BoundaryInput.Confirm());
        Assert.Equal([M(1.5), M(2)], setup.SegmentBoundaries);

        foreach (string invalid in new[] { "0", "2000", "2500", "-100" })
        {
            setup.BoundaryInput.Begin();
            setup.BoundaryInput.Text = invalid;
            Assert.False(setup.BoundaryInput.Confirm());
            setup.BoundaryInput.Cancel();
            Assert.Equal([M(1.5), M(2)], setup.SegmentBoundaries);
        }
        setup.SelectedSegmentIndex = 0;
        Assert.False(setup.CanEditBoundary);
    }

    [Fact]
    public void RemovingASegmentLetsTheLeftNeighbourAbsorbItAndTheFirstOneTheRight()
    {
        var middle = Setup(Document(segments: 3));
        middle.SelectedSegmentIndex = 1;
        middle.RemoveSegmentCommand.Execute(null);
        Assert.Equal([M(2)], middle.SegmentBoundaries);
        Assert.Equal(["S1", "S3"], middle.BuildSegments().Select(s => s.Material.Name));
        Assert.Equal(0, middle.SelectedSegmentIndex);

        var first = Setup(Document(segments: 3));
        first.RemoveSegmentCommand.Execute(null);
        Assert.Equal([M(2)], first.SegmentBoundaries);
        Assert.Equal(["S2", "S3"], first.BuildSegments().Select(s => s.Material.Name));

        var last = Setup(Document(segments: 3));
        last.SelectedSegmentIndex = 2;
        last.RemoveSegmentCommand.Execute(null);
        Assert.Equal([M(1)], last.SegmentBoundaries);
        Assert.Equal(["S1", "S2"], last.BuildSegments().Select(s => s.Material.Name));
        Assert.Equal(1, last.SelectedSegmentIndex);
        Assert.False(Setup(Document()).RemoveSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void TheLengthMustExceedTheLastBoundaryUntilTheBoundaryIsRemoved()
    {
        var setup = Setup(Document(segments: 3));
        setup.LengthInput.Begin();
        setup.LengthInput.Text = "2000";
        Assert.False(setup.LengthInput.Confirm());
        Assert.Equal(Strings.LengthExcludesSegments, setup.LengthInput.ErrorText);
        setup.LengthInput.Cancel();

        setup.SelectedSegmentIndex = 2;
        setup.RemoveSegmentCommand.Execute(null);
        setup.LengthInput.Begin();
        setup.LengthInput.Text = "1500";
        Assert.True(setup.LengthInput.Confirm());
        Assert.Equal(M(1.5), setup.SelectedLength);
    }

    [Fact]
    public void ApplyNeedsAMaterialWithDensityInEverySegmentWhenSelfWeightIsOn()
    {
        var setup = Setup(Document(selfWeight: true, segments: 3));
        setup.CalculationTitleInput.Text = "Title";
        Assert.True(setup.CanApply);
        setup.SelectedSegmentIndex = 1;
        setup.SelectedMaterial = Steel("No density", density: null);
        Assert.NotNull(setup.MaterialError);
        Assert.False(setup.CanApply);
        setup.SelectedSegmentIndex = 0;
        Assert.Null(setup.MaterialError);
        Assert.False(setup.CanApply);
        setup.SelectedSegmentIndex = 1;
        setup.SelectedMaterial = Steel("With density");
        Assert.True(setup.CanApply);
    }

    [Fact]
    public async Task ApplyingCommitsSegmentsBoundariesAndLengthInOneRevision()
    {
        int analyses = 0;
        var main = new MainWindowViewModel(beam => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(beam); });
        main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main);
        main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!;
        var before = editor.Session.CurrentRevision;
        main.EditProject();
        var setup = main.Setup;
        Assert.True(Split(setup, "500"));
        setup.SelectedMaterial = Steel("Second");
        setup.SelectedSection = Rectangle(0.05, 0.1);
        int count = analyses;

        Assert.True(await setup.ConfirmAsync());

        Assert.Equal(MainViewMode.Editor, main.Mode);
        var document = editor.Document;
        Assert.Equal([M(0.5)], document.SegmentBoundaries);
        Assert.Equal(2, document.Segments.Count);
        Assert.Equal("Second", document.Segments[1].Material.Name);
        Assert.Equal(count + 1, analyses);
        Assert.NotSame(before, editor.Session.CurrentRevision);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Document.SegmentBoundaries);
        Assert.Same(before, editor.Session.CurrentRevision);
        Assert.False(editor.Undo());
    }
}
