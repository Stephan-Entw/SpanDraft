using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Controls;

/// <summary>Adds technical runs to ordinary TextBlocks without changing their styles or layout.</summary>
public sealed class TechnicalTextPresenter : AvaloniaObject
{
    public static readonly AttachedProperty<TechnicalText?> ContentProperty =
        AvaloniaProperty.RegisterAttached<TechnicalTextPresenter, TextBlock, TechnicalText?>("Content");
    public static TechnicalText? GetContent(TextBlock text) => text.GetValue(ContentProperty);
    public static void SetContent(TextBlock text, TechnicalText? value) => text.SetValue(ContentProperty, value);
    static TechnicalTextPresenter()
    {
        ContentProperty.Changed.AddClassHandler<TextBlock>((text, _) =>
        {
            if (GetContent(text) is null)
                text.Inlines?.Clear();
            else Update(text);
        });
        TextBlock.FontSizeProperty.Changed.AddClassHandler<TextBlock>((text, _) => Update(text));
    }
    private static void Update(TextBlock text)
    {
        if (GetContent(text) is not { } content) return;
        text.Inlines!.Clear();
        foreach (var run in content.Runs)
            text.Inlines.Add(new Run(run.Text)
            {
                FontSize = text.FontSize * TechnicalTypography.FontScale(run),
                FontStyle = FontStyleOf(run), BaselineAlignment = BaselineOf(run)
            });
    }
    internal static FontStyle FontStyleOf(TechnicalRun run) =>
        TechnicalTypography.IsItalic(run) ? FontStyle.Italic : FontStyle.Normal;
    internal static BaselineAlignment BaselineOf(TechnicalRun run) => run.Position switch
    {
        TechnicalPosition.Subscript => BaselineAlignment.Subscript,
        TechnicalPosition.Superscript => BaselineAlignment.Superscript,
        _ => BaselineAlignment.Baseline
    };
    internal static TextRunProperties Properties(TechnicalRun run, Typeface typeface, double size, IBrush? brush) =>
        new GenericTextRunProperties(new Typeface(typeface.FontFamily, FontStyleOf(run), typeface.Weight, typeface.Stretch),
            size * TechnicalTypography.FontScale(run), foregroundBrush: brush, baselineAlignment: BaselineOf(run));
}
