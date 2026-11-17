using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Placing and editing spring supports: the draft buffer, its validation and the commit.</summary>
public sealed class DesktopSpringSupportEditTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);

    private static EditorViewModel Editor(params EditorSupport[] supports) => new(
        new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            supports.Length == 0 ? [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(1000), SupportType.Roller, "B")] : supports),
        () => { }, BeamAnalysis.Analyze);

    private static SupportDraftViewModel PlaceSpring(EditorViewModel editor, SpringKind kind = SpringKind.Translational, double mm = 400)
    {
        editor.ToggleSpringTool(kind);
        editor.HoverPlacement(Mm(mm));
        Assert.True(editor.PlaceSupport());
        return editor.SupportDraft!;
    }

    private static SupportTypeOption Option(SupportDraftViewModel draft, SpringKind kind) => draft.Types.Single(o => o.Kind == kind);

    [Fact]
    public void ATranslationalSpringStartsWithItsEditableSiDefaultAndPreviewsTheTranslationalSymbol()
    {
        var editor = Editor();
        editor.ToggleSpringTool(SpringKind.Translational);
        Assert.True(editor.IsTranslationalSpringTool);
        Assert.False(editor.IsRotationalSpringTool);
        editor.HoverPlacement(Mm(400));
        Assert.Equal(SupportType.Spring, editor.Preview!.Type);
        Assert.NotNull(editor.Preview.Spring!.Translational);
        Assert.Equal(1e6, editor.Preview.Spring.Translational!.Value.NewtonsPerMeter);
        Assert.Null(editor.Preview.Spring.Rotational);
        Assert.True(editor.PlaceSupport());
        var draft = editor.SupportDraft!;
        Assert.True(draft.IsSpring);
        Assert.Equal(SpringKind.Translational, draft.Kind);
        Assert.True(draft.HasTranslational);
        Assert.False(draft.HasRotational);
        Assert.False(draft.CanRigidTranslation);
        Assert.Equal("1000", draft.TranslationalText);
        Assert.Equal("", draft.RotationalText);
        Assert.True(draft.IsValid);
        Assert.Null(draft.TranslationalError);
        Assert.False(editor.Preview!.IsInvalid);
        Assert.Equal(1e6, draft.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(editor.Preview.Spring!.Rotational);
        Assert.Equal(2, editor.Document.Supports.Count);
        Assert.Empty(editor.Session.UndoHistory);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(1e6, editor.Document.Supports.Single(s => s.Type == SupportType.Spring).Spring!.Translational!.Value.NewtonsPerMeter);
    }

    [Fact]
    public void ARotationalSpringStartsWithItsEditableSiDefaultAndPreviewsTheRotationalSymbol()
    {
        var editor = Editor();
        editor.ToggleSpringTool(SpringKind.Rotational);
        Assert.True(editor.IsRotationalSpringTool);
        Assert.False(editor.IsTranslationalSpringTool);
        editor.HoverPlacement(Mm(400));
        Assert.Equal(SupportType.Spring, editor.Preview!.Type);
        Assert.Null(editor.Preview.Spring!.Translational);
        Assert.NotNull(editor.Preview.Spring.Rotational);
        Assert.Equal(1000, editor.Preview.Spring.Rotational!.Value.NewtonMetersPerRadian);
        Assert.True(editor.PlaceSupport());
        var draft = editor.SupportDraft!;
        Assert.Equal(SpringKind.Rotational, draft.Kind);
        Assert.False(draft.HasTranslational);
        Assert.True(draft.HasRotational);
        Assert.True(draft.CanRigidTranslation);
        Assert.Equal("1000", draft.RotationalText);
        Assert.Equal("", draft.TranslationalText);
        Assert.True(draft.IsValid);
        Assert.Null(draft.RotationalError);
        Assert.False(editor.Preview!.IsInvalid);
        Assert.Equal(1000, draft.Spring!.Rotational!.Value.NewtonMetersPerRadian);
        Assert.Null(editor.Preview!.Spring!.Translational);
        Assert.Equal(2, editor.Document.Supports.Count);
        Assert.Empty(editor.Session.UndoHistory);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(1000, editor.Document.Supports.Single(s => s.Type == SupportType.Spring).Spring!.Rotational!.Value.NewtonMetersPerRadian);
    }

    [Theory]
    [InlineData(SpringKind.Translational)]
    [InlineData(SpringKind.Rotational)]
    public void NewSpringDefaultsAreConvertedToTheSelectedUnitsAndRemainEditable(SpringKind kind)
    {
        var profile = UnitProfile.Default.WithUnit(QuantityKind.TranslationalStiffness, UnitCatalog.KilonewtonPerMillimeter)
            .WithUnit(QuantityKind.RotationalStiffness, UnitCatalog.KilonewtonMeterPerRadian);
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { }, resultPresentation: new(profile));
        var draft = PlaceSpring(editor, kind);
        bool translational = kind == SpringKind.Translational;
        Assert.Equal(translational ? "kN/mm" : "kN·m/rad", translational ? draft.TranslationalUnit : draft.RotationalUnit);
        Assert.Equal("1", translational ? draft.TranslationalText : draft.RotationalText);
        Assert.Equal(translational ? 1e6 : 1000, translational
            ? draft.Spring!.Translational!.Value.NewtonsPerMeter : draft.Spring!.Rotational!.Value.NewtonMetersPerRadian);
        if (translational) draft.TranslationalText = "2"; else draft.RotationalText = "2";
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        var support = Assert.Single(editor.Document.Supports);
        Assert.Equal(translational ? 2e6 : 2000, translational
            ? support.Spring!.Translational!.Value.NewtonsPerMeter : support.Spring!.Rotational!.Value.NewtonMetersPerRadian);
    }

    [Fact]
    public void TheTwoSpringToolsAreCheckedIndependentlyAndAreSingleShot()
    {
        var editor = Editor();
        editor.ToggleSpringTool(SpringKind.Translational);
        Assert.True(editor.IsTranslationalSpringTool); Assert.False(editor.IsRotationalSpringTool);
        editor.ToggleSpringTool(SpringKind.Rotational);
        Assert.False(editor.IsTranslationalSpringTool); Assert.True(editor.IsRotationalSpringTool);
        editor.ToggleSpringTool(SpringKind.Rotational);
        Assert.False(editor.IsTranslationalSpringTool); Assert.False(editor.IsRotationalSpringTool);
        Assert.Null(editor.PlacementTool);
        editor.ToggleSpringTool(SpringKind.Translational);
        editor.ToggleSupportTool(SupportType.Roller);
        Assert.False(editor.IsTranslationalSpringTool); Assert.True(editor.IsRollerTool);
        editor.ToggleSpringTool(SpringKind.Rotational);
        Assert.False(editor.IsRollerTool);
        editor.HoverPlacement(Mm(400));
        Assert.True(editor.PlaceSupport());
        editor.SupportDraft!.RotationalText = "30";
        Assert.True(editor.ConfirmSupport());
        Assert.False(editor.IsRotationalSpringTool);
        Assert.False(editor.IsTranslationalSpringTool);
    }

    [Fact]
    public void ASpringSupportCannotBePlacedWithoutAnExplicitTranslationalOrRotationalKind()
    {
        var editor = Editor();
        Assert.Throws<ArgumentException>(() => editor.ToggleSupportTool(SupportType.Spring));
        Assert.Null(editor.PlacementTool);
        Assert.Throws<ArgumentException>(() => new SupportDraftViewModel(() => editor.Document, null, SupportType.Spring, Mm(400),
            UnitProfile.Default, null, null));
        // A rigid support draft needs no kind; a spring is offered only as a translational or a rotational spring.
        var draft = new SupportDraftViewModel(() => editor.Document, null, SupportType.Pinned, Mm(400));
        Assert.Equal([null, null, null, SpringKind.Translational, SpringKind.Rotational], draft.Types.Select(o => o.Kind));
    }

    [Fact]
    public void ConfirmingCommitsTheTranslationalStiffnessInSiAndTheCommittedDocumentHoldsTheSpring()
    {
        var editor = Editor();
        var draft = PlaceSpring(editor);
        draft.TranslationalText = InputQuantityFormatter.FormatExact(2.5e6, UnitProfile.Default[QuantityKind.TranslationalStiffness]);
        Assert.True(draft.IsValid);
        Assert.Equal(2.5e6, draft.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.True(editor.ConfirmSupport());
        var spring = Assert.Single(editor.Document.Supports, s => s.Type == SupportType.Spring);
        Assert.Equal(Mm(400), spring.Position);
        Assert.Equal(2.5e6, spring.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(spring.Spring.Rotational);
        Assert.False(spring.Spring.RigidTranslation);
        Assert.True(editor.Presentation.IsSuccess);
        // The spring takes load: its reaction is the force -k w.
        var node = editor.Presentation.Result!.Solution.Nodes.Single(n => n.Position == Mm(400));
        Assert.NotNull(node.ReactionY);
        Assert.NotNull(node.ReactionX);
        Assert.Null(node.ReactionMoment);
    }

    [Fact]
    public void ConfirmingCommitsTheRotationalStiffnessInSiWithoutATranslationalSpring()
    {
        var editor = Editor();
        var draft = PlaceSpring(editor, SpringKind.Rotational);
        draft.RotationalText = "30";
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        var spring = Assert.Single(editor.Document.Supports, s => s.Type == SupportType.Spring);
        Assert.Equal(30, spring.Spring!.Rotational!.Value.NewtonMetersPerRadian);
        Assert.Null(spring.Spring.Translational);
        Assert.False(spring.Spring.RigidTranslation);
        Assert.True(editor.Presentation.IsSuccess);
        Assert.NotNull(editor.Presentation.Result!.Solution.Nodes.Single(n => n.Position == Mm(400)).ReactionMoment);
    }

    [Theory]
    [InlineData(SpringKind.Translational, "")]
    [InlineData(SpringKind.Translational, "0")]
    [InlineData(SpringKind.Translational, "-5")]
    [InlineData(SpringKind.Translational, "abc")]
    [InlineData(SpringKind.Translational, "1e999")]
    [InlineData(SpringKind.Rotational, "")]
    [InlineData(SpringKind.Rotational, "0")]
    [InlineData(SpringKind.Rotational, "-5")]
    [InlineData(SpringKind.Rotational, "abc")]
    [InlineData(SpringKind.Rotational, "1e999")]
    public void ANonPositiveOrUnreadableStiffnessIsRejectedAtItsField(SpringKind kind, string text)
    {
        var draft = PlaceSpring(Editor(), kind);
        if (kind == SpringKind.Translational) draft.TranslationalText = text; else draft.RotationalText = text;
        Assert.False(draft.IsValid);
        Assert.True(kind == SpringKind.Translational ? draft.HasTranslationalError : draft.HasRotationalError);
        Assert.Null(draft.Spring);
    }

    [Fact]
    public void RigidTranslationIsOnlyOfferedForARotationalSpringAndResetsWhenTheKindChanges()
    {
        var draft = PlaceSpring(Editor());
        Assert.False(draft.CanRigidTranslation);
        draft.RigidTranslation = true;
        Assert.False(draft.RigidTranslation);
        draft.SelectedType = Option(draft, SpringKind.Rotational);
        Assert.True(draft.CanRigidTranslation);
        draft.RigidTranslation = true;
        Assert.True(draft.RigidTranslation);
        draft.RotationalText = "30";
        Assert.True(draft.Spring!.RigidTranslation);
        draft.SelectedType = Option(draft, SpringKind.Translational);
        Assert.False(draft.RigidTranslation);
        Assert.False(draft.CanRigidTranslation);
        draft.TranslationalText = "1000";
        Assert.False(draft.Spring!.RigidTranslation);
        draft.SelectedType = Option(draft, SpringKind.Rotational);
        Assert.False(draft.RigidTranslation);
        Assert.False(draft.Spring!.RigidTranslation);
    }

    [Fact]
    public void SwitchingBetweenTheOfferedTypesKeepsNoStaleOrInadmissibleSpringData()
    {
        var editor = Editor();
        var draft = PlaceSpring(editor);
        draft.TranslationalText = "2500";
        draft.SelectedType = Option(draft, SpringKind.Rotational);
        // The typed text is kept, but only the chosen spring is built.
        Assert.Equal("2500", draft.TranslationalText);
        Assert.False(draft.HasTranslational);
        Assert.False(draft.IsValid);
        Assert.Null(draft.Spring);
        draft.RotationalText = "40";
        Assert.Null(draft.Spring!.Translational);
        Assert.Equal(40, draft.Spring.Rotational!.Value.NewtonMetersPerRadian);
        Assert.Null(editor.Preview!.Spring!.Translational);
        draft.Type = SupportType.Pinned;
        Assert.Null(draft.Spring);
        Assert.Null(editor.Preview!.Spring);
        draft.SelectedType = Option(draft, SpringKind.Translational);
        Assert.Equal(SpringKind.Translational, draft.Kind);
        Assert.Equal(2.5e6, draft.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(draft.Spring.Rotational);
        Assert.Equal(SupportType.Spring, draft.SelectedType!.Type);
    }

    [Theory]
    [InlineData(SpringKind.Translational, false)]
    [InlineData(SpringKind.Translational, true)]
    [InlineData(SpringKind.Rotational, false)]
    [InlineData(SpringKind.Rotational, true)]
    public void EditingAnExistingOrLoadedSpringKeepsItsKindAndExactInputValue(SpringKind kind, bool loaded)
    {
        var id = Guid.NewGuid();
        const double exact = 1234567.8901234567;
        bool translational = kind == SpringKind.Translational;
        var spring = translational ? new SpringData(TranslationalStiffness.FromNewtonsPerMeter(exact), null)
            : new SpringData(null, RotationalStiffness.FromNewtonMetersPerRadian(exact));
        var editor = Editor(new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(1000), SupportType.Roller, "B"),
            new(id, Mm(500), SupportType.Spring, "S", spring));
        var state = new ProjectState(editor.Document, new());
        if (loaded) state = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(state));
        var profile = UnitProfile.UnitedStates;
        editor = EditorViewModel.ForSession(ProjectSession.Create(state), () => { }, resultPresentation: new(profile));
        Assert.True(editor.EditSupport(id));
        var draft = editor.SupportDraft!;
        Assert.Equal(kind, draft.Kind);
        Assert.Equal(!translational, draft.HasRotational);
        string text = InputQuantityFormatter.FormatExact(exact,
            profile[translational ? QuantityKind.TranslationalStiffness : QuantityKind.RotationalStiffness]);
        Assert.Equal(text, translational ? draft.TranslationalText : draft.RotationalText);
        Assert.Equal(spring, draft.Spring);
        draft.NameText = "Changed";
        Assert.Equal(text, translational ? draft.TranslationalText : draft.RotationalText);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(spring, editor.Document.Supports.Single(s => s.Id == id).Spring);
    }

    [Fact]
    public void SwitchingAnExistingRigidSupportToASpringAndBackLeavesNoSpringData()
    {
        var editor = Editor();
        var id = editor.Document.Supports[0].Id;
        Assert.True(editor.EditSupport(id));
        var draft = editor.SupportDraft!;
        draft.SelectedType = Option(draft, SpringKind.Translational);
        Assert.True(draft.HasTranslational);
        Assert.False(draft.IsValid);
        draft.TranslationalText = "1000";
        Assert.True(draft.IsValid);
        draft.SelectedType = draft.Types.Single(o => o.Type == SupportType.Pinned);
        Assert.True(draft.IsValid);
        Assert.Null(draft.Spring);
        Assert.True(editor.ConfirmSupport());
        Assert.Null(editor.Document.Supports.Single(s => s.Id == id).Spring);
    }

    [Fact]
    public void DraggingAnExistingSpringKeepsItsSpringsAndTheSymbol()
    {
        var id = Guid.NewGuid();
        var editor = Editor(new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(1000), SupportType.Roller, "B"),
            new(id, Mm(500), SupportType.Spring, "S", new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4), true)));
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Mm(600));
        Assert.True(editor.Preview!.Spring!.RigidTranslation);
        Assert.NotNull(editor.Preview.Spring.Rotational);
        Assert.True(editor.EndSupportDrag());
        var draft = editor.SupportDraft!;
        Assert.Equal(SpringKind.Rotational, draft.Kind);
        Assert.Equal(Mm(600), draft.CanvasPosition);
        Assert.True(draft.RigidTranslation);
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(Mm(600), editor.Document.Supports.Single(s => s.Id == id).Position);
        Assert.True(editor.Document.Supports.Single(s => s.Id == id).Spring!.RigidTranslation);
    }

    [Fact]
    public void StiffnessTextFollowsTheSelectedDisplayUnits()
    {
        var profile = UnitProfile.StructuralEngineering;
        var draft = new SupportDraftViewModel(() => Editor().Document, null, SupportType.Spring, Mm(400), profile, null, SpringKind.Translational);
        Assert.Equal("kN/m", draft.TranslationalUnit);
        Assert.Equal("kN·m/rad", draft.RotationalUnit);
        draft.TranslationalText = "2500";
        Assert.Equal(2.5e6, draft.Spring!.Translational!.Value.NewtonsPerMeter);
    }

    [Fact]
    public void ASpringAtARigidSupportPositionIsRejectedLikeAnyOtherSupportPosition()
    {
        var editor = Editor();
        var draft = PlaceSpring(editor);
        draft.PositionText = InputQuantityFormatter.FormatExact(0, UnitProfile.Default[QuantityKind.BeamLength]);
        draft.TranslationalText = "1000";
        Assert.False(draft.IsValid);
        Assert.NotNull(draft.ErrorText);
    }
}
