using System.Globalization;
using SpanDraft.Analysis;
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

public sealed class DesktopLengthReferenceTests
{
    private static Length M(double value) => Length.FromMeters(value);

    private static EditorDocument Document(string kind)
    {
        var document = new EditorDocument(M(2), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")]);
        var end = new PositionRef.AtSegmentEnd(document.Segments[0].Id);
        return kind switch
        {
            "support" => document.WithSupports([.. document.Supports,
                new(Guid.NewGuid(), M(2), SupportType.Roller, "B") { Reference = end }]),
            "force" => document.WithLoads([new EditorPointForce(Guid.NewGuid(), M(2), Force.FromNewtons(-1000), "F1")
                { Reference = end }]),
            "moment" => document.WithLoads([new EditorPointMoment(Guid.NewGuid(), M(2), Moment.FromNewtonMeters(100), "M1")
                { Reference = end }]),
            "distributed" => document.WithDistributedLoads([new(Guid.NewGuid(), M(.5), M(2),
                ForcePerLength.FromNewtonsPerMeter(-1000), "q1") { EndReference = end }]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static (Guid Id, Length Position, PositionRef? Reference) Endpoint(EditorDocument document, string kind) => kind switch
    {
        "support" => (document.Supports[^1].Id, document.Supports[^1].Position, document.Supports[^1].Reference),
        "distributed" => (document.DistributedLoads[0].Id, document.DistributedLoads[0].EndPosition, document.DistributedLoads[0].EndReference),
        _ => (document.Loads[0].Id, document.Loads[0].Position, document.Loads[0].Reference)
    };

    private sealed class Observed
    {
        public int Analyses { get; private set; }
        public EditorViewModel Editor { get; }

        public Observed(EditorDocument document, EditorPresentationState? presentation = null)
        {
            Editor = EditorViewModel.ForSession(ProjectSession.Create(new(document, presentation ?? new())), () => { },
                beam => { Analyses++; return BeamAnalysis.Analyze(beam); });
        }
    }

    private static bool Confirm(EditorViewModel editor, double meters)
    {
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = (meters * 1000).ToString(CultureInfo.CurrentCulture);
        return editor.DimensionLength.Confirm();
    }

    [Theory]
    [InlineData("support", 1)] [InlineData("support", 3)]
    [InlineData("force", 1)] [InlineData("force", 3)]
    [InlineData("moment", 1)] [InlineData("moment", 3)]
    [InlineData("distributed", 1)] [InlineData("distributed", 3)]
    public void InlineLengthMovesEndBindingsInOneUndoableCommit(string kind, double meters)
    {
        var original = Document(kind);
        var before = Endpoint(original, kind);
        var presentation = new EditorPresentationState().WithOffset(before.Id, new(20, -10));
        var observed = new Observed(original, presentation);
        var editor = observed.Editor;

        Assert.Null(BeamLengthConstraints.Error(original, M(meters)));
        Assert.Empty(BeamLengthConstraints.BlockingEntities(original, M(meters)));
        Assert.True(Confirm(editor, meters));
        Assert.Equal(M(meters), editor.Document.Length);
        Assert.Equal((before.Id, M(meters), before.Reference), Endpoint(editor.Document, kind));
        Assert.Equal(original.Segments[0].Id, editor.Document.Segments[0].Id);
        Assert.Same(presentation, editor.EditorPresentation);
        Assert.Null(editor.ConstraintConflict);
        Assert.Single(editor.Session.UndoHistory);
        Assert.Equal(2, observed.Analyses);

        var changed = editor.Document;
        Assert.True(Confirm(editor, meters));
        Assert.Single(editor.Session.UndoHistory);
        Assert.Equal(2, observed.Analyses);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Document);
        Assert.Equal(before, Endpoint(editor.Document, kind));
        Assert.Equal(3, observed.Analyses);
        Assert.True(editor.Redo());
        Assert.Same(changed, editor.Document);
        Assert.Equal(4, observed.Analyses);
    }

    private static EditorViewModel AssertRejected(EditorDocument document, Guid blocker, string error)
    {
        var observed = new Observed(document);
        var editor = observed.Editor;
        var revision = editor.Session.CurrentRevision;

        Assert.Equal(error, BeamLengthConstraints.Error(document, M(1)));
        Assert.Equal(blocker, Assert.Single(BeamLengthConstraints.BlockingEntities(document, M(1))));
        Assert.False(Confirm(editor, 1));
        Assert.Equal(error, editor.DimensionLength.ErrorText);
        Assert.Equal(blocker, Assert.Single(editor.ConflictEntityIds));
        Assert.Same(revision, editor.Session.CurrentRevision);
        Assert.Empty(editor.Session.UndoHistory);
        Assert.Equal(1, observed.Analyses);
        return editor;
    }

    [Theory]
    [InlineData("support")] [InlineData("force")] [InlineData("moment")] [InlineData("distributed")]
    public void APositionAtTheOldEndWithoutAnEndBindingStillBlocksShortening(string kind)
    {
        var document = Document(kind);
        document = kind switch
        {
            "support" => document.WithSupports(document.Supports.Select(s => s with { Reference = null })),
            "distributed" => document.WithDistributedLoads(document.DistributedLoads.Select(l => l with { EndReference = null })),
            _ => document.WithLoads(document.Loads.Select(l => l with { Reference = null }))
        };
        AssertRejected(document, Endpoint(document, kind).Id,
            kind == "support" ? Strings.LengthExcludesSupports : Strings.LengthExcludesEntities);
    }

    [Fact]
    public void ADistancePastTheNewSegmentEndIsRejectedWithoutChangingItsReference()
    {
        var document = Document("force");
        document = document.WithLoads(document.Loads.Select(l => l with
        { Reference = new PositionRef.FromSegmentStart(document.Segments[0].Id, M(2)) }));
        AssertRejected(document, document.Loads[0].Id,
            string.Format(CultureInfo.CurrentCulture, Strings.SegmentChangeBlocked, "F1"));
    }

    [Theory]
    [InlineData(SupportType.Roller)] [InlineData(SupportType.Pinned)]
    public void ShorteningRejectsCoincidentSupportsOfTheSameOrDifferentTypes(SupportType type)
    {
        var document = Document("support");
        var later = new EditorSupport(Guid.NewGuid(), M(1), type, "C");
        document = document.WithSupports([.. document.Supports, later]);
        var change = document.ResizeSegment(document.Segments[0].Id, M(1));
        Assert.False(change.IsAccepted);
        Assert.Equal(later.Id, Assert.Single(change.Conflicts).EntityId);
        AssertRejected(document, later.Id, string.Format(CultureInfo.CurrentCulture, Strings.SegmentChangeBlocked, "C"));
    }

    [Fact]
    public void ShorteningRejectsACollapsedLineLoad()
    {
        var document = Document("distributed");
        document = document.WithDistributedLoads(document.DistributedLoads.Select(l => l with { StartPosition = M(1) }));
        AssertRejected(document, document.DistributedLoads[0].Id,
            string.Format(CultureInfo.CurrentCulture, Strings.SegmentChangeBlocked, "q1"));
    }

    [Fact]
    public void ShorteningRejectsAMomentReleaseAtTheNewBeamEnd()
    {
        var document = Document("support");
        var release = new EditorMomentRelease(Guid.NewGuid(), M(1), "G1");
        document = document.WithMomentReleases([release]);
        AssertRejected(document, release.Id, Strings.LengthExcludesEntities);
    }

    [Fact]
    public void MixedReferencesHighlightOnlyTheFixedBlockerAndCorrectionOrCancelClearsTheConflict()
    {
        var document = Document("distributed");
        var blocker = new EditorSupport(Guid.NewGuid(), M(1.5), SupportType.Roller, "B");
        document = document.WithSupports([.. document.Supports, blocker]);
        var editor = AssertRejected(document, blocker.Id, Strings.LengthExcludesSupports);

        editor.DimensionLength.Text = "1750";
        Assert.Null(editor.ConstraintConflict);
        Assert.Empty(editor.ConflictEntityIds);
        Assert.Same(document, editor.Document);
        editor.DimensionLength.Text = "1000";
        Assert.False(editor.DimensionLength.Confirm());
        editor.DimensionLength.Cancel();
        Assert.Null(editor.ConstraintConflict);
        Assert.Empty(editor.ConflictEntityIds);
        Assert.Same(document, editor.Document);
    }

    [Fact]
    public void ChangingTheDocumentReevaluatesTheRejectedLengthAgainstItsReferences()
    {
        var document = Document("support");
        var blocker = new EditorPointForce(Guid.NewGuid(), M(1.5), Force.FromNewtons(-1000), "F1");
        document = document.WithLoads([blocker]);
        var editor = AssertRejected(document, blocker.Id, Strings.LengthExcludesEntities);

        Assert.True(editor.Session.Commit(editor.Session.CurrentRevision.State with { Document = document.WithLoads([]) }));
        Assert.Null(editor.ConstraintConflict);
        Assert.Empty(editor.ConflictEntityIds);
        Assert.False(editor.DimensionLength.HasError);
        Assert.Equal(M(2), editor.Document.Length);
        Assert.Equal(M(2), editor.Document.Supports[^1].Position);
        Assert.True(editor.DimensionLength.Confirm());
        Assert.Equal(M(1), editor.Document.Length);
        Assert.Equal(M(1), editor.Document.Supports[^1].Position);
    }

    [Fact]
    public void TotalLengthResizesOnlyTheLastSegmentButInlineEditingRemainsDisabledForSeveral()
    {
        EditorSegment Segment(double length) => new(ProjectTemplates.Material, ProjectTemplates.Section, SectionAxisDesignation.Y)
            { Length = M(length) };
        var document = new EditorDocument([Segment(1), Segment(2)]);
        document = document.WithSupports([new(Guid.NewGuid(), M(0), SupportType.Fixed, "A"),
            new(Guid.NewGuid(), M(3), SupportType.Roller, "B")
                { Reference = new PositionRef.AtSegmentEnd(document.Segments[^1].Id) }]);

        var check = BeamLengthConstraints.Evaluate(document, M(2));
        var resized = Assert.IsType<EditorDocument>(check.Document);
        Assert.Null(check.ErrorText);
        Assert.Empty(check.BlockingEntityIds);
        Assert.Equal([M(1), M(1)], resized.Segments.Select(s => s.Length));
        Assert.Equal(M(2), resized.Length);
        Assert.Equal(M(2), resized.Supports[^1].Position);
        Assert.Equal(document.Segments.Select(s => s.Id), resized.Segments.Select(s => s.Id));
        Assert.Equal(Strings.LengthExcludesSegments, BeamLengthConstraints.Error(document, M(1)));
        Assert.Equal(Strings.InvalidLength, BeamLengthConstraints.Error(document, default));
        Assert.Empty(BeamLengthConstraints.BlockingEntities(document, default));

        var observed = new Observed(document);
        Assert.False(Confirm(observed.Editor, 2));
        Assert.Equal(Strings.LengthFromSegments, observed.Editor.DimensionLength.ErrorText);
        Assert.Same(document, observed.Editor.Document);
        Assert.Empty(observed.Editor.Session.UndoHistory);
        Assert.Equal(1, observed.Analyses);
    }

    [Fact]
    public void SetupAppliesLengthAndPropertiesTogetherAndKeepsTheEndBindingAndIdentity()
    {
        var original = Document("support");
        var before = original.Segments[0];
        var observed = new Observed(original);
        var editor = observed.Editor;
        var material = new Material("Changed steel", Pressure.FromPascals(200e9), Pressure.FromMegapascals(355), MaterialStrengthType.Re);
        var section = new RectangleSectionGeometry(M(.1), M(.2));
        var info = new ProjectInfo("Shortened beam", "Project", "Drawing", "Author");

        Assert.True(editor.ApplySetup(M(1), section, SectionAxisDesignation.Z, material, "Custom section", info));
        Assert.Equal(M(1), editor.Document.Length);
        Assert.Equal(M(1), editor.Document.Supports[^1].Position);
        Assert.Equal(original.Supports[^1].Reference, editor.Document.Supports[^1].Reference);
        Assert.Equal(before.Id, editor.Document.Segments[0].Id);
        Assert.Same(section, editor.Document.Section);
        Assert.Same(material, editor.Document.Material);
        Assert.Equal(SectionAxisDesignation.Z, editor.Document.BendingAxis);
        Assert.Equal("Custom section", editor.Document.SectionDisplayName);
        Assert.Equal(info, editor.Session.CurrentRevision.State.Info);
        Assert.Single(editor.Session.UndoHistory);
        Assert.Equal(2, observed.Analyses);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Document);
        Assert.Null(editor.Session.CurrentRevision.State.Info);
    }
}
