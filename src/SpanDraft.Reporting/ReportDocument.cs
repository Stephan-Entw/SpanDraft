using SpanDraft.Reporting.Notation;
using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace SpanDraft.Reporting;

/// <summary>One frozen A4 composition, shared by preview, PDF and printer output.</summary>
internal sealed partial class ReportDocument : IDisposable
{
    internal const float Mm = 72 / 25.4f;
    internal const float PageWidth = 210 * Mm, PageHeight = 297 * Mm;
    internal const float Margin = 14 * Mm, Gutter = 4 * Mm, LeftWidth = 128 * Mm, RightWidth = 50 * Mm;
    internal const float RowHeight = 15, FooterTop = PageHeight - 58;
    private const float BlockGap = 12, LineHeight = 12, HeadingHeight = 18;
    // A segment's detail block: sketch, section values and material values side by side.
    internal const float SketchWidth = 50 * Mm, SketchHeight = 42 * Mm, DetailGap = 6 * Mm,
        DetailMiddleWidth = 60 * Mm, DetailRightWidth = PageWidth - 2 * Margin - SketchWidth - DetailMiddleWidth - 2 * DetailGap;
    private static readonly SKColor Ink = SKColor.Parse("#202631"), Secondary = SKColor.Parse("#5E6878"),
        Rule = SKColor.Parse("#C5CDD8");
    private readonly ReportTechnicalGraphics graphics;
    private readonly List<IDisposable> ownedGraphics = [];
    private readonly SegmentDetail[] details;
    private readonly ReportTableLayout?[] tableLayouts;
    private readonly float summaryHeight, notesHeight, embeddedNotesHeight;
    private readonly List<(string Text, SKRect Bounds)> textRuns = [];
    private readonly (IReadOnlyList<HeaderLine> Lines, float BodyTop) projectHeader;
    internal ReportData Data { get; }
    internal IReadOnlyList<ReportPage> Pages { get; }
    internal IReadOnlyList<(string Text, SKRect Bounds)> TextRuns => textRuns;

    private sealed record SegmentDetail(string[] SectionLines, TechnicalText[] SectionProperties,
        string[] MaterialLines, TechnicalText[] MaterialProperties, float Content, float Height);

    // The room beside a sketch that can hold the notes of a one-page report: the columns of the detail block after the sketch.
    private const float EmbeddedNotesWidth = DetailMiddleWidth + DetailGap + DetailRightWidth, EmbeddedNotesGap = 8;

    internal ReportDocument(ReportData data, ReportTechnicalGraphics graphics)
    {
        // Copy collection boundaries: a report cannot change after it has been composed.
        Data = data with
        {
            Metadata = data.Metadata.ToArray(),
            Segments = data.Segments.Select(s => s with { SectionProperties = s.SectionProperties.ToArray(),
                MaterialProperties = s.MaterialProperties.ToArray() }).ToArray(),
            Indicators = data.Indicators.ToArray(),
            Labels = data.Labels with { Assumptions = data.Labels.Assumptions.ToArray() },
            Tables = data.Tables.Select(t => t with { Headers = t.Headers.ToArray(),
                CellWrap = t.CellWrap?.ToArray(),
                Rows = t.Rows.Select(r => (IReadOnlyList<string>)r.ToArray()).ToArray() }).ToArray()
        };
        this.graphics = graphics;
        details = Data.Segments.Select(Detail).ToArray();
        projectHeader = CreateProjectHeader();
        summaryHeight = 19 + SummaryRows().Sum(row => row.Max(cell => cell.Label.Length * LineHeight + 4));
        notesHeight = 20 + Data.Labels.Assumptions.Sum(n => Wrap(n, PageWidth - 2 * Margin, 8).Length * LineHeight);
        embeddedNotesHeight = 20 + Data.Labels.Assumptions.Sum(n => Wrap(n, EmbeddedNotesWidth, 8).Length * LineHeight);
        tableLayouts = Data.Tables.Select(table => table.Rows.Count == 0 ? null : LayoutTable(table)).ToArray();
        Pages = Compose();
    }

