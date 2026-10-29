using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Solver;

namespace SpanDraft.Desktop.ViewModels;

public sealed record ResultProbeRow(string Label, string Value);

public sealed partial class EditorViewModel
{
    private bool _isResultProbeTool, _isResultProbeFixed, _probeAtStation;
    private Length? _resultProbePosition;
    private IReadOnlyList<ResultProbeRow> _resultProbeRows = [];

    public ActionCommand ResultProbeToolCommand { get; }
    public bool CanResultProbe => Presentation.IsSuccess && !IsBusy;
    public bool IsResultProbeTool => _isResultProbeTool;
    public bool IsResultProbeFixed => _isResultProbeFixed;
    public Length? ResultProbePosition => _resultProbePosition;
    public IReadOnlyList<ResultProbeRow> ResultProbeRows => _resultProbeRows;

    public void ToggleResultProbeTool()
    {
        if (!CanResultProbe) return;
        bool deactivate = IsResultProbeTool;
        CancelEditorInteraction();
        if (deactivate) return;
        _isResultProbeTool = true;
        Notify(nameof(IsResultProbeTool));
        Notify(nameof(IsEditorNeutral));
    }

    public void HoverResultProbe(Length? position, bool atStation = false)
    {
        if (!IsResultProbeTool || IsResultProbeFixed || !CanResultProbe) return;
        SetResultProbe(position, atStation);
    }

    public void ToggleResultProbeFixed(Length position, bool atStation = false)
    {
        if (!IsResultProbeTool || !CanResultProbe || position.Meters > Document.Length.Meters) return;
        SetResultProbe(position, atStation);
        _isResultProbeFixed = !_isResultProbeFixed;
        Notify(nameof(IsResultProbeFixed));
    }

    public void CancelResultProbe()
    {
        if (!IsResultProbeTool) return;
        _isResultProbeTool = _isResultProbeFixed = _probeAtStation = false;
        _resultProbePosition = null;
        RefreshResultProbe();
        Notify(nameof(ResultProbePosition));
        Notify(nameof(IsResultProbeFixed));
        Notify(nameof(IsResultProbeTool));
        Notify(nameof(IsEditorNeutral));
    }

    private void SetResultProbe(Length? position, bool atStation)
    {
        if (position is { } p && p.Meters > Document.Length.Meters) position = null;
        if (_resultProbePosition == position && _probeAtStation == atStation) return;
        _resultProbePosition = position;
        _probeAtStation = atStation;
        RefreshResultProbe();
        Notify(nameof(ResultProbePosition));
    }

    private void RefreshResultProbe()
    {
        var rows = new List<ResultProbeRow>();
        if (ResultProbePosition is { } position && Presentation.Result is { } result)
        {
            var unit = ResultPresentation.Profile[QuantityKind.BeamLength];
            rows.Add(new("x", QuantityFormatter.WithUnit(InputQuantityFormatter.FormatExact(position.Meters, unit), unit)));
            var solution = result.Solution;
            bool end = position == Document.Length;
            var primary = solution.EvaluateAt(position, end ? EvaluationSide.Left : EvaluationSide.Right);
            var left = _probeAtStation && position.Meters > 0 && !end
                ? solution.EvaluateAt(position, EvaluationSide.Left) : primary;
            Add("w", QuantityKind.TransverseDisplacement, left.TransverseDisplacement.Meters, primary.TransverseDisplacement.Meters);
            Add("θ", QuantityKind.Rotation, left.RotationRadians, primary.RotationRadians);
            Add("V", QuantityKind.TransverseForce, left.ShearForce.Newtons, primary.ShearForce.Newtons);
            Add("M", QuantityKind.Moment, left.BendingMoment.NewtonMeters, primary.BendingMoment.NewtonMeters);
        }
        _resultProbeRows = rows;
        Notify(nameof(ResultProbeRows));

        void Add(string label, QuantityKind kind, double left, double right)
        {
            string a = QuantityFormatter.Format(left, kind, ResultPresentation);
            string b = QuantityFormatter.Format(right, kind, ResultPresentation);
            if (a == b) rows.Add(new(label, b));
            else { rows.Add(new(label + "−", a)); rows.Add(new(label + "+", b)); }
        }
    }

    private void RefreshResultProbeAvailability()
    {
        Notify(nameof(CanResultProbe));
        ResultProbeToolCommand.Refresh();
    }
}
