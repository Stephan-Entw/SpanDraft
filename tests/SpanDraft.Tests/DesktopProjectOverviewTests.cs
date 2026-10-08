using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopProjectOverviewTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static EditorDocument Document() => new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
        [new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "A")],
        [new EditorPointForce(Guid.NewGuid(), Mm(400), Force.FromNewtons(-100), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Mm(600), Moment.FromNewtonMeters(50), "M1")], new(2, 2, 2, 2),
        [new(Guid.NewGuid(), Mm(200), Mm(800), ForcePerLength.FromNewtonsPerMeter(-500), "q1")]);

    private static EditorViewModel Observed(EditorDocument document, out Func<int> analyses)
    {
        int count = 0;
        var editor = new EditorViewModel(document, () => { }, beam => { count++; return BeamAnalysis.Analyze(beam); });
        analyses = () => count;
        return editor;
    }

    private static Guid Id(EditorDocument document, AutoNameKind kind) => kind switch
    {
        AutoNameKind.Support => document.Supports[0].Id,
        AutoNameKind.Force => document.Loads[0].Id,
        AutoNameKind.Moment => document.Loads[1].Id,
        _ => document.DistributedLoads[0].Id
    };

    private static IReadOnlyList<ProjectOverviewItem> Items(EditorViewModel editor, AutoNameKind kind) =>
        kind == AutoNameKind.Support ? editor.Overview.Supports : editor.Overview.Loads;

    private static void StartNew(EditorViewModel editor, AutoNameKind kind)
    {
        if (kind == AutoNameKind.Support)
        {
            editor.ToggleSupportTool(SupportType.Pinned);
            editor.HoverPlacement(Mm(900));
            Assert.True(editor.PlaceSupport());
        }
        else if (kind == AutoNameKind.DistributedLoad)
        {
            editor.ToggleDistributedLoadTool();
            editor.HoverDistributedLoadPlacement(Mm(300));
            Assert.True(editor.PlaceDistributedLoadEndpoint());
            editor.HoverDistributedLoadPlacement(Mm(700));
            Assert.True(editor.PlaceDistributedLoadEndpoint());
        }
        else
        {
            editor.ToggleLoadTool(kind == AutoNameKind.Force ? PointLoadKind.Force : PointLoadKind.Moment);
            editor.HoverLoadPlacement(Mm(900));
            Assert.True(editor.PlaceLoad());
        }
    }

    private static void Edit(EditorViewModel editor, AutoNameKind kind, Guid id) => Assert.True(kind switch
    {
        AutoNameKind.Support => editor.EditSupport(id),
        AutoNameKind.DistributedLoad => editor.EditDistributedLoad(id),
        _ => editor.EditLoad(id)
    });

    private static void Rename(EditorViewModel editor, AutoNameKind kind, string name)
    {
        if (kind == AutoNameKind.Support) editor.SupportDraft!.NameText = name;
        else if (kind == AutoNameKind.DistributedLoad) editor.DistributedLoadDraft!.NameText = name;
        else editor.LoadDraft!.NameText = name;
    }

    private static bool Confirm(EditorViewModel editor, AutoNameKind kind) => kind switch
    {
        AutoNameKind.Support => editor.ConfirmSupport(),
        AutoNameKind.DistributedLoad => editor.ConfirmDistributedLoad(),
        _ => editor.ConfirmLoad()
    };

    [Theory]
    [InlineData("de-DE", "Einspannung")]
    [InlineData("en-US", "Fixed Support")]
    public void ProjectionShowsCommittedGeometryAndOrderedEntitiesInCurrentCulture(
        string culture, string supportType)
    {
        using var scope = new UiCultureScope(culture);
        var doc = Document();
        var editor = Observed(doc, out var analyses);
        var overview = editor.Overview;
        Assert.Equal(SectionDisplay.Name(doc.Section), overview.Section);
        Assert.Equal(doc.Material.Name, overview.Material);
        Assert.Equal(culture == "de-DE" ? "Länge 1000 mm" : "Length 1000 mm", overview.Length);
        Assert.Equal(doc.Supports[0].Id, Assert.Single(overview.Supports).Id);
        Assert.Equal(supportType, overview.Supports[0].Type);
        Assert.Equal("0", overview.Supports[0].Position);
        Assert.Empty(overview.Supports[0].Value);
        Assert.Equal(new[] { "F1", "M1", "q1" }, overview.Loads.Select(l => l.Name));
        Assert.Equal(new[] { "F", "M", "q" }, overview.Loads.Select(l => l.Type));
        Assert.Equal(new[] { "-100 N", "50 Nm", "-500 N/m" }, overview.Loads.Select(l => l.Value));
        Assert.Equal(new[] { "400", "600", "200…800" }, overview.Loads.Select(l => l.Position));
        Assert.True(overview.HasSupports);
        Assert.True(overview.HasLoads);
        Assert.Same(editor.Presentation, overview.Analysis);
        Assert.Equal(1, analyses());
        Assert.Throws<NotSupportedException>(() => ((IList<ProjectOverviewItem>)overview.Supports).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ProjectOverviewItem>)overview.Loads).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ProjectOverviewReaction>)overview.Reactions).Clear());
    }

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void EverySupportTypeAppearsOnlyAfterCommit(SupportType type)
    {
        var doc = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section);
        var editor = Observed(doc, out var analyses);
        var before = editor.Overview;
        editor.ToggleSupportTool(type);
        editor.HoverPlacement(Mm(200));
        Assert.Same(before, editor.Overview);
        Assert.True(editor.PlaceSupport());
        Assert.Empty(editor.Overview.Supports);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal("A", Assert.Single(editor.Overview.Supports).Name);
        Assert.Equal("200", editor.Overview.Supports[0].Position);
        Assert.Equal(type, Assert.Single(editor.Document.Supports).Type);
        Assert.Equal(2, analyses());
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    [InlineData(AutoNameKind.DistributedLoad)]
    public void NewDraftAndFailedConfirmationDoNotPublishBeforeOneMechanicalCommit(AutoNameKind kind)
    {
        var editor = Observed(Document(), out var analyses);
        var before = editor.Overview;
        int notifications = 0;
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.Overview)) notifications++; };
        StartNew(editor, kind);
        Rename(editor, kind, "");
        Assert.False(Confirm(editor, kind));
        Assert.Same(before, editor.Overview);
        Assert.Equal(1, analyses());
        Assert.Equal(0, notifications);
        Rename(editor, kind, "Committed entity");
        Assert.True(Confirm(editor, kind));
        Assert.Contains(Items(editor, kind), i => i.Name == "Committed entity");
        Assert.NotSame(before.Analysis, editor.Overview.Analysis);
        Assert.Same(editor.Presentation, editor.Overview.Analysis);
        Assert.Equal(2, analyses());
        Assert.Equal(1, notifications);
        Assert.True(editor.Undo());
        Assert.Equal(before.Supports, editor.Overview.Supports);
        Assert.Equal(before.Loads, editor.Overview.Loads);
        Assert.True(editor.Redo());
        Assert.Contains(Items(editor, kind), i => i.Name == "Committed entity");
        Assert.Equal(4, analyses());
        Assert.Equal(3, notifications);
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    [InlineData(AutoNameKind.DistributedLoad)]
    public void HoverBuffersAndDragKeepExistingCommittedRowsUntilConfirmation(AutoNameKind kind)
    {
        var editor = Observed(Document(), out var analyses);
        var before = editor.Overview;
        var id = Id(editor.Document, kind);
        if (kind == AutoNameKind.Support) editor.HoverSupport(id);
        else if (kind == AutoNameKind.DistributedLoad) editor.HoverDistributedLoad(id);
        else editor.HoverLoad(id);
        Assert.Same(before, editor.Overview);
        Edit(editor, kind, id);
        Rename(editor, kind, "Draft name");
        if (kind == AutoNameKind.Support)
        {
            editor.SupportDraft!.PositionText = "100";
            Assert.True(editor.BeginSupportDrag(id));
            editor.UpdateSupportDrag(Mm(100));
            Assert.Same(before, editor.Overview);
            Assert.True(editor.EndSupportDrag());
        }
        else if (kind == AutoNameKind.DistributedLoad)
        {
            editor.DistributedLoadDraft!.StartText = "100";
            editor.DistributedLoadDraft.IntensityText = "-750";
            Assert.True(editor.BeginDistributedLoadDrag(id, DistributedLoadEndpoint.Start));
            editor.UpdateDistributedLoadDrag(Mm(100));
            Assert.Same(before, editor.Overview);
            Assert.True(editor.EndDistributedLoadDrag());
        }
        else
        {
            editor.LoadDraft!.PositionText = "100";
            editor.LoadDraft.ValueText = "-750";
            Assert.True(editor.BeginLoadDrag(id));
            editor.UpdateLoadDrag(Mm(100));
            Assert.Same(before, editor.Overview);
            Assert.True(editor.EndLoadDrag());
        }
        Assert.Same(before, editor.Overview);
        Assert.Equal(1, analyses());
        Assert.True(Confirm(editor, kind));
        var committed = Assert.Single(Items(editor, kind), i => i.Id == id);
        Assert.Equal("Draft name", committed.Name);
        Assert.Equal(kind == AutoNameKind.DistributedLoad ? "100…800" : "100", committed.Position);
        Assert.Equal(2, analyses());
    }

    [Theory]
    [InlineData(AutoNameKind.Support)]
    [InlineData(AutoNameKind.Force)]
    [InlineData(AutoNameKind.Moment)]
    [InlineData(AutoNameKind.DistributedLoad)]
    public void RenameAndItsHistoryUpdateOverviewWithoutAnalysis(AutoNameKind kind)
    {
        var editor = Observed(Document(), out var analyses);
        var id = Id(editor.Document, kind);
        var before = editor.Overview;
        string original = Items(editor, kind).Single(i => i.Id == id).Name;
        Edit(editor, kind, id);
        Rename(editor, kind, "New name");
        Assert.Same(before, editor.Overview);
        Assert.True(Confirm(editor, kind));
        Assert.Equal("New name", Items(editor, kind).Single(i => i.Id == id).Name);
        if (kind == AutoNameKind.Support) Assert.Equal("New name", Assert.Single(editor.Overview.Reactions).Name);
        Assert.Same(before.Analysis, editor.Overview.Analysis);
        Assert.True(editor.Undo());
        Assert.Equal(original, Items(editor, kind).Single(i => i.Id == id).Name);
        Assert.True(editor.Redo());
        Assert.Equal("New name", Items(editor, kind).Single(i => i.Id == id).Name);
        Assert.Equal(1, analyses());
    }

    [Fact]
    public void AnnotationAndLengthPreviewsDoNotRefreshOverviewOrAnalyze()
    {
        var editor = Observed(Document(), out var analyses);
        var before = editor.Overview;
        int notifications = 0;
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.Overview)) notifications++; };
        var id = editor.Document.Supports[0].Id;
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "1200";
        editor.PreviewAnnotationOffset(id, new(20, 30));
        Assert.Same(before, editor.Overview);
        editor.CancelEditorInteraction();
        Assert.True(editor.SetAnnotationOffset(id, new(20, 30)));
        Assert.True(editor.Undo());
        Assert.True(editor.Redo());
        Assert.Same(before, editor.Overview);
        Assert.Equal(1, analyses());
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("de-DE", "Länge 1000 mm", "Länge 1200 mm")]
    [InlineData("en-US", "Length 1000 mm", "Length 1200 mm")]
    public void ModelLengthPublishesOnlyAfterCommitAndFollowsMechanicalHistory(string culture, string initial, string committed)
    {
        using var scope = new UiCultureScope(culture);
        var editor = Observed(Document(), out var analyses);
        var before = editor.Overview;
        Assert.Equal(initial, before.Length);
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "500";
        Assert.False(editor.DimensionLength.Confirm());
        Assert.Same(before, editor.Overview);
        Assert.Equal(1, analyses());
        editor.DimensionLength.Text = "1200";
        Assert.Same(before, editor.Overview);
        Assert.True(editor.DimensionLength.Confirm());
        Assert.Equal(committed, editor.Overview.Length);
        Assert.Equal(2, analyses());
        Assert.True(editor.Undo());
        Assert.Equal(initial, editor.Overview.Length);
        Assert.True(editor.Redo());
        Assert.Equal(committed, editor.Overview.Length);
        Assert.Equal(4, analyses());
    }

    [Theory]
    [InlineData(AnalysisPresentationKind.MissingSupports)]
    [InlineData(AnalysisPresentationKind.IncompleteModel)]
    [InlineData(AnalysisPresentationKind.UnstableModel)]
    public void FailedAnalysisClearsPreviousResultsAndHistoryRestoresMatchingOverview(AnalysisPresentationKind failure)
    {
        var editor = Observed(Document(), out var analyses);
        var successful = editor.Overview;
        Assert.NotEmpty(successful.Reactions);
        Assert.NotEmpty(successful.Analysis.Stress);
        var doc = editor.Document;
        var supports = failure switch
        {
            AnalysisPresentationKind.MissingSupports => Array.Empty<EditorSupport>(),
            AnalysisPresentationKind.IncompleteModel => [doc.Supports[0] with { Position = Mm(1100) }],
            _ => [doc.Supports[0] with { Type = SupportType.Roller }]
        };
        Assert.True(editor.Session.Commit(new(doc.WithSupports(supports), editor.EditorPresentation)));
        var failed = editor.Overview;
        Assert.Equal(failure, failed.Analysis.Kind);
        Assert.Equal(supports.Select(s => s.Name), failed.Supports.Select(s => s.Name));
        Assert.Equal(successful.Loads, failed.Loads);
        Assert.Equal(successful.Section, failed.Section);
        Assert.Equal(successful.Material, failed.Material);
        Assert.Empty(failed.Reactions);
        Assert.Null(failed.Analysis.Result);
        Assert.Empty(failed.Analysis.Displacement);
        Assert.Empty(failed.Analysis.Moment);
        Assert.Empty(failed.Analysis.Stress);
        Assert.Empty(failed.Analysis.SafetyFactor);
        Assert.NotEmpty(failed.Analysis.StatusText);
        Assert.Equal(2, analyses());
        Assert.True(editor.Undo());
        Assert.Equal(successful.Reactions, editor.Overview.Reactions);
        Assert.Equal(successful.Analysis.Stress, editor.Overview.Analysis.Stress);
        Assert.True(editor.Redo());
        Assert.Equal(failure, editor.Overview.Analysis.Kind);
        Assert.Empty(editor.Overview.Reactions);
        Assert.Equal(4, analyses());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReactionsFollowSupportOrderCurrentNamesAndNullableSolutionComponents(bool loaded)
    {
        // Deliberately opposite to node order, to verify the position-based association.
        var doc = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(1000), SupportType.Roller, "Right"), new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "Left")],
            loaded ? [new EditorPointForce(Guid.NewGuid(), Mm(500), Force.FromNewtons(1000), "F1")] : []);
        var editor = Observed(doc, out var analyses);
        Assert.Equal(new[] { "Right", "Left" }, editor.Overview.Reactions.Select(r => r.Name));
        var right = editor.Overview.Reactions[0];
        Assert.Empty(right.Rx);
        Assert.Empty(right.Moment);
        foreach (var support in doc.Supports)
        {
            var node = editor.Presentation.Result!.Solution.Nodes.Single(n => n.Position == support.Position);
            var row = editor.Overview.Reactions.Single(r => r.Id == support.Id);
            Assert.Equal(QuantityFormatter.FormatNumber(node.ReactionY!.Value.Newtons, QuantityKind.TransverseForce,
                references: editor.Presentation.References), row.Ry);
            if (node.ReactionX is { } rx) Assert.Equal(QuantityFormatter.FormatNumber(rx.Newtons, QuantityKind.AxialForce,
                references: editor.Presentation.References), row.Rx);
            if (loaded) Assert.StartsWith("-", row.Ry);
            else Assert.Equal("0", row.Ry);
        }
        if (!loaded)
        {
            Assert.Equal("0", editor.Overview.Reactions[1].Rx);
            Assert.Equal("0 mm", editor.Overview.Analysis.Displacement);
            Assert.Equal("0 N·m", editor.Overview.Analysis.Moment);
            Assert.Equal("0 MPa", editor.Overview.Analysis.Stress);
            Assert.Equal("∞", editor.Overview.Analysis.SafetyFactor);
        }
        Assert.Equal(1, analyses());
    }

    [Fact]
    public void FixedSupportMomentAndProjectSetupUseCurrentAnalysisAndDocument()
    {
        var editor = Observed(Document(), out var analyses);
        var moment = editor.Presentation.Result!.Solution.Nodes[0].ReactionMoment!.Value;
        Assert.Equal(QuantityFormatter.FormatNumber(moment.NewtonMeters, QuantityKind.Moment,
            references: editor.Presentation.References), Assert.Single(editor.Overview.Reactions).Moment);
        var material = new Material("Other steel", Pressure.FromPascals(200e9), Pressure.FromPascals(250e6));
        var section = new CircleSection(Mm(50));
        editor.ApplySetup(section, material);
        Assert.Equal("Other steel", editor.Overview.Material);
        Assert.Equal(SectionDisplay.Name(section), editor.Overview.Section);
        Assert.Same(editor.Presentation, editor.Overview.Analysis);
        Assert.Equal(2, analyses());
    }

    [Theory]
    [InlineData("de-DE", "-1230")]
    [InlineData("en-US", "-1230")]
    public void ReactionDisplayUsesResultPrecisionWithoutRoundingCommittedLoads(string culture, string expected)
    {
        using var scope = new UiCultureScope(culture);
        const double value = 1234.5678;
        var doc = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "A")],
            [new EditorPointForce(Guid.NewGuid(), Mm(1000), Force.FromNewtons(value), "F1")]);
        var editor = Observed(doc, out var analyses);
        var reaction = Assert.Single(editor.Overview.Reactions);
        Assert.Equal("A", reaction.Name);
        Assert.Equal("0", reaction.Rx);
        Assert.Equal(expected, reaction.Ry);
        Assert.Equal(expected, reaction.Moment);
        Assert.Equal(value, Assert.Single(editor.Document.Loads).Value);
        Assert.Equal(1, analyses());
    }

    [Theory]
    [InlineData("de-DE", "400,12", "-100,57 N", "600,99", "50,12 Nm", "200,25…800,75", "-500,99 N/m")]
    [InlineData("en-US", "400.12", "-100.57 N", "600.99", "50.12 Nm", "200.25…800.75", "-500.99 N/m")]
    public void TableCellsRoundOnlyDisplayValuesInCurrentUICulture(string culture, string forcePosition,
        string force, string momentPosition, string moment, string range, string intensity)
    {
        using var scope = new UiCultureScope(culture);
        var doc = Document().WithLoads([
            new EditorPointForce(Guid.NewGuid(), Mm(400.123456), Force.FromNewtons(-100.56789), "F1"),
            new EditorPointMoment(Guid.NewGuid(), Mm(600.987654), Moment.FromNewtonMeters(50.123456), "M1")])
            .WithDistributedLoads([new(Guid.NewGuid(), Mm(200.2468), Mm(800.7531),
                ForcePerLength.FromNewtonsPerMeter(-500.98765), "q1")]);
        var editor = Observed(doc, out var analyses);
        Assert.Equal(new[] { forcePosition, momentPosition, range }, editor.Overview.Loads.Select(l => l.Position));
        Assert.Equal(new[] { force, moment, intensity }, editor.Overview.Loads.Select(l => l.Value));
        Assert.Same(doc, editor.Document);
        Assert.Equal(-100.56789, editor.Document.Loads[0].Value);
        Assert.Equal(50.123456, editor.Document.Loads[1].Value);
        Assert.Equal(-500.98765, editor.Document.DistributedLoads[0].Intensity.NewtonsPerMeter);
        Assert.Equal(1, analyses());
    }

    [Theory]
    [InlineData("de-DE", 1234.5678, "1234,57")]
    [InlineData("en-US", -1234.5678, "-1234.57")]
    [InlineData("de-DE", -0.000123456, "-123·10⁻⁶")]
    [InlineData("en-US", 0.000123456, "123·10⁻⁶")]
    [InlineData("de-DE", 12345678, "12,3·10⁶")]
    [InlineData("en-US", -12345678, "-12.3·10⁶")]
    [InlineData("en-US", -0.0, "0")]
    [InlineData("de-DE", double.PositiveInfinity, "∞")]
    public void CompactNumbersBoundWidthWithoutHidingSmallNonzeroValues(string culture, double value, string expected)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal(expected, UiNumbers.Compact(value));
    }

    [Fact]
    public void EmptyDocumentHasNoTableRowsOrSyntheticPlaceholders()
    {
        var editor = Observed(new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section), out var analyses);
        Assert.False(editor.Overview.HasSupports);
        Assert.False(editor.Overview.HasLoads);
        Assert.Empty(editor.Overview.Supports);
        Assert.Empty(editor.Overview.Loads);
        Assert.Empty(editor.Overview.Reactions);
        Assert.Equal(AnalysisPresentationKind.MissingSupports, editor.Overview.Analysis.Kind);
        Assert.Equal(1, analyses());
    }
}

internal sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
    public UiCultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
