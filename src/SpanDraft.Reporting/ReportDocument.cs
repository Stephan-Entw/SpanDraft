using SpanDraft.Reporting.Notation;
using System.Globalization;
using SkiaSharp;

namespace SpanDraft.Reporting;

/// <summary>One frozen A4 composition, shared by preview, PDF and printer output.</summary>
internal sealed class ReportDocument : IDisposable
{
    internal const float Mm = 72 / 25.4f;
    internal const float PageWidth = 210 * Mm, PageHeight = 297 * Mm;
    internal const float Margin = 14 * Mm, Gutter = 4 * Mm, LeftWidth = 128 * Mm, RightWidth = 50 * Mm;
    internal const float MainHeight = 510, RowHeight = 15, FooterTop = PageHeight - 58;
    private const float BlockGap = 12, LineHeight = 12, SectionDrawingHeight = 118;
    private static readonly SKColor Ink = SKColor.Parse("#202631"), Secondary = SKColor.Parse("#5E6878"),
        Rule = SKColor.Parse("#C5CDD8");
    private readonly ReportTechnicalGraphics graphics;
    private readonly List<IDisposable> ownedGraphics = [];
    private readonly string[] sectionLines, materialLines;
    private readonly TechnicalText[] sectionProperties, materialProperties;
    private readonly float materialTop, firstTableTop, summaryHeight;
    private readonly List<(string Text, SKRect Bounds)> textRuns = [];
    internal ReportData Data { get; }
    internal IReadOnlyList<ReportPage> Pages { get; }
    internal IReadOnlyList<(string Text, SKRect Bounds)> TextRuns => textRuns;

    internal ReportDocument(ReportData data, ReportTechnicalGraphics graphics)
    {
        // Copy collection boundaries: a report cannot change after it has been composed.
        Data = data with
        {
            Metadata = data.Metadata.ToArray(), SectionProperties = data.SectionProperties.ToArray(),
            MaterialProperties = data.MaterialProperties.ToArray(), Indicators = data.Indicators.ToArray(),
            Labels = data.Labels with { Assumptions = data.Labels.Assumptions.ToArray() },
            Tables = data.Tables.Select(t => t with { Headers = t.Headers.ToArray(), Widths = t.Widths.ToArray(),
                Rows = t.Rows.Select(r => (IReadOnlyList<string>)r.ToArray()).ToArray() }).ToArray()
        };
        this.graphics = graphics;
        var metadata = Data.Metadata.ToList();
        sectionLines = CompactName(Data.Section, Data.Labels.Section, false, metadata);
        sectionProperties = PropertyLines(Data.SectionProperties);
        materialLines = CompactName(Data.Material, Data.Labels.Material, true, metadata);
        Data = Data with { Metadata = metadata.ToArray() };
        materialProperties = PropertyLines(Data.MaterialProperties);
        materialTop = sectionLines.Length * LineHeight + SectionDrawingHeight
            + sectionProperties.Length * LineHeight + BlockGap;
        firstTableTop = materialTop + 18 + materialLines.Length * LineHeight + 2
            + materialProperties.Length * LineHeight + BlockGap;
        summaryHeight = MeasureSummary();
        Pages = Compose();
    }

    internal void Own(IDisposable graphic) => ownedGraphics.Add(graphic);

    private string[] CompactName(string value, string label, bool strong, List<(string Label, string Value)> metadata)
    {
        var lines = Wrap(value, RightWidth, 8.5f, strong);
        if (lines.Length <= 3) return lines;
        // Keep the complete name in the header pages instead of displacing the technical drawing.
        metadata.Add((label, value));
        return [.. lines.Take(2), "…"];
    }

    private TechnicalText[] PropertyLines(IReadOnlyList<TechnicalText> properties)
    {
        var joined = TechnicalText.Join("    ", properties);
        return Width(joined, 8.5f) <= RightWidth ? [joined]
            : properties.SelectMany(p => Wrap(p, RightWidth, 8.5f)).ToArray();
    }

