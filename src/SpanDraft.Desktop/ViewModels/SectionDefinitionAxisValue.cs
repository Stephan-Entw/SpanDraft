using SpanDraft.Core.Sections;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Stable blocks preserve editing focus while committed properties and selection change.</summary>
public sealed class SectionDefinitionAxisValue(SectionAxisDesignation axis, IReadOnlyList<SectionDefinitionInput> inputs) : ObservableObject
{
    private string _label = "", _inertiaLabel = "", _positiveLabel = "", _negativeLabel = "";
    private string _inertia = "—", _positive = "—", _negative = "—";
    private bool _selected, _selectable;
    public SectionAxisDesignation Axis { get; } = axis;
    public IReadOnlyList<SectionDefinitionInput> Inputs { get; } = inputs;
    public string Label => _label;
    public string InertiaLabel => _inertiaLabel;
    public string PositiveLabel => _positiveLabel;
    public string NegativeLabel => _negativeLabel;
    public string Inertia => _inertia;
    public string PositiveModulus => _positive;
    public string NegativeModulus => _negative;
    public bool IsSelected => _selected;
    public bool IsSelectable => _selectable;
    public void Update(string label, string inertiaLabel, string positiveLabel, string negativeLabel,
        string inertia, string positive, string negative, bool selected, bool selectable)
    {
        Set(ref _label, label, nameof(Label)); Set(ref _inertiaLabel, inertiaLabel, nameof(InertiaLabel));
        Set(ref _positiveLabel, positiveLabel, nameof(PositiveLabel)); Set(ref _negativeLabel, negativeLabel, nameof(NegativeLabel));
        Set(ref _inertia, inertia, nameof(Inertia)); Set(ref _positive, positive, nameof(PositiveModulus)); Set(ref _negative, negative, nameof(NegativeModulus));
        Set(ref _selected, selected, nameof(IsSelected)); Set(ref _selectable, selectable, nameof(IsSelectable));
    }
}
