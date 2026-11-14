using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
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

    private static SupportDraftViewModel PlaceSpring(EditorViewModel editor, double mm = 400)
    {
        editor.ToggleSupportTool(SupportType.Spring);
        editor.HoverPlacement(Mm(mm));
        Assert.True(editor.PlaceSupport());
        return editor.SupportDraft!;
    }

    [Fact]
    public void ANewSpringStartsWithATranslationalSpringThatNeedsItsStiffnessAndPreviewsTheSymbol()
    {
        var editor = Editor();
        editor.ToggleSupportTool(SupportType.Spring);
        Assert.True(editor.IsSpringTool);
        editor.HoverPlacement(Mm(400));
        Assert.Equal(SupportType.Spring, editor.Preview!.Type);
        Assert.NotNull(editor.Preview.Spring!.Translational);
        Assert.True(editor.PlaceSupport());
        var draft = editor.SupportDraft!;
        Assert.True(draft.IsSpring);
        Assert.True(draft.HasTranslational);
        Assert.False(draft.HasRotational);
        Assert.Equal("", draft.TranslationalText);
        Assert.False(draft.IsValid);
        Assert.NotNull(draft.TranslationalError);
        Assert.True(editor.Preview!.IsInvalid);
        Assert.NotNull(editor.Preview.Spring!.Translational);
        Assert.False(editor.ConfirmSupport());
        Assert.Equal(2, editor.Document.Supports.Count);
    }

    [Fact]
    public void ConfirmingCommitsTheStiffnessInSiAndTheCommittedDocumentHoldsTheSpring()
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

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1e999")]
    public void ANonPositiveOrUnreadableStiffnessIsRejectedAtTheField(string text)
    {
        var draft = PlaceSpring(Editor());
        draft.TranslationalText = text;
        Assert.False(draft.IsValid);
        Assert.True(draft.HasTranslationalError);
        Assert.Null(draft.Spring);
    }

    [Fact]
    public void WithoutAnySpringTheDraftIsInvalidAndEachSpringValidatesItsOwnField()
    {
        var draft = PlaceSpring(Editor());
        draft.HasTranslational = false;
        Assert.True(draft.HasSpringError);
        Assert.False(draft.IsValid);
        draft.HasRotational = true;
        Assert.False(draft.HasSpringError);
        Assert.True(draft.HasRotationalError);
        draft.RotationalText = "30";
        Assert.True(draft.IsValid);
        draft.HasTranslational = true;
        Assert.True(draft.HasTranslationalError);
        draft.TranslationalText = "2500";
        Assert.True(draft.IsValid);
        Assert.Equal(2.5e6, draft.Spring!.Translational!.Value.NewtonsPerMeter);
    }

    [Fact]
    public void RigidTranslationNeedsARotationalSpringAndExcludesATranslationalOne()
    {
        var draft = PlaceSpring(Editor());
        Assert.False(draft.CanRigidTranslation);
        draft.RigidTranslation = true;
        Assert.False(draft.RigidTranslation);
        draft.HasTranslational = false;
        draft.HasRotational = true;
        Assert.True(draft.CanRigidTranslation);
        draft.RigidTranslation = true;
        Assert.True(draft.RigidTranslation);
        draft.RotationalText = "30";
        Assert.True(draft.Spring!.RigidTranslation);
        draft.HasTranslational = true;
        Assert.False(draft.RigidTranslation);
        draft.HasTranslational = false;
        draft.RigidTranslation = true;
        draft.HasRotational = false;
        Assert.False(draft.RigidTranslation);
    }

    [Fact]
    public void EditingAnExistingSpringKeepsItsExactValuesAndAChangeIsMechanical()
    {
        var id = Guid.NewGuid();
        double k = 1234567.8901234567;
        var editor = Editor(new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"),
            new(Guid.NewGuid(), Mm(1000), SupportType.Roller, "B"),
            new(id, Mm(500), SupportType.Spring, "S", new(TranslationalStiffness.FromNewtonsPerMeter(k), RotationalStiffness.FromNewtonMetersPerRadian(0.1))));
        Assert.True(editor.EditSupport(id));
        var draft = editor.SupportDraft!;
        Assert.True(draft.HasTranslational);
        Assert.True(draft.HasRotational);
        Assert.True(draft.IsValid);
        // Untouched text restores the exact SI value.
        Assert.Equal(k, draft.Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Equal(0.1, draft.Spring.Rotational!.Value.NewtonMetersPerRadian);
        draft.RotationalText = "";
        Assert.False(draft.IsValid);
        draft.HasRotational = false;
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        var updated = editor.Document.Supports.Single(s => s.Id == id);
        Assert.Null(updated.Spring!.Rotational);
        Assert.Equal(k, updated.Spring.Translational!.Value.NewtonsPerMeter);
    }

    [Fact]
    public void SwitchingAnExistingRigidSupportToASpringAndBackLeavesNoSpringData()
    {
        var editor = Editor();
        var id = editor.Document.Supports[0].Id;
        Assert.True(editor.EditSupport(id));
        var draft = editor.SupportDraft!;
        draft.Type = SupportType.Spring;
        Assert.True(draft.HasTranslational);
        Assert.False(draft.IsValid);
        draft.TranslationalText = "1000";
        Assert.True(draft.IsValid);
        draft.Type = SupportType.Pinned;
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
        Assert.Equal(Mm(600), draft.CanvasPosition);
        Assert.True(draft.RigidTranslation);
        Assert.True(draft.IsValid);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(Mm(600), editor.Document.Supports.Single(s => s.Id == id).Position);
        Assert.True(editor.Document.Supports.Single(s => s.Id == id).Spring!.RigidTranslation);
    }

    [Fact]
    public void StiffnessTextFollowsTheSelectedDisplayUnitsAndSettingsChangesRebuildTheDraft()
    {
        var profile = UnitProfile.StructuralEngineering;
        var draft = new SupportDraftViewModel(() => Editor().Document, null, SupportType.Spring, Mm(400), profile);
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
