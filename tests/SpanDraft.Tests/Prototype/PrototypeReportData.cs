using SkiaSharp;
using SpanDraft.Core.Sections.Parametric;

namespace SpanDraft.Reporting.Prototype;

// Layout-review inputs only. Derive the production contract after visual approval.
internal sealed record PrototypeReportData(string Title, string? Project, string? Reference, string Author,
    string Version, string Date, RectangularHollowSectionGeometry Section,
    string Inertia, string Modulus, IReadOnlyList<PrototypeTable> Tables,
    IReadOnlyList<(string Label, string Value, string? Position)> Indicators);

internal enum PrototypeGraphic { Beam, Displacement, Shear, Moment }
internal sealed record PrototypeTechnicalGraphics(float BeamHeight,
    Action<SKCanvas, PrototypeGraphic, SKRect> Draw);
internal sealed record PrototypeTable(string Title, string[] Headers, float[] Widths, string[][] Rows);
internal sealed record PrototypeTableFragment(int Table, int Start, int Count, SKRect Bounds);
internal sealed record PrototypePage(float BodyTop, IReadOnlyList<PrototypeTableFragment> Tables,
    bool HasGraphics, float? SummaryTop);
