using SkiaSharp;
using SpanDraft.Core.Sections.Geometry;

namespace SpanDraft.Reporting.Prototype;

/// <summary>A paper-layout experiment, deliberately internal until the layout review.</summary>
internal sealed class PrototypeReport : IDisposable
{
    internal const float Mm = 72 / 25.4f;
    internal const float PageWidth = 210 * Mm, PageHeight = 297 * Mm;
    internal const float Margin = 14 * Mm, Gutter = 4 * Mm, LeftWidth = 128 * Mm, RightWidth = 50 * Mm;
    internal const float MainHeight = 510, RowHeight = 15, TableHeadingHeight = 32;
    internal const float FooterTop = PageHeight - 58, SummaryHeight = 122;
    private const float BlockGap = 12, SectionPropertiesTop = 133, MaterialPropertiesTop = 32;
    private const float MaterialTop = SectionPropertiesTop + RowHeight - 2 + BlockGap;
    private const float FirstTableTop = MaterialTop + MaterialPropertiesTop + RowHeight - 2 + BlockGap;
    private static readonly SKColor Ink = SKColor.Parse("#202631"), Secondary = SKColor.Parse("#5E6878"),
        Rule = SKColor.Parse("#C5CDD8");
    private readonly PrototypeReportData data;
    private readonly PrototypeTechnicalGraphics graphics;
    private readonly SKTypeface regular = SKTypeface.FromFamilyName("Arial");
    private readonly SKTypeface bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    internal IReadOnlyList<PrototypePage> Pages { get; }
    internal List<(string Text, SKRect Bounds)> TextRuns { get; } = [];
    internal string FontFamily => regular.FamilyName;

    internal PrototypeReport(PrototypeReportData data, PrototypeTechnicalGraphics graphics, int continuationColumns = 2)
    {
        this.data = data;
        this.graphics = graphics;
        if (continuationColumns is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(continuationColumns));
        Pages = Compose(data, continuationColumns);
    }

    private static string[][] Metadata(PrototypeReportData data) => new[]
    {
        new[] { "Titel", data.Title }, new[] { "Projekt", data.Project ?? "" },
        new[] { "Referenz", data.Reference ?? "" }, new[] { "Erstellt von", data.Author }
    }.Where(row => !string.IsNullOrWhiteSpace(row[1])).ToArray();

    private static IReadOnlyList<PrototypePage> Compose(PrototypeReportData data, int columns)
    {
        float bodyTop = Margin + 44 + Metadata(data).Length * 12;
        var pages = new List<PrototypePage>();
        var fragments = new List<PrototypeTableFragment>();
        int table = 0, start = 0;
        float y = bodyTop + FirstTableTop;
        while (table < data.Tables.Count)
        {
            int count = Math.Min(data.Tables[table].Rows.Length - start,
                (int)Math.Floor((bodyTop + MainHeight - y - TableHeadingHeight) / RowHeight));
            if (count <= 0) break;
            fragments.Add(new(table, start, count, new(Margin + LeftWidth + Gutter, y,
                PageWidth - Margin, y + TableHeadingHeight + count * RowHeight)));
            start += count;
            if (start != data.Tables[table].Rows.Length) break;
            table++; start = 0; y = fragments[^1].Bounds.Bottom + BlockGap;
        }
        pages.Add(new(bodyTop, fragments.ToArray(), true, table == data.Tables.Count ? bodyTop + MainHeight + 16 : null));

        const float continuationGutter = 6 * Mm;
        // shortcut: fixed-height fixture rows only; measure wrapped production rows after layout approval.
        while (table < data.Tables.Count)
        {
            fragments = [];
            bodyTop = Margin + 54;
            bool summaryPage = pages.Count == 1;
            float bottom = FooterTop - 14 - (summaryPage ? SummaryHeight + 16 : 0);
            float width = (PageWidth - 2 * Margin - (columns - 1) * continuationGutter) / columns;
            for (int column = 0; column < columns && table < data.Tables.Count; column++)
            {
                int count = Math.Min(data.Tables[table].Rows.Length - start,
                    (int)Math.Floor((bottom - bodyTop - TableHeadingHeight) / RowHeight));
                float x = Margin + column * (width + continuationGutter);
                fragments.Add(new(table, start, count, new(x, bodyTop, x + width,
                    bodyTop + TableHeadingHeight + count * RowHeight)));
                start += count;
                if (start == data.Tables[table].Rows.Length) { table++; start = 0; }
            }
            float? summaryTop = summaryPage ? fragments.Max(f => f.Bounds.Bottom) + 20 : null;
            pages.Add(new(bodyTop, fragments.ToArray(), false, summaryTop));
        }
        return pages;
    }

