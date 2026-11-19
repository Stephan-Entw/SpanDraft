using System.Globalization;
using System.Text.RegularExpressions;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Reporting;

internal sealed partial class ReportDocument
{
    private const float ValueSize = 8.5f, MinimumValueSize = 7, TableHeaderSize = 7.5f, MinimumColumnGap = 4;
    private const float LayoutTolerance = .001f;

    private readonly record struct TableToken(int Start, int End, bool KeepTogether);
    private sealed record TableParagraph(TechnicalText Text, TableToken[] Tokens);

    private ReportTableLayout LayoutTable(ReportTable table)
    {
        int count = table.Rows[0].Count;
        float contentBudget = RightWidth - MinimumColumnGap * (count - 1);
        var headers = table.Headers.Select(h => TableParagraphs(h, ReportCellWrap.Text, header: true)).ToArray();
        var rows = table.Rows.Select(row => row.Select((text, column) =>
            TableParagraphs(text, table.CellWrap?[column] ?? ReportCellWrap.Text, section: table.StrongFirstLine)).ToArray()).ToArray();
        var glyphWidths = new Dictionary<(string Text, float Size, bool Strong), float>();
        var headerWidths = Enumerable.Range(0, count).Select(i => i < headers.Length
            ? MinimumWidth(headers[i], TableHeaderSize, i == 0) : 0).ToArray();
        var minima = Enumerable.Range(0, count).Select(i => ColumnMinimum(i, ValueSize)).ToArray();
        var widths = (float[])minima.Clone();
        var sizes = Enumerable.Repeat(ValueSize, count).ToArray();
        if (minima.Sum() > contentBudget)
        {
            widths = Enumerable.Range(0, count).Select(i => ColumnMinimum(i, MinimumValueSize)).ToArray();
            float sizeFloor = MinimumValueSize;
            if (widths.Sum() > contentBudget)
            {
                // Keep unusually long values intact within the column, even below the usual size floor.
                widths = (float[])headerWidths.Clone();
                sizeFloor = 0;
            }
            float available = contentBudget - widths.Sum();
            if (available < 0)
                throw new InvalidOperationException($"The report table '{table.Title}' exceeds the available width.");

            float needed = minima.Sum() - widths.Sum();
            for (int i = 0; i < count; i++)
            {
                if (minima[i] <= widths[i]) continue;
                widths[i] += available * (minima[i] - widths[i]) / needed;
                float low = sizeFloor, high = ValueSize;
                for (int step = 0; step < 12; step++)
                {
                    float middle = (low + high) / 2;
                    if (ColumnMinimum(i, middle) <= widths[i]) low = middle;
                    else high = middle;
                }
                sizes[i] = low;
            }
        }
        else if (count > 1)
        {
            var preferred = widths.Select((minimum, i) =>
            {
                float natural = rows.Max(row => row[i].Select((p, line) =>
                    Width(p.Text, ValueSize, i == 0 && (!table.StrongFirstLine || line == 0))).Max());
                // Bound the influence of user-defined names in multi-column tables.
                if (i == 0 && count > 1) natural = Math.Min(natural, RightWidth / 3);
                return Math.Max(natural, minimum);
            }).ToArray();

            float growth = preferred.Sum() - minima.Sum();
            float available = contentBudget - minima.Sum();
            for (int i = 0; i < count; i++)
                widths[i] = growth <= available ? preferred[i] : minima[i] + (preferred[i] - minima[i]) * (available / growth);
        }

        if (count == 1) widths[0] = RightWidth;
        var wrappedRows = rows.Select(row => row.Select((paragraphs, i) => WrapCell(paragraphs,
            widths[i], sizes[i], i == 0, table.StrongFirstLine).Select(line => line.PlainText).ToArray()).ToArray()).ToArray();
        if (count > 1)
            // After wrapping, only the width of actual lines stays in each column.
            for (int i = 0; i < count; i++)
            {
                int column = i;
                widths[i] = Math.Max(headerWidths[i], wrappedRows.Max(row => row[column].Select((line, index) =>
                    Width(line, sizes[column], column == 0 && (!table.StrongFirstLine || index == 0))).Max()));
            }

        float gap = count == 1 ? 0 : (RightWidth - widths.Sum()) / (count - 1);
        var naturalHeaders = headers.Select((paragraphs, i) =>
            paragraphs.Max(p => Width(p.Text, TableHeaderSize, i == 0))).ToArray();
        float left = 0;
        var columns = new ReportTableColumn[count];
        for (int i = 0; i < count; i++)
        {
            float headerRoom = widths[i];
            bool leftAligned = table.IsLeftAligned(i);
            int neighbour = i + (leftAligned ? 1 : -1);
            if (neighbour >= 0 && neighbour < headers.Length)
            {
                // Opposing headings share the spare space, up to what each heading needs.
                float spare = Math.Max(0, gap - MinimumColumnGap);
                if (table.IsLeftAligned(neighbour) != leftAligned)
                    spare -= Math.Clamp(naturalHeaders[neighbour] - widths[neighbour], 0, spare / 2);
                headerRoom += spare;
            }
            columns[i] = new(left, widths[i], sizes[i], i < headers.Length
                ? WrapCell(headers[i], headerRoom, TableHeaderSize, i == 0).ToArray() : []);
            left += widths[i] + gap;
        }
        var title = Wrap(table.Title, RightWidth, 10, true);
        var continuationTitle = Wrap(table.Title + " · " + Data.Labels.Continuation, RightWidth, 10, true);
        float headerHeight = columns.Max(c => c.Header.Length) * LineHeight + 8;
        return new(columns, wrappedRows, wrappedRows.Select(r => r.Max(c => c.Length)).ToArray(),
            title, continuationTitle, title.Length * LineHeight + headerHeight, continuationTitle.Length * LineHeight + headerHeight);

        float ColumnMinimum(int column, float size) => Math.Max(headerWidths[column],
            rows.Select(row => MinimumWidth(row[column], size, column == 0, table.StrongFirstLine)).DefaultIfEmpty().Max());

        float MinimumWidth(TableParagraph[] paragraphs, float size, bool strong, bool strongFirstLine = false)
        {
            float minimum = 0;
            for (int i = 0; i < paragraphs.Length; i++)
            {
                var paragraph = paragraphs[i];
                // The first visual line of the row can be strong even when its first paragraph wraps.
                bool bold = strong && (!strongFirstLine || i == 0);
                foreach (var token in paragraph.Tokens)
                {
                    var part = paragraph.Text.Slice(token.Start, token.End - token.Start);
                    if (token.KeepTogether)
                    {
                        minimum = Math.Max(minimum, Width(part, size, bold));
                        continue;
                    }

                    var elements = StringInfo.GetTextElementEnumerator(part.PlainText);
                    while (elements.MoveNext())
                    {
                        string element = elements.GetTextElement();
                        var key = (element, size, bold);
                        if (!glyphWidths.TryGetValue(key, out float measured))
                            glyphWidths[key] = measured = Width(element, size, bold);
                        minimum = Math.Max(minimum, measured);
                    }
                }
            }
            return minimum;
        }
    }

