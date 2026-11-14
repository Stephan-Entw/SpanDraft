using SpanDraft.Analysis;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Editing linearly varying distributed loads: two intensities, a following end value, commit and overview.</summary>
public sealed class DesktopLinearLoadTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static ForcePerLength Q(double value) => ForcePerLength.FromNewtonsPerMeter(value);
    private static string Text(double value) => InputQuantityFormatter.FormatExact(value, UnitCatalog.NewtonPerMeter);
    private static EditorDocument Document(params EditorDistributedLoad[] loads) =>
        new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, distributedLoads: loads);
    private static EditorDistributedLoad Load(double q1, double q2, string name = "q1") =>
        new(Guid.NewGuid(), Mm(200), Mm(800), Q(q1), Q(q2), name);

    private static EditorViewModel NewDraft(EditorDocument? document = null)
    {
        var editor = new EditorViewModel(document ?? Document(), () => { });
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Mm(200));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        editor.HoverDistributedLoadPlacement(Mm(800));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        return editor;
    }

    [Fact]
    public void ANewLoadStartsConstantAndItsEndIntensityFollowsTheStartUntilItIsTouched()
    {
        var editor = NewDraft();
        var draft = editor.DistributedLoadDraft!;
        Assert.Equal(draft.StartIntensityText, draft.EndIntensityText);
        draft.StartIntensityText = Text(-300);
        Assert.Equal(Text(-300), draft.EndIntensityText);
        Assert.Equal((-300, -300), (editor.DistributedLoadPreview!.StartIntensity, editor.DistributedLoadPreview.EndIntensity));
        draft.EndIntensityText = Text(700);
        draft.StartIntensityText = Text(-100);
        Assert.Equal(Text(700), draft.EndIntensityText);
        Assert.Equal((-100, 700), (editor.DistributedLoadPreview.StartIntensity, editor.DistributedLoadPreview.EndIntensity));
        Assert.True(editor.ConfirmDistributedLoad());
        var load = Assert.Single(editor.Document.DistributedLoads);
        Assert.Equal((-100, 700), (load.StartIntensity.NewtonsPerMeter, load.EndIntensity.NewtonsPerMeter));
        Assert.False(load.IsConstant);
        var core = Assert.IsType<DistributedLoad>(editor.Document.ToBeamModel().Loads.Single());
        Assert.Equal((-100, 700), (core.StartIntensity.NewtonsPerMeter, core.EndIntensity.NewtonsPerMeter));
    }

    [Fact]
    public void AnUntouchedConstantLoadStaysConstantWhenOnlyTheStartIsEdited()
    {
        var editor = NewDraft();
        editor.DistributedLoadDraft!.StartIntensityText = Text(250);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.True(Assert.Single(editor.Document.DistributedLoads).IsConstant);
        Assert.Equal(250, editor.Document.DistributedLoads[0].EndIntensity.NewtonsPerMeter);
    }

    [Fact]
    public void ExistingLoadsOpenWithTheirOwnValuesAndAConstantOneFollowsAgain()
    {
        var linear = Load(-500, 300, "q1");
        var constant = Load(-200, -200, "q2");
        var editor = new EditorViewModel(Document(linear, constant), () => { });
        Assert.True(editor.EditDistributedLoad(linear.Id));
        var draft = editor.DistributedLoadDraft!;
        Assert.Equal((Text(-500), Text(300)), (draft.StartIntensityText, draft.EndIntensityText));
        draft.StartIntensityText = Text(-600);
        Assert.Equal(Text(300), draft.EndIntensityText); // A linear load never follows.
        editor.CancelEditorInteraction();
        Assert.True(editor.EditDistributedLoad(constant.Id));
        draft = editor.DistributedLoadDraft!;
        draft.StartIntensityText = Text(-250);
        Assert.Equal(Text(-250), draft.EndIntensityText);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.True(editor.Document.DistributedLoads[1].IsConstant);
    }

    [Fact]
    public void BothIntensitiesAreValidatedIndependentlyAndAFollowingEndReportsNoSecondError()
    {
        var editor = NewDraft();
        var draft = editor.DistributedLoadDraft!;
        draft.StartIntensityText = "oops";
        Assert.True(draft.HasStartIntensityError);
        Assert.False(draft.HasEndIntensityError); // The end follows, so its error is shown once at the start.
        Assert.False(draft.IsValid); Assert.False(editor.ConfirmDistributedLoad());
        draft.StartIntensityText = Text(100);
        draft.EndIntensityText = "broken";
        Assert.False(draft.HasStartIntensityError); Assert.True(draft.HasEndIntensityError);
        Assert.False(draft.IsValid); Assert.False(editor.ConfirmDistributedLoad());
        Assert.Equal(100, editor.DistributedLoadPreview!.StartIntensity);
        draft.EndIntensityText = Text(-100);
        Assert.True(draft.IsValid); Assert.True(editor.ConfirmDistributedLoad());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 500)]
    [InlineData(-500, 0)]
    [InlineData(-500, 500)]
    public void ZeroAndSignChangeAreValidIntensityPairs(double q1, double q2)
    {
        var editor = NewDraft();
        var draft = editor.DistributedLoadDraft!;
        draft.StartIntensityText = Text(q1);
        draft.EndIntensityText = Text(q2);
        Assert.True(editor.ConfirmDistributedLoad());
        var load = Assert.Single(editor.Document.DistributedLoads);
        Assert.Equal((q1, q2), (load.StartIntensity.NewtonsPerMeter, load.EndIntensity.NewtonsPerMeter));
        Assert.Equal(q1 == q2, load.IsConstant);
    }

    [Fact]
    public void ChangingOnlyTheEndIntensityOfAnExistingLoadRecalculatesAndUndoRestoresIt()
    {
        int calculations = 0;
        var original = Load(-500, -500);
        var editor = new EditorViewModel(Document(original), () => { }, beam => { calculations++; return BeamAnalysis.Analyze(beam); });
        Assert.Equal(1, calculations);
        Assert.True(editor.EditDistributedLoad(original.Id));
        editor.DistributedLoadDraft!.EndIntensityText = Text(-400);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Equal(2, calculations);
        var changed = editor.Document.DistributedLoads[0];
        Assert.Equal(-500, changed.StartIntensity.NewtonsPerMeter);
        Assert.Equal(-400, changed.EndIntensity.NewtonsPerMeter);
        // A confirmed edit without any change recalculates nothing.
        Assert.True(editor.EditDistributedLoad(original.Id));
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Equal(2, calculations);
    }

    [Fact]
    public void TheOverviewShowsBothEndIntensitiesOfALinearLoadOnlyOnce()
    {
        var editor = new EditorViewModel(Document(Load(-500, 300, "q1"), Load(-200, -200, "q2")), () => { });
        var items = editor.Overview.DistributedLoads;
        string linear = items.Single(i => i.Name == "q1").Value, constant = items.Single(i => i.Name == "q2").Value;
        Assert.Contains(" … ", linear);
        Assert.Equal(1, linear.Split(UnitCatalog.NewtonPerMeter.Symbol).Length - 1);
        Assert.StartsWith(QuantityFormatter.FormatInputNumber(-500, UnitCatalog.NewtonPerMeter, editor.ResultPresentation), linear);
        Assert.DoesNotContain("…", constant);
        Assert.Equal(QuantityFormatter.FormatInput(-200, UnitCatalog.NewtonPerMeter, editor.ResultPresentation), constant);
    }
}