    internal void WritePdf(Stream output)
    {
        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
        {
            Title = data.Title, Author = data.Author, Creator = "SpanDraft " + data.Version,
            Creation = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
            Modified = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc)
        }) ?? throw new InvalidOperationException("Cannot create the prototype PDF.");
        for (int page = 0; page < Pages.Count; page++)
        {
            DrawPage(document.BeginPage(PageWidth, PageHeight), page);
            document.EndPage();
        }
        document.Close();
    }

    internal void WritePng(Stream output, int page)
    {
        const float scale = 300 / 72f;
        using var surface = SKSurface.Create(new SKImageInfo((int)Math.Round(PageWidth * scale),
            (int)Math.Round(PageHeight * scale))) ?? throw new InvalidOperationException("Cannot create the prototype preview.");
        surface.Canvas.Scale(scale);
        DrawPage(surface.Canvas, page);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        png.SaveTo(output);
    }

    internal void DrawPage(SKCanvas canvas, int pageIndex)
    {
        var page = Pages[pageIndex];
        TextRuns.Clear();
        canvas.Clear(SKColors.White);
        Text(canvas, pageIndex == 0 ? "Berechnungsbericht" : "Berechnungsbericht · Fortsetzung",
            PageWidth / 2, Margin, 16, strong: true, align: SKTextAlign.Center);
        if (pageIndex == 0)
        {
            float y = Margin + 28;
            foreach (var row in Metadata(data))
            {
                Text(canvas, row[0], Margin, y, color: Secondary);
                Text(canvas, row[1], Margin + 64, y, strong: row[0] == "Titel");
                y += 12;
            }
        }
        else Text(canvas, data.Title, Margin, Margin + 27, color: Secondary);
        Line(canvas, Margin, page.BodyTop - 12, PageWidth - Margin, page.BodyTop - 12, Rule, .6f);

        if (page.HasGraphics)
        {
            foreach (var area in TechnicalAreas(pageIndex)) graphics.Draw(canvas, area.Kind, area.Bounds);
            DrawSection(canvas, page.BodyTop);
            DrawMaterial(canvas, page.BodyTop + MaterialTop);
        }
        foreach (var fragment in page.Tables) DrawTable(canvas, fragment);
        if (page.SummaryTop is { } top) DrawSummary(canvas, top);
        Line(canvas, Margin, FooterTop, PageWidth - Margin, FooterTop, Rule, .6f);
        Text(canvas, "SpanDraft " + data.Version, Margin, FooterTop + 10, 7.5f, color: Secondary);
        Text(canvas, $"Seite {pageIndex + 1} von {Pages.Count}", PageWidth / 2, FooterTop + 10, 7.5f,
            align: SKTextAlign.Center, color: Secondary);
        Text(canvas, data.Date, PageWidth - Margin, FooterTop + 10, 7.5f, align: SKTextAlign.Right, color: Secondary);
    }

    internal IReadOnlyList<(PrototypeGraphic Kind, SKRect Bounds)> TechnicalAreas(int pageIndex)
    {
        var page = Pages[pageIndex];
        if (!page.HasGraphics) return [];
        float top = page.BodyTop;
        var areas = new List<(PrototypeGraphic, SKRect)>
        {
            (PrototypeGraphic.Beam, new(Margin, top, Margin + LeftWidth, top + graphics.BeamHeight))
        };
        top += graphics.BeamHeight + 4;
        float bottom = page.SummaryTop.HasValue ? page.BodyTop + MainHeight : FooterTop - 14;
        float height = (bottom - top) / 3;
        if (height < 60) throw new InvalidOperationException("The fixture leaves insufficient room for the diagrams.");
        foreach (var kind in new[] { PrototypeGraphic.Displacement, PrototypeGraphic.Shear, PrototypeGraphic.Moment })
        {
            areas.Add((kind, new(Margin, top, Margin + LeftWidth, top + height)));
            top += height;
        }
        return areas;
    }

    private void DrawSection(SKCanvas canvas, float top)
    {
        float x = Margin + LeftWidth + Gutter;
        Text(canvas, "Rechteckhohlprofil · y-y", x, top + 1);
        var section = data.Section;
        float scale = 720, shapeX = x + 50, shapeY = top + 25;
        SKPoint Project(SectionPoint p) => new(shapeX + (float)p.Y.Meters * scale,
            shapeY + (float)(section.Height.Meters - p.Z.Meters) * scale);
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in new[] { section.Geometry.OuterContour }.Concat(section.Geometry.Holes))
        {
            path.MoveTo(Project(contour.Segments[0].Start));
            foreach (var segment in contour.Segments)
            {
                if (segment is SectionArc arc)
                {
                    var c = Project(arc.Center); float r = (float)arc.Radius.Meters * scale;
                    float angle = (float)(-Math.Atan2(arc.Start.Z.Meters - arc.Center.Z.Meters,
                        arc.Start.Y.Meters - arc.Center.Y.Meters) * 180 / Math.PI);
                    path.ArcTo(new(c.X - r, c.Y - r, c.X + r, c.Y + r), angle, (float)(-arc.SweepRadians * 180 / Math.PI), false);
                }
                else path.LineTo(Project(segment.End));
            }
            path.Close();
        }
        using var fill = new SKPaint { Color = SKColor.Parse("#E8ECF1"), IsAntialias = true };
        canvas.DrawPath(path, fill);
        using var stroke = Stroke(Ink, .8f);
        canvas.DrawPath(path, stroke);
        float width = (float)section.Width.Meters * scale, height = (float)section.Height.Meters * scale;
        using var dash = SKPathEffect.CreateDash([4, 2, 1, 2], 0);
        using var axis = Stroke(Secondary, .5f); axis.PathEffect = dash;
        canvas.DrawLine(shapeX - 8, shapeY + height / 2, shapeX + width + 10, shapeY + height / 2, axis);
        canvas.DrawLine(shapeX + width / 2, shapeY - 5, shapeX + width / 2, shapeY + height + 5, axis);
        Text(canvas, "y", shapeX + width + 12, shapeY + height / 2 - 4, 7.5f, color: Secondary);
        Text(canvas, "z", shapeX + width / 2 + 4, shapeY - 8, 7.5f, color: Secondary);
        Dimension(canvas, shapeX, shapeY + height + 13, shapeX + width, shapeY + height + 13);
        Line(canvas, shapeX, shapeY + height + 2, shapeX, shapeY + height + 16, Secondary, .4f);
        Line(canvas, shapeX + width, shapeY + height + 2, shapeX + width, shapeY + height + 16, Secondary, .4f);
        Text(canvas, "b = 60 mm", shapeX + width / 2, shapeY + height + 19, 7.5f, align: SKTextAlign.Center);
        Dimension(canvas, shapeX - 13, shapeY, shapeX - 13, shapeY + height);
        Line(canvas, shapeX - 16, shapeY, shapeX - 2, shapeY, Secondary, .4f);
        Line(canvas, shapeX - 16, shapeY + height, shapeX - 2, shapeY + height, Secondary, .4f);
        canvas.Save(); canvas.Translate(shapeX - 26, shapeY + height / 2); canvas.RotateDegrees(-90);
        Text(canvas, "h = 100 mm", 0, 0, 7.5f, align: SKTextAlign.Center, track: false); canvas.Restore();
        Text(canvas, "t = 4 mm", shapeX + width + 10, shapeY + 6, 7.5f);
        Line(canvas, shapeX + width - 1.5f, shapeY + 11, shapeX + width + 7, shapeY + 11, Secondary, .4f);
        Text(canvas, "r = 6 mm", shapeX + width + 10, shapeY + 23, 7.5f);
        Line(canvas, shapeX + width - 2, shapeY + 2, shapeX + width + 7, shapeY + 26, Secondary, .4f);
        PropertyPair(canvas, x, top + SectionPropertiesTop, "Iy", data.Inertia, "Wy", data.Modulus);
    }

    private void DrawMaterial(SKCanvas canvas, float top)
    {
        float x = Margin + LeftWidth + Gutter;
        Heading(canvas, "Material", x, top, RightWidth);
        Text(canvas, "S235JR", x, top + 18, strong: true);
        PropertyPair(canvas, x, top + MaterialPropertiesTop, "E", "210000 MPa", "Re", "235 MPa");
    }

    private void PropertyPair(SKCanvas canvas, float x, float y, string firstLabel, string firstValue, string secondLabel, string secondValue)
    {
        Text(canvas, firstLabel + " = " + firstValue, x, y);
        Text(canvas, secondLabel + " = " + secondValue, x + RightWidth, y, align: SKTextAlign.Right);
    }

    private void DrawTable(SKCanvas canvas, PrototypeTableFragment fragment)
    {
        var table = data.Tables[fragment.Table]; var bounds = fragment.Bounds;
        Heading(canvas, table.Title + (fragment.Start > 0 ? " · Fortsetzung" : ""), bounds.Left, bounds.Top, bounds.Width);
        float[] edges = new float[table.Widths.Length + 1]; edges[0] = bounds.Left;
        for (int i = 0; i < table.Widths.Length; i++) edges[i + 1] = edges[i] + table.Widths[i] * bounds.Width;
        for (int column = 0; column < table.Headers.Length; column++)
            Cell(table.Headers[column], column, bounds.Top + 18, 7.5f, Secondary);
        Line(canvas, bounds.Left, bounds.Top + 29, bounds.Right, bounds.Top + 29, Rule, .4f);
        for (int row = 0; row < fragment.Count; row++)
        {
            var values = table.Rows[fragment.Start + row];
            for (int column = 0; column < values.Length; column++)
                Cell(values[column], column, bounds.Top + TableHeadingHeight + row * RowHeight + 2, 8.5f, Ink);
        }
        void Cell(string value, int column, float y, float size, SKColor color)
        {
            float x = column == 0 ? edges[column] : edges[column + 1];
            var rect = Text(canvas, value, x, y, size, strong: column == 0,
                align: column == 0 ? SKTextAlign.Left : SKTextAlign.Right, color: color);
            if (rect.Left < edges[column] - .1f || rect.Right > edges[column + 1] + .1f)
                throw new InvalidOperationException($"Prototype table cell does not fit: {value}");
        }
    }

    private void DrawSummary(SKCanvas canvas, float top)
    {
        float width = PageWidth - 2 * Margin;
        Heading(canvas, "Maßgebende Kennwerte", Margin, top, width);
        for (int index = 0; index < data.Indicators.Count; index++)
        {
            var indicator = data.Indicators[index];
            float x = Margin + (index % 2) * (width / 2 + 6), y = top + 19 + index / 2 * 28;
            Text(canvas, indicator.Label, x, y, 8.5f, color: Secondary);
            Text(canvas, indicator.Value, x + width / 2 - 14, y, 9, strong: true, align: SKTextAlign.Right);
            if (indicator.Position is { } position) Text(canvas, position, x + width / 2 - 14, y + 12, 7.5f,
                align: SKTextAlign.Right, color: Secondary);
        }
        Heading(canvas, "Berechnungsgrundlagen / Hinweise", Margin, top + 68, width);
        Text(canvas, "Euler-Bernoulli · linear elastisch · kleine Verformungen · konstanter Werkstoff und Querschnitt.", Margin, top + 86, 8);
        Text(canvas, "Schubverformung und Eigengewicht sind nicht berücksichtigt. S = Re / σb,max ist eine elastische Bewertung,", Margin, top + 98, 8);
        Text(canvas, "kein normativer Festigkeits- oder Sicherheitsnachweis.", Margin, top + 110, 8);
    }

    private void Heading(SKCanvas canvas, string text, float x, float y, float width)
    {
        Text(canvas, text, x, y, 10, strong: true);
        Line(canvas, x, y + 14, x + width, y + 14, Rule, .5f);
    }

    private SKRect Text(SKCanvas canvas, string text, float x, float y, float size = 8.5f,
        bool strong = false, SKTextAlign align = SKTextAlign.Left, SKColor? color = null, bool track = true)
    {
        using var font = new SKFont(strong ? bold : regular, size);
        // Arial lacks some technical superscripts; use system glyph fallback, never replacement boxes.
        var fonts = new List<(string Text, SKFont Font)>();
        foreach (var rune in text.EnumerateRunes())
        {
            string character = rune.ToString();
            SKFont selected = font;
            if (!font.ContainsGlyphs(character))
            {
                using var face = SKFontManager.Default.MatchCharacter(rune.Value)
                    ?? throw new InvalidOperationException($"No report font provides {character}.");
                selected = new SKFont(face, size);
                if (!selected.ContainsGlyphs(character)) throw new InvalidOperationException($"Missing report glyph {character}.");
            }
            if (fonts.Count > 0 && ReferenceEquals(fonts[^1].Font, selected))
                fonts[^1] = (fonts[^1].Text + character, selected);
            else fonts.Add((character, selected));
        }
        using var paint = new SKPaint { Color = color ?? Ink, IsAntialias = true };
        float width = fonts.Sum(run => run.Font.MeasureText(run.Text));
        float left = x - (align == SKTextAlign.Right ? width : align == SKTextAlign.Center ? width / 2 : 0);
        var bounds = new SKRect(left, y, left + width, y + font.Metrics.Descent - font.Metrics.Ascent);
        float cursor = left;
        foreach (var run in fonts)
        {
            canvas.DrawText(run.Text, cursor, y - font.Metrics.Ascent, SKTextAlign.Left, run.Font, paint);
            cursor += run.Font.MeasureText(run.Text);
            if (!ReferenceEquals(run.Font, font)) run.Font.Dispose();
        }
        if (track) TextRuns.Add((text, bounds));
        return bounds;
    }

    private static SKPaint Stroke(SKColor color, float width) => new()
    { Color = color, StrokeWidth = width, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

    private static void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width)
    { using var paint = Stroke(color, width); canvas.DrawLine(x1, y1, x2, y2, paint); }

    private static void Arrow(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color)
    {
        Line(canvas, x1, y1, x2, y2, color, width);
        float angle = MathF.Atan2(y2 - y1, x2 - x1), length = 3.5f;
        using var path = new SKPath();
        path.MoveTo(x2, y2);
        path.LineTo(x2 - length * MathF.Cos(angle) + 1.3f * MathF.Sin(angle), y2 - length * MathF.Sin(angle) - 1.3f * MathF.Cos(angle));
        path.LineTo(x2 - length * MathF.Cos(angle) - 1.3f * MathF.Sin(angle), y2 - length * MathF.Sin(angle) + 1.3f * MathF.Cos(angle));
        path.Close();
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawPath(path, paint);
    }

    private static void Dimension(SKCanvas canvas, float x1, float y1, float x2, float y2)
    { Arrow(canvas, x1, y1, x2, y2, .45f, Secondary); Arrow(canvas, x2, y2, x1, y1, .45f, Secondary); }

    public void Dispose() { regular.Dispose(); bold.Dispose(); }
}
