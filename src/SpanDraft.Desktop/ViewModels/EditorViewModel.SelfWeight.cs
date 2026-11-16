using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class EditorViewModel
{
    private bool _selfWeightFlyout;
    private string? _selfWeightFeedback;
    public ActionCommand ToggleSelfWeightCommand { get; }
    public ActionCommand ToggleSelfWeightVisibilityCommand { get; }
    public ActionCommand DisableSelfWeightCommand { get; }
    public bool IsSelfWeightEnabled => Document.SelfWeight?.Enabled == true;
    public bool IsSelfWeightVisible => EditorPresentation.SelfWeightVisible;
    public bool IsSelfWeightFlyoutVisible => _selfWeightFlyout && IsSelfWeightEnabled;
    public string SelfWeightVisibilityAction => IsSelfWeightVisible ? Strings.HideSelfWeight : Strings.ShowSelfWeight;
    // Segments with the same intensity share one label value; otherwise the label lists each intensity in segment order.
    public string SelfWeightLabel => Document.SelfWeightLoads is { Count: > 0 } loads
        ? EditorSelfWeight.Symbol + " = " + string.Join("; ", loads.Select(load => load.StartIntensity.NewtonsPerMeter).Distinct()
            .Select(value => QuantityFormatter.FormatResult(value, QuantityKind.DistributedLoad, ResultPresentation))) : "";
    public TechnicalText SelfWeightValue => SelfWeightLabel is { Length: > 0 } label
        ? new TechnicalText(new TechnicalRun(EditorSelfWeight.Symbol, Role: TechnicalRole.Variable)) + label[EditorSelfWeight.Symbol.Length..] : "";
    // The flyout shows the area and density of the first segment; further segments have their own values in Project Setup.
    public TechnicalText SelfWeightArea => TechnicalSymbols.Area + " = "
        + SectionDisplay.Property(Document.Segments[0].Section, Document.Segments[0].Section.Area.SquareMeters, QuantityKind.Area, ResultPresentation);
    public TechnicalText SelfWeightDensity => Document.Segments[0].Material.Density is { } density
        ? TechnicalSymbols.Density + QuantityFormatter.FormatInputEquation("", density.KilogramsPerCubicMeter, ResultPresentation.Profile[QuantityKind.Density], ResultPresentation)
        : TechnicalSymbols.Density + " = —";
    public string? SelfWeightFeedback => _selfWeightFeedback;
    public bool HasSelfWeightFeedback => SelfWeightFeedback is not null;

    public bool ToggleSelfWeight()
    {
        using var measurement = _performance?.Begin(InteractionAction.SelfWeight);
        if (IsBusy) return false;
        bool enable = !IsSelfWeightEnabled;
        CancelEditorInteraction();
        if (enable && Document.SelfWeightActivationError is { } error)
        {
            _selfWeightFeedback = error;
            NotifySelfWeightState();
            return false;
        }
        var weight = Document.SelfWeight is { } existing ? existing with { Enabled = enable } : new(Guid.NewGuid(), true);
        Commit(Document with { SelfWeight = weight }, Document.SelfWeight is null
            ? EditorPresentation.WithSelfWeightVisibility(true) : EditorPresentation);
        measurement?.Complete();
        return true;
    }

    public bool EditSelfWeight(Guid id)
    {
        if (IsBusy || Document.SelfWeight is not { Enabled: true } weight || weight.Id != id) return false;
        if (IsSelfWeightFlyoutVisible) return true;
        if (!IsEditorNeutral) return false;
        _selfWeightFlyout = true;
        NotifySelfWeightState();
        return true;
    }

    public void ToggleSelfWeightVisibility()
    {
        using var measurement = _performance?.Begin(InteractionAction.SelfWeightVisibility);
        if (IsBusy || !IsSelfWeightEnabled) return;
        Commit(Document, EditorPresentation.WithSelfWeightVisibility(!IsSelfWeightVisible));
        measurement?.Complete();
    }

    public void DisableSelfWeight()
    {
        if (!IsBusy && IsSelfWeightEnabled) ToggleSelfWeight();
    }

    public void CancelSelfWeightInteraction()
    {
        _selfWeightFlyout = false;
        _selfWeightFeedback = null;
        NotifySelfWeightState();
    }

    private bool CanApplyMaterial(Material material)
    {
        if (!IsSelfWeightEnabled || material.Density is not null) return true;
        _selfWeightFeedback = Strings.SelfWeightMissingDensity;
        NotifySelfWeightState();
        return false;
    }

    private void NotifySelfWeightState()
    {
        foreach (string name in new[] { nameof(IsSelfWeightEnabled), nameof(IsSelfWeightVisible), nameof(IsSelfWeightFlyoutVisible),
            nameof(SelfWeightVisibilityAction), nameof(SelfWeightFeedback), nameof(HasSelfWeightFeedback),
            nameof(IsEditorNeutral) }) Notify(name);
        NotifySelfWeightValues();
    }

    private void NotifySelfWeightValues()
    {
        foreach (string name in new[] { nameof(SelfWeightLabel), nameof(SelfWeightValue), nameof(SelfWeightArea), nameof(SelfWeightDensity) }) Notify(name);
    }
}
