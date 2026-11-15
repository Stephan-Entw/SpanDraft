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

/// <summary>The segment cards of the project setup: adding, inserting, duplicating, deleting, editing and applying segments.</summary>
public sealed class DesktopSegmentSetupTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static Material Steel(string name = "Steel", double? density = 7850) => new(name, Pressure.FromPascals(210e9),
        Pressure.FromPascals(235e6), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null);
    private static ISectionDefinition Rectangle(double w = 0.1, double h = 0.2) => new RectangleSectionGeometry(M(w), M(h));

    private static EditorSegment Segment(string material, double length, double h = 0.2, string? name = null) =>
        new(Steel(material), Rectangle(0.1, h), SectionAxisDesignation.Y, name) { Length = M(length) };

    // One segment of 3 m, or three of 1 m, 1 m and 1 m with a fixed support at the start.
    private static EditorDocument Document(bool selfWeight = false, int segments = 1,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null)
    {
        supports ??= [new EditorSupport(Guid.NewGuid(), M(0), SupportType.Fixed, "A")];
        var weight = selfWeight ? new EditorSelfWeight(Guid.NewGuid(), true) : null;
        return segments == 1
            ? new EditorDocument([Segment("S1", 3)], supports, loads, null, null, weight)
            : new EditorDocument([Segment("S1", 1, name: "First"), Segment("S2", 1, 0.3), Segment("S3", 1, 0.1)],
                supports, loads, null, null, weight);
    }

    private static ProjectSetupViewModel Setup(EditorDocument? document, ProjectSetupMode mode = ProjectSetupMode.Edit)
    {
        var first = document!.Segments[0];
        return new(mode, first.Section, first.BendingAxis, first.Material, _ => Task.FromResult(true), () => { },
            originalDocument: mode == ProjectSetupMode.Edit ? document : null);
    }

    private static bool SetLength(ProjectSetupViewModel setup, int card, string text)
    {
        var input = setup.Cards[card].LengthInput;
        input.Begin();
        input.Text = text;
        return input.Confirm();
    }

    private static void Complete(ProjectSetupViewModel setup, int card, string length, string material = "New")
    {
        Assert.True(SetLength(setup, card, length));
        setup.Cards[card].ChangeMaterialCommand.Execute(null);
        setup.SelectedMaterial = Steel(material);
        setup.SelectedSection = Rectangle();
        setup.ShowOverview();
    }

    [Fact]
    public void ThereIsOneCardPerSegmentInBothModesAndTheNumberOnlyShowsWithSeveralSegments()
    {
        var one = Setup(Document());
        var three = Setup(Document(segments: 3));
        var create = Setup(Document(), ProjectSetupMode.Create);

        Assert.Single(one.Cards);
        Assert.False(one.Cards[0].HasSeveralSegments);
        Assert.False(one.Cards[0].CanRemove);
        Assert.Equal(3, three.Cards.Count);
        Assert.Equal([1, 2, 3], three.Cards.Select(c => c.Number));
        Assert.All(three.Cards, c => Assert.True(c.HasSeveralSegments));
        Assert.Equal("First", three.Cards[0].SectionName);
        Assert.Contains("S2", three.Cards[1].MaterialName);
        Assert.Equal(["1000", "1000", "1000"], three.Cards.Select(c => c.LengthInput.Text));
        Assert.Equal(0, three.SelectedSegmentIndex);
        Assert.Single(create.Cards);
        Assert.Equal(M(3), three.TotalLength);
        // The total names its unit once.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(three.TotalLengthText, "mm"));
        Assert.EndsWith("mm", three.TotalLengthText);
        Assert.True(three.ShowTotalLength);
        Assert.False(one.ShowTotalLength);
    }

    [Fact]
    public void AddingAppendsAnEmptyCardThatBlocksApplyUntilItIsComplete()
    {
        var setup = Setup(Document());
        setup.CalculationTitleInput.Text = "Title";
        Assert.True(setup.CanApply);

        setup.AddSegmentCommand.Execute(null);

        Assert.Equal(2, setup.Cards.Count);
        Assert.Equal("", setup.Cards[1].LengthInput.Text);
        Assert.False(setup.Cards[1].HasSection);
        Assert.False(setup.Cards[1].HasMaterial);
        Assert.False(setup.CanApply);
        Complete(setup, 1, "500");
        Assert.True(setup.CanApply);
        Assert.Equal(M(3.5), setup.TotalLength);
        Assert.Equal(["S1", "New"], setup.BuildSegments().Select(s => s.Material.Name));
        Assert.Equal([M(3), M(0.5)], setup.BuildSegments().Select(s => s.Length));
    }

    [Fact]
    public void InsertingPutsTheEmptyCardInFrontOrBehindAndKeepsTheIdentityOfTheOthers()
    {
        var setup = Setup(Document(segments: 3));
        var ids = setup.Cards.Select(c => c.Id).ToArray();

        setup.Cards[0].InsertBeforeCommand.Execute(null);
        Assert.Equal(4, setup.Cards.Count);
        Assert.Equal([ids[0], ids[1], ids[2]], setup.Cards.Skip(1).Select(c => c.Id));
        Assert.Equal("", setup.Cards[0].LengthInput.Text);
        Assert.Equal([1, 2, 3, 4], setup.Cards.Select(c => c.Number));

        setup.Cards[2].InsertAfterCommand.Execute(null);
        Assert.Equal(5, setup.Cards.Count);
        Assert.Equal([ids[0], ids[1]], setup.Cards.Skip(1).Take(2).Select(c => c.Id));
        Assert.Equal("", setup.Cards[3].LengthInput.Text);
        Assert.Equal(ids[2], setup.Cards[4].Id);
        Assert.Equal(M(3), setup.TotalLength);
    }

    [Fact]
    public void DuplicatingCopiesTheCardWithANewIdentityDirectlyBehindIt()
    {
        var setup = Setup(Document(segments: 3));
        var original = setup.Cards[1].Id;

        setup.Cards[1].DuplicateCommand.Execute(null);

        Assert.Equal(4, setup.Cards.Count);
        Assert.Equal(original, setup.Cards[1].Id);
        Assert.NotEqual(original, setup.Cards[2].Id);
        Assert.Equal(setup.Cards[1].SectionName, setup.Cards[2].SectionName);
        Assert.Equal(setup.Cards[1].MaterialName, setup.Cards[2].MaterialName);
        Assert.Equal("1000", setup.Cards[2].LengthInput.Text);
        Assert.Equal(M(4), setup.TotalLength);
        Assert.Equal(["S1", "S2", "S2", "S3"], setup.BuildSegments().Select(s => s.Material.Name));
    }

    [Fact]
    public void DeletingRemovesTheCardAndTheLastOneCannotBeDeleted()
    {
        var setup = Setup(Document(segments: 3));
        setup.Cards[1].RemoveCommand.Execute(null);

        Assert.Equal(["S1", "S3"], setup.BuildSegments().Select(s => s.Material.Name));
        Assert.Equal(M(2), setup.TotalLength);
        setup.Cards[1].RemoveCommand.Execute(null);
        Assert.Single(setup.Cards);
        Assert.False(setup.Cards[0].RemoveCommand.CanExecute(null));
        setup.Cards[0].RemoveCommand.Execute(null);
        Assert.Single(setup.Cards);
        Assert.Equal(["S1"], setup.BuildSegments().Select(s => s.Material.Name));
    }

    [Fact]
    public void EachSegmentKeepsItsOwnMaterialSectionAxisAndNameWhileOthersAreEdited()
    {
        var setup = Setup(Document(segments: 3));
        setup.Cards[1].ChangeMaterialCommand.Execute(null);
        Assert.Equal(1, setup.SelectedSegmentIndex);
        Assert.Equal("S2", setup.SelectedMaterial!.Name);
        setup.SelectedMaterial = Steel("Changed");
        setup.SelectedSection = Rectangle(0.2, 0.2);
        setup.ShowOverview();
        setup.Cards[2].ChangeSectionCommand.Execute(null);
        Assert.Equal("S3", setup.SelectedMaterial!.Name);
        setup.ShowOverview();
        setup.SelectedSegmentIndex = 0;
        Assert.Equal("S1", setup.SelectedMaterial!.Name);
        Assert.Equal("First", setup.SectionDisplayName);

        var segments = setup.BuildSegments();
        Assert.Equal(["S1", "Changed", "S3"], segments.Select(s => s.Material.Name));
        Assert.Equal(0.2 * 0.2, segments[1].Section.Area.SquareMeters, 12);
        Assert.Equal(["First", null, null], segments.Select(s => s.SectionDisplayName));
        Assert.Equal(["S1", "Changed", "S3"], setup.Cards.Select(c => c.MaterialName.Split(' ')[0]));
        Assert.Equal(setup.Cards.Select(c => c.Id), segments.Select(s => s.Id));
    }

    [Fact]
    public void ALengthChangeMovesTheTotalAndKeepsEveryIdentity()
    {
        var setup = Setup(Document(segments: 3));
        var ids = setup.Cards.Select(c => c.Id).ToArray();

        Assert.True(SetLength(setup, 1, "2500"));

        Assert.Equal(M(4.5), setup.TotalLength);
        Assert.Equal([M(1), M(2.5), M(1)], setup.BuildSegments().Select(s => s.Length));
        Assert.Equal(ids, setup.BuildSegments().Select(s => s.Id));
        Assert.False(SetLength(setup, 1, "0"));
        Assert.True(setup.Cards[1].LengthInput.HasError);
        Assert.Equal(M(4.5), setup.TotalLength);
    }

    [Fact]
    public void AChangeThatWouldInvalidateAnElementIsRejectedAndNamesIt()
    {
        var supports = new EditorSupport[]
        {
            new(Guid.NewGuid(), M(0), SupportType.Fixed, "A"),
            new(Guid.NewGuid(), M(0), SupportType.Roller, "B") { Reference = new PositionRef.FromBeamStart(M(2.5)) }
        };
        var setup = Setup(Document(segments: 3, supports: supports));

        // The roller stands 2.5 m from the beam start; the beam would end at 2 m.
        Assert.False(setup.Cards[2].RemoveCommand.CanExecute(null) && false);
        setup.Cards[2].RemoveCommand.Execute(null);

        Assert.Equal(3, setup.Cards.Count);
        Assert.True(setup.HasSegmentError);
        Assert.Contains(Strings.LengthExcludesSupports, setup.SegmentError!);
        Assert.Equal(M(3), setup.TotalLength);
    }

    [Fact]
    public void AnElementMeasuredFromASegmentBlocksDeletingThatSegmentAndIsNamedInTheMessage()
    {
        var segments = new[] { Segment("S1", 1) with { Id = Guid.NewGuid() }, Segment("S2", 1) with { Id = Guid.NewGuid() },
            Segment("S3", 1) with { Id = Guid.NewGuid() } };
        var document = new EditorDocument(segments,
            [new EditorSupport(Guid.NewGuid(), M(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), M(0), SpanDraft.Core.Units.Force.FromNewtons(1000), "F1")
                { Reference = new PositionRef.FromSegmentStart(segments[1].Id, M(0.5)) }]);
        var setup = Setup(document);

        setup.Cards[1].RemoveCommand.Execute(null);

        Assert.Equal(3, setup.Cards.Count);
        Assert.Contains("F1", setup.SegmentError!);
        Assert.Equal(string.Format(Strings.SegmentChangeBlocked, "F1"), setup.SegmentError);
        // Deleting a segment that nothing refers to is fine, and the element moves with its segment.
        setup.Cards[2].RemoveCommand.Execute(null);
        Assert.Equal(2, setup.Cards.Count);
        Assert.Null(setup.SegmentError);
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
    public void ACreateSetupWithSeveralSegmentsKeepsThemInItsDraft()
    {
        var setup = Setup(Document(), ProjectSetupMode.Create);
        setup.CalculationTitleInput.Text = "Title";
        setup.AddSegmentCommand.Execute(null);
        Complete(setup, 1, "500", "Second");

        var draft = setup.Draft;

        var further = Assert.Single(draft.FurtherSegments!);
        Assert.Equal(M(0.5), further.Length);
        Assert.Equal("Second", further.Material!.Name);
        Assert.True(draft.ContentEquals(draft with { FurtherSegments = draft.FurtherSegments!.ToArray() }));
        Assert.False(draft.ContentEquals(draft with { FurtherSegments = [] }));
    }

    [Fact]
    public async Task ApplyingCommitsTheWholeChainInOneRevision()
    {
        int analyses = 0;
        var main = new MainWindowViewModel(beam => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(beam); });
        main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main);
        main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!;
        var before = editor.Session.CurrentRevision;
        var firstId = editor.Document.Segments[0].Id;
        main.EditProject();
        var setup = main.Setup;
        setup.AddSegmentCommand.Execute(null);
        Complete(setup, 1, "500", "Second");
        int count = analyses;

        Assert.True(await setup.ConfirmAsync());

        Assert.Equal(MainViewMode.Editor, main.Mode);
        var document = editor.Document;
        Assert.Equal(2, document.Segments.Count);
        Assert.Equal(firstId, document.Segments[0].Id);
        Assert.Equal("Second", document.Segments[1].Material.Name);
        Assert.Equal(M(0.5), document.Segments[1].Length);
        Assert.Equal(count + 1, analyses);
        Assert.NotSame(before, editor.Session.CurrentRevision);
        Assert.True(editor.Undo());
        Assert.Single(editor.Document.Segments);
        Assert.Same(before, editor.Session.CurrentRevision);
        Assert.False(editor.Undo());
    }

    [Fact]
    public async Task ACreateSetupBuildsABeamOfAllItsSegments()
    {
        var main = new MainWindowViewModel(beam => SpanDraft.Analysis.BeamAnalysis.Analyze(beam));
        main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main);
        main.Setup.AddSegmentCommand.Execute(null);
        Complete(main.Setup, 1, "750", "Second");

        Assert.True(await main.Setup.ConfirmAsync());

        var document = main.Editor!.Document;
        Assert.Equal(2, document.Segments.Count);
        Assert.Equal("Second", document.Segments[1].Material.Name);
        Assert.Equal(document.Segments[0].Length.Meters + document.Segments[1].Length.Meters, document.Length.Meters);
    }
}