    private IReadOnlyList<ReportPage> Compose()
    {
        var pages = new List<ReportPage>();
        var metadata = Data.Metadata.SelectMany(m => Wrap(m.Value, PageWidth - 2 * Margin - 64, 8.5f,
            m.Label == Data.Metadata.FirstOrDefault().Label).Select((line, i) => (i == 0 ? m.Label : "", line))).ToArray();
        // Very long metadata has its own pages; it must not squeeze the technical diagrams out of existence.
        bool separateMetadata = metadata.Length > 8;
        if (separateMetadata)
            foreach (var chunk in metadata.Chunk(48))
                pages.Add(new(Margin + 44 + chunk.Length * LineHeight, false, null, [], chunk));
        var firstMetadata = separateMetadata ? [] : metadata;
        float bodyTop = separateMetadata ? ContinuationTop : Margin + 44 + firstMetadata.Length * LineHeight;
        float mainBottom = Math.Min(bodyTop + MainHeight, FooterTop - summaryHeight - 20);
        bool fitsGraphicsWithSummary = mainBottom - bodyTop >= graphics.BeamHeight + 4 + 3 * 60;
        if (!fitsGraphicsWithSummary) mainBottom = FooterTop - 14;
        int table = 0, row = 0, line = 0;
        var fragments = FillColumn(Margin + LeftWidth + Gutter, bodyTop + firstTableTop,
            RightWidth, mainBottom, ref table, ref row, ref line);
        bool complete = table == Data.Tables.Count;
        bool firstSummary = complete && fitsGraphicsWithSummary;
        pages.Add(new(bodyTop, true, firstSummary ? mainBottom + 16 : null, fragments, firstMetadata));
        bool summaryPending = !firstSummary;
        while (table < Data.Tables.Count || summaryPending)
        {
            int previousTable = table, previousRow = row, previousLine = line;
            bodyTop = ContinuationTop;
            float bottom = FooterTop - 14 - (summaryPending ? summaryHeight + 20 : 0);
            fragments = [];
            const float gap = 6 * Mm;
            float width = (PageWidth - 2 * Margin - gap) / 2;
            for (int column = 0; column < 2 && table < Data.Tables.Count; column++)
                fragments.AddRange(FillColumn(Margin + column * (width + gap), bodyTop, width, bottom,
                    ref table, ref row, ref line, singleTable: true));
            if (!summaryPending && previousTable == table && previousRow == row && previousLine == line)
                throw new InvalidOperationException("A report table cannot fit on a continuation page.");
            float? summary = summaryPending ? (fragments.Count == 0 ? bodyTop : fragments.Max(f => f.Bounds.Bottom)) + 20 : null;
            pages.Add(new(bodyTop, false, summary, fragments.ToArray(), []));
            summaryPending = false;
        }
        return pages.AsReadOnly();
    }

    private float ContinuationTop => Margin + 42 + Math.Min(2, Wrap(Data.Title, PageWidth - 2 * Margin, 8.5f).Length) * LineHeight;

    private List<ReportTableFragment> FillColumn(float x, float y, float width, float bottom,
        ref int table, ref int row, ref int firstLine, bool singleTable = false)
    {
        var fragments = new List<ReportTableFragment>();
        while (table < Data.Tables.Count)
        {
            var input = Data.Tables[table];
            if (input.Rows.Count == 0) { table++; row = firstLine = 0; continue; }
            float heading = TableHeading(input, width);
            if (bottom - y < heading + RowHeight) break;
            var slices = new List<ReportRowSlice>();
            float cursor = y + heading;
            while (row < input.Rows.Count)
            {
                var cells = input.Rows[row].Select((value, column) =>
                    Wrap(value, input.Widths[column] * width - 4, 8.5f, column == 0)).ToArray();
                int lines = cells.Max(c => c.Length);
                int capacity = (int)Math.Floor((bottom - cursor - 3) / LineHeight);
                int remaining = lines - firstLine;
                if (capacity <= 0) break;
                // Keep normal rows together; only split a row that cannot fit on a fresh continuation column.
                int maximum = (int)Math.Floor((FooterTop - 14 - ContinuationTop - heading - 3) / LineHeight);
                if (remaining > capacity && firstLine == 0 && (remaining <= maximum || width == RightWidth)) break;
                int count = Math.Min(remaining, capacity);
                slices.Add(new(row, firstLine, count, cells));
                cursor += Math.Max(RowHeight, count * LineHeight + 3);
                firstLine += count;
                if (firstLine != lines) break;
                row++; firstLine = 0;
            }
            if (slices.Count == 0) break;
            fragments.Add(new(table, new(x, y, x + width, cursor), heading, slices.ToArray()));
            if (row != input.Rows.Count) break;
            table++; row = firstLine = 0;
            if (singleTable) break;
            y = cursor + BlockGap;
        }
        return fragments;
    }

    private float TableHeading(ReportTable table, float width)
    {
        float title = Wrap(table.Title + " · " + Data.Labels.Continuation, width, 10, true).Length * LineHeight + 6;
        int lines = table.Headers.Select((h, i) => Wrap(h, table.Widths[i] * width, 7.5f, i == 0).Length).Max();
        return title + lines * LineHeight + 2;
    }

