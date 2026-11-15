using SpanDraft.Reporting.Notation;
using SkiaSharp;

namespace SpanDraft.Reporting;

// Display values are frozen by Desktop. Reporting never interprets a domain model.
internal sealed record ReportData(string Title, string Author, string Version, DateTimeOffset CreatedAt,
    string Date, IReadOnlyList<(string Label, string Value)> Metadata, IReadOnlyList<ReportSegment> Segments,
    IReadOnlyList<ReportTable> Tables, IReadOnlyList<ReportIndicator> Indicators, ReportLabels Labels);
/// <summary>One segment's detail block: its heading, section and material with the values used for the calculation.</summary>
internal sealed record ReportSegment(string Title, string Section, IReadOnlyList<TechnicalText> SectionProperties,
    string Material, IReadOnlyList<TechnicalText> MaterialProperties);
internal sealed record ReportLabels(string Heading, string Continuation, string PageFormat,
    string Section, string Material, string Summary, string Notes, IReadOnlyList<string> Assumptions,
    string Title, string Project, string Reference, string Author);
/// <param name="StrongFirstLine">Each row is a heading line followed by a description that wraps without being shortened.</param>
internal sealed record ReportTable(string Title, IReadOnlyList<TechnicalText> Headers,
    IReadOnlyList<float> Widths, IReadOnlyList<IReadOnlyList<string>> Rows, int? LeftAlignedColumn = null,
    bool StrongFirstLine = false);
internal sealed record ReportIndicator(TechnicalText Label, string Value, string? Position);
internal enum ReportGraphic { Beam, Displacement, Shear, Moment, Section }
/// <param name="MinimumDiagramHeight">The height of a result diagram with a plot of its normal compact height.</param>
/// <param name="Draw">Draws a graphic; the index selects the segment of a section sketch.</param>
internal sealed record ReportTechnicalGraphics(float BeamHeight, float MinimumDiagramHeight,
    Action<SKCanvas, ReportGraphic, int, SKRect> Draw);
internal sealed record ReportRowSlice(int Row, int FirstLine, int LineCount, IReadOnlyList<string[]> Cells);
internal sealed record ReportTableFragment(int Table, SKRect Bounds, float HeadingHeight,
    IReadOnlyList<ReportRowSlice> Rows);
internal enum ReportBlockKind { Summary, Segment, Notes }
/// <summary>A block of the page flow below the tables; it always lies completely on one page.</summary>
/// <param name="Embedded">The notes sit in the free room beside the sketch of the last segment block instead of below it.</param>
internal sealed record ReportBlock(ReportBlockKind Kind, int Index, float Top, float Height, bool Embedded = false);
/// <param name="UpperBottom">Where the upper two-column region ends; the diagrams of the first graphic page fill down to it.</param>
internal sealed record ReportPage(float BodyTop, bool HasGraphics, float UpperBottom,
    IReadOnlyList<ReportTableFragment> Tables, IReadOnlyList<(string Label, string Value)> Metadata,
    IReadOnlyList<ReportBlock> Blocks)
{
    internal float? SummaryTop => Blocks.FirstOrDefault(b => b.Kind == ReportBlockKind.Summary)?.Top;
}
