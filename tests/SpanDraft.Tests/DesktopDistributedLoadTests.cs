using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopDistributedLoadTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static EditorUniformDistributedLoad Load(double start = 200, double end = 800, double q = -500, string name = "q1") =>
        new(Guid.NewGuid(), Mm(start), Mm(end), ForcePerLength.FromNewtonsPerMeter(q), name);
    private static EditorDocument Document(params EditorUniformDistributedLoad[] loads) =>
        new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, distributedLoads: loads);

    private static EditorViewModel Observed(EditorDocument doc, out Func<int> calls)
    {
        int count = 0;
        var editor = new EditorViewModel(doc, () => { }, beam => { count++; return BeamAnalysis.Analyze(beam); });
        calls = () => count;
        return editor;
    }

    private static void NewDraft(EditorViewModel editor, double first = 200, double second = 800)
    {
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Mm(first));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        editor.HoverDistributedLoadPlacement(Mm(second));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        Assert.True(editor.IsDistributedLoadFlyoutVisible);
    }

    [Fact]
    public void DocumentDefensivelyCopiesAndMapsAllLoadKindsWithoutDesktopMetadata()
    {
        var a = Load(0, 1000, 0, " q1 ");
        var b = Load(123.456, 987.654, 400, "q2");
        EditorUniformDistributedLoad[] source = [a, b];
        EditorPointLoad[] points = [new EditorPointMoment(Guid.NewGuid(), Mm(150), Moment.FromNewtonMeters(10), "M1"),
            new EditorPointForce(Guid.NewGuid(), Mm(250), Force.FromNewtons(-100), "F1")];
        var doc = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: points, distributedLoads: source);
        source[0] = b;
        Assert.Equal(new[] { a, b }, doc.DistributedLoads);
        Assert.Throws<NotSupportedException>(() => ((IList<EditorUniformDistributedLoad>)doc.DistributedLoads).Clear());
        Assert.Equal("q1", a.Name);
        var beam = doc.ToBeamModel();
        Assert.IsType<PointMoment>(beam.Loads[0]);
        Assert.IsType<PointForce>(beam.Loads[1]);
        var mapped = Assert.IsType<UniformDistributedLoad>(beam.Loads[3]);
        Assert.Equal(b.StartPosition, mapped.StartPosition);
        Assert.Equal(b.EndPosition, mapped.EndPosition);
        Assert.Equal(b.Intensity, mapped.Intensity);
        Assert.Equal(doc.DistributedLoads, doc.WithLoads([]).WithSupports([]).DistributedLoads);
        Assert.Equal(doc.DistributedLoads, (doc with { Length = Mm(1200) }).DistributedLoads);
    }

    [Theory]
    [InlineData(200, 800)]
    [InlineData(800, 200)]
    [InlineData(0, 1000)]
    public void TwoClickPlacementOrdersEndpointsAndAnalyzesOnlyAtCommit(double first, double second)
    {
        var editor = Observed(Document(), out var calls);
        NewDraft(editor, first, second);
        Assert.Equal(Mm(Math.Min(first, second)), editor.DistributedLoadPreview!.StartPosition);
        Assert.Equal(Mm(Math.Max(first, second)), editor.DistributedLoadPreview.EndPosition);
        Assert.Equal(-500, editor.DistributedLoadPreview.Intensity);
        Assert.Empty(editor.Document.DistributedLoads);
        Assert.Equal(1, calls());
        Assert.Equal(1, editor.Document.NamingState.NextDistributedLoadNumber);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Equal(2, calls());
        Assert.Equal("q1", Assert.Single(editor.Document.DistributedLoads).Name);
        Assert.Equal(2, editor.Document.NamingState.NextDistributedLoadNumber);
        Assert.False(editor.IsDistributedLoadTool);
        Assert.True(editor.IsEditorNeutral);
    }

    [Fact]
    public void NullLengthAndPointerLeaveKeepPlacementWithoutConsumingName()
    {
        var editor = Observed(Document(), out var calls);
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Mm(400));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        Assert.False(editor.PlaceDistributedLoadEndpoint());
        Assert.True(editor.DistributedLoadPreview!.IsInvalid);
        editor.HoverDistributedLoadPlacement(null);
        Assert.Null(editor.DistributedLoadPreview);
        Assert.Equal(Mm(400), editor.DistributedFirstEndpoint);
        editor.HoverDistributedLoadPlacement(Mm(700));
        Assert.True(editor.PlaceDistributedLoadEndpoint());
        editor.CancelEditorInteraction();
        Assert.Equal("q1", EntityNaming.Peek(editor.Document, AutoNameKind.DistributedLoad).Name);
        Assert.Equal(1, calls());
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(500)]
    [InlineData(0)]
    public void SignedIntensityPreviewsImmediatelyButRangeOnlyMovesAtCommit(double q)
    {
        var editor = Observed(Document(Load()), out var calls);
        Assert.True(editor.EditDistributedLoad(editor.Document.DistributedLoads[0].Id));
        var draft = editor.DistributedLoadDraft!;
        draft.StartText = "300";
        draft.EndText = "900";
        draft.IntensityText = UiNumbers.Format(q);
        Assert.Equal(Mm(200), editor.DistributedLoadPreview!.StartPosition);
        Assert.Equal(Mm(800), editor.DistributedLoadPreview.EndPosition);
        Assert.Equal(q, editor.DistributedLoadPreview.Intensity);
        Assert.Equal(1, calls());
        Assert.True(editor.ConfirmDistributedLoad());
        var load = Assert.Single(editor.Document.DistributedLoads);
        Assert.Equal(Mm(300), load.StartPosition);
        Assert.Equal(Mm(900), load.EndPosition);
        Assert.Equal(q, load.Intensity.NewtonsPerMeter);
        Assert.Equal(2, calls());
    }

    [Theory]
    [InlineData("", "800", "-500")]
    [InlineData("-1", "800", "-500")]
    [InlineData("800", "800", "-500")]
    [InlineData("900", "800", "-500")]
    [InlineData("200", "1001", "-500")]
    [InlineData("200", "800", "NaN")]
    [InlineData("200", "800", "Infinity")]
    [InlineData("200", "800", "1e999")]
    public void InvalidBuffersNeverCommitOrMoveCanvas(string start, string end, string q)
    {
        var editor = Observed(Document(Load()), out var calls);
        var original = editor.Document;
        editor.EditDistributedLoad(original.DistributedLoads[0].Id);
        var draft = editor.DistributedLoadDraft!;
        draft.StartText = start; draft.EndText = end; draft.IntensityText = q;
        Assert.False(editor.ConfirmDistributedLoad());
        Assert.True(editor.IsDistributedLoadFlyoutVisible);
        Assert.True(editor.DistributedLoadPreview!.IsInvalid);
        Assert.Equal(Mm(200), draft.Preview.StartPosition);
        Assert.Equal(Mm(800), draft.Preview.EndPosition);
        Assert.Same(original, editor.Document);
        editor.CancelEditorInteraction();
        Assert.Equal(1, calls());
    }

    [Fact]
    public void NoOpRenameCancelAndDeleteHaveTheCentralAnalysisSemantics()
    {
        var load = Load();
        var editor = Observed(Document(load), out var calls);
        var document = editor.Document;
        var analysis = editor.Presentation;
        editor.EditDistributedLoad(load.Id);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Same(document, editor.Document);
        editor.EditDistributedLoad(load.Id);
        editor.DistributedLoadDraft!.NameText = "  Motor  ";
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Equal("Motor", editor.Document.DistributedLoads[0].Name);
        Assert.Same(analysis, editor.Presentation);
        Assert.Equal(1, calls());
        editor.EditDistributedLoad(load.Id);
        editor.DistributedLoadDraft!.NameText = "Discard";
        editor.DistributedLoadDraft.IntensityText = "100";
        editor.CancelEditorInteraction();
        Assert.Equal("Motor", editor.Document.DistributedLoads[0].Name);
        Assert.Equal(-500, editor.Document.DistributedLoads[0].Intensity.NewtonsPerMeter);
        editor.EditDistributedLoad(load.Id);
        editor.SetAnnotationOffset(load.Id, new(10, 15));
        editor.DeleteDistributedLoad();
        Assert.Empty(editor.Document.DistributedLoads);
        Assert.Empty(editor.EditorPresentation.AnnotationOffsets);
        Assert.Equal(2, calls());
    }

    [Fact]
    public void NamingIsMonotoneAcrossManualNamesCollisionSkippingCancellationAndOtherKinds()
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(0), SupportType.Fixed, "Q1");
        var doc = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, [support]);
        var editor = Observed(doc, out var calls);
        NewDraft(editor);
        Assert.Equal("q2", editor.DistributedLoadDraft!.NameText);
        editor.DistributedLoadDraft.NameText = "Q1";
        Assert.False(editor.ConfirmDistributedLoad());
        Assert.Equal(1, editor.Document.NamingState.NextDistributedLoadNumber);
        editor.DistributedLoadDraft.NameText = "Payload";
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Equal(3, editor.Document.NamingState.NextDistributedLoadNumber);
        var load = editor.Document.DistributedLoads[0];
        editor.EditDistributedLoad(load.Id); editor.DeleteDistributedLoad();
        NewDraft(editor);
        Assert.Equal("q3", editor.DistributedLoadDraft!.NameText);
        editor.CancelEditorInteraction();
        Assert.Equal(3, editor.Document.NamingState.NextDistributedLoadNumber);
        Assert.Equal(1, editor.Document.NamingState.NextForceNumber);
        Assert.Equal(1, editor.Document.NamingState.NextMomentNumber);
        Assert.Equal(1, editor.Document.NamingState.NextSupportOrdinal);
        Assert.Equal(3, calls());
        Assert.Throws<ArgumentException>(() => doc.WithDistributedLoads([Load(name: "q1")]));
        Assert.Throws<ArgumentException>(() => editor.Document.WithDistributedLoads([], new(nextDistributedLoadNumber: 2)));
    }

    [Theory]
    [InlineData("de-DE", "123,5", "900,5", "-500,25")]
    [InlineData("en-US", "123.5", "900.5", "-500.25")]
    public void ParsingUsesUICultureWithoutThousandsSeparators(string culture, string start, string end, string intensity)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var editor = new EditorViewModel(Document(), () => { });
            NewDraft(editor);
            var draft = editor.DistributedLoadDraft!;
            draft.StartText = start; draft.EndText = end; draft.IntensityText = intensity;
            Assert.True(editor.ConfirmDistributedLoad());
            var load = editor.Document.DistributedLoads[0];
            Assert.Equal(Mm(123.5), load.StartPosition);
            Assert.Equal(Mm(900.5), load.EndPosition);
            Assert.Equal(-500.25, load.Intensity.NewtonsPerMeter);
            editor.EditDistributedLoad(load.Id);
            editor.DistributedLoadDraft!.IntensityText = culture == "en-US" ? "1,000" : "1.000";
            Assert.False(editor.ConfirmDistributedLoad());
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Fact]
    public void UnmodifiedBuffersKeepExactSIValuesAndSetupKeepsPresentation()
    {
        var load = new EditorUniformDistributedLoad(Guid.NewGuid(), Length.FromMeters(.12345678912345678),
            Length.FromMeters(.9876543219876543), ForcePerLength.FromNewtonsPerMeter(-500.1234567891234), "q1");
        var editor = Observed(Document(load), out var calls);
        editor.SetAnnotationOffset(load.Id, new(10, -20));
        editor.EditDistributedLoad(load.Id);
        editor.DistributedLoadDraft!.NameText = "Payload";
        Assert.True(editor.ConfirmDistributedLoad());
        var edited = editor.Document.DistributedLoads[0];
        Assert.Equal(load.StartPosition, edited.StartPosition);
        Assert.Equal(load.EndPosition, edited.EndPosition);
        Assert.Equal(load.Intensity, edited.Intensity);
        editor.ApplySetup(ProjectTemplates.Section, ProjectTemplates.Material);
        Assert.Equal(new AnnotationOffset(10, -20), editor.EditorPresentation.AnnotationOffsets[load.Id]);
        Assert.Equal(edited, editor.Document.DistributedLoads[0]);
        Assert.Equal(1, calls());
    }
}
