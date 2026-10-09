using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopNamingAndPresentationTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);

    private sealed class Session
    {
        public int Calls { get; private set; }
        public BeamModel? LastBeam { get; private set; }
        public MainWindowViewModel Main { get; }
        public EditorViewModel Editor => Main.Editor!;
        public Session()
        {
            Main = new(beam => { Calls++; LastBeam = beam; return BeamAnalysis.Analyze(beam); });
            Main.Setup.ApplyCommand.Execute(null);
        }
        public void NewDraft(AutoNameKind kind, double mm = 300)
        {
            if (kind == AutoNameKind.Support)
            {
                Editor.ToggleSupportTool(SupportType.Fixed);
                Editor.HoverPlacement(Mm(mm));
                Assert.True(Editor.PlaceSupport());
            }
            else
            {
                Editor.ToggleLoadTool(kind == AutoNameKind.Force ? PointLoadKind.Force : PointLoadKind.Moment);
                Editor.HoverLoadPlacement(Mm(mm));
                Assert.True(Editor.PlaceLoad());
            }
        }
        public string NameText
        {
            get => Editor.SupportDraft?.NameText ?? Editor.LoadDraft!.NameText;
            set { if (Editor.SupportDraft is { } support) support.NameText = value; else Editor.LoadDraft!.NameText = value; }
        }
        public AutoNameCandidate Candidate => (Editor.SupportDraft?.AutoCandidate ?? Editor.LoadDraft?.AutoCandidate)!.Value;
        public bool Confirm() => Editor.SupportDraft is not null ? Editor.ConfirmSupport() : Editor.ConfirmLoad();
        public Guid Create(AutoNameKind kind, double mm = 300, string? name = null)
        {
            NewDraft(kind, mm);
            if (name is not null) NameText = name;
            Assert.True(Confirm());
            return kind == AutoNameKind.Support ? Editor.Document.Supports[^1].Id : Editor.Document.Loads[^1].Id;
        }
        public void Edit(AutoNameKind kind, Guid id) => Assert.True(kind == AutoNameKind.Support ? Editor.EditSupport(id) : Editor.EditLoad(id));
        public string Name(Guid id) => Editor.Document.NamedEntities.Single(e => e.Id == id).Name;
        public void Delete(AutoNameKind kind, Guid id)
        {
            Edit(kind, id);
            if (kind == AutoNameKind.Support) Editor.DeleteSupport(); else Editor.DeleteLoad();
        }
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "B")]
    [InlineData(3, "C")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(28, "AB")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    public void SupportOrdinalsUseTheStableAlphabeticSequence(long ordinal, string name) =>
        Assert.Equal(name, EntityNaming.Format(AutoNameKind.Support, ordinal));

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void SuccessfulCreatesAssignThreeConsecutiveNamesAndAdvanceOnlyTheirCounter(AutoNameKind kind)
    {
        var s = new Session();
        for (int i = 1; i <= 3; i++)
        {
            var id = s.Create(kind, i * 100);
            Assert.Equal(EntityNaming.Format(kind, i), s.Name(id));
        }
        Assert.Equal(4, s.Editor.Document.NamingState.Next(kind));
        foreach (var other in Enum.GetValues<AutoNameKind>().Where(k => k != kind))
            Assert.Equal(1, s.Editor.Document.NamingState.Next(other));
        Assert.Equal(4, s.Calls);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void CancelAndFailedCommitDoNotConsumeTheDraftCandidate(AutoNameKind kind)
    {
        var s = new Session();
        var original = s.Editor.Document;
        s.NewDraft(kind);
        var candidate = s.Candidate;
        s.NameText = " ";
        Assert.False(s.Confirm());
        Assert.Same(original, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        s.NameText = "Temporary rename";
        s.Editor.CancelEditorInteraction();
        s.NewDraft(kind);
        Assert.Equal(candidate, s.Candidate);
        Assert.Equal(candidate.Name, s.NameText);
        s.Editor.CancelEditorInteraction();
        Assert.Same(original, s.Editor.Document);
        Assert.Equal(1, s.Editor.Document.NamingState.Next(kind));
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void CreateConsumesTheProposedNameEvenAfterManualRename(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind, 100, "  Motor mount  ");
        Assert.Equal("Motor mount", s.Name(id));
        Assert.Equal(2, s.Editor.Document.NamingState.Next(kind));
        s.NewDraft(kind, 200);
        Assert.Equal(EntityNaming.Format(kind, 2), s.NameText);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void DeleteAndRenameNeverReuseConsumedNamesOrDeriveCountersFromStrings(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind);
        var naming = s.Editor.Document.NamingState;
        s.Edit(kind, id);
        s.NameText = EntityNaming.Format(kind, 999);
        Assert.True(s.Confirm());
        Assert.Same(naming, s.Editor.Document.NamingState);
        Assert.Equal(2, s.Calls);
        s.Delete(kind, id);
        Assert.Same(naming, s.Editor.Document.NamingState);
        Assert.Empty(s.Editor.Document.NamedEntities);
        s.NewDraft(kind);
        Assert.Equal(EntityNaming.Format(kind, 2), s.NameText);
        Assert.Equal(3, s.Calls);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void ManuallyOccupiedFutureCandidatesAreSkippedPermanentlyAfterCreate(AutoNameKind kind)
    {
        var s = new Session();
        var manual = s.Create(kind, 100, EntityNaming.Format(kind, 3).ToLowerInvariant());
        var second = s.Create(kind, 200);
        Assert.Equal(EntityNaming.Format(kind, 2), s.Name(second));
        var fourth = s.Create(kind, 300);
        Assert.Equal(EntityNaming.Format(kind, 4), s.Name(fourth));
        Assert.Equal(5, s.Editor.Document.NamingState.Next(kind));
        s.Delete(kind, manual);
        var fifth = s.Create(kind, 400);
        Assert.Equal(EntityNaming.Format(kind, 5), s.Name(fifth));
    }

    [Fact]
    public void NamesAreTrimmedAndCaseInsensitiveAcrossAllEntityKinds()
    {
        var s = new Session();
        var force = s.Create(AutoNameKind.Force, name: " Drive ");
        Assert.Equal("Drive", s.Name(force));
        var document = s.Editor.Document;
        foreach (var kind in new[] { AutoNameKind.Support, AutoNameKind.Moment })
        {
            s.NewDraft(kind);
            s.NameText = " dRiVe ";
            Assert.False(s.Confirm());
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(2, s.Calls);
            s.Editor.CancelEditorInteraction();
        }
        s.Edit(AutoNameKind.Force, force);
        s.NameText = "drive";
        Assert.True(s.Confirm());
        Assert.Equal("drive", s.Name(force));
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void EmptyNamesAreRejectedByEntitiesAndDrafts(string name)
    {
        Assert.Throws<ArgumentException>(() => new EditorSupport(Guid.NewGuid(), Mm(0), SupportType.Fixed, name));
        Assert.Throws<ArgumentException>(() => EditorPointLoad.Create(Guid.NewGuid(), Mm(0), PointLoadKind.Force, 1, name));
        var s = new Session();
        s.NewDraft(AutoNameKind.Force);
        s.NameText = name;
        Assert.True(s.Editor.LoadDraft!.HasNameError);
        Assert.False(s.Confirm());
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void DocumentBoundaryRejectsCrossKindDuplicatesAndCounterRollback()
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(0), SupportType.Fixed, " A ");
        var load = EditorPointLoad.Create(Guid.NewGuid(), Mm(0), PointLoadKind.Force, 10, " a ");
        Assert.Equal("A", support.Name);
        Assert.Equal("a", load.Name);
        Assert.Throws<ArgumentException>(() => new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, [support], [load]));
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, [support], namingState: new(3, 4, 5));
        Assert.Throws<ArgumentException>(() => document.WithSupports([], new(2, 4, 5)));
        Assert.Throws<ArgumentException>(() => document.WithLoads([], new(3, 3, 5)));
        Assert.Throws<ArgumentException>(() => document.WithLoads([], new(3, 4, 4)));
        Assert.Same(document.NamingState, document.WithSupports([]).NamingState);
        Assert.Same(document.NamingState, document.WithLoads([]).NamingState);
        Assert.Same(document.NamingState, (document with { Length = Mm(2000) }).NamingState);
    }

    [Fact]
    public void ExplicitProjectCounterIsIndependentOfNamesAndCollisionsIncludeOtherEntityKinds()
    {
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "f2")],
            [EditorPointLoad.Create(Guid.NewGuid(), Mm(100), PointLoadKind.Moment, 10, "F999")], new(1, 2, 1));
        var candidate = EntityNaming.Peek(document, AutoNameKind.Force);
        Assert.Equal("F3", candidate.Name);
        Assert.Equal(2, document.NamingState.NextForceNumber);
        var consumed = document.NamingState.Consume(candidate);
        Assert.Equal(4, consumed.NextForceNumber);
        Assert.Equal(1, consumed.NextSupportOrdinal);
        Assert.Throws<ArgumentException>(() => consumed.Consume(candidate));
        Assert.Throws<OverflowException>(() => new NamingState(nextForceNumber: long.MaxValue)
            .Consume(new(AutoNameKind.Force, long.MaxValue, EntityNaming.Format(AutoNameKind.Force, long.MaxValue))));
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void RenameOnlyIsTransactionalAndPreservesAnalysisAndCoreValues(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind);
        var document = s.Editor.Document;
        var analysis = s.Editor.Presentation;
        var beam = s.LastBeam;
        s.Edit(kind, id);
        s.NameText = "Cancelled rename";
        s.Editor.CancelEditorInteraction();
        Assert.Same(document, s.Editor.Document);
        s.Edit(kind, id);
        s.NameText = "Renamed";
        Assert.True(s.Confirm());
        Assert.Equal("Renamed", s.Name(id));
        Assert.Equal(2, s.Calls);
        Assert.Same(analysis, s.Editor.Presentation);
        Assert.Same(beam, s.LastBeam);
        Assert.Same(document.NamingState, s.Editor.Document.NamingState);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(document.ToBeamModel(), s.Editor.Document.ToBeamModel()));
        var renamed = s.Editor.Document;
        s.Edit(kind, id);
        Assert.True(s.Confirm());
        Assert.Same(renamed, s.Editor.Document);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void RenameAndMechanicalChangeCommitTogetherWithOneAnalysis(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind);
        int commits = 0;
        s.Editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditorViewModel.Document)) commits++; };
        s.Edit(kind, id);
        s.NameText = "Combined";
        if (kind == AutoNameKind.Support) s.Editor.SupportDraft!.PositionText = "400";
        else { s.Editor.LoadDraft!.PositionText = "400"; s.Editor.LoadDraft.ValueText = "123"; }
        Assert.True(s.Confirm());
        Assert.Equal(1, commits);
        Assert.Equal(3, s.Calls);
        Assert.Equal("Combined", s.Name(id));
        Assert.Equal(Mm(400), kind == AutoNameKind.Support ? s.Editor.Document.Supports[0].Position : s.Editor.Document.Loads[0].Position);
        if (kind != AutoNameKind.Support) Assert.Equal(123, s.Editor.Document.Loads[0].Value);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void NamesAndDraftRenameSurviveRepeatedDrags(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind, name: "Original");
        s.Edit(kind, id);
        s.NameText = "Dragged";
        var naming = s.Editor.Document.NamingState;
        foreach (double x in new[] { 400d, 500 })
        {
            if (kind == AutoNameKind.Support)
            {
                Assert.True(s.Editor.BeginSupportDrag(id));
                s.Editor.UpdateSupportDrag(Mm(x));
                Assert.True(s.Editor.EndSupportDrag());
            }
            else
            {
                Assert.True(s.Editor.BeginLoadDrag(id));
                s.Editor.UpdateLoadDrag(Mm(x));
                Assert.True(s.Editor.EndLoadDrag());
            }
            Assert.Equal("Dragged", s.NameText);
            Assert.Equal("Original", s.Name(id));
            Assert.Equal(2, s.Calls);
        }
        Assert.True(s.Confirm());
        Assert.Equal("Dragged", s.Name(id));
        Assert.Same(naming, s.Editor.Document.NamingState);
        Assert.Equal(3, s.Calls);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    public void OffsetChangesAreSessionMetadataAndDeletingAnEntityPrunesOnlyItsOffset(AutoNameKind kind)
    {
        var s = new Session();
        var id = s.Create(kind, 100);
        var other = s.Create(kind, 200);
        var document = s.Editor.Document;
        var analysis = s.Editor.Presentation;
        var beam = s.LastBeam;
        Assert.True(s.Editor.SetAnnotationOffset(id, new(12, -3)));
        Assert.True(s.Editor.SetAnnotationOffset(other, new(-4, 5)));
        var state = s.Editor.EditorPresentation;
        Assert.True(s.Editor.SetAnnotationOffset(id, new(12, -3)));
        Assert.Same(state, s.Editor.EditorPresentation);
        Assert.Same(document, s.Editor.Document);
        Assert.Same(analysis, s.Editor.Presentation);
        Assert.Same(beam, s.LastBeam);
        Assert.Equal(3, s.Calls);
        s.Editor.CancelEditorInteraction();
        Assert.Same(state, s.Editor.EditorPresentation);
        Assert.False(s.Editor.SetAnnotationOffset(Guid.NewGuid(), new(1, 2)));
        s.Delete(kind, id);
        Assert.False(s.Editor.EditorPresentation.AnnotationOffsets.ContainsKey(id));
        Assert.Equal(new AnnotationOffset(-4, 5), s.Editor.EditorPresentation.AnnotationOffsets[other]);
        Assert.Equal(4, s.Calls);
        Assert.True(s.Editor.SetAnnotationOffset(other, null));
        Assert.Empty(s.Editor.EditorPresentation.AnnotationOffsets);
        Assert.Equal(4, s.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupNavigationPreservesNamesCountersAndSessionPresentation(bool apply)
    {
        var s = new Session();
        var support = s.Create(AutoNameKind.Support, 0, "Bearing");
        var force = s.Create(AutoNameKind.Force, 700, "Drive");
        s.Create(AutoNameKind.Moment, 500, "Torque");
        s.Editor.SetAnnotationOffset(force, new(14, 21));
        var editor = s.Editor;
        var document = editor.Document;
        var presentation = editor.EditorPresentation;
        s.Main.EditProject();
        Assert.Same(editor, s.Editor);
        s.Main.Setup.SelectedMaterial = new("Other", Pressure.FromPascals(200e9), Pressure.FromMegapascals(355));
        if (apply) s.Main.Setup.ApplyCommand.Execute(null); else s.Main.Setup.CancelCommand.Execute(null);
        Assert.Same(editor, s.Editor);
        Assert.Same(presentation, s.Editor.EditorPresentation);
        Assert.Same(document.NamingState, s.Editor.Document.NamingState);
        Assert.Equal(document.Supports, s.Editor.Document.Supports);
        Assert.Equal(document.Loads, s.Editor.Document.Loads);
        Assert.Equal("Bearing", s.Name(support));
        Assert.Equal("Drive", s.Name(force));
        Assert.Equal(apply ? 5 : 4, s.Calls);
    }

    [Fact]
    public void NewProjectSessionStartsWithNewPresentationAndNamingState()
    {
        var s = new Session();
        var id = s.Create(AutoNameKind.Force);
        s.Editor.SetAnnotationOffset(id, new(5, 6));
        var previous = s.Editor;
        // The retained Create setup represents creating a fresh project, without introducing a new UI action.
        s.Main.Setup.ApplyCommand.Execute(null);
        Assert.NotSame(previous, s.Editor);
        Assert.Empty(s.Editor.EditorPresentation.AnnotationOffsets);
        Assert.Empty(s.Editor.Document.NamedEntities);
        Assert.Equal(new NamingState(), s.Editor.Document.NamingState);
        Assert.Equal(new AnnotationOffset(5, 6), previous.EditorPresentation.AnnotationOffsets[id]);
    }

    [Fact]
    public void PresentationCopiesAreDefensiveAndOffsetsMustBeFinite()
    {
        var id = Guid.NewGuid();
        var source = new Dictionary<Guid, AnnotationOffset> { [id] = new(1, 2) };
        var state = new EditorPresentationState(source);
        source[id] = new(3, 4);
        Assert.Equal(new AnnotationOffset(1, 2), state.AnnotationOffsets[id]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<Guid, AnnotationOffset>)state.AnnotationOffsets).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationOffset(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationOffset(0, double.PositiveInfinity));
        var reset = state.WithOffset(id, null);
        Assert.Empty(reset.AnnotationOffsets);
        Assert.Equal(new AnnotationOffset(1, 2), state.AnnotationOffsets[id]);
    }

    [Fact]
    public void MetadataCommitsPreserveRejectedLengthSessionAndConflict()
    {
        var s = new Session();
        var id = s.Create(AutoNameKind.Force, 900);
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = "700";
        Assert.False(s.Editor.DimensionLength.Confirm());
        var conflict = s.Editor.ConstraintConflict;
        var analysis = s.Editor.Presentation;
        s.Edit(AutoNameKind.Force, id);
        s.NameText = "Renamed during conflict";
        Assert.True(s.Confirm());
        s.Editor.SetAnnotationOffset(id, new(1, 2));
        Assert.Same(conflict, s.Editor.ConstraintConflict);
        Assert.True(s.Editor.DimensionLength.IsEditing);
        Assert.True(s.Editor.DimensionLength.HasError);
        Assert.Equal("700", s.Editor.DimensionLength.Text);
        Assert.Same(analysis, s.Editor.Presentation);
        Assert.Equal(2, s.Calls);
    }

    [Fact]
    public void EquivalentNewSetupObjectsCommitMetadataWithoutAnalysis()
    {
        var s = new Session();
        var document = s.Editor.Document;
        var material = new Material("Alternate name", document.Material.YoungsModulus, document.Material.YieldStrength,
            document.Material.Density, document.Material.PoissonRatio);
        var original = Assert.IsType<SpanDraft.Core.Sections.Parametric.RectangularHollowSectionGeometry>(document.Section);
        var section = new SpanDraft.Core.Sections.Parametric.RectangularHollowSectionGeometry(
            original.Width, original.Height, original.WallThickness, original.OuterRadius);
        Assert.NotSame(original, section);
        var analysis = s.Editor.Presentation;
        s.Editor.ApplySetup(section, document.BendingAxis, material);
        Assert.Same(material, s.Editor.Document.Material);
        Assert.Same(section, s.Editor.Document.Section);
        Assert.Same(analysis, s.Editor.Presentation);
        Assert.Equal(1, s.Calls);
    }
}
