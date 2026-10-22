using System.Globalization;
using System.Text.RegularExpressions;

namespace SpanDraft.Desktop;

/// <summary>Windows-safe picker suggestions; never changes project content or file identity.</summary>
internal static class ProjectSaveFileName
{
    private const string Extension = ".spandraft";
    private const int MaxStemLength = 120;

    public static string Suggest(string? currentFilePath, string? title, string fallback)
    {
        if (currentFilePath is not null) return Path.GetFileName(currentFilePath);
        string stem = (title ?? "").Trim().TrimEnd(' ', '.');
        if (stem.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) stem = stem[..^Extension.Length];
        // Explicit Windows characters: development and rendering tests also run on macOS/Linux.
        stem = Regex.Replace(stem, @"[\s-]*[<>:""/\\|?*\x00-\x1F][\s<>:""/\\|?*\x00-\x1F-]*", " - ");
        stem = Regex.Replace(stem, @"\s+", " ").Trim().TrimEnd(' ', '.');
        if (stem.Length > MaxStemLength)
        {
            int boundary = Array.FindLast(StringInfo.ParseCombiningCharacters(stem), i => i <= MaxStemLength);
            stem = stem[..boundary].TrimEnd(' ', '.');
        }
        if (stem.Trim(' ', '-', '.').Length == 0) stem = fallback;
        string deviceName = stem.Split('.')[0].TrimEnd(' ');
        if (Regex.IsMatch(deviceName, @"\A(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (stem.Length == MaxStemLength)
                stem = stem[..Array.FindLast(StringInfo.ParseCombiningCharacters(stem), i => i < MaxStemLength)].TrimEnd(' ', '.');
            stem = "_" + stem;
        }
        return stem + Extension;
    }
}
