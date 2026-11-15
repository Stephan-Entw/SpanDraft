using System.Globalization;
using SpanDraft.Analysis;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The segment row under the beam: its labels and boundaries, and the names shared with the beam model.</summary>
public sealed class DesktopSegmentRowTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("S235JR (1.0038)", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re);
    private static EditorSegment Segment(double length, string? name = null) =>
        new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y, name) { Length = M(length) };

    private static ProjectOverviewState Overview(EditorDocument document) =>
        ProjectOverviewState.From(document, AnalysisPresentationState.FromOutcome(BeamAnalysis.Analyze(document.ToBeamModel())));

    // Every character is 10 wide, so a label's width is its length in tens.
    private static double Ten(string text) => text.Length * 10;
    private static readonly string[] Candidates = ["Long section name · Material", "Abschnitt 12", "12"];

    [Theory]
    [InlineData(400, 0)]    // the full description fits
    [InlineData(200, 1)]    // only "Abschnitt n" fits
    [InlineData(40, 2)]     // only the number fits
    public void TheMostDetailedLabelThatFitsIsChosen(double width, int expected) =>
        Assert.Equal(Candidates[expected], SegmentRowLayout.Choose(width, Candidates, Ten));

    [Fact]
    public void AFittingLabelNeedsItsPaddingOnBothSidesAndAnExactFitStillCounts()
    {
        double exact = Ten(Candidates[1]) + 2 * SegmentRowLayout.LabelPadding;
        Assert.Equal(Candidates[1], SegmentRowLayout.Choose(exact, [Candidates[0], Candidates[1]], Ten));
        Assert.Null(SegmentRowLayout.Choose(exact - 0.01, [Candidates[0], Candidates[1]], Ten));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(12)]
    [InlineData(double.NaN)]
    public void WithoutRoomForAnyLabelThereIsNoLabelAndNothingIsShortened(double width) =>
        Assert.Null(SegmentRowLayout.Choose(width, Candidates, Ten));

    [Fact]
    public void TheBoundariesAreTheStationsOfTheLayoutEvenInASchematicDrawing()
    {
        // A station with wide extents next to a boundary makes the drawing schematic; the row follows it exactly.
        var layout = StationLayout.Compute(10, 0, 800,
            [new(1, 200, 200), new(2, 200, 200), new(2.05, 200, 200), new(4, 0, 0)]);
        Assert.True(layout.IsDistorted);
        var inputs = new SegmentRowInput[]
        {
            new(0, 2.05, ["a"]), new(2.05, 4, ["b"]), new(4, 10, ["c"])
        };

        var spans = SegmentRowLayout.Create(inputs, layout.Transform, Ten);

        Assert.Equal(layout.Transform.PhysicalToScreen(2.05), spans[0].EndX);
        Assert.Equal(spans[0].EndX, spans[1].StartX);
        Assert.Equal(layout.Stations.Single(s => s.PhysicalX == 2.05).ScreenX, spans[1].StartX, 6);
        Assert.Equal(layout.Stations.Single(s => s.PhysicalX == 4).ScreenX, spans[2].StartX, 6);
        Assert.Equal(layout.Stations[0].ScreenX, spans[0].StartX, 6);
        Assert.Equal(layout.Stations[^1].ScreenX, spans[^1].EndX, 6);
    }

    [Fact]
    public void TheRowNeverAsksTheLayoutForRoom()
    {
        // The same layout serves the beam with and without a row; a tiny segment simply gets no label.
        var requirements = new StationRequirement[] { new(0.1, 0, 0) };
        var layout = StationLayout.Compute(10, 0, 1000, requirements);
        var spans = SegmentRowLayout.Create([new(0, 0.1, ["Abschnitt 1", "1"]), new(0.1, 10, ["Abschnitt 2", "2"])], layout.Transform, Ten);

        Assert.Null(spans[0].Text);
        Assert.Equal("Abschnitt 2", spans[1].Text);
        Assert.Equal(StationLayout.Compute(10, 0, 1000, requirements).Stations.Select(s => s.ScreenX), layout.Stations.Select(s => s.ScreenX));
    }

    [Fact]
    public void TheSegmentTextsAreCompleteSharedAndJoinedByADot()
    {
        var named = Segment(1, "My own profile name");
        var longName = Segment(1, new string('P', 120));
        Assert.Equal(string.Format(CultureInfo.CurrentUICulture, Strings.SegmentSelected, 3), SegmentLabels.Title(3));
        Assert.Equal("My own profile name · " + MaterialDisplay.Format(Steel), SegmentLabels.Description(named));
        Assert.Equal(new string('P', 120) + " · " + MaterialDisplay.Format(Steel), SegmentLabels.Description(longName));
        Assert.Equal("Abschnitt 2 · My own profile name · " + MaterialDisplay.Format(Steel), SegmentLabels.RowLabel(named, 2));
        Assert.Equal([SegmentLabels.RowLabel(named, 2), SegmentLabels.Title(2), "2"], SegmentLabels.RowCandidates(named, 2));
        Assert.Equal(SectionDisplay.Name(Segment(1).Section), SegmentLabels.SectionName(Segment(1)));
    }

    [Fact]
    public void TheBeamModelBlocksCarryTheSameHeadingAndDescriptionAsTheRow()
    {
        var document = new EditorDocument([Segment(1, "First"), Segment(2), Segment(1.5, "Third")]);
        var overview = Overview(document);

        Assert.Equal([1, 2, 3], overview.Segments.Select(s => s.Number));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(SegmentLabels.Title(i + 1), overview.Segments[i].Title);
            Assert.Equal(SegmentLabels.Description(document.Segments[i], ResultPresentationOptions.Default), overview.Segments[i].Description);
            Assert.Equal(SegmentLabels.Title(i + 1) + SegmentLabels.Separator + overview.Segments[i].Description,
                SegmentLabels.RowCandidates(document.Segments[i], i + 1, ResultPresentationOptions.Default)[0]);
        }
        // A beam of one segment is described the same way: a heading and one description.
        var single = Overview(new EditorDocument([Segment(1, "Only")]));
        Assert.Equal(SegmentLabels.Title(1), Assert.Single(single.Segments).Title);
        Assert.Equal("Only · " + MaterialDisplay.Format(Steel), single.Segments[0].Description);
    }
}