    private static TableParagraph[] TableParagraphs(TechnicalText text, ReportCellWrap kind, bool header = false, bool section = false)
    {
        int offset = 0;
        var paragraphs = new List<TableParagraph>();
        foreach (string line in text.PlainText.Split('\n'))
        {
            string value = line.TrimEnd('\r');
            var paragraph = text.Slice(offset, value.Length);
            offset += line.Length + 1;
            var protectedSpaces = section ? ProtectedSpaces(value) : [];
            int equation = kind == ReportCellWrap.SupportDescription ? value.IndexOf(" = ", StringComparison.Ordinal) : -1;
            if (equation >= 0)
            {
                protectedSpaces.Add(equation);
                for (int i = equation + 3; i < value.Length; i++)
                    if (value[i] == ' ') protectedSpaces.Add(i);
            }
            if (header)
                foreach (Match unit in Regex.Matches(value, @"\[[^\]]+\]"))
                    for (int i = unit.Index; i < unit.Index + unit.Length; i++)
                        if (value[i] == ' ') protectedSpaces.Add(i);

            var tokens = new List<TableToken>();
            int start = 0;
            bool isQuantity = kind is ReportCellWrap.Quantity or ReportCellWrap.QuantityRange or ReportCellWrap.ShearPair;
            // A short identifier pair stays together. Longer user-defined names wrap as free text.
            bool sharedName = kind == ReportCellWrap.SharedSupportName && Regex.IsMatch(value, @"\A[^\s+]{1,4} \+ [^\s+]{1,4}\z");
            for (int i = 0; i < value.Length; i++)
            {
                bool afterRangeSeparator = kind == ReportCellWrap.QuantityRange && value[i] == '…';
                bool afterShearSeparator = kind == ReportCellWrap.ShearPair && value[i] == '/' && i > 0 && i + 1 < value.Length
                    && value[i - 1] == ' ' && value[i + 1] == ' ';
                if (afterRangeSeparator || afterShearSeparator)
                    AddToken(i + 1);
                else if (!isQuantity && !sharedName && value[i] == ' ' && !protectedSpaces.Contains(i))
                    AddToken(i);
            }
            AddToken(value.Length);
            paragraphs.Add(new(paragraph, tokens.ToArray()));

            void AddToken(int end)
            {
                int next = end;
                while (start < end && value[start] == ' ') start++;
                while (start < end && value[end - 1] == ' ') end--;
                if (start < end)
                {
                    var part = value.AsSpan(start, end - start);
                    tokens.Add(new(start, end, header || isQuantity || sharedName || kind == ReportCellWrap.SupportDescription
                        || part.Contains(' ') || (section && part.ContainsAny('\u00a0', '\u202f'))));
                }
                start = next;
            }
        }
        return paragraphs.ToArray();
    }

    private IEnumerable<TechnicalText> WrapCell(TableParagraph[] paragraphs, float width, float size,
        bool strong, bool strongFirstLine = false)
    {
        bool first = true;
        foreach (var (text, tokens) in paragraphs)
        {
            if (tokens.Length == 0)
            {
                yield return text;
                first = false;
                continue;
            }

            int start = tokens[0].Start, end = start;
            foreach (var token in tokens)
            {
                if (end == start) start = end = token.Start;
                bool bold = strong && (!strongFirstLine || first);
                if (end > start && Width(text.Slice(start, token.End - start), size, bold) > width + LayoutTolerance)
                {
                    yield return text.Slice(start, end - start);
                    first = false;
                    bold = strong && !strongFirstLine;
                    start = end = token.Start;
                }
                if (end == start && !token.KeepTogether)
                {
                    var part = text.Slice(start, token.End - start);
                    if (Width(part, size, bold) > width + LayoutTolerance)
                    {
                        foreach (var line in Wrap(part, width, size, bold, keepNonbreakingGroup: false))
                            yield return line;
                        first = false;
                        start = end = token.End;
                        continue;
                    }
                }
                end = token.End;
            }
            if (end > start)
            {
                yield return text.Slice(start, end - start);
                first = false;
            }
        }
    }
}
