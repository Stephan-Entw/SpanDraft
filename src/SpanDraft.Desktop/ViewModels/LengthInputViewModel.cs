using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Transient input session for the single inline dimension editor.</summary>
public sealed class LengthInputViewModel(Func<Length> read, Action<Length> commit) : ObservableObject
{
    private string _text = UiNumbers.Format(read().Millimeters);
    private bool _isEditing;
    private bool _hasError;

    public string Text { get => _text; set { if (Set(ref _text, value)) HasError = false; } }
    public bool IsEditing { get => _isEditing; private set { if (Set(ref _isEditing, value)) Notify(nameof(IsDisplay)); } }
    public bool IsDisplay => !IsEditing;
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public string ErrorText => Strings.InvalidLength;
    public string DisplayText => UiNumbers.Format(read().Millimeters) + " mm";

    public void Begin()
    {
        if (IsEditing) return;
        Text = UiNumbers.Format(read().Millimeters);
        HasError = false;
        IsEditing = true;
    }

    public bool Confirm()
    {
        if (!IsEditing) return true;
        if (!UiNumbers.TryParseLength(Text, out Length length))
        {
            HasError = true;
            return false;
        }
        // Close before committing: subsequent focus loss must not commit again.
        IsEditing = false;
        commit(length);
        Refresh();
        return true;
    }

    public void Cancel()
    {
        IsEditing = false;
        Refresh();
    }

    public void LoseFocus()
    {
        if (IsEditing && !Confirm()) Cancel();
    }

    public void Refresh()
    {
        if (!IsEditing) Text = UiNumbers.Format(read().Millimeters);
        HasError = false;
        Notify(nameof(DisplayText));
    }
}
