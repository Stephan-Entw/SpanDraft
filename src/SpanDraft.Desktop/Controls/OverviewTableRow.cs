using Avalonia;
using Avalonia.Controls;

namespace SpanDraft.Desktop.Controls;

/// <summary>
/// The columns of one overview table. Each data row reports the natural width of its cells; every column is as wide
/// as its widest data line, and the rest of the table width is spread evenly over the gaps between the columns.
/// Headings never widen a column: they reach into the neighbouring gap instead. Tables are sized independently.
/// </summary>
public sealed class OverviewTableScope : AvaloniaObject
{
    public const double MinGap = 12;
    public static readonly StyledProperty<string> AlignmentsProperty = AvaloniaProperty.Register<OverviewTableScope, string>(nameof(Alignments), "LLR");
    public static readonly StyledProperty<int> WrapColumnProperty = AvaloniaProperty.Register<OverviewTableScope, int>(nameof(WrapColumn), -1);

    private readonly HashSet<OverviewTableRow> _rows = [];
    private readonly Dictionary<OverviewTableRow, (double[] Natural, double Wrapped)> _data = [];

    /// <summary>"L" or "R" per column: text starts at the left column edge, numbers end at the right one.</summary>
    public string Alignments { get => GetValue(AlignmentsProperty); set => SetValue(AlignmentsProperty, value); }

    /// <summary>The value column that may wrap after an ellipsis when the table is too narrow; -1 for none.</summary>
    public int WrapColumn { get => GetValue(WrapColumnProperty); set => SetValue(WrapColumnProperty, value); }

    internal int Count => Alignments.Length;
    internal double[] Widths { get; private set; } = [];
    internal double[] Lefts { get; private set; } = [];

    /// <summary>The widest the wrapping column may become while every gap keeps its minimum.</summary>
    internal double WrapLimit { get; private set; } = double.PositiveInfinity;

    internal void Attach(OverviewTableRow row) => _rows.Add(row);

    internal void Detach(OverviewTableRow row)
    {
        _rows.Remove(row);
        if (_data.Remove(row)) InvalidateRows(null);
    }

    /// <summary>Records a data row's cell widths for the table width and re-lays out the other rows if the columns change.</summary>
    internal void Report(OverviewTableRow row, double[] natural, double wrapped, double width)
    {
        _data[row] = (natural, wrapped);
        int n = Count;
        var widths = new double[n];
        foreach (var (cells, _) in _data.Values)
            for (int c = 0; c < n && c < cells.Length; c++) widths[c] = Math.Max(widths[c], cells[c]);
        int wrap = WrapColumn;
        if (wrap >= 0 && wrap < n)
        {
            // Rule of the wrapping value: it only wraps where the full line would squeeze a gap below its minimum,
            // and then the column is as wide as its longest wrapped line.
            WrapLimit = Math.Max(0, width - (widths.Sum() - widths[wrap]) - MinGap * (n - 1));
            if (widths[wrap] > WrapLimit)
                widths[wrap] = Math.Min(WrapLimit, _data.Values.Max(d => d.Wrapped));
        }
        double gap = n > 1 ? Math.Max(MinGap, (width - widths.Sum()) / (n - 1)) : 0;
        var lefts = new double[n];
        for (int c = 1; c < n; c++) lefts[c] = lefts[c - 1] + widths[c - 1] + gap;
        if (widths.SequenceEqual(Widths) && lefts.SequenceEqual(Lefts)) return;
        Widths = widths; Lefts = lefts;
        InvalidateRows(row);
    }

    private void InvalidateRows(OverviewTableRow? except)
    {
        foreach (var other in _rows.Where(r => r != except)) other.InvalidateMeasure();
    }
}

/// <summary>
/// One row of an overview table, laid out by its <see cref="OverviewTableScope"/>: cells on <see cref="ColumnProperty"/>
/// in the first line, detail cells (<see cref="RowProperty"/> 1) below their column.
/// </summary>
public sealed class OverviewTableRow : Panel
{
    /// <summary>
    /// A name wider than this is shortened with an ellipsis: 72 DIP hold about ten bold characters, enough for
    /// generated and short user names, while the type or value keeps most of the panel.
    /// </summary>
    public const double NameCap = 72;

