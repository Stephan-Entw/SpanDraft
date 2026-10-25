using SpanDraft.Reporting.Notation;
using SkiaSharp;

namespace SpanDraft.Reporting;

// Display values are frozen by Desktop. Reporting never interprets a domain model.
internal sealed record ReportData(string Title, string Author, string Version, DateTimeOffset CreatedAt,
    string Date, IReadOnlyList<(string Label, string Value)> Metadata, string Section,
    IReadOnlyList<TechnicalText> SectionProperties, string Material, IReadOnlyList<TechnicalText> MaterialProperties,
    IReadOnlyList<ReportTable> Tables, IReadOnlyList<ReportIndicator> Indicators, ReportLabels Labels);
internal sealed record ReportLabels(string Heading, string Continuation, string PageFormat,
    string Section, string Material, string Summary, string Notes, IReadOnlyList<string> Assumptions,
    string Title, string Project, string Reference, string Author);
internal sealed record ReportTable(string Title, IReadOnlyList<TechnicalText> Headers,
    IReadOnlyList<float> Widths, IReadOnlyList<IReadOnlyList<string>> Rows);
internal sealed record ReportIndicator(TechnicalText Label, string Value, string? Position);
internal enum ReportGraphic { Beam, Displacement, Shear, Moment, Section }
internal sealed record ReportTechnicalGraphics(float BeamHeight, Action<SKCanvas, ReportGraphic, SKRect> Draw);
internal sealed record ReportRowSlice(int Row, int FirstLine, int LineCount, IReadOnlyList<string[]> Cells);
internal sealed record ReportTableFragment(int Table, SKRect Bounds, float HeadingHeight,
    IReadOnlyList<ReportRowSlice> Rows);
internal sealed record ReportPage(float BodyTop, bool HasGraphics, float? SummaryTop,
    IReadOnlyList<ReportTableFragment> Tables, IReadOnlyList<(string Label, string Value)> Metadata);
