namespace SpanDraft.Reporting.Notation;

public enum TechnicalPosition { Normal, Subscript, Superscript }
public enum TechnicalRole { Text, Variable }
public sealed record TechnicalRun(string Text, TechnicalPosition Position = TechnicalPosition.Normal,
    TechnicalRole Role = TechnicalRole.Text);

/// <summary>Presentation-only runs, independent of fonts, UI frameworks and the domain model.</summary>
public sealed class TechnicalText
{
    public TechnicalText(params TechnicalRun[] runs)
    {
        Runs = Array.AsReadOnly(runs.Where(r => r.Text.Length > 0).ToArray());
        PlainText = string.Concat(Runs.Select(r => r.Text));
    }

    public IReadOnlyList<TechnicalRun> Runs { get; }
    public string PlainText { get; }
    public override string ToString() => PlainText;
    public static implicit operator TechnicalText(string text) => new(new TechnicalRun(text));
    public static TechnicalText operator +(TechnicalText left, TechnicalText right) => new([.. left.Runs, .. right.Runs]);
    public static TechnicalText Join(string separator, IEnumerable<TechnicalText> texts) =>
        new(texts.SelectMany((text, index) => index == 0 ? text.Runs : ((TechnicalText)separator + text).Runs).ToArray());

    public TechnicalText Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > PlainText.Length - length) throw new ArgumentOutOfRangeException(nameof(length));
        var runs = new List<TechnicalRun>();
        int offset = 0;
        foreach (var run in Runs)
        {
            int first = Math.Max(start, offset), end = Math.Min(start + length, offset + run.Text.Length);
            if (end > first) runs.Add(run with { Text = run.Text.Substring(first - offset, end - first) });
            offset += run.Text.Length;
        }
        return new(runs.ToArray());
    }
}

public static class TechnicalTypography
{
    public const bool ItalicVariables = true;
    public const double ScriptScale = .7;
    public static bool IsItalic(TechnicalRun run, bool italicVariables = ItalicVariables) =>
        italicVariables && run.Role == TechnicalRole.Variable;
    public static double FontScale(TechnicalRun run) => run.Position == TechnicalPosition.Normal ? 1 : ScriptScale;
}
