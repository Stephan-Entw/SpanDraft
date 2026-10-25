using SkiaSharp;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Reporting;

internal sealed record ReportGlyphRun(string Text, SKFont Font, float X, float Baseline, SKRect Ink);

/// <summary>The same positioned vector-text runs supply measurement, wrapping and drawing.</summary>
internal sealed class ReportTextLayout : IDisposable
{
    private readonly List<SKFont> fonts = [];
    private readonly List<ReportGlyphRun> runs = [];
    internal IReadOnlyList<ReportGlyphRun> Runs => runs;
    internal float Width { get; private set; }
    internal float Height { get; private set; }

    internal ReportTextLayout(TechnicalText text, float size, bool strong = false)
    {
        try
        {
            var baselineFont = Font(size, strong, false);
            var metrics = baselineFont.Metrics;
            float top = metrics.Ascent, bottom = metrics.Descent, left = 0, right = 0, cursor = 0;
            foreach (var run in text.Runs)
            {
                var font = Font(size * (float)TechnicalTypography.FontScale(run), strong, TechnicalTypography.IsItalic(run));
                var groups = new List<(string Text, SKFont Font)>();
                foreach (var rune in run.Text.EnumerateRunes())
                {
                    string character = rune.ToString();
                    var selected = font;
                    if (!font.ContainsGlyphs(character))
                    {
                        using var face = SKFontManager.Default.MatchCharacter("Arial", font.Typeface.FontStyle, null, rune.Value)
                            ?? throw new InvalidOperationException($"No report font provides {character}.");
                        selected = new SKFont(face, font.Size); fonts.Add(selected);
                        if (!selected.ContainsGlyphs(character)) throw new InvalidOperationException($"Missing report glyph {character}.");
                    }
                    if (groups.Count > 0 && ReferenceEquals(groups[^1].Font, selected))
                        groups[^1] = (groups[^1].Text + character, selected);
                    else groups.Add((character, selected));
                }
                foreach (var group in groups)
                {
                    var m = group.Font.Metrics;
                    float shift = run.Position switch
                    {
                        TechnicalPosition.Subscript => metrics.Descent - m.Descent,
                        TechnicalPosition.Superscript => metrics.Ascent - m.Ascent,
                        _ => 0
                    };
                    float advance = group.Font.MeasureText(group.Text, out var ink);
                    ink.Offset(cursor, shift);
                    runs.Add(new(group.Text, group.Font, cursor, shift, ink));
                    top = Math.Min(top, Math.Min(shift + m.Ascent, ink.Top));
                    bottom = Math.Max(bottom, Math.Max(shift + m.Descent, ink.Bottom));
                    left = Math.Min(left, ink.Left);
                    right = Math.Max(right, ink.Right);
                    cursor += advance;
                }
            }
            Width = Math.Max(cursor, right) - left;
            Height = bottom - top;
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i]; var ink = run.Ink; ink.Offset(-left, -top);
                runs[i] = run with { X = run.X - left, Baseline = run.Baseline - top, Ink = ink };
            }
        }
        catch { Dispose(); throw; }
    }

    private SKFont Font(float size, bool strong, bool italic)
    {
        var style = strong ? italic ? SKFontStyle.BoldItalic : SKFontStyle.Bold
            : italic ? SKFontStyle.Italic : SKFontStyle.Normal;
        using var face = SKTypeface.FromFamilyName("Arial", style);
        var font = new SKFont(face, size); fonts.Add(font); return font;
    }
    internal void Draw(SKCanvas canvas, float x, float y, SKPaint paint)
    {
        foreach (var run in runs)
            canvas.DrawText(run.Text, x + run.X, y + run.Baseline, SKTextAlign.Left, run.Font, paint);
    }
    public void Dispose() { foreach (var font in fonts) font.Dispose(); fonts.Clear(); }
}