    public static readonly AttachedProperty<OverviewTableScope?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<OverviewTableRow, Control, OverviewTableScope?>("Scope", inherits: true);
    public static readonly AttachedProperty<int> ColumnProperty = AvaloniaProperty.RegisterAttached<OverviewTableRow, Control, int>("Column");
    public static readonly AttachedProperty<int> RowProperty = AvaloniaProperty.RegisterAttached<OverviewTableRow, Control, int>("Row");
    public static readonly StyledProperty<bool> IsHeaderProperty = AvaloniaProperty.Register<OverviewTableRow, bool>(nameof(IsHeader));

    public static OverviewTableScope? GetScope(Control control) => control.GetValue(ScopeProperty);
    public static void SetScope(Control control, OverviewTableScope? value) => control.SetValue(ScopeProperty, value);
    public static int GetColumn(Control control) => control.GetValue(ColumnProperty);
    public static void SetColumn(Control control, int value) => control.SetValue(ColumnProperty, value);
    public static int GetRow(Control control) => control.GetValue(RowProperty);
    public static void SetRow(Control control, int value) => control.SetValue(RowProperty, value);

    /// <summary>A heading only follows the columns; it never widens them.</summary>
    public bool IsHeader { get => GetValue(IsHeaderProperty); set => SetValue(IsHeaderProperty, value); }

    private OverviewTableScope? _scope;
    private double _lineHeight;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scope?.Detach(this);
        _scope = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var scope = GetScope(this) ?? throw new InvalidOperationException("An overview table row needs a table scope.");
        if (!ReferenceEquals(scope, _scope)) { _scope?.Detach(this); _scope = scope; scope.Attach(this); }
        int n = scope.Count;
        var cells = Children.Where(c => c.IsVisible).ToArray();
        // An unconstrained measure (never the case inside the overview) keeps the last columns.
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : scope.Widths.Sum() + OverviewTableScope.MinGap * (n - 1);
        if (!IsHeader && double.IsFinite(availableSize.Width))
        {
            var natural = new double[n];
            foreach (var cell in cells)
            {
                cell.Measure(Size.Infinity);
                int column = Column(cell, n);
                double w = column == 0 ? Math.Min(cell.DesiredSize.Width, NameCap) : cell.DesiredSize.Width;
                natural[column] = Math.Max(natural[column], w);
            }
            // The wrapped width depends on the limit, which depends on the other columns; two rounds settle it.
            for (int round = 0; round < 2; round++)
            {
                double limit = scope.WrapLimit, wrapped = 0;
                foreach (var cell in cells.Where(c => Column(c, n) == scope.WrapColumn))
                {
                    cell.Measure(new Size(limit, double.PositiveInfinity));
                    wrapped = Math.Max(wrapped, cell.DesiredSize.Width);
                }
                scope.Report(this, natural, wrapped, width);
                if (scope.WrapLimit.Equals(limit)) break;
            }
        }
        double first = 0, details = 0;
        foreach (var cell in cells)
        {
            int column = Column(cell, n);
            // Cells measure at their column width, so only a value of the wrapping column wraps; headings keep one line.
            double cellWidth = IsHeader || scope.Widths.Length != n ? double.PositiveInfinity : scope.Widths[column];
            cell.Measure(new Size(cellWidth, double.PositiveInfinity));
            if (GetRow(cell) == 0) first = Math.Max(first, cell.DesiredSize.Height);
            else details = Math.Max(details, cell.DesiredSize.Height);
        }
        _lineHeight = Math.Max(first, MinHeight);
        return new Size(width, _lineHeight + details);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_scope is not { } scope || scope.Widths.Length != scope.Count) return finalSize;
        foreach (var cell in Children.Where(c => c.IsVisible))
        {
            int column = Column(cell, scope.Count);
            double columnWidth = scope.Widths[column];
            // A heading wider than its column reaches into the gap: to the right in a text column, to the left in a number column.
            double w = IsHeader ? Math.Max(columnWidth, cell.DesiredSize.Width) : columnWidth;
            double x = scope.Alignments[column] == 'R' ? scope.Lefts[column] + columnWidth - w : scope.Lefts[column];
            cell.Arrange(GetRow(cell) == 0
                ? new Rect(x, 0, w, _lineHeight)
                : new Rect(x, _lineHeight, w, cell.DesiredSize.Height));
        }
        return finalSize;
    }

    private static int Column(Control cell, int count) => Math.Clamp(GetColumn(cell), 0, count - 1);
}
