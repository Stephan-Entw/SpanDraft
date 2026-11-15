using System.Globalization;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

/// <summary>
/// The names of a segment wherever the editor shows it: the segment row under the beam and the blocks of the beam model.
/// One source, so that the row and the side bar can never describe the same segment differently.
/// </summary>
public static class SegmentLabels
{
    /// <summary>Joins the section and the material, which belongs to the segment's definition.</summary>
    public const string Separator = " · ";

    /// <summary>The unique heading of a segment, "Abschnitt 1".</summary>
    public static string Title(int number) => string.Format(CultureInfo.CurrentUICulture, Strings.SegmentSelected, number);

    /// <summary>The section's name; an explicit display name wins over the generated one.</summary>
    public static string SectionName(EditorSegment segment, ResultPresentationOptions? options = null) =>
        segment.SectionDisplayName ?? SectionDisplay.Name(segment.Section, options);

    /// <summary>The section and material in one text, complete: it is never shortened.</summary>
    public static string Description(string section, string material) => section + Separator + material;

    public static string Description(EditorSegment segment, ResultPresentationOptions? options = null) =>
        Description(SectionName(segment, options), MaterialDisplay.Format(segment.Material));

    /// <summary>The candidates of a segment row from the most to the least detailed.</summary>
    public static IReadOnlyList<string> RowCandidates(EditorSegment segment, int number, ResultPresentationOptions? options = null) =>
        [Description(segment, options), Title(number), number.ToString(CultureInfo.CurrentUICulture)];
}
