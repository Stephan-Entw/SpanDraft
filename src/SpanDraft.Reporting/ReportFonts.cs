using SkiaSharp;

namespace SpanDraft.Reporting;

internal static class ReportFonts
{
    private static readonly Lazy<SKTypeface>[] Faces = Enumerable.Range(0, 4)
        .Select(index => new Lazy<SKTypeface>(() => Load((index & 2) != 0, (index & 1) != 0))).ToArray();
    internal static SKTypeface Face(bool strong, bool italic) => Faces[(strong ? 2 : 0) + (italic ? 1 : 0)].Value;
    private static SKTypeface Load(bool strong, bool italic)
    {
        string style = strong ? italic ? "SemiBoldItalic" : "SemiBold" : italic ? "Italic" : "Regular";
        using var stream = typeof(ReportFonts).Assembly.GetManifestResourceStream($"SpanDraft.Reporting.Fonts.IBMPlexSans-{style}.ttf")
            ?? throw new InvalidOperationException("Missing bundled report font: " + style);
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("Invalid report font: " + style);
    }
}