    internal IReadOnlyList<(ReportGraphic Kind, SKRect Bounds)> TechnicalAreas(int pageIndex)
    {
        var page = Pages[pageIndex];
        if (!page.HasGraphics) return [];
        float top = page.BodyTop;
        float bottom = page.SummaryTop.HasValue ? page.SummaryTop.Value - 16 : FooterTop - 14;
        float height = (bottom - top - graphics.BeamHeight - 4) / 3;
        if (height < 60) throw new InvalidOperationException("Insufficient space for the report diagrams.");
        var areas = new List<(ReportGraphic, SKRect)>
        {
            (ReportGraphic.Beam, new(Margin, top, Margin + LeftWidth, top + graphics.BeamHeight))
        };
        top += graphics.BeamHeight + 4;
        foreach (var kind in new[] { ReportGraphic.Displacement, ReportGraphic.Shear, ReportGraphic.Moment })
        { areas.Add((kind, new(Margin, top, Margin + LeftWidth, top + height))); top += height; }
        float x = Margin + LeftWidth + Gutter;
        top = page.BodyTop + sectionLines.Length * LineHeight;
        areas.Add((ReportGraphic.Section, new(x, top, x + RightWidth, top + SectionDrawingHeight)));
        return areas;
    }

    internal void WritePdf(Stream output)
    {
        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
        {
            Title = Data.Title, Author = Data.Author, Creator = "SpanDraft " + Data.Version,
            Creation = Data.CreatedAt.UtcDateTime, Modified = Data.CreatedAt.UtcDateTime
        }) ?? throw new InvalidOperationException("Cannot create the report PDF.");
        for (int i = 0; i < Pages.Count; i++)
        { DrawPage(document.BeginPage(PageWidth, PageHeight), i); document.EndPage(); }
        document.Close();
    }

    internal SKBitmap RenderPage(int page, float dpi = 300)
    {
        if (!float.IsFinite(dpi) || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        float scale = dpi / 72;
        var bitmap = new SKBitmap(new SKImageInfo((int)Math.Round(PageWidth * scale),
            (int)Math.Round(PageHeight * scale), SKColorType.Bgra8888, SKAlphaType.Opaque));
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Scale(scale); DrawPage(canvas, page);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    internal void WritePng(Stream output, int page)
    {
        using var bitmap = RenderPage(page);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        png.SaveTo(output);
    }

    internal void DrawPage(SKCanvas canvas, int index)
    {
        var page = Pages[index];
        textRuns.Clear();
        using var restore = new SKAutoCanvasRestore(canvas);
        canvas.Clear(SKColors.White);
        Text(canvas, Data.Labels.Heading + (index > 0 ? " · " + Data.Labels.Continuation : ""),
            PageWidth / 2, Margin, 16, strong: true, align: SKTextAlign.Center);
        if (page.Metadata.Count > 0)
        {
            float y = Margin + 28;
            foreach (var (label, value) in page.Metadata)
            { Text(canvas, label, Margin, y, color: Secondary); Text(canvas, value, Margin + 64, y, strong: label == Data.Metadata.FirstOrDefault().Label); y += LineHeight; }
        }
        else if (index > 0)
        {
            float y = Margin + 27;
            foreach (string line in Wrap(Data.Title, PageWidth - 2 * Margin, 8.5f).Take(2))
            { Text(canvas, line, Margin, y, color: Secondary); y += LineHeight; }
        }
        Line(canvas, Margin, page.BodyTop - 12, PageWidth - Margin, page.BodyTop - 12, Rule, .6f);
        if (page.HasGraphics)
        {
            foreach (var area in TechnicalAreas(index)) graphics.Draw(canvas, area.Kind, area.Bounds);
            float x = Margin + LeftWidth + Gutter;
            Lines(canvas, sectionLines, x, page.BodyTop);
            Lines(canvas, sectionProperties, x, page.BodyTop + sectionLines.Length * LineHeight + SectionDrawingHeight);
            float top = page.BodyTop + materialTop;
            Heading(canvas, Data.Labels.Material, x, top, RightWidth);
            Lines(canvas, materialLines, x, top + 18, strong: true);
            Lines(canvas, materialProperties, x, top + 18 + materialLines.Length * LineHeight + 2);
        }
        foreach (var fragment in page.Tables) DrawTable(canvas, fragment);
        if (page.SummaryTop is { } summary) DrawSummary(canvas, summary);
        Line(canvas, Margin, FooterTop, PageWidth - Margin, FooterTop, Rule, .6f);
        using var footer = new ReportTextLayout("SpanDraft " + Data.Version + Data.Date
            + string.Format(CultureInfo.InvariantCulture, Data.Labels.PageFormat, index + 1, Pages.Count), 7.5f);
        float footerY = Math.Min(FooterTop + 10, PageHeight - Margin - footer.Height);
        Text(canvas, "SpanDraft " + Data.Version, Margin, footerY, 7.5f, color: Secondary);
        Text(canvas, string.Format(CultureInfo.InvariantCulture, Data.Labels.PageFormat, index + 1, Pages.Count),
            PageWidth / 2, footerY, 7.5f, align: SKTextAlign.Center, color: Secondary);
        Text(canvas, Data.Date, PageWidth - Margin, footerY, 7.5f, align: SKTextAlign.Right, color: Secondary);
    }

    private void DrawTable(SKCanvas canvas, ReportTableFragment fragment)
    {
        var table = Data.Tables[fragment.Table]; var bounds = fragment.Bounds;
        bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
        var title = Wrap(table.Title + (continued ? " · " + Data.Labels.Continuation : ""), bounds.Width, 10, true);
        Lines(canvas, title, bounds.Left, bounds.Top, 10, true);
        float titleHeight = Wrap(table.Title + " · " + Data.Labels.Continuation, bounds.Width, 10, true).Length * LineHeight;
        Line(canvas, bounds.Left, bounds.Top + titleHeight + 2, bounds.Right, bounds.Top + titleHeight + 2, Rule, .5f);
        float x = bounds.Left;
        for (int i = 0; i < table.Headers.Count; i++)
        {
            float cellWidth = table.Widths[i] * bounds.Width;
            var lines = Wrap(table.Headers[i], cellWidth, 7.5f, i == 0);
            Lines(canvas, lines, i == 0 ? x : x + cellWidth, bounds.Top + titleHeight + 6, 7.5f,
                i == 0, i == 0 ? SKTextAlign.Left : SKTextAlign.Right, Secondary);
            x += cellWidth;
        }
        Line(canvas, bounds.Left, bounds.Top + fragment.HeadingHeight - 3,
            bounds.Right, bounds.Top + fragment.HeadingHeight - 3, Rule, .4f);
        float y = bounds.Top + fragment.HeadingHeight + 2;
        foreach (var row in fragment.Rows)
        {
            x = bounds.Left;
            for (int i = 0; i < row.Cells.Count; i++)
            {
                float cellWidth = table.Widths[i] * bounds.Width;
                Lines(canvas, row.Cells[i].Skip(row.FirstLine).Take(row.LineCount), i == 0 ? x : x + cellWidth,
                    y, 8.5f, i == 0, i == 0 ? SKTextAlign.Left : SKTextAlign.Right);
                x += cellWidth;
            }
            y += Math.Max(RowHeight, row.LineCount * LineHeight + 3);
        }
    }

    private float MeasureSummary()
    {
        float width = (PageWidth - 2 * Margin) / 2 - 14;
        float height = 19;
        for (int i = 0; i < Data.Indicators.Count; i += 2)
            height += Enumerable.Range(i, Math.Min(2, Data.Indicators.Count - i)).Max(n =>
                Math.Max(1, Wrap(Data.Indicators[n].Label, width - Width(Data.Indicators[n].Value, 9, true) - 8, 8.5f).Length)
                    * LineHeight + (Data.Indicators[n].Position is null ? 0 : LineHeight) + 4);
        return height + 17 + Data.Labels.Assumptions.Sum(n => Wrap(n, PageWidth - 2 * Margin, 8).Length * LineHeight);
    }

    private void DrawSummary(SKCanvas canvas, float top)
    {
        float width = PageWidth - 2 * Margin, cellWidth = width / 2 - 14;
        Heading(canvas, Data.Labels.Summary, Margin, top, width);
        float y = top + 19;
        for (int row = 0; row < Data.Indicators.Count; row += 2)
        {
            float height = 0;
            for (int i = row; i < Math.Min(row + 2, Data.Indicators.Count); i++)
            {
                var indicator = Data.Indicators[i];
                float x = Margin + (i % 2) * (width / 2 + 6);
                var label = Wrap(indicator.Label, cellWidth - Width(indicator.Value, 9, true) - 8, 8.5f);
                Lines(canvas, label, x, y, color: Secondary);
                Text(canvas, indicator.Value, x + cellWidth, y, 9, true, SKTextAlign.Right);
                float h = label.Length * LineHeight;
                if (indicator.Position is { } position)
                {
                    var positions = Wrap(position, cellWidth, 7.5f);
                    Lines(canvas, positions, x + cellWidth, y + h, 7.5f, align: SKTextAlign.Right, color: Secondary);
                    h += positions.Length * LineHeight;
                }
                height = Math.Max(height, h + 4);
            }
            y += height;
        }
        Heading(canvas, Data.Labels.Notes, Margin, y + 2, width);
        y += 20;
        foreach (string assumption in Data.Labels.Assumptions)
        {
            var lines = Wrap(assumption, width, 8);
            Lines(canvas, lines, Margin, y, 8);
            y += lines.Length * LineHeight;
        }
    }

    private void Lines(SKCanvas canvas, IEnumerable<string> lines, float x, float y, float size = 8.5f,
        bool strong = false, SKTextAlign align = SKTextAlign.Left, SKColor? color = null)
    { foreach (string line in lines) { Text(canvas, line, x, y, size, strong, align, color); y += LineHeight; } }

    private void Heading(SKCanvas canvas, string text, float x, float y, float width)
    { Text(canvas, text, x, y, 10, true); Line(canvas, x, y + 14, x + width, y + 14, Rule, .5f); }

    private float Width(string text, float size, bool strong = false) => Width((TechnicalText)text, size, strong);
    private float Width(TechnicalText text, float size, bool strong = false)
    {
        using var layout = new ReportTextLayout(text, size, strong);
        return layout.Width;
    }

    private string[] Wrap(string text, float width, float size, bool strong = false) =>
        Wrap((TechnicalText)text, width, size, strong).Select(line => line.PlainText).ToArray();

    private TechnicalText[] Wrap(TechnicalText text, float width, float size, bool strong = false)
    {
        var lines = new List<TechnicalText>();
        int offset = 0;
        foreach (string paragraph in text.PlainText.Split('\n'))
        {
            int length = paragraph.TrimEnd('\r').Length;
            var pending = text.Slice(offset, length);
            offset += paragraph.Length + 1;
            if (length == 0) { lines.Add(pending); continue; }
            while (pending.PlainText.Length > 0)
            {
                if (Width(pending, size, strong) <= width) { lines.Add(pending); break; }
                int[] elements = StringInfo.ParseCombiningCharacters(pending.PlainText);
                int low = 1, high = elements.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    int end = mid == elements.Length ? pending.PlainText.Length : elements[mid];
                    if (Width(pending.Slice(0, end), size, strong) <= width) low = mid; else high = mid - 1;
                }
                int count = low == elements.Length ? pending.PlainText.Length : elements[low];
                int space = pending.PlainText.LastIndexOf(' ', Math.Max(0, count - 1), count);
                if (space > 0) count = space;
                lines.Add(pending.Slice(0, count));
                int next = count;
                while (next < pending.PlainText.Length && pending.PlainText[next] == ' ') next++;
                pending = pending.Slice(next, pending.PlainText.Length - next);
            }
        }
        return lines.ToArray();
    }
    private void Lines(SKCanvas canvas, IEnumerable<TechnicalText> lines, float x, float y, float size = 8.5f,
        bool strong = false, SKTextAlign align = SKTextAlign.Left, SKColor? color = null)
    { foreach (var line in lines) { Text(canvas, line, x, y, size, strong, align, color); y += LineHeight; } }

    private SKRect Text(SKCanvas canvas, string text, float x, float y, float size = 8.5f,
        bool strong = false, SKTextAlign align = SKTextAlign.Left, SKColor? color = null, bool track = true) =>
        Text(canvas, (TechnicalText)text, x, y, size, strong, align, color, track);

    private SKRect Text(SKCanvas canvas, TechnicalText text, float x, float y, float size = 8.5f,
        bool strong = false, SKTextAlign align = SKTextAlign.Left, SKColor? color = null, bool track = true)
    {
        using var layout = new ReportTextLayout(text, size, strong);
        using var paint = new SKPaint { Color = color ?? Ink, IsAntialias = true };
        float left = x - (align == SKTextAlign.Right ? layout.Width : align == SKTextAlign.Center ? layout.Width / 2 : 0);
        layout.Draw(canvas, left, y, paint);
        var bounds = new SKRect(left, y, left + layout.Width, y + layout.Height);
        if (track) textRuns.Add((text.PlainText, bounds));
        return bounds;
    }

    private static void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width)
    {
        using var paint = new SKPaint { Color = color, StrokeWidth = width, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    public void Dispose()
    {
        foreach (var graphic in ownedGraphics) graphic.Dispose();
        ownedGraphics.Clear();
    }
}
