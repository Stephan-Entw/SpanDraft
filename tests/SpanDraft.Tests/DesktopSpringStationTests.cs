using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// A translational and a rotational spring are two supports of their own, also at one station. They are joined only when the
/// Core model is built, and nothing joins them in the editor, the flyout or the file.
/// </summary>
public sealed class DesktopSpringStationTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));

    private static SpringData T(double k) => new(TranslationalStiffness.FromNewtonsPerMeter(k), null);
    private static SpringData R(double k, bool rigid = false) => new(null, RotationalStiffness.FromNewtonMetersPerRadian(k), rigid);

    private static readonly Guid WId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static EditorSupport Pinned(double mm) => new(Guid.NewGuid(), Mm(mm), SupportType.Pinned, "P");
    private static EditorSupport Roller(double mm) => new(Guid.NewGuid(), Mm(mm), SupportType.Roller, "R");
    private static EditorSupport W(double mm, double k = 1e5) => new(WId, Mm(mm), SupportType.Spring, "W", T(k));
    private static EditorSupport D(double mm, double k = 1e4, bool rigid = false) => new(DId, Mm(mm), SupportType.Spring, "D", R(k, rigid));

    private static EditorViewModel Editor(IEnumerable<EditorSupport> supports, params EditorMomentRelease[] releases) => new(
        new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, supports).WithMomentReleases(releases),
        () => { }, BeamAnalysis.Analyze);

    private static EditorViewModel Station() => Editor([Pinned(0), Roller(1000), W(500), D(500)]);

    private static void Drag(EditorViewModel editor, Guid id, double mm)
    {
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Mm(mm));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
    }

    [Fact]
    public void ATranslationalAndARotationalSpringAtOneStationAreTwoEditorSupportsAndOneCoreSupport()
    {
        var editor = Station();
        var supports = editor.Document.Supports;
        Assert.Equal(4, supports.Count);
        Assert.NotEqual(supports[2].Id, supports[3].Id);
        Assert.NotEqual(supports[2].Name, supports[3].Name);
        Assert.Equal(T(1e5), supports[2].Spring);
        Assert.Equal(R(1e4), supports[3].Spring);
        var core = editor.Document.ToBeamModel().Supports;
        // The one Core support stands where the first of its two editor supports stands.
        Assert.Equal(3, core.Count);
        Assert.Equal([SupportType.Pinned, SupportType.Roller, SupportType.Spring], core.Select(s => s.Type));
        Assert.Equal(1e5, core[2].TranslationalSpring!.Value.NewtonsPerMeter);
        Assert.Equal(1e4, core[2].RotationalSpring!.Value.NewtonMetersPerRadian);
        Assert.Equal(Mm(500), core[2].Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreSupportMappingPreservesDocumentOrderForEitherSpringOrder(bool reverse)
    {
        EditorSupport[] springs = reverse ? [D(500), W(500)] : [W(500), D(500)];
        EditorSupport[] supports = [Pinned(0), springs[0], Roller(1000), springs[1]];
        var core = SupportStations.ToCore(supports);
        Assert.Equal(3, core.Count);
        Assert.Equal(new[] { supports[0].Id }, core[0].EditorIds);
        Assert.Equal(reverse ? new[] { DId, WId } : new[] { WId, DId }, core[1].EditorIds);
        Assert.Equal(new[] { supports[2].Id }, core[2].EditorIds);
        Assert.Equal(SupportType.Spring, core[1].Support.Type);
        Assert.Equal(Mm(500), core[1].Support.Position);
        Assert.Equal(1e5, core[1].Support.TranslationalSpring!.Value.NewtonsPerMeter);
        Assert.Equal(1e4, core[1].Support.RotationalSpring!.Value.NewtonMetersPerRadian);
    }

    [Fact]
    public void BothSpringsOfAStationAreHitSeparatelyAtTheirOwnStrokes()
    {
        var v = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        EditorSupport[] supports = [W(500), D(500)];
        double beamY = v.Viewport.BeamY, x = 500;
        var transform = v.Layout.Transform;
        // The zigzag below the ring on the axis, and the stem and the outer turn of the spiral beside it.
        Assert.Equal(WId, SupportSymbol.HitTest(supports, transform, beamY, x, beamY + 5));
        Assert.Equal(DId, SupportSymbol.HitTest(supports, transform, beamY, x - SpringSupportGeometry.SpiralOuterRadius, beamY + 14));
        Assert.Equal(DId, SupportSymbol.HitTest(supports, transform, beamY, x + SpringSupportGeometry.SpiralOuterRadius, beamY));
        // The ground line, the hatches and the ring belong to both: no spring is chosen by the order of the list.
        Assert.Null(SupportSymbol.HitTest(supports, transform, beamY, x + 15, beamY + SchematicMetrics.SupportGroundY));
        Assert.Null(SupportSymbol.HitTest(supports.Reverse().ToArray(), transform, beamY, x + 15, beamY + SchematicMetrics.SupportGroundY));
        // Either spring alone is found wherever its symbol is.
        Assert.Equal(DId, SupportSymbol.HitTest([supports[1]], transform, beamY, x + 15, beamY + SchematicMetrics.SupportGroundY));
    }

    [Fact]
    public void MovingOneSpringAwayGivesTwoSupportsAtTwoPositionsAndMovingItBackJoinsThemAgainOnlyForCore()
    {
        var editor = Station();
        Drag(editor, DId, 700);
        var supports = editor.Document.Supports;
        Assert.Equal(Mm(500), supports.Single(s => s.Id == WId).Position);
        Assert.Equal(Mm(700), supports.Single(s => s.Id == DId).Position);
        Assert.Equal(4, editor.Document.ToBeamModel().Supports.Count);

        Drag(editor, DId, 500);
        Assert.Equal(4, editor.Document.Supports.Count);
        Assert.Equal(3, editor.Document.ToBeamModel().Supports.Count);
        // Neither spring took anything from the other.
        Assert.Equal(T(1e5), editor.Document.Supports.Single(s => s.Id == WId).Spring);
        Assert.Equal(R(1e4), editor.Document.Supports.Single(s => s.Id == DId).Spring);
        Assert.Equal("W", editor.Document.Supports.Single(s => s.Id == WId).Name);
        Assert.Equal("D", editor.Document.Supports.Single(s => s.Id == DId).Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeletingOneSpringOfAStationLeavesTheOtherUnchanged(bool deleteTranslational)
    {
        var editor = Station();
        var (deleted, kept) = deleteTranslational ? (WId, DId) : (DId, WId);
        var before = editor.Document.Supports.Single(s => s.Id == kept);
        Assert.True(editor.EditSupport(deleted));
        editor.DeleteSupport();
        Assert.Equal(3, editor.Document.Supports.Count);
        Assert.DoesNotContain(editor.Document.Supports, s => s.Id == deleted);
        Assert.Equal(before, editor.Document.Supports.Single(s => s.Id == kept));
        Assert.Equal(3, editor.Document.ToBeamModel().Supports.Count);
    }

    [Fact]
    public void EditingASpringShowsOnlyItsOwnFieldAndNeverACombinedType()
    {
        var editor = Station();
        foreach (var (id, kind) in new[] { (WId, SpringKind.Translational), (DId, SpringKind.Rotational) })
        {
            Assert.True(editor.EditSupport(id));
            var draft = editor.SupportDraft!;
            Assert.Equal(kind, draft.Kind);
            Assert.Equal(kind == SpringKind.Translational, draft.HasTranslational);
            Assert.Equal(kind == SpringKind.Rotational, draft.HasRotational);
            Assert.Equal([null, null, null, SpringKind.Translational, SpringKind.Rotational], draft.Types.Select(o => o.Kind));
            Assert.True(draft.IsValid);
            editor.CancelSupportInteraction();
        }
    }

    [Fact]
    public void ChangingTheTypeOfOneSpringToTheKindOfTheOtherIsRefused()
    {
        var editor = Station();
        Assert.True(editor.EditSupport(WId));
        var draft = editor.SupportDraft!;
        draft.RotationalText = "10";
        draft.SelectedType = draft.Types.Single(o => o.Kind == SpringKind.Rotational);
        Assert.False(draft.IsValid);
        Assert.Equal(Strings.SupportAlreadyExists, draft.ErrorText);
        Assert.False(editor.ConfirmSupport());
        editor.CancelSupportInteraction();

        Assert.True(editor.EditSupport(DId));
        draft = editor.SupportDraft!;
        draft.TranslationalText = "1000";
        draft.SelectedType = draft.Types.Single(o => o.Kind == SpringKind.Translational);
        Assert.False(draft.IsValid);
        Assert.Equal(Strings.SupportAlreadyExists, draft.ErrorText);
        Assert.False(editor.ConfirmSupport());
        // Nothing was committed.
        Assert.Equal(T(1e5), editor.Document.Supports.Single(s => s.Id == WId).Spring);
        Assert.Equal(R(1e4), editor.Document.Supports.Single(s => s.Id == DId).Spring);
    }

    [Fact]
    public void ARigidTranslationIsRefusedWhereATranslationalSpringStands()
    {
        var editor = Station();
        Assert.True(editor.EditSupport(DId));
        var draft = editor.SupportDraft!;
        Assert.False(draft.CanRigidTranslation);
        draft.RigidTranslation = true;
        Assert.False(draft.RigidTranslation);
        editor.CancelSupportInteraction();

        // A rigid rotational spring alone, moved onto the translational spring, is refused the same way.
        editor = Editor([Pinned(0), Roller(1000), W(500), D(700, rigid: true)]);
        Assert.True(editor.BeginSupportDrag(DId));
        editor.UpdateSupportDrag(Mm(500));
        Assert.True(editor.Preview!.IsInvalid);
        Assert.Equal(Strings.SupportAlreadyExists, editor.SupportFeedback);
        Assert.False(editor.EndSupportDrag());
        // ... and so is typing the position of the translational spring into its flyout.
        editor.CancelSupportInteraction();
        Assert.True(editor.EditSupport(DId));
        editor.SupportDraft!.PositionText = InputQuantityFormatter.FormatExact(0.5, UnitProfile.Default[QuantityKind.BeamLength]);
        Assert.False(editor.SupportDraft.IsValid);
        Assert.Equal(Strings.SupportAlreadyExists, editor.SupportDraft.ErrorText);
    }

    [Fact]
    public void PlacingTheSecondSpringOnTheStationOfTheOtherKindIsAllowedAndEveryOtherSharedStationIsRefused()
    {
        var editor = Editor([Pinned(0), Roller(1000), W(500)]);
        editor.ToggleSpringTool(SpringKind.Rotational);
        editor.HoverPlacement(Mm(500));
        Assert.False(editor.Preview!.IsInvalid);
        Assert.NotNull(editor.Preview.Spring!.Rotational);
        Assert.True(editor.PlaceSupport());
        var draft = editor.SupportDraft!;
        // A support of its own: a new one, with the next name.
        Assert.Null(draft.OriginalId);
        Assert.Equal("A", draft.NameText);
        draft.RotationalText = "10";
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(4, editor.Document.Supports.Count);
        Assert.Equal(3, editor.Document.ToBeamModel().Supports.Count);

        // A third support, two springs of one kind, a spring beside a rigid support: all refused, as before.
        foreach (var (tool, kind, mm) in new (SupportType?, SpringKind?, double)[]
        {
            (SupportType.Roller, null, 500), (SupportType.Fixed, null, 500), (SupportType.Spring, SpringKind.Translational, 500),
            (SupportType.Spring, SpringKind.Rotational, 500), (SupportType.Spring, SpringKind.Translational, 0),
            (SupportType.Spring, SpringKind.Rotational, 1000)
        })
        {
            if (kind is { } springKind) editor.ToggleSpringTool(springKind); else editor.ToggleSupportTool(tool!.Value);
            editor.HoverPlacement(Mm(mm));
            Assert.True(editor.Preview!.IsInvalid);
            Assert.Equal(Strings.SupportAlreadyExists, editor.SupportFeedback);
            Assert.False(editor.PlaceSupport());
            editor.CancelSupportInteraction();
        }
    }

    [Fact]
    public void ARotationalSpringCannotJoinATranslationalSpringAtAMomentReleaseButATranslationalOneCan()
    {
        var release = new EditorMomentRelease(Guid.NewGuid(), Mm(500), "G");
        var editor = Editor([Pinned(0), Roller(1000), W(500)], release);
        editor.ToggleSpringTool(SpringKind.Rotational);
        editor.HoverPlacement(Mm(500));
        Assert.True(editor.Preview!.IsInvalid);
        Assert.Equal(Strings.SupportAtMomentRelease, editor.SupportFeedback);
        Assert.False(editor.PlaceSupport());
        editor.CancelSupportInteraction();

        editor = Editor([Pinned(0), Roller(1000), new EditorSupport(WId, Mm(700), SupportType.Spring, "W", T(1e5))], release);
        editor.ToggleSpringTool(SpringKind.Translational);
        editor.HoverPlacement(Mm(500));
        Assert.False(editor.Preview!.IsInvalid);
    }

    [Fact]
    public void ASpringAndARigidSupportOrTwoSpringsOfOneKindAtOnePositionAreConflictsOfTheDocument()
    {
        var steel = new EditorSegment(Steel, new RectangleSectionGeometry(Mm(100), Mm(200)), SectionAxisDesignation.Y) { Length = Mm(1000) };
        foreach (var extra in new EditorSupport[]
        {
            new(Guid.NewGuid(), Mm(500), SupportType.Roller, "X"),
            new(Guid.NewGuid(), Mm(500), SupportType.Spring, "X", T(2e5)),
            new(Guid.NewGuid(), Mm(500), SupportType.Spring, "X", R(2e4)),
            new(Guid.NewGuid(), Mm(500), SupportType.Spring, "X", R(2e4, true))
        })
        {
            var document = new EditorDocument([steel], [Pinned(0), W(500), D(500), extra]);
            var change = document.ResizeSegment(document.Segments[0].Id, Mm(900));
            Assert.False(change.IsAccepted);
            Assert.Contains(change.Conflicts, c => c.EntityName == "X");
        }
        // A rigid rotational spring does not join a translational one.
        var rigidPair = new EditorDocument([steel], [Pinned(0), W(500), D(500, rigid: true)]);
        Assert.False(rigidPair.ResizeSegment(rigidPair.Segments[0].Id, Mm(900)).IsAccepted);
    }

    [Fact]
    public void TheTwoSpringsKeepTheirOwnPositionReferencesAndSeparateWhenOneSegmentChanges()
    {
        EditorSegment s1 = new(Steel, new RectangleSectionGeometry(Mm(100), Mm(200)), SectionAxisDesignation.Y)
        { Length = Mm(2000), Id = Guid.NewGuid() };
        EditorSegment s2 = s1 with { Length = Mm(3000), Id = Guid.NewGuid() };
        // A at x = 2 m from the beam start, B at the end of the first segment, which is also at x = 2 m.
        var a = new EditorSupport(WId, Mm(2000), SupportType.Spring, "A", T(1e5));
        var b = new EditorSupport(DId, Mm(2000), SupportType.Spring, "B", R(1e4)) { Reference = new PositionRef.AtSegmentEnd(s1.Id) };
        var document = new EditorDocument([s1, s2], [Pinned(0), a, b]);
        Assert.Equal(2, document.ToBeamModel().Supports.Count);

        var changed = document.ResizeSegment(s1.Id, Mm(1500));
        Assert.True(changed.IsAccepted);
        var after = changed.Document!;
        // The one that is bound to the segment goes with it, the other stays; nobody took the other's reference.
        Assert.Equal(Mm(2000), after.Supports.Single(s => s.Id == WId).Position);
        Assert.Equal(Mm(1500), after.Supports.Single(s => s.Id == DId).Position);
        Assert.Null(after.Supports.Single(s => s.Id == WId).Reference);
        Assert.Equal(new PositionRef.AtSegmentEnd(s1.Id), after.Supports.Single(s => s.Id == DId).Reference);
        Assert.Equal(3, after.ToBeamModel().Supports.Count);
    }

    [Fact]
    public void TwoSpringsAtOneStationRoundtripThroughTheFileWithEverythingTheyOwn()
    {
        EditorSegment s1 = new(Steel, new RectangleSectionGeometry(Mm(100), Mm(200)), SectionAxisDesignation.Y)
        { Length = Mm(2000), Id = Guid.Parse("33333333-3333-3333-3333-333333333333") };
        EditorSegment s2 = s1 with { Length = Mm(3000), Id = Guid.Parse("44444444-4444-4444-4444-444444444444") };
        var a = new EditorSupport(WId, Mm(2000), SupportType.Spring, "B", T(1234567.8901234567))
       ;
        var b = new EditorSupport(DId, Mm(2000), SupportType.Spring, "C", R(0.1)) { Reference = new PositionRef.AtSegmentEnd(s1.Id) };
        var document = new EditorDocument([s1, s2], [new(Guid.Parse("55555555-5555-5555-5555-555555555555"), Mm(0),
            SupportType.Pinned, "A"), a, b], namingState: new NamingState(4));
        var state = new ProjectState(document, new EditorPresentationState());

        byte[] bytes = ProjectFileCodec.Serialize(state);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        Assert.Equal(document.Supports, loaded.Document.Supports);
        Assert.Equal(4, loaded.Document.NamingState.NextSupportOrdinal);
        var wire = System.Text.Json.Nodes.JsonNode.Parse(bytes)!["document"]!["supports"]!.AsArray();
        // Two entries, one stiffness each, and the file is as it was before: an own id, name and reference per spring.
        Assert.Equal(3, wire.Count);
        Assert.Equal(["id", "name", "type", "position", "translationalStiffness"],
            wire[1]!.AsObject().Select(p => p.Key).Where(k => k != "positionRef"));
        Assert.Equal(["id", "name", "type", "positionRef", "rotationalStiffness"], wire[2]!.AsObject().Select(p => p.Key).Where(k => k != "position"));
        Assert.Equal(BitConverter.DoubleToInt64Bits(1234567.8901234567), BitConverter.DoubleToInt64Bits(
            loaded.Document.Supports.Single(s => s.Id == WId).Spring!.Translational!.Value.NewtonsPerMeter));
        // After reading, the two are still two supports that Core sees as one.
        Assert.Equal(2, loaded.Document.ToBeamModel().Supports.Count);
        Assert.Equal(["B", "C"], loaded.Document.Supports.Where(s => s.Type == SupportType.Spring).Select(s => s.Name));
    }

    [Fact]
    public void ATranslationalAndARotationalSpringOfAnOldFileAreReadAsTwoSupportsAndTheSecondOneGetsADerivedStableIdentity()
    {
        // The old shape: one spring entry with both stiffnesses, followed by a name the next free one must skip.
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "V5", "spring-supports.spandraft")))!;
        var loaded = ProjectFileCodec.Deserialize(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        var d = loaded.Document.Supports.Single(s => s.Name == "D");
        var second = loaded.Document.Supports.Single(s => s.Name == "F");
        Assert.Equal(d.Position, second.Position);
        Assert.Equal(d.Reference, second.Reference);
        Assert.Equal(1.5e6, d.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(d.Spring.Rotational);
        Assert.Equal(8e3, second.Spring!.Rotational!.Value.NewtonMetersPerRadian);
        // The new identity is not used by any entity of the file and is the same on every reading.
        var ids = loaded.Document.EntityIds.ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEqual(Guid.Empty, second.Id);
        var again = ProjectFileCodec.Deserialize(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        Assert.True(loaded.ContentEquals(again));
        // Saving writes the two as two entries, and reading that gives the same project.
        var saved = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded));
        Assert.True(loaded.ContentEquals(saved));
        var wire = System.Text.Json.Nodes.JsonNode.Parse(ProjectFileCodec.Serialize(loaded))!["document"]!["supports"]!.AsArray();
        Assert.DoesNotContain(wire, s => s!["translationalStiffness"] is not null && s["rotationalStiffness"] is not null);
    }

    [Fact]
    public void ADerivedIdentityAvoidsEveryIdOfTheProjectAndTheEmptyId()
    {
        var original = Guid.NewGuid();
        var first = SpringSupportSplit.Derive(original, new HashSet<Guid>());
        Assert.Equal(first, SpringSupportSplit.Derive(original, new HashSet<Guid>()));
        Assert.NotEqual(original, first);
        var second = SpringSupportSplit.Derive(original, new HashSet<Guid> { first });
        Assert.NotEqual(first, second);
        Assert.NotEqual(Guid.Empty, second);
    }
}