    private SegmentDetail Detail(ReportSegment segment)
    {
        // Names are never shortened: they wrap inside their column and the block grows with them.
        var section = Wrap(segment.Section, DetailMiddleWidth, 8.5f, protectedSpaces: ProtectedSpaces(segment.Section));
        var sectionValues = PropertyLines(segment.SectionProperties, DetailMiddleWidth);
        var material = Wrap(segment.Material, DetailRightWidth, 8.5f, true);
        var materialValues = PropertyLines(segment.MaterialProperties, DetailRightWidth);
        float middle = LineHeight + section.Length * LineHeight + 2 + sectionValues.Length * LineHeight;
        float right = LineHeight + material.Length * LineHeight + 2 + materialValues.Length * LineHeight;
        float content = Math.Max(middle, right);
        return new(section, sectionValues, material, materialValues, content,
            HeadingHeight + Math.Max(SketchHeight, content) + 2);
    }

    /// <summary>Protects quantity/unit spaces and complete radius clauses from line breaks.</summary>
    private static HashSet<int> ProtectedSpaces(string value) => Regex.Matches(value,
            @"\([ \t]*r[ \t]*=[^()\r\n]+\)|(?<=\d)[ \t]+(?=(?:mm|cm|dm|m|µm|μm|in|ft)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .SelectMany(match => Enumerable.Range(match.Index, match.Length).Where(i => value[i] == ' ')).ToHashSet();

    internal void Own(IDisposable graphic) => ownedGraphics.Add(graphic);

    private TechnicalText[] PropertyLines(IReadOnlyList<TechnicalText> properties, float width)
    {
        var joined = TechnicalText.Join("    ", properties);
        return Width(joined, 8.5f) <= width ? [joined]
            : properties.SelectMany(p => Wrap(p, width, 8.5f)).ToArray();
    }

    private sealed class PageBuilder(float bodyTop, bool hasGraphics)
    {
        internal float BodyTop { get; } = bodyTop;
        internal bool HasGraphics { get; } = hasGraphics;
        internal float UpperBottom { get; set; }
        internal List<ReportTableFragment> Tables { get; set; } = [];
        internal IReadOnlyList<(string Label, string Value)> Metadata { get; set; } = [];
        internal List<ReportBlock> Blocks { get; } = [];
    }

    private IReadOnlyList<ReportPage> Compose()
    {
        var pages = new List<PageBuilder>();
        var metadata = Data.Metadata.SelectMany(m => Wrap(m.Value, PageWidth - 2 * Margin - 64, 8.5f,
            m.Label == Data.Metadata.FirstOrDefault().Label).Select((line, i) => (i == 0 ? m.Label : "", line))).ToArray();
        // The left column at its normal compact height: the beam and three diagrams with their minimum plot.
        float leftMinimum = graphics.BeamHeight + 4 + 3 * graphics.MinimumDiagramHeight;
        // Very long metadata has its own pages; it must not squeeze the technical diagrams out of existence.
        bool separateMetadata = metadata.Length > 8 || projectHeader.BodyTop + leftMinimum > FooterTop - 14;
        if (separateMetadata)
            foreach (var chunk in metadata.Chunk(48))
                pages.Add(new(Margin + 44 + chunk.Length * LineHeight, false) { Metadata = chunk });
        var firstMetadata = separateMetadata ? [] : metadata;
        float bodyTop = separateMetadata ? ContinuationTop : projectHeader.BodyTop;
        const float bottom = FooterTop - 14;
        const float columnGap = 6 * Mm;
        const float columnWidth = (PageWidth - 2 * Margin - columnGap) / 2;
        var freshRegion = new SKRect(Margin, ContinuationTop, Margin + columnWidth, bottom);
        var cursor = new TableCursor();
        bool firstPage = true;
        PageBuilder current;
        float flowTop;
        while (true)
        {
            var before = cursor;
            var fragments = new List<ReportTableFragment>();
            if (firstPage)
            {
                var region = new SKRect(Margin + LeftWidth + Gutter, bodyTop, PageWidth - Margin, bottom);
                var filled = FillRegion(region, cursor, freshRegion.Height, firstPage: true);
                fragments.AddRange(filled.Fragments);
                cursor = filled.Cursor;
            }
            else
            {
                bodyTop = ContinuationTop;
                for (int column = 0; column < 2; column++)
                {
                    float x = Margin + column * (columnWidth + columnGap);
                    var filled = FillRegion(new(x, bodyTop, x + columnWidth, bottom), cursor, freshRegion.Height);
                    fragments.AddRange(filled.Fragments);
                    cursor = filled.Cursor;
                }
            }

            bool complete = cursor.TableIndex == Data.Tables.Count;
            if (!firstPage && complete)
                fragments = BalanceContinuationColumns(before, freshRegion, columnGap, fragments);
            if (!complete && !firstPage && cursor == before)
                throw new InvalidOperationException("A report table cannot fit on a continuation page.");

            float tablesBottom = fragments.Count == 0 ? bodyTop : fragments.Max(f => f.Bounds.Bottom);
            current = new(bodyTop, firstPage) { Tables = fragments, Metadata = firstPage ? firstMetadata : [] };
            // The diagrams start at their compact height; only the room the right column needs beyond it is shared out.
            if (firstPage) current.UpperBottom = Math.Max(tablesBottom, bodyTop + leftMinimum);
            pages.Add(current);
            if (complete)
            {
                flowTop = firstPage ? current.UpperBottom + 16 : fragments.Count == 0 ? bodyTop : tablesBottom + 20;
                break;
            }
            firstPage = false;
        }

        // The page flow below the tables: the key figures, every segment's details, then the notes. A block that does
        // not fit completely moves to the next page; no block depends on a page number or on a number of segments.
        var flow = new List<(ReportBlockKind Kind, int Index, float Height)> { (ReportBlockKind.Summary, 0, summaryHeight) };
        flow.AddRange(details.Select((d, i) => (ReportBlockKind.Segment, i, d.Height)));
        flow.Add((ReportBlockKind.Notes, 0, notesHeight));
        float y = flowTop;
        foreach (var (kind, index, height) in flow)
        {
            if (y + height > bottom)
            {
                current = new(ContinuationTop, false);
                pages.Add(current);
                y = ContinuationTop;
                if (y + height > bottom)
                    throw new InvalidOperationException("A report block cannot fit on a continuation page.");
            }
            current.Blocks.Add(new(kind, index, y, height));
            y += height + BlockGap;
        }
        EmbedNotesIfItKeepsTheReportOnOnePage(pages);
        return pages.Select(p => new ReportPage(p.BodyTop, p.HasGraphics, p.UpperBottom, p.Tables.ToArray(), p.Metadata,
            p.Blocks.ToArray())).ToArray();
    }

    /// <summary>
    /// A report whose only overflow is the notes block stays on one page when the notes fit completely in the free room
    /// beside the sketch of the last segment, below its values. A report that needs several pages anyway keeps the notes
    /// as a block of their own over the full width, so this is no second layout mode but one fit rule.
    /// </summary>
    private void EmbedNotesIfItKeepsTheReportOnOnePage(List<PageBuilder> pages)
    {
        if (pages.Count != 2 || pages[1].HasGraphics || pages[1].Tables.Count > 0 || details.Length == 0) return;
        if (pages[1].Blocks.Count != 1 || pages[1].Blocks[0].Kind != ReportBlockKind.Notes) return;
        var last = pages[0].Blocks.LastOrDefault();
        if (last is not { Kind: ReportBlockKind.Segment } segment) return;
        // The block keeps its height: the notes use only room that is free beside the sketch anyway.
        if (details[segment.Index].Content + EmbeddedNotesGap + embeddedNotesHeight > SketchHeight) return;
        float top = segment.Top + HeadingHeight + details[segment.Index].Content + EmbeddedNotesGap;
        pages[0].Blocks.Add(new(ReportBlockKind.Notes, 0, top, embeddedNotesHeight, Embedded: true));
        pages.RemoveAt(1);
    }

    private float ContinuationTop => Margin + 42 + Math.Min(2, Wrap(Data.Title, PageWidth - 2 * Margin, 8.5f).Length) * LineHeight;

    private List<ReportTableFragment> BalanceContinuationColumns(TableCursor start, SKRect leftRegion,
        float columnGap, List<ReportTableFragment> original)
    {
        // Keep a single short table compact rather than creating a second tiny column.
        if (original.Select(f => f.Table).Distinct().Count() <= 1 && original.Sum(f => f.Rows.Sum(r => Math.Max(RowHeight, r.LineCount * LineHeight + 3))) <= 3 * RowHeight)
            return original;
        var rightRegion = new SKRect(leftRegion.Right + columnGap, leftRegion.Top,
            leftRegion.Right + columnGap + leftRegion.Width, leftRegion.Bottom);
        float Height(IEnumerable<ReportTableFragment> column) =>
            column.Select(f => f.Bounds.Bottom - leftRegion.Top).DefaultIfEmpty(0).Max();
        float bestLeft = Height(original.Where(f => f.Bounds.Left == leftRegion.Left));
        float bestDifference = Math.Abs(bestLeft - Height(original.Where(f => f.Bounds.Left > leftRegion.Left)));
        var best = original;
        // Every possible left prefix already lies in the greedily filled left column.
        // Probe only complete-row boundaries; FillRegion retains all wrapping and fragmentation rules.
        foreach (var fragment in original.Where(f => f.Bounds.Left == leftRegion.Left))
        {
            float bottom = fragment.Bounds.Top + fragment.HeadingHeight;
            foreach (var row in fragment.Rows)
            {
                bottom += Math.Max(RowHeight, row.LineCount * LineHeight + 3);
                if (row.FirstLine + row.LineCount != fragment.Layout.RowLineCounts[row.Row]) continue;
                var left = FillRegion(new(leftRegion.Left, leftRegion.Top, leftRegion.Right,
                    Math.Min(leftRegion.Bottom, bottom + .001f)), start, leftRegion.Height);
                var right = FillRegion(rightRegion, left.Cursor, leftRegion.Height);
                if (right.Cursor.TableIndex != Data.Tables.Count || right.Fragments.Count == 0) continue;
                float leftHeight = Height(left.Fragments), rightHeight = Height(right.Fragments);
                float difference = Math.Abs(leftHeight - rightHeight);
                // Equal scores prefer the fuller left column for a stable reading order.
                if (difference < bestDifference - .001f ||
                    (Math.Abs(difference - bestDifference) <= .001f && leftHeight > bestLeft))
                {
                    best = [.. left.Fragments, .. right.Fragments];
                    bestDifference = difference;
                    bestLeft = leftHeight;
                }
            }
        }
        return best;
    }

    private readonly record struct TableCursor(int TableIndex = 0, int RowIndex = 0, int RowLineOffset = 0)
    {
        internal bool Continued => RowIndex > 0 || RowLineOffset > 0;
    }

    private (List<ReportTableFragment> Fragments, TableCursor Cursor) FillRegion(
        SKRect region, TableCursor cursor, float freshHeight, bool firstPage = false)
    {
        var fragments = new List<ReportTableFragment>();
        float y = region.Top;
        while (cursor.TableIndex < Data.Tables.Count)
        {
            var layout = tableLayouts[cursor.TableIndex];
            if (layout is null) { cursor = new(cursor.TableIndex + 1); continue; }
            float heading = cursor.Continued ? layout.ContinuationHeadingHeight : layout.HeadingHeight;
            if (region.Bottom - y < heading + RowHeight) break;
            var slices = new List<ReportRowSlice>();
            float rowTop = y + heading;
            int table = cursor.TableIndex;
            while (cursor.RowIndex < layout.Rows.Count)
            {
                var cells = layout.Rows[cursor.RowIndex];
                int lines = layout.RowLineCounts[cursor.RowIndex];
                int remaining = lines - cursor.RowLineOffset;
                float available = region.Bottom - rowTop;
                int count = remaining;
                if (Math.Max(RowHeight, remaining * LineHeight + 3) > available)
                {
                    float wholeRowHeight = (cursor.Continued ? layout.ContinuationHeadingHeight : layout.HeadingHeight)
                        + Math.Max(RowHeight, lines * LineHeight + 3);
                    // Keep rows that fit a fresh column intact. Split oversized rows on continuation pages.
                    if (cursor.RowLineOffset == 0 &&
                        (wholeRowHeight <= freshHeight || firstPage)) break;
                    count = (int)Math.Floor((available - 3) / LineHeight);
                    if (count <= 0 || available < RowHeight) break;
                }
                slices.Add(new(cursor.RowIndex, cursor.RowLineOffset, count, cells));
                rowTop += Math.Max(RowHeight, count * LineHeight + 3);
                if (count < remaining)
                {
                    cursor = cursor with { RowLineOffset = cursor.RowLineOffset + count };
                    break;
                }
                cursor = cursor with { RowIndex = cursor.RowIndex + 1, RowLineOffset = 0 };
            }
            if (slices.Count == 0) break;
            fragments.Add(new(table, new(region.Left, y, region.Left + RightWidth, rowTop), heading, slices.ToArray(), layout));
            if (cursor.RowIndex != layout.Rows.Count) break;
            cursor = new(table + 1);
            y = rowTop + BlockGap;
        }
        return (fragments, cursor);
    }

    internal IReadOnlyList<(ReportGraphic Kind, SKRect Bounds)> TechnicalAreas(int pageIndex)
    {
        var page = Pages[pageIndex];
        if (!page.HasGraphics) return [];
        float top = page.BodyTop;
        float height = (page.UpperBottom - top - graphics.BeamHeight - 4) / 3;
        if (height < graphics.MinimumDiagramHeight - .01f) throw new InvalidOperationException("Insufficient space for the report diagrams.");
        var areas = new List<(ReportGraphic, SKRect)>
        {
            (ReportGraphic.Beam, new(Margin, top, Margin + LeftWidth, top + graphics.BeamHeight))
        };
        top += graphics.BeamHeight + 4;
        foreach (var kind in new[] { ReportGraphic.Displacement, ReportGraphic.Shear, ReportGraphic.Moment })
        { areas.Add((kind, new(Margin, top, Margin + LeftWidth, top + height))); top += height; }
        return areas;
    }

    /// <summary>The three columns of a segment's detail block: sketch, section values and material values.</summary>
    internal (SKRect Sketch, SKRect Section, SKRect Material) DetailColumns(ReportBlock block)
    {
        float top = block.Top + HeadingHeight, bottom = block.Top + block.Height;
        float section = Margin + SketchWidth + DetailGap, material = section + DetailMiddleWidth + DetailGap;
        return (new(Margin, top, Margin + SketchWidth, top + SketchHeight), new(section, top, section + DetailMiddleWidth, bottom),
            new(material, top, material + DetailRightWidth, bottom));
    }

    /// <summary>The section sketch of every segment, with the page its detail block lies on.</summary>
    internal IReadOnlyList<(int Page, int Segment, SKRect Bounds)> SketchAreas() =>
        Pages.SelectMany((page, index) => page.Blocks.Where(b => b.Kind == ReportBlockKind.Segment)
            .Select(b => (index, b.Index, new SKRect(Margin, b.Top + HeadingHeight, Margin + SketchWidth,
                b.Top + HeadingHeight + SketchHeight)))).ToArray();

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
            PageWidth / 2, Margin, 8.5f, align: SKTextAlign.Center, color: Secondary);
        float headerBottom = page.BodyTop - 12;
        if (page.HasGraphics && (page.Metadata.Count > 0 || Data.Metadata.Count == 0))
            headerBottom = DrawProjectHeader(canvas);
        else if (page.Metadata.Count > 0)
        {
            string activeLabel = Pages.Take(index).SelectMany(p => p.Metadata)
                .LastOrDefault(m => m.Label.Length > 0).Label ?? "";
            float y = Margin + 28;
            foreach (var (label, value) in page.Metadata)
            {
                if (label.Length > 0) activeLabel = label;
                if (activeLabel == Data.Labels.Title)
                    Text(canvas, value, PageWidth / 2, y, 9.5f, strong: true, align: SKTextAlign.Center);
                else
                {
                    Text(canvas, label, Margin, y, color: Secondary);
                    Text(canvas, value, Margin + 64, y);
                }
                y += LineHeight;
            }
        }
        else if (index > 0)
        {
            float y = Margin + 27;
            foreach (string line in Wrap(Data.Title, PageWidth - 2 * Margin, 8.5f).Take(2))
            { Text(canvas, line, Margin, y, color: Secondary); y += LineHeight; }
        }
        Line(canvas, Margin, headerBottom, PageWidth - Margin, headerBottom, Rule, .6f);
        if (page.HasGraphics)
            foreach (var area in TechnicalAreas(index)) graphics.Draw(canvas, area.Kind, 0, area.Bounds);
        foreach (var fragment in page.Tables) DrawTable(canvas, fragment);
        foreach (var block in page.Blocks)
            switch (block.Kind)
            {
                case ReportBlockKind.Summary: DrawSummary(canvas, block.Top); break;
                case ReportBlockKind.Segment: DrawSegment(canvas, block); break;
                default: DrawNotes(canvas, block); break;
            }
        Line(canvas, Margin, FooterTop, PageWidth - Margin, FooterTop, Rule, .6f);
        using var footer = new ReportTextLayout("SpanDraft " + Data.Version + Data.Date
            + string.Format(CultureInfo.InvariantCulture, Data.Labels.PageFormat, index + 1, Pages.Count), 7.5f);
        float footerY = Math.Min(FooterTop + 10, PageHeight - Margin - footer.Height);
        Text(canvas, "SpanDraft " + Data.Version, Margin, footerY, 7.5f, color: Secondary);
        Text(canvas, string.Format(CultureInfo.InvariantCulture, Data.Labels.PageFormat, index + 1, Pages.Count),
            PageWidth / 2, footerY, 7.5f, align: SKTextAlign.Center, color: Secondary);
        Text(canvas, Data.Date, PageWidth - Margin, footerY, 7.5f, align: SKTextAlign.Right, color: Secondary);
    }

    private sealed record HeaderLine(string Text, float X, float Top, float Size,
        bool Strong = false, bool Secondary = false, SKTextAlign Align = SKTextAlign.Left);

    private (IReadOnlyList<HeaderLine> Lines, float BodyTop) CreateProjectHeader()
    {
        const float titleSize = 13.5f, gap = 12;
        float width = PageWidth - 2 * Margin, columnsWidth = width - 2 * gap;
        string[] labels = [Data.Labels.Project, Data.Labels.Reference, Data.Labels.Author];
        float[] widths = [columnsWidth / 2, columnsWidth / 4, columnsWidth / 4];
        var lines = new List<HeaderLine>();
        float Add(HeaderLine line)
        {
            using var layout = new ReportTextLayout(line.Text, line.Size, line.Strong);
            lines.Add(line);
            return layout.Height;
        }

        // Reserve the measured header before pagination; project and reference retain their placeholders.
        float y = Margin + 13;
        foreach (string line in Wrap(Data.Title, width, titleSize, true))
            y += Math.Max(17, Add(new(line, PageWidth / 2, y, titleSize, Strong: true, Align: SKTextAlign.Center)) + 2);
        y += 4;
        float x = Margin, labelHeight = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            if (i == 2 && string.IsNullOrWhiteSpace(Data.Author)) continue;
            labelHeight = Math.Max(labelHeight, Add(new(labels[i], x, y, 7, Secondary: true)));
            x += widths[i] + gap;
        }
        float valueTop = y + labelHeight + 3, bottom = valueTop;
        x = Margin;
        for (int i = 0; i < labels.Length; i++)
        {
            if (i == 2 && string.IsNullOrWhiteSpace(Data.Author)) continue;
            string? value = Data.Metadata.FirstOrDefault(m => m.Label == labels[i]).Value;
            y = valueTop;
            foreach (string line in Wrap(string.IsNullOrWhiteSpace(value) ? "-" : value, widths[i], 8.5f))
            {
                float height = Add(new(line, x, y, 8.5f));
                bottom = Math.Max(bottom, y + height);
                y += Math.Max(LineHeight, height + 1);
            }
            x += widths[i] + gap;
        }
        y = bottom + 4;
        foreach (var field in Data.Metadata.Where(m => m.Label != Data.Labels.Title && !labels.Contains(m.Label)))
        {
            bool first = true;
            foreach (string line in Wrap(field.Value, width - 64, 8.5f))
            {
                float height = Add(new(line, Margin + 64, y, 8.5f));
                if (first) height = Math.Max(height, Add(new(field.Label, Margin, y, 8.5f, Secondary: true)));
                bottom = y + height;
                y += Math.Max(LineHeight, height + 1);
                first = false;
            }
        }
        return (lines.AsReadOnly(), bottom + 18);
    }

    private float DrawProjectHeader(SKCanvas canvas)
    {
        foreach (var line in projectHeader.Lines)
            Text(canvas, line.Text, line.X, line.Top, line.Size, line.Strong, line.Align, line.Secondary ? Secondary : Ink);
        return projectHeader.BodyTop - 12;
    }

    private void DrawTable(SKCanvas canvas, ReportTableFragment fragment)
    {
        var table = Data.Tables[fragment.Table]; var bounds = fragment.Bounds;
        var layout = fragment.Layout;
        bool continued = fragment.Rows[0].Row > 0 || fragment.Rows[0].FirstLine > 0;
        var title = continued ? layout.ContinuationTitle : layout.Title;
        Lines(canvas, title, bounds.Left, bounds.Top, 10, true);
        float titleHeight = title.Length * LineHeight;
        Line(canvas, bounds.Left, bounds.Top + titleHeight + 2, bounds.Right, bounds.Top + titleHeight + 2, Rule, .5f);
        for (int i = 0; i < table.Headers.Count; i++)
        {
            var column = layout.Columns[i];
            bool leftAligned = table.IsLeftAligned(i);
            Lines(canvas, column.Header, bounds.Left + (leftAligned ? column.Left : column.Right),
                bounds.Top + titleHeight + 6, TableHeaderSize,
                i == 0, leftAligned ? SKTextAlign.Left : SKTextAlign.Right, Secondary);
        }
        if (table.Headers.Count > 0)
            Line(canvas, bounds.Left, bounds.Top + fragment.HeadingHeight - 3,
                bounds.Right, bounds.Top + fragment.HeadingHeight - 3, Rule, .4f);
        float y = bounds.Top + fragment.HeadingHeight + 2;
        foreach (var row in fragment.Rows)
        {
            for (int i = 0; i < row.Cells.Count; i++)
            {
                var column = layout.Columns[i];
                bool leftAligned = table.IsLeftAligned(i);
                var lines = row.Cells[i].Skip(row.FirstLine).Take(row.LineCount).ToArray();
                float size = column.ValueSize;
                if (table.StrongFirstLine)
                {
                    // The heading line of a row is strong; its description is not.
                    for (int line = 0; line < lines.Length; line++)
                        Text(canvas, lines[line], bounds.Left + column.Left, y + line * LineHeight, size, strong: row.FirstLine + line == 0);
                }
                else
                    Lines(canvas, lines, bounds.Left + (leftAligned ? column.Left : column.Right),
                        y, size, i == 0, leftAligned ? SKTextAlign.Left : SKTextAlign.Right);
            }
            y += Math.Max(RowHeight, row.LineCount * LineHeight + 3);
        }
    }

    private sealed record SummaryCell(ReportIndicator Indicator, float Left, float Right,
        float PositionLeft, TechnicalText[] Label);

    private const float SummaryPositionGap = 18;

    private IEnumerable<SummaryCell[]> SummaryRows()
    {
        float width = PageWidth - 2 * Margin;
        float leftWidth = (width - Gutter) * .55f;
        int rows = (Data.Indicators.Count + 1) / 2;
        var positionInsets = Data.Indicators.Chunk(Math.Max(1, rows)).Select(column =>
            column.Where(indicator => indicator.Position is not null)
                .Select(indicator => Width(indicator.Value, 9, true) + SummaryPositionGap + Width(indicator.Position!, 7.5f))
                .DefaultIfEmpty().Max()).ToArray();
        for (int row = 0; row < rows; row++)
        {
            var cells = new List<SummaryCell>();
            for (int column = 0; column < 2; column++)
            {
                int index = column * rows + row;
                if (index >= Data.Indicators.Count) continue;
                var indicator = Data.Indicators[index];
                float left = column == 0 ? Margin : Margin + leftWidth + Gutter;
                float right = column == 0 ? Margin + leftWidth : PageWidth - Margin;
                float valueLeft = right - Width(indicator.Value, 9, true);
                float positionLeft = right - positionInsets[column];
                float labelRight = indicator.Position is null ? valueLeft : positionLeft;
                var label = Wrap(indicator.Label, labelRight - left - 8, 8.5f);
                cells.Add(new(indicator, left, right, positionLeft, label));
            }
            yield return cells.ToArray();
        }
    }

    private void DrawSummary(SKCanvas canvas, float top)
    {
        float width = PageWidth - 2 * Margin;
        Heading(canvas, Data.Labels.Summary, Margin, top, width);
        float y = top + 19;
        foreach (var row in SummaryRows())
        {
            foreach (var cell in row)
            {
                Lines(canvas, cell.Label, cell.Left, y);
                Text(canvas, cell.Indicator.Value, cell.Right, y, 9, true, SKTextAlign.Right);
                if (cell.Indicator.Position is { } position)
                    Text(canvas, position, cell.PositionLeft, y, 7.5f, color: Secondary);
            }
            y += row.Max(cell => cell.Label.Length * LineHeight + 4);
        }
    }

    private void DrawNotes(SKCanvas canvas, ReportBlock block)
    {
        // Embedded notes stand in the columns beside the sketch of the last segment.
        float x = block.Embedded ? Margin + SketchWidth + DetailGap : Margin;
        float width = block.Embedded ? EmbeddedNotesWidth : PageWidth - 2 * Margin;
        Heading(canvas, Data.Labels.Notes, x, block.Top, width);
        float y = block.Top + 20;
        foreach (string assumption in Data.Labels.Assumptions)
        {
            var lines = Wrap(assumption, width, 8);
            Lines(canvas, lines, x, y, 8);
            y += lines.Length * LineHeight;
        }
    }

    private void DrawSegment(SKCanvas canvas, ReportBlock block)
    {
        var segment = Data.Segments[block.Index];
        var detail = details[block.Index];
        Heading(canvas, segment.Title, Margin, block.Top, PageWidth - 2 * Margin);
        float top = block.Top + HeadingHeight;
        graphics.Draw(canvas, ReportGraphic.Section, block.Index, new(Margin, top, Margin + SketchWidth, top + SketchHeight));
        float x = Margin + SketchWidth + DetailGap;
        Text(canvas, Data.Labels.Section, x, top, 7.5f, color: Secondary);
        float y = top + LineHeight;
        Lines(canvas, detail.SectionLines, x, y); y += detail.SectionLines.Length * LineHeight + 2;
        Lines(canvas, detail.SectionProperties, x, y);
        x += DetailMiddleWidth + DetailGap;
        Text(canvas, Data.Labels.Material, x, top, 7.5f, color: Secondary);
        y = top + LineHeight;
        Lines(canvas, detail.MaterialLines, x, y, strong: true); y += detail.MaterialLines.Length * LineHeight + 2;
        Lines(canvas, detail.MaterialProperties, x, y);
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

    private string[] Wrap(string text, float width, float size, bool strong = false, IReadOnlySet<int>? protectedSpaces = null) =>
        Wrap((TechnicalText)text, width, size, strong, protectedSpaces).Select(line => line.PlainText).ToArray();

    private TechnicalText[] Wrap(TechnicalText text, float width, float size, bool strong = false,
        IReadOnlySet<int>? protectedSpaces = null, bool keepNonbreakingGroup = true)
    {
        var lines = new List<TechnicalText>();
        int offset = 0;
        foreach (string paragraph in text.PlainText.Split('\n'))
        {
            int length = paragraph.TrimEnd('\r').Length;
            var pending = text.Slice(offset, length);
            int pendingOffset = offset;
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
                while (space > 0 && protectedSpaces?.Contains(pendingOffset + space) == true)
                    space = pending.PlainText.LastIndexOf(' ', space - 1);
                if (space > 0) count = space;
                else if (keepNonbreakingGroup)
                {
                    // Emergency word wrapping must still honor nonbreaking numeric groups.
                    int end = pending.PlainText.IndexOf(' ');
                    if (end < 0) end = pending.PlainText.Length;
                    if (pending.PlainText.AsSpan(0, end).ContainsAny('\u202f', '\u00a0')) count = end;
                }
                lines.Add(pending.Slice(0, count));
                int next = count;
                while (next < pending.PlainText.Length && pending.PlainText[next] == ' ') next++;
                pending = pending.Slice(next, pending.PlainText.Length - next);
                pendingOffset += next;
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
